using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Modules.Incidents;

public sealed class LateIncidentLocationProcessor(
    First10DbContext database,
    IncidentPersistence incidentPersistence)
{
    public async Task<bool> ApplyAsync(
        ApplyLateIncidentLocation command,
        CancellationToken cancellationToken = default)
    {
        if (command.EvidenceId == Guid.Empty
            || command.Latitude is < -90 or > 90
            || command.Longitude is < -180 or > 180
            || command.Confidence < IncidentMatchPolicy.MinimumLocationConfidence)
        {
            return false;
        }

        var match = await incidentPersistence.CreateOrMatchAsync(command.TriageCaseId, cancellationToken);
        if (match is null)
        {
            return false;
        }

        database.ChangeTracker.Clear();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext('first10-incident-matching'))",
            cancellationToken);
        if (await database.IncidentTimelineEvents.AnyAsync(
                x => x.EventId == command.EvidenceId,
                cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var source = await database.IncidentSourceReports.SingleAsync(
            x => x.ReportId == command.TriageCaseId,
            cancellationToken);
        if (!source.TryApplyLateLocation(
                command.Latitude,
                command.Longitude,
                command.Confidence,
                $"pin:{command.EvidenceId:N}"))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        database.IncidentTimelineEvents.Add(IncidentTimelineEvent.LateLocation(
            command.EvidenceId,
            source,
            command.OccurredAtUtc,
            command.ReceivedAtUtc));
        var earliest = source.OccurredAtUtc - IncidentMatchPolicy.MaximumTimeDifference;
        var latest = source.OccurredAtUtc + IncidentMatchPolicy.MaximumTimeDifference;
        var nearby = await database.Incidents
            .Include(x => x.SourceReports)
            .Where(x => x.Id != source.IncidentId
                        && x.SourceReports.Any(report =>
                            report.OccurredAtUtc >= earliest && report.OccurredAtUtc <= latest))
            .ToArrayAsync(cancellationToken);
        var candidate = ToCandidate(source);
        var links = new List<IncidentCandidateLink>();
        foreach (var possible in nearby.Where(incident => incident.SourceReports.Any(report =>
                     IncidentMatchPolicy.Evaluate(ToCandidate(report), candidate).Decision
                     is IncidentMatchDecision.Qualifying or IncidentMatchDecision.UncertainLocation)))
        {
            var link = IncidentCandidateLink.Create(
                source.IncidentId,
                possible.Id,
                "late_location_candidate",
                command.ReceivedAtUtc);
            if (!await database.IncidentCandidateLinks.AnyAsync(x =>
                    x.FirstIncidentId == link.FirstIncidentId
                    && x.SecondIncidentId == link.SecondIncidentId
                    && x.Reason == link.Reason,
                cancellationToken))
            {
                links.Add(link);
            }
        }

        database.IncidentCandidateLinks.AddRange(links);
        await AuditWriter.AppendAsync(
            database,
            "incidents.report.late_location_applied",
            "worker:incident-matcher",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["incidentId"] = source.IncidentId,
                ["reportId"] = source.ReportId,
                ["evidenceId"] = command.EvidenceId,
                ["candidateLinkCount"] = links.Count
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static IncidentReportCandidate ToCandidate(IncidentSourceReport report) => new(
        report.ReportId,
        report.ReporterIndependenceKey,
        report.OccurredAtUtc,
        report.ReceivedAtUtc,
        report.Latitude,
        report.Longitude,
        report.LocationConfidence,
        report.IncidentType,
        report.Severity,
        report.CasualtyMinimum,
        report.CasualtyMaximum,
        report.EvidenceReferences.Split(',', StringSplitOptions.RemoveEmptyEntries),
        report.VerifiedPilotIdentityKey,
        report.LocationDescription,
        report.Direction,
        report.VictimState,
        report.SceneState);
}

public static class ApplyLateIncidentLocationHandler
{
    public static async Task Handle(
        ApplyLateIncidentLocation command,
        LateIncidentLocationProcessor processor,
        CancellationToken cancellationToken)
    {
        await processor.ApplyAsync(command, cancellationToken);
    }
}
