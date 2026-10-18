using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

public class Package_db
{
    private readonly string _connectionString;

    /// <summary>
    /// A package with no farm passes through untouched; a package whose farm has an
    /// unverified farmer (a required document type still missing an Approved document)
    /// is excluded from public browse - likewise for the package's PJ organiser, when
    /// one exists (pPjId is always a pj.pjId, resolved via ur1). CreatePacket only ever
    /// saves a package for a PJ, so a package without one can't be created through the
    /// API. Requires the query to
    /// `LEFT JOIN farm f`, `LEFT JOIN farmer fa ON fa.farmerId = f.farmFarmerId`,
    /// `LEFT JOIN pj pj1 ON p.pPjId = pj1.pjId`, and
    /// `LEFT JOIN userRoles ur1 ON pj1.pjUserRoleId = ur1.userRoleId`.
    /// </summary>
    private const string FarmerDocsApprovedPredicate = @"
        (f.farmId IS NULL OR NOT EXISTS (
            SELECT 1 FROM roleDocumentRequirements rdr
            WHERE rdr.roleCode = 'FARMER' AND rdr.isRequired = 1
              AND NOT EXISTS (
                  SELECT 1 FROM userDocumentation ud
                  WHERE ud.udUserRole = fa.fUserRoleId AND ud.udDtId = rdr.documentTypeId
                    AND ud.udIsDeleted = 0 AND ud.udStatus = 'Approved'
              )
        ))";

    private const string OrganiserDocsApprovedPredicate = @"
        (ur1.userRoleId IS NULL OR NOT EXISTS (
            SELECT 1 FROM roleDocumentRequirements rdr
            JOIN roles roPj ON roPj.roleId = ur1.urRoleId
            WHERE rdr.roleCode = roPj.roleCode AND rdr.isRequired = 1
              AND NOT EXISTS (
                  SELECT 1 FROM userDocumentation ud
                  WHERE ud.udUserRole = ur1.userRoleId AND ud.udDtId = rdr.documentTypeId
                    AND ud.udIsDeleted = 0 AND ud.udStatus = 'Approved'
              )
        ))";

    private const string RequiredPackageDocsApprovedPredicate = FarmerDocsApprovedPredicate + " AND " + OrganiserDocsApprovedPredicate
        + PubliclyListedPredicate;

    /// <summary>
    /// What the public may see besides the document rule: not switched off or hidden (package or its farm),
    /// not past its "available till" date, and not owned by a suspended outfitter or farmer.
    /// </summary>
    private const string PubliclyListedPredicate = @"
        AND ISNULL(p.pStatus, '') NOT IN ('Inactive', 'Hidden', 'Draft')
        AND (p.pAvailableTill IS NULL OR p.pAvailableTill >= CAST(GETDATE() AS date))
        AND (f.farmId IS NULL OR ISNULL(f.farmStatus, '') <> 'Inactive')
        AND NOT EXISTS (SELECT 1 FROM users su WHERE su.userId = ur1.urUserId AND su.uIsSuspended = 1)
        AND NOT EXISTS (SELECT 1 FROM userRoles fur JOIN users fu ON fu.userId = fur.urUserId
                        WHERE fur.userRoleId = fa.fUserRoleId AND fu.uIsSuspended = 1)";

    public Package_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Creates a new hunting package and returns its generated id. request.PPjId is the
    /// organising PJ's userId (PackageController sets it to the caller); it is resolved
    /// to that user's pj.pjId, never matched against pjId/userRoleId directly. Returns 0
    /// (nothing saved) when that user is not a PJ.
    /// </summary>
    public virtual async Task<int> CreatePacket(CreatePackageRequest request)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        int? actualPjId = null;
        if (request.PPjId.HasValue)
        {
            string pjQuery = @"SELECT TOP 1 pj.pjId
                              FROM pj
                              JOIN userRoles ur ON pj.pjUserRoleId = ur.userRoleId
                              WHERE ur.urUserId = @userId";
            using var pjCmd = new SqlCommand(pjQuery, conn);
            pjCmd.Parameters.AddWithValue("@userId", request.PPjId.Value);
            var result = await pjCmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value) 
            {
                actualPjId = Convert.ToInt32(result);
            }
            else
            {
                // Auto-create pj record if user has PJ role
                string ensureQ = @"DECLARE @urId INT = (SELECT TOP 1 userRoleId FROM userRoles ur JOIN roles r ON ur.urRoleId = r.roleId WHERE ur.urUserId = @userId AND (r.roleCode = 'PJ' OR r.roleCode = 'Uitruster' OR r.roleName LIKE '%Uitruster%' OR r.roleName LIKE '%Verskaffer%'));
                                   IF @urId IS NOT NULL
                                   BEGIN
                                       INSERT INTO pj (pjUserRoleId, pjBusinessName) VALUES (@urId, 'MikPunt Uitruster');
                                       SELECT SCOPE_IDENTITY();
                                   END";
                using var ensureCmd = new SqlCommand(ensureQ, conn);
                ensureCmd.Parameters.AddWithValue("@userId", request.PPjId.Value);
                var createdId = await ensureCmd.ExecuteScalarAsync();
                if (createdId != null && createdId != DBNull.Value)
                    actualPjId = Convert.ToInt32(createdId);
            }
        }

        // Every package belongs to a PJ: without one it would skip the PJ document
        // check and go straight to the public marketplace. 0 = refused (not a PJ).
        if (actualPjId == null) return 0;

        string q = @"INSERT INTO packages (pTitle, pDescription, pStockPrice, pDurationDays, pMaxParticipants, 
                                           pIncludedItems, pStatus, pCreatedTimestamp, pAvailableFrom, pAvailableTill, 
                                           pFarmId, pPjId, pMinExpeditionLength, pMinGuests, pMaxGuests, pInclusions, pExclusions)
                     VALUES (@title, @desc, @price, @duration, @maxPart, @items, @status, GETDATE(), 
                             @from, @till, @farmId, @pjId, @minExp, @minGuests, @maxGuests, @inclusions, @exclusions);
                     SELECT SCOPE_IDENTITY();";
        
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@title", request.PTitle);
        cmd.Parameters.AddWithValue("@desc", request.PDescription);
        cmd.Parameters.AddWithValue("@price", request.PStockPrice);
        cmd.Parameters.AddWithValue("@duration", request.PDurationDays);
        cmd.Parameters.AddWithValue("@maxPart", request.PMaxParticipants);
        cmd.Parameters.AddWithValue("@items", request.PIncludedItems);
        cmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(request.PStatus) ? "Active" : request.PStatus);
        cmd.Parameters.AddWithValue("@from", request.PAvailableFrom ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@till", request.PAvailableTill ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@farmId", request.PFarmId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@pjId", actualPjId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@minExp", request.PMinExpeditionLength ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@minGuests", request.PMinGuests ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@maxGuests", request.PMaxGuests ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@inclusions", (object?)NormaliseItemList(request.PInclusions) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@exclusions", (object?)NormaliseItemList(request.PExclusions) ?? DBNull.Value);
        
        var id = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(id);
    }

    /// <summary>Updates an existing package's details.</summary>
    public virtual async Task<bool> UpdatePacket(int id, UpdatePackageRequest request)
    {
        var sets = new List<string>();
        var parameters = new List<(string, object)>();
        
        if (request.PTitle != null)
        {
            sets.Add("pTitle = @title");
            parameters.Add(("@title", request.PTitle));
        }
        if (request.PDescription != null)
        {
            sets.Add("pDescription = @desc");
            parameters.Add(("@desc", request.PDescription));
        }
        if (request.PStockPrice.HasValue)
        {
            sets.Add("pStockPrice = @price");
            parameters.Add(("@price", request.PStockPrice.Value));
        }
        if (request.PDurationDays.HasValue)
        {
            sets.Add("pDurationDays = @duration");
            parameters.Add(("@duration", request.PDurationDays.Value));
        }
        if (request.PMaxParticipants.HasValue)
        {
            sets.Add("pMaxParticipants = @maxPart");
            parameters.Add(("@maxPart", request.PMaxParticipants.Value));
        }
        if (request.PIncludedItems != null)
        {
            sets.Add("pIncludedItems = @items");
            parameters.Add(("@items", request.PIncludedItems));
        }
        if (request.PStatus != null)
        {
            sets.Add("pStatus = @status");
            parameters.Add(("@status", request.PStatus));
        }
        if (request.PAvailableFrom.HasValue)
        {
            sets.Add("pAvailableFrom = @from");
            parameters.Add(("@from", request.PAvailableFrom.Value));
        }
        if (request.PAvailableTill.HasValue)
        {
            sets.Add("pAvailableTill = @till");
            parameters.Add(("@till", request.PAvailableTill.Value));
        }
        if (request.PMinExpeditionLength.HasValue)
        {
            sets.Add("pMinExpeditionLength = @minLen");
            parameters.Add(("@minLen", request.PMinExpeditionLength.Value));
        }
        if (request.PMinGuests.HasValue)
        {
            sets.Add("pMinGuests = @minGuests");
            parameters.Add(("@minGuests", request.PMinGuests.Value));
        }
        if (request.PMaxGuests.HasValue)
        {
            sets.Add("pMaxGuests = @maxGuests");
            parameters.Add(("@maxGuests", request.PMaxGuests.Value));
        }
        if (request.PFarmId.HasValue)
        {
            sets.Add("pFarmId = @farmId");
            parameters.Add(("@farmId", request.PFarmId.Value));
        }
        if (request.PInclusions != null)
        {
            sets.Add("pInclusions = @inclusions");
            parameters.Add(("@inclusions", (object?)NormaliseItemList(request.PInclusions) ?? DBNull.Value));
        }
        if (request.PExclusions != null)
        {
            sets.Add("pExclusions = @exclusions");
            parameters.Add(("@exclusions", (object?)NormaliseItemList(request.PExclusions) ?? DBNull.Value));
        }
        
        if (sets.Count == 0) return false;
        
        string q = $"UPDATE packages SET {string.Join(",", sets)} WHERE packageId = @id";
        
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(q, conn);
        
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// "Ingesluit" / "Nie ingesluit nie" lists: one trimmed item per line, blanks dropped,
    /// at most 30 items of 150 characters (the columns hold 2000). Empty becomes null.
    /// </summary>
    public static string? NormaliseItemList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var items = text.Replace("\r", "").Split('\n')
            .Select(i => i.Trim().TrimStart('-', '*', '•').Trim())
            .Where(i => i.Length > 0)
            .Select(i => i.Length > 150 ? i[..150] : i)
            .Take(30)
            .ToList();
        // Drop whole items (never cut one in half) until the list fits the 2000-character column.
        while (items.Count > 0 && items.Sum(i => i.Length) + items.Count - 1 > 2000)
            items.RemoveAt(items.Count - 1);
        return items.Count == 0 ? null : string.Join("\n", items);
    }

    /// <summary>Legacy unpaginated listing - delegates to GetPacketsPaginated with a generous page size.</summary>
    public virtual async Task<List<PackageResponse>> GetPackets()
    {
        var res = await GetPacketsPaginated(1, 1000); // legacy
        return res.Data;
    }

    // One column list, set of joins and reader for every public package query (browse,
    // detail, filtered search), so a new column is added in one place.
    private static readonly string PackageSelect = @"
        SELECT p.packageId, p.pTitle, p.pDescription, p.pStockPrice, p.pDurationDays, p.pMaxParticipants, p.pIncludedItems, p.pStatus,
               p.pAvailableFrom, p.pAvailableTill, p.pFarmId, p.pPjId, p.pMinExpeditionLength, p.pMinGuests, p.pMaxGuests,
               ISNULL(f.farmName, '') AS farmName, ISNULL(a.addressRegion, 'Limpopo') AS provName,
               ISNULL(CASE WHEN a.addressCity IS NOT NULL AND a.addressRegion IS NOT NULL THEN a.addressCity + ', ' + a.addressRegion WHEN a.addressCity IS NOT NULL THEN a.addressCity ELSE a.addressRegion END, 'Limpopo') AS location,
               COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(u.uName, '') + ' ' + ISNULL(u.uSurname, ''))), ''), 'MikPunt Uitruster') AS organiserName,
               ISNULL(u.userId, 0) AS organiserUserId,
               CAST(CASE WHEN " + FarmerDocsApprovedPredicate + @" THEN 1 ELSE 0 END AS BIT) AS farmerDocsApproved,
               CAST(CASE WHEN " + OrganiserDocsApprovedPredicate + @" THEN 1 ELSE 0 END AS BIT) AS organiserDocsApproved,
               p.pInclusions, p.pExclusions,
               CAST(CASE WHEN f.farmImagesJson IS NOT NULL AND f.farmImagesJson NOT IN ('', '[]') THEN 1 ELSE 0 END AS BIT) AS farmHasImages
        FROM packages p
        LEFT JOIN farm f ON p.pFarmId = f.farmId
        LEFT JOIN farmer fa ON fa.farmerId = f.farmFarmerId
        LEFT JOIN addresses a ON f.farmAddressId = a.addressId
        LEFT JOIN pj pj1 ON p.pPjId = pj1.pjId
        LEFT JOIN userRoles ur1 ON pj1.pjUserRoleId = ur1.userRoleId
        LEFT JOIN users u ON u.userId = ur1.urUserId";

    /// <summary>Maps one row of <see cref="PackageSelect"/>.</summary>
    private static PackageResponse ReadPackage(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Title = r.GetString(1),
        Description = r.IsDBNull(2) ? "" : r.GetString(2),
        StockPrice = r.GetDecimal(3),
        DurationDays = r.IsDBNull(4) ? 0 : Convert.ToInt32(r.GetValue(4)),
        MaxParticipants = r.IsDBNull(5) ? 0 : Convert.ToInt32(r.GetValue(5)),
        IncludedItems = r.IsDBNull(6) ? "" : r.GetString(6),
        Status = r.GetString(7),
        AvailableFrom = r.IsDBNull(8) ? null : r.GetDateTime(8),
        AvailableTill = r.IsDBNull(9) ? null : r.GetDateTime(9),
        FarmId = r.IsDBNull(10) ? null : r.GetInt32(10),
        PjId = r.IsDBNull(11) ? null : r.GetInt32(11),
        MinExpeditionLength = r.IsDBNull(12) ? null : Convert.ToInt32(r.GetValue(12)),
        MinGuests = r.IsDBNull(13) ? null : Convert.ToInt32(r.GetValue(13)),
        MaxGuests = r.IsDBNull(14) ? null : Convert.ToInt32(r.GetValue(14)),
        FarmName = r.GetString(15),
        Province = r.GetString(16),
        Location = r.GetString(17),
        OrganiserName = r.GetString(18),
        OrganiserUserId = r.GetInt32(19),
        FarmerDocumentsApproved = r.GetBoolean(20),
        OrganiserDocumentsApproved = r.GetBoolean(21),
        Inclusions = r.IsDBNull(22) ? "" : r.GetString(22),
        Exclusions = r.IsDBNull(23) ? "" : r.GetString(23),
        FarmHasImages = r.GetBoolean(24),
    };

    /// <summary>Runs <see cref="PackageSelect"/> with the given WHERE/ORDER tail and parameters.</summary>
    private async Task<List<PackageResponse>> QueryPackagesAsync(string tail, params (string Name, object? Value)[] parameters)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(PackageSelect + "\n" + tail, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<PackageResponse>();
        while (await r.ReadAsync()) list.Add(ReadPackage(r));
        return list;
    }

    /// <summary>Public, paginated package browse - excludes packages whose farm's farmer or PJ organiser has an outstanding required document (RequiredPackageDocsApprovedPredicate).</summary>
    public virtual async Task<PaginatedPackageResponse> GetPacketsPaginated(int page, int pageSize)
    {
        int total;
        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            string countQ = @"SELECT COUNT(*) FROM packages p
                               LEFT JOIN farm f ON p.pFarmId = f.farmId
                               LEFT JOIN farmer fa ON fa.farmerId = f.farmFarmerId
                               LEFT JOIN pj pj1 ON p.pPjId = pj1.pjId
                               LEFT JOIN userRoles ur1 ON pj1.pjUserRoleId = ur1.userRoleId
                               WHERE " + RequiredPackageDocsApprovedPredicate;
            using var countCmd = new SqlCommand(countQ, conn);
            total = (int)(await countCmd.ExecuteScalarAsync())!;
        }

        var list = await QueryPackagesAsync(
            "WHERE " + RequiredPackageDocsApprovedPredicate + @"
             ORDER BY p.pCreatedTimestamp DESC
             OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
            ("@offset", (page - 1) * pageSize), ("@pageSize", pageSize));
        return new PaginatedPackageResponse { Data = list, TotalCount = total, Page = page, PageSize = pageSize };
    }

    /// <summary>
    /// Single-package lookup used by the public package-detail page (unauthenticated,
    /// no ownership check) - must apply the same verification gate as
    /// GetPacketsPaginated/GetPacketFiltered, or a package hidden from browse/search
    /// would still be fully viewable by anyone who knows/guesses its id.
    /// </summary>
    public virtual async Task<PackageResponse?> GetPacket(int id)
        => (await QueryPackagesAsync("WHERE p.packageId = @id AND " + RequiredPackageDocsApprovedPredicate, ("@id", id))).FirstOrDefault();

    /// <summary>Structured package search (species, price range, dates, etc.), same verification gating as GetPacketsPaginated.</summary>
    public virtual Task<List<PackageResponse>> GetPacketFiltered(PackageFilterRequest filter)
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object?)>();

        if (filter.PFarmId.HasValue)
        {
            conditions.Add("p.pFarmId = @farmId");
            parameters.Add(("@farmId", filter.PFarmId.Value));
        }
        if (filter.PPjId.HasValue)
        {
            conditions.Add(@"p.pPjId IN (SELECT pj.pjId FROM pj JOIN userRoles ur ON pj.pjUserRoleId = ur.userRoleId WHERE ur.urUserId = @userId)");
            parameters.Add(("@userId", filter.PPjId.Value));
        }
        if (filter.PStatus != null)
        {
            conditions.Add("p.pStatus = @status");
            parameters.Add(("@status", filter.PStatus));
        }

        conditions.Add(RequiredPackageDocsApprovedPredicate);
        return QueryPackagesAsync("WHERE " + string.Join(" AND ", conditions) + " ORDER BY p.pCreatedTimestamp DESC", parameters.ToArray());
    }

    /// <summary>
    /// Every package the given user organises, whatever its status and including the ones the
    /// marketplace hides because a required document isn't approved yet - so the PJ can see
    /// them and why they're hidden (FarmerDocumentsApproved / OrganiserDocumentsApproved).
    /// </summary>
    public virtual Task<List<PackageResponse>> GetPackagesForOrganiser(int userId)
        => QueryPackagesAsync(@"WHERE p.pPjId IN (SELECT pj.pjId FROM pj JOIN userRoles ur ON pj.pjUserRoleId = ur.userRoleId WHERE ur.urUserId = @userId)
                                ORDER BY p.pCreatedTimestamp DESC", ("@userId", userId));

    /// <summary>Whether any booking uses this package - DeletePacket then deactivates it instead of deleting it.</summary>
    public virtual async Task<bool> PackageHasBookings(int id)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT CASE WHEN EXISTS (SELECT 1 FROM bookings WHERE bookPackageId = @id) THEN 1 ELSE 0 END", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }

    /// <summary>Deletes a package - or, when bookings use it, deactivates it (pStatus 'Inactive') to keep the booking history.</summary>
    public virtual async Task<bool> DeletePacket(int id)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        
        // Check if bookings exist for this package
        string checkQ = "SELECT COUNT(*) FROM bookings WHERE bookPackageId = @id";
        using (var checkCmd = new SqlCommand(checkQ, conn))
        {
            checkCmd.Parameters.AddWithValue("@id", id);
            int bookingCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
            if (bookingCount > 0)
            {
                // Soft-delete to preserve booking history and avoid foreign key constraint violations
                string softDeleteQ = "UPDATE packages SET pStatus = 'Inactive' WHERE packageId = @id";
                using var softCmd = new SqlCommand(softDeleteQ, conn);
                softCmd.Parameters.AddWithValue("@id", id);
                return await softCmd.ExecuteNonQueryAsync() > 0;
            }
        }

        string q = "DELETE FROM packages WHERE packageId = @id";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", id);
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }
}


