using Microsoft.Data.SqlClient;
using MobileApi.Models;

namespace MobileApi.Database;

/// <summary>
/// The other hunters on a booking (table bookingCompanions, see db/AddBookingCompanions.sql).
/// The main hunter stays bookings.bookHunterId and is the only one who pays; once the
/// booking is confirmed (Approved/Paid) they may add hunters up to the booking's hunter
/// count. Only Hunter accounts can be added; an email without an account becomes an
/// invite that links itself once that email is verified on a Hunter account. The
/// WebAPI's BookingCompanions_db is the original; this file is generated from it
/// (same SQL, same rules) minus the WebApp-only "trips I'm joining" query, because the
/// app lists those trips through MobileBookings_db.GetHunterBookings instead.
/// </summary>
public class MobileBookingCompanions_db
{
    private readonly string _connectionString;
    public MobileBookingCompanions_db(string connectionString) => _connectionString = connectionString;

    /// <summary>SQL predicate for a Hunter role row aliased r (role codes seen in this DB: Hunter, HUNTER, Jagter, jagter).</summary>
    private const string IsHunterRole = "UPPER(r.roleCode) IN ('HUNTER', 'JAGTER')";

    private sealed record BookingHead(string Status, int HuntGuests, int MainUserId, int MainUserRoleId, string MainName, string MainEmail, string Title);

    private static async Task<BookingHead?> ReadBookingHeadAsync(SqlConnection conn, SqlTransaction? tx, int bookingId, bool lockRow)
    {
        // UPDLOCK on the booking row serialises concurrent adds/removes for one booking,
        // so two quick adds can never both take the last open slot.
        string q = $@"SELECT ISNULL(b.bookStatus, ''), ISNULL(b.bookHuntGuests, 1), urH.urUserId, b.bookHunterId,
                             LTRIM(RTRIM(ISNULL(uH.uName, '') + ' ' + ISNULL(uH.uSurname, ''))),
                             LOWER(LTRIM(RTRIM(ISNULL(uH.uEmail, '')))), ISNULL(p.pTitle, '')
                      FROM bookings b {(lockRow ? "WITH (UPDLOCK, ROWLOCK)" : "")}
                      JOIN userRoles urH ON b.bookHunterId = urH.userRoleId
                      JOIN users uH ON urH.urUserId = uH.userId
                      LEFT JOIN packages p ON b.bookPackageId = p.packageId
                      WHERE b.bookingId = @id";
        using var cmd = new SqlCommand(q, conn, tx);
        cmd.Parameters.AddWithValue("@id", bookingId);
        using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new BookingHead(r.GetString(0), Convert.ToInt32(r.GetValue(1)), r.GetInt32(2), r.GetInt32(3), r.GetString(4), r.GetString(5), r.GetString(6));
    }

    /// <summary>
    /// The booking's hunters for the caller, or null when the booking does not exist.
    /// callerIsOnBooking tells the controller whether the caller is the main hunter or a
    /// linked companion (the controller also admits the booking's PJ/PH/VG/farmer).
    /// </summary>
    public virtual async Task<(BookingHuntersResponse? Roster, bool CallerIsOnBooking)> GetBookingHunters(int bookingId, int callerUserId, bool isAdmin)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        var head = await ReadBookingHeadAsync(conn, null, bookingId, lockRow: false);
        if (head == null) return (null, false);

        bool callerIsMain = head.MainUserId == callerUserId;
        bool showEmails = callerIsMain || isAdmin;
        var roster = new BookingHuntersResponse
        {
            BookingId = bookingId,
            BookingStatus = head.Status,
            MaxHunters = Math.Max(1, head.HuntGuests),
            CallerIsMainHunter = callerIsMain,
            CanManage = (callerIsMain || isAdmin) && BookingCompanionRules.IsConfirmed(head.Status),
        };
        roster.Hunters.Add(new BookingHunterDto
        {
            Name = head.MainName,
            Email = showEmails ? head.MainEmail : null,
            Status = "Main",
            IsMainHunter = true,
            IsYou = callerIsMain,
        });

        const string q = @"SELECT c.companionId, c.status, c.inviteName, c.inviteEmail, ur.urUserId,
                                  LTRIM(RTRIM(ISNULL(u.uName, '') + ' ' + ISNULL(u.uSurname, '')))
                           FROM bookingCompanions c
                           LEFT JOIN userRoles ur ON c.userRoleId = ur.userRoleId
                           LEFT JOIN users u ON ur.urUserId = u.userId
                           WHERE c.bookingId = @id
                           ORDER BY c.companionId";
        bool callerIsCompanion = false;
        using (var cmd = new SqlCommand(q, conn))
        {
            cmd.Parameters.AddWithValue("@id", bookingId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                int? userId = r.IsDBNull(4) ? null : r.GetInt32(4);
                string status = r.GetString(1);
                bool isYou = userId == callerUserId && status == "Linked";
                callerIsCompanion |= isYou;
                string accountName = r.IsDBNull(5) ? "" : r.GetString(5);
                roster.Hunters.Add(new BookingHunterDto
                {
                    CompanionId = r.GetInt32(0),
                    Status = status,
                    Name = string.IsNullOrWhiteSpace(accountName) ? r.GetString(2) : accountName,
                    Email = showEmails ? r.GetString(3) : null,
                    IsYou = isYou,
                });
            }
        }

        roster.OpenSlots = Math.Max(0, BookingCompanionRules.MaxCompanions(head.HuntGuests) - (roster.Hunters.Count - 1));
        return (roster, callerIsMain || callerIsCompanion);
    }

    /// <summary>
    /// Adds a hunter to a confirmed booking. An existing, verified Hunter account is
    /// linked at once; an unknown (or not yet verified) email becomes a Pending invite.
    /// An existing account without the Hunter role is refused: non-hunting people
    /// cannot be added.
    /// </summary>
    public virtual async Task<AddCompanionResult> AddCompanion(int bookingId, int callerUserId, bool isAdmin, string name, string email)
    {
        string normalizedEmail = BookingCompanionRules.NormalizeEmail(email);
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            var head = await ReadBookingHeadAsync(conn, tx, bookingId, lockRow: true);
            if (head == null) return await Done(new AddCompanionResult(AddCompanionOutcome.BookingNotFound));
            if (!isAdmin && head.MainUserId != callerUserId) return await Done(new AddCompanionResult(AddCompanionOutcome.NotMainHunter));
            if (!BookingCompanionRules.IsConfirmed(head.Status)) return await Done(new AddCompanionResult(AddCompanionOutcome.NotConfirmed));
            if (normalizedEmail == head.MainEmail) return await Done(new AddCompanionResult(AddCompanionOutcome.IsMainHunter));

            int taken;
            bool emailTaken;
            using (var cmd = new SqlCommand(@"SELECT COUNT(*), SUM(CASE WHEN inviteEmail = @email THEN 1 ELSE 0 END)
                                               FROM bookingCompanions WHERE bookingId = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", bookingId);
                cmd.Parameters.AddWithValue("@email", normalizedEmail);
                using var r = await cmd.ExecuteReaderAsync();
                await r.ReadAsync();
                taken = r.GetInt32(0);
                emailTaken = !r.IsDBNull(1) && r.GetInt32(1) > 0;
            }
            if (emailTaken) return await Done(new AddCompanionResult(AddCompanionOutcome.AlreadyAdded));
            if (taken >= BookingCompanionRules.MaxCompanions(head.HuntGuests)) return await Done(new AddCompanionResult(AddCompanionOutcome.Full));

            // Does this email already have an account, and is it a hunter?
            int? accountUserId = null;
            int? hunterUserRoleId = null;
            bool verified = false;
            string accountName = "";
            using (var cmd = new SqlCommand($@"SELECT TOP 1 u.userId, ISNULL(u.uIsEmailVerified, 0),
                                                      LTRIM(RTRIM(ISNULL(u.uName, '') + ' ' + ISNULL(u.uSurname, ''))),
                                                      (SELECT TOP 1 ur.userRoleId FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId
                                                       WHERE ur.urUserId = u.userId AND {IsHunterRole} ORDER BY ur.userRoleId)
                                               FROM users u
                                               WHERE LOWER(LTRIM(RTRIM(u.uEmail))) = @email
                                               ORDER BY u.userId", conn, tx))
            {
                cmd.Parameters.AddWithValue("@email", normalizedEmail);
                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    accountUserId = r.GetInt32(0);
                    verified = Convert.ToBoolean(r.GetValue(1));
                    accountName = r.GetString(2);
                    hunterUserRoleId = r.IsDBNull(3) ? null : r.GetInt32(3);
                }
            }
            if (accountUserId != null && hunterUserRoleId == null) return await Done(new AddCompanionResult(AddCompanionOutcome.NotAHunter));
            if (accountUserId == head.MainUserId) return await Done(new AddCompanionResult(AddCompanionOutcome.IsMainHunter));

            // Linking straight away needs a verified email - the same rule the invite claim uses.
            bool link = hunterUserRoleId != null && verified;
            string storedName = link && !string.IsNullOrWhiteSpace(accountName) ? accountName : name.Trim();
            int companionId;
            using (var cmd = new SqlCommand(@"INSERT INTO bookingCompanions (bookingId, userRoleId, inviteName, inviteEmail, status, addedByUserId, linkedAt)
                                               OUTPUT inserted.companionId
                                               VALUES (@id, @ur, @name, @email, @status, @by, CASE WHEN @status = 'Linked' THEN SYSUTCDATETIME() END)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", bookingId);
                cmd.Parameters.AddWithValue("@ur", link ? hunterUserRoleId!.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@name", storedName);
                cmd.Parameters.AddWithValue("@email", normalizedEmail);
                cmd.Parameters.AddWithValue("@status", link ? "Linked" : "Pending");
                cmd.Parameters.AddWithValue("@by", callerUserId);
                companionId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            await tx.CommitAsync();

            var hunter = new BookingHunterDto { CompanionId = companionId, Name = storedName, Email = normalizedEmail, Status = link ? "Linked" : "Pending" };
            return new AddCompanionResult(link ? AddCompanionOutcome.Linked : AddCompanionOutcome.Invited, hunter, head.Title, head.MainName);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Unique index backstop (same person added twice at the same moment).
            await tx.RollbackAsync();
            return new AddCompanionResult(AddCompanionOutcome.AlreadyAdded);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        async Task<AddCompanionResult> Done(AddCompanionResult result)
        {
            await tx.RollbackAsync();
            return result;
        }
    }

    /// <summary>
    /// Removes an added hunter. The main hunter (or an Admin) may remove anyone; an added
    /// hunter may remove themselves (leave the trip). Not once the hunt is Completed.
    /// </summary>
    public virtual async Task<RemoveCompanionOutcome> RemoveCompanion(int bookingId, int companionId, int callerUserId, bool isAdmin)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            var head = await ReadBookingHeadAsync(conn, tx, bookingId, lockRow: true);
            int? companionUserId = null;
            bool found = false;
            if (head != null)
            {
                using var cmd = new SqlCommand(@"SELECT ur.urUserId FROM bookingCompanions c
                                                 LEFT JOIN userRoles ur ON c.userRoleId = ur.userRoleId
                                                 WHERE c.companionId = @cid AND c.bookingId = @id", conn, tx);
                cmd.Parameters.AddWithValue("@cid", companionId);
                cmd.Parameters.AddWithValue("@id", bookingId);
                using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    found = true;
                    companionUserId = r.IsDBNull(0) ? null : r.GetInt32(0);
                }
            }
            if (head == null || !found)
            {
                await tx.RollbackAsync();
                return RemoveCompanionOutcome.NotFound;
            }
            if (!isAdmin && head.MainUserId != callerUserId && companionUserId != callerUserId)
            {
                await tx.RollbackAsync();
                return RemoveCompanionOutcome.Forbidden;
            }
            if (!isAdmin && string.Equals(head.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync();
                return RemoveCompanionOutcome.Locked;
            }

            using (var cmd = new SqlCommand("DELETE FROM bookingCompanions WHERE companionId = @cid AND bookingId = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cid", companionId);
                cmd.Parameters.AddWithValue("@id", bookingId);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return RemoveCompanionOutcome.Removed;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Links every Pending invite for this user's email, provided the email is verified and
    /// the user has a Hunter role. Idempotent; returns how many invites were linked. Runs
    /// after email verification and before listing a hunter's trips, so it also catches a
    /// user who only adds the Hunter role later.
    /// </summary>
    public virtual async Task<int> ClaimPendingInvites(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = $@"UPDATE c
                      SET c.userRoleId = h.userRoleId, c.status = 'Linked', c.linkedAt = SYSUTCDATETIME()
                      FROM bookingCompanions c
                      JOIN users u ON u.userId = @uid AND ISNULL(u.uIsEmailVerified, 0) = 1
                                  AND LOWER(LTRIM(RTRIM(u.uEmail))) = c.inviteEmail
                      CROSS APPLY (SELECT TOP 1 ur.userRoleId FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId
                                   WHERE ur.urUserId = u.userId AND {IsHunterRole} ORDER BY ur.userRoleId) h
                      JOIN bookings b ON b.bookingId = c.bookingId
                      WHERE c.status = 'Pending'
                        AND b.bookHunterId <> h.userRoleId
                        AND NOT EXISTS (SELECT 1 FROM bookingCompanions c2 WHERE c2.bookingId = c.bookingId AND c2.userRoleId = h.userRoleId)";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        return await cmd.ExecuteNonQueryAsync();
    }
}
