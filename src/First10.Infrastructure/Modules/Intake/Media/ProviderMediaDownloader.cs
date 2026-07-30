using System.Buffers;
using System.Security.Cryptography;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;

namespace First10.Infrastructure.Modules.Intake.Media;

public interface IProviderMediaSource
{
    IntakeChannel Channel { get; }

    Task<ProviderMediaContent> OpenAsync(
        string providerMediaHandle,
        IntakeMediaKind kind,
        CancellationToken cancellationToken = default);
}

public sealed class ProviderMediaContent(
    Stream content,
    string? declaredContentType,
    long? declaredLength,
    IDisposable? owner = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public string? DeclaredContentType { get; } = declaredContentType;

    public long? DeclaredLength { get; } = declaredLength;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
        owner?.Dispose();
    }
}

public sealed class DownloadedMedia : IDisposable
{
    private byte[]? _buffer;

    internal DownloadedMedia(byte[] buffer, int length, string contentType)
    {
        _buffer = buffer;
        Length = length;
        ContentType = contentType;
    }

    public int Length { get; }

    public string ContentType { get; }

    public ReadOnlyMemory<byte> Bytes => _buffer is null
        ? throw new ObjectDisposedException(nameof(DownloadedMedia))
        : _buffer.AsMemory(0, Length);

    public void Dispose()
    {
        if (_buffer is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_buffer);
        _buffer = null;
    }
}

public sealed class ProviderMediaDownloader(IEnumerable<IProviderMediaSource> sources)
{
    public async Task<DownloadedMedia> DownloadAsync(
        IntakeChannel channel,
        string providerMediaHandle,
        IntakeMediaKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMediaHandle);
        var source = sources.Single(x => x.Channel == channel);
        await using var remote = await source.OpenAsync(
            providerMediaHandle,
            kind,
            cancellationToken);
        var maximumBytes = MediaPrivacyPolicy.MaximumBytes(kind);
        if (remote.DeclaredLength is < 0 || remote.DeclaredLength > maximumBytes)
        {
            throw new MediaPrivacyException(MediaFailureCode.PayloadTooLarge);
        }

        if (!MediaPrivacyPolicy.IsAllowedDeclaredContentType(kind, remote.DeclaredContentType))
        {
            throw new MediaPrivacyException(MediaFailureCode.UnsupportedContentType);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(maximumBytes + 1);
        var length = 0;
        try
        {
            while (length <= maximumBytes)
            {
                var read = await remote.Content.ReadAsync(
                    buffer.AsMemory(length, maximumBytes + 1 - length),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length == 0)
            {
                throw new MediaPrivacyException(MediaFailureCode.DownloadRejected);
            }

            if (length > maximumBytes)
            {
                throw new MediaPrivacyException(MediaFailureCode.PayloadTooLarge);
            }

            var detectedContentType = MediaContentInspector.Detect(buffer.AsSpan(0, length));
            if (!string.Equals(
                    detectedContentType,
                    remote.DeclaredContentType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new MediaPrivacyException(MediaFailureCode.ContentTypeMismatch);
            }

            var owned = GC.AllocateUninitializedArray<byte>(length);
            buffer.AsSpan(0, length).CopyTo(owned);
            return new DownloadedMedia(owned, length, detectedContentType);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

public static class MediaContentInspector
{
    public static string Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (content.Length >= 12
            && content[..4].SequenceEqual("RIFF"u8)
            && content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (content.StartsWith("OggS"u8))
        {
            return "audio/ogg";
        }

        if (content.Length >= 2 && content[0] == 0xFF && (content[1] & 0xF6) == 0xF0)
        {
            return "audio/aac";
        }

        if (content.StartsWith("ID3"u8)
            || content.Length >= 2 && content[0] == 0xFF && (content[1] & 0xE0) == 0xE0)
        {
            return "audio/mpeg";
        }

        if (content.Length >= 12 && content.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return "audio/mp4";
        }

        if (content.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
        {
            return "audio/webm";
        }

        throw new MediaPrivacyException(MediaFailureCode.UnsupportedContentType);
    }
}
