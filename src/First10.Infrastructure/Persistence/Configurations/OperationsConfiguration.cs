using First10.Modules.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class PilotGateEvidenceConfiguration : IEntityTypeConfiguration<PilotGateEvidence>
{
    public void Configure(EntityTypeBuilder<PilotGateEvidence> builder)
    {
        builder.ToTable("pilot_gate_evidence", "operations");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Type, x.RecordedAtUtc });
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.EvidenceReference).HasMaxLength(512);
        builder.Property(x => x.RecordedBy).HasMaxLength(128);
    }
}
