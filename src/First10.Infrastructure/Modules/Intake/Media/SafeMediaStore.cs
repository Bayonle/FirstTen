using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using First10.Modules.Intake.Media;
using Microsoft.Extensions.Configuration;

namespace First10.Infrastructure.Modules.Intake.Media;

public sealed record EncryptedMediaPayload(byte[] Bytes, string KeyVersion);

public interface IMediaEnvelopeEncryptor
{
    EncryptedMediaPayload Encrypt(Guid assetId, ReadOnlySpan<byte> safeDerivative);

    byte[] Decrypt(Guid assetId, ReadOnlySpan<byte> encryptedPayload);
}

public sealed class AesGcmMediaEnvelopeEncryptor(IConfiguration configuration) : IMediaEnvelopeEncryptor
{
    private static ReadOnlySpan<byte> Magic => "F10M"u8;

    public EncryptedMediaPayload Encrypt(Guid assetId, ReadOnlySpan<byte> safeDerivative)
    {
        if (safeDerivative.IsEmpty)
        {
            throw new ArgumentException("Safe derivative cannot be empty.", nameof(safeDerivative));
        }

        var keyVersion = configuration["MediaEncryption:ActiveKeyVersion"];
        if (string.IsNullOrWhiteSpace(keyVersion) || keyVersion.Length > byte.MaxValue)
        {
            throw new InvalidOperationException("Media encryption is not configured.");
        }

        var key = ReadKey(keyVersion);

        var versionBytes = Encoding.UTF8.GetBytes(keyVersion);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = GC.AllocateUninitializedArray<byte>(safeDerivative.Length);
        var associatedData = CreateAssociatedData(assetId, versionBytes);
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, safeDerivative, ciphertext, tag, associatedData);

            var envelope = GC.AllocateUninitializedArray<byte>(
                Magic.Length + 2 + versionBytes.Length + nonce.Length + tag.Length + ciphertext.Length);
            var offset = 0;
            Magic.CopyTo(envelope);
            offset += Magic.Length;
            envelope[offset++] = 1;
            envelope[offset++] = (byte)versionBytes.Length;
            versionBytes.CopyTo(envelope, offset);
            offset += versionBytes.Length;
            nonce.CopyTo(envelope, offset);
            offset += nonce.Length;
            tag.CopyTo(envelope, offset);
            offset += tag.Length;
            ciphertext.CopyTo(envelope, offset);
            return new EncryptedMediaPayload(envelope, keyVersion);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public byte[] Decrypt(Guid assetId, ReadOnlySpan<byte> encryptedPayload)
    {
        const int fixedHeaderLength = 4 + 2 + 12 + 16;
        if (encryptedPayload.Length <= fixedHeaderLength
            || !encryptedPayload[..4].SequenceEqual(Magic)
            || encryptedPayload[4] != 1)
        {
            throw new CryptographicException("Media envelope is invalid.");
        }

        var versionLength = encryptedPayload[5];
        if (versionLength == 0 || encryptedPayload.Length <= fixedHeaderLength + versionLength)
        {
            throw new CryptographicException("Media envelope is invalid.");
        }

        var keyVersion = Encoding.UTF8.GetString(encryptedPayload.Slice(6, versionLength));
        var nonceOffset = 6 + versionLength;
        var tagOffset = nonceOffset + 12;
        var ciphertextOffset = tagOffset + 16;
        var plaintext = GC.AllocateUninitializedArray<byte>(encryptedPayload.Length - ciphertextOffset);
        var key = ReadKey(keyVersion);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(
                encryptedPayload.Slice(nonceOffset, 12),
                encryptedPayload[ciphertextOffset..],
                encryptedPayload.Slice(tagOffset, 16),
                plaintext,
                CreateAssociatedData(assetId, encryptedPayload.Slice(6, versionLength)));
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] ReadKey(string keyVersion)
    {
        var encodedKey = configuration[$"MediaEncryption:Keys:{keyVersion}"];
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            throw new InvalidOperationException("Media encryption is not configured.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Media encryption is not configured.");
        }

        if (key.Length == 32)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        throw new InvalidOperationException("Media encryption is not configured.");
    }

    private static byte[] CreateAssociatedData(Guid assetId, ReadOnlySpan<byte> versionBytes)
    {
        var associatedData = new byte[16 + versionBytes.Length];
        assetId.TryWriteBytes(associatedData);
        versionBytes.CopyTo(associatedData.AsSpan(16));
        return associatedData;
    }
}

public sealed class SafeMediaStore(
    IAmazonS3 client,
    IMediaEnvelopeEncryptor encryptor,
    IConfiguration configuration) : ISafeMediaStore, ISafeMediaReader
{
    private const int MaximumEnvelopeOverhead = 256;

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var bucket = configuration["ObjectStorage:SafeMediaBucket"] ?? "first10-safe-media";
        return client.DeleteObjectAsync(bucket, objectKey, cancellationToken);
    }

    public async Task<SafeMediaObject> StoreAsync(
        Guid assetId,
        IntakeMediaKind kind,
        ReadOnlyMemory<byte> safeDerivative,
        string contentType,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        var bucket = configuration["ObjectStorage:SafeMediaBucket"] ?? "first10-safe-media";
        var objectKey = MediaPrivacyPolicy.CreateOpaqueObjectKey(assetId, kind);
        var encrypted = encryptor.Encrypt(assetId, safeDerivative.Span);
        try
        {
            await EnsureBucketAsync(bucket, cancellationToken);
            using var content = new MemoryStream(encrypted.Bytes, writable: false);
            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = objectKey,
                InputStream = content,
                AutoCloseStream = false,
                ContentType = "application/octet-stream"
            };
            request.Metadata["first10-content-type"] = contentType;
            request.Metadata["first10-key-version"] = encrypted.KeyVersion;
            request.Metadata["first10-expires-at"] = expiresAtUtc.ToUniversalTime().ToString("O");
            await client.PutObjectAsync(request, cancellationToken);
            return new SafeMediaObject(
                objectKey,
                contentType,
                safeDerivative.Length,
                encrypted.KeyVersion,
                expiresAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted.Bytes);
        }
    }

    public async Task<SafeMediaContent> ReadAsync(
        Guid assetId,
        string objectKey,
        string contentType,
        long expectedPlaintextLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (expectedPlaintextLength is <= 0 or > int.MaxValue - MaximumEnvelopeOverhead)
        {
            throw new InvalidOperationException("Safe media length is invalid.");
        }

        var maximumEncryptedLength = checked((int)expectedPlaintextLength + MaximumEnvelopeOverhead);
        var bucket = configuration["ObjectStorage:SafeMediaBucket"] ?? "first10-safe-media";
        using var response = await client.GetObjectAsync(
            new GetObjectRequest { BucketName = bucket, Key = objectKey },
            cancellationToken);
        if (response.ContentLength <= 0 || response.ContentLength > maximumEncryptedLength)
        {
            throw new InvalidOperationException("Safe media object length is invalid.");
        }

        var encrypted = GC.AllocateUninitializedArray<byte>(checked((int)response.ContentLength));
        try
        {
            var offset = 0;
            while (offset < encrypted.Length)
            {
                var read = await response.ResponseStream.ReadAsync(
                    encrypted.AsMemory(offset),
                    cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("Safe media object ended unexpectedly.");
                }

                offset += read;
            }

            var trailing = new byte[1];
            try
            {
                if (await response.ResponseStream.ReadAsync(trailing, cancellationToken) != 0)
                {
                    throw new InvalidOperationException("Safe media object exceeds its declared length.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(trailing);
            }

            var plaintext = encryptor.Decrypt(assetId, encrypted);
            if (plaintext.LongLength != expectedPlaintextLength)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                throw new InvalidOperationException("Safe media plaintext length is invalid.");
            }

            return new SafeMediaContent(plaintext, contentType);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    private async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        if (await AmazonS3Util.DoesS3BucketExistV2Async(client, bucket))
        {
            await EnsureVersioningDisabledAsync(bucket, cancellationToken);
            return;
        }

        try
        {
            await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (
            exception.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
        }

        await EnsureVersioningDisabledAsync(bucket, cancellationToken);
    }

    private async Task EnsureVersioningDisabledAsync(string bucket, CancellationToken cancellationToken)
    {
        var versioning = await client.GetBucketVersioningAsync(
            new GetBucketVersioningRequest { BucketName = bucket },
            cancellationToken);
        if (versioning.VersioningConfig.Status != VersionStatus.Off)
        {
            throw new InvalidOperationException("Safe media storage must not retain object versions.");
        }
    }

    public static IAmazonS3 CreateClient(IConfiguration configuration)
    {
        var endpoint = configuration["ObjectStorage:Endpoint"]
            ?? throw new InvalidOperationException("Object storage endpoint is required.");
        var accessKey = configuration["ObjectStorage:AccessKey"]
            ?? throw new InvalidOperationException("Object storage credentials are required.");
        var secretKey = configuration["ObjectStorage:SecretKey"]
            ?? throw new InvalidOperationException("Object storage credentials are required.");
        return new AmazonS3Client(
            new BasicAWSCredentials(accessKey, secretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                UseHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
                AllowAutoRedirect = false,
                DisableLogging = true,
                Timeout = TimeSpan.FromSeconds(15),
                MaxErrorRetry = 1
            });
    }
}
