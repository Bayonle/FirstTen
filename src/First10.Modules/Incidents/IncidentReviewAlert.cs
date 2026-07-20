namespace First10.Modules.Incidents;

public enum IncidentReviewAlertStatus
{
    Open = 1,
    Resolved = 2
}

public enum IncidentReviewPriority
{
    High = 1
}

public sealed class IncidentReviewAlert
{
    private IncidentReviewAlert()
    {
    }

    private IncidentReviewAlert(Guid incidentId, DateTimeOffset raisedAtUtc)
    {
        Id = Guid.NewGuid();
        IncidentId = incidentId;
        RaisedAtUtc = raisedAtUtc;
        Status = IncidentReviewAlertStatus.Open;
        Priority = IncidentReviewPriority.High;
        Reason = "singleton_unconfirmed_after_60_seconds";
    }

    public Guid Id { get; private set; }

    public Guid IncidentId { get; private set; }

    public DateTimeOffset RaisedAtUtc { get; private set; }

    public IncidentReviewAlertStatus Status { get; private set; }

    public IncidentReviewPriority Priority { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public static IncidentReviewAlert Raise(Guid incidentId, DateTimeOffset raisedAtUtc)
    {
        if (incidentId == Guid.Empty)
        {
            throw new ArgumentException("Incident ID is required.", nameof(incidentId));
        }

        return new IncidentReviewAlert(incidentId, raisedAtUtc);
    }

    public bool TryResolve(DateTimeOffset resolvedAtUtc)
    {
        if (Status == IncidentReviewAlertStatus.Resolved)
        {
            return false;
        }

        Status = IncidentReviewAlertStatus.Resolved;
        ResolvedAtUtc = resolvedAtUtc;
        return true;
    }
}

public sealed record ReviewSingletonIncident(Guid IncidentId);
