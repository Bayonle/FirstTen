namespace First10.Infrastructure.Messaging;

public static class First10Queues
{
    public const string FastIntake = "fast-intake";
    public const string MediaAndAi = "media-ai";
    public const string Outbound = "outbound";
    public const string Maintenance = "maintenance";
    public const string UiNotifications = "ui-notifications";
}

public enum First10RuntimeRole
{
    Api,
    Worker
}
