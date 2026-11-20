using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Notifications.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Jobs;

/// <summary>
/// Runs every scheduled check at start-up and then on a timer: the meeting reminders every minute, the others every
/// <c>IntervalMinutes</c>. Each check gets its own scope, so one that fails (logged, then retried on the next tick)
/// doesn't stop the others. With several API instances, the unique recipient and key index makes a racing duplicate
/// fail instead of being sent twice.
/// </summary>
public sealed class NotificationScheduler(
    IServiceScopeFactory scopes,
    IOptions<NotificationSchedulerOptions> options,
    ILogger<NotificationScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        var minute = 0;
        do
        {
            var everyCheck = minute++ % options.Value.IntervalMinutes == 0;
            foreach (var check in Enum.GetValues<ScheduledCheck>().Where(c => everyCheck || c == ScheduledCheck.MeetingReminders))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IScheduledNotifications>().RunAsync(check, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "The scheduled notification check {Check} failed.", check);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
