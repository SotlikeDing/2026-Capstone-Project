using Microsoft.Data.SqlClient;
using MobileApi.Models;

namespace MobileApi.Database;

public class Chats_db
{
    private readonly string _connectionString;

    public Chats_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    public virtual async Task<bool> IsParticipant(int chatId, int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"
            SELECT COUNT(*)
            FROM chatParticipants
            WHERE cpChatId = @ChatId AND cpUserId = @UserId AND cpLeftAtTimestamp IS NULL";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@ChatId", chatId);
        cmd.Parameters.AddWithValue("@UserId", userId);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    public async Task<int> CreateChat(CreateChatRequest ccRequest)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var transaction = conn.BeginTransaction();

        try
        {
            if (ccRequest.SenderId <= 0)
            {
                ccRequest.SenderId = ccRequest.ChatParticipants
                    .Where(participant => participant.UserId > 0)
                    .Select(participant => participant.UserId)
                    .FirstOrDefault();
            }

            if (ccRequest.SenderId <= 0)
                throw new InvalidOperationException("SenderId is required to create a chat.");

            const string insertChatQuery = @"
                INSERT INTO chat (cCreatedTimestamp, cTitle)
                VALUES (@CreatedAt, @Title);
                SELECT SCOPE_IDENTITY();";

            using var chatCmd = new SqlCommand(insertChatQuery, conn, transaction);
            chatCmd.Parameters.AddWithValue("@CreatedAt", ccRequest.CreatedAt);
            chatCmd.Parameters.AddWithValue(
                "@Title",
                string.IsNullOrWhiteSpace(ccRequest.Title) ? (object)DBNull.Value : ccRequest.Title
            );

            var chatId = Convert.ToInt32(await chatCmd.ExecuteScalarAsync());

            const string insertParticipantQuery = @"
                INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp)
                VALUES (@ChatId, @UserId, @JoinedTimestamp);";

            var participantIds = ccRequest.ChatParticipants
                .Where(p => p.UserId > 0)
                .Select(p => p.UserId)
                .ToHashSet();

            participantIds.Add(ccRequest.SenderId);

            foreach (var userId in participantIds)
            {
                using var participantCmd = new SqlCommand(insertParticipantQuery, conn, transaction);
                participantCmd.Parameters.AddWithValue("@ChatId", chatId);
                participantCmd.Parameters.AddWithValue("@UserId", userId);
                participantCmd.Parameters.AddWithValue("@JoinedTimestamp", DateTime.UtcNow);
                await participantCmd.ExecuteNonQueryAsync();
            }

            const string insertMessageQuery = @"
                INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
                VALUES (@ChatId, @SenderId, @Content, @SentTimestamp);
                SELECT SCOPE_IDENTITY();";

            using var messageCmd = new SqlCommand(insertMessageQuery, conn, transaction);
            messageCmd.Parameters.AddWithValue("@ChatId", chatId);
            messageCmd.Parameters.AddWithValue("@SenderId", ccRequest.SenderId);
            messageCmd.Parameters.AddWithValue("@Content", ccRequest.InitialMessage);
            messageCmd.Parameters.AddWithValue("@SentTimestamp", DateTime.UtcNow);

            await messageCmd.ExecuteScalarAsync();

            transaction.Commit();
            return chatId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<Chat>> GetChats(int userId)
    {
        var chats = new List<Chat>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string chatQuery = @"
            SELECT DISTINCT c.chatId, c.cCreatedTimestamp, c.cTitle
            FROM chat c
            INNER JOIN chatParticipants cp ON c.chatId = cp.cpChatId
            WHERE cp.cpUserId = @UserId
              AND cp.cpLeftAtTimestamp IS NULL
            ORDER BY c.cCreatedTimestamp DESC";

        using var chatCmd = new SqlCommand(chatQuery, conn);
        chatCmd.Parameters.AddWithValue("@UserId", userId);

        using var chatReader = await chatCmd.ExecuteReaderAsync();

        while (await chatReader.ReadAsync())
        {
            chats.Add(new Chat
            {
                Id = chatReader.GetInt32(chatReader.GetOrdinal("chatId")),
                CreatedTimestamp = chatReader.GetDateTime(chatReader.GetOrdinal("cCreatedTimestamp")),
                Title = chatReader.IsDBNull(chatReader.GetOrdinal("cTitle"))
                    ? null
                    : chatReader.GetString(chatReader.GetOrdinal("cTitle")),
                Participants = new List<ChatParticipant>(),
                Messages = new List<Message>()
            });
        }

        chatReader.Close();

        foreach (var chat in chats)
        {
            const string participantQuery = @"
                SELECT cp.chatParticipantId, cp.cpChatId, cp.cpUserId, cp.cpJoinedTimestamp,
                       cp.cpLeftAtTimestamp, cp.cpLastReadMessage
                FROM chatParticipants cp
                WHERE cp.cpChatId = @ChatId
                  AND cp.cpLeftAtTimestamp IS NULL";

            using var participantCmd = new SqlCommand(participantQuery, conn);
            participantCmd.Parameters.AddWithValue("@ChatId", chat.Id);

            using var participantReader = await participantCmd.ExecuteReaderAsync();

            while (await participantReader.ReadAsync())
            {
                chat.Participants.Add(new ChatParticipant
                {
                    Id = participantReader.GetInt32(participantReader.GetOrdinal("chatParticipantId")),
                    ChatId = participantReader.GetInt32(participantReader.GetOrdinal("cpChatId")),
                    UserId = participantReader.GetInt32(participantReader.GetOrdinal("cpUserId")),
                    JoinedTimestamp = participantReader.GetDateTime(participantReader.GetOrdinal("cpJoinedTimestamp")),
                    LeftAtTimestamp = participantReader.IsDBNull(participantReader.GetOrdinal("cpLeftAtTimestamp"))
                        ? null
                        : participantReader.GetDateTime(participantReader.GetOrdinal("cpLeftAtTimestamp")),
                    LastReadMessageId = participantReader.IsDBNull(participantReader.GetOrdinal("cpLastReadMessage"))
                        ? null
                        : participantReader.GetInt32(participantReader.GetOrdinal("cpLastReadMessage"))
                });
            }

            participantReader.Close();
        }

        return chats;
    }

    public async Task<List<Message>> GetChatMessagesPaginated(int chatId, int pageNumber = 1, int pageSize = 50)
    {
        var messages = new List<Message>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = @"
            SELECT
                cm.chatMessageId,
                cm.cmChatId,
                cm.cmSenderId,
                cm.cmContent,
                cm.cmSentTimestamp,
                u.uName,
                u.uSurname
            FROM chatMessages cm
            INNER JOIN users u ON cm.cmSenderId = u.userId
            WHERE cm.cmChatId = @ChatId
            ORDER BY cm.cmSentTimestamp DESC
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@ChatId", chatId);
        cmd.Parameters.AddWithValue("@Offset", (pageNumber - 1) * pageSize);
        cmd.Parameters.AddWithValue("@PageSize", pageSize);

        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            messages.Add(new Message
            {
                Id = reader.GetInt32(reader.GetOrdinal("chatMessageId")),
                ChatId = reader.GetInt32(reader.GetOrdinal("cmChatId")),
                SenderId = reader.GetInt32(reader.GetOrdinal("cmSenderId")),
                SenderName = $"{reader.GetString(reader.GetOrdinal("uName"))} {reader.GetString(reader.GetOrdinal("uSurname"))}",
                Content = reader.GetString(reader.GetOrdinal("cmContent")),
                // Stored as UTC; mark it so the JSON carries a 'Z' and clients
                // don't have to guess the zone.
                SentAt = DateTime.SpecifyKind(
                    reader.GetDateTime(reader.GetOrdinal("cmSentTimestamp")),
                    DateTimeKind.Utc)
            });
        }

        messages.Reverse();
        return messages;
    }

    public async Task<bool> SetReadStatus(int chatId, int userId, int lastReadMessageId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"
            UPDATE chatParticipants
            SET cpLastReadMessage = @m
            WHERE cpChatId = @chatId AND cpUserId = @u";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@m", lastReadMessageId);
        cmd.Parameters.AddWithValue("@chatId", chatId);
        cmd.Parameters.AddWithValue("@u", userId);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<int> CreateMessage(int chatId, int senderId, string content)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string checkQuery = @"
            SELECT COUNT(*)
            FROM chatParticipants
            WHERE cpChatId = @chatId AND cpUserId = @senderId";

        using (var checkCmd = new SqlCommand(checkQuery, conn))
        {
            checkCmd.Parameters.AddWithValue("@chatId", chatId);
            checkCmd.Parameters.AddWithValue("@senderId", senderId);

            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (exists == 0)
                throw new InvalidOperationException("Sender is not a participant in this chat.");
        }

        const string q = @"
            INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
            VALUES (@c, @s, @t, GETUTCDATE());
            SELECT SCOPE_IDENTITY();";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@c", chatId);
        cmd.Parameters.AddWithValue("@s", senderId);
        cmd.Parameters.AddWithValue("@t", content);

        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    public async Task<bool> DeleteMessage(int id, int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = "DELETE FROM chatMessages WHERE chatMessageId = @id AND cmSenderId = @u";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@u", userId);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<NotificationStatus> GetNotificationStatus(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"
            SELECT COUNT(*)
            FROM chatMessages cm
            INNER JOIN chatParticipants cp ON cp.cpChatId = cm.cmChatId
            WHERE cp.cpUserId = @u
              AND cm.cmSenderId <> @u
              AND cm.chatMessageId > ISNULL(cp.cpLastReadMessage, 0)";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@u", userId);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        return new NotificationStatus
        {
            IsEnabled = true,
            UnreadCount = count,
            LastNotified = DateTime.UtcNow
        };
    }
}

