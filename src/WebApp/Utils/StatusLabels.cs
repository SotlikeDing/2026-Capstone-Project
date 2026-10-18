namespace WebApp.Utils;

/// <summary>
/// Single source of truth for translating backend status codes to Afrikaans display
/// text. Several pages used to hand-roll their own if/else chain for this and drifted
/// out of sync with each other (e.g. some had no case for "Paid"/"Completed"), silently
/// leaking the raw English status code to the user. Route every status render through
/// here instead of adding another one-off chain.
/// </summary>
public static class StatusLabels
{
    /// <summary>Booking lifecycle status, including the two-stage PH/VG staff-acceptance state ("PendingStaffAcceptance") ahead of admin approval.</summary>
    public static string Booking(string? status) => status switch
    {
        "Pending" => "Afwagtend",
        "PendingStaffAcceptance" => "Wag op PH/VG se Bevestiging",
        "Approved" => "Goedgekeur",
        "Paid" => "Betaal (Bevestig)",
        "Completed" => "Voltooi",
        "Rejected" => "Van Hand Gewys",
        "Cancelled" => "Gekanselleer",
        _ => status ?? ""
    };

    /// <summary>Role-verification / generic approval-queue status (Pending/Approved/Rejected).</summary>
    public static string Verification(string? status) => status switch
    {
        "Pending" => "Afwagtend",
        "Approved" => "Goedgekeur",
        "Rejected" => "Afgekeur",
        _ => status ?? ""
    };

    /// <summary>Per-document admin-verification status shown on the owner's document manager and public profiles.</summary>
    public static string DocumentVerification(string? status) => status switch
    {
        "Pending" => "Wag vir Verifikasie",
        "Approved" => "Geverifieer",
        "Rejected" => "Afgekeur",
        _ => status ?? ""
    };

    /// <summary>Chat-flag / rating-comment / rating-dispute moderation queue status.</summary>
    public static string Moderation(string? status) => status switch
    {
        "PendingReview" => "Wag vir Hersiening",
        "Dismissed" => "Afgehandel",
        "Upheld" => "Gehandhaaf",
        "Denied" => "Van Hand Gewys",
        _ => status ?? ""
    };

    /// <summary>Admin audit-log action codes (PascalCase C# identifiers) shown as visible text.</summary>
    public static string AuditAction(string? action) => action switch
    {
        "ApproveRoleVerification" => "Rolverifikasie Goedgekeur",
        "RejectRoleVerification" => "Rolverifikasie Afgekeur",
        "SuspendUser" => "Gebruiker Geskors",
        "BanUser" => "Gebruiker Verban",
        "CorrectBookingDispute" => "Besprekingsdispuut Reggestel",
        "ToggleFeatureFlag" => "Kenmerkvlag Gewissel",
        _ => action ?? ""
    };
}
