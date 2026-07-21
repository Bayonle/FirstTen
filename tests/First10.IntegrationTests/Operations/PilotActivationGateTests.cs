using First10.Infrastructure.Modules.Operations;
using First10.Infrastructure.Persistence;
using First10.Modules.Operations;
using First10.Modules.Guidance;
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
        await database.PilotGateEvidence.ExecuteDeleteAsync();
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
        await database.PilotGateEvidence.ExecuteDeleteAsync();
        var gate = new PilotActivationGate(database, Configuration(sandbox: true), TimeProvider.System);

        Assert.False((await gate.EvaluateAsync()).IsOpen);
        Assert.True(await gate.CanUseWhatsAppAsync("2348000000000"));
        Assert.False(await gate.CanUseWhatsAppAsync("2348111111111"));
    }

    [Fact]
    public async Task LatestRevocationOverridesEarlierApproval()
    {
        await using var database = Database();
        await database.Database.MigrateAsync();
        await database.PilotGateEvidence.ExecuteDeleteAsync();
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

    [Fact]
    public async Task CompleteEvidenceProfileAndTransitionGuidanceOpenProductionGate()
    {
        await using var database = Database();
        await database.Database.MigrateAsync();
        await database.PilotGateEvidence.ExecuteDeleteAsync();
        var now = new DateTimeOffset(2044, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var type in Enum.GetValues<PilotGateEvidenceType>())
        {
            database.PilotGateEvidence.Add(PilotGateEvidence.Record(
                Guid.NewGuid(), type, PilotGateEvidenceDecision.Approved,
                $"evidence://{type}", "admin", now));
        }

        var requirements = new[]
        {
            (GuidancePurpose.InitialSafety, "initial"),
            (GuidancePurpose.ResponseStatus, "verified"),
            (GuidancePurpose.ResponseStatus, "dispatched"),
            (GuidancePurpose.ResponseStatus, "arrived"),
            (GuidancePurpose.ResponseStatus, "transported"),
            (GuidancePurpose.ResponseStatus, "closed"),
            (GuidancePurpose.ReopenCorrection, "reopened")
        };
        foreach (var requirement in requirements)
        {
            var set = GuidanceTemplateSet.CreateDraft(
                Guid.NewGuid(),
                $"gate-{requirement.Item2}-{Guid.NewGuid():N}",
                requirement.Item1,
                GuidanceCategory.None,
                GuidanceSeverityBand.Conservative,
                "gate-v1",
                requirement.Item2,
                "conservative corridor default",
                true,
                Enum.GetValues<GuidanceLanguage>().Select(language => Locale(language, requirement.Item2)));
            Assert.True(set.TryApproveAndEnable(true, "clinical-approver", now));
            database.GuidanceTemplateSets.Add(set);
        }

        await database.SaveChangesAsync();
        var gate = new PilotActivationGate(database, Configuration(sandbox: false), TimeProvider.System);

        Assert.True((await gate.EvaluateAsync()).IsOpen);
        Assert.True(await gate.CanUseWhatsAppAsync("2348000000000"));
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
            ["Channels:WhatsApp:SandboxAllowedDestinations:0"] = "2348000000000",
            ["OpenAI:ProjectId"] = "test-project",
            ["OpenAI:TranscriptionModel"] = "transcribe-model",
            ["OpenAI:TriageModel"] = "triage-model",
            ["OpenAI:CrewBriefingModel"] = "briefing-model",
            ["OpenAI:Region"] = "test-region",
            ["OpenAI:RetentionMode"] = "disabled",
            ["OpenAI:ApprovedProfile:ProjectId"] = "test-project",
            ["OpenAI:ApprovedProfile:TranscriptionModel"] = "transcribe-model",
            ["OpenAI:ApprovedProfile:TriageModel"] = "triage-model",
            ["OpenAI:ApprovedProfile:CrewBriefingModel"] = "briefing-model",
            ["OpenAI:ApprovedProfile:Region"] = "test-region",
            ["OpenAI:ApprovedProfile:RetentionMode"] = "disabled"
        })
        .Build();

    private static GuidanceLocaleDraft Locale(GuidanceLanguage language, string trigger)
    {
        var text = $"Approved {trigger} guidance in {language}.";
        return new GuidanceLocaleDraft(
            language,
            text,
            GuidanceTemplateAsset.ComputeTextSha256(text),
            $"voice/{trigger}-{language}.ogg",
            new string('a', 64),
            "approved-speech-model",
            "approved-voice",
            "{}");
    }
}
