using System.Data;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using MobileApi.Models;

namespace MobileApi.Services;

public interface IMobileAccessService
{
    Task<MobileRoleContext> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<BookingAccess?> GetBookingAccessAsync(MobileRoleContext caller, int bookingId, CancellationToken cancellationToken = default);
    Task<List<MobileBookingResponse>> GetBookingsAsync(MobileRoleContext caller, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the selected role from the signed JWT and confirms it against the
/// database on every protected feature request. Unknown and website-only roles
/// intentionally resolve to Restricted; there is no Hunter fallback.
/// </summary>
public sealed class MobileAccessService : IMobileAccessService
{
    private readonly string _connectionString;
    private readonly ILogger<MobileAccessService> _logger;

    private static readonly IReadOnlyDictionary<MobileRole, IReadOnlySet<string>> CapabilityMap =
        new Dictionary<MobileRole, IReadOnlySet<string>>
        {
            [MobileRole.Restricted] = new HashSet<string>(StringComparer.Ordinal),
            [MobileRole.Hunter] = Set(
                MobileCapabilityNames.ViewTrophyRoom,
                MobileCapabilityNames.SubmitTrophy,
                MobileCapabilityNames.ViewBookingMedia,
                MobileCapabilityNames.UploadBookingMedia,
                MobileCapabilityNames.EditOwnBookingMedia,
                MobileCapabilityNames.SubmitRoute,
                MobileCapabilityNames.GenerateRecap,
                MobileCapabilityNames.ViewRecap),
            [MobileRole.Ph] = Set(
                MobileCapabilityNames.ReviewTrophies,
                MobileCapabilityNames.ViewBookingMedia,
                MobileCapabilityNames.UploadBookingMedia,
                MobileCapabilityNames.EditOwnBookingMedia,
                MobileCapabilityNames.ModerateBookingMedia,
                MobileCapabilityNames.SelectRoute,
                MobileCapabilityNames.GenerateRecap,
                MobileCapabilityNames.PublishRecap,
                MobileCapabilityNames.ViewRecap),
            [MobileRole.Vg] = Set(
                MobileCapabilityNames.ViewBookingMedia,
                MobileCapabilityNames.UploadBookingMedia,
                MobileCapabilityNames.UploadBookingVideo,
                MobileCapabilityNames.EditOwnBookingMedia,
                MobileCapabilityNames.ViewRecap),
        };

    public MobileAccessService(IConfiguration configuration, ILogger<MobileAccessService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
        _logger = logger;
    }

    public async Task<MobileRoleContext> ResolveAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0 ||
            !int.TryParse(principal.FindFirstValue("userRoleId"), out var userRoleId) || userRoleId <= 0)
        {
            _logger.LogWarning("Restricted mobile access: missing authenticated user or selected role claim");
            return Restricted(0, 0, string.Empty);
        }

        const string sql = """
            SELECT r.roleCode
            FROM dbo.userRoles ur
            INNER JOIN dbo.roles r ON r.roleId = ur.urRoleId
            INNER JOIN dbo.users u ON u.userId = ur.urUserId
            WHERE ur.userRoleId = @UserRoleId
              AND ur.urUserId = @UserId
              AND ISNULL(u.uIsSuspended, 0) = 0
              AND ISNULL(u.uIsEmailVerified, 0) = 1;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@UserRoleId", SqlDbType.Int).Value = userRoleId;
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
        await connection.OpenAsync(cancellationToken);
        var roleCode = (await command.ExecuteScalarAsync(cancellationToken))?.ToString()?.Trim() ?? string.Empty;
        var claimedRoleCode = principal.FindFirstValue(ClaimTypes.Role)?.Trim();

        if (string.IsNullOrWhiteSpace(roleCode) ||
            (!string.IsNullOrWhiteSpace(claimedRoleCode) &&
             !string.Equals(NormalizeRoleCode(claimedRoleCode), NormalizeRoleCode(roleCode), StringComparison.Ordinal)))
        {
            _logger.LogWarning("Restricted mobile access: selected role claim failed validation for user {UserId}", userId);
            return Restricted(userId, userRoleId, roleCode);
        }

        var role = ParseRole(roleCode);
        if (role == MobileRole.Restricted)
        {
            _logger.LogInformation("Restricted mobile access for unsupported role code {RoleCode} on user {UserId}", roleCode, userId);
        }

        return new MobileRoleContext(userId, userRoleId, role, roleCode, CapabilityMap[role]);
    }

    public async Task<BookingAccess?> GetBookingAccessAsync(
        MobileRoleContext caller,
        int bookingId,
        CancellationToken cancellationToken = default)
    {
        if (!caller.IsSupported || bookingId <= 0)
        {
            return null;
        }

        const string sql = """
            SELECT b.bookingId,
                   ISNULL(NULLIF(LTRIM(RTRIM(p.pTitle)), ''), CONCAT('Booking #', b.bookingId)) AS title,
                   CAST(b.bookStartDate AS datetime2), CAST(b.bookEndDate AS datetime2),
                   ISNULL(b.bookStatus, ''),
                   CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(b.bookStatus, '')))) IN
                       ('CANCELLED', 'CANCELED', 'AFGEKEUR', 'GEKANSELLEER') THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                   CASE
                     WHEN @Role = 'Hunter' AND (
                       b.bookHunterId = @UserRoleId OR EXISTS (
                         SELECT 1 FROM dbo.bookingCompanions bc
                         WHERE bc.bookingId = b.bookingId AND bc.userRoleId = @UserRoleId AND bc.status = 'Linked')) THEN CAST(1 AS bit)
                     WHEN @Role = 'PH' AND EXISTS (
                       SELECT 1 FROM dbo.ph x WHERE x.phId = b.bookPhId AND x.phUserRoleId = @UserRoleId) THEN CAST(1 AS bit)
                     WHEN @Role = 'VG' AND EXISTS (
                       SELECT 1 FROM dbo.vg x WHERE x.vgId = b.bookVgId AND x.vgUserRoleId = @UserRoleId) THEN CAST(1 AS bit)
                     ELSE CAST(0 AS bit)
                   END AS isParticipant
            FROM dbo.bookings b
            LEFT JOIN dbo.packages p ON p.packageId = b.bookPackageId
            WHERE b.bookingId = @BookingId;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Role", SqlDbType.NVarChar, 20).Value = RoleName(caller.Role);
        command.Parameters.Add("@UserRoleId", SqlDbType.Int).Value = caller.UserRoleId;
        command.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var endDate = reader.GetDateTime(3);
        var status = reader.GetString(4);
        var isCancelled = reader.GetBoolean(5);
        var isParticipant = reader.GetBoolean(6);
        var normalizedStatus = status.Trim().ToUpperInvariant();
        var serverToday = DateTime.UtcNow.Date;
        var completed = normalizedStatus is "COMPLETED" or "VOLTOOI" || endDate.Date < serverToday;
        return new BookingAccess(
            reader.GetInt32(0), reader.GetString(1), reader.GetDateTime(2), endDate,
            status, isCancelled, isParticipant && completed && !isCancelled, isParticipant);
    }

    public async Task<List<MobileBookingResponse>> GetBookingsAsync(
        MobileRoleContext caller,
        CancellationToken cancellationToken = default)
    {
        if (!caller.IsSupported)
        {
            return [];
        }

        const string sql = """
            SELECT b.bookingId,
                   ISNULL(NULLIF(LTRIM(RTRIM(p.pTitle)), ''), CONCAT('Booking #', b.bookingId)),
                   ISNULL(NULLIF(LTRIM(RTRIM(f.farmName)), ''), 'Jagplaas'),
                   ISNULL(NULLIF(LTRIM(RTRIM(CONCAT(a.addressCity, CASE WHEN a.addressRegion IS NULL THEN '' ELSE ', ' + a.addressRegion END))), ''), 'Ligging onbekend'),
                   CAST(b.bookStartDate AS datetime2), CAST(b.bookEndDate AS datetime2), ISNULL(b.bookStatus, ''),
                   LTRIM(RTRIM(ISNULL(u.uName, '') + ' ' + ISNULL(u.uSurname, ''))),
                   CONVERT(int, ISNULL(b.bookHuntGuests, 1)), CONVERT(int, ISNULL(b.bookNonHuntGuests, 0)),
                   CASE WHEN b.bookHunterId = @UserRoleId THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
                   1 + (SELECT COUNT(*) FROM dbo.bookingCompanions bc2 WHERE bc2.bookingId = b.bookingId AND bc2.status = 'Linked')
            FROM dbo.bookings b
            JOIN dbo.userRoles hur ON hur.userRoleId = b.bookHunterId
            JOIN dbo.users u ON u.userId = hur.urUserId
            LEFT JOIN dbo.packages p ON p.packageId = b.bookPackageId
            LEFT JOIN dbo.farm f ON f.farmId = p.pFarmId
            LEFT JOIN dbo.addresses a ON a.addressId = f.farmAddressId
            WHERE
              (@Role = 'Hunter' AND (b.bookHunterId = @UserRoleId OR EXISTS (
                 SELECT 1 FROM dbo.bookingCompanions bc WHERE bc.bookingId = b.bookingId AND bc.userRoleId = @UserRoleId AND bc.status = 'Linked')))
              OR (@Role = 'PH' AND EXISTS (
                 SELECT 1 FROM dbo.ph x WHERE x.phId = b.bookPhId AND x.phUserRoleId = @UserRoleId))
              OR (@Role = 'VG' AND EXISTS (
                 SELECT 1 FROM dbo.vg x WHERE x.vgId = b.bookVgId AND x.vgUserRoleId = @UserRoleId))
            ORDER BY b.bookStartDate DESC, b.bookingId DESC;
            """;

        var result = new List<MobileBookingResponse>();
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Role", SqlDbType.NVarChar, 20).Value = RoleName(caller.Role);
        command.Parameters.Add("@UserRoleId", SqlDbType.Int).Value = caller.UserRoleId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MobileBookingResponse
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                FarmName = reader.GetString(2),
                Location = reader.GetString(3),
                StartDate = reader.GetDateTime(4),
                EndDate = reader.GetDateTime(5),
                Status = FormatStatus(reader.GetString(6)),
                HunterName = reader.GetString(7),
                HuntGuests = reader.GetInt32(8),
                NonHuntGuests = reader.GetInt32(9),
                IsMainHunter = reader.GetBoolean(10),
                MaxHunters = Math.Max(1, reader.GetInt32(8)),
                HunterCount = Math.Max(1, reader.GetInt32(11)),
                CanManageHunters = false
            });
        }
        return result;
    }

    public static MobileRole ParseRole(string? roleCode) => NormalizeRoleCode(roleCode) switch
    {
        "HUNTER" or "JAGTER" => MobileRole.Hunter,
        "PH" or "PROFESSIONALHUNTER" or "PROFESSIONELEJAGTER" => MobileRole.Ph,
        "VG" or "VIDEOGRAPHER" or "VIDEOGRAAF" => MobileRole.Vg,
        _ => MobileRole.Restricted
    };

    public static string RoleName(MobileRole role) => role switch
    {
        MobileRole.Hunter => "Hunter",
        MobileRole.Ph => "PH",
        MobileRole.Vg => "VG",
        _ => "Restricted"
    };

    public static IReadOnlySet<string> CapabilitiesFor(MobileRole role) => CapabilityMap[role];

    private static string NormalizeRoleCode(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static MobileRoleContext Restricted(int userId, int userRoleId, string roleCode) =>
        new(userId, userRoleId, MobileRole.Restricted, roleCode, CapabilityMap[MobileRole.Restricted]);

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private static string FormatStatus(string? status)
    {
        var value = (status ?? string.Empty).Trim().ToUpperInvariant();
        if (value is "COMPLETED" or "VOLTOOI") return "Voltooi";
        if (value is "APPROVED" or "CONFIRMED" or "GOEDGEKEUR" or "PAID" or "BETAAL") return "Goedgekeur";
        if (value is "REJECTED" or "DECLINED" or "CANCELLED" or "CANCELED" or "AFGEKEUR") return "Afgekeur";
        return "Wag Nog";
    }
}
