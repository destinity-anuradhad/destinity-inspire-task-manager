using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using TaskTracker.Data;
using TaskTracker.Models;
using TaskTracker.Services.Options;

namespace TaskTracker.Services;

public class SmtpEmailService(IOptions<EmailOptions> opts, ILogger<SmtpEmailService> logger, IServiceScopeFactory scopeFactory) : IEmailService
{
    private readonly EmailOptions _email = opts.Value;

    public async Task<EmailDeliveryResult> SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        bool    success      = false;
        string? errorMessage = null;

        if (string.IsNullOrWhiteSpace(_email.Password))
        {
            logger.LogWarning("Email not sent (no SMTP password configured): {Subject} → {To}", subject, toEmail);
            errorMessage = "No SMTP password configured";
        }
        else
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                string.IsNullOrEmpty(_email.FromName) ? "Task Manager" : _email.FromName,
                _email.FromAddress));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;
            message.Body    = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            try
            {
                await client.ConnectAsync(_email.SmtpHost, _email.SmtpPort, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(_email.Username, _email.Password);
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

        return new EmailDeliveryResult(success, errorMessage);
    }
}
