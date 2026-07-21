using First10.Infrastructure.Modules.Intake.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace First10.Infrastructure.Modules.Operations;

public sealed partial class RetentionScheduler(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RetentionScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        await ScheduleAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ScheduleAsync(stoppingToken);
        }
    }

    private async Task ScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new DeleteExpiredMedia(timeProvider.GetUtcNow()));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RetentionSchedulingFailed(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 12001,
        Level = LogLevel.Error,
        Message = "Retention scheduling failed; durable data remains pending deletion.")]
    private static partial void RetentionSchedulingFailed(ILogger logger, Exception exception);
}
