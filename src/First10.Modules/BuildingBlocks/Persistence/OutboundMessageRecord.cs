namespace First10.Modules.BuildingBlocks.Persistence;

public sealed class OutboundMessageRecord
{
    private OutboundMessageRecord()
    {
    }

    private OutboundMessageRecord(
        Guid id,
        Guid semanticIdentity,
        string contractType,
        string payload,
        DateTimeOffset enqueuedAtUtc)
    {
        Id = id;
        SemanticIdentity = semanticIdentity;
        ContractType = contractType;
        Payload = payload;
        EnqueuedAtUtc = enqueuedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid SemanticIdentity { get; private set; }

    public string ContractType { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset EnqueuedAtUtc { get; private set; }

    public static OutboundMessageRecord Create(
        Guid semanticIdentity,
        string contractType,
        string payload,
        DateTimeOffset enqueuedAtUtc) =>
        new(Guid.NewGuid(), semanticIdentity, contractType, payload, enqueuedAtUtc);
}
