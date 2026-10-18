using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class Package
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("durationDays")]
    public int DurationDays { get; set; }

    [JsonPropertyName("maxParticipants")]
    public int MaxParticipants { get; set; }

    [JsonPropertyName("includedItems")]
    public string IncludedItems { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Active";

    [JsonPropertyName("createdTimestamp")]
    public DateTime CreatedTimestamp { get; set; }

    [JsonPropertyName("availableFrom")]
    public DateTime? AvailableFrom { get; set; }

    [JsonPropertyName("availableTill")]
    public DateTime? AvailableTill { get; set; }

    [JsonPropertyName("farmId")]
    public int? FarmId { get; set; }

    [JsonPropertyName("pjId")]
    public int? PjId { get; set; }

    [JsonPropertyName("minExpeditionLength")]
    public int? MinExpeditionLength { get; set; }

    [JsonPropertyName("minGuests")]
    public int? MinGuests { get; set; }

    [JsonPropertyName("farm")]
    public Farm? Farm { get; set; }
}

public class CreatePackageRequest
{
    public string PTitle { get; set; } = string.Empty;
    public string PDescription { get; set; } = string.Empty;
    public decimal PStockPrice { get; set; }
    public int PDurationDays { get; set; }
    public int PMaxParticipants { get; set; }
    public string PIncludedItems { get; set; } = string.Empty;
    public DateTime? PAvailableFrom { get; set; }
    public DateTime? PAvailableTill { get; set; }
    public int? PFarmId { get; set; }
    public int? PPjId { get; set; }
    public int? PMinExpeditionLength { get; set; }
    public int? PMinGuests { get; set; }
    public int? PMaxGuests { get; set; }
    public string PStatus { get; set; } = "Active";
}

public class UpdatePackageRequest
{
    public string? PTitle { get; set; }
    public string? PDescription { get; set; }
    public decimal? PStockPrice { get; set; }
    public int? PDurationDays { get; set; }
    public int? PMaxParticipants { get; set; }
    public string? PIncludedItems { get; set; }
    public string? PStatus { get; set; }
    public DateTime? PAvailableFrom { get; set; }
    public DateTime? PAvailableTill { get; set; }
    public int? PMinExpeditionLength { get; set; }
    public int? PMinGuests { get; set; }
    public int? PMaxGuests { get; set; }
}

public class PackageResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal StockPrice { get; set; }
    public int DurationDays { get; set; }
    public int MaxParticipants { get; set; }
    public string IncludedItems { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? AvailableFrom { get; set; }
    public DateTime? AvailableTill { get; set; }
    public int? FarmId { get; set; }
    public int? PjId { get; set; }
    public int? MinExpeditionLength { get; set; }
    public int? MinGuests { get; set; }
    public int? MaxGuests { get; set; }
}

public class PackageFilterRequest
{
    public int? PFarmId { get; set; }
    public int? PPjId { get; set; }
    public string? PStatus { get; set; }
}

public class VgPackage
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public decimal Price { get; set; }
}

