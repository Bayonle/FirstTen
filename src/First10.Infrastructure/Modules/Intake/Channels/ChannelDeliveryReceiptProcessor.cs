using First10.Infrastructure.Modules.Intake.Channels.WhatsApp;
using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace First10.Infrastructure.Modules.Intake.Channels;

public sealed class ChannelDeliveryReceiptProcessor(First10DbContext database)
{
    public async Task ApplyAsync(
        WhatsAppInboundAdapter.DeliveryReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        var intent = await database.IntakePromptIntents.SingleOrDefaultAsync(
            x => x.Channel == IntakeChannel.WhatsApp
                 && x.ProviderMessageId == receipt.ProviderMessageId,
            cancellationToken);
        if (intent is null)
        {
            var exists = await database.IntakeRecoveryItems.AnyAsync(
                x => x.Channel == IntakeChannel.WhatsApp
                     && x.ProviderMessageId == receipt.ProviderMessageId
                     && x.Reason == "unknown_delivery_receipt",
                cancellationToken);
            if (!exists)
            {
                database.IntakeRecoveryItems.Add(IntakeRecoveryItem.Create(
                    IntakeChannel.WhatsApp,
                    receipt.ProviderMessageId,
                    "unknown_delivery_receipt",
                    receipt.OccurredAtUtc));
            }
        }
        else
        {
            intent.TryApplyReceipt(receipt.Status, receipt.OccurredAtUtc, receipt.FailureCode);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            database.ChangeTracker.Clear();
        }
    }
}
