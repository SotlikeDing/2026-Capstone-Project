using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Unused - no controller or database class references this class; AdResponse below is the real shape Ads_db returns.</summary>
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

    [JsonPropertyName("targetUrl")]
    public string? TargetUrl { get; set; }

    [JsonPropertyName("paymentRef")]
    public string? PaymentRef { get; set; }

    [JsonPropertyName("lastPaymentMonths")]
    public int? LastPaymentMonths { get; set; }

    [JsonPropertyName("lastPaymentAmount")]
    public decimal? LastPaymentAmount { get; set; }

    [JsonPropertyName("lastPaymentDate")]
    public DateTime? LastPaymentDate { get; set; }

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

/// <summary>Payload for creating a new ad.</summary>
public class CreateAdRequest
{
    public int? UserId { get; set; }
    public string? AdvertiserName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LongDescription { get; set; }
    public string? Type { get; set; } = "Banner";
    public string? Category { get; set; }
    public string? Status { get; set; }
    public decimal? Price { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? ContactNumber { get; set; }
    public string? TargetUrl { get; set; }
    public string? PaymentRef { get; set; }
    public int? LastPaymentMonths { get; set; }
    public decimal? LastPaymentAmount { get; set; }
    public DateTime? LastPaymentDate { get; set; }
    public string? ImagesJson { get; set; }
    public CreateAddressRequest? Address { get; set; }
}

/// <summary>Partial-update payload for an ad - null fields are left unchanged.</summary>
public class UpdateAdRequest
{
    public string? AdvertiserName { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? LongDescription { get; set; }
    public string? Type { get; set; }
    public decimal? Price { get; set; }
    public string? Status { get; set; }
    public string? Category { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? ContactNumber { get; set; }
    public string? TargetUrl { get; set; }
    public string? PaymentRef { get; set; }
    public int? LastPaymentMonths { get; set; }
    public decimal? LastPaymentAmount { get; set; }
    public DateTime? LastPaymentDate { get; set; }
    public string? ImagesJson { get; set; }
    public UpdateAddressRequest? Address { get; set; }
}

/// <summary>Partial-update payload for an ad's address.</summary>
public class UpdateAddressRequest
{
    public string? AddressNumber { get; set; }
    public string? AddressStreetName { get; set; }
    public string? AddressCity { get; set; }
    public string? AddressRegion { get; set; }
    public string? AddressPostalCode { get; set; }
    public string? AddressCountry { get; set; }
}

/// <summary>Response shape for an ad, as actually returned by Ads_db.</summary>
public class AdResponse
{
    public int Id { get; set; }
    public int ApId { get; set; }
    public int? UserId { get; set; }
    public string? AdvertiserName { get; set; }
    public string? AdvertiserEmail { get; set; }
    public string? AdvertiserPhone { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LongDescription { get; set; }
    public string? Type { get; set; }
    public string? Category { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int Views { get; set; }
    public int Clicks { get; set; }
    public string? ImagesJson { get; set; }
    public string? ContactNumber { get; set; }
    public string? TargetUrl { get; set; }
    public string? PaymentRef { get; set; }
    public int? LastPaymentMonths { get; set; }
    public decimal? LastPaymentAmount { get; set; }
    public DateTime? LastPaymentDate { get; set; }
    public Address? Address { get; set; }
}

/// <summary>Response shape for an ad payment record (adPayments table), denormalized with ad title/advertiser name for display.</summary>
public class AdPaymentRecord
{
    [JsonPropertyName("paymentId")]
    public int PaymentId { get; set; }

    [JsonPropertyName("adId")]
    public int AdId { get; set; }

    [JsonPropertyName("paymentRef")]
    public string PaymentRef { get; set; } = string.Empty;

    [JsonPropertyName("paymentDate")]
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("months")]
    public int Months { get; set; } = 1;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; } = 100m;

    [JsonPropertyName("periodStartDate")]
    public DateTime PeriodStartDate { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("periodEndDate")]
    public DateTime PeriodEndDate { get; set; } = DateTime.UtcNow.AddMonths(1);

    [JsonPropertyName("paymentMethod")]
    public string PaymentMethod { get; set; } = "Simulated Gateway";

    [JsonPropertyName("adTitle")]
    public string? AdTitle { get; set; }

    [JsonPropertyName("advertiserName")]
    public string? AdvertiserName { get; set; }
}

/// <summary>Payload for recording an ad subscription payment (Ads_db.RecordPayment).</summary>
public class RecordPaymentRequest
{
    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("paymentRef")]
    public string PaymentRef { get; set; } = string.Empty;

    [JsonPropertyName("months")]
    public int Months { get; set; } = 1;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; } = 100m;

    [JsonPropertyName("periodStartDate")]
    public DateTime? PeriodStartDate { get; set; }

    [JsonPropertyName("periodEndDate")]
    public DateTime? PeriodEndDate { get; set; }

    [JsonPropertyName("paymentMethod")]
    public string? PaymentMethod { get; set; } = "Simulated Gateway";
}

