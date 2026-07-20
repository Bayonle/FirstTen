using First10.Modules.Intake.Media;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class IntakeMediaAssetConfiguration : IEntityTypeConfiguration<IntakeMediaAsset>
{
    public void Configure(EntityTypeBuilder<IntakeMediaAsset> builder)
    {
        builder.ToTable("media_assets", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.InputId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
        builder.HasOne<GuidedSessionInput>()
            .WithOne()
            .HasForeignKey<IntakeMediaAsset>(x => x.InputId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.SafeObjectKey).HasMaxLength(160);
        builder.Property(x => x.SafeContentType).HasMaxLength(80);
        builder.Property(x => x.EncryptionKeyVersion).HasMaxLength(64);
        builder.Property(x => x.PrivacyProcessorVersion).HasMaxLength(64);
        builder.Property(x => x.FailureCode).HasConversion<string>().HasMaxLength(64);
    }
}
