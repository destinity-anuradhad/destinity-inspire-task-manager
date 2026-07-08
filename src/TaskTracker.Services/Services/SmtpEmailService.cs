using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using TaskTracker.Data;
using TaskTracker.Models;

namespace TaskTracker.Services;

public class SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger, IServiceScopeFactory scopeFactory) : IEmailService
{
    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        var cfg      = config.GetSection("Email");
        var password = cfg["Password"] ?? "";

        bool    success      = false;
        string? errorMessage = null;

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Email not sent (no SMTP password configured): {Subject} → {To}", subject, toEmail);
            errorMessage = "No SMTP password configured";
        }
        else
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(cfg["FromName"] ?? "Task Manager", cfg["FromAddress"]));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;
            message.Body    = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            try
            {
                await client.ConnectAsync(cfg["SmtpHost"], int.Parse(cfg["SmtpPort"] ?? "587"), SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(cfg["Username"], password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                logger.LogInformation("Email sent: {Subject} → {To}", subject, toEmail);
                success = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email: {Subject} → {To}", subject, toEmail);
                errorMessage = ex.Message;
            }
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EmailLogs.Add(new EmailLog
            {
                ToEmail      = toEmail,
                ToName       = toName,
                Subject      = subject,
                HtmlBody     = htmlBody,
                SentAt       = DateTime.Now,
                IsSuccess    = success,
                ErrorMessage = errorMessage
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write email log for: {Subject} → {To}", subject, toEmail);
        }
    }
}
