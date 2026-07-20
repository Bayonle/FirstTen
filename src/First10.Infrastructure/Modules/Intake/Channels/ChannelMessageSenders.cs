using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using First10.Modules.Intake;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;

namespace First10.Infrastructure.Modules.Intake.Channels;

public sealed record ProviderSendResult(
    ChannelDeliveryStatus Status,
    string? ProviderMessageId,
    string FailureCode)
{
    public static ProviderSendResult Accepted(string providerMessageId) =>
        new(ChannelDeliveryStatus.Accepted, providerMessageId, string.Empty);

    public static ProviderSendResult Failed(string failureCode) =>
        new(ChannelDeliveryStatus.Failed, null, failureCode);

    public static ProviderSendResult Unknown(string failureCode) =>
        new(ChannelDeliveryStatus.Unknown, null, failureCode);
}

public interface IChannelMessageSender
{
    IntakeChannel Channel { get; }

    Task<ProviderSendResult> SendAsync(
        string destination,
        string text,
        CancellationToken cancellationToken = default);
}

public sealed class TelegramChannelMessageSender(IConfiguration configuration) : IChannelMessageSender
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public IntakeChannel Channel => IntakeChannel.Telegram;

    public async Task<ProviderSendResult> SendAsync(
        string destination,
        string text,
        CancellationToken cancellationToken = default)
    {
        var botToken = configuration["Channels:Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return ProviderSendResult.Failed("telegram_not_configured");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.telegram.org/bot{botToken}/sendMessage")
        {
            Content = JsonContent.Create(new { chat_id = destination, text })
        };
        try
        {
            using var suppression = SuppressInstrumentationScope.Begin();
            using var response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ProviderSendResult.Failed($"telegram_http_{(int)response.StatusCode}");
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            return root.TryGetProperty("ok", out var ok)
                   && ok.GetBoolean()
                   && root.TryGetProperty("result", out var result)
                   && result.TryGetProperty("message_id", out var messageId)
                ? ProviderSendResult.Accepted(messageId.GetRawText())
                : ProviderSendResult.Unknown("telegram_response_unknown");
        }
        catch (HttpRequestException)
        {
            return ProviderSendResult.Unknown("telegram_network_unknown");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderSendResult.Unknown("telegram_timeout_unknown");
        }
        catch (JsonException)
        {
            return ProviderSendResult.Unknown("telegram_response_unknown");
        }
    }
}

public sealed class WhatsAppChannelMessageSender(IConfiguration configuration) : IChannelMessageSender
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public IntakeChannel Channel => IntakeChannel.WhatsApp;

    public async Task<ProviderSendResult> SendAsync(
        string destination,
        string text,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = configuration["Channels:WhatsApp:GraphApiBaseUrl"];
        var phoneNumberId = configuration["Channels:WhatsApp:PhoneNumberId"];
        var accessToken = configuration["Channels:WhatsApp:AccessToken"];
        if (string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(phoneNumberId)
            || string.IsNullOrWhiteSpace(accessToken))
        {
            return ProviderSendResult.Failed("whatsapp_not_configured");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl.TrimEnd('/')}/{phoneNumberId}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = destination,
                type = "text",
                text = new { preview_url = false, body = text }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var suppression = SuppressInstrumentationScope.Begin();
            using var response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ProviderSendResult.Failed($"whatsapp_http_{(int)response.StatusCode}");
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            return TryReadMessageId(document.RootElement, out var messageId)
                ? ProviderSendResult.Accepted(messageId)
                : ProviderSendResult.Unknown("whatsapp_response_unknown");
        }
        catch (HttpRequestException)
        {
            return ProviderSendResult.Unknown("whatsapp_network_unknown");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderSendResult.Unknown("whatsapp_timeout_unknown");
        }
        catch (JsonException)
        {
            return ProviderSendResult.Unknown("whatsapp_response_unknown");
        }
    }

    private static bool TryReadMessageId(JsonElement root, out string messageId)
    {
        messageId = string.Empty;
        if (!root.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array
            || messages.GetArrayLength() == 0
            || !messages[0].TryGetProperty("id", out var id))
        {
            return false;
        }

        messageId = id.GetString() ?? string.Empty;
        return messageId.Length > 0;
    }
}
