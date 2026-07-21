using System.Security.Cryptography;
using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Modules.Intake.Delivery;
using First10.Infrastructure.Modules.Dispatch;
using First10.Infrastructure.Modules.Guidance;
using First10.Infrastructure.Persistence;
using First10.Modules.Dispatch;
using First10.Modules.Guidance;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using First10.Modules.Intake;
using First10.Modules.Intake.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace First10.IntegrationTests.Guidance;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class GuidancePersistenceTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task InitialGuidancePinsApprovedLocaleAndReplayCreatesOneIntent()
    {
        var origin = new DateTimeOffset(2038, 8, 22, 10, 0, 0, TimeSpan.Zero);
        var caseId = await SeedTriageAsync("reporter-guidance", origin, ReportedLanguage.Yoruba);
        await using (var seed = Database())
        {
            seed.GuidanceTemplateSets.Add(ApprovedSet(
                GuidancePurpose.InitialSafety,
                "initial",
                GuidanceSeverityBand.Conservative,
                true,
                origin));
            await seed.SaveChangesAsync();
        }

        await using (var create = Database())
        {
            var processor = new GuidanceIntentProcessor(
                create,
                new GuidanceAssetStore(create),
                new FixedTimeProvider(origin.AddSeconds(8)));
            var request = new InitialGuidanceRequested(caseId, origin.AddSeconds(30));
            var first = await processor.CreateInitialAsync(request);
            var replay = await processor.CreateInitialAsync(request);
            Assert.NotNull(first);
            Assert.NotNull(replay);
            Assert.Equal(first.IntentId, replay.IntentId);
            Assert.True(first.ReadyForDelivery);
        }

        await using var verification = Database();
        var intent = await verification.GuidanceIntents.SingleAsync(x => x.TriageCaseId == caseId);
        Assert.Equal(GuidanceLanguage.Yoruba, intent.Language);
        Assert.Equal("Yoruba exact", intent.ExactText);
        Assert.Equal(origin.AddSeconds(30), intent.DeadlineAtUtc);
        Assert.Equal(GuidanceIntentStatus.Pending, intent.Status);
    }

    [Fact]
    public async Task MissingClinicalApprovalCreatesVisibleBlockedIntent()
    {
        var origin = new DateTimeOffset(2039, 9, 23, 11, 0, 0, TimeSpan.Zero);
        var caseId = await SeedTriageAsync(
            "reporter-blocked",
            origin,
            ReportedLanguage.English,
            First10.Modules.Intake.Triage.GuidanceCategory.OkadaCollision);
        await using var database = Database();
        var processor = new GuidanceIntentProcessor(
            database,
            new GuidanceAssetStore(database),
            new FixedTimeProvider(origin.AddSeconds(30)));

        var result = await processor.CreateInitialAsync(
            new InitialGuidanceRequested(caseId, origin.AddSeconds(30)));

        Assert.NotNull(result);
        Assert.False(result.ReadyForDelivery);
        Assert.Equal(
            GuidanceIntentStatus.BlockedNoApprovedTemplate,
            (await database.GuidanceIntents.SingleAsync(x => x.Id == result.IntentId)).Status);
    }

    [Fact]
    public async Task CommittedDispatchTransitionCreatesOneApprovedStatusIntent()
    {
        var origin = new DateTimeOffset(2040, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var caseId = await SeedTriageAsync("reporter-status", origin, ReportedLanguage.English);
        var incident = Incident.Create(
            Guid.NewGuid(),
            Candidate(caseId, "reporter-status", origin),
            origin.AddSeconds(5));
        Assert.True(incident.TryApplySingletonReviewDecision(
            SingletonReviewDecision.Verify,
            null,
            origin.AddSeconds(6)));
        await using (var seed = Database())
        {
            seed.Incidents.Add(incident);
            seed.GuidanceTemplateSets.Add(ApprovedSet(
                GuidancePurpose.ResponseStatus,
                "verified",
                GuidanceSeverityBand.High,
                false,
                origin));
            await seed.SaveChangesAsync();
        }

        var transitionId = Guid.NewGuid();
        await using (var transitionDatabase = Database())
        {
            var guidance = new GuidanceIntentProcessor(
                transitionDatabase,
                new GuidanceAssetStore(transitionDatabase),
                new FixedTimeProvider(origin.AddSeconds(7)));
            var processor = new DispatchTransitionProcessor(
                transitionDatabase,
                guidance,
                new FixedTimeProvider(origin.AddSeconds(7)));
            var outcome = await processor.ApplyAsync(new TransitionIncidentDispatch(
                transitionId,
                incident.Id,
                DispatchStatus.Verified,
                1,
                "dispatcher-integration",
                First10Roles.Dispatcher));
            Assert.Equal(DispatchTransitionResult.Applied, outcome.Result);
            Assert.Single(outcome.DeliveryIntentIds);
        }

        await using var verification = Database();
        Assert.Single(await verification.DispatchTransitions.Where(x => x.Id == transitionId).ToArrayAsync());
        var intent = await verification.GuidanceIntents.SingleAsync(x => x.IncidentId == incident.Id);
        Assert.Equal(GuidancePurpose.ResponseStatus, intent.Purpose);
        Assert.Equal("English exact", intent.ExactText);
        Assert.Single(await verification.IncidentTimelineEvents.Where(x => x.EventId == transitionId).ToArrayAsync());
    }

    [Fact]
    public async Task AcceptedTextThenFailedVoiceBecomesTerminalUnknownWithoutBlindRetry()
    {
        var temporary = Directory.CreateTempSubdirectory("first10-guidance-");
        try
        {
            var voiceBytes = "approved-voice-fixture"u8.ToArray();
            await File.WriteAllBytesAsync(Path.Combine(temporary.FullName, "en.ogg"), voiceBytes);
            var voiceHash = Convert.ToHexStringLower(SHA256.HashData(voiceBytes));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:ReporterPseudonymKey"] = new string('p', 32),
                    ["Security:ReporterContactKeyVersion"] = "test-v1",
                    ["Guidance:AssetRoot"] = temporary.FullName
                })
                .Build();
            await using var database = Database();
            await database.Database.MigrateAsync();
            var contacts = new ProtectedContactIdentityResolver(
                database,
                DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(temporary.FullName, "keys"))),
                configuration);
            var contact = await contacts.ResolveAsync(IntakeChannel.Telegram, "integration-chat");
            var set = GuidanceTemplateSet.CreateDraft(
                Guid.NewGuid(),
                "delivery-composite-test",
                GuidancePurpose.InitialSafety,
                First10.Modules.Guidance.GuidanceCategory.RoadTrafficCollision,
                GuidanceSeverityBand.Conservative,
                "2040.2",
                "initial",
                "integration eligibility",
                true,
                [
                    DraftWithVoice(GuidanceLanguage.English, "English exact", "en.ogg", voiceHash),
                    DraftWithVoice(GuidanceLanguage.NigerianPidgin, "Pidgin exact", "en.ogg", voiceHash),
                    DraftWithVoice(GuidanceLanguage.Yoruba, "Yoruba exact", "en.ogg", voiceHash)
                ]);
            Assert.True(set.TryApproveAndEnable(true, "clinical", DateTimeOffset.UtcNow));
            var intent = GuidanceIntent.Create(
                First10.Modules.BuildingBlocks.Contracts.SemanticMessageIdentity.Create("integration", Guid.NewGuid().ToString("N")),
                GuidancePurpose.InitialSafety,
                "initial",
                null,
                Guid.NewGuid(),
                contact.ContactReference,
                GuidanceChannel.Telegram,
                GuidanceLanguage.English,
                set,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddSeconds(30));
            database.GuidanceTemplateSets.Add(set);
            database.GuidanceIntents.Add(intent);
            await database.SaveChangesAsync();
            var text = new FakeTextSender();
            var voice = new FakeVoiceSender();
            var sender = new OutboundChannelSender(
                database,
                contacts,
                [text],
                [voice],
                new FileGuidanceVoiceAssetReader(configuration),
                TimeProvider.System);

            var retry = await sender.TryDeliverAsync(intent.Id);

            Assert.Null(retry);
            Assert.Equal(1, text.Calls);
            Assert.Equal(1, voice.Calls);
            Assert.Equal(GuidanceIntentStatus.Unknown, intent.Status);
            Assert.False(intent.CanAttemptDelivery);
            Assert.Contains("text_accepted_voice_", intent.FailureCode, StringComparison.Ordinal);
        }
        finally
        {
            temporary.Delete(true);
        }
    }

    private async Task<Guid> SeedTriageAsync(
        string reporterKey,
        DateTimeOffset occurredAtUtc,
        ReportedLanguage language,
        First10.Modules.Intake.Triage.GuidanceCategory guidanceCategory =
            First10.Modules.Intake.Triage.GuidanceCategory.RoadTrafficCollision)
    {
        await using var database = Database();
        await database.Database.MigrateAsync();
        var caseId = Guid.NewGuid();
        var session = GuidedIntakeSession.Open(
            caseId,
            new InboundChannelEnvelope
            {
                SchemaVersion = 1,
                Channel = IntakeChannel.Telegram,
                ProviderMessageId = $"guidance-{caseId:N}",
                ReporterKey = reporterKey,
                ContactReference = Guid.NewGuid(),
                ContentKind = IntakeContentKind.Voice,
                ProviderMediaHandle = $"voice-{caseId:N}",
                OccurredAtUtc = occurredAtUtc,
                CorrelationKey = $"guidance-{caseId:N}"
            },
            TimeSpan.FromMinutes(10));
        var triage = TriageCase.Open(caseId, occurredAtUtc, TimeSpan.FromMinutes(1));
        Assert.True(triage.TryStartProcessing(occurredAtUtc.AddSeconds(1)));
        Assert.True(triage.TryApplyAssessment(
            new StructuredTriage(
                IncidentType.RoadTrafficCollision,
                SeverityTier.High,
                1,
                1,
                language,
                "reviewed location",
                TriageUncertainty.Low,
                [new TriageEvidenceReference($"transcript:{caseId:N}", TriageEvidenceKind.Transcript)],
                guidanceCategory),
            "integration/triage",
            occurredAtUtc.AddSeconds(4),
            1));
        database.GuidedIntakeSessions.Add(session);
        database.TriageCases.Add(triage);
        await database.SaveChangesAsync();
        return caseId;
    }

    private static GuidanceTemplateSet ApprovedSet(
        GuidancePurpose purpose,
        string trigger,
        GuidanceSeverityBand severity,
        bool conservative,
        DateTimeOffset now)
    {
        var set = GuidanceTemplateSet.CreateDraft(
            Guid.NewGuid(),
            $"{purpose}-{trigger}-{Guid.NewGuid():N}",
            purpose,
            First10.Modules.Guidance.GuidanceCategory.RoadTrafficCollision,
            severity,
            "2040.1",
            trigger,
            "integration eligibility",
            conservative,
            [
                Draft(GuidanceLanguage.English, "English exact", "en.ogg"),
                Draft(GuidanceLanguage.NigerianPidgin, "Pidgin exact", "pcm.ogg"),
                Draft(GuidanceLanguage.Yoruba, "Yoruba exact", "yo.ogg")
            ]);
        Assert.True(set.TryApproveAndEnable(true, "clinical-integration", now));
        return set;
    }

    private static GuidanceLocaleDraft Draft(GuidanceLanguage language, string text, string key) =>
        new(
            language,
            text,
            GuidanceTemplateAsset.ComputeTextSha256(text),
            key,
            new string('a', 64),
            "gpt-4o-mini-tts",
            "alloy",
            "{}");

    private static GuidanceLocaleDraft DraftWithVoice(
        GuidanceLanguage language,
        string text,
        string key,
        string voiceHash) =>
        new(
            language,
            text,
            GuidanceTemplateAsset.ComputeTextSha256(text),
            key,
            voiceHash,
            "gpt-4o-mini-tts",
            "alloy",
            "{}");

    private static IncidentReportCandidate Candidate(Guid reportId, string reporterKey, DateTimeOffset occurredAtUtc) =>
        new(
            reportId,
            reporterKey,
            occurredAtUtc,
            occurredAtUtc.AddSeconds(4),
            7.2,
            4.2,
            0.99,
            IncidentKind.RoadTrafficCollision,
            IncidentSeverity.High,
            1,
            1,
            [$"triage:{reportId:N}"],
            $"verified:{reporterKey}",
            "reviewed location",
            IncidentTravelDirection.Unknown,
            ObservedVictimState.Unknown,
            ObservedSceneState.Unknown);

    private First10DbContext Database() => new(
        PersistenceConfiguration.CreateOptions(postgres.ConnectionString));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeTextSender : IChannelMessageSender
    {
        public int Calls { get; private set; }
        public IntakeChannel Channel => IntakeChannel.Telegram;

        public Task<ProviderSendResult> SendAsync(
            string destination,
            string text,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(ProviderSendResult.Accepted("text-accepted"));
        }
    }

    private sealed class FakeVoiceSender : IChannelVoiceMessageSender
    {
        public int Calls { get; private set; }
        public IntakeChannel Channel => IntakeChannel.Telegram;

        public Task<ProviderSendResult> SendVoiceAsync(
            string destination,
            ReadOnlyMemory<byte> voice,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(ProviderSendResult.Failed("voice_rejected"));
        }
    }
}
