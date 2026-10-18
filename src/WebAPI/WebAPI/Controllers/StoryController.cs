using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// CRUD for storyData records - despite the name, this just links a booking to an
/// external "expedition number" (sdExpeditionNum), not narrative story content;
/// StoryResponse/CreateStoryRequest carry no title/text fields. Access is limited to
/// the booking's participants (hunter, PH, VG, PJ, farmer) and Admins.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class StoryController : ControllerBase
{
    private readonly Story_db _db;
    private readonly ResourceOwnership_db _ownership;
    public StoryController(Story_db db, ResourceOwnership_db ownership) { _db = db; _ownership = ownership; }

    /// <summary>Creates a new story for a booking the caller takes part in and returns its generated id.</summary>
    [HttpPost("createStory")]
    public async Task<IActionResult> CreateStory([FromBody] CreateStoryRequest request)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetBookingParticipantUserIds(request.SdBookingId))) return Forbid();
        var id = await _db.CreateStory(request);
        return Ok(new { id });
    }

    /// <summary>Lists all stories - Admin only.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("getStories")]
    public async Task<IActionResult> GetStories() => Ok(await _db.GetStories());

    /// <summary>Fetches a single story by id - only the booking's participants or an Admin.</summary>
    [HttpGet("getStory/{id}")]
    public async Task<IActionResult> GetStory(int id)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetStoryOwnerUserIds(id))) return Forbid();
        var story = await _db.GetStory(id);
        return story == null ? NotFound() : Ok(story);
    }

    /// <summary>Updates an existing story - only the booking's participants or an Admin.</summary>
    [HttpPost("editStory/{id}")]
    public async Task<IActionResult> EditStory(int id, [FromBody] EditStoryRequest request)
    {
        if (!await User.IsOwnerOrAdminAsync(() => _ownership.GetStoryOwnerUserIds(id))) return Forbid();
        var ok = await _db.EditStory(id, request);
        return ok ? Ok() : NotFound();
    }
}
