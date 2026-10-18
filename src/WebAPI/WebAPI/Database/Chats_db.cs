using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// Real-time chat data access: chat/message CRUD, read-receipt tracking, unread
/// counts, direct-chat find-or-create, and keyword-based moderation flagging
/// (mirrors Rating_db's comment moderation).
/// </summary>
public class Chats_db
{
    private readonly string _connectionString;
    public Chats_db() : this(string.Empty) { }
    public Chats_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Creates a new chat with its participants (deduplicated) and an optional initial message, all in one transaction.</summary>
    public virtual async Task<int> CreateChat(CreateChatRequest ccRequest)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var transaction = conn.BeginTransaction();

        try
        {
            // If senderId not provided or <= 0, infer from participants
            if (ccRequest.SenderId <= 0)
            {
                ccRequest.SenderId = ccRequest.ChatParticipants
                    .Where(p => p.UserId > 0)
                    .Select(p => p.UserId)
                    .FirstOrDefault();
            }

            var createdAt = (ccRequest.CreatedAt == default || ccRequest.CreatedAt == DateTime.MinValue)
                ? DateTime.UtcNow
                : ccRequest.CreatedAt;

            // Step 1: Insert the chat and get the new chat ID
            const string insertChatQuery = @"INSERT INTO chat (cCreatedTimestamp, cTitle) 
                                             VALUES (@CreatedAt, @Title); 
                                             SELECT SCOPE_IDENTITY();";

            using var chatCmd = new SqlCommand(insertChatQuery, conn, transaction);
            chatCmd.Parameters.AddWithValue("@CreatedAt", createdAt);
            chatCmd.Parameters.AddWithValue("@Title", string.IsNullOrWhiteSpace(ccRequest.Title) ? (object)DBNull.Value : ccRequest.Title);

            int chatId = Convert.ToInt32(await chatCmd.ExecuteScalarAsync());

            // Step 2: Insert all chat participants (deduplicated)
            var participantIds = ccRequest.ChatParticipants
                .Where(p => p.UserId > 0)
                .Select(p => p.UserId)
                .ToHashSet();

            if (ccRequest.ParticipantIds != null)
            {
                foreach (var pid in ccRequest.ParticipantIds.Where(id => id > 0))
                {
                    participantIds.Add(pid);
                }
            }

            if (ccRequest.SenderId > 0)
            {
                participantIds.Add(ccRequest.SenderId);
            }

            const string insertParticipantQuery = @"INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) 
                                                    VALUES (@ChatId, @UserId, @JoinedTimestamp);";

            foreach (var userId in participantIds)
            {
                using var participantCmd = new SqlCommand(insertParticipantQuery, conn, transaction);
                participantCmd.Parameters.AddWithValue("@ChatId", chatId);
                participantCmd.Parameters.AddWithValue("@UserId", userId);
                participantCmd.Parameters.AddWithValue("@JoinedTimestamp", DateTime.UtcNow);
                await participantCmd.ExecuteNonQueryAsync();
            }

            // Step 3: Insert initial message if provided
            if (!string.IsNullOrWhiteSpace(ccRequest.Message) && ccRequest.SenderId > 0)
            {
                const string insertMessageQuery = @"INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp) 
                                                    VALUES (@ChatId, @SenderId, @Content, @SentTimestamp); 
                                                    SELECT SCOPE_IDENTITY();";

                using var messageCmd = new SqlCommand(insertMessageQuery, conn, transaction);
                messageCmd.Parameters.AddWithValue("@ChatId", chatId);
                messageCmd.Parameters.AddWithValue("@SenderId", ccRequest.SenderId);
                messageCmd.Parameters.AddWithValue("@Content", ccRequest.Message);
                messageCmd.Parameters.AddWithValue("@SentTimestamp", DateTime.UtcNow);

                await messageCmd.ExecuteScalarAsync();
            }

            transaction.Commit();
            return chatId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>Lists a user's chats with last-message preview and unread count, in one query (OUTER APPLY per chat) rather than N+1 round trips.</summary>
    public virtual async Task<List<Chat>> GetChats(int userId)
    {
        var chats = new List<Chat>();
        var chatLookup = new Dictionary<int, Chat>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Optimized single query avoiding N+1 queries. Outer apply for latest message & unread count.
        const string query = @"
            SELECT 
                c.chatId, 
                c.cCreatedTimestamp, 
                c.cTitle,
                lm.cmContent AS LastMessage,
                lm.cmSentTimestamp AS LastMessageTimestamp,
                ISNULL(unread.UnreadCount, 0) AS UnreadCount,
                cp.chatParticipantId,
                cp.cpChatId,
                cp.cpUserId,
                cp.cpJoinedTimestamp,
                cp.cpLeftAtTimestamp,
                cp.cpLastReadMessage,
                u.uName,
                u.uSurname
            FROM chat c
            INNER JOIN chatParticipants myCp ON c.chatId = myCp.cpChatId AND myCp.cpUserId = @UserId
            LEFT JOIN chatParticipants cp ON c.chatId = cp.cpChatId
            LEFT JOIN users u ON cp.cpUserId = u.userId
            OUTER APPLY (
                SELECT TOP 1 cm.cmContent, cm.cmSentTimestamp
                FROM chatMessages cm
                WHERE cm.cmChatId = c.chatId
                ORDER BY cm.cmSentTimestamp DESC, cm.chatMessageId DESC
            ) lm
            OUTER APPLY (
                SELECT COUNT(*) AS UnreadCount
                FROM chatMessages cm2
                WHERE cm2.cmChatId = c.chatId
                  AND cm2.cmSenderId <> @UserId
                  AND cm2.chatMessageId > ISNULL(myCp.cpLastReadMessage, 0)
            ) unread
            ORDER BY ISNULL(lm.cmSentTimestamp, c.cCreatedTimestamp) DESC, c.chatId DESC, cp.chatParticipantId ASC";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);

        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int chatId = reader.GetInt32(reader.GetOrdinal("chatId"));
            if (!chatLookup.TryGetValue(chatId, out var chat))
            {
                chat = new Chat
                {
                    Id = chatId,
                    CreatedTimestamp = reader.GetDateTime(reader.GetOrdinal("cCreatedTimestamp")),
                    Title = reader.IsDBNull(reader.GetOrdinal("cTitle")) ? null : reader.GetString(reader.GetOrdinal("cTitle")),
                    LastMessage = reader.IsDBNull(reader.GetOrdinal("LastMessage")) ? null : reader.GetString(reader.GetOrdinal("LastMessage")),
                    LastMessageTimestamp = reader.IsDBNull(reader.GetOrdinal("LastMessageTimestamp")) ? null : reader.GetDateTime(reader.GetOrdinal("LastMessageTimestamp")),
                    UnreadCount = reader.GetInt32(reader.GetOrdinal("UnreadCount")),
                    Participants = new List<ChatParticipant>(),
                    Messages = new List<Message>()
                };
                chatLookup[chatId] = chat;
                chats.Add(chat);
            }

            if (!reader.IsDBNull(reader.GetOrdinal("chatParticipantId")))
            {
                int cpUserId = reader.GetInt32(reader.GetOrdinal("cpUserId"));
                if (!chat.Participants.Any(p => p.UserId == cpUserId))
                {
                    string? firstName = reader.IsDBNull(reader.GetOrdinal("uName")) ? null : reader.GetString(reader.GetOrdinal("uName"));
                    string? surname = reader.IsDBNull(reader.GetOrdinal("uSurname")) ? null : reader.GetString(reader.GetOrdinal("uSurname"));
                    string? fullName = (firstName != null || surname != null) ? $"{firstName} {surname}".Trim() : null;

                    chat.Participants.Add(new ChatParticipant
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("chatParticipantId")),
                        ChatId = reader.GetInt32(reader.GetOrdinal("cpChatId")),
                        UserId = cpUserId,
                        JoinedTimestamp = reader.GetDateTime(reader.GetOrdinal("cpJoinedTimestamp")),
                        LeftAtTimestamp = reader.IsDBNull(reader.GetOrdinal("cpLeftAtTimestamp"))
                            ? null
                            : reader.GetDateTime(reader.GetOrdinal("cpLeftAtTimestamp")),
                        LastReadMessageId = reader.IsDBNull(reader.GetOrdinal("cpLastReadMessage"))
                            ? null
                            : reader.GetInt32(reader.GetOrdinal("cpLastReadMessage")),
                        UserName = fullName
                    });
                }
            }
        }

        return chats;
    }

    /// <summary>Paginated message history, fetched newest-first then reversed into chronological order for the client.</summary>
    public virtual async Task<List<Message>> GetChatMessagesPaginated(int chatId, int pageNumber = 1, int pageSize = 50)
    {
        var messages = new List<Message>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = @"
            SELECT cm.chatMessageId, cm.cmChatId, cm.cmSenderId, cm.cmContent, cm.cmSentTimestamp, u.uName, u.uSurname
            FROM chatMessages cm
            INNER JOIN users u ON cm.cmSenderId = u.userId
            WHERE cm.cmChatId = @ChatId
            ORDER BY cm.cmSentTimestamp DESC
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@ChatId", chatId);
        cmd.Parameters.AddWithValue("@Offset", Math.Max(0, (pageNumber - 1) * pageSize));
        cmd.Parameters.AddWithValue("@PageSize", Math.Max(1, pageSize));

        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            messages.Add(new Message
            {
                Id = reader.GetInt32(reader.GetOrdinal("chatMessageId")),
                ChatId = reader.GetInt32(reader.GetOrdinal("cmChatId")),
                SenderId = reader.GetInt32(reader.GetOrdinal("cmSenderId")),
                SenderName = $"{reader.GetString(reader.GetOrdinal("uName"))} {reader.GetString(reader.GetOrdinal("uSurname"))}".Trim(),
                Content = reader.GetString(reader.GetOrdinal("cmContent")),
                SentAt = reader.GetDateTime(reader.GetOrdinal("cmSentTimestamp"))
            });
        }

        // Chronological order for clients
        messages.Reverse();

        return messages;
    }

    /// <summary>
    /// Marks messages read up to lastReadMessageId (never moving the watermark
    /// backwards), or - if 0/omitted - marks everything in the chat read by
    /// jumping straight to the latest message id.
    /// </summary>
    public virtual async Task<bool> SetReadStatus(int chatId, int userId, int lastReadMessageId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string q = lastReadMessageId > 0
            ? @"UPDATE chatParticipants
                SET cpLastReadMessage = CASE 
                    WHEN @m > ISNULL(cpLastReadMessage, 0) THEN @m 
                    ELSE ISNULL(cpLastReadMessage, @m) 
                END
                WHERE cpChatId = @chatId AND cpUserId = @u"
            : @"UPDATE chatParticipants
                SET cpLastReadMessage = ISNULL((SELECT MAX(chatMessageId) FROM chatMessages WHERE cmChatId = @chatId), 0)
                WHERE cpChatId = @chatId AND cpUserId = @u";

        using var cmd = new SqlCommand(q, conn);
        if (lastReadMessageId > 0)
        {
            cmd.Parameters.AddWithValue("@m", lastReadMessageId);
        }
        cmd.Parameters.AddWithValue("@chatId", chatId);
        cmd.Parameters.AddWithValue("@u", userId);

        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    /// <summary>Sends a message after confirming the sender is a chat participant (throws InvalidOperationException otherwise), then runs it through moderation flagging.</summary>
    public virtual async Task<int> CreateMessage(int chatId, int senderId, string content)
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
        int messageId = Convert.ToInt32(id);

        await FlagIfSuspiciousAsync(conn, messageId, chatId, senderId, content);

        return messageId;
    }

    // Keyword-based fee-bypass detection for the Admin Dashboard's chat moderation
    // queue. Best-effort: a flagging failure must never block message delivery.
    private async Task FlagIfSuspiciousAsync(SqlConnection conn, int messageId, int chatId, int senderId, string content)
    {
        try
        {
            var matched = WebAPI.Services.ModerationKeywords.FeeBypassKeywords.Where(k => content.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matched.Count == 0) return;

            await EnsureFlaggedMessagesTableAsync(conn);

            const string insertQ = @"INSERT INTO flaggedChatMessages (fcmMessageId, fcmChatId, fcmSenderId, fcmMessageText, fcmFlaggedKeywords, fcmSentAt, fcmStatus)
                                      VALUES (@messageId, @chatId, @senderId, @text, @keywords, SYSUTCDATETIME(), 'PendingReview');";
            using var insertCmd = new SqlCommand(insertQ, conn);
            insertCmd.Parameters.AddWithValue("@messageId", messageId);
            insertCmd.Parameters.AddWithValue("@chatId", chatId);
            insertCmd.Parameters.AddWithValue("@senderId", senderId);
            insertCmd.Parameters.AddWithValue("@text", content);
            insertCmd.Parameters.AddWithValue("@keywords", string.Join(", ", matched));
            await insertCmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Never let moderation-queue bookkeeping break message sending.
        }
    }

    private static bool _flaggedMessagesTableEnsured = false;

    /// <summary>Creates the flaggedChatMessages table on first use (in-code migration, checked once per process).</summary>
    private async Task EnsureFlaggedMessagesTableAsync(SqlConnection conn)
    {
        if (_flaggedMessagesTableEnsured) return;
        string q = @"IF OBJECT_ID('flaggedChatMessages', 'U') IS NULL
                     BEGIN
                         CREATE TABLE flaggedChatMessages (
                             flagId INT IDENTITY(1,1) PRIMARY KEY,
                             fcmMessageId INT NOT NULL FOREIGN KEY REFERENCES chatMessages(chatMessageId),
                             fcmChatId INT NOT NULL,
                             fcmSenderId INT NOT NULL,
                             fcmMessageText NVARCHAR(MAX) NOT NULL,
                             fcmFlaggedKeywords NVARCHAR(500) NOT NULL,
                             fcmSentAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                             fcmStatus NVARCHAR(30) NOT NULL DEFAULT 'PendingReview',
                             fcmReviewedBy INT NULL,
                             fcmReviewedAt DATETIME2 NULL
                         );
                     END";
        using var cmd = new SqlCommand(q, conn);
        await cmd.ExecuteNonQueryAsync();
        _flaggedMessagesTableEnsured = true;
    }

    /// <summary>
    /// Flagged messages for the Admin Dashboard's moderation queue. "Recipient" is
    /// the other participant in the chat - fine for this app's 1:1 direct chats; in a
    /// hypothetical group chat this picks one arbitrary other participant.
    /// </summary>
    public virtual async Task<List<FlaggedMessageDto>> GetFlaggedMessages()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureFlaggedMessagesTableAsync(conn);

        const string q = @"SELECT f.flagId, f.fcmChatId, f.fcmMessageId, f.fcmSenderId,
                                   ISNULL(su.uName + ' ' + su.uSurname, ''),
                                   ISNULL(recipient.userId, 0), ISNULL(ru.uName + ' ' + ru.uSurname, ''),
                                   f.fcmMessageText, f.fcmFlaggedKeywords, f.fcmSentAt, f.fcmStatus
                            FROM flaggedChatMessages f
                            LEFT JOIN users su ON su.userId = f.fcmSenderId
                            OUTER APPLY (
                                SELECT TOP 1 cp.cpUserId AS userId
                                FROM chatParticipants cp
                                WHERE cp.cpChatId = f.fcmChatId AND cp.cpUserId <> f.fcmSenderId
                            ) recipient
                            LEFT JOIN users ru ON ru.userId = recipient.userId
                            ORDER BY f.fcmSentAt DESC";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<FlaggedMessageDto>();
        while (await r.ReadAsync())
        {
            list.Add(new FlaggedMessageDto
            {
                Id = r.GetInt32(0),
                ChatId = r.GetInt32(1),
                MessageId = r.GetInt32(2),
                SenderUserId = r.GetInt32(3),
                SenderUserName = r.GetString(4),
                RecipientUserId = r.GetInt32(5),
                RecipientUserName = r.GetString(6),
                MessageText = r.GetString(7),
                FlaggedKeywords = r.GetString(8),
                SentAt = r.GetDateTime(9),
                Status = r.GetString(10)
            });
        }
        return list;
    }

    /// <summary>Marks a flagged message reviewed, recording who and when.</summary>
    public virtual async Task<bool> ModerateMessage(int flagId, string status, int reviewerUserId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureFlaggedMessagesTableAsync(conn);

        const string q = @"UPDATE flaggedChatMessages
                            SET fcmStatus = @status, fcmReviewedBy = @reviewer, fcmReviewedAt = SYSUTCDATETIME()
                            WHERE flagId = @flagId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@reviewer", reviewerUserId);
        cmd.Parameters.AddWithValue("@flagId", flagId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Deletes a message - only succeeds if userId is actually its sender (the WHERE clause itself enforces this).</summary>
    public virtual async Task<bool> DeleteMessage(int id, int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = "DELETE FROM chatMessages WHERE chatMessageId = @id AND cmSenderId = @u";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@u", userId);

        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    /// <summary>Unread chat-message count across every chat a user is still an active participant in (excludes chats they've left).</summary>
    public virtual async Task<NotificationStatus> GetNotificationStatus(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Fixed multi-row subquery error by using clean JOIN on chatParticipants
        const string q = @"
            SELECT COUNT(*)
            FROM chatMessages cm
            INNER JOIN chatParticipants cp ON cp.cpChatId = cm.cmChatId
            WHERE cp.cpUserId = @u
              AND cp.cpLeftAtTimestamp IS NULL
              AND cm.cmSenderId <> @u
              AND cm.chatMessageId > ISNULL(cp.cpLastReadMessage, 0)";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@u", userId);

        int count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return new NotificationStatus
        {
            IsEnabled = true,
            UnreadCount = count,
            LastNotified = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Finds the existing exactly-2-participant chat between two users, or creates
    /// one. If found and an initialMessage is given, it's sent into the existing
    /// chat via CreateMessage (which runs it through moderation); if creating fresh,
    /// the message is inserted directly in the same transaction, then flagged after commit.
    /// </summary>
    public virtual async Task<int> GetOrCreateDirectChat(int user1Id, int user2Id, string? initialMessage = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Check if a direct 2-person chat between user1Id and user2Id already exists
        const string findQuery = @"
            SELECT cp1.cpChatId
            FROM chatParticipants cp1
            INNER JOIN chatParticipants cp2 ON cp1.cpChatId = cp2.cpChatId
            WHERE cp1.cpUserId = @User1Id AND cp2.cpUserId = @User2Id
              AND (
                  SELECT COUNT(*) 
                  FROM chatParticipants cpAll 
                  WHERE cpAll.cpChatId = cp1.cpChatId
              ) = 2";

        using (var findCmd = new SqlCommand(findQuery, conn))
        {
            findCmd.Parameters.AddWithValue("@User1Id", user1Id);
            findCmd.Parameters.AddWithValue("@User2Id", user2Id);

            var existingResult = await findCmd.ExecuteScalarAsync();
            if (existingResult != null && existingResult != DBNull.Value)
            {
                int existingChatId = Convert.ToInt32(existingResult);
                if (!string.IsNullOrWhiteSpace(initialMessage))
                {
                    await CreateMessage(existingChatId, user1Id, initialMessage);
                }
                return existingChatId;
            }
        }

        // Create direct chat inside transaction
        using var transaction = conn.BeginTransaction();
        try
        {
            const string insertChatQuery = @"
                INSERT INTO chat (cCreatedTimestamp, cTitle) 
                VALUES (@CreatedAt, NULL); 
                SELECT SCOPE_IDENTITY();";

            using var chatCmd = new SqlCommand(insertChatQuery, conn, transaction);
            chatCmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
            int newChatId = Convert.ToInt32(await chatCmd.ExecuteScalarAsync());

            const string insertParticipantQuery = @"
                INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) 
                VALUES (@ChatId, @UserId, @JoinedTimestamp);";

            var participants = new[] { user1Id, user2Id }.Distinct();
            foreach (var pId in participants)
            {
                using var pCmd = new SqlCommand(insertParticipantQuery, conn, transaction);
                pCmd.Parameters.AddWithValue("@ChatId", newChatId);
                pCmd.Parameters.AddWithValue("@UserId", pId);
                pCmd.Parameters.AddWithValue("@JoinedTimestamp", DateTime.UtcNow);
                await pCmd.ExecuteNonQueryAsync();
            }

            int? newMessageId = null;
            if (!string.IsNullOrWhiteSpace(initialMessage))
            {
                const string insertMessageQuery = @"
                    INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
                    VALUES (@ChatId, @SenderId, @Content, @SentTimestamp);
                    SELECT SCOPE_IDENTITY();";

                using var msgCmd = new SqlCommand(insertMessageQuery, conn, transaction);
                msgCmd.Parameters.AddWithValue("@ChatId", newChatId);
                msgCmd.Parameters.AddWithValue("@SenderId", user1Id);
                msgCmd.Parameters.AddWithValue("@Content", initialMessage);
                msgCmd.Parameters.AddWithValue("@SentTimestamp", DateTime.UtcNow);
                newMessageId = Convert.ToInt32(await msgCmd.ExecuteScalarAsync());
            }

            transaction.Commit();

            if (newMessageId.HasValue && !string.IsNullOrWhiteSpace(initialMessage))
            {
                await FlagIfSuspiciousAsync(conn, newMessageId.Value, newChatId, user1Id, initialMessage!);
            }

            return newChatId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>Whether a user is a current (not left) participant of a chat.</summary>
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

    /// <summary>A user's display name for message attribution, falling back to "Gebruiker" if not found/blank.</summary>
    public virtual async Task<string> GetSenderName(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = "SELECT uName, uSurname FROM users WHERE userId = @UserId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var first = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var last = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var fullName = $"{first} {last}".Trim();
            if (!string.IsNullOrEmpty(fullName)) return fullName;
        }

        return "Gebruiker";
    }
}
