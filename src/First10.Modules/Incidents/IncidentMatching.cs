namespace First10.Modules.Incidents;

public enum IncidentKind
{
    Unknown = 0,
    RoadTrafficCollision = 1,
    RoadTrafficCollisionWithFire = 2,
    OkadaCollision = 3
}

public enum IncidentSeverity
{
    Unknown = 0,
    Moderate = 1,
    High = 2,
    Critical = 3
}

public enum IncidentTravelDirection
{
    Unknown = 0,
    LagosInbound = 1,
    IbadanInbound = 2
}

public enum ObservedVictimState
{
    Unknown = 0,
    Responsive = 1,
    Unresponsive = 2,
    Moving = 3,
    Trapped = 4
}

public enum ObservedSceneState
{
    Unknown = 0,
    TrafficHazard = 1,
    FireOrSmoke = 2,
    CrowdForming = 3,
    SceneClearing = 4
}

public enum IncidentMatchDecision
{
    Qualifying = 1,
    UncertainLocation = 2,
    OutsideDistance = 3,
    OutsideTime = 4
}

public sealed record IncidentMatchResult(
    IncidentMatchDecision Decision,
    double? DistanceMeters,
    TimeSpan TimeDifference);

public sealed record IncidentReportCandidate(
    Guid ReportId,
    string ReporterIndependenceKey,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    double? Latitude,
    double? Longitude,
    double? LocationConfidence,
    IncidentKind IncidentType,
    IncidentSeverity Severity,
    int? CasualtyMinimum,
    int? CasualtyMaximum,
    IReadOnlyList<string> EvidenceReferences,
    string? VerifiedPilotIdentityKey = null,
    string? LocationDescription = null,
    IncidentTravelDirection Direction = IncidentTravelDirection.Unknown,
    ObservedVictimState VictimState = ObservedVictimState.Unknown,
    ObservedSceneState SceneState = ObservedSceneState.Unknown);

public static class IncidentMatchPolicy
{
    public const double EarthRadiusMeters = 6_371_008.8;
    public const double MaximumDistanceMeters = 200;
    public static readonly TimeSpan MaximumTimeDifference = TimeSpan.FromMinutes(5);
    public const double MinimumLocationConfidence = 0.85;

    public static IncidentMatchResult Evaluate(
        IncidentReportCandidate first,
        IncidentReportCandidate second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var timeDifference = (first.OccurredAtUtc - second.OccurredAtUtc).Duration();
        if (timeDifference > MaximumTimeDifference)
        {
            return new IncidentMatchResult(IncidentMatchDecision.OutsideTime, null, timeDifference);
        }

        if (!HasAcceptedLocation(first) || !HasAcceptedLocation(second))
        {
            return new IncidentMatchResult(IncidentMatchDecision.UncertainLocation, null, timeDifference);
        }

        var distance = DistanceMeters(
            first.Latitude!.Value,
            first.Longitude!.Value,
            second.Latitude!.Value,
            second.Longitude!.Value);
        return new IncidentMatchResult(
            distance <= MaximumDistanceMeters + 1e-6
                ? IncidentMatchDecision.Qualifying
                : IncidentMatchDecision.OutsideDistance,
            distance,
            timeDifference);
    }

    public static double DistanceMeters(
        double firstLatitude,
        double firstLongitude,
        double secondLatitude,
        double secondLongitude)
    {
        var firstLatitudeRadians = DegreesToRadians(firstLatitude);
        var secondLatitudeRadians = DegreesToRadians(secondLatitude);
        var latitudeDelta = secondLatitudeRadians - firstLatitudeRadians;
        var longitudeDelta = DegreesToRadians(secondLongitude - firstLongitude);
        var haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
            + Math.Cos(firstLatitudeRadians)
            * Math.Cos(secondLatitudeRadians)
            * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(haversine)));
    }

    private static bool HasAcceptedLocation(IncidentReportCandidate candidate) =>
        candidate.Latitude is >= -90 and <= 90
        && candidate.Longitude is >= -180 and <= 180
        && candidate.LocationConfidence >= MinimumLocationConfidence;

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
