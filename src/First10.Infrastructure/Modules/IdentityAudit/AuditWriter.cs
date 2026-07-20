using System.Data;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Modules.IdentityAudit;

public static class AuditWriter
{
    public static async Task<AuditEvent> AppendAsync(
        First10DbContext database,
        string action,
        string actorId,
        AuditPayload payload,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        await database.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(683377602)",
            cancellationToken);
        var previous = await database.AuditEvents
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
        var auditEvent = AuditEvent.Create(
            (previous?.Sequence ?? 0) + 1,
            DateTimeOffset.UtcNow,
            action,
            actorId,
            payload,
            previous?.Hash ?? string.Empty);

        database.AuditEvents.Add(auditEvent);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return auditEvent;
    }
}
