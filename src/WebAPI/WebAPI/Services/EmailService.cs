using System.Net;
using System.Net.Mail;

namespace WebAPI.Services;

/// <summary>Gmail SMTP email sender. Falls back to console-logging the email (dev mode) when sender credentials aren't configured, rather than failing.</summary>
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

    /// <summary>
    /// Sends via SMTP, or logs to console if no sender credentials are configured.
    /// Never throws - a send failure is caught and logged internally (with the
    /// email body dumped to console as a fallback), which is why every caller's own
    /// wrapping try/catch around this call is technically redundant defense-in-depth
    /// rather than strictly necessary.
    /// </summary>
    public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_senderEmail) || string.IsNullOrWhiteSpace(_senderPassword))
            {
                Console.WriteLine($"[SMTP DEV LOG] Email to '{toEmail}' | Subject: '{subject}'");
                Console.WriteLine($"[SMTP DEV LOG] Body/OTP Token Details:\n{body}\n");
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
            Console.WriteLine($"[SMTP SUCCESS] Successfully sent email to {toEmail} via {_smtpServer}:{_smtpPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SMTP ERROR] Could not send email to {toEmail} via SMTP. Error: {ex.Message}");
            Console.WriteLine($"[SMTP DEV FALLBACK] Body/OTP Token Details for {toEmail}:\n{body}\n");
        }
    }
}
