namespace First10.ArchitectureTests;

public sealed class DeploymentTopologyTests
{
    [Fact]
    public void OnlyApiAndWorkerDefineProductionContainerImages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dockerfiles = Directory
            .GetFiles(Path.Combine(repositoryRoot, "src"), "Dockerfile", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["src/First10.Api/Dockerfile", "src/First10.Worker/Dockerfile"],
            dockerfiles);
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
