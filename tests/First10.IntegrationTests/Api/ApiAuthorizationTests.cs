using First10.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace First10.IntegrationTests.Api;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class ApiAuthorizationTests(Persistence.PostgresFixture postgres)
{
    [Theory]
    [InlineData("/api/incidents")]
    [InlineData("/api/operations/health")]
    [InlineData("/api/guidance/templates")]
    [InlineData("/api/recognition/aggregates")]
    public async Task OperationalRoutesRevealNothingToAnonymousCallers(string path)
    {
        await MigrateAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var values));
        Assert.Contains("script-src 'self'", Assert.Single(values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignalRNegotiationRequiresDispatcherSession()
    {
        await MigrateAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsync(
            "/hubs/operations/negotiate?negotiateVersion=1",
            new StringContent(string.Empty));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AntiforgeryBootstrapIsAnonymousButReturnsOnlyTokenContract()
    {
        await MigrateAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/auth/antiforgery");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"token\"", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", payload, StringComparison.OrdinalIgnoreCase);
    }

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseSetting("ConnectionStrings:first10", postgres.ConnectionString)
            .UseSetting("Security:ReporterPseudonymKey", new string('t', 32))
            .UseSetting("Security:ReporterContactKeyVersion", "integration-v1"));

    private async Task MigrateAsync()
    {
        await using var database = new First10DbContext(
            PersistenceConfiguration.CreateOptions(postgres.ConnectionString));
        await database.Database.MigrateAsync();
    }
}
