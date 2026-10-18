using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
[Authorize]
[Route("api/mobile-access")]
public sealed class MobileAccessController : ControllerBase
{
    private readonly IMobileAccessService _access;

    public MobileAccessController(IMobileAccessService access) => _access = access;

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        return Ok(new Models.MobileAccessResponse
        {
            UserId = caller.UserId,
            UserRoleId = caller.UserRoleId,
            Role = MobileAccessService.RoleName(caller.Role),
            RoleCode = caller.RoleCode,
            IsSupported = caller.IsSupported,
            RestrictionReason = caller.IsSupported ? null : "Hierdie rol het nie toegang tot die mobiele jagfunksies nie.",
            Capabilities = caller.Capabilities.OrderBy(x => x).ToArray()
        });
    }
}
