namespace First10.Modules.Intake.Triage;

public enum TranscriptionQuality
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public sealed record ReporterTranscript(
    string Text,
    string EvidenceReference,
    DateTimeOffset SourceOccurredAtUtc,
    double? MeanTokenConfidence,
    TranscriptionQuality Quality,
    string Model,
    int InputTokens = 0,
    int OutputTokens = 0);

public sealed record TriageProviderRequest(
    ReporterTranscript Transcript,
    byte[]? BlurredJpeg,
    string? ImageEvidenceReference,
    string SafetyIdentifier,
    IReadOnlySet<GuidanceCategory> EnabledGuidanceCategories,
    int ExpectedAuthoritativeVersion);

public sealed record TriageProviderResult(
    StructuredTriage Triage,
    string ModelConfiguration,
    int InputTokens = 0,
    int OutputTokens = 0);

public interface IReporterAudioTranscriber
{
    Task<ReporterTranscript> TranscribeAsync(
        ReadOnlyMemory<byte> safeAudio,
        string contentType,
        string evidenceReference,
        DateTimeOffset sourceOccurredAtUtc,
        CancellationToken cancellationToken = default);
}

public interface IStructuredTriageProvider
{
    Task<TriageProviderResult> TriageAsync(
        TriageProviderRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record TryTriageSession(Guid SessionId);
