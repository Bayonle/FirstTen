using First10.Modules.BuildingBlocks.Persistence;
using First10.Modules.Incidents;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace First10.Infrastructure.Persistence;

public sealed class First10DbContext(DbContextOptions<First10DbContext> options) : DbContext(options)
{
    public DbSet<InboundMessageReceipt> InboundMessageReceipts => Set<InboundMessageReceipt>();

    public DbSet<OutboundMessageRecord> OutboundMessageRecords => Set<OutboundMessageRecord>();

    public DbSet<IncidentTimelineEvent> IncidentTimelineEvents => Set<IncidentTimelineEvent>();

    public DbSet<IncidentLocation> IncidentLocations => Set<IncidentLocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(First10DbContext).Assembly);
        modelBuilder.MapWolverineEnvelopeStorage();
    }
}
