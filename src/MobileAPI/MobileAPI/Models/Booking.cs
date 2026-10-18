using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class Booking
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("pakketId")]
    public int PakketId { get; set; }

    [JsonPropertyName("pakketName")]
    public string PakketName { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("bookingDate")]
    public DateTime BookingDate { get; set; }

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public DateTime EndDate { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Pending";

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("paymentStatus")]
    public string PaymentStatus { get; set; } = "Pending";

    [JsonPropertyName("huntGuests")]
    public int HuntGuests { get; set; } = 1;

    [JsonPropertyName("nonHuntGuests")]
    public int NonHuntGuests { get; set; } = 0;

    [JsonPropertyName("packageId")]
    public int? PackageId { get; set; }

    [JsonPropertyName("package")]
    public Package? Package { get; set; }
}

public class CreateBookingRequest
{
    public int PakketId { get; set; }
    public int HunterId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int HuntGuests { get; set; } = 1;
    public int NonHuntGuests { get; set; } = 0;
    public string? FirearmOption { get; set; }
    public string? SpecialNotes { get; set; }
    public bool WantsVideographer { get; set; }
    public List<string>? BookedSpecies { get; set; }
}

public class UpdateBookingRequest
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }
    public int? HuntGuests { get; set; }
    public int? NonHuntGuests { get; set; }
    public string? PaymentStatus { get; set; }
}

public class BookingResponse
{
    public int Id { get; set; }
    public int PakketId { get; set; }
    public string PakketName { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime BookingDate { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public int HuntGuests { get; set; }
    public int NonHuntGuests { get; set; }
}

public class BookingRequestModel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("bookingId")]
    public int BookingId { get; set; }

    [JsonPropertyName("requestedByUserId")]
    public int RequestedByUserId { get; set; }

    [JsonPropertyName("requestedByName")]
    public string RequestedByName { get; set; } = string.Empty;

    [JsonPropertyName("requestStatus")]
    public string RequestStatus { get; set; } = "Pending";

    [JsonPropertyName("requestDate")]
    public DateTime RequestDate { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;
}

public class BookingRequestResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class BookingsDataResponse
{
    public int Id { get; set; }
    public int PakketId { get; set; }
    public string PakketName { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime BookingDate { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class BookingsData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("pakketId")]
    public int PakketId { get; set; }

    [JsonPropertyName("pakketName")]
    public string PakketName { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("bookingDate")]
    public DateTime BookingDate { get; set; }

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public DateTime EndDate { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("paymentStatus")]
    public string PaymentStatus { get; set; } = string.Empty;
}

public class BookingsRequest
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("bookingId")]
    public int BookingId { get; set; }

    [JsonPropertyName("requestedByUserId")]
    public int RequestedByUserId { get; set; }

    [JsonPropertyName("requestedByName")]
    public string RequestedByName { get; set; } = string.Empty;

    [JsonPropertyName("requestStatus")]
    public string RequestStatus { get; set; } = string.Empty;

    [JsonPropertyName("requestDate")]
    public DateTime RequestDate { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;
}

public class BookingsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

