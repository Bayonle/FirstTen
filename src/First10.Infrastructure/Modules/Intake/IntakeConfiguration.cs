using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Modules.Intake.Channels.Telegram;
using First10.Infrastructure.Modules.Intake.Channels.WhatsApp;
using First10.Infrastructure.Modules.Intake.Media;
using First10.Modules.Intake.Media;
using Amazon.S3;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
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
        services.AddScoped<MediaPrivacyPipeline>();
        services.AddScoped<IMediaDegradationPublisher, WolverineMediaDegradationPublisher>();
        services.AddSingleton<TelegramInboundAdapter>();
        services.AddSingleton<WhatsAppInboundAdapter>();
        services.AddSingleton<IChannelMessageSender, TelegramChannelMessageSender>();
        services.AddSingleton<IChannelMessageSender, WhatsAppChannelMessageSender>();
        services.AddSingleton<IProviderMediaSource, TelegramMediaSource>();
        services.AddSingleton<IProviderMediaSource, WhatsAppMediaSource>();
        services.AddSingleton<ProviderMediaDownloader>();
        services.AddSingleton<IFaceDetector, OnnxYuNetFaceDetector>();
        services.AddSingleton<IImageRedactor, OnnxFaceRedactor>();
        services.AddSingleton<IAudioSanitizer, OggOpusAudioSanitizer>();
        services.AddSingleton<IMediaEnvelopeEncryptor, AesGcmMediaEnvelopeEncryptor>();
        services.AddSingleton<IAmazonS3>(provider =>
            SafeMediaStore.CreateClient(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton<ISafeMediaStore, SafeMediaStore>();
        return services;
    }
}
