using First10.Infrastructure.Modules.Intake;
using First10.Infrastructure.Modules.Intake.Location;
using First10.Infrastructure.Modules.Intake.Media;
using First10.Infrastructure.Modules.Intake.OpenAI;
using First10.Infrastructure.Modules.Intake.Triage;
using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using First10.Modules.Intake.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace First10.IntegrationTests.Intake;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class TriagePipelineTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task PrivacySafeAudioBecomesSourceLinkedTriageAndReviewedLocation()
    {
        var openedAt = DateTimeOffset.UtcNow;
        await using var provider = await CreateProviderAsync();
        Guid sessionId;
        Guid audioAssetId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<First10DbContext>();
            var opened = await new GuidedIntakeProcessor(database).ApplyAsync(Envelope(openedAt));
            sessionId = opened!.SessionId;
            var session = await database.GuidedIntakeSessions.Include(x => x.Inputs)
                .SingleAsync(x => x.Id == sessionId);
            var input = session.Inputs.Single();
            audioAssetId = input.Id;
            var asset = IntakeMediaAsset.Create(input.Id, session.Id, IntakeMediaKind.Audio, openedAt);
            asset.TryMarkStored(
                "safe/audio/opaque",
                "audio/wav",
                15,
                "test-v1",
                "ogg-opus-sanitizer-v1",
                openedAt.AddSeconds(1),
                openedAt.AddDays(30));
            database.IntakeMediaAssets.Add(asset);
            await database.SaveChangesAsync();
        }

        var transcriber = new FakeTranscriber();
        var triageProvider = new FakeTriageProvider();
        var time = new FixedTimeProvider(openedAt.AddSeconds(5));
        await using (var processingScope = provider.CreateAsyncScope())
        {
            var database = processingScope.ServiceProvider.GetRequiredService<First10DbContext>();
            var processor = new TriageSessionProcessor(
                database,
                new FakeSafeMediaReader("RIFF-safe-audio"u8.ToArray()),
                new OpenAiAudioPreparer(),
                transcriber,
                triageProvider,
                new OpenAiSafetyIdentifier(Configuration()),
                new CorridorGazetteer(
                [
                    new CorridorLandmark(
                        "mowe-toll-gate",
                        "Mowe Toll Gate",
                        ["mowe toll gate", "toll gate"],
                        6.808,
                        3.439,
                        TravelDirection.IbadanInbound,
                        true)
                ]),
                Configuration(),
                time);

            await processor.ProcessAsync(sessionId);
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<First10DbContext>();
        var triageCase = await verification.TriageCases.Include(x => x.Assessments)
            .SingleAsync(x => x.Id == sessionId);
        var assessment = triageCase.Assessments.Single();
        Assert.Equal(TriageStatus.Completed, triageCase.Status);
        Assert.True(assessment.IsAuthoritative);
        Assert.Equal("mowe-toll-gate", assessment.ResolvedLandmarkId);
        Assert.Equal(TravelDirection.IbadanInbound, assessment.ResolvedDirection);
        Assert.Equal($"transcript:{audioAssetId:N}", assessment.EvidenceReferences);
        Assert.Equal("audio/wav", transcriber.ContentType);
        Assert.StartsWith("f10_", triageProvider.Request!.SafetyIdentifier, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-voice-handle", System.Text.Json.JsonSerializer.Serialize(triageProvider.Request), StringComparison.Ordinal);
        Assert.True(await verification.AuditEvents.AnyAsync(x => x.Action == "intake.triage.completed"));
    }

    [Fact]
    public async Task ProviderFailureRemainsVisibleAndDeadlineStillCreatesSingleManualPath()
    {
        var openedAt = DateTimeOffset.UtcNow;
        await using var provider = await CreateProviderAsync();
        Guid sessionId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<First10DbContext>();
            var opened = await new GuidedIntakeProcessor(database).ApplyAsync(Envelope(openedAt));
            sessionId = opened!.SessionId;
            var input = (await database.GuidedIntakeSessions.Include(x => x.Inputs)
                .SingleAsync(x => x.Id == sessionId)).Inputs.Single();
            var asset = IntakeMediaAsset.Create(input.Id, sessionId, IntakeMediaKind.Audio, openedAt);
            asset.TryMarkStored(
                "safe/audio/opaque-failure",
                "audio/wav",
                15,
                "test-v1",
                "ogg-opus-sanitizer-v1",
                openedAt.AddSeconds(1),
                openedAt.AddDays(30));
            database.IntakeMediaAssets.Add(asset);
            await database.SaveChangesAsync();
        }

        await using (var processingScope = provider.CreateAsyncScope())
        {
            var database = processingScope.ServiceProvider.GetRequiredService<First10DbContext>();
            var processor = new TriageSessionProcessor(
                database,
                new FakeSafeMediaReader("RIFF-safe-audio"u8.ToArray()),
                new OpenAiAudioPreparer(),
                new FailingTranscriber(),
                new FakeTriageProvider(),
                new OpenAiSafetyIdentifier(Configuration()),
                new CorridorGazetteer([]),
                Configuration(),
                new FixedTimeProvider(openedAt.AddSeconds(5)));
            await processor.ProcessAsync(sessionId);
        }

        await using (var deadlineScope = provider.CreateAsyncScope())
        {
            var deadline = new TriageDeadlineProcessor(
                deadlineScope.ServiceProvider.GetRequiredService<First10DbContext>());
            Assert.NotNull(await deadline.ApplyAsync(sessionId, openedAt.AddSeconds(30)));
            Assert.Null(await deadline.ApplyAsync(sessionId, openedAt.AddSeconds(31)));
        }

        await using var verificationScope = provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<First10DbContext>();
        Assert.True(await verification.IntakeRecoveryItems.AnyAsync(x =>
            x.Reason == "triage_transcription_http_429"));
        Assert.Single(await verification.ManualTriageAlerts.Where(x => x.TriageCaseId == sessionId).ToArrayAsync());
        Assert.Single(await verification.IntakePromptIntents.Where(x =>
            x.SessionId == sessionId && x.Prompt == IntakePrompt.ManualReviewFallback).ToArrayAsync());
    }

    [Fact]
    public async Task RepeatedMediaUsesLatestOccurredInputWithDeterministicAssetTieBreak()
    {
        var openedAt = DateTimeOffset.UtcNow;
        await using var provider = await CreateProviderAsync();
        Guid sessionId;
        Guid expectedAudioAssetId;
        Guid expectedImageAssetId;
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<First10DbContext>();
            var first = Envelope(openedAt);
            var session = GuidedIntakeSession.Open(Guid.NewGuid(), first, TimeSpan.FromMinutes(2));
            var repeatedAt = openedAt.AddSeconds(3);
            Assert.True(session.TryAttach(RepeatedEnvelope(session, "voice-second", IntakeContentKind.Voice, repeatedAt)));
            Assert.True(session.TryAttach(RepeatedEnvelope(session, "photo-first", IntakeContentKind.Photo, openedAt.AddSeconds(2))));
            Assert.True(session.TryAttach(RepeatedEnvelope(session, "photo-second", IntakeContentKind.Photo, repeatedAt)));

            var audioInputs = session.Inputs.Where(x => x.ContentKind == IntakeContentKind.Voice).ToArray();
            var imageInputs = session.Inputs.Where(x => x.ContentKind == IntakeContentKind.Photo).ToArray();
            expectedAudioAssetId = audioInputs
                .OrderByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.Id)
                .First().Id;
            expectedImageAssetId = imageInputs
                .OrderByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.Id)
                .First().Id;

            database.GuidedIntakeSessions.Add(session);
            database.TriageCases.Add(TriageCase.Open(session.Id, openedAt));
            database.IntakeMediaAssets.AddRange(audioInputs.Select(input => StoredAsset(
                input,
                IntakeMediaKind.Audio,
                "audio/wav",
                openedAt)));
            database.IntakeMediaAssets.AddRange(imageInputs.Select(input => StoredAsset(
                input,
                IntakeMediaKind.Image,
                "image/jpeg",
                openedAt)));
            await database.SaveChangesAsync();
            sessionId = session.Id;
        }

        var reader = new RecordingSafeMediaReader();
        var triageProvider = new FakeTriageProvider();
        await using (var processingScope = provider.CreateAsyncScope())
        {
            var processor = new TriageSessionProcessor(
                processingScope.ServiceProvider.GetRequiredService<First10DbContext>(),
                reader,
                new OpenAiAudioPreparer(),
                new FakeTranscriber(),
                triageProvider,
                new OpenAiSafetyIdentifier(Configuration()),
                new CorridorGazetteer([]),
                Configuration(),
                new FixedTimeProvider(openedAt.AddSeconds(5)));

            await processor.ProcessAsync(sessionId);
        }

        Assert.Equal([expectedAudioAssetId, expectedImageAssetId], reader.ReadAssetIds);
        Assert.Equal($"transcript:{expectedAudioAssetId:N}", triageProvider.Request!.Transcript.EvidenceReference);
        Assert.Equal($"image:{expectedImageAssetId:N}", triageProvider.Request.ImageEvidenceReference);
    }

    private async Task<ServiceProvider> CreateProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(Configuration());
        services.AddFirst10Persistence(postgres.ConnectionString);
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<First10DbContext>().Database.MigrateAsync();
        return provider;
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenAI:SafetyIdentifierKey"] = "integration-only-safety-key-0000000000001"
        })
        .Build();

    private static InboundChannelEnvelope Envelope(DateTimeOffset at) => new()
    {
        SchemaVersion = 1,
        Channel = IntakeChannel.Telegram,
        ProviderMessageId = $"triage-pipeline-{Guid.NewGuid():N}",
        ReporterKey = $"triage-pipeline-reporter-{Guid.NewGuid():N}",
        ContactReference = Guid.NewGuid(),
        ContentKind = IntakeContentKind.Voice,
        ProviderMediaHandle = "provider-voice-handle",
        OccurredAtUtc = at,
        CorrelationKey = "triage-pipeline"
    };

    private static InboundChannelEnvelope RepeatedEnvelope(
        GuidedIntakeSession session,
        string providerMessageId,
        IntakeContentKind kind,
        DateTimeOffset occurredAtUtc) => new()
        {
            SchemaVersion = 1,
            Channel = session.Channel,
            ProviderMessageId = providerMessageId,
            ReporterKey = session.ReporterKey,
            ContactReference = session.ContactReference,
            ContentKind = kind,
            ProviderMediaHandle = $"provider-{providerMessageId}",
            OccurredAtUtc = occurredAtUtc,
            CorrelationKey = session.CorrelationKey
        };

    private static IntakeMediaAsset StoredAsset(
        GuidedSessionInput input,
        IntakeMediaKind kind,
        string contentType,
        DateTimeOffset openedAt)
    {
        var asset = IntakeMediaAsset.Create(input.Id, input.SessionId, kind, openedAt);
        Assert.True(asset.TryMarkStored(
            $"safe/{kind.ToString().ToLowerInvariant()}/{input.Id:N}",
            contentType,
            15,
            "test-v1",
            "test-privacy-v1",
            openedAt.AddSeconds(4),
            openedAt.AddDays(30)));
        return asset;
    }

    private sealed class FakeSafeMediaReader(byte[] content) : ISafeMediaReader
    {
        public Task<SafeMediaContent> ReadAsync(
            Guid assetId,
            string objectKey,
            string contentType,
            long expectedPlaintextLength,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SafeMediaContent(content.ToArray(), contentType));
    }

    private sealed class RecordingSafeMediaReader : ISafeMediaReader
    {
        public List<Guid> ReadAssetIds { get; } = [];

        public Task<SafeMediaContent> ReadAsync(
            Guid assetId,
            string objectKey,
            string contentType,
            long expectedPlaintextLength,
            CancellationToken cancellationToken = default)
        {
            ReadAssetIds.Add(assetId);
            var content = contentType == "audio/wav"
                ? "RIFF-safe-audio"u8.ToArray()
                : "blurred-image"u8.ToArray();
            return Task.FromResult(new SafeMediaContent(content, contentType));
        }
    }

    private sealed class FakeTranscriber : IReporterAudioTranscriber
    {
        public string? ContentType { get; private set; }

        public Task<ReporterTranscript> TranscribeAsync(
            ReadOnlyMemory<byte> safeAudio,
            string contentType,
            string evidenceReference,
            DateTimeOffset sourceOccurredAtUtc,
            CancellationToken cancellationToken = default)
        {
            ContentType = contentType;
            return Task.FromResult(new ReporterTranscript(
                "Mowe inbound near the toll gate",
                evidenceReference,
                sourceOccurredAtUtc,
                0.92,
                TranscriptionQuality.High,
                "gpt-4o-transcribe"));
        }
    }

    private sealed class FailingTranscriber : IReporterAudioTranscriber
    {
        public Task<ReporterTranscript> TranscribeAsync(
            ReadOnlyMemory<byte> safeAudio,
            string contentType,
            string evidenceReference,
            DateTimeOffset sourceOccurredAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new OpenAiProviderException("transcription_http_429");
    }

    private sealed class FakeTriageProvider : IStructuredTriageProvider
    {
        public TriageProviderRequest? Request { get; private set; }

        public Task<TriageProviderResult> TriageAsync(
            TriageProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new TriageProviderResult(
                new StructuredTriage(
                    IncidentType.RoadTrafficCollision,
                    SeverityTier.High,
                    1,
                    2,
                    ReportedLanguage.English,
                    "Mowe inbound near the toll gate",
                    TriageUncertainty.Medium,
                    [new TriageEvidenceReference(
                        request.Transcript.EvidenceReference,
                        TriageEvidenceKind.Transcript)],
                    GuidanceCategory.None),
                "gpt-5.6-luna/reasoning-low/triage-v1/image-low"));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
