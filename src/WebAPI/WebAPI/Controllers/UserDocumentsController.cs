using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// Owner-facing document management for the PJ/PH/VG/Farmer credential-verification
/// workflow: uploading, replacing and deleting a role's supporting documents, plus
/// reading them back (full, for the owner/admin; or a file-free summary, publicly).
/// Every write is scoped to the caller's own userRoleId (or an admin acting on their
/// behalf) - see CallerId/CallerIsAdmin. Admin's read/decide side of the workflow
/// (the verification queue itself) lives in AdminController.
/// </summary>
[Authorize]
[ApiController]
[Route("api/documents")]
public class UserDocumentsController : ControllerBase
{
    private readonly UserDocuments_db _db;

    private static readonly Regex AllowedDataUrl = new(@"^data:(application/pdf|image/png|image/jpeg);base64,", RegexOptions.Compiled);
    private const int MaxDataUrlLength = 7_000_000; // ~5MB raw file, base64-inflated

    public UserDocumentsController(UserDocuments_db db)
    {
        _db = db;
    }

    /// <summary>The calling user's own id, from their validated JWT - 0 if unauthenticated/unparseable.</summary>
    private int CallerId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    /// <summary>Whether the caller holds the Admin role - admins may act on any user's documents.</summary>
    private bool CallerIsAdmin => User.IsInRole("Admin");

    /// <summary>Which document types a role needs and which are mandatory - public, used to render upload/registration forms.</summary>
    [AllowAnonymous]
    [HttpGet("requirements/{roleCode}")]
    [EnableRateLimiting("DocumentReadPolicy")]
    public async Task<IActionResult> GetRequirements(string roleCode)
    {
        return Ok(await _db.GetRequiredDocumentTypes(roleCode));
    }

    /// <summary>Full documents (file included) for a role instance - owner or admin only.</summary>
    [HttpGet("{userRoleId}")]
    [Authorize]
    public async Task<IActionResult> GetDocuments(int userRoleId)
    {
        int ownerUserId = await _db.GetUserRoleOwnerUserId(userRoleId);
        if (ownerUserId == 0) return NotFound(new { message = "Rol nie gevind nie." });
        if (!CallerIsAdmin && CallerId != ownerUserId)
            return StatusCode(403, new { message = "U het nie toegang tot hierdie dokumente nie." });

        return Ok(await _db.GetDocumentsForUserRole(userRoleId));
    }

    /// <summary>
    /// Public-safe summary for cross-role profile viewing: document type + uploaded/missing
    /// status only, never the file. Anyone can call this (it's shown on public profiles),
    /// which is exactly why it never includes udFilePath.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{userRoleId}/summary")]
    [EnableRateLimiting("DocumentReadPolicy")]
    public async Task<IActionResult> GetDocumentSummary(int userRoleId, [FromQuery] string roleCode)
    {
        if (string.IsNullOrWhiteSpace(roleCode))
            return BadRequest(new { message = "roleCode is verpligtend." });

        string? actualRoleCode = await _db.GetRoleCodeForUserRole(userRoleId);
        if (actualRoleCode == null) return NotFound(new { message = "Rol nie gevind nie." });
        if (!string.Equals(actualRoleCode, roleCode, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "roleCode stem nie ooreen met hierdie rol nie." });

        return Ok(await _db.GetDocumentSummaryForUserRole(userRoleId, roleCode));
    }

    /// <summary>Uploads (or, if one already exists for this document type, overwrites) a document for a role. Resets its status to Pending review.</summary>
    [HttpPost("{userRoleId}/upload")]
    [Authorize]
    [EnableRateLimiting("DocumentPolicy")]
    public async Task<IActionResult> Upload(int userRoleId, [FromBody] UploadUserDocumentRequest request)
    {
        var validation = ValidateFile(request.FileName, request.FileDataUrl);
        if (validation != null) return BadRequest(new { message = validation });
        if (request.DocumentTypeId <= 0)
            return BadRequest(new { message = "Ongeldige dokumenttipe." });

        int ownerUserId = await _db.GetUserRoleOwnerUserId(userRoleId);
        if (ownerUserId == 0) return NotFound(new { message = "Rol nie gevind nie." });
        if (!CallerIsAdmin && CallerId != ownerUserId)
            return StatusCode(403, new { message = "U kan slegs dokumente vir uself oplaai." });

        if (!await _db.IsValidDocumentTypeForRole(userRoleId, request.DocumentTypeId))
            return BadRequest(new { message = "Ongeldige dokumenttipe vir hierdie rol." });

        int id = await _db.UploadDocument(userRoleId, request.DocumentTypeId, request.FileName, request.FileDataUrl, CallerId);
        return Ok(new { success = true, id });
    }

    /// <summary>Replaces an existing document's file in place, resetting its verification status back to Pending.</summary>
    [HttpPut("{documentId}/replace")]
    [Authorize]
    [EnableRateLimiting("DocumentPolicy")]
    public async Task<IActionResult> Replace(int documentId, [FromBody] ReplaceUserDocumentRequest request)
    {
        var validation = ValidateFile(request.FileName, request.FileDataUrl);
        if (validation != null) return BadRequest(new { message = validation });

        var (_, ownerUserId) = await _db.GetDocumentOwner(documentId);
        if (ownerUserId == 0) return NotFound(new { message = "Dokument nie gevind nie." });
        if (!CallerIsAdmin && CallerId != ownerUserId)
            return StatusCode(403, new { message = "U kan slegs u eie dokumente vervang." });

        bool ok = await _db.ReplaceDocument(documentId, request.FileName, request.FileDataUrl, CallerId);
        return ok ? Ok(new { success = true }) : NotFound(new { message = "Dokument nie gevind nie." });
    }

    /// <summary>Deletes an optional document. A required document type can never be bare-deleted, only replaced (see the IsRequired check below).</summary>
    [HttpDelete("{documentId}")]
    [Authorize]
    [EnableRateLimiting("DocumentPolicy")]
    public async Task<IActionResult> Delete(int documentId)
    {
        var (_, ownerUserId) = await _db.GetDocumentOwner(documentId);
        if (ownerUserId == 0) return NotFound(new { message = "Dokument nie gevind nie." });
        if (!CallerIsAdmin && CallerId != ownerUserId)
            return StatusCode(403, new { message = "U kan slegs u eie dokumente skrap." });

        // The profile must always have its minimum required documents - a required
        // document type can be replaced (uploading a new one overwrites in place) but
        // never bare-deleted, since that would drop the profile below the minimum.
        var context = await _db.GetDeletionContext(documentId);
        if (context == null) return NotFound(new { message = "Dokument nie gevind nie." });
        if (context.IsRequired)
        {
            return BadRequest(new
            {
                message = $"'{context.DocumentTypeName}' is 'n verpligte dokument vir hierdie rol en kan nie geskrap word nie - laai eerder 'n vervangende dokument op."
            });
        }

        bool ok = await _db.DeleteDocument(documentId, CallerId);
        return ok ? Ok(new { success = true }) : NotFound(new { message = "Dokument nie gevind nie." });
    }

    /// <summary>Full history of upload/replace/delete/verification-decision events for a role's documents - owner or admin only.</summary>
    [HttpGet("{userRoleId}/audit")]
    [Authorize]
    public async Task<IActionResult> GetAuditTrail(int userRoleId)
    {
        int ownerUserId = await _db.GetUserRoleOwnerUserId(userRoleId);
        if (ownerUserId == 0) return NotFound(new { message = "Rol nie gevind nie." });
        if (!CallerIsAdmin && CallerId != ownerUserId)
            return StatusCode(403, new { message = "U het nie toegang tot hierdie oudit-geskiedenis nie." });

        return Ok(await _db.GetAuditTrail(userRoleId));
    }

    /// <summary>Checks filename presence, MIME type (PDF/PNG/JPEG only, via AllowedDataUrl) and size cap. Returns an Afrikaans error message, or null if valid.</summary>
    private static string? ValidateFile(string fileName, string fileDataUrl)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "Lêernaam is verpligtend.";
        if (string.IsNullOrWhiteSpace(fileDataUrl) || !AllowedDataUrl.IsMatch(fileDataUrl))
            return "Slegs PDF-, PNG- of JPEG-lêers word aanvaar.";
        if (fileDataUrl.Length > MaxDataUrlLength)
            return "Lêer is te groot (maksimum 5MB).";
        return null;
    }
}
