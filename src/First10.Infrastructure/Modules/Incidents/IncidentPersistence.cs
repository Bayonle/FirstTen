using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using First10.Modules.Intake;
using First10.Modules.Intake.Triage;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace First10.Infrastructure.Modules.Incidents;

public sealed record IncidentMatchOutcome(
    Guid IncidentId,
    bool Created,
    bool AutoVerified,
    int CandidateLinkCount,
    DateTimeOffset? ReviewDueAtUtc);

public sealed class IncidentPersistence(
    First10DbContext database,
    TimeProvider timeProvider,
    IPilotReporterIdentityRegistry? reporterIdentityRegistry = null)
{
    public async Task<IncidentMatchOutcome?> CreateOrMatchAsync(
        Guid triageCaseId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext('first10-incident-matching'))",
            cancellationToken);
        var existingReport = await database.IncidentSourceReports.SingleOrDefaultAsync(
            x => x.ReportId == triageCaseId,
            cancellationToken);
        if (existingReport is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new IncidentMatchOutcome(existingReport.IncidentId, false, false, 0, null);
        }

        var candidate = await BuildCandidateAsync(triageCaseId, cancellationToken);
        if (candidate is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var earliest = candidate.OccurredAtUtc - IncidentMatchPolicy.MaximumTimeDifference;
        var latest = candidate.OccurredAtUtc + IncidentMatchPolicy.MaximumTimeDifference;
        var nearby = await database.Incidents
            .Include(x => x.SourceReports)
            .Include(x => x.Conflicts)
            .Where(x => x.SourceReports.Any(report =>
                report.OccurredAtUtc >= earliest && report.OccurredAtUtc <= latest))
            .ToArrayAsync(cancellationToken);
        var evaluated = nearby
            .Select(incident => new
            {
                Incident = incident,
                Results = incident.SourceReports
                    .Select(report => IncidentMatchPolicy.Evaluate(ToCandidate(report), candidate))
                    .ToArray()
            })
            .ToArray();
        var qualifying = evaluated
            .Where(x => x.Results.Any(result => result.Decision == IncidentMatchDecision.Qualifying))
            .ToArray();
        var now = timeProvider.GetUtcNow();
        if (qualifying.Length == 1)
        {
            var incident = qualifying[0].Incident;
            var sourceIds = incident.SourceReports.Select(x => x.ReportId).ToHashSet();
            var conflictIds = incident.Conflicts.Select(x => x.Id).ToHashSet();
            var wasAutoVerified = incident.VerificationStatus == IncidentVerificationStatus.AutoVerified;
            if (!incident.TryAttach(candidate, now))
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var source = incident.SourceReports.Single(x => !sourceIds.Contains(x.ReportId));
            database.IncidentSourceReports.Add(source);
            var newConflicts = incident.Conflicts.Where(x => !conflictIds.Contains(x.Id)).ToArray();
            database.IncidentConflicts.AddRange(newConflicts);
            database.IncidentTimelineEvents.Add(IncidentTimelineEvent.ReportLinked(source));
            foreach (var conflict in newConflicts)
            {
                database.IncidentTimelineEvents.Add(IncidentTimelineEvent.StateChanged(
                    incident.Id,
                    "conflict-detected",
                    "system:incident-matcher",
                    source.OccurredAtUtc,
                    now,
                    new
                    {
                        conflictId = conflict.Id,
                        field = conflict.Field.ToString(),
                        conflict.LeftReportId,
                        conflict.RightReportId
                    }));
            }

            var openAlert = await database.IncidentReviewAlerts.SingleOrDefaultAsync(
                x => x.IncidentId == incident.Id && x.Status == IncidentReviewAlertStatus.Open,
                cancellationToken);
            if (openAlert?.TryResolve(now) == true)
            {
                database.IncidentTimelineEvents.Add(IncidentTimelineEvent.StateChanged(
                    incident.Id,
                    "singleton-review-resolved",
                    "system:incident-matcher",
                    source.OccurredAtUtc,
                    now,
                    new { alertId = openAlert.Id, resolution = "corroborated" }));
            }

            if (!wasAutoVerified && incident.VerificationStatus == IncidentVerificationStatus.AutoVerified)
            {
                database.IncidentTimelineEvents.Add(IncidentTimelineEvent.StateChanged(
                    incident.Id,
                    "incident-auto-verified",
                    "system:incident-matcher",
                    source.OccurredAtUtc,
                    now,
                    new
                    {
                        verifiedIndependentReporterCount = incident.VerifiedIndependentReporterCount,
                        corroboratingContactCount = incident.IndependentReporterCount
                    }));
            }

            var uncertainCandidates = evaluated
                .Where(x => x.Incident.Id != incident.Id
                            && x.Results.Any(result => result.Decision == IncidentMatchDecision.UncertainLocation))
                .Select(x => IncidentCandidateLink.Create(
                    incident.Id,
                    x.Incident.Id,
                    "uncertain_location",
                    now))
                .ToArray();
            var uncertainLinks = new List<IncidentCandidateLink>();
            foreach (var link in uncertainCandidates)
            {
                if (!await database.IncidentCandidateLinks.AnyAsync(x =>
                        x.FirstIncidentId == link.FirstIncidentId
                        && x.SecondIncidentId == link.SecondIncidentId
                        && x.Reason == link.Reason,
                    cancellationToken))
                {
                    uncertainLinks.Add(link);
                }
            }

            database.IncidentCandidateLinks.AddRange(uncertainLinks);

            await AppendAuditAsync(incident, source.ReportId, "matched", cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new IncidentMatchOutcome(
                incident.Id,
                false,
                incident.VerificationStatus == IncidentVerificationStatus.AutoVerified,
                uncertainLinks.Count,
                null);
        }

        var created = Incident.Create(Guid.NewGuid(), candidate, now);
        database.Incidents.Add(created);
        var createdSource = created.SourceReports.Single();
        database.IncidentTimelineEvents.Add(IncidentTimelineEvent.ReportLinked(createdSource));
        var links = new List<IncidentCandidateLink>();
        foreach (var possible in evaluated.Where(x => qualifying.Length > 1
                     ? x.Results.Any(result => result.Decision == IncidentMatchDecision.Qualifying)
                     : x.Results.Any(result => result.Decision == IncidentMatchDecision.UncertainLocation)))
        {
            links.Add(IncidentCandidateLink.Create(
                created.Id,
                possible.Incident.Id,
                qualifying.Length > 1 ? "multiple_qualifying_incidents" : "uncertain_location",
                now));
        }

        database.IncidentCandidateLinks.AddRange(links);
        await AppendAuditAsync(created, createdSource.ReportId, "created", cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new IncidentMatchOutcome(created.Id, true, false, links.Count, created.ReviewDueAtUtc);
    }

    private async Task<IncidentReportCandidate?> BuildCandidateAsync(
        Guid triageCaseId,
        CancellationToken cancellationToken)
    {
        var triageCase = await database.TriageCases
            .Include(x => x.Assessments)
            .SingleOrDefaultAsync(x => x.Id == triageCaseId, cancellationToken);
        if (triageCase is null)
        {
            return null;
        }

        var session = await database.GuidedIntakeSessions
            .Include(x => x.Inputs)
            .SingleAsync(x => x.Id == triageCase.SessionId, cancellationToken);
        var verifiedIdentityKey = reporterIdentityRegistry?.ResolveVerifiedIdentityKey(session.ReporterKey);
        if (triageCase.AuthoritativeAssessmentId is null)
        {
            if (triageCase.Status != TriageStatus.ManualReview)
            {
                return null;
            }

            var pin = session.Inputs
                .Where(x => x.ContentKind == IntakeContentKind.Location
                            && x.Latitude.HasValue
                            && x.Longitude.HasValue)
                .OrderByDescending(x => x.OccurredAtUtc)
                .FirstOrDefault();
            return new IncidentReportCandidate(
                triageCase.Id,
                session.ReporterKey,
                session.Inputs.Min(x => x.OccurredAtUtc),
                triageCase.ManualReviewRaisedAtUtc ?? timeProvider.GetUtcNow(),
                pin?.Latitude,
                pin?.Longitude,
                pin is null ? null : 1,
                IncidentKind.Unknown,
                IncidentSeverity.Unknown,
                null,
                null,
                session.Inputs.Select(x => $"input:{x.Id:N}").ToArray(),
                verifiedIdentityKey,
                pin is null ? null : "reporter-pin",
                IncidentTravelDirection.Unknown,
                ObservedVictimState.Unknown,
                ObservedSceneState.Unknown);
        }

        var assessment = triageCase.Assessments.Single(x => x.Id == triageCase.AuthoritativeAssessmentId);
        return new IncidentReportCandidate(
            triageCase.Id,
            session.ReporterKey,
            session.Inputs.Min(x => x.OccurredAtUtc),
            assessment.ReceivedAtUtc,
            assessment.ResolvedLatitude,
            assessment.ResolvedLongitude,
            assessment.LocationConfidence,
            Map(assessment.IncidentType),
            Map(assessment.Severity),
            assessment.CasualtyMinimum,
            assessment.CasualtyMaximum,
            assessment.EvidenceReferences.Split(',', StringSplitOptions.RemoveEmptyEntries),
            verifiedIdentityKey,
            assessment.ResolvedLandmarkId ?? assessment.LocationPhrase,
            Map(assessment.ResolvedDirection),
            ObservedVictimState.Unknown,
            ObservedSceneState.Unknown);
    }

    private static IncidentKind Map(IncidentType value) => value switch
    {
        IncidentType.RoadTrafficCollision => IncidentKind.RoadTrafficCollision,
        IncidentType.RoadTrafficCollisionWithFire => IncidentKind.RoadTrafficCollisionWithFire,
        IncidentType.OkadaCollision => IncidentKind.OkadaCollision,
        _ => IncidentKind.Unknown
    };

    private static IncidentSeverity Map(SeverityTier value) => value switch
    {
        SeverityTier.Moderate => IncidentSeverity.Moderate,
        SeverityTier.High => IncidentSeverity.High,
        SeverityTier.Critical => IncidentSeverity.Critical,
        _ => IncidentSeverity.Unknown
    };

    private static IncidentTravelDirection Map(TravelDirection value) => value switch
    {
        TravelDirection.LagosInbound => IncidentTravelDirection.LagosInbound,
        TravelDirection.IbadanInbound => IncidentTravelDirection.IbadanInbound,
        _ => IncidentTravelDirection.Unknown
    };

    private static IncidentReportCandidate ToCandidate(IncidentSourceReport report) => new(
        report.ReportId,
        report.ReporterIndependenceKey,
        report.OccurredAtUtc,
        report.ReceivedAtUtc,
        report.Latitude,
        report.Longitude,
        report.LocationConfidence,
        report.IncidentType,
        report.Severity,
        report.CasualtyMinimum,
        report.CasualtyMaximum,
        report.EvidenceReferences.Split(',', StringSplitOptions.RemoveEmptyEntries),
        report.VerifiedPilotIdentityKey,
        report.LocationDescription,
        report.Direction,
        report.VictimState,
        report.SceneState);

    private Task<AuditEvent> AppendAuditAsync(
        Incident incident,
        Guid reportId,
        string result,
        CancellationToken cancellationToken) =>
        AuditWriter.AppendAsync(
            database,
            $"incidents.report.{result}",
            "worker:incident-matcher",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["incidentId"] = incident.Id,
                ["reportId"] = reportId,
                ["verificationStatus"] = incident.VerificationStatus.ToString(),
                ["sourceReportCount"] = incident.SourceReports.Count,
                ["unresolvedConflictCount"] = incident.Conflicts.Count(x => !x.IsResolved)
            }),
            cancellationToken);
}

public static class CreateOrMatchIncidentHandler
{
    public static async Task Handle(
        CreateOrMatchIncident command,
        IncidentPersistence persistence,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var outcome = await persistence.CreateOrMatchAsync(command.TriageCaseId, cancellationToken);
        if (outcome is { Created: true, ReviewDueAtUtc: not null })
        {
            await bus.ScheduleAsync(
                new ReviewSingletonIncident(outcome.IncidentId),
                outcome.ReviewDueAtUtc.Value);
        }
    }
}
