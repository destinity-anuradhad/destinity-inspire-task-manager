namespace TaskTracker.Services;

public interface IEmailService
{
    Task<EmailDeliveryResult> SendAsync(string toEmail, string toName, string subject, string htmlBody);
}

public record EmailDeliveryResult(bool Success, string? Error = null);
