using First10.Infrastructure.Persistence;
using First10.Infrastructure.Persistence.Writes;
using First10.Infrastructure.Messaging;
using First10.Modules.BuildingBlocks.Contracts;
using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace First10.IntegrationTests.Persistence;

[Collection(PostgresTestGroup.Name)]
public sealed class OutboxDeliveryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DuplicateChannelMessageCreatesOneReceiptAndOneDownstreamCommand()
    {
        var options = PersistenceConfiguration.CreateOptions(postgres.ConnectionString);
        await using var database = new First10DbContext(options);
        await database.Database.MigrateAsync();

        var identity = SemanticMessageIdentity.Create("telegram", $"update-{Guid.NewGuid():N}");
        var command = new AcceptedInboundWork(Guid.NewGuid(), "trace-safe-01");

        var firstAccepted = await InboundReceiptWriter.TryAcceptAsync(database, identity, command);
        var replayAccepted = await InboundReceiptWriter.TryAcceptAsync(database, identity, command);

        Assert.True(firstAccepted);
        Assert.False(replayAccepted);
        Assert.Equal(1, await database.InboundMessageReceipts.CountAsync(x => x.Scope == identity.Scope && x.Key == identity.Key));
        Assert.Equal(1, await database.OutboundMessageRecords.CountAsync(x => x.SemanticIdentity == command.WorkId));
    }

    [Fact]
    public async Task ConcurrentReceiptWritesAcceptExactlyOneCopy()
    {
        var options = PersistenceConfiguration.CreateOptions(postgres.ConnectionString);
        await using (var migrationDatabase = new First10DbContext(options))
        {
            await migrationDatabase.Database.MigrateAsync();
        }

        var identity = SemanticMessageIdentity.Create("telegram", $"concurrent-{Guid.NewGuid():N}");
        var command = new AcceptedInboundWork(Guid.NewGuid(), "trace-safe-02");
        await using var firstDatabase = new First10DbContext(options);
        await using var secondDatabase = new First10DbContext(options);

        var outcomes = await Task.WhenAll(
            InboundReceiptWriter.TryAcceptAsync(firstDatabase, identity, command),
            InboundReceiptWriter.TryAcceptAsync(secondDatabase, identity, command));

        Assert.Single(outcomes, accepted => accepted);
        await using var verificationDatabase = new First10DbContext(options);
        Assert.Equal(1, await verificationDatabase.InboundMessageReceipts.CountAsync(
            x => x.Scope == identity.Scope && x.Key == identity.Key));
        Assert.Equal(1, await verificationDatabase.OutboundMessageRecords.CountAsync(
            x => x.SemanticIdentity == command.WorkId));
    }

    [Fact]
    public async Task SemanticOutboundReplayDoesNotCreateAnotherDeliveryRecord()
    {
        var options = PersistenceConfiguration.CreateOptions(postgres.ConnectionString);
        await using var database = new First10DbContext(options);
        await database.Database.MigrateAsync();
        var command = new AcceptedInboundWork(Guid.NewGuid(), "trace-safe-03");

        var firstAccepted = await InboundReceiptWriter.TryAcceptAsync(
            database,
            SemanticMessageIdentity.Create("telegram", $"first-{Guid.NewGuid():N}"),
            command);
        var replayAccepted = await InboundReceiptWriter.TryAcceptAsync(
            database,
            SemanticMessageIdentity.Create("telegram", $"second-{Guid.NewGuid():N}"),
            command);

        Assert.True(firstAccepted);
        Assert.False(replayAccepted);
        Assert.Equal(1, await database.OutboundMessageRecords.CountAsync(
            x => x.SemanticIdentity == command.WorkId));
    }

    [Fact]
    public async Task EfOutboxCommitsStateAndDeliversThroughPostgres()
    {
        var options = PersistenceConfiguration.CreateOptions(postgres.ConnectionString);
        await using (var database = new First10DbContext(options))
        {
            await database.Database.MigrateAsync();
        }

        ProbeDelivery.Reset();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddFirst10Persistence(postgres.ConnectionString);
        builder.UseWolverine(wolverine =>
        {
            WolverineConfiguration.Configure(
                wolverine,
                postgres.ConnectionString,
                First10RuntimeRole.Worker);
            wolverine.PublishMessage<OutboxProbe>().ToPostgresqlQueue(First10Queues.Maintenance);
            wolverine.Discovery.IncludeAssembly(typeof(OutboxProbeHandler).Assembly);
        });

        using var host = builder.Build();
        await host.StartAsync();

        var probe = new OutboxProbe(Guid.NewGuid());
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<First10DbContext>>();
            outbox.DbContext.InboundMessageReceipts.Add(InboundMessageReceipt.Create(
                "test",
                $"outbox-{probe.Id:N}",
                probe.Id,
                DateTimeOffset.UtcNow));
            await outbox.PublishAsync(probe);
            await outbox.SaveChangesAndFlushMessagesAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.Equal(probe.Id, await ProbeDelivery.WaitAsync(timeout.Token));
        await host.StopAsync();
    }
}

public sealed record OutboxProbe(Guid Id);

public static class OutboxProbeHandler
{
    public static void Handle(OutboxProbe probe) => ProbeDelivery.Signal(probe.Id);
}

internal static class ProbeDelivery
{
    private static TaskCompletionSource<Guid> _completion = CreateCompletion();

    public static void Reset() => _completion = CreateCompletion();

    public static void Signal(Guid id) => _completion.TrySetResult(id);

    public static Task<Guid> WaitAsync(CancellationToken cancellationToken) =>
        _completion.Task.WaitAsync(cancellationToken);

    private static TaskCompletionSource<Guid> CreateCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
