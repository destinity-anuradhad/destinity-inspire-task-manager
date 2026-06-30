using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace TaskTracker.Services;

public class SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger) : IEmailService
{
    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        var cfg = config.GetSection("Email");
        var password = cfg["Password"] ?? "";
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Email not sent (no SMTP password configured): {Subject} → {To}", subject, toEmail);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(cfg["FromName"] ?? "Task Manager", cfg["FromAddress"]));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(cfg["SmtpHost"], int.Parse(cfg["SmtpPort"] ?? "587"), SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(cfg["Username"], password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            logger.LogInformation("Email sent: {Subject} → {To}", subject, toEmail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email: {Subject} → {To}", subject, toEmail);
        }
    }
}
