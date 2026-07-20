using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Modules.Intake.Channels.Telegram;
using First10.Infrastructure.Modules.Intake.Channels.WhatsApp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using First10.Infrastructure.Persistence;

namespace First10.Infrastructure.Modules.Intake;

public static class IntakeConfiguration
{
    public static IServiceCollection AddFirst10Intake(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName("First10")
            .PersistKeysToDbContext<First10DbContext>();
        services.AddScoped<ProtectedContactIdentityResolver>();
        services.AddScoped<IContactIdentityResolver>(provider =>
            provider.GetRequiredService<ProtectedContactIdentityResolver>());
        services.AddScoped<ChannelEnvelopeMapper>();
        services.AddScoped<ChannelWebhookIngress>();
        services.AddScoped<GuidedIntakeProcessor>();
        services.AddScoped<ChannelDeliveryReceiptProcessor>();
        services.AddScoped<IntakePromptDeliveryService>();
        services.AddSingleton<TelegramInboundAdapter>();
        services.AddSingleton<WhatsAppInboundAdapter>();
        services.AddSingleton<IChannelMessageSender, TelegramChannelMessageSender>();
        services.AddSingleton<IChannelMessageSender, WhatsAppChannelMessageSender>();
        return services;
    }
}
