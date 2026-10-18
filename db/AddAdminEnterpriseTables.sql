-- Run this script against the existing Mikpunt database.
-- Adds real, persistent, tamper-evident storage for the Admin Dashboard:
--   adminAuditLog          (append-only; UPDATE/DELETE blocked by triggers below)
--   featureFlags
--   roleVerificationRequests
-- Idempotent: safe to re-run. Does not touch existing data.

USE Mikpunt;
GO

IF OBJECT_ID(N'dbo.users', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.users-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;
GO

-- ============================================================
-- adminAuditLog: immutable/append-only admin action ledger
-- ============================================================
IF OBJECT_ID(N'dbo.adminAuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.adminAuditLog (
        auditId INT IDENTITY(1,1) PRIMARY KEY,
        auditAdminUserId INT NOT NULL,
        auditAdminUserName NVARCHAR(200) NOT NULL,
        auditAction NVARCHAR(100) NOT NULL,
        auditTargetEntityType NVARCHAR(100) NOT NULL,
        auditTargetEntityId NVARCHAR(100) NOT NULL,
        auditReason NVARCHAR(MAX) NOT NULL,
        auditIpAddress NVARCHAR(64) NULL,
        auditUserAgent NVARCHAR(512) NULL,
        auditTimestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );

    PRINT 'adminAuditLog table created.';
END
ELSE
BEGIN
    PRINT 'adminAuditLog table already exists.';
END;
GO

-- Block modification/removal of existing audit rows so the log is tamper-evident even
-- against direct SQL access, not just through the API.
IF OBJECT_ID(N'dbo.trg_adminAuditLog_NoUpdate', N'TR') IS NULL
BEGIN
    EXEC('CREATE TRIGGER dbo.trg_adminAuditLog_NoUpdate ON dbo.adminAuditLog
          INSTEAD OF UPDATE AS
          BEGIN
              RAISERROR(''adminAuditLog is append-only: UPDATE is not permitted.'', 16, 1);
              ROLLBACK TRANSACTION;
          END');
    PRINT 'trg_adminAuditLog_NoUpdate created.';
END
ELSE
BEGIN
    PRINT 'trg_adminAuditLog_NoUpdate already exists.';
END;
GO

IF OBJECT_ID(N'dbo.trg_adminAuditLog_NoDelete', N'TR') IS NULL
BEGIN
    EXEC('CREATE TRIGGER dbo.trg_adminAuditLog_NoDelete ON dbo.adminAuditLog
          INSTEAD OF DELETE AS
          BEGIN
              RAISERROR(''adminAuditLog is append-only: DELETE is not permitted.'', 16, 1);
              ROLLBACK TRANSACTION;
          END');
    PRINT 'trg_adminAuditLog_NoDelete created.';
END
ELSE
BEGIN
    PRINT 'trg_adminAuditLog_NoDelete already exists.';
END;
GO

-- ============================================================
-- featureFlags
-- ============================================================
IF OBJECT_ID(N'dbo.featureFlags', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.featureFlags (
        flagKey NVARCHAR(150) PRIMARY KEY,
        flagDescription NVARCHAR(500) NOT NULL,
        flagIsEnabled BIT NOT NULL DEFAULT 0,
        flagLastUpdated DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        flagUpdatedBy NVARCHAR(200) NOT NULL DEFAULT 'System'
    );

    INSERT INTO dbo.featureFlags (flagKey, flagDescription, flagIsEnabled, flagLastUpdated, flagUpdatedBy) VALUES
        ('Enable_New_User_Registrations', 'Laat nuwe gebruikers toe om te registreer', 1, SYSUTCDATETIME(), 'System Admin'),
        ('Enable_Maintenance_Mode', 'Plaas stelsel in onderhoudmodus', 0, SYSUTCDATETIME(), 'System Admin'),
        ('Enable_PayFast_Sandbox', 'PayFast Sandbox toetspunt aktief', 1, SYSUTCDATETIME(), 'System Admin');

    PRINT 'featureFlags table created and seeded.';
END
ELSE
BEGIN
    PRINT 'featureFlags table already exists.';
END;
GO

-- ============================================================
-- roleVerificationRequests
-- ============================================================
IF OBJECT_ID(N'dbo.roleVerificationRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.roleVerificationRequests (
        rvId INT IDENTITY(1,1) PRIMARY KEY,
        rvUserId INT NOT NULL,
        rvUserName NVARCHAR(200) NOT NULL,
        rvUserEmail NVARCHAR(200) NOT NULL,
        rvRequestedRoleCode NVARCHAR(50) NOT NULL,
        rvRequestedRoleName NVARCHAR(150) NOT NULL,
        rvDocumentName NVARCHAR(200) NULL,
        rvDocumentReference NVARCHAR(200) NULL,
        rvDocumentUrl NVARCHAR(MAX) NULL,
        rvStatus NVARCHAR(30) NOT NULL DEFAULT 'Pending',
        rvSubmittedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        rvDecidedAt DATETIME2 NULL,
        rvRejectionReason NVARCHAR(MAX) NULL
    );

    INSERT INTO dbo.roleVerificationRequests
        (rvUserId, rvUserName, rvUserEmail, rvRequestedRoleCode, rvRequestedRoleName, rvDocumentName, rvDocumentReference, rvDocumentUrl, rvStatus, rvSubmittedAt) VALUES
        (2, 'Kobus Wessels', 'kobus.wessels@example.com', 'PH', 'Professionele Jagter (Gids)', 'PHASA Lisensie', 'PH-2026-9912', NULL, 'Pending', DATEADD(DAY, -2, SYSUTCDATETIME())),
        (3, 'Piet Botha', 'piet.botha@example.com', 'Boer', 'Plaaseienaar / Farmer', 'Plaas Titelakte', 'TD-9941', NULL, 'Pending', DATEADD(DAY, -1, SYSUTCDATETIME()));

    PRINT 'roleVerificationRequests table created and seeded.';
END
ELSE
BEGIN
    PRINT 'roleVerificationRequests table already exists.';
END;
GO
