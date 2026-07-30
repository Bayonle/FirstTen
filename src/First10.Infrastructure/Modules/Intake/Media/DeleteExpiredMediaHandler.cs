using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using First10.Modules.Intake.Media;
using Microsoft.EntityFrameworkCore;

namespace First10.Infrastructure.Modules.Intake.Media;

public sealed record DeleteExpiredMedia(DateTimeOffset RequestedAtUtc);

public static class DeleteExpiredMediaHandler
{
    public static async Task Handle(
        DeleteExpiredMedia command,
        First10DbContext database,
        ISafeMediaStore store,
        CancellationToken cancellationToken)
    {
        var expired = await database.IntakeMediaAssets
            .Where(x => x.Status == MediaProcessingStatus.Stored
                        && x.ExpiresAtUtc <= command.RequestedAtUtc)
            .OrderBy(x => x.ExpiresAtUtc)
            .Take(100)
            .ToArrayAsync(cancellationToken);
        foreach (var asset in expired)
        {
            if (asset.SafeObjectKey is null)
            {
                throw new InvalidOperationException("Stored media is missing its safe object key.");
            }

            await store.DeleteAsync(asset.SafeObjectKey, cancellationToken);
            if (!asset.TryMarkDeleted(command.RequestedAtUtc))
            {
                continue;
            }

            await AuditWriter.AppendAsync(
                database,
                "intake.media.deleted",
                "worker:retention",
                AuditPayload.Create(new Dictionary<string, object?>
                {
                    ["assetId"] = asset.Id,
                    ["kind"] = asset.Kind.ToString(),
                    ["expiredAtUtc"] = asset.ExpiresAtUtc
                }),
                cancellationToken);
        }
    }
}
