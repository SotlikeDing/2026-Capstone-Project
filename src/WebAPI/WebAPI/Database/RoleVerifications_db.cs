using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// The role-application verification queue: a user requesting a PJ/PH/VG/Farmer
/// role submits supporting documentation, and an admin approves or rejects it.
/// Distinct from UserDocuments_db's per-document workflow - this is the (older,
/// simpler) whole-role verification, one row per role request rather than one row
/// per required document type.
/// </summary>
public class RoleVerifications_db
{
    private readonly string _connectionString;

    public RoleVerifications_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Lists every role-verification request across all statuses, newest first.</summary>
    public virtual async Task<List<RoleVerificationRequestDto>> GetAllAsync()
    {
        var results = new List<RoleVerificationRequestDto>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string query = @"SELECT rvId, rvUserId, rvUserName, rvUserEmail, rvRequestedRoleCode, rvRequestedRoleName,
                                 rvDocumentName, rvDocumentReference, rvDocumentUrl, rvStatus, rvSubmittedAt, rvDecidedAt, rvRejectionReason
                          FROM roleVerificationRequests
                          ORDER BY rvSubmittedAt DESC;";
        using var cmd = new SqlCommand(query, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(Map(reader));
        }

        return results;
    }

    /// <summary>Approves or rejects a pending request. The rvStatus = 'Pending' guard doubles as optimistic concurrency - an already-decided request returns null instead of being re-decided.</summary>
    public virtual async Task<RoleVerificationRequestDto?> DecideAsync(int id, bool approve, string reason)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string status = approve ? "Approved" : "Rejected";
        string query = @"UPDATE roleVerificationRequests
                          SET rvStatus = @status, rvDecidedAt = SYSUTCDATETIME(), rvRejectionReason = @rejectionReason
                          OUTPUT INSERTED.rvId, INSERTED.rvUserId, INSERTED.rvUserName, INSERTED.rvUserEmail, INSERTED.rvRequestedRoleCode, INSERTED.rvRequestedRoleName,
                                 INSERTED.rvDocumentName, INSERTED.rvDocumentReference, INSERTED.rvDocumentUrl, INSERTED.rvStatus, INSERTED.rvSubmittedAt, INSERTED.rvDecidedAt, INSERTED.rvRejectionReason
                          WHERE rvId = @id AND rvStatus = 'Pending';";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@rejectionReason", approve ? (object)DBNull.Value : reason);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return Map(reader);
        }
        return null;
    }

    /// <summary>Maps one roleVerificationRequests row to its DTO.</summary>
    private static RoleVerificationRequestDto Map(SqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        UserId = reader.GetInt32(1),
        UserName = reader.GetString(2),
        UserEmail = reader.GetString(3),
        RequestedRoleCode = reader.GetString(4),
        RequestedRoleName = reader.GetString(5),
        DocumentName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
        DocumentReference = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
        DocumentUrl = reader.IsDBNull(8) ? null : reader.GetString(8),
        Status = reader.GetString(9),
        SubmittedAt = reader.GetDateTime(10),
        DecidedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
        RejectionReason = reader.IsDBNull(12) ? null : reader.GetString(12)
    };
}
