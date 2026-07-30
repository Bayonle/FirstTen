using Microsoft.Extensions.DependencyInjection;

namespace First10.Infrastructure.Modules.Recognition;

public static class RecognitionConfiguration
{
    public static IServiceCollection AddFirst10Recognition(this IServiceCollection services)
    {
        services.AddScoped<RecognitionAwardProcessor>();
        services.AddScoped<RecognitionConsentProcessor>();
        services.AddScoped<RecognitionAdjustmentProcessor>();
        return services;
    }
}
