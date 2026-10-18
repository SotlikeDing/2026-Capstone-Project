namespace MobileApi.Models.Auth;

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int UserId { get; set; }
    public int UserRoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string Role { get; set; } = "Restricted";
    public IReadOnlyCollection<string> Capabilities { get; set; } = [];
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
