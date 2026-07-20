using First10.Modules.IdentityAudit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events", "identity_audit");
        builder.HasKey(x => x.Sequence);
        builder.Property(x => x.Sequence).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(160);
        builder.Property(x => x.ActorId).HasMaxLength(128);
        builder.Property(x => x.PayloadJson).HasColumnType("text");
        builder.Property(x => x.PreviousHash).HasMaxLength(64);
        builder.Property(x => x.Hash).HasMaxLength(64);
    }
}
