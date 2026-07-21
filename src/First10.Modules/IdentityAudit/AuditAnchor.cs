namespace First10.Modules.IdentityAudit;

public sealed class AuditAnchor
{
    private AuditAnchor()
    {
    }

    public Guid Id { get; private set; }
    public long LastSequence { get; private set; }
    public string LastHash { get; private set; } = string.Empty;
    public string ExternalReference { get; private set; } = string.Empty;
    public string RecordedBy { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public static AuditAnchor Create(
        Guid id,
        long lastSequence,
        string lastHash,
        string externalReference,
        string recordedBy,
        DateTimeOffset recordedAtUtc)
    {
        if (id == Guid.Empty || lastSequence <= 0 || lastHash.Length != 64 || lastHash.Any(x => !Uri.IsHexDigit(x)))
        {
            throw new ArgumentException("A valid anchor ID, sequence, and SHA-256 audit hash are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(externalReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedBy);
        return new AuditAnchor
        {
            Id = id,
            LastSequence = lastSequence,
            LastHash = lastHash.ToLowerInvariant(),
            ExternalReference = externalReference.Trim(),
            RecordedBy = recordedBy.Trim(),
            RecordedAtUtc = recordedAtUtc
        };
    }
}
