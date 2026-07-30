using First10.Api.Auth;
using First10.Modules.BuildingBlocks.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace First10.Api.Realtime;

public static class IncidentChangedHandler
{
    public static Task Handle(
        IncidentChanged change,
        IHubContext<OperationsHub> hub,
        CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync("incidentChanged", new
        {
            incidentId = change.IncidentId,
            version = change.Version,
            category = change.Category
        }, cancellationToken);
}
