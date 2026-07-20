using System.Net;
using System.Security.Cryptography;
using System.Text;
using First10.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using First10.Modules.Intake;

namespace First10.IntegrationTests.Intake;

[Collection(Persistence.PostgresTestGroup.Name)]
public sealed class WebhookHttpTests(Persistence.PostgresFixture postgres)
{
    private const string TelegramSecret = "telegram-test-webhook-secret";
    private const string MetaAppSecret = "meta-test-app-secret";
    private const string MetaVerifyToken = "meta-test-verify-token";
    private const string PseudonymKey = "http-integration-pseudonym-key-000000000001";

    [Fact]
    public void CredentialOverlapAcceptsPreviousOnlyInsideWindow()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(First10.Api.Webhooks.WebhookSecurity.SecretMatchesWithOverlap(
            "active-secret",
            "previous-secret",
            now.AddMinutes(5).ToString("O"),
            "previous-secret",
            now));
        Assert.False(First10.Api.Webhooks.WebhookSecurity.SecretMatchesWithOverlap(
            "active-secret",
            "previous-secret",
            now.AddMinutes(-1).ToString("O"),
            "previous-secret",
            now));

        var body = "signed-body"u8.ToArray();
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("previous-secret"),
            body));
        Assert.True(First10.Api.Webhooks.WebhookSecurity.MetaSignatureMatchesWithOverlap(
            "active-secret",
            "previous-secret",
            now.AddMinutes(5).ToString("O"),
            $"sha256={signature}",
            body,
            now));
    }

    [Fact]
    public async Task AuthenticatedWebhooksAcceptOnceAndInvalidRequestsFailBeforeParsing()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:first10", postgres.ConnectionString);
            builder.UseSetting("Channels:Telegram:WebhookSecret", TelegramSecret);
            builder.UseSetting("Channels:WhatsApp:AppSecret", MetaAppSecret);
            builder.UseSetting("Channels:WhatsApp:VerifyToken", MetaVerifyToken);
            builder.UseSetting("Security:ReporterPseudonymKey", PseudonymKey);
            builder.UseSetting("Security:ReporterContactKeyVersion", "http-test-v1");
            builder.UseEnvironment("Testing");
        });
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<First10DbContext>().Database.MigrateAsync();
        }

        var telegramJson = """
            {"update_id":800001,"message":{"message_id":201,"date":1784558400,"chat":{"id":11223344,"type":"private"},"voice":{"file_id":"tg-http-voice","duration":8}}}
            """;
        Assert.Equal(HttpStatusCode.OK, (await SendTelegramAsync(client, telegramJson, TelegramSecret)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendTelegramAsync(client, telegramJson, TelegramSecret)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendTelegramAsync(client, "not-json", "wrong-secret")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendTelegramAsync(client, "not-json", TelegramSecret)).StatusCode);

        using var oversized = new HttpRequestMessage(HttpMethod.Post, "/webhooks/telegram")
        {
            Content = new ByteArrayContent(new byte[(256 * 1024) + 1])
        };
        oversized.Headers.Add("X-Telegram-Bot-Api-Secret-Token", TelegramSecret);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(oversized)).StatusCode);

        var verification = await client.GetAsync(
            $"/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={MetaVerifyToken}&hub.challenge=challenge-42");
        Assert.Equal("challenge-42", await verification.Content.ReadAsStringAsync());

        var whatsappJson = """
            {"object":"whatsapp_business_account","entry":[{"id":"waba-test","changes":[{"field":"messages","value":{"messaging_product":"whatsapp","messages":[{"from":"2348001234567","id":"wamid.http-voice","timestamp":"1784558400","type":"audio","audio":{"id":"wa-http-voice","voice":true}}]}}]}]}
            """;
        Assert.Equal(HttpStatusCode.OK, (await SendWhatsAppAsync(client, whatsappJson)).StatusCode);

        Guid deliveryIntentId;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<First10DbContext>();
            Assert.Equal(1, await database.InboundMessageReceipts.CountAsync(
                x => x.Scope == "telegram" && x.Key == "800001"));
            Assert.Equal(1, await database.InboundMessageReceipts.CountAsync(
                x => x.Scope == "whatsapp" && x.Key == "wamid.http-voice"));
            var contact = await database.ReporterContacts.SingleAsync(x => x.Channel == IntakeChannel.WhatsApp);
            Assert.DoesNotContain("2348001234567", contact.ProtectedDestination, StringComparison.Ordinal);
            var session = GuidedIntakeSession.Open(
                Guid.NewGuid(),
                new InboundChannelEnvelope
                {
                    SchemaVersion = 1,
                    Channel = IntakeChannel.WhatsApp,
                    ProviderMessageId = "setup-input",
                    ReporterKey = contact.ReporterKey,
                    ContactReference = contact.Id,
                    ContentKind = IntakeContentKind.Voice,
                    ProviderMediaHandle = "setup-handle",
                    OccurredAtUtc = DateTimeOffset.UtcNow,
                    CorrelationKey = contact.ReporterKey
                },
                TimeSpan.FromMinutes(2));
            var intent = IntakePromptIntent.Create(
                session,
                IntakePrompt.Acknowledgement,
                IntakeLanguage.English,
                DateTimeOffset.UtcNow);
            intent.TryMarkProviderAccepted("wamid.outbound-receipt", DateTimeOffset.UtcNow);
            database.GuidedIntakeSessions.Add(session);
            database.IntakePromptIntents.Add(intent);
            await database.SaveChangesAsync();
            deliveryIntentId = intent.Id;
        }

        var statusJson = """
            {"entry":[{"changes":[{"value":{"statuses":[{"id":"wamid.outbound-receipt","status":"delivered","timestamp":"1784558415"}]}}]}]}
            """;
        Assert.Equal(HttpStatusCode.OK, (await SendWhatsAppAsync(client, statusJson)).StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<First10DbContext>();
        Assert.Equal(
            ChannelDeliveryStatus.Delivered,
            (await verificationDatabase.IntakePromptIntents.SingleAsync(x => x.Id == deliveryIntentId)).DeliveryStatus);
    }

    private static Task<HttpResponseMessage> SendTelegramAsync(
        HttpClient client,
        string json,
        string secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/telegram")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", secret);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendWhatsAppAsync(HttpClient client, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(MetaAppSecret), bytes));
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp")
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hub-Signature-256", $"sha256={signature}");
        return client.SendAsync(request);
    }
}
