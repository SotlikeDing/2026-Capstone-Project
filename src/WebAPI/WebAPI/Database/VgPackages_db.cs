using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using WebAPI.Models;

namespace WebAPI.Database
{
    /// <summary>A videographer's (VG) service packages (vgPackages table), keyed off the VG's userId rather than their vg/userRole id directly.</summary>
    public class VgPackages_db
    {
        private readonly string _connectionString;

        public VgPackages_db(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Creates a VG package. request.VgId is actually the VG's userId - it's
        /// resolved to the real vgId via a userRoles join first; returns 0 if that
        /// user isn't a registered VG.
        /// </summary>
        public async Task<int> CreateVgPackage(CreateVgPackageRequest request)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            int? actualVgId = null;
            string vgQuery = "SELECT TOP 1 vg.vgId FROM vg JOIN userRoles ur ON vg.vgUserRoleId = ur.userRoleId WHERE ur.urUserId = @userId";
            using var vgCmd = new SqlCommand(vgQuery, conn);
            vgCmd.Parameters.AddWithValue("@userId", request.VgId);
            var result = await vgCmd.ExecuteScalarAsync();
            if (result != null) actualVgId = Convert.ToInt32(result);

            if (actualVgId == null) return 0; // Or throw exception

            string q = @"INSERT INTO vgPackages (vgId, vgpTitle, vgpRegionCovered, vgpEquipmentProvided, vgpDescription, vgpPrice)
                         VALUES (@vgId, @title, @region, @equip, @desc, @price);
                         SELECT SCOPE_IDENTITY();";

            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@vgId", actualVgId);
            cmd.Parameters.AddWithValue("@title", request.Title ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@region", request.RegionCovered ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@equip", request.EquipmentProvided ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@desc", request.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@price", request.Price);

            var id = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(id);
        }

        /// <summary>Lists a VG's packages by their userId (resolved to vgId internally) - returns an empty list, not an error, if the user isn't a registered VG.</summary>
        public async Task<List<VgPackageResponse>> GetVgPackages(int userId)
        {
            var list = new List<VgPackageResponse>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            int? actualVgId = null;
            string vgQuery = "SELECT TOP 1 vg.vgId FROM vg JOIN userRoles ur ON vg.vgUserRoleId = ur.userRoleId WHERE ur.urUserId = @userId";
            using var vgCmd = new SqlCommand(vgQuery, conn);
            vgCmd.Parameters.AddWithValue("@userId", userId);
            var result = await vgCmd.ExecuteScalarAsync();
            if (result != null) actualVgId = Convert.ToInt32(result);

            if (actualVgId == null) return list;

            string q = @"SELECT vgPackageId, vgpTitle, vgpRegionCovered, vgpEquipmentProvided, vgpDescription, vgpPrice FROM vgPackages WHERE vgId = @vgId";

            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@vgId", actualVgId);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new VgPackageResponse
                {
                    Id = reader.GetInt32(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    RegionCovered = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    EquipmentProvided = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Description = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Price = reader.GetDecimal(5),
                    Status = "Aktief"
                });
            }
            return list;
        }

        /// <summary>Deletes a VG package by its own id.</summary>
        public async Task<bool> DeleteVgPackage(int id)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            string q = "DELETE FROM vgPackages WHERE vgPackageId = @id";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@id", id);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
    }
}
