using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Response shape after submitting a new permit application.</summary>
public class HuntingPermitResponse
{
    public int Id { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>A userDocumentation row read back as a hunting-permit application (see HuntingPermit_db's caveats about this not actually being filtered by document type).</summary>
public class HuntingPermitApplication
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("userRole")]
    public int UserRole { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }
}

/// <summary>Payload for a new permit application, with the SAPS 520 form embedded if applicable.</summary>
public class CreatePermitApplicationRequest
{
    public int FarmId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Species { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public SapsForm? SapsForm { get; set; }
}
