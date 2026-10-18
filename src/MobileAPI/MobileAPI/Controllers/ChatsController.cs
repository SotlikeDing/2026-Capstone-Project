using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Models;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/chats")]
[Authorize]
public class ChatsController : ControllerBase
{
    private readonly Chats_db _db;

    public ChatsController(Chats_db db)
    {
        _db = db;
    }

    [HttpPost("createChat")]
    public async Task<IActionResult> CreateChat([FromBody] CreateChatRequest request)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Forbid();
        }

        if (request.ChatParticipants == null || request.ChatParticipants.Count == 0)
            return BadRequest(new { message = "At least one chat participant is required." });

        if (request.SenderId <= 0)
        {
            request.SenderId = request.ChatParticipants
                .Where(participant => participant.UserId > 0)
                .Select(participant => participant.UserId)
                .FirstOrDefault();
        }

        if (request.SenderId <= 0)
            return BadRequest(new { message = "SenderId is required, or the first participant must have a valid UserId." });

        if (request.SenderId != currentUserId)
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.InitialMessage))
            return BadRequest(new { message = "InitialMessage is required." });

        try
        {
            var chatId = await _db.CreateChat(request);

            return Ok(new
            {
                success = true,
                message = "Chat created successfully",
                chatId
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Error creating chat: {ex.Message}" });
        }
    }

    [HttpGet("user/{userId}/chats")]
    public async Task<IActionResult> GetUserChats(int userId)
    {
        if (!TryGetCurrentUserId(out var currentUserId) || currentUserId != userId)
        {
            return Forbid();
        }

        var chats = await _db.GetChats(userId);
        return Ok(chats);
    }

    [HttpGet("{chatId}/messages")]
    public async Task<IActionResult> GetChatMessages(int chatId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        // Every other action in this controller checks the caller is the actual
        // participant before returning/mutating data - this one didn't, letting any
        // authenticated user read any other user's private chat messages just by
        // guessing/enumerating chatId.
        if (!TryGetCurrentUserId(out var currentUserId) || !await _db.IsParticipant(chatId, currentUserId))
        {
            return Forbid();
        }

        var messages = await _db.GetChatMessagesPaginated(chatId, page, pageSize);
        return Ok(messages);
    }

    [HttpPost("{chatId}/messages")]
    public async Task<IActionResult> CreateMessage(int chatId, [FromBody] SendMessageRequest request)
    {
        if (!TryGetCurrentUserId(out var currentUserId) || currentUserId != request.SenderId)
            return Forbid();

        if (chatId <= 0)
            return BadRequest(new { message = "Invalid chatId." });

        if (request.SenderId <= 0)
            return BadRequest(new { message = "SenderId is required." });

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "Content is required." });

        try
        {
            var messageId = await _db.CreateMessage(chatId, request.SenderId, request.Content);

            return Ok(new
            {
                success = true,
                messageId
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Error sending message: {ex.Message}" });
        }
    }

    [HttpPost("setReadStatus")]
    public async Task<IActionResult> SetReadStatus([FromBody] SetReadStatusRequest request)
    {
        if (!TryGetCurrentUserId(out var currentUserId) || currentUserId != request.UserId)
        {
            return Forbid();
        }

        var ok = await _db.SetReadStatus(request.ChatId, request.UserId, request.LastReadMessageId);
        return ok ? Ok(new { success = true }) : BadRequest(new { success = false });
    }

    [HttpDelete("deleteMessage/{id}/{userId}")]
    public async Task<IActionResult> DeleteMessage(int id, int userId)
    {
        if (!TryGetCurrentUserId(out var currentUserId) || currentUserId != userId)
        {
            return Forbid();
        }

        return (await _db.DeleteMessage(id, userId)) ? Ok() : Forbid();
    }

    [HttpGet("getNotificationStatus/{userId}")]
    public async Task<IActionResult> GetNotificationStatus(int userId)
    {
        if (!TryGetCurrentUserId(out var currentUserId) || currentUserId != userId)
        {
            return Forbid();
        }

        return Ok(await _db.GetNotificationStatus(userId));
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claimValue, out userId);
    }
}

