using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// SAPS hunting-permit application workflow. A fourth consumer of the shared
/// userDocumentation table (alongside Permit_db, Weapon_db and
/// UserDocuments_db's own PJ/PH/VG/Farmer credential workflow), tagged with
/// document-type id 1 for SAPS 520 forms.
/// </summary>
public class HuntingPermit_db
{
    private readonly string _connectionString;
    public HuntingPermit_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Saves a new permit application, JSON-serializing the SAPS form payload into udFilePath with status "Saved".</summary>
    public async Task<int> NewApplication(CreatePermitApplicationRequest request, int userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        // Stored with the SAPS 520 forms (dbo.sapsApplications), not as a verification
        // document: it used to be written as document type 1 ("ID Dokument").
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            request.FarmId, request.StartDate, request.EndDate, request.Species, request.Quantity, request.SapsForm
        });
        const string q = @"INSERT INTO sapsApplications (userId, userRoleId, formJson, status, applicationType)
                           SELECT urUserId, userRoleId, @json, 'Saved', 'HuntingPermit' FROM userRoles WHERE userRoleId = @ur;
                           SELECT SCOPE_IDENTITY();";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@ur", userRoleId);
        cmd.Parameters.AddWithValue("@json", json);
        var id = await cmd.ExecuteScalarAsync();
        return id == null || id == DBNull.Value ? 0 : Convert.ToInt32(id);
    }

    private const string ApplicationColumns = @"SELECT sapsApplicationId, ISNULL(userRoleId, 0),
            CONCAT(CASE applicationType WHEN 'HuntingPermit' THEN N'Jagpermit-aansoek - ' ELSE N'SAPS 520 - ' END, FORMAT(submittedAt, 'yyyy-MM-dd')),
            status, formJson
        FROM sapsApplications";

    /// <summary>Every hunting-permit application and SAPS 520 form (admin queue), newest first.</summary>
    public Task<List<HuntingPermitApplication>> GetApplications()
        => QueryApplicationsAsync(ApplicationColumns + " ORDER BY submittedAt DESC", null);

    /// <summary>One hunter role's applications and SAPS 520 forms, newest first.</summary>
    public Task<List<HuntingPermitApplication>> GetApplicationsByUserRole(int userRoleId)
        => QueryApplicationsAsync(ApplicationColumns + " WHERE userRoleId = @ur ORDER BY submittedAt DESC", userRoleId);

    private async Task<List<HuntingPermitApplication>> QueryApplicationsAsync(string q, int? userRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(q, conn);
        if (userRoleId.HasValue) cmd.Parameters.AddWithValue("@ur", userRoleId.Value);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<HuntingPermitApplication>();
        while (await r.ReadAsync())
        {
            list.Add(new HuntingPermitApplication
            {
                Id = r.GetInt32(0),
                UserRole = r.GetInt32(1),
                Name = r.GetString(2),
                Status = r.GetString(3),
                FilePath = r.IsDBNull(4) ? null : r.GetString(4)
            });
        }
        return list;
    }

    /// <summary>Sets an application's status (e.g. "Approved"/"Rejected").</summary>
    public async Task<bool> SetApproval(int id, string status)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "UPDATE sapsApplications SET status = @st WHERE sapsApplicationId = @id";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@st", status);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Persists a submitted SAPS 520 form's raw JSON, with status "Submitted".
    /// Despite the parameter name, userId is stored directly as udUserRole - the
    /// caller (HuntingPermitController.SubmitSaps520) actually passes a raw userId
    /// here, not a userRoleId, unlike NewApplication above.
    /// </summary>
    public async Task<int> SaveSapsForm(int userId, string jsonContent)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        // Filed under the user (and their hunter role), never in userDocumentation: a SAPS 520
        // isn't a verification document and a hunter may submit one per trip.
        string q = @"INSERT INTO sapsApplications (userId, userRoleId, formJson, status)
                     VALUES (@u, (SELECT TOP 1 ur.userRoleId FROM userRoles ur JOIN roles r ON r.roleId = ur.urRoleId
                                  WHERE ur.urUserId = @u AND UPPER(r.roleCode) IN ('HUNTER', 'JAGTER') ORDER BY ur.userRoleId),
                             @fp, 'Submitted');
                     SELECT SCOPE_IDENTITY();";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@fp", jsonContent);
        var res = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(res);
    }
}
