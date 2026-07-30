namespace First10.Modules.Incidents;

public sealed record LocationClaim(
    Guid EventId,
    Guid IncidentId,
    string Source,
    double Latitude,
    double Longitude,
    DateTimeOffset OccurredAtUtc);
