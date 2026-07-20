using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.EntityFrameworkCore;

namespace First10.Infrastructure.Persistence;

public static class PersistenceConfiguration
{
    public static DbContextOptions<First10DbContext> CreateOptions(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = new DbContextOptionsBuilder<First10DbContext>();
        Configure(options, connectionString);
        return options.Options;
    }

    public static IServiceCollection AddFirst10Persistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextWithWolverineIntegration<First10DbContext>(options =>
            Configure(options, connectionString));

        return services;
    }

    private static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        string connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform"));
}
