using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using System.Data;

namespace First10.Infrastructure.Modules.Intake;

public static class AcceptInboundEnvelopeHandler
{
    public static async Task Handle(
        AcceptInboundEnvelope command,
        GuidedIntakeProcessor processor,
        First10DbContext database,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var outcome = await processor.ApplyAsync(command.Envelope, cancellationToken);
        if (command.Envelope.ContentKind is IntakeContentKind.Photo or IntakeContentKind.Voice)
        {
            var inputId = await database.GuidedIntakeSessions
                .Where(x => x.Channel == command.Envelope.Channel
                            && x.ContactReference == command.Envelope.ContactReference)
                .SelectMany(x => x.Inputs)
                .Where(x => x.ProviderMessageId == command.Envelope.ProviderMessageId)
                .Select(x => x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (inputId != Guid.Empty)
            {
                await bus.PublishAsync(new ProcessIntakeMedia(inputId));
            }
        }

        if (outcome is not null)
        {
            foreach (var promptIntentId in outcome.PromptIntentIds)
            {
                await bus.PublishAsync(new DeliverIntakePrompt(promptIntentId));
            }

            await bus.ScheduleAsync(
                new RemindMissingLocation(outcome.SessionId),
                outcome.LocationReminderDueAtUtc);
            await bus.ScheduleAsync(
                new ExpireGuidedSession(outcome.SessionId),
                outcome.ExpiresAtUtc);
        }
    }
}

public sealed record OpenedSessionSchedule(
    Guid SessionId,
    DateTimeOffset LocationReminderDueAtUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<Guid> PromptIntentIds);

public sealed class GuidedIntakeProcessor(First10DbContext database)
{
    private static readonly TimeSpan CollectionWindow = TimeSpan.FromMinutes(2);

    public async Task<OpenedSessionSchedule?> ApplyAsync(
        InboundChannelEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({envelope.ReporterKey}))",
            cancellationToken);
        var candidates = await database.GuidedIntakeSessions
            .Include(x => x.Inputs)
            .Where(x => x.Channel == envelope.Channel
                        && x.ReporterKey == envelope.ReporterKey
                        && x.CorrelationKey == envelope.CorrelationKey
                        && x.ExpiresAtUtc >= envelope.OccurredAtUtc)
            .ToListAsync(cancellationToken);
        var eligibleSessions = candidates.Where(x => x.IsOpenAt(envelope.OccurredAtUtc)).ToArray();
        if (eligibleSessions.Length > 1)
        {
            database.IntakeRecoveryItems.Add(IntakeRecoveryItem.Create(
                envelope.Channel,
                envelope.ProviderMessageId,
                "multiple_open_sessions",
                DateTimeOffset.UtcNow,
                envelope.ContactReference,
                envelope.ReporterKey));
        }

        var session = GuidedSessionSelector.SelectMostRecentOpen(candidates, envelope);
        if (session is not null)
        {
            if (session.TryAttach(envelope))
            {
                database.GuidedSessionInputs.Add(session.Inputs.Single(
                    x => x.ProviderMessageId == envelope.ProviderMessageId));
            }

            await database.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return null;
        }

        if (envelope.ContentKind is not (IntakeContentKind.Photo or IntakeContentKind.Voice))
        {
            database.IntakeRecoveryItems.Add(IntakeRecoveryItem.Create(
                envelope.Channel,
                envelope.ProviderMessageId,
                envelope.ContentKind == IntakeContentKind.Unsupported
                    ? "unsupported_content"
                    : "unattached_input",
                DateTimeOffset.UtcNow,
                envelope.ContactReference,
                envelope.ReporterKey));
            await database.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return null;
        }

        session = GuidedIntakeSession.Open(Guid.NewGuid(), envelope, CollectionWindow);
        database.GuidedIntakeSessions.Add(session);
        var promptIntentIds = new List<Guid>
        {
            AddPrompt(session, IntakePrompt.Acknowledgement)
        };
        foreach (var prompt in session.PendingPrompts)
        {
            promptIntentIds.Add(AddPrompt(session, prompt));
        }

        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new OpenedSessionSchedule(
            session.Id,
            session.LocationReminderDueAtUtc,
            session.ExpiresAtUtc,
            promptIntentIds);
    }

    private Guid AddPrompt(GuidedIntakeSession session, IntakePrompt prompt)
    {
        var intent = IntakePromptIntent.Create(
            session,
            prompt,
            IntakeLanguage.English,
            DateTimeOffset.UtcNow);
        database.IntakePromptIntents.Add(intent);
        return intent.Id;
    }
}

public static class RemindMissingLocationHandler
{
    public static async Task Handle(
        RemindMissingLocation command,
        First10DbContext database,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var session = await database.GuidedIntakeSessions
            .Include(x => x.Inputs)
            .SingleOrDefaultAsync(x => x.Id == command.SessionId, cancellationToken);
        if (session?.TryScheduleLocationReminder(DateTimeOffset.UtcNow) == true)
        {
            var intent = IntakePromptIntent.Create(
                session,
                IntakePrompt.RemindLocation,
                IntakeLanguage.English,
                DateTimeOffset.UtcNow);
            database.IntakePromptIntents.Add(intent);
            await bus.PublishAsync(new DeliverIntakePrompt(intent.Id));
        }
    }
}

public static class ExpireGuidedSessionHandler
{
    public static async Task Handle(
        ExpireGuidedSession command,
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var session = await database.GuidedIntakeSessions
            .Include(x => x.Inputs)
            .SingleOrDefaultAsync(x => x.Id == command.SessionId, cancellationToken);
        session?.TryExpire(DateTimeOffset.UtcNow);
    }
}
