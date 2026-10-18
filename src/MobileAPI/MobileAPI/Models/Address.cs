namespace MobileApi.Models;

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

