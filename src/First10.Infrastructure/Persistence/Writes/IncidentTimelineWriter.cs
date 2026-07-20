using First10.Modules.Incidents;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace First10.Infrastructure.Persistence.Writes;

public static class IncidentTimelineWriter
{
    public static async Task<bool> AppendLocationClaimAsync(
        First10DbContext database,
        LocationClaim claim,
        CancellationToken cancellationToken = default)
    {
        if (await database.IncidentTimelineEvents.AnyAsync(x => x.EventId == claim.EventId, cancellationToken))
        {
            return false;
        }

        database.IncidentTimelineEvents.Add(IncidentTimelineEvent.From(claim));

        var location = await database.IncidentLocations.SingleOrDefaultAsync(
            x => x.IncidentId == claim.IncidentId,
            cancellationToken);
        if (location is null)
        {
            database.IncidentLocations.Add(IncidentLocation.From(claim));
        }
        else
        {
            location.Apply(claim);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            database.ChangeTracker.Clear();
            return false;
        }
    }
}
