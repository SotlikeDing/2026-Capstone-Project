using Microsoft.Data.SqlClient;
using MobileApi.Models;

namespace MobileApi.Database;

public class FarmRatings_db
{
    private readonly string _connectionString;

    public FarmRatings_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Creates or updates the logged-in hunter's farm rating for one booking.
    /// Eligibility and ownership are checked inside the same transaction as the
    /// upsert, so a client cannot rate an arbitrary farm or somebody else's trip.
    /// A trip is eligible once explicitly completed, or once its end date has
    /// passed unless it was rejected/cancelled.
    /// </summary>
    public virtual async Task<SaveFarmRatingResult> SetFarmRating(
        int bookingId,
        int raterUserId,
        int stars,
        string? description)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = @"
            SET XACT_ABORT ON;
            BEGIN TRAN;

            IF NOT EXISTS (SELECT 1 FROM bookings WHERE bookingId = @bookingId)
            BEGIN
                ROLLBACK TRAN;
                SELECT -1;
                RETURN;
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM bookings b
                JOIN userRoles hunterRole ON b.bookHunterId = hunterRole.userRoleId
                WHERE b.bookingId = @bookingId AND hunterRole.urUserId = @raterUserId
            )
            BEGIN
                ROLLBACK TRAN;
                SELECT -2;
                RETURN;
            END;

            DECLARE @farmId INT;
            DECLARE @status NVARCHAR(MAX);
            DECLARE @endDate DATE;

            SELECT @farmId = p.pFarmId, @status = b.bookStatus, @endDate = b.bookEndDate
            FROM bookings b
            JOIN packages p ON b.bookPackageId = p.packageId
            WHERE b.bookingId = @bookingId;

            IF @farmId IS NULL
            BEGIN
                ROLLBACK TRAN;
                SELECT -3;
                RETURN;
            END;

            DECLARE @normalizedStatus NVARCHAR(MAX) = UPPER(LTRIM(RTRIM(ISNULL(@status, ''))));
            DECLARE @isCompleted BIT = CASE WHEN @normalizedStatus IN ('COMPLETED', 'VOLTOOI') THEN 1 ELSE 0 END;
            DECLARE @isCancelled BIT = CASE WHEN @normalizedStatus IN
                ('REJECTED', 'AFGEKEUR', 'CANCELLED', 'CANCELED', 'GEKANSELLEER') THEN 1 ELSE 0 END;

            IF @isCompleted = 0 AND (@endDate IS NULL OR @endDate >= CAST(GETDATE() AS DATE) OR @isCancelled = 1)
            BEGIN
                ROLLBACK TRAN;
                SELECT -4;
                RETURN;
            END;

            DECLARE @saved TABLE (farmRatingId INT);

            MERGE INTO farmRatings WITH (HOLDLOCK) AS target
            USING (SELECT @bookingId AS bookingId, @raterUserId AS raterUserId, @farmId AS farmId) AS source
                ON target.bookingId = source.bookingId
                AND target.raterUserId = source.raterUserId
                AND target.farmId = source.farmId
            WHEN MATCHED THEN
                UPDATE SET stars = @stars,
                           description = @description,
                           updatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (bookingId, farmId, raterUserId, stars, description)
                VALUES (@bookingId, @farmId, @raterUserId, @stars, @description)
            OUTPUT inserted.farmRatingId INTO @saved;

            COMMIT TRAN;
            SELECT TOP 1 farmRatingId FROM @saved;";

        await using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@bookingId", bookingId);
        cmd.Parameters.AddWithValue("@raterUserId", raterUserId);
        cmd.Parameters.AddWithValue("@stars", stars);
        cmd.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);

        var scalar = await cmd.ExecuteScalarAsync();
        int resultCode = scalar == null || scalar == DBNull.Value ? 0 : Convert.ToInt32(scalar);

        return resultCode switch
        {
            -1 => new SaveFarmRatingResult { Status = SaveFarmRatingStatus.BookingNotFound },
            -2 => new SaveFarmRatingResult { Status = SaveFarmRatingStatus.Forbidden },
            -3 => new SaveFarmRatingResult { Status = SaveFarmRatingStatus.FarmNotFound },
            -4 => new SaveFarmRatingResult { Status = SaveFarmRatingStatus.BookingNotEligible },
            > 0 => new SaveFarmRatingResult
            {
                Status = SaveFarmRatingStatus.Success,
                RatingId = resultCode,
                FarmId = await GetFarmIdForRating(conn, resultCode)
            },
            _ => new SaveFarmRatingResult { Status = SaveFarmRatingStatus.BookingNotFound }
        };
    }

    public virtual async Task<FarmRatingSummary?> GetFarmRatingSummary(int farmId)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = @"
            IF NOT EXISTS (SELECT 1 FROM farm WHERE farmId = @farmId)
            BEGIN
                SELECT CAST(NULL AS DECIMAL(4,2)), CAST(NULL AS INT);
                RETURN;
            END;

            SELECT AVG(CAST(stars AS DECIMAL(4,2))), COUNT(*)
            FROM farmRatings
            WHERE farmId = @farmId;";

        await using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@farmId", farmId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.IsDBNull(1))
        {
            return null;
        }

        return new FarmRatingSummary
        {
            FarmId = farmId,
            AverageStars = reader.IsDBNull(0) ? null : reader.GetDecimal(0),
            ReviewCount = reader.GetInt32(1)
        };
    }

    private static async Task<int> GetFarmIdForRating(SqlConnection conn, int ratingId)
    {
        const string query = "SELECT farmId FROM farmRatings WHERE farmRatingId = @ratingId";
        await using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@ratingId", ratingId);
        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }
}
