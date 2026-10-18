using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class Advertisement
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("advertisementId")]
    public int AdvertisementId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("shortDescription")]
    public string? ShortDescription { get; set; }

    [JsonPropertyName("longDescription")]
    public string? LongDescription { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Active";

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("createdTimestamp")]
    public DateTime CreatedTimestamp { get; set; }

    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; set; }

    [JsonPropertyName("expirationTimestamp")]
    public DateTime? ExpirationTimestamp { get; set; }

    [JsonPropertyName("apId")]
    public int ApId { get; set; }

    [JsonPropertyName("creater")]
    public string Creator { get; set; } = string.Empty;

    [JsonPropertyName("contactNumber")]
    public string? ContactNumber { get; set; }

    [JsonPropertyName("addressId")]
    public int? AddressId { get; set; }

    [JsonPropertyName("address")]
    public Address? Address { get; set; }
    public int? Views { get; set; }

    [JsonIgnore]
    public DateTime? ExpiryDate
    {
        get => ExpirationTimestamp;
        set => ExpirationTimestamp = value;
    }
}

public class CreateAdRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public decimal? Price { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? ContactNumber { get; set; }
    public CreateAddressRequest? Address { get; set; }
}

public class UpdateAdRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? Status { get; set; }
    public string? Category { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? ContactNumber { get; set; }
    public UpdateAddressRequest? Address { get; set; }
}

public class UpdateAddressRequest
{
    public string? AddressNumber { get; set; }
    public string? AddressStreetName { get; set; }
    public string? AddressCity { get; set; }
    public string? AddressRegion { get; set; }
    public string? AddressPostalCode { get; set; }
    public string? AddressCountry { get; set; }
}

public class AdResponse
{
    public int Id { get; set; }
    public int ApId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int Views { get; set; }
    public string? ContactNumber { get; set; }
    public Address? Address { get; set; }
}

