using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WebAPI.Database;
using WebAPI.Hubs;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// Real-time chat: creating chats (group or direct), listing a user's chats,
/// sending/reading/deleting messages, read-receipt tracking, and unread-message
/// notification status. Every mutation is broadcast to the chat's SignalR group
/// (see BroadcastToChat) so open clients update live without polling.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatsController : ControllerBase
{
    private readonly Chats_db _db;
    private readonly IHubContext<ChatHub>? _hubContext;

    public ChatsController(Chats_db db, IHubContext<ChatHub>? hubContext = null)
    {
        _db = db;
        _hubContext = hubContext;
    }


    /// <summary>The calling user's own id, from their validated JWT - [Authorize] on the controller already rejects a request with no identity before this runs.</summary>
    private bool TryGetCurrentUserId(out int currentUserId)
    {
        currentUserId = 0;
        if (HttpContext == null) return false;

        // The caller's identity comes ONLY from the authenticated JWT - an
        // "X-User-Id" header used to be trusted outright here with no verification,
        // letting anyone impersonate any user simply by setting that header. [Authorize]
        // on the controller already rejects unauthenticated requests before this runs.
        if (User?.Identity?.IsAuthenticated != true) return false;

        var userIdClaim = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("userId")?.Value
            ?? User.FindFirst("id")?.Value;

        return int.TryParse(userIdClaim, out currentUserId) && currentUserId > 0;
    }

    /// <summary>Whether the caller holds the Admin role.</summary>
    private bool IsAdmin()
    {
        if (HttpContext == null || User?.Identity?.IsAuthenticated != true) return false;

        return User.IsInRole("Admin")
            || User.IsInRole("Administrator")
            || User.IsInRole("Administrateur")
            || string.Equals(User.FindFirst("role")?.Value, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pushes a SignalR event to every client connected to a chat's group. Best-effort - a broadcast failure never fails the HTTP request that triggered it.</summary>
    private async Task BroadcastToChat(int chatId, string method, object payload)
    {
        if (_hubContext == null) return;

        try
        {
            await _hubContext.Clients.Group(chatId.ToString()).SendAsync(method, payload);
        }
        catch
        {
            // Background broadcast failure is non-fatal
        }
    }

    /// <summary>Creates a new chat (group or, via one participant, effectively direct) with an initial message. Adds the caller as a participant automatically if not already listed.</summary>
    [HttpPost("createChat")]
    [HttpPost]
    public async Task<IActionResult> CreateChat([FromBody] CreateChatRequest request)
    {
        if (request == null)
            return BadRequest(new { message = "Request body is required." });

        if (request.ChatParticipants == null || request.ChatParticipants.Count == 0)
            return BadRequest(new { message = "At least one chat participant is required." });

        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "Initial message is required." });

        if (TryGetCurrentUserId(out var currentUserId))
        {
            if (request.SenderId <= 0)
            {
                request.SenderId = currentUserId;
            }
            else if (request.SenderId != currentUserId && !IsAdmin())
            {
                return StatusCode(403, new { message = "SenderId stem nie ooreen met die huidige gebruiker nie." });
            }

            if (!request.ChatParticipants.Any(p => p.UserId == currentUserId))
            {
                request.ChatParticipants.Add(new ChatParticipant { UserId = currentUserId });
            }
        }

        if (request.SenderId <= 0)
        {
            request.SenderId = request.ChatParticipants
                .Where(p => p.UserId > 0)
                .Select(p => p.UserId)
                .FirstOrDefault();
        }

        if (request.SenderId <= 0)
            return BadRequest(new { message = "SenderId is required, or the first participant must have a valid UserId." });

        try
        {
            if (request.ParticipantIds != null && request.ParticipantIds.Count > 0)
            {
                foreach (var pid in request.ParticipantIds.Where(id => id > 0))
                {
                    if (!request.ChatParticipants.Any(p => p.UserId == pid))
                    {
                        request.ChatParticipants.Add(new ChatParticipant { UserId = pid });
                    }
                }
            }

            var chatId = await _db.CreateChat(request);

            if (!string.IsNullOrWhiteSpace(request.Message) && request.SenderId > 0)
            {
                var senderName = await _db.GetSenderName(request.SenderId);
                var initialMsg = new Message
                {
                    ChatId = chatId,
                    SenderId = request.SenderId,
                    SenderName = senderName,
                    Content = request.Message,
                    SentAt = DateTime.UtcNow
                };

                await BroadcastToChat(chatId, "ReceiveMessage", initialMsg);
            }

            return Ok(new
            {
                success = true,
                message = "Chat created successfully",
                chatId = chatId
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CHAT] Error creating chat: {ex}");
            return StatusCode(500, new { message = "Die kletsboodskap kon nie verwerk word nie. Probeer asseblief weer." });
        }
    }

    /// <summary>Finds (or creates, if none exists yet) the 1:1 direct chat between the caller and targetUserId, optionally sending an initial message.</summary>
    [HttpPost("direct/{targetUserId:int}")]
    public async Task<IActionResult> GetOrCreateDirectChat(int targetUserId, [FromBody] DirectChatRequest? request = null)
    {
        if (targetUserId <= 0)
            return BadRequest(new { message = "Invalid targetUserId." });

        int senderId = 0;
        if (TryGetCurrentUserId(out var currentUserId))
        {
            senderId = currentUserId;
        }
        else if (request != null && request.SenderId > 0)
        {
            senderId = request.SenderId;
        }

        if (senderId <= 0)
            return BadRequest(new { message = "SenderId is required or authentication required." });

        if (senderId == targetUserId)
            return BadRequest(new { message = "Cannot create direct chat with yourself." });

        try
        {
            string? initialMessage = request?.InitialMessage ?? request?.Message;
            int chatId;
            try
            {
                chatId = await _db.GetOrCreateDirectChat(senderId, targetUserId, initialMessage);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 547)
            {
                return NotFound(new { message = "Die gebruiker met wie u wil klets, bestaan nie." });
            }

            if (!string.IsNullOrWhiteSpace(initialMessage))
            {
                var senderName = await _db.GetSenderName(senderId);
                var msg = new Message
                {
                    ChatId = chatId,
                    SenderId = senderId,
                    SenderName = senderName,
                    Content = initialMessage,
                    SentAt = DateTime.UtcNow
                };

                await BroadcastToChat(chatId, "ReceiveMessage", msg);
            }

            return Ok(new
            {
                success = true,
                chatId = chatId,
                message = "Direct chat ready"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CHAT] Error getting or creating direct chat: {ex}");
            return StatusCode(500, new { message = "Die kletsboodskap kon nie verwerk word nie. Probeer asseblief weer." });
        }
    }

    /// <summary>Lists a user's chats - defaults to the caller's own when no userId is given; targeting another user requires ownership or Admin.</summary>
    [HttpGet]
    [HttpGet("{userId:int}")]
    [HttpGet("user/{userId:int}/chats")]
    public async Task<IActionResult> GetUserChats([FromRoute] int? userId = null)
    {
        if (userId.HasValue && userId.Value <= 0)
            return BadRequest(new { message = "Invalid userId." });

        int targetUserId;
        if (userId.HasValue && userId.Value > 0)
        {
            targetUserId = userId.Value;
            if (!TryGetCurrentUserId(out var currentUserId))
            {
                return Unauthorized(new { message = "Gebruiker nie geverifieer nie." });
            }
            if (currentUserId != targetUserId && !IsAdmin())
            {
                return StatusCode(403, new { message = "Geen toegang tot ander gebruikers se kletse nie." });
            }
        }
        else
        {
            if (!TryGetCurrentUserId(out targetUserId))
            {
                return Unauthorized(new { message = "Gebruiker nie geverifieer nie of userId ontbreek." });
            }
        }

        var chats = await _db.GetChats(targetUserId);
        return Ok(chats);
    }

    /// <summary>Paginated message history for a chat. The caller must be a participant, unless they're an Admin.</summary>
    [HttpGet("{chatId:int}/messages")]
    [HttpGet("{chatId:int}/chatMessages")]
    public async Task<IActionResult> GetChatMessages(int chatId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (chatId <= 0)
            return BadRequest(new { message = "Ongeldige chatId." });

        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized(new { message = "Gebruiker nie geverifieer nie." });

        if (!IsAdmin())
        {
            bool isParticipant = await _db.IsParticipant(chatId, currentUserId);
            if (!isParticipant)
                return StatusCode(403, new { message = "U is nie 'n deelnemer aan hierdie klets nie." });
        }

        var messages = await _db.GetChatMessagesPaginated(chatId, page, pageSize);
        return Ok(messages);
    }

    /// <summary>Sends a message in a chat and broadcasts it to connected clients. The sender must already be a chat participant.</summary>
    [HttpPost("{chatId:int}/messages")]
    [HttpPost("createMessage/{chatId:int}")]
    public async Task<IActionResult> CreateMessage(int chatId, [FromBody] SendMessageRequest request)
    {
        if (chatId <= 0)
            return BadRequest(new { message = "Invalid chatId." });

        if (request == null)
            return BadRequest(new { message = "Request body is required." });

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "Content is required." });

        if (TryGetCurrentUserId(out var currentUserId))
        {
            if (request.SenderId <= 0)
            {
                request.SenderId = currentUserId;
            }
            else if (request.SenderId != currentUserId && !IsAdmin())
            {
                return StatusCode(403, new { message = "SenderId stem nie ooreen met die huidige gebruiker nie." });
            }
        }

        if (request.SenderId <= 0)
            return BadRequest(new { message = "SenderId is required." });

        bool isParticipant = await _db.IsParticipant(chatId, request.SenderId);
        if (!isParticipant)
            return StatusCode(403, new { message = "Sender is not a participant in this chat." });

        try
        {
            var messageId = await _db.CreateMessage(chatId, request.SenderId, request.Content);
            var senderName = await _db.GetSenderName(request.SenderId);

            var messageDto = new Message
            {
                Id = messageId,
                ChatId = chatId,
                SenderId = request.SenderId,
                SenderName = senderName,
                Content = request.Content,
                SentAt = DateTime.UtcNow
            };

            await BroadcastToChat(chatId, "ReceiveMessage", messageDto);

            return Ok(new
            {
                success = true,
                messageId,
                chatId,
                message = messageDto
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CHAT] Error sending message: {ex}");
            return StatusCode(500, new { message = "Die kletsboodskap kon nie verwerk word nie. Probeer asseblief weer." });
        }
    }

    /// <summary>Marks messages read up to a given point (chatId taken from the request body).</summary>
    [HttpPost("setReadStatus")]
    public async Task<IActionResult> SetReadStatus([FromBody] SetReadStatusRequest request)
    {
        return await SetReadStatusInternal(null, request);
    }

    /// <summary>Marks messages read up to a given point (chatId taken from the route).</summary>
    [HttpPost("{chatId:int}/read")]
    public async Task<IActionResult> SetReadStatusForChat(int chatId, [FromBody] SetReadStatusRequest? request = null)
    {
        return await SetReadStatusInternal(chatId, request);
    }

    /// <summary>Shared implementation for both read-status endpoints above - route chatId wins over the body's when both are given. Broadcasts a "MessagesRead" event on success.</summary>
    private async Task<IActionResult> SetReadStatusInternal(int? chatId, SetReadStatusRequest? request)
    {
        request ??= new SetReadStatusRequest();

        int resolvedChatId = (chatId.HasValue && chatId.Value > 0) ? chatId.Value : request.ChatId;
        if (resolvedChatId <= 0)
            return BadRequest(new { message = "Ongeldige chatId." });

        int resolvedUserId = request.UserId;
        if (TryGetCurrentUserId(out var currentUserId))
        {
            if (resolvedUserId <= 0)
            {
                resolvedUserId = currentUserId;
            }
            else if (resolvedUserId != currentUserId && !IsAdmin())
            {
                return StatusCode(403, new { message = "Kan nie leesstatus vir 'n ander gebruiker opdateer nie." });
            }
        }

        if (resolvedUserId <= 0)
            return BadRequest(new { message = "UserId is required." });

        bool isParticipant = await _db.IsParticipant(resolvedChatId, resolvedUserId);
        if (!isParticipant)
            return StatusCode(403, new { message = "Gebruiker is nie 'n deelnemer aan hierdie klets nie." });

        bool ok = await _db.SetReadStatus(resolvedChatId, resolvedUserId, request.LastReadMessageId);
        if (ok)
        {
            var payload = new
            {
                chatId = resolvedChatId,
                userId = resolvedUserId,
                lastReadMessageId = request.LastReadMessageId,
                readAt = DateTime.UtcNow
            };

            await BroadcastToChat(resolvedChatId, "MessagesRead", payload);

            return Ok(new
            {
                success = true,
                chatId = resolvedChatId,
                userId = resolvedUserId,
                lastReadMessageId = request.LastReadMessageId
            });
        }

        return StatusCode(403, new { message = "Gebruiker is nie 'n deelnemer aan hierdie klets nie." });
    }

    /// <summary>Deletes a message. A non-admin caller may only delete their own.</summary>
    [HttpDelete("deleteMessage/{id:int}/{userId:int}")]
    [HttpDelete("messages/{id:int}")]
    public async Task<IActionResult> DeleteMessage(int id, [FromRoute] int? userId = null)
    {
        if (id <= 0 || (userId.HasValue && userId.Value <= 0))
            return BadRequest(new { message = "Invalid id or userId." });

        int targetUserId = userId ?? 0;
        if (TryGetCurrentUserId(out var currentUserId))
        {
            if (targetUserId <= 0)
            {
                targetUserId = currentUserId;
            }
            else if (targetUserId != currentUserId && !IsAdmin())
            {
                return StatusCode(403, new { message = "Kan nie ander gebruikers se boodskappe uitvee nie." });
            }
        }

        if (targetUserId <= 0)
            return BadRequest(new { message = "UserId is required." });

        bool ok = await _db.DeleteMessage(id, targetUserId);
        return ok ? Ok(new { success = true }) : Forbid();
    }

    /// <summary>Unread chat-message count for a user (distinct from the general userNotifications bell) - defaults to the caller's own.</summary>
    [HttpGet("notifications")]
    [HttpGet("getNotificationStatus/{userId:int}")]
    public async Task<IActionResult> GetNotificationStatus([FromRoute] int? userId = null)
    {
        if (userId.HasValue && userId.Value <= 0)
            return BadRequest(new { message = "Invalid userId." });

        int targetUserId;
        if (userId.HasValue && userId.Value > 0)
        {
            targetUserId = userId.Value;
            if (!TryGetCurrentUserId(out var currentUserId))
            {
                return Unauthorized(new { message = "Gebruiker nie geverifieer nie." });
            }
            if (currentUserId != targetUserId && !IsAdmin())
            {
                return StatusCode(403, new { message = "Geen toegang tot ander gebruikers se kennisgewings nie." });
            }
        }
        else
        {
            if (!TryGetCurrentUserId(out targetUserId))
            {
                return Unauthorized(new { message = "Gebruiker nie geverifieer nie of userId ontbreek." });
            }
        }

        var status = await _db.GetNotificationStatus(targetUserId);
        return Ok(status);
    }
}
