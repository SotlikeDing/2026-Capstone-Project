using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// Per-user (role) hunting permits: listing a role's own permits and issuing a new
/// one. Distinct from PermitsController's static permit-type catalogue and
/// HuntingPermitController's SAPS firearm-permit application flow. Permits are
/// userDocumentation rows owned via udUserRole.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PermitController : ControllerBase
{
    private readonly Permit_db _db;
    private readonly ResourceOwnership_db _ownership;
    public PermitController(Permit_db db, ResourceOwnership_db ownership) { _db = db; _ownership = ownership; }

    /// <summary>Lists the permits issued to a given userRoleId - only that role's owner or an Admin.</summary>
    [HttpGet("getUserPermits/{userRoleId}")]
    public async Task<IActionResult> GetUserPermits(int userRoleId)
    {
        if (!await User.OwnsUserRoleOrAdminAsync(_ownership, userRoleId)) return Forbid();
        return Ok(await _db.GetUserPermits(userRoleId));
    }

    /// <summary>Issues a new permit record on one of the caller's own roles (Admins may target any role).</summary>
    [HttpPost("createUserPermit")]
    public async Task<IActionResult> CreateUserPermit([FromBody] CreateUserPermitRequest request)
    {
        if (!await User.OwnsUserRoleOrAdminAsync(_ownership, request.UdUserRole)) return Forbid();
        try
        {
            var id = await _db.CreateUserPermit(request);
            return Ok(new { id });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Conflict(new { message = "U het reeds 'n aktiewe dokument van hierdie tipe. Vervang of verwyder eers die bestaande een." });
        }
    }

    /// <summary>Lists every user's documentation rows (id, name, status) - Admin only.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("getPermit")]
    public async Task<IActionResult> GetPermit() => Ok(await _db.GetPermit());
}
