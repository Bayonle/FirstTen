using System.Security.Claims;
using First10.Api.Auth;
using First10.Infrastructure.Messaging;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Modules.Incidents;
using First10.Infrastructure.Persistence;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace First10.Api.Endpoints.Incidents;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .RequireAuthorization(IdentityConfiguration.DispatcherPolicy)
            .WithTags("Incidents");

        group.MapGet("/", ListAsync).WithName("ListActiveIncidents");
        group.MapGet("/{incidentId:guid}", DetailAsync).WithName("GetIncidentDetail");
        group.MapGet("/{incidentId:guid}/timeline", TimelineAsync).WithName("GetIncidentTimeline");
        group.MapGet("/{incidentId:guid}/briefing", BriefingAsync).WithName("GetIncidentCrewBriefing");
        group.MapPost("/{incidentId:guid}/review", ReviewAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        group.MapPost("/{incidentId:guid}/conflicts/{conflictId:guid}/resolve", ResolveConflictAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        group.MapPost("/{incidentId:guid}/notes", AppendNoteAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var rows = await database.Incidents.AsNoTracking()
            .Where(x => x.VerificationStatus != IncidentVerificationStatus.DispatcherRejected)
            .OrderBy(x => x.VerificationStatus == IncidentVerificationStatus.AwaitingConfirmation ? 0 : 1)
            .ThenBy(x => x.ReviewDueAtUtc)
            .Select(x => new
            {
                id = x.Id,
                verificationStatus = x.VerificationStatus.ToString(),
                version = x.Version,
                createdAtUtc = x.CreatedAtUtc,
                reviewDueAtUtc = x.ReviewDueAtUtc,
                sourceCount = x.SourceReports.Count,
                unresolvedConflictCount = x.Conflicts.Count(conflict => !conflict.IsResolved),
                dispatchStatus = database.IncidentDispatches
                    .Where(dispatch => dispatch.IncidentId == x.Id)
                    .Select(dispatch => dispatch.Status.ToString())
                    .SingleOrDefault() ?? "AwaitingVerification",
                dispatchVersion = database.IncidentDispatches
                    .Where(dispatch => dispatch.IncidentId == x.Id)
                    .Select(dispatch => (int?)dispatch.Version)
                    .SingleOrDefault() ?? 1
            })
            .Take(200)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> DetailAsync(
        Guid incidentId,
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var incident = await database.Incidents.AsNoTracking()
            .Include(x => x.SourceReports)
            .Include(x => x.Conflicts)
            .Include(x => x.Observations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == incidentId, cancellationToken);
        if (incident is null)
        {
            return Results.NotFound();
        }

        var dispatch = await database.IncidentDispatches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IncidentId == incidentId, cancellationToken);
        var guidance = await database.GuidanceIntents.AsNoTracking()
            .Where(x => x.IncidentId == incidentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                id = x.Id,
                purpose = x.Purpose.ToString(),
                trigger = x.Trigger,
                language = x.Language.ToString(),
                status = x.Status.ToString(),
                createdAtUtc = x.CreatedAtUtc,
                deadlineAtUtc = x.DeadlineAtUtc,
                failureCode = x.FailureCode
            })
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new
        {
            id = incident.Id,
            verificationStatus = incident.VerificationStatus.ToString(),
            version = incident.Version,
            incident.CreatedAtUtc,
            incident.ReviewDueAtUtc,
            incident.VerifiedAtUtc,
            incident.RejectedAtUtc,
            incident.RejectionReason,
            dispatch = dispatch is null ? null : new
            {
                status = dispatch.Status.ToString(),
                dispatch.Version,
                dispatch.UpdatedAtUtc,
                dispatch.LastReopenReason
            },
            sources = incident.SourceReports.OrderBy(x => x.ReceivedAtUtc).Select(x => new
            {
                reportId = x.ReportId,
                occurredAtUtc = x.OccurredAtUtc,
                receivedAtUtc = x.ReceivedAtUtc,
                incidentType = x.IncidentType.ToString(),
                severity = x.Severity.ToString(),
                x.CasualtyMinimum,
                x.CasualtyMaximum,
                victimState = x.VictimState.ToString(),
                sceneState = x.SceneState.ToString(),
                x.LocationDescription,
                direction = x.Direction.ToString(),
                x.Latitude,
                x.Longitude,
                x.LocationConfidence,
                evidenceReferences = x.EvidenceReferences.Split(',', StringSplitOptions.RemoveEmptyEntries)
            }),
            conflicts = incident.Conflicts.Select(x => new
            {
                id = x.Id,
                field = x.Field.ToString(),
                x.LeftReportId,
                x.LeftClaimId,
                x.RightReportId,
                x.RightClaimId,
                x.LeftValue,
                x.RightValue,
                x.IsResolved,
                x.SelectedReportId,
                x.SelectedClaimId,
                x.ResolvedAtUtc
            }),
            observations = incident.Observations.Select(x => new
            {
                id = x.Id,
                x.SourceReportId,
                x.OccurredAtUtc,
                x.ReceivedAtUtc,
                victimState = x.VictimState.ToString(),
                sceneState = x.SceneState.ToString(),
                x.LocationDescription,
                direction = x.Direction.ToString(),
                x.EvidenceReference
            }),
            guidance
        });
    }

    private static async Task<IResult> BriefingAsync(
        Guid incidentId,
        First10DbContext database,
        CrewBriefingGenerator generator,
        CancellationToken cancellationToken)
    {
        var incident = await database.Incidents.AsNoTracking()
            .Include(x => x.SourceReports)
            .Include(x => x.Conflicts)
            .Include(x => x.Observations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == incidentId, cancellationToken);
        if (incident is null)
        {
            return Results.NotFound();
        }

        var briefing = await generator.GenerateAsync(incident, cancellationToken);
        return Results.Ok(new
        {
            briefing.IncidentId,
            briefing.Text,
            briefing.UsedAiOrdering,
            claims = briefing.OrderedClaims.Select(x => new
            {
                x.ClaimId,
                x.SourceReportId,
                x.EvidenceReferences
            })
        });
    }

    private static async Task<IResult> TimelineAsync(
        Guid incidentId,
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        if (!await database.Incidents.AnyAsync(x => x.Id == incidentId, cancellationToken))
        {
            return Results.NotFound();
        }

        var timeline = await database.IncidentTimelineEvents.AsNoTracking()
            .Where(x => x.IncidentId == incidentId)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.ReceivedAtUtc)
            .Select(x => new
            {
                id = x.EventId,
                type = x.EventType,
                source = x.Source,
                x.Latitude,
                x.Longitude,
                x.OccurredAtUtc,
                x.ReceivedAtUtc,
                payloadJson = x.PayloadJson
            })
            .ToArrayAsync(cancellationToken);
        return Results.Ok(timeline);
    }

    private static async Task<IResult> ReviewAsync(
        Guid incidentId,
        ReviewIncidentRequest request,
        SingletonIncidentDecisionProcessor processor,
        First10DbContext database,
        IMessageBus bus,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SingletonReviewDecision>(request.Decision, true, out var decision))
        {
            return Problems.Validation("decision", "Decision must be Verify, Reject, or KeepOpen.");
        }

        var actor = Actor(context);
        var applied = await processor.DecideAsync(new DecideSingletonIncident(
            request.DecisionId,
            incidentId,
            decision,
            actor,
            request.Reason,
            request.ReviewedIncidentLga,
            request.ExpectedVersion), cancellationToken);
        if (!applied)
        {
            return await ConflictOrNotFoundAsync(incidentId, database, cancellationToken);
        }

        var version = await database.Incidents.Where(x => x.Id == incidentId)
            .Select(x => x.Version).SingleAsync(cancellationToken);
        await bus.PublishAsync(new IncidentChanged(incidentId, version, "review", DateTimeOffset.UtcNow));
        return Results.Ok(new { incidentId, version });
    }

    private static async Task<IResult> ResolveConflictAsync(
        Guid incidentId,
        Guid conflictId,
        ResolveConflictRequest request,
        IncidentConflictResolutionProcessor processor,
        First10DbContext database,
        IMessageBus bus,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var applied = await processor.ResolveAsync(new ResolveIncidentConflict(
            incidentId,
            conflictId,
            request.SelectedClaimId,
            Actor(context),
            request.ExpectedVersion), cancellationToken);
        if (!applied)
        {
            return await ConflictOrNotFoundAsync(incidentId, database, cancellationToken);
        }

        var version = await database.Incidents.Where(x => x.Id == incidentId)
            .Select(x => x.Version).SingleAsync(cancellationToken);
        await bus.PublishAsync(new IncidentChanged(incidentId, version, "conflict", DateTimeOffset.UtcNow));
        return Results.Ok(new { incidentId, version });
    }

    private static async Task<IResult> AppendNoteAsync(
        Guid incidentId,
        AppendIncidentNoteRequest request,
        First10DbContext database,
        IMessageBus bus,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var note = request.Text?.Trim();
        if (string.IsNullOrWhiteSpace(note) || note.Length > 2000)
        {
            return Problems.Validation("text", "A plain-text note of 1–2000 characters is required.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({incidentId.ToString()}))",
            cancellationToken);
        var incident = await database.Incidents.SingleOrDefaultAsync(x => x.Id == incidentId, cancellationToken);
        if (incident is null)
        {
            return Results.NotFound();
        }

        if (!incident.TryAdvanceOperationalVersion(request.ExpectedVersion))
        {
            return Results.Conflict(new { incidentId, currentVersion = incident.Version });
        }

        var actor = Actor(context);
        var eventId = request.NoteId == Guid.Empty ? Guid.NewGuid() : request.NoteId;
        var now = DateTimeOffset.UtcNow;
        database.IncidentTimelineEvents.Add(IncidentTimelineEvent.StateChangedWithId(
            eventId,
            incidentId,
            "dispatcher-note-added",
            $"dispatcher:{actor}",
            now,
            now,
            new { note, request.SourceClaimReferences }));
        await AuditWriter.AppendAsync(database, "incidents.note.added", actor, AuditPayload.Create(
            new Dictionary<string, object?>
            {
                ["incidentId"] = incidentId,
                ["noteId"] = eventId,
                ["sourceClaimReferences"] = request.SourceClaimReferences
            }), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await bus.PublishAsync(new IncidentChanged(incidentId, incident.Version, "note", now));
        return Results.Ok(new { noteId = eventId, incidentId, version = incident.Version });
    }

    private static async Task<IResult> ConflictOrNotFoundAsync(
        Guid incidentId,
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var version = await database.Incidents.AsNoTracking()
            .Where(x => x.Id == incidentId)
            .Select(x => (int?)x.Version)
            .SingleOrDefaultAsync(cancellationToken);
        return version.HasValue
            ? Results.Conflict(new { incidentId, currentVersion = version.Value })
            : Results.NotFound();
    }

    private static string Actor(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
}

public sealed record ReviewIncidentRequest(
    Guid DecisionId,
    string Decision,
    int ExpectedVersion,
    string? Reason,
    string? ReviewedIncidentLga);

public sealed record ResolveConflictRequest(Guid SelectedClaimId, int ExpectedVersion);

public sealed record AppendIncidentNoteRequest(
    Guid NoteId,
    int ExpectedVersion,
    string Text,
    IReadOnlyList<string> SourceClaimReferences);

internal static class Problems
{
    public static IResult Validation(string field, string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { [field] = [message] });
}
