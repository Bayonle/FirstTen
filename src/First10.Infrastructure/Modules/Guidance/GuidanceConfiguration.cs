using First10.Infrastructure.Modules.Dispatch;
using First10.Infrastructure.Modules.Intake.Delivery;
using Microsoft.Extensions.DependencyInjection;

namespace First10.Infrastructure.Modules.Guidance;

public static class GuidanceConfiguration
{
    public static IServiceCollection AddFirst10DispatchAndGuidance(this IServiceCollection services)
    {
        services.AddScoped<GuidanceAssetStore>();
        services.AddScoped<GuidanceIntentProcessor>();
        services.AddSingleton<IGuidanceVoiceAssetReader, FileGuidanceVoiceAssetReader>();
        services.AddScoped<OutboundChannelSender>();
        services.AddScoped<DispatchTransitionProcessor>();
        return services;
    }
}
