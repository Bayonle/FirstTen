using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace First10.Infrastructure.Modules.Intake.Media;

public static class MediaProcessingDegradedHandler
{
    public static async Task Handle(
        MediaProcessingDegraded message,
        First10DbContext database,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var session = await database.GuidedIntakeSessions.SingleOrDefaultAsync(
            x => x.Id == message.SessionId,
            cancellationToken);
        if (session is null)
        {
            return;
        }

        var prompt = (await database.Set<GuidedSessionInput>().SingleAsync(
            x => x.Id == message.InputId,
            cancellationToken)).ContentKind == IntakeContentKind.Photo
            ? IntakePrompt.ContinueWithoutImage
            : IntakePrompt.ContinueWithoutAudio;
        var existing = await database.IntakePromptIntents.SingleOrDefaultAsync(
            x => x.SessionId == session.Id && x.Prompt == prompt,
            cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var intent = IntakePromptIntent.Create(
            session,
            prompt,
            IntakeLanguage.English,
            DateTimeOffset.UtcNow);
        database.IntakePromptIntents.Add(intent);
        await bus.PublishAsync(new DeliverIntakePrompt(intent.Id));
    }
}
