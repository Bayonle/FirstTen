using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class IncidentTimelineEventConfiguration : IEntityTypeConfiguration<IncidentTimelineEvent>
{
    public void Configure(EntityTypeBuilder<IncidentTimelineEvent> builder)
    {
        builder.ToTable("incident_timeline_events", "incidents");
        builder.HasKey(x => x.EventId);
        builder.HasIndex(x => new { x.IncidentId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.IncidentId, x.ReceivedAtUtc });
        builder.Property(x => x.EventType).HasMaxLength(80);
        builder.Property(x => x.Source).HasMaxLength(80);
        builder.Property(x => x.PayloadJson)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
    }
}
