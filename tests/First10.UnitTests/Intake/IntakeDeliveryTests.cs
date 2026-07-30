using First10.Modules.Intake;

namespace First10.UnitTests.Intake;

public sealed class IntakeDeliveryTests
{
    [Fact]
    public void ProviderAcceptanceIsNotHumanDelivery()
    {
        var intent = CreateIntent();

        Assert.True(intent.TryMarkProviderAccepted("provider-message-1", DateTimeOffset.UtcNow));

        Assert.Equal(ChannelDeliveryStatus.Accepted, intent.DeliveryStatus);
        Assert.NotEqual(ChannelDeliveryStatus.Delivered, intent.DeliveryStatus);
        Assert.Equal("provider-message-1", intent.ProviderMessageId);
        Assert.False(intent.TryMarkProviderAccepted("provider-message-2", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DeliveryReceiptCanAdvanceAcceptedMessageAndReplayIsIdempotent()
    {
        var intent = CreateIntent();
        intent.TryMarkProviderAccepted("provider-message-1", DateTimeOffset.UtcNow);

        Assert.True(intent.TryApplyReceipt(ChannelDeliveryStatus.Delivered, DateTimeOffset.UtcNow));
        Assert.False(intent.TryApplyReceipt(ChannelDeliveryStatus.Delivered, DateTimeOffset.UtcNow));
        Assert.Equal(ChannelDeliveryStatus.Delivered, intent.DeliveryStatus);
    }

    [Fact]
    public void FailureRemainsVisibleAndCanBeRetried()
    {
        var intent = CreateIntent();

        Assert.True(intent.TryMarkFailed("provider_unavailable", DateTimeOffset.UtcNow));
        Assert.Equal(ChannelDeliveryStatus.Failed, intent.DeliveryStatus);
        Assert.Equal(1, intent.AttemptCount);
        Assert.True(intent.CanAttemptDelivery);
    }

    [Fact]
    public void AmbiguousNetworkFailureBecomesUnknownAndDoesNotAutoRetry()
    {
        var intent = CreateIntent();

        Assert.True(intent.TryMarkUnknown("network_outcome_unknown", DateTimeOffset.UtcNow));

        Assert.Equal(ChannelDeliveryStatus.Unknown, intent.DeliveryStatus);
        Assert.False(intent.CanAttemptDelivery);
    }

    [Theory]
    [InlineData(IntakeLanguage.English)]
    [InlineData(IntakeLanguage.NigerianPidgin)]
    [InlineData(IntakeLanguage.Yoruba)]
    public void EveryPilotLanguageHasEveryIntakePrompt(IntakeLanguage language)
    {
        foreach (var prompt in Enum.GetValues<IntakePrompt>())
        {
            Assert.False(string.IsNullOrWhiteSpace(IntakeMessageCatalogue.Get(language, prompt)));
        }
    }

    private static IntakePromptIntent CreateIntent()
    {
        var session = GuidedIntakeSession.Open(
            Guid.NewGuid(),
            new InboundChannelEnvelope
            {
                SchemaVersion = 1,
                Channel = IntakeChannel.Telegram,
                ProviderMessageId = "input-message",
                ReporterKey = "reporter-key",
                ContactReference = Guid.NewGuid(),
                ContentKind = IntakeContentKind.Voice,
                ProviderMediaHandle = "voice-handle",
                OccurredAtUtc = DateTimeOffset.UtcNow,
                CorrelationKey = "reporter-key"
            },
            TimeSpan.FromMinutes(2));
        return IntakePromptIntent.Create(
            session,
            IntakePrompt.Acknowledgement,
            IntakeLanguage.English,
            DateTimeOffset.UtcNow);
    }
}
