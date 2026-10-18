using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class UserDocumentation
{
    [JsonPropertyName("udId")]
    public int UdId { get; set; }

    [JsonPropertyName("userRoleId")]
    public int UserRoleId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }

    [JsonPropertyName("issuedTimestamp")]
    public DateTime? IssuedTimestamp { get; set; }

    [JsonPropertyName("expirationTimestamp")]
    public DateTime? ExpirationTimestamp { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Pending";

    [JsonPropertyName("uploadedTimestamp")]
    public DateTime? UploadedTimestamp { get; set; }

    [JsonPropertyName("dtId")]
    public int? DtId { get; set; }

    [JsonPropertyName("documentType")]
    public DocumentType? DocumentType { get; set; }
}

public class DocumentType
{
    [JsonPropertyName("dtId")]
    public int DtId { get; set; }

    [JsonPropertyName("dtName")]
    public string? DtName { get; set; }

    [JsonPropertyName("dtNumber")]
    public string? DtNumber { get; set; }
}

public class HuntingPermitApplicationRequest
{
    public int? UdUserRole { get; set; }
    public string? UdName { get; set; }
    public string? UdFilePath { get; set; }
    public string? UdStatus { get; set; }
    public int? UdDtId { get; set; }
}

public class WeaponResponse
{
    public int Id { get; set; }
    public int UserRoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FilePath { get; set; }
}

public class CreateWeaponRequest
{
    public int UdUserRole { get; set; }
    public string UdName { get; set; } = "Weapon";
    public string? UdFilePath { get; set; }
    public DateTime? UdIssuedTimestamp { get; set; }
    public DateTime? UdExpirationTimestamp { get; set; }
    public string UdStatus { get; set; } = "Pending";
    public int UdDtId { get; set; } = 5;
}

public class CreateUserPermitRequest
{
    public int UdUserRole { get; set; }
    public string UdName { get; set; } = string.Empty;
    public string? UdFilePath { get; set; }
    public DateTime? UdIssuedTimestamp { get; set; }
    public DateTime? UdExpirationTimestamp { get; set; }
    public string UdStatus { get; set; } = "Pending";
    public int UdDtId { get; set; } = 1;
}

