using System.Text.Json;

namespace First10.ContractTests.OpenApi;

public sealed class OpenApiCompatibilityTests
{
    [Fact]
    public void VersionedConsoleSurfaceContainsRequiredTaskEndpoints()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "openapi-v1-surface.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var endpoints = document.RootElement.GetProperty("requiredEndpoints")
            .EnumerateArray()
            .Select(x => $"{x.GetProperty("method").GetString()} {x.GetProperty("path").GetString()}")
            .ToHashSet(StringComparer.Ordinal);

        string[] required =
        [
            "GET /api/incidents",
            "GET /api/incidents/{incidentId}",
            "GET /api/incidents/{incidentId}/timeline",
            "POST /api/incidents/{incidentId}/review",
            "POST /api/incidents/{incidentId}/dispatch",
            "POST /api/incidents/{incidentId}/notes",
            "POST /api/incidents/{incidentId}/conflicts/{conflictId}/resolve",
            "GET /api/guidance/templates",
            "POST /api/guidance/templates/{templateId}/approve",
            "GET /api/recognition/aggregates",
            "GET /api/operations/activation",
            "GET /api/operations/health"
        ];
        Assert.All(required, endpoint => Assert.Contains(endpoint, endpoints));
        Assert.Equal(1, document.RootElement.GetProperty("majorVersion").GetInt32());
    }
}
