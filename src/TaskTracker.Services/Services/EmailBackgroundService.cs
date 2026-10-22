using Microsoft.Extensions.Hosting;

namespace TaskTracker.Services;

public sealed class EmailBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<EmailBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();
                var item = await queue.ClaimNextAsync(stoppingToken);
                if (item == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
                var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
                var result = await email.SendAsync(item.ToEmail, item.ToName, item.Subject, item.HtmlBody);
                if (result.Success) await queue.MarkSentAsync(item.Id, stoppingToken);
                else await queue.MarkFailedAsync(item.Id, result.Error, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Email outbox processing failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
