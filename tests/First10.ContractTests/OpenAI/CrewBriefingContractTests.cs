using System.Net;
using System.Text;
using System.Text.Json;
using First10.Infrastructure.Modules.Incidents;
using First10.Infrastructure.Modules.Intake.OpenAI;
using First10.Modules.Incidents;
using Microsoft.Extensions.Configuration;

namespace First10.ContractTests.OpenAI;

public sealed class CrewBriefingContractTests
{
    [Fact]
    public async Task RequestIsStrictToolFreeNonStoredAndCanOnlyReturnKnownClaimIds()
    {
        var incident = IncidentWithOneReport();
        var projection = CrewBriefingProjection.From(incident);
        var claimId = Assert.Single(projection.Claims).ClaimId;
        var handler = new RecordingHandler(_ => CompletedResponse(
            $$"""{"ordered_claim_ids":["{{claimId}}"]}"""));
        var client = new OpenAiCrewBriefingOrderClient(HttpClient(handler), Configuration());

        var result = await client.OrderAsync(new CrewBriefingOrderRequest(
            projection,
            "safety-incident-1"));

        Assert.Equal([claimId], result.OrderedClaimIds);
        Assert.Equal("gpt-5.6-sol/reasoning-low/crew-briefing-order-v1", result.ModelConfiguration);
        using var document = JsonDocument.Parse(handler.Body!);
        var root = document.RootElement;
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.TryGetProperty("tools", out _));
        Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
        var format = root.GetProperty("text").GetProperty("format");
        Assert.True(format.GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var ordered = schema.GetProperty("properties").GetProperty("ordered_claim_ids");
        Assert.Equal(1, ordered.GetProperty("minItems").GetInt32());
        Assert.Equal(1, ordered.GetProperty("maxItems").GetInt32());
        Assert.Equal(claimId, ordered.GetProperty("items").GetProperty("enum")[0].GetString());
        Assert.DoesNotContain("provider-media-handle", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FabricatedAiClaimIsRejectedAndGeneratorUsesChronologicalFallback()
    {
        var incident = IncidentWithOneReport();
        var projection = CrewBriefingProjection.From(incident);
        var handler = new RecordingHandler(_ => CompletedResponse(
            "{\"ordered_claim_ids\":[\"report:fabricated\"]}"));
        var client = new OpenAiCrewBriefingOrderClient(HttpClient(handler), Configuration());

        var exception = await Assert.ThrowsAsync<CrewBriefingProviderException>(() =>
            client.OrderAsync(new CrewBriefingOrderRequest(projection, "safety-incident-1")));
        Assert.Equal("crew_briefing_invalid_claim_selection", exception.Code);

        var generator = new CrewBriefingGenerator(
            new FabricatingProvider(),
            new OpenAiSafetyIdentifier(Configuration()));
        var briefing = await generator.GenerateAsync(incident);
        Assert.False(briefing.UsedAiOrdering);
        Assert.Contains(Assert.Single(projection.Claims).ClaimId, briefing.OrderedClaims.Select(x => x.ClaimId));
    }

    private static Incident IncidentWithOneReport()
    {
        var at = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        return Incident.Create(
            Guid.NewGuid(),
            new IncidentReportCandidate(
                Guid.NewGuid(),
                "pseudonymous-reporter",
                at,
                at.AddSeconds(3),
                6.8,
                3.4,
                0.96,
                IncidentKind.RoadTrafficCollision,
                IncidentSeverity.High,
                1,
                2,
                ["transcript:approved-source"]),
            at.AddSeconds(3));
    }

    private static HttpResponseMessage CompletedResponse(string outputText) => JsonResponse(JsonSerializer.Serialize(new
    {
        status = "completed",
        usage = new { input_tokens = 50, output_tokens = 5, total_tokens = 55 },
        output = new[]
        {
            new { type = "message", content = new[] { new { type = "output_text", text = outputText } } }
        }
    }));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpClient HttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.openai.com/v1/")
    };

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "test-key-never-sent",
            ["OpenAI:SafetyIdentifierKey"] = "contract-only-safety-key-00000000000001",
            ["OpenAI:ProjectId"] = "project-contract",
            ["OpenAI:Region"] = "global",
            ["OpenAI:RetentionMode"] = "disabled",
            ["OpenAI:TranscriptionModel"] = "gpt-4o-transcribe",
            ["OpenAI:TriageModel"] = "gpt-5.6-sol",
            ["OpenAI:CrewBriefingModel"] = OpenAiCrewBriefingOrderClient.DefaultModel,
            ["OpenAI:ApprovedProfile:ProjectId"] = "project-contract",
            ["OpenAI:ApprovedProfile:Region"] = "global",
            ["OpenAI:ApprovedProfile:RetentionMode"] = "disabled",
            ["OpenAI:ApprovedProfile:TranscriptionModel"] = "gpt-4o-transcribe",
            ["OpenAI:ApprovedProfile:TriageModel"] = "gpt-5.6-sol",
            ["OpenAI:ApprovedProfile:CrewBriefingModel"] = OpenAiCrewBriefingOrderClient.DefaultModel
        })
        .Build();

    private sealed class FabricatingProvider : ICrewBriefingOrderProvider
    {
        public Task<CrewBriefingOrderResult> OrderAsync(
            CrewBriefingOrderRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CrewBriefingOrderResult(
                ["report:fabricated"],
                "fake",
                0,
                0));
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }
}
