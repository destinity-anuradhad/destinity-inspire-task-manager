using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace TaskTracker.Services;

/// <summary>
/// Drains the webhook delivery outbox: claims a due row, POSTs the signed payload, and marks it
/// delivered or reschedules with backoff. Mirrors <see cref="EmailBackgroundService"/>.
/// </summary>
public sealed class WebhookBackgroundService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    ILogger<WebhookBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IWebhookService>();
                var dispatch = await service.ClaimNextAsync(stoppingToken);
                if (dispatch == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
                await SendAsync(service, dispatch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook outbox processing failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task SendAsync(IWebhookService service, WebhookDispatch dispatch, CancellationToken ct)
    {
        try
        {
            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var signature = Sign(dispatch.Secret, timestamp, dispatch.Payload);

            using var content = new StringContent(dispatch.Payload, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, dispatch.Url) { Content = content };
            request.Headers.TryAddWithoutValidation("X-TaskTracker-Event", dispatch.EventType);
            request.Headers.TryAddWithoutValidation("X-TaskTracker-Delivery", dispatch.EventId);
            request.Headers.TryAddWithoutValidation("X-TaskTracker-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("X-TaskTracker-Signature", "sha256=" + signature);
            request.Headers.UserAgent.ParseAdd("TaskTracker-Webhook/1.0");

            using var response = await client.SendAsync(request, ct);
            var code = (int)response.StatusCode;
            if (code is >= 200 and < 300)
                await service.MarkDeliveredAsync(dispatch.DeliveryId, code, ct);
            else
                await service.MarkFailedAsync(dispatch.DeliveryId, code, $"HTTP {code} {response.ReasonPhrase}", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // app shutdown — leave the row Processing; the stale-claim sweep reclaims it
        }
        catch (Exception ex)
        {
            // Timeouts (TaskCanceledException with ct not cancelled), DNS, TLS, connection refused, etc.
            await service.MarkFailedAsync(dispatch.DeliveryId, null, ex.Message, ct);
        }
    }

    /// <summary>HMAC-SHA256 over "timestamp.payload" so receivers can reject stale or replayed requests.</summary>
    private static string Sign(string secret, string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
