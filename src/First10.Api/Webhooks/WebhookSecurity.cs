using System.Security.Cryptography;
using System.Text;
using System.Buffers;

namespace First10.Api.Webhooks;

public static class WebhookSecurity
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

    public static bool SecretMatchesWithOverlap(
        string? active,
        string? previous,
        string? previousValidUntilUtc,
        string? supplied,
        DateTimeOffset now)
    {
        if (SecretMatches(active, supplied))
        {
            return true;
        }

        return DateTimeOffset.TryParse(
                   previousValidUntilUtc,
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.AssumeUniversal,
                   out var validUntil)
               && now <= validUntil
               && SecretMatches(previous, supplied);
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

    public static bool MetaSignatureMatchesWithOverlap(
        string? active,
        string? previous,
        string? previousValidUntilUtc,
        string? signature,
        ReadOnlySpan<byte> body,
        DateTimeOffset now)
    {
        if (MetaSignatureMatches(active, signature, body))
        {
            return true;
        }

        return DateTimeOffset.TryParse(
                   previousValidUntilUtc,
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.AssumeUniversal,
                   out var validUntil)
               && now <= validUntil
               && MetaSignatureMatches(previous, signature, body);
    }

    public static async Task<byte[]?> ReadBoundedAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumPayloadBytes)
        {
            return null;
        }

        using var body = new MemoryStream(capacity: MaximumPayloadBytes);
        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var read = await request.Body.ReadAsync(rented, cancellationToken);
                if (read == 0)
                {
                    return body.ToArray();
                }

                if (body.Length + read > MaximumPayloadBytes)
                {
                    return null;
                }

                await body.WriteAsync(rented.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
