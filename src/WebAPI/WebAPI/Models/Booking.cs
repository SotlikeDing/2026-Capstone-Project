using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Unused - no controller or database class references this class; BookingResponse below is the real shape Bookings_db returns.</summary>
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

/// <summary>Payload for creating a new booking.</summary>
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

/// <summary>Partial-update payload for a booking (admin correction / status change) - null fields are left unchanged.</summary>
public class UpdateBookingRequest
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }
    public int? HuntGuests { get; set; }
    public int? NonHuntGuests { get; set; }
    public string? PaymentStatus { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? AccommodationPrice { get; set; }
    public string? PriceBreakdownJson { get; set; }
}

/// <summary>Payload for a PJ assigning staff and confirming pricing on a booking.</summary>
public class ApproveWithStaffRequest
{
    public int PhUserId { get; set; }
    public int? VgUserId { get; set; }
    public decimal? ConfirmedTotalPrice { get; set; }
    public decimal? ConfirmedAccommodationPrice { get; set; }
    public string? PriceBreakdownJson { get; set; }
}

/// <summary>Payload for rejecting a booking request.</summary>
public class RejectBookingRequest
{
    public string? ReasonCode { get; set; }
    public string? ReasonDescription { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Payload for a PH/VG accepting or declining their staff assignment.</summary>
public class RespondStaffAssignmentRequest
{
    public string Role { get; set; } = string.Empty; // "PH" or "VG"
    public bool Accept { get; set; }
    public string? DeclineReason { get; set; }
}

/// <summary>Full booking details, as actually returned by Bookings_db.</summary>
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
    public string HunterEmail { get; set; } = string.Empty;
    public string HunterPhone { get; set; } = string.Empty;
    public int? AssignedPhUserId { get; set; }
    public string? AssignedPhName { get; set; }
    public int? AssignedVgUserId { get; set; }
    public string? AssignedVgName { get; set; }
    public string FarmName { get; set; } = string.Empty;
    public string Province { get; set; } = "Limpopo";
    public string? PaymentReference { get; set; }
    public string? RejectionReason { get; set; }
    public string? RejectionNotes { get; set; }
    public string? FirearmOption { get; set; }
    public string? SpecialNotes { get; set; }
    public int? FarmId { get; set; }
    public decimal? ConfirmedAccommodationPrice { get; set; }
    public string? PriceBreakdownJson { get; set; }
    public bool WantsVideographer { get; set; }
    public List<string> BookedSpecies { get; set; } = new();
    public int? PjUserId { get; set; }
    public string? PjName { get; set; }
    public string? PhAssignmentStatus { get; set; }
    public string? VgAssignmentStatus { get; set; }
    public string? PhDeclineReason { get; set; }
    public string? VgDeclineReason { get; set; }
}

/// <summary>One species line item within a confirmed booking's price breakdown.</summary>
public class ConfirmedSpeciesPriceItem
{
    public string SpeciesName { get; set; } = string.Empty;
    public decimal PricePerAnimal { get; set; }
    public int AvailableCount { get; set; }
    public int SelectedCount { get; set; } = 1;
}

/// <summary>Structured price breakdown (serialized into bookings.bookPriceBreakdown) confirmed by the PJ when assigning staff.</summary>
public class BookingPriceBreakdown
{
    public int Days { get; set; }
    public int HunterCount { get; set; }
    public int ObserverCount { get; set; }
    public decimal DailyRateHunter { get; set; }
    public decimal DailyRateObserver { get; set; }
    public decimal AccommodationTotal { get; set; }
    public List<ConfirmedSpeciesPriceItem> ConfirmedSpecies { get; set; } = new();
    public decimal GameTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Currency { get; set; } = "USD";
    public decimal DepositPercentage { get; set; } = 50m;
    public decimal DepositAmount { get; set; }
    public decimal OutstandingBalance { get; set; }
    public string? ScheduleOverrideReason { get; set; }
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One entry in a booking's change-history log (bookingAuditLogs table).</summary>
public class BookingAuditLog
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public int PerformedByUserId { get; set; }
    public string PerformedByName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A pending booking request, for the global booking-requests queue (Bookings_db.GetBookingRequests).</summary>
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

/// <summary>Unused - no controller or database class references this class.</summary>
public class BookingRequestResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

/// <summary>Unused - no controller or database class references this class; BookingResponse is the real shape returned everywhere.</summary>
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

/// <summary>Unused - see BookingsDataResponse's remarks.</summary>
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

/// <summary>Unused - see BookingRequestModel for the shape actually returned.</summary>
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
