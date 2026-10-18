using Microsoft.Data.SqlClient;

namespace WebAPI.Database;

/// <summary>Small lookups the payment service needs that don't belong to one feature's table.</summary>
public class Payments_db
{
    private readonly string _connectionString;
    public Payments_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Whether the user holds the role (by roles.roleCode, case-insensitive) - e.g. only a PH can pay for a PH listing.</summary>
    public virtual async Task<bool> UserHasRole(int userId, string roleCode)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        const string q = @"SELECT TOP 1 1 FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId
                           WHERE ur.urUserId = @userId AND UPPER(r.roleCode) = UPPER(@roleCode)";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@roleCode", roleCode);
        return await cmd.ExecuteScalarAsync() != null;
    }
}
