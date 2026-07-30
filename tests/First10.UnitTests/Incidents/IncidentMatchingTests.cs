using First10.Modules.Incidents;

namespace First10.UnitTests.Incidents;

public sealed class IncidentMatchingTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IndependentReportsWithinDistanceAndTimeMergeAndAutoVerify()
    {
        var first = Candidate("report-1", "reporter-a", 6.8000, 3.4000, OccurredAt);
        var incident = Incident.Create(Guid.NewGuid(), first, OccurredAt.AddSeconds(2));
        var second = Candidate(
            "report-2",
            "reporter-b",
            LatitudeOffset(6.8000, 120),
            3.4000,
            OccurredAt.AddMinutes(4));

        Assert.Equal(IncidentMatchDecision.Qualifying, IncidentMatchPolicy.Evaluate(first, second).Decision);
        Assert.True(incident.TryAttach(second, OccurredAt.AddMinutes(4).AddSeconds(2)));
        Assert.Equal(IncidentVerificationStatus.AutoVerified, incident.VerificationStatus);
        Assert.Equal(2, incident.SourceReports.Count);
        Assert.Equal(2, incident.IndependentReporterCount);
    }

    [Fact]
    public void DistanceAndTimeBoundariesAreInclusiveAndOneUnitBeyondDoesNotMerge()
    {
        var origin = Candidate("origin", "reporter-a", 6.8000, 3.4000, OccurredAt);
        var atDistance = Candidate(
            "at-distance",
            "reporter-b",
            LatitudeOffset(6.8000, 200),
            3.4000,
            OccurredAt.AddMinutes(5));
        var beyondDistance = Candidate(
            "beyond-distance",
            "reporter-b",
            LatitudeOffset(6.8000, 201),
            3.4000,
            OccurredAt.AddMinutes(5));
        var beyondTime = atDistance with
        {
            ReportId = Guid.NewGuid(),
            OccurredAtUtc = OccurredAt.AddMinutes(5).AddMilliseconds(1)
        };

        var boundary = IncidentMatchPolicy.Evaluate(origin, atDistance);
        Assert.Equal(IncidentMatchDecision.Qualifying, boundary.Decision);
        Assert.InRange(boundary.DistanceMeters!.Value, 199.999, 200.001);
        Assert.Equal(IncidentMatchDecision.OutsideDistance, IncidentMatchPolicy.Evaluate(origin, beyondDistance).Decision);
        Assert.Equal(IncidentMatchDecision.OutsideTime, IncidentMatchPolicy.Evaluate(origin, beyondTime).Decision);
    }

    [Fact]
    public void SameReporterNeverCountsAsIndependentConfirmation()
    {
        var first = Candidate("report-1", "same-reporter", 6.8, 3.4, OccurredAt);
        var incident = Incident.Create(Guid.NewGuid(), first, OccurredAt);

        Assert.True(incident.TryAttach(
            Candidate("report-2", "same-reporter", 6.8001, 3.4, OccurredAt.AddMinutes(1)),
            OccurredAt.AddMinutes(1)));

        Assert.Equal(IncidentVerificationStatus.AwaitingConfirmation, incident.VerificationStatus);
        Assert.Equal(1, incident.IndependentReporterCount);
        Assert.Equal(2, incident.SourceReports.Count);
    }

    [Fact]
    public void DistinctUnverifiedContactsCorroborateButCannotAutoVerify()
    {
        var first = Candidate("report-1", "contact-a", 6.8, 3.4, OccurredAt, verified: false);
        var incident = Incident.Create(Guid.NewGuid(), first, OccurredAt);

        Assert.True(incident.TryAttach(
            Candidate("report-2", "contact-b", 6.8001, 3.4, OccurredAt.AddMinutes(1), verified: false),
            OccurredAt.AddMinutes(1)));

        Assert.Equal(2, incident.IndependentReporterCount);
        Assert.Equal(0, incident.VerifiedIndependentReporterCount);
        Assert.Equal(IncidentVerificationStatus.AwaitingConfirmation, incident.VerificationStatus);
    }

    [Fact]
    public void TwoAccountsMappedToSameVerifiedIdentityCannotAutoVerify()
    {
        var first = Candidate("report-1", "account-a", 6.8, 3.4, OccurredAt) with
        {
            VerifiedPilotIdentityKey = "pilot-person-1"
        };
        var incident = Incident.Create(Guid.NewGuid(), first, OccurredAt);
        var second = Candidate("report-2", "account-b", 6.8001, 3.4, OccurredAt.AddMinutes(1)) with
        {
            VerifiedPilotIdentityKey = "pilot-person-1"
        };

        Assert.True(incident.TryAttach(second, OccurredAt.AddMinutes(1)));
        Assert.Equal(2, incident.IndependentReporterCount);
        Assert.Equal(1, incident.VerifiedIndependentReporterCount);
        Assert.Equal(IncidentVerificationStatus.AwaitingConfirmation, incident.VerificationStatus);
    }

    [Fact]
    public void UncertainOrMissingCoordinatesRemainSeparateCandidates()
    {
        var reviewed = Candidate("report-1", "reporter-a", 6.8, 3.4, OccurredAt);
        var ambiguous = Candidate("report-2", "reporter-b", 6.8001, 3.4, OccurredAt.AddMinutes(1)) with
        {
            LocationConfidence = 0.6
        };
        var missing = Candidate("report-3", "reporter-c", null, null, OccurredAt.AddMinutes(1));

        Assert.Equal(IncidentMatchDecision.UncertainLocation, IncidentMatchPolicy.Evaluate(reviewed, ambiguous).Decision);
        Assert.Equal(IncidentMatchDecision.UncertainLocation, IncidentMatchPolicy.Evaluate(reviewed, missing).Decision);
    }

    [Fact]
    public void SingletonStartsSixtySecondReviewTimer()
    {
        var receivedAt = OccurredAt.AddSeconds(3);
        var incident = Incident.Create(
            Guid.NewGuid(),
            Candidate("report-1", "reporter-a", 6.8, 3.4, OccurredAt),
            receivedAt);

        Assert.Equal(receivedAt.AddSeconds(60), incident.ReviewDueAtUtc);
        Assert.Equal(IncidentVerificationStatus.AwaitingConfirmation, incident.VerificationStatus);
    }

    [Fact]
    public void LateAcceptedLocationAddsEvidenceWithoutChangingReportTiming()
    {
        var candidate = Candidate("report-1", "reporter-a", null, null, OccurredAt);
        var incident = Incident.Create(Guid.NewGuid(), candidate, OccurredAt.AddSeconds(2));
        var report = Assert.Single(incident.SourceReports);

        Assert.True(report.TryApplyLateLocation(6.8, 3.4, 1, "pin:late"));

        Assert.Equal(6.8, report.Latitude);
        Assert.Equal(OccurredAt, report.OccurredAtUtc);
        Assert.Equal(OccurredAt.AddSeconds(2), report.ReceivedAtUtc);
        Assert.Contains("triage:report-1", report.EvidenceReferences, StringComparison.Ordinal);
        Assert.Contains("pin:late", report.EvidenceReferences, StringComparison.Ordinal);
    }

    internal static IncidentReportCandidate Candidate(
        string seed,
        string reporterKey,
        double? latitude,
        double? longitude,
        DateTimeOffset occurredAtUtc,
        bool verified = true) => new(
        Guid.Parse(seed switch
        {
            "report-1" => "10000000-0000-0000-0000-000000000001",
            "report-2" => "10000000-0000-0000-0000-000000000002",
            "report-3" => "10000000-0000-0000-0000-000000000003",
            "origin" => "10000000-0000-0000-0000-000000000004",
            "at-distance" => "10000000-0000-0000-0000-000000000005",
            "beyond-distance" => "10000000-0000-0000-0000-000000000006",
            _ => "10000000-0000-0000-0000-000000000007"
        }),
        reporterKey,
        occurredAtUtc,
        occurredAtUtc.AddSeconds(2),
        latitude,
        longitude,
        latitude.HasValue ? 0.95 : null,
        IncidentKind.RoadTrafficCollision,
        IncidentSeverity.High,
        1,
        2,
        [$"triage:{seed}"],
        verified ? $"verified:{reporterKey}" : null,
        "reviewed-landmark",
        IncidentTravelDirection.IbadanInbound,
        ObservedVictimState.Responsive,
        ObservedSceneState.TrafficHazard);

    private static double LatitudeOffset(double latitude, double metres) =>
        latitude + metres / IncidentMatchPolicy.EarthRadiusMeters * 180 / Math.PI;
}
