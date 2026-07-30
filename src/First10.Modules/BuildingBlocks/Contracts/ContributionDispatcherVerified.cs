namespace First10.Modules.BuildingBlocks.Contracts;

public sealed record ContributionDispatcherVerified(
    Guid ContributionId,
    Guid IncidentId,
    Guid ReportId,
    string ReporterKey,
    Guid ContactReference,
    string Channel,
    string Language,
    string ReviewedIncidentLga,
    DateTimeOffset VerifiedAtUtc);
