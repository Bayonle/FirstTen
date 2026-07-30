using System.Security.Cryptography;
using System.Text;
using First10.Infrastructure.Modules.Intake;
using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace First10.IntegrationTests.Intake;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class GuidedIntakePersistenceTests(Persistence.PostgresFixture postgres)
{
    private static readonly Guid SessionContactReference = Guid.Parse("9a60a6a3-c9f0-480a-a436-851df434dc8f");
    [Fact]
    public async Task ContactDestinationIsProtectedAndDownstreamSessionUsesOnlyOpaqueIdentity()
    {
        await using var provider = await CreateProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var resolver = (ProtectedContactIdentityResolver)scope.ServiceProvider
            .GetRequiredService<IContactIdentityResolver>();
        var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
        const string providerAddress = "2348001234567";

        var identity = await resolver.ResolveAsync(IntakeChannel.WhatsApp, providerAddress);
        var stored = await database.ReporterContacts.SingleAsync(x => x.Id == identity.ContactReference);

        Assert.DoesNotContain(providerAddress, stored.ProtectedDestination, StringComparison.Ordinal);
        Assert.NotEqual(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(providerAddress))),
            stored.ReporterKey);
        Assert.Equal(providerAddress, await resolver.ResolveDestinationAsync(identity.ContactReference));

        scope.ServiceProvider.GetRequiredService<IConfiguration>()["Security:ReporterContactKeyVersion"] = "integration-v2";
        var rotatedIdentity = await resolver.ResolveAsync(IntakeChannel.WhatsApp, "2348007654321");
        Assert.Equal(
            "integration-v2",
            (await database.ReporterContacts.SingleAsync(x => x.Id == rotatedIdentity.ContactReference))
                .EncryptionKeyVersion);
        Assert.Equal(providerAddress, await resolver.ResolveDestinationAsync(identity.ContactReference));
    }

    [Fact]
    public async Task VoicePhotoAndLocationPersistAsOneSessionWithOnePromptSet()
    {
        await using var provider = await CreateProviderAsync();
        var at = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

        var first = Envelope("voice", IntakeContentKind.Voice, at);
        var opened = await ApplyAndSaveAsync(provider, first);
        Assert.NotNull(opened);
        Assert.Null(await ApplyAndSaveAsync(
            provider,
            Envelope("photo", IntakeContentKind.Photo, at.AddSeconds(4))));
        Assert.Null(await ApplyAndSaveAsync(provider, Envelope(
            "location",
            IntakeContentKind.Location,
            at.AddSeconds(8),
            new IntakeLocation(6.6018, 3.3515))));

        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
        var session = await database.GuidedIntakeSessions.Include(x => x.Inputs).SingleAsync(
            x => x.Id == opened.SessionId);
        Assert.Equal(GuidedSessionStatus.ReadyForPrivacyProcessing, session.Status);
        Assert.Equal(3, session.Inputs.Count);
        Assert.Equal(3, await database.IntakePromptIntents.CountAsync(x => x.SessionId == session.Id));
        Assert.All(session.Inputs, input => Assert.DoesNotContain(
            "2348001234567",
            System.Text.Json.JsonSerializer.Serialize(input),
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsupportedOrUnattachedInputCreatesVisibleRecoveryWork()
    {
        await using var provider = await CreateProviderAsync();
        var unsupported = Envelope("unsupported", IntakeContentKind.Unsupported, DateTimeOffset.UtcNow);

        Assert.Null(await ApplyAndSaveAsync(provider, unsupported));

        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
        var recovery = await database.IntakeRecoveryItems.SingleAsync(
            x => x.ProviderMessageId == unsupported.ProviderMessageId);
        Assert.Equal("unsupported_content", recovery.Reason);
        Assert.Equal(IntakeRecoveryStatus.Open, recovery.Status);
    }

    [Fact]
    public async Task PromptDeliveryResolvesDestinationOnlyInsideSenderBoundary()
    {
        await using var provider = await CreateProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ProtectedContactIdentityResolver>();
        var identity = await resolver.ResolveAsync(IntakeChannel.Telegram, "99887766");
        var envelope = Envelope("delivery", IntakeContentKind.Voice, DateTimeOffset.UtcNow) with
        {
            Channel = IntakeChannel.Telegram,
            ContactReference = identity.ContactReference,
            ReporterKey = identity.ReporterKey,
            CorrelationKey = identity.ReporterKey
        };
        var processor = scope.ServiceProvider.GetRequiredService<GuidedIntakeProcessor>();
        var outcome = await processor.ApplyAsync(envelope);
        var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
        var acknowledgement = await database.IntakePromptIntents.SingleAsync(
            x => x.Id == outcome!.PromptIntentIds[0]);
        var sender = new RecordingSender(IntakeChannel.Telegram);
        var delivery = new IntakePromptDeliveryService(database, resolver, [sender]);

        Assert.Null(await delivery.TryDeliverAsync(new DeliverIntakePrompt(acknowledgement.Id)));

        Assert.Equal("99887766", sender.Destination);
        Assert.Equal(ChannelDeliveryStatus.Accepted, acknowledgement.DeliveryStatus);
        Assert.Equal("provider-outbound-1", acknowledgement.ProviderMessageId);
    }

    [Fact]
    public async Task MultipleEligibleSessionsAttachToNewestAndCreateRecoveryMarker()
    {
        await using var provider = await CreateProviderAsync();
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<First10DbContext>();
            var at = DateTimeOffset.UtcNow;
            database.GuidedIntakeSessions.AddRange(
                GuidedIntakeSession.Open(
                    Guid.NewGuid(),
                    Envelope("older-session", IntakeContentKind.Photo, at),
                    TimeSpan.FromMinutes(2)),
                GuidedIntakeSession.Open(
                    Guid.NewGuid(),
                    Envelope("newer-session", IntakeContentKind.Voice, at.AddSeconds(10)),
                    TimeSpan.FromMinutes(2)));
            await database.SaveChangesAsync();
        }

        var location = Envelope(
            "ambiguous-location",
            IntakeContentKind.Location,
            DateTimeOffset.UtcNow.AddSeconds(20),
            new IntakeLocation(6.6018, 3.3515));
        await ApplyAndSaveAsync(provider, location);

        await using var verificationScope = provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<First10DbContext>();
        var sessions = await verification.GuidedIntakeSessions
            .Include(x => x.Inputs)
            .Where(x => x.ReporterKey == location.ReporterKey)
            .OrderBy(x => x.OpenedAtUtc)
            .ToArrayAsync();
        Assert.DoesNotContain(sessions[0].Inputs, x => x.ContentKind == IntakeContentKind.Location);
        Assert.Contains(sessions[1].Inputs, x => x.ContentKind == IntakeContentKind.Location);
        Assert.True(await verification.IntakeRecoveryItems.AnyAsync(
            x => x.ProviderMessageId == location.ProviderMessageId
                 && x.Reason == "multiple_open_sessions"));
    }

    private static async Task<OpenedSessionSchedule?> ApplyAndSaveAsync(
        ServiceProvider provider,
        InboundChannelEnvelope envelope)
    {
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<GuidedIntakeProcessor>()
            .ApplyAsync(envelope);
        await scope.ServiceProvider.GetRequiredService<First10DbContext>().SaveChangesAsync();
        return result;
    }

    private async Task<ServiceProvider> CreateProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ReporterPseudonymKey"] = "integration-only-pseudonym-key-000000000001",
                ["Security:ReporterContactKeyVersion"] = "integration-v1"
            })
            .Build());
        services.AddFirst10Persistence(postgres.ConnectionString);
        services.AddFirst10Intake();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<First10DbContext>().Database.MigrateAsync();
        return provider;
    }

    private static InboundChannelEnvelope Envelope(
        string id,
        IntakeContentKind kind,
        DateTimeOffset at,
        IntakeLocation? location = null) => new()
        {
            SchemaVersion = 1,
            Channel = IntakeChannel.WhatsApp,
            ProviderMessageId = $"{id}-{Guid.NewGuid():N}",
            ReporterKey = "reporter-integration",
            ContactReference = SessionContactReference,
            ContentKind = kind,
            ProviderMediaHandle = kind is IntakeContentKind.Photo or IntakeContentKind.Voice ? $"handle-{id}" : null,
            OccurredAtUtc = at,
            Location = location,
            CorrelationKey = "correlation"
        };

    private sealed class RecordingSender(IntakeChannel channel) : IChannelMessageSender
    {
        public IntakeChannel Channel { get; } = channel;

        public string? Destination { get; private set; }

        public Task<ProviderSendResult> SendAsync(
            string destination,
            string text,
            CancellationToken cancellationToken = default)
        {
            Destination = destination;
            return Task.FromResult(ProviderSendResult.Accepted("provider-outbound-1"));
        }
    }
}
