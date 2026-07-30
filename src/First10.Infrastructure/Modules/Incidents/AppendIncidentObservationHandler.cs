using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Modules.Incidents;

public sealed class IncidentObservationProcessor(First10DbContext database)
{
    public async Task<bool> AppendAsync(
        AppendIncidentObservation command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        IncidentObservation observation;
        try
        {
            observation = IncidentObservation.Create(command);
        }
        catch (ArgumentException)
        {
            return false;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({command.IncidentId.ToString()}))",
            cancellationToken);
        if (await database.IncidentTimelineEvents.AnyAsync(
                x => x.EventId == command.ObservationId,
                cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var incident = await database.Incidents
            .Include(x => x.SourceReports)
            .Include(x => x.Conflicts)
            .Include(x => x.Observations)
            .SingleOrDefaultAsync(x => x.Id == command.IncidentId, cancellationToken);
        if (incident is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var existingConflictIds = incident.Conflicts.Select(x => x.Id).ToHashSet();
        if (!incident.TryAttachObservation(observation))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        database.IncidentObservations.Add(observation);
        database.IncidentTimelineEvents.Add(IncidentTimelineEvent.ObservationAdded(observation));
        foreach (var conflict in incident.Conflicts.Where(x => !existingConflictIds.Contains(x.Id)))
        {
            database.IncidentConflicts.Add(conflict);
            database.IncidentTimelineEvents.Add(IncidentTimelineEvent.StateChanged(
                incident.Id,
                "conflict-detected",
                $"report:{command.SourceReportId:N}",
                observation.OccurredAtUtc,
                observation.ReceivedAtUtc,
                new
                {
                    conflictId = conflict.Id,
                    field = conflict.Field.ToString(),
                    conflict.LeftClaimId,
                    conflict.RightClaimId,
                    conflict.LeftReportId,
                    conflict.RightReportId
                }));
        }

        await AuditWriter.AppendAsync(
            database,
            "incidents.relay.observation_added",
            $"report:{command.SourceReportId:N}",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["incidentId"] = incident.Id,
                ["observationId"] = observation.Id,
                ["sourceReportId"] = observation.SourceReportId,
                ["unresolvedConflictCount"] = incident.Conflicts.Count(x => !x.IsResolved)
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

public static class AppendIncidentObservationHandler
{
    public static async Task Handle(
        AppendIncidentObservation command,
        IncidentObservationProcessor processor,
        CancellationToken cancellationToken)
    {
        await processor.AppendAsync(command, cancellationToken);
    }
}
