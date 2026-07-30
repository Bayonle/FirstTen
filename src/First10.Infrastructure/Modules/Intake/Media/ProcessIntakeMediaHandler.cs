using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using First10.Modules.Intake.Triage;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace First10.Infrastructure.Modules.Intake.Media;

public interface IMediaDegradationPublisher
{
    ValueTask PublishAsync(
        MediaProcessingDegraded message,
        CancellationToken cancellationToken = default);
}

public sealed class WolverineMediaDegradationPublisher(IMessageBus bus) : IMediaDegradationPublisher
{
    public ValueTask PublishAsync(
        MediaProcessingDegraded message,
        CancellationToken cancellationToken = default) =>
        bus.PublishAsync(message);
}

public interface ITriageKickoffPublisher
{
    ValueTask PublishAsync(TryTriageSession message, CancellationToken cancellationToken = default);
}

public sealed class WolverineTriageKickoffPublisher(IMessageBus bus) : ITriageKickoffPublisher
{
    public ValueTask PublishAsync(
        TryTriageSession message,
        CancellationToken cancellationToken = default) =>
        bus.PublishAsync(message);
}

public static class ProcessIntakeMediaHandler
{
    public static async Task Handle(
        ProcessIntakeMedia command,
        First10DbContext database,
        MediaPrivacyPipeline pipeline,
        IMediaDegradationPublisher degradationPublisher,
        ITriageKickoffPublisher triageKickoffPublisher,
        CancellationToken cancellationToken)
    {
        var session = await database.GuidedIntakeSessions
            .Include(x => x.Inputs)
            .SingleOrDefaultAsync(
                x => x.Inputs.Any(input => input.Id == command.InputId),
                cancellationToken);
        var input = session?.Inputs.SingleOrDefault(x => x.Id == command.InputId);
        if (session is null
            || input?.ProviderMediaHandle is null
            || input.ContentKind is not (IntakeContentKind.Photo or IntakeContentKind.Voice))
        {
            return;
        }

        var kind = input.ContentKind == IntakeContentKind.Photo
            ? IntakeMediaKind.Image
            : IntakeMediaKind.Audio;
        var asset = await database.IntakeMediaAssets.SingleOrDefaultAsync(
            x => x.Id == command.InputId,
            cancellationToken);
        if (asset is not null && asset.Status != MediaProcessingStatus.Pending)
        {
            return;
        }

        asset ??= IntakeMediaAsset.Create(input.Id, session.Id, kind, DateTimeOffset.UtcNow);
        if (database.Entry(asset).State == EntityState.Detached)
        {
            database.IntakeMediaAssets.Add(asset);
        }

        var expiresAtUtc = DateTimeOffset.UtcNow.Add(MediaPrivacyPolicy.DefaultRetention);
        try
        {
            var processed = await pipeline.ProcessAsync(
                asset.Id,
                session.Channel,
                input.ProviderMediaHandle,
                kind,
                expiresAtUtc,
                cancellationToken);
            var stored = processed.SafeObject;
            asset.TryMarkStored(
                stored.ObjectKey,
                stored.ContentType,
                stored.Length,
                stored.EncryptionKeyVersion,
                processed.PrivacyProcessorVersion,
                DateTimeOffset.UtcNow,
                stored.ExpiresAtUtc);
            await AppendAuditAsync(database, asset, "intake.media.sanitized", cancellationToken);
            await triageKickoffPublisher.PublishAsync(
                new TryTriageSession(session.Id),
                cancellationToken);
        }
        catch (MediaPrivacyException exception)
        {
            asset.TryReject(exception.FailureCode, DateTimeOffset.UtcNow);
            database.IntakeRecoveryItems.Add(IntakeRecoveryItem.Create(
                session.Channel,
                input.ProviderMessageId,
                $"media_{exception.FailureCode.ToString().ToLowerInvariant()}",
                DateTimeOffset.UtcNow,
                session.ContactReference,
                session.ReporterKey));
            await degradationPublisher.PublishAsync(new MediaProcessingDegraded(
                session.Id,
                input.Id,
                exception.FailureCode), cancellationToken);
            await AppendAuditAsync(database, asset, "intake.media.rejected", cancellationToken);
        }
    }

    private static Task<AuditEvent> AppendAuditAsync(
        First10DbContext database,
        IntakeMediaAsset asset,
        string action,
        CancellationToken cancellationToken) =>
        AuditWriter.AppendAsync(
            database,
            action,
            "worker:media",
            AuditPayload.Create(new Dictionary<string, object?>
            {
                ["assetId"] = asset.Id,
                ["inputId"] = asset.InputId,
                ["kind"] = asset.Kind.ToString(),
                ["result"] = asset.Status.ToString(),
                ["failureCode"] = asset.FailureCode?.ToString(),
                ["processorVersion"] = asset.PrivacyProcessorVersion
            }),
            cancellationToken);
}
