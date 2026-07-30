using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace First10.Infrastructure.Modules.IdentityAudit;

public sealed class MfaRecoveryService(
    First10DbContext database,
    UserManager<First10User> users)
{
    public static readonly TimeSpan RecentReauthenticationWindow = TimeSpan.FromMinutes(5);

    public async Task<CreatedInvitation> BeginResetAsync(
        Guid targetUserId,
        Guid administratorUserId,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId == administratorUserId)
        {
            throw new InvalidOperationException("Administrators cannot use assisted recovery on their own account.");
        }

        var administrator = await users.FindByIdAsync(administratorUserId.ToString())
            ?? throw new UnauthorizedAccessException("Administrator account is unavailable.");
        if (!await users.IsInRoleAsync(administrator, First10Roles.Administrator)
            || administrator.LastReauthenticatedAtUtc is null
            || DateTimeOffset.UtcNow - administrator.LastReauthenticatedAtUtc > RecentReauthenticationWindow)
        {
            throw new UnauthorizedAccessException("Recent administrator reauthentication is required.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({targetUserId.ToString()}))",
            cancellationToken);
        var target = await users.FindByIdAsync(targetUserId.ToString())
            ?? throw new InvalidOperationException("Target account is unavailable.");
        var role = (await users.GetRolesAsync(target)).Single();

        EnsureSucceeded(await users.ResetAuthenticatorKeyAsync(target));
        EnsureSucceeded(await users.SetTwoFactorEnabledAsync(target, false));
        _ = await users.GenerateNewTwoFactorRecoveryCodesAsync(target, 10)
            ?? throw new InvalidOperationException("Prior recovery codes could not be invalidated.");
        target.RevokeSessions();
        EnsureSucceeded(await users.UpdateAsync(target));

        var (rawToken, tokenHash) = InvitationService.CreateToken();
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30);
        var recovery = UserInvitation.Create(
            target.Id,
            role,
            InvitationPurposes.MfaRecovery,
            tokenHash,
            administrator.Id.ToString(),
            expiresAtUtc);
        database.UserInvitations.Add(recovery);
        await database.SaveChangesAsync(cancellationToken);
        await AuditWriter.AppendAsync(database, "identity.mfa_recovery.started", administrator.Id.ToString(), AuditPayload.Create(new Dictionary<string, object?>
        {
            ["targetUserId"] = target.Id,
            ["result"] = "issued"
        }), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreatedInvitation(recovery.Id, rawToken, expiresAtUtc);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(x => x.Description)));
        }
    }
}
