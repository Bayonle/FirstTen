using First10.Infrastructure.Modules.IdentityAudit;
using First10.Modules.BuildingBlocks.Persistence;
using First10.Modules.Incidents;
using First10.Modules.Intake;
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

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<OutboundMessageRecord> OutboundMessageRecords => Set<OutboundMessageRecord>();

    public DbSet<IncidentTimelineEvent> IncidentTimelineEvents => Set<IncidentTimelineEvent>();

    public DbSet<IncidentLocation> IncidentLocations => Set<IncidentLocation>();

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
