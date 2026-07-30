using First10.Modules.IdentityAudit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class AuditAnchorConfiguration : IEntityTypeConfiguration<AuditAnchor>
{
    public void Configure(EntityTypeBuilder<AuditAnchor> builder)
    {
        builder.ToTable("audit_anchors", "identity_audit");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.LastSequence).IsUnique();
        builder.Property(x => x.LastHash).HasMaxLength(64);
        builder.Property(x => x.ExternalReference).HasMaxLength(512);
        builder.Property(x => x.RecordedBy).HasMaxLength(128);
    }
}
