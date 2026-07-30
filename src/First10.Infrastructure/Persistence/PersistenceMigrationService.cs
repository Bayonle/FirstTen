using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using First10.Infrastructure.Modules.IdentityAudit;

namespace First10.Infrastructure.Persistence;

public sealed class PersistenceMigrationService(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        await IdentityRoleSeeder.SeedAsync(database, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
