using System.Globalization;

namespace First10.Modules.Incidents;

public sealed record CrewBriefingClaim(
    string ClaimId,
    Guid? SourceReportId,
    string Text,
    IReadOnlyList<string> EvidenceReferences,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc);

public sealed record CrewBriefingProjection(
    Guid IncidentId,
    IncidentVerificationStatus VerificationStatus,
    IReadOnlyList<CrewBriefingClaim> Claims)
{
    public static CrewBriefingProjection From(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        var reports = incident.SourceReports
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.ReceivedAtUtc)
            .ThenBy(x => x.ReportId)
            .ToArray();
        var claims = reports
            .Select(report => new CrewBriefingClaim(
                $"report:{report.ReportId:N}",
                report.ReportId,
                FormatReport(report),
                report.EvidenceReferences.Split(',', StringSplitOptions.RemoveEmptyEntries),
                report.OccurredAtUtc,
                report.ReceivedAtUtc))
            .ToList();
        claims.AddRange(incident.Observations.Select(observation => new CrewBriefingClaim(
            $"observation:{observation.Id:N}",
            observation.SourceReportId,
            FormatObservation(observation),
            [observation.EvidenceReference],
            observation.OccurredAtUtc,
            observation.ReceivedAtUtc)));
        foreach (var conflict in incident.Conflicts.OrderBy(x => x.Id))
        {
            var sourceClaims = claims
                .Where(x => x.ClaimId.EndsWith(ClaimId(conflict.LeftClaimId), StringComparison.Ordinal)
                            || x.ClaimId.EndsWith(ClaimId(conflict.RightClaimId), StringComparison.Ordinal))
                .ToArray();
            claims.Add(new CrewBriefingClaim(
                $"conflict:{conflict.Id:N}",
                null,
                $"Contradiction in {conflict.Field}: {conflict.LeftValue} versus {conflict.RightValue}; "
                + (conflict.IsResolved
                    ? $"dispatcher selected report {conflict.SelectedReportId:N}."
                    : "dispatcher resolution required."),
                sourceClaims.SelectMany(x => x.EvidenceReferences)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                sourceClaims.Max(x => x.OccurredAtUtc),
                sourceClaims.Max(x => x.ReceivedAtUtc)));
        }

        return new CrewBriefingProjection(
            incident.Id,
            incident.VerificationStatus,
            claims.OrderBy(x => x.OccurredAtUtc)
                .ThenBy(x => x.ReceivedAtUtc)
                .ThenBy(x => x.ClaimId, StringComparer.Ordinal)
                .ToArray());
    }

    private static string ClaimId(Guid id) =>
        // Initial report claims use their report ID; relay observations use their observation ID.
        // Probe both forms at the call site by normalizing to the suffix when matching.
        id.ToString("N");

    private static string FormatObservation(IncidentObservation observation)
    {
        var location = observation.Latitude.HasValue && observation.Longitude.HasValue
            ? $"{observation.Latitude.Value:F6},{observation.Longitude.Value:F6}"
            : observation.LocationDescription ?? "unresolved";
        return $"Relay observation: victim {observation.VictimState}; scene {observation.SceneState}; "
            + $"location {location}; direction {observation.Direction}.";
    }

    private static string FormatReport(IncidentSourceReport report)
    {
        var casualties = report.CasualtyMinimum == report.CasualtyMaximum
            ? report.CasualtyMinimum?.ToString(CultureInfo.InvariantCulture) ?? "unknown"
            : $"{report.CasualtyMinimum?.ToString(CultureInfo.InvariantCulture) ?? "?"}-{report.CasualtyMaximum?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
        var location = report.Latitude.HasValue && report.Longitude.HasValue
            ? $"{report.Latitude.Value:F6},{report.Longitude.Value:F6} (confidence {report.LocationConfidence:F2})"
            : "unresolved";
        return $"{report.IncidentType}; severity {report.Severity}; casualties {casualties}; "
            + $"victim {report.VictimState}; scene {report.SceneState}; location {location}; "
            + $"landmark {report.LocationDescription ?? "unresolved"}; direction {report.Direction}; "
            + $"occurred {report.OccurredAtUtc:O}; received {report.ReceivedAtUtc:O}.";
    }
}

public sealed record CrewBriefing(
    Guid IncidentId,
    string Text,
    IReadOnlyList<CrewBriefingClaim> OrderedClaims,
    bool UsedAiOrdering);

public static class CrewBriefingRenderer
{
    public static CrewBriefing RenderDeterministic(CrewBriefingProjection projection) =>
        Render(projection, projection.Claims.Select(x => x.ClaimId).ToArray(), false);

    public static CrewBriefing Render(
        CrewBriefingProjection projection,
        IReadOnlyList<string> orderedClaimIds,
        bool usedAiOrdering)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(orderedClaimIds);
        var byId = projection.Claims.ToDictionary(x => x.ClaimId, StringComparer.Ordinal);
        if (orderedClaimIds.Count != byId.Count
            || orderedClaimIds.Distinct(StringComparer.Ordinal).Count() != byId.Count
            || orderedClaimIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new CrewBriefingValidationException("invalid_claim_selection");
        }

        var ordered = orderedClaimIds.Select(id => byId[id]).ToArray();
        var lines = new List<string>
        {
            $"Incident {projection.IncidentId:N} — {projection.VerificationStatus}"
        };
        lines.AddRange(ordered.Select(claim =>
        {
            var source = claim.SourceReportId.HasValue
                ? $"source report {claim.SourceReportId:N}"
                : claim.ClaimId;
            var evidence = claim.EvidenceReferences.Count == 0
                ? "no evidence reference"
                : string.Join(", ", claim.EvidenceReferences);
            return $"[{source}; evidence {evidence}] {claim.Text}";
        }));
        return new CrewBriefing(projection.IncidentId, string.Join(Environment.NewLine, lines), ordered, usedAiOrdering);
    }
}

public sealed class CrewBriefingValidationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
