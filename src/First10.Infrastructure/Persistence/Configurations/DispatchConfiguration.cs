using First10.Modules.Dispatch;
using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class IncidentDispatchConfiguration : IEntityTypeConfiguration<IncidentDispatch>
{
    public void Configure(EntityTypeBuilder<IncidentDispatch> builder)
    {
        builder.ToTable("incident_dispatches", "dispatch");
        builder.HasKey(x => x.IncidentId);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.LastReopenReason).HasMaxLength(240);
        builder.HasOne<Incident>()
            .WithOne()
            .HasForeignKey<IncidentDispatch>(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DispatchTransitionConfiguration : IEntityTypeConfiguration<DispatchTransition>
{
    public void Configure(EntityTypeBuilder<DispatchTransition> builder)
    {
        builder.ToTable("transitions", "dispatch");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.IncidentId, x.OccurredAtUtc });
        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DispatcherId).HasMaxLength(128);
        builder.Property(x => x.Reason).HasMaxLength(240);
        builder.HasOne<IncidentDispatch>()
            .WithMany()
            .HasForeignKey(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
