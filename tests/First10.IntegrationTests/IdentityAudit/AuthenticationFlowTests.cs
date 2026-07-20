using System.Buffers.Binary;
using System.Security.Cryptography;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace First10.IntegrationTests.IdentityAudit;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class AuthenticationFlowTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task InvitedDispatcherMustCompleteTotpEnrollmentBeforeAuthentication()
    {
        await using var provider = await CreateProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<First10User>>();
        var email = $"dispatcher-{Guid.NewGuid():N}@first10.test";

        var invitation = await invitations.CreateAsync(email, First10Roles.Dispatcher, "admin-01");
        var enrollment = await invitations.BeginEnrollmentAsync(invitation.Token);

        var invalid = await invitations.CompleteEnrollmentAsync(
            invitation.Token,
            "Strong-Pilot-Password-42!",
            "000000");
        Assert.False(invalid.Succeeded);

        var validCode = GenerateTotp(enrollment.SharedKey, DateTimeOffset.UtcNow);
        var accepted = await invitations.CompleteEnrollmentAsync(
            invitation.Token,
            "Strong-Pilot-Password-42!",
            validCode);

        Assert.True(accepted.Succeeded);
        Assert.Equal(10, accepted.RecoveryCodes.Count);
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(user.InvitationAcceptedAtUtc.HasValue);
        Assert.True(user.TwoFactorEnabled);
        Assert.True(await users.IsInRoleAsync(user, First10Roles.Dispatcher));

        var replay = await invitations.CompleteEnrollmentAsync(
            invitation.Token,
            "Another-Strong-Password-42!",
            validCode);
        Assert.False(replay.Succeeded);
    }

    [Fact]
    public async Task RecoveryCodesAreSingleUseAndSessionRevocationIsImmediate()
    {
        await using var provider = await CreateProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<First10User>>();
        var email = $"recovery-{Guid.NewGuid():N}@first10.test";
        var invitation = await invitations.CreateAsync(email, First10Roles.Dispatcher, "admin-01");
        var enrollment = await invitations.BeginEnrollmentAsync(invitation.Token);
        var accepted = await invitations.CompleteEnrollmentAsync(
            invitation.Token,
            "Strong-Pilot-Password-42!",
            GenerateTotp(enrollment.SharedKey, DateTimeOffset.UtcNow));
        var user = (await users.FindByEmailAsync(email))!;

        var firstUse = await users.RedeemTwoFactorRecoveryCodeAsync(user, accepted.RecoveryCodes[0]);
        var replay = await users.RedeemTwoFactorRecoveryCodeAsync(user, accepted.RecoveryCodes[0]);
        Assert.True(firstUse.Succeeded);
        Assert.False(replay.Succeeded);

        var issuedVersion = user.SessionVersion;
        user.RevokeSessions();
        await users.UpdateAsync(user);

        Assert.False(SessionVersionValidator.IsCurrent(user, issuedVersion));
        Assert.True(SessionVersionValidator.IsCurrent(user, user.SessionVersion));
    }

    [Fact]
    public async Task RecentlyReauthenticatedAdministratorCanIssueOneTimeMfaRecovery()
    {
        await using var provider = await CreateProviderAsync();
        await using var scope = provider.CreateAsyncScope();
        var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();
        var recovery = scope.ServiceProvider.GetRequiredService<MfaRecoveryService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<First10User>>();

        var admin = await EnrollAsync(invitations, users, First10Roles.Administrator, "admin");
        var dispatcher = await EnrollAsync(invitations, users, First10Roles.Dispatcher, "dispatcher");
        admin.User.MarkReauthenticated(DateTimeOffset.UtcNow);
        Assert.True((await users.UpdateAsync(admin.User)).Succeeded);
        var issuedSessionVersion = dispatcher.User.SessionVersion;

        var reset = await recovery.BeginResetAsync(dispatcher.User.Id, admin.User.Id);
        var refreshedDispatcher = (await users.FindByIdAsync(dispatcher.User.Id.ToString()))!;
        Assert.False(refreshedDispatcher.TwoFactorEnabled);
        Assert.False((await users.RedeemTwoFactorRecoveryCodeAsync(
            refreshedDispatcher,
            dispatcher.Enrollment.RecoveryCodes[0])).Succeeded);
        Assert.False(SessionVersionValidator.IsCurrent(refreshedDispatcher, issuedSessionVersion));

        var reEnrollment = await invitations.BeginEnrollmentAsync(reset.Token);
        var completed = await invitations.CompleteEnrollmentAsync(
            reset.Token,
            "password-is-not-changed-during-recovery",
            GenerateTotp(reEnrollment.SharedKey, DateTimeOffset.UtcNow));

        Assert.True(completed.Succeeded);
        Assert.Equal(10, completed.RecoveryCodes.Count);
        Assert.True((await users.FindByIdAsync(dispatcher.User.Id.ToString()))!.TwoFactorEnabled);
        Assert.False((await invitations.CompleteEnrollmentAsync(
            reset.Token,
            "ignored",
            GenerateTotp(reEnrollment.SharedKey, DateTimeOffset.UtcNow))).Succeeded);
    }

    [Fact]
    public async Task BootstrapSecretCanCreateExactlyOneAdministratorUnderConcurrency()
    {
        const string secret = "concurrent-test-bootstrap-secret";
        await using var provider = await CreateProviderAsync(secret);
        await using (var cleanupScope = provider.CreateAsyncScope())
        {
            var database = cleanupScope.ServiceProvider.GetRequiredService<First10DbContext>();
            await database.Database.ExecuteSqlRawAsync(
                "TRUNCATE identity_audit.user_invitations, identity_audit.users, identity_audit.audit_events CASCADE");
        }

        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var attempts = await Task.WhenAll(
            firstScope.ServiceProvider.GetRequiredService<BootstrapService>()
                .TryCreateFirstAdministratorAsync("first-admin@first10.test", secret),
            secondScope.ServiceProvider.GetRequiredService<BootstrapService>()
                .TryCreateFirstAdministratorAsync("second-admin@first10.test", secret));

        Assert.Single(attempts, invitation => invitation is not null);
        await using var verificationScope = provider.CreateAsyncScope();
        var users = verificationScope.ServiceProvider.GetRequiredService<First10DbContext>().Users;
        Assert.Equal(1, await users.CountAsync());
    }

    private static async Task<(First10User User, EnrollmentResult Enrollment)> EnrollAsync(
        InvitationService invitations,
        UserManager<First10User> users,
        string role,
        string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@first10.test";
        var invitation = await invitations.CreateAsync(email, role, "test-administrator");
        var enrollment = await invitations.BeginEnrollmentAsync(invitation.Token);
        var result = await invitations.CompleteEnrollmentAsync(
            invitation.Token,
            "Strong-Pilot-Password-42!",
            GenerateTotp(enrollment.SharedKey, DateTimeOffset.UtcNow));
        Assert.True(result.Succeeded);
        return ((await users.FindByEmailAsync(email))!, result);
    }

    private async Task<ServiceProvider> CreateProviderAsync(string? bootstrapSecret = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFirst10Persistence(postgres.ConnectionString);
        services.AddFirst10Identity();
        if (bootstrapSecret is not null)
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:BootstrapSecret"] = bootstrapSecret
                })
                .Build());
        }

        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
        await database.Database.MigrateAsync();
        await IdentityRoleSeeder.SeedAsync(scope.ServiceProvider);
        return provider;
    }

    private static string GenerateTotp(string base32Secret, DateTimeOffset now)
    {
        var key = DecodeBase32(base32Secret);
        var timestep = (ulong)(now.ToUnixTimeSeconds() / 30);
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(counter, timestep);
#pragma warning disable CA5350 // TOTP authenticator compatibility requires HMAC-SHA1 per RFC 6238.
        var hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var binaryCode = ((hash[offset] & 0x7f) << 24)
                         | (hash[offset + 1] << 16)
                         | (hash[offset + 2] << 8)
                         | hash[offset + 3];
        return (binaryCode % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in value.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character, StringComparison.Ordinal);
            bitsLeft += 5;
            if (bitsLeft < 8)
            {
                continue;
            }

            output.Add((byte)(buffer >> (bitsLeft - 8)));
            bitsLeft -= 8;
            buffer &= (1 << bitsLeft) - 1;
        }

        return [.. output];
    }
}
