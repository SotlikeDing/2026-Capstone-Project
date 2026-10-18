namespace MobileApi.Models;

// Mirrors WebAPI/Models/BookingCompanionModels.cs - both APIs share the bookingCompanions
// table (created by the WebAPI's db/AddBookingCompanions.sql migration) and the same rules.

public static class BookingCompanionRules
{
    /// <summary>The main hunter may add/remove hunters only on a confirmed booking (Approved by the PJ, or Paid).</summary>
    public static readonly IReadOnlySet<string> ConfirmedStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Approved", "Paid" };

    public static bool IsConfirmed(string? status) => status != null && ConfirmedStatuses.Contains(status.Trim());

    /// <summary>bookHuntGuests counts the main hunter too.</summary>
    public static int MaxCompanions(int huntGuests) => Math.Max(0, Math.Max(1, huntGuests) - 1);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

public class BookingHunterDto
{
    /// <summary>bookingCompanions.companionId; 0 for the main hunter.</summary>
    public int CompanionId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Only for the main hunter's own view.</summary>
    public string? Email { get; set; }

    /// <summary>"Main", "Linked" or "Pending".</summary>
    public string Status { get; set; } = "Linked";
    public bool IsMainHunter { get; set; }
    public bool IsYou { get; set; }
}

public class BookingHuntersResponse
{
    public int BookingId { get; set; }
    public string BookingStatus { get; set; } = string.Empty;
    public int MaxHunters { get; set; }
    public int OpenSlots { get; set; }
    public bool CanManage { get; set; }
    public bool CallerIsMainHunter { get; set; }
    public List<BookingHunterDto> Hunters { get; set; } = new();
}

public class AddBookingHunterRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public enum AddCompanionOutcome
{
    Linked,
    Invited,
    BookingNotFound,
    NotMainHunter,
    NotConfirmed,
    Full,
    IsMainHunter,
    NotAHunter,
    AlreadyAdded,
}

public sealed record AddCompanionResult(AddCompanionOutcome Outcome, BookingHunterDto? Hunter = null, string? BookingTitle = null, string? MainHunterName = null);

public enum RemoveCompanionOutcome
{
    Removed,
    NotFound,
    Forbidden,
    Locked,
}
