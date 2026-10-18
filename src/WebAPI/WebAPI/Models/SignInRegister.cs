using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Payload for signing in.</summary>
public class LoginRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Unused - no controller or database class references this class; SignInResponse below is the real shape UserController.SignIn returns.</summary>
public class LoginResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("RoleSelection")]
    public string RoleSelection { get; set; } = string.Empty;
}

/// <summary>Response shape for a successful sign-in.</summary>
public class SignInResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    // WebApp's LoginApiResponseModel expects "accessToken" (camelCase, matching the
    // rest of this payload and every other endpoint) - the previous "access_token"
    // name meant WebApp could never actually read the real token and silently fell
    // back to a hardcoded placeholder string on every login.
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("roles")]
    public List<UserRoleData> Roles { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>Payload for exchanging a refresh token for a new access token.</summary>
public class RefreshTokenRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Registration payload - covers the base account fields plus every role's
/// specific fields flattened into one form (only the fields matching the
/// selected role(s) are actually persisted, by User_db.RegisterUser).
/// </summary>
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

    [JsonPropertyName("phone_country_code")]
    public string PhoneCountryCode { get; set; } = "+27";

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("primary_role_code")]
    public string PrimaryRoleCode { get; set; } = "Jagter"; // 'Jagter', 'Boer', 'PJ', 'PH', 'VG', 'AP'

    [JsonPropertyName("selected_roles")]
    public List<string> SelectedRoles { get; set; } = new();

    [JsonPropertyName("profile_picture_url")]
    public string? ProfilePictureUrl { get; set; }

    // Role-specific fields
    [JsonPropertyName("leaderboard_nickname")]
    public string LeaderboardNickname { get; set; } = string.Empty;

    [JsonPropertyName("passport_or_id_number")]
    public string PassportOrIdNumber { get; set; } = string.Empty;

    [JsonPropertyName("pj_license_number")]
    public string PjLicenseNumber { get; set; } = string.Empty;

    [JsonPropertyName("pj_license_expiry")]
    public string PjLicenseExpiry { get; set; } = string.Empty;

    [JsonPropertyName("pj_province_documents")]
    public string PjProvinceDocuments { get; set; } = string.Empty;

    [JsonPropertyName("cipc_reg_number")]
    public string CipcRegNumber { get; set; } = string.Empty;

    [JsonPropertyName("tax_vat_number")]
    public string TaxVatNumber { get; set; } = string.Empty;

    [JsonPropertyName("physical_address")]
    public string PhysicalAddress { get; set; } = string.Empty;

    [JsonPropertyName("ph_license_number")]
    public string PhLicenseNumber { get; set; } = string.Empty;

    [JsonPropertyName("ph_issuing_province")]
    public string PhIssuingProvince { get; set; } = string.Empty;

    [JsonPropertyName("ph_license_expiry")]
    public string PhLicenseExpiry { get; set; } = string.Empty;

    [JsonPropertyName("dangerous_game_endorsement")]
    public string DangerousGameEndorsement { get; set; } = string.Empty;

    [JsonPropertyName("ph_association_name")]
    public string PhAssociationName { get; set; } = string.Empty;

    [JsonPropertyName("ph_association_member_number")]
    public string PhAssociationMemberNumber { get; set; } = string.Empty;

    [JsonPropertyName("ph_association_document")]
    public string PhAssociationDocument { get; set; } = string.Empty;

    [JsonPropertyName("first_aid_cert_details")]
    public string FirstAidCertDetails { get; set; } = string.Empty;

    [JsonPropertyName("first_aid_expiry")]
    public string FirstAidExpiry { get; set; } = string.Empty;

    [JsonPropertyName("firearm_competency_details")]
    public string FirearmCompetencyDetails { get; set; } = string.Empty;

    [JsonPropertyName("drone_licenses")]
    public string DroneLicenses { get; set; } = string.Empty;

    [JsonPropertyName("media_qualifications")]
    public string MediaQualifications { get; set; } = string.Empty;

    [JsonPropertyName("farm_name")]
    public string FarmName { get; set; } = string.Empty;

    [JsonPropertyName("farm_region")]
    public string FarmRegion { get; set; } = string.Empty;

    [JsonPropertyName("farm_reg_number")]
    public string FarmRegNumber { get; set; } = string.Empty;

    [JsonPropertyName("business_name")]
    public string BusinessName { get; set; } = string.Empty;

    [JsonPropertyName("terms_accepted")]
    public bool TermsAccepted { get; set; } = false;

    [JsonIgnore]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>Response shape after registering.</summary>
public class RegisterResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}

/// <summary>Unused - no controller or database class references this class.</summary>
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

/// <summary>Payload for assigning an additional role to a user.</summary>
public class SelectRoleRequest
{
    public int UserId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
}

/// <summary>Response shape after selecting/assigning a role.</summary>
public class SelectRoleResponse
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? RoleCode { get; set; }
    public string? RoleName { get; set; }
    public string? Status { get; set; }
    public DateTime AssignedDate { get; set; }
}

/// <summary>Unused - no controller or database class references this class.</summary>
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

/// <summary>A role assignment as shown to the client (SignInResponse.Roles, etc.).</summary>
public class UserRoleData
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime AssignedDate { get; set; }
}

/// <summary>Unused - see UserData's remarks.</summary>
public class UserResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("user")]
    public UserData? User { get; set; }
}

/// <summary>Unused - no controller or database class references this class.</summary>
public class UserRoleResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("role")]
    public UserRoleData? Role { get; set; }
}

/// <summary>Unused - no controller or database class references this class.</summary>
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

/// <summary>Unused - no controller or database class references this class.</summary>
public class UserRoleRequest
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("roleCode")]
    public string RoleCode { get; set; } = string.Empty;
}

/// <summary>Unused - see UserData's remarks.</summary>
public class UserDataResponse
{
    [JsonPropertyName("user")]
    public UserData User { get; set; } = new();
}

/// <summary>Unused - no controller or database class references this class (a role-catalogue display card shape, never wired up).</summary>
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

/// <summary>Payload for starting the password-reset flow.</summary>
public class ForgotPasswordRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Response shape after requesting a password reset - always reports success regardless of whether the email exists, to prevent account enumeration.</summary>
public class ForgotPasswordResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>Payload for completing the password-reset flow.</summary>
public class ResetPasswordRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("newPassword")]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Response shape after resetting a password.</summary>
public class ResetPasswordResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>Payload for requesting a fresh email-verification code.</summary>
public class ResendVerificationEmailRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}
