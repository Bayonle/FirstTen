namespace First10.Modules.Intake;

public enum IntakeRecoveryStatus
{
    Open = 1,
    Resolved = 2
}

public sealed class IntakeRecoveryItem
{
    private IntakeRecoveryItem()
    {
    }

    private IntakeRecoveryItem(
        Guid id,
        IntakeChannel channel,
        string providerMessageId,
        string reason,
        Guid? contactReference,
        string? reporterKey,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Channel = channel;
        ProviderMessageId = providerMessageId;
        Reason = reason;
        ContactReference = contactReference;
        ReporterKey = reporterKey;
        CreatedAtUtc = createdAtUtc;
        Status = IntakeRecoveryStatus.Open;
    }

    public Guid Id { get; private set; }

    public IntakeChannel Channel { get; private set; }

    public string ProviderMessageId { get; private set; } = string.Empty;

    public string Reason { get; private set; } = string.Empty;

    public Guid? ContactReference { get; private set; }

    public string? ReporterKey { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IntakeRecoveryStatus Status { get; private set; }

    public static IntakeRecoveryItem Create(
        IntakeChannel channel,
        string providerMessageId,
        string reason,
        DateTimeOffset createdAtUtc,
        Guid? contactReference = null,
        string? reporterKey = null) =>
        new(
            Guid.NewGuid(),
            channel,
            providerMessageId,
            reason,
            contactReference,
            reporterKey,
            createdAtUtc);
}
