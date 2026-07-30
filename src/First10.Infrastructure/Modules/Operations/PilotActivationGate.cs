using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.Guidance;
using First10.Modules.IdentityAudit;
using First10.Modules.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace First10.Infrastructure.Modules.Operations;

public interface IPilotActivationGate
{
    Task<ActivationGateStatus> EvaluateAsync(CancellationToken cancellationToken = default);
    Task<bool> CanUseWhatsAppAsync(string? destination = null, CancellationToken cancellationToken = default);
}

public sealed class PilotActivationGate(
    First10DbContext database,
    IConfiguration configuration,
    TimeProvider timeProvider) : IPilotActivationGate
{
    private static readonly PilotGateEvidenceType[] RequiredEvidence =
        Enum.GetValues<PilotGateEvidenceType>();

    public async Task<ActivationGateStatus> EvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        var blockers = new List<string>();
        var evidence = await database.PilotGateEvidence.AsNoTracking()
            .OrderBy(x => x.RecordedAtUtc)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var type in RequiredEvidence)
        {
            var latest = evidence.LastOrDefault(x => x.Type == type);
            if (latest?.Decision != PilotGateEvidenceDecision.Approved)
            {
                blockers.Add($"evidence_{type.ToString().ToLowerInvariant()}_missing_or_revoked");
            }
        }

        foreach (var key in new[]
                 {
                     "ProjectId", "Region", "RetentionMode", "TranscriptionModel", "TriageModel", "CrewBriefingModel"
                 })
        {
            RequireMatching(blockers, $"OpenAI:{key}", $"OpenAI:ApprovedProfile:{key}");
        }
        Require(blockers, "Channels:WhatsApp:PhoneNumberId");
        Require(blockers, "Channels:WhatsApp:AccessToken");
        Require(blockers, "Channels:WhatsApp:AppSecret");
        var templates = await database.GuidanceTemplateSets.AsNoTracking()
            .Where(x => x.EnabledAtUtc != null && x.SupersededAtUtc == null && x.IsConservativeDefault)
            .Select(x => new { x.Purpose, x.Trigger })
            .ToArrayAsync(cancellationToken);
        var requiredGuidance = new[]
        {
            (GuidancePurpose.InitialSafety, "initial"),
            (GuidancePurpose.ResponseStatus, "verified"),
            (GuidancePurpose.ResponseStatus, "dispatched"),
            (GuidancePurpose.ResponseStatus, "arrived"),
            (GuidancePurpose.ResponseStatus, "transported"),
            (GuidancePurpose.ResponseStatus, "closed"),
            (GuidancePurpose.ReopenCorrection, "reopened")
        };
        foreach (var required in requiredGuidance)
        {
            if (!templates.Any(x => x.Purpose == required.Item1 && x.Trigger == required.Item2))
            {
                blockers.Add($"approved_guidance_{required.Item1.ToString().ToLowerInvariant()}_{required.Item2}_missing");
            }
        }

        return new ActivationGateStatus(blockers.Count == 0, blockers, timeProvider.GetUtcNow());
    }

    public async Task<bool> CanUseWhatsAppAsync(
        string? destination = null,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Channels:WhatsApp:SandboxMode"))
        {
            return (await EvaluateAsync(cancellationToken)).IsOpen;
        }

        var environmentName = configuration["DOTNET_ENVIRONMENT"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"];
        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var allowlist = configuration.GetSection("Channels:WhatsApp:SandboxAllowedDestinations")
            .Get<string[]>() ?? [];
        return allowlist.Length > 0
               && (destination is null || allowlist.Contains(destination, StringComparer.Ordinal));
    }

    public async Task<bool> RecordAsync(
        Guid id,
        PilotGateEvidenceType type,
        PilotGateEvidenceDecision decision,
        string evidenceReference,
        string recordedBy,
        CancellationToken cancellationToken = default)
    {
        if (await database.PilotGateEvidence.AnyAsync(x => x.Id == id, cancellationToken))
        {
            return false;
        }

        var record = PilotGateEvidence.Record(
            id,
            type,
            decision,
            evidenceReference,
            recordedBy,
            timeProvider.GetUtcNow());
        database.PilotGateEvidence.Add(record);
        await AuditWriter.AppendAsync(database, "operations.pilot_gate.evidence_recorded", recordedBy,
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["evidenceId"] = id,
                ["type"] = type.ToString(),
                ["decision"] = decision.ToString(),
                ["evidenceReference"] = evidenceReference
            }), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void Require(List<string> blockers, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            blockers.Add($"configuration_{key.Replace(':', '_').ToLowerInvariant()}_missing");
        }
    }

    private void RequireMatching(List<string> blockers, string effectiveKey, string approvedKey)
    {
        Require(blockers, effectiveKey);
        Require(blockers, approvedKey);
        var effective = configuration[effectiveKey];
        var approved = configuration[approvedKey];
        if (!string.IsNullOrWhiteSpace(effective)
            && !string.IsNullOrWhiteSpace(approved)
            && !string.Equals(effective, approved, StringComparison.Ordinal))
        {
            blockers.Add($"configuration_{effectiveKey.Replace(':', '_').ToLowerInvariant()}_not_approved");
        }
    }
}
