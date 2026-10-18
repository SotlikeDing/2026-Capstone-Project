-- Run this script against the existing Mikpunt database.
-- It adds optional whole-metre shot distance without changing existing trophies.

USE Mikpunt;
GO

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'dbo.trophies', N'U') IS NULL
BEGIN
    THROW 50000, N'Die dbo.trophies-tabel bestaan nie in hierdie databasis nie.', 1;
END;

IF COL_LENGTH(N'dbo.trophies', N'tShotDistanceMeters') IS NULL
BEGIN
    ALTER TABLE dbo.trophies
        ADD tShotDistanceMeters INT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.trophies')
      AND name = N'CK_trophies_tShotDistanceMeters_NonNegative'
)
BEGIN
    ALTER TABLE dbo.trophies WITH CHECK
        ADD CONSTRAINT CK_trophies_tShotDistanceMeters_NonNegative
        CHECK (tShotDistanceMeters IS NULL OR tShotDistanceMeters >= 0);

    ALTER TABLE dbo.trophies
        CHECK CONSTRAINT CK_trophies_tShotDistanceMeters_NonNegative;
END;
GO

COMMIT TRANSACTION;
GO

PRINT 'Opsionele skootafstand is beskikbaar.';
GO
