using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using WebAPI.Models;

namespace WebAPI.Database
{
    /// <summary>A PH's per-service day-rate pricing entries (phPricing table), keyed off the PH's own userId rather than their ph/userRole id directly.</summary>
    public class PhPricing_db
    {
        private readonly string _connectionString;

        public PhPricing_db(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Creates a pricing entry. Despite the name, request.PhId is actually the
        /// PH's userId, not their ph table id - it's resolved to the real phId via
        /// a userRoles join before the insert.
        /// </summary>
        public async Task<int> CreatePhPricing(CreatePhPricingRequest request)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            int actualPhId = 0;
            string phQuery = "SELECT TOP 1 ph.phId FROM ph JOIN userRoles ur ON ph.phUserRoleId = ur.userRoleId WHERE ur.urUserId = @userId";
            using (var phCmd = new SqlCommand(phQuery, conn))
            {
                phCmd.Parameters.AddWithValue("@userId", request.PhId);
                var result = await phCmd.ExecuteScalarAsync();
                if (result != null) actualPhId = Convert.ToInt32(result);
            }

            string q = @"INSERT INTO phPricing (phpPhId, phpGameType, phpHunterCount, phpPrice)
                         VALUES (@phId, @game, @count, @price);
                         SELECT SCOPE_IDENTITY();";

            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@phId", actualPhId);
            cmd.Parameters.AddWithValue("@game", request.GameType);
            cmd.Parameters.AddWithValue("@count", request.HunterCount);
            cmd.Parameters.AddWithValue("@price", request.Price);

            var id = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(id);
        }

        /// <summary>Lists a PH's pricing entries by their userId (not phId) - all returned rows are reported as Status "Aktief" (there's no actual status column).</summary>
        public async Task<List<PhPricingResponse>> GetPhPricing(int phUserId)
        {
            var list = new List<PhPricingResponse>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            string q = @"SELECT p.phPricingId, p.phpGameType, p.phpHunterCount, p.phpPrice
                         FROM phPricing p
                         JOIN ph ON p.phpPhId = ph.phId
                         JOIN userRoles ur ON ph.phUserRoleId = ur.userRoleId
                         WHERE ur.urUserId = @userId";

            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@userId", phUserId);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new PhPricingResponse
                {
                    Id = reader.GetInt32(0),
                    GameType = reader.GetString(1),
                    HunterCount = Convert.ToInt32(reader.GetInt64(2)),
                    Price = reader.GetDecimal(3),
                    Status = "Aktief"
                });
            }
            return list;
        }

        /// <summary>Deletes a pricing entry by its own id.</summary>
        public async Task<bool> DeletePhPricing(int id)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            string q = "DELETE FROM phPricing WHERE phPricingId = @id";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@id", id);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
    }
}
