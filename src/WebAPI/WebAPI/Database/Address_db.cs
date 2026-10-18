using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>Shared address deduplication: finds-or-creates a row in the addresses table for any entity (farm, business, etc.) that needs a physical address.</summary>
public class Address_db
{
    private readonly string _connectionString;

    public Address_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Returns the id of an existing address matching number/street/city/postal
    /// code, or inserts a new one and returns its id. Deliberately does not match
    /// on province/country/coordinates - two addresses differing only in those
    /// fields are treated as the same address.
    /// </summary>
    public async Task<int> GetAddressIdForOther(Dictionary<string, object> addressData)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        
        // Check if address exists
        string checkQ = @"SELECT addressId FROM addresses 
                         WHERE addressNumber = @num AND addressStreetName = @street 
                         AND addressCity = @city AND addressPostalCode = @postal";
        
        using var checkCmd = new SqlCommand(checkQ, conn);
        checkCmd.Parameters.AddWithValue("@num", addressData["addressNumber"] ?? "");
        checkCmd.Parameters.AddWithValue("@street", addressData["addressStreetName"] ?? "");
        checkCmd.Parameters.AddWithValue("@city", addressData["addressCity"] ?? "");
        checkCmd.Parameters.AddWithValue("@postal", addressData["addressPostalCode"] ?? "");
        
        var existing = await checkCmd.ExecuteScalarAsync();
        if (existing != null)
            return Convert.ToInt32(existing);
        
        // Insert new address
        string insertQ = @"INSERT INTO addresses (addressNumber, addressStreetName, addressCity, 
                           addressRegion, addressPostalCode, addressCountry, addressLatitude, addressLongitude)
                           VALUES (@num, @street, @city, @province, @postal, @country, @lat, @lng);
                           SELECT SCOPE_IDENTITY();";
        
        using var insertCmd = new SqlCommand(insertQ, conn);
        insertCmd.Parameters.AddWithValue("@num", addressData["addressNumber"] ?? "");
        insertCmd.Parameters.AddWithValue("@street", addressData["addressStreetName"] ?? "");
        insertCmd.Parameters.AddWithValue("@city", addressData["addressCity"] ?? "");
        insertCmd.Parameters.AddWithValue("@province", addressData["addressRegion"] ?? "");
        insertCmd.Parameters.AddWithValue("@postal", addressData["addressPostalCode"] ?? "");
        insertCmd.Parameters.AddWithValue("@country", addressData["addressCountry"] ?? "");
        insertCmd.Parameters.AddWithValue("@lat", addressData.ContainsKey("addressLatitude") ? addressData["addressLatitude"] ?? DBNull.Value : DBNull.Value);
        insertCmd.Parameters.AddWithValue("@lng", addressData.ContainsKey("addressLongitude") ? addressData["addressLongitude"] ?? DBNull.Value : DBNull.Value);
        
        return Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
    }
}

