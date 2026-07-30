using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class ReporterContactConfiguration : IEntityTypeConfiguration<ReporterContact>
{
    public void Configure(EntityTypeBuilder<ReporterContact> builder)
    {
        builder.ToTable("reporter_contacts", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Channel, x.ReporterKey }).IsUnique();
        builder.Property(x => x.ReporterKey).HasMaxLength(64);
        builder.Property(x => x.ProtectedDestination).HasColumnType("text");
        builder.Property(x => x.EncryptionKeyVersion).HasMaxLength(32);
    }
}
