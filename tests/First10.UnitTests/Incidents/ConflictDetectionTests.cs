using First10.Modules.Incidents;

namespace First10.UnitTests.Incidents;

public sealed class ConflictDetectionTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ContradictoryCasualtyClaimsRemainSourceLinkedAndVisible()
    {
        var first = IncidentMatchingTests.Candidate("report-1", "reporter-a", 6.8, 3.4, OccurredAt);
        var incident = Incident.Create(Guid.NewGuid(), first, OccurredAt);
        var second = IncidentMatchingTests.Candidate(
            "report-2",
            "reporter-b",
            6.8001,
            3.4,
            OccurredAt.AddMinutes(1)) with
        {
            CasualtyMinimum = 4,
            CasualtyMaximum = 5
        };

        Assert.True(incident.TryAttach(second, OccurredAt.AddMinutes(1)));

        var conflict = Assert.Single(incident.Conflicts, x => x.Field == IncidentConflictField.CasualtyEstimate);
        Assert.Equal(first.ReportId, conflict.LeftReportId);
        Assert.Equal(second.ReportId, conflict.RightReportId);
        Assert.Equal("1-2", conflict.LeftValue);
        Assert.Equal("4-5", conflict.RightValue);
        Assert.False(conflict.IsResolved);
        Assert.Equal(2, incident.SourceReports.Count);
    }

    [Fact]
    public void IncidentTypeAndSeverityConflictsRequireExplicitResolution()
    {
        var first = IncidentMatchingTests.Candidate("report-1", "reporter-a", 6.8, 3.4, OccurredAt);
        var incident = Incident.Create(Guid.NewGuid(), first, OccurredAt);
        var second = IncidentMatchingTests.Candidate(
            "report-2",
            "reporter-b",
            6.8001,
            3.4,
            OccurredAt.AddMinutes(1)) with
        {
            IncidentType = IncidentKind.RoadTrafficCollisionWithFire,
            Severity = IncidentSeverity.Critical
        };

        Assert.True(incident.TryAttach(second, OccurredAt.AddMinutes(1)));
        Assert.Contains(incident.Conflicts, x => x.Field == IncidentConflictField.IncidentType);
        Assert.Contains(incident.Conflicts, x => x.Field == IncidentConflictField.Severity);
        Assert.True(incident.HasUnresolvedConflicts);

        foreach (var conflict in incident.Conflicts)
        {
            Assert.True(incident.TryResolveConflict(
                conflict.Id,
                first.ReportId,
                "dispatcher-1",
                OccurredAt.AddMinutes(2)));
            Assert.False(incident.TryResolveConflict(
                conflict.Id,
                second.ReportId,
                "dispatcher-2",
                OccurredAt.AddMinutes(3)));
        }

        Assert.False(incident.HasUnresolvedConflicts);
    }

    [Fact]
    public void VictimSceneAndDirectionClaimsRemainSourceLinked()
    {
        var first = IncidentMatchingTests.Candidate("report-1", "reporter-a", 6.8, 3.4, OccurredAt);
        var incident = Incident.Create(Guid.NewGuid(), first, OccurredAt);
        var second = IncidentMatchingTests.Candidate(
            "report-2",
            "reporter-b",
            6.8001,
            3.4,
            OccurredAt.AddMinutes(1)) with
        {
            VictimState = ObservedVictimState.Unresponsive,
            SceneState = ObservedSceneState.FireOrSmoke,
            Direction = IncidentTravelDirection.LagosInbound
        };

        Assert.True(incident.TryAttach(second, OccurredAt.AddMinutes(1)));

        Assert.Contains(incident.Conflicts, x =>
            x.Field == IncidentConflictField.VictimState
            && x.LeftClaimId == first.ReportId
            && x.RightClaimId == second.ReportId);
        Assert.Contains(incident.Conflicts, x => x.Field == IncidentConflictField.SceneState);
        Assert.Contains(incident.Conflicts, x => x.Field == IncidentConflictField.TravelDirection);
    }

    [Fact]
    public void RelayObservationIsImmutableSourceLinkedAndConflictsWithContemporaneousClaim()
    {
        var first = IncidentMatchingTests.Candidate("report-1", "reporter-a", 6.8, 3.4, OccurredAt);
        var incident = Incident.Create(Guid.NewGuid(), first, OccurredAt);
        var observationId = Guid.NewGuid();
        var observation = IncidentObservation.Create(new AppendIncidentObservation(
            observationId,
            incident.Id,
            first.ReportId,
            ObservedVictimState.Unresponsive,
            ObservedSceneState.CrowdForming,
            "other carriageway",
            IncidentTravelDirection.LagosInbound,
            6.8006,
            3.4,
            OccurredAt.AddMinutes(2),
            OccurredAt.AddMinutes(2).AddSeconds(5),
            "transcript:relay-1"));

        Assert.True(incident.TryAttachObservation(observation));

        Assert.Same(observation, Assert.Single(incident.Observations));
        Assert.Contains(incident.Conflicts, x =>
            x.Field == IncidentConflictField.VictimState
            && x.RightClaimId == observationId
            && x.RightReportId == first.ReportId);
        Assert.Contains(incident.Conflicts, x => x.Field == IncidentConflictField.Location);
    }
}
