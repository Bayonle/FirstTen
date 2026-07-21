using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.Recognition;
using Microsoft.EntityFrameworkCore;

namespace First10.Api.Endpoints.Recognition;

public static class RecognitionEndpoints
{
    public static IEndpointRouteBuilder MapRecognitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/recognition")
            .RequireAuthorization(IdentityConfiguration.AdministratorPolicy)
            .WithTags("Recognition");
        group.MapGet("/aggregates", AggregatesAsync);
        group.MapGet("/reconciliation", ReconciliationAsync);
        return endpoints;
    }

    private static async Task<IResult> AggregatesAsync(
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var awards = await database.ContributionRecognitionAwards.AsNoTracking()
            .Select(x => new { x.Id, x.ReporterKey, x.ReviewedIncidentLga })
            .ToArrayAsync(cancellationToken);
        var consent = await database.RecognitionConsentDecisions.AsNoTracking()
            .OrderBy(x => x.DecidedAtUtc).ThenBy(x => x.Id)
            .Select(x => new { x.ReporterKey, x.Choice })
            .ToArrayAsync(cancellationToken);
        var adjustments = await database.RecognitionAwardAdjustments.AsNoTracking()
            .OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
            .Select(x => new { x.AwardId, x.Kind })
            .ToArrayAsync(cancellationToken);
        var optedIn = consent.GroupBy(x => x.ReporterKey, StringComparer.Ordinal)
            .Where(group => group.Last().Choice == RecognitionConsentChoice.OptIn)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var inactive = adjustments.GroupBy(x => x.AwardId)
            .Where(group => group.Last().Kind == RecognitionAdjustmentKind.Invalidate)
            .Select(group => group.Key)
            .ToHashSet();
        var result = awards
            .Where(x => optedIn.Contains(x.ReporterKey) && !inactive.Contains(x.Id))
            .GroupBy(x => x.ReviewedIncidentLga, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { lga = group.Key, count = group.Count() })
            .OrderByDescending(x => x.count)
            .ThenBy(x => x.lga, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Results.Ok(result);
    }

    private static async Task<IResult> ReconciliationAsync(
        First10DbContext database,
        CancellationToken cancellationToken)
    {
        var awardCount = await database.ContributionRecognitionAwards.CountAsync(cancellationToken);
        var distinctContributionCount = await database.ContributionRecognitionAwards
            .Select(x => x.ContributionId).Distinct().CountAsync(cancellationToken);
        var notificationCount = await database.RecognitionNotificationIntents.CountAsync(cancellationToken);
        return Results.Ok(new
        {
            awardCount,
            distinctContributionCount,
            notificationCount,
            reconciled = awardCount == distinctContributionCount && awardCount == notificationCount,
            serviceHoursEnabled = RecognitionPolicy.SupportsServiceHours,
            monetaryValueEnabled = RecognitionPolicy.SupportsMonetaryValue
        });
    }
}
