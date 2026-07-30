using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Modules.Intake.Media;
using First10.Infrastructure.Persistence;
using First10.Modules.Intake.Media;
using Microsoft.EntityFrameworkCore;

namespace First10.Api.Endpoints.Media;

public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/media/{assetId:guid}", StreamAsync)
            .RequireAuthorization(IdentityConfiguration.DispatcherPolicy)
            .WithTags("Media");
        return endpoints;
    }

    private static async Task StreamAsync(
        Guid assetId,
        First10DbContext database,
        ISafeMediaReader reader,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var asset = await database.IntakeMediaAssets.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == assetId, cancellationToken);
        if (asset is null
            || asset.Status != MediaProcessingStatus.Stored
            || asset.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || string.IsNullOrWhiteSpace(asset.SafeObjectKey)
            || string.IsNullOrWhiteSpace(asset.SafeContentType)
            || asset.SafeLength is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var content = await reader.ReadAsync(
            asset.Id,
            asset.SafeObjectKey,
            asset.SafeContentType,
            asset.SafeLength.Value,
            cancellationToken);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = content.ContentType;
        context.Response.ContentLength = content.Bytes.Length;
        context.Response.Headers.CacheControl = "no-store, private";
        context.Response.Headers.ContentDisposition = "inline";
        await context.Response.Body.WriteAsync(content.Bytes, cancellationToken);
    }
}
