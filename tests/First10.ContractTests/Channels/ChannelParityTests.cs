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
        Assert.Equal(telegramSession.Status, whatsappSession.Status);
        Assert.Equal(telegramSession.VisibleGaps, whatsappSession.VisibleGaps);
        Assert.Equal(
            telegramSession.Inputs.Select(x => (x.ContentKind, x.OccurredAtUtc)),
            whatsappSession.Inputs.Select(x => (x.ContentKind, x.OccurredAtUtc)));
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

    private static GuidedIntakeSession Replay(InboundChannelEnvelope[] envelopes)
    {
        var session = GuidedIntakeSession.Open(Guid.NewGuid(), envelopes[0], TimeSpan.FromMinutes(2));
        foreach (var envelope in envelopes.Skip(1))
        {
            Assert.True(session.TryAttach(envelope));
        }

        return session;
    }

    private sealed class FixtureContactIdentityResolver : IContactIdentityResolver
    {
        public Task<ContactIdentity> ResolveAsync(
            IntakeChannel channel,
            string providerAddress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContactIdentity(ContactReference, "fixture-pseudonym"));
    }
}
