namespace First10.Modules.Intake.Media;

public enum IntakeMediaKind
{
    Image = 1,
    Audio = 2
}

public enum MediaProcessingStatus
{
    Pending = 1,
    Stored = 2,
    Rejected = 3,
    Deleted = 4
}

public enum MediaFailureCode
{
    ProviderHandleExpired = 1,
    ProviderUnavailable = 2,
    DownloadRejected = 3,
    PayloadTooLarge = 4,
    UnsupportedContentType = 5,
    ContentTypeMismatch = 6,
    DecodeRejected = 7,
    ResourceLimitExceeded = 8,
    RedactionUncertain = 9,
    RedactionFailed = 10,
    StorageFailed = 11
}

public static class MediaPrivacyPolicy
{
    public const int MaximumImageBytes = 12 * 1024 * 1024;
    public const int MaximumAudioBytes = 16 * 1024 * 1024;
    public const int MaximumImageDimension = 8_192;
    public const long MaximumDecodedPixels = 32_000_000;
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);

    public static bool IsAllowedDeclaredContentType(IntakeMediaKind kind, string? contentType) =>
        kind switch
        {
            IntakeMediaKind.Image => contentType is "image/jpeg" or "image/png" or "image/webp",
            IntakeMediaKind.Audio => contentType is "audio/ogg" or "audio/mpeg" or "audio/mp4"
                or "audio/aac" or "audio/webm",
            _ => false
        };

    public static int MaximumBytes(IntakeMediaKind kind) => kind switch
    {
        IntakeMediaKind.Image => MaximumImageBytes,
        IntakeMediaKind.Audio => MaximumAudioBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported media kind.")
    };

    public static bool IsSafeDecodedImageSize(int width, int height) =>
        width > 0
        && height > 0
        && width <= MaximumImageDimension
        && height <= MaximumImageDimension
        && (long)width * height <= MaximumDecodedPixels;

    public static string CreateOpaqueObjectKey(Guid assetId, IntakeMediaKind kind)
    {
        if (assetId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty asset ID is required.", nameof(assetId));
        }

        var category = kind == IntakeMediaKind.Image ? "images" : "audio";
        return $"safe/{category}/{assetId:N}";
    }
}

public sealed record RedactedImage(
    ReadOnlyMemory<byte> Bytes,
    string ContentType,
    int Width,
    int Height,
    int FaceCount,
    string ModelVersion);

public interface IImageRedactor
{
    Task<RedactedImage> RedactAsync(
        ReadOnlyMemory<byte> rawImage,
        CancellationToken cancellationToken = default);
}

public sealed class MediaPrivacyException(MediaFailureCode failureCode)
    : Exception(failureCode.ToString())
{
    public MediaFailureCode FailureCode { get; } = failureCode;
}
