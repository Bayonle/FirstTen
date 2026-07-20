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

public interface IChannelVoiceMessageSender
{
    IntakeChannel Channel { get; }

    Task<ProviderSendResult> SendVoiceAsync(
        string destination,
        ReadOnlyMemory<byte> voice,
        string contentType,
        CancellationToken cancellationToken = default);
}

public sealed class TelegramChannelMessageSender(IConfiguration configuration)
    : IChannelMessageSender, IChannelVoiceMessageSender
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

    public async Task<ProviderSendResult> SendVoiceAsync(
        string destination,
        ReadOnlyMemory<byte> voice,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var botToken = configuration["Channels:Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return ProviderSendResult.Failed("telegram_not_configured");
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(destination), "chat_id");
        var audio = new ByteArrayContent(voice.ToArray());
        audio.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(audio, "voice", "guidance.ogg");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.telegram.org/bot{botToken}/sendVoice")
        {
            Content = content
        };
        return await SendTelegramAsync(request, cancellationToken);
    }

    private static async Task<ProviderSendResult> SendTelegramAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
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

public sealed class WhatsAppChannelMessageSender(IConfiguration configuration)
    : IChannelMessageSender, IChannelVoiceMessageSender
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

    public async Task<ProviderSendResult> SendVoiceAsync(
        string destination,
        ReadOnlyMemory<byte> voice,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var settings = ReadSettings();
        if (settings is null)
        {
            return ProviderSendResult.Failed("whatsapp_not_configured");
        }

        string mediaId;
        using (var upload = new HttpRequestMessage(
                   HttpMethod.Post,
                   $"{settings.Value.BaseUrl}/{settings.Value.PhoneNumberId}/media"))
        {
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent("whatsapp"), "messaging_product");
            var audio = new ByteArrayContent(voice.ToArray());
            audio.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            multipart.Add(audio, "file", "guidance.ogg");
            upload.Content = multipart;
            upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Value.AccessToken);
            var uploaded = await SendWhatsAppAsync(upload, "whatsapp_media_upload", cancellationToken);
            if (uploaded.Status != ChannelDeliveryStatus.Accepted || uploaded.ProviderMessageId is null)
            {
                return uploaded;
            }

            mediaId = uploaded.ProviderMessageId;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{settings.Value.BaseUrl}/{settings.Value.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = destination,
                type = "audio",
                audio = new { id = mediaId }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Value.AccessToken);
        return await SendWhatsAppAsync(request, "whatsapp_voice", cancellationToken);
    }

    private (string BaseUrl, string PhoneNumberId, string AccessToken)? ReadSettings()
    {
        var baseUrl = configuration["Channels:WhatsApp:GraphApiBaseUrl"];
        var phoneNumberId = configuration["Channels:WhatsApp:PhoneNumberId"];
        var accessToken = configuration["Channels:WhatsApp:AccessToken"];
        return string.IsNullOrWhiteSpace(baseUrl)
               || string.IsNullOrWhiteSpace(phoneNumberId)
               || string.IsNullOrWhiteSpace(accessToken)
            ? null
            : (baseUrl.TrimEnd('/'), phoneNumberId, accessToken);
    }

    private static async Task<ProviderSendResult> SendWhatsAppAsync(
        HttpRequestMessage request,
        string failurePrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            using var suppression = SuppressInstrumentationScope.Begin();
            using var response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ProviderSendResult.Failed($"{failurePrefix}_http_{(int)response.StatusCode}");
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("id", out var uploadedId)
                && !string.IsNullOrWhiteSpace(uploadedId.GetString()))
            {
                return ProviderSendResult.Accepted(uploadedId.GetString()!);
            }

            return TryReadMessageId(document.RootElement, out var messageId)
                ? ProviderSendResult.Accepted(messageId)
                : ProviderSendResult.Unknown($"{failurePrefix}_response_unknown");
        }
        catch (HttpRequestException)
        {
            return ProviderSendResult.Unknown($"{failurePrefix}_network_unknown");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderSendResult.Unknown($"{failurePrefix}_timeout_unknown");
        }
        catch (JsonException)
        {
            return ProviderSendResult.Unknown($"{failurePrefix}_response_unknown");
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
