using First10.Infrastructure.Persistence;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace First10.Infrastructure.Modules.Intake.Channels;

public sealed class ChannelWebhookIngress(
    ChannelEnvelopeMapper mapper,
    IDbContextOutbox<First10DbContext> outbox)
{
    public async Task<bool> TryAcceptAsync(
        ProviderInboundMessage providerMessage,
        CancellationToken cancellationToken = default)
    {
        var envelope = await mapper.MapAsync(providerMessage, cancellationToken);
        var identity = SemanticMessageIdentity.Create(
            providerMessage.Channel.ToString(),
            providerMessage.ProviderMessageId);
        if (await outbox.DbContext.InboundMessageReceipts.AnyAsync(
                x => x.Scope == identity.Scope && x.Key == identity.Key,
                cancellationToken))
        {
            return false;
        }

        var workId = Guid.NewGuid();
        outbox.DbContext.InboundMessageReceipts.Add(InboundMessageReceipt.Create(
            identity.Scope,
            identity.Key,
            workId,
            DateTimeOffset.UtcNow));
        await outbox.PublishAsync(new AcceptInboundEnvelope(workId, envelope));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            outbox.DbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
