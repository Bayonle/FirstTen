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

        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "First10.AppHost", "Program.cs"));
        Assert.Contains(".WithEnvironment(\"ObjectStorage__AccessKey\", objectStorageAccessKey)", source, StringComparison.Ordinal);
        Assert.Contains(".WithEnvironment(\"ObjectStorage__SecretKey\", objectStorageSecretKey)", source, StringComparison.Ordinal);
        Assert.Contains(".WithEnvironment(\"MediaEncryption__Keys__local-v1\", mediaEncryptionKey)", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "First10.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the First10 repository root.");
    }
}
