namespace WebAPI.Models;

/// <summary>A physical address row, shared across farms/businesses/ads via Address_db's dedup lookup.</summary>
public class Address
{
    public int AddressId { get; set; }
    public string? AddressNumber { get; set; }
    public string? AddressStreetName { get; set; }
    public string? AddressCity { get; set; }
    public string? AddressRegion { get; set; }
    public string? AddressPostalCode { get; set; }
    public string? AddressCountry { get; set; }
    public decimal? AddressLatitude { get; set; }
    public decimal? AddressLongitude { get; set; }
}

/// <summary>Payload for creating a new address (same shape as Address, minus the generated id).</summary>
public class CreateAddressRequest
{
    public string? AddressNumber { get; set; }
    public string? AddressStreetName { get; set; }
    public string? AddressCity { get; set; }
    public string? AddressRegion { get; set; }
    public string? AddressPostalCode { get; set; }
    public string? AddressCountry { get; set; }
    public decimal? AddressLatitude { get; set; }
    public decimal? AddressLongitude { get; set; }
}
