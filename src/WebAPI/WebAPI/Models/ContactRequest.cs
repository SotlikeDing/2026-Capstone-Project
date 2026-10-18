namespace WebAPI.Models;

/// <summary>
/// Unused duplicate: ContactController defines and actually uses its own
/// ContactRequest class (in the WebAPI.Controllers namespace, not this one) -
/// nothing references this WebAPI.Models version.
/// </summary>
public class ContactRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
}
