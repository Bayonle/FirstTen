using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.Guidance;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using First10.Modules.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace First10.Api.Endpoints.Operations;

public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var operations = endpoints.MapGroup("/api/operations")
            .RequireAuthorization(IdentityConfiguration.AdministratorPolicy)
            .WithTags("Operations");
        operations.MapGet("/health", HealthAsync);
        operations.MapGet("/activation", ActivationAsync);
        operations.MapGet("/audit", AuditAsync);
        operations.MapGet("/users", UsersAsync);
        return endpoints;
    }

    private static async Task<IResult> HealthAsync(
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var databaseAvailable = await database.Database.CanConnectAsync(cancellationToken);
        var audit = databaseAvailable
            ? await database.AuditEvents.AsNoTracking().OrderBy(x => x.Sequence).ToArrayAsync(cancellationToken)
            : [];
        var health = new OperationalHealth(
            databaseAvailable,
            databaseAvailable && AuditChainVerifier.IsValid(audit),
            databaseAvailable
                ? await database.IncidentReviewAlerts.CountAsync(
                    x => x.Status == IncidentReviewAlertStatus.Open, cancellationToken)
                : -1,
            databaseAvailable
                ? await database.GuidanceIntents.CountAsync(
                    x => x.Status == GuidanceIntentStatus.Failed || x.Status == GuidanceIntentStatus.Unknown,
                    cancellationToken)
                : -1,
            DateTimeOffset.UtcNow);
        return health.DatabaseAvailable && health.AuditChainValid
            ? Results.Ok(health)
            : Results.Json(health, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult ActivationAsync(IConfiguration configuration)
    {
        var blockers = new List<string>();
        Require(configuration, blockers, "Pilot:FrscApprovalReference");
        Require(configuration, blockers, "Pilot:ClinicalApprovalReference");
        Require(configuration, blockers, "Pilot:DataProtectionApprovalReference");
        Require(configuration, blockers, "Pilot:RetentionApprovalReference");
        Require(configuration, blockers, "OpenAI:ApprovedProfile:Version");
        if (!configuration.GetValue<bool>("Guidance:ActivationApproved"))
        {
            blockers.Add("approved_guidance_bundle_missing");
        }

        return Results.Ok(new ActivationGateStatus(blockers.Count == 0, blockers, DateTimeOffset.UtcNow));
    }

    private static async Task<IResult> AuditAsync(
        First10DbContext database,
        long? after,
        CancellationToken cancellationToken)
    {
        var rows = await database.AuditEvents.AsNoTracking()
            .Where(x => x.Sequence > (after ?? 0))
            .OrderBy(x => x.Sequence)
            .Take(500)
            .Select(x => new
            {
                x.Sequence,
                x.OccurredAtUtc,
                x.Action,
                x.ActorId,
                x.PayloadJson,
                x.PreviousHash,
                x.Hash
            })
            .ToArrayAsync(cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> UsersAsync(
        UserManager<First10User> userManager,
        CancellationToken cancellationToken)
    {
        var users = await userManager.Users.AsNoTracking()
            .OrderBy(x => x.Email)
            .Select(x => new
            {
                x.Id,
                x.Email,
                x.EmailConfirmed,
                x.InvitationAcceptedAtUtc,
                x.LastReauthenticatedAtUtc,
                x.SessionVersion,
                x.LockoutEnd
            })
            .ToArrayAsync(cancellationToken);
        return Results.Ok(users);
    }

    private static void Require(IConfiguration configuration, List<string> blockers, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            blockers.Add($"missing_{key.Replace(':', '_').ToLowerInvariant()}");
        }
    }
}
