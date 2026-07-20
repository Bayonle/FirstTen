using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class IncidentLocationConfiguration : IEntityTypeConfiguration<IncidentLocation>
{
    public void Configure(EntityTypeBuilder<IncidentLocation> builder)
    {
        builder.ToTable("incident_locations", "incidents");
        builder.HasKey(x => x.IncidentId);
        builder.Property(x => x.Source).HasMaxLength(80);
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
