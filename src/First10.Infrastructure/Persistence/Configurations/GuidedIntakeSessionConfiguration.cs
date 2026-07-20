using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class GuidedIntakeSessionConfiguration : IEntityTypeConfiguration<GuidedIntakeSession>
{
    public void Configure(EntityTypeBuilder<GuidedIntakeSession> builder)
    {
        builder.ToTable("guided_sessions", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Channel, x.ReporterKey, x.OpenedAtUtc });
        builder.Property(x => x.ReporterKey).HasMaxLength(64);
        builder.Property(x => x.CorrelationKey).HasMaxLength(64);
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(48);
        builder.HasMany(x => x.Inputs)
            .WithOne()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Inputs).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.PendingPrompts);
        builder.Ignore(x => x.VisibleGaps);
    }
}

internal sealed class GuidedSessionInputConfiguration : IEntityTypeConfiguration<GuidedSessionInput>
{
    public void Configure(EntityTypeBuilder<GuidedSessionInput> builder)
    {
        builder.ToTable("guided_session_inputs", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SessionId, x.ProviderMessageId }).IsUnique();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(256);
        builder.Property(x => x.ContentKind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ProviderMediaHandle).HasMaxLength(512);
    }
}

internal sealed class IntakePromptIntentConfiguration : IEntityTypeConfiguration<IntakePromptIntent>
{
    public void Configure(EntityTypeBuilder<IntakePromptIntent> builder)
    {
        builder.ToTable("prompt_intents", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SessionId, x.Prompt }).IsUnique();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Prompt).HasConversion<string>().HasMaxLength(48);
        builder.Property(x => x.Language).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CatalogueVersion).HasMaxLength(32);
        builder.Property(x => x.DeliveryStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(512);
        builder.HasIndex(x => new { x.Channel, x.ContactReference, x.ProviderMessageId }).IsUnique();
        builder.Property(x => x.FailureCode).HasMaxLength(80);
    }
}

internal sealed class IntakeRecoveryItemConfiguration : IEntityTypeConfiguration<IntakeRecoveryItem>
{
    public void Configure(EntityTypeBuilder<IntakeRecoveryItem> builder)
    {
        builder.ToTable("recovery_items", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Channel, x.ProviderMessageId, x.Reason }).IsUnique();
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(512);
        builder.Property(x => x.Reason).HasMaxLength(80);
        builder.Property(x => x.ReporterKey).HasMaxLength(64);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
    }
}
