using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// Per-role hunting-permit records. Reads/writes the same userDocumentation table
/// UserDocuments_db manages for the PJ/PH/VG/Farmer credential-verification
/// workflow, but with its own status vocabulary ("Saved"/"Submitted" rather than
/// "Pending"/"Approved"/"Rejected") and no requirement/verification concept -
/// UserDocuments_db.GetPendingVerificationQueue deliberately INNER JOINs
/// roleDocumentRequirements to exclude rows this class creates from that admin queue.
/// </summary>
public class Permit_db
{
    private readonly string _connectionString;
    public Permit_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Lists permits issued to a userRoleId.</summary>
    public async Task<List<PermitResponse>> GetUserPermits(int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        // SAPS 520 applications live in their own table; they are listed with a negative id so
        // they can never be confused with a userDocumentation row.
        string q = @"SELECT udId, udName, udStatus, udIssuedTimestamp, udExpirationTimestamp
                     FROM userDocumentation WHERE udUserRole = @r AND ISNULL(udIsDeleted, 0) = 0
                     UNION ALL
                     SELECT -sapsApplicationId, CONCAT(CASE applicationType WHEN 'HuntingPermit' THEN N'Jagpermit-aansoek - ' ELSE N'SAPS 520 - ' END, FORMAT(submittedAt, 'yyyy-MM-dd')), status, NULL, NULL
                     FROM sapsApplications WHERE userRoleId = @r";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@r", userRoleId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<PermitResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new PermitResponse
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Status = r.GetString(2),
                Issued = r.IsDBNull(3) ? null : r.GetDateTime(3),
                Expires = r.IsDBNull(4) ? null : r.GetDateTime(4)
            });
        }
        return list;
    }

    /// <summary>Inserts a new permit record and returns its generated id.</summary>
    public async Task<int> CreateUserPermit(CreateUserPermitRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"INSERT INTO userDocumentation (udUserRole, udName, udFilePath, udIssuedTimestamp, udExpirationTimestamp, udStatus, udDtId)
                     VALUES (@ur, @n, @fp, @issued, @exp, @st, @dt);
                     SELECT SCOPE_IDENTITY();";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@ur", request.UdUserRole);
        cmd.Parameters.AddWithValue("@n", request.UdName);
        cmd.Parameters.AddWithValue("@fp", request.UdFilePath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@issued", request.UdIssuedTimestamp ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@exp", request.UdExpirationTimestamp ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@st", request.UdStatus);
        cmd.Parameters.AddWithValue("@dt", request.UdDtId);

        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    /// <summary>Lists every permit record in the system, unfiltered.</summary>
    public async Task<List<PermitResponse>> GetPermit()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT udId, udName, udStatus FROM userDocumentation";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<PermitResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new PermitResponse
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Status = r.GetString(2)
            });
        }
        return list;
    }
}
