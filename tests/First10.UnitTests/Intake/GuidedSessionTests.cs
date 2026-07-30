using First10.Modules.Intake;

namespace First10.UnitTests.Intake;

public sealed class GuidedSessionTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(IntakeContentKind.Photo, IntakeContentKind.Voice)]
    [InlineData(IntakeContentKind.Voice, IntakeContentKind.Photo)]
    public void PhotoAndVoiceCanArriveInEitherOrder(
        IntakeContentKind firstKind,
        IntakeContentKind secondKind)
    {
        var session = GuidedIntakeSession.Open(
            Guid.NewGuid(),
            Envelope("message-1", firstKind, StartedAt),
            TimeSpan.FromMinutes(2));

        Assert.Contains(
            firstKind == IntakeContentKind.Photo ? IntakePrompt.RequestVoice : IntakePrompt.RequestPhoto,
            session.PendingPrompts);
        Assert.Contains(IntakePrompt.RequestLocation, session.PendingPrompts);

        Assert.True(session.TryAttach(Envelope("message-2", secondKind, StartedAt.AddSeconds(5))));
        Assert.True(session.TryAttach(Envelope(
            "message-3",
            IntakeContentKind.Location,
            StartedAt.AddSeconds(9),
            new IntakeLocation(6.6018, 3.3515))));

        Assert.Equal(GuidedSessionStatus.ReadyForPrivacyProcessing, session.Status);
        Assert.Equal(
            ["message-1", "message-2", "message-3"],
            session.Inputs.OrderBy(x => x.OccurredAtUtc).Select(x => x.ProviderMessageId));
    }

    [Fact]
    public void DuplicateDeliveryDoesNotCreateAnotherInputOrPrompt()
    {
        var first = Envelope("message-1", IntakeContentKind.Photo, StartedAt);
        var session = GuidedIntakeSession.Open(Guid.NewGuid(), first, TimeSpan.FromMinutes(2));
        var prompts = session.PendingPrompts.ToArray();

        Assert.False(session.TryAttach(first));
        Assert.Single(session.Inputs);
        Assert.Equal(prompts, session.PendingPrompts);
    }

    [Fact]
    public void LocationRequestHasOneReminderThenExpiresVisibly()
    {
        var session = GuidedIntakeSession.Open(
            Guid.NewGuid(),
            Envelope("message-1", IntakeContentKind.Voice, StartedAt),
            TimeSpan.FromMinutes(2));

        Assert.False(session.TryScheduleLocationReminder(StartedAt.AddSeconds(29)));
        Assert.True(session.TryScheduleLocationReminder(StartedAt.AddSeconds(30)));
        Assert.False(session.TryScheduleLocationReminder(StartedAt.AddSeconds(31)));

        Assert.True(session.TryExpire(StartedAt.AddMinutes(2)));
        Assert.Equal(GuidedSessionStatus.ManualReview, session.Status);
        Assert.Contains(IntakeGap.Location, session.VisibleGaps);
        Assert.Contains(IntakeGap.Photo, session.VisibleGaps);
    }

    [Fact]
    public void MostRecentOpenSessionWinsWithoutCrossAttachingExpiredWork()
    {
        var older = GuidedIntakeSession.Open(
            Guid.NewGuid(),
            Envelope("older", IntakeContentKind.Photo, StartedAt),
            TimeSpan.FromMinutes(2));
        var newer = GuidedIntakeSession.Open(
            Guid.NewGuid(),
            Envelope("newer", IntakeContentKind.Voice, StartedAt.AddSeconds(30)),
            TimeSpan.FromMinutes(2));
        var location = Envelope(
            "pin",
            IntakeContentKind.Location,
            StartedAt.AddMinutes(1),
            new IntakeLocation(6.6018, 3.3515));

        Assert.Same(newer, GuidedSessionSelector.SelectMostRecentOpen([older, newer], location));
        Assert.Null(GuidedSessionSelector.SelectMostRecentOpen(
            [older],
            location with { OccurredAtUtc = StartedAt.AddMinutes(3) }));
    }

    private static InboundChannelEnvelope Envelope(
        string providerMessageId,
        IntakeContentKind kind,
        DateTimeOffset occurredAtUtc,
        IntakeLocation? location = null) => new()
        {
            SchemaVersion = 1,
            Channel = IntakeChannel.Telegram,
            ProviderMessageId = providerMessageId,
            ReporterKey = "pseudonymous-reporter-key",
            ContactReference = Guid.Parse("53ba1f69-8105-4666-a152-6dfe4241e661"),
            ContentKind = kind,
            ProviderMediaHandle = kind is IntakeContentKind.Photo or IntakeContentKind.Voice
                ? $"handle-{providerMessageId}"
                : null,
            OccurredAtUtc = occurredAtUtc,
            Location = location,
            CorrelationKey = "conversation-1"
        };
}
