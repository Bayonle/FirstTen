using System.Reflection;
using System.Text.Json;
using First10.Modules.Intake.Triage;

namespace First10.Infrastructure.Modules.Intake.Location;

public sealed class CorridorGazetteer
{
    private const string EmbeddedResource = "First10.Infrastructure.data.corridors.berger-mowe.geojson";
    private readonly IReadOnlyList<CorridorLandmark> _landmarks;

    public CorridorGazetteer()
    {
        using var stream = typeof(CorridorGazetteer).Assembly.GetManifestResourceStream(EmbeddedResource)
            ?? throw new InvalidOperationException("The corridor gazetteer is missing.");
        _landmarks = Load(stream);
    }

    public CorridorGazetteer(IReadOnlyList<CorridorLandmark> landmarks)
    {
        _landmarks = landmarks;
    }

    public ResolvedCorridorLocation? Resolve(
        string? phrase,
        double? pinLatitude,
        double? pinLongitude,
        string evidenceReference) =>
        CorridorLocationPolicy.Resolve(
            phrase,
            pinLatitude,
            pinLongitude,
            evidenceReference,
            _landmarks);

    public static IReadOnlyList<CorridorLandmark> Load(Stream geoJson)
    {
        using var document = JsonDocument.Parse(geoJson, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16
        });
        var root = document.RootElement;
        if (root.GetProperty("type").GetString() != "FeatureCollection")
        {
            throw new InvalidOperationException("Corridor gazetteer must be a GeoJSON FeatureCollection.");
        }

        var landmarks = new List<CorridorLandmark>();
        foreach (var feature in root.GetProperty("features").EnumerateArray())
        {
            var properties = feature.GetProperty("properties");
            var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
            var direction = properties.GetProperty("direction").GetString() switch
            {
                "lagos_inbound" => TravelDirection.LagosInbound,
                "ibadan_inbound" => TravelDirection.IbadanInbound,
                _ => TravelDirection.Unknown
            };
            landmarks.Add(new CorridorLandmark(
                properties.GetProperty("id").GetString() ?? throw Invalid(),
                properties.GetProperty("name").GetString() ?? throw Invalid(),
                properties.GetProperty("aliases").EnumerateArray()
                    .Select(x => x.GetString() ?? string.Empty)
                    .Where(x => x.Length > 0)
                    .ToArray(),
                coordinates[1].GetDouble(),
                coordinates[0].GetDouble(),
                direction,
                properties.GetProperty("review_status").GetString() == "frsc_reviewed"));
        }

        return landmarks;

        static InvalidOperationException Invalid() => new("Corridor gazetteer entry is invalid.");
    }
}
