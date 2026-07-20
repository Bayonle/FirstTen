using First10.Modules.Intake.Media;

namespace First10.UnitTests.Intake;

public sealed class MediaPolicyTests
{
    [Theory]
    [InlineData(IntakeMediaKind.Image, "image/jpeg")]
    [InlineData(IntakeMediaKind.Image, "image/png")]
    [InlineData(IntakeMediaKind.Image, "image/webp")]
    [InlineData(IntakeMediaKind.Audio, "audio/ogg")]
    [InlineData(IntakeMediaKind.Audio, "audio/mpeg")]
    public void DeclaredMediaTypesAreExplicitlyAllowlisted(IntakeMediaKind kind, string contentType)
    {
        Assert.True(MediaPrivacyPolicy.IsAllowedDeclaredContentType(kind, contentType));
        Assert.False(MediaPrivacyPolicy.IsAllowedDeclaredContentType(kind, "application/octet-stream"));
        Assert.False(MediaPrivacyPolicy.IsAllowedDeclaredContentType(kind, null));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(8000, 4000, true)]
    [InlineData(8193, 100, false)]
    [InlineData(8000, 8000, false)]
    [InlineData(0, 100, false)]
    public void DecodedImageLimitsBlockDimensionAndPixelBombs(int width, int height, bool expected)
    {
        Assert.Equal(expected, MediaPrivacyPolicy.IsSafeDecodedImageSize(width, height));
    }

    [Fact]
    public void SafeObjectKeyContainsNoProviderOrReporterIdentity()
    {
        var assetId = Guid.Parse("50df69de-2178-49c5-b927-a8a043be86bc");

        var key = MediaPrivacyPolicy.CreateOpaqueObjectKey(assetId, IntakeMediaKind.Image);

        Assert.Equal("safe/images/50df69de217849c5b927a8a043be86bc", key);
        Assert.DoesNotContain("telegram", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reporter", key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrivacyFailuresExposeOnlyClosedReasonCodes()
    {
        var exception = new MediaPrivacyException(MediaFailureCode.RedactionUncertain);

        Assert.Equal(MediaFailureCode.RedactionUncertain, exception.FailureCode);
        Assert.Equal("RedactionUncertain", exception.Message);
    }

    [Fact]
    public void MediaAssetStateIsIdempotentAndRetentionCannotDeleteEarly()
    {
        var now = DateTimeOffset.UtcNow;
        var asset = IntakeMediaAsset.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            IntakeMediaKind.Image,
            now);

        Assert.True(asset.TryMarkStored(
            MediaPrivacyPolicy.CreateOpaqueObjectKey(asset.Id, IntakeMediaKind.Image),
            "image/jpeg",
            512,
            "test-v1",
            "test-redactor-v1",
            now.AddSeconds(1),
            now.AddDays(30)));
        Assert.False(asset.TryMarkStored(
            "safe/images/replay",
            "image/jpeg",
            512,
            "test-v1",
            "test-redactor-v1",
            now.AddSeconds(2),
            now.AddDays(30)));
        Assert.False(asset.TryMarkDeleted(now.AddDays(29)));
        Assert.True(asset.TryMarkDeleted(now.AddDays(30)));
        Assert.Equal(MediaProcessingStatus.Deleted, asset.Status);
        Assert.Null(asset.SafeObjectKey);
    }
}
