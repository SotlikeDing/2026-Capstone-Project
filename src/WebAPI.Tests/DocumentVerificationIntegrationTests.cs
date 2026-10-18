using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Database;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Proves the admin document-verification workflow added on top of
/// UserDocuments_db: every upload/replace starts (or resets to) 'Pending',
/// VerifyDocument can only decide a document once (the udStatus = 'Pending' guard
/// doubles as optimistic concurrency), and AreRequiredDocumentsApproved - the
/// predicate People_db/Package_db/BookingsController gate on - only turns true once
/// every required document type for the role is Approved. Needs a real SQL Server
/// (raw MERGE/transaction behavior, not something a mock can prove), like
/// UserDocumentsIntegrationTests. Self-skips if no local DB is available.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class DocumentVerificationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _connectionString;
    private int _adminUserId;
    private int _userId;
    private int _userRoleId;
    private bool _dbAvailable;

    public DocumentVerificationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        _connectionString = config.GetConnectionString("DefaultConnection")!;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            _dbAvailable = true;

            using (var cmd = new SqlCommand(@"
                INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
                OUTPUT inserted.userId
                VALUES ('DocVerifyTest', 'Owner', CONCAT('docverify.owner.', NEWID(), '@test.local'), 'x', '0820000004', 1, 1, SYSUTCDATETIME(), 0)", conn))
            {
                _userId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            using (var cmd = new SqlCommand(@"
                INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
                OUTPUT inserted.userId
                VALUES ('DocVerifyTest', 'Admin', CONCAT('docverify.admin.', NEWID(), '@test.local'), 'x', '0820000005', 1, 1, SYSUTCDATETIME(), 0)", conn))
            {
                _adminUserId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            int phRoleId;
            using (var cmd = new SqlCommand("SELECT roleId FROM roles WHERE roleCode = 'PH'", conn))
            {
                phRoleId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            using (var cmd = new SqlCommand("INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES (@u, @r)", conn))
            {
                cmd.Parameters.AddWithValue("@u", _userId);
                cmd.Parameters.AddWithValue("@r", phRoleId);
                _userRoleId = (int)(await cmd.ExecuteScalarAsync())!;
            }
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        async Task Exec(string sql)
        {
            try
            {
                using var cmd = new SqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* keep going - see RatingConcurrencyIntegrationTests for why */ }
        }

        await Exec("ALTER TABLE documentAuditLog DISABLE TRIGGER ALL");
        await Exec($"DELETE FROM documentAuditLog WHERE userRoleId = {_userRoleId}");
        await Exec("ALTER TABLE documentAuditLog ENABLE TRIGGER ALL");
        await Exec($"DELETE FROM userDocumentation WHERE udUserRole = {_userRoleId}");
        await Exec($"DELETE FROM userRoles WHERE userRoleId = {_userRoleId}");
        await Exec($"DELETE FROM users WHERE userId IN ({_userId}, {_adminUserId})");
    }

    [Fact]
    public async Task UploadDocument_StartsPending_AndVerifyDocument_Approves()
    {
        if (!_dbAvailable) return;

        var db = new UserDocuments_db(_connectionString);
        var requirements = await db.GetRequiredDocumentTypes("PH");
        int requiredTypeId = requirements.First(r => r.IsRequired).DocumentTypeId;

        int docId = await db.UploadDocument(_userRoleId, requiredTypeId, "licence.pdf", "data:application/pdf;base64,AAAA", _userId);
        var uploaded = (await db.GetDocumentsForUserRole(_userRoleId)).Single(d => d.Id == docId);
        Assert.Equal("Pending", uploaded.Status);

        var result = await db.VerifyDocument(docId, _adminUserId, approve: true, reason: null);
        Assert.NotNull(result);
        Assert.Equal("Approved", result!.Status);

        var verified = (await db.GetDocumentsForUserRole(_userRoleId)).Single(d => d.Id == docId);
        Assert.Equal("Approved", verified.Status);
        Assert.NotNull(verified.VerifiedAt);
    }

    [Fact]
    public async Task VerifyDocument_CannotDecideTheSameDocumentTwice()
    {
        if (!_dbAvailable) return;

        var db = new UserDocuments_db(_connectionString);
        var requirements = await db.GetRequiredDocumentTypes("PH");
        int requiredTypeId = requirements.First(r => r.IsRequired).DocumentTypeId;

        int docId = await db.UploadDocument(_userRoleId, requiredTypeId, "licence.pdf", "data:application/pdf;base64,AAAA", _userId);

        var first = await db.VerifyDocument(docId, _adminUserId, approve: false, reason: "Onduidelik");
        Assert.NotNull(first);
        Assert.Equal("Rejected", first!.Status);

        // Already decided - a second decision (even by another admin) must be a no-op.
        var second = await db.VerifyDocument(docId, _adminUserId, approve: true, reason: null);
        Assert.Null(second);
    }

    [Fact]
    public async Task ReplaceDocument_ResetsAnApprovedDocumentBackToPending()
    {
        if (!_dbAvailable) return;

        var db = new UserDocuments_db(_connectionString);
        var requirements = await db.GetRequiredDocumentTypes("PH");
        int requiredTypeId = requirements.First(r => r.IsRequired).DocumentTypeId;

        int docId = await db.UploadDocument(_userRoleId, requiredTypeId, "licence.pdf", "data:application/pdf;base64,AAAA", _userId);
        await db.VerifyDocument(docId, _adminUserId, approve: true, reason: null);

        bool replaced = await db.ReplaceDocument(docId, "licence-v2.pdf", "data:application/pdf;base64,BBBB", _userId);
        Assert.True(replaced);

        var afterReplace = (await db.GetDocumentsForUserRole(_userRoleId)).Single(d => d.Id == docId);
        Assert.Equal("Pending", afterReplace.Status);
        Assert.Null(afterReplace.VerifiedAt);
    }

    [Fact]
    public async Task AreRequiredDocumentsApproved_OnlyTrueOnceEveryRequiredTypeIsApproved()
    {
        if (!_dbAvailable) return;

        var db = new UserDocuments_db(_connectionString);
        var requirements = await db.GetRequiredDocumentTypes("PH");
        var requiredTypeIds = requirements.Where(r => r.IsRequired).Select(r => r.DocumentTypeId).ToList();
        Assert.True(requiredTypeIds.Count >= 2, "PH must have at least two required document types for this test to be meaningful.");

        Assert.False(await db.AreRequiredDocumentsApproved(_userRoleId), "A role with no documents at all must not be reported as verified.");

        var uploadedIds = new List<int>();
        foreach (var typeId in requiredTypeIds)
            uploadedIds.Add(await db.UploadDocument(_userRoleId, typeId, $"doc-{typeId}.pdf", "data:application/pdf;base64,AAAA", _userId));

        Assert.False(await db.AreRequiredDocumentsApproved(_userRoleId), "Still Pending - must not be reported as verified yet.");

        for (int i = 0; i < uploadedIds.Count - 1; i++)
            await db.VerifyDocument(uploadedIds[i], _adminUserId, approve: true, reason: null);

        Assert.False(await db.AreRequiredDocumentsApproved(_userRoleId), "One required type still Pending - must not be reported as fully verified.");

        await db.VerifyDocument(uploadedIds[^1], _adminUserId, approve: true, reason: null);

        Assert.True(await db.AreRequiredDocumentsApproved(_userRoleId), "Every required type is now Approved - must be reported as fully verified.");
    }

    [Fact]
    public async Task People_GetPeople_ExcludesRoleUntilAllRequiredDocumentsAreApproved()
    {
        if (!_dbAvailable) return;

        var documentsDb = new UserDocuments_db(_connectionString);
        var peopleDb = new People_db(_connectionString);
        var requirements = await documentsDb.GetRequiredDocumentTypes("PH");
        var requiredTypeIds = requirements.Where(r => r.IsRequired).Select(r => r.DocumentTypeId).ToList();

        var beforeUpload = await peopleDb.GetPeople("PH");
        Assert.DoesNotContain(beforeUpload, p => p.UserRoleId == _userRoleId);

        var uploadedIds = new List<int>();
        foreach (var typeId in requiredTypeIds)
            uploadedIds.Add(await documentsDb.UploadDocument(_userRoleId, typeId, $"doc-{typeId}.pdf", "data:application/pdf;base64,AAAA", _userId));

        var stillPending = await peopleDb.GetPeople("PH");
        Assert.DoesNotContain(stillPending, p => p.UserRoleId == _userRoleId);

        foreach (var docId in uploadedIds)
            await documentsDb.VerifyDocument(docId, _adminUserId, approve: true, reason: null);

        var afterApproval = await peopleDb.GetPeople("PH");
        Assert.Contains(afterApproval, p => p.UserRoleId == _userRoleId);
    }

    [Fact]
    public async Task Package_GetPacketsPaginated_ExcludesPackageUntilFarmerIsFullyVerified()
    {
        if (!_dbAvailable) return;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        int farmerRoleId;
        using (var cmd = new SqlCommand("SELECT roleId FROM roles WHERE roleCode = 'FARMER'", conn))
            farmerRoleId = (int)(await cmd.ExecuteScalarAsync())!;

        int farmerUserRoleId;
        using (var cmd = new SqlCommand("INSERT INTO userRoles (urUserId, urRoleId) OUTPUT inserted.userRoleId VALUES (@u, @r)", conn))
        {
            cmd.Parameters.AddWithValue("@u", _userId);
            cmd.Parameters.AddWithValue("@r", farmerRoleId);
            farmerUserRoleId = (int)(await cmd.ExecuteScalarAsync())!;
        }

        int farmerId;
        using (var cmd = new SqlCommand("INSERT INTO farmer (fUserRoleId, fBusinessName) OUTPUT inserted.farmerId VALUES (@u, 'Doc Verify Test Farm Owner')", conn))
        {
            cmd.Parameters.AddWithValue("@u", farmerUserRoleId);
            farmerId = (int)(await cmd.ExecuteScalarAsync())!;
        }

        int farmId;
        using (var cmd = new SqlCommand("INSERT INTO farm (farmFarmerId, farmName) OUTPUT inserted.farmId VALUES (@f, 'Doc Verify Test Farm')", conn))
        {
            cmd.Parameters.AddWithValue("@f", farmerId);
            farmId = (int)(await cmd.ExecuteScalarAsync())!;
        }

        int packageId;
        using (var cmd = new SqlCommand(@"
            INSERT INTO packages (pCreatedTimestamp, pFarmId, pStockPrice, pTitle, pStatus)
            OUTPUT inserted.packageId
            VALUES (SYSUTCDATETIME(), @f, 1000, 'Doc Verify Test Package', 'Active')", conn))
        {
            cmd.Parameters.AddWithValue("@f", farmId);
            packageId = (int)(await cmd.ExecuteScalarAsync())!;
        }

        try
        {
            var documentsDb = new UserDocuments_db(_connectionString);
            var packageDb = new Package_db(_connectionString);
            var requirements = await documentsDb.GetRequiredDocumentTypes("FARMER");
            var requiredTypeIds = requirements.Where(r => r.IsRequired).Select(r => r.DocumentTypeId).ToList();

            var beforeUpload = await packageDb.GetPacketsPaginated(1, 1000);
            Assert.DoesNotContain(beforeUpload.Data, p => p.Id == packageId);
            Assert.Null(await packageDb.GetPacket(packageId));

            var uploadedIds = new List<int>();
            foreach (var typeId in requiredTypeIds)
                uploadedIds.Add(await documentsDb.UploadDocument(farmerUserRoleId, typeId, $"doc-{typeId}.pdf", "data:application/pdf;base64,AAAA", _userId));

            var stillPending = await packageDb.GetPacketsPaginated(1, 1000);
            Assert.DoesNotContain(stillPending.Data, p => p.Id == packageId);
            Assert.Null(await packageDb.GetPacket(packageId));

            foreach (var docId in uploadedIds)
                await documentsDb.VerifyDocument(docId, _adminUserId, approve: true, reason: null);

            var afterApproval = await packageDb.GetPacketsPaginated(1, 1000);
            Assert.Contains(afterApproval.Data, p => p.Id == packageId);
            Assert.NotNull(await packageDb.GetPacket(packageId));
        }
        finally
        {
            async Task Exec(string sql)
            {
                try { using var cmd = new SqlCommand(sql, conn); await cmd.ExecuteNonQueryAsync(); }
                catch { }
            }

            await Exec("ALTER TABLE documentAuditLog DISABLE TRIGGER ALL");
            await Exec($"DELETE FROM documentAuditLog WHERE userRoleId = {farmerUserRoleId}");
            await Exec("ALTER TABLE documentAuditLog ENABLE TRIGGER ALL");
            await Exec($"DELETE FROM userDocumentation WHERE udUserRole = {farmerUserRoleId}");
            await Exec($"DELETE FROM packages WHERE packageId = {packageId}");
            await Exec($"DELETE FROM farm WHERE farmId = {farmId}");
            await Exec($"DELETE FROM farmer WHERE farmerId = {farmerId}");
            await Exec($"DELETE FROM userRoles WHERE userRoleId = {farmerUserRoleId}");
        }
    }
}
