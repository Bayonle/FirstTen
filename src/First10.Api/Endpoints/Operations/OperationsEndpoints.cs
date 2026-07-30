using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.Guidance;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using First10.Modules.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using First10.Infrastructure.Modules.Operations;
using Microsoft.AspNetCore.Antiforgery;
using System.Security.Claims;

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
        operations.MapPost("/activation/evidence", RecordEvidenceAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        operations.MapGet("/audit", AuditAsync);
        operations.MapGet("/audit/anchors", AuditAnchorsAsync);
        operations.MapPost("/audit/anchors", RecordAuditAnchorAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
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

    private static async Task<IResult> ActivationAsync(
        IPilotActivationGate activationGate,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await activationGate.EvaluateAsync(cancellationToken));
    }

    private static async Task<IResult> RecordEvidenceAsync(
        RecordPilotEvidenceRequest request,
        PilotActivationGate activationGate,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PilotGateEvidenceType>(request.Type, true, out var type)
            || !Enum.TryParse<PilotGateEvidenceDecision>(request.Decision, true, out var decision))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["evidence"] = ["Evidence type or decision is invalid."]
            });
        }

        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var recorded = await activationGate.RecordAsync(
            request.Id,
            type,
            decision,
            request.EvidenceReference,
            actor,
            cancellationToken);
        return recorded ? Results.Accepted() : Results.Conflict();
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

    private static async Task<IResult> AuditAnchorsAsync(
        First10DbContext database,
        CancellationToken cancellationToken) =>
        Results.Ok(await database.AuditAnchors.AsNoTracking()
            .OrderByDescending(x => x.LastSequence)
            .Select(x => new
            {
                x.Id,
                x.LastSequence,
                x.LastHash,
                x.ExternalReference,
                x.RecordedBy,
                x.RecordedAtUtc
            })
            .Take(100)
            .ToArrayAsync(cancellationToken));

    private static async Task<IResult> RecordAuditAnchorAsync(
        RecordAuditAnchorRequest request,
        First10DbContext database,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var latest = await database.AuditEvents.AsNoTracking()
            .OrderByDescending(x => x.Sequence)
            .Select(x => new { x.Sequence, x.Hash })
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null
            || latest.Sequence != request.LastSequence
            || !string.Equals(latest.Hash, request.LastHash, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new { error = "Anchor must match the current verified audit head." });
        }

        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var anchor = AuditAnchor.Create(
            request.Id,
            request.LastSequence,
            request.LastHash,
            request.ExternalReference,
            actor,
            DateTimeOffset.UtcNow);
        database.AuditAnchors.Add(anchor);
        await AuditWriter.AppendAsync(database, "audit.anchor.recorded", actor, AuditPayload.Create(
            new Dictionary<string, object?>
            {
                ["anchorId"] = anchor.Id,
                ["lastSequence"] = anchor.LastSequence,
                ["lastHash"] = anchor.LastHash,
                ["externalReference"] = anchor.ExternalReference
            }), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/operations/audit/anchors/{anchor.Id}", new { anchor.Id });
    }

}

public sealed record RecordPilotEvidenceRequest(
    Guid Id,
    string Type,
    string Decision,
    string EvidenceReference);

public sealed record RecordAuditAnchorRequest(
    Guid Id,
    long LastSequence,
    string LastHash,
    string ExternalReference);
