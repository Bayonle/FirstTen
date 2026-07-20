using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.Guidance;

namespace First10.ContractTests.Channels;

public sealed class StatusDeliveryParityTests
{
    [Fact]
    public void TelegramAndWhatsAppPinTheSameApprovedTextAndVoiceAsset()
    {
        const string exactText = "Approved status update";
        var drafts = Enum.GetValues<GuidanceLanguage>().Select(language => new GuidanceLocaleDraft(
            language,
            exactText,
            GuidanceTemplateAsset.ComputeTextSha256(exactText),
            $"status/{language}.ogg",
            new string('b', 64),
            "gpt-4o-mini-tts",
            "alloy",
            "{}"));
        var template = GuidanceTemplateSet.CreateDraft(
            Guid.NewGuid(), "arrived-default", GuidancePurpose.ResponseStatus,
            GuidanceCategory.None, GuidanceSeverityBand.Conservative, "example",
            "arrived", "committed arrived transition", true, drafts);
        template.TryApproveAndEnable(true, "clinical", DateTimeOffset.UtcNow);

        var telegram = Create(GuidanceChannel.Telegram, template);
        var whatsapp = Create(GuidanceChannel.WhatsApp, template);

        Assert.Equal(telegram.ExactText, whatsapp.ExactText);
        Assert.Equal(telegram.TextSha256, whatsapp.TextSha256);
        Assert.Equal(telegram.VoiceSha256, whatsapp.VoiceSha256);
    }

    private static GuidanceIntent Create(GuidanceChannel channel, GuidanceTemplateSet template) =>
        GuidanceIntent.Create(
            SemanticMessageIdentity.Create("parity", channel.ToString()),
            GuidancePurpose.ResponseStatus,
            "arrived",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            channel,
            GuidanceLanguage.English,
            template,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1));
}
