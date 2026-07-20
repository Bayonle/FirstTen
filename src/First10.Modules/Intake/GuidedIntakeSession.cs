namespace First10.Modules.Intake;

public enum GuidedSessionStatus
{
    Collecting = 1,
    AwaitingLocation = 2,
    ReadyForPrivacyProcessing = 3,
    ManualReview = 4
}

public enum IntakePrompt
{
    Acknowledgement = 1,
    RequestPhoto = 2,
    RequestVoice = 3,
    RequestLocation = 4,
    RemindLocation = 5,
    ContinueWithoutImage = 6,
    ContinueWithoutAudio = 7
}

public enum IntakeGap
{
    Photo = 1,
    Voice = 2,
    Location = 3
}

public sealed class GuidedIntakeSession
{
    private readonly List<GuidedSessionInput> _inputs = [];

    private GuidedIntakeSession()
    {
    }

    public Guid Id { get; private set; }

    public IntakeChannel Channel { get; private set; }

    public string ReporterKey { get; private set; } = string.Empty;

    public Guid ContactReference { get; private set; }

    public string CorrelationKey { get; private set; } = string.Empty;

    public DateTimeOffset OpenedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset LocationReminderDueAtUtc { get; private set; }

    public DateTimeOffset? LocationReminderSentAtUtc { get; private set; }

    public GuidedSessionStatus Status { get; private set; }

    public int Version { get; private set; } = 1;

    public IReadOnlyCollection<GuidedSessionInput> Inputs => _inputs.AsReadOnly();

    public IReadOnlyCollection<IntakePrompt> PendingPrompts
    {
        get
        {
            var prompts = new List<IntakePrompt>();
            if (!Has(IntakeContentKind.Photo))
            {
                prompts.Add(IntakePrompt.RequestPhoto);
            }

            if (!Has(IntakeContentKind.Voice))
            {
                prompts.Add(IntakePrompt.RequestVoice);
            }

            if (!Has(IntakeContentKind.Location))
            {
                prompts.Add(IntakePrompt.RequestLocation);
            }

            return prompts;
        }
    }

    public IReadOnlyCollection<IntakeGap> VisibleGaps
    {
        get
        {
            var gaps = new List<IntakeGap>();
            if (!Has(IntakeContentKind.Photo))
            {
                gaps.Add(IntakeGap.Photo);
            }

            if (!Has(IntakeContentKind.Voice))
            {
                gaps.Add(IntakeGap.Voice);
            }

            if (!Has(IntakeContentKind.Location))
            {
                gaps.Add(IntakeGap.Location);
            }

            return gaps;
        }
    }

    public static GuidedIntakeSession Open(
        Guid id,
        InboundChannelEnvelope first,
        TimeSpan collectionWindow)
    {
        ArgumentNullException.ThrowIfNull(first);
        if (first.SchemaVersion != 1)
        {
            throw new ArgumentException("Unsupported inbound envelope version.", nameof(first));
        }

        if (first.ContentKind is not (IntakeContentKind.Photo or IntakeContentKind.Voice))
        {
            throw new ArgumentException("A guided session must start with a photo or voice note.", nameof(first));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(collectionWindow, TimeSpan.Zero);

        var session = new GuidedIntakeSession
        {
            Id = id,
            Channel = first.Channel,
            ReporterKey = first.ReporterKey,
            ContactReference = first.ContactReference,
            CorrelationKey = first.CorrelationKey,
            OpenedAtUtc = first.OccurredAtUtc,
            ExpiresAtUtc = first.OccurredAtUtc.Add(collectionWindow),
            LocationReminderDueAtUtc = first.OccurredAtUtc.AddSeconds(30),
            Status = GuidedSessionStatus.Collecting
        };
        session._inputs.Add(GuidedSessionInput.From(first, id));
        session.RefreshStatus();
        return session;
    }

    public bool TryAttach(InboundChannelEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!IsOpenAt(envelope.OccurredAtUtc)
            || envelope.Channel != Channel
            || envelope.ReporterKey != ReporterKey
            || envelope.CorrelationKey != CorrelationKey
            || envelope.ContentKind == IntakeContentKind.Unsupported
            || _inputs.Any(x => x.ProviderMessageId == envelope.ProviderMessageId))
        {
            return false;
        }

        if (envelope.ContentKind == IntakeContentKind.Location
            && envelope.Location?.IsValid != true)
        {
            return false;
        }

        _inputs.Add(GuidedSessionInput.From(envelope, Id));
        Version++;
        RefreshStatus();
        return true;
    }

    public bool TryScheduleLocationReminder(DateTimeOffset now)
    {
        if (Status == GuidedSessionStatus.ReadyForPrivacyProcessing
            || Has(IntakeContentKind.Location)
            || LocationReminderSentAtUtc.HasValue
            || now < LocationReminderDueAtUtc
            || now > ExpiresAtUtc)
        {
            return false;
        }

        LocationReminderSentAtUtc = now;
        Version++;
        return true;
    }

    public bool TryExpire(DateTimeOffset now)
    {
        if (Status is GuidedSessionStatus.ReadyForPrivacyProcessing or GuidedSessionStatus.ManualReview
            || now < ExpiresAtUtc)
        {
            return false;
        }

        Status = GuidedSessionStatus.ManualReview;
        Version++;
        return true;
    }

    public bool IsOpenAt(DateTimeOffset occurredAtUtc) =>
        Status is GuidedSessionStatus.Collecting or GuidedSessionStatus.AwaitingLocation
        && occurredAtUtc >= OpenedAtUtc
        && occurredAtUtc <= ExpiresAtUtc;

    private bool Has(IntakeContentKind kind) => _inputs.Any(x => x.ContentKind == kind);

    private void RefreshStatus()
    {
        Status = Has(IntakeContentKind.Photo) && Has(IntakeContentKind.Voice)
            ? Has(IntakeContentKind.Location)
                ? GuidedSessionStatus.ReadyForPrivacyProcessing
                : GuidedSessionStatus.AwaitingLocation
            : GuidedSessionStatus.Collecting;
    }
}

public sealed class GuidedSessionInput
{
    private GuidedSessionInput()
    {
    }

    private GuidedSessionInput(
        Guid id,
        Guid sessionId,
        string providerMessageId,
        IntakeContentKind contentKind,
        string? providerMediaHandle,
        DateTimeOffset occurredAtUtc,
        double? latitude,
        double? longitude)
    {
        Id = id;
        SessionId = sessionId;
        ProviderMessageId = providerMessageId;
        ContentKind = contentKind;
        ProviderMediaHandle = providerMediaHandle;
        OccurredAtUtc = occurredAtUtc;
        Latitude = latitude;
        Longitude = longitude;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public string ProviderMessageId { get; private set; } = string.Empty;

    public IntakeContentKind ContentKind { get; private set; }

    public string? ProviderMediaHandle { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    internal static GuidedSessionInput From(InboundChannelEnvelope envelope, Guid sessionId) =>
        new(
            Guid.NewGuid(),
            sessionId,
            envelope.ProviderMessageId,
            envelope.ContentKind,
            envelope.ProviderMediaHandle,
            envelope.OccurredAtUtc,
            envelope.Location?.Latitude,
            envelope.Location?.Longitude);
}

public static class GuidedSessionSelector
{
    public static GuidedIntakeSession? SelectMostRecentOpen(
        IEnumerable<GuidedIntakeSession> sessions,
        InboundChannelEnvelope envelope) =>
        sessions
            .Where(x => x.Channel == envelope.Channel
                        && x.ReporterKey == envelope.ReporterKey
                        && x.CorrelationKey == envelope.CorrelationKey
                        && x.IsOpenAt(envelope.OccurredAtUtc))
            .OrderByDescending(x => x.OpenedAtUtc)
            .FirstOrDefault();
}
