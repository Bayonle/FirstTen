using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.IdentityAudit;
using First10.Modules.Recognition;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Modules.Recognition;

public sealed class RecognitionAwardProcessor(
    First10DbContext database,
    TimeProvider timeProvider)
{
    public async Task<Guid?> ApplyAsync(
        ContributionDispatcherVerified contribution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (contribution.ContributionId == Guid.Empty
            || contribution.ReportId == Guid.Empty
            || string.IsNullOrWhiteSpace(contribution.ReporterKey))
        {
            return null;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({contribution.ContributionId.ToString()}))",
            cancellationToken);
        var existing = await database.ContributionRecognitionAwards.SingleOrDefaultAsync(
            x => x.ContributionId == contribution.ContributionId,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing.Id;
        }

        var award = ContributionRecognitionAward.Create(
            contribution.ContributionId,
            contribution.ReporterKey,
            contribution.ReviewedIncidentLga,
            contribution.VerifiedAtUtc);
        var notification = RecognitionNotificationIntent.Create(
            award.Id,
            contribution.ContactReference,
            contribution.Channel,
            contribution.Language,
            timeProvider.GetUtcNow());
        database.ContributionRecognitionAwards.Add(award);
        database.RecognitionNotificationIntents.Add(notification);
        await AuditWriter.AppendAsync(
            database,
            "recognition.badge.awarded",
            "worker:recognition",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["awardId"] = award.Id,
                ["contributionId"] = contribution.ContributionId,
                ["reporterKey"] = contribution.ReporterKey,
                ["reviewedIncidentLga"] = award.ReviewedIncidentLga,
                ["policyVersion"] = award.PolicyVersion
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return award.Id;
    }
}

public sealed class RecognitionConsentProcessor(
    First10DbContext database,
    TimeProvider timeProvider)
{
    public async Task<bool> ApplyAsync(
        SetRecognitionConsent command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.DecisionId == Guid.Empty || string.IsNullOrWhiteSpace(command.ReporterKey))
        {
            return false;
        }

        if (await database.RecognitionConsentDecisions.AnyAsync(
                x => x.Id == command.DecisionId,
                cancellationToken))
        {
            return false;
        }

        var decision = RecognitionConsentDecision.Create(
            command.DecisionId,
            command.ReporterKey,
            command.Choice,
            timeProvider.GetUtcNow());
        database.RecognitionConsentDecisions.Add(decision);
        await AuditWriter.AppendAsync(
            database,
            $"recognition.consent.{command.Choice.ToString().ToLowerInvariant()}",
            $"reporter:{command.ReporterKey}",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["decisionId"] = decision.Id,
                ["reporterKey"] = decision.ReporterKey
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class RecognitionAdjustmentProcessor(
    First10DbContext database,
    TimeProvider timeProvider)
{
    public async Task<bool> ApplyAsync(
        AdjustRecognitionAward command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.AdjustmentId == Guid.Empty
            || command.AwardId == Guid.Empty
            || !await database.ContributionRecognitionAwards.AnyAsync(
                x => x.Id == command.AwardId,
                cancellationToken)
            || await database.RecognitionAwardAdjustments.AnyAsync(
                x => x.Id == command.AdjustmentId,
                cancellationToken))
        {
            return false;
        }

        database.RecognitionAwardAdjustments.Add(RecognitionAwardAdjustment.Create(
            command.AdjustmentId,
            command.AwardId,
            command.Kind,
            command.ReasonCode,
            timeProvider.GetUtcNow()));
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public static class ContributionDispatcherVerifiedHandler
{
    public static async Task Handle(
        ContributionDispatcherVerified contribution,
        RecognitionAwardProcessor processor,
        CancellationToken cancellationToken)
    {
        await processor.ApplyAsync(contribution, cancellationToken);
    }
}

public static class SetRecognitionConsentHandler
{
    public static async Task Handle(
        SetRecognitionConsent command,
        RecognitionConsentProcessor processor,
        CancellationToken cancellationToken)
    {
        await processor.ApplyAsync(command, cancellationToken);
    }
}

public static class AdjustRecognitionAwardHandler
{
    public static async Task Handle(
        AdjustRecognitionAward command,
        RecognitionAdjustmentProcessor processor,
        CancellationToken cancellationToken)
    {
        await processor.ApplyAsync(command, cancellationToken);
    }
}
