using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>Advertiser (AP) role: ad CRUD, impression/click tracking, and payment recording against the advertisements/adPayments tables.</summary>
public class Ads_db
{
    private readonly string _connectionString;
    private readonly Address_db _addressDb;
    private static bool _tableColumnsChecked = false;
    private static readonly SemaphoreSlim _schemaLock = new(1, 1);

    public Ads_db(string connectionString, Address_db addressDb)
    {
        _connectionString = connectionString;
        _addressDb = addressDb;
    }

    /// <summary>
    /// Adds any missing advertisements columns/indexes and creates the adPayments
    /// table on first use (in-code migration). A SemaphoreSlim guards the
    /// check-then-act against concurrent callers racing on first use, not just the
    /// static bool alone.
    /// </summary>
    public virtual async Task EnsureColumnsAsync(SqlConnection conn)
    {
        if (_tableColumnsChecked) return;
        await _schemaLock.WaitAsync();
        try
        {
            if (_tableColumnsChecked) return;
            // No SET NOCOUNT ON here: it would stay on for this connection, and callers
            // check ExecuteNonQuery's row count (-1 under NOCOUNT) to report success.
            string q = @"
                IF COL_LENGTH('advertisements', 'adViews') IS NULL
                    ALTER TABLE advertisements ADD adViews INT DEFAULT 0;
                IF COL_LENGTH('advertisements', 'adClicks') IS NULL
                    ALTER TABLE advertisements ADD adClicks INT DEFAULT 0;
                IF COL_LENGTH('advertisements', 'adImagesJson') IS NULL
                    ALTER TABLE advertisements ADD adImagesJson NVARCHAR(MAX) NULL;
                IF COL_LENGTH('advertisements', 'adTargetUrl') IS NULL
                    ALTER TABLE advertisements ADD adTargetUrl NVARCHAR(500) NULL;
                IF COL_LENGTH('advertisements', 'adPaymentRef') IS NULL
                    ALTER TABLE advertisements ADD adPaymentRef NVARCHAR(100) NULL;
                IF COL_LENGTH('advertisements', 'adLastPaymentMonths') IS NULL
                    ALTER TABLE advertisements ADD adLastPaymentMonths INT NULL;
                IF COL_LENGTH('advertisements', 'adLastPaymentAmount') IS NULL
                    ALTER TABLE advertisements ADD adLastPaymentAmount DECIMAL(18,2) NULL;
                IF COL_LENGTH('advertisements', 'adLastPaymentDate') IS NULL
                    ALTER TABLE advertisements ADD adLastPaymentDate DATETIME2 NULL;
                IF COL_LENGTH('advertisements', 'adAdvertiserName') IS NULL
                    ALTER TABLE advertisements ADD adAdvertiserName NVARCHAR(200) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ads_status_expiry' AND object_id = OBJECT_ID('advertisements'))
                    CREATE NONCLUSTERED INDEX IX_ads_status_expiry ON advertisements (adExpirationTimestamp) INCLUDE (adStatus); -- adStatus is NVARCHAR(MAX): not allowed as an index key
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ads_apId' AND object_id = OBJECT_ID('advertisements'))
                    CREATE NONCLUSTERED INDEX IX_ads_apId ON advertisements (adApId);

                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'adPayments')
                BEGIN
                    CREATE TABLE adPayments (
                        adPaymentId INT IDENTITY(1,1) PRIMARY KEY,
                        adId INT NOT NULL FOREIGN KEY REFERENCES advertisements(advertisementId) ON DELETE CASCADE,
                        paymentRef NVARCHAR(100) NOT NULL,
                        paymentDate DATETIME2 NOT NULL DEFAULT GETDATE(),
                        months INT NOT NULL,
                        amount DECIMAL(18,2) NOT NULL,
                        periodStartDate DATETIME2 NOT NULL,
                        periodEndDate DATETIME2 NOT NULL,
                        paymentMethod NVARCHAR(50) DEFAULT 'Simulated Gateway',
                        createdTimestamp DATETIME2 NOT NULL DEFAULT GETDATE()
                    );
                END";
            using var cmd = new SqlCommand(q, conn);
            await cmd.ExecuteNonQueryAsync();
            _tableColumnsChecked = true;
        }
        catch (Exception ex)
        {
            // Not fatal (the columns usually exist already), but never silent: a failing
            // batch reruns on every call until it is fixed.
            Console.WriteLine($"[Ads_db] Schema check failed: {ex.Message}");
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    /// <summary>Paginated ad listing, optionally filtered by status, owning user, or AP profile.</summary>
    public virtual async Task<List<AdResponse>> GetAds(int page = 1, int pageSize = 100, string? status = null, int? userId = null, int? apId = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);

        // Auto-update expired ads to 'Verval'
        try
        {
            using var expCmd = new SqlCommand(
                "UPDATE advertisements SET adStatus = 'Verval' WHERE adStatus = 'Aktief' AND adExpirationTimestamp IS NOT NULL AND adExpirationTimestamp < GETDATE()", 
                conn);
            await expCmd.ExecuteNonQueryAsync();
        }
        catch { }

        string q = @"SET NOCOUNT ON;
                     SELECT a.advertisementId, a.adApId, a.adTitle, a.adShortDescription, a.adCategory,
                            a.adStatus, a.adPrice, a.adCreatedTimestamp, a.adExpirationTimestamp, a.adContactNumber,
                            ISNULL(a.adViews, 0), ISNULL(a.adClicks, 0), a.adImagesJson, a.adTargetUrl,
                            a.adPaymentRef, a.adLastPaymentMonths, a.adLastPaymentAmount, a.adLastPaymentDate,
                            ur.urUserId,
                            ISNULL(NULLIF(a.adAdvertiserName, ''), LTRIM(RTRIM(ISNULL(u.uName, '') + ' ' + ISNULL(u.uSurname, '')))) AS advertiserName,
                            u.uEmail AS advertiserEmail,
                            u.uPhone AS advertiserPhone,
                            a.adType,
                            a.adLongDescription
                     FROM advertisements a WITH (NOLOCK)
                     LEFT JOIN ap WITH (NOLOCK) ON a.adApId = ap.apId
                     LEFT JOIN userRoles ur WITH (NOLOCK) ON ap.apUserRoleId = ur.userRoleId
                     LEFT JOIN users u WITH (NOLOCK) ON ur.urUserId = u.userId
                     WHERE (@status IS NULL OR a.adStatus = @status)
                       AND (@userId IS NULL OR ur.urUserId = @userId)
                       AND (@apId IS NULL OR a.adApId = @apId)
                     ORDER BY a.advertisementId DESC
                     OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(status) ? (object)DBNull.Value : status);
        cmd.Parameters.AddWithValue("@userId", userId.HasValue && userId.Value > 0 ? (object)userId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@apId", apId.HasValue && apId.Value > 0 ? (object)apId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@pageSize", pageSize);

        using var r = await cmd.ExecuteReaderAsync();
        var l = new List<AdResponse>();
        while (await r.ReadAsync())
        {
            l.Add(new AdResponse
            {
                Id = r.GetInt32(0),
                ApId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                Title = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                Description = r.IsDBNull(3) ? null : r.GetString(3),
                Category = r.IsDBNull(4) ? null : r.GetString(4),
                Status = r.IsDBNull(5) ? "Pending" : r.GetString(5),
                Price = r.IsDBNull(6) ? null : r.GetDecimal(6),
                CreatedDate = r.IsDBNull(7) ? DateTime.MinValue : r.GetDateTime(7),
                ExpiryDate = r.IsDBNull(8) ? null : r.GetDateTime(8),
                ContactNumber = r.IsDBNull(9) ? null : r.GetString(9),
                Views = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                Clicks = r.IsDBNull(11) ? 0 : r.GetInt32(11),
                ImagesJson = r.IsDBNull(12) ? null : r.GetString(12),
                TargetUrl = r.IsDBNull(13) ? null : r.GetString(13),
                PaymentRef = r.IsDBNull(14) ? null : r.GetString(14),
                LastPaymentMonths = r.IsDBNull(15) ? null : r.GetInt32(15),
                LastPaymentAmount = r.IsDBNull(16) ? null : r.GetDecimal(16),
                LastPaymentDate = r.IsDBNull(17) ? null : r.GetDateTime(17),
                UserId = r.IsDBNull(18) ? null : r.GetInt32(18),
                AdvertiserName = r.IsDBNull(19) ? null : r.GetString(19),
                AdvertiserEmail = r.IsDBNull(20) ? null : r.GetString(20),
                AdvertiserPhone = r.IsDBNull(21) ? null : r.GetString(21),
                Type = r.IsDBNull(22) ? "Banner" : r.GetString(22),
                LongDescription = r.IsDBNull(23) ? null : r.GetString(23)
            });
        }
        return l;
    }

    /// <summary>Fetches a single ad's full details, auto-flipping it to "Verval" (expired) first if its expiry date has passed and it's still marked Aktief.</summary>
    public virtual async Task<AdResponse?> GetAd(int id)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);

        // Auto-update expired ads to 'Verval'
        try
        {
            using var expCmd = new SqlCommand(
                "UPDATE advertisements SET adStatus = 'Verval' WHERE advertisementId = @id AND adStatus = 'Aktief' AND adExpirationTimestamp IS NOT NULL AND adExpirationTimestamp < GETDATE()", 
                conn);
            expCmd.Parameters.AddWithValue("@id", id);
            await expCmd.ExecuteNonQueryAsync();
        }
        catch { }

        string q = @"SET NOCOUNT ON;
                     SELECT a.advertisementId, a.adApId, a.adTitle, a.adShortDescription, a.adCategory,
                            a.adStatus, a.adPrice, a.adCreatedTimestamp, a.adExpirationTimestamp, a.adContactNumber,
                            ISNULL(a.adViews, 0), ISNULL(a.adClicks, 0), a.adImagesJson, a.adTargetUrl,
                            a.adPaymentRef, a.adLastPaymentMonths, a.adLastPaymentAmount, a.adLastPaymentDate,
                            addr.addressId, addr.addressNumber, addr.addressStreetName, addr.addressCity,
                            addr.addressRegion, addr.addressPostalCode, addr.addressCountry,
                            ur.urUserId,
                            ISNULL(NULLIF(a.adAdvertiserName, ''), LTRIM(RTRIM(ISNULL(u.uName, '') + ' ' + ISNULL(u.uSurname, '')))) AS advertiserName,
                            u.uEmail AS advertiserEmail,
                            u.uPhone AS advertiserPhone,
                            a.adType,
                            a.adLongDescription
                     FROM advertisements a WITH (NOLOCK)
                     LEFT JOIN addresses addr WITH (NOLOCK) ON a.adAddressId = addr.addressId
                     LEFT JOIN ap WITH (NOLOCK) ON a.adApId = ap.apId
                     LEFT JOIN userRoles ur WITH (NOLOCK) ON ap.apUserRoleId = ur.userRoleId
                     LEFT JOIN users u WITH (NOLOCK) ON ur.urUserId = u.userId
                     WHERE a.advertisementId = @id";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", id);
        using var r = await cmd.ExecuteReaderAsync();

        if (await r.ReadAsync())
        {
            var result = new AdResponse
            {
                Id = r.GetInt32(0),
                ApId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                Title = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                Description = r.IsDBNull(3) ? null : r.GetString(3),
                Category = r.IsDBNull(4) ? null : r.GetString(4),
                Status = r.IsDBNull(5) ? "Pending" : r.GetString(5),
                Price = r.IsDBNull(6) ? null : r.GetDecimal(6),
                CreatedDate = r.IsDBNull(7) ? DateTime.MinValue : r.GetDateTime(7),
                ExpiryDate = r.IsDBNull(8) ? null : r.GetDateTime(8),
                ContactNumber = r.IsDBNull(9) ? null : r.GetString(9),
                Views = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                Clicks = r.IsDBNull(11) ? 0 : r.GetInt32(11),
                ImagesJson = r.IsDBNull(12) ? null : r.GetString(12),
                TargetUrl = r.IsDBNull(13) ? null : r.GetString(13),
                PaymentRef = r.IsDBNull(14) ? null : r.GetString(14),
                LastPaymentMonths = r.IsDBNull(15) ? null : r.GetInt32(15),
                LastPaymentAmount = r.IsDBNull(16) ? null : r.GetDecimal(16),
                LastPaymentDate = r.IsDBNull(17) ? null : r.GetDateTime(17)
            };

            if (!r.IsDBNull(18))
            {
                result.Address = new Address
                {
                    AddressId = r.GetInt32(18),
                    AddressNumber = r.IsDBNull(19) ? null : r.GetString(19),
                    AddressStreetName = r.IsDBNull(20) ? null : r.GetString(20),
                    AddressCity = r.IsDBNull(21) ? null : r.GetString(21),
                    AddressRegion = r.IsDBNull(22) ? null : r.GetString(22),
                    AddressPostalCode = r.IsDBNull(23) ? null : r.GetString(23),
                    AddressCountry = r.IsDBNull(24) ? null : r.GetString(24)
                };
            }

            result.UserId = r.IsDBNull(25) ? null : r.GetInt32(25);
            result.AdvertiserName = r.IsDBNull(26) ? null : r.GetString(26);
            result.AdvertiserEmail = r.IsDBNull(27) ? null : r.GetString(27);
            result.AdvertiserPhone = r.IsDBNull(28) ? null : r.GetString(28);
            result.Type = r.IsDBNull(29) ? "Banner" : r.GetString(29);
            result.LongDescription = r.IsDBNull(30) ? null : r.GetString(30);

            return result;
        }
        return null;
    }

    /// <summary>Increments an ad's view counter, unless the viewer is the ad's own owner (self-views don't count, but the call still reports success).</summary>
    public virtual async Task<bool> RecordImpression(int adId, int? viewerUserId = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);

        // If viewer is the owner of the ad, do not increment (exclude self-views)
        if (viewerUserId.HasValue && viewerUserId.Value > 0)
        {
            string checkOwnerQ = @"SELECT TOP 1 1 FROM advertisements a 
                                   JOIN ap ON a.adApId = ap.apId 
                                   JOIN userRoles ur ON ap.apUserRoleId = ur.userRoleId 
                                   WHERE a.advertisementId = @id AND ur.urUserId = @uid";
            using var chkCmd = new SqlCommand(checkOwnerQ, conn);
            chkCmd.Parameters.AddWithValue("@id", adId);
            chkCmd.Parameters.AddWithValue("@uid", viewerUserId.Value);
            var isOwner = await chkCmd.ExecuteScalarAsync();
            if (isOwner != null && isOwner != DBNull.Value)
            {
                return true; // Ignored self-view safely
            }
        }

        string q = "UPDATE advertisements SET adViews = ISNULL(adViews, 0) + 1 WHERE advertisementId = @id";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", adId);
        return (await cmd.ExecuteNonQueryAsync()) > 0;
    }

    /// <summary>Increments an ad's click counter, same self-click exclusion as RecordImpression.</summary>
    public virtual async Task<bool> RecordClick(int adId, int? viewerUserId = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);

        // If viewer is the owner of the ad, do not increment
        if (viewerUserId.HasValue && viewerUserId.Value > 0)
        {
            string checkOwnerQ = @"SELECT TOP 1 1 FROM advertisements a 
                                   JOIN ap ON a.adApId = ap.apId 
                                   JOIN userRoles ur ON ap.apUserRoleId = ur.userRoleId 
                                   WHERE a.advertisementId = @id AND ur.urUserId = @uid";
            using var chkCmd = new SqlCommand(checkOwnerQ, conn);
            chkCmd.Parameters.AddWithValue("@id", adId);
            chkCmd.Parameters.AddWithValue("@uid", viewerUserId.Value);
            var isOwner = await chkCmd.ExecuteScalarAsync();
            if (isOwner != null && isOwner != DBNull.Value)
            {
                return true;
            }
        }

        string q = "UPDATE advertisements SET adClicks = ISNULL(adClicks, 0) + 1 WHERE advertisementId = @id";
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@id", adId);
        return (await cmd.ExecuteNonQueryAsync()) > 0;
    }


    /// <summary>
    /// Creates a new ad. Auto-provisions the AP role/ap row for userId if they
    /// don't have one yet (falling back to an arbitrary existing ap row if that
    /// provisioning fails, rather than erroring), resolves/creates the address via
    /// Address_db, sanitizes dates against SQL Server's minimum DATETIME (1753-01-01)
    /// to avoid an overflow exception, and records a payment row if a payment
    /// reference was supplied on creation. Returns the new ad's generated id.
    /// </summary>
    public virtual async Task<int> CreateAd(int userId, CreateAdRequest request)
    {
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
        await EnsureColumnsAsync(conn);

        // The ad belongs to the caller's own AP role. Creating an ad never grants that role
        // (it used to add an AP role to whoever called this, and fall back to another
        // advertiser's account for an unknown user) - only the AP entity row is created
        // the first time an existing advertiser posts.
        int? apUserRoleId = null;
        int? actualApId = null;
        const string apQuery = @"SELECT TOP 1 ur.userRoleId, ap.apId
                                 FROM userRoles ur
                                 JOIN roles r ON ur.urRoleId = r.roleId
                                 LEFT JOIN ap ON ap.apUserRoleId = ur.userRoleId
                                 WHERE ur.urUserId = @userId AND UPPER(r.roleCode) = 'AP'
                                 ORDER BY CASE WHEN ap.apId IS NULL THEN 1 ELSE 0 END, ur.userRoleId";
        using (var apCmd = new SqlCommand(apQuery, conn))
        {
            apCmd.Parameters.AddWithValue("@userId", userId);
            using var r = await apCmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                apUserRoleId = r.GetInt32(0);
                actualApId = r.IsDBNull(1) ? null : r.GetInt32(1);
            }
        }
        if (apUserRoleId == null)
            throw new UnauthorizedAccessException("Slegs 'n geregistreerde adverteerder (AP) kan 'n advertensie plaas.");

        if (actualApId == null)
        {
            using var insApCmd = new SqlCommand("INSERT INTO ap (apUserRoleId) OUTPUT inserted.apId VALUES (@urId)", conn);
            insApCmd.Parameters.AddWithValue("@urId", apUserRoleId.Value);
            actualApId = (int)(await insApCmd.ExecuteScalarAsync())!;
        }

        // Sanitize dates to prevent SqlDateTime overflow (< 1753-01-01)
        object expiryVal = DBNull.Value;
        if (request.ExpiryDate.HasValue && request.ExpiryDate.Value >= new DateTime(1753, 1, 1))
        {
            expiryVal = request.ExpiryDate.Value;
        }

        object lastPaymentDateVal = DBNull.Value;
        if (request.LastPaymentDate.HasValue && request.LastPaymentDate.Value >= new DateTime(1753, 1, 1))
        {
            lastPaymentDateVal = request.LastPaymentDate.Value;
        }

        string q = @"INSERT INTO advertisements (adApId, adTitle, adShortDescription, adLongDescription, adType, adCategory, adStatus, adCreatedTimestamp, adExpirationTimestamp, adAddressId, adContactNumber, adTargetUrl, adPaymentRef, adLastPaymentMonths, adLastPaymentAmount, adLastPaymentDate, adPrice, adViews, adClicks, adImagesJson, adAdvertiserName) 
                     VALUES (@ap, @t, @s, @l, @type, @category, @status, GETDATE(), @expiry, @addr, @contact, @targetUrl, @paymentRef, @lastMonths, @lastAmount, @lastDate, @price, 0, 0, @imagesJson, @advName);
                     SELECT SCOPE_IDENTITY();";
        
        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@ap", actualApId!.Value);
        cmd.Parameters.AddWithValue("@t", string.IsNullOrWhiteSpace(request.Title) ? "Advertensie" : request.Title.Trim());
        cmd.Parameters.AddWithValue("@s", (object?)request.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@l", (object?)request.LongDescription ?? (object?)request.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", string.IsNullOrWhiteSpace(request.Type) ? "Banner" : request.Type.Trim());
        cmd.Parameters.AddWithValue("@category", string.IsNullOrWhiteSpace(request.Category) ? "Toerusting" : request.Category.Trim());
        cmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(request.Status) ? "Pending Payment" : request.Status.Trim());
        cmd.Parameters.AddWithValue("@expiry", expiryVal);
        cmd.Parameters.AddWithValue("@addr", addressId == 0 ? DBNull.Value : addressId);
        cmd.Parameters.AddWithValue("@contact", (object?)request.ContactNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@targetUrl", string.IsNullOrWhiteSpace(request.TargetUrl) ? (object)DBNull.Value : request.TargetUrl.Trim());
        cmd.Parameters.AddWithValue("@paymentRef", string.IsNullOrWhiteSpace(request.PaymentRef) ? (object)DBNull.Value : request.PaymentRef.Trim());
        cmd.Parameters.AddWithValue("@lastMonths", request.LastPaymentMonths.HasValue && request.LastPaymentMonths.Value > 0 ? (object)request.LastPaymentMonths.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@lastAmount", request.LastPaymentAmount.HasValue && request.LastPaymentAmount.Value > 0 ? (object)request.LastPaymentAmount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@lastDate", lastPaymentDateVal);
        cmd.Parameters.AddWithValue("@price", request.Price.HasValue ? (object)request.Price.Value : 0m); // adPrice = total paid so far (RecordPayment adds to it)
        cmd.Parameters.Add("@imagesJson", System.Data.SqlDbType.NVarChar, -1).Value = string.IsNullOrWhiteSpace(request.ImagesJson) ? (object)DBNull.Value : request.ImagesJson;
        cmd.Parameters.AddWithValue("@advName", string.IsNullOrWhiteSpace(request.AdvertiserName) ? (object)DBNull.Value : request.AdvertiserName.Trim());
        
        var id = await cmd.ExecuteScalarAsync(); 
        int newAdId = Convert.ToInt32(id);

        // If payment ref provided on creation, persist to adPayments table
        if (newAdId > 0 && !string.IsNullOrWhiteSpace(request.PaymentRef))
        {
            try
            {
                var pStart = DateTime.UtcNow;
                var pEnd = (expiryVal != DBNull.Value && expiryVal is DateTime dt) ? dt : DateTime.UtcNow.AddMonths(request.LastPaymentMonths ?? 1);
                string insPayQ = @"INSERT INTO adPayments (adId, paymentRef, paymentDate, months, amount, periodStartDate, periodEndDate, paymentMethod)
                                   VALUES (@adId, @pRef, @pDate, @months, @amount, @pStart, @pEnd, 'Gateway');";
                using var payCmd = new SqlCommand(insPayQ, conn);
                payCmd.Parameters.AddWithValue("@adId", newAdId);
                payCmd.Parameters.AddWithValue("@pRef", request.PaymentRef.Trim());
                payCmd.Parameters.AddWithValue("@pDate", request.LastPaymentDate ?? DateTime.UtcNow);
                payCmd.Parameters.AddWithValue("@months", request.LastPaymentMonths ?? 1);
                payCmd.Parameters.AddWithValue("@amount", request.LastPaymentAmount ?? request.Price ?? 100m);
                payCmd.Parameters.AddWithValue("@pStart", pStart);
                payCmd.Parameters.AddWithValue("@pEnd", pEnd);
                await payCmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        return newAdId;
    }


    /// <summary>
    /// Partial update of an ad - only non-null request fields are written. When
    /// userId is supplied, the UPDATE's own WHERE clause restricts it to either the
    /// ad's owner (resolved via ap/userRoles) or an Admin, so ownership is enforced
    /// atomically in the same statement rather than via a separate pre-check.
    /// </summary>
    public virtual async Task<bool> UpdateAd(int id, UpdateAdRequest request, int? userId = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);

        int? addressId = null;
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

        var sets = new List<string>();
        var parameters = new List<(string, object)>();

        if (request.Title != null) { sets.Add("adTitle = @title"); parameters.Add(("@title", request.Title)); }
        if (request.Description != null) { sets.Add("adShortDescription = @shortDesc"); parameters.Add(("@shortDesc", request.Description)); }
        if (request.LongDescription != null) { sets.Add("adLongDescription = @longDesc"); parameters.Add(("@longDesc", request.LongDescription)); }
        if (request.Type != null) { sets.Add("adType = @type"); parameters.Add(("@type", request.Type)); }
        if (request.AdvertiserName != null) { sets.Add("adAdvertiserName = @advName"); parameters.Add(("@advName", string.IsNullOrWhiteSpace(request.AdvertiserName) ? (object)DBNull.Value : request.AdvertiserName.Trim())); }
        if (request.Status != null) { sets.Add("adStatus = @status"); parameters.Add(("@status", request.Status)); }
        if (request.Price.HasValue) { sets.Add("adPrice = @price"); parameters.Add(("@price", request.Price.Value)); }
        if (request.Category != null) { sets.Add("adCategory = @category"); parameters.Add(("@category", request.Category)); }
        if (request.ExpiryDate.HasValue)
        {
            if (request.ExpiryDate.Value >= new DateTime(1753, 1, 1))
            {
                sets.Add("adExpirationTimestamp = @expiry");
                parameters.Add(("@expiry", request.ExpiryDate.Value));
            }
            else
            {
                sets.Add("adExpirationTimestamp = NULL");
            }
        }
        if (request.ContactNumber != null) { sets.Add("adContactNumber = @contact"); parameters.Add(("@contact", request.ContactNumber)); }
        if (request.TargetUrl != null) { sets.Add("adTargetUrl = @targetUrl"); parameters.Add(("@targetUrl", string.IsNullOrWhiteSpace(request.TargetUrl) ? (object)DBNull.Value : request.TargetUrl.Trim())); }
        if (request.PaymentRef != null) { sets.Add("adPaymentRef = @paymentRef"); parameters.Add(("@paymentRef", string.IsNullOrWhiteSpace(request.PaymentRef) ? (object)DBNull.Value : request.PaymentRef.Trim())); }
        if (request.LastPaymentMonths.HasValue) { sets.Add("adLastPaymentMonths = @lastPaymentMonths"); parameters.Add(("@lastPaymentMonths", request.LastPaymentMonths.Value)); }
        if (request.LastPaymentAmount.HasValue) { sets.Add("adLastPaymentAmount = @lastPaymentAmount"); parameters.Add(("@lastPaymentAmount", request.LastPaymentAmount.Value)); }
        if (request.LastPaymentDate.HasValue)
        {
            if (request.LastPaymentDate.Value >= new DateTime(1753, 1, 1))
            {
                sets.Add("adLastPaymentDate = @lastPaymentDate");
                parameters.Add(("@lastPaymentDate", request.LastPaymentDate.Value));
            }
            else
            {
                sets.Add("adLastPaymentDate = NULL");
            }
        }
        if (request.ImagesJson != null) { sets.Add("adImagesJson = @imagesJson"); parameters.Add(("@imagesJson", request.ImagesJson)); }
        if (addressId.HasValue) { sets.Add("adAddressId = @addressId"); parameters.Add(("@addressId", addressId.Value)); }

        if (sets.Count == 0) return false;

        string q = $"UPDATE advertisements SET {string.Join(", ", sets)} WHERE advertisementId = @id";
        if (userId.HasValue && userId.Value > 0)
        {
            q += " AND (adApId IN (SELECT ap.apId FROM ap JOIN userRoles ur ON ap.apUserRoleId = ur.userRoleId WHERE ur.urUserId = @userId) OR EXISTS (SELECT 1 FROM userRoles urAdmin JOIN roles rAdmin ON urAdmin.urRoleId = rAdmin.roleId WHERE urAdmin.urUserId = @userId AND (rAdmin.roleCode = 'ADMIN' OR rAdmin.roleName = 'Administrateur')))";
        }

        using var cmd = new SqlCommand(q, conn);
        foreach (var (name, value) in parameters)
        {
            if (name == "@imagesJson")
            {
                cmd.Parameters.Add("@imagesJson", System.Data.SqlDbType.NVarChar, -1).Value = value ?? DBNull.Value;
            }
            else
            {
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }
        cmd.Parameters.AddWithValue("@id", id);
        if (userId.HasValue && userId.Value > 0)
        {
            cmd.Parameters.AddWithValue("@userId", userId.Value);
        }

        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    /// <summary>
    /// Records a payment and, in the same transaction, activates the ad
    /// (adStatus = 'Aktief') through the paid-for period. Note: adPrice is
    /// incremented by the payment amount rather than overwritten
    /// (ISNULL(adPrice, 0) + @amount) - it accumulates total paid, not a fixed
    /// listing price. userId, if given, must be the ad's owner or an Admin.
    /// </summary>
    public virtual async Task<bool> RecordPayment(int adId, RecordPaymentRequest request, int? userId = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);

        // Verify authorization if userId provided
        if (userId.HasValue && userId.Value > 0)
        {
            string authQ = @"SELECT TOP 1 1 FROM advertisements a 
                             JOIN ap ON a.adApId = ap.apId 
                             JOIN userRoles ur ON ap.apUserRoleId = ur.userRoleId 
                             WHERE a.advertisementId = @id AND (ur.urUserId = @userId OR EXISTS (SELECT 1 FROM userRoles urAdmin JOIN roles rAdmin ON urAdmin.urRoleId = rAdmin.roleId WHERE urAdmin.urUserId = @userId AND (rAdmin.roleCode = 'ADMIN' OR rAdmin.roleName = 'Administrateur')))";
            using var authCmd = new SqlCommand(authQ, conn);
            authCmd.Parameters.AddWithValue("@id", adId);
            authCmd.Parameters.AddWithValue("@userId", userId.Value);
            var isAllowed = await authCmd.ExecuteScalarAsync();
            if (isAllowed == null || isAllowed == DBNull.Value)
            {
                return false;
            }
        }

        var startDate = request.PeriodStartDate ?? DateTime.UtcNow;
        var endDate = request.PeriodEndDate ?? startDate.AddMonths(Math.Max(1, request.Months));

        using var tx = await conn.BeginTransactionAsync();
        try
        {
            // 0. Idempotent on the payment reference: PayFast retries its notification, so the
            //    same pf_payment_id must never extend the ad twice. The range lock keeps two
            //    concurrent deliveries from both passing this check.
            string dupQ = "SELECT 1 FROM adPayments WITH (UPDLOCK, HOLDLOCK) WHERE adId = @adId AND paymentRef = @pRef";
            using (var dupCmd = new SqlCommand(dupQ, conn, (SqlTransaction)tx))
            {
                dupCmd.Parameters.AddWithValue("@adId", adId);
                dupCmd.Parameters.AddWithValue("@pRef", request.PaymentRef.Trim());
                if (await dupCmd.ExecuteScalarAsync() != null)
                {
                    await tx.CommitAsync();
                    return true;
                }
            }

            // 1. Insert into adPayments
            string insPayQ = @"INSERT INTO adPayments (adId, paymentRef, paymentDate, months, amount, periodStartDate, periodEndDate, paymentMethod)
                               VALUES (@adId, @pRef, GETDATE(), @months, @amount, @pStart, @pEnd, @pMethod);";
            using var payCmd = new SqlCommand(insPayQ, conn, (SqlTransaction)tx);
            payCmd.Parameters.AddWithValue("@adId", adId);
            payCmd.Parameters.AddWithValue("@pRef", request.PaymentRef.Trim());
            payCmd.Parameters.AddWithValue("@months", request.Months);
            payCmd.Parameters.AddWithValue("@amount", request.Amount);
            payCmd.Parameters.AddWithValue("@pStart", startDate);
            payCmd.Parameters.AddWithValue("@pEnd", endDate);
            payCmd.Parameters.AddWithValue("@pMethod", request.PaymentMethod ?? "Simulated Gateway");
            await payCmd.ExecuteNonQueryAsync();

            // 2. Atomically update advertisement activation
            string updAdQ = @"UPDATE advertisements 
                              SET adStatus = 'Aktief',
                                  adExpirationTimestamp = @expiry,
                                  adPaymentRef = @pRef,
                                  adLastPaymentMonths = @months,
                                  adLastPaymentAmount = @amount,
                                  adLastPaymentDate = GETDATE(),
                                  adPrice = ISNULL(adPrice, 0) + @amount
                              WHERE advertisementId = @adId;";
            using var updCmd = new SqlCommand(updAdQ, conn, (SqlTransaction)tx);
            updCmd.Parameters.AddWithValue("@adId", adId);
            updCmd.Parameters.AddWithValue("@expiry", endDate);
            updCmd.Parameters.AddWithValue("@pRef", request.PaymentRef.Trim());
            updCmd.Parameters.AddWithValue("@months", request.Months);
            updCmd.Parameters.AddWithValue("@amount", request.Amount);

            int updated = await updCmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return updated > 0;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Lists payment records, optionally filtered by ad and/or the paying user, newest first.</summary>
    public virtual async Task<List<AdPaymentRecord>> GetPayments(int? adId = null, int? userId = null)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await EnsureColumnsAsync(conn);

        string q = @"SET NOCOUNT ON;
                     SELECT p.adPaymentId, p.adId, p.paymentRef, p.paymentDate, p.months, p.amount,
                            p.periodStartDate, p.periodEndDate, p.paymentMethod,
                            a.adTitle,
                            LTRIM(RTRIM(ISNULL(u.uName, '') + ' ' + ISNULL(u.uSurname, ''))) AS advertiserName
                     FROM adPayments p WITH (NOLOCK)
                     JOIN advertisements a WITH (NOLOCK) ON p.adId = a.advertisementId
                     LEFT JOIN ap WITH (NOLOCK) ON a.adApId = ap.apId
                     LEFT JOIN userRoles ur WITH (NOLOCK) ON ap.apUserRoleId = ur.userRoleId
                     LEFT JOIN users u WITH (NOLOCK) ON ur.urUserId = u.userId
                     WHERE (@adId IS NULL OR p.adId = @adId)
                       AND (@userId IS NULL OR ur.urUserId = @userId)
                     ORDER BY p.paymentDate DESC, p.adPaymentId DESC";

        using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@adId", adId.HasValue && adId.Value > 0 ? (object)adId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@userId", userId.HasValue && userId.Value > 0 ? (object)userId.Value : DBNull.Value);

        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<AdPaymentRecord>();
        while (await r.ReadAsync())
        {
            list.Add(new AdPaymentRecord
            {
                PaymentId = r.GetInt32(0),
                AdId = r.GetInt32(1),
                PaymentRef = r.GetString(2),
                PaymentDate = r.GetDateTime(3),
                Months = r.GetInt32(4),
                Amount = r.GetDecimal(5),
                PeriodStartDate = r.GetDateTime(6),
                PeriodEndDate = r.GetDateTime(7),
                PaymentMethod = r.IsDBNull(8) ? "Simulated Gateway" : r.GetString(8),
                AdTitle = r.IsDBNull(9) ? "" : r.GetString(9),
                AdvertiserName = r.IsDBNull(10) ? "" : r.GetString(10)
            });
        }
        return list;
    }

    /// <summary>Deletes an ad. Same owner-or-Admin restriction (enforced in the DELETE's own WHERE clause) as UpdateAd when userId is supplied.</summary>
    public virtual async Task<bool> DeleteAd(int id, int? userId = null)
    {
        using var conn = new SqlConnection(_connectionString); 
        await conn.OpenAsync(); 
        string q = "DELETE FROM advertisements WHERE advertisementId = @id"; 
        if (userId.HasValue && userId.Value > 0)
        {
            q += " AND (adApId IN (SELECT ap.apId FROM ap JOIN userRoles ur ON ap.apUserRoleId = ur.userRoleId WHERE ur.urUserId = @userId) OR EXISTS (SELECT 1 FROM userRoles urAdmin JOIN roles rAdmin ON urAdmin.urRoleId = rAdmin.roleId WHERE urAdmin.urUserId = @userId AND (rAdmin.roleCode = 'ADMIN' OR rAdmin.roleName = 'Administrateur')))";
        }
        using var cmd = new SqlCommand(q, conn); 
        cmd.Parameters.AddWithValue("@id", id);
        if (userId.HasValue && userId.Value > 0)
        {
            cmd.Parameters.AddWithValue("@userId", userId.Value);
        }
        int rows = await cmd.ExecuteNonQueryAsync(); 
        return rows > 0;
    }
}

