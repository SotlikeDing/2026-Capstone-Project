namespace WebAPI.Models;

/// <summary>
/// Booking statuses in which the main hunter may add or remove hunters: the booking
/// is confirmed (Approved by the PJ, or already Paid). Before that the trip may still
/// change or be rejected; once Completed the roster is history.
/// </summary>
public static class BookingCompanionRules
{
    public static readonly IReadOnlySet<string> ConfirmedStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Approved", "Paid" };

    public static bool IsConfirmed(string? status) => status != null && ConfirmedStatuses.Contains(status.Trim());

    /// <summary>bookHuntGuests counts the main hunter too; a missing/zero value means just the main hunter.</summary>
    public static int MaxCompanions(int huntGuests) => Math.Max(0, Math.Max(1, huntGuests) - 1);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>One hunter on a booking: the main hunter or an added companion.</summary>
public class BookingHunterDto
{
    /// <summary>bookingCompanions.companionId; 0 for the main hunter.</summary>
    public int CompanionId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Only filled in for the main hunter's own view (to see who was invited); null for everyone else.</summary>
    public string? Email { get; set; }

    /// <summary>"Main", "Linked" (has an account) or "Pending" (invited, not registered/verified yet).</summary>
    public string Status { get; set; } = "Linked";
    public bool IsMainHunter { get; set; }

    /// <summary>The caller themselves - lets the UI offer "leave this trip".</summary>
    public bool IsYou { get; set; }
}

/// <summary>The hunter roster of one booking, as the caller may see it.</summary>
public class BookingHuntersResponse
{
    public int BookingId { get; set; }
    public string BookingStatus { get; set; } = string.Empty;

    /// <summary>The booking's hunter count, main hunter included.</summary>
    public int MaxHunters { get; set; }

    /// <summary>Slots still open for added hunters.</summary>
    public int OpenSlots { get; set; }

    /// <summary>The caller is the main hunter (or an Admin) and the booking is confirmed.</summary>
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

/// <summary>
/// A booking seen by an added hunter. Deliberately has no price, payment or voucher
/// field at all: the main hunter is responsible for payment, and nothing can leak
/// through a field that does not exist.
/// </summary>
public class CompanionBookingResponse
{
    public int Id { get; set; }
    public string PackageTitle { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string MainHunterName { get; set; } = string.Empty;
    public int HuntGuests { get; set; }
    public int NonHuntGuests { get; set; }
    public string? FirearmOption { get; set; }
    public string? PhName { get; set; }
    public string? VgName { get; set; }
    public string? PjName { get; set; }
    public List<string> BookedSpecies { get; set; } = new();
}
