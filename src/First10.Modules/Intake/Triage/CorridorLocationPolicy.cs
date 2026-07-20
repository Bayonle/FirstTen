using System.Text;

namespace First10.Modules.Intake.Triage;

public enum TravelDirection
{
    Unknown = 0,
    LagosInbound = 1,
    IbadanInbound = 2
}

public sealed record CorridorLandmark(
    string Id,
    string Name,
    IReadOnlyList<string> Aliases,
    double Latitude,
    double Longitude,
    TravelDirection Direction,
    bool IsReviewed);

public sealed record ResolvedCorridorLocation(
    double Latitude,
    double Longitude,
    string LandmarkId,
    TravelDirection Direction,
    double Confidence,
    string EvidenceReference,
    bool FromPin);

public static class CorridorLocationPolicy
{
    public static ResolvedCorridorLocation? Resolve(
        string? locationPhrase,
        double? pinLatitude,
        double? pinLongitude,
        string evidenceReference,
        IReadOnlyCollection<CorridorLandmark> landmarks,
        double minimumConfidence = 0.85)
    {
        if (pinLatitude is >= -90 and <= 90 && pinLongitude is >= -180 and <= 180)
        {
            return new ResolvedCorridorLocation(
                pinLatitude.Value,
                pinLongitude.Value,
                "reporter-pin",
                TravelDirection.Unknown,
                1,
                evidenceReference,
                true);
        }

        if (string.IsNullOrWhiteSpace(locationPhrase))
        {
            return null;
        }

        var normalized = Normalize(locationPhrase);
        var statedDirection = ParseDirection(normalized);
        var matches = landmarks
            .Where(x => x.IsReviewed)
            .Select(x => new
            {
                Landmark = x,
                Confidence = x.Aliases
                    .Append(x.Name)
                    .Select(Normalize)
                    .Where(alias => alias.Length >= 4 && normalized.Contains(alias, StringComparison.Ordinal))
                    .Select(alias => Math.Min(0.99, 0.85 + (alias.Length / 200d)))
                    .DefaultIfEmpty(0)
                    .Max()
            })
            .Where(x => x.Confidence >= minimumConfidence)
            .Where(x => statedDirection == TravelDirection.Unknown
                        || x.Landmark.Direction == TravelDirection.Unknown
                        || x.Landmark.Direction == statedDirection)
            .OrderByDescending(x => x.Confidence)
            .ToArray();

        if (matches.Length != 1)
        {
            return null;
        }

        var match = matches[0];
        return new ResolvedCorridorLocation(
            match.Landmark.Latitude,
            match.Landmark.Longitude,
            match.Landmark.Id,
            statedDirection == TravelDirection.Unknown ? match.Landmark.Direction : statedDirection,
            match.Confidence,
            evidenceReference,
            false);
    }

    private static TravelDirection ParseDirection(string normalized) =>
        normalized.Contains("lagos inbound", StringComparison.Ordinal)
        || normalized.Contains("towards lagos", StringComparison.Ordinal)
            ? TravelDirection.LagosInbound
            : normalized.Contains("ibadan inbound", StringComparison.Ordinal)
              || normalized.Contains("mowe inbound", StringComparison.Ordinal)
              || normalized.Contains("towards mowe", StringComparison.Ordinal)
                ? TravelDirection.IbadanInbound
                : TravelDirection.Unknown;

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
