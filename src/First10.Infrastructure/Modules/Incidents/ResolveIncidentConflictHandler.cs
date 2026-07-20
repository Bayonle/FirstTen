using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Modules.Incidents;

public sealed class IncidentConflictResolutionProcessor(
    First10DbContext database,
    TimeProvider timeProvider)
{
    public async Task<bool> ResolveAsync(
        ResolveIncidentConflict command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({command.IncidentId.ToString()}))",
            cancellationToken);
        var incident = await database.Incidents
            .Include(x => x.Conflicts)
            .SingleOrDefaultAsync(x => x.Id == command.IncidentId, cancellationToken);
        var conflict = incident?.Conflicts.SingleOrDefault(x => x.Id == command.ConflictId);
        var now = timeProvider.GetUtcNow();
        if (incident is null
            || conflict is null
            || !incident.TryResolveConflict(
                command.ConflictId,
                command.SelectedReportId,
                command.ResolvedBy,
                now))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        database.IncidentTimelineEvents.Add(IncidentTimelineEvent.StateChanged(
            incident.Id,
            "conflict-resolved",
            $"dispatcher:{command.ResolvedBy}",
            now,
            now,
            new
            {
                conflictId = conflict.Id,
                field = conflict.Field.ToString(),
                selectedReportId = conflict.SelectedReportId,
                selectedClaimId = conflict.SelectedClaimId
            }));
        await AuditWriter.AppendAsync(
            database,
            "incidents.conflict.resolved",
            command.ResolvedBy,
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["incidentId"] = incident.Id,
                ["conflictId"] = conflict.Id,
                ["field"] = conflict.Field.ToString(),
                ["selectedReportId"] = conflict.SelectedReportId,
                ["selectedClaimId"] = conflict.SelectedClaimId
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

public static class ResolveIncidentConflictHandler
{
    public static async Task Handle(
        ResolveIncidentConflict command,
        IncidentConflictResolutionProcessor processor,
        CancellationToken cancellationToken)
    {
        await processor.ResolveAsync(command, cancellationToken);
    }
}
