using First10.Infrastructure.Modules.IdentityAudit;
using First10.Modules.BuildingBlocks.Persistence;
using First10.Modules.Incidents;
using First10.Modules.Dispatch;
using First10.Modules.Guidance;
using First10.Modules.Intake;
using First10.Modules.Intake.Media;
using First10.Modules.Intake.Triage;
using First10.Modules.IdentityAudit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace First10.Infrastructure.Persistence;

public sealed class First10DbContext(DbContextOptions<First10DbContext> options)
    : IdentityDbContext<First10User, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<InboundMessageReceipt> InboundMessageReceipts => Set<InboundMessageReceipt>();

    public DbSet<ReporterContact> ReporterContacts => Set<ReporterContact>();

    public DbSet<GuidedIntakeSession> GuidedIntakeSessions => Set<GuidedIntakeSession>();

    public DbSet<GuidedSessionInput> GuidedSessionInputs => Set<GuidedSessionInput>();

    public DbSet<IntakePromptIntent> IntakePromptIntents => Set<IntakePromptIntent>();

    public DbSet<IntakeRecoveryItem> IntakeRecoveryItems => Set<IntakeRecoveryItem>();

    public DbSet<IntakeMediaAsset> IntakeMediaAssets => Set<IntakeMediaAsset>();

    public DbSet<TriageCase> TriageCases => Set<TriageCase>();

    public DbSet<TriageAssessment> TriageAssessments => Set<TriageAssessment>();

    public DbSet<ManualTriageAlert> ManualTriageAlerts => Set<ManualTriageAlert>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<OutboundMessageRecord> OutboundMessageRecords => Set<OutboundMessageRecord>();

    public DbSet<IncidentTimelineEvent> IncidentTimelineEvents => Set<IncidentTimelineEvent>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<IncidentSourceReport> IncidentSourceReports => Set<IncidentSourceReport>();

    public DbSet<IncidentConflict> IncidentConflicts => Set<IncidentConflict>();

    public DbSet<IncidentCandidateLink> IncidentCandidateLinks => Set<IncidentCandidateLink>();

    public DbSet<IncidentReviewAlert> IncidentReviewAlerts => Set<IncidentReviewAlert>();

    public DbSet<IncidentObservation> IncidentObservations => Set<IncidentObservation>();

    public DbSet<IncidentLocation> IncidentLocations => Set<IncidentLocation>();

    public DbSet<IncidentDispatch> IncidentDispatches => Set<IncidentDispatch>();

    public DbSet<DispatchTransition> DispatchTransitions => Set<DispatchTransition>();

    public DbSet<GuidanceTemplateSet> GuidanceTemplateSets => Set<GuidanceTemplateSet>();

    public DbSet<GuidanceTemplateAsset> GuidanceTemplateAssets => Set<GuidanceTemplateAsset>();

    public DbSet<GuidanceIntent> GuidanceIntents => Set<GuidanceIntent>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(First10DbContext).Assembly);
        builder.MapWolverineEnvelopeStorage();

        builder.Entity<First10User>().ToTable("users", "identity_audit");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles", "identity_audit");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", "identity_audit");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", "identity_audit");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", "identity_audit");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", "identity_audit");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", "identity_audit");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys", "identity_audit");
    }
}
