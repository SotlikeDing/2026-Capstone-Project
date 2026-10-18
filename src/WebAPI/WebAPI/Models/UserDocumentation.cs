using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Generic userDocumentation row shape - not the same DTO as UserDocumentModels' UserDocumentDto (that's the PJ/PH/VG/Farmer credential workflow's own shape).</summary>
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

/// <summary>A document-type reference row (documentType table).</summary>
public class DocumentType
{
    [JsonPropertyName("dtId")]
    public int DtId { get; set; }

    [JsonPropertyName("dtName")]
    public string? DtName { get; set; }

    [JsonPropertyName("dtNumber")]
    public string? DtNumber { get; set; }
}

/// <summary>Unused - HuntingPermit_db's actual request types are CreatePermitApplicationRequest and the raw SAPS 520 JsonElement, not this class.</summary>
public class HuntingPermitApplicationRequest
{
    public int? UdUserRole { get; set; }
    public string? UdName { get; set; }
    public string? UdFilePath { get; set; }
    public string? UdStatus { get; set; }
    public int? UdDtId { get; set; }
}

/// <summary>Response shape for a Permit_db permit record.</summary>
public class PermitResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? Issued { get; set; }
    public DateTime? Expires { get; set; }
}

/// <summary>Response shape for a Weapon_db weapon record.</summary>
public class WeaponResponse
{
    public int Id { get; set; }
    public int UserRoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FilePath { get; set; }
}

/// <summary>Payload for registering a weapon - UdDtId defaults to 5, the hardcoded "weapon" document type in the shared userDocumentation table.</summary>
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

/// <summary>Payload for issuing a permit - UdDtId defaults to 1, the hardcoded "SAPS 520" document type in the shared userDocumentation table.</summary>
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
