using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using First10.Infrastructure.Modules.IdentityAudit;

namespace First10.Api.Auth;

[Authorize(Policy = IdentityConfiguration.DispatcherPolicy)]
public sealed class OperationsHub : Hub;
