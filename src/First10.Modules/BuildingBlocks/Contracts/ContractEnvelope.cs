namespace First10.Modules.BuildingBlocks.Contracts;

public sealed record ContractEnvelope<TPayload>
{
    public required Guid MessageId { get; init; }

    public required Guid CorrelationId { get; init; }

    public Guid? CausationId { get; init; }

    public required int SchemaVersion { get; init; }

    public required string TraceId { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required TPayload Payload { get; init; }
}

public sealed record AcceptedInboundWork(Guid WorkId, string TraceId);
