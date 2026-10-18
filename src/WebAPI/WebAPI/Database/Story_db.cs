using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>
/// Data access for the storyData table - despite the class name, this only links a
/// booking to an "expedition number" (sdExpeditionNum), not narrative story text.
/// </summary>
public class Story_db
{
    private readonly string _connectionString;
    public Story_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Creates a storyData row linking a booking to an (optional) expedition number.</summary>
    public async Task<int> CreateStory(CreateStoryRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"INSERT INTO storyData (sdBookingId, sdExpeditionNum) VALUES (@b, @e);
                     SELECT SCOPE_IDENTITY();";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@b", request.SdBookingId);
        cmd.Parameters.AddWithValue("@e", request.SdExpeditionNum ?? (object)DBNull.Value);

        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    /// <summary>Lists every storyData record.</summary>
    public async Task<List<StoryResponse>> GetStories()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT storyDataId, sdBookingId, sdExpeditionNum FROM storyData";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<StoryResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new StoryResponse
            {
                Id = r.GetInt32(0),
                BookingId = r.GetInt32(1),
                ExpeditionNum = r.IsDBNull(2) ? (long?)null : r.GetInt64(2)
            });
        }
        return list;
    }

    /// <summary>Fetches a single storyData record by id.</summary>
    public async Task<StoryResponse?> GetStory(int id)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT storyDataId, sdBookingId, sdExpeditionNum FROM storyData WHERE storyDataId = @id";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();

        if (await r.ReadAsync())
        {
            return new StoryResponse
            {
                Id = r.GetInt32(0),
                BookingId = r.GetInt32(1),
                ExpeditionNum = r.IsDBNull(2) ? (long?)null : r.GetInt64(2)
            };
        }
        return null;
    }

    /// <summary>Updates a storyData record's expedition number. No-ops (returns false) if the request omits it.</summary>
    public async Task<bool> EditStory(int id, EditStoryRequest request)
    {
        if (request.SdExpeditionNum == null)
            return false;

        string q = "UPDATE storyData SET sdExpeditionNum = @exp WHERE storyDataId = @id";

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@exp", request.SdExpeditionNum.Value);
        cmd.Parameters.AddWithValue("@id", id);

        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }
}
