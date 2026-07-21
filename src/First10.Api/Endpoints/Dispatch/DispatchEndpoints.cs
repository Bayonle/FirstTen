using System.Security.Claims;
using First10.Infrastructure.Modules.Dispatch;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.Dispatch;
using First10.Modules.Guidance;
using First10.Modules.IdentityAudit;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace First10.Api.Endpoints.Dispatch;

public static class DispatchEndpoints
{
    public static IEndpointRouteBuilder MapDispatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/incidents/{incidentId:guid}/dispatch", TransitionAsync)
            .RequireAuthorization(IdentityConfiguration.DispatcherPolicy)
            .WithTags("Dispatch")
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        return endpoints;
    }

    private static async Task<IResult> TransitionAsync(
        Guid incidentId,
        DispatchTransitionRequest request,
        DispatchTransitionProcessor processor,
        First10DbContext database,
        IMessageBus bus,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DispatchStatus>(request.TargetStatus, true, out var target))
        {
            return Endpoints.Incidents.Problems.Validation("targetStatus", "Unknown dispatch status.");
        }

        var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var outcome = await processor.ApplyAsync(new TransitionIncidentDispatch(
            request.TransitionId,
            incidentId,
            target,
            request.ExpectedVersion,
            actor,
            First10Roles.Dispatcher,
            request.Reason), cancellationToken);
        if (outcome.Result == DispatchTransitionResult.IncidentNotFound)
        {
            return Results.NotFound();
        }

        if (outcome.Result != DispatchTransitionResult.Applied)
        {
            var current = await database.IncidentDispatches.AsNoTracking()
                .Where(x => x.IncidentId == incidentId)
                .Select(x => new { status = x.Status.ToString(), version = x.Version })
                .SingleOrDefaultAsync(cancellationToken);
            return Results.Conflict(new { result = outcome.Result.ToString(), current });
        }

        foreach (var intentId in outcome.DeliveryIntentIds)
        {
            await bus.PublishAsync(new DeliverGuidanceIntent(intentId));
        }

        var state = await database.IncidentDispatches.AsNoTracking()
            .Where(x => x.IncidentId == incidentId)
            .Select(x => new { status = x.Status.ToString(), version = x.Version })
            .SingleAsync(cancellationToken);
        await bus.PublishAsync(new IncidentChanged(incidentId, state.version, "dispatch", DateTimeOffset.UtcNow));
        return Results.Ok(new { incidentId, state.status, state.version, outcome.DeliveryIntentIds });
    }
}

public sealed record DispatchTransitionRequest(
    Guid TransitionId,
    string TargetStatus,
    int ExpectedVersion,
    string? Reason);
