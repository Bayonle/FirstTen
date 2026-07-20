using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace First10.Infrastructure.Modules.Intake.OpenAI;

public sealed class OpenAiSafetyIdentifier(IConfiguration configuration)
{
    public string Create(string reporterKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reporterKey);
        var configuredKey = configuration["OpenAI:SafetyIdentifierKey"];
        if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Length < 32)
        {
            throw new OpenAiProviderException("openai_safety_identifier_not_configured");
        }

        var key = Encoding.UTF8.GetBytes(configuredKey);
        try
        {
            var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(reporterKey));
            return $"f10_{Convert.ToHexStringLower(hash)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
