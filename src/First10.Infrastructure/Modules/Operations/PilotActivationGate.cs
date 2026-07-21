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
    Task<bool> CanUseWhatsAppAsync(CancellationToken cancellationToken = default);
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

        Require(blockers, "OpenAI:ApprovedProfile:ProjectId");
        Require(blockers, "OpenAI:ApprovedProfile:Model");
        Require(blockers, "OpenAI:ApprovedProfile:Region");
        Require(blockers, "OpenAI:ApprovedProfile:RetentionMode");
        Require(blockers, "Channels:WhatsApp:PhoneNumberId");
        Require(blockers, "Channels:WhatsApp:AccessToken");
        Require(blockers, "Channels:WhatsApp:AppSecret");
        var initial = await database.GuidanceTemplateSets.AsNoTracking().AnyAsync(
            x => x.EnabledAtUtc != null
                 && x.SupersededAtUtc == null
                 && x.Purpose == GuidancePurpose.InitialSafety,
            cancellationToken);
        var status = await database.GuidanceTemplateSets.AsNoTracking().AnyAsync(
            x => x.EnabledAtUtc != null
                 && x.SupersededAtUtc == null
                 && x.Purpose == GuidancePurpose.ResponseStatus,
            cancellationToken);
        if (!initial || !status)
        {
            blockers.Add("approved_guidance_coverage_incomplete");
        }

        return new ActivationGateStatus(blockers.Count == 0, blockers, timeProvider.GetUtcNow());
    }

    public async Task<bool> CanUseWhatsAppAsync(CancellationToken cancellationToken = default) =>
        configuration.GetValue<bool>("Channels:WhatsApp:SandboxMode")
        || (await EvaluateAsync(cancellationToken)).IsOpen;

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
}
