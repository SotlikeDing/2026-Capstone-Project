using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// CRUD for firearm/weapon records, used by SAPS permit applications and PH profiles.
/// Weapons are userDocumentation rows (udDtId = 5) owned via udUserRole; each user sees
/// and manages only their own, while Admins see all.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class WeaponController : ControllerBase
{
    private readonly Weapon_db _db;
    private readonly ResourceOwnership_db _ownership;
    public WeaponController(Weapon_db db, ResourceOwnership_db ownership) { _db = db; _ownership = ownership; }

    /// <summary>Registers a new weapon record on one of the caller's own roles and returns its generated id.</summary>
    [HttpPost("addWeapon")]
    public async Task<IActionResult> AddWeapon([FromBody] CreateWeaponRequest request)
    {
        if (!await User.OwnsUserRoleOrAdminAsync(_ownership, request.UdUserRole)) return Forbid();
        try
        {
            var id = await _db.AddWeapon(request);
            return Ok(new { id });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Conflict(new { message = "U het reeds 'n aktiewe dokument van hierdie tipe. Vervang of verwyder eers die bestaande een." });
        }
    }

    /// <summary>Lists every user's weapon records - Admin only.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("getWeapons")]
    public async Task<IActionResult> GetWeapons() => Ok(await _db.GetWeapons());

    /// <summary>Fetches a single weapon record by id - only its owner or an Admin.</summary>
    [HttpGet("getWeapon/{id}")]
    public async Task<IActionResult> GetWeapon(int id)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetUserDocumentOwnerUserIds(id))) return Forbid();
        var weapon = await _db.GetWeapon(id);
        return weapon == null ? NotFound() : Ok(weapon);
    }

    /// <summary>Deletes a weapon record - only its owner or an Admin.</summary>
    [HttpDelete("deleteWeapon/{id}")]
    public async Task<IActionResult> DeleteWeapon(int id)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetUserDocumentOwnerUserIds(id))) return Forbid();
        return (await _db.DeleteWeapon(id)) ? Ok() : NotFound();
    }
}
