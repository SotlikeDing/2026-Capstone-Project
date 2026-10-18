using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Unused - no controller or database class references this class; TeamMemberResponse below is the real shape Team_db returns.</summary>
public class Team
{
    [JsonPropertyName("teamId")]
    public int TeamId { get; set; }

    [JsonPropertyName("teamPjUserRoleId")]
    public int TeamPjUserRoleId { get; set; }

    [JsonPropertyName("teamMemberUserRoleId")]
    public int TeamMemberUserRoleId { get; set; }
}

/// <summary>A team-membership record (team table row).</summary>
public class TeamMemberResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("memberUserRoleId")]
    public int MemberUserRoleId { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

/// <summary>Payload for directly adding a team member (bypassing the invite/accept flow).</summary>
public class AddTeamMemberRequest
{
    public int TeamPjId { get; set; }
    public int MemberId { get; set; }
}

/// <summary>Unused - no controller or database class references this class.</summary>
public class AddTeamMemberResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

/// <summary>A candidate profile in the PJ's team-browse view (Team_db.BrowseByRoles).</summary>
public class BrowseProfileResponse
{
    [JsonPropertyName("userRoleId")]
    public int UserRoleId { get; set; }

    /// <summary>
    /// The role's paid listing is active (roleSubscriptions: active and not expired).
    /// Public on purpose - it only says whether the profile is listed, never what was
    /// paid; the full subscription record stays owner-only.
    /// </summary>
    [JsonPropertyName("isSubscriptionActive")]
    public bool IsSubscriptionActive { get; set; }

    [JsonPropertyName("subscriptionExpiryDate")]
    public DateTime? SubscriptionExpiryDate { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("roleCode")]
    public string? RoleCode { get; set; }

    [JsonPropertyName("roleName")]
    public string? RoleName { get; set; }

    [JsonPropertyName("businessName")]
    public string? BusinessName { get; set; }

    [JsonPropertyName("bio")]
    public string? Bio { get; set; }

    [JsonPropertyName("yearsExperience")]
    public long YearsExperience { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }
}

/// <summary>A team invite record.</summary>
public class TeamInviteResponse
{
    [JsonPropertyName("inviteId")]
    public int InviteId { get; set; }

    [JsonPropertyName("pjUserRoleId")]
    public int PjUserRoleId { get; set; }

    [JsonPropertyName("pjUserId")]
    public int PjUserId { get; set; }

    [JsonPropertyName("pjName")]
    public string? PjName { get; set; }

    [JsonPropertyName("pjSurname")]
    public string? PjSurname { get; set; }

    [JsonPropertyName("pjEmail")]
    public string? PjEmail { get; set; }

    [JsonPropertyName("targetUserId")]
    public int TargetUserId { get; set; }

    [JsonPropertyName("targetName")]
    public string? TargetName { get; set; }

    [JsonPropertyName("targetRoleCode")]
    public string? TargetRoleCode { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("inviteDate")]
    public DateTime InviteDate { get; set; }

    /// <summary>Expedition / season the PJ is recruiting for, as typed on the invite.</summary>
    [JsonPropertyName("expeditionName")]
    public string? ExpeditionName { get; set; }

    /// <summary>The offer (rates, dates, conditions) the PJ attached to the invite.</summary>
    [JsonPropertyName("terms")]
    public string? Terms { get; set; }
}

/// <summary>Payload for sending a team invite.</summary>
public class SendInviteRequest
{
    public int PjUserId { get; set; }
    public int MemberUserRoleId { get; set; }
    public int TargetUserId { get; set; }
    public string? ExpeditionName { get; set; }
    public string? Terms { get; set; }
}

/// <summary>Unused - no controller or database class references this class.</summary>
public class SendInviteResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

/// <summary>Payload for removing a team member, with the PJ's chosen reason and (if needed) confirmation past an upcoming-bookings warning.</summary>
public class RemoveTeamMemberRequest
{
    [JsonPropertyName("teamId")]
    public int TeamId { get; set; }

    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; set; }

    [JsonPropertyName("reasonDescription")]
    public string? ReasonDescription { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("confirmReassignment")]
    public bool ConfirmReassignment { get; set; }

    [JsonPropertyName("pjUserId")]
    public int? PjUserId { get; set; }
}

/// <summary>One booking that conflicts with a proposed team-member removal.</summary>
public class BookingConflictDto
{
    [JsonPropertyName("bookingId")]
    public int BookingId { get; set; }

    [JsonPropertyName("packageTitle")]
    public string? PackageTitle { get; set; }

    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public DateTime EndDate { get; set; }

    [JsonPropertyName("hunterName")]
    public string? HunterName { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>Response shape for Team_db.CheckMemberRemovalConflicts.</summary>
public class RemovalConflictCheckResponse
{
    [JsonPropertyName("teamId")]
    public int TeamId { get; set; }

    [JsonPropertyName("memberUserId")]
    public int MemberUserId { get; set; }

    [JsonPropertyName("memberName")]
    public string? MemberName { get; set; }

    [JsonPropertyName("memberRole")]
    public string? MemberRole { get; set; }

    [JsonPropertyName("canRemoveDirectly")]
    public bool CanRemoveDirectly { get; set; }

    [JsonPropertyName("hasActiveBookingConflict")]
    public bool HasActiveBookingConflict { get; set; }

    [JsonPropertyName("hasUpcomingBookingConflict")]
    public bool HasUpcomingBookingConflict { get; set; }

    [JsonPropertyName("activeBookings")]
    public List<BookingConflictDto> ActiveBookings { get; set; } = new();

    [JsonPropertyName("upcomingBookings")]
    public List<BookingConflictDto> UpcomingBookings { get; set; } = new();

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>One selectable reason code for removing a team member.</summary>
public class RemovalReasonOptionDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>Response shape for Team_db.ExecuteTeamMemberRemoval - enough detail for the caller to notify the removed member.</summary>
public class TeamRemovalResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("teamId")]
    public int TeamId { get; set; }

    [JsonPropertyName("removedMemberUserId")]
    public int RemovedMemberUserId { get; set; }

    [JsonPropertyName("removedMemberName")]
    public string? RemovedMemberName { get; set; }

    [JsonPropertyName("removedMemberEmail")]
    public string? RemovedMemberEmail { get; set; }

    [JsonPropertyName("unassignedBookingCount")]
    public int UnassignedBookingCount { get; set; }

    [JsonPropertyName("unassignedBookingIds")]
    public List<int> UnassignedBookingIds { get; set; } = new();

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>One in-app bell notification (userNotifications table row).</summary>
public class UserNotificationDto
{
    [JsonPropertyName("notificationId")]
    public int NotificationId { get; set; }

    [JsonPropertyName("recipientUserId")]
    public int RecipientUserId { get; set; }

    [JsonPropertyName("senderUserId")]
    public int? SenderUserId { get; set; }

    [JsonPropertyName("notificationType")]
    public string? NotificationType { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("actionUrl")]
    public string? ActionUrl { get; set; }

    [JsonPropertyName("isRead")]
    public bool IsRead { get; set; }

    [JsonPropertyName("createdTimestamp")]
    public DateTime CreatedTimestamp { get; set; }
}
