using Microsoft.Data.SqlClient;
using MobileApi.Models;

namespace MobileApi.Database;

public class PermitStatus_db
{
    private readonly string _connectionString;

    public PermitStatus_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<PermitResponse>> GetUserPermitStatuses(int userRoleId)
    {
        var permits = new List<PermitResponse>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = @"
            SELECT udId, udName, udStatus, udIssuedTimestamp, udExpirationTimestamp, udUploadedTimestamp AS sortAt
            FROM userDocumentation
            WHERE udUserRole = @UserRoleId AND ISNULL(udIsDeleted, 0) = 0
            UNION ALL
            -- Hunting-permit applications and SAPS 520 forms (dbo.sapsApplications); negative ids keep them apart.
            SELECT -sapsApplicationId,
                   CONCAT(CASE applicationType WHEN 'HuntingPermit' THEN N'Jagpermit-aansoek - ' ELSE N'SAPS 520 - ' END, FORMAT(submittedAt, 'yyyy-MM-dd')),
                   status, NULL, NULL, submittedAt
            FROM sapsApplications
            WHERE userRoleId = @UserRoleId
            ORDER BY sortAt DESC";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@UserRoleId", userRoleId);

        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            permits.Add(new PermitResponse
            {
                Id = reader.GetInt32(0),
                Name = reader.IsDBNull(1) ? "Permit" : reader.GetString(1),
                Status = FormatPermitStatus(reader.IsDBNull(2) ? null : reader.GetString(2)),
                Issued = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                Expires = reader.IsDBNull(4) ? null : reader.GetDateTime(4)
            });
        }

        return permits;
    }

    private static string FormatPermitStatus(string? status)
    {
        var normalized = (status ?? "").Trim().ToLower().Replace(" ", "");

        if (normalized.Contains("goedgekeur") ||
            normalized.Contains("approved") ||
            normalized.Contains("accept") ||
            normalized.Contains("aktief"))
        {
            return "Goedgekeur";
        }

        if (normalized.Contains("afgekeur") ||
            normalized.Contains("rejected") ||
            normalized.Contains("declined") ||
            normalized.Contains("denied"))
        {
            return "Afgekeur";
        }

        return "Wag Nog";
    }
}
