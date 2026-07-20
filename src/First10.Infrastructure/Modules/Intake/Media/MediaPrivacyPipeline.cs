using First10.Modules.Intake;
using First10.Modules.Intake.Media;

namespace First10.Infrastructure.Modules.Intake.Media;

public sealed class SanitizedAudio(
    byte[] bytes,
    string contentType,
    string sanitizerVersion) : IDisposable
{
    private byte[]? _bytes = bytes;

    public ReadOnlyMemory<byte> Bytes => _bytes is null
        ? throw new ObjectDisposedException(nameof(SanitizedAudio))
        : _bytes;

    public string ContentType { get; } = contentType;

    public string SanitizerVersion { get; } = sanitizerVersion;

    public void Dispose()
    {
        if (_bytes is null)
        {
            return;
        }

        System.Security.Cryptography.CryptographicOperations.ZeroMemory(_bytes);
        _bytes = null;
    }
}

public interface IAudioSanitizer
{
    Task<SanitizedAudio> SanitizeAsync(
        ReadOnlyMemory<byte> rawAudio,
        string sourceContentType,
        CancellationToken cancellationToken = default);
}

public sealed record SafeMediaObject(
    string ObjectKey,
    string ContentType,
    long Length,
    string EncryptionKeyVersion,
    DateTimeOffset ExpiresAtUtc);

public sealed record ProcessedMediaResult(
    SafeMediaObject SafeObject,
    string PrivacyProcessorVersion);

public interface ISafeMediaStore
{
    Task<SafeMediaObject> StoreAsync(
        Guid assetId,
        IntakeMediaKind kind,
        ReadOnlyMemory<byte> safeDerivative,
        string contentType,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
}

public sealed class MediaPrivacyPipeline(
    ProviderMediaDownloader downloader,
    IImageRedactor imageRedactor,
    IAudioSanitizer audioSanitizer,
    ISafeMediaStore safeMediaStore)
{
    public async Task<ProcessedMediaResult> ProcessAsync(
        Guid assetId,
        IntakeChannel channel,
        string providerMediaHandle,
        IntakeMediaKind kind,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var downloaded = await DownloadAsync();

        if (kind == IntakeMediaKind.Image)
        {
            RedactedImage redacted;
            try
            {
                redacted = await imageRedactor.RedactAsync(downloaded.Bytes, cancellationToken);
            }
            catch (MediaPrivacyException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new MediaPrivacyException(MediaFailureCode.RedactionFailed);
            }

            if (redacted.FaceCount <= 0
                || redacted.Bytes.IsEmpty
                || redacted.ContentType != "image/jpeg"
                || !MediaPrivacyPolicy.IsSafeDecodedImageSize(redacted.Width, redacted.Height))
            {
                throw new MediaPrivacyException(MediaFailureCode.RedactionUncertain);
            }

            var stored = await StoreAsync(
                assetId,
                kind,
                redacted.Bytes,
                redacted.ContentType,
                expiresAtUtc);
            return new ProcessedMediaResult(stored, redacted.ModelVersion);
        }

        using SanitizedAudio sanitized = await SanitizeAudioAsync();
        if (sanitized.Bytes.IsEmpty
            || !MediaPrivacyPolicy.IsAllowedDeclaredContentType(kind, sanitized.ContentType))
        {
            throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
        }

        var storedAudio = await StoreAsync(
            assetId,
            kind,
            sanitized.Bytes,
            sanitized.ContentType,
            expiresAtUtc);
        return new ProcessedMediaResult(storedAudio, sanitized.SanitizerVersion);

        async Task<SanitizedAudio> SanitizeAudioAsync()
        {
            try
            {
                return await audioSanitizer.SanitizeAsync(
                    downloaded.Bytes,
                    downloaded.ContentType,
                    cancellationToken);
            }
            catch (MediaPrivacyException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
            }
        }

        async Task<DownloadedMedia> DownloadAsync()
        {
            try
            {
                return await downloader.DownloadAsync(
                    channel,
                    providerMediaHandle,
                    kind,
                    cancellationToken);
            }
            catch (MediaPrivacyException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new MediaPrivacyException(MediaFailureCode.DownloadRejected);
            }
        }

        async Task<SafeMediaObject> StoreAsync(
            Guid id,
            IntakeMediaKind mediaKind,
            ReadOnlyMemory<byte> derivative,
            string derivativeContentType,
            DateTimeOffset expiry)
        {
            try
            {
                return await safeMediaStore.StoreAsync(
                    id,
                    mediaKind,
                    derivative,
                    derivativeContentType,
                    expiry,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new MediaPrivacyException(MediaFailureCode.StorageFailed);
            }
        }
    }
}
