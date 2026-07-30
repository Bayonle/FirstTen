using System.Security.Cryptography;
using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Persistence;
using First10.Modules.Guidance;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using First10.Infrastructure.Modules.Operations;

namespace First10.Infrastructure.Modules.Intake.Delivery;

/// <summary>
/// Intake is the only module permitted to resolve a protected reporter destination.
/// Guidance supplies an opaque contact reference and immutable approved payload only.
/// </summary>
public sealed class OutboundChannelSender(
    First10DbContext database,
    ProtectedContactIdentityResolver contacts,
    IEnumerable<IChannelMessageSender> senders,
    IEnumerable<IChannelVoiceMessageSender> voiceSenders,
    IGuidanceVoiceAssetReader voiceAssets,
    TimeProvider timeProvider,
    PilotMetrics? metrics = null)
{
    public async Task<TimeSpan?> TryDeliverAsync(
        Guid intentId,
        CancellationToken cancellationToken = default)
    {
        await database.Database.OpenConnectionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_lock(hashtext({intentId.ToString()}))",
            cancellationToken);
        try
        {
            var intent = await database.GuidanceIntents.SingleOrDefaultAsync(
                x => x.Id == intentId,
                cancellationToken);
            if (intent is null || !intent.CanAttemptDelivery)
            {
                return null;
            }

            var attempts = await database.GuidanceDeliveryAttempts
                .Where(x => x.GuidanceIntentId == intentId)
                .OrderBy(x => x.AttemptNumber)
                .ToListAsync(cancellationToken);
            var interrupted = attempts.FirstOrDefault(x => x.Status == GuidanceDeliveryAttemptStatus.Started);
            if (interrupted is not null)
            {
                var now = timeProvider.GetUtcNow();
                interrupted.TryComplete(
                    GuidanceDeliveryAttemptStatus.Unknown,
                    now,
                    null,
                    "provider_outcome_unknown_after_interruption");
                intent.TryMarkUnknown("provider_outcome_unknown_after_interruption", now);
                await database.SaveChangesAsync(cancellationToken);
                metrics?.DeliveryException(intent.Channel.ToString(), "interrupted_unknown");
                return null;
            }

            var voice = await voiceAssets.ReadVerifiedAsync(
                intent.VoiceAssetKey,
                intent.VoiceSha256,
                cancellationToken);
            if (voice is null)
            {
                intent.TryMarkFailed("guidance_voice_asset_unavailable", timeProvider.GetUtcNow());
                await database.SaveChangesAsync(cancellationToken);
                return intent.CanAttemptDelivery
                    ? TimeSpan.FromSeconds(intent.AttemptCount == 1 ? 5 : 15)
                    : null;
            }

            // Destination plaintext exists only inside this Intake-owned boundary and is never persisted.
            var destination = await contacts.ResolveDestinationAsync(intent.ContactReference, cancellationToken);
            var channel = intent.Channel switch
            {
                GuidanceChannel.Telegram => First10.Modules.Intake.IntakeChannel.Telegram,
                GuidanceChannel.WhatsApp => First10.Modules.Intake.IntakeChannel.WhatsApp,
                _ => throw new InvalidOperationException("Unsupported guidance delivery channel.")
            };
            var sender = senders.Single(x => x.Channel == channel);
            var acceptedText = attempts.LastOrDefault(x =>
                x.Component == GuidanceDeliveryComponent.Text
                && x.Status is GuidanceDeliveryAttemptStatus.Accepted or GuidanceDeliveryAttemptStatus.Delivered);
            ProviderSendResult textResult;
            if (acceptedText is not null)
            {
                textResult = ProviderSendResult.Accepted(acceptedText.ProviderMessageId!);
            }
            else
            {
                var textAttempt = GuidanceDeliveryAttempt.Start(
                    intent.Id,
                    GuidanceDeliveryComponent.Text,
                    attempts.Count(x => x.Component == GuidanceDeliveryComponent.Text) + 1,
                    timeProvider.GetUtcNow());
                database.GuidanceDeliveryAttempts.Add(textAttempt);
                await database.SaveChangesAsync(cancellationToken);
                textResult = await sender.SendAsync(destination, intent.ExactText, cancellationToken);
                Complete(textAttempt, textResult, timeProvider.GetUtcNow());
                await database.SaveChangesAsync(cancellationToken);
            }

            if (textResult.Status != First10.Modules.Intake.ChannelDeliveryStatus.Accepted)
            {
                var now = timeProvider.GetUtcNow();
                if (textResult.Status == First10.Modules.Intake.ChannelDeliveryStatus.Failed)
                {
                    intent.TryMarkFailed(textResult.FailureCode, now);
                    metrics?.DeliveryException(channel.ToString(), "failed");
                }
                else
                {
                    intent.TryMarkUnknown(textResult.FailureCode, now);
                    metrics?.DeliveryException(channel.ToString(), "unknown");
                }

                await database.SaveChangesAsync(cancellationToken);
                return intent.CanAttemptDelivery
                    ? TimeSpan.FromSeconds(intent.AttemptCount == 1 ? 5 : 15)
                    : null;
            }

            var acceptedVoice = attempts.LastOrDefault(x =>
                x.Component == GuidanceDeliveryComponent.Voice
                && x.Status is GuidanceDeliveryAttemptStatus.Accepted or GuidanceDeliveryAttemptStatus.Delivered);
            ProviderSendResult voiceResult;
            if (acceptedVoice is not null)
            {
                voiceResult = ProviderSendResult.Accepted(acceptedVoice.ProviderMessageId!);
            }
            else
            {
                var voiceAttempt = GuidanceDeliveryAttempt.Start(
                    intent.Id,
                    GuidanceDeliveryComponent.Voice,
                    attempts.Count(x => x.Component == GuidanceDeliveryComponent.Voice) + 1,
                    timeProvider.GetUtcNow());
                database.GuidanceDeliveryAttempts.Add(voiceAttempt);
                await database.SaveChangesAsync(cancellationToken);
                var voiceSender = voiceSenders.Single(x => x.Channel == channel);
                voiceResult = await voiceSender.SendVoiceAsync(
                    destination,
                    voice.Bytes,
                    voice.ContentType,
                    cancellationToken);
                Complete(voiceAttempt, voiceResult, timeProvider.GetUtcNow());
            }
            if (voiceResult.Status == First10.Modules.Intake.ChannelDeliveryStatus.Accepted)
            {
                intent.TryMarkAccepted(
                    $"text:{textResult.ProviderMessageId};voice:{voiceResult.ProviderMessageId}",
                    timeProvider.GetUtcNow());
            }
            else
            {
                // Text may already be visible. Never resend the composite automatically.
                intent.TryMarkUnknown(
                    $"text_accepted_voice_{voiceResult.FailureCode}",
                    timeProvider.GetUtcNow());
                metrics?.DeliveryException(channel.ToString(), "voice_unknown_or_failed");
            }

            await database.SaveChangesAsync(cancellationToken);
            return null;
        }
        finally
        {
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_unlock(hashtext({intentId.ToString()}))",
                CancellationToken.None);
            await database.Database.CloseConnectionAsync();
        }
    }

    private static void Complete(
        GuidanceDeliveryAttempt attempt,
        ProviderSendResult result,
        DateTimeOffset completedAtUtc)
    {
        var status = result.Status switch
        {
            First10.Modules.Intake.ChannelDeliveryStatus.Accepted => GuidanceDeliveryAttemptStatus.Accepted,
            First10.Modules.Intake.ChannelDeliveryStatus.Failed => GuidanceDeliveryAttemptStatus.Failed,
            _ => GuidanceDeliveryAttemptStatus.Unknown
        };
        attempt.TryComplete(status, completedAtUtc, result.ProviderMessageId, result.FailureCode);
    }
}

public sealed record GuidanceVoiceAsset(ReadOnlyMemory<byte> Bytes, string ContentType);

public interface IGuidanceVoiceAssetReader
{
    Task<GuidanceVoiceAsset?> ReadVerifiedAsync(
        string assetKey,
        string expectedSha256,
        CancellationToken cancellationToken = default);
}

public sealed class FileGuidanceVoiceAssetReader(IConfiguration configuration)
    : IGuidanceVoiceAssetReader
{
    private const int MaximumVoiceBytes = 10 * 1024 * 1024;

    public async Task<GuidanceVoiceAsset?> ReadVerifiedAsync(
        string assetKey,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetKey)
            || expectedSha256.Length != 64
            || expectedSha256.Any(x => !Uri.IsHexDigit(x)))
        {
            return null;
        }

        var root = Path.GetFullPath(
            configuration["Guidance:AssetRoot"]
            ?? Path.Combine(AppContext.BaseDirectory, "guidance"));
        var path = Path.GetFullPath(Path.Combine(root, assetKey));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumVoiceBytes)
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(actual),
                System.Text.Encoding.ASCII.GetBytes(expectedSha256.ToLowerInvariant())))
        {
            CryptographicOperations.ZeroMemory(bytes);
            return null;
        }

        var contentType = string.Equals(Path.GetExtension(path), ".ogg", StringComparison.OrdinalIgnoreCase)
            ? "audio/ogg"
            : "audio/mpeg";
        return new GuidanceVoiceAsset(bytes, contentType);
    }
}

public static class DeliverGuidanceIntentHandler
{
    public static async Task Handle(
        DeliverGuidanceIntent command,
        OutboundChannelSender sender,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var retry = await sender.TryDeliverAsync(command.IntentId, cancellationToken);
        if (retry.HasValue)
        {
            await bus.ScheduleAsync(command, retry.Value);
        }
    }
}
