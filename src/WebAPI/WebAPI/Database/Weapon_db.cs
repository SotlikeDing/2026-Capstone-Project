using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// Firearm/weapon records - yet another consumer of the shared userDocumentation
/// table (see Permit_db's note), scoped by the hardcoded document-type id 5
/// (weapons) rather than a dedicated table.
/// </summary>
public class Weapon_db
{
    private readonly string _connectionString;
    public Weapon_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Inserts a new weapon record (udDtId fixed at 5) and returns its generated id.</summary>
    public async Task<int> AddWeapon(CreateWeaponRequest request)
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

    /// <summary>Lists every weapon record (udDtId = 5) across all users.</summary>
    public async Task<List<WeaponResponse>> GetWeapons()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT udId, udUserRole, udName, udStatus, udFilePath FROM userDocumentation WHERE udDtId = 5";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<WeaponResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new WeaponResponse
            {
                Id = r.GetInt32(0),
                UserRoleId = r.GetInt32(1),
                Name = r.GetString(2),
                Status = r.GetString(3),
                FilePath = r.IsDBNull(4) ? null : r.GetString(4)
            });
        }
        return list;
    }

    /// <summary>Fetches a single weapon record by id.</summary>
    public async Task<WeaponResponse?> GetWeapon(int id)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT udId, udUserRole, udName, udStatus, udFilePath FROM userDocumentation WHERE udId = @id AND udDtId = 5";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();

        if (await r.ReadAsync())
        {
            return new WeaponResponse
            {
                Id = r.GetInt32(0),
                UserRoleId = r.GetInt32(1),
                Name = r.GetString(2),
                Status = r.GetString(3),
                FilePath = r.IsDBNull(4) ? null : r.GetString(4)
            };
        }
        return null;
    }

    /// <summary>Deletes a weapon record.</summary>
    public async Task<bool> DeleteWeapon(int id)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "DELETE FROM userDocumentation WHERE udId = @id AND udDtId = 5";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", id);
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }
}
