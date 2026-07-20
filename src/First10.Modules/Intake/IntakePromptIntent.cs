namespace First10.Modules.Intake;

public enum IntakeLanguage
{
    English = 1,
    NigerianPidgin = 2,
    Yoruba = 3
}

public enum ChannelDeliveryStatus
{
    Pending = 0,
    Accepted = 1,
    Sent = 2,
    Delivered = 3,
    Failed = 4,
    Unknown = 5
}

public sealed class IntakePromptIntent
{
    private IntakePromptIntent()
    {
    }

    private IntakePromptIntent(
        Guid id,
        Guid sessionId,
        Guid contactReference,
        IntakeChannel channel,
        IntakePrompt prompt,
        IntakeLanguage language,
        string catalogueVersion,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        SessionId = sessionId;
        ContactReference = contactReference;
        Channel = channel;
        Prompt = prompt;
        Language = language;
        CatalogueVersion = catalogueVersion;
        CreatedAtUtc = createdAtUtc;
        DeliveryStatus = ChannelDeliveryStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid ContactReference { get; private set; }

    public IntakeChannel Channel { get; private set; }

    public IntakePrompt Prompt { get; private set; }

    public IntakeLanguage Language { get; private set; }

    public string CatalogueVersion { get; private set; } = string.Empty;

    public ChannelDeliveryStatus DeliveryStatus { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public string? FailureCode { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset? LastAttemptAtUtc { get; private set; }

    public DateTimeOffset? StatusChangedAtUtc { get; private set; }

    public bool CanAttemptDelivery =>
        AttemptCount < 3
        && DeliveryStatus is ChannelDeliveryStatus.Pending
            or ChannelDeliveryStatus.Failed;

    public static IntakePromptIntent Create(
        GuidedIntakeSession session,
        IntakePrompt prompt,
        IntakeLanguage language,
        DateTimeOffset createdAtUtc) =>
        new(
            Guid.NewGuid(),
            session.Id,
            session.ContactReference,
            session.Channel,
            prompt,
            language,
            IntakeMessageCatalogue.Version,
            createdAtUtc);

    public bool TryMarkProviderAccepted(string providerMessageId, DateTimeOffset occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);
        if (!CanAttemptDelivery)
        {
            return false;
        }

        AttemptCount++;
        LastAttemptAtUtc = occurredAtUtc;
        StatusChangedAtUtc = occurredAtUtc;
        ProviderMessageId = providerMessageId;
        FailureCode = null;
        DeliveryStatus = ChannelDeliveryStatus.Accepted;
        return true;
    }

    public bool TryMarkFailed(string failureCode, DateTimeOffset occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (!CanAttemptDelivery)
        {
            return false;
        }

        AttemptCount++;
        LastAttemptAtUtc = occurredAtUtc;
        StatusChangedAtUtc = occurredAtUtc;
        FailureCode = failureCode;
        DeliveryStatus = ChannelDeliveryStatus.Failed;
        return true;
    }

    public bool TryMarkUnknown(string failureCode, DateTimeOffset occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (!CanAttemptDelivery)
        {
            return false;
        }

        AttemptCount++;
        LastAttemptAtUtc = occurredAtUtc;
        StatusChangedAtUtc = occurredAtUtc;
        FailureCode = failureCode;
        DeliveryStatus = ChannelDeliveryStatus.Unknown;
        return true;
    }

    public bool TryApplyReceipt(
        ChannelDeliveryStatus status,
        DateTimeOffset occurredAtUtc,
        string? failureCode = null)
    {
        if (DeliveryStatus == status
            || DeliveryStatus == ChannelDeliveryStatus.Delivered
            || status is ChannelDeliveryStatus.Pending or ChannelDeliveryStatus.Accepted)
        {
            return false;
        }

        DeliveryStatus = status;
        StatusChangedAtUtc = occurredAtUtc;
        FailureCode = status == ChannelDeliveryStatus.Failed ? failureCode ?? "provider_failed" : null;
        return true;
    }
}

public static class IntakeMessageCatalogue
{
    public const string Version = "2026-07-20.2";

    private static readonly Dictionary<(IntakeLanguage, IntakePrompt), string> Messages =
        new Dictionary<(IntakeLanguage, IntakePrompt), string>
        {
            [(IntakeLanguage.English, IntakePrompt.Acknowledgement)] = "Report received. Stay safe and do not enter traffic.",
            [(IntakeLanguage.English, IntakePrompt.RequestPhoto)] = "Please send a photo only if it is safe to do so.",
            [(IntakeLanguage.English, IntakePrompt.RequestVoice)] = "Please send a short voice note describing what happened.",
            [(IntakeLanguage.English, IntakePrompt.RequestLocation)] = "Please share the crash location pin.",
            [(IntakeLanguage.English, IntakePrompt.RemindLocation)] = "We still need the location pin. If it is unsafe, do not move closer.",
            [(IntakeLanguage.English, IntakePrompt.ContinueWithoutImage)] = "We could not safely use that image. Please send a voice note and location pin; we will continue without the image.",
            [(IntakeLanguage.English, IntakePrompt.ContinueWithoutAudio)] = "We could not safely use that voice note. Please share a location pin; we will continue with manual review.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.Acknowledgement)] = "We don receive your report. Abeg stay safe, no enter road.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.RequestPhoto)] = "If e safe, abeg send picture of the crash.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.RequestVoice)] = "Abeg send short voice note tell us wetin happen.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.RequestLocation)] = "Abeg share the crash location pin.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.RemindLocation)] = "We still need location pin. If e no safe, no move go near.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.ContinueWithoutImage)] = "We no fit use that picture safely. Abeg send voice note and location pin; we go continue without the picture.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.ContinueWithoutAudio)] = "We no fit use that voice note safely. Abeg send location pin; person go review the report.",
            [(IntakeLanguage.Yoruba, IntakePrompt.Acknowledgement)] = "A ti gba iroyin naa. Jowo duro lailewu, mase wo oju popona.",
            [(IntakeLanguage.Yoruba, IntakePrompt.RequestPhoto)] = "Jowo fi aworan ijamba ranse ti o ba le se lailewu.",
            [(IntakeLanguage.Yoruba, IntakePrompt.RequestVoice)] = "Jowo fi ifiranse ohun kukuru ranse nipa ohun to sele.",
            [(IntakeLanguage.Yoruba, IntakePrompt.RequestLocation)] = "Jowo pin ami ibi ti ijamba naa wa.",
            [(IntakeLanguage.Yoruba, IntakePrompt.RemindLocation)] = "A tun nilo ami ibi naa. Ma sunmo ti ko ba lewu.",
            [(IntakeLanguage.Yoruba, IntakePrompt.ContinueWithoutImage)] = "A ko le lo aworan naa lailewu. Jowo fi ohun ati ami ibi ranse; a o tesiwaju laisi aworan naa.",
            [(IntakeLanguage.Yoruba, IntakePrompt.ContinueWithoutAudio)] = "A ko le lo ohun naa lailewu. Jowo fi ami ibi ranse; eniyan yoo se ayewo iroyin naa.",
        };

    public static string Get(IntakeLanguage language, IntakePrompt prompt) => Messages[(language, prompt)];
}

public sealed record RemindMissingLocation(Guid SessionId);

public sealed record ExpireGuidedSession(Guid SessionId);

public sealed record DeliverIntakePrompt(Guid IntentId);
