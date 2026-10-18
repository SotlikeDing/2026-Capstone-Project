using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace WebAPI.Database
{
    /// <summary>A user's hunting-permit record, as stored in the permits table.</summary>
    public class PermitModelDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string PermitNumber { get; set; } = string.Empty;
        public string PermitType { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string DocumentUrl { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
    }

    /// <summary>Data access for the permits table (synchronous - predates the rest of the codebase's async pattern).</summary>
    public class Permits_db
    {
        private readonly string _connectionString;
        public Permits_db(string connectionString) => _connectionString = connectionString;

        /// <summary>Lists all permits belonging to a user.</summary>
        public List<PermitModelDto> GetPermits(int userId)
        {
            var list = new List<PermitModelDto>();
            using (SqlConnection con = new SqlConnection(_connectionString))
            {
                con.Open();
                SqlCommand cmd = new SqlCommand("SELECT Id, UserId, PermitNumber, PermitType, IssueDate, ExpiryDate, DocumentUrl, Province FROM permits WHERE UserId = @u", con);
                cmd.Parameters.AddWithValue("@u", userId);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new PermitModelDto
                        {
                            Id = reader.GetInt32(0),
                            UserId = reader.GetInt32(1),
                            PermitNumber = reader.GetString(2),
                            PermitType = reader.GetString(3),
                            IssueDate = reader.GetDateTime(4),
                            ExpiryDate = reader.GetDateTime(5),
                            DocumentUrl = reader.GetString(6),
                            Province = reader.GetString(7)
                        });
                    }
                }
            }
            return list;
        }

        /// <summary>Inserts a new permit record and returns its generated id.</summary>
        public int AddPermit(PermitModelDto p)
        {
            using (SqlConnection con = new SqlConnection(_connectionString))
            {
                con.Open();
                SqlCommand cmd = new SqlCommand("INSERT INTO permits (UserId, PermitNumber, PermitType, IssueDate, ExpiryDate, DocumentUrl, Province) OUTPUT INSERTED.Id VALUES (@u, @num, @type, @iss, @exp, @doc, @prov)", con);
                cmd.Parameters.AddWithValue("@u", p.UserId);
                cmd.Parameters.AddWithValue("@num", p.PermitNumber);
                cmd.Parameters.AddWithValue("@type", p.PermitType);
                cmd.Parameters.AddWithValue("@iss", p.IssueDate);
                cmd.Parameters.AddWithValue("@exp", p.ExpiryDate);
                cmd.Parameters.AddWithValue("@doc", p.DocumentUrl);
                cmd.Parameters.AddWithValue("@prov", p.Province);
                return (int)cmd.ExecuteScalar();
            }
        }
    }
}
