using First10.Modules.Incidents;

namespace First10.UnitTests.Incidents;

public sealed class CrewBriefingTests
{
    [Fact]
    public void DeterministicBriefingOrdersByOccurrenceAndKeepsSourceAndConflictReferences()
    {
        var at = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var later = IncidentMatchingTests.Candidate("report-1", "reporter-a", 6.8, 3.4, at.AddMinutes(4));
        var incident = Incident.Create(Guid.NewGuid(), later, at.AddMinutes(4).AddSeconds(2));
        var earlier = IncidentMatchingTests.Candidate("report-2", "reporter-b", 6.8001, 3.4, at) with
        {
            CasualtyMinimum = 4,
            CasualtyMaximum = 5,
            ReceivedAtUtc = at.AddMinutes(4).AddSeconds(10)
        };
        Assert.True(incident.TryAttach(earlier, at.AddMinutes(4).AddSeconds(10)));

        var briefing = CrewBriefingRenderer.RenderDeterministic(CrewBriefingProjection.From(incident));

        Assert.False(briefing.UsedAiOrdering);
        Assert.Equal(earlier.ReportId, briefing.OrderedClaims[0].SourceReportId);
        Assert.Equal(later.ReportId, briefing.OrderedClaims[1].SourceReportId);
        Assert.Contains($"source report {earlier.ReportId:N}", briefing.Text, StringComparison.Ordinal);
        Assert.Contains("dispatcher resolution required", briefing.Text, StringComparison.Ordinal);
        Assert.Contains("occurred 2026-07-20T12:00:00.0000000+00:00", briefing.Text, StringComparison.Ordinal);
        Assert.Contains("received 2026-07-20T12:04:10.0000000+00:00", briefing.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownDuplicateOrMissingAiClaimIdsAreRejected()
    {
        var at = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var incident = Incident.Create(
            Guid.NewGuid(),
            IncidentMatchingTests.Candidate("report-1", "reporter-a", 6.8, 3.4, at),
            at);
        var projection = CrewBriefingProjection.From(incident);

        Assert.Equal("invalid_claim_selection", Assert.Throws<CrewBriefingValidationException>(() =>
            CrewBriefingRenderer.Render(projection, ["report:fabricated"], true)).Code);
        Assert.Equal("invalid_claim_selection", Assert.Throws<CrewBriefingValidationException>(() =>
            CrewBriefingRenderer.Render(projection, [], true)).Code);
    }
}
