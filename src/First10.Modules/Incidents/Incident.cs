using System.Globalization;

namespace First10.Modules.Incidents;

public enum IncidentVerificationStatus
{
    AwaitingConfirmation = 1,
    AutoVerified = 2,
    DispatcherVerified = 3,
    DispatcherRejected = 4
}

public enum IncidentConflictField
{
    IncidentType = 1,
    Severity = 2,
    CasualtyEstimate = 3,
    VictimState = 4,
    SceneState = 5,
    Location = 6,
    TravelDirection = 7
}

public enum SingletonReviewDecision
{
    Verify = 1,
    Reject = 2,
    KeepOpen = 3
}

public sealed class Incident
{
    private readonly List<IncidentSourceReport> _sourceReports = [];
    private readonly List<IncidentConflict> _conflicts = [];
    private readonly List<IncidentObservation> _observations = [];

    private Incident()
    {
    }

    private Incident(
        Guid id,
        IncidentReportCandidate first,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        CreatedAtUtc = createdAtUtc;
        ReviewDueAtUtc = createdAtUtc.AddSeconds(60);
        VerificationStatus = IncidentVerificationStatus.AwaitingConfirmation;
        _sourceReports.Add(IncidentSourceReport.From(id, first));
    }

    public Guid Id { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ReviewDueAtUtc { get; private set; }

    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    public DateTimeOffset? RejectedAtUtc { get; private set; }

    public string? RejectionReason { get; private set; }

    public IncidentVerificationStatus VerificationStatus { get; private set; }

    public int Version { get; private set; } = 1;

    public IReadOnlyCollection<IncidentSourceReport> SourceReports => _sourceReports.AsReadOnly();

    public IReadOnlyCollection<IncidentConflict> Conflicts => _conflicts.AsReadOnly();

    public IReadOnlyCollection<IncidentObservation> Observations => _observations.AsReadOnly();

    public int IndependentReporterCount => _sourceReports
        .Select(x => x.ReporterIndependenceKey)
        .Distinct(StringComparer.Ordinal)
        .Count();

    public int VerifiedIndependentReporterCount => _sourceReports
        .Where(x => !string.IsNullOrWhiteSpace(x.VerifiedPilotIdentityKey))
        .Select(x => x.VerifiedPilotIdentityKey!)
        .Distinct(StringComparer.Ordinal)
        .Count();

    public bool HasUnresolvedConflicts => _conflicts.Any(x => !x.IsResolved);

    public static Incident Create(
        Guid id,
        IncidentReportCandidate first,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || first.ReportId == Guid.Empty)
        {
            throw new ArgumentException("Incident and report IDs are required.");
        }

        return new Incident(id, first, createdAtUtc);
    }

    public bool TryAttach(IncidentReportCandidate candidate, DateTimeOffset attachedAtUtc)
    {
        if (_sourceReports.Any(x => x.ReportId == candidate.ReportId)
            || !_sourceReports.Any(existing =>
                IncidentMatchPolicy.Evaluate(existing.ToCandidate(), candidate).Decision
                == IncidentMatchDecision.Qualifying))
        {
            return false;
        }

        var source = IncidentSourceReport.From(Id, candidate);
        foreach (var existing in _sourceReports)
        {
            AddConflicts(existing, source);
        }

        _sourceReports.Add(source);
        Version++;
        if (VerificationStatus == IncidentVerificationStatus.AwaitingConfirmation
            && VerifiedIndependentReporterCount >= 2)
        {
            VerificationStatus = IncidentVerificationStatus.AutoVerified;
            VerifiedAtUtc = attachedAtUtc;
            Version++;
        }

        return true;
    }

    public bool TryApplySingletonReviewDecision(
        SingletonReviewDecision decision,
        string? reason,
        DateTimeOffset decidedAtUtc)
    {
        if (VerificationStatus != IncidentVerificationStatus.AwaitingConfirmation)
        {
            return false;
        }

        switch (decision)
        {
            case SingletonReviewDecision.Verify:
                VerificationStatus = IncidentVerificationStatus.DispatcherVerified;
                VerifiedAtUtc = decidedAtUtc;
                break;
            case SingletonReviewDecision.Reject:
                ArgumentException.ThrowIfNullOrWhiteSpace(reason);
                VerificationStatus = IncidentVerificationStatus.DispatcherRejected;
                RejectedAtUtc = decidedAtUtc;
                RejectionReason = reason.Trim();
                break;
            case SingletonReviewDecision.KeepOpen:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision));
        }

        Version++;
        return true;
    }

    public bool TryResolveConflict(
        Guid conflictId,
        Guid selectedReportId,
        string resolvedBy,
        DateTimeOffset resolvedAtUtc)
    {
        var conflict = _conflicts.SingleOrDefault(x => x.Id == conflictId);
        if (conflict?.TryResolve(selectedReportId, resolvedBy, resolvedAtUtc) != true)
        {
            return false;
        }

        Version++;
        return true;
    }

    public bool TryAdvanceOperationalVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            return false;
        }

        Version++;
        return true;
    }

    public bool TryAttachObservation(IncidentObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.IncidentId != Id
            || _observations.Any(x => x.Id == observation.Id)
            || !_sourceReports.Any(x => x.ReportId == observation.SourceReportId)
            || VerificationStatus == IncidentVerificationStatus.DispatcherRejected)
        {
            return false;
        }

        foreach (var report in _sourceReports)
        {
            AddObservationConflicts(observation, report.ReportId, report.ReportId,
                report.VictimState, report.SceneState, report.LocationDescription,
                report.Direction, report.Latitude, report.Longitude);
        }

        foreach (var prior in _observations.Where(x =>
                     (x.OccurredAtUtc - observation.OccurredAtUtc).Duration() <= TimeSpan.FromMinutes(10)))
        {
            AddObservationConflicts(observation, prior.Id, prior.SourceReportId,
                prior.VictimState, prior.SceneState, prior.LocationDescription,
                prior.Direction, prior.Latitude, prior.Longitude);
        }

        _observations.Add(observation);
        Version++;
        return true;
    }

    private void AddObservationConflicts(
        IncidentObservation observation,
        Guid otherClaimId,
        Guid otherReportId,
        ObservedVictimState victimState,
        ObservedSceneState sceneState,
        string? locationDescription,
        IncidentTravelDirection direction,
        double? latitude,
        double? longitude)
    {
        AddIfDifferent(
            IncidentConflictField.VictimState,
            otherClaimId,
            otherReportId,
            observation.Id,
            observation.SourceReportId,
            victimState.ToString(),
            observation.VictimState.ToString(),
            victimState != ObservedVictimState.Unknown
            && observation.VictimState != ObservedVictimState.Unknown
            && victimState != observation.VictimState);
        AddIfDifferent(
            IncidentConflictField.SceneState,
            otherClaimId,
            otherReportId,
            observation.Id,
            observation.SourceReportId,
            sceneState.ToString(),
            observation.SceneState.ToString(),
            sceneState != ObservedSceneState.Unknown
            && observation.SceneState != ObservedSceneState.Unknown
            && sceneState != observation.SceneState);
        AddIfDifferent(
            IncidentConflictField.TravelDirection,
            otherClaimId,
            otherReportId,
            observation.Id,
            observation.SourceReportId,
            direction.ToString(),
            observation.Direction.ToString(),
            direction != IncidentTravelDirection.Unknown
            && observation.Direction != IncidentTravelDirection.Unknown
            && direction != observation.Direction);
        var distanceConflict = latitude.HasValue && longitude.HasValue
            && observation.Latitude.HasValue && observation.Longitude.HasValue
            && IncidentMatchPolicy.DistanceMeters(
                latitude.Value,
                longitude.Value,
                observation.Latitude.Value,
                observation.Longitude.Value) > 50;
        var descriptionConflict = !string.IsNullOrWhiteSpace(locationDescription)
            && !string.IsNullOrWhiteSpace(observation.LocationDescription)
            && !string.Equals(locationDescription, observation.LocationDescription, StringComparison.OrdinalIgnoreCase);
        AddIfDifferent(
            IncidentConflictField.Location,
            otherClaimId,
            otherReportId,
            observation.Id,
            observation.SourceReportId,
            FormatLocation(latitude, longitude, locationDescription, direction),
            FormatLocation(observation.Latitude, observation.Longitude, observation.LocationDescription, observation.Direction),
            distanceConflict || descriptionConflict);
    }

    private void AddConflicts(IncidentSourceReport left, IncidentSourceReport right)
    {
        AddIfDifferent(
            IncidentConflictField.IncidentType,
            left,
            right,
            left.IncidentType.ToString(),
            right.IncidentType.ToString(),
            left.IncidentType != IncidentKind.Unknown
            && right.IncidentType != IncidentKind.Unknown
            && left.IncidentType != right.IncidentType);
        AddIfDifferent(
            IncidentConflictField.Severity,
            left,
            right,
            left.Severity.ToString(),
            right.Severity.ToString(),
            left.Severity != IncidentSeverity.Unknown
            && right.Severity != IncidentSeverity.Unknown
            && left.Severity != right.Severity);
        AddIfDifferent(
            IncidentConflictField.CasualtyEstimate,
            left,
            right,
            FormatRange(left.CasualtyMinimum, left.CasualtyMaximum),
            FormatRange(right.CasualtyMinimum, right.CasualtyMaximum),
            left.CasualtyMinimum != right.CasualtyMinimum
            || left.CasualtyMaximum != right.CasualtyMaximum);
        AddIfDifferent(
            IncidentConflictField.VictimState,
            left,
            right,
            left.VictimState.ToString(),
            right.VictimState.ToString(),
            left.VictimState != ObservedVictimState.Unknown
            && right.VictimState != ObservedVictimState.Unknown
            && left.VictimState != right.VictimState);
        AddIfDifferent(
            IncidentConflictField.SceneState,
            left,
            right,
            left.SceneState.ToString(),
            right.SceneState.ToString(),
            left.SceneState != ObservedSceneState.Unknown
            && right.SceneState != ObservedSceneState.Unknown
            && left.SceneState != right.SceneState);
        AddIfDifferent(
            IncidentConflictField.TravelDirection,
            left,
            right,
            left.Direction.ToString(),
            right.Direction.ToString(),
            left.Direction != IncidentTravelDirection.Unknown
            && right.Direction != IncidentTravelDirection.Unknown
            && left.Direction != right.Direction);
        AddIfDifferent(
            IncidentConflictField.Location,
            left,
            right,
            FormatLocation(left),
            FormatLocation(right),
            HasMaterialLocationConflict(left, right));
    }

    private void AddIfDifferent(
        IncidentConflictField field,
        IncidentSourceReport left,
        IncidentSourceReport right,
        string leftValue,
        string rightValue,
        bool differs)
    {
        AddIfDifferent(
            field,
            left.ReportId,
            left.ReportId,
            right.ReportId,
            right.ReportId,
            leftValue,
            rightValue,
            differs);
    }

    private void AddIfDifferent(
        IncidentConflictField field,
        Guid leftClaimId,
        Guid leftReportId,
        Guid rightClaimId,
        Guid rightReportId,
        string leftValue,
        string rightValue,
        bool differs)
    {
        if (!differs || _conflicts.Any(x =>
                x.Field == field
                && x.LeftClaimId == leftClaimId
                && x.RightClaimId == rightClaimId))
        {
            return;
        }

        _conflicts.Add(IncidentConflict.CreateClaims(
            Id,
            field,
            leftClaimId,
            leftReportId,
            rightClaimId,
            rightReportId,
            leftValue,
            rightValue));
    }

    private static string FormatRange(int? minimum, int? maximum) =>
        minimum == maximum
            ? minimum?.ToString(CultureInfo.InvariantCulture) ?? "unknown"
            : $"{minimum?.ToString(CultureInfo.InvariantCulture) ?? "?"}-{maximum?.ToString(CultureInfo.InvariantCulture) ?? "?"}";

    private static string FormatLocation(IncidentSourceReport report) =>
        FormatLocation(report.Latitude, report.Longitude, report.LocationDescription, report.Direction);

    private static string FormatLocation(
        double? latitude,
        double? longitude,
        string? description,
        IncidentTravelDirection direction) =>
        latitude.HasValue && longitude.HasValue
            ? $"{latitude.Value:F6},{longitude.Value:F6}; {description ?? "unnamed"}; {direction}"
            : $"unresolved; {description ?? "unnamed"}; {direction}";

    private static bool HasMaterialLocationConflict(IncidentSourceReport left, IncidentSourceReport right)
    {
        if (left.Latitude.HasValue && left.Longitude.HasValue
            && right.Latitude.HasValue && right.Longitude.HasValue
            && IncidentMatchPolicy.DistanceMeters(
                left.Latitude.Value,
                left.Longitude.Value,
                right.Latitude.Value,
                right.Longitude.Value) > 50)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(left.LocationDescription)
            && !string.IsNullOrWhiteSpace(right.LocationDescription)
            && !string.Equals(
                left.LocationDescription,
                right.LocationDescription,
                StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ResolveIncidentConflict(
    Guid IncidentId,
    Guid ConflictId,
    Guid SelectedReportId,
    string ResolvedBy,
    int? ExpectedVersion = null);

public sealed record DecideSingletonIncident(
    Guid DecisionId,
    Guid IncidentId,
    SingletonReviewDecision Decision,
    string DecidedBy,
    string? Reason,
    string? ReviewedIncidentLga = null,
    int? ExpectedVersion = null);

public sealed class IncidentSourceReport
{
    private IncidentSourceReport()
    {
    }

    private IncidentSourceReport(Guid incidentId, IncidentReportCandidate candidate)
    {
        ReportId = candidate.ReportId;
        IncidentId = incidentId;
        ReporterIndependenceKey = candidate.ReporterIndependenceKey;
        VerifiedPilotIdentityKey = candidate.VerifiedPilotIdentityKey;
        OccurredAtUtc = candidate.OccurredAtUtc;
        ReceivedAtUtc = candidate.ReceivedAtUtc;
        Latitude = candidate.Latitude;
        Longitude = candidate.Longitude;
        LocationConfidence = candidate.LocationConfidence;
        IncidentType = candidate.IncidentType;
        Severity = candidate.Severity;
        CasualtyMinimum = candidate.CasualtyMinimum;
        CasualtyMaximum = candidate.CasualtyMaximum;
        LocationDescription = candidate.LocationDescription;
        Direction = candidate.Direction;
        VictimState = candidate.VictimState;
        SceneState = candidate.SceneState;
        EvidenceReferences = string.Join(',', candidate.EvidenceReferences);
    }

    public Guid ReportId { get; private set; }

    public Guid IncidentId { get; private set; }

    public string ReporterIndependenceKey { get; private set; } = string.Empty;

    public string? VerifiedPilotIdentityKey { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public double? LocationConfidence { get; private set; }

    public IncidentKind IncidentType { get; private set; }

    public IncidentSeverity Severity { get; private set; }

    public int? CasualtyMinimum { get; private set; }

    public int? CasualtyMaximum { get; private set; }

    public string? LocationDescription { get; private set; }

    public IncidentTravelDirection Direction { get; private set; }

    public ObservedVictimState VictimState { get; private set; }

    public ObservedSceneState SceneState { get; private set; }

    public string EvidenceReferences { get; private set; } = string.Empty;

    internal static IncidentSourceReport From(Guid incidentId, IncidentReportCandidate candidate) =>
        new(incidentId, candidate);

    internal IncidentReportCandidate ToCandidate() => new(
        ReportId,
        ReporterIndependenceKey,
        OccurredAtUtc,
        ReceivedAtUtc,
        Latitude,
        Longitude,
        LocationConfidence,
        IncidentType,
        Severity,
        CasualtyMinimum,
        CasualtyMaximum,
        EvidenceReferences.Split(',', StringSplitOptions.RemoveEmptyEntries),
        VerifiedPilotIdentityKey,
        LocationDescription,
        Direction,
        VictimState,
        SceneState);

    public bool TryApplyLateLocation(
        double latitude,
        double longitude,
        double confidence,
        string evidenceReference)
    {
        if (latitude is < -90 or > 90
            || longitude is < -180 or > 180
            || confidence < IncidentMatchPolicy.MinimumLocationConfidence
            || string.IsNullOrWhiteSpace(evidenceReference)
            || LocationConfidence.HasValue && LocationConfidence > confidence)
        {
            return false;
        }

        Latitude = latitude;
        Longitude = longitude;
        LocationConfidence = confidence;
        var references = EvidenceReferences.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Append(evidenceReference)
            .Distinct(StringComparer.Ordinal);
        EvidenceReferences = string.Join(',', references);
        return true;
    }
}

public sealed class IncidentConflict
{
    private IncidentConflict()
    {
    }

    private IncidentConflict(
        Guid incidentId,
        IncidentConflictField field,
        Guid leftClaimId,
        Guid leftReportId,
        Guid rightClaimId,
        Guid rightReportId,
        string leftValue,
        string rightValue)
    {
        Id = Guid.NewGuid();
        IncidentId = incidentId;
        Field = field;
        LeftClaimId = leftClaimId;
        LeftReportId = leftReportId;
        RightClaimId = rightClaimId;
        RightReportId = rightReportId;
        LeftValue = leftValue;
        RightValue = rightValue;
    }

    public Guid Id { get; private set; }

    public Guid IncidentId { get; private set; }

    public IncidentConflictField Field { get; private set; }

    public Guid LeftReportId { get; private set; }

    public Guid LeftClaimId { get; private set; }

    public Guid RightReportId { get; private set; }

    public Guid RightClaimId { get; private set; }

    public string LeftValue { get; private set; } = string.Empty;

    public string RightValue { get; private set; } = string.Empty;

    public Guid? SelectedReportId { get; private set; }

    public Guid? SelectedClaimId { get; private set; }

    public string? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public bool IsResolved => SelectedReportId.HasValue;

    internal static IncidentConflict CreateClaims(
        Guid incidentId,
        IncidentConflictField field,
        Guid leftClaimId,
        Guid leftReportId,
        Guid rightClaimId,
        Guid rightReportId,
        string leftValue,
        string rightValue) =>
        new(incidentId, field, leftClaimId, leftReportId, rightClaimId, rightReportId, leftValue, rightValue);

    public bool TryResolve(Guid selectedReportId, string resolvedBy, DateTimeOffset resolvedAtUtc)
    {
        if (IsResolved
            || selectedReportId != LeftReportId && selectedReportId != RightReportId
            && selectedReportId != LeftClaimId && selectedReportId != RightClaimId)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedBy);
        SelectedClaimId = selectedReportId == LeftClaimId || selectedReportId == RightClaimId
            ? selectedReportId
            : selectedReportId == LeftReportId ? LeftClaimId : RightClaimId;
        SelectedReportId = SelectedClaimId == LeftClaimId ? LeftReportId : RightReportId;
        ResolvedBy = resolvedBy;
        ResolvedAtUtc = resolvedAtUtc;
        return true;
    }
}
