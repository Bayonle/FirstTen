using System.Security.Cryptography;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace First10.Infrastructure.Modules.IdentityAudit;

public sealed class InvitationService(
    First10DbContext database,
    UserManager<First10User> users)
{
    public async Task<CreatedInvitation> CreateAsync(
        string email,
        string role,
        string invitedBy,
        CancellationToken cancellationToken = default)
    {
        if (!First10Roles.All.Contains(role, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unknown First10 role.", nameof(role));
        }

        await using var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        var user = First10User.CreateInvited(email.Trim());
        var creation = await users.CreateAsync(user);
        EnsureSucceeded(creation);
        EnsureSucceeded(await users.AddToRoleAsync(user, role));
        EnsureSucceeded(await users.ResetAuthenticatorKeyAsync(user));

        var rawToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var expiresAtUtc = DateTimeOffset.UtcNow.AddHours(24);
        var invitation = UserInvitation.Create(
            user.Id,
            role,
            InvitationPurposes.AccountEnrollment,
            HashToken(rawToken),
            invitedBy,
            expiresAtUtc);
        database.UserInvitations.Add(invitation);
        await database.SaveChangesAsync(cancellationToken);
        await AuditWriter.AppendAsync(database, "identity.invitation.created", invitedBy, AuditPayload.Create(new Dictionary<string, object?>
        {
            ["role"] = role,
            ["userId"] = user.Id
        }), cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new CreatedInvitation(invitation.Id, rawToken, expiresAtUtc);
    }

    public async Task<InvitationEnrollment> BeginEnrollmentAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var invitation = await FindRedeemableAsync(token, cancellationToken)
            ?? throw new InvalidOperationException("Invitation is invalid or expired.");
        var user = await users.FindByIdAsync(invitation.UserId.ToString())
            ?? throw new InvalidOperationException("Invited user no longer exists.");
        var sharedKey = await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("Authenticator key is unavailable.");
        return new InvitationEnrollment(user.Id, sharedKey);
    }

    public async Task<EnrollmentResult> CompleteEnrollmentAsync(
        string token,
        string password,
        string authenticatorCode,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var tokenHash = HashToken(token);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({tokenHash}))",
            cancellationToken);
        var invitation = await FindRedeemableAsync(token, cancellationToken);
        if (invitation is null)
        {
            return EnrollmentResult.Failure("Invitation is invalid or expired.");
        }

        var user = await users.FindByIdAsync(invitation.UserId.ToString());
        if (user is null)
        {
            return EnrollmentResult.Failure("Invited user no longer exists.");
        }

        var normalizedCode = authenticatorCode.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, normalizedCode))
        {
            await AuditWriter.AppendAsync(database, "identity.invitation.acceptance_failed", user.Id.ToString(), AuditPayload.Create(new Dictionary<string, object?>
            {
                ["reason"] = "invalid_mfa"
            }), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return EnrollmentResult.Failure("Authenticator code is invalid.");
        }

        if (invitation.Purpose == InvitationPurposes.AccountEnrollment)
        {
            var passwordResult = await users.AddPasswordAsync(user, password);
            if (!passwordResult.Succeeded)
            {
                return EnrollmentResult.Failure([.. passwordResult.Errors.Select(x => x.Description)]);
            }
        }

        var acceptedAtUtc = DateTimeOffset.UtcNow;
        if (invitation.Purpose == InvitationPurposes.AccountEnrollment)
        {
            user.AcceptInvitation(acceptedAtUtc);
        }

        EnsureSucceeded(await users.SetTwoFactorEnabledAsync(user, true));
        invitation.Redeem(acceptedAtUtc);
        await database.SaveChangesAsync(cancellationToken);
        var generatedRecoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)
            ?? throw new InvalidOperationException("Recovery codes could not be generated.");
        var recoveryCodes = generatedRecoveryCodes.ToArray();
        var action = invitation.Purpose == InvitationPurposes.MfaRecovery
            ? "identity.mfa_recovery.completed"
            : "identity.invitation.accepted";
        await AuditWriter.AppendAsync(database, action, user.Id.ToString(), AuditPayload.Create(new Dictionary<string, object?>
        {
            ["role"] = invitation.Role,
            ["result"] = "accepted",
            ["purpose"] = invitation.Purpose
        }), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return EnrollmentResult.Success(recoveryCodes);
    }

    internal static (string Raw, string Hash) CreateToken()
    {
        var raw = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return (raw, HashToken(raw));
    }

    private async Task<UserInvitation?> FindRedeemableAsync(string token, CancellationToken cancellationToken)
    {
        var hash = HashToken(token);
        var invitation = await database.UserInvitations.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        return invitation is not null && invitation.CanRedeem(DateTimeOffset.UtcNow) ? invitation : null;
    }

    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(x => x.Description)));
        }
    }
}
