using Microsoft.Extensions.Hosting;

namespace TaskTracker.Services;

/// <summary>
/// Periodically evaluates time-based automation rules (e.g. "due soon" reminders) that no single task
/// write can trigger. Runs hourly; the engine de-duplicates per (rule, task) per day, so a task due
/// tomorrow is reminded once regardless of how many sweeps see it. Mirrors
/// <see cref="WebhookBackgroundService"/>.
/// </summary>
public sealed class AutomationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<AutomationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }   // shutting down before the first sweep

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAutomationService>();
                var count = await service.RunScheduledAsync(stoppingToken);
                if (count > 0)
                    logger.LogInformation("Automation: {Count} scheduled action(s) executed.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled automation sweep failed.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
