namespace First10.Modules.Incidents;

public sealed class IncidentTimelineEvent
{
    private IncidentTimelineEvent()
    {
    }

    private IncidentTimelineEvent(LocationClaim claim)
    {
        EventId = claim.EventId;
        IncidentId = claim.IncidentId;
        EventType = "location-claimed";
        Source = claim.Source;
        Latitude = claim.Latitude;
        Longitude = claim.Longitude;
        OccurredAtUtc = claim.OccurredAtUtc;
    }

    public Guid EventId { get; private set; }

    public Guid IncidentId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Source { get; private set; } = string.Empty;

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static IncidentTimelineEvent From(LocationClaim claim) => new(claim);
}
