using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using First10.Modules.Recognition;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace First10.Infrastructure.Modules.Incidents;

public sealed class SingletonIncidentReviewProcessor(
    First10DbContext database,
    TimeProvider timeProvider)
{
    public async Task<IncidentReviewAlert?> RaiseAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({incidentId.ToString()}))",
            cancellationToken);
        var incident = await database.Incidents
            .Include(x => x.SourceReports)
            .SingleOrDefaultAsync(
            x => x.Id == incidentId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (incident is null
            || incident.VerificationStatus != IncidentVerificationStatus.AwaitingConfirmation
            || incident.SourceReports.Count != 1
            || now < incident.ReviewDueAtUtc
            || await database.IncidentReviewAlerts.AnyAsync(x => x.IncidentId == incidentId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var alert = IncidentReviewAlert.Raise(incident.Id, now);
        database.IncidentReviewAlerts.Add(alert);
        database.IncidentTimelineEvents.Add(IncidentTimelineEvent.StateChanged(
            incident.Id,
            "singleton-review-raised",
            "system:incident-review-timer",
            incident.ReviewDueAtUtc,
            now,
            new { alertId = alert.Id, alert.Reason }));
        await AuditWriter.AppendAsync(
            database,
            "incidents.singleton.review_raised",
            "worker:incident-review-timer",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["incidentId"] = incident.Id,
                ["alertId"] = alert.Id,
                ["reviewDueAtUtc"] = incident.ReviewDueAtUtc
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return alert;
    }
}

public static class ReviewSingletonIncidentHandler
{
    public static async Task Handle(
        ReviewSingletonIncident command,
        SingletonIncidentReviewProcessor processor,
        CancellationToken cancellationToken)
    {
        await processor.RaiseAsync(command.IncidentId, cancellationToken);
    }
}

public sealed class SingletonIncidentDecisionProcessor(
    First10DbContext database,
    TimeProvider timeProvider,
    IMessageBus? bus = null)
{
    public async Task<bool> DecideAsync(
        DecideSingletonIncident command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.DecisionId == Guid.Empty
            || command.IncidentId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.DecidedBy)
            || command.Decision == SingletonReviewDecision.Reject
               && string.IsNullOrWhiteSpace(command.Reason))
        {
            return false;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({command.IncidentId.ToString()}))",
            cancellationToken);
        if (await database.IncidentTimelineEvents.AnyAsync(
                x => x.EventId == command.DecisionId,
                cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var incident = await database.Incidents
            .Include(x => x.SourceReports)
            .SingleOrDefaultAsync(
            x => x.Id == command.IncidentId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (incident is null
            || !incident.TryApplySingletonReviewDecision(command.Decision, command.Reason, now))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        IncidentReviewAlert? alert = null;
        if (command.Decision is SingletonReviewDecision.Verify or SingletonReviewDecision.Reject)
        {
            alert = await database.IncidentReviewAlerts.SingleOrDefaultAsync(
                x => x.IncidentId == incident.Id && x.Status == IncidentReviewAlertStatus.Open,
                cancellationToken);
            alert?.TryResolve(now);
        }

        database.IncidentTimelineEvents.Add(IncidentTimelineEvent.StateChangedWithId(
            command.DecisionId,
            incident.Id,
            $"singleton-{command.Decision.ToString().ToLowerInvariant()}",
            $"dispatcher:{command.DecidedBy}",
            now,
            now,
            new
            {
                decision = command.Decision.ToString(),
                command.Reason,
                alertId = alert?.Id
            }));
        await AuditWriter.AppendAsync(
            database,
            $"incidents.singleton.{command.Decision.ToString().ToLowerInvariant()}",
            command.DecidedBy,
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["incidentId"] = incident.Id,
                ["decisionId"] = command.DecisionId,
                ["reason"] = command.Reason,
                ["alertId"] = alert?.Id
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        if (command.Decision == SingletonReviewDecision.Verify && bus is not null)
        {
            var reportIds = incident.SourceReports.Select(x => x.ReportId).ToArray();
            var triageCases = await database.TriageCases
                .Include(x => x.Assessments)
                .Where(x => reportIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            var sessionIds = triageCases.Values.Select(x => x.SessionId).ToArray();
            var sessions = await database.GuidedIntakeSessions
                .Where(x => sessionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            foreach (var source in incident.SourceReports)
            {
                var triage = triageCases[source.ReportId];
                var session = sessions[triage.SessionId];
                var language = triage.AuthoritativeAssessmentId.HasValue
                    ? triage.Assessments.Single(x => x.Id == triage.AuthoritativeAssessmentId.Value).Language.ToString()
                    : "English";
                await bus.PublishAsync(new ContributionDispatcherVerified(
                    source.ReportId,
                    incident.Id,
                    source.ReportId,
                    source.ReporterIndependenceKey,
                    session.ContactReference,
                    session.Channel.ToString(),
                    language,
                    ContributionRecognitionAward.NormalizeLga(command.ReviewedIncidentLga),
                    now));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

public static class DecideSingletonIncidentHandler
{
    public static async Task Handle(
        DecideSingletonIncident command,
        SingletonIncidentDecisionProcessor processor,
        CancellationToken cancellationToken)
    {
        await processor.DecideAsync(command, cancellationToken);
    }
}
