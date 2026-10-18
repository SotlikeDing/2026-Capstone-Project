-- Run this script against the existing Mikpunt database.
-- It adds the vgpTitle, vgpRegionCovered and vgpEquipmentProvided columns
-- to dbo.vgPackages without changing or deleting existing data. These
-- columns are already expected by WebAPI.Database.VgPackages_db but were
-- missing from databases created before the columns were added to
-- db/DB_create.sql and db/Complete_create.sql.

USE Mikpunt;
GO

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'dbo.vgPackages', N'U') IS NULL
BEGIN
    THROW 50000, N'Die dbo.vgPackages-tabel bestaan nie in hierdie databasis nie.', 1;
END;

IF COL_LENGTH(N'dbo.vgPackages', N'vgpTitle') IS NULL
BEGIN
    ALTER TABLE dbo.vgPackages
        ADD vgpTitle NVARCHAR(MAX) NULL;
END;
GO

IF COL_LENGTH(N'dbo.vgPackages', N'vgpRegionCovered') IS NULL
BEGIN
    ALTER TABLE dbo.vgPackages
        ADD vgpRegionCovered NVARCHAR(MAX) NULL;
END;
GO

IF COL_LENGTH(N'dbo.vgPackages', N'vgpEquipmentProvided') IS NULL
BEGIN
    ALTER TABLE dbo.vgPackages
        ADD vgpEquipmentProvided NVARCHAR(MAX) NULL;
END;
GO

COMMIT TRANSACTION;
GO

PRINT 'vgPackages titel, streek en toerusting kolomme is beskikbaar.';
GO
