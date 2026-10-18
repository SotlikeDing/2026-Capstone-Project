using WebAPI.Services;
using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>Farm and game-inventory management: farm CRUD, farmer-facing dashboard stats, and per-farm species/animal inventory.</summary>
public class Farmer_db
{
    private readonly string _connectionString;
    private readonly Address_db _addressDb;

    public Farmer_db(string connectionString, Address_db addressDb)
    {
        _connectionString = connectionString;
        _addressDb = addressDb;
    }

    /// <summary>Paginated, searchable/filterable public farm listing.</summary>
    public virtual async Task<List<FarmResponse>> GetAllFarms(string? searchTerm = null, string? status = null, int page = 1, int pageSize = 10)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
    
        var conditions = new List<string>();
        var parameters = new List<(string, object)>();
    
        if (!string.IsNullOrEmpty(searchTerm))
        {
            conditions.Add("(f.farmName LIKE @search OR f.farmDescription LIKE @search)");
            parameters.Add(("@search", $"%{searchTerm}%"));
        }
        if (!string.IsNullOrEmpty(status))
        {
            conditions.Add("f.farmStatus = @status");
            parameters.Add(("@status", status));
        }
        else
        {
            // The public search: no switched-off farms, unnamed placeholder rows or farms of suspended farmers.
            conditions.Add("ISNULL(f.farmStatus, '') <> 'Inactive' AND LEN(LTRIM(ISNULL(f.farmName, ''))) > 0");
            conditions.Add("NOT EXISTS (SELECT 1 FROM userRoles fur JOIN users fu ON fu.userId = fur.urUserId WHERE fur.userRoleId = fa.fUserRoleId AND fu.uIsSuspended = 1)");
        }
    
        string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
    
        string query = $@"
            SELECT f.farmId, f.farmName, f.farmDescription, f.farmSize, f.farmStatus,
                   a.addressCity, a.addressRegion, a.addressCountry,
                   fa.fUserRoleId, f.farmTerrainType, f.farmFencingType
            FROM farm f
            JOIN farmer fa ON f.farmFarmerId = fa.farmerId
            LEFT JOIN addresses a ON f.farmAddressId = a.addressId
            {whereClause}
            ORDER BY f.farmName
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
    
        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@pageSize", pageSize);
    
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    
        using var reader = await cmd.ExecuteReaderAsync();
        var farms = new List<FarmResponse>();
    
        while (await reader.ReadAsync())
        {
            farms.Add(new FarmResponse
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Size = reader.IsDBNull(3) ? 0 : (double)reader.GetDecimal(3),
                Status = reader.IsDBNull(4) ? null : reader.GetString(4),
                Location = JoinLocation(reader, 5, 6, 7),
                FarmerUserRoleId = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                TerrainType = reader.IsDBNull(9) ? null : reader.GetString(9),
                FencingType = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }
    
        return farms;
    }
    
    
    /// <summary>Fetches a single farm's details by id.</summary>
    public virtual async Task<FarmResponse?> GetFarm(int farmId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        string query = @"SELECT f.farmId, f.farmName, f.farmDescription, f.farmSize, f.farmStatus,
                             a.addressCity, a.addressRegion, a.addressCountry,
                             fa.fUserRoleId, f.farmTerrainType, f.farmFencingType
                      FROM farm f
                      JOIN farmer fa ON f.farmFarmerId = fa.farmerId
                      LEFT JOIN addresses a ON f.farmAddressId = a.addressId
                      WHERE f.farmId = @farmId";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@farmId", farmId);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new FarmResponse
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Size = reader.IsDBNull(3) ? 0 : (double)reader.GetDecimal(3),
                Status = reader.IsDBNull(4) ? null : reader.GetString(4),
                Location = JoinLocation(reader, 5, 6, 7),
                FarmerUserRoleId = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                TerrainType = reader.IsDBNull(9) ? null : reader.GetString(9),
                FencingType = reader.IsDBNull(10) ? null : reader.GetString(10)
            };
        }
        return null;
    }
    
    /// <summary>Dashboard summary for a farmer: total farms and total animal quota across them. ActiveHuntingPermits is always 0 - not actually wired up to any permit data.</summary>
    public virtual async Task<FarmStatsResponse> GetStats(int farmerUserRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string q = @"SELECT COUNT(DISTINCT f.farmId) as TotalFarms,
                            SUM(aa.aaQuota) as TotalAnimals,
                            0 as ActivePermits
                     FROM farm f
                     LEFT JOIN availableAnimals aa ON f.farmId = aa.aaFarmId
                     WHERE f.farmFarmerId IN (SELECT farmerId FROM farmer WHERE fUserRoleId = @farmerId)";
        
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@farmerId", farmerUserRoleId);
        
        using var r = await cmd.ExecuteReaderAsync();
        
        if (await r.ReadAsync())
        {
            return new FarmStatsResponse 
            { 
                TotalFarms = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                TotalAnimals = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetInt64(1)),
                ActiveHuntingPermits = r.IsDBNull(2) ? 0 : r.GetInt32(2)
            };
        }
        
        return new FarmStatsResponse();
    }

    // This method belongs in Package_db, not Farmer_db
    /// <summary>Partial update of an availableAnimals row - only the fields present on the request are written.</summary>
    public virtual async Task<bool> EditAnimals(EditAnimalsRequest request)
    {
        var sets = new List<string>();
        var parameters = new List<(string, object)>();
        
        if (request.AaName != null)
        {
            sets.Add("aaName = @name");
            parameters.Add(("@name", request.AaName));
        }
        if (request.AaQuota.HasValue)
        {
            sets.Add("aaQuota = @quota");
            parameters.Add(("@quota", request.AaQuota.Value));
        }
        if (request.AaEntityPrice.HasValue)
        {
            sets.Add("aaEntityPrice = @price");
            parameters.Add(("@price", request.AaEntityPrice.Value));
        }
        
        if (sets.Count == 0) return false;
        
        string q = $"UPDATE availableAnimals SET {string.Join(",", sets)} WHERE aaId = @id";
        
        using var conn = new SqlConnection(_connectionString); 
        await conn.OpenAsync(); 
        using var cmd = new SqlCommand(q, conn);
        
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", request.AaId);
        
        int rows = await cmd.ExecuteNonQueryAsync(); 
        return rows > 0;
    }
    
    /// <summary>
    /// The user's Farmer (Boer) userRoleId, or null when they don't hold that role.
    /// Only a farmer may own a farm; creating one never grants the role (that goes
    /// through registration and admin verification).
    /// </summary>
    public virtual async Task<int?> GetFarmerUserRoleId(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        const string q = @"SELECT TOP 1 ur.userRoleId FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId
                           WHERE ur.urUserId = @userId AND (UPPER(r.roleCode) IN ('FARMER', 'BOER') OR r.roleName LIKE '%Boer%')
                           ORDER BY ur.userRoleId";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    /// <summary>
    /// Creates a farm owned by farmerUserId (FarmerController passes the caller, or an
    /// Admin-chosen user) - a userId, never reinterpreted as a userRoleId. The owner must
    /// already hold the Farmer role (throws UnauthorizedAccessException otherwise); their
    /// farmer row is created if missing, then the farm itself, its farmLodge
    /// pricing/amenities row (defaulted to R850/day if not given), and any initial
    /// species inventory.
    /// </summary>
    public virtual async Task<int> CreateFarm(int farmerUserId, CreateFarmRequest request)
    {
        if (farmerUserId <= 0) throw new ArgumentOutOfRangeException(nameof(farmerUserId));
        int farmerUserRoleId = await GetFarmerUserRoleId(farmerUserId)
            ?? throw new UnauthorizedAccessException("Slegs 'n geregistreerde boer kan 'n plaas skep.");

        int addressId = 0;
        if (request.Address != null)
        {
            var addressData = new Dictionary<string, object>
            {
                ["addressNumber"] = request.Address.AddressNumber ?? "",
                ["addressStreetName"] = request.Address.AddressStreetName ?? "",
                ["addressCity"] = request.Address.AddressCity ?? "",
                ["addressRegion"] = request.Address.AddressRegion ?? "",
                ["addressPostalCode"] = request.Address.AddressPostalCode ?? "",
                ["addressCountry"] = request.Address.AddressCountry ?? ""
            };
            addressId = await _addressDb.GetAddressIdForOther(addressData);
        }

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string q = @"
            DECLARE @uRoleId INT = @farmerUserRoleId;
            DECLARE @fId INT = (SELECT TOP 1 farmerId FROM farmer WHERE fUserRoleId = @uRoleId);
            IF @fId IS NULL
            BEGIN
                INSERT INTO farmer (fUserRoleId) VALUES (@uRoleId);
                SET @fId = SCOPE_IDENTITY();
            END

            INSERT INTO farm (farmFarmerId, farmDescription, farmName, farmSize, farmAddressId, farmTerrainType, farmFencingType, farmStatus)
            VALUES (@fId, @desc, @name, @size, @addrId, @terrain, @fencing, @status);
            SELECT SCOPE_IDENTITY();";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@farmerUserRoleId", farmerUserRoleId);
        cmd.Parameters.AddWithValue("@desc", request.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@name", request.Name);
        cmd.Parameters.AddWithValue("@size", request.Size);
        cmd.Parameters.AddWithValue("@addrId", addressId == 0 ? DBNull.Value : addressId);
        cmd.Parameters.AddWithValue("@terrain", request.TerrainType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@fencing", request.FencingType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@status", "Aktief");

        int newFarmId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        if (request.ImageUrls is { Count: > 0 })
        {
            using var imgCmd = new SqlCommand("UPDATE farm SET farmImagesJson = @json WHERE farmId = @fid", conn);
            imgCmd.Parameters.AddWithValue("@json", (object?)FarmImages.BuildJson(newFarmId, request.ImageUrls, null) ?? DBNull.Value);
            imgCmd.Parameters.AddWithValue("@fid", newFarmId);
            await imgCmd.ExecuteNonQueryAsync();
        }

        // Insert farmLodge for amenities and daily rates
        string lodgeQ = @"INSERT INTO farmLodge (flFarmId, flPrice, flFacilities)
                          VALUES (@fid, @price, @fac);";
        using var lodgeCmd = new SqlCommand(lodgeQ, conn);
        lodgeCmd.Parameters.AddWithValue("@fid", newFarmId);
        lodgeCmd.Parameters.AddWithValue("@price", request.DailyRateHunter > 0 ? request.DailyRateHunter : 850m);
        lodgeCmd.Parameters.AddWithValue("@fac", request.Amenities != null && request.Amenities.Count > 0 ? string.Join(", ", request.Amenities) : (object)DBNull.Value);
        await lodgeCmd.ExecuteNonQueryAsync();

        if (request.Species != null && request.Species.Count > 0)
        {
            foreach (var sp in request.Species)
            {
                string sq = @"INSERT INTO availableAnimals (aaFarmId, aaName, aaQuota, aaEntityPrice)
                             VALUES (@fid, @name, @quota, @price);";
                using var scmd = new SqlCommand(sq, conn);
                scmd.Parameters.AddWithValue("@fid", newFarmId);
                scmd.Parameters.AddWithValue("@name", sp.SpeciesName);
                scmd.Parameters.AddWithValue("@quota", sp.AvailableCount);
                scmd.Parameters.AddWithValue("@price", sp.PriceTrophy > 0 ? sp.PriceTrophy : sp.PriceMale);
                await scmd.ExecuteNonQueryAsync();
            }
        }

        return newFarmId;
    }

    /// <summary>Deletes a farm along with its animal inventory and lodge record.</summary>
    public virtual async Task<bool> DeleteFarm(int farmId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        
        using var cmd1 = new SqlCommand("DELETE FROM availableAnimals WHERE aaFarmId = @id", conn);
        cmd1.Parameters.AddWithValue("@id", farmId);
        await cmd1.ExecuteNonQueryAsync();

        using var cmdLodge = new SqlCommand("DELETE FROM farmLodge WHERE flFarmId = @id", conn);
        cmdLodge.Parameters.AddWithValue("@id", farmId);
        await cmdLodge.ExecuteNonQueryAsync();

        using var cmd2 = new SqlCommand("DELETE FROM farm WHERE farmId = @id", conn);
        cmd2.Parameters.AddWithValue("@id", farmId);
        return await cmd2.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Lists a specific farmer's own farms.</summary>
    public virtual async Task<List<FarmResponse>> GetFarmerFarms(int farmerUserRoleId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT f.farmId, f.farmName, f.farmDescription, f.farmSize, f.farmStatus, a.addressCity, a.addressRegion, a.addressCountry
                     FROM farm f
                     LEFT JOIN addresses a ON f.farmAddressId = a.addressId
                     WHERE f.farmFarmerId IN (SELECT farmerId FROM farmer WHERE fUserRoleId = @farmerRoleId)
                     ORDER BY f.farmName";
        
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@farmerRoleId", farmerUserRoleId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<FarmResponse>();
        
        while (await r.ReadAsync())
        {
            list.Add(new FarmResponse
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Description = r.IsDBNull(2) ? null : r.GetString(2),
                Size = r.IsDBNull(3) ? 0 : (double)r.GetDecimal(3),
                Status = r.IsDBNull(4) ? null : r.GetString(4),
                Location = $"{r.GetString(5)}, {r.GetString(6)}, {r.GetString(7)}",
                FarmerUserRoleId = farmerUserRoleId
            });
        }
        return list;
    }

    /// <summary>
    /// Partial update of a farm's details, plus an upsert of its farmLodge pricing/
    /// amenities row if either is included in the request (inserting with R850/
    /// "Lodge" defaults for whichever half of the pair wasn't supplied).
    /// </summary>
    public virtual async Task<bool> UpdateFarm(int farmId, UpdateFarmRequest request)
    {
        var sets = new List<string>();
        var parameters = new List<(string, object)>();
        
        if (request.Description != null)
        {
            sets.Add("farmDescription = @desc");
            parameters.Add(("@desc", request.Description));
        }
        if (request.Name != null)
        {
            sets.Add("farmName = @name");
            parameters.Add(("@name", request.Name));
        }
        if (request.Size.HasValue)
        {
            sets.Add("farmSize = @size");
            parameters.Add(("@size", request.Size.Value));
        }
        if (request.TerrainType != null)
        {
            sets.Add("farmTerrainType = @terrain");
            parameters.Add(("@terrain", request.TerrainType));
        }
        if (request.FencingType != null)
        {
            sets.Add("farmFencingType = @fencing");
            parameters.Add(("@fencing", request.FencingType));
        }
        if (request.Status != null)
        {
            sets.Add("farmStatus = @status");
            parameters.Add(("@status", request.Status));
        }
        
        if (sets.Count == 0 && !request.DailyRateHunter.HasValue && request.Amenities == null && request.ImageUrls == null) return false;
        
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        if (request.ImageUrls != null)
        {
            using var readCmd = new SqlCommand("SELECT farmImagesJson FROM farm WHERE farmId = @id", conn);
            readCmd.Parameters.AddWithValue("@id", farmId);
            var existingJson = await readCmd.ExecuteScalarAsync() as string;
            sets.Add("farmImagesJson = @images");
            parameters.Add(("@images", (object?)FarmImages.BuildJson(farmId, request.ImageUrls, existingJson) ?? DBNull.Value));
        }

        if (sets.Count > 0)
        {
            string q = $"UPDATE farm SET {string.Join(",", sets)} WHERE farmId = @id";
            using var cmd = new SqlCommand(q, conn);
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", farmId);
            await cmd.ExecuteNonQueryAsync();
        }

        if (request.DailyRateHunter.HasValue || request.Amenities != null)
        {
            var lSets = new List<string>();
            var lParams = new List<(string, object)>();
            if (request.DailyRateHunter.HasValue)
            {
                lSets.Add("flPrice = @price");
                lParams.Add(("@price", request.DailyRateHunter.Value));
            }
            if (request.Amenities != null)
            {
                lSets.Add("flFacilities = @fac");
                lParams.Add(("@fac", string.Join(", ", request.Amenities)));
            }

            if (lSets.Count > 0)
            {
                // Upsert logic for farmLodge
                string lq = $@"
                    IF EXISTS (SELECT 1 FROM farmLodge WHERE flFarmId = @fid)
                        UPDATE farmLodge SET {string.Join(",", lSets)} WHERE flFarmId = @fid
                    ELSE
                        INSERT INTO farmLodge (flFarmId, flPrice, flFacilities) 
                        VALUES (@fid, ISNULL(@price, 850), ISNULL(@fac, 'Lodge'))";
                
                using var lCmd = new SqlCommand(lq, conn);
                foreach (var (name, value) in lParams)
                    lCmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
                lCmd.Parameters.AddWithValue("@fid", farmId);

                // Add default params in case they are missing in upsert but needed for insert
                if (!lParams.Any(p => p.Item1 == "@price")) lCmd.Parameters.AddWithValue("@price", DBNull.Value);
                if (!lParams.Any(p => p.Item1 == "@fac")) lCmd.Parameters.AddWithValue("@fac", DBNull.Value);

                await lCmd.ExecuteNonQueryAsync();
            }
        }
        
        return true;
    }
    
    /// <summary>Lists a farm's game/animal inventory.</summary>
    public virtual async Task<List<AnimalResponse>> GetFarmAnimals(int farmId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT aaId, aaName, aaQuota, aaEntityPrice FROM availableAnimals WHERE aaFarmId = @fid";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@fid", farmId);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<AnimalResponse>();
        while (await r.ReadAsync())
        {
            list.Add(new AnimalResponse
            {
                Id = r.GetInt32(0),
                Species = r.GetString(1),
                Count = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetInt64(2)),
                Price = r.IsDBNull(3) ? null : r.GetDecimal(3)
            });
        }
        return list;
    }
    
    /// <summary>Adds a species inventory entry to a farm (price left NULL - not set through this path).</summary>
    public virtual async Task<int> AddAnimal(AddAnimalRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"INSERT INTO availableAnimals (aaFarmId, aaName, aaQuota, aaEntityPrice)
                     VALUES (@farmId, @species, @count, NULL);
                     SELECT SCOPE_IDENTITY();";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@farmId", request.FarmId);
        cmd.Parameters.AddWithValue("@species", request.Species);
        cmd.Parameters.AddWithValue("@count", request.Count);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// Full farm listing with game inventory, for admin views. Backfills missing
    /// data with hardcoded defaults throughout (Limpopo province, "Boer" farmer
    /// name, R850 rate, a stock amenities list) so the UI never has to handle
    /// nulls. Male/female/observer prices are derived from the trophy price via
    /// fixed ratios (60%/40%/50%) rather than stored separately.
    /// </summary>
    /// <summary>Website paths of a farm's photos, or the shared placeholder when it has none.</summary>
    private static List<string> FarmImageUrls(int farmId, int count, int version) =>
        count > 0
            ? Enumerable.Range(0, count).Select(i => FarmImages.WebPath(farmId, i, version)).ToList()
            : new List<string> { "images/savanna_sunrise.jpg" };

    /// <summary>One farm photo's bytes and type, or null when the farm has no photo at that index.</summary>
    public virtual async Task<(byte[] Bytes, string ContentType)?> GetFarmImage(int farmId, int index)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT farmImagesJson FROM farm WHERE farmId = @id", conn);
        cmd.Parameters.AddWithValue("@id", farmId);
        return FarmImages.Decode(await cmd.ExecuteScalarAsync() as string, index);
    }

    public virtual async Task<List<FarmDetailResponse>> GetAllFarmDetails()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string query = @"
            SELECT f.farmId, f.farmName, f.farmDescription, f.farmSize, f.farmStatus,
                   ISNULL(a.addressCity, '') as addressCity, ISNULL(a.addressRegion, 'Limpopo') as addressRegion,
                   ISNULL(fa.fUserRoleId, 0) as fUserRoleId, ISNULL(u.userId, 2) as farmerUserId, ISNULL(u.uName + ' ' + u.uSurname, 'Boer') as farmerName,
                   ISNULL(fl.flPrice, 850) as dailyRate, ISNULL(fl.flFacilities, '') as facilities,
                   CASE WHEN ISJSON(f.farmImagesJson) = 1 THEN (SELECT COUNT(*) FROM OPENJSON(f.farmImagesJson)) ELSE 0 END as imageCount,
                   ISNULL(CAST(CHECKSUM(f.farmImagesJson) AS INT), 0) as imageVersion
            FROM farm f
            LEFT JOIN farmer fa ON f.farmFarmerId = fa.farmerId
            LEFT JOIN userRoles ur ON fa.fUserRoleId = ur.userRoleId
            LEFT JOIN users u ON ur.urUserId = u.userId
            LEFT JOIN addresses a ON f.farmAddressId = a.addressId
            LEFT JOIN farmLodge fl ON f.farmId = fl.flFarmId
            ORDER BY f.farmName";

        using var cmd = new SqlCommand(query, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        var farms = new List<FarmDetailResponse>();

        while (await reader.ReadAsync())
        {
            var size = reader.IsDBNull(3) ? 0 : (double)reader.GetDecimal(3);
            var city = reader.GetString(5);
            var prov = reader.GetString(6);
            var loc = !string.IsNullOrEmpty(city) ? $"{city}, {prov}" : prov;
            var facilities = reader.GetString(11);
            var amenities = !string.IsNullOrWhiteSpace(facilities)
                ? facilities.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList()
                : new List<string> { "Lodge", "Slagfasiliteite", "Koelkamer", "Spoorsnyers" };

            farms.Add(new FarmDetailResponse
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Size = size,
                SizeHectares = size,
                Status = reader.IsDBNull(4) ? "Active" : reader.GetString(4),
                IsActive = true,
                NearestTown = city,
                Province = prov,
                Location = loc,
                FarmerUserRoleId = reader.GetInt32(7),
                FarmerUserId = reader.GetInt32(8),
                FarmerName = reader.GetString(9),
                DailyRateHunter = reader.GetDecimal(10),
                DailyRateObserver = reader.GetDecimal(10) * 0.5m,
                Amenities = amenities,
                ImageUrls = FarmImageUrls(reader.GetInt32(0), reader.GetInt32(12), reader.GetInt32(13))
            });
        }
        reader.Close();

        foreach (var farm in farms)
        {
            string animQ = "SELECT aaId, aaFarmId, aaName, aaEntityPrice, aaQuota FROM availableAnimals WHERE aaFarmId = @fid";
            using var aCmd = new SqlCommand(animQ, conn);
            aCmd.Parameters.AddWithValue("@fid", farm.Id);
            using var ar = await aCmd.ExecuteReaderAsync();
            while (await ar.ReadAsync())
            {
                farm.GameInventory.Add(new GameSpeciesResponse
                {
                    Id = ar.GetInt32(0),
                    FarmId = ar.GetInt32(1),
                    SpeciesName = ar.GetString(2),
                    PriceTrophy = ar.IsDBNull(3) ? 0 : ar.GetDecimal(3),
                    PriceMale = ar.IsDBNull(3) ? 0 : ar.GetDecimal(3) * 0.6m,
                    PriceFemale = ar.IsDBNull(3) ? 0 : ar.GetDecimal(3) * 0.4m,
                    AvailableCount = ar.IsDBNull(4) ? 0 : Convert.ToInt32(ar.GetValue(4))
                });
            }
        }

        return farms;
    }

    /// <summary>"Street, City, Region" from nullable address columns - a farm without an address must not break the whole list.</summary>
    private static string JoinLocation(SqlDataReader reader, params int[] columns)
        => string.Join(", ", columns.Where(c => !reader.IsDBNull(c)).Select(reader.GetString).Where(v => !string.IsNullOrWhiteSpace(v)));
}

/// <summary>Partial-update payload for EditAnimals - only non-null fields are written.</summary>
public class EditAnimalsRequest
{
    public int AaId { get; set; }
    public string? AaName { get; set; }
    public long? AaQuota { get; set; }
    public decimal? AaEntityPrice { get; set; }

}
