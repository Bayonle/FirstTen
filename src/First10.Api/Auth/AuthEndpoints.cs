using System.Security.Claims;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Modules.IdentityAudit;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace First10.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapFirst10Authentication(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");

        group.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        }).AllowAnonymous();

        group.MapPost("/bootstrap", async (
            BootstrapRequest request,
            BootstrapService bootstrap,
            CancellationToken cancellationToken) =>
        {
            var invitation = await bootstrap.TryCreateFirstAdministratorAsync(
                request.Email,
                request.Secret,
                cancellationToken);
            return invitation is null ? Results.Unauthorized() : Results.Ok(invitation);
        }).AllowAnonymous().WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapPost("/invitations", async (
            CreateInvitationRequest request,
            InvitationService invitations,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var actor = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            return Results.Ok(await invitations.CreateAsync(request.Email, request.Role, actor, cancellationToken));
        }).RequireAuthorization(IdentityConfiguration.AdministratorPolicy)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapPost("/mfa-recovery", async (
            MfaRecoveryRequest request,
            MfaRecoveryService recovery,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var actorValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(actorValue, out var actorId))
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await recovery.BeginResetAsync(request.UserId, actorId, cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        }).RequireAuthorization(IdentityConfiguration.AdministratorPolicy)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapPost("/invitations/enrollment", async (
            InvitationTokenRequest request,
            InvitationService invitations,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await invitations.BeginEnrollmentAsync(request.Token, cancellationToken));
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(new { error = "Invitation is invalid or expired." });
            }
        }).AllowAnonymous().WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapPost("/invitations/accept", async (
            AcceptInvitationRequest request,
            InvitationService invitations,
            CancellationToken cancellationToken) =>
        {
            var result = await invitations.CompleteEnrollmentAsync(
                request.Token,
                request.Password,
                request.AuthenticatorCode,
                cancellationToken);
            return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
        }).AllowAnonymous().WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapPost("/login/password", async (
            PasswordLoginRequest request,
            UserManager<First10User> users,
            SignInManager<First10User> signIn) =>
        {
            var user = await users.FindByEmailAsync(request.Email);
            if (user?.InvitationAcceptedAtUtc is null)
            {
                return Results.Unauthorized();
            }

            var result = await signIn.PasswordSignInAsync(user, request.Password, false, true);
            return result.RequiresTwoFactor
                ? Results.Ok(new { status = "mfa_required" })
                : Results.Unauthorized();
        }).AllowAnonymous().WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapPost("/login/mfa", async (
            MfaLoginRequest request,
            SignInManager<First10User> signIn,
            UserManager<First10User> users,
            First10.Infrastructure.Persistence.First10DbContext database,
            CancellationToken cancellationToken) =>
        {
            var pendingUser = await signIn.GetTwoFactorAuthenticationUserAsync();
            var code = request.AuthenticatorCode.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);
            var result = await signIn.TwoFactorAuthenticatorSignInAsync(code, false, false);
            await RecordSecondFactorResultAsync(pendingUser, result.Succeeded, "authenticator", users, database, cancellationToken);
            return result.Succeeded ? Results.Ok(new { status = "authenticated" }) : Results.Unauthorized();
        }).AllowAnonymous().WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapPost("/login/recovery", async (
            RecoveryCodeLoginRequest request,
            SignInManager<First10User> signIn,
            UserManager<First10User> users,
            First10.Infrastructure.Persistence.First10DbContext database,
            CancellationToken cancellationToken) =>
        {
            var pendingUser = await signIn.GetTwoFactorAuthenticationUserAsync();
            var result = await signIn.TwoFactorRecoveryCodeSignInAsync(request.RecoveryCode);
            await RecordSecondFactorResultAsync(pendingUser, result.Succeeded, "recovery", users, database, cancellationToken);
            return result.Succeeded ? Results.Ok(new { status = "authenticated" }) : Results.Unauthorized();
        }).AllowAnonymous().WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapPost("/logout", async (SignInManager<First10User> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization().WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        return endpoints;
    }

    private static async Task RecordSecondFactorResultAsync(
        First10User? user,
        bool succeeded,
        string method,
        UserManager<First10User> users,
        First10.Infrastructure.Persistence.First10DbContext database,
        CancellationToken cancellationToken)
    {
        if (user is not null && succeeded)
        {
            user.MarkReauthenticated(DateTimeOffset.UtcNow);
            await users.UpdateAsync(user);
        }

        await AuditWriter.AppendAsync(database, "identity.login.second_factor", user?.Id.ToString() ?? "anonymous", AuditPayload.Create(new Dictionary<string, object?>
        {
            ["method"] = method,
            ["result"] = succeeded ? "succeeded" : "failed"
        }), cancellationToken);
    }
}

public sealed record BootstrapRequest(string Email, string Secret);

public sealed record CreateInvitationRequest(string Email, string Role);

public sealed record InvitationTokenRequest(string Token);

public sealed record AcceptInvitationRequest(string Token, string Password, string AuthenticatorCode);

public sealed record PasswordLoginRequest(string Email, string Password);

public sealed record MfaLoginRequest(string AuthenticatorCode);

public sealed record RecoveryCodeLoginRequest(string RecoveryCode);

public sealed record MfaRecoveryRequest(Guid UserId);
