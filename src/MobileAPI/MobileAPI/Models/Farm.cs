using System.Text.Json.Serialization;

namespace MobileApi.Models;

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

public class CreateFarmRequest
{
    public int FarmFarmerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public double Size { get; set; }
    public string? Description { get; set; }
    public string? TerrainType { get; set; }
    public string? FencingType { get; set; }
    public CreateAddressRequest? Address { get; set; }
}

public class UpdateFarmRequest
{
    public string? Name { get; set; }
    public string? Location { get; set; }
    public double? Size { get; set; }
    public string? Description { get; set; }
    public string? TerrainType { get; set; }
    public string? FencingType { get; set; }
    public string? Status { get; set; }
}

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

public class FarmStatsResponse
{
    [JsonPropertyName("totalFarms")]
    public int TotalFarms { get; set; }

    [JsonPropertyName("totalAnimals")]
    public int TotalAnimals { get; set; }

    [JsonPropertyName("activeHuntingPermits")]
    public int ActiveHuntingPermits { get; set; }
}

public class FarmStats
{
    [JsonPropertyName("totalFarms")]
    public int TotalFarms { get; set; }

    [JsonPropertyName("totalAnimals")]
    public int TotalAnimals { get; set; }

    [JsonPropertyName("activeHuntingPermits")]
    public int ActiveHuntingPermits { get; set; }
}

public class AddAnimalRequest
{
    public int FarmId { get; set; }
    public string Species { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class AnimalResponse
{
    public int Id { get; set; }
    public string Species { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal? Price { get; set; }
}



