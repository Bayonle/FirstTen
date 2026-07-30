namespace First10.Modules.Intake.Triage;

public enum TriageStatus
{
    AwaitingEvidence = 1,
    Processing = 2,
    Completed = 3,
    ManualReview = 4
}

public enum IncidentType
{
    Unknown = 0,
    RoadTrafficCollision = 1,
    RoadTrafficCollisionWithFire = 2,
    OkadaCollision = 3
}

public enum SeverityTier
{
    Unknown = 0,
    Moderate = 1,
    High = 2,
    Critical = 3
}

public enum ReportedLanguage
{
    Unknown = 0,
    English = 1,
    NigerianPidgin = 2,
    Yoruba = 3
}

public enum TriageUncertainty
{
    Low = 1,
    Medium = 2,
    High = 3
}

public enum GuidanceCategory
{
    None = 0,
    RoadTrafficCollision = 1,
    RoadTrafficCollisionWithFire = 2,
    OkadaCollision = 3
}

public enum TriageEvidenceKind
{
    Transcript = 1,
    BlurredImage = 2,
    LocationPin = 3
}

public sealed record TriageEvidenceReference(
    string Reference,
    TriageEvidenceKind Kind);

public sealed record StructuredTriage(
    IncidentType IncidentType,
    SeverityTier Severity,
    int? CasualtyMinimum,
    int? CasualtyMaximum,
    ReportedLanguage Language,
    string? LocationPhrase,
    TriageUncertainty Uncertainty,
    IReadOnlyList<TriageEvidenceReference> EvidenceReferences,
    GuidanceCategory GuidanceCategory);

public sealed class TriageCase
{
    private readonly List<TriageAssessment> _assessments = [];

    private TriageCase()
    {
    }

    private TriageCase(Guid sessionId, DateTimeOffset openedAtUtc, TimeSpan deadline)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deadline, TimeSpan.Zero);
        Id = sessionId;
        SessionId = sessionId;
        OpenedAtUtc = openedAtUtc;
        DeadlineAtUtc = openedAtUtc.Add(deadline);
        Status = TriageStatus.AwaitingEvidence;
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; private set; }

    public DateTimeOffset DeadlineAtUtc { get; private set; }

    public TriageStatus Status { get; private set; }

    public int AuthoritativeVersion { get; private set; } = 1;

    public Guid? AuthoritativeAssessmentId { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? ManualReviewRaisedAtUtc { get; private set; }

    public IReadOnlyCollection<TriageAssessment> Assessments => _assessments.AsReadOnly();

    public static TriageCase Open(
        Guid sessionId,
        DateTimeOffset openedAtUtc,
        TimeSpan? deadline = null)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        return new TriageCase(sessionId, openedAtUtc, deadline ?? TimeSpan.FromSeconds(30));
    }

    public bool TryStartProcessing(DateTimeOffset now)
    {
        if (Status != TriageStatus.AwaitingEvidence || now >= DeadlineAtUtc)
        {
            return false;
        }

        Status = TriageStatus.Processing;
        return true;
    }

    public bool TryApplyAssessment(
        StructuredTriage result,
        string modelConfiguration,
        DateTimeOffset receivedAtUtc,
        int expectedAuthoritativeVersion)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelConfiguration);

        var canBecomeAuthoritative = receivedAtUtc < DeadlineAtUtc
            && Status is TriageStatus.AwaitingEvidence or TriageStatus.Processing
            && AuthoritativeVersion == expectedAuthoritativeVersion
            && AuthoritativeAssessmentId is null;
        var assessment = TriageAssessment.Create(
            Id,
            result,
            modelConfiguration,
            receivedAtUtc,
            canBecomeAuthoritative);
        _assessments.Add(assessment);

        if (!canBecomeAuthoritative)
        {
            return false;
        }

        AuthoritativeAssessmentId = assessment.Id;
        CompletedAtUtc = receivedAtUtc;
        Status = TriageStatus.Completed;
        AuthoritativeVersion++;
        return true;
    }

    public bool TryEnterManualReview(DateTimeOffset now)
    {
        if (now < DeadlineAtUtc
            || Status == TriageStatus.Completed
            || ManualReviewRaisedAtUtc.HasValue)
        {
            return false;
        }

        Status = TriageStatus.ManualReview;
        ManualReviewRaisedAtUtc = now;
        AuthoritativeVersion++;
        return true;
    }

    public void RecordDispatcherChange()
    {
        AuthoritativeVersion++;
    }
}

public sealed class TriageAssessment
{
    private TriageAssessment()
    {
    }

    private TriageAssessment(
        Guid id,
        Guid triageCaseId,
        StructuredTriage result,
        string modelConfiguration,
        DateTimeOffset receivedAtUtc,
        bool isAuthoritative)
    {
        Id = id;
        TriageCaseId = triageCaseId;
        IncidentType = result.IncidentType;
        Severity = result.Severity;
        CasualtyMinimum = result.CasualtyMinimum;
        CasualtyMaximum = result.CasualtyMaximum;
        Language = result.Language;
        LocationPhrase = result.LocationPhrase;
        Uncertainty = result.Uncertainty;
        GuidanceCategory = result.GuidanceCategory;
        EvidenceReferences = string.Join(',', result.EvidenceReferences.Select(x => x.Reference));
        ModelConfiguration = modelConfiguration;
        ReceivedAtUtc = receivedAtUtc;
        IsAuthoritative = isAuthoritative;
    }

    public Guid Id { get; private set; }

    public Guid TriageCaseId { get; private set; }

    public IncidentType IncidentType { get; private set; }

    public SeverityTier Severity { get; private set; }

    public int? CasualtyMinimum { get; private set; }

    public int? CasualtyMaximum { get; private set; }

    public ReportedLanguage Language { get; private set; }

    public string? LocationPhrase { get; private set; }

    public TriageUncertainty Uncertainty { get; private set; }

    public GuidanceCategory GuidanceCategory { get; private set; }

    public string EvidenceReferences { get; private set; } = string.Empty;

    public string ModelConfiguration { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public bool IsAuthoritative { get; private set; }

    public double? ResolvedLatitude { get; private set; }

    public double? ResolvedLongitude { get; private set; }

    public string? ResolvedLandmarkId { get; private set; }

    public TravelDirection ResolvedDirection { get; private set; }

    public double? LocationConfidence { get; private set; }

    public string? LocationEvidenceReference { get; private set; }

    public bool LocationFromPin { get; private set; }

    public bool TryApplyResolvedLocation(ResolvedCorridorLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (ResolvedLatitude.HasValue && (!location.FromPin || LocationFromPin))
        {
            return false;
        }

        ResolvedLatitude = location.Latitude;
        ResolvedLongitude = location.Longitude;
        ResolvedLandmarkId = location.LandmarkId;
        ResolvedDirection = location.Direction;
        LocationConfidence = location.Confidence;
        LocationEvidenceReference = location.EvidenceReference;
        LocationFromPin = location.FromPin;
        return true;
    }

    internal static TriageAssessment Create(
        Guid triageCaseId,
        StructuredTriage result,
        string modelConfiguration,
        DateTimeOffset receivedAtUtc,
        bool isAuthoritative) =>
        new(Guid.NewGuid(), triageCaseId, result, modelConfiguration, receivedAtUtc, isAuthoritative);
}
