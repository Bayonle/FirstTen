using First10.Modules;
using NetArchTest.Rules;

namespace First10.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    private static readonly string[] ProductModules =
    [
        "Intake",
        "Incidents",
        "Dispatch",
        "Guidance",
        "Recognition",
        "IdentityAudit",
        "Operations"
    ];

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void ProductModulesDoNotDependOnOtherModuleImplementations(string module)
    {
        var forbiddenNamespaces = ProductModules
            .Where(other => !string.Equals(other, module, StringComparison.Ordinal))
            .Select(other => $"First10.Modules.{other}")
            .ToArray();

        var result = Types.InAssembly(typeof(ModulesAssemblyMarker).Assembly)
            .That()
            .ResideInNamespaceMatching($"^First10\\.Modules\\.{module}(\\.|$)")
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{module} depends on another product module implementation: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();

        foreach (var module in ProductModules)
        {
            data.Add(module);
        }

        return data;
    }
}
