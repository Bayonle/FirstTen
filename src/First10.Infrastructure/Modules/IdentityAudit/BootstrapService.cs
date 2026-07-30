using System.Security.Cryptography;
using System.Text;
using System.Data;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace First10.Infrastructure.Modules.IdentityAudit;

public sealed class BootstrapService(
    First10DbContext database,
    InvitationService invitations,
    IConfiguration configuration)
{
    public async Task<CreatedInvitation?> TryCreateFirstAdministratorAsync(
        string email,
        string suppliedSecret,
        CancellationToken cancellationToken = default)
    {
        var configuredSecret = configuration["Security:BootstrapSecret"];
        if (string.IsNullOrWhiteSpace(configuredSecret) || !SecretsMatch(configuredSecret, suppliedSecret))
        {
            return null;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await database.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(683377601)",
            cancellationToken);
        if (await database.Users.AnyAsync(cancellationToken))
        {
            return null;
        }

        var invitation = await invitations.CreateAsync(
            email,
            First10Roles.Administrator,
            "bootstrap",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invitation;
    }

    private static bool SecretsMatch(string expected, string supplied)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}
