namespace First10.Modules.Incidents;

public sealed class IncidentCandidateLink
{
    private IncidentCandidateLink()
    {
    }

    private IncidentCandidateLink(
        Guid firstIncidentId,
        Guid secondIncidentId,
        string reason,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        FirstIncidentId = firstIncidentId.CompareTo(secondIncidentId) < 0 ? firstIncidentId : secondIncidentId;
        SecondIncidentId = firstIncidentId.CompareTo(secondIncidentId) < 0 ? secondIncidentId : firstIncidentId;
        Reason = reason;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid FirstIncidentId { get; private set; }

    public Guid SecondIncidentId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static IncidentCandidateLink Create(
        Guid firstIncidentId,
        Guid secondIncidentId,
        string reason,
        DateTimeOffset createdAtUtc)
    {
        if (firstIncidentId == Guid.Empty
            || secondIncidentId == Guid.Empty
            || firstIncidentId == secondIncidentId)
        {
            throw new ArgumentException("Two different incident IDs are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new IncidentCandidateLink(firstIncidentId, secondIncidentId, reason, createdAtUtc);
    }
}

public sealed record CreateOrMatchIncident(Guid TriageCaseId);

public sealed record ApplyLateIncidentLocation(
    Guid TriageCaseId,
    Guid EvidenceId,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    double Latitude,
    double Longitude,
    double Confidence);
