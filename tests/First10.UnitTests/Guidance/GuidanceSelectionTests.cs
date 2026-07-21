using First10.Modules.Guidance;
using First10.Modules.BuildingBlocks.Contracts;

namespace First10.UnitTests.Guidance;

public sealed class GuidanceSelectionTests
{
    [Fact]
    public void CompleteThreeLanguageBundleIsAtomicallyApprovedAndExactAssetIsSelected()
    {
        var set = CreateSet();

        Assert.Null(set.SelectAsset(GuidanceLanguage.Yoruba));
        Assert.False(set.TryApproveAndEnable(false, "dispatcher", DateTimeOffset.UtcNow));
        Assert.True(set.TryApproveAndEnable(true, "clinician-1", DateTimeOffset.UtcNow));
        Assert.Equal("Yoruba exact", set.SelectAsset(GuidanceLanguage.Yoruba)!.ExactText);
    }

    [Fact]
    public void InvalidProposalFallsBackOnlyToEnabledConservativePolicy()
    {
        var set = CreateSet();
        set.TryApproveAndEnable(true, "clinician-1", DateTimeOffset.UtcNow);

        var selected = GuidanceTemplateSelector.Select(
            [set], GuidancePurpose.InitialSafety, "initial", GuidanceCategory.OkadaCollision,
            GuidanceSeverityBand.Critical, Guid.NewGuid());

        Assert.Same(set, selected);
    }

    [Fact]
    public void UnknownProviderOutcomeIsTerminalForAutomaticRetry()
    {
        var set = CreateSet();
        set.TryApproveAndEnable(true, "clinician-1", DateTimeOffset.UtcNow);
        var intent = GuidanceIntent.Create(
            SemanticMessageIdentity.Create("test", "one"),
            GuidancePurpose.InitialSafety, "initial", null, Guid.NewGuid(), Guid.NewGuid(),
            GuidanceChannel.Telegram, GuidanceLanguage.English, set,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30));

        Assert.True(intent.TryMarkUnknown("accepted_before_ack", DateTimeOffset.UtcNow));
        Assert.False(intent.CanAttemptDelivery);
        Assert.False(intent.TryMarkAccepted("provider-id", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void StartedDeliveryAttemptCanBeClosedAsUnknownWithoutResending()
    {
        var started = DateTimeOffset.UtcNow;
        var attempt = GuidanceDeliveryAttempt.Start(
            Guid.NewGuid(), GuidanceDeliveryComponent.Text, 1, started);

        Assert.True(attempt.TryComplete(
            GuidanceDeliveryAttemptStatus.Unknown,
            started.AddSeconds(5),
            null,
            "provider_outcome_unknown_after_interruption"));
        Assert.Equal(GuidanceDeliveryAttemptStatus.Unknown, attempt.Status);
        Assert.False(attempt.TryComplete(
            GuidanceDeliveryAttemptStatus.Accepted,
            started.AddSeconds(6),
            "would-be-duplicate",
            null));
    }

    private static GuidanceTemplateSet CreateSet()
    {
        var drafts = new[]
        {
            Draft(GuidanceLanguage.English, "English exact", "en.ogg"),
            Draft(GuidanceLanguage.NigerianPidgin, "Pidgin exact", "pcm.ogg"),
            Draft(GuidanceLanguage.Yoruba, "Yoruba exact", "yo.ogg")
        };
        return GuidanceTemplateSet.CreateDraft(
            Guid.NewGuid(), "initial-conservative", GuidancePurpose.InitialSafety,
            GuidanceCategory.RoadTrafficCollision, GuidanceSeverityBand.Conservative,
            "2026.07.example", "initial", "non-fire road report", true, drafts);
    }

    private static GuidanceLocaleDraft Draft(GuidanceLanguage language, string text, string key) =>
        new(language, text, GuidanceTemplateAsset.ComputeTextSha256(text), key,
            new string('a', 64), "gpt-4o-mini-tts", "alloy", "{}");
}
