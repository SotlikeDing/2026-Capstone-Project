namespace WebAPI.Models;

/// <summary>Which document types a role needs, and whether each is mandatory - drives both the upload form and the minimum-requirements guard on delete.</summary>
public class DocumentRequirementDto
{
    public int DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
}

/// <summary>Full document record, including the file itself - owner/admin only.</summary>
public class UserDocumentDto
{
    public int Id { get; set; }
    public int UserRoleId { get; set; }
    public int DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileDataUrl { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public int? UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedByName { get; set; }
    public string? RejectionReason { get; set; }
}

/// <summary>
/// What a viewer who is NOT the owner/admin is allowed to see: whether a required
/// credential exists and when it was uploaded - never the file itself, never the
/// rejection reason.
/// </summary>
public class UserDocumentSummaryDto
{
    public int DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsUploaded { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string? Status { get; set; }
}

/// <summary>Payload for uploading a new document.</summary>
public class UploadUserDocumentRequest
{
    public int DocumentTypeId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileDataUrl { get; set; } = string.Empty;
}

/// <summary>Payload for replacing an existing document's file.</summary>
public class ReplaceUserDocumentRequest
{
    public string FileName { get; set; } = string.Empty;
    public string FileDataUrl { get; set; } = string.Empty;
}

/// <summary>One entry in a document's immutable audit trail.</summary>
public class DocumentAuditEntryDto
{
    public int LogId { get; set; }
    public string ActionType { get; set; } = string.Empty; // 'Created', 'Replaced', 'Deleted', 'Approved', 'Rejected'
    public string DocumentTypeName { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public int ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>One row in the admin's document-verification queue.</summary>
public class PendingDocumentVerificationDto
{
    public int Id { get; set; }
    public int UserRoleId { get; set; }
    public int OwnerUserId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public int DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileDataUrl { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public bool IsRequired { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime? VerifiedAt { get; set; }
    public string? RejectionReason { get; set; }
}

/// <summary>Payload for an admin's approve/reject decision - Reason is required when Approve is false.</summary>
public class DocumentVerificationDecisionRequest
{
    public bool Approve { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Everything the controller needs to notify the owner after a decision.</summary>
public class DocumentVerificationResultDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string DocumentTypeName { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? RejectionReason { get; set; }
}
