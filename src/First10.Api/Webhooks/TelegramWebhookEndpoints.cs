using First10.Infrastructure.Modules.Intake.Channels;
using First10.Infrastructure.Modules.Intake.Channels.Telegram;

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
            if (!WebhookSecurity.SecretMatches(
                    configuration["Channels:Telegram:WebhookSecret"],
                    request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault()))
            {
                return Results.Unauthorized();
            }

            var body = await WebhookSecurity.ReadBoundedAsync(request, cancellationToken);
            if (body is null)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
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
