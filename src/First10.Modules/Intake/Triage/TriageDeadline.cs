namespace First10.Modules.Intake.Triage;

public sealed record EnforceTriageDeadline(Guid TriageCaseId);

public enum ManualTriageAlertStatus
{
    Pending = 1,
    Acknowledged = 2
}

public sealed class ManualTriageAlert
{
    private ManualTriageAlert()
    {
    }

    private ManualTriageAlert(Guid triageCaseId, DateTimeOffset createdAtUtc)
    {
        Id = triageCaseId;
        TriageCaseId = triageCaseId;
        CreatedAtUtc = createdAtUtc;
        Priority = 1;
        Status = ManualTriageAlertStatus.Pending;
        Message = "Manual triage required. Review the report now; FRSC emergency line: 122.";
    }

    public Guid Id { get; private set; }

    public Guid TriageCaseId { get; private set; }

    public int Priority { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public ManualTriageAlertStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }

    public static ManualTriageAlert Create(Guid triageCaseId, DateTimeOffset createdAtUtc) =>
        new(triageCaseId, createdAtUtc);

    public bool TryAcknowledge(DateTimeOffset acknowledgedAtUtc)
    {
        if (Status == ManualTriageAlertStatus.Acknowledged)
        {
            return false;
        }

        Status = ManualTriageAlertStatus.Acknowledged;
        AcknowledgedAtUtc = acknowledgedAtUtc;
        return true;
    }
}
