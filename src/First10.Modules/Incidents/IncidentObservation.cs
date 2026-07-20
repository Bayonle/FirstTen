namespace First10.Modules.Incidents;

public sealed class IncidentObservation
{
    private IncidentObservation()
    {
    }

    private IncidentObservation(
        Guid id,
        Guid incidentId,
        Guid sourceReportId,
        ObservedVictimState victimState,
        ObservedSceneState sceneState,
        string? locationDescription,
        IncidentTravelDirection direction,
        double? latitude,
        double? longitude,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset receivedAtUtc,
        string evidenceReference)
    {
        Id = id;
        IncidentId = incidentId;
        SourceReportId = sourceReportId;
        VictimState = victimState;
        SceneState = sceneState;
        LocationDescription = locationDescription;
        Direction = direction;
        Latitude = latitude;
        Longitude = longitude;
        OccurredAtUtc = occurredAtUtc;
        ReceivedAtUtc = receivedAtUtc;
        EvidenceReference = evidenceReference;
    }

    public Guid Id { get; private set; }

    public Guid IncidentId { get; private set; }

    public Guid SourceReportId { get; private set; }

    public ObservedVictimState VictimState { get; private set; }

    public ObservedSceneState SceneState { get; private set; }

    public string? LocationDescription { get; private set; }

    public IncidentTravelDirection Direction { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public string EvidenceReference { get; private set; } = string.Empty;

    public static IncidentObservation Create(AppendIncidentObservation command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ObservationId == Guid.Empty
            || command.IncidentId == Guid.Empty
            || command.SourceReportId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.EvidenceReference)
            || command.ReceivedAtUtc < command.OccurredAtUtc
            || command.Latitude.HasValue != command.Longitude.HasValue
            || command.Latitude is < -90 or > 90
            || command.Longitude is < -180 or > 180
            || command.VictimState == ObservedVictimState.Unknown
               && command.SceneState == ObservedSceneState.Unknown
               && string.IsNullOrWhiteSpace(command.LocationDescription)
               && command.Direction == IncidentTravelDirection.Unknown
               && !command.Latitude.HasValue)
        {
            throw new ArgumentException("A valid, source-linked observation is required.", nameof(command));
        }

        return new IncidentObservation(
            command.ObservationId,
            command.IncidentId,
            command.SourceReportId,
            command.VictimState,
            command.SceneState,
            string.IsNullOrWhiteSpace(command.LocationDescription) ? null : command.LocationDescription.Trim(),
            command.Direction,
            command.Latitude,
            command.Longitude,
            command.OccurredAtUtc,
            command.ReceivedAtUtc,
            command.EvidenceReference.Trim());
    }
}

public sealed record AppendIncidentObservation(
    Guid ObservationId,
    Guid IncidentId,
    Guid SourceReportId,
    ObservedVictimState VictimState,
    ObservedSceneState SceneState,
    string? LocationDescription,
    IncidentTravelDirection Direction,
    double? Latitude,
    double? Longitude,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    string EvidenceReference);
