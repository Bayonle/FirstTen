using Microsoft.Extensions.Configuration;

namespace First10.Infrastructure.Modules.Incidents;

public interface IPilotReporterIdentityRegistry
{
    string? ResolveVerifiedIdentityKey(string reporterIndependenceKey);
}

public sealed class ConfiguredPilotReporterIdentityRegistry(IConfiguration configuration)
    : IPilotReporterIdentityRegistry
{
    public string? ResolveVerifiedIdentityKey(string reporterIndependenceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reporterIndependenceKey);
        foreach (var mapping in configuration
                     .GetSection("Incidents:VerifiedPilotIdentityMappings")
                     .Get<string[]>() ?? [])
        {
            var separator = mapping.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == mapping.Length - 1)
            {
                continue;
            }

            var registeredReporterKey = mapping[..separator];
            if (string.Equals(registeredReporterKey, reporterIndependenceKey, StringComparison.Ordinal))
            {
                return mapping[(separator + 1)..];
            }
        }

        return null;
    }
}
