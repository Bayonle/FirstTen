using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using Microsoft.EntityFrameworkCore;

namespace First10.IntegrationTests.IdentityAudit;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class AuditIntegrityTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task ChainedAuditEventsDetectDatabaseTampering()
    {
        var options = PersistenceConfiguration.CreateOptions(postgres.ConnectionString);
        await using var database = new First10DbContext(options);
        await database.Database.MigrateAsync();
        await database.Database.ExecuteSqlRawAsync("DELETE FROM identity_audit.audit_events");

        await AuditWriter.AppendAsync(database, "identity.invitation.created", "admin-01", AuditPayload.Create(new Dictionary<string, object?>
        {
            ["role"] = First10Roles.Dispatcher
        }));
        await AuditWriter.AppendAsync(database, "identity.invitation.accepted", "dispatcher-01", AuditPayload.Create(new Dictionary<string, object?>
        {
            ["result"] = "accepted"
        }));

        database.ChangeTracker.Clear();
        var original = await database.AuditEvents.OrderBy(x => x.Sequence).ToListAsync();
        Assert.True(AuditChainVerifier.IsValid(original));

        await database.Database.ExecuteSqlRawAsync(
            "UPDATE identity_audit.audit_events SET \"Action\" = 'tampered' WHERE \"Sequence\" = 1");
        database.ChangeTracker.Clear();
        var tampered = await database.AuditEvents.OrderBy(x => x.Sequence).ToListAsync();

        Assert.False(AuditChainVerifier.IsValid(tampered));
    }
}
