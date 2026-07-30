using First10.Modules.Intake.Triage;

namespace First10.UnitTests.Intake;

public sealed class TriagePolicyTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DeadlineRaisesManualReviewOnceAndLateAiCannotBecomeAuthoritative()
    {
        var triage = TriageCase.Open(Guid.NewGuid(), StartedAt);
        var originalVersion = triage.AuthoritativeVersion;

        Assert.True(triage.TryStartProcessing(StartedAt.AddSeconds(5)));
        Assert.True(triage.TryEnterManualReview(StartedAt.AddSeconds(30)));
        Assert.False(triage.TryEnterManualReview(StartedAt.AddSeconds(31)));

        Assert.False(triage.TryApplyAssessment(
            ValidResult(),
            "gpt-5.6-luna/reasoning-low/schema-v1",
            StartedAt.AddSeconds(31),
            originalVersion));
        Assert.Equal(TriageStatus.ManualReview, triage.Status);
        Assert.Null(triage.AuthoritativeAssessmentId);
        Assert.Single(triage.Assessments);
        Assert.False(triage.Assessments.Single().IsAuthoritative);
    }

    [Fact]
    public void DispatcherChangeMakesAnInFlightAiResultLateEvidence()
    {
        var triage = TriageCase.Open(Guid.NewGuid(), StartedAt);
        var versionAtRequest = triage.AuthoritativeVersion;
        Assert.True(triage.TryStartProcessing(StartedAt.AddSeconds(2)));

        triage.RecordDispatcherChange();

        Assert.False(triage.TryApplyAssessment(
            ValidResult(),
            "gpt-5.6-luna/reasoning-low/schema-v1",
            StartedAt.AddSeconds(10),
            versionAtRequest));
        Assert.False(triage.Assessments.Single().IsAuthoritative);
    }

    [Fact]
    public void InvalidCountsEvidenceAndDisabledGuidanceAreRejected()
    {
        var enabled = new HashSet<GuidanceCategory> { GuidanceCategory.RoadTrafficCollision };
        var evidence = new HashSet<string> { "transcript:voice-1" };

        Assert.Equal("implausible_casualty_range", Assert.Throws<TriageValidationException>(() =>
            TriagePolicy.Validate(ValidResult() with { CasualtyMinimum = 4, CasualtyMaximum = 2 }, enabled, evidence)).Code);
        Assert.Equal("invalid_evidence_reference", Assert.Throws<TriageValidationException>(() =>
            TriagePolicy.Validate(ValidResult() with
            {
                EvidenceReferences = [new TriageEvidenceReference("made-up", TriageEvidenceKind.Transcript)]
            }, enabled, evidence)).Code);
        Assert.Equal("disabled_guidance_category", Assert.Throws<TriageValidationException>(() =>
            TriagePolicy.Validate(ValidResult() with
            {
                GuidanceCategory = GuidanceCategory.RoadTrafficCollisionWithFire
            }, enabled, evidence)).Code);
    }

    [Fact]
    public void ReviewedUniqueLandmarkResolvesButAmbiguousOrUnreviewedDataDoesNot()
    {
        var landmarks = new[]
        {
            new CorridorLandmark(
                "mowe-toll-gate",
                "Mowe Toll Gate",
                ["mowe toll gate", "toll gate"],
                6.808,
                3.439,
                TravelDirection.IbadanInbound,
                true),
            new CorridorLandmark(
                "pending-place",
                "Pending Place",
                ["pending place"],
                6.81,
                3.44,
                TravelDirection.Unknown,
                false)
        };

        var resolved = CorridorLocationPolicy.Resolve(
            "Mowe inbound, near the toll gate",
            null,
            null,
            "transcript:voice-1",
            landmarks);

        Assert.NotNull(resolved);
        Assert.Equal("mowe-toll-gate", resolved.LandmarkId);
        Assert.Equal(TravelDirection.IbadanInbound, resolved.Direction);
        Assert.Null(CorridorLocationPolicy.Resolve(
            "pending place",
            null,
            null,
            "transcript:voice-1",
            landmarks));
        Assert.True(CorridorLocationPolicy.Resolve(
            null,
            6.8,
            3.4,
            "pin:location-1",
            landmarks)!.FromPin);
    }

    private static StructuredTriage ValidResult() => new(
        IncidentType.RoadTrafficCollision,
        SeverityTier.High,
        1,
        2,
        ReportedLanguage.NigerianPidgin,
        "Mowe inbound, near the toll gate",
        TriageUncertainty.Medium,
        [new TriageEvidenceReference("transcript:voice-1", TriageEvidenceKind.Transcript)],
        GuidanceCategory.RoadTrafficCollision);
}
