using System.Net;
using System.Net.Mail;

namespace MobileApi.Services;

public interface IEmailService
{
    /// <summary>Sends one email. Never throws: failures are logged, because a mail problem must not undo the action that triggered it.</summary>
    Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
}

/// <summary>
/// SMTP sender reading the same EmailSettings section as the WebAPI's EmailService, so
/// both APIs send from the same MikPunt address. Without sender credentials (dev) it
/// logs the email to the console instead.
/// </summary>
public class EmailService : IEmailService
{
    private readonly string _smtpServer;
    private readonly int _smtpPort;
    private readonly bool _enableSsl;
    private readonly string _senderEmail;
    private readonly string _senderPassword;

    public EmailService(IConfiguration config)
    {
        _smtpServer = config["EmailSettings:SmtpServer"] ?? "smtp.gmail.com";
        _smtpPort = int.TryParse(config["EmailSettings:SmtpPort"], out int p) ? p : 587;
        _enableSsl = !bool.TryParse(config["EmailSettings:EnableSsl"], out bool ssl) || ssl;
        _senderEmail = config["EmailSettings:SenderEmail"] ?? "";
        _senderPassword = config["EmailSettings:SenderPassword"] ?? "";
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_senderEmail) || string.IsNullOrWhiteSpace(_senderPassword))
            {
                Console.WriteLine($"[SMTP DEV LOG] Email to '{toEmail}' | Subject: '{subject}'\n{body}\n");
                return;
            }

            using var client = new SmtpClient(_smtpServer, _smtpPort)
            {
                Credentials = new NetworkCredential(_senderEmail, _senderPassword),
                EnableSsl = _enableSsl
            };
            using var mailMessage = new MailMessage
            {
                From = new MailAddress(_senderEmail, "MikPunt Ondersteuning"),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };
            mailMessage.To.Add(toEmail);
            await client.SendMailAsync(mailMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SMTP ERROR] Could not send email to {toEmail}: {ex.Message}");
        }
    }
}
