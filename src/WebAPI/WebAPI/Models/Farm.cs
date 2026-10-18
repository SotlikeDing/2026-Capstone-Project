using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Only referenced as the type of Package's optional Farm property - Farmer_db's actual reads return FarmResponse/FarmDetailResponse instead, so this class is never populated anywhere.</summary>
public class Farm
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("size")]
    public double Size { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("farmerUserRoleId")]
    public int FarmerUserRoleId { get; set; }

    [JsonPropertyName("terrainType")]
    public string? TerrainType { get; set; }

    [JsonPropertyName("fencingType")]
    public string? FencingType { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Active";

    [JsonPropertyName("addressId")]
    public int? AddressId { get; set; }

    [JsonPropertyName("address")]
    public Address? Address { get; set; }

    [JsonPropertyName("animals")]
    public List<Animal>? Animals { get; set; }
}

/// <summary>Payload for creating a farm, with initial species inventory and lodge amenities/pricing.</summary>
public class CreateFarmRequest
{
    public int FarmFarmerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public double Size { get; set; }
    public string? Description { get; set; }
    public string? TerrainType { get; set; }
    public string? FencingType { get; set; }
    public decimal DailyRateHunter { get; set; }
    public List<string>? Amenities { get; set; }
    public CreateAddressRequest? Address { get; set; }
    public List<CreateGameSpeciesRequest>? Species { get; set; }
    /// <summary>Photos as image data URLs (max 6, see FarmImages).</summary>
    public List<string>? ImageUrls { get; set; }
}

/// <summary>One species entry in a CreateFarmRequest's initial inventory.</summary>
public class CreateGameSpeciesRequest
{
    public string SpeciesName { get; set; } = string.Empty;
    public decimal PriceMale { get; set; }
    public decimal PriceFemale { get; set; }
    public decimal PriceTrophy { get; set; }
    public int AvailableCount { get; set; }
}

/// <summary>Partial-update payload for a farm - null fields are left unchanged.</summary>
public class UpdateFarmRequest
{
    public string? Name { get; set; }
    public string? Location { get; set; }
    public double? Size { get; set; }
    public string? Description { get; set; }
    public string? TerrainType { get; set; }
    public string? FencingType { get; set; }
    public string? Status { get; set; }
    public decimal? DailyRateHunter { get; set; }
    public List<string>? Amenities { get; set; }
    /// <summary>The farm's photos in order: new ones as data URLs, kept ones as the plaasfoto/{farmId}/{index} paths the API handed out. Null leaves them unchanged.</summary>
    public List<string>? ImageUrls { get; set; }
}

/// <summary>Basic farm response shape (no game inventory/lodge details - see FarmDetailResponse for the richer admin view).</summary>
public class FarmResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public double Size { get; set; }
    public string? Description { get; set; }
    public int FarmerUserRoleId { get; set; }
    public string? Status { get; set; }
    public string? TerrainType { get; set; }
    public string? FencingType { get; set; }
}

/// <summary>Dashboard summary stats for a farmer's own farms (Farmer_db.GetStats).</summary>
public class FarmStatsResponse
{
    [JsonPropertyName("totalFarms")]
    public int TotalFarms { get; set; }

    [JsonPropertyName("totalAnimals")]
    public int TotalAnimals { get; set; }

    [JsonPropertyName("activeHuntingPermits")]
    public int ActiveHuntingPermits { get; set; }
}

/// <summary>Unused exact duplicate of FarmStatsResponse - no controller or database class references this class.</summary>
public class FarmStats
{
    [JsonPropertyName("totalFarms")]
    public int TotalFarms { get; set; }

    [JsonPropertyName("totalAnimals")]
    public int TotalAnimals { get; set; }

    [JsonPropertyName("activeHuntingPermits")]
    public int ActiveHuntingPermits { get; set; }
}

/// <summary>Payload for adding a species entry to a farm's inventory.</summary>
public class AddAnimalRequest
{
    public int FarmId { get; set; }
    public string Species { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Response shape for a farm's animal-inventory entry.</summary>
public class AnimalResponse
{
    public int Id { get; set; }
    public string Species { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>Full farm listing shape with game inventory and lodge details - backfilled with sensible defaults wherever data is missing (see Farmer_db.GetAllFarmDetails).</summary>
public class FarmDetailResponse
{
    public int Id { get; set; }
    public int FarmerUserId { get; set; }
    public int FarmerUserRoleId { get; set; }
    public string FarmerName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Province { get; set; } = "Limpopo";
    public string NearestTown { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public double Size { get; set; }
    public double SizeHectares { get; set; }
    public decimal DailyRateHunter { get; set; }
    public decimal DailyRateObserver { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public bool IsActive { get; set; } = true;
    public List<string> Amenities { get; set; } = new();
    public List<string> ImageUrls { get; set; } = new();
    public List<GameSpeciesResponse> GameInventory { get; set; } = new();
}

/// <summary>A game species entry as shown in FarmDetailResponse's inventory (male/female/trophy pricing derived from a single stored price - see Farmer_db.GetAllFarmDetails).</summary>
public class GameSpeciesResponse
{
    public int Id { get; set; }
    public int FarmId { get; set; }
    public string SpeciesName { get; set; } = string.Empty;
    public decimal PriceMale { get; set; }
    public decimal PriceFemale { get; set; }
    public decimal PriceTrophy { get; set; }
    public int AvailableCount { get; set; }
}
