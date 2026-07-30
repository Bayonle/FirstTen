using System.Diagnostics;
using System.Text.Json;
using First10.Infrastructure.Modules.Intake.OpenAI;
using First10.Modules.Intake.Triage;
using Microsoft.Extensions.Configuration;

const int minimumCases = 30;
const int minimumCasesPerLanguage = 10;
const double minimumOverallAccuracy = 0.90;
const double minimumPerLanguageAccuracy = 0.85;
const double maximumP95Milliseconds = 25_000;
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: First10.AiBenchmarks <private-manifest.json> <result.json> [--escalate]");
    return 2;
}

if (Environment.GetEnvironmentVariable("FIRST10_AI_BENCHMARK_ACK") != "approved-private-dataset")
{
    Console.Error.WriteLine("Set FIRST10_AI_BENCHMARK_ACK=approved-private-dataset after dataset approval.");
    return 2;
}

var manifestPath = Path.GetFullPath(args[0]);
var resultPath = Path.GetFullPath(args[1]);
var manifest = JsonSerializer.Deserialize<AiDatasetManifest>(
    await File.ReadAllTextAsync(manifestPath),
    jsonOptions) ?? throw new InvalidOperationException("AI benchmark manifest is invalid.");
if (manifest.Cases.Count < minimumCases)
{
    Console.Error.WriteLine($"AI benchmark requires at least {minimumCases} labelled cases.");
    return 1;
}

if (manifest.Pricing.Any(x => x.InputUsdPerMillion <= 0 || x.OutputUsdPerMillion <= 0))
{
    Console.Error.WriteLine("Replace all zero pricing placeholders with current reviewed account pricing.");
    return 1;
}

var languages = new[] { "english", "nigerian_pidgin", "yoruba" };
var missingLanguageCoverage = languages.Where(language =>
    manifest.Cases.Count(item => item.Expected.Language == language) < minimumCasesPerLanguage).ToArray();
if (missingLanguageCoverage.Length > 0)
{
    Console.Error.WriteLine($"AI dataset lacks 10-case coverage for: {string.Join(", ", missingLanguageCoverage)}");
    return 1;
}

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var safetyKey = Environment.GetEnvironmentVariable("FIRST10_OPENAI_SAFETY_IDENTIFIER_KEY");
if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(safetyKey))
{
    Console.Error.WriteLine("OPENAI_API_KEY and FIRST10_OPENAI_SAFETY_IDENTIFIER_KEY are required.");
    return 2;
}

var models = args.Length == 3 && args[2] == "--escalate"
    ? new[] { "gpt-5.6-luna", "gpt-5.6-terra", "gpt-5.6-sol" }
    : new[] { "gpt-5.6-luna" };
var manifestDirectory = Path.GetDirectoryName(manifestPath)!;
var reports = new List<AiModelReport>();
foreach (var model in models)
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["OpenAI:ApiKey"] = apiKey,
        ["OpenAI:SafetyIdentifierKey"] = safetyKey,
        ["OpenAI:TriageModel"] = model
    }).Build();
    using var transcriptionHttp = new HttpClient { BaseAddress = new Uri("https://api.openai.com/v1/"), Timeout = TimeSpan.FromSeconds(25) };
    using var triageHttp = new HttpClient { BaseAddress = new Uri("https://api.openai.com/v1/"), Timeout = TimeSpan.FromSeconds(25) };
    var transcriber = new OpenAiTranscriptionClient(transcriptionHttp, configuration);
    var triage = new OpenAiStructuredTriageClient(triageHttp, configuration);
    var audioPreparer = new OpenAiAudioPreparer();
    var safetyIdentifier = new OpenAiSafetyIdentifier(configuration);
    var caseResults = new List<AiCaseResult>();
    foreach (var item in manifest.Cases)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var audioPath = Path.GetFullPath(item.AudioFile, manifestDirectory);
            var audio = await File.ReadAllBytesAsync(audioPath);
            byte[]? image = null;
            try
            {
                using var prepared = audioPreparer.Prepare(audio, ContentType(audioPath));
                var transcript = await transcriber.TranscribeAsync(
                    prepared.Bytes,
                    prepared.ContentType,
                    $"transcript:{item.Id}",
                    item.SourceOccurredAtUtc);
                if (!string.IsNullOrWhiteSpace(item.BlurredImageFile))
                {
                    image = await File.ReadAllBytesAsync(
                        Path.GetFullPath(item.BlurredImageFile, manifestDirectory));
                }

                var enabled = item.Expected.GuidanceCategory == "none"
                    ? new HashSet<GuidanceCategory>()
                    : new HashSet<GuidanceCategory> { ParseGuidance(item.Expected.GuidanceCategory) };
                var output = await triage.TriageAsync(new TriageProviderRequest(
                    transcript,
                    image,
                    image is null ? null : $"image:{item.Id}",
                    safetyIdentifier.Create(item.Id),
                    enabled,
                    1));
                timer.Stop();
                var passed = Matches(output.Triage, item.Expected);
                var price = manifest.Pricing.SingleOrDefault(x => x.Model == model)
                    ?? throw new InvalidOperationException($"Pricing missing for {model}.");
                var transcriptionPrice = manifest.Pricing.SingleOrDefault(x => x.Model == OpenAiTranscriptionClient.Model)
                    ?? throw new InvalidOperationException("Transcription pricing is missing.");
                var cost = TokenCost(transcript.InputTokens, transcript.OutputTokens, transcriptionPrice)
                    + TokenCost(output.InputTokens, output.OutputTokens, price);
                caseResults.Add(new AiCaseResult(
                    item.Id,
                    item.Expected.Language,
                    passed,
                    output.Triage.IncidentType == IncidentType.Unknown,
                    false,
                    timer.Elapsed.TotalMilliseconds,
                    transcript.InputTokens + output.InputTokens,
                    transcript.OutputTokens + output.OutputTokens,
                    cost,
                    null));
            }
            finally
            {
                Array.Clear(audio);
                if (image is not null)
                {
                    Array.Clear(image);
                }
            }
        }
        catch (Exception exception)
        {
            timer.Stop();
            caseResults.Add(new AiCaseResult(
                item.Id,
                item.Expected.Language,
                false,
                false,
                exception is OpenAiProviderException { Code: "disabled_guidance_category" or "triage_schema_drift" },
                timer.Elapsed.TotalMilliseconds,
                0,
                0,
                0,
                exception is OpenAiProviderException providerException
                    ? providerException.Code
                    : "benchmark_case_failed"));
        }
    }

    var accuracy = caseResults.Count(x => x.Passed) / (double)caseResults.Count;
    var perLanguage = languages.ToDictionary(
        language => language,
        language => caseResults.Where(x => x.Language == language).Count(x => x.Passed)
                    / (double)caseResults.Count(x => x.Language == language));
    var orderedLatency = caseResults.Select(x => x.Milliseconds).Order().ToArray();
    var p95 = orderedLatency[(int)Math.Ceiling(orderedLatency.Length * 0.95) - 1];
    var unsafeOverreach = caseResults.Count(x => x.UnsafeOverreach);
    reports.Add(new AiModelReport(
        model,
        OpenAiStructuredTriageClient.SchemaVersion,
        OpenAiStructuredTriageClient.ImageDetail,
        accuracy,
        perLanguage,
        caseResults.Count(x => x.Abstained),
        unsafeOverreach,
        p95,
        caseResults.Sum(x => x.EstimatedUsd),
        accuracy >= minimumOverallAccuracy
        && perLanguage.Values.All(value => value >= minimumPerLanguageAccuracy)
        && unsafeOverreach == 0
        && p95 <= maximumP95Milliseconds,
        caseResults));
}

var report = new AiBenchmarkReport(
    DateTimeOffset.UtcNow,
    manifest.Version,
    minimumOverallAccuracy,
    minimumPerLanguageAccuracy,
    maximumP95Milliseconds,
    reports);
await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(report, jsonOptions));
Console.WriteLine(JsonSerializer.Serialize(report, jsonOptions));
return reports[0].Passed ? 0 : 1;

static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".ogg" or ".oga" or ".opus" => "audio/ogg",
    ".wav" => "audio/wav",
    ".mp3" => "audio/mpeg",
    ".webm" => "audio/webm",
    ".mp4" => "audio/mp4",
    ".m4a" => "audio/x-m4a",
    _ => throw new InvalidOperationException("Unsupported benchmark audio type.")
};

static bool Matches(StructuredTriage actual, ExpectedTriage expected) =>
    actual.IncidentType == ParseIncident(expected.IncidentType)
    && actual.Severity == ParseSeverity(expected.Severity)
    && actual.CasualtyMinimum == expected.CasualtyMinimum
    && actual.CasualtyMaximum == expected.CasualtyMaximum
    && actual.Language == ParseLanguage(expected.Language)
    && actual.Uncertainty == ParseUncertainty(expected.Uncertainty)
    && actual.GuidanceCategory == ParseGuidance(expected.GuidanceCategory)
    && (expected.LocationTerms.Count == 0
        || expected.LocationTerms.Any(term => actual.LocationPhrase?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));

static GuidanceCategory ParseGuidance(string value) =>
    value switch
    {
        "none" => GuidanceCategory.None,
        "road_traffic_collision" => GuidanceCategory.RoadTrafficCollision,
        "road_traffic_collision_with_fire" => GuidanceCategory.RoadTrafficCollisionWithFire,
        "okada_collision" => GuidanceCategory.OkadaCollision,
        _ => throw new InvalidOperationException("Unknown expected guidance category.")
    };

static IncidentType ParseIncident(string value) => value switch
{
    "unknown" => IncidentType.Unknown,
    "road_traffic_collision" => IncidentType.RoadTrafficCollision,
    "road_traffic_collision_with_fire" => IncidentType.RoadTrafficCollisionWithFire,
    "okada_collision" => IncidentType.OkadaCollision,
    _ => throw new InvalidOperationException("Unknown expected incident type.")
};

static SeverityTier ParseSeverity(string value) => value switch
{
    "unknown" => SeverityTier.Unknown,
    "moderate" => SeverityTier.Moderate,
    "high" => SeverityTier.High,
    "critical" => SeverityTier.Critical,
    _ => throw new InvalidOperationException("Unknown expected severity.")
};

static ReportedLanguage ParseLanguage(string value) => value switch
{
    "unknown" => ReportedLanguage.Unknown,
    "english" => ReportedLanguage.English,
    "nigerian_pidgin" => ReportedLanguage.NigerianPidgin,
    "yoruba" => ReportedLanguage.Yoruba,
    _ => throw new InvalidOperationException("Unknown expected language.")
};

static TriageUncertainty ParseUncertainty(string value) => value switch
{
    "low" => TriageUncertainty.Low,
    "medium" => TriageUncertainty.Medium,
    "high" => TriageUncertainty.High,
    _ => throw new InvalidOperationException("Unknown expected uncertainty.")
};

static double TokenCost(int inputTokens, int outputTokens, ModelPricing pricing) =>
    (inputTokens * pricing.InputUsdPerMillion + outputTokens * pricing.OutputUsdPerMillion) / 1_000_000d;

internal sealed record AiDatasetManifest(
    string Version,
    IReadOnlyList<ModelPricing> Pricing,
    IReadOnlyList<AiDatasetCase> Cases);

internal sealed record ModelPricing(
    string Model,
    double InputUsdPerMillion,
    double OutputUsdPerMillion);

internal sealed record AiDatasetCase(
    string Id,
    string AudioFile,
    string? BlurredImageFile,
    DateTimeOffset SourceOccurredAtUtc,
    ExpectedTriage Expected);

internal sealed record ExpectedTriage(
    string IncidentType,
    string Severity,
    int? CasualtyMinimum,
    int? CasualtyMaximum,
    string Language,
    IReadOnlyList<string> LocationTerms,
    string Uncertainty,
    string GuidanceCategory);

internal sealed record AiCaseResult(
    string Id,
    string Language,
    bool Passed,
    bool Abstained,
    bool UnsafeOverreach,
    double Milliseconds,
    int InputTokens,
    int OutputTokens,
    double EstimatedUsd,
    string? FailureCode);

internal sealed record AiModelReport(
    string Model,
    string SchemaVersion,
    string ImageDetail,
    double Accuracy,
    IReadOnlyDictionary<string, double> PerLanguageAccuracy,
    int AbstentionCount,
    int UnsafeOverreachCount,
    double P95Milliseconds,
    double EstimatedUsd,
    bool Passed,
    IReadOnlyList<AiCaseResult> Cases);

internal sealed record AiBenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string DatasetVersion,
    double RequiredOverallAccuracy,
    double RequiredPerLanguageAccuracy,
    double MaximumP95Milliseconds,
    IReadOnlyList<AiModelReport> Models);
