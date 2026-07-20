using First10.Infrastructure.Modules.Intake.Media;
using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace First10.IntegrationTests.Intake;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class MediaRetentionTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task ExpiredProviderHandleCreatesRejectedAssetRecoveryAuditAndContinuation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFirst10Persistence(postgres.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
        await database.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var session = CreatePhotoSession(now);
        var input = session.Inputs.Single();
        database.GuidedIntakeSessions.Add(session);
        await database.SaveChangesAsync();
        var publisher = new RecordingDegradationPublisher();
        var pipeline = new MediaPrivacyPipeline(
            new ProviderMediaDownloader([new ExpiredMediaSource()]),
            new RejectingRedactor(),
            new RejectingAudioSanitizer(),
            new DeletionRecordingStore());

        await ProcessIntakeMediaHandler.Handle(
            new ProcessIntakeMedia(input.Id),
            database,
            pipeline,
            publisher,
            CancellationToken.None);

        var asset = await database.IntakeMediaAssets.SingleAsync(x => x.Id == input.Id);
        Assert.Equal(MediaProcessingStatus.Rejected, asset.Status);
        Assert.Equal(MediaFailureCode.ProviderHandleExpired, asset.FailureCode);
        Assert.True(await database.IntakeRecoveryItems.AnyAsync(x =>
            x.ProviderMessageId == input.ProviderMessageId
            && x.Reason == "media_providerhandleexpired"));
        Assert.True(await database.AuditEvents.AnyAsync(x => x.Action == "intake.media.rejected"));
        Assert.Equal(MediaFailureCode.ProviderHandleExpired, publisher.Published.Single().FailureCode);
    }

    [Fact]
    public async Task ExpiredSafeObjectIsDeletedAndAuditedWithoutRetainingItsKey()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFirst10Persistence(postgres.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
        await database.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var session = CreatePhotoSession(now.AddDays(-31));
        var input = session.Inputs.Single();
        var asset = IntakeMediaAsset.Create(input.Id, session.Id, IntakeMediaKind.Image, now.AddDays(-31));
        var objectKey = MediaPrivacyPolicy.CreateOpaqueObjectKey(asset.Id, asset.Kind);
        asset.TryMarkStored(
            objectKey,
            "image/jpeg",
            1024,
            "test-v1",
            "test-redactor-v1",
            now.AddDays(-31),
            now.AddMinutes(-1));
        database.GuidedIntakeSessions.Add(session);
        database.IntakeMediaAssets.Add(asset);
        await database.SaveChangesAsync();
        var store = new DeletionRecordingStore();

        await DeleteExpiredMediaHandler.Handle(
            new DeleteExpiredMedia(now),
            database,
            store,
            CancellationToken.None);

        Assert.Equal([objectKey], store.DeletedKeys);
        Assert.Equal(MediaProcessingStatus.Deleted, asset.Status);
        Assert.Null(asset.SafeObjectKey);
        Assert.True(await database.AuditEvents.AnyAsync(x =>
            x.Action == "intake.media.deleted" && x.ActorId == "worker:retention"));
    }

    private static GuidedIntakeSession CreatePhotoSession(DateTimeOffset occurredAtUtc) =>
        GuidedIntakeSession.Open(
            Guid.NewGuid(),
            new InboundChannelEnvelope
            {
                SchemaVersion = 1,
                Channel = IntakeChannel.Telegram,
                ProviderMessageId = $"retention-{Guid.NewGuid():N}",
                ReporterKey = "retention-reporter",
                ContactReference = Guid.NewGuid(),
                ContentKind = IntakeContentKind.Photo,
                ProviderMediaHandle = "opaque-handle",
                OccurredAtUtc = occurredAtUtc,
                CorrelationKey = "retention-correlation"
            },
            TimeSpan.FromMinutes(2));

    private sealed class ExpiredMediaSource : IProviderMediaSource
    {
        public IntakeChannel Channel => IntakeChannel.Telegram;

        public Task<ProviderMediaContent> OpenAsync(
            string providerMediaHandle,
            IntakeMediaKind kind,
            CancellationToken cancellationToken = default) =>
            throw new MediaPrivacyException(MediaFailureCode.ProviderHandleExpired);
    }

    private sealed class RejectingRedactor : IImageRedactor
    {
        public Task<RedactedImage> RedactAsync(
            ReadOnlyMemory<byte> rawImage,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Redactor must not run for an expired handle.");
    }

    private sealed class RejectingAudioSanitizer : IAudioSanitizer
    {
        public Task<SanitizedAudio> SanitizeAsync(
            ReadOnlyMemory<byte> rawAudio,
            string sourceContentType,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Audio sanitizer must not run for an expired handle.");
    }

    private sealed class RecordingDegradationPublisher : IMediaDegradationPublisher
    {
        public List<MediaProcessingDegraded> Published { get; } = [];

        public ValueTask PublishAsync(
            MediaProcessingDegraded message,
            CancellationToken cancellationToken = default)
        {
            Published.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeletionRecordingStore : ISafeMediaStore
    {
        public List<string> DeletedKeys { get; } = [];

        public Task<SafeMediaObject> StoreAsync(
            Guid assetId,
            IntakeMediaKind kind,
            ReadOnlyMemory<byte> safeDerivative,
            string contentType,
            DateTimeOffset expiresAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(objectKey);
            return Task.CompletedTask;
        }
    }
}
