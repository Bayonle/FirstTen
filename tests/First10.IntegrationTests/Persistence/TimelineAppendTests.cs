using First10.Infrastructure.Persistence;
using First10.Infrastructure.Persistence.Writes;
using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;

namespace First10.IntegrationTests.Persistence;

[Collection(PostgresTestGroup.Name)]
public sealed class TimelineAppendTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ConflictingLocationClaimsArePreservedAndProjectedAsAConflict()
    {
        var options = PersistenceConfiguration.CreateOptions(postgres.ConnectionString);
        await using var database = new First10DbContext(options);
        await database.Database.MigrateAsync();
        var incidentId = Guid.NewGuid();

        await IncidentTimelineWriter.AppendLocationClaimAsync(
            database,
            new LocationClaim(Guid.NewGuid(), incidentId, "reporter", 6.6018, 3.3515, DateTimeOffset.UtcNow));
        await IncidentTimelineWriter.AppendLocationClaimAsync(
            database,
            new LocationClaim(Guid.NewGuid(), incidentId, "dispatcher", 6.6120, 3.3601, DateTimeOffset.UtcNow));

        var events = await database.IncidentTimelineEvents
            .Where(x => x.IncidentId == incidentId)
            .OrderBy(x => x.OccurredAtUtc)
            .ToListAsync();
        var projection = await database.IncidentLocations.SingleAsync(x => x.IncidentId == incidentId);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, x => x.Source == "reporter");
        Assert.Contains(events, x => x.Source == "dispatcher");
        Assert.True(projection.HasConflict);
    }

    [Fact]
    public async Task ReplayingATimelineEventIsIdempotent()
    {
        var options = PersistenceConfiguration.CreateOptions(postgres.ConnectionString);
        await using var database = new First10DbContext(options);
        await database.Database.MigrateAsync();
        var claim = new LocationClaim(Guid.NewGuid(), Guid.NewGuid(), "reporter", 6.6018, 3.3515, DateTimeOffset.UtcNow);

        var firstAppend = await IncidentTimelineWriter.AppendLocationClaimAsync(database, claim);
        var replayAppend = await IncidentTimelineWriter.AppendLocationClaimAsync(database, claim);

        Assert.True(firstAppend);
        Assert.False(replayAppend);
        Assert.Equal(1, await database.IncidentTimelineEvents.CountAsync(x => x.EventId == claim.EventId));
    }

    [Fact]
    public async Task ConcurrentClaimsAreBothPreservedWithoutSilentOverwrite()
    {
        var options = PersistenceConfiguration.CreateOptions(postgres.ConnectionString);
        await using (var migrationDatabase = new First10DbContext(options))
        {
            await migrationDatabase.Database.MigrateAsync();
        }

        var incidentId = Guid.NewGuid();
        await using var seedDatabase = new First10DbContext(options);
        await IncidentTimelineWriter.AppendLocationClaimAsync(
            seedDatabase,
            new LocationClaim(Guid.NewGuid(), incidentId, "reporter", 6.6018, 3.3515, DateTimeOffset.UtcNow));

        await using var firstDatabase = new First10DbContext(options);
        await using var secondDatabase = new First10DbContext(options);
        var firstClaim = new LocationClaim(Guid.NewGuid(), incidentId, "dispatcher-a", 6.6120, 3.3601, DateTimeOffset.UtcNow);
        var secondClaim = new LocationClaim(Guid.NewGuid(), incidentId, "dispatcher-b", 6.6200, 3.3701, DateTimeOffset.UtcNow);

        var outcomes = await Task.WhenAll(
            IncidentTimelineWriter.AppendLocationClaimAsync(firstDatabase, firstClaim),
            IncidentTimelineWriter.AppendLocationClaimAsync(secondDatabase, secondClaim));

        Assert.All(outcomes, Assert.True);
        await using var verificationDatabase = new First10DbContext(options);
        Assert.Equal(3, await verificationDatabase.IncidentTimelineEvents.CountAsync(
            x => x.IncidentId == incidentId));
        Assert.True((await verificationDatabase.IncidentLocations.SingleAsync(
            x => x.IncidentId == incidentId)).HasConflict);
    }
}
