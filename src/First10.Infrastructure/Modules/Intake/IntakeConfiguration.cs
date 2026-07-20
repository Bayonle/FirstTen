using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Modules.Intake.Channels.Telegram;
using First10.Infrastructure.Modules.Intake.Channels.WhatsApp;
using First10.Infrastructure.Modules.Intake.Media;
using First10.Infrastructure.Modules.Intake.Triage;
using First10.Infrastructure.Modules.Intake.Location;
using First10.Infrastructure.Modules.Intake.OpenAI;
using First10.Modules.Intake.Triage;
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
        services.AddScoped<TriageDeadlineProcessor>();
        services.AddScoped<IMediaDegradationPublisher, WolverineMediaDegradationPublisher>();
        services.AddScoped<ITriageKickoffPublisher, WolverineTriageKickoffPublisher>();
        services.AddScoped<TriageSessionProcessor>();
        services.AddSingleton<OpenAiAudioPreparer>();
        services.AddSingleton<OpenAiSafetyIdentifier>();
        services.AddSingleton<CorridorGazetteer>();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<IReporterAudioTranscriber, OpenAiTranscriptionClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddHttpClient<IStructuredTriageProvider, OpenAiStructuredTriageClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
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
        services.AddSingleton<SafeMediaStore>();
        services.AddSingleton<ISafeMediaStore>(provider => provider.GetRequiredService<SafeMediaStore>());
        services.AddSingleton<ISafeMediaReader>(provider => provider.GetRequiredService<SafeMediaStore>());
        return services;
    }
}
