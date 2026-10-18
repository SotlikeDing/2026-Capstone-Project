using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// PJ team management: adding/removing PH/VG/Farmer members to a PJ's team,
/// browsing candidates and sending/responding to team invites, member removal
/// (with booking-conflict checks and chat/email notification to the removed
/// member), and the shared in-app notification bell (userNotifications table -
/// also written to by Bookings_db and AdminController, but read/marked-read here).
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TeamController : ControllerBase
{
    private readonly Team_db _db;
    private readonly Chats_db? _chatsDb;
    private readonly Services.IEmailService? _emailService;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<WebAPI.Hubs.ChatHub>? _hubContext;

    private readonly ResourceOwnership_db? _ownership;

    public TeamController(
        Team_db db,
        Chats_db? chatsDb = null,
        Services.IEmailService? emailService = null,
        Microsoft.AspNetCore.SignalR.IHubContext<WebAPI.Hubs.ChatHub>? hubContext = null,
        ResourceOwnership_db? ownership = null)
    {
        _db = db;
        _ownership = ownership;
        _chatsDb = chatsDb;
        _emailService = emailService;
        _hubContext = hubContext;
    }

    /// <summary>The calling user's own id, from their validated JWT only - never trusts a client-supplied header/claim override.</summary>
    private bool TryGetCurrentUserId(out int currentUserId)
    {
        currentUserId = 0;
        if (HttpContext == null) return false;

        // The caller's identity comes ONLY from the authenticated JWT - an
        // "X-User-Id" header used to be trusted outright here with no verification,
        // letting anyone impersonate any user simply by setting that header.
        if (User?.Identity?.IsAuthenticated != true) return false;

        var userIdClaim = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("userId")?.Value
            ?? User.FindFirst("id")?.Value;

        return int.TryParse(userIdClaim, out currentUserId) && currentUserId > 0;
    }

    /// <summary>Whether the caller holds the Admin role (checked across a few different claim shapes for compatibility).</summary>
    /// <summary>Runs an ownership lookup, or returns "no owners" when the service isn't available (fails closed).</summary>
    private Task<HashSet<int>> Owners(Func<ResourceOwnership_db, Task<HashSet<int>>> lookup)
        => _ownership == null ? Task.FromResult(new HashSet<int>()) : lookup(_ownership);

    /// <summary>The team row's PJ or its member (or an Admin).</summary>
    private async Task<bool> CallerIsOnTeamRowAsync(int teamId)
        => await User.IsOwnerOrAdminAsync(() => Owners(o => o.GetTeamOwnerUserIds(teamId)))
           || await User.IsOwnerOrAdminAsync(() => Owners(o => o.GetTeamMemberUserIds(teamId)));

    /// <summary>The caller owns userRoleId (or is an Admin).</summary>
    private async Task<bool> CallerOwnsUserRoleAsync(int userRoleId)
        => User.IsAdmin() || (_ownership != null && await User.OwnsUserRoleOrAdminAsync(_ownership, userRoleId));

    private bool IsAdmin()
    {
        if (HttpContext == null || User?.Identity?.IsAuthenticated != true) return false;

        return User.IsInRole("Admin")
            || User.IsInRole("Administrator")
            || User.IsInRole("Administrateur")
            || string.Equals(User.FindFirst("role")?.Value, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, "Admin", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>Lists all members of a PJ's team.</summary>
    [HttpGet("getTeamMembers/{teamPjId}")]
    public async Task<IActionResult> GetTeamMembers(int teamPjId)
    {
        if (!User.IsSelfOrAdmin(teamPjId)) return Forbid();
        return Ok(await _db.GetTeamMembers(teamPjId));
    }

    /// <summary>Fetches a single team-membership record by id.</summary>
    [HttpGet("getTeamMember/{id}")]
    public async Task<IActionResult> GetTeamMember(int id)
    {
        if (!await CallerIsOnTeamRowAsync(id)) return Forbid();
        var member = await _db.GetTeamMember(id);
        return member == null ? NotFound() : Ok(member);
    }

    /// <summary>Lists every PJ team a given member (PH/VG/Farmer userRoleId) belongs to.</summary>
    [HttpGet("getTeamsForMember/{memberUserRoleId}")]
    public async Task<IActionResult> GetTeamsForMember(int memberUserRoleId)
    {
        if (!await CallerOwnsUserRoleAsync(memberUserRoleId)) return Forbid();
        return Ok(await _db.GetTeamsForMember(memberUserRoleId));
    }

    /// <summary>Directly adds a member to a PJ's team (bypassing the invite/accept flow - used for admin/seed scenarios).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("addTeamMember")]
    public async Task<IActionResult> AddTeamMember([FromBody] AddTeamMemberRequest request)
    {
        var id = await _db.AddTeamMember(request);
        return Ok(new { id });
    }

    /// <summary>Removes a team-membership record directly, with none of RemoveMember's conflict checks or notifications.</summary>
    [HttpDelete("deleteTeamMember/{id}")]
    public async Task<IActionResult> DeleteTeamMember(int id)
    {
        if (!await CallerIsOnTeamRowAsync(id)) return Forbid();
        return (await _db.DeleteTeamMember(id)) ? Ok() : NotFound();
    }

    // ── Browse / Invite endpoints ────────────────────────────────────────────

    /// <summary>Browses candidate PH/VG/Farmer profiles a PJ could invite to their team, filtered by role codes (comma-separated query param).</summary>
    [HttpGet("browse")]
    public async Task<IActionResult> Browse([FromQuery] string roles = "PH,Boer,VG")
    {
        var roleCodes = roles.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(r => r.Trim())
                             .ToList();
        var result = await _db.BrowseByRoles(roleCodes);
        return Ok(result);
    }

    /// <summary>
    /// Sends a team invite. Resolves the target's userRoleId by trying PH, then VG,
    /// then Boer/FARMER, then falling back to any role they hold, when the caller
    /// didn't already supply a specific MemberUserRoleId.
    /// </summary>
    [HttpPost("sendInvite")]
    public async Task<IActionResult> SendInvite([FromBody] SendInviteRequest request)
    {
        try
        {
            if (request.ExpeditionName?.Trim().Length > 200)
                return BadRequest(new { error = "ExpeditionName may not exceed 200 characters." });
            if (request.Terms?.Trim().Length > 1000)
                return BadRequest(new { error = "Terms may not exceed 1000 characters." });

            if (request.PjUserId <= 0)
                return BadRequest(new { error = "PjUserId is missing or invalid." });

            // Only the PJ themselves (or an Admin) may send invites for their team.
            if (!User.IsSelfOrAdmin(request.PjUserId)) return Forbid();

            if (request.TargetUserId == request.PjUserId)
                return BadRequest(new { error = "U kan nie uself as 'n spanlid uitnooi nie." });

            var pjUserRoleId = await _db.GetUserRoleId(request.PjUserId, "PJ");
            if (pjUserRoleId == 0)
                return BadRequest(new { error = "User is not registered as an active PJ." });

            int memberRoleId = request.MemberUserRoleId;
            if (memberRoleId <= 0 && request.TargetUserId > 0)
            {
                memberRoleId = await _db.GetUserRoleId(request.TargetUserId, "PH");
                if (memberRoleId <= 0) memberRoleId = await _db.GetUserRoleId(request.TargetUserId, "VG");
                if (memberRoleId <= 0) memberRoleId = await _db.GetUserRoleId(request.TargetUserId, "Boer");
                if (memberRoleId <= 0) memberRoleId = await _db.GetUserRoleId(request.TargetUserId, "FARMER");
                if (memberRoleId <= 0) memberRoleId = await _db.GetAnyUserRoleId(request.TargetUserId);
            }

            if (memberRoleId <= 0)
                return BadRequest(new { error = "Target user does not have a suitable role (PH, VG, Boer)." });

            var id = await _db.SendInvite(
                pjUserRoleId,
                memberRoleId,
                request.ExpeditionName,
                request.Terms);
            return Ok(new { inviteId = id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
        }
    }

    /// <summary>Lists pending invites addressed to a given member userRoleId.</summary>
    [HttpGet("getInvites/{memberUserRoleId}")]
    public async Task<IActionResult> GetInvites(int memberUserRoleId)
    {
        if (!await CallerOwnsUserRoleAsync(memberUserRoleId)) return Forbid();
        var invites = await _db.GetInvitesForMember(memberUserRoleId);
        return Ok(invites);
    }

    /// <summary>Lists invites addressed to a user across all of their roles (not just one userRoleId).</summary>
    [HttpGet("getInvitesForUser/{userId}")]
    public async Task<IActionResult> GetInvitesForUser(int userId)
    {
        if (!User.IsSelfOrAdmin(userId)) return Forbid();
        var invites = await _db.GetInvitesForUser(userId);
        return Ok(invites);
    }

    /// <summary>Lists invites a PJ has sent out, for their own "sent invites" tracking view.</summary>
    [HttpGet("getSentInvites/{pjUserId}")]
    public async Task<IActionResult> GetSentInvites(int pjUserId)
    {
        if (!User.IsSelfOrAdmin(pjUserId)) return Forbid();
        var invites = await _db.GetSentInvitesForPj(pjUserId);
        return Ok(invites);
    }

    /// <summary>Cancels a still-pending invite (PJ withdrawing it before the target responds).</summary>
    [HttpDelete("cancelInvite/{inviteId}")]
    public async Task<IActionResult> CancelInvite(int inviteId)
    {
        if (!await User.IsOwnerOrAdminAsync(() => Owners(o => o.GetInviteSenderUserIds(inviteId)))) return Forbid();
        var ok = await _db.CancelInvite(inviteId);
        return ok ? Ok() : NotFound();
    }

    /// <summary>Accepts or declines a team invite.</summary>
    [HttpPost("respondInvite/{inviteId}")]
    public async Task<IActionResult> RespondInvite(int inviteId, [FromQuery] bool accept)
    {
        if (!await User.IsOwnerOrAdminAsync(() => Owners(o => o.GetInviteRecipientUserIds(inviteId)))) return Forbid();
        var success = await _db.RespondToInvite(inviteId, accept);
        return success ? Ok(new { accepted = accept }) : NotFound();
    }

    // ── Member Removal & Notification endpoints ──────────────────────────────

    /// <summary>Checks whether removing a member would conflict with active/upcoming bookings, before the PJ commits to removing them.</summary>
    [HttpGet("checkRemovalConflicts/{teamId}")]
    public async Task<IActionResult> CheckRemovalConflicts(int teamId)
    {
        if (!await User.IsOwnerOrAdminAsync(() => Owners(o => o.GetTeamOwnerUserIds(teamId)))) return Forbid();
        var result = await _db.CheckMemberRemovalConflicts(teamId);
        return Ok(result);
    }

    /// <summary>Lists the standard reason codes a PJ can pick from when removing a team member.</summary>
    [AllowAnonymous]
    [HttpGet("removalReasons")]
    public IActionResult GetRemovalReasons()
    {
        return Ok(_db.GetRemovalReasons());
    }

    /// <summary>
    /// Removes a team member. On success, best-effort notifies the removed member via
    /// a system chat message (SignalR broadcast) and an email - both failures are
    /// logged and swallowed rather than failing the removal itself, since the removal
    /// has already succeeded by the time either dispatch runs.
    /// </summary>
    [Authorize]
    [HttpPost("removeMember")]
    public async Task<IActionResult> RemoveMember([FromBody] RemoveTeamMemberRequest request)
    {
        if (request.TeamId <= 0)
            return BadRequest(new { error = "Ongeldige span ID / Invalid TeamId." });

        // request.PjUserId used to be trusted as a fallback identity whenever
        // TryGetCurrentUserId failed - since ExecuteTeamMemberRemoval treats this value
        // as the real team-ownership check, that let anyone who knew (or guessed) a
        // PJ's userId remove members from that PJ's team with no authentication at all.
        if (!TryGetCurrentUserId(out int currentUserId))
            return Unauthorized();

        try
        {
            var result = await _db.ExecuteTeamMemberRemoval(request.TeamId, request, IsAdmin() ? 0 : currentUserId);

            // Dispatches system chat message via _chatsDb.GetOrCreateDirectChat and _hubContext.Clients.Group.SendAsync("ReceiveMessage", ...)
            if (_chatsDb != null && result.RemovedMemberUserId > 0)
            {
                try
                {
                    int senderId = currentUserId > 0 ? currentUserId : 1;
                    string reasonText = !string.IsNullOrWhiteSpace(request.ReasonDescription) 
                        ? request.ReasonDescription 
                        : (!string.IsNullOrWhiteSpace(request.ReasonCode) ? request.ReasonCode : "Onderlinge ooreenkoms");
                    string chatMsg = $"[Stelsel Kennisgewing] U spanlidmaatskap is beëindig. Rede: {reasonText}";

                    int chatId = await _chatsDb.GetOrCreateDirectChat(senderId, result.RemovedMemberUserId, chatMsg);
                    if (_hubContext != null && chatId > 0)
                    {
                        var msg = new Message
                        {
                            ChatId = chatId,
                            SenderId = senderId,
                            SenderName = "Stelsel",
                            Content = chatMsg,
                            SentAt = DateTime.UtcNow
                        };
                        await _hubContext.Clients.Group(chatId.ToString()).SendAsync("ReceiveMessage", msg);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TeamRemoval Chat Dispatch Error]: {ex.Message}");
                }
            }

            // Dispatches HTML email to member via _emailService.SendEmailAsync
            if (_emailService != null && !string.IsNullOrWhiteSpace(result.RemovedMemberEmail))
            {
                try
                {
                    string reasonText = !string.IsNullOrWhiteSpace(request.ReasonDescription) 
                        ? request.ReasonDescription 
                        : (!string.IsNullOrWhiteSpace(request.ReasonCode) ? request.ReasonCode : "Nie gespesifiseer nie");
                    string subject = "MikPunt - Kennisgewing van Spanlidmaatskap Beëindiging";
                    string body = $@"
                        <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                            <h2 style='color: #8B0000;'>MikPunt Span Kennisgewing</h2>
                            <p>Goeiedag {result.RemovedMemberName},</p>
                            <p>U lidmaatskap van die uitrusterspan is beëindig.</p>
                            <p><strong>Rede:</strong> {reasonText}</p>
                            {(string.IsNullOrWhiteSpace(request.Notes) ? "" : $"<p><strong>Notas:</strong> {request.Notes}</p>")}
                            {(result.UnassignedBookingCount > 0 ? $"<p>Daar is <strong>{result.UnassignedBookingCount}</strong> toekomstige bespreking(s) waarvan u ontkoppel is.</p>" : "")}
                            <hr style='border: 0; border-top: 1px solid #ccc;' />
                            <p style='font-size: 12px; color: #777;'>Hierdie is 'n outomatiese stelsel-kennisgewing vanaf die MikPunt platform.</p>
                        </div>";
                    await _emailService.SendEmailAsync(result.RemovedMemberEmail, subject, body, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TeamRemoval Email Dispatch Error]: {ex.Message}");
                }
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message == "ACTIVE_BOOKING_CONFLICT")
        {
            return BadRequest(new { error = "ACTIVE_BOOKING_CONFLICT", message = "Lid kan nie verwyder word nie weens 'n aktiewe jag-konflik." });
        }
        catch (InvalidOperationException ex) when (ex.Message == "UPCOMING_BOOKINGS_REQUIRE_CONFIRMATION")
        {
            return StatusCode(409, new { error = "UPCOMING_BOOKINGS_REQUIRE_CONFIRMATION", message = "Lid het komende besprekings wat hertoewysing vereis. Bevestiging benodig." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // These three endpoints previously had no [Authorize] and used
    // `TryGetCurrentUserId(...) && currentUserId != userId && !IsAdmin()` as their only
    // guard - which fails OPEN, not closed, for an unauthenticated caller:
    // TryGetCurrentUserId returns false with no JWT at all, so the && short-circuits and
    // the whole condition is false, skipping the 403 and serving any user's private
    // notifications (and letting anyone mark any notification as read) to a fully
    // anonymous caller. [Authorize] plus an explicit "no identity -> Unauthorized" check
    // closes that: only an authenticated caller can even reach the ownership check now.
    /// <summary>Lists in-app bell notifications for a user (paginated), owner or admin only.</summary>
    [Authorize]
    [HttpGet("notifications/{userId}")]
    public async Task<IActionResult> GetUserNotifications(int userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized();
        if (currentUserId != userId && !IsAdmin())
        {
            return StatusCode(403, new { error = "Toegang geweier / Forbidden: U kan nie ander gebruikers se kennisgewings besigtig nie." });
        }

        var list = await _db.GetUserNotifications(userId, page, pageSize);
        return Ok(list);
    }

    /// <summary>Unread bell-notification count for a user, owner or admin only - powers the header badge.</summary>
    [Authorize]
    [HttpGet("notifications/unreadCount/{userId}")]
    public async Task<IActionResult> GetUnreadNotificationCount(int userId)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized();
        if (currentUserId != userId && !IsAdmin())
        {
            return StatusCode(403, new { error = "Toegang geweier / Forbidden: U kan nie ander gebruikers se kennisgewings besigtig nie." });
        }

        var count = await _db.GetUnreadNotificationCount(userId);
        return Ok(new { unreadCount = count });
    }

    /// <summary>Marks a single notification read - only its owner or an admin may do so.</summary>
    [Authorize]
    [HttpPost("notifications/markAsRead/{id}")]
    public async Task<IActionResult> MarkNotificationAsRead(int id)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized();

        // Ownership is enforced inside the UPDATE's WHERE clause (recipientUserId =
        // caller, or caller is Admin) - this previously had no ownership check
        // whatsoever, so any caller could mark any other user's notification as read.
        var ok = await _db.MarkNotificationAsRead(id, currentUserId, IsAdmin());
        return ok ? Ok(new { success = true }) : NotFound();
    }
}

