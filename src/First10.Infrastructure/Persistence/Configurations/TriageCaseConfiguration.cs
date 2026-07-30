using First10.Modules.Intake.Triage;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class TriageCaseConfiguration : IEntityTypeConfiguration<TriageCase>
{
    public void Configure(EntityTypeBuilder<TriageCase> builder)
    {
        builder.ToTable("triage_cases", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.SessionId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.DeadlineAtUtc });
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsConcurrencyToken();
        builder.Property(x => x.AuthoritativeVersion).IsConcurrencyToken();
        builder.HasOne<GuidedIntakeSession>()
            .WithOne()
            .HasForeignKey<TriageCase>(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Assessments)
            .WithOne()
            .HasForeignKey(x => x.TriageCaseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Assessments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TriageAssessmentConfiguration : IEntityTypeConfiguration<TriageAssessment>
{
    public void Configure(EntityTypeBuilder<TriageAssessment> builder)
    {
        builder.ToTable("triage_assessments", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TriageCaseId, x.ReceivedAtUtc });
        builder.Property(x => x.IncidentType).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Language).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Uncertainty).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.GuidanceCategory).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.LocationPhrase).HasMaxLength(512);
        builder.Property(x => x.EvidenceReferences).HasMaxLength(1024);
        builder.Property(x => x.ModelConfiguration).HasMaxLength(160);
        builder.Property(x => x.ResolvedLandmarkId).HasMaxLength(128);
        builder.Property(x => x.ResolvedDirection).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.LocationEvidenceReference).HasMaxLength(160);
    }
}

internal sealed class ManualTriageAlertConfiguration : IEntityTypeConfiguration<ManualTriageAlert>
{
    public void Configure(EntityTypeBuilder<ManualTriageAlert> builder)
    {
        builder.ToTable("manual_triage_alerts", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TriageCaseId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.Priority, x.CreatedAtUtc });
        builder.Property(x => x.Message).HasMaxLength(256);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasOne<TriageCase>()
            .WithOne()
            .HasForeignKey<ManualTriageAlert>(x => x.TriageCaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
