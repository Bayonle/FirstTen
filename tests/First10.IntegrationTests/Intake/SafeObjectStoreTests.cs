using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using First10.Infrastructure.Modules.Intake.Media;
using First10.Modules.Intake.Media;
using Microsoft.Extensions.Configuration;

namespace First10.IntegrationTests.Intake;

[Collection(ObjectStorageTestGroup.Name)]
public sealed class SafeObjectStoreTests(ObjectStorageFixture storage)
{
    [Fact]
    public async Task S3CompatibleStoreContainsOnlyAuthenticatedCiphertextAndDeletesWithoutVersion()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObjectStorage:Endpoint"] = storage.Endpoint,
                ["ObjectStorage:AccessKey"] = ObjectStorageFixture.User,
                ["ObjectStorage:SecretKey"] = ObjectStorageFixture.Password,
                ["ObjectStorage:SafeMediaBucket"] = $"first10-test-{Guid.NewGuid():N}",
                ["MediaEncryption:ActiveKeyVersion"] = "integration-v1",
                ["MediaEncryption:Keys:integration-v1"] = Convert.ToBase64String(
                    Enumerable.Repeat((byte)77, 32).ToArray())
            }).Build();
        using var client = SafeMediaStore.CreateClient(configuration);
        var encryptor = new AesGcmMediaEnvelopeEncryptor(configuration);
        var store = new SafeMediaStore(client, encryptor, configuration);
        var assetId = Guid.NewGuid();
        byte[] safeDerivative = [0xFF, 0xD8, 0xFF, 20, 21, 22, 23, 24];

        var stored = await store.StoreAsync(
            assetId,
            IntakeMediaKind.Image,
            safeDerivative,
            "image/jpeg",
            DateTimeOffset.UtcNow.AddDays(30));
        using var response = await client.GetObjectAsync(
            configuration["ObjectStorage:SafeMediaBucket"],
            stored.ObjectKey);
        using var encryptedBuffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(encryptedBuffer);
        var encrypted = encryptedBuffer.ToArray();
        var decrypted = encryptor.Decrypt(assetId, encrypted);
        using var readBack = await store.ReadAsync(
            assetId,
            stored.ObjectKey,
            stored.ContentType,
            stored.Length);

        Assert.Equal("application/octet-stream", response.Headers.ContentType);
        Assert.False(encrypted.AsSpan().IndexOf(safeDerivative) >= 0);
        Assert.Equal(safeDerivative, decrypted);
        Assert.Equal(safeDerivative, readBack.Bytes.ToArray());
        Assert.Equal("image/jpeg", readBack.ContentType);
        var versioning = await client.GetBucketVersioningAsync(new GetBucketVersioningRequest
        {
            BucketName = configuration["ObjectStorage:SafeMediaBucket"]
        });
        Assert.Equal(VersionStatus.Off, versioning.VersioningConfig.Status);

        await store.DeleteAsync(stored.ObjectKey);

        var missing = await Assert.ThrowsAnyAsync<AmazonS3Exception>(() => client.GetObjectAsync(
            configuration["ObjectStorage:SafeMediaBucket"],
            stored.ObjectKey));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
        CryptographicOperations.ZeroMemory(encrypted);
        CryptographicOperations.ZeroMemory(decrypted);
    }
}
