using System.Text.Json;

namespace First10.Modules.IdentityAudit;

public sealed record AuditPayload
{
    private static readonly string[] SensitiveFragments =
    [
        "password",
        "totp",
        "authenticator",
        "recoverycode",
        "sessioncookie",
        "reporterphone",
        "reportercontact",
        "destination",
        "secret",
        "token"
    ];

    private AuditPayload(string json)
    {
        Json = json;
    }

    public string Json { get; }

    public static AuditPayload Create(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var field in values.Keys)
        {
            var normalized = string.Concat(field.Where(char.IsLetterOrDigit)).ToLowerInvariant();
            if (SensitiveFragments.Any(normalized.Contains))
            {
                throw new ArgumentException($"Audit field '{field}' may contain sensitive data.", nameof(values));
            }
        }

        var ordered = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (field, value) in values)
        {
            ordered[field] = value;
        }
        return new AuditPayload(JsonSerializer.Serialize(ordered));
    }
}
