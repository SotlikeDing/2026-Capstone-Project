-- Real document upload/management for PJ, PH, VG and Farmer roles (previously these
-- roles only had free-text "license number" fields - no actual file was ever stored).
-- Adds:
--   roleDocumentRequirements - which document types are mandatory for which role;
--     the profile must always have an active document against every required type,
--     enforced in Database/UserDocuments_db.cs (required documents can be replaced,
--     never bare-deleted).
--   userDocumentation additions - udUploadedBy (actor) and udIsDeleted (soft delete,
--     so the audit trail below can still join back to the row's metadata).
--   documentAuditLog - immutable/append-only trail of every upload/replace/delete
--     (same INSTEAD OF trigger pattern as adminAuditLog/ratingHistory).
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
-- New documentType rows this feature needs, beyond what already exists
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM documentType WHERE dtName = N'BTW / Belasting Registrasie')
    INSERT INTO documentType (dtName, dtNumber) VALUES (N'BTW / Belasting Registrasie', N'TAX-VAT-REG');
IF NOT EXISTS (SELECT 1 FROM documentType WHERE dtName = N'Provinsiale Uitrusterlisensie')
    INSERT INTO documentType (dtName, dtNumber) VALUES (N'Provinsiale Uitrusterlisensie', N'PROV-OUTFITTER-LIC');
IF NOT EXISTS (SELECT 1 FROM documentType WHERE dtName = N'Vereniging Lidmaatskapsertifikaat')
    INSERT INTO documentType (dtName, dtNumber) VALUES (N'Vereniging Lidmaatskapsertifikaat', N'ASSOC-MEMBERSHIP');
IF NOT EXISTS (SELECT 1 FROM documentType WHERE dtName = N'Vuurwapenbevoegdheidslisensie')
    INSERT INTO documentType (dtName, dtNumber) VALUES (N'Vuurwapenbevoegdheidslisensie', N'FIREARM-COMPETENCY');
IF NOT EXISTS (SELECT 1 FROM documentType WHERE dtName = N'SACAA Registrasie')
    INSERT INTO documentType (dtName, dtNumber) VALUES (N'SACAA Registrasie', N'SACAA-REG');
IF NOT EXISTS (SELECT 1 FROM documentType WHERE dtName = N'RPL Lisensie')
    INSERT INTO documentType (dtName, dtNumber) VALUES (N'RPL Lisensie', N'RPL-LIC');
-- Already exist from earlier seed data (db/ToetsData.sql), reused as-is:
--   'Besigheidsregistrasie' (PJ + Farmer), 'Eerstehulp Sertifikaat' (PH), 'Drone Lisensie' (VG)
GO

-- ============================================================
-- roleDocumentRequirements: which document types are mandatory per role
-- ============================================================
IF OBJECT_ID(N'dbo.roleDocumentRequirements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.roleDocumentRequirements (
        requirementId INT IDENTITY(1,1) PRIMARY KEY,
        roleCode NVARCHAR(20) NOT NULL,
        documentTypeId INT NOT NULL FOREIGN KEY REFERENCES documentType(dtId),
        isRequired BIT NOT NULL DEFAULT 1,
        CONSTRAINT UQ_roleDocumentRequirements UNIQUE (roleCode, documentTypeId)
    );
    PRINT 'roleDocumentRequirements table created.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'PJ' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Besigheidsregistrasie'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('PJ', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Besigheidsregistrasie'));
IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'PJ' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'BTW / Belasting Registrasie'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('PJ', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'BTW / Belasting Registrasie'));
IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'PJ' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Provinsiale Uitrusterlisensie'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('PJ', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Provinsiale Uitrusterlisensie'));

IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'PH' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Vereniging Lidmaatskapsertifikaat'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('PH', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Vereniging Lidmaatskapsertifikaat'));
IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'PH' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Eerstehulp Sertifikaat'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('PH', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Eerstehulp Sertifikaat'));
IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'PH' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Vuurwapenbevoegdheidslisensie'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('PH', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Vuurwapenbevoegdheidslisensie'));

IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'VG' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'SACAA Registrasie'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('VG', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'SACAA Registrasie'));
IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'VG' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'RPL Lisensie'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('VG', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'RPL Lisensie'));
IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'VG' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Drone Lisensie'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('VG', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Drone Lisensie'));

IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'FARMER' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Besigheidsregistrasie'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId) VALUES ('FARMER', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Besigheidsregistrasie'));
GO

-- One optional, non-required slot per role so "delete" has something real to act on
-- without ever being able to drop a profile below its required minimum (the required
-- rows above can only ever be replaced - see UserDocuments_db.DeleteDocument's guard).
IF NOT EXISTS (SELECT 1 FROM documentType WHERE dtName = N'Addisionele Ondersteunende Dokument')
    INSERT INTO documentType (dtName, dtNumber) VALUES (N'Addisionele Ondersteunende Dokument', N'SUPPLEMENTARY');
GO

IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'PJ' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Addisionele Ondersteunende Dokument'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId, isRequired) VALUES ('PJ', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Addisionele Ondersteunende Dokument'), 0);
IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'PH' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Addisionele Ondersteunende Dokument'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId, isRequired) VALUES ('PH', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Addisionele Ondersteunende Dokument'), 0);
IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'VG' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Addisionele Ondersteunende Dokument'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId, isRequired) VALUES ('VG', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Addisionele Ondersteunende Dokument'), 0);
IF NOT EXISTS (SELECT 1 FROM roleDocumentRequirements WHERE roleCode = 'FARMER' AND documentTypeId = (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Addisionele Ondersteunende Dokument'))
    INSERT INTO roleDocumentRequirements (roleCode, documentTypeId, isRequired) VALUES ('FARMER', (SELECT MIN(dtId) FROM documentType WHERE dtName = N'Addisionele Ondersteunende Dokument'), 0);
GO

-- ============================================================
-- userDocumentation additions
-- ============================================================
IF COL_LENGTH('dbo.userDocumentation', 'udUploadedBy') IS NULL
    ALTER TABLE dbo.userDocumentation ADD udUploadedBy INT NULL;
GO

IF COL_LENGTH('dbo.userDocumentation', 'udIsDeleted') IS NULL
    ALTER TABLE dbo.userDocumentation ADD udIsDeleted BIT NOT NULL DEFAULT 0;
GO

-- ============================================================
-- documentAuditLog: immutable/append-only trail
-- ============================================================
IF OBJECT_ID(N'dbo.documentAuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.documentAuditLog (
        logId INT IDENTITY(1,1) PRIMARY KEY,
        documentId INT NOT NULL FOREIGN KEY REFERENCES userDocumentation(udId),
        userRoleId INT NOT NULL,
        actorUserId INT NOT NULL,
        actionType NVARCHAR(20) NOT NULL, -- 'Created', 'Replaced', 'Deleted'
        documentTypeName NVARCHAR(200) NOT NULL,
        fileName NVARCHAR(300) NULL,
        createdAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    PRINT 'documentAuditLog table created.';
END;
GO

IF OBJECT_ID(N'dbo.trg_documentAuditLog_NoUpdate', N'TR') IS NULL
BEGIN
    EXEC('CREATE TRIGGER dbo.trg_documentAuditLog_NoUpdate ON dbo.documentAuditLog
          INSTEAD OF UPDATE AS
          BEGIN
              RAISERROR(''documentAuditLog is append-only: UPDATE is not permitted.'', 16, 1);
              ROLLBACK TRANSACTION;
          END');
    PRINT 'trg_documentAuditLog_NoUpdate created.';
END;
GO

IF OBJECT_ID(N'dbo.trg_documentAuditLog_NoDelete', N'TR') IS NULL
BEGIN
    EXEC('CREATE TRIGGER dbo.trg_documentAuditLog_NoDelete ON dbo.documentAuditLog
          INSTEAD OF DELETE AS
          BEGIN
              RAISERROR(''documentAuditLog is append-only: DELETE is not permitted.'', 16, 1);
              ROLLBACK TRANSACTION;
          END');
    PRINT 'trg_documentAuditLog_NoDelete created.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM systemSettings WHERE settingKey = 'DocumentRateLimitPermitLimit')
    INSERT INTO systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('DocumentRateLimitPermitLimit', '20', 'Maksimum dokument-oplaai/vervang/skrap aksies binne die tydvenster, per gebruiker', 0);
GO

IF NOT EXISTS (SELECT 1 FROM systemSettings WHERE settingKey = 'DocumentRateLimitWindowSeconds')
    INSERT INTO systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('DocumentRateLimitWindowSeconds', '60', 'Tydvenster (sekondes) vir die dokument tempoperk', 0);
GO

IF NOT EXISTS (SELECT 1 FROM systemSettings WHERE settingKey = 'DocumentReadRateLimitPermitLimit')
    INSERT INTO systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('DocumentReadRateLimitPermitLimit', '60', 'Maksimum publieke dokument-vereiste/opsomming aanvrae binne die tydvenster, per IP', 0);
GO

IF NOT EXISTS (SELECT 1 FROM systemSettings WHERE settingKey = 'DocumentReadRateLimitWindowSeconds')
    INSERT INTO systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('DocumentReadRateLimitWindowSeconds', '60', 'Tydvenster (sekondes) vir die publieke dokument-leestempoperk', 0);
GO

-- Defense-in-depth against the identified double-submit/two-tab race: even if the
-- application-layer MERGE upsert in UserDocuments_db.UploadDocument is ever bypassed
-- or mis-ported, the database itself refuses a second active document of the same
-- type for the same role instance.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_userDocumentation_ActiveRoleType')
    CREATE UNIQUE INDEX UQ_userDocumentation_ActiveRoleType
    ON dbo.userDocumentation (udUserRole, udDtId)
    WHERE udIsDeleted = 0 AND udUserRole IS NOT NULL;
GO
