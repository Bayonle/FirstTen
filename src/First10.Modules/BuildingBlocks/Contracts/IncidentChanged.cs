namespace First10.Modules.BuildingBlocks.Contracts;

public sealed record IncidentChanged(
    Guid IncidentId,
    int Version,
    string Category,
    DateTimeOffset ChangedAtUtc);
