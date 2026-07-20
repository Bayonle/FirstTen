using System.Text.Json;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;

namespace First10.Infrastructure.Modules.Intake.Media;

public sealed class TelegramMediaSource(IConfiguration configuration) : IProviderMediaSource
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public IntakeChannel Channel => IntakeChannel.Telegram;

    public async Task<ProviderMediaContent> OpenAsync(
        string providerMediaHandle,
        IntakeMediaKind kind,
        CancellationToken cancellationToken = default)
    {
        var botToken = configuration["Channels:Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            throw new MediaPrivacyException(MediaFailureCode.ProviderUnavailable);
        }

        try
        {
            var metadataUri = new Uri(
                $"https://api.telegram.org/bot{botToken}/getFile?file_id={Uri.EscapeDataString(providerMediaHandle)}");
            using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, metadataUri);
            using var suppression = SuppressInstrumentationScope.Begin();
            using var metadataResponse = await Client.SendAsync(metadataRequest, cancellationToken);
            if (!metadataResponse.IsSuccessStatusCode)
            {
                throw new MediaPrivacyException(
                    metadataResponse.StatusCode is System.Net.HttpStatusCode.BadRequest
                        or System.Net.HttpStatusCode.NotFound
                        ? MediaFailureCode.ProviderHandleExpired
                        : MediaFailureCode.ProviderUnavailable);
            }

            using var document = await JsonDocument.ParseAsync(
                await metadataResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var result = document.RootElement.GetProperty("result");
            var filePath = result.GetProperty("file_path").GetString();
            if (!IsSafeProviderPath(filePath))
            {
                throw new MediaPrivacyException(MediaFailureCode.DownloadRejected);
            }

            var providerLength = result.TryGetProperty("file_size", out var fileSize)
                ? fileSize.GetInt64()
                : (long?)null;
            var downloadUri = new Uri($"https://api.telegram.org/file/bot{botToken}/{filePath}");
            var downloadRequest = new HttpRequestMessage(HttpMethod.Get, downloadUri);
            var downloadResponse = await Client.SendAsync(
                downloadRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            downloadRequest.Dispose();
            if (!downloadResponse.IsSuccessStatusCode)
            {
                downloadResponse.Dispose();
                throw new MediaPrivacyException(MediaFailureCode.ProviderUnavailable);
            }

            var contentType = downloadResponse.Content.Headers.ContentType?.MediaType;
            var contentLength = downloadResponse.Content.Headers.ContentLength ?? providerLength;
            var stream = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken);
            return new ProviderMediaContent(stream, contentType, contentLength, downloadResponse);
        }
        catch (MediaPrivacyException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new MediaPrivacyException(MediaFailureCode.ProviderUnavailable);
        }
        catch (TaskCanceledException)
        {
            throw new MediaPrivacyException(MediaFailureCode.ProviderUnavailable);
        }
        catch (JsonException)
        {
            throw new MediaPrivacyException(MediaFailureCode.DownloadRejected);
        }
        catch (InvalidOperationException)
        {
            throw new MediaPrivacyException(MediaFailureCode.DownloadRejected);
        }
    }

    public static bool IsSafeProviderPath(string? filePath) =>
        !string.IsNullOrWhiteSpace(filePath)
        && filePath.Length <= 512
        && filePath[0] != '/'
        && !filePath.Contains("..", StringComparison.Ordinal)
        && !filePath.Contains('\\')
        && !filePath.Contains('?')
        && !filePath.Contains('#')
        && Uri.TryCreate(filePath, UriKind.Relative, out _);
}
