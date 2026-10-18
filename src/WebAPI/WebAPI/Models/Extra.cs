using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Response shape for Extra_db.GetTaxidermy.</summary>
public class TaxidermyResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

/// <summary>Response shape for Extra_db.GetSlaughterhouses.</summary>
public class SlaughterhouseResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

/// <summary>Response shape for Extra_db.GetDangerousAnimals.</summary>
public class DangerousAnimalResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? Quota { get; set; }
}

/// <summary>A South African province, for address/location dropdowns.</summary>
public class Province
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
