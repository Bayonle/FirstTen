using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Modules.Intake.Channels.Telegram;
using System.Text.Json;

namespace First10.Api.Webhooks;

public static class TelegramWebhookEndpoints
{
    public static IEndpointRouteBuilder MapTelegramWebhook(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/telegram", async (
            HttpRequest request,
            IConfiguration configuration,
            TelegramInboundAdapter adapter,
            ChannelWebhookIngress ingress,
            CancellationToken cancellationToken) =>
        {
            if (!WebhookSecurity.SecretMatchesWithOverlap(
                    configuration["Channels:Telegram:WebhookSecret"],
                    configuration["Channels:Telegram:PreviousWebhookSecret"],
                    configuration["Channels:Telegram:PreviousWebhookSecretValidUntilUtc"],
                    request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault(),
                    DateTimeOffset.UtcNow))
            {
                return Results.Unauthorized();
            }

            var body = await WebhookSecurity.ReadBoundedAsync(request, cancellationToken);
            if (body is null)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            try
            {
                foreach (var message in adapter.Parse(body))
                {
                    await ingress.TryAcceptAsync(message, cancellationToken);
                }
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid Telegram webhook payload." });
            }

            return Results.Ok();
        }).AllowAnonymous().DisableAntiforgery();

        return endpoints;
    }
}
