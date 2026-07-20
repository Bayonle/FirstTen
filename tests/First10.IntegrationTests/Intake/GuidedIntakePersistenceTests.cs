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
    }

    [Fact]
    public async Task VoicePhotoAndLocationPersistAsOneSessionWithOnePromptSet()
    {
        await using var provider = await CreateProviderAsync();
        var at = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

        var first = Envelope("voice", IntakeContentKind.Voice, at);
        Assert.NotNull(await ApplyAndSaveAsync(provider, first));
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
            x => x.ReporterKey == first.ReporterKey);
        Assert.Equal(GuidedSessionStatus.ReadyForPrivacyProcessing, session.Status);
        Assert.Equal(3, session.Inputs.Count);
        Assert.Equal(3, await database.IntakePromptIntents.CountAsync(x => x.SessionId == session.Id));
        Assert.All(session.Inputs, input => Assert.DoesNotContain(
            "2348001234567",
            System.Text.Json.JsonSerializer.Serialize(input),
            StringComparison.Ordinal));
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
}
