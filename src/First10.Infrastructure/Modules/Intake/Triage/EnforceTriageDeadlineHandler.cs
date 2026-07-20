using System.Data;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using First10.Modules.Incidents;
using First10.Modules.Intake;
using First10.Modules.Intake.Triage;
using First10.Modules.Guidance;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace First10.Infrastructure.Modules.Intake.Triage;

public sealed record TriageDeadlineOutcome(
    Guid AlertId,
    Guid ReporterPromptIntentId,
    DateTimeOffset FirstReceiptDeadlineUtc);

public sealed class TriageDeadlineProcessor(First10DbContext database)
{
    public async Task<TriageDeadlineOutcome?> ApplyAsync(
        Guid triageCaseId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({triageCaseId.ToString()}))",
            cancellationToken);

        var triageCase = await database.TriageCases.SingleOrDefaultAsync(
            x => x.Id == triageCaseId,
            cancellationToken);
        if (triageCase is null || !triageCase.TryEnterManualReview(now))
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return null;
        }

        var session = await database.GuidedIntakeSessions.SingleAsync(
            x => x.Id == triageCase.SessionId,
            cancellationToken);
        var alert = ManualTriageAlert.Create(triageCase.Id, now);
        var prompt = IntakePromptIntent.Create(
            session,
            IntakePrompt.ManualReviewFallback,
            IntakeLanguage.English,
            now);
        database.ManualTriageAlerts.Add(alert);
        database.IntakePromptIntents.Add(prompt);
        await AuditWriter.AppendAsync(
            database,
            "intake.triage.manual_review",
            "worker:triage-deadline",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["triageCaseId"] = triageCase.Id,
                ["sessionId"] = triageCase.SessionId,
                ["deadlineAtUtc"] = triageCase.DeadlineAtUtc,
                ["alertId"] = alert.Id,
                ["reporterPromptIntentId"] = prompt.Id
            }),
            cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new TriageDeadlineOutcome(alert.Id, prompt.Id, triageCase.DeadlineAtUtc);
    }
}

public static class EnforceTriageDeadlineHandler
{
    public static async Task Handle(
        EnforceTriageDeadline command,
        TriageDeadlineProcessor processor,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var outcome = await processor.ApplyAsync(
            command.TriageCaseId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (outcome is not null)
        {
            await bus.PublishAsync(new DeliverIntakePrompt(outcome.ReporterPromptIntentId));
            await bus.PublishAsync(new CreateOrMatchIncident(command.TriageCaseId));
            await bus.PublishAsync(new InitialGuidanceRequested(
                command.TriageCaseId,
                outcome.FirstReceiptDeadlineUtc));
        }
    }
}
