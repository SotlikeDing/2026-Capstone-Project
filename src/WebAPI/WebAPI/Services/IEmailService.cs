namespace WebAPI.Services;

/// <summary>Outbound email abstraction - see EmailService for the Gmail SMTP implementation.</summary>
public interface IEmailService
{
    /// <summary>Sends one email. Callers throughout the codebase treat failures as best-effort (wrap this in try/catch and swallow) rather than letting a broken mail server fail the underlying action.</summary>
    Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
}
