using Microsoft.Data.SqlClient;
using MobileApi.Models;

namespace MobileApi.Database;

public class MobileBookings_db
{
    private readonly string _connectionString;

    public MobileBookings_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// The hunter's own bookings (they are the main hunter, bookHunterId) plus the bookings
    /// a main hunter added them to (bookingCompanions, Linked). No price fields either way.
    /// </summary>
    public async Task<List<MobileBookingResponse>> GetHunterBookings(int userRoleId)
    {
        var bookings = new List<MobileBookingResponse>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = @"
        WITH me AS (SELECT urUserId FROM userRoles WHERE userRoleId = @UserRoleId),
        mine AS (
            SELECT b.bookingId, CAST(1 AS BIT) AS isMain
            FROM bookings b
            WHERE b.bookHunterId = @UserRoleId
            UNION
            SELECT c.bookingId, CAST(0 AS BIT)
            FROM bookingCompanions c
            JOIN userRoles urC ON c.userRoleId = urC.userRoleId
            JOIN me ON urC.urUserId = me.urUserId
            WHERE c.status = 'Linked'
        )
        SELECT
            b.bookingId,
            COALESCE(p.pTitle, 'Bespreking') AS Title,
            COALESCE(f.farmName, 'Jagplaas') AS FarmName,
            LTRIM(RTRIM(CONCAT(
                COALESCE(a.addressCity, ''),
                CASE
                    WHEN a.addressCity IS NOT NULL AND a.addressRegion IS NOT NULL THEN ', '
                    ELSE ''
                END,
                COALESCE(a.addressRegion, '')
            ))) AS Location,
            b.bookStartDate,
            b.bookEndDate,
            COALESCE(b.bookStatus, 'Pending') AS Status,
            COALESCE(CONCAT(u.uName, ' ', u.uSurname), 'Jagter') AS HunterName,
            b.bookHuntGuests,
            b.bookNonHuntGuests,
            ownerRole.urUserId AS FarmOwnerUserId,
            f.farmId,
            farmSummary.AverageStars,
            farmSummary.ReviewCount,
            CAST(CASE
                WHEN m.isMain = 1
                     AND UPPER(LTRIM(RTRIM(COALESCE(b.bookStatus, '')))) IN ('COMPLETED', 'VOLTOOI') THEN 1
                WHEN m.isMain = 1
                     AND b.bookEndDate < CAST(GETDATE() AS DATE)
                     AND UPPER(LTRIM(RTRIM(COALESCE(b.bookStatus, '')))) NOT IN
                         ('REJECTED', 'AFGEKEUR', 'CANCELLED', 'CANCELED', 'GEKANSELLEER') THEN 1
                ELSE 0
            END AS BIT) AS CanRateFarm,
            myRating.stars AS MyFarmRatingStars,
            myRating.description AS MyFarmRatingDescription,
            m.isMain,
            COALESCE(b.bookStatus, '') AS RawStatus,
            (SELECT COUNT(*) FROM bookingCompanions c3 WHERE c3.bookingId = b.bookingId) AS CompanionCount
        FROM mine m
        JOIN bookings b ON b.bookingId = m.bookingId
        JOIN userRoles ur ON b.bookHunterId = ur.userRoleId
        LEFT JOIN users u ON ur.urUserId = u.userId
        LEFT JOIN packages p ON b.bookPackageId = p.packageId
        LEFT JOIN farm f ON p.pFarmId = f.farmId
        LEFT JOIN addresses a ON f.farmAddressId = a.addressId
        LEFT JOIN farmer fr ON f.farmFarmerId = fr.farmerId
        LEFT JOIN userRoles ownerRole ON fr.fUserRoleId = ownerRole.userRoleId
        OUTER APPLY (
            SELECT AVG(CAST(r.stars AS DECIMAL(4,2))) AS AverageStars,
                   COUNT(*) AS ReviewCount
            FROM farmRatings r
            WHERE r.farmId = f.farmId
        ) farmSummary
        LEFT JOIN farmRatings myRating
            ON myRating.bookingId = b.bookingId
            AND myRating.farmId = f.farmId
            AND myRating.raterUserId = (SELECT urUserId FROM me)
        ORDER BY b.bookStartDate DESC, b.bookingId DESC";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@UserRoleId", userRoleId);

        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            bookings.Add(new MobileBookingResponse
            {
                Id = reader.GetInt32(0),
                Title = reader.IsDBNull(1) ? "Bespreking" : reader.GetString(1),
                FarmName = reader.IsDBNull(2) ? "Jagplaas" : reader.GetString(2),
                Location = reader.IsDBNull(3) || string.IsNullOrWhiteSpace(reader.GetString(3))
                    ? "Ligging onbekend"
                    : reader.GetString(3),
                StartDate = reader.GetDateTime(4),
                EndDate = reader.GetDateTime(5),
                Status = FormatBookingStatus(reader.IsDBNull(6) ? null : reader.GetString(6)),
                HunterName = reader.IsDBNull(7) ? "Jagter" : reader.GetString(7),
                HuntGuests = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8)),
                NonHuntGuests = reader.IsDBNull(9) ? 0 : Convert.ToInt32(reader.GetValue(9)),
                FarmOwnerUserId = reader.IsDBNull(10) ? null : Convert.ToInt32(reader.GetValue(10)),
                FarmId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                FarmAverageRating = reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                FarmReviewCount = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
                CanRateFarm = !reader.IsDBNull(14) && reader.GetBoolean(14),
                MyFarmRatingStars = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                MyFarmRatingDescription = reader.IsDBNull(16) ? null : reader.GetString(16),
                IsMainHunter = reader.GetBoolean(17),
            });
            var booking = bookings[^1];
            booking.MaxHunters = Math.Max(1, booking.HuntGuests);
            booking.HunterCount = 1 + reader.GetInt32(19);
            booking.CanManageHunters = booking.IsMainHunter && BookingCompanionRules.IsConfirmed(reader.GetString(18));
        }

        return bookings;
    }

    private static string FormatBookingStatus(string? status)
    {
        var normalized = (status ?? "").Trim().ToLower().Replace(" ", "");

        if (normalized.Contains("completed") || normalized.Contains("voltooi"))
        {
            return "Voltooi";
        }

        if (normalized.Contains("goedgekeur") ||
            normalized.Contains("approved") ||
            normalized.Contains("confirmed") ||
            normalized.Contains("bevestig"))
        {
            return "Goedgekeur";
        }

        if (normalized.Contains("cancelled") ||
            normalized.Contains("canceled") ||
            normalized.Contains("gekanselleer"))
        {
            return "Gekanselleer";
        }

        if (normalized.Contains("afgekeur") ||
            normalized.Contains("rejected") ||
            normalized.Contains("declined"))
        {
            return "Afgekeur";
        }

        if (normalized.Contains("paid") || normalized.Contains("betaal"))
        {
            return "Betaal";
        }

        return "Wag Nog";
    }
}
