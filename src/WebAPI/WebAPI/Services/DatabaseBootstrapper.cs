using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WebAPI.Services;

/// <summary>
/// Brings the configured database fully up to date on every app startup, with no
/// external .sql files or sqlcmd step required - the scripts travel embedded in the
/// assembly itself (see WebAPI.csproj). Written for a CI/CD deploy to a hosted server:
/// point a brand new, empty SQL Server at this app and it creates the database and
/// full schema on first boot; on every later deploy it just re-applies the same
/// idempotent migration scripts, which is a safe no-op once already applied.
///
/// Every script that ran is recorded in dbo.schemaVersions with a checksum of its text,
/// so later starts skip it unless the script changed. A failing migration stops startup
/// (the container restarts and the deploy smoke test goes red) unless
/// Database:ContinueOnMigrationFailure is true.
///
/// Deliberately does NOT include one-off seed/cleanup/delete scripts (ToetsData.sql,
/// CleanDB.sql, DeleteUser.sql, etc.) - only genuinely idempotent schema migrations
/// are embedded and run here.
/// </summary>
public static class DatabaseBootstrapper
{
    private const string BootstrapScript = "db.Complete_create.sql";

    // Order matters - later scripts depend on tables/columns created by earlier ones.
    private static readonly string[] MigrationScriptOrder =
    {
        "db.AddAdminEnterpriseTables.sql",
        "db.AddUserAccountStatusAndChatModeration.sql",
        "db.AddSystemSettings.sql",
        "db.AddRatingsSystem.sql",
        "db.AddRatingModerationAndSafety.sql",   // needs ratings + systemSettings
        "db.AddRatingHistoryAndDisputes.sql",    // needs ratings + systemSettings
        "db.AddFarmRatings.sql",                 // needs bookings + farm + users
        "db.AddUserDocumentManagement.sql",      // needs systemSettings
        "db.AddDocumentVerification.sql",        // needs userDocumentation + documentAuditLog (verifiedBy/At, rejection reason)
        "db.AddEmailVerificationCodes.sql",      // standalone: pending e-mail verification codes
        "db.AddSapsApplicationsAndPermits.sql",  // needs users + userRoles + userDocumentation
        "db.MoveHuntingPermitApplications.sql",  // needs sapsApplications
        "db.AddTeamInviteTerms.sql",             // needs teamInvites
        "db.DedupeProvinces.sql",                // data repair: duplicate province seed
        "db.AddTrophyRoom.sql",
        "db.AddRowlandWardTrophyEvaluation.sql", // needs trophies
        "db.AddRowlandWardProfilePreference.sql",
        "db.AddTrophyShotDistance.sql",          // needs trophies
        "db.AddBookingStaffAcceptance.sql",      // needs bookings
        "db.AddAiHelper.sql",                    // standalone: AI advice log + learning observations
        "db.AddBookingCompanions.sql",           // needs bookings + userRoles
        "db.AddVgPackagesColumns.sql",           // needs vgPackages
        "db.AddSharedBookingMediaRecaps.sql",    // needs bookings + userRoles + trophies (MobileAPI media/recaps/YouTube)
        "db.AddBookingMediaVideoClips.sql",      // needs bookingMedia
        "db.DedupeSeedDuplicates.sql",           // data repair: duplicate document types / users / user roles + unique indexes
        "db.AddUsdToZarRateSetting.sql",         // needs systemSettings
        "db.NormalizeAccountStatusAndFlags.sql", // needs users.uAccountStatus + featureFlags
        "db.AddPackagePricesInZarSetting.sql",   // needs systemSettings
        "db.AddFarmImagesAndPackageInclusions.sql", // needs farm + packages
    };

    /// <summary>The migration scripts in the order they run (embedded resource names).</summary>
    public static IReadOnlyList<string> MigrationScripts => MigrationScriptOrder;

    private const string VersionTableSql = @"
IF OBJECT_ID(N'dbo.schemaVersions', N'U') IS NULL
    CREATE TABLE dbo.schemaVersions (
        script     NVARCHAR(200) NOT NULL PRIMARY KEY,
        checksum   CHAR(64)      NOT NULL,
        appliedAt  DATETIME2     NOT NULL CONSTRAINT DF_schemaVersions_appliedAt DEFAULT SYSUTCDATETIME(),
        durationMs INT           NOT NULL,
        runCount   INT           NOT NULL CONSTRAINT DF_schemaVersions_runCount DEFAULT 1
    );";

    private const string RecordVersionSql = @"
MERGE dbo.schemaVersions AS t
USING (SELECT @script AS script) AS s ON t.script = s.script
WHEN MATCHED THEN UPDATE SET checksum = @checksum, appliedAt = SYSUTCDATETIME(), durationMs = @ms, runCount = t.runCount + 1
WHEN NOT MATCHED THEN INSERT (script, checksum, durationMs) VALUES (@script, @checksum, @ms);";

    /// <summary>
    /// Entry point, called once at app startup (see Program.cs). Creates the schema
    /// from scratch if dbo.users doesn't exist yet, then applies every migration script in
    /// MigrationScriptOrder that hasn't run yet in its current form (per dbo.schemaVersions).
    /// Each script is still written to be idempotent (its own IF COL_LENGTH/IF NOT EXISTS
    /// guards), so re-running one is always safe. Throws on the first failing script
    /// unless <paramref name="continueOnError"/> is set.
    /// </summary>
    public static async Task RunAsync(string targetConnectionString, bool continueOnError = false)
    {
        // Several migration scripts open an explicit transaction in one GO-separated
        // batch and COMMIT it several batches later (standard, valid T-SQL under
        // sqlcmd/SSMS). Under MARS (which the app's normal connection string enables,
        // for unrelated concurrent-query reasons elsewhere) SQL Server requires a
        // transaction to be committed within the same MARS batch it started in, and
        // errors ("transaction started in a MARS batch is still active at the end of
        // the batch") once script execution moves on to the next GO batch. Migrations
        // run once at startup on their own short-lived connections, so there's no
        // downside to just disabling MARS for them specifically.
        string migrationConnectionString = ToMarsDisabledConnectionString(targetConnectionString);

        bool schemaExists = await TableExistsAsync(targetConnectionString, "users");

        if (!schemaExists)
        {
            Console.WriteLine("[DB BOOTSTRAP] dbo.users not found - running Complete_create.sql to create the database/schema.");
            await BootstrapSchemaAsync(migrationConnectionString);
        }

        // Two instances starting together (a rolling deploy, parallel test hosts) must not both
        // run the same scripts: hold a session-level app lock until the pass is done.
        using var lockConn = new SqlConnection(migrationConnectionString);
        await lockConn.OpenAsync();
        using (var takeLock = new SqlCommand(
            "DECLARE @r INT; EXEC @r = sp_getapplock @Resource = N'Mikpunt.DatabaseBootstrapper', @LockMode = N'Exclusive', " +
            "@LockOwner = N'Session', @LockTimeout = 300000; SELECT @r;", lockConn) { CommandTimeout = 320 })
        {
            if (Convert.ToInt32(await takeLock.ExecuteScalarAsync()) < 0)
                throw new InvalidOperationException("Timed out waiting for another instance's database migration pass.");
        }

        var applied = await ReadAppliedVersionsAsync(migrationConnectionString);
        int ran = 0, skipped = 0, failed = 0;

        foreach (var script in MigrationScriptOrder)
        {
            string checksum = Checksum(ReadEmbeddedResource(script));
            if (applied.TryGetValue(script, out var known) && known == checksum)
            {
                skipped++;
                continue;
            }

            var timer = Stopwatch.StartNew();
            try
            {
                await ExecuteScriptAsync(migrationConnectionString, script);
                await RecordVersionAsync(migrationConnectionString, script, checksum, (int)timer.ElapsedMilliseconds);
                ran++;
                Console.WriteLine($"[DB BOOTSTRAP] Applied '{script}' in {timer.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[DB BOOTSTRAP] Migration '{script}' failed: {ex.Message}");
                if (!continueOnError)
                    throw new InvalidOperationException(
                        $"Database migration '{script}' failed, so the app will not start on a half-migrated schema. " +
                        "Fix the script, or set Database__ContinueOnMigrationFailure=true to start anyway.", ex);
            }
        }

        Console.WriteLine($"[DB BOOTSTRAP] Schema check/migration pass complete: {ran} applied, {skipped} already up to date, {failed} failed.");
    }

    /// <summary>SHA-256 of the script text with line endings normalised, so a CRLF/LF checkout doesn't count as a change.</summary>
    public static string Checksum(string sql)
    {
        string normalised = sql.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿').Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised))).ToLowerInvariant();
    }

    /// <summary>Creates dbo.schemaVersions if needed and returns script -> checksum for everything already applied.</summary>
    private static async Task<Dictionary<string, string>> ReadAppliedVersionsAsync(string connectionString)
    {
        var applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using (var create = new SqlCommand(VersionTableSql, conn))
            await create.ExecuteNonQueryAsync();
        using var cmd = new SqlCommand("SELECT script, checksum FROM dbo.schemaVersions", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            applied[reader.GetString(0)] = reader.GetString(1);
        return applied;
    }

    /// <summary>Records (or re-records, after a script changed) a successfully applied migration.</summary>
    private static async Task RecordVersionAsync(string connectionString, string script, string checksum, int durationMs)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(RecordVersionSql, conn);
        cmd.Parameters.AddWithValue("@script", script);
        cmd.Parameters.AddWithValue("@checksum", checksum);
        cmd.Parameters.AddWithValue("@ms", durationMs);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Runs Complete_create.sql, which contains its own
    /// "IF NOT EXISTS (SELECT name FROM sys.databases ...) CREATE DATABASE Mikpunt"
    /// guard. Tries the given connection string first (covers a hosting platform
    /// that already pre-provisioned an empty database - the guard just skips CREATE
    /// DATABASE and proceeds straight to creating tables in it). Only falls back to a
    /// `master`-catalog connection if the target database doesn't exist yet at all,
    /// since opening a connection to a nonexistent database fails before any query
    /// can run. Expects an already MARS-disabled connection string (see RunAsync).
    /// </summary>
    private static async Task BootstrapSchemaAsync(string migrationConnectionString)
    {
        try
        {
            using var conn = new SqlConnection(migrationConnectionString);
            await conn.OpenAsync();
            await ExecuteScriptAsync(conn, BootstrapScript);
            return;
        }
        catch (SqlException)
        {
            // Most likely the target database itself doesn't exist yet - fall through
            // to the master-catalog connection below.
        }

        try
        {
            var masterConnectionString = ToMasterConnectionString(migrationConnectionString);
            using var conn = new SqlConnection(masterConnectionString);
            await conn.OpenAsync();
            await ExecuteScriptAsync(conn, BootstrapScript);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB BOOTSTRAP] Could not create the database/schema: {ex.Message}. " +
                "If the app's SQL login can't CREATE DATABASE, pre-provision an empty 'Mikpunt' " +
                "database via your hosting platform and restart the app - it will populate the schema into it.");
        }
    }

    /// <summary>Same server/credentials, but pointed at the `master` catalog so a connection can succeed even when the target database doesn't exist yet.</summary>
    private static string ToMasterConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        return builder.ConnectionString;
    }

    /// <summary>Same connection string with MultipleActiveResultSets forced off, required for scripts that open/commit a transaction across multiple GO batches (see the comment in RunAsync).</summary>
    private static string ToMarsDisabledConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { MultipleActiveResultSets = false };
        return builder.ConnectionString;
    }

    /// <summary>Probes for dbo.&lt;tableName&gt; via OBJECT_ID; treats a failed connection the same as "table not found" since that almost always means the database itself doesn't exist yet.</summary>
    private static async Task<bool> TableExistsAsync(string connectionString, string tableName)
    {
        try
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT OBJECT_ID(@name, N'U')", conn);
            cmd.Parameters.AddWithValue("@name", $"dbo.{tableName}");
            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value;
        }
        catch (SqlException)
        {
            // Can't even connect - almost certainly the database doesn't exist yet.
            return false;
        }
    }

    /// <summary>Opens a fresh connection to run the named embedded migration script (minus any USE line), then delegates to the connection-scoped overload.</summary>
    private static async Task ExecuteScriptAsync(string connectionString, string resourceName)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await ExecuteSqlAsync(conn, PrepareMigrationSql(ReadEmbeddedResource(resourceName)));
    }

    /// <summary>
    /// Many migration scripts start with "USE Mikpunt;" so they can be run by hand in SSMS. Run by the
    /// app, that line would switch away from the configured database (a copy, a test database, a
    /// differently named production database) to whatever is called Mikpunt - so it is dropped here.
    /// </summary>
    public static string PrepareMigrationSql(string sql) =>
        Regex.Replace(sql, @"^\s*USE\s+\[?\w+\]?\s*;?\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>Runs the named embedded script as-is (used for Complete_create.sql, which creates and USEs the database itself).</summary>
    private static Task ExecuteScriptAsync(SqlConnection conn, string resourceName) =>
        ExecuteSqlAsync(conn, ReadEmbeddedResource(resourceName));

    /// <summary>Splits the script into GO-delimited batches and executes each sequentially on the given connection (2-minute timeout each, since schema scripts can be slow on a cold database).</summary>
    private static async Task ExecuteSqlAsync(SqlConnection conn, string sql)
    {
        foreach (var batch in SplitOnGo(sql))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            using var cmd = new SqlCommand(batch, conn) { CommandTimeout = 120 };
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Loads a .sql file embedded as a resource (see WebAPI.csproj's EmbeddedResource entries) by its logical resource name.</summary>
    public static string ReadEmbeddedResource(string logicalName)
    {
        var assembly = typeof(DatabaseBootstrapper).Assembly;
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded migration script '{logicalName}' not found in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>GO is a sqlcmd/SSMS batch separator, not real T-SQL - SqlCommand can't execute a script containing it as a single statement.</summary>
    private static IEnumerable<string> SplitOnGo(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
}
