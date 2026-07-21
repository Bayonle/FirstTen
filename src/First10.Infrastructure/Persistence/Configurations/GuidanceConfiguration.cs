using First10.Modules.Guidance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class GuidanceTemplateSetConfiguration : IEntityTypeConfiguration<GuidanceTemplateSet>
{
    public void Configure(EntityTypeBuilder<GuidanceTemplateSet> builder)
    {
        builder.ToTable("template_sets", "guidance");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new
        {
            x.TemplateKey,
            x.PolicyVersion
        }).IsUnique();
        builder.HasIndex(x => new { x.Purpose, x.Trigger, x.Category, x.SeverityBand, x.EnabledAtUtc });
        builder.Property(x => x.TemplateKey).HasMaxLength(96);
        builder.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.SeverityBand).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PolicyVersion).HasMaxLength(48);
        builder.Property(x => x.Trigger).HasMaxLength(48);
        builder.Property(x => x.EligibilityContext).HasMaxLength(512);
        builder.Property(x => x.ApprovedBy).HasMaxLength(128);
        builder.HasMany(x => x.Assets)
            .WithOne()
            .HasForeignKey(x => x.TemplateSetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Assets).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.IsEnabled);
    }
}

internal sealed class GuidanceTemplateAssetConfiguration : IEntityTypeConfiguration<GuidanceTemplateAsset>
{
    public void Configure(EntityTypeBuilder<GuidanceTemplateAsset> builder)
    {
        builder.ToTable("template_assets", "guidance");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TemplateSetId, x.Language }).IsUnique();
        builder.Property(x => x.Language).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ExactText).HasColumnType("text");
        builder.Property(x => x.TextSha256).HasMaxLength(64);
        builder.Property(x => x.VoiceAssetKey).HasMaxLength(240);
        builder.Property(x => x.VoiceSha256).HasMaxLength(64);
        builder.Property(x => x.SpeechModel).HasMaxLength(80);
        builder.Property(x => x.SpeechVoice).HasMaxLength(80);
        builder.Property(x => x.SpeechSettings).HasColumnType("jsonb");
        builder.Ignore(x => x.HasValidChecksums);
    }
}

internal sealed class GuidanceIntentConfiguration : IEntityTypeConfiguration<GuidanceIntent>
{
    public void Configure(EntityTypeBuilder<GuidanceIntent> builder)
    {
        builder.ToTable("intents", "guidance");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.SemanticKey).IsUnique();
        builder.HasIndex(x => new { x.Status, x.DeadlineAtUtc });
        builder.Property(x => x.SemanticKey).HasMaxLength(240);
        builder.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Trigger).HasMaxLength(48);
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.Language).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PolicyVersion).HasMaxLength(48);
        builder.Property(x => x.ExactText).HasColumnType("text");
        builder.Property(x => x.TextSha256).HasMaxLength(64);
        builder.Property(x => x.VoiceAssetKey).HasMaxLength(240);
        builder.Property(x => x.VoiceSha256).HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(48);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(160);
        builder.Property(x => x.FailureCode).HasMaxLength(80);
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.Ignore(x => x.CanAttemptDelivery);
    }
}

internal sealed class GuidanceDeliveryAttemptConfiguration : IEntityTypeConfiguration<GuidanceDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<GuidanceDeliveryAttempt> builder)
    {
        builder.ToTable("delivery_attempts", "guidance");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.GuidanceIntentId, x.Component, x.AttemptNumber }).IsUnique();
        builder.HasIndex(x => x.ProviderMessageId)
            .IsUnique()
            .HasFilter("\"ProviderMessageId\" IS NOT NULL");
        builder.Property(x => x.Component).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsConcurrencyToken();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(160);
        builder.Property(x => x.FailureCode).HasMaxLength(80);
        builder.HasOne<GuidanceIntent>()
            .WithMany()
            .HasForeignKey(x => x.GuidanceIntentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
