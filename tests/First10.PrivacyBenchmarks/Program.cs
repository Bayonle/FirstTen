using System.Diagnostics;
using System.Text.Json;
using First10.Infrastructure.Modules.Intake.Media;
using First10.Modules.Intake.Media;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

const double requiredImageSuccessRate = 0.98;
const double maximumP95Milliseconds = 1_000;
string[] requiredCategories = ["rotated", "partial", "multiple", "low-light", "helmet", "frame-edge"];
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: First10.PrivacyBenchmarks <manifest.json> <model.onnx> [result.json]");
    return 2;
}

var manifestPath = Path.GetFullPath(args[0]);
var modelPath = Path.GetFullPath(args[1]);
var manifest = JsonSerializer.Deserialize<PrivacyDatasetManifest>(
    await File.ReadAllTextAsync(manifestPath),
    jsonOptions)
    ?? throw new InvalidOperationException("Privacy benchmark manifest is invalid.");
if (manifest.Cases.Count < 50)
{
    Console.Error.WriteLine("Privacy release gate requires at least 50 labelled images.");
    return 1;
}

var representedCategories = manifest.Cases.SelectMany(x => x.Categories).ToHashSet(StringComparer.Ordinal);
var missingCategories = requiredCategories.Where(x => !representedCategories.Contains(x)).ToArray();
if (missingCategories.Length > 0)
{
    Console.Error.WriteLine($"Privacy dataset is missing categories: {string.Join(", ", missingCategories)}");
    return 1;
}

var configuration = new ConfigurationBuilder().AddInMemoryCollection(
    new Dictionary<string, string?> { ["Models:FaceRedaction:Path"] = modelPath }).Build();
using var detector = new OnnxYuNetFaceDetector(configuration);
var redactor = new OnnxFaceRedactor(detector);
var manifestDirectory = Path.GetDirectoryName(manifestPath)!;
var results = new List<PrivacyCaseResult>();
foreach (var item in manifest.Cases)
{
    if (item.Faces.Count == 0)
    {
        throw new InvalidOperationException($"Face-positive case '{item.Id}' has no labels.");
    }

    var imagePath = Path.GetFullPath(item.File, manifestDirectory);
    var rawImage = await File.ReadAllBytesAsync(imagePath);
    using var image = SKBitmap.Decode(imagePath)
        ?? throw new InvalidOperationException($"Case '{item.Id}' could not be decoded.");
    var detections = detector.Detect(image);
    var timer = Stopwatch.StartNew();
    var redactionCompleted = true;
    try
    {
        _ = await redactor.RedactAsync(rawImage);
    }
    catch (MediaPrivacyException)
    {
        redactionCompleted = false;
    }

    timer.Stop();
    var matched = item.Faces.Count(face => detections.Any(detection => IntersectionOverUnion(face, detection) >= 0.30));
    results.Add(new PrivacyCaseResult(
        item.Id,
        item.Categories,
        item.Faces.Count,
        matched,
        detections.Count,
        redactionCompleted,
        timer.Elapsed.TotalMilliseconds));
    Array.Clear(rawImage);
}

var fullySuccessful = results.Count(x =>
    x.RedactionCompleted && x.ExpectedFaces == x.MatchedFaces);
var imageSuccessRate = fullySuccessful / (double)results.Count;
var orderedLatency = results.Select(x => x.Milliseconds).Order().ToArray();
var p95Milliseconds = orderedLatency[(int)Math.Ceiling(orderedLatency.Length * 0.95) - 1];
var report = new PrivacyBenchmarkReport(
    DateTimeOffset.UtcNow,
    OnnxYuNetFaceDetector.Version,
    OnnxYuNetFaceDetector.ExpectedSha256,
    OnnxYuNetFaceDetector.Licence,
    results.Count,
    imageSuccessRate,
    p95Milliseconds,
    requiredImageSuccessRate,
    maximumP95Milliseconds,
    imageSuccessRate >= requiredImageSuccessRate && p95Milliseconds <= maximumP95Milliseconds,
    results);
var json = JsonSerializer.Serialize(report, jsonOptions);
if (args.Length == 3)
{
    await File.WriteAllTextAsync(Path.GetFullPath(args[2]), json);
}

Console.WriteLine(json);
return report.Passed ? 0 : 1;

static double IntersectionOverUnion(LabelledFace expected, FaceBounds actual)
{
    var intersectionWidth = Math.Max(
        0,
        Math.Min(expected.X + expected.Width, actual.X + actual.Width) - Math.Max(expected.X, actual.X));
    var intersectionHeight = Math.Max(
        0,
        Math.Min(expected.Y + expected.Height, actual.Y + actual.Height) - Math.Max(expected.Y, actual.Y));
    var intersection = intersectionWidth * intersectionHeight;
    var union = expected.Width * expected.Height + actual.Width * actual.Height - intersection;
    return union <= 0 ? 0 : intersection / union;
}

internal sealed record PrivacyDatasetManifest(string Version, IReadOnlyList<PrivacyDatasetCase> Cases);

internal sealed record PrivacyDatasetCase(
    string Id,
    string File,
    IReadOnlyList<string> Categories,
    IReadOnlyList<LabelledFace> Faces);

internal sealed record LabelledFace(float X, float Y, float Width, float Height);

internal sealed record PrivacyCaseResult(
    string Id,
    IReadOnlyList<string> Categories,
    int ExpectedFaces,
    int MatchedFaces,
    int DetectedFaces,
    bool RedactionCompleted,
    double Milliseconds);

internal sealed record PrivacyBenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string ModelVersion,
    string ModelSha256,
    string ModelLicence,
    int ImageCount,
    double ImageSuccessRate,
    double P95Milliseconds,
    double RequiredImageSuccessRate,
    double MaximumP95Milliseconds,
    bool Passed,
    IReadOnlyList<PrivacyCaseResult> Cases);
