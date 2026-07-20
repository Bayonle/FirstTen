using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using First10.Api.Auth;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace First10.IntegrationTests.IdentityAudit;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class AuthenticationHttpTests(Persistence.PostgresFixture postgres)
{
    [Fact]
    public async Task HttpAndSignalREnforceInvitationMfaRolesAndSessionRevocation()
    {
        const string bootstrapSecret = "test-only-bootstrap-secret-42";
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:first10", postgres.ConnectionString);
            builder.UseSetting("Security:BootstrapSecret", bootstrapSecret);
            builder.UseEnvironment("Testing");
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        await using (var initializationScope = factory.Services.CreateAsyncScope())
        {
            var database = initializationScope.ServiceProvider.GetRequiredService<First10DbContext>();
            await database.Database.MigrateAsync();
            await database.Database.ExecuteSqlRawAsync(
                "TRUNCATE identity_audit.user_invitations, identity_audit.users, identity_audit.audit_events CASCADE");
            await IdentityRoleSeeder.SeedAsync(database);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dispatch/probe")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(
            "/hubs/operations/negotiate?negotiateVersion=1",
            content: null)).StatusCode);

        var adminEmail = $"admin-{Guid.NewGuid():N}@first10.test";
        var adminInvitation = await PostAndReadAsync<CreatedInvitation>(
            client,
            "/api/auth/bootstrap",
            new BootstrapRequest(adminEmail, bootstrapSecret));
        var adminEnrollment = await PostAndReadAsync<InvitationEnrollment>(
            client,
            "/api/auth/invitations/enrollment",
            new InvitationTokenRequest(adminInvitation.Token));
        await PostAndReadAsync<EnrollmentResult>(
            client,
            "/api/auth/invitations/accept",
            new AcceptInvitationRequest(
                adminInvitation.Token,
                "Strong-Admin-Password-42!",
                GenerateTotp(adminEnrollment.SharedKey)));
        await LoginAsync(client, adminEmail, "Strong-Admin-Password-42!", adminEnrollment.SharedKey);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/probe")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dispatch/probe")).StatusCode);

        var dispatcherEmail = $"dispatcher-{Guid.NewGuid():N}@first10.test";
        var dispatcherInvitation = await PostAndReadAsync<CreatedInvitation>(
            client,
            "/api/auth/invitations",
            new CreateInvitationRequest(dispatcherEmail, First10Roles.Dispatcher));
        var dispatcherEnrollment = await PostAndReadAsync<InvitationEnrollment>(
            client,
            "/api/auth/invitations/enrollment",
            new InvitationTokenRequest(dispatcherInvitation.Token));
        await PostAndReadAsync<EnrollmentResult>(
            client,
            "/api/auth/invitations/accept",
            new AcceptInvitationRequest(
                dispatcherInvitation.Token,
                "Strong-Dispatch-Password-42!",
                GenerateTotp(dispatcherEnrollment.SharedKey)));
        await PostAsync(client, "/api/auth/logout", new { });
        await LoginAsync(client, dispatcherEmail, "Strong-Dispatch-Password-42!", dispatcherEnrollment.SharedKey);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dispatch/probe")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/probe")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            "/hubs/operations/negotiate?negotiateVersion=1",
            content: null)).StatusCode);

        await using (var revocationScope = factory.Services.CreateAsyncScope())
        {
            var users = revocationScope.ServiceProvider.GetRequiredService<UserManager<First10User>>();
            var dispatcher = (await users.FindByEmailAsync(dispatcherEmail))!;
            dispatcher.RevokeSessions();
            Assert.True((await users.UpdateAsync(dispatcher)).Succeeded);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dispatch/probe")).StatusCode);
    }

    private static async Task LoginAsync(HttpClient client, string email, string password, string sharedKey)
    {
        await PostAsync(client, "/api/auth/login/password", new PasswordLoginRequest(email, password));
        await PostAsync(client, "/api/auth/login/mfa", new MfaLoginRequest(GenerateTotp(sharedKey)));
    }

    private static async Task<T> PostAndReadAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await PostAsync(client, path, body);
        var value = await response.Content.ReadFromJsonAsync<T>();
        return value ?? throw new InvalidOperationException($"Endpoint '{path}' returned no JSON body.");
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var tokenDocument = await client.GetFromJsonAsync<JsonDocument>("/api/auth/antiforgery")
            ?? throw new InvalidOperationException("Antiforgery endpoint returned no token.");
        var token = tokenDocument.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("Antiforgery request token is missing.");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"POST {path} failed with {(int)response.StatusCode}: {detail}");
        }

        return response;
    }

    private static string GenerateTotp(string base32Secret)
    {
        var key = DecodeBase32(base32Secret);
        var timestep = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
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
