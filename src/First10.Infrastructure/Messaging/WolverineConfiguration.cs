using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace First10.Infrastructure.Messaging;

public static class WolverineConfiguration
{
    public static void Configure(
        WolverineOptions options,
        string connectionString,
        First10RuntimeRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        options.UsePostgresqlPersistenceAndTransport(
                connectionString,
                "wolverine",
                transportSchema: "wolverine_queues")
            .AutoProvision();
        options.UseEntityFrameworkCoreTransactions();
        options.Policies.AutoApplyTransactions();

        if (role is First10RuntimeRole.Api)
        {
            options.ListenToPostgresqlQueue(First10Queues.UiNotifications);
            return;
        }

        options.ListenToPostgresqlQueue(First10Queues.FastIntake);
        options.ListenToPostgresqlQueue(First10Queues.MediaAndAi);
        options.ListenToPostgresqlQueue(First10Queues.Outbound);
        options.ListenToPostgresqlQueue(First10Queues.Maintenance);
    }
}
