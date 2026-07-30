using System.Security.Cryptography;
using System.Text;
using First10.Infrastructure.Persistence;
using First10.Modules.Intake;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace First10.Infrastructure.Modules.Intake.Channels;

public sealed class ProtectedContactIdentityResolver(
    First10DbContext database,
    IDataProtectionProvider dataProtection,
    IConfiguration configuration) : IContactIdentityResolver
{
    public async Task<ContactIdentity> ResolveAsync(
        IntakeChannel channel,
        string providerAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAddress);
        var reporterKey = DeriveReporterKey(channel, providerAddress);
        var existing = await database.ReporterContacts.SingleOrDefaultAsync(
            x => x.Channel == channel && x.ReporterKey == reporterKey,
            cancellationToken);
        if (existing is not null)
        {
            existing.Touch(DateTimeOffset.UtcNow);
            await database.SaveChangesAsync(cancellationToken);
            return new ContactIdentity(existing.Id, reporterKey);
        }

        var keyVersion = configuration["Security:ReporterContactKeyVersion"] ?? "v1";
        var protector = CreateProtector(keyVersion);
        var contact = ReporterContact.Create(
            channel,
            reporterKey,
            protector.Protect(providerAddress),
            keyVersion,
            DateTimeOffset.UtcNow);
        database.ReporterContacts.Add(contact);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new ContactIdentity(contact.Id, reporterKey);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            database.ChangeTracker.Clear();
            existing = await database.ReporterContacts.SingleAsync(
                x => x.Channel == channel && x.ReporterKey == reporterKey,
                cancellationToken);
            return new ContactIdentity(existing.Id, reporterKey);
        }
    }

    public async Task<string> ResolveDestinationAsync(
        Guid contactReference,
        CancellationToken cancellationToken = default)
    {
        var contact = await database.ReporterContacts.SingleAsync(
            x => x.Id == contactReference,
            cancellationToken);
        return CreateProtector(contact.EncryptionKeyVersion).Unprotect(contact.ProtectedDestination);
    }

    private string DeriveReporterKey(IntakeChannel channel, string providerAddress)
    {
        var key = configuration["Security:ReporterPseudonymKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            throw new InvalidOperationException(
                "Security:ReporterPseudonymKey must be supplied by the environment and contain at least 32 characters.");
        }

        var value = Encoding.UTF8.GetBytes($"{channel}:{providerAddress}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), value));
    }

    private IDataProtector CreateProtector(string keyVersion) =>
        dataProtection.CreateProtector("First10.Intake.ReporterDestination", keyVersion);
}
