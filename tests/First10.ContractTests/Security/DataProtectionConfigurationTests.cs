using First10.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace First10.ContractTests.Security;

public sealed class DataProtectionConfigurationTests
{
    [Fact]
    public void ProductionRegistrationFailsWithoutExternalWrappingCertificate()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddFirst10DataProtection(configuration, requireWrappedKeys: true));

        Assert.Contains("DataProtectionWrappingCertificateBase64", exception.Message, StringComparison.Ordinal);
    }
}
