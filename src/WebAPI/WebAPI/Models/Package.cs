using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>
/// Only referenced as the type of a Booking model's optional Package property -
/// Package_db's actual reads return PackageResponse instead, so this class is
/// never populated anywhere in this codebase.
/// </summary>
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

/// <summary>Payload for creating a new package.</summary>
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
    /// <summary>What the price includes, one item per line.</summary>
    public string? PInclusions { get; set; }
    /// <summary>What the hunter pays separately, one item per line.</summary>
    public string? PExclusions { get; set; }
}

/// <summary>Partial-update payload for a package - null fields are left unchanged.</summary>
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
    public int? PFarmId { get; set; }
    public string? PInclusions { get; set; }
    public string? PExclusions { get; set; }
}

/// <summary>Response shape for a package, denormalized with farm/organiser info for display without a separate lookup.</summary>
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
    public string FarmName { get; set; } = string.Empty;
    public string Province { get; set; } = "Limpopo";
    public string Location { get; set; } = string.Empty;
    public string OrganiserName { get; set; } = string.Empty;
    public int OrganiserUserId { get; set; }
    /// <summary>The farm's farmer has every required document approved (or the package has no farm).</summary>
    public bool FarmerDocumentsApproved { get; set; } = true;
    /// <summary>The organising PJ has every required document approved.</summary>
    public bool OrganiserDocumentsApproved { get; set; } = true;
    /// <summary>Shown on the marketplace: both of the above.</summary>
    public bool IsPubliclyVisible => FarmerDocumentsApproved && OrganiserDocumentsApproved;
    /// <summary>What the price includes, one item per line ("Ingesluit").</summary>
    public string Inclusions { get; set; } = string.Empty;
    /// <summary>What is paid separately, one item per line ("Nie ingesluit nie").</summary>
    public string Exclusions { get; set; } = string.Empty;
    /// <summary>The package's farm has photos (served by api/farmer/{farmId}/images/{index}).</summary>
    public bool FarmHasImages { get; set; }
}

/// <summary>Structured search filters for package browsing.</summary>
public class PackageFilterRequest
{
    public int? PFarmId { get; set; }
    public int? PPjId { get; set; }
    public string? PStatus { get; set; }
}

/// <summary>Unused - no controller or database class references this class (VgPackageResponse in PhPricingModels.cs is the real VG-package shape).</summary>
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


/// <summary>A page of packages plus the total count, for pagination UI.</summary>
public class PaginatedPackageResponse
{
    public System.Collections.Generic.List<PackageResponse> Data { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
