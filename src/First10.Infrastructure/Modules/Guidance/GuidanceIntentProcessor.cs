using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.Guidance;
using First10.Modules.IdentityAudit;
using First10.Modules.Intake;
using First10.Modules.Intake.Triage;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wolverine;

namespace First10.Infrastructure.Modules.Guidance;

public sealed record GuidanceIntentCreation(Guid IntentId, bool ReadyForDelivery);

public sealed class GuidanceIntentProcessor(
    First10DbContext database,
    GuidanceAssetStore assets,
    TimeProvider timeProvider)
{
    public async Task<GuidanceIntentCreation?> CreateInitialAsync(
        InitialGuidanceRequested request,
        CancellationToken cancellationToken = default)
    {
        var semantic = SemanticMessageIdentity.Create("guidance.initial", request.TriageCaseId.ToString("N"));
        var existing = await database.GuidanceIntents.SingleOrDefaultAsync(
            x => x.SemanticKey == semantic.ToString(),
            cancellationToken);
        if (existing is not null)
        {
            return new GuidanceIntentCreation(existing.Id, existing.CanAttemptDelivery);
        }

        var triage = await database.TriageCases
            .Include(x => x.Assessments)
            .SingleOrDefaultAsync(x => x.Id == request.TriageCaseId, cancellationToken);
        if (triage is null)
        {
            return null;
        }

        var session = await database.GuidedIntakeSessions.SingleAsync(
            x => x.Id == triage.SessionId,
            cancellationToken);
        var assessment = triage.AuthoritativeAssessmentId.HasValue
            ? triage.Assessments.Single(x => x.Id == triage.AuthoritativeAssessmentId.Value)
            : null;
        var category = MapCategory(assessment?.GuidanceCategory ?? First10.Modules.Intake.Triage.GuidanceCategory.None);
        var severity = MapSeverity(assessment?.Severity ?? SeverityTier.Unknown);
        var language = MapLanguage(assessment?.Language ?? ReportedLanguage.Unknown);
        var selected = await assets.SelectEnabledAsync(
            GuidancePurpose.InitialSafety,
            "initial",
            category,
            severity,
            cancellationToken: cancellationToken);
        var intent = GuidanceIntent.Create(
            semantic,
            GuidancePurpose.InitialSafety,
            "initial",
            null,
            triage.Id,
            session.ContactReference,
            MapChannel(session.Channel),
            language,
            selected,
            timeProvider.GetUtcNow(),
            request.FirstReceiptDeadlineUtc);
        database.GuidanceIntents.Add(intent);
        await AuditWriter.AppendAsync(
            database,
            intent.CanAttemptDelivery ? "guidance.initial.created" : "guidance.initial.blocked",
            "worker:guidance",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["triageCaseId"] = triage.Id,
                ["intentId"] = intent.Id,
                ["templateSetId"] = intent.TemplateSetId,
                ["deadlineAtUtc"] = intent.DeadlineAtUtc,
                ["blocked"] = !intent.CanAttemptDelivery
            }),
            cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            database.ChangeTracker.Clear();
            existing = await database.GuidanceIntents.SingleAsync(
                x => x.SemanticKey == semantic.ToString(),
                cancellationToken);
            return new GuidanceIntentCreation(existing.Id, existing.CanAttemptDelivery);
        }

        return new GuidanceIntentCreation(intent.Id, intent.CanAttemptDelivery);
    }

    public async Task<IReadOnlyList<GuidanceIntentCreation>> CreateStatusAsync(
        Guid incidentId,
        Guid transitionId,
        string trigger,
        GuidancePurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var reportIds = await database.IncidentSourceReports
            .Where(x => x.IncidentId == incidentId)
            .Select(x => x.ReportId)
            .ToArrayAsync(cancellationToken);
        var triageCases = await database.TriageCases
            .Include(x => x.Assessments)
            .Where(x => reportIds.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
        var sessionIds = triageCases.Select(x => x.SessionId).ToArray();
        var sessions = await database.GuidedIntakeSessions
            .Where(x => sessionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var results = new List<GuidanceIntentCreation>();
        foreach (var triage in triageCases)
        {
            var session = sessions[triage.SessionId];
            var assessment = triage.AuthoritativeAssessmentId.HasValue
                ? triage.Assessments.Single(x => x.Id == triage.AuthoritativeAssessmentId.Value)
                : null;
            var category = MapCategory(assessment?.GuidanceCategory ?? First10.Modules.Intake.Triage.GuidanceCategory.None);
            var severity = MapSeverity(assessment?.Severity ?? SeverityTier.Unknown);
            var language = MapLanguage(assessment?.Language ?? ReportedLanguage.Unknown);
            var selected = await assets.SelectEnabledAsync(
                purpose,
                trigger,
                category,
                severity,
                cancellationToken: cancellationToken);
            var policyVersion = selected?.PolicyVersion ?? "none";
            var semantic = SemanticMessageIdentity.Create(
                "guidance.status",
                $"{incidentId:N}:{session.ReporterKey}:{transitionId:N}:{language}:{policyVersion}");
            var existing = await database.GuidanceIntents.SingleOrDefaultAsync(
                x => x.SemanticKey == semantic.ToString(),
                cancellationToken);
            if (existing is not null)
            {
                continue;
            }

            var now = timeProvider.GetUtcNow();
            var intent = GuidanceIntent.Create(
                semantic,
                purpose,
                trigger,
                incidentId,
                triage.Id,
                session.ContactReference,
                MapChannel(session.Channel),
                language,
                selected,
                now,
                now.AddMinutes(1));
            database.GuidanceIntents.Add(intent);
            results.Add(new GuidanceIntentCreation(intent.Id, intent.CanAttemptDelivery));
        }

        if (results.Count > 0)
        {
            await AuditWriter.AppendAsync(
                database,
                "guidance.status.created",
                "worker:dispatch",
                AuditPayload.Create(new Dictionary<string, object?>
                {
                    ["incidentId"] = incidentId,
                    ["transitionId"] = transitionId,
                    ["trigger"] = trigger,
                    ["intentCount"] = results.Count,
                    ["readyCount"] = results.Count(x => x.ReadyForDelivery)
                }),
                cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
        }

        return results;
    }

    private static GuidanceLanguage MapLanguage(ReportedLanguage language) => language switch
    {
        ReportedLanguage.NigerianPidgin => GuidanceLanguage.NigerianPidgin,
        ReportedLanguage.Yoruba => GuidanceLanguage.Yoruba,
        _ => GuidanceLanguage.English
    };

    private static GuidanceChannel MapChannel(IntakeChannel channel) => channel switch
    {
        IntakeChannel.Telegram => GuidanceChannel.Telegram,
        IntakeChannel.WhatsApp => GuidanceChannel.WhatsApp,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unsupported guidance channel.")
    };

    private static First10.Modules.Guidance.GuidanceCategory MapCategory(
        First10.Modules.Intake.Triage.GuidanceCategory category) => category switch
        {
            First10.Modules.Intake.Triage.GuidanceCategory.RoadTrafficCollision =>
                First10.Modules.Guidance.GuidanceCategory.RoadTrafficCollision,
            First10.Modules.Intake.Triage.GuidanceCategory.RoadTrafficCollisionWithFire =>
                First10.Modules.Guidance.GuidanceCategory.RoadTrafficCollisionWithFire,
            First10.Modules.Intake.Triage.GuidanceCategory.OkadaCollision =>
                First10.Modules.Guidance.GuidanceCategory.OkadaCollision,
            _ => First10.Modules.Guidance.GuidanceCategory.None
        };

    private static GuidanceSeverityBand MapSeverity(SeverityTier severity) => severity switch
    {
        SeverityTier.Moderate => GuidanceSeverityBand.Moderate,
        SeverityTier.High => GuidanceSeverityBand.High,
        SeverityTier.Critical => GuidanceSeverityBand.Critical,
        _ => GuidanceSeverityBand.Conservative
    };
}

public static class InitialGuidanceRequestedHandler
{
    public static async Task Handle(
        InitialGuidanceRequested request,
        GuidanceIntentProcessor processor,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await processor.CreateInitialAsync(request, cancellationToken);
        if (result?.ReadyForDelivery == true)
        {
            await bus.PublishAsync(new DeliverGuidanceIntent(result.IntentId));
        }
    }
}
