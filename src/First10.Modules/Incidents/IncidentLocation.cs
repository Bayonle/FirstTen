namespace First10.Modules.Incidents;

public sealed class IncidentLocation
{
    private IncidentLocation()
    {
    }

    private IncidentLocation(LocationClaim claim)
    {
        IncidentId = claim.IncidentId;
        Latitude = claim.Latitude;
        Longitude = claim.Longitude;
        Source = claim.Source;
        LastEventId = claim.EventId;
        Version = 1;
    }

    public Guid IncidentId { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public string Source { get; private set; } = string.Empty;

    public bool HasConflict { get; private set; }

    public Guid LastEventId { get; private set; }

    public int Version { get; private set; }

    public static IncidentLocation From(LocationClaim claim) => new(claim);

    public void Apply(LocationClaim claim)
    {
        HasConflict |= Latitude != claim.Latitude || Longitude != claim.Longitude;
        LastEventId = claim.EventId;
        Version++;
    }
}
