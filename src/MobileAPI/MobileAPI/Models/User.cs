using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class User
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("surname")]
    public string Surname { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("registrationDate")]
    public DateTime RegistrationDate { get; set; }

    [JsonPropertyName("isEmailVerified")]
    public bool IsEmailVerified { get; set; }

    [JsonPropertyName("isPhoneVerified")]
    public bool IsPhoneVerified { get; set; }

    [JsonPropertyName("isSuspended")]
    public bool IsSuspended { get; set; }

    [JsonPropertyName("roles")]
    public List<UserRole> Roles { get; set; } = new();
}

public class UserRole
{
    [JsonPropertyName("userRoleId")]
    public int UserRoleId { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("roleId")]
    public int RoleId { get; set; }

    [JsonPropertyName("roleCode")]
    public string? RoleCode { get; set; }

    [JsonPropertyName("roleName")]
    public string? RoleName { get; set; }
}

public class Role
{
    [JsonPropertyName("roleId")]
    public int RoleId { get; set; }

    [JsonPropertyName("roleCode")]
    public string RoleCode { get; set; } = string.Empty;

    [JsonPropertyName("roleName")]
    public string RoleName { get; set; } = string.Empty;
}

public class FullUser
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("data")]
    public FullUserData[] Data { get; set; } = [];
}

public class FullUserData
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("surname")]
    public string Surname { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("registrationDate")]
    public string RegistrationDate { get; set; } = string.Empty;

    [JsonPropertyName("isEmailVerified")]
    public bool IsEmailVerified { get; set; }

    [JsonPropertyName("isPhoneVerified")]
    public bool IsPhoneVerified { get; set; }

    [JsonPropertyName("isSuspended")]
    public bool IsSuspended { get; set; }

    [JsonPropertyName("roles")]
    public object[] Roles { get; set; } = [];
}

