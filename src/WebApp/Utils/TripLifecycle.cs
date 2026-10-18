using WebApp.Models;

namespace WebApp.Utils;

/// <summary>
/// When a trip counts as "over" for the post-trip features (team ratings, VG media for
/// the hunt story). Mirrors the servers: WebAPI's BookingParticipantsDto.IsRateable and
/// MobileAPI's booking eligibility - the server stays the authority, this only decides
/// whether a page shows the panel at all.
/// </summary>
public static class TripLifecycle
{
    /// <summary>Completed, or a confirmed (Approved/Paid) trip whose last day has passed.</summary>
    public static bool IsFinished(BookingModel booking) =>
        string.Equals(booking.Status, "Completed", StringComparison.OrdinalIgnoreCase)
        || ((string.Equals(booking.Status, "Approved", StringComparison.OrdinalIgnoreCase)
             || string.Equals(booking.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            && booking.EndDate.Date < DateTime.UtcNow.Date);
}
