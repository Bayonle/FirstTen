using System.Text.Json;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.BuildingBlocks.Persistence;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace First10.Infrastructure.Persistence.Writes;

public static class InboundReceiptWriter
{
    public static async Task<bool> TryAcceptAsync(
        First10DbContext database,
        SemanticMessageIdentity identity,
        AcceptedInboundWork command,
        CancellationToken cancellationToken = default)
    {
        var alreadyAccepted = await database.InboundMessageReceipts.AnyAsync(
            x => x.Scope == identity.Scope && x.Key == identity.Key,
            cancellationToken);
        if (alreadyAccepted)
        {
            return false;
        }

        var acceptedAtUtc = DateTimeOffset.UtcNow;
        database.InboundMessageReceipts.Add(InboundMessageReceipt.Create(
            identity.Scope,
            identity.Key,
            command.WorkId,
            acceptedAtUtc));
        database.OutboundMessageRecords.Add(OutboundMessageRecord.Create(
            command.WorkId,
            typeof(AcceptedInboundWork).FullName!,
            JsonSerializer.Serialize(command),
            acceptedAtUtc));

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            database.ChangeTracker.Clear();
            return false;
        }
    }
}
