using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace First10.Infrastructure.Modules.IdentityAudit;

public sealed class First10ClaimsPrincipalFactory(
    UserManager<First10User> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<First10User, IdentityRole<Guid>>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(First10User user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(
            First10ClaimTypes.SessionVersion,
            user.SessionVersion.ToString(CultureInfo.InvariantCulture)));
        return identity;
    }
}

public static class First10ClaimTypes
{
    public const string SessionVersion = "first10:session-version";
}

public sealed class CurrentSessionRequirement : IAuthorizationRequirement;

public sealed class CurrentSessionHandler(UserManager<First10User> users)
    : AuthorizationHandler<CurrentSessionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CurrentSessionRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var issuedVersion = context.User.FindFirstValue(First10ClaimTypes.SessionVersion);
        if (!Guid.TryParse(userId, out _) ||
            !int.TryParse(issuedVersion, CultureInfo.InvariantCulture, out var version))
        {
            return;
        }

        var user = await users.FindByIdAsync(userId);
        if (user is not null && SessionVersionValidator.IsCurrent(user, version))
        {
            context.Succeed(requirement);
        }
    }
}
