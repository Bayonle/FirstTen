using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using System.Data;

namespace First10.Infrastructure.Modules.Intake;

public static class AcceptInboundEnvelopeHandler
{
    public static async Task Handle(
        AcceptInboundEnvelope command,
        GuidedIntakeProcessor processor,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var outcome = await processor.ApplyAsync(command.Envelope, cancellationToken);
        if (outcome is not null)
        {
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
    DateTimeOffset ExpiresAtUtc);

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
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return null;
        }

        session = GuidedIntakeSession.Open(Guid.NewGuid(), envelope, CollectionWindow);
        database.GuidedIntakeSessions.Add(session);
        AddPrompt(session, IntakePrompt.Acknowledgement);
        foreach (var prompt in session.PendingPrompts)
        {
            AddPrompt(session, prompt);
        }

        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new OpenedSessionSchedule(
            session.Id,
            session.LocationReminderDueAtUtc,
            session.ExpiresAtUtc);
    }

    private void AddPrompt(GuidedIntakeSession session, IntakePrompt prompt) =>
        database.IntakePromptIntents.Add(IntakePromptIntent.Create(
            session,
            prompt,
            IntakeLanguage.English,
            DateTimeOffset.UtcNow));
}

public static class RemindMissingLocationHandler
{
    public static async Task Handle(
        RemindMissingLocation command,
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var session = await database.GuidedIntakeSessions
            .Include(x => x.Inputs)
            .SingleOrDefaultAsync(x => x.Id == command.SessionId, cancellationToken);
        if (session?.TryScheduleLocationReminder(DateTimeOffset.UtcNow) == true)
        {
            database.IntakePromptIntents.Add(IntakePromptIntent.Create(
                session,
                IntakePrompt.RemindLocation,
                IntakeLanguage.English,
                DateTimeOffset.UtcNow));
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
