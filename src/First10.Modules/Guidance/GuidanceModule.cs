using System.Security.Cryptography;
using System.Text;
using First10.Modules.BuildingBlocks.Contracts;
namespace First10.Modules.Guidance;

public enum GuidanceChannel
{
    Telegram = 1,
    WhatsApp = 2
}

public enum GuidanceLanguage
{
    English = 1,
    NigerianPidgin = 2,
    Yoruba = 3
}

public enum GuidanceCategory
{
    None = 0,
    RoadTrafficCollision = 1,
    RoadTrafficCollisionWithFire = 2,
    OkadaCollision = 3
}

public enum GuidancePurpose
{
    InitialSafety = 1,
    ResponseStatus = 2,
    ReopenCorrection = 3
}

public enum GuidanceSeverityBand
{
    Conservative = 1,
    Moderate = 2,
    High = 3,
    Critical = 4
}

public enum GuidanceIntentStatus
{
    Pending = 1,
    Accepted = 2,
    Delivered = 3,
    Failed = 4,
    Unknown = 5,
    BlockedNoApprovedTemplate = 6
}

public sealed record GuidanceLocaleDraft(
    GuidanceLanguage Language,
    string ExactText,
    string TextSha256,
    string VoiceAssetKey,
    string VoiceSha256,
    string SpeechModel,
    string SpeechVoice,
    string SpeechSettings);

public sealed class GuidanceTemplateSet
{
    private readonly List<GuidanceTemplateAsset> _assets = [];

    private GuidanceTemplateSet()
    {
    }

    public Guid Id { get; private set; }
    public string TemplateKey { get; private set; } = string.Empty;
    public GuidancePurpose Purpose { get; private set; }
    public GuidanceCategory Category { get; private set; }
    public GuidanceSeverityBand SeverityBand { get; private set; }
    public string PolicyVersion { get; private set; } = string.Empty;
    public string Trigger { get; private set; } = string.Empty;
    public string EligibilityContext { get; private set; } = string.Empty;
    public bool IsConservativeDefault { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? EnabledAtUtc { get; private set; }
    public DateTimeOffset? SupersededAtUtc { get; private set; }
    public IReadOnlyCollection<GuidanceTemplateAsset> Assets => _assets.AsReadOnly();
    public bool IsEnabled => EnabledAtUtc.HasValue && !SupersededAtUtc.HasValue;

    public static GuidanceTemplateSet CreateDraft(
        Guid id,
        string templateKey,
        GuidancePurpose purpose,
        GuidanceCategory category,
        GuidanceSeverityBand severityBand,
        string policyVersion,
        string trigger,
        string eligibilityContext,
        bool isConservativeDefault,
        IEnumerable<GuidanceLocaleDraft> locales)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Template set ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        ArgumentException.ThrowIfNullOrWhiteSpace(eligibilityContext);
        ArgumentNullException.ThrowIfNull(locales);
        var drafts = locales.ToArray();
        var required = Enum.GetValues<GuidanceLanguage>();
        if (drafts.Length != required.Length
            || drafts.Select(x => x.Language).Distinct().Count() != required.Length
            || required.Any(language => drafts.All(x => x.Language != language)))
        {
            throw new ArgumentException("English, Nigerian Pidgin, and Yoruba assets are required.", nameof(locales));
        }

        var set = new GuidanceTemplateSet
        {
            Id = id,
            TemplateKey = templateKey.Trim(),
            Purpose = purpose,
            Category = category,
            SeverityBand = severityBand,
            PolicyVersion = policyVersion.Trim(),
            Trigger = trigger.Trim(),
            EligibilityContext = eligibilityContext.Trim(),
            IsConservativeDefault = isConservativeDefault
        };
        set._assets.AddRange(drafts.Select(x => GuidanceTemplateAsset.Create(id, x)));
        return set;
    }

    public bool TryApproveAndEnable(
        bool isAuthorized,
        string approverId,
        DateTimeOffset approvedAtUtc)
    {
        if (!isAuthorized)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(approverId);
        if (ApprovedAtUtc.HasValue || !HasCompleteValidAssets())
        {
            return false;
        }

        ApprovedBy = approverId.Trim();
        ApprovedAtUtc = approvedAtUtc;
        EnabledAtUtc = approvedAtUtc;
        return true;
    }

    public bool TrySupersede(DateTimeOffset supersededAtUtc)
    {
        if (!IsEnabled || supersededAtUtc < EnabledAtUtc)
        {
            return false;
        }

        SupersededAtUtc = supersededAtUtc;
        return true;
    }

    public GuidanceTemplateAsset? SelectAsset(GuidanceLanguage language) =>
        IsEnabled ? _assets.SingleOrDefault(x => x.Language == language) : null;

    private bool HasCompleteValidAssets() =>
        _assets.Count == 3
        && _assets.Select(x => x.Language).Distinct().Count() == 3
        && _assets.All(x => x.HasValidChecksums);
}

public sealed class GuidanceTemplateAsset
{
    private GuidanceTemplateAsset()
    {
    }

    public Guid Id { get; private set; }
    public Guid TemplateSetId { get; private set; }
    public GuidanceLanguage Language { get; private set; }
    public string ExactText { get; private set; } = string.Empty;
    public string TextSha256 { get; private set; } = string.Empty;
    public string VoiceAssetKey { get; private set; } = string.Empty;
    public string VoiceSha256 { get; private set; } = string.Empty;
    public string SpeechModel { get; private set; } = string.Empty;
    public string SpeechVoice { get; private set; } = string.Empty;
    public string SpeechSettings { get; private set; } = string.Empty;
    public bool HasValidChecksums =>
        IsSha256(TextSha256)
        && IsSha256(VoiceSha256)
        && string.Equals(TextSha256, ComputeTextSha256(ExactText), StringComparison.OrdinalIgnoreCase);

    internal static GuidanceTemplateAsset Create(Guid templateSetId, GuidanceLocaleDraft draft)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.ExactText);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.VoiceAssetKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.SpeechModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.SpeechVoice);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.SpeechSettings);
        return new GuidanceTemplateAsset
        {
            Id = Guid.NewGuid(),
            TemplateSetId = templateSetId,
            Language = draft.Language,
            ExactText = draft.ExactText,
            TextSha256 = draft.TextSha256.ToLowerInvariant(),
            VoiceAssetKey = draft.VoiceAssetKey.Trim(),
            VoiceSha256 = draft.VoiceSha256.ToLowerInvariant(),
            SpeechModel = draft.SpeechModel.Trim(),
            SpeechVoice = draft.SpeechVoice.Trim(),
            SpeechSettings = draft.SpeechSettings.Trim()
        };
    }

    public static string ComputeTextSha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
}

public static class GuidanceTemplateSelector
{
    public static GuidanceTemplateSet? Select(
        IEnumerable<GuidanceTemplateSet> sets,
        GuidancePurpose purpose,
        string trigger,
        GuidanceCategory category,
        GuidanceSeverityBand severityBand,
        Guid? proposedTemplateId = null)
    {
        var enabled = sets.Where(x => x.IsEnabled && x.Purpose == purpose).ToArray();
        if (proposedTemplateId.HasValue)
        {
            var proposed = enabled.SingleOrDefault(x => x.Id == proposedTemplateId.Value);
            if (proposed is not null
                && proposed.Trigger == trigger
                && proposed.Category == category
                && proposed.SeverityBand == severityBand)
            {
                return proposed;
            }
        }

        return enabled
            .Where(x => x.Trigger == trigger
                        && x.Category == category
                        && x.SeverityBand == severityBand)
            .OrderByDescending(x => x.PolicyVersion, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? enabled
                .Where(x => x.Trigger == trigger && x.IsConservativeDefault)
                .OrderByDescending(x => x.PolicyVersion, StringComparer.Ordinal)
                .FirstOrDefault();
    }
}

public sealed class GuidanceIntent
{
    private GuidanceIntent()
    {
    }

    public Guid Id { get; private set; }
    public string SemanticKey { get; private set; } = string.Empty;
    public GuidancePurpose Purpose { get; private set; }
    public string Trigger { get; private set; } = string.Empty;
    public Guid? IncidentId { get; private set; }
    public Guid TriageCaseId { get; private set; }
    public Guid ContactReference { get; private set; }
    public GuidanceChannel Channel { get; private set; }
    public GuidanceLanguage Language { get; private set; }
    public Guid? TemplateSetId { get; private set; }
    public string PolicyVersion { get; private set; } = string.Empty;
    public string ExactText { get; private set; } = string.Empty;
    public string TextSha256 { get; private set; } = string.Empty;
    public string VoiceAssetKey { get; private set; } = string.Empty;
    public string VoiceSha256 { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset DeadlineAtUtc { get; private set; }
    public GuidanceIntentStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? FailureCode { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public bool CanAttemptDelivery => Status is GuidanceIntentStatus.Pending or GuidanceIntentStatus.Failed
                                      && AttemptCount < 3;

    public static GuidanceIntent Create(
        SemanticMessageIdentity semanticIdentity,
        GuidancePurpose purpose,
        string trigger,
        Guid? incidentId,
        Guid triageCaseId,
        Guid contactReference,
        GuidanceChannel channel,
        GuidanceLanguage language,
        GuidanceTemplateSet? template,
        DateTimeOffset createdAtUtc,
        DateTimeOffset deadlineAtUtc)
    {
        var asset = template?.SelectAsset(language);
        return new GuidanceIntent
        {
            Id = Guid.NewGuid(),
            SemanticKey = semanticIdentity.ToString(),
            Purpose = purpose,
            Trigger = trigger,
            IncidentId = incidentId,
            TriageCaseId = triageCaseId,
            ContactReference = contactReference,
            Channel = channel,
            Language = language,
            TemplateSetId = template?.Id,
            PolicyVersion = template?.PolicyVersion ?? string.Empty,
            ExactText = asset?.ExactText ?? string.Empty,
            TextSha256 = asset?.TextSha256 ?? string.Empty,
            VoiceAssetKey = asset?.VoiceAssetKey ?? string.Empty,
            VoiceSha256 = asset?.VoiceSha256 ?? string.Empty,
            CreatedAtUtc = createdAtUtc,
            DeadlineAtUtc = deadlineAtUtc,
            Status = asset is null
                ? GuidanceIntentStatus.BlockedNoApprovedTemplate
                : GuidanceIntentStatus.Pending
        };
    }

    public bool TryMarkAccepted(string providerMessageId, DateTimeOffset atUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        return TryCompleteAttempt(GuidanceIntentStatus.Accepted, atUtc, null, providerMessageId);
    }

    public bool TryMarkFailed(string failureCode, DateTimeOffset atUtc) =>
        TryCompleteAttempt(GuidanceIntentStatus.Failed, atUtc, failureCode, null);

    public bool TryMarkUnknown(string failureCode, DateTimeOffset atUtc) =>
        TryCompleteAttempt(GuidanceIntentStatus.Unknown, atUtc, failureCode, null);

    public bool TryApplyReceipt(bool delivered, string? failureCode, DateTimeOffset atUtc)
    {
        if (Status is not (GuidanceIntentStatus.Accepted or GuidanceIntentStatus.Unknown)
            || !delivered && string.IsNullOrWhiteSpace(failureCode))
        {
            return false;
        }

        Status = delivered ? GuidanceIntentStatus.Delivered : GuidanceIntentStatus.Failed;
        FailureCode = delivered ? null : failureCode;
        LastAttemptAtUtc = atUtc;
        return true;
    }

    private bool TryCompleteAttempt(
        GuidanceIntentStatus target,
        DateTimeOffset atUtc,
        string? failureCode,
        string? providerMessageId)
    {
        if (!CanAttemptDelivery || target != GuidanceIntentStatus.Accepted && string.IsNullOrWhiteSpace(failureCode))
        {
            return false;
        }

        AttemptCount++;
        LastAttemptAtUtc = atUtc;
        Status = target;
        FailureCode = failureCode;
        ProviderMessageId = providerMessageId;
        return true;
    }
}

public sealed record InitialGuidanceRequested(Guid TriageCaseId, DateTimeOffset FirstReceiptDeadlineUtc);
public sealed record DeliverGuidanceIntent(Guid IntentId);
