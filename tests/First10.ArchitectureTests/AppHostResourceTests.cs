using Aspire.Hosting.Testing;

namespace First10.ArchitectureTests;

public sealed class AppHostResourceTests
{
    [Fact]
    public async Task AppHostDescribesTheCompleteLocalTopology()
    {
        using var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.First10_AppHost>(
            [
                "Parameters:object-storage-access-key=first10-test",
                "Parameters:object-storage-secret-key=first10-test-secret"
            ]);

        var resourceNames = builder.Resources
            .Select(resource => resource.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("api", resourceNames);
        Assert.Contains("worker", resourceNames);
        Assert.Contains("web", resourceNames);
        Assert.Contains("postgres", resourceNames);
        Assert.Contains("first10", resourceNames);
        Assert.Contains("object-storage", resourceNames);
    }
}
