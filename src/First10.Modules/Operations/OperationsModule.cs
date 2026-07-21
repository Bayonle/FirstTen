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
