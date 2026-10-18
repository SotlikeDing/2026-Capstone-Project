using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;

namespace WebAPI.Controllers;

/// <summary>
/// Public "recruit"/discovery endpoints for browsing PJ, PH, VG and Farmer public
/// profiles. Unauthenticated - anyone can search for and view these roles, which is
/// the whole point (hunters/organisers finding people to work with). People_db
/// itself is responsible for excluding a role whose required verification documents
/// aren't all Approved yet.
/// </summary>
[Authorize]
[ApiController]
[Route("api/recruit")]
public class PeopleController : ControllerBase
{
    private readonly People_db _db;
    public PeopleController(People_db db) { _db = db; }

    /// <summary>Lists public profiles for a role (PJ/PH/VG/Boer or Farmer), verified roles only.</summary>
    [HttpGet("getPeople/{roleCode}")]
    public async Task<IActionResult> GetPeople(string roleCode) => Ok(await _db.GetPeople(roleCode));

    /// <summary>
    /// Fetches a single public profile by userRoleId. Unlike GetPeople, this direct
    /// lookup is NOT gated on verification status - it's used for already-known
    /// context (e.g. a counterparty on an existing booking), not discovery.
    /// </summary>
    [HttpGet("getPerson/{id}")]
    public async Task<IActionResult> GetPerson(int id)
    {
        var profile = await _db.GetPerson(id);
        return profile == null ? NotFound() : Ok(profile);
    }
}
