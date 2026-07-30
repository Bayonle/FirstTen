namespace First10.Modules.Intake;

public enum IntakeChannel
{
    Telegram = 1,
    WhatsApp = 2
}
public enum IntakeContentKind
{
    Photo = 1,
    Voice = 2,
    Location = 3,
    Unsupported = 4
}

public sealed record IntakeLocation(double Latitude, double Longitude)
{
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

public sealed record InboundChannelEnvelope
{
    public required int SchemaVersion { get; init; }

    public required IntakeChannel Channel { get; init; }

    public required string ProviderMessageId { get; init; }

    public required string ReporterKey { get; init; }

    public required Guid ContactReference { get; init; }

    public required IntakeContentKind ContentKind { get; init; }

    public string? ProviderMediaHandle { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public IntakeLocation? Location { get; init; }

    public required string CorrelationKey { get; init; }
}
