using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Modules.Intake.Channels.WhatsApp;

namespace First10.Api.Webhooks;

public static class WhatsAppWebhookEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppWebhook(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/webhooks/whatsapp", (
            HttpRequest request,
            IConfiguration configuration) =>
        {
            var mode = request.Query["hub.mode"].FirstOrDefault();
            var token = request.Query["hub.verify_token"].FirstOrDefault();
            var challenge = request.Query["hub.challenge"].FirstOrDefault();
            return mode == "subscribe"
                   && WebhookSecurity.SecretMatches(
                       configuration["Channels:WhatsApp:VerifyToken"],
                       token)
                   && !string.IsNullOrWhiteSpace(challenge)
                ? Results.Text(challenge, "text/plain")
                : Results.Unauthorized();
        }).AllowAnonymous();

        endpoints.MapPost("/webhooks/whatsapp", async (
            HttpRequest request,
            IConfiguration configuration,
            WhatsAppInboundAdapter adapter,
            ChannelWebhookIngress ingress,
            CancellationToken cancellationToken) =>
        {
            var body = await WebhookSecurity.ReadBoundedAsync(request, cancellationToken);
            if (body is null)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            if (!WebhookSecurity.MetaSignatureMatches(
                    configuration["Channels:WhatsApp:AppSecret"],
                    request.Headers["X-Hub-Signature-256"].FirstOrDefault(),
                    body))
            {
                return Results.Unauthorized();
            }

            foreach (var message in adapter.Parse(body))
            {
                await ingress.TryAcceptAsync(message, cancellationToken);
            }

            return Results.Ok();
        }).AllowAnonymous().DisableAntiforgery();

        return endpoints;
    }
}
