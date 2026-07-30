using Amazon.S3;
using Amazon.S3.Model;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace First10.Infrastructure.Modules.Operations;

public static class ReadinessChecks
{
    public static IServiceCollection AddFirst10ReadinessChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<First10DatabaseHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<First10ObjectStorageHealthCheck>("object-storage", tags: ["ready"]);
        return services;
    }
}

public sealed class First10DatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("PostgreSQL rejected the readiness probe.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL readiness probe failed.", exception);
        }
    }
}

public sealed class First10ObjectStorageHealthCheck(
    IAmazonS3 storage,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var bucket = configuration["ObjectStorage:SafeMediaBucket"] ?? "first10-safe-media";
            await storage.GetBucketLocationAsync(
                new GetBucketLocationRequest { BucketName = bucket },
                cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Safe-media object storage readiness probe failed.", exception);
        }
    }
}
