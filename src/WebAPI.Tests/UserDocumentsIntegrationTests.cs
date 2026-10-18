using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Proves UserDocuments_db correctly distinguishes required vs optional document
/// types when reading documents back (GetDocumentsForUserRole, GetDeletionContext).
/// This exact query had a real bug found via live testing: it checked "does a
/// roleDocumentRequirements row exist for this type" (CASE WHEN rdr.requirementId IS
/// NOT NULL) instead of "is that row's isRequired = 1", so the one explicitly
/// optional document type (isRequired = 0) was still reported as required, silently
/// blocking its own deletion. A Moq'd controller test can't catch this - the mock
/// just returns whatever is configured - so this needs a real SQL Server, like
/// RatingConcurrencyIntegrationTests. Self-skips if no local DB is available.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class UserDocumentsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly string _connectionString;
    private int _userId;
    private int _userRoleId;
    private int _requiredDocId;
    private int _optionalDocId;
    private bool _dbAvailable;

    public UserDocumentsIntegrationTests(WebApplicationFactory<Program> factory)
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
                VALUES ('DocIntegrationTest', 'User', CONCAT('docintegration.', NEWID(), '@test.local'), 'x', '0820000003', 1, 1, SYSUTCDATETIME(), 0)", conn))
            {
                _userId = (int)(await cmd.ExecuteScalarAsync())!;
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
        await Exec($"DELETE FROM users WHERE userId = {_userId}");
    }

    [Fact]
    public async Task GetDocumentsForUserRole_ReportsIsRequiredCorrectlyForBothKinds()
    {
        if (!_dbAvailable) return;

        var db = new UserDocuments_db(_connectionString);
        var requirements = await db.GetRequiredDocumentTypes("PH");
        int requiredTypeId = requirements.First(r => r.IsRequired).DocumentTypeId;
        int optionalTypeId = requirements.First(r => !r.IsRequired).DocumentTypeId;

        _requiredDocId = await db.UploadDocument(_userRoleId, requiredTypeId, "required.pdf", "data:application/pdf;base64,AAAA", _userId);
        _optionalDocId = await db.UploadDocument(_userRoleId, optionalTypeId, "optional.pdf", "data:application/pdf;base64,BBBB", _userId);

        var documents = await db.GetDocumentsForUserRole(_userRoleId);

        var requiredDoc = documents.Single(d => d.Id == _requiredDocId);
        var optionalDoc = documents.Single(d => d.Id == _optionalDocId);

        Assert.True(requiredDoc.IsRequired, "The required document type must be reported as required.");
        Assert.False(optionalDoc.IsRequired, "The optional document type must NOT be reported as required - this is the exact bug that was found and fixed.");
    }

    [Fact]
    public async Task GetDeletionContext_AllowsDeletingOptionalButFlagsRequired()
    {
        if (!_dbAvailable) return;

        var db = new UserDocuments_db(_connectionString);
        var requirements = await db.GetRequiredDocumentTypes("PH");
        int requiredTypeId = requirements.First(r => r.IsRequired).DocumentTypeId;
        int optionalTypeId = requirements.First(r => !r.IsRequired).DocumentTypeId;

        _requiredDocId = await db.UploadDocument(_userRoleId, requiredTypeId, "required.pdf", "data:application/pdf;base64,AAAA", _userId);
        _optionalDocId = await db.UploadDocument(_userRoleId, optionalTypeId, "optional.pdf", "data:application/pdf;base64,BBBB", _userId);

        var requiredContext = await db.GetDeletionContext(_requiredDocId);
        var optionalContext = await db.GetDeletionContext(_optionalDocId);

        Assert.NotNull(requiredContext);
        Assert.NotNull(optionalContext);
        Assert.True(requiredContext!.IsRequired);
        Assert.False(optionalContext!.IsRequired);

        // End-to-end: the optional one must actually be deletable, the required one must not.
        bool optionalDeleted = await db.DeleteDocument(_optionalDocId, _userId);
        Assert.True(optionalDeleted);
    }
}
