using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class TaxidermyResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class SlaughterhouseResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class DangerousAnimalResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? Quota { get; set; }
}

public class Province
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

