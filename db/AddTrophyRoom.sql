-- Run this script against the existing Mikpunt database.
-- It adds the Trophy Room table without changing or deleting existing data.

USE Mikpunt;
GO

IF OBJECT_ID(N'dbo.users', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.users-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;

IF OBJECT_ID(N'dbo.trophies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.trophies (
        trophyId INT IDENTITY(1,1) PRIMARY KEY,
        tOwnerUserId INT NOT NULL FOREIGN KEY REFERENCES dbo.users(userId),
        tSpecies NVARCHAR(120) NOT NULL,
        tPhotoPath NVARCHAR(500) NOT NULL,
        tAntlerSize DECIMAL(10, 2) NULL, -- centimetres
        tCarcassWeight DECIMAL(10, 2) NULL, -- kilograms
        tFirearmType NVARCHAR(120) NOT NULL,
        tHarvestDate DATE NOT NULL,
        tLocationText NVARCHAR(200) NOT NULL,
        tNotes NVARCHAR(MAX) NULL,
        tIsPublic BIT NOT NULL DEFAULT 0,
        tShotDistanceMeters INT NULL,
        tCreatedTimestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        tUpdatedTimestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_trophies_tShotDistanceMeters_NonNegative
            CHECK (tShotDistanceMeters IS NULL OR tShotDistanceMeters >= 0)
    );

    PRINT 'Trophy Room table is available.';
END
ELSE
BEGIN
    PRINT 'Trophy Room table already exists.';
END;
GO

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
