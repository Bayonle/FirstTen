using First10.Infrastructure.Modules.Operations;
using First10.Infrastructure.Persistence;
using First10.Modules.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace First10.IntegrationTests.Operations;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class PilotActivationGateTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task MissingExternalEvidenceFailsClosedForProductionWhatsApp()
    {
        await using var database = Database();
        await database.Database.MigrateAsync();
        var gate = new PilotActivationGate(database, Configuration(sandbox: false), TimeProvider.System);

        var status = await gate.EvaluateAsync();

        Assert.False(status.IsOpen);
        Assert.False(await gate.CanUseWhatsAppAsync());
        Assert.Contains(status.BlockingReasons, reason => reason.Contains("frscapproval", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExplicitSandboxKeepsControlledTestingAvailableWithoutOpeningPublicGate()
    {
        await using var database = Database();
        await database.Database.MigrateAsync();
        var gate = new PilotActivationGate(database, Configuration(sandbox: true), TimeProvider.System);

        Assert.False((await gate.EvaluateAsync()).IsOpen);
        Assert.True(await gate.CanUseWhatsAppAsync());
    }

    [Fact]
    public async Task LatestRevocationOverridesEarlierApproval()
    {
        await using var database = Database();
        await database.Database.MigrateAsync();
        var now = new DateTimeOffset(2043, 1, 1, 0, 0, 0, TimeSpan.Zero);
        database.PilotGateEvidence.Add(PilotGateEvidence.Record(
            Guid.NewGuid(), PilotGateEvidenceType.FrscApproval, PilotGateEvidenceDecision.Approved,
            "evidence://approval", "admin", now));
        database.PilotGateEvidence.Add(PilotGateEvidence.Record(
            Guid.NewGuid(), PilotGateEvidenceType.FrscApproval, PilotGateEvidenceDecision.Revoked,
            "evidence://revoked", "admin", now.AddMinutes(1)));
        await database.SaveChangesAsync();
        var gate = new PilotActivationGate(database, Configuration(sandbox: false), TimeProvider.System);

        Assert.Contains((await gate.EvaluateAsync()).BlockingReasons,
            reason => reason.Contains("frscapproval", StringComparison.Ordinal));
    }

    private First10DbContext Database() => new(
        PersistenceConfiguration.CreateOptions(postgres.ConnectionString));

    private static IConfiguration Configuration(bool sandbox) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Channels:WhatsApp:SandboxMode"] = sandbox.ToString(),
            ["Channels:WhatsApp:PhoneNumberId"] = "test-phone",
            ["Channels:WhatsApp:AccessToken"] = "test-token",
            ["Channels:WhatsApp:AppSecret"] = "test-secret",
            ["OpenAI:ApprovedProfile:ProjectId"] = "test-project",
            ["OpenAI:ApprovedProfile:Model"] = "test-model",
            ["OpenAI:ApprovedProfile:Region"] = "test-region",
            ["OpenAI:ApprovedProfile:RetentionMode"] = "disabled"
        })
        .Build();
}
