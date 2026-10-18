using Microsoft.AspNetCore.Mvc;
using WebAPI.Services;

namespace WebAPI.Controllers;

/// <summary>Handles the public "Contact Us" form: emails the support team and sends the submitter a confirmation receipt.</summary>
[ApiController]
[Route("api/[controller]")]
public class ContactController : ControllerBase
{
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    public ContactController(IEmailService emailService, IConfiguration config)
    {
        _emailService = emailService;
        _config = config;
    }

    /// <summary>
    /// Validates the submission and returns success immediately - both emails are
    /// fired off on a background Task rather than awaited, so a slow/failed SMTP
    /// send never delays or fails the HTTP response the visitor is waiting on.
    /// </summary>
    [HttpPost]
    public IActionResult SendContactEmail([FromBody] ContactRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Message))
        {
            return BadRequest(new { Message = "Ongeldige versoek", Success = false });
        }

        string adminSubject = $"Nuwe MikPunt Kontak Ons Versoek: {request.Subject}";
        string adminBody = $@"
            <h3>MikPunt Kontak Vorm Ontvang</h3>
            <p><strong>Naam:</strong> {request.Name}</p>
            <p><strong>E-pos:</strong> {request.Email}</p>
            <p><strong>Onderwerp:</strong> {request.Subject}</p>
            <p><strong>Boodskap:</strong><br/>{request.Message.Replace("\n", "<br/>")}</p>
        ";

        string userSubject = $"Ons het jou boodskap ontvang: {request.Subject}";
        string userBody = $@"
            <h3>Beste {request.Name},</h3>
            <p>Baie dankie dat jy met <strong>MikPunt</strong> in verbinding getree het!</p>
            <p>Ons het jou navraag met die onderwerp <em>'{request.Subject}'</em> suksesvol ontvang. Ons ondersteuningspan hersien jou boodskap en sal so gou moontlik met jou in verbinding tree.</p>
            <hr/>
            <p><strong>Jou Oorspronklike Boodskap:</strong></p>
            <p>{request.Message.Replace("\n", "<br/>")}</p>
            <br/>
            <p>Vriendelike groete,<br/><strong>MikPunt Ondersteuningspan</strong></p>
        ";

        // Get the support email address
        string supportEmail = _config["EmailSettings:SenderEmail"] ?? "support@mikpunt.co.za";

        _ = Task.Run(async () => 
        {
            try
            {
                // 1. Send notification to support team
                await _emailService.SendEmailAsync(supportEmail, adminSubject, adminBody, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EMAIL BACKGROUND] Failed to send contact notification to support ({supportEmail}): {ex.Message}");
            }

            try
            {
                // 2. Send confirmation receipt to the user who submitted the form
                await _emailService.SendEmailAsync(request.Email, userSubject, userBody, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EMAIL BACKGROUND] Failed to send receipt confirmation to user ({request.Email}): {ex.Message}");
            }
        });

        return Ok(new { Message = "Sukses", Success = true });
    }
}

/// <summary>Payload for the public contact form.</summary>
public class ContactRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
}

