using System.Text.Json;
using First10.Modules.Intake;

namespace First10.Infrastructure.Modules.Intake.Channels.Telegram;

public sealed class TelegramInboundAdapter : IChannelInboundAdapter
{
    public IReadOnlyList<ProviderInboundMessage> Parse(ReadOnlyMemory<byte> payload)
    {
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (!root.TryGetProperty("update_id", out var updateId)
            || !root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("message_id", out _)
            || !message.TryGetProperty("date", out var sentAt)
            || !message.TryGetProperty("chat", out var chat)
            || !chat.TryGetProperty("id", out var chatId))
        {
            return [];
        }

        var (kind, mediaHandle, location) = ReadContent(message);
        var address = chatId.GetRawText();
        return
        [
            new ProviderInboundMessage(
                IntakeChannel.Telegram,
                updateId.GetRawText(),
                address,
                address,
                kind,
                mediaHandle,
                DateTimeOffset.FromUnixTimeSeconds(sentAt.GetInt64()),
                location)
        ];
    }

    private static (IntakeContentKind Kind, string? Handle, IntakeLocation? Location) ReadContent(
        JsonElement message)
    {
        if (message.TryGetProperty("voice", out var voice)
            && voice.TryGetProperty("file_id", out var voiceId))
        {
            return (IntakeContentKind.Voice, voiceId.GetString(), null);
        }

        if (message.TryGetProperty("photo", out var photos)
            && photos.ValueKind == JsonValueKind.Array
            && photos.GetArrayLength() > 0)
        {
            var largest = photos[photos.GetArrayLength() - 1];
            return (IntakeContentKind.Photo, largest.GetProperty("file_id").GetString(), null);
        }

        if (message.TryGetProperty("location", out var pin))
        {
            return (
                IntakeContentKind.Location,
                null,
                new IntakeLocation(
                    pin.GetProperty("latitude").GetDouble(),
                    pin.GetProperty("longitude").GetDouble()));
        }

        return (IntakeContentKind.Unsupported, null, null);
    }
}
