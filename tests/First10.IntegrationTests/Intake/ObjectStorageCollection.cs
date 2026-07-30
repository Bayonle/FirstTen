using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace First10.IntegrationTests.Intake;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ObjectStorageTestGroup : ICollectionFixture<ObjectStorageFixture>
{
    public const string Name = "object-storage";
}

public sealed class ObjectStorageFixture : IAsyncLifetime
{
    private const string AccessKey = "first10-integration";
    private const string SecretKey = "first10-integration-secret";
    private readonly IContainer _container = new ContainerBuilder(
        "minio/minio:RELEASE.2025-09-07T16-13-09Z")
        .WithEnvironment("MINIO_ROOT_USER", AccessKey)
        .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
        .WithPortBinding(9000, true)
        .WithCommand("server", "/data")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
            request.ForPort(9000).ForPath("/minio/health/ready")))
        .Build();

    public string Endpoint => $"http://127.0.0.1:{_container.GetMappedPublicPort(9000)}";

    public static string User => AccessKey;

    public static string Password => SecretKey;

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
