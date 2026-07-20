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
        ReceivedAtUtc = claim.OccurredAtUtc;
        PayloadJson = "{}";
    }

    public Guid EventId { get; private set; }

    public Guid IncidentId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Source { get; private set; } = string.Empty;

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public string PayloadJson { get; private set; } = "{}";

    public static IncidentTimelineEvent From(LocationClaim claim) => new(claim);

    public static IncidentTimelineEvent ReportLinked(IncidentSourceReport report) => new()
    {
        EventId = Guid.NewGuid(),
        IncidentId = report.IncidentId,
        EventType = "report-linked",
        Source = $"triage:{report.ReportId:N}",
        Latitude = report.Latitude,
        Longitude = report.Longitude,
        OccurredAtUtc = report.OccurredAtUtc,
        ReceivedAtUtc = report.ReceivedAtUtc,
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            reportId = report.ReportId,
            incidentType = report.IncidentType.ToString(),
            severity = report.Severity.ToString(),
            casualtyMinimum = report.CasualtyMinimum,
            casualtyMaximum = report.CasualtyMaximum,
            victimState = report.VictimState.ToString(),
            sceneState = report.SceneState.ToString(),
            report.LocationDescription,
            direction = report.Direction.ToString(),
            locationConfidence = report.LocationConfidence,
            evidenceReferences = report.EvidenceReferences.Split(',', StringSplitOptions.RemoveEmptyEntries)
        })
    };

    public static IncidentTimelineEvent StateChanged(
        Guid incidentId,
        string eventType,
        string source,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset receivedAtUtc,
        object payload) => new()
        {
            EventId = Guid.NewGuid(),
            IncidentId = incidentId,
            EventType = eventType,
            Source = source,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = receivedAtUtc,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload)
        };

    public static IncidentTimelineEvent StateChangedWithId(
        Guid eventId,
        Guid incidentId,
        string eventType,
        string source,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset receivedAtUtc,
        object payload) => new()
        {
            EventId = eventId,
            IncidentId = incidentId,
            EventType = eventType,
            Source = source,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = receivedAtUtc,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload)
        };

    public static IncidentTimelineEvent LateLocation(
        Guid evidenceId,
        IncidentSourceReport report,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset receivedAtUtc) => new()
        {
            EventId = evidenceId,
            IncidentId = report.IncidentId,
            EventType = "late-location-evidence",
            Source = $"triage:{report.ReportId:N}",
            Latitude = report.Latitude,
            Longitude = report.Longitude,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = receivedAtUtc,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                reportId = report.ReportId,
                evidenceReference = $"pin:{evidenceId:N}",
                report.LocationConfidence
            })
        };

    public static IncidentTimelineEvent ObservationAdded(IncidentObservation observation) => new()
    {
        EventId = observation.Id,
        IncidentId = observation.IncidentId,
        EventType = "relay-observation-added",
        Source = $"report:{observation.SourceReportId:N}",
        Latitude = observation.Latitude,
        Longitude = observation.Longitude,
        OccurredAtUtc = observation.OccurredAtUtc,
        ReceivedAtUtc = observation.ReceivedAtUtc,
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            observationId = observation.Id,
            sourceReportId = observation.SourceReportId,
            victimState = observation.VictimState.ToString(),
            sceneState = observation.SceneState.ToString(),
            observation.LocationDescription,
            direction = observation.Direction.ToString(),
            observation.EvidenceReference
        })
    };
}
