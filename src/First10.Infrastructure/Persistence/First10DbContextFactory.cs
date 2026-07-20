using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace First10.Infrastructure.Persistence;

public sealed class First10DbContextFactory : IDesignTimeDbContextFactory<First10DbContext>
{
    public First10DbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? "Host=localhost;Port=5432;Database=first10;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<First10DbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "platform"))
            .Options;

        return new First10DbContext(options);
    }
}
