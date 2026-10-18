using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class Profile
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("surname")]
    public string Surname { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("profilePictureUrl")]
    public string ProfilePictureUrl { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class ProfileResponse
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime Registered { get; set; }
    public List<UserRoleData> Roles { get; set; } = new();
}

public class UpdateProfileRequest
{
    public string? Name { get; set; }
    public string? Surname { get; set; }
    public string? Phone { get; set; }
}

public class UpdateProfileResponse
{
    public string Message { get; set; } = string.Empty;
}

