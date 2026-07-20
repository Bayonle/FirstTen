using System.Security.Cryptography;
using First10.Modules.Intake.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace First10.Infrastructure.Modules.Intake.Media;

public readonly record struct FaceBounds(float X, float Y, float Width, float Height, float Score);

public interface IFaceDetector
{
    string ModelVersion { get; }

    IReadOnlyList<FaceBounds> Detect(SKBitmap image);
}

public sealed class OnnxFaceRedactor(IFaceDetector detector) : IImageRedactor
{
    private const float BoundsMargin = 0.45f;

    public Task<RedactedImage> RedactAsync(
        ReadOnlyMemory<byte> rawImage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var data = SKData.CreateCopy(rawImage.Span);
        using var codec = SKCodec.Create(data)
            ?? throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);
        if (!MediaPrivacyPolicy.IsSafeDecodedImageSize(codec.Info.Width, codec.Info.Height))
        {
            throw new MediaPrivacyException(MediaFailureCode.ResourceLimitExceeded);
        }

        var outputInfo = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque);
        using var decoded = SKBitmap.Decode(data)
            ?? throw new MediaPrivacyException(MediaFailureCode.DecodeRejected);

        cancellationToken.ThrowIfCancellationRequested();
        var faces = detector.Detect(decoded);
        if (faces.Count == 0)
        {
            throw new MediaPrivacyException(MediaFailureCode.RedactionUncertain);
        }

        using var output = new SKBitmap(outputInfo);
        using (var canvas = new SKCanvas(output))
        {
            var fullImage = new SKRect(0, 0, decoded.Width, decoded.Height);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
            canvas.DrawBitmap(decoded, fullImage, sampling);
            foreach (var face in faces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bounds = ExpandAndClamp(face, decoded.Width, decoded.Height);
                if (bounds.Width < 2 || bounds.Height < 2)
                {
                    throw new MediaPrivacyException(MediaFailureCode.RedactionUncertain);
                }

                canvas.Save();
                canvas.ClipRect(bounds);
                using var paint = new SKPaint
                {
                    ImageFilter = SKImageFilter.CreateBlur(
                        Math.Max(12, bounds.Width * 0.16f),
                        Math.Max(12, bounds.Height * 0.16f),
                        SKShaderTileMode.Clamp),
                    IsAntialias = true
                };
                canvas.DrawBitmap(decoded, fullImage, sampling, paint);
                canvas.Restore();
            }
        }

        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85)
            ?? throw new MediaPrivacyException(MediaFailureCode.RedactionFailed);
        var derivative = encoded.ToArray();
        return Task.FromResult(new RedactedImage(
            derivative,
            "image/jpeg",
            output.Width,
            output.Height,
            faces.Count,
            detector.ModelVersion));
    }

    public static SKRect ExpandAndClamp(FaceBounds face, int imageWidth, int imageHeight)
    {
        var marginX = face.Width * BoundsMargin;
        var marginY = face.Height * BoundsMargin;
        return new SKRect(
            Math.Clamp(face.X - marginX, 0, imageWidth),
            Math.Clamp(face.Y - marginY, 0, imageHeight),
            Math.Clamp(face.X + face.Width + marginX, 0, imageWidth),
            Math.Clamp(face.Y + face.Height + marginY, 0, imageHeight));
    }
}

public sealed class OnnxYuNetFaceDetector : IFaceDetector, IDisposable
{
    public const string Version = "opencv-yunet-2026may";
    public const string Licence = "MIT";
    public const string ExpectedSha256 = "ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0";
    private const float ConfidenceThreshold = 0.55f;
    private const float NmsThreshold = 0.30f;
    private const int MaximumInferenceDimension = 2_048;
    private readonly object _sessionLock = new();
    private readonly string? _modelPath;
    private InferenceSession? _session;

    public OnnxYuNetFaceDetector(IConfiguration configuration)
    {
        _modelPath = configuration["Models:FaceRedaction:Path"];
    }

    public string ModelVersion => Version;

    public IReadOnlyList<FaceBounds> Detect(SKBitmap image)
    {
        var session = GetSession();
        var scale = Math.Min(
            1f,
            Math.Min(
                MaximumInferenceDimension / (float)image.Width,
                MaximumInferenceDimension / (float)image.Height));
        var inferenceWidth = Math.Max(1, (int)MathF.Round(image.Width * scale));
        var inferenceHeight = Math.Max(1, (int)MathF.Round(image.Height * scale));
        var paddedWidth = RoundUp(inferenceWidth, 32);
        var paddedHeight = RoundUp(inferenceHeight, 32);
        using var resized = Resize(image, inferenceWidth, inferenceHeight);
        var inputData = GC.AllocateUninitializedArray<float>(3 * paddedWidth * paddedHeight);
        try
        {
            FillNchwBgr(resized, inputData, paddedWidth, paddedHeight);
            using var input = OrtValue.CreateTensorValueFromMemory(
                inputData,
                [1, 3, paddedHeight, paddedWidth]);
            var inputs = new Dictionary<string, OrtValue> { [session.InputNames[0]] = input };
            string[] outputNames =
            [
                "cls_8", "cls_16", "cls_32",
                "obj_8", "obj_16", "obj_32",
                "bbox_8", "bbox_16", "bbox_32",
                "kps_8", "kps_16", "kps_32"
            ];
            using var results = session.Run(new RunOptions(), inputs, outputNames);
            var candidates = Decode(
                results,
                paddedWidth,
                paddedHeight,
                inferenceWidth,
                inferenceHeight,
                scale);
            return NonMaximumSuppression(candidates);
        }
        finally
        {
            Array.Clear(inputData);
        }
    }

    public void Dispose()
    {
        lock (_sessionLock)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    private InferenceSession GetSession()
    {
        if (_session is not null)
        {
            return _session;
        }

        lock (_sessionLock)
        {
            if (_session is not null)
            {
                return _session;
            }

            if (string.IsNullOrWhiteSpace(_modelPath) || !File.Exists(_modelPath))
            {
                throw new MediaPrivacyException(MediaFailureCode.RedactionFailed);
            }

            try
            {
                using var model = File.OpenRead(_modelPath);
                var checksum = Convert.ToHexStringLower(SHA256.HashData(model));
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(checksum),
                        Convert.FromHexString(ExpectedSha256)))
                {
                    throw new MediaPrivacyException(MediaFailureCode.RedactionFailed);
                }

                _session = new InferenceSession(_modelPath, new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    IntraOpNumThreads = 1,
                    InterOpNumThreads = 1,
                    LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
                });
                return _session;
            }
            catch (MediaPrivacyException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new MediaPrivacyException(MediaFailureCode.RedactionFailed);
            }
        }
    }

    private static SKBitmap Resize(SKBitmap image, int width, int height)
    {
        if (image.Width == width && image.Height == height)
        {
            return image.Copy();
        }

        var resized = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        if (!image.ScalePixels(resized, new SKSamplingOptions(SKCubicResampler.Mitchell)))
        {
            resized.Dispose();
            throw new MediaPrivacyException(MediaFailureCode.ResourceLimitExceeded);
        }

        return resized;
    }

    private static void FillNchwBgr(SKBitmap image, Span<float> target, int width, int height)
    {
        var plane = width * height;
        var pixels = image.GetPixelSpan();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var index = y * width + x;
                var pixelOffset = y * image.RowBytes + x * 4;
                target[index] = pixels[pixelOffset];
                target[plane + index] = pixels[pixelOffset + 1];
                target[2 * plane + index] = pixels[pixelOffset + 2];
            }
        }
    }

    private static List<FaceBounds> Decode(
        IDisposableReadOnlyCollection<OrtValue> outputs,
        int paddedWidth,
        int paddedHeight,
        int inferenceWidth,
        int inferenceHeight,
        float scale)
    {
        int[] strides = [8, 16, 32];
        var faces = new List<FaceBounds>();
        for (var strideIndex = 0; strideIndex < strides.Length; strideIndex++)
        {
            var stride = strides[strideIndex];
            var columns = paddedWidth / stride;
            var rows = paddedHeight / stride;
            var classification = outputs[strideIndex].GetTensorDataAsSpan<float>();
            var objectness = outputs[strideIndex + 3].GetTensorDataAsSpan<float>();
            var boxes = outputs[strideIndex + 6].GetTensorDataAsSpan<float>();
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var index = row * columns + column;
                    var score = MathF.Sqrt(
                        Math.Clamp(classification[index], 0, 1)
                        * Math.Clamp(objectness[index], 0, 1));
                    if (score < ConfidenceThreshold)
                    {
                        continue;
                    }

                    var centerX = (column + boxes[index * 4]) * stride;
                    var centerY = (row + boxes[index * 4 + 1]) * stride;
                    var width = MathF.Exp(boxes[index * 4 + 2]) * stride;
                    var height = MathF.Exp(boxes[index * 4 + 3]) * stride;
                    var left = centerX - width / 2;
                    var top = centerY - height / 2;
                    var right = centerX + width / 2;
                    var bottom = centerY + height / 2;
                    if (right <= 0 || bottom <= 0 || left >= inferenceWidth || top >= inferenceHeight)
                    {
                        continue;
                    }

                    left = Math.Clamp(left, 0, inferenceWidth);
                    top = Math.Clamp(top, 0, inferenceHeight);
                    right = Math.Clamp(right, 0, inferenceWidth);
                    bottom = Math.Clamp(bottom, 0, inferenceHeight);
                    faces.Add(new FaceBounds(
                        left / scale,
                        top / scale,
                        (right - left) / scale,
                        (bottom - top) / scale,
                        score));
                }
            }
        }

        return faces;
    }

    private static List<FaceBounds> NonMaximumSuppression(List<FaceBounds> candidates)
    {
        var kept = new List<FaceBounds>();
        foreach (var candidate in candidates.OrderByDescending(x => x.Score).Take(5_000))
        {
            if (kept.All(existing => IntersectionOverUnion(candidate, existing) < NmsThreshold))
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    private static float IntersectionOverUnion(FaceBounds left, FaceBounds right)
    {
        var intersectionWidth = Math.Max(
            0,
            Math.Min(left.X + left.Width, right.X + right.Width) - Math.Max(left.X, right.X));
        var intersectionHeight = Math.Max(
            0,
            Math.Min(left.Y + left.Height, right.Y + right.Height) - Math.Max(left.Y, right.Y));
        var intersection = intersectionWidth * intersectionHeight;
        var union = left.Width * left.Height + right.Width * right.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static int RoundUp(int value, int divisor) => ((value + divisor - 1) / divisor) * divisor;
}
