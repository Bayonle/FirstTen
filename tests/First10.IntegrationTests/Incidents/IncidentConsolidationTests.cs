using First10.Infrastructure.Modules.Incidents;
using First10.Infrastructure.Persistence;
using First10.Modules.Incidents;
using First10.Modules.Intake;
using First10.Modules.Intake.Triage;
using Microsoft.EntityFrameworkCore;

namespace First10.IntegrationTests.Incidents;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class IncidentConsolidationTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task IndependentSourceReportsMergeAutoVerifyAndPreserveContradictionsInTimelineOrder()
    {
        var origin = new DateTimeOffset(2031, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var firstCaseId = await SeedTriageAsync(
            "reporter-alpha",
            origin.AddMinutes(4),
            origin.AddMinutes(4).AddSeconds(8),
            7.25,
            4.15,
            0.97,
            1,
            2);
        var secondCaseId = await SeedTriageAsync(
            "reporter-bravo",
            origin,
            origin.AddMinutes(4).AddSeconds(18),
            LatitudeOffset(7.25, 120),
            4.15,
            0.96,
            4,
            5);

        var verifiedRegistry = new PassThroughVerifiedRegistry();
        var first = await MatchAsync(firstCaseId, origin.AddMinutes(4).AddSeconds(10), verifiedRegistry);
        var second = await MatchAsync(secondCaseId, origin.AddMinutes(4).AddSeconds(20), verifiedRegistry);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.True(second.AutoVerified);
        Assert.Equal(first.IncidentId, second.IncidentId);

        await using var verification = Database();
        var incident = await verification.Incidents
            .Include(x => x.SourceReports)
            .Include(x => x.Conflicts)
            .SingleAsync(x => x.Id == first.IncidentId);
        Assert.Equal(IncidentVerificationStatus.AutoVerified, incident.VerificationStatus);
        Assert.Equal(2, incident.SourceReports.Count);
        Assert.Equal(2, incident.IndependentReporterCount);
        Assert.Contains(incident.Conflicts, x =>
            x.Field == IncidentConflictField.CasualtyEstimate && !x.IsResolved);

        var timeline = await verification.IncidentTimelineEvents
            .Where(x => x.IncidentId == first.IncidentId)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.ReceivedAtUtc)
            .ToArrayAsync();
        var linkedReports = timeline.Where(x => x.EventType == "report-linked").ToArray();
        Assert.Equal(2, linkedReports.Length);
        Assert.Equal(secondCaseId, ReportId(linkedReports[0].PayloadJson));
        Assert.Equal(firstCaseId, ReportId(linkedReports[1].PayloadJson));
        Assert.Equal(origin.AddMinutes(4).AddSeconds(18), linkedReports[0].ReceivedAtUtc);
        Assert.Contains(timeline, x => x.EventType == "conflict-detected");
        Assert.Contains(timeline, x => x.EventType == "incident-auto-verified");

        var replay = await MatchAsync(secondCaseId, origin.AddMinutes(4).AddSeconds(30), verifiedRegistry);
        Assert.NotNull(replay);
        Assert.False(replay.Created);
        Assert.Equal(2, await verification.IncidentSourceReports.CountAsync(
            x => x.IncidentId == first.IncidentId));

        var conflictId = incident.Conflicts.Single(x => x.Field == IncidentConflictField.CasualtyEstimate).Id;
        var resolution = new ResolveIncidentConflict(
            incident.Id,
            conflictId,
            firstCaseId,
            "dispatcher-integration");
        await using (var resolutionDatabase = Database())
        {
            var processor = new IncidentConflictResolutionProcessor(
                resolutionDatabase,
                new FixedTimeProvider(origin.AddMinutes(5)));
            Assert.True(await processor.ResolveAsync(resolution));
        }

        await using var resolvedVerification = Database();
        Assert.True((await resolvedVerification.IncidentConflicts.SingleAsync(x => x.Id == conflictId)).IsResolved);
        Assert.Single(await resolvedVerification.IncidentTimelineEvents.Where(x =>
            x.IncidentId == incident.Id && x.EventType == "conflict-resolved").ToArrayAsync());
    }

    [Fact]
    public async Task AmbiguousLocationStaysSeparateAndIsLinkedForDispatcherReview()
    {
        var origin = new DateTimeOffset(2032, 2, 16, 11, 0, 0, TimeSpan.Zero);
        var reviewedCaseId = await SeedTriageAsync(
            "reporter-charlie",
            origin,
            origin.AddSeconds(4),
            8.1,
            5.2,
            0.98,
            1,
            1);
        var ambiguousCaseId = await SeedTriageAsync(
            "reporter-delta",
            origin.AddMinutes(1),
            origin.AddMinutes(1).AddSeconds(4),
            8.1001,
            5.2,
            0.60,
            1,
            1);

        var first = await MatchAsync(reviewedCaseId, origin.AddSeconds(5));
        var second = await MatchAsync(ambiguousCaseId, origin.AddMinutes(1).AddSeconds(5));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.IncidentId, second.IncidentId);
        Assert.True(second.Created);
        Assert.Equal(1, second.CandidateLinkCount);

        await using var verification = Database();
        var link = await verification.IncidentCandidateLinks.SingleAsync(x =>
            x.FirstIncidentId == first.IncidentId && x.SecondIncidentId == second.IncidentId
            || x.FirstIncidentId == second.IncidentId && x.SecondIncidentId == first.IncidentId);
        Assert.Equal("uncertain_location", link.Reason);
        Assert.Equal(2, await verification.Incidents.CountAsync(x =>
            x.Id == first.IncidentId || x.Id == second.IncidentId));
    }

    [Fact]
    public async Task SingletonRaisesExactlyOneVisibleReviewAlertAtSixtySeconds()
    {
        var origin = new DateTimeOffset(2033, 3, 17, 12, 0, 0, TimeSpan.Zero);
        var caseId = await SeedTriageAsync(
            "reporter-echo",
            origin,
            origin.AddSeconds(4),
            9.1,
            6.2,
            0.98,
            1,
            1);
        var match = await MatchAsync(caseId, origin.AddSeconds(5));
        Assert.NotNull(match);
        Assert.Equal(origin.AddSeconds(65), match.ReviewDueAtUtc);

        await using (var earlyDatabase = Database())
        {
            var early = new SingletonIncidentReviewProcessor(
                earlyDatabase,
                new FixedTimeProvider(origin.AddSeconds(64)));
            Assert.Null(await early.RaiseAsync(match.IncidentId));
        }

        await using (var dueDatabase = Database())
        {
            var due = new SingletonIncidentReviewProcessor(
                dueDatabase,
                new FixedTimeProvider(origin.AddSeconds(65)));
            Assert.NotNull(await due.RaiseAsync(match.IncidentId));
        }

        await using (var replayDatabase = Database())
        {
            var replay = new SingletonIncidentReviewProcessor(
                replayDatabase,
                new FixedTimeProvider(origin.AddSeconds(66)));
            Assert.Null(await replay.RaiseAsync(match.IncidentId));
        }

        await using var verification = Database();
        Assert.Single(await verification.IncidentReviewAlerts
            .Where(x => x.IncidentId == match.IncidentId && x.Status == IncidentReviewAlertStatus.Open)
            .ToArrayAsync());
        Assert.Single(await verification.IncidentTimelineEvents
            .Where(x => x.IncidentId == match.IncidentId && x.EventType == "singleton-review-raised")
            .ToArrayAsync());
    }

    [Fact]
    public async Task LateLocationEvidenceUpdatesSourceWithoutErasingItsOccurrenceAndReceiptTimes()
    {
        var origin = new DateTimeOffset(2034, 4, 18, 13, 0, 0, TimeSpan.Zero);
        var caseId = await SeedTriageAsync(
            "reporter-foxtrot",
            origin,
            origin.AddSeconds(4),
            0,
            0,
            0,
            1,
            1,
            resolveLocation: false);
        var match = await MatchAsync(caseId, origin.AddSeconds(5));
        Assert.NotNull(match);
        var evidenceId = Guid.NewGuid();
        var command = new ApplyLateIncidentLocation(
            caseId,
            evidenceId,
            origin.AddSeconds(2),
            origin.AddSeconds(20),
            9.4,
            6.5,
            1);

        await using (var applyDatabase = Database())
        {
            var persistence = new IncidentPersistence(
                applyDatabase,
                new FixedTimeProvider(origin.AddSeconds(20)));
            var processor = new LateIncidentLocationProcessor(applyDatabase, persistence);
            Assert.True(await processor.ApplyAsync(command));
        }

        await using (var replayDatabase = Database())
        {
            var persistence = new IncidentPersistence(
                replayDatabase,
                new FixedTimeProvider(origin.AddSeconds(21)));
            var processor = new LateIncidentLocationProcessor(replayDatabase, persistence);
            Assert.False(await processor.ApplyAsync(command));
        }

        await using var verification = Database();
        var source = await verification.IncidentSourceReports.SingleAsync(x => x.ReportId == caseId);
        Assert.Equal(9.4, source.Latitude);
        Assert.Contains($"pin:{evidenceId:N}", source.EvidenceReferences, StringComparison.Ordinal);
        var timeline = await verification.IncidentTimelineEvents.SingleAsync(x => x.EventId == evidenceId);
        Assert.Equal("late-location-evidence", timeline.EventType);
        Assert.Equal(origin.AddSeconds(2), timeline.OccurredAtUtc);
        Assert.Equal(origin.AddSeconds(20), timeline.ReceivedAtUtc);
    }

    [Fact]
    public async Task ManualFallbackCreatesQueryableIncidentWithoutAiOrLocation()
    {
        var origin = new DateTimeOffset(2035, 5, 19, 14, 0, 0, TimeSpan.Zero);
        var caseId = await SeedManualFallbackAsync("reporter-manual", origin);

        var match = await MatchAsync(caseId, origin.AddSeconds(31));

        Assert.NotNull(match);
        Assert.True(match.Created);
        await using var verification = Database();
        var incident = await verification.Incidents
            .Include(x => x.SourceReports)
            .SingleAsync(x => x.Id == match.IncidentId);
        var source = Assert.Single(incident.SourceReports);
        Assert.Null(source.Latitude);
        Assert.Equal(IncidentKind.Unknown, source.IncidentType);
        Assert.Equal(IncidentVerificationStatus.AwaitingConfirmation, incident.VerificationStatus);
    }

    [Fact]
    public async Task RelayObservationsAreIdempotentAndPreserveVisibleVictimStateConflict()
    {
        var origin = new DateTimeOffset(2036, 6, 20, 15, 0, 0, TimeSpan.Zero);
        var caseId = await SeedTriageAsync(
            "reporter-relay",
            origin,
            origin.AddSeconds(4),
            7.1,
            4.2,
            0.99,
            1,
            1);
        var match = await MatchAsync(caseId, origin.AddSeconds(5));
        Assert.NotNull(match);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await using (var appendDatabase = Database())
        {
            var processor = new IncidentObservationProcessor(appendDatabase);
            Assert.True(await processor.AppendAsync(new AppendIncidentObservation(
                firstId,
                match.IncidentId,
                caseId,
                ObservedVictimState.Responsive,
                ObservedSceneState.Unknown,
                null,
                IncidentTravelDirection.Unknown,
                null,
                null,
                origin.AddSeconds(6),
                origin.AddSeconds(7),
                "voice:relay-1")));
        }

        await using (var appendDatabase = Database())
        {
            var processor = new IncidentObservationProcessor(appendDatabase);
            var second = new AppendIncidentObservation(
                secondId,
                match.IncidentId,
                caseId,
                ObservedVictimState.Unresponsive,
                ObservedSceneState.Unknown,
                null,
                IncidentTravelDirection.Unknown,
                null,
                null,
                origin.AddSeconds(8),
                origin.AddSeconds(9),
                "voice:relay-2");
            Assert.True(await processor.AppendAsync(second));
            Assert.False(await processor.AppendAsync(second));
        }

        await using var verification = Database();
        Assert.Equal(2, await verification.IncidentObservations.CountAsync(x => x.IncidentId == match.IncidentId));
        Assert.Contains(await verification.IncidentConflicts.Where(x => x.IncidentId == match.IncidentId).ToArrayAsync(),
            x => x.Field == IncidentConflictField.VictimState && !x.IsResolved);
        Assert.Equal(2, await verification.IncidentTimelineEvents.CountAsync(x =>
            x.IncidentId == match.IncidentId && x.EventType == "relay-observation-added"));
    }

    [Fact]
    public async Task SingletonDecisionResolvesAlertAndReplayCannotRegressIncident()
    {
        var origin = new DateTimeOffset(2037, 7, 21, 16, 0, 0, TimeSpan.Zero);
        var caseId = await SeedTriageAsync(
            "reporter-singleton-decision",
            origin,
            origin.AddSeconds(4),
            7.2,
            4.3,
            0.99,
            1,
            1);
        var match = await MatchAsync(caseId, origin.AddSeconds(5));
        Assert.NotNull(match);

        await using (var alertDatabase = Database())
        {
            var processor = new SingletonIncidentReviewProcessor(
                alertDatabase,
                new FixedTimeProvider(origin.AddSeconds(65)));
            Assert.NotNull(await processor.RaiseAsync(match.IncidentId));
        }

        var command = new DecideSingletonIncident(
            Guid.NewGuid(),
            match.IncidentId,
            SingletonReviewDecision.Verify,
            "dispatcher-integration",
            null);
        await using (var decisionDatabase = Database())
        {
            var processor = new SingletonIncidentDecisionProcessor(
                decisionDatabase,
                new FixedTimeProvider(origin.AddSeconds(66)));
            Assert.True(await processor.DecideAsync(command));
        }

        await using (var replayDatabase = Database())
        {
            var processor = new SingletonIncidentDecisionProcessor(
                replayDatabase,
                new FixedTimeProvider(origin.AddSeconds(67)));
            Assert.False(await processor.DecideAsync(command));
        }

        await using var verification = Database();
        Assert.Equal(
            IncidentVerificationStatus.DispatcherVerified,
            (await verification.Incidents.SingleAsync(x => x.Id == match.IncidentId)).VerificationStatus);
        Assert.Equal(
            IncidentReviewAlertStatus.Resolved,
            (await verification.IncidentReviewAlerts.SingleAsync(x => x.IncidentId == match.IncidentId)).Status);
        Assert.Single(await verification.IncidentTimelineEvents.Where(x =>
            x.IncidentId == match.IncidentId && x.EventId == command.DecisionId).ToArrayAsync());
    }

    private async Task<Guid> SeedTriageAsync(
        string reporterKey,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset receivedAtUtc,
        double latitude,
        double longitude,
        double confidence,
        int casualtiesMinimum,
        int casualtiesMaximum,
        bool resolveLocation = true)
    {
        await using var database = Database();
        await database.Database.MigrateAsync();
        var caseId = Guid.NewGuid();
        var envelope = new InboundChannelEnvelope
        {
            SchemaVersion = 1,
            Channel = IntakeChannel.Telegram,
            ProviderMessageId = $"incident-{caseId:N}",
            ReporterKey = reporterKey,
            ContactReference = Guid.NewGuid(),
            ContentKind = IntakeContentKind.Voice,
            ProviderMediaHandle = $"voice-{caseId:N}",
            OccurredAtUtc = occurredAtUtc,
            CorrelationKey = $"incident-{caseId:N}"
        };
        var session = GuidedIntakeSession.Open(caseId, envelope, TimeSpan.FromMinutes(10));
        var triageCase = TriageCase.Open(caseId, occurredAtUtc, TimeSpan.FromMinutes(10));
        Assert.True(triageCase.TryStartProcessing(receivedAtUtc.AddSeconds(-1)));
        Assert.True(triageCase.TryApplyAssessment(
            new StructuredTriage(
                IncidentType.RoadTrafficCollision,
                SeverityTier.High,
                casualtiesMinimum,
                casualtiesMaximum,
                ReportedLanguage.English,
                "reviewed corridor location",
                TriageUncertainty.Low,
                [new TriageEvidenceReference($"transcript:{caseId:N}", TriageEvidenceKind.Transcript)],
                GuidanceCategory.None),
            "integration/structured-triage",
            receivedAtUtc,
            1));
        if (resolveLocation)
        {
            Assert.True(triageCase.Assessments.Single().TryApplyResolvedLocation(
                new ResolvedCorridorLocation(
                    latitude,
                    longitude,
                    "integration-landmark",
                    TravelDirection.Unknown,
                    confidence,
                    $"transcript:{caseId:N}",
                    false)));
        }
        database.GuidedIntakeSessions.Add(session);
        database.TriageCases.Add(triageCase);
        await database.SaveChangesAsync();
        return caseId;
    }

    private async Task<Guid> SeedManualFallbackAsync(string reporterKey, DateTimeOffset occurredAtUtc)
    {
        await using var database = Database();
        await database.Database.MigrateAsync();
        var caseId = Guid.NewGuid();
        var envelope = new InboundChannelEnvelope
        {
            SchemaVersion = 1,
            Channel = IntakeChannel.Telegram,
            ProviderMessageId = $"manual-{caseId:N}",
            ReporterKey = reporterKey,
            ContactReference = Guid.NewGuid(),
            ContentKind = IntakeContentKind.Voice,
            ProviderMediaHandle = $"voice-{caseId:N}",
            OccurredAtUtc = occurredAtUtc,
            CorrelationKey = $"manual-{caseId:N}"
        };
        var session = GuidedIntakeSession.Open(caseId, envelope, TimeSpan.FromMinutes(10));
        var triageCase = TriageCase.Open(caseId, occurredAtUtc, TimeSpan.FromSeconds(30));
        Assert.True(triageCase.TryEnterManualReview(occurredAtUtc.AddSeconds(30)));
        database.GuidedIntakeSessions.Add(session);
        database.TriageCases.Add(triageCase);
        await database.SaveChangesAsync();
        return caseId;
    }

    private async Task<IncidentMatchOutcome?> MatchAsync(
        Guid caseId,
        DateTimeOffset now,
        IPilotReporterIdentityRegistry? registry = null)
    {
        await using var database = Database();
        return await new IncidentPersistence(database, new FixedTimeProvider(now), registry)
            .CreateOrMatchAsync(caseId);
    }

    private First10DbContext Database() => new(
        PersistenceConfiguration.CreateOptions(postgres.ConnectionString));

    private static Guid ReportId(string payloadJson) =>
        System.Text.Json.JsonDocument.Parse(payloadJson).RootElement.GetProperty("reportId").GetGuid();

    private static double LatitudeOffset(double latitude, double metres) =>
        latitude + metres / IncidentMatchPolicy.EarthRadiusMeters * 180 / Math.PI;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PassThroughVerifiedRegistry : IPilotReporterIdentityRegistry
    {
        public string ResolveVerifiedIdentityKey(string reporterIndependenceKey) =>
            $"verified:{reporterIndependenceKey}";
    }
}
