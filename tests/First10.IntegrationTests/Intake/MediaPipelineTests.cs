using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Text;
using System.Net;
using Microsoft.Extensions.Configuration;
using First10.Infrastructure.Modules.Intake.Media;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using SkiaSharp;

namespace First10.IntegrationTests.Intake;

public sealed class MediaPipelineTests
{
    [Fact]
    public void PinnedOnnxModelLoadsAndRunsWhenBenchmarkArtifactIsPresent()
    {
        var modelPath = Environment.GetEnvironmentVariable("FIRST10_FACE_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Models:FaceRedaction:Path"] = modelPath }).Build();
        using var detector = new OnnxYuNetFaceDetector(configuration);
        using var image = new SKBitmap(320, 320);
        image.Erase(SKColors.Black);

        var faces = detector.Detect(image);

        Assert.Empty(faces);
        Assert.Equal(OnnxYuNetFaceDetector.Version, detector.ModelVersion);
    }

    [Fact]
    public void MissingDetectorArtifactFailsWithClosedPrivacyReason()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Models:FaceRedaction:Path"] = Path.Combine(
                    Path.GetTempPath(),
                    $"missing-{Guid.NewGuid():N}.onnx")
            }).Build();
        using var detector = new OnnxYuNetFaceDetector(configuration);
        using var image = new SKBitmap(32, 32);

        var error = Assert.Throws<MediaPrivacyException>(() => detector.Detect(image));

        Assert.Equal(MediaFailureCode.RedactionFailed, error.FailureCode);
    }

    [Fact]
    public void MediaEnvelopeIsAuthenticatedOpaqueAndKeyVersioned()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["MediaEncryption:ActiveKeyVersion"] = "test-v2",
                ["MediaEncryption:Keys:test-v2"] = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray())
            }).Build();
        var encryptor = new AesGcmMediaEnvelopeEncryptor(configuration);
        var assetId = Guid.NewGuid();
        byte[] derivative = [1, 2, 3, 4, 5, 6, 7, 8];

        var first = encryptor.Encrypt(assetId, derivative);
        var second = encryptor.Encrypt(assetId, derivative);
        var decrypted = encryptor.Decrypt(assetId, first.Bytes);

        Assert.Equal("test-v2", first.KeyVersion);
        Assert.Equal(derivative, decrypted);
        Assert.False(first.Bytes.AsSpan().IndexOf(derivative) >= 0);
        Assert.NotEqual(first.Bytes, second.Bytes);
        first.Bytes[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(assetId, first.Bytes));
        CryptographicOperations.ZeroMemory(decrypted);
        CryptographicOperations.ZeroMemory(first.Bytes);
        CryptographicOperations.ZeroMemory(second.Bytes);
    }

    [Theory]
    [InlineData("photos/file_12.jpg", true)]
    [InlineData("voice/file_12.oga", true)]
    [InlineData("../metadata", false)]
    [InlineData("https://internal.invalid/file", false)]
    [InlineData("/absolute/path", false)]
    [InlineData("photos/file.jpg?redirect=http://127.0.0.1", false)]
    public void TelegramFilePathsCannotEscapeTheFixedProviderOrigin(string path, bool expected)
    {
        Assert.Equal(expected, TelegramMediaSource.IsSafeProviderPath(path));
    }

    [Theory]
    [InlineData("https://lookaside.example/media/1", true)]
    [InlineData("http://lookaside.example/media/1", false)]
    [InlineData("https://127.0.0.1/media/1", false)]
    [InlineData("https://lookaside.example:8443/media/1", false)]
    [InlineData("https://lookaside.example@internal.invalid/media/1", false)]
    public void WhatsAppMediaUrlsRequireExactAllowlistedHttpsOrigin(string value, bool expected)
    {
        var valid = SafeRemoteMediaPolicy.TryValidateUri(
            value,
            ["lookaside.example"],
            out var uri);
        if (valid && uri is not null)
        {
            valid = !IPAddress.TryParse(uri.Host, out var address)
                    || SafeRemoteMediaPolicy.IsPublicAddress(address);
        }

        Assert.Equal(expected, valid);
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("192.168.1.5", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("fc00::1", false)]
    public void ResolvedPrivateAndSpecialAddressesAreRejected(string value, bool expected)
    {
        Assert.Equal(expected, SafeRemoteMediaPolicy.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Fact]
    public async Task OggSanitizerRemovesReporterMetadataWithoutChangingTheSourceBuffer()
    {
        var source = CreateOggOpusFixture("DEVICE=sensitive-comment");
        var original = source.ToArray();

        using var sanitized = await new OggOpusAudioSanitizer().SanitizeAsync(source, "audio/ogg");

        Assert.Equal(original, source);
        Assert.DoesNotContain(
            "sensitive-comment",
            Encoding.UTF8.GetString(sanitized.Bytes.Span),
            StringComparison.Ordinal);
        Assert.Equal("audio/ogg", sanitized.ContentType);
    }

    [Fact]
    public async Task RedactorReencodesToMetadataFreeJpegAndExpandsFaceBounds()
    {
        using var bitmap = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Orange);
            using var paint = new SKPaint();
            for (var y = 25; y < 75; y += 5)
            {
                for (var x = 25; x < 75; x += 5)
                {
                    paint.Color = ((x + y) / 5) % 2 == 0 ? SKColors.White : SKColors.Navy;
                    canvas.DrawRect(new SKRect(x, y, x + 5, y + 5), paint);
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var source = png.ToArray();
        var redactor = new OnnxFaceRedactor(new StubFaceDetector(
            new FaceBounds(40, 40, 20, 20, 0.95f)));

        var result = await redactor.RedactAsync(source);

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(1, result.FaceCount);
        Assert.Equal("fixture-detector", result.ModelVersion);
        Assert.False(source.SequenceEqual(result.Bytes.ToArray()));
        using var derivative = SKBitmap.Decode(result.Bytes.ToArray());
        Assert.NotNull(derivative);
        Assert.True(
            MeanHorizontalContrast(derivative, 30, 70, 30, 70)
            < MeanHorizontalContrast(bitmap, 30, 70, 30, 70) * 0.5);
        var expanded = OnnxFaceRedactor.ExpandAndClamp(
            new FaceBounds(40, 40, 20, 20, 0.95f),
            100,
            100);
        Assert.True(expanded.Left < 40 && expanded.Top < 40);
        Assert.True(expanded.Right > 60 && expanded.Bottom > 60);
    }

    [Fact]
    public async Task RawImageBufferIsClearedAndOnlyDerivativeReachesStorage()
    {
        byte[] raw = [0xFF, 0xD8, 0xFF, 11, 12, 13, 14, 15];
        byte[] derivative = [0xFF, 0xD8, 0xFF, 90, 91];
        var source = new RecordingSource(raw, "image/jpeg");
        var store = new RecordingStore();
        var redactor = new StubRedactor(derivative, faceCount: 1);
        var pipeline = new MediaPrivacyPipeline(
            new ProviderMediaDownloader([source]),
            redactor,
            new RejectingAudioSanitizer(),
            store);

        await pipeline.ProcessAsync(
            Guid.NewGuid(),
            IntakeChannel.Telegram,
            "opaque-provider-handle",
            IntakeMediaKind.Image,
            DateTimeOffset.UtcNow.AddDays(30));

        Assert.Equal(derivative, store.StoredBytes);
        Assert.False(raw.SequenceEqual(store.StoredBytes!));
        Assert.All(redactor.ObservedRawBuffer!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task DetectorUncertaintyFailsClosedBeforeStorageAndClearsRawBuffer()
    {
        byte[] raw = [0xFF, 0xD8, 0xFF, 21, 22, 23];
        var source = new RecordingSource(raw, "image/jpeg");
        var store = new RecordingStore();
        var redactor = new StubRedactor([0xFF, 0xD8, 0xFF, 44], faceCount: 0);
        var pipeline = new MediaPrivacyPipeline(
            new ProviderMediaDownloader([source]),
            redactor,
            new RejectingAudioSanitizer(),
            store);

        var error = await Assert.ThrowsAsync<MediaPrivacyException>(() => pipeline.ProcessAsync(
            Guid.NewGuid(),
            IntakeChannel.Telegram,
            "opaque-provider-handle",
            IntakeMediaKind.Image,
            DateTimeOffset.UtcNow.AddDays(30)));

        Assert.Equal(MediaFailureCode.RedactionUncertain, error.FailureCode);
        Assert.Null(store.StoredBytes);
        Assert.All(redactor.ObservedRawBuffer!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task MimeConfusionIsRejectedBeforeRedactionOrStorage()
    {
        byte[] disguisedPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var source = new RecordingSource(disguisedPng, "image/jpeg");
        var redactor = new CountingRedactor();
        var store = new RecordingStore();
        var pipeline = new MediaPrivacyPipeline(
            new ProviderMediaDownloader([source]),
            redactor,
            new RejectingAudioSanitizer(),
            store);

        var error = await Assert.ThrowsAsync<MediaPrivacyException>(() => pipeline.ProcessAsync(
            Guid.NewGuid(),
            IntakeChannel.Telegram,
            "opaque-provider-handle",
            IntakeMediaKind.Image,
            DateTimeOffset.UtcNow.AddDays(30)));

        Assert.Equal(MediaFailureCode.ContentTypeMismatch, error.FailureCode);
        Assert.Equal(0, redactor.CallCount);
        Assert.Null(store.StoredBytes);
    }

    private sealed class RecordingSource(byte[] bytes, string contentType) : IProviderMediaSource
    {
        public IntakeChannel Channel => IntakeChannel.Telegram;

        public byte[]? LastBuffer { get; private set; }

        public Task<ProviderMediaContent> OpenAsync(
            string providerMediaHandle,
            IntakeMediaKind kind,
            CancellationToken cancellationToken = default)
        {
            LastBuffer = bytes.ToArray();
            return Task.FromResult(new ProviderMediaContent(
                new MemoryStream(LastBuffer, writable: false),
                contentType,
                LastBuffer.Length));
        }
    }

    private static byte[] CreateOggOpusFixture(string comment)
    {
        byte[] head = [.. "OpusHead"u8, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var vendor = "test-encoder"u8.ToArray();
        var commentBytes = Encoding.UTF8.GetBytes(comment);
        var tags = new byte[8 + 4 + vendor.Length + 4 + 4 + commentBytes.Length];
        "OpusTags"u8.CopyTo(tags);
        BinaryPrimitives.WriteUInt32LittleEndian(tags.AsSpan(8, 4), (uint)vendor.Length);
        vendor.CopyTo(tags, 12);
        var countOffset = 12 + vendor.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(tags.AsSpan(countOffset, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(tags.AsSpan(countOffset + 4, 4), (uint)commentBytes.Length);
        commentBytes.CopyTo(tags, countOffset + 8);

        var page = new byte[27 + 2 + head.Length + tags.Length];
        "OggS"u8.CopyTo(page);
        page[4] = 0;
        page[5] = 2;
        page[26] = 2;
        page[27] = (byte)head.Length;
        page[28] = (byte)tags.Length;
        head.CopyTo(page, 29);
        tags.CopyTo(page, 29 + head.Length);
        return page;
    }

    private static double MeanHorizontalContrast(
        SKBitmap image,
        int left,
        int right,
        int top,
        int bottom)
    {
        long total = 0;
        var comparisons = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right - 1; x++)
            {
                var current = image.GetPixel(x, y);
                var next = image.GetPixel(x + 1, y);
                total += Math.Abs(current.Red - next.Red)
                         + Math.Abs(current.Green - next.Green)
                         + Math.Abs(current.Blue - next.Blue);
                comparisons++;
            }
        }

        return total / (double)comparisons;
    }

    private sealed class StubRedactor(byte[] derivative, int faceCount) : IImageRedactor
    {
        public byte[]? ObservedRawBuffer { get; private set; }

        public Task<RedactedImage> RedactAsync(
            ReadOnlyMemory<byte> rawImage,
            CancellationToken cancellationToken = default)
        {
            Assert.True(MemoryMarshal.TryGetArray(rawImage, out var segment));
            ObservedRawBuffer = segment.Array;
            return Task.FromResult(new RedactedImage(
                derivative,
                "image/jpeg",
                100,
                100,
                faceCount,
                "test-model"));
        }
    }

    private sealed class StubFaceDetector(params FaceBounds[] faces) : IFaceDetector
    {
        public string ModelVersion => "fixture-detector";

        public IReadOnlyList<FaceBounds> Detect(SKBitmap image) => faces;
    }

    private sealed class CountingRedactor : IImageRedactor
    {
        public int CallCount { get; private set; }

        public Task<RedactedImage> RedactAsync(
            ReadOnlyMemory<byte> rawImage,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("This redactor must not be called.");
        }
    }

    private sealed class RejectingAudioSanitizer : IAudioSanitizer
    {
        public Task<SanitizedAudio> SanitizeAsync(
            ReadOnlyMemory<byte> rawAudio,
            string sourceContentType,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This sanitizer must not be called.");
    }

    private sealed class RecordingStore : ISafeMediaStore
    {
        public byte[]? StoredBytes { get; private set; }

        public Task<SafeMediaObject> StoreAsync(
            Guid assetId,
            IntakeMediaKind kind,
            ReadOnlyMemory<byte> safeDerivative,
            string contentType,
            DateTimeOffset expiresAtUtc,
            CancellationToken cancellationToken = default)
        {
            StoredBytes = safeDerivative.ToArray();
            return Task.FromResult(new SafeMediaObject(
                MediaPrivacyPolicy.CreateOpaqueObjectKey(assetId, kind),
                contentType,
                StoredBytes.LongLength,
                "test-v1",
                expiresAtUtc));
        }

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
