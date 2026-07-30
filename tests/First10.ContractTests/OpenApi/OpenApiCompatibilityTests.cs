using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace First10.ContractTests.OpenApi;

public sealed class OpenApiCompatibilityTests
{
    [Fact]
    public async Task VersionedConsoleSurfaceContainsRequiredTaskEndpoints()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "openapi-v1-surface.json");
        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var approvedEndpoints = fixture.RootElement.GetProperty("requiredEndpoints")
            .EnumerateArray()
            .Select(x => $"{x.GetProperty("method").GetString()} {x.GetProperty("path").GetString()}")
            .ToHashSet(StringComparer.Ordinal);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting(
                "ConnectionStrings:first10",
                "Host=127.0.0.1;Port=1;Database=first10_contract;Username=first10;Password=not-used");
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var live = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var liveEndpoints = live.RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => HttpMethods.Contains(operation.Name))
                .Select(operation => $"{operation.Name.ToUpperInvariant()} {NormalizePath(path.Name)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(approvedEndpoints, endpoint => Assert.Contains(endpoint, liveEndpoints));
        var securitySchemes = live.RootElement.GetProperty("components").GetProperty("securitySchemes");
        Assert.True(securitySchemes.TryGetProperty("First10Session", out _));
        Assert.True(securitySchemes.TryGetProperty("First10Antiforgery", out _));
        Assert.NotEmpty(live.RootElement.GetProperty("paths")
            .GetProperty("/api/incidents")
            .GetProperty("get")
            .GetProperty("security")
            .EnumerateArray());
        var fingerprint = ComputeContractFingerprint(live.RootElement, approvedEndpoints);
        Console.WriteLine($"OpenAPI contract fingerprint: {fingerprint}");
        Assert.Equal(
            fixture.RootElement.GetProperty("contractSha256").GetString(),
            fingerprint);
        Assert.Equal(1, fixture.RootElement.GetProperty("majorVersion").GetInt32());
    }

    private static readonly HashSet<string> HttpMethods =
        ["get", "post", "put", "patch", "delete", "head", "options"];

    private static string NormalizePath(string path) =>
        path.Length > 1 ? path.TrimEnd('/') : path;

    private static string ComputeContractFingerprint(
        JsonElement document,
        IReadOnlySet<string> approvedEndpoints)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var endpoint in approvedEndpoints.Order(StringComparer.Ordinal))
            {
                var separator = endpoint.IndexOf(' ');
                var method = endpoint[..separator].ToLowerInvariant();
                var path = endpoint[(separator + 1)..];
                writer.WritePropertyName(endpoint);
                WriteCanonical(writer, document.GetProperty("paths").GetProperty(path).GetProperty(method));
            }

            writer.WritePropertyName("securitySchemes");
            if (document.TryGetProperty("components", out var components)
                && components.TryGetProperty("securitySchemes", out var securitySchemes))
            {
                WriteCanonical(writer, securitySchemes);
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
