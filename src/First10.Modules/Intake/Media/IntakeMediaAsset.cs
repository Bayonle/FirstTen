namespace First10.Modules.Intake.Media;

public sealed class IntakeMediaAsset
{
    private IntakeMediaAsset()
    {
    }

    private IntakeMediaAsset(
        Guid inputId,
        Guid sessionId,
        IntakeMediaKind kind,
        DateTimeOffset createdAtUtc)
    {
        Id = inputId;
        InputId = inputId;
        SessionId = sessionId;
        Kind = kind;
        CreatedAtUtc = createdAtUtc;
        Status = MediaProcessingStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid InputId { get; private set; }

    public Guid SessionId { get; private set; }

    public IntakeMediaKind Kind { get; private set; }

    public MediaProcessingStatus Status { get; private set; }

    public string? SafeObjectKey { get; private set; }

    public string? SafeContentType { get; private set; }

    public long? SafeLength { get; private set; }

    public string? EncryptionKeyVersion { get; private set; }

    public string? PrivacyProcessorVersion { get; private set; }

    public MediaFailureCode? FailureCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public static IntakeMediaAsset Create(
        Guid inputId,
        Guid sessionId,
        IntakeMediaKind kind,
        DateTimeOffset createdAtUtc)
    {
        if (inputId == Guid.Empty || sessionId == Guid.Empty)
        {
            throw new ArgumentException("Media input and session IDs are required.");
        }

        return new IntakeMediaAsset(inputId, sessionId, kind, createdAtUtc);
    }

    public bool TryMarkStored(
        string safeObjectKey,
        string safeContentType,
        long safeLength,
        string encryptionKeyVersion,
        string privacyProcessorVersion,
        DateTimeOffset processedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (Status != MediaProcessingStatus.Pending)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(safeObjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeContentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptionKeyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(privacyProcessorVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(safeLength);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresAtUtc, processedAtUtc);

        SafeObjectKey = safeObjectKey;
        SafeContentType = safeContentType;
        SafeLength = safeLength;
        EncryptionKeyVersion = encryptionKeyVersion;
        PrivacyProcessorVersion = privacyProcessorVersion;
        ProcessedAtUtc = processedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Status = MediaProcessingStatus.Stored;
        return true;
    }

    public bool TryReject(MediaFailureCode failureCode, DateTimeOffset processedAtUtc)
    {
        if (Status != MediaProcessingStatus.Pending)
        {
            return false;
        }

        FailureCode = failureCode;
        ProcessedAtUtc = processedAtUtc;
        Status = MediaProcessingStatus.Rejected;
        return true;
    }

    public bool TryMarkDeleted(DateTimeOffset deletedAtUtc)
    {
        if (Status != MediaProcessingStatus.Stored
            || ExpiresAtUtc is null
            || deletedAtUtc < ExpiresAtUtc)
        {
            return false;
        }

        SafeObjectKey = null;
        DeletedAtUtc = deletedAtUtc;
        Status = MediaProcessingStatus.Deleted;
        return true;
    }
}

public sealed record ProcessIntakeMedia(Guid InputId);

public sealed record MediaProcessingDegraded(
    Guid SessionId,
    Guid InputId,
    MediaFailureCode FailureCode);
