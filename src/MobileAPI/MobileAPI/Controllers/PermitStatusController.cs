using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/permits")]
[Authorize]
public class PermitStatusController : ControllerBase
{
    private readonly PermitStatus_db _db;

    public PermitStatusController(PermitStatus_db db)
    {
        _db = db;
    }

    [HttpGet("user/{userRoleId}")]
    public async Task<IActionResult> GetUserPermitStatuses(int userRoleId)
    {
        if (!UserHasUserRole(userRoleId))
        {
            return Forbid();
        }

        var permits = await _db.GetUserPermitStatuses(userRoleId);
        return Ok(permits);
    }

    private bool UserHasUserRole(int userRoleId)
    {
        var claimValue = User.FindFirstValue("userRoleId");
        return int.TryParse(claimValue, out var currentUserRoleId) && currentUserRoleId == userRoleId;
    }
}
