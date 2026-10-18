using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class LoginRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("RoleSelection")]
    public string RoleSelection { get; set; } = string.Empty;
}

public class SignInResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("roles")]
    public List<UserRoleData> Roles { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class RegisterRequest
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("surname")]
    public string Surname { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonIgnore]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class RegisterResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}

public class RegisterApiResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public int UserId { get; set; }
}

public class SelectRoleRequest
{
    public int UserId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
}

public class SelectRoleResponse
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? RoleCode { get; set; }
    public string? RoleName { get; set; }
    public string? Status { get; set; }
    public DateTime AssignedDate { get; set; }
}

public class UserData
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? PasswordHash { get; set; } = string.Empty;
    public DateTime? RegDate { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsPhoneVerified { get; set; }
    public List<UserRoleData> Roles { get; set; } = new();
}

public class UserRoleData
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime AssignedDate { get; set; }
}

public class UserResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("user")]
    public UserData? User { get; set; }
}

public class UserRoleResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("role")]
    public UserRoleData? Role { get; set; }
}

public class UserRequest
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
}

public class UserRoleRequest
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("roleCode")]
    public string RoleCode { get; set; } = string.Empty;
}

public class UserDataResponse
{
    [JsonPropertyName("user")]
    public UserData User { get; set; } = new();
}

public class RoleItem
{
    public RoleItem() { }
    public RoleItem(string name, string icon, string shortDescription, string longDescription)
    {
        Name = name;
        Icon = icon;
        ShortDescription = shortDescription;
        LongDescription = longDescription;
    }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("shortDescription")]
    public string ShortDescription { get; set; } = string.Empty;

    [JsonPropertyName("longDescription")]
    public string LongDescription { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

public class ForgotPasswordResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("newPassword")]
    public string NewPassword { get; set; } = string.Empty;
}

public class ResetPasswordResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class ResendVerificationEmailRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

