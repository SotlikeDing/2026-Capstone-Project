using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// Append-only access to adminAuditLog. Intentionally exposes no Update/Delete method —
/// the table itself also rejects UPDATE/DELETE via triggers (see
/// db/AddAdminEnterpriseTables.sql), so tampering is blocked at both layers.
/// </summary>
public class AdminAudit_db
{
    private readonly string _connectionString;

    public AdminAudit_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Appends one audit-log entry, stamping the server-generated id and UTC timestamp back onto the returned DTO.</summary>
    public virtual async Task<AdminAuditLogDto> InsertAsync(AdminAuditLogDto entry)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string query = @"INSERT INTO adminAuditLog
                            (auditAdminUserId, auditAdminUserName, auditAction, auditTargetEntityType, auditTargetEntityId, auditReason, auditIpAddress, auditUserAgent, auditTimestamp)
                          OUTPUT INSERTED.auditId, INSERTED.auditTimestamp
                          VALUES
                            (@adminUserId, @adminUserName, @action, @targetType, @targetId, @reason, @ipAddress, @userAgent, SYSUTCDATETIME());";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@adminUserId", entry.AdminUserId);
        cmd.Parameters.AddWithValue("@adminUserName", entry.AdminUserName);
        cmd.Parameters.AddWithValue("@action", entry.Action);
        cmd.Parameters.AddWithValue("@targetType", entry.TargetEntityType);
        cmd.Parameters.AddWithValue("@targetId", entry.TargetEntityId);
        cmd.Parameters.AddWithValue("@reason", entry.Reason);
        cmd.Parameters.AddWithValue("@ipAddress", (object?)entry.IpAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@userAgent", (object?)entry.UserAgent ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            entry.Id = reader.GetInt32(0);
            entry.Timestamp = reader.GetDateTime(1);
        }
        return entry;
    }

    /// <summary>Paginated, filterable audit-log search - admin matches by name substring or exact id, newest entries first.</summary>
    public virtual async Task<List<AdminAuditLogDto>> GetAsync(string? admin = null, string? action = null, DateTime? startDate = null, DateTime? endDate = null, int page = 1, int pageSize = 500)
    {
        var results = new List<AdminAuditLogDto>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string query = @"SELECT auditId, auditAdminUserId, auditAdminUserName, auditAction, auditTargetEntityType, auditTargetEntityId, auditReason, auditIpAddress, auditUserAgent, auditTimestamp
                          FROM adminAuditLog
                          WHERE (@admin IS NULL OR auditAdminUserName LIKE '%' + @admin + '%' OR CAST(auditAdminUserId AS NVARCHAR(20)) = @admin)
                            AND (@action IS NULL OR auditAction = @action)
                            AND (@startDate IS NULL OR auditTimestamp >= @startDate)
                            AND (@endDate IS NULL OR auditTimestamp <= @endDate)
                          ORDER BY auditTimestamp DESC
                          OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@admin", (object?)admin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action", (object?)action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@startDate", (object?)startDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@endDate", (object?)endDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@offset", Math.Max(0, (page - 1) * pageSize));
        cmd.Parameters.AddWithValue("@pageSize", pageSize);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new AdminAuditLogDto
            {
                Id = reader.GetInt32(0),
                AdminUserId = reader.GetInt32(1),
                AdminUserName = reader.GetString(2),
                Action = reader.GetString(3),
                TargetEntityType = reader.GetString(4),
                TargetEntityId = reader.GetString(5),
                Reason = reader.GetString(6),
                IpAddress = reader.IsDBNull(7) ? null : reader.GetString(7),
                UserAgent = reader.IsDBNull(8) ? null : reader.GetString(8),
                Timestamp = reader.GetDateTime(9)
            });
        }

        return results;
    }
}
