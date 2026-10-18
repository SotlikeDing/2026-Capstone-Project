using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// The full booking lifecycle: creation, browse-by-role reads, the PJ
/// approve/reject/staff-assignment flow, PH/VG accept-or-decline of a staff
/// assignment (which finalizes the booking once every assigned role has
/// accepted), payment status (idempotent), post-booking corrections, and audit
/// history. See BookingsController for the HTTP-facing rules layered on top.
/// </summary>
public class Bookings_db
{
    private readonly string _connectionString;
    public Bookings_db(string connectionString) => _connectionString = connectionString;

    private static bool _columnsEnsured = false;

    /// <summary>Adds any columns this DB predates (rejection reason/notes, pricing, species JSON, etc.) on first use.</summary>
    private async Task EnsureColumnsAsync(SqlConnection conn, SqlTransaction? tx = null)
    {
        if (_columnsEnsured) return;
        try
        {
            string ensureQ = @"
                IF COL_LENGTH('bookings', 'bookRejectionReason') IS NULL
                    ALTER TABLE bookings ADD bookRejectionReason NVARCHAR(255) NULL;
                IF COL_LENGTH('bookings', 'bookRejectionNotes') IS NULL
                    ALTER TABLE bookings ADD bookRejectionNotes NVARCHAR(MAX) NULL;
                IF COL_LENGTH('bookings', 'bookFirearmOption') IS NULL
                    ALTER TABLE bookings ADD bookFirearmOption NVARCHAR(100) NULL;
                IF COL_LENGTH('bookings', 'bookSpecialNotes') IS NULL
                    ALTER TABLE bookings ADD bookSpecialNotes NVARCHAR(MAX) NULL;
                IF COL_LENGTH('bookings', 'bookWantsVideographer') IS NULL
                    ALTER TABLE bookings ADD bookWantsVideographer BIT NULL;
                IF COL_LENGTH('bookings', 'bookPaymentReference') IS NULL
                    ALTER TABLE bookings ADD bookPaymentReference NVARCHAR(100) NULL;
                IF COL_LENGTH('bookings', 'bookTotalPrice') IS NULL
                    ALTER TABLE bookings ADD bookTotalPrice DECIMAL(18, 2) NULL;
                IF COL_LENGTH('bookings', 'bookAccommodationPrice') IS NULL
                    ALTER TABLE bookings ADD bookAccommodationPrice DECIMAL(18, 2) NULL;
                IF COL_LENGTH('bookings', 'bookPriceBreakdown') IS NULL
                    ALTER TABLE bookings ADD bookPriceBreakdown NVARCHAR(MAX) NULL;
                IF COL_LENGTH('bookings', 'bookSpecies') IS NULL
                    ALTER TABLE bookings ADD bookSpecies NVARCHAR(MAX) NULL;
                IF COL_LENGTH('bookings', 'bookPaidAt') IS NULL
                    ALTER TABLE bookings ADD bookPaidAt DATETIME2 NULL;
            ";
            using var cmd = new SqlCommand(ensureQ, conn, tx);
            await cmd.ExecuteNonQueryAsync();
            _columnsEnsured = true;
        }
        catch { }
    }

    /// <summary>Parses bookSpecies (JSON array or comma-separated string, for backward compatibility) into a plain list.</summary>
    private static List<string> ParseSpecies(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        if (raw.TrimStart().StartsWith("["))
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw);
                if (list != null) return list;
            }
            catch { }
        }
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    // ── Full booking rows ────────────────────────────────────────────────────
    // One column list, one set of joins and one reader for every query that returns a
    // complete BookingResponse (PJ list, admin list, a hunter's list, one booking), so a
    // new column is added in one place instead of four.

    private const string FullBookingSelect = @"
        SELECT b.bookingId, b.bookStartDate, b.bookEndDate, b.bookStatus, b.bookHuntGuests, b.bookNonHuntGuests,
               u.uName, u.uSurname, b.bookPackageId, p.pTitle,
               ISNULL(u.uEmail, '') AS hunterEmail, ISNULL(u.uPhone, '') AS hunterPhone,
               uPh.userId AS phUserId, ISNULL(uPh.uName + ' ' + uPh.uSurname, '') AS phName,
               uVg.userId AS vgUserId, ISNULL(uVg.uName + ' ' + uVg.uSurname, '') AS vgName,
               ISNULL(f.farmName, '') AS farmName, ISNULL(a.addressRegion, 'Limpopo') AS province,
               COALESCE(b.bookTotalPrice, (ISNULL(p.pStockPrice, 0) * ISNULL(b.bookHuntGuests, 1))) AS totalAmount,
               ISNULL(b.bookPaymentStatus, 'Pending') AS paymentStatus,
               ISNULL(u.userId, 0) AS hunterUserId,
               ISNULL(b.bookPaymentReference, 'MP-EXP-' + CAST(b.bookingId AS VARCHAR)) AS paymentRef,
               b.bookRejectionReason, b.bookRejectionNotes, b.bookFirearmOption, b.bookSpecialNotes,
               p.pFarmId AS farmId, b.bookAccommodationPrice, b.bookPriceBreakdown,
               ISNULL(b.bookWantsVideographer, 0) AS wantsVideographer, b.bookSpecies,
               uPj.userId AS pjUserId, ISNULL(uPj.uName + ' ' + uPj.uSurname, '') AS pjName,
               phAsg.status, phAsg.declineReason, vgAsg.status, vgAsg.declineReason
        FROM bookings b
        JOIN packages p ON b.bookPackageId = p.packageId
        JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
        JOIN users u ON urH.urUserId = u.userId
        LEFT JOIN farm f ON p.pFarmId = f.farmId
        LEFT JOIN addresses a ON f.farmAddressId = a.addressId
        LEFT JOIN ph ON b.bookPhId = ph.phId
        LEFT JOIN userRoles urPh ON ph.phUserRoleId = urPh.userRoleId
        LEFT JOIN users uPh ON urPh.urUserId = uPh.userId
        LEFT JOIN vg ON b.bookVgId = vg.vgId
        LEFT JOIN userRoles urVg ON vg.vgUserRoleId = urVg.userRoleId
        LEFT JOIN users uVg ON urVg.urUserId = uVg.userId
        LEFT JOIN pj ON p.pPjId = pj.pjId
        LEFT JOIN userRoles urPj ON pj.pjUserRoleId = urPj.userRoleId
        LEFT JOIN users uPj ON urPj.urUserId = uPj.userId
        OUTER APPLY (SELECT TOP 1 status, declineReason FROM bookingStaffAssignments WHERE bookingId = b.bookingId AND staffRole = 'PH' ORDER BY assignmentId DESC) phAsg
        OUTER APPLY (SELECT TOP 1 status, declineReason FROM bookingStaffAssignments WHERE bookingId = b.bookingId AND staffRole = 'VG' ORDER BY assignmentId DESC) vgAsg";

    private static string? NullIfBlank(SqlDataReader r, int i) => r.IsDBNull(i) || string.IsNullOrWhiteSpace(r.GetString(i)) ? null : r.GetString(i);

    /// <summary>Maps one row of <see cref="FullBookingSelect"/>.</summary>
    private static BookingResponse ReadFullBooking(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        StartDate = r.GetDateTime(1),
        EndDate = r.GetDateTime(2),
        Status = r.GetString(3),
        HuntGuests = r.IsDBNull(4) ? 1 : Convert.ToInt32(r.GetValue(4)),
        NonHuntGuests = r.IsDBNull(5) ? 0 : Convert.ToInt32(r.GetValue(5)),
        UserName = $"{r.GetString(6)} {r.GetString(7)}",
        PakketId = r.GetInt32(8),
        PakketName = r.IsDBNull(9) ? "" : r.GetString(9),
        HunterEmail = r.GetString(10),
        HunterPhone = r.GetString(11),
        AssignedPhUserId = r.IsDBNull(12) ? null : r.GetInt32(12),
        AssignedPhName = NullIfBlank(r, 13),
        AssignedVgUserId = r.IsDBNull(14) ? null : r.GetInt32(14),
        AssignedVgName = NullIfBlank(r, 15),
        FarmName = r.GetString(16),
        Province = r.GetString(17),
        TotalAmount = r.GetDecimal(18),
        PaymentStatus = r.GetString(19),
        UserId = r.GetInt32(20),
        PaymentReference = r.IsDBNull(21) ? null : r.GetString(21),
        RejectionReason = r.IsDBNull(22) ? null : r.GetString(22),
        RejectionNotes = r.IsDBNull(23) ? null : r.GetString(23),
        FirearmOption = r.IsDBNull(24) ? null : r.GetString(24),
        SpecialNotes = r.IsDBNull(25) ? null : r.GetString(25),
        FarmId = r.IsDBNull(26) ? null : r.GetInt32(26),
        ConfirmedAccommodationPrice = r.IsDBNull(27) ? null : r.GetDecimal(27),
        PriceBreakdownJson = r.IsDBNull(28) ? null : r.GetString(28),
        WantsVideographer = !r.IsDBNull(29) && Convert.ToBoolean(r.GetValue(29)),
        BookedSpecies = ParseSpecies(r.IsDBNull(30) ? null : r.GetString(30)),
        PjUserId = r.IsDBNull(31) ? null : r.GetInt32(31),
        PjName = NullIfBlank(r, 32),
        PhAssignmentStatus = r.IsDBNull(33) ? null : r.GetString(33),
        PhDeclineReason = r.IsDBNull(34) ? null : r.GetString(34),
        VgAssignmentStatus = r.IsDBNull(35) ? null : r.GetString(35),
        VgDeclineReason = r.IsDBNull(36) ? null : r.GetString(36),
    };

    /// <summary>Runs <see cref="FullBookingSelect"/> with the given WHERE/ORDER BY tail and an optional @id parameter.</summary>
    private async Task<List<BookingResponse>> QueryFullBookingsAsync(string tail, int? id = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);
        using var cmd = new SqlCommand(FullBookingSelect + "\n" + tail, conn);
        if (id.HasValue) cmd.Parameters.AddWithValue("@id", id.Value);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<BookingResponse>();
        while (await r.ReadAsync()) list.Add(ReadFullBooking(r));
        return list;
    }

    /// <summary>Lists bookings for a PJ's packages.</summary>
    public virtual Task<List<BookingResponse>> GetBookings(int id)
        => QueryFullBookingsAsync(@"WHERE p.pPjId IN (SELECT pj2.pjId FROM pj pj2 JOIN userRoles ur ON pj2.pjUserRoleId = ur.userRoleId WHERE ur.urUserId = @id)
                                    ORDER BY b.bookingId DESC", id);

    /// <summary>Lists every booking in the system, for admin review/dispute resolution.</summary>
    public virtual Task<List<BookingResponse>> GetAllBookingsForAdmin()
        => QueryFullBookingsAsync("ORDER BY b.bookingId DESC");

    /// <summary>Lists bookings a PH is (or was) assigned to.</summary>
    public virtual async Task<List<BookingResponse>> GetPhBookings(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT b.bookingId, b.bookStartDate, b.bookEndDate, b.bookStatus, b.bookHuntGuests, b.bookNonHuntGuests, u.uName, u.uSurname, b.bookPackageId, p.pTitle, pjUser.userId, pjUser.uName, pjUser.uSurname,
                            phAsg.status, phAsg.declineReason
                     FROM bookings b
                     LEFT JOIN ph ON b.bookPhId = ph.phId
                     LEFT JOIN userRoles ur ON ph.phUserRoleId = ur.userRoleId
                     JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
                     JOIN users u ON urH.urUserId = u.userId
                     LEFT JOIN packages p ON b.bookPackageId = p.packageId
                     LEFT JOIN pj pj1 ON p.pPjId = pj1.pjId
                     LEFT JOIN userRoles urPj1 ON pj1.pjUserRoleId = urPj1.userRoleId
                     LEFT JOIN users pjUser ON pjUser.userId = urPj1.urUserId
                     OUTER APPLY (SELECT TOP 1 status, declineReason FROM bookingStaffAssignments WHERE bookingId = b.bookingId AND staffRole = 'PH' ORDER BY assignmentId DESC) phAsg
                     WHERE ur.urUserId = @id";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", userId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<BookingResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new BookingResponse
            {
                Id = r.GetInt32(0),
                StartDate = r.GetDateTime(1),
                EndDate = r.GetDateTime(2),
                Status = r.GetString(3),
                HuntGuests = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4)),
                NonHuntGuests = r.IsDBNull(5) ? 0 : Convert.ToInt32(r.GetValue(5)),
                UserName = $"{r.GetString(6)} {r.GetString(7)}",
                PakketId = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                PakketName = r.IsDBNull(9) ? "" : r.GetString(9),
                PjUserId = r.IsDBNull(10) ? 1 : r.GetInt32(10),
                PjName = r.IsDBNull(11) ? "Hendrik Viljoen (PJ)" : $"{r.GetString(11)} {r.GetString(12)}",
                TotalAmount = 0,
                PaymentStatus = "Pending",
                PhAssignmentStatus = r.IsDBNull(13) ? null : r.GetString(13),
                PhDeclineReason = r.IsDBNull(14) ? null : r.GetString(14)
            });
        }
        return list;
    }

    /// <summary>Lists bookings a VG is (or was) assigned to.</summary>
    public virtual async Task<List<BookingResponse>> GetVgBookings(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT b.bookingId, b.bookStartDate, b.bookEndDate, b.bookStatus, b.bookHuntGuests, b.bookNonHuntGuests, u.uName, u.uSurname, b.bookPackageId, p.pTitle,
                            vgAsg.status, vgAsg.declineReason
                     FROM bookings b
                     JOIN vg ON b.bookVgId = vg.vgId
                     JOIN userRoles ur ON vg.vgUserRoleId = ur.userRoleId
                     JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
                     JOIN users u ON urH.urUserId = u.userId
                     LEFT JOIN packages p ON b.bookPackageId = p.packageId
                     OUTER APPLY (SELECT TOP 1 status, declineReason FROM bookingStaffAssignments WHERE bookingId = b.bookingId AND staffRole = 'VG' ORDER BY assignmentId DESC) vgAsg
                     WHERE ur.urUserId = @id";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", userId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<BookingResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new BookingResponse
            {
                Id = r.GetInt32(0),
                StartDate = r.GetDateTime(1),
                EndDate = r.GetDateTime(2),
                Status = r.GetString(3),
                HuntGuests = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4)),
                NonHuntGuests = r.IsDBNull(5) ? 0 : Convert.ToInt32(r.GetValue(5)),
                UserName = $"{r.GetString(6)} {r.GetString(7)}",
                PakketId = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                PakketName = r.IsDBNull(9) ? "" : r.GetString(9), TotalAmount = 0, PaymentStatus = "Pending",
                VgAssignmentStatus = r.IsDBNull(10) ? null : r.GetString(10),
                VgDeclineReason = r.IsDBNull(11) ? null : r.GetString(11)
            });
        }
        return list;
    }

    /// <summary>Lists bookings against a specific farmer's farms.</summary>
    public virtual async Task<List<BookingResponse>> GetFarmBookings(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);
        string q = @"SELECT b.bookingId, b.bookStartDate, b.bookEndDate, b.bookStatus, b.bookHuntGuests, b.bookNonHuntGuests, u.uName, u.uSurname, b.bookPackageId, p.pTitle,
                            f.farmId, ISNULL(f.farmName, '') AS farmName,
                            COALESCE(b.bookTotalPrice, (ISNULL(p.pStockPrice, 0) * ISNULL(b.bookHuntGuests, 1))) AS totalAmount,
                            ISNULL(b.bookPaymentStatus, 'Pending') AS paymentStatus,
                            u.userId AS hunterUserId,
                            ISNULL(b.bookPaymentReference, 'MP-EXP-' + CAST(b.bookingId AS VARCHAR)) AS paymentRef
                     FROM bookings b
                     JOIN packages p ON b.bookPackageId = p.packageId
                     JOIN farm f ON p.pFarmId = f.farmId
                     JOIN farmer ON f.farmFarmerId = farmer.farmerId
                     JOIN userRoles ur ON farmer.fUserRoleId = ur.userRoleId
                     JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
                     JOIN users u ON urH.urUserId = u.userId
                     WHERE ur.urUserId = @id
                     ORDER BY b.bookingId DESC";
        
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", userId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<BookingResponse>();
        
        while (await r.ReadAsync())
        {
            list.Add(new BookingResponse
            {
                Id = r.GetInt32(0),
                StartDate = r.GetDateTime(1),
                EndDate = r.GetDateTime(2),
                Status = r.GetString(3),
                HuntGuests = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4)),
                NonHuntGuests = r.IsDBNull(5) ? 0 : Convert.ToInt32(r.GetValue(5)),
                UserName = $"{r.GetString(6)} {r.GetString(7)}",
                PakketId = r.GetInt32(8),
                PakketName = r.IsDBNull(9) ? "" : r.GetString(9),
                FarmId = r.GetInt32(10),
                FarmName = r.GetString(11),
                TotalAmount = r.GetDecimal(12),
                PaymentStatus = r.GetString(13),
                UserId = r.GetInt32(14),
                PaymentReference = r.GetString(15)
            });
        }
        return list;
    }

    /// <summary>Fetches full details for a single booking, including assigned staff and price breakdown.</summary>
    public virtual async Task<BookingResponse?> GetSpecificBooking(int id)
        => (await QueryFullBookingsAsync("WHERE b.bookingId = @id", id)).FirstOrDefault();

    /// <summary>Deletes a booking - ownership is enforced in the DELETE's own WHERE clause (only the hunter who owns it can delete it).</summary>
    /// <summary>
    /// The hunter cancels their own booking. Bookings are never hard-deleted (staff
    /// assignments, companions, ratings and media hang off them); an early-stage request
    /// (Pending / waiting for staff / rejected) is marked Cancelled. Once approved or
    /// paid, cancelling involves the PJ and refunds, so it is refused here.
    /// </summary>
    public virtual async Task<CancelBookingResult> DeleteBooking(int bookingId, int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        const string q = @"
            DECLARE @status NVARCHAR(100) = (
                SELECT b.bookStatus FROM bookings b
                JOIN userRoles ur ON ur.userRoleId = b.bookHunterId
                WHERE b.bookingId = @b AND ur.urUserId = @u);
            IF @status IS NULL SELECT 'NotOwner';
            ELSE IF @status IN ('Pending', 'PendingStaffAcceptance', 'Rejected', 'Cancelled')
            BEGIN
                UPDATE bookings SET bookStatus = 'Cancelled' WHERE bookingId = @b;
                UPDATE bookingStaffAssignments SET status = 'Superseded' WHERE bookingId = @b AND status = 'Pending';
                SELECT 'Cancelled';
            END
            ELSE SELECT 'NotCancellable';";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@b", bookingId);
        cmd.Parameters.AddWithValue("@u", userId);
        var result = (string?)await cmd.ExecuteScalarAsync();
        return Enum.TryParse<CancelBookingResult>(result, out var parsed) ? parsed : CancelBookingResult.NotOwner;
    }

    /// <summary>Lists a hunter's own bookings.</summary>
    public virtual Task<List<BookingResponse>> GetHunterBookings(int userId)
        => QueryFullBookingsAsync("WHERE urH.urUserId = @id ORDER BY b.bookingId DESC", userId);

    /// <summary>Resolves the userRoleId of the farm's farmer behind a package, or null if the package has no farm.</summary>
    public virtual async Task<int?> GetFarmerUserRoleIdForPackage(int packageId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT fa.fUserRoleId
                            FROM packages p
                            JOIN farm f ON p.pFarmId = f.farmId
                            JOIN farmer fa ON fa.farmerId = f.farmFarmerId
                            WHERE p.packageId = @packageId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@packageId", packageId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    /// <summary>
    /// Resolves the userRoleId of the package's PJ organiser, or null if none can be
    /// resolved to a real role (mirrors Package_db.RequiredPackageDocsApprovedPredicate:
    /// pPjId is a pj.pjId, resolved via pj1.pjUserRoleId).
    /// </summary>
    public virtual async Task<int?> GetPjUserRoleIdForPackage(int packageId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string q = @"SELECT ur1.userRoleId
                            FROM packages p
                            LEFT JOIN pj pj1 ON p.pPjId = pj1.pjId
                            LEFT JOIN userRoles ur1 ON pj1.pjUserRoleId = ur1.userRoleId
                            WHERE p.packageId = @packageId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@packageId", packageId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    /// <summary>
    /// Creates a new booking request in "Pending" status and stamps a generated
    /// payment reference (MP-EXP-000123 style). Note: totalAmount is accepted but
    /// never written anywhere - pricing is only set later, when the PJ confirms it
    /// via ApproveBookingWithStaff. userId is resolved to the caller's own Hunter
    /// userRoleId (throws if they don't have one) rather than trusted directly.
    /// </summary>
    public virtual async Task<int> CreateBooking(CreateBookingRequest request, int userId, int packageId, decimal totalAmount)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);
        
        string hq = "SELECT TOP 1 ur.userRoleId FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId WHERE ur.urUserId = @uid AND (r.roleCode = 'Hunter' OR r.roleCode = 'HUNTER' OR r.roleCode = 'Jagter' OR r.roleCode = 'jagter')";
        using var hCmd = new SqlCommand(hq, conn);
        hCmd.Parameters.AddWithValue("@uid", userId);
        var res = await hCmd.ExecuteScalarAsync();
        int actualHunterId = Convert.ToInt32(res);
        if (actualHunterId == 0)
            throw new InvalidOperationException($"Hunter role record not found for userId={userId}. Ensure the user has an active Hunter/Jagter role.");

        string q = @"INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookHuntGuests, bookNonHuntGuests, bookStatus, bookFirearmOption, bookSpecialNotes, bookWantsVideographer, bookSpecies) 
                     VALUES (@h, @p, @s, @e, @hg, @nhg, @st, @fo, @sn, @wv, @sp);
                     DECLARE @newId INT = SCOPE_IDENTITY();
                     UPDATE bookings SET bookPaymentReference = 'MP-EXP-' + RIGHT('000000' + CAST(@newId AS VARCHAR(6)), 6) WHERE bookingId = @newId;
                     SELECT @newId;";
        
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@h", actualHunterId);
        cmd.Parameters.AddWithValue("@p", packageId);
        cmd.Parameters.AddWithValue("@s", request.StartDate);
        cmd.Parameters.AddWithValue("@e", request.EndDate);
        cmd.Parameters.AddWithValue("@hg", request.HuntGuests);
        cmd.Parameters.AddWithValue("@nhg", request.NonHuntGuests);
        cmd.Parameters.AddWithValue("@st", "Pending");
        cmd.Parameters.AddWithValue("@fo", (object?)request.FirearmOption ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sn", (object?)request.SpecialNotes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wv", request.WantsVideographer);
        
        string? speciesStr = null;
        if (request.BookedSpecies != null && request.BookedSpecies.Count > 0)
            speciesStr = string.Join(", ", request.BookedSpecies);
        cmd.Parameters.AddWithValue("@sp", (object?)speciesStr ?? DBNull.Value);
        
        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    /// <summary>Lists all Pending booking requests across every PJ, for a global "requests" queue.</summary>
    public virtual async Task<List<BookingRequestModel>> GetBookingRequests()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT b.bookingId, b.bookHunterId, u.uName, u.uSurname, b.bookStartDate, b.bookStatus
                     FROM bookings b
                     JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
                     JOIN users u ON urH.urUserId = u.userId
                     WHERE b.bookStatus = 'Pending'";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<BookingRequestModel>();
        
        while (await r.ReadAsync())
        {
            list.Add(new BookingRequestModel
            {
                BookingId = r.GetInt32(0),
                RequestedByUserId = r.GetInt32(1),
                RequestedByName = $"{r.GetString(2)} {r.GetString(3)}",
                RequestDate = r.GetDateTime(4),
                RequestStatus = r.GetString(5)
            });
        }
        return list;
    }
    
    /// <summary>
    /// Legacy direct status setter (prefer ApproveBookingWithStaff/
    /// RespondToStaffAssignment for the normal approval flow, which also assigns
    /// staff). If a booking was Approved and is now being rejected/cancelled, its
    /// package slot is restored (and un-marked "Vol Bespreek"/fully booked if that
    /// was the reason it was full).
    /// </summary>
    public virtual async Task<bool> ApproveBooking(int id, string status)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            // Check previous status and package id
            string checkQ = "SELECT bookStatus, bookPackageId, ISNULL(bookHuntGuests, 1) FROM bookings WHERE bookingId = @id";
            string? prevStatus = null;
            int pkgId = 0;
            int guests = 1;
            using (var checkCmd = new SqlCommand(checkQ, conn, tx))
            {
                checkCmd.Parameters.AddWithValue("@id", id);
                using var r = await checkCmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    prevStatus = r.IsDBNull(0) ? null : r.GetString(0);
                    pkgId = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    guests = r.IsDBNull(2) ? 1 : Convert.ToInt32(r.GetValue(2));
                }
            }

            string q = "UPDATE bookings SET bookStatus = @s WHERE bookingId = @id";
            using (var cmd = new SqlCommand(q, conn, tx))
            {
                cmd.Parameters.AddWithValue("@s", status);
                cmd.Parameters.AddWithValue("@id", id);
                int rows = await cmd.ExecuteNonQueryAsync();
                if (rows == 0)
                {
                    tx.Rollback();
                    return false;
                }
            }

            // If the booking was previously Approved and is now being cancelled or rejected, restore package slots
            bool wasApproved = string.Equals(prevStatus, "Approved", StringComparison.OrdinalIgnoreCase);
            bool isNowCancelledOrRejected = string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(status, "Afgekeur", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(status, "Gekanselleer", StringComparison.OrdinalIgnoreCase);

            if (wasApproved && isNowCancelledOrRejected && pkgId > 0)
            {
                string restoreQ = @"UPDATE packages 
                                   SET pMaxParticipants = pMaxParticipants + @guests,
                                       pStatus = CASE WHEN pStatus = 'Vol Bespreek' THEN 'Active' ELSE pStatus END
                                   WHERE packageId = @pkgId";
                using (var restoreCmd = new SqlCommand(restoreQ, conn, tx))
                {
                    restoreCmd.Parameters.AddWithValue("@guests", guests);
                    restoreCmd.Parameters.AddWithValue("@pkgId", pkgId);
                    await restoreCmd.ExecuteNonQueryAsync();
                }
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

    /// <summary>
    /// Which of PH/VG has a real say before a booking counts as confirmed. A booking
    /// that has already reached one of these statuses is treated as locked-in: the PJ
    /// can still swap staff on it (existing behaviour, preserved below), but that swap
    /// takes effect immediately rather than going through the Pending-acceptance gate,
    /// since re-litigating an already-confirmed hunt isn't what this feature is for.
    /// </summary>
    private static readonly HashSet<string> LockedInBookingStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Approved", "Paid", "Completed"
    };

    /// <summary>Latest (by assignmentId) row for this booking+role - Superseded rows are
    /// only ever older than this, so "latest" is always "current" without needing to
    /// track it any other way.</summary>
    private static async Task<(int? AssigneeUserId, string? Status, string? DeclineReason)> GetCurrentStaffAssignmentAsync(SqlConnection conn, SqlTransaction tx, int bookingId, string role)
    {
        const string q = @"SELECT TOP 1 assigneeUserId, status, declineReason
                            FROM bookingStaffAssignments
                            WHERE bookingId = @bookingId AND staffRole = @role
                            ORDER BY assignmentId DESC";
        using var cmd = new SqlCommand(q, conn, tx);
        cmd.Parameters.AddWithValue("@bookingId", bookingId);
        cmd.Parameters.AddWithValue("@role", role);
        using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
            return (r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2));
        return (null, null, null);
    }

    /// <summary>
    /// Creates a fresh Pending assignment for (bookingId, role), superseding whatever
    /// non-terminal row was there before - unless the incoming assignee is literally
    /// the same person who already has a Pending/Accepted row, in which case this is a
    /// no-op (idempotent resubmission, e.g. PJ re-saves pricing without changing staff).
    /// Returns true if a new row was actually created (so the caller knows whether to
    /// send a fresh "please respond" notification).
    /// </summary>
    private async Task<bool> AssignStaffRoleAsync(SqlConnection conn, SqlTransaction tx, int bookingId, string role, int assigneeUserId, int assignedByUserId)
    {
        var current = await GetCurrentStaffAssignmentAsync(conn, tx, bookingId, role);
        bool unchanged = current.AssigneeUserId == assigneeUserId &&
                          (current.Status == "Pending" || current.Status == "Accepted");
        if (unchanged) return false;

        if (current.AssigneeUserId.HasValue && (current.Status == "Pending" || current.Status == "Accepted"))
        {
            const string supQ = @"UPDATE bookingStaffAssignments SET status = 'Superseded'
                                   WHERE bookingId = @bookingId AND staffRole = @role AND status IN ('Pending','Accepted')";
            using var supCmd = new SqlCommand(supQ, conn, tx);
            supCmd.Parameters.AddWithValue("@bookingId", bookingId);
            supCmd.Parameters.AddWithValue("@role", role);
            await supCmd.ExecuteNonQueryAsync();
        }

        const string insQ = @"INSERT INTO bookingStaffAssignments (bookingId, staffRole, assigneeUserId, assignedByUserId, status, assignedAt)
                               VALUES (@bookingId, @role, @assignee, @assignedBy, 'Pending', SYSUTCDATETIME())";
        using var insCmd = new SqlCommand(insQ, conn, tx);
        insCmd.Parameters.AddWithValue("@bookingId", bookingId);
        insCmd.Parameters.AddWithValue("@role", role);
        insCmd.Parameters.AddWithValue("@assignee", assigneeUserId);
        insCmd.Parameters.AddWithValue("@assignedBy", assignedByUserId);
        await insCmd.ExecuteNonQueryAsync();
        return true;
    }

    /// <summary>Clears whatever non-terminal assignment a role has (no replacement) -
    /// used when the PJ resubmits with e.g. no videographer requested anymore. Returns
    /// the userId that was cleared (so the caller can tell them they're no longer
    /// needed), or null if there was nothing non-terminal to clear.</summary>
    private async Task<int?> ClearStaffRoleAsync(SqlConnection conn, SqlTransaction tx, int bookingId, string role)
    {
        var current = await GetCurrentStaffAssignmentAsync(conn, tx, bookingId, role);
        if (!current.AssigneeUserId.HasValue || (current.Status != "Pending" && current.Status != "Accepted"))
            return null;

        const string supQ = @"UPDATE bookingStaffAssignments SET status = 'Superseded'
                               WHERE bookingId = @bookingId AND staffRole = @role AND status IN ('Pending','Accepted')";
        using var supCmd = new SqlCommand(supQ, conn, tx);
        supCmd.Parameters.AddWithValue("@bookingId", bookingId);
        supCmd.Parameters.AddWithValue("@role", role);
        await supCmd.ExecuteNonQueryAsync();
        return current.AssigneeUserId;
    }

    /// <summary>Whether userId actually holds the given role ("PH" or "VG") - guards
    /// against a stale/tampered client sending an arbitrary userId as the assignee,
    /// which would otherwise silently strand the booking (a ph/vg row would still get
    /// created for them, but they'd never see a "please respond" notification since
    /// they don't hold the role that dashboard filters on).</summary>
    public virtual async Task<bool> IsValidStaffAssignee(int userId, string role)
    {
        if (userId <= 0) return false;
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string roleFilter = string.Equals(role, "VG", StringComparison.OrdinalIgnoreCase)
            ? "(r.roleCode = 'VG' OR r.roleCode = 'Videograaf')"
            : "(r.roleCode = 'PH' OR r.roleCode = 'Professionele Jagter')";
        string q = $@"SELECT COUNT(1) FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId WHERE ur.urUserId = @userId AND {roleFilter}";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    /// <summary>
    /// PJ assigns PH (required) and VG (optional) to a booking and confirms
    /// pricing. If the booking is already locked in (Approved/Paid/Completed - see
    /// LockedInBookingStatuses), a staff swap takes effect immediately with no
    /// acceptance gate; otherwise each assignment goes through
    /// AssignStaffRoleAsync/ClearStaffRoleAsync's Pending-acceptance flow, and the
    /// booking itself stays wherever it was (this call alone does not approve
    /// it - see RespondToStaffAssignment for that).
    /// </summary>
    public virtual async Task<bool> ApproveBookingWithStaff(int bookingId, int phUserId, int? vgUserId, decimal? confirmedTotalPrice = null, decimal? accommodationPrice = null, string? priceBreakdownJson = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            // Read current status: a booking that's already locked in (Approved/Paid/
            // Completed) gets an immediate staff swap, same as always; anything earlier
            // than that goes through the new Pending-acceptance gate below.
            string currentStatus = "";
            string statusCheckQ = "SELECT bookStatus FROM bookings WHERE bookingId = @bookingId";
            using (var scCmd = new SqlCommand(statusCheckQ, conn, tx))
            {
                scCmd.Parameters.AddWithValue("@bookingId", bookingId);
                var statusRes = await scCmd.ExecuteScalarAsync();
                if (statusRes == null)
                {
                    tx.Rollback();
                    return false;
                }
                currentStatus = statusRes.ToString() ?? "";
            }
            bool wasAlreadyApproved = LockedInBookingStatuses.Contains(currentStatus);

            int? phId = null;
            if (phUserId > 0)
            {
                string phQ = @"DECLARE @phId INT = (SELECT TOP 1 ph.phId FROM ph JOIN userRoles ur ON ph.phUserRoleId = ur.userRoleId WHERE ur.urUserId = @phUserId);
                               IF @phId IS NULL
                               BEGIN
                                   DECLARE @phUrId INT = (SELECT TOP 1 userRoleId FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId WHERE ur.urUserId = @phUserId AND (r.roleCode = 'PH' OR r.roleCode = 'Professionele Jagter'));
                                   IF @phUrId IS NOT NULL
                                   BEGIN
                                       -- Daily rates left NULL (not the column DEFAULT 2800/4500):
                                       -- this PH hasn't configured tariffs yet, so the dashboard
                                       -- must prompt them rather than silently show a fake rate.
                                       INSERT INTO ph (phUserRoleId, phBio, phStandardDailyRate, phDangerousGameDailyRate) VALUES (@phUrId, 'MikPunt PH Gids', NULL, NULL);
                                       SET @phId = SCOPE_IDENTITY();
                                   END
                               END
                               SELECT @phId;";
                using var phCmd = new SqlCommand(phQ, conn, tx);
                phCmd.Parameters.AddWithValue("@phUserId", phUserId);
                var phRes = await phCmd.ExecuteScalarAsync();
                if (phRes != null && phRes != DBNull.Value) phId = Convert.ToInt32(phRes);
            }

            int? vgId = null;
            if (vgUserId.HasValue && vgUserId.Value > 0)
            {
                string vgQ = @"DECLARE @vgId INT = (SELECT TOP 1 vg.vgId FROM vg JOIN userRoles ur ON vg.vgUserRoleId = ur.userRoleId WHERE ur.urUserId = @vgUserId);
                               IF @vgId IS NULL
                               BEGIN
                                   DECLARE @vgUrId INT = (SELECT TOP 1 userRoleId FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId WHERE ur.urUserId = @vgUserId AND (r.roleCode = 'VG' OR r.roleCode = 'Videograaf'));
                                   IF @vgUrId IS NOT NULL
                                   BEGIN
                                       INSERT INTO vg (vgUserRoleId) VALUES (@vgUrId);
                                       SET @vgId = SCOPE_IDENTITY();
                                   END
                               END
                               SELECT @vgId;";
                using var vgCmd = new SqlCommand(vgQ, conn, tx);
                vgCmd.Parameters.AddWithValue("@vgUserId", vgUserId.Value);
                var vgRes = await vgCmd.ExecuteScalarAsync();
                if (vgRes != null && vgRes != DBNull.Value) vgId = Convert.ToInt32(vgRes);
            }

            string newBookStatus = wasAlreadyApproved ? currentStatus : "PendingStaffAcceptance";
            string updateBookingQ = @"UPDATE bookings
                                     SET bookStatus = @newStatus,
                                         bookPhId = @phId,
                                         bookVgId = @vgId,
                                         bookTotalPrice = COALESCE(@totalPrice, bookTotalPrice),
                                         bookAccommodationPrice = COALESCE(@accomPrice, bookAccommodationPrice),
                                         bookPriceBreakdown = COALESCE(@breakdown, bookPriceBreakdown)
                                     WHERE bookingId = @bookingId";
            using (var bCmd = new SqlCommand(updateBookingQ, conn, tx))
            {
                bCmd.Parameters.AddWithValue("@newStatus", newBookStatus);
                bCmd.Parameters.AddWithValue("@phId", phId ?? (object)DBNull.Value);
                bCmd.Parameters.AddWithValue("@vgId", vgId ?? (object)DBNull.Value);
                bCmd.Parameters.AddWithValue("@totalPrice", (object?)confirmedTotalPrice ?? DBNull.Value);
                bCmd.Parameters.AddWithValue("@accomPrice", (object?)accommodationPrice ?? DBNull.Value);
                bCmd.Parameters.AddWithValue("@breakdown", (object?)priceBreakdownJson ?? DBNull.Value);
                bCmd.Parameters.AddWithValue("@bookingId", bookingId);
                int rows = await bCmd.ExecuteNonQueryAsync();
                if (rows == 0)
                {
                    tx.Rollback();
                    return false;
                }
            }

            // Package slots are no longer decremented here: for a brand new assignment
            // the booking isn't confirmed until every assigned role accepts (see the
            // finalize step in RespondToStaffAssignment, which does the decrement
            // exactly once); for a swap on an already-locked-in booking, slots were
            // already decremented the first time round, so there's nothing to redo.

            // Fetch context needed for both the assignment rows (assignedByUserId) and notifications
            int ctxHunterUserId = 0, ctxPjUid = 0;
            string ctxPkgTitle = "", ctxPayRef = "", ctxPhName = "Professionele Jagter", ctxPjName = "MikPunt Uitruster", ctxHunterName = "";
            DateTime ctxStartD = default, ctxEndD = default;
            try
            {
                string notifInfoQ = @"SELECT urH.urUserId, p.pTitle, b.bookStartDate, b.bookEndDate, ISNULL(b.bookPaymentReference, 'MP-EXP-' + CAST(b.bookingId AS VARCHAR)),
                                             uPh.uName + ' ' + uPh.uSurname AS phName,
                                             ISNULL(uPj1.userId, 0) AS pjUserId,
                                             COALESCE(uPj1.uName + ' ' + uPj1.uSurname, 'MikPunt Uitruster') AS pjName,
                                             uH.uName + ' ' + uH.uSurname AS hunterName
                                      FROM bookings b
                                      JOIN packages p ON b.bookPackageId = p.packageId
                                      JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
                                      JOIN users uH ON urH.urUserId = uH.userId
                                      LEFT JOIN pj pj ON p.pPjId = pj.pjId
                                      LEFT JOIN userRoles urPj1 ON pj.pjUserRoleId = urPj1.userRoleId
                                      LEFT JOIN users uPj1 ON urPj1.urUserId = uPj1.userId
                                      LEFT JOIN users uPh ON uPh.userId = @phUserId
                                      WHERE b.bookingId = @bookingId";
                using var nInfoCmd = new SqlCommand(notifInfoQ, conn, tx);
                nInfoCmd.Parameters.AddWithValue("@phUserId", phUserId);
                nInfoCmd.Parameters.AddWithValue("@bookingId", bookingId);
                using var nr = await nInfoCmd.ExecuteReaderAsync();
                if (await nr.ReadAsync())
                {
                    ctxHunterUserId = nr.GetInt32(0);
                    ctxPkgTitle = nr.GetString(1);
                    ctxStartD = nr.GetDateTime(2);
                    ctxEndD = nr.GetDateTime(3);
                    ctxPayRef = nr.GetString(4);
                    ctxPhName = nr.IsDBNull(5) ? "Professionele Jagter" : nr.GetString(5);
                    ctxPjUid = nr.GetInt32(6);
                    ctxPjName = nr.GetString(7);
                    ctxHunterName = nr.GetString(8);
                }
            }
            catch { }

            string notifInsertQ = @"INSERT INTO userNotifications (recipientUserId, senderUserId, notificationType, title, message, actionUrl, isRead, createdTimestamp)
                                   VALUES (@recipientUserId, @senderUserId, @type, @title, @msg, @url, 0, GETDATE())";

            if (wasAlreadyApproved)
            {
                // Immediate-effect staff swap on an already-confirmed booking - preserves
                // the app's original one-shot behaviour exactly, no acceptance gate.
                try
                {
                    using (var insCmd = new SqlCommand(notifInsertQ, conn, tx))
                    {
                        insCmd.Parameters.AddWithValue("@recipientUserId", ctxHunterUserId);
                        insCmd.Parameters.AddWithValue("@senderUserId", ctxPjUid > 0 ? ctxPjUid : (object)DBNull.Value);
                        insCmd.Parameters.AddWithValue("@type", "BOOKING_APPROVED");
                        insCmd.Parameters.AddWithValue("@title", "Ekspedisie Goedgekeur! 🎯");
                        string priceText = confirmedTotalPrice.HasValue ? $" Bevestigde prys: $ {confirmedTotalPrice.Value:N2} USD." : "";
                        insCmd.Parameters.AddWithValue("@msg", $"U jagekspedisie vir '{ctxPkgTitle}' ({ctxStartD:dd MMM} - {ctxEndD:dd MMM yyyy}) is goedgekeur! Aangewese PH: {ctxPhName}.{priceText} Verwysing: {ctxPayRef}.");
                        insCmd.Parameters.AddWithValue("@url", "/jagter/besprekings");
                        await insCmd.ExecuteNonQueryAsync();
                    }

                    if (phUserId > 0 && phUserId != ctxPjUid)
                    {
                        using var phNotifCmd = new SqlCommand(notifInsertQ, conn, tx);
                        phNotifCmd.Parameters.AddWithValue("@recipientUserId", phUserId);
                        phNotifCmd.Parameters.AddWithValue("@senderUserId", ctxPjUid > 0 ? ctxPjUid : (object)DBNull.Value);
                        phNotifCmd.Parameters.AddWithValue("@type", "STAFF_ASSIGNMENT");
                        phNotifCmd.Parameters.AddWithValue("@title", "Nuwe Ekspedisie Toewysing (PH) 🎯");
                        phNotifCmd.Parameters.AddWithValue("@msg", $"U is deur {ctxPjName} as Professionele Jagter toegewys aan 'n ekspedisie vir {ctxHunterName} ({ctxStartD:dd MMM} - {ctxEndD:dd MMM yyyy}).");
                        phNotifCmd.Parameters.AddWithValue("@url", "/profiel");
                        await phNotifCmd.ExecuteNonQueryAsync();
                    }

                    if (vgUserId.HasValue && vgUserId.Value > 0 && vgUserId.Value != ctxPjUid)
                    {
                        using var vgNotifCmd = new SqlCommand(notifInsertQ, conn, tx);
                        vgNotifCmd.Parameters.AddWithValue("@recipientUserId", vgUserId.Value);
                        vgNotifCmd.Parameters.AddWithValue("@senderUserId", ctxPjUid > 0 ? ctxPjUid : (object)DBNull.Value);
                        vgNotifCmd.Parameters.AddWithValue("@type", "STAFF_ASSIGNMENT");
                        vgNotifCmd.Parameters.AddWithValue("@title", "Nuwe Ekspedisie Toewysing (VG) 🎥");
                        vgNotifCmd.Parameters.AddWithValue("@msg", $"U is deur {ctxPjName} as Videograaf toegewys aan 'n ekspedisie vir {ctxHunterName} ({ctxStartD:dd MMM} - {ctxEndD:dd MMM yyyy}).");
                        vgNotifCmd.Parameters.AddWithValue("@url", "/profiel");
                        await vgNotifCmd.ExecuteNonQueryAsync();
                    }
                }
                catch { }
            }
            else
            {
                // New assignment (or reassignment before anyone has accepted): create
                // Pending acceptance rows and ask PH/VG to respond, instead of assuming
                // approval. Idempotent - resubmitting the same PH/VG (e.g. just to
                // update pricing) doesn't spam a fresh notification.
                bool phIsNew = false, vgIsNew = false;
                int? vgClearedUserId = null;
                if (phId.HasValue)
                    phIsNew = await AssignStaffRoleAsync(conn, tx, bookingId, "PH", phUserId, ctxPjUid);

                if (vgId.HasValue)
                    vgIsNew = await AssignStaffRoleAsync(conn, tx, bookingId, "VG", vgUserId!.Value, ctxPjUid);
                else
                    vgClearedUserId = await ClearStaffRoleAsync(conn, tx, bookingId, "VG");

                try
                {
                    if (phIsNew && phUserId > 0 && phUserId != ctxPjUid)
                    {
                        using var phNotifCmd = new SqlCommand(notifInsertQ, conn, tx);
                        phNotifCmd.Parameters.AddWithValue("@recipientUserId", phUserId);
                        phNotifCmd.Parameters.AddWithValue("@senderUserId", ctxPjUid > 0 ? ctxPjUid : (object)DBNull.Value);
                        phNotifCmd.Parameters.AddWithValue("@type", "STAFF_ASSIGNMENT");
                        phNotifCmd.Parameters.AddWithValue("@title", "Nuwe Ekspedisie Toewysing (PH) - Aksie Nodig 🎯");
                        phNotifCmd.Parameters.AddWithValue("@msg", $"U is deur {ctxPjName} as Professionele Jagter toegewys aan 'n ekspedisie vir {ctxHunterName} ({ctxStartD:dd MMM} - {ctxEndD:dd MMM yyyy}). Aanvaar of wys asseblief van die hand op u paneelbord.");
                        phNotifCmd.Parameters.AddWithValue("@url", "/ph/paneelbord");
                        await phNotifCmd.ExecuteNonQueryAsync();
                    }

                    if (vgIsNew && vgUserId.HasValue && vgUserId.Value > 0 && vgUserId.Value != ctxPjUid)
                    {
                        using var vgNotifCmd = new SqlCommand(notifInsertQ, conn, tx);
                        vgNotifCmd.Parameters.AddWithValue("@recipientUserId", vgUserId.Value);
                        vgNotifCmd.Parameters.AddWithValue("@senderUserId", ctxPjUid > 0 ? ctxPjUid : (object)DBNull.Value);
                        vgNotifCmd.Parameters.AddWithValue("@type", "STAFF_ASSIGNMENT");
                        vgNotifCmd.Parameters.AddWithValue("@title", "Nuwe Ekspedisie Toewysing (VG) - Aksie Nodig 🎥");
                        vgNotifCmd.Parameters.AddWithValue("@msg", $"U is deur {ctxPjName} as Videograaf toegewys aan 'n ekspedisie vir {ctxHunterName} ({ctxStartD:dd MMM} - {ctxEndD:dd MMM yyyy}). Aanvaar of wys asseblief van die hand op u paneelbord.");
                        vgNotifCmd.Parameters.AddWithValue("@url", "/vg/paneelbord");
                        await vgNotifCmd.ExecuteNonQueryAsync();
                    }

                    if (vgClearedUserId.HasValue && vgClearedUserId.Value > 0 && vgClearedUserId.Value != ctxPjUid)
                    {
                        using var vgClearNotifCmd = new SqlCommand(notifInsertQ, conn, tx);
                        vgClearNotifCmd.Parameters.AddWithValue("@recipientUserId", vgClearedUserId.Value);
                        vgClearNotifCmd.Parameters.AddWithValue("@senderUserId", ctxPjUid > 0 ? ctxPjUid : (object)DBNull.Value);
                        vgClearNotifCmd.Parameters.AddWithValue("@type", "STAFF_ASSIGNMENT_REMOVED");
                        vgClearNotifCmd.Parameters.AddWithValue("@title", "Toewysing Teruggetrek");
                        vgClearNotifCmd.Parameters.AddWithValue("@msg", $"{ctxPjName} het u toewysing as Videograaf vir {ctxHunterName} se ekspedisie ({ctxStartD:dd MMM} - {ctxEndD:dd MMM yyyy}) teruggetrek - 'n videograaf word nie meer vir hierdie ekspedisie benodig nie.");
                        vgClearNotifCmd.Parameters.AddWithValue("@url", "/vg/paneelbord");
                        await vgClearNotifCmd.ExecuteNonQueryAsync();
                    }
                }
                catch { }

                if (phIsNew)
                    await LogAuditAsync(conn, tx, bookingId, "PH Toegewys (wag vir aanvaarding)", ctxPjUid, ctxPjName, ctxPhName);
                if (vgIsNew)
                    await LogAuditAsync(conn, tx, bookingId, "VG Toegewys (wag vir aanvaarding)", ctxPjUid, ctxPjName, "");
                if (vgClearedUserId.HasValue)
                    await LogAuditAsync(conn, tx, bookingId, "VG Toewysing Teruggetrek", ctxPjUid, ctxPjName, "");
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

    /// <summary>Outcome of RespondToStaffAssignment - NotFound covers "no Pending assignment exists"; Forbidden means the caller isn't the assignee.</summary>
    public enum StaffResponseResult { Ok, NotFound, Forbidden, InvalidRole }

    /// <summary>
    /// PH/VG accepts or declines the assignment the PJ gave them. Accepting flips the
    /// booking to Approved (with the usual slot-decrement + hunter notification) once
    /// every currently-assigned role has accepted; declining clears that role off the
    /// booking so the PJ dashboard shows it needs a new pick, and notifies the PJ.
    /// When the PH and VG respond at the same moment their transactions can deadlock
    /// (each reads the other's assignment row); SQL Server then aborts one of them with
    /// error 1205, which is retried here so neither user sees a failure.
    /// </summary>
    public virtual async Task<(StaffResponseResult Result, bool BookingNowApproved)> RespondToStaffAssignment(int bookingId, string role, int callerUserId, bool accept, string? declineReason)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await RespondToStaffAssignmentOnce(bookingId, role, callerUserId, accept, declineReason);
            }
            catch (SqlException ex) when (ex.Number == 1205 && attempt < maxAttempts)
            {
                // Deadlock victim: the whole transaction was rolled back, so it is safe to rerun.
                await Task.Delay(50 * attempt);
            }
        }
    }

    /// <summary>A single transactional attempt of RespondToStaffAssignment.</summary>
    private async Task<(StaffResponseResult Result, bool BookingNowApproved)> RespondToStaffAssignmentOnce(int bookingId, string role, int callerUserId, bool accept, string? declineReason)
    {
        if (string.Equals(role, "PH", StringComparison.OrdinalIgnoreCase))
            role = "PH";
        else if (string.Equals(role, "VG", StringComparison.OrdinalIgnoreCase))
            role = "VG";
        else
            return (StaffResponseResult.InvalidRole, false);

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            var current = await GetCurrentStaffAssignmentAsync(conn, tx, bookingId, role);
            if (current.AssigneeUserId == null || current.Status != "Pending")
            {
                tx.Rollback();
                return (StaffResponseResult.NotFound, false);
            }
            if (current.AssigneeUserId.Value != callerUserId)
            {
                tx.Rollback();
                return (StaffResponseResult.Forbidden, false);
            }

            string newAssignmentStatus = accept ? "Accepted" : "Declined";
            const string updQ = @"UPDATE bookingStaffAssignments
                                   SET status = @status, respondedAt = SYSUTCDATETIME(), declineReason = @reason
                                   WHERE bookingId = @bookingId AND staffRole = @role AND status = 'Pending'";
            using (var updCmd = new SqlCommand(updQ, conn, tx))
            {
                updCmd.Parameters.AddWithValue("@status", newAssignmentStatus);
                updCmd.Parameters.AddWithValue("@reason", (object?)declineReason ?? DBNull.Value);
                updCmd.Parameters.AddWithValue("@bookingId", bookingId);
                updCmd.Parameters.AddWithValue("@role", role);
                int rows = await updCmd.ExecuteNonQueryAsync();
                if (rows == 0)
                {
                    tx.Rollback();
                    return (StaffResponseResult.NotFound, false);
                }
            }

            int ctxHunterUserId = 0, ctxPjUid = 0;
            bool ctxWantsVideographer = false;
            string ctxPkgTitle = "", ctxPjName = "Uitruster", ctxHunterName = "";
            DateTime ctxStartD = default, ctxEndD = default;
            {
                const string ctxQ = @"SELECT p.pTitle, b.bookStartDate, b.bookEndDate, urH.urUserId,
                                              uH.uName + ' ' + uH.uSurname,
                                              ISNULL(uPj1.userId, 0),
                                              COALESCE(uPj1.uName + ' ' + uPj1.uSurname, 'Uitruster'),
                                              ISNULL(b.bookVgId, 0),
                                              ISNULL(b.bookWantsVideographer, 0)
                                       FROM bookings b
                                       JOIN packages p ON b.bookPackageId = p.packageId
                                       JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
                                       JOIN users uH ON urH.urUserId = uH.userId
                                       LEFT JOIN pj pj ON p.pPjId = pj.pjId
                                       LEFT JOIN userRoles urPj1 ON pj.pjUserRoleId = urPj1.userRoleId
                                       LEFT JOIN users uPj1 ON urPj1.urUserId = uPj1.userId
                                       WHERE b.bookingId = @bookingId";
                using var ctxCmd = new SqlCommand(ctxQ, conn, tx);
                ctxCmd.Parameters.AddWithValue("@bookingId", bookingId);
                using var cr = await ctxCmd.ExecuteReaderAsync();
                if (await cr.ReadAsync())
                {
                    ctxPkgTitle = cr.GetString(0);
                    ctxStartD = cr.GetDateTime(1);
                    ctxEndD = cr.GetDateTime(2);
                    ctxHunterUserId = cr.GetInt32(3);
                    ctxHunterName = cr.GetString(4);
                    ctxPjUid = cr.GetInt32(5);
                    ctxPjName = cr.GetString(6);
                    ctxWantsVideographer = !cr.IsDBNull(8) && Convert.ToBoolean(cr.GetValue(8));
                }
            }

            string actorLabel = role == "PH" ? "PH gids" : "Videograaf";
            const string notifInsertQ = @"INSERT INTO userNotifications (recipientUserId, senderUserId, notificationType, title, message, actionUrl, isRead, createdTimestamp)
                                         VALUES (@recipientUserId, @senderUserId, @type, @title, @msg, @url, 0, GETDATE())";

            if (!accept)
            {
                string clearCol = role == "PH" ? "bookPhId" : "bookVgId";
                using (var clrCmd = new SqlCommand($"UPDATE bookings SET {clearCol} = NULL WHERE bookingId = @bookingId", conn, tx))
                {
                    clrCmd.Parameters.AddWithValue("@bookingId", bookingId);
                    await clrCmd.ExecuteNonQueryAsync();
                }

                if (ctxPjUid > 0)
                {
                    try
                    {
                        using var nCmd = new SqlCommand(notifInsertQ, conn, tx);
                        nCmd.Parameters.AddWithValue("@recipientUserId", ctxPjUid);
                        nCmd.Parameters.AddWithValue("@senderUserId", callerUserId);
                        nCmd.Parameters.AddWithValue("@type", "STAFF_DECLINED");
                        nCmd.Parameters.AddWithValue("@title", $"Toewysing Van Hand Gewys ({role}) ⚠️");
                        string reasonText = string.IsNullOrWhiteSpace(declineReason) ? "" : $" Rede: {declineReason}.";
                        nCmd.Parameters.AddWithValue("@msg", $"Die {actorLabel} wat u vir {ctxHunterName} se ekspedisie ({ctxStartD:dd MMM} - {ctxEndD:dd MMM yyyy}) toegewys het, het die toewysing van hand gewys.{reasonText} Wys asseblief 'n ander {actorLabel} toe.");
                        nCmd.Parameters.AddWithValue("@url", "/pj/besprekings");
                        await nCmd.ExecuteNonQueryAsync();
                    }
                    catch { }
                }

                await LogAuditAsync(conn, tx, bookingId, $"Personeel Van Hand Gewys ({role})", callerUserId, actorLabel, declineReason ?? "");
                tx.Commit();
                return (StaffResponseResult.Ok, false);
            }

            // Accepted - check whether every currently-assigned role has now accepted.
            string? phStatus = role == "PH" ? "Accepted" : (await GetCurrentStaffAssignmentAsync(conn, tx, bookingId, "PH")).Status;
            // The hunter's original request (bookWantsVideographer) - not merely whether a
            // VG happens to be assigned right now - decides whether a VG must accept before
            // auto-approving. Otherwise a declined-and-not-yet-reassigned VG would let the
            // booking finalize without one, even though the hunter explicitly asked for one.
            bool vgRequired = ctxWantsVideographer;
            string? vgStatus = !vgRequired ? null : (role == "VG" ? "Accepted" : (await GetCurrentStaffAssignmentAsync(conn, tx, bookingId, "VG")).Status);
            bool everyoneAccepted = phStatus == "Accepted" && (!vgRequired || vgStatus == "Accepted");

            await LogAuditAsync(conn, tx, bookingId, $"Personeel Aanvaar ({role})", callerUserId, actorLabel, "");

            if (!everyoneAccepted)
            {
                if (ctxPjUid > 0)
                {
                    try
                    {
                        using var nCmd = new SqlCommand(notifInsertQ, conn, tx);
                        nCmd.Parameters.AddWithValue("@recipientUserId", ctxPjUid);
                        nCmd.Parameters.AddWithValue("@senderUserId", callerUserId);
                        nCmd.Parameters.AddWithValue("@type", "STAFF_ACCEPTED");
                        nCmd.Parameters.AddWithValue("@title", $"Toewysing Aanvaar ({role}) ✓");
                        nCmd.Parameters.AddWithValue("@msg", $"Die {actorLabel} het die toewysing vir {ctxHunterName} se ekspedisie aanvaar. Nog wagtend op die ander toegewysde persoon.");
                        nCmd.Parameters.AddWithValue("@url", "/pj/besprekings");
                        await nCmd.ExecuteNonQueryAsync();
                    }
                    catch { }
                }
                tx.Commit();
                return (StaffResponseResult.Ok, false);
            }

            // Finalize: everyone has accepted - mirrors the original one-shot approval's
            // slot decrement + hunter notification, now run exactly once, right here.
            using (var fCmd = new SqlCommand("UPDATE bookings SET bookStatus = 'Approved' WHERE bookingId = @bookingId", conn, tx))
            {
                fCmd.Parameters.AddWithValue("@bookingId", bookingId);
                await fCmd.ExecuteNonQueryAsync();
            }

            string decrementQ = @"UPDATE p
                                 SET p.pMaxParticipants = CASE WHEN p.pMaxParticipants >= ISNULL(b.bookHuntGuests, 1) THEN p.pMaxParticipants - ISNULL(b.bookHuntGuests, 1) ELSE 0 END,
                                     p.pStatus = CASE WHEN (p.pMaxParticipants - ISNULL(b.bookHuntGuests, 1)) <= 0 THEN 'Vol Bespreek' ELSE p.pStatus END
                                 FROM packages p
                                 JOIN bookings b ON p.packageId = b.bookPackageId
                                 WHERE b.bookingId = @bookingId";
            using (var dCmd = new SqlCommand(decrementQ, conn, tx))
            {
                dCmd.Parameters.AddWithValue("@bookingId", bookingId);
                await dCmd.ExecuteNonQueryAsync();
            }

            if (ctxHunterUserId > 0)
            {
                try
                {
                    using var nCmd = new SqlCommand(notifInsertQ, conn, tx);
                    nCmd.Parameters.AddWithValue("@recipientUserId", ctxHunterUserId);
                    nCmd.Parameters.AddWithValue("@senderUserId", ctxPjUid > 0 ? ctxPjUid : (object)DBNull.Value);
                    nCmd.Parameters.AddWithValue("@type", "BOOKING_APPROVED");
                    nCmd.Parameters.AddWithValue("@title", "Ekspedisie Goedgekeur! 🎯");
                    nCmd.Parameters.AddWithValue("@msg", $"U jagekspedisie vir '{ctxPkgTitle}' ({ctxStartD:dd MMM} - {ctxEndD:dd MMM yyyy}) is goedgekeur! U toegewysde personeel het almal aanvaar.");
                    nCmd.Parameters.AddWithValue("@url", "/jagter/besprekings");
                    await nCmd.ExecuteNonQueryAsync();
                }
                catch { }
            }

            await LogAuditAsync(conn, tx, bookingId, "Bespreking Outomaties Goedgekeur", 0, "Stelsel", "Alle toegewysde personeel het aanvaar.");

            tx.Commit();
            return (StaffResponseResult.Ok, true);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Rejects a booking with an optional reason/notes, restoring the package slot if it had already been Approved (same logic as ApproveBooking's rollback path).</summary>
    public virtual async Task<bool> RejectBookingWithReason(int bookingId, string? reason, string? notes)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);
        using var tx = conn.BeginTransaction();
        try
        {
            string checkQ = @"SELECT b.bookStatus, b.bookPackageId, ISNULL(b.bookHuntGuests, 1),
                                     urH.urUserId, p.pTitle, b.bookStartDate, b.bookEndDate,
                                     ISNULL(uPj1.userId, 0) AS pjUserId
                              FROM bookings b
                              JOIN packages p ON b.bookPackageId = p.packageId
                              JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
                              LEFT JOIN pj pj ON p.pPjId = pj.pjId
                              LEFT JOIN userRoles urPj1 ON pj.pjUserRoleId = urPj1.userRoleId
                              LEFT JOIN users uPj1 ON urPj1.urUserId = uPj1.userId
                              WHERE b.bookingId = @id";
            string? prevStatus = null;
            int pkgId = 0;
            int guests = 1;
            int hunterUserId = 0;
            string pkgTitle = "Jagekspedisie";
            int pjUid = 0;

            using (var checkCmd = new SqlCommand(checkQ, conn, tx))
            {
                checkCmd.Parameters.AddWithValue("@id", bookingId);
                using var r = await checkCmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    prevStatus = r.IsDBNull(0) ? null : r.GetString(0);
                    pkgId = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    guests = r.IsDBNull(2) ? 1 : Convert.ToInt32(r.GetValue(2));
                    hunterUserId = r.GetInt32(3);
                    pkgTitle = r.IsDBNull(4) ? "Jagekspedisie" : r.GetString(4);
                    pjUid = r.IsDBNull(7) ? 0 : r.GetInt32(7);
                }
            }

            string q = @"UPDATE bookings 
                         SET bookStatus = 'Rejected', 
                             bookRejectionReason = @reason, 
                             bookRejectionNotes = @notes 
                         WHERE bookingId = @id";
            using (var cmd = new SqlCommand(q, conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", bookingId);
                cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
                int rows = await cmd.ExecuteNonQueryAsync();
                if (rows == 0)
                {
                    tx.Rollback();
                    return false;
                }
            }

            // Restore package slots if previously Approved
            bool wasApproved = string.Equals(prevStatus, "Approved", StringComparison.OrdinalIgnoreCase);
            if (wasApproved && pkgId > 0)
            {
                string restoreQ = @"UPDATE packages 
                                   SET pMaxParticipants = pMaxParticipants + @guests,
                                       pStatus = CASE WHEN pStatus = 'Vol Bespreek' THEN 'Active' ELSE pStatus END
                                   WHERE packageId = @pkgId";
                using var restoreCmd = new SqlCommand(restoreQ, conn, tx);
                restoreCmd.Parameters.AddWithValue("@guests", guests);
                restoreCmd.Parameters.AddWithValue("@pkgId", pkgId);
                await restoreCmd.ExecuteNonQueryAsync();
            }

            // Insert notification for hunter
            if (hunterUserId > 0)
            {
                try
                {
                    string notifInsertQ = @"INSERT INTO userNotifications (recipientUserId, senderUserId, notificationType, title, message, actionUrl, isRead, createdTimestamp)
                                           VALUES (@recipientUserId, @senderUserId, 'BOOKING_REJECTED', @title, @msg, '/jagter/besprekings', 0, GETDATE())";
                    using var insCmd = new SqlCommand(notifInsertQ, conn, tx);
                    insCmd.Parameters.AddWithValue("@recipientUserId", hunterUserId);
                    insCmd.Parameters.AddWithValue("@senderUserId", pjUid > 0 ? pjUid : (object)DBNull.Value);
                    insCmd.Parameters.AddWithValue("@title", "Besprekingsversoek van die hand gewys");
                    string reasonText = !string.IsNullOrWhiteSpace(reason) ? reason : "Onvoldoende beskikbaarheid";
                    string noteText = !string.IsNullOrWhiteSpace(notes) ? $" Notas van uitruster: {notes}" : "";
                    insCmd.Parameters.AddWithValue("@msg", $"U besprekingsversoek vir '{pkgTitle}' kon nie aanvaar word nie. Rede: {reasonText}.{noteText}");
                    await insCmd.ExecuteNonQueryAsync();
                }
                catch { }
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

    /// <summary>Outcome of SetPaymentStatus - AlreadyPaid signals the idempotent no-op path, distinct from a fresh Applied.</summary>
    public enum PaymentStatusResult { Applied, AlreadyPaid, NotFound }

    /// <summary>
    /// Records a booking's payment status. Idempotent for status="Paid": if the
    /// booking is already marked Paid this is a no-op that reports AlreadyPaid
    /// instead of silently "succeeding" again - closes the double-payment hole where
    /// a reloaded PayFast return URL, browser Back+Forward, a re-shared payment link,
    /// two open tabs, or a retried ITN webhook could each re-fire this call. The row
    /// is locked (UPDLOCK, ROWLOCK) for the whole check-then-act so two concurrent
    /// calls can't both observe "not yet paid" and both apply the payment - same
    /// intent as Rating_db.SetRoleRating's MERGE...WITH (HOLDLOCK) guard, adapted
    /// here since this is a plain state transition on an existing row rather than an
    /// upsert.
    /// </summary>
    public virtual Task<PaymentStatusResult> SetPaymentStatus(int id, string status)
        => SetPaymentStatus(id, status, null);

    /// <summary>As SetPaymentStatus(id, status), also storing the gateway's payment reference (pf_payment_id) when one is given.</summary>
    public virtual async Task<PaymentStatusResult> SetPaymentStatus(int id, string status, string? paymentReference)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        // Runs outside the transaction below deliberately: DDL is transactional in SQL
        // Server, so if this ran inside `tx` and a later call rolled that transaction
        // back (e.g. a NotFound booking id), the ALTER TABLE would be rolled back too
        // while the in-memory _columnsEnsured flag stayed permanently true - silently
        // "forgetting" the column exists for the rest of the process.
        await EnsureColumnsAsync(conn);
        using var tx = conn.BeginTransaction();
        try
        {
            string checkQ = "SELECT bookPaymentStatus FROM bookings WITH (UPDLOCK, ROWLOCK) WHERE bookingId = @id";
            string? currentPaymentStatus;
            using (var checkCmd = new SqlCommand(checkQ, conn, tx))
            {
                checkCmd.Parameters.AddWithValue("@id", id);
                var res = await checkCmd.ExecuteScalarAsync();
                if (res == null)
                {
                    tx.Rollback();
                    return PaymentStatusResult.NotFound;
                }
                currentPaymentStatus = res as string;
            }

            bool alreadyPaid = string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase)
                             && string.Equals(currentPaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase);
            if (alreadyPaid)
            {
                tx.Commit(); // release the row lock; nothing to change
                return PaymentStatusResult.AlreadyPaid;
            }

            string q = @"UPDATE bookings
                         SET bookPaymentStatus = @s,
                             bookStatus = CASE WHEN @s = 'Paid' AND bookStatus = 'Approved' THEN 'Paid' ELSE bookStatus END,
                             bookPaidAt = CASE WHEN @s = 'Paid' THEN SYSUTCDATETIME() ELSE bookPaidAt END,
                             bookPaymentReference = COALESCE(@ref, bookPaymentReference)
                         WHERE bookingId = @id";
            using (var cmd = new SqlCommand(q, conn, tx))
            {
                cmd.Parameters.AddWithValue("@s", status);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@ref", string.IsNullOrWhiteSpace(paymentReference) ? DBNull.Value : paymentReference.Trim());
                await cmd.ExecuteNonQueryAsync();
            }

            if (string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase))
                // PayFast always supplies its pf_payment_id; without one the status was set by hand in the admin console.
                await LogAuditAsync(conn, tx, id, "Betaling Ontvang", 0,
                    string.IsNullOrWhiteSpace(paymentReference) ? "Administrateur (handmatig)" : "Betalingstelsel (PayFast)",
                    string.IsNullOrWhiteSpace(paymentReference) ? "Deposito as betaal gemerk." : $"Deposito as betaal gemerk (verwysing {paymentReference.Trim()}).");

            tx.Commit();
            return PaymentStatusResult.Applied;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Partial update of a booking's details/status/pricing - only non-null request
    /// fields are written. Used for admin dispute corrections; note this does not
    /// itself write to bookingAuditLogs (see GetBookingAuditLogs) - AdminController
    /// records the correction separately in the general adminAuditLog instead.
    /// </summary>
    public virtual async Task<bool> UpdateBooking(int id, UpdateBookingRequest request)
    {
        var sets = new List<string>();
        var parameters = new List<(string, object)>();
        
        if (request.StartDate.HasValue)
        {
            sets.Add("bookStartDate = @startDate");
            parameters.Add(("@startDate", request.StartDate.Value));
        }
        if (request.EndDate.HasValue)
        {
            sets.Add("bookEndDate = @endDate");
            parameters.Add(("@endDate", request.EndDate.Value));
        }
        if (request.HuntGuests.HasValue)
        {
            sets.Add("bookHuntGuests = @huntGuests");
            parameters.Add(("@huntGuests", request.HuntGuests.Value));
        }
        if (request.NonHuntGuests.HasValue)
        {
            sets.Add("bookNonHuntGuests = @nonHuntGuests");
            parameters.Add(("@nonHuntGuests", request.NonHuntGuests.Value));
        }
        if (request.Status != null)
        {
            sets.Add("bookStatus = @status");
            parameters.Add(("@status", request.Status));
        }
        if (request.PaymentStatus != null)
        {
            sets.Add("bookPaymentStatus = @paymentStatus");
            parameters.Add(("@paymentStatus", request.PaymentStatus));
        }
        if (request.TotalPrice.HasValue)
        {
            sets.Add("bookTotalPrice = @totalPrice");
            parameters.Add(("@totalPrice", request.TotalPrice.Value));
        }
        if (request.AccommodationPrice.HasValue)
        {
            sets.Add("bookAccommodationPrice = @accommodationPrice");
            parameters.Add(("@accommodationPrice", request.AccommodationPrice.Value));
        }
        if (request.PriceBreakdownJson != null)
        {
            sets.Add("bookPriceBreakdown = @priceBreakdown");
            parameters.Add(("@priceBreakdown", request.PriceBreakdownJson));
        }

        if (sets.Count == 0) return false;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);

        string q = $"UPDATE bookings SET {string.Join(",", sets)} WHERE bookingId = @id";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", id);

        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Creates the bookingAuditLogs table on first use (in-code migration).</summary>
    private async Task EnsureAuditTableAsync(SqlConnection conn, SqlTransaction? tx = null)
    {
        string q = @"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'bookingAuditLogs')
                     BEGIN
                         CREATE TABLE bookingAuditLogs (
                             auditId INT IDENTITY(1,1) PRIMARY KEY,
                             bookingId INT NOT NULL,
                             actionType NVARCHAR(100) NOT NULL,
                             performedByUserId INT NOT NULL DEFAULT 0,
                             performedByName NVARCHAR(200) NULL,
                             details NVARCHAR(MAX) NULL,
                             createdAt DATETIME NOT NULL DEFAULT GETDATE()
                         );
                     END";
        using var cmd = new SqlCommand(q, conn, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Records one bookingAuditLogs entry on its own connection (e.g. an admin correction) - best-effort.</summary>
    public virtual async Task AddBookingAuditLog(int bookingId, string actionType, int performedByUserId, string performedByName, string details)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();
        await LogAuditAsync(conn, tx, bookingId, actionType, performedByUserId, performedByName, details);
        tx.Commit();
    }

    /// <summary>Records one bookingAuditLogs entry within the caller's transaction (used by the staff-assignment flow) - best-effort, failures are swallowed.</summary>
    public async Task LogAuditAsync(SqlConnection conn, SqlTransaction tx, int bookingId, string actionType, int performedByUserId, string performedByName, string details)
    {
        try
        {
            await EnsureAuditTableAsync(conn, tx);
            string q = @"INSERT INTO bookingAuditLogs (bookingId, actionType, performedByUserId, performedByName, details, createdAt)
                         VALUES (@bookingId, @actionType, @performedByUserId, @performedByName, @details, GETDATE())";
            using var cmd = new SqlCommand(q, conn, tx);
            cmd.Parameters.AddWithValue("@bookingId", bookingId);
            cmd.Parameters.AddWithValue("@actionType", actionType);
            cmd.Parameters.AddWithValue("@performedByUserId", performedByUserId);
            cmd.Parameters.AddWithValue("@performedByName", (object?)performedByName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@details", (object?)details ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    /// <summary>Change-history log for a booking (staff assignments/acceptances, etc. - see LogAuditAsync), newest first.</summary>
    public virtual async Task<List<BookingAuditLog>> GetBookingAuditLogs(int bookingId)
    {
        var list = new List<BookingAuditLog>();
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await EnsureAuditTableAsync(conn);

            string q = @"SELECT auditId, bookingId, actionType, performedByUserId, ISNULL(performedByName, 'Stelsel'), ISNULL(details, ''), createdAt
                         FROM bookingAuditLogs
                         WHERE bookingId = @bookingId
                         ORDER BY auditId DESC";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@bookingId", bookingId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new BookingAuditLog
                {
                    Id = r.GetInt32(0),
                    BookingId = r.GetInt32(1),
                    ActionType = r.GetString(2),
                    PerformedByUserId = r.GetInt32(3),
                    PerformedByName = r.GetString(4),
                    Details = r.GetString(5),
                    CreatedAt = r.GetDateTime(6)
                });
            }
        }
        catch { }
        return list;
    }

    /// <summary>
    /// Who was actually on a booking (as raw userIds, not userRoleId/phId/vgId) and
    /// its current status - used to validate a rating is for a real participant on a
    /// real, completed trip before it's accepted.
    /// </summary>
    public virtual async Task<BookingParticipantsDto> GetBookingParticipants(int bookingId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string q = @"SELECT b.bookStatus, urHunter.urUserId, urPh.urUserId, urVg.urUserId,
                            CAST(b.bookEndDate AS datetime2),
                            uHunter.uName + ' ' + uHunter.uSurname, uPh.uName + ' ' + uPh.uSurname, uVg.uName + ' ' + uVg.uSurname
                     FROM bookings b
                     LEFT JOIN userRoles urHunter ON b.bookHunterId = urHunter.userRoleId
                     LEFT JOIN users uHunter ON uHunter.userId = urHunter.urUserId
                     LEFT JOIN ph ON b.bookPhId = ph.phId
                     LEFT JOIN userRoles urPh ON ph.phUserRoleId = urPh.userRoleId
                     LEFT JOIN users uPh ON uPh.userId = urPh.urUserId
                     LEFT JOIN vg ON b.bookVgId = vg.vgId
                     LEFT JOIN userRoles urVg ON vg.vgUserRoleId = urVg.userRoleId
                     LEFT JOIN users uVg ON uVg.userId = urVg.urUserId
                     WHERE b.bookingId = @bookingId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@bookingId", bookingId);
        using var r = await cmd.ExecuteReaderAsync();

        if (!await r.ReadAsync())
        {
            return new BookingParticipantsDto { BookingExists = false };
        }

        return new BookingParticipantsDto
        {
            BookingExists = true,
            Status = r.IsDBNull(0) ? null : r.GetString(0),
            HunterUserId = r.IsDBNull(1) ? null : r.GetInt32(1),
            PhUserId = r.IsDBNull(2) ? null : r.GetInt32(2),
            VgUserId = r.IsDBNull(3) ? null : r.GetInt32(3),
            EndDate = r.IsDBNull(4) ? null : r.GetDateTime(4),
            HunterName = r.IsDBNull(5) ? null : r.GetString(5),
            PhName = r.IsDBNull(6) ? null : r.GetString(6),
            VgName = r.IsDBNull(7) ? null : r.GetString(7)
        };
    }

    /// <summary>
    /// Number of distinct completed trips a user took part in, in any of the three
    /// participant capacities (Hunter, PH guide, VG videographer). Used for the
    /// "Voltooide Jagte" stat on profile/dashboard pages - this is a fact about the
    /// bookings table, not derived from the ratings table.
    /// </summary>
    public virtual async Task<int> GetCompletedTripCount(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string q = @"SELECT COUNT(DISTINCT b.bookingId)
                     FROM bookings b
                     LEFT JOIN userRoles urHunter ON b.bookHunterId = urHunter.userRoleId
                     LEFT JOIN ph ON b.bookPhId = ph.phId
                     LEFT JOIN userRoles urPh ON ph.phUserRoleId = urPh.userRoleId
                     LEFT JOIN vg ON b.bookVgId = vg.vgId
                     LEFT JOIN userRoles urVg ON vg.vgUserRoleId = urVg.userRoleId
                     WHERE (b.bookStatus = 'Completed'
                            OR (b.bookStatus IN ('Approved', 'Paid') AND b.bookEndDate < CAST(DATEADD(hour, 2, SYSUTCDATETIME()) AS date)))
                       AND (urHunter.urUserId = @userId OR urPh.urUserId = @userId OR urVg.urUserId = @userId)";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }
}

public enum CancelBookingResult { NotOwner = 0, Cancelled, NotCancellable }
