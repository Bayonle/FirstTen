using First10.Modules.IdentityAudit;
using Microsoft.AspNetCore.Identity;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace First10.Infrastructure.Modules.IdentityAudit;

public static class IdentityRoleSeeder
{
    public static async Task SeedAsync(First10DbContext database, CancellationToken cancellationToken = default)
    {
        foreach (var roleName in First10Roles.All)
        {
            var normalizedName = roleName.ToUpperInvariant();
            if (await database.Roles.AnyAsync(x => x.NormalizedName == normalizedName, cancellationToken))
            {
                continue;
            }

            database.Roles.Add(new IdentityRole<Guid>(roleName)
            {
                Id = Guid.NewGuid(),
                NormalizedName = normalizedName
            });
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var roleName in First10Roles.All)
        {
            if (await roles.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roles.CreateAsync(new IdentityRole<Guid>(roleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join(", ", result.Errors.Select(x => x.Description)));
            }
        }
    }
}
