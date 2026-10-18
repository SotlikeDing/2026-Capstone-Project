namespace MobileApi.Models.Auth;

public class AuthenticatedUser
{
    public int UserId { get; set; }
    public int UserRoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public List<AuthenticatedRole> AvailableRoles { get; set; } = [];
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;

    /// <summary>Only set by a credential check (login); the app may not be used before the email is verified.</summary>
    public bool IsEmailVerified { get; set; } = true;
    public bool IsSuspended { get; set; }

    public string DisplayName =>
        string.Join(" ", new[] { Name, Surname }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class AuthenticatedRole
{
    public int UserRoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsSupportedMobileRole { get; set; }
}
