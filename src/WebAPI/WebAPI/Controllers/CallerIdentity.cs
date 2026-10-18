using System.Security.Claims;

namespace WebAPI.Controllers;

/// <summary>
/// Reads the calling user's identity from the JWT minted by UserController.GenerateJwtToken
/// (NameIdentifier = userId, Role = each role code). Used for "caller must be this user or
/// an Admin" ownership checks on endpoints that take a userId in the route or body.
/// </summary>
public static class CallerIdentity
{
    /// <summary>The caller's userId from the token, or null when unauthenticated / unparseable (null-safe: controllers without an HttpContext have no User).</summary>
    public static int? GetUserId(this ClaimsPrincipal? user)
    {
        var raw = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value;
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    /// <summary>True when the caller holds the Admin role (Program.cs normalises any casing to "Admin").</summary>
    public static bool IsAdmin(this ClaimsPrincipal? user) => user?.IsInRole("Admin") == true;

    /// <summary>
    /// True for a role code that grants administrator rights (ADMIN, Administrateur,
    /// Administrator - any casing). Only an existing Admin may assign such a role;
    /// self-service role endpoints must reject it for everyone else.
    /// </summary>
    public static bool IsAdminRoleCode(string? roleCode)
        => !string.IsNullOrWhiteSpace(roleCode) && roleCode.Trim().StartsWith("ADMIN", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the caller is targetUserId themselves or an Admin.</summary>
    public static bool IsSelfOrAdmin(this ClaimsPrincipal? user, int targetUserId)
        => user.IsAdmin() || user.GetUserId() == targetUserId;

    /// <summary>
    /// True when the caller is an Admin or one of the resource's owners. The owner lookup
    /// (a ResourceOwnership_db call) only runs for non-Admins. A missing row has no owners,
    /// so it is denied rather than revealing whether it exists.
    /// </summary>
    public static async Task<bool> IsOwnerOrAdminAsync(this ClaimsPrincipal? user, Func<Task<HashSet<int>>> getOwnerUserIds)
    {
        if (user.IsAdmin()) return true;
        var callerId = user.GetUserId();
        return callerId.HasValue && (await getOwnerUserIds()).Contains(callerId.Value);
    }

    /// <summary>True when the caller is an Admin or owns the given userRoles row.</summary>
    public static async Task<bool> OwnsUserRoleOrAdminAsync(this ClaimsPrincipal? user, Database.ResourceOwnership_db ownership, int userRoleId)
    {
        if (user.IsAdmin()) return true;
        var callerId = user.GetUserId();
        return callerId.HasValue && await ownership.GetUserIdForUserRole(userRoleId) == callerId.Value;
    }
}
