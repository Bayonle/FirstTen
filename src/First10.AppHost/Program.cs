var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var database = builder
    .AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume("first10-postgres-data")
    .AddDatabase("first10");

var objectStorageAccessKey = builder.AddParameter("object-storage-access-key", secret: true);
var objectStorageSecretKey = builder.AddParameter("object-storage-secret-key", secret: true);
var bootstrapSecret = builder.AddParameter("bootstrap-secret", secret: true);
var telegramWebhookSecret = builder.AddParameter("telegram-webhook-secret", secret: true);
var telegramBotToken = builder.AddParameter("telegram-bot-token", secret: true);
var reporterPseudonymKey = builder.AddParameter("reporter-pseudonym-key", secret: true);
var mediaEncryptionKey = builder.AddParameter("media-encryption-key", secret: true);
var faceRedactionModelPath = builder.AddParameter("face-redaction-model-path");
var openAiApiKey = builder.AddParameter("openai-api-key", secret: true);
var openAiSafetyIdentifierKey = builder.AddParameter("openai-safety-identifier-key", secret: true);

var objectStorage = builder
    .AddContainer("object-storage", "minio/minio", "RELEASE.2025-09-07T16-13-09Z")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", objectStorageAccessKey)
    .WithEnvironment("MINIO_ROOT_PASSWORD", objectStorageSecretKey)
    .WithHttpEndpoint(targetPort: 9000, name: "s3")
    .WithHttpEndpoint(targetPort: 9001, name: "console")
    .WithVolume("first10-object-storage-data", "/data");

var api = builder
    .AddProject<Projects.First10_Api>("api")
    .WithReference(database)
    .WithEnvironment("Security__BootstrapSecret", bootstrapSecret)
    .WithEnvironment("Security__ReporterPseudonymKey", reporterPseudonymKey)
    .WithEnvironment("Security__ReporterContactKeyVersion", "local-v1")
    .WithEnvironment("Channels__Telegram__WebhookSecret", telegramWebhookSecret)
    .WithEnvironment("ObjectStorage__Endpoint", objectStorage.GetEndpoint("s3"))
    .WaitFor(database)
    .WaitFor(objectStorage);

builder
    .AddProject<Projects.First10_Worker>("worker")
    .WithReference(database)
    .WithEnvironment("Security__ReporterPseudonymKey", reporterPseudonymKey)
    .WithEnvironment("Security__ReporterContactKeyVersion", "local-v1")
    .WithEnvironment("Channels__Telegram__BotToken", telegramBotToken)
    .WithEnvironment("ObjectStorage__AccessKey", objectStorageAccessKey)
    .WithEnvironment("ObjectStorage__SecretKey", objectStorageSecretKey)
    .WithEnvironment("ObjectStorage__Endpoint", objectStorage.GetEndpoint("s3"))
    .WithEnvironment("MediaEncryption__ActiveKeyVersion", "local-v1")
    .WithEnvironment("MediaEncryption__Keys__local-v1", mediaEncryptionKey)
    .WithEnvironment("Models__FaceRedaction__Path", faceRedactionModelPath)
    .WithEnvironment("OpenAI__ApiKey", openAiApiKey)
    .WithEnvironment("OpenAI__SafetyIdentifierKey", openAiSafetyIdentifierKey)
    .WithEnvironment("OpenAI__TriageModel", "gpt-5.6-luna")
    .WaitFor(database)
    .WaitFor(objectStorage);

builder
    .AddViteApp("web", "../../web")
    .WithNpm(install: true)
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
