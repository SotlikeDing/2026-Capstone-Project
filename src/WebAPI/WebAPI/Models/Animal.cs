using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>
/// A farm's animal-inventory entry. Only referenced as the type of Farm's optional
/// Animals list - the actual farm-animal read/write paths (Farmer_db.GetFarmAnimals/
/// AddAnimal) use AnimalResponse/AddAnimalRequest instead, so this class's own
/// Animals property is never actually populated anywhere in this codebase.
/// </summary>
public class Animal
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("species")]
    public string Species { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("farmId")]
    public int FarmId { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("quota")]
    public long? Quota { get; set; }
}

