using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Modules.Intake.Channels.Telegram;
using First10.Infrastructure.Modules.Intake.Channels.WhatsApp;
using First10.Modules.Intake;

namespace First10.ContractTests.Channels;

public sealed class ChannelParityTests
{
    private static readonly Guid ContactReference = Guid.Parse("c73bf559-532f-4d05-a75e-0417cec20609");
    private static readonly string[] StageFiles = ["01-voice.json", "02-photo.json", "03-location.json"];

    [Fact]
    public async Task EquivalentRecordedFixturesProduceEquivalentSessionBehavior()
    {
        var identity = new FixtureContactIdentityResolver();
        var mapper = new ChannelEnvelopeMapper(identity);
        var telegram = LoadStage(new TelegramInboundAdapter(), "telegram");
        var whatsapp = LoadStage(new WhatsAppInboundAdapter(), "whatsapp");

        var telegramEnvelopes = await MapAsync(mapper, telegram);
        var whatsappEnvelopes = await MapAsync(mapper, whatsapp);

        Assert.Equal(
            telegramEnvelopes.Select(SemanticShape),
            whatsappEnvelopes.Select(SemanticShape));

        var telegramSession = Replay(telegramEnvelopes);
        var whatsappSession = Replay(whatsappEnvelopes);
        Assert.Equal(telegramSession.InitialPrompts, whatsappSession.InitialPrompts);
        Assert.Equal(telegramSession.Session.Status, whatsappSession.Session.Status);
        Assert.Equal(telegramSession.Session.VisibleGaps, whatsappSession.Session.VisibleGaps);
        Assert.Equal(
            telegramSession.Session.Inputs.Select(x => (x.ContentKind, x.OccurredAtUtc)),
            whatsappSession.Session.Inputs.Select(x => (x.ContentKind, x.OccurredAtUtc)));
    }

    [Fact]
    public async Task MapperReplacesProviderAddressWithOpaqueAndPseudonymousIdentity()
    {
        var providerMessage = LoadStage(new TelegramInboundAdapter(), "telegram")[0];
        var envelope = await new ChannelEnvelopeMapper(new FixtureContactIdentityResolver()).MapAsync(providerMessage);

        Assert.Equal(ContactReference, envelope.ContactReference);
        Assert.Equal("fixture-pseudonym", envelope.ReporterKey);
        Assert.DoesNotContain(providerMessage.ProviderAddress, System.Text.Json.JsonSerializer.Serialize(envelope), StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsAppDeliveryFixtureMapsDeliveredStatusWithoutPayloadDetails()
    {
        var payload = """
            {"entry":[{"changes":[{"value":{"statuses":[{"id":"wamid.outbound-1","status":"delivered","timestamp":"1784558415","recipient_id":"2348000000000"}]}}]}]}
            """u8.ToArray();

        var receipt = Assert.Single(WhatsAppInboundAdapter.ParseDeliveryReceipts(payload));

        Assert.Equal("wamid.outbound-1", receipt.ProviderMessageId);
        Assert.Equal(ChannelDeliveryStatus.Delivered, receipt.Status);
        Assert.Null(receipt.FailureCode);
    }

    private static ProviderInboundMessage[] LoadStage(IChannelInboundAdapter adapter, string channel) =>
        StageFiles
            .SelectMany(file => adapter.Parse(File.ReadAllBytes(FixturePath(channel, file))))
            .ToArray();

    private static async Task<InboundChannelEnvelope[]> MapAsync(
        ChannelEnvelopeMapper mapper,
        ProviderInboundMessage[] messages) =>
        await Task.WhenAll(messages.Select(x => mapper.MapAsync(x)));

    private static string FixturePath(string channel, string file) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", channel, file);

    private static (IntakeContentKind Kind, DateTimeOffset At, IntakeLocation? Location) SemanticShape(
        InboundChannelEnvelope envelope) =>
        (envelope.ContentKind, envelope.OccurredAtUtc, envelope.Location);

    private static ReplayResult Replay(InboundChannelEnvelope[] envelopes)
    {
        var session = GuidedIntakeSession.Open(Guid.NewGuid(), envelopes[0], TimeSpan.FromMinutes(2));
        var initialPrompts = new[] { IntakePrompt.Acknowledgement }
            .Concat(session.PendingPrompts)
            .ToArray();
        foreach (var envelope in envelopes.Skip(1))
        {
            Assert.True(session.TryAttach(envelope));
        }

        return new ReplayResult(session, initialPrompts);
    }

    private sealed record ReplayResult(
        GuidedIntakeSession Session,
        IReadOnlyList<IntakePrompt> InitialPrompts);

    private sealed class FixtureContactIdentityResolver : IContactIdentityResolver
    {
        public Task<ContactIdentity> ResolveAsync(
            IntakeChannel channel,
            string providerAddress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContactIdentity(ContactReference, "fixture-pseudonym"));
    }
}
