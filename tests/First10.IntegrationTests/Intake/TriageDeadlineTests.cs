using First10.Infrastructure.Modules.Intake;
using First10.Infrastructure.Modules.Intake.Triage;
using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using First10.Modules.Intake.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace First10.IntegrationTests.Intake;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class TriageDeadlineTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task DeadlineAtomicallyCreatesOneHighPriorityAlertOneFallbackAndAudit()
    {
        await using var provider = await CreateProviderAsync();
        var openedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Guid triageCaseId;
        await using (var intakeScope = provider.CreateAsyncScope())
        {
            var processor = intakeScope.ServiceProvider.GetRequiredService<GuidedIntakeProcessor>();
            var opened = await processor.ApplyAsync(Envelope(openedAt));
            triageCaseId = opened!.SessionId;
        }

        await using (var deadlineScope = provider.CreateAsyncScope())
        {
            var processor = deadlineScope.ServiceProvider.GetRequiredService<TriageDeadlineProcessor>();
            Assert.NotNull(await processor.ApplyAsync(triageCaseId, openedAt.AddSeconds(30)));
            Assert.Null(await processor.ApplyAsync(triageCaseId, openedAt.AddSeconds(31)));
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<First10DbContext>();
        var triageCase = await database.TriageCases.SingleAsync(x => x.Id == triageCaseId);
        var alert = await database.ManualTriageAlerts.SingleAsync(x => x.TriageCaseId == triageCaseId);
        var fallback = await database.IntakePromptIntents.SingleAsync(
            x => x.SessionId == triageCaseId && x.Prompt == IntakePrompt.ManualReviewFallback);

        Assert.Equal(TriageStatus.ManualReview, triageCase.Status);
        Assert.Equal(1, alert.Priority);
        Assert.Equal(ManualTriageAlertStatus.Pending, alert.Status);
        Assert.Contains("122", alert.Message, StringComparison.Ordinal);
        Assert.Contains("dispatcher", IntakeMessageCatalogue.Get(fallback.Language, fallback.Prompt), StringComparison.OrdinalIgnoreCase);
        Assert.Single(await database.AuditEvents
            .Where(x => x.Action == "intake.triage.manual_review"
                        && x.PayloadJson.Contains(triageCaseId.ToString()))
            .ToArrayAsync());
    }

    [Fact]
    public async Task CompletedTriageDoesNotRaiseManualAlert()
    {
        await using var provider = await CreateProviderAsync();
        var openedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Guid triageCaseId;
        await using (var intakeScope = provider.CreateAsyncScope())
        {
            var opened = await intakeScope.ServiceProvider.GetRequiredService<GuidedIntakeProcessor>()
                .ApplyAsync(Envelope(openedAt));
            triageCaseId = opened!.SessionId;
            var database = intakeScope.ServiceProvider.GetRequiredService<First10DbContext>();
            var triageCase = await database.TriageCases.Include(x => x.Assessments)
                .SingleAsync(x => x.Id == triageCaseId);
            var version = triageCase.AuthoritativeVersion;
            triageCase.TryStartProcessing(openedAt.AddSeconds(2));
            Assert.True(triageCase.TryApplyAssessment(
                ValidResult(),
                "gpt-5.6-luna/reasoning-low/schema-v1",
                openedAt.AddSeconds(10),
                version));
            database.TriageAssessments.Add(triageCase.Assessments.Single());
            await database.SaveChangesAsync();
        }

        await using (var deadlineScope = provider.CreateAsyncScope())
        {
            Assert.Null(await deadlineScope.ServiceProvider.GetRequiredService<TriageDeadlineProcessor>()
                .ApplyAsync(triageCaseId, openedAt.AddSeconds(30)));
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<First10DbContext>();
        Assert.False(await verification.ManualTriageAlerts.AnyAsync(x => x.TriageCaseId == triageCaseId));
    }

    private async Task<ServiceProvider> CreateProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ReporterPseudonymKey"] = "integration-only-pseudonym-key-000000000001",
                ["Security:ReporterContactKeyVersion"] = "integration-v1",
                ["ObjectStorage:Endpoint"] = "http://127.0.0.1:1",
                ["ObjectStorage:AccessKey"] = "unused",
                ["ObjectStorage:SecretKey"] = "unused"
            })
            .Build());
        services.AddFirst10Persistence(postgres.ConnectionString);
        services.AddFirst10Intake();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<First10DbContext>().Database.MigrateAsync();
        return provider;
    }

    private static InboundChannelEnvelope Envelope(DateTimeOffset at) => new()
    {
        SchemaVersion = 1,
        Channel = IntakeChannel.Telegram,
        ProviderMessageId = $"triage-{Guid.NewGuid():N}",
        ReporterKey = $"triage-reporter-{Guid.NewGuid():N}",
        ContactReference = Guid.NewGuid(),
        ContentKind = IntakeContentKind.Voice,
        ProviderMediaHandle = "provider-voice-handle",
        OccurredAtUtc = at,
        CorrelationKey = "triage-deadline"
    };

    private static StructuredTriage ValidResult() => new(
        IncidentType.RoadTrafficCollision,
        SeverityTier.High,
        1,
        2,
        ReportedLanguage.English,
        "Mowe inbound, near the toll gate",
        TriageUncertainty.Medium,
        [new TriageEvidenceReference("transcript:voice", TriageEvidenceKind.Transcript)],
        GuidanceCategory.RoadTrafficCollision);
}
