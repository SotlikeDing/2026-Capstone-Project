-- Admin verification workflow for documents uploaded by roles that need docs
-- verified (PJ, PH, VG, Farmer). Until now every upload was stamped
-- udStatus = 'Active' and treated as valid forever - nobody ever reviewed it.
-- Adds:
--   userDocumentation.udVerifiedBy/udVerifiedAt/udRejectionReason - who decided,
--     when, and why (if rejected).
--   udStatus repurposed as the verification state ('Pending'/'Approved'/'Rejected'),
--     matching the vocabulary db/Complete_create.sql seed data already used.
--   documentAuditLog.notes - holds the admin's decision reason for
--     Approved/Rejected entries (mirrors adminAuditLog's Reason column).
-- No index on udStatus (see below - it's NVARCHAR(MAX), which can't be a key column);
-- the admin queue is low-volume enough that a table scan is fine.
-- Idempotent: safe to re-run. Backfills existing 'Active' rows to 'Approved' so no
-- currently-visible role silently disappears from search the moment this ships.

GO

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.userDocumentation', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.userDocumentation-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;
GO

-- ============================================================
-- userDocumentation additions
-- ============================================================
IF COL_LENGTH('dbo.userDocumentation', 'udVerifiedBy') IS NULL
    ALTER TABLE dbo.userDocumentation ADD udVerifiedBy INT NULL;
GO

IF COL_LENGTH('dbo.userDocumentation', 'udVerifiedAt') IS NULL
    ALTER TABLE dbo.userDocumentation ADD udVerifiedAt DATETIME2 NULL;
GO

IF COL_LENGTH('dbo.userDocumentation', 'udRejectionReason') IS NULL
    ALTER TABLE dbo.userDocumentation ADD udRejectionReason NVARCHAR(MAX) NULL;
GO

-- Backfill: everything the old code stamped 'Active' was, in effect, accepted
-- without review - grandfather it in as 'Approved' rather than yanking every
-- existing PJ/PH/VG/Farmer out of public search on deploy day.
UPDATE dbo.userDocumentation
SET udStatus = 'Approved', udVerifiedAt = udUploadedTimestamp
WHERE udStatus = 'Active' AND udIsDeleted = 0;
GO

-- No index on udStatus: it's NVARCHAR(MAX) (legacy column type, not worth widening
-- just for this), which SQL Server won't allow as an index key. The admin queue is
-- low-volume enough (manual document review) that a table scan is fine.

-- ============================================================
-- documentAuditLog.notes - admin decision reason
-- ============================================================
IF OBJECT_ID(N'dbo.documentAuditLog', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.documentAuditLog-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;
GO

IF COL_LENGTH('dbo.documentAuditLog', 'notes') IS NULL
    ALTER TABLE dbo.documentAuditLog ADD notes NVARCHAR(MAX) NULL;
GO
