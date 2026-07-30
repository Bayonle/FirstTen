namespace First10.Modules.Operations;

public sealed record ActivationGateStatus(
    bool IsOpen,
    IReadOnlyList<string> BlockingReasons,
    DateTimeOffset EvaluatedAtUtc);

public sealed record OperationalHealth(
    bool DatabaseAvailable,
    bool AuditChainValid,
    int OpenManualReviewAlerts,
    int FailedOrUnknownDeliveries,
    DateTimeOffset EvaluatedAtUtc);

public enum PilotGateEvidenceType
{
    FrscApproval = 1,
    ClinicalLibraryApproval = 2,
    MetaProductionAccess = 3,
    LegalDpiaApproval = 4,
    DataProtectionApproval = 5,
    RetentionApproval = 6,
    PrivacyBenchmarkPassed = 7,
    AiBenchmarkPassed = 8,
    RecoveryDrillPassed = 9,
    RestoreTestPassed = 10,
    CorridorExercisePassed = 11
}

public enum PilotGateEvidenceDecision
{
    Approved = 1,
    Revoked = 2
}

public sealed class PilotGateEvidence
{
    private PilotGateEvidence()
    {
    }

    public Guid Id { get; private set; }
    public PilotGateEvidenceType Type { get; private set; }
    public PilotGateEvidenceDecision Decision { get; private set; }
    public string EvidenceReference { get; private set; } = string.Empty;
    public string RecordedBy { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; private set; }

    public static PilotGateEvidence Record(
        Guid id,
        PilotGateEvidenceType type,
        PilotGateEvidenceDecision decision,
        string evidenceReference,
        string recordedBy,
        DateTimeOffset recordedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Evidence record ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordedBy);
        return new PilotGateEvidence
        {
            Id = id,
            Type = type,
            Decision = decision,
            EvidenceReference = evidenceReference.Trim(),
            RecordedBy = recordedBy.Trim(),
            RecordedAtUtc = recordedAtUtc
        };
    }
}
