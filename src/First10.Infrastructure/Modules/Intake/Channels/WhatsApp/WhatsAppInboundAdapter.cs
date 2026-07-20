using System.Globalization;
using System.Text.Json;
using First10.Modules.Intake;

namespace First10.Infrastructure.Modules.Intake.Channels.WhatsApp;

public sealed class WhatsAppInboundAdapter : IChannelInboundAdapter
{
    public IReadOnlyList<ProviderInboundMessage> Parse(ReadOnlyMemory<byte> payload)
    {
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
        var messages = new List<ProviderInboundMessage>();
        if (!document.RootElement.TryGetProperty("entry", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return messages;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes))
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)
                    || !value.TryGetProperty("messages", out var inboundMessages))
                {
                    continue;
                }

                foreach (var message in inboundMessages.EnumerateArray())
                {
                    var address = message.GetProperty("from").GetString()
                        ?? throw new JsonException("WhatsApp sender is missing.");
                    var (kind, handle, location) = ReadContent(message);
                    messages.Add(new ProviderInboundMessage(
                        IntakeChannel.WhatsApp,
                        message.GetProperty("id").GetString()
                            ?? throw new JsonException("WhatsApp message ID is missing."),
                        address,
                        address,
                        kind,
                        handle,
                        DateTimeOffset.FromUnixTimeSeconds(long.Parse(
                            message.GetProperty("timestamp").GetString()!,
                            CultureInfo.InvariantCulture)),
                        location));
                }
            }
        }

        return messages;
    }

    private static (IntakeContentKind Kind, string? Handle, IntakeLocation? Location) ReadContent(
        JsonElement message)
    {
        var type = message.GetProperty("type").GetString();
        if (type == "audio" && message.TryGetProperty("audio", out var audio))
        {
            return (IntakeContentKind.Voice, audio.GetProperty("id").GetString(), null);
        }

        if (type == "image" && message.TryGetProperty("image", out var image))
        {
            return (IntakeContentKind.Photo, image.GetProperty("id").GetString(), null);
        }

        if (type == "location" && message.TryGetProperty("location", out var pin))
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
