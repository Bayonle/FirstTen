using First10.Infrastructure.Modules.Guidance;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.Dispatch;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.Guidance;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using First10.Infrastructure.Modules.Operations;

namespace First10.Infrastructure.Modules.Dispatch;

public sealed record DispatchTransitionOutcome(
    DispatchTransitionResult Result,
    IReadOnlyList<Guid> DeliveryIntentIds);

public sealed class DispatchTransitionProcessor(
    First10DbContext database,
    GuidanceIntentProcessor guidance,
    TimeProvider timeProvider,
    PilotMetrics? metrics = null,
    IDbContextOutbox<First10DbContext>? outbox = null)
{
    public async Task<DispatchTransitionOutcome> ApplyAsync(
        TransitionIncidentDispatch command,
        CancellationToken cancellationToken = default)
    {
        if (command.TransitionId == Guid.Empty
            || command.IncidentId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.DispatcherId))
        {
            return new DispatchTransitionOutcome(DispatchTransitionResult.Invalid, []);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({command.IncidentId.ToString()}))",
            cancellationToken);
        if (await database.DispatchTransitions.AnyAsync(x => x.Id == command.TransitionId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new DispatchTransitionOutcome(DispatchTransitionResult.Duplicate, []);
        }

        var incident = await database.Incidents.SingleOrDefaultAsync(
            x => x.Id == command.IncidentId,
            cancellationToken);
        if (incident is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DispatchTransitionOutcome(DispatchTransitionResult.IncidentNotFound, []);
        }

        if (command.TargetStatus == DispatchStatus.Verified
            && incident.VerificationStatus is not (
                IncidentVerificationStatus.AutoVerified or IncidentVerificationStatus.DispatcherVerified))
        {
            await transaction.CommitAsync(cancellationToken);
            return new DispatchTransitionOutcome(DispatchTransitionResult.Invalid, []);
        }

        var dispatch = await database.IncidentDispatches.SingleOrDefaultAsync(
            x => x.IncidentId == command.IncidentId,
            cancellationToken);
        if (dispatch is null)
        {
            dispatch = IncidentDispatch.Create(command.IncidentId, timeProvider.GetUtcNow());
            database.IncidentDispatches.Add(dispatch);
        }

        var prior = dispatch.Status;
        var now = timeProvider.GetUtcNow();
        var result = dispatch.TryTransition(
            command.TargetStatus,
            First10RolePermissions.Allows(command.DispatcherRole, First10Permissions.DispatchIncident),
            command.Reason,
            command.ExpectedVersion,
            now);
        if (result != DispatchTransitionResult.Applied)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DispatchTransitionOutcome(result, []);
        }

        var transition = DispatchTransition.Create(
            command.TransitionId,
            command.IncidentId,
            prior,
            command.TargetStatus,
            command.DispatcherId,
            command.Reason,
            now,
            dispatch.Version);
        database.DispatchTransitions.Add(transition);
        database.IncidentTimelineEvents.Add(IncidentTimelineEvent.StateChangedWithId(
            transition.Id,
            incident.Id,
            $"dispatch-{command.TargetStatus.ToString().ToLowerInvariant()}",
            $"dispatcher:{command.DispatcherId}",
            now,
            now,
            new
            {
                from = prior.ToString(),
                to = command.TargetStatus.ToString(),
                command.Reason,
                dispatch.Version
            }));
        await AuditWriter.AppendAsync(
            database,
            $"dispatch.{command.TargetStatus.ToString().ToLowerInvariant()}",
            command.DispatcherId,
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["incidentId"] = incident.Id,
                ["transitionId"] = transition.Id,
                ["from"] = prior.ToString(),
                ["to"] = command.TargetStatus.ToString(),
                ["reason"] = command.Reason,
                ["version"] = dispatch.Version
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        var purpose = command.TargetStatus == DispatchStatus.Reopened
            ? GuidancePurpose.ReopenCorrection
            : GuidancePurpose.ResponseStatus;
        var created = await guidance.CreateStatusAsync(
            incident.Id,
            transition.Id,
            command.TargetStatus.ToString().ToLowerInvariant(),
            purpose,
            cancellationToken);
        if (outbox is not null)
        {
            foreach (var intentId in created.Where(x => x.ReadyForDelivery).Select(x => x.IntentId))
            {
                await outbox.PublishAsync(new DeliverGuidanceIntent(intentId));
            }

            if (command.TargetStatus == DispatchStatus.Verified)
            {
                await PublishRecognitionAsync(outbox, incident.Id, now, cancellationToken);
            }

            await outbox.PublishAsync(new IncidentChanged(
                incident.Id,
                dispatch.Version,
                "dispatch",
                now));
            await database.SaveChangesAsync(cancellationToken);
        }
        if (command.TargetStatus == DispatchStatus.Dispatched)
        {
            metrics?.Dispatched(now - incident.CreatedAtUtc);
        }
        await transaction.CommitAsync(cancellationToken);
        if (outbox is not null)
        {
            await outbox.FlushOutgoingMessagesAsync();
        }
        return new DispatchTransitionOutcome(
            DispatchTransitionResult.Applied,
            created.Where(x => x.ReadyForDelivery).Select(x => x.IntentId).ToArray());
    }

    private async Task PublishRecognitionAsync(
        IDbContextOutbox<First10DbContext> messageOutbox,
        Guid incidentId,
        DateTimeOffset verifiedAtUtc,
        CancellationToken cancellationToken)
    {
        var sources = await database.IncidentSourceReports.AsNoTracking()
            .Where(x => x.IncidentId == incidentId)
            .ToArrayAsync(cancellationToken);
        var reportIds = sources.Select(x => x.ReportId).ToArray();
        var triageCases = await database.TriageCases.AsNoTracking()
            .Include(x => x.Assessments)
            .Where(x => reportIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var sessionIds = triageCases.Values.Select(x => x.SessionId).ToArray();
        var sessions = await database.GuidedIntakeSessions.AsNoTracking()
            .Where(x => sessionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var source in sources)
        {
            var triage = triageCases[source.ReportId];
            var session = sessions[triage.SessionId];
            var language = triage.AuthoritativeAssessmentId.HasValue
                ? triage.Assessments.Single(x => x.Id == triage.AuthoritativeAssessmentId.Value).Language.ToString()
                : "English";
            await messageOutbox.PublishAsync(new ContributionDispatcherVerified(
                source.ReportId,
                incidentId,
                source.ReportId,
                source.ReporterIndependenceKey,
                session.ContactReference,
                session.Channel.ToString(),
                language,
                "Unknown",
                verifiedAtUtc));
        }
    }
}

public static class DispatchTransitionHandler
{
    public static async Task Handle(
        TransitionIncidentDispatch command,
        DispatchTransitionProcessor processor,
        CancellationToken cancellationToken)
    {
        await processor.ApplyAsync(command, cancellationToken);
    }
}
