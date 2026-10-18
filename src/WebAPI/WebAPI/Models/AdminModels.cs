namespace WebAPI.Models;

/// <summary>One admin audit-log entry (append-only - see AdminAudit_db).</summary>
public class AdminAuditLogDto
{
    public int Id { get; set; }
    public int AdminUserId { get; set; }
    public string AdminUserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public string TargetEntityId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>Payload for manually recording a free-form audit-log entry.</summary>
public class CreateAuditLogRequest
{
    // AdminUserId / AdminUserName in the body are informational only — the controller
    // always overrides them with the authenticated caller's JWT claims before writing,
    // so a client can't spoof who performed the action.
    public int AdminUserId { get; set; }
    public string AdminUserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public string TargetEntityId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A feature flag's current state.</summary>
public class FeatureFlagDto
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime LastUpdated { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>Payload for toggling a feature flag.</summary>
public class ToggleFlagRequest
{
    public string Key { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A role-verification request (RoleVerifications_db) - the older, whole-role sibling of the per-document PJ/PH/VG/Farmer credential workflow.</summary>
public class RoleVerificationRequestDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string RequestedRoleCode { get; set; } = string.Empty;
    public string RequestedRoleName { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string DocumentReference { get; set; } = string.Empty;
    public string? DocumentUrl { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public string? RejectionReason { get; set; }
}

/// <summary>Payload for an admin's role-verification decision - Reason is always required (unlike the document-verification decision, which only requires it on reject).</summary>
public class RoleVerificationDecisionRequest
{
    public bool Approve { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Payload for an admin's booking dispute correction.</summary>
public class CorrectBookingRequest
{
    public decimal CorrectedTotalPrice { get; set; }
    public decimal? CorrectedAccommodationTotal { get; set; }
    public int CorrectedHunterCount { get; set; }
    public int CorrectedObserverCount { get; set; }
    public string? Status { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Payload for changing a user's account status (Approved/Suspended/Banned/etc.).</summary>
public class UpdateUserStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A chat message flagged for moderation review.</summary>
public class FlaggedMessageDto
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int MessageId { get; set; }
    public int SenderUserId { get; set; }
    public string SenderUserName { get; set; } = string.Empty;
    public int RecipientUserId { get; set; }
    public string RecipientUserName { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public string FlaggedKeywords { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "PendingReview";
}

/// <summary>Payload for an admin's moderation decision on a flagged chat message or rating comment.</summary>
public class ModerateMessageRequest
{
    public string Action { get; set; } = string.Empty; // "Dismissed" or "ActionTaken"
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A system setting's current value.</summary>
public class SystemSettingDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>Payload for updating a system setting.</summary>
public class UpdateSettingRequest
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
