namespace First10.Modules.Intake;

public sealed class ReporterContact
{
    private ReporterContact()
    {
    }

    private ReporterContact(
        Guid id,
        IntakeChannel channel,
        string reporterKey,
        string protectedDestination,
        string encryptionKeyVersion,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Channel = channel;
        ReporterKey = reporterKey;
        ProtectedDestination = protectedDestination;
        EncryptionKeyVersion = encryptionKeyVersion;
        CreatedAtUtc = createdAtUtc;
        LastUsedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public IntakeChannel Channel { get; private set; }

    public string ReporterKey { get; private set; } = string.Empty;

    public string ProtectedDestination { get; private set; } = string.Empty;

    public string EncryptionKeyVersion { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastUsedAtUtc { get; private set; }

    public static ReporterContact Create(
        IntakeChannel channel,
        string reporterKey,
        string protectedDestination,
        string encryptionKeyVersion,
        DateTimeOffset createdAtUtc) =>
        new(
            Guid.NewGuid(),
            channel,
            reporterKey,
            protectedDestination,
            encryptionKeyVersion,
            createdAtUtc);

    public void Touch(DateTimeOffset usedAtUtc) => LastUsedAtUtc = usedAtUtc;
}
