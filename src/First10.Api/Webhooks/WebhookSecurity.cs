using System.Security.Cryptography;
using System.Text;

namespace First10.Api.Webhooks;

internal static class WebhookSecurity
{
    public const int MaximumPayloadBytes = 256 * 1024;

    public static bool SecretMatches(string? expected, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    public static bool MetaSignatureMatches(string? appSecret, string? signature, ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrWhiteSpace(appSecret)
            || signature is null
            || !signature.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return false;
        }

        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature[7..]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (supplied.Length != 32)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret!), body);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    public static async Task<byte[]?> ReadBoundedAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumPayloadBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        return buffer.Length <= MaximumPayloadBytes ? buffer.ToArray() : null;
    }
}
