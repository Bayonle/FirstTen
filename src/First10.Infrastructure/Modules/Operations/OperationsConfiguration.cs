using Microsoft.Extensions.DependencyInjection;

namespace First10.Infrastructure.Modules.Operations;

public static class OperationsConfiguration
{
    public static IServiceCollection AddFirst10Operations(this IServiceCollection services)
    {
        services.AddScoped<PilotActivationGate>();
        services.AddScoped<IPilotActivationGate>(provider => provider.GetRequiredService<PilotActivationGate>());
        services.AddSingleton<PilotMetrics>();
        return services;
    }
}
