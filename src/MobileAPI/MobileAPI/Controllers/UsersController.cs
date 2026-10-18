using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;

namespace MobileApi.Controllers;

// Signed-in users only: anonymous callers could otherwise probe which e-mail
// addresses have accounts and harvest names.
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly Users_db _usersDb;

    public UsersController(Users_db usersDb)
    {
        _usersDb = usersDb;
    }

    [HttpGet("by-email")]
    public async Task<IActionResult> GetByEmail([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { message = "Email is required." });
        }

        var user = await _usersDb.GetByEmailAsync(email.Trim());

        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        return Ok(user);
    }
}

