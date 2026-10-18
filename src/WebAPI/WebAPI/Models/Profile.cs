using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Unused - Profile_db/ProfileController use ProfileResponse below instead.</summary>
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

/// <summary>Response shape for Profile_db.GetProfile - a user's base profile plus their assigned roles.</summary>
public class ProfileResponse
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? IdPassportNum { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public DateTime Registered { get; set; }
    public List<UserRoleData> Roles { get; set; } = new();
}

/// <summary>Partial-update payload for a user's base profile - null fields are left unchanged.</summary>
public class UpdateProfileRequest
{
    public string? Name { get; set; }
    public string? Surname { get; set; }
    public string? Phone { get; set; }
    public string? IdPassportNum { get; set; }
    public string? ProfilePictureUrl { get; set; }
}

/// <summary>
/// Server-side checks for <see cref="UpdateProfileRequest"/> - the WebApp validates the same
/// fields more strictly, but the endpoint can be called directly. Values are trimmed in place.
/// Limits follow the users table (uPhone is nvarchar(15), uIdPassportNum nvarchar(100)).
/// </summary>
public static class UpdateProfileValidator
{
    public const int MaxNameLength = 50;
    public const int MaxPhoneLength = 15;
    public const int MaxIdPassportLength = 100;
    /// <summary>A 300x300 PNG as a data URL is ~250 KB; this leaves room without letting the column grow unbounded.</summary>
    public const int MaxPictureLength = 2_000_000;

    private static readonly System.Text.RegularExpressions.Regex NamePattern = new(@"^\p{L}[\p{L}\p{M} .'-]*$");
    private static readonly System.Text.RegularExpressions.Regex PhonePattern = new(@"^\+?[0-9 ()-]+$");
    private static readonly System.Text.RegularExpressions.Regex IdPattern = new(@"^[A-Za-z0-9 .<-]+$");
    private static readonly System.Text.RegularExpressions.Regex PicturePattern = new(@"^data:image/(png|jpeg|webp);base64,[A-Za-z0-9+/=]+$");

    /// <summary>Null when the request is fine, else the Afrikaans message for the first problem found.</summary>
    public static string? Validate(UpdateProfileRequest r)
    {
        if (r.Name != null)
        {
            r.Name = r.Name.Trim();
            if (!ValidName(r.Name)) return $"Voornaam moet 1-{MaxNameLength} letters wees (spasies, koppeltekens en apostrofs mag).";
        }
        if (r.Surname != null)
        {
            r.Surname = r.Surname.Trim();
            if (!ValidName(r.Surname)) return $"Van moet 1-{MaxNameLength} letters wees (spasies, koppeltekens en apostrofs mag).";
        }
        if (r.Phone != null)
        {
            r.Phone = r.Phone.Trim();
            int digits = r.Phone.Count(char.IsAsciiDigit);
            if (r.Phone.Length > MaxPhoneLength || !PhonePattern.IsMatch(r.Phone) || digits < 9)
                return $"Telefoonnommer moet 9-{MaxPhoneLength} syfers wees, bv. 082 555 0192 of +27825550192.";
        }
        if (r.IdPassportNum != null)
        {
            r.IdPassportNum = r.IdPassportNum.Trim();
            if (r.IdPassportNum.Length > MaxIdPassportLength || (r.IdPassportNum.Length > 0 && !IdPattern.IsMatch(r.IdPassportNum)))
                return "ID- of paspoortnommer mag net letters, syfers, spasies en - . < bevat.";
        }
        if (!string.IsNullOrEmpty(r.ProfilePictureUrl))
        {
            if (r.ProfilePictureUrl.Length > MaxPictureLength || !PicturePattern.IsMatch(r.ProfilePictureUrl))
                return "Profielfoto moet 'n PNG-, JPEG- of WebP-prent kleiner as 1.5 MB wees.";
        }
        return null;
    }

    private static bool ValidName(string value) => value.Length is >= 1 and <= MaxNameLength && NamePattern.IsMatch(value);
}

/// <summary>Response shape after a profile update.</summary>
public class UpdateProfileResponse
{
    public string Message { get; set; } = string.Empty;
}
