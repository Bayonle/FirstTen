using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;

namespace First10.Infrastructure.Modules.Intake.Media;

public sealed class WhatsAppMediaSource(IConfiguration configuration) : IProviderMediaSource
{
    private static readonly HttpClient MetadataClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public IntakeChannel Channel => IntakeChannel.WhatsApp;

    public async Task<ProviderMediaContent> OpenAsync(
        string providerMediaHandle,
        IntakeMediaKind kind,
        CancellationToken cancellationToken = default)
    {
        var graphApiBaseUrl = configuration["Channels:WhatsApp:GraphApiBaseUrl"];
        var accessToken = configuration["Channels:WhatsApp:AccessToken"];
        var allowedHosts = (configuration["Channels:WhatsApp:AllowedMediaHosts"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!Uri.TryCreate(graphApiBaseUrl, UriKind.Absolute, out var graphBase)
            || graphBase.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(accessToken)
            || allowedHosts.Length == 0)
        {
            throw new MediaPrivacyException(MediaFailureCode.ProviderUnavailable);
        }

        try
        {
            var metadataUri = new Uri(
                $"{graphApiBaseUrl!.TrimEnd('/')}/{Uri.EscapeDataString(providerMediaHandle)}");
            using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, metadataUri);
            metadataRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var suppression = SuppressInstrumentationScope.Begin();
            using var metadataResponse = await MetadataClient.SendAsync(metadataRequest, cancellationToken);
            if (!metadataResponse.IsSuccessStatusCode)
            {
                throw new MediaPrivacyException(
                    metadataResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
                        ? MediaFailureCode.ProviderHandleExpired
                        : MediaFailureCode.ProviderUnavailable);
            }

            using var document = await JsonDocument.ParseAsync(
                await metadataResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            var url = root.GetProperty("url").GetString();
            var mimeType = root.GetProperty("mime_type").GetString();
            var fileSize = root.TryGetProperty("file_size", out var size)
                ? size.GetInt64()
                : (long?)null;
            if (!SafeRemoteMediaPolicy.TryValidateUri(url, allowedHosts, out var mediaUri))
            {
                throw new MediaPrivacyException(MediaFailureCode.DownloadRejected);
            }

            var downloadClient = SafeRemoteMediaPolicy.CreatePinnedClient(mediaUri!.Host);
            var downloadRequest = new HttpRequestMessage(HttpMethod.Get, mediaUri);
            downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var downloadResponse = await downloadClient.SendAsync(
                downloadRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            downloadRequest.Dispose();
            if (!downloadResponse.IsSuccessStatusCode)
            {
                downloadResponse.Dispose();
                downloadClient.Dispose();
                throw new MediaPrivacyException(MediaFailureCode.ProviderHandleExpired);
            }

            var responseType = downloadResponse.Content.Headers.ContentType?.MediaType;
            if (responseType is not null
                && !string.Equals(responseType, mimeType, StringComparison.OrdinalIgnoreCase))
            {
                downloadResponse.Dispose();
                downloadClient.Dispose();
                throw new MediaPrivacyException(MediaFailureCode.ContentTypeMismatch);
            }

            var contentLength = downloadResponse.Content.Headers.ContentLength ?? fileSize;
            var stream = await downloadResponse.Content.ReadAsStreamAsync(cancellationToken);
            return new ProviderMediaContent(
                stream,
                mimeType,
                contentLength,
                new CompositeDisposable(downloadResponse, downloadClient));
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
        catch (SocketException)
        {
            throw new MediaPrivacyException(MediaFailureCode.DownloadRejected);
        }
    }

    private sealed class CompositeDisposable(params IDisposable[] items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }
}

public static class SafeRemoteMediaPolicy
{
    public static bool TryValidateUri(
        string? value,
        IReadOnlyCollection<string> allowedHosts,
        out Uri? uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !allowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
        {
            uri = null;
            return false;
        }

        return true;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xFE) != 0xFC;
        }

        return bytes[0] != 0
               && bytes[0] != 10
               && bytes[0] != 127
               && bytes[0] < 224
               && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
               && !(bytes[0] == 169 && bytes[1] == 254)
               && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
               && !(bytes[0] == 192 && bytes[1] == 168)
               && !(bytes[0] == 198 && bytes[1] is 18 or 19);
    }

    public static HttpClient CreatePinnedClient(string allowedHost)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!string.Equals(
                        context.DnsEndPoint.Host,
                        allowedHost,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new SocketException((int)SocketError.AccessDenied);
                }

                var addresses = await Dns.GetHostAddressesAsync(
                    context.DnsEndPoint.Host,
                    cancellationToken);
                if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
                {
                    throw new SocketException((int)SocketError.AccessDenied);
                }

                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(
                            new IPEndPoint(address, context.DnsEndPoint.Port),
                            cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException)
                    {
                        socket.Dispose();
                    }
                }

                throw new SocketException((int)SocketError.HostUnreachable);
            }
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }
}
