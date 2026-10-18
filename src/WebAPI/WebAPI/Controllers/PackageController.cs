using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// CRUD and browse/search for hunting packages (the bookable product: a farm +
/// species + duration combination, optionally organised by a PJ). The public
/// list/filter/detail reads are gated by Package_db against document-verification
/// status of the package's farmer and PJ - see RequiredPackageDocsApprovedPredicate.
/// </summary>
[Authorize]
[ApiController]
[Route("api/packets")]
public class PackageController : ControllerBase
{
    private readonly Package_db _db;
    private readonly ResourceOwnership_db _ownership;
    public PackageController(Package_db db, ResourceOwnership_db ownership) { _db = db; _ownership = ownership; }

    /// <summary>
    /// Creates a new package and returns its generated id. The organiser (PPjId, a userId)
    /// is always the caller - only an Admin may create a package on someone else's behalf.
    /// Any farm may be linked (PJs organise hunts on other farmers' land); that farm's
    /// farmer then co-owns the package (see ResourceOwnership_db.GetPackageOwnerUserIds).
    /// </summary>
    [HttpPost("createPacket")]
    public async Task<IActionResult> CreatePacket([FromBody] CreatePackageRequest request)
    {
        if (!User.IsAdmin() || request.PPjId is not > 0)
            request.PPjId = User.GetUserId();

        var id = await _db.CreatePacket(request);
        if (id <= 0)
            return StatusCode(403, new { message = "Slegs 'n geregistreerde uitruster (PJ) kan 'n pakket skep." });
        return Ok(new { Id = id });
    }

    /// <summary>Updates an existing package's details - only its PJ, its farm's farmer, or an Admin.</summary>
    [HttpPost("updatePacket/{id}")]
    public async Task<IActionResult> UpdatePacket(int id, [FromBody] UpdatePackageRequest request)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetPackageOwnerUserIds(id))) return Forbid();
        var ok = await _db.UpdatePacket(id, request);
        return ok ? Ok() : NotFound();
    }

    /// <summary>Legacy unpaginated listing (delegates to GetPacketsPaginated internally) - prefer the paginated endpoint for new callers.</summary>
    [AllowAnonymous]
    [HttpGet("getPackets")]
    public async Task<IActionResult> GetPackets() => Ok(await _db.GetPackets());

    /// <summary>The caller's own packages, including those the marketplace still hides (see IsPubliclyVisible).</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        if (!int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId) || userId <= 0)
            return Unauthorized();
        return Ok(await _db.GetPackagesForOrganiser(userId));
    }

    /// <summary>Public, paginated package browse/search - excludes packages tied to an unverified farmer or PJ.</summary>
    [AllowAnonymous]
    [HttpGet("getPacketsPaginated")]
    public async Task<IActionResult> GetPacketsPaginated([FromQuery] int page = 1, [FromQuery] int pageSize = 10) => Ok(await _db.GetPacketsPaginated(page, pageSize));

    /// <summary>Single-package detail lookup, same verification gating as the list endpoints - returns 404 if hidden.</summary>
    [AllowAnonymous]
    [HttpGet("getPacket/{id}")]
    public async Task<IActionResult> GetPacket(int id)
    {
        var p = await _db.GetPacket(id);
        return p == null ? NotFound() : Ok(p);
    }

    /// <summary>Package search with structured filters (species, price range, dates, etc.).</summary>
    [AllowAnonymous]
    [HttpPost("getPacketFiltered")]
    public async Task<IActionResult> GetPacketFiltered([FromBody] PackageFilterRequest filter)
    {
        return Ok(await _db.GetPacketFiltered(filter));
    }

    /// <summary>Deletes a package - only its PJ, its farm's farmer, or an Admin.</summary>
    [HttpDelete("deletePacket/{id}")]
    public async Task<IActionResult> DeletePacket(int id)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetPackageOwnerUserIds(id))) return Forbid();
        // A package with bookings is deactivated, not deleted (booking history) - say which.
        bool deactivated = await _db.PackageHasBookings(id);
        return (await _db.DeletePacket(id)) ? Ok(new { deleted = !deactivated, deactivated }) : NotFound();
    }
}
