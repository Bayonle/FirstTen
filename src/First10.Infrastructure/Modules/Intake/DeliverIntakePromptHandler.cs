using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace First10.Infrastructure.Modules.Intake;

public static class DeliverIntakePromptHandler
{
    public static async Task Handle(
        DeliverIntakePrompt command,
        IntakePromptDeliveryService delivery,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var retryDelay = await delivery.TryDeliverAsync(command, cancellationToken);
        if (retryDelay.HasValue)
        {
            await bus.ScheduleAsync(command, retryDelay.Value);
        }
    }
}

public sealed class IntakePromptDeliveryService(
    First10DbContext database,
    ProtectedContactIdentityResolver contacts,
    IEnumerable<IChannelMessageSender> senders)
{
    public async Task<TimeSpan?> TryDeliverAsync(
        DeliverIntakePrompt command,
        CancellationToken cancellationToken = default)
    {
        var intent = await database.IntakePromptIntents.SingleOrDefaultAsync(
            x => x.Id == command.IntentId,
            cancellationToken);
        if (intent is null || !intent.CanAttemptDelivery)
        {
            return null;
        }

        var sender = senders.Single(x => x.Channel == intent.Channel);
        var destination = await contacts.ResolveDestinationAsync(
            intent.ContactReference,
            cancellationToken);
        var text = IntakeMessageCatalogue.Get(intent.Language, intent.Prompt);
        var result = await sender.SendAsync(destination, text, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        TimeSpan? retryDelay = null;
        switch (result.Status)
        {
            case ChannelDeliveryStatus.Accepted:
                intent.TryMarkProviderAccepted(result.ProviderMessageId!, now);
                break;
            case ChannelDeliveryStatus.Failed:
                intent.TryMarkFailed(result.FailureCode, now);
                if (intent.CanAttemptDelivery)
                {
                    retryDelay = TimeSpan.FromSeconds(intent.AttemptCount == 1 ? 5 : 15);
                }

                break;
            default:
                intent.TryMarkUnknown(result.FailureCode, now);
                break;
        }

        await database.SaveChangesAsync(cancellationToken);
        return retryDelay;
    }
}
