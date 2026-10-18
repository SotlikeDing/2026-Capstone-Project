using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// Post-trip star ratings: submission (with full edit history and concurrency-safe
/// upsert), aggregate stats and recent-reviews reads, the ratee-dispute workflow
/// (submit/resolve), and a keyword-based moderation queue for comments that look
/// like they're trying to arrange payment outside the platform.
/// </summary>
public class Rating_db
{
    private readonly string _connectionString;
    public Rating_db(string connectionString) => _connectionString = connectionString;

    /// <summary>
    /// Records (or corrects) one user's rating of another for a specific completed
    /// booking. Idempotent on (bookingId, raterUserId, rateeUserId): resubmitting for
    /// the same trip updates the existing row instead of failing the unique
    /// constraint. Uses MERGE...WITH (HOLDLOCK) rather than a separate
    /// IF EXISTS/UPDATE/ELSE/INSERT so the check-then-act is atomic - two concurrent
    /// submissions for the same trip can no longer both take the INSERT branch and
    /// crash on the unique constraint (see RatingConcurrencyIntegrationTests).
    ///
    /// The same batch writes an immutable history row capturing both the before
    /// value (deleted.*, NULL on first creation) and after value (inserted.*).
    /// SQL Server won't let OUTPUT INTO target ratingHistory directly - it refuses
    /// any INTO target that has enabled triggers, append-only-enforcing INSTEAD OF
    /// triggers included - so the MERGE's OUTPUT first lands in a table variable
    /// (which can never have triggers), then a plain INSERT (not blocked; only
    /// UPDATE/DELETE are) copies it into ratingHistory. Both statements run in one
    /// explicit transaction so the rating and its history row are never split.
    /// </summary>
    public virtual async Task<int> SetRoleRating(SetRatingRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string q = @"
            BEGIN TRAN;

            DECLARE @HistoryOutput TABLE (
                changeType NVARCHAR(10), ratingId INT, bookingId INT, raterUserId INT, rateeUserId INT,
                previousStars INT NULL, previousComment NVARCHAR(MAX) NULL, newStars INT, newComment NVARCHAR(MAX) NULL, changedAt DATETIME2
            );

            MERGE INTO ratings WITH (HOLDLOCK) AS target
            USING (SELECT @bookingId AS bookingId, @rater AS raterUserId, @ratee AS rateeUserId) AS src
                ON target.bookingId = src.bookingId AND target.raterUserId = src.raterUserId AND target.rateeUserId = src.rateeUserId
            WHEN MATCHED THEN
                UPDATE SET stars = @stars, comment = @comment, rateeRoleCode = @roleCode
            WHEN NOT MATCHED THEN
                INSERT (bookingId, raterUserId, rateeUserId, rateeRoleCode, stars, comment)
                VALUES (@bookingId, @rater, @ratee, @roleCode, @stars, @comment)
            OUTPUT $action, inserted.ratingId, inserted.bookingId, inserted.raterUserId, inserted.rateeUserId,
                   deleted.stars, deleted.comment, inserted.stars, inserted.comment, SYSUTCDATETIME()
            INTO @HistoryOutput;

            INSERT INTO ratingHistory (changeType, ratingId, bookingId, raterUserId, rateeUserId, previousStars, previousComment, newStars, newComment, changedAt)
            SELECT changeType, ratingId, bookingId, raterUserId, rateeUserId, previousStars, previousComment, newStars, newComment, changedAt
            FROM @HistoryOutput;

            COMMIT;

            SELECT ratingId FROM ratings WHERE bookingId = @bookingId AND raterUserId = @rater AND rateeUserId = @ratee;";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@bookingId", request.BookingId);
        cmd.Parameters.AddWithValue("@rater", request.RaterUserId);
        cmd.Parameters.AddWithValue("@ratee", request.RateeUserId);
        cmd.Parameters.AddWithValue("@roleCode", request.RateeRoleCode);
        cmd.Parameters.AddWithValue("@stars", request.Stars);
        cmd.Parameters.AddWithValue("@comment", (object?)request.Comment ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        int ratingId = result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);

        if (ratingId > 0 && !string.IsNullOrWhiteSpace(request.Comment))
        {
            await FlagIfSuspiciousAsync(conn, ratingId, request.RaterUserId, request.RateeUserId, request.Comment);
        }

        return ratingId;
    }

    /// <summary>
    /// Average stars + rating count for a user, optionally scoped to the role they
    /// were rated in (e.g. only their PH ratings, separate from Hunter ratings on the
    /// same account). Excludes ratings whose dispute was upheld.
    /// </summary>
    public virtual async Task<RatingSummaryDto> GetRatingSummary(int userId, string? roleCode = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string q = @"SELECT AVG(CAST(stars AS DECIMAL(4,2))), COUNT(*)
                     FROM ratings
                     WHERE rateeUserId = @userId
                       AND isExcluded = 0
                       AND (@roleCode IS NULL OR rateeRoleCode = @roleCode)";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@roleCode", (object?)roleCode ?? DBNull.Value);

        using var r = await cmd.ExecuteReaderAsync();
        var summary = new RatingSummaryDto();
        if (await r.ReadAsync())
        {
            summary.AverageStars = r.IsDBNull(0) ? 0m : r.GetDecimal(0);
            summary.RatingCount = r.GetInt32(1);
        }
        return summary;
    }

    /// <summary>Most recent ratings received by a user, with the rater's name and comment, for a reviews list. Excludes ratings whose dispute was upheld.</summary>
    public virtual async Task<List<RecentRatingDto>> GetRecentRatings(int userId, int take = 5, string? roleCode = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string q = @"SELECT TOP (@take) rt.ratingId, u.uName + ' ' + u.uSurname, rt.stars, rt.comment, rt.createdAt, rt.rateeRoleCode
                     FROM ratings rt
                     JOIN users u ON rt.raterUserId = u.userId
                     WHERE rt.rateeUserId = @userId
                       AND rt.isExcluded = 0
                       AND (@roleCode IS NULL OR rt.rateeRoleCode = @roleCode)
                     ORDER BY rt.createdAt DESC";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@take", take);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@roleCode", (object?)roleCode ?? DBNull.Value);

        var list = new List<RecentRatingDto>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new RecentRatingDto
            {
                RatingId = r.GetInt32(0),
                RaterName = r.GetString(1),
                Stars = r.GetInt32(2),
                Comment = r.IsDBNull(3) ? null : r.GetString(3),
                CreatedAt = r.GetDateTime(4),
                RoleContext = r.GetString(5)
            });
        }
        return list;
    }

    /// <summary>Full immutable edit history for one rating (newest first), for admin/dispute review.</summary>
    public virtual async Task<List<RatingHistoryEntryDto>> GetRatingHistory(int ratingId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT historyId, changeType, ratingId, previousStars, previousComment, newStars, newComment, changedAt
                            FROM ratingHistory
                            WHERE ratingId = @ratingId
                            ORDER BY changedAt DESC";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@ratingId", ratingId);

        var list = new List<RatingHistoryEntryDto>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new RatingHistoryEntryDto
            {
                HistoryId = r.GetInt32(0),
                ChangeType = r.GetString(1),
                RatingId = r.GetInt32(2),
                PreviousStars = r.IsDBNull(3) ? null : r.GetInt32(3),
                PreviousComment = r.IsDBNull(4) ? null : r.GetString(4),
                NewStars = r.GetInt32(5),
                NewComment = r.IsDBNull(6) ? null : r.GetString(6),
                ChangedAt = r.GetDateTime(7)
            });
        }
        return list;
    }

    /// <summary>The ratings one rater already gave on one booking, so the "rate your team" form can show and correct them.</summary>
    public virtual async Task<List<OwnRatingDto>> GetOwnRatingsForBooking(int bookingId, int raterUserId)
    {
        var list = new List<OwnRatingDto>();
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        const string q = "SELECT rateeUserId, stars, comment FROM ratings WHERE bookingId = @bookingId AND raterUserId = @rater";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@bookingId", bookingId);
        cmd.Parameters.AddWithValue("@rater", raterUserId);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new OwnRatingDto
            {
                RateeUserId = r.GetInt32(0),
                Stars = r.GetInt32(1),
                Comment = r.IsDBNull(2) ? null : r.GetString(2)
            });
        }
        return list;
    }

    /// <summary>Who a rating belongs to (rateeUserId) - used to check dispute ownership. 0 if not found.</summary>
    public virtual async Task<int> GetRatingRateeUserId(int ratingId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = "SELECT rateeUserId FROM ratings WHERE ratingId = @ratingId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@ratingId", ratingId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    /// <summary>
    /// The ratee formally disputing a rating they received. One dispute per rating
    /// (UQ_ratingDisputes_ratingId) - resubmitting while one is still PendingReview
    /// just updates the reason. Once an admin has resolved it (Upheld/Denied) that
    /// decision is final: this deliberately does NOT touch an already-resolved
    /// dispute, so a ratee can't just keep resubmitting to force endless re-review of
    /// a decision they don't like. Returns false in that case (0 rows affected).
    /// </summary>
    public virtual async Task<bool> CreateDispute(int ratingId, int disputedByUserId, string reason)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"
            IF EXISTS (SELECT 1 FROM ratingDisputes WHERE ratingId = @ratingId AND status = 'PendingReview')
                UPDATE ratingDisputes SET reason = @reason
                WHERE ratingId = @ratingId AND status = 'PendingReview';
            ELSE IF NOT EXISTS (SELECT 1 FROM ratingDisputes WHERE ratingId = @ratingId)
                INSERT INTO ratingDisputes (ratingId, disputedByUserId, reason)
                VALUES (@ratingId, @disputedBy, @reason);";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@ratingId", ratingId);
        cmd.Parameters.AddWithValue("@disputedBy", disputedByUserId);
        cmd.Parameters.AddWithValue("@reason", reason);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Current status of a rating's dispute, or null if none exists - used to give an accurate error when a resolved dispute can't be resubmitted.</summary>
    public virtual async Task<string?> GetDisputeStatus(int ratingId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = "SELECT status FROM ratingDisputes WHERE ratingId = @ratingId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@ratingId", ratingId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : (string)result;
    }

    /// <summary>Dispute queue for the Admin Dashboard, optionally filtered by status.</summary>
    public virtual async Task<List<RatingDisputeDto>> GetDisputes(string? status = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT d.disputeId, d.ratingId, d.disputedByUserId, ISNULL(du.uName + ' ' + du.uSurname, ''),
                                   rt.raterUserId, ISNULL(ru.uName + ' ' + ru.uSurname, ''), rt.stars, rt.comment,
                                   d.reason, d.status, d.createdAt, d.reviewedBy, d.reviewedAt, d.resolutionNotes
                            FROM ratingDisputes d
                            JOIN ratings rt ON rt.ratingId = d.ratingId
                            LEFT JOIN users du ON du.userId = d.disputedByUserId
                            LEFT JOIN users ru ON ru.userId = rt.raterUserId
                            WHERE (@status IS NULL OR d.status = @status)
                            ORDER BY d.createdAt DESC";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);

        var list = new List<RatingDisputeDto>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new RatingDisputeDto
            {
                DisputeId = r.GetInt32(0),
                RatingId = r.GetInt32(1),
                DisputedByUserId = r.GetInt32(2),
                DisputedByUserName = r.GetString(3),
                RaterUserId = r.GetInt32(4),
                RaterUserName = r.GetString(5),
                Stars = r.GetInt32(6),
                Comment = r.IsDBNull(7) ? null : r.GetString(7),
                Reason = r.GetString(8),
                Status = r.GetString(9),
                CreatedAt = r.GetDateTime(10),
                ReviewedBy = r.IsDBNull(11) ? null : r.GetInt32(11),
                ReviewedAt = r.IsDBNull(12) ? null : r.GetDateTime(12),
                ResolutionNotes = r.IsDBNull(13) ? null : r.GetString(13)
            });
        }
        return list;
    }

    /// <summary>
    /// Admin resolution of a dispute. Upholding it excludes the rating from public
    /// averages/reviews (ratings.isExcluded = 1) - the row and its full edit history
    /// stay intact, this just stops it from counting. Both writes happen in one
    /// transaction so a dispute can never end up "Upheld" without the exclusion
    /// actually taking effect, or vice versa.
    /// </summary>
    public virtual async Task<bool> ResolveDispute(int disputeId, string status, int reviewerUserId, string? notes)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            const string updateDisputeQ = @"UPDATE ratingDisputes
                                             SET status = @status, reviewedBy = @reviewer, reviewedAt = SYSUTCDATETIME(), resolutionNotes = @notes
                                             WHERE disputeId = @disputeId";
            using var updateCmd = new SqlCommand(updateDisputeQ, conn, tx);
            updateCmd.Parameters.AddWithValue("@status", status);
            updateCmd.Parameters.AddWithValue("@reviewer", reviewerUserId);
            updateCmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@disputeId", disputeId);
            int rows = await updateCmd.ExecuteNonQueryAsync();

            if (rows == 0)
            {
                tx.Rollback();
                return false;
            }

            if (string.Equals(status, "Upheld", StringComparison.OrdinalIgnoreCase))
            {
                const string excludeQ = @"UPDATE ratings SET isExcluded = 1
                                           WHERE ratingId = (SELECT ratingId FROM ratingDisputes WHERE disputeId = @disputeId)";
                using var excludeCmd = new SqlCommand(excludeQ, conn, tx);
                excludeCmd.Parameters.AddWithValue("@disputeId", disputeId);
                await excludeCmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // --- Rating-comment moderation (mirrors Chats_db's flaggedChatMessages queue) ---

    /// <summary>Flags a rating comment for moderation if it contains a fee-bypass keyword (e.g. arranging payment off-platform). Best-effort - failures here never block the rating itself.</summary>
    private async Task FlagIfSuspiciousAsync(SqlConnection conn, int ratingId, int raterUserId, int rateeUserId, string comment)
    {
        try
        {
            var matched = WebAPI.Services.ModerationKeywords.FeeBypassKeywords
                .Where(k => comment.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matched.Count == 0) return;

            await EnsureFlaggedRatingCommentsTableAsync(conn);

            // Resubmitting an edited comment shouldn't pile up duplicate flags for the
            // same rating - replace the pending flag instead of stacking a new one.
            const string q = @"
                DELETE FROM flaggedRatingComments WHERE frcRatingId = @ratingId AND frcStatus = 'PendingReview';
                INSERT INTO flaggedRatingComments (frcRatingId, frcRaterUserId, frcRateeUserId, frcCommentText, frcFlaggedKeywords)
                VALUES (@ratingId, @rater, @ratee, @text, @keywords);";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@ratingId", ratingId);
            cmd.Parameters.AddWithValue("@rater", raterUserId);
            cmd.Parameters.AddWithValue("@ratee", rateeUserId);
            cmd.Parameters.AddWithValue("@text", comment);
            cmd.Parameters.AddWithValue("@keywords", string.Join(", ", matched));
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Never let moderation-queue bookkeeping block a rating from being saved.
        }
    }

    private static bool _flaggedRatingCommentsTableEnsured = false;

    /// <summary>Creates the flaggedRatingComments table on first use (in-code migration, checked once per process via the static flag above).</summary>
    private async Task EnsureFlaggedRatingCommentsTableAsync(SqlConnection conn)
    {
        if (_flaggedRatingCommentsTableEnsured) return;
        string q = @"IF OBJECT_ID('flaggedRatingComments', 'U') IS NULL
                     BEGIN
                         CREATE TABLE flaggedRatingComments (
                             flagId INT IDENTITY(1,1) PRIMARY KEY,
                             frcRatingId INT NOT NULL FOREIGN KEY REFERENCES ratings(ratingId),
                             frcRaterUserId INT NOT NULL,
                             frcRateeUserId INT NOT NULL,
                             frcCommentText NVARCHAR(MAX) NOT NULL,
                             frcFlaggedKeywords NVARCHAR(500) NOT NULL,
                             frcCreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                             frcStatus NVARCHAR(30) NOT NULL DEFAULT 'PendingReview',
                             frcReviewedBy INT NULL,
                             frcReviewedAt DATETIME2 NULL
                         );
                     END";
        using var cmd = new SqlCommand(q, conn);
        await cmd.ExecuteNonQueryAsync();
        _flaggedRatingCommentsTableEnsured = true;
    }

    /// <summary>Flagged rating comments for the Admin Dashboard's moderation queue.</summary>
    public virtual async Task<List<FlaggedRatingCommentDto>> GetFlaggedRatingComments()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureFlaggedRatingCommentsTableAsync(conn);

        const string q = @"SELECT f.flagId, f.frcRatingId, f.frcRaterUserId, ISNULL(ru.uName + ' ' + ru.uSurname, ''),
                                   f.frcRateeUserId, ISNULL(eu.uName + ' ' + eu.uSurname, ''),
                                   f.frcCommentText, f.frcFlaggedKeywords, f.frcCreatedAt, f.frcStatus
                            FROM flaggedRatingComments f
                            LEFT JOIN users ru ON ru.userId = f.frcRaterUserId
                            LEFT JOIN users eu ON eu.userId = f.frcRateeUserId
                            ORDER BY f.frcCreatedAt DESC";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<FlaggedRatingCommentDto>();
        while (await r.ReadAsync())
        {
            list.Add(new FlaggedRatingCommentDto
            {
                Id = r.GetInt32(0),
                RatingId = r.GetInt32(1),
                RaterUserId = r.GetInt32(2),
                RaterUserName = r.GetString(3),
                RateeUserId = r.GetInt32(4),
                RateeUserName = r.GetString(5),
                CommentText = r.GetString(6),
                FlaggedKeywords = r.GetString(7),
                CreatedAt = r.GetDateTime(8),
                Status = r.GetString(9)
            });
        }
        return list;
    }

    /// <summary>Marks a flagged comment reviewed (e.g. Dismissed/ActionTaken), recording who and when.</summary>
    public virtual async Task<bool> ModerateRatingComment(int flagId, string status, int reviewerUserId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureFlaggedRatingCommentsTableAsync(conn);

        const string q = @"UPDATE flaggedRatingComments
                            SET frcStatus = @status, frcReviewedBy = @reviewer, frcReviewedAt = SYSUTCDATETIME()
                            WHERE flagId = @flagId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@reviewer", reviewerUserId);
        cmd.Parameters.AddWithValue("@flagId", flagId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
