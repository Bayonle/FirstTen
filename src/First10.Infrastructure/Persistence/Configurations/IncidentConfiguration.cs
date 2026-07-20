using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents", "incidents");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.VerificationStatus, x.ReviewDueAtUtc });
        builder.Property(x => x.VerificationStatus).HasConversion<string>().HasMaxLength(48).IsConcurrencyToken();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.RejectionReason).HasMaxLength(240);
        builder.HasMany(x => x.SourceReports)
            .WithOne()
            .HasForeignKey(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.SourceReports).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Conflicts)
            .WithOne()
            .HasForeignKey(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Conflicts).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Observations)
            .WithOne()
            .HasForeignKey(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Observations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.IndependentReporterCount);
        builder.Ignore(x => x.VerifiedIndependentReporterCount);
        builder.Ignore(x => x.HasUnresolvedConflicts);
    }
}

internal sealed class IncidentSourceReportConfiguration : IEntityTypeConfiguration<IncidentSourceReport>
{
    public void Configure(EntityTypeBuilder<IncidentSourceReport> builder)
    {
        builder.ToTable("source_reports", "incidents");
        builder.HasKey(x => x.ReportId);
        builder.HasIndex(x => new { x.IncidentId, x.OccurredAtUtc });
        builder.Property(x => x.ReporterIndependenceKey).HasMaxLength(64);
        builder.Property(x => x.VerifiedPilotIdentityKey).HasMaxLength(64);
        builder.Property(x => x.IncidentType).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.EvidenceReferences).HasMaxLength(1024);
        builder.Property(x => x.LocationDescription).HasMaxLength(240);
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.VictimState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.SceneState).HasConversion<string>().HasMaxLength(32);
    }
}

internal sealed class IncidentConflictConfiguration : IEntityTypeConfiguration<IncidentConflict>
{
    public void Configure(EntityTypeBuilder<IncidentConflict> builder)
    {
        builder.ToTable("conflicts", "incidents");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.IncidentId, x.Field, x.LeftClaimId, x.RightClaimId }).IsUnique();
        builder.Property(x => x.Field).HasConversion<string>().HasMaxLength(48);
        builder.Property(x => x.LeftValue).HasMaxLength(128);
        builder.Property(x => x.RightValue).HasMaxLength(128);
        builder.Property(x => x.ResolvedBy).HasMaxLength(128);
        builder.Ignore(x => x.IsResolved);
    }
}

internal sealed class IncidentObservationConfiguration : IEntityTypeConfiguration<IncidentObservation>
{
    public void Configure(EntityTypeBuilder<IncidentObservation> builder)
    {
        builder.ToTable("observations", "incidents");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.IncidentId, x.OccurredAtUtc });
        builder.Property(x => x.VictimState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.SceneState).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.LocationDescription).HasMaxLength(240);
        builder.Property(x => x.EvidenceReference).HasMaxLength(160);
        builder.HasOne<IncidentSourceReport>()
            .WithMany()
            .HasForeignKey(x => x.SourceReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class IncidentCandidateLinkConfiguration : IEntityTypeConfiguration<IncidentCandidateLink>
{
    public void Configure(EntityTypeBuilder<IncidentCandidateLink> builder)
    {
        builder.ToTable("candidate_links", "incidents");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.FirstIncidentId, x.SecondIncidentId, x.Reason }).IsUnique();
        builder.Property(x => x.Reason).HasMaxLength(64);
        builder.HasOne<Incident>()
            .WithMany()
            .HasForeignKey(x => x.FirstIncidentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Incident>()
            .WithMany()
            .HasForeignKey(x => x.SecondIncidentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class IncidentReviewAlertConfiguration : IEntityTypeConfiguration<IncidentReviewAlert>
{
    public void Configure(EntityTypeBuilder<IncidentReviewAlert> builder)
    {
        builder.ToTable("review_alerts", "incidents");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.IncidentId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Reason).HasMaxLength(80);
        builder.HasOne<Incident>()
            .WithMany()
            .HasForeignKey(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
