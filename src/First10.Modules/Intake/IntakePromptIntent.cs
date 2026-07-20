namespace First10.Modules.Intake;

public enum IntakeLanguage
{
    English = 1,
    NigerianPidgin = 2,
    Yoruba = 3
}

public enum ChannelDeliveryStatus
{
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
        IntakePrompt prompt,
        IntakeLanguage language,
        string catalogueVersion,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        SessionId = sessionId;
        ContactReference = contactReference;
        Prompt = prompt;
        Language = language;
        CatalogueVersion = catalogueVersion;
        CreatedAtUtc = createdAtUtc;
        DeliveryStatus = ChannelDeliveryStatus.Accepted;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid ContactReference { get; private set; }

    public IntakePrompt Prompt { get; private set; }

    public IntakeLanguage Language { get; private set; }

    public string CatalogueVersion { get; private set; } = string.Empty;

    public ChannelDeliveryStatus DeliveryStatus { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static IntakePromptIntent Create(
        GuidedIntakeSession session,
        IntakePrompt prompt,
        IntakeLanguage language,
        DateTimeOffset createdAtUtc) =>
        new(
            Guid.NewGuid(),
            session.Id,
            session.ContactReference,
            prompt,
            language,
            IntakeMessageCatalogue.Version,
            createdAtUtc);
}

public static class IntakeMessageCatalogue
{
    public const string Version = "2026-07-20.1";

    private static readonly Dictionary<(IntakeLanguage, IntakePrompt), string> Messages =
        new Dictionary<(IntakeLanguage, IntakePrompt), string>
        {
            [(IntakeLanguage.English, IntakePrompt.Acknowledgement)] = "Report received. Stay safe and do not enter traffic.",
            [(IntakeLanguage.English, IntakePrompt.RequestPhoto)] = "Please send a photo only if it is safe to do so.",
            [(IntakeLanguage.English, IntakePrompt.RequestVoice)] = "Please send a short voice note describing what happened.",
            [(IntakeLanguage.English, IntakePrompt.RequestLocation)] = "Please share the crash location pin.",
            [(IntakeLanguage.English, IntakePrompt.RemindLocation)] = "We still need the location pin. If it is unsafe, do not move closer.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.Acknowledgement)] = "We don receive your report. Abeg stay safe, no enter road.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.RequestPhoto)] = "If e safe, abeg send picture of the crash.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.RequestVoice)] = "Abeg send short voice note tell us wetin happen.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.RequestLocation)] = "Abeg share the crash location pin.",
            [(IntakeLanguage.NigerianPidgin, IntakePrompt.RemindLocation)] = "We still need location pin. If e no safe, no move go near.",
            [(IntakeLanguage.Yoruba, IntakePrompt.Acknowledgement)] = "A ti gba iroyin naa. Jowo duro lailewu, mase wo oju popona.",
            [(IntakeLanguage.Yoruba, IntakePrompt.RequestPhoto)] = "Jowo fi aworan ijamba ranse ti o ba le se lailewu.",
            [(IntakeLanguage.Yoruba, IntakePrompt.RequestVoice)] = "Jowo fi ifiranse ohun kukuru ranse nipa ohun to sele.",
            [(IntakeLanguage.Yoruba, IntakePrompt.RequestLocation)] = "Jowo pin ami ibi ti ijamba naa wa.",
            [(IntakeLanguage.Yoruba, IntakePrompt.RemindLocation)] = "A tun nilo ami ibi naa. Ma sunmo ti ko ba lewu.",
        };

    public static string Get(IntakeLanguage language, IntakePrompt prompt) => Messages[(language, prompt)];
}

public sealed record RemindMissingLocation(Guid SessionId);

public sealed record ExpireGuidedSession(Guid SessionId);
