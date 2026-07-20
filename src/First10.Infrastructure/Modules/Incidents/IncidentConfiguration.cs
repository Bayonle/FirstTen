using Microsoft.Extensions.DependencyInjection;

namespace First10.Infrastructure.Modules.Incidents;

public static class IncidentConfiguration
{
    public static IServiceCollection AddFirst10Incidents(this IServiceCollection services)
    {
        services.AddSingleton<IPilotReporterIdentityRegistry, ConfiguredPilotReporterIdentityRegistry>();
        services.AddScoped<IncidentPersistence>();
        services.AddScoped<SingletonIncidentReviewProcessor>();
        services.AddScoped<SingletonIncidentDecisionProcessor>();
        services.AddScoped<IncidentConflictResolutionProcessor>();
        services.AddScoped<LateIncidentLocationProcessor>();
        services.AddScoped<IncidentObservationProcessor>();
        services.AddScoped<CrewBriefingGenerator>();
        services.AddHttpClient<ICrewBriefingOrderProvider, OpenAiCrewBriefingOrderClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        return services;
    }
}
