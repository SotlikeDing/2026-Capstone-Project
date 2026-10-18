using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// Farm and game-inventory management for the Farmer (Boer) role: creating/editing
/// farms, listing a farmer's own farms or all farms (admin/browse views), and
/// managing each farm's animal/species inventory.
/// </summary>
[Authorize]
[ApiController]
[Route("api/farmer")]   // Base route for all farmer endpoints
public class FarmerController : ControllerBase
{
    private readonly Farmer_db _db;
    private readonly ResourceOwnership_db _ownership;
    public FarmerController(Farmer_db db, ResourceOwnership_db ownership) { _db = db; _ownership = ownership; }

    // -------------------- Farmer endpoints --------------------

    /// <summary>Dashboard summary stats for the calling farmer's own farms.</summary>
    [HttpGet("getStats")]
    public async Task<IActionResult> GetStats()
    {
        var farmerUserRoleId = await GetCallerFarmerUserRoleId();
        if (farmerUserRoleId == 0) return Forbid();
        return Ok(await _db.GetStats(farmerUserRoleId));
    }

    /// <summary>
    /// Creates a new farm owned by the caller. request.FarmFarmerId (a userId) is only
    /// honoured for an Admin creating a farm on a farmer's behalf.
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateFarm([FromBody] CreateFarmRequest request)
    {
        int ownerUserId = User.IsAdmin() && request.FarmFarmerId > 0
            ? request.FarmFarmerId
            : User.GetUserId() ?? 0;
        if (ownerUserId <= 0)
            return Unauthorized("No valid user.");
        var invalid = ValidateListing(request.Name, request.Description, request.Size);
        if (invalid != null) return BadRequest(new { message = invalid });

        // Only a farmer may own a farm - creating one must never hand out the Farmer role
        // (that goes through registration and admin verification).
        try
        {
            var farmId = await _db.CreateFarm(ownerUserId, request);
            return Ok(new { FarmId = farmId });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Paginated, searchable/filterable listing of all farms (public browse).</summary>
    [AllowAnonymous]
    [HttpGet("getAllFarms")]
    public async Task<IActionResult> GetAllFarms(
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var farms = await _db.GetAllFarms(searchTerm, status, page, pageSize);
        return Ok(farms);
    }

    /// <summary>Full unfiltered farm listing with details, for admin views.</summary>
    [AllowAnonymous]
    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        var farms = await _db.GetAllFarmDetails();
        return Ok(farms);
    }

    /// <summary>Lists farms owned by a farmer - defaults to the calling user's own farms; another farmer's list is Admin-only.</summary>
    [HttpGet("getFarmerFarms/{farmerUserRoleId?}")]
    public async Task<IActionResult> GetFarmerFarms(int? farmerUserRoleId = null)
    {
        if (farmerUserRoleId.HasValue)
        {
            if (!await User.OwnsUserRoleOrAdminAsync(_ownership, farmerUserRoleId.Value)) return Forbid();
            return Ok(await _db.GetFarmerFarms(farmerUserRoleId.Value));
        }
        var id = await GetCallerFarmerUserRoleId();
        if (id == 0) return Forbid();
        return Ok(await _db.GetFarmerFarms(id));
    }

    /// <summary>Updates a farm's details - only its farmer or an Admin.</summary>
    [HttpPost("update/{farmId}")]
    public async Task<IActionResult> UpdateFarm(int farmId, [FromBody] UpdateFarmRequest request)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetFarmOwnerUserIds(farmId))) return Forbid();
        var invalid = ValidateListing(request.Name, request.Description, request.Size);
        if (invalid != null) return BadRequest(new { message = invalid });
        try
        {
            var result = await _db.UpdateFarm(farmId, request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Farms are listed publicly, so a name must read as a name (3-100 characters with at
    /// least one letter - "12" or "2312231" used to appear in the directory) and a
    /// description, when given, must say something (20+ characters). Null = valid; on an
    /// update, fields that aren't being changed (null) are skipped.
    /// </summary>
    internal static string? ValidateListing(string? name, string? description, double? size)
    {
        if (name != null)
        {
            var trimmed = name.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 100 || !trimmed.Any(char.IsLetter))
                return "Die plaasnaam moet 3 tot 100 karakters wees en letters bevat.";
        }
        if (!string.IsNullOrWhiteSpace(description) && description.Trim().Length < 20)
            return "Beskryf die plaas in ten minste 20 karakters.";
        if (size.HasValue && size.Value <= 0)
            return "Die plaasgrootte moet groter as 0 wees.";
        return null;
    }

    /// <summary>Deletes a farm - only its farmer or an Admin. Registered on both DELETE and POST so it works from clients that can't send a DELETE verb.</summary>
    [HttpDelete("delete/{farmId}")]
    [HttpPost("delete/{farmId}")]
    public async Task<IActionResult> DeleteFarm(int farmId)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetFarmOwnerUserIds(farmId))) return Forbid();
        var result = await _db.DeleteFarm(farmId);
        return Ok(new { Success = result });
    }

    /// <summary>Adds a species entry to a farm's game inventory - only the farm's farmer or an Admin.</summary>
    [HttpPost("addAnimal")]
    public async Task<IActionResult> AddAnimal([FromBody] AddAnimalRequest request)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetFarmOwnerUserIds(request.FarmId))) return Forbid();
        var id = await _db.AddAnimal(request);
        return Ok(new { Id = id });
    }

    /// <summary>Updates an existing animal-inventory entry - only the farm's farmer or an Admin.</summary>
    [HttpPost("editAnimal/{id}")]
    public async Task<IActionResult> EditAnimal(int id, [FromBody] EditAnimalsRequest request)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetAnimalOwnerUserIds(id))) return Forbid();
        request.AaId = id;
        var result = await _db.EditAnimals(request);
        return Ok(result); // returns true/false
    }

    /// <summary>Lists a farm's game/animal inventory.</summary>
    [AllowAnonymous]
    [HttpGet("getAnimals/{farmId}")]
    public async Task<IActionResult> GetFarmAnimals(int farmId)
        => Ok(await _db.GetFarmAnimals(farmId));

    /// <summary>
    /// The calling user's farmer userRoleId, looked up from their userId (the JWT carries
    /// no role-row ids), or 0 when they don't hold the farmer role.
    /// </summary>
    private async Task<int> GetCallerFarmerUserRoleId()
    {
        var userId = User.GetUserId();
        return userId.HasValue ? await _ownership.GetFarmerUserRoleIdForUser(userId.Value) ?? 0 : 0;
    }

    /// <summary>Fetches a single farm's details by id.</summary>
    [AllowAnonymous]
    [HttpGet("getFarm/{farmId}")]
    public async Task<IActionResult> GetFarm(int farmId)
    {
        var farm = await _db.GetFarm(farmId);
        return farm == null ? NotFound() : Ok(farm);
    }

    /// <summary>One farm photo (public, like the farm listing). Lists only carry these paths, never the bytes.</summary>
    [AllowAnonymous]
    [HttpGet("{farmId:int}/images/{index:int}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetFarmImage(int farmId, int index)
    {
        var image = await _db.GetFarmImage(farmId, index);
        return image is { } found ? File(found.Bytes, found.ContentType) : NotFound();
    }
}
