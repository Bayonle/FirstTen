using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Modules.Intake.Channels.WhatsApp;
using System.Text.Json;

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
            ChannelDeliveryReceiptProcessor deliveryReceipts,
            CancellationToken cancellationToken) =>
        {
            var body = await WebhookSecurity.ReadBoundedAsync(request, cancellationToken);
            if (body is null)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            if (!WebhookSecurity.MetaSignatureMatchesWithOverlap(
                    configuration["Channels:WhatsApp:AppSecret"],
                    configuration["Channels:WhatsApp:PreviousAppSecret"],
                    configuration["Channels:WhatsApp:PreviousAppSecretValidUntilUtc"],
                    request.Headers["X-Hub-Signature-256"].FirstOrDefault(),
                    body,
                    DateTimeOffset.UtcNow))
            {
                return Results.Unauthorized();
            }

            try
            {
                foreach (var message in adapter.Parse(body))
                {
                    await ingress.TryAcceptAsync(message, cancellationToken);
                }

                foreach (var receipt in WhatsAppInboundAdapter.ParseDeliveryReceipts(body))
                {
                    await deliveryReceipts.ApplyAsync(receipt, cancellationToken);
                }
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid WhatsApp webhook payload." });
            }

            return Results.Ok();
        }).AllowAnonymous().DisableAntiforgery();

        return endpoints;
    }
}
