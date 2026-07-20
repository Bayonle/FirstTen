namespace First10.Modules.BuildingBlocks.Contracts;

public abstract record ModuleContract(Guid MessageId, DateTimeOffset OccurredAtUtc);
