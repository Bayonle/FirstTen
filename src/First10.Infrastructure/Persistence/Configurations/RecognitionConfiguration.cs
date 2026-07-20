using First10.Modules.Recognition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class ContributionRecognitionAwardConfiguration
    : IEntityTypeConfiguration<ContributionRecognitionAward>
{
    public void Configure(EntityTypeBuilder<ContributionRecognitionAward> builder)
    {
        builder.ToTable("contribution_awards", "recognition");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ContributionId).IsUnique();
        builder.HasIndex(x => new { x.ReviewedIncidentLga, x.AwardedAtUtc });
        builder.Property(x => x.ReporterKey).HasMaxLength(128);
        builder.Property(x => x.ReviewedIncidentLga).HasMaxLength(96);
        builder.Property(x => x.BadgeKey).HasMaxLength(64);
        builder.Property(x => x.PolicyVersion).HasMaxLength(64);
    }
}

internal sealed class RecognitionConsentDecisionConfiguration
    : IEntityTypeConfiguration<RecognitionConsentDecision>
{
    public void Configure(EntityTypeBuilder<RecognitionConsentDecision> builder)
    {
        builder.ToTable("consent_decisions", "recognition");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.SemanticKey).IsUnique();
        builder.HasIndex(x => new { x.ReporterKey, x.DecidedAtUtc });
        builder.Property(x => x.SemanticKey).HasMaxLength(128);
        builder.Property(x => x.ReporterKey).HasMaxLength(128);
        builder.Property(x => x.Choice).HasConversion<string>().HasMaxLength(24);
    }
}

internal sealed class RecognitionAwardAdjustmentConfiguration
    : IEntityTypeConfiguration<RecognitionAwardAdjustment>
{
    public void Configure(EntityTypeBuilder<RecognitionAwardAdjustment> builder)
    {
        builder.ToTable("award_adjustments", "recognition");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.AwardId, x.OccurredAtUtc });
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.ReasonCode).HasMaxLength(96);
    }
}

internal sealed class RecognitionNotificationIntentConfiguration
    : IEntityTypeConfiguration<RecognitionNotificationIntent>
{
    public void Configure(EntityTypeBuilder<RecognitionNotificationIntent> builder)
    {
        builder.ToTable("notification_intents", "recognition");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.AwardId).IsUnique();
        builder.Property(x => x.Channel).HasMaxLength(24);
        builder.Property(x => x.Language).HasMaxLength(32);
        builder.Property(x => x.ExactText).HasMaxLength(1024);
    }
}
