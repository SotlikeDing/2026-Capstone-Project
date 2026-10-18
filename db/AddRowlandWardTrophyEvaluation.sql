-- Run this script against the existing Mikpunt database.
-- It adds versioned, trophy-specific Rowland Ward evaluations without changing legacy trophies.

USE Mikpunt;
GO

IF OBJECT_ID(N'dbo.trophies', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.trophies-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;
GO

IF COL_LENGTH(N'dbo.trophies', N'tRowlandWardClassId') IS NULL
BEGIN
    ALTER TABLE dbo.trophies
        ADD tRowlandWardClassId NVARCHAR(160) NULL;
END;
GO

IF COL_LENGTH(N'dbo.trophies', N'tRowlandWardSpeciesNameAf') IS NULL
BEGIN
    ALTER TABLE dbo.trophies
        ADD tRowlandWardSpeciesNameAf NVARCHAR(160) NULL;
END;
GO

IF OBJECT_ID(N'dbo.trophyRowlandWardEvaluations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.trophyRowlandWardEvaluations (
        trophyRowlandWardEvaluationId BIGINT IDENTITY(1,1) PRIMARY KEY,
        trweTrophyId INT NOT NULL,
        trweIsCurrent BIT NOT NULL CONSTRAINT DF_trophyRowlandWardEvaluations_IsCurrent DEFAULT (1),
        trweSchemaVersion INT NOT NULL,
        trweClassId NVARCHAR(160) NOT NULL,
        trweDisplayNameAf NVARCHAR(160) NOT NULL,
        trweMethodCode NVARCHAR(20) NOT NULL,
        trweMeasurementProfileId NVARCHAR(80) NOT NULL,
        trweSourceVersionId NVARCHAR(120) NOT NULL,
        trweEditionReleaseIdentity NVARCHAR(160) NOT NULL,
        trweSourceMasterSha256 NVARCHAR(64) NULL,
        trweEvaluatedAtUtc DATETIME2(7) NOT NULL,
        trweScoreCanonicalNumerator BIGINT NULL,
        trweScoreCanonicalDenominator BIGINT NULL,
        trweScoreCanonicalUnitCode NVARCHAR(20) NULL,
        trweScoreDisplayNumerator BIGINT NULL,
        trweScoreDisplayDenominator BIGINT NULL,
        trweScoreDisplayUnitCode NVARCHAR(20) NULL,
        trweMinimumState NVARCHAR(40) NOT NULL,
        trweMinimumCanonicalNumerator BIGINT NULL,
        trweMinimumCanonicalDenominator BIGINT NULL,
        trweMinimumCanonicalUnitCode NVARCHAR(20) NULL,
        trweMinimumDisplayNumerator BIGINT NULL,
        trweMinimumDisplayDenominator BIGINT NULL,
        trweMinimumDisplayUnitCode NVARCHAR(20) NULL,
        trweWorldRecordState NVARCHAR(40) NOT NULL,
        trweWorldRecordCanonicalNumerator BIGINT NULL,
        trweWorldRecordCanonicalDenominator BIGINT NULL,
        trweWorldRecordCanonicalUnitCode NVARCHAR(20) NULL,
        trweWorldRecordDisplayNumerator BIGINT NULL,
        trweWorldRecordDisplayDenominator BIGINT NULL,
        trweWorldRecordDisplayUnitCode NVARCHAR(20) NULL,
        trweQualificationState NVARCHAR(60) NOT NULL,
        trweMinimumReached BIT NULL,
        trweScoreExpressionJson NVARCHAR(MAX) NOT NULL,
        trweMinimumExpressionJson NVARCHAR(MAX) NOT NULL,
        trweMinimumComparisonOperator NVARCHAR(40) NOT NULL,
        trweCreatedTimestamp DATETIME2(7) NOT NULL
            CONSTRAINT DF_trophyRowlandWardEvaluations_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_trophyRowlandWardEvaluations_Trophy
            FOREIGN KEY (trweTrophyId) REFERENCES dbo.trophies(trophyId) ON DELETE CASCADE,
        CONSTRAINT CK_trophyRowlandWardEvaluations_SchemaVersion
            CHECK (trweSchemaVersion > 0),
        CONSTRAINT CK_trophyRowlandWardEvaluations_Score
            CHECK (
                (trweScoreCanonicalNumerator IS NULL
                    AND trweScoreCanonicalDenominator IS NULL
                    AND trweScoreCanonicalUnitCode IS NULL
                    AND trweScoreDisplayNumerator IS NULL
                    AND trweScoreDisplayDenominator IS NULL
                    AND trweScoreDisplayUnitCode IS NULL)
                OR
                (trweScoreCanonicalNumerator IS NOT NULL
                    AND trweScoreCanonicalDenominator > 0
                    AND trweScoreCanonicalUnitCode IS NOT NULL
                    AND trweScoreDisplayNumerator IS NOT NULL
                    AND trweScoreDisplayDenominator > 0
                    AND trweScoreDisplayUnitCode IS NOT NULL)
            ),
        CONSTRAINT CK_trophyRowlandWardEvaluations_Minimum
            CHECK (
                (trweMinimumCanonicalNumerator IS NULL
                    AND trweMinimumCanonicalDenominator IS NULL
                    AND trweMinimumCanonicalUnitCode IS NULL
                    AND trweMinimumDisplayNumerator IS NULL
                    AND trweMinimumDisplayDenominator IS NULL
                    AND trweMinimumDisplayUnitCode IS NULL)
                OR
                (trweMinimumCanonicalNumerator IS NOT NULL
                    AND trweMinimumCanonicalDenominator > 0
                    AND trweMinimumCanonicalUnitCode IS NOT NULL
                    AND trweMinimumDisplayNumerator IS NOT NULL
                    AND trweMinimumDisplayDenominator > 0
                    AND trweMinimumDisplayUnitCode IS NOT NULL)
            ),
        CONSTRAINT CK_trophyRowlandWardEvaluations_WorldRecord
            CHECK (
                (trweWorldRecordCanonicalNumerator IS NULL
                    AND trweWorldRecordCanonicalDenominator IS NULL
                    AND trweWorldRecordCanonicalUnitCode IS NULL
                    AND trweWorldRecordDisplayNumerator IS NULL
                    AND trweWorldRecordDisplayDenominator IS NULL
                    AND trweWorldRecordDisplayUnitCode IS NULL)
                OR
                (trweWorldRecordCanonicalNumerator IS NOT NULL
                    AND trweWorldRecordCanonicalDenominator > 0
                    AND trweWorldRecordCanonicalUnitCode IS NOT NULL
                    AND trweWorldRecordDisplayNumerator IS NOT NULL
                    AND trweWorldRecordDisplayDenominator > 0
                    AND trweWorldRecordDisplayUnitCode IS NOT NULL)
            )
    );
END;
GO

IF COL_LENGTH(N'dbo.trophyRowlandWardEvaluations', N'trweSourceMasterSha256') IS NULL
BEGIN
    ALTER TABLE dbo.trophyRowlandWardEvaluations
        ADD trweSourceMasterSha256 NVARCHAR(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.trophyRowlandWardEvaluations')
      AND name = N'UX_trophyRowlandWardEvaluations_Current'
)
BEGIN
    CREATE UNIQUE INDEX UX_trophyRowlandWardEvaluations_Current
        ON dbo.trophyRowlandWardEvaluations(trweTrophyId)
        WHERE trweIsCurrent = 1;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.trophyRowlandWardEvaluations')
      AND name = N'IX_trophyRowlandWardEvaluations_Trophy'
)
BEGIN
    CREATE INDEX IX_trophyRowlandWardEvaluations_Trophy
        ON dbo.trophyRowlandWardEvaluations(trweTrophyId, trweEvaluatedAtUtc DESC);
END;
GO

IF OBJECT_ID(N'dbo.trophyRowlandWardMeasurements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.trophyRowlandWardMeasurements (
        trophyRowlandWardMeasurementId BIGINT IDENTITY(1,1) PRIMARY KEY,
        trwmEvaluationId BIGINT NOT NULL,
        trwmFieldId NVARCHAR(100) NOT NULL,
        trwmLabelAf NVARCHAR(160) NOT NULL,
        trwmKind NVARCHAR(20) NOT NULL,
        trwmRole NVARCHAR(60) NOT NULL,
        trwmInputNumerator BIGINT NULL,
        trwmInputDenominator BIGINT NULL,
        trwmInputUnitCode NVARCHAR(20) NULL,
        trwmCanonicalNumerator BIGINT NULL,
        trwmCanonicalDenominator BIGINT NULL,
        trwmCanonicalUnitCode NVARCHAR(20) NULL,
        trwmScoringNumerator BIGINT NULL,
        trwmScoringDenominator BIGINT NULL,
        trwmScoringUnitCode NVARCHAR(20) NULL,
        trwmIntegerValue BIGINT NULL,
        trwmBooleanValue BIT NULL,
        CONSTRAINT FK_trophyRowlandWardMeasurements_Evaluation
            FOREIGN KEY (trwmEvaluationId)
            REFERENCES dbo.trophyRowlandWardEvaluations(trophyRowlandWardEvaluationId)
            ON DELETE CASCADE,
        CONSTRAINT UQ_trophyRowlandWardMeasurements_EvaluationField
            UNIQUE (trwmEvaluationId, trwmFieldId),
        CONSTRAINT CK_trophyRowlandWardMeasurements_ValueShape
            CHECK (
                (trwmKind = N'rational'
                    AND trwmInputNumerator IS NOT NULL
                    AND trwmInputDenominator > 0
                    AND trwmInputUnitCode IS NOT NULL
                    AND trwmCanonicalNumerator IS NOT NULL
                    AND trwmCanonicalDenominator > 0
                    AND trwmCanonicalUnitCode IS NOT NULL
                    AND trwmScoringNumerator IS NOT NULL
                    AND trwmScoringDenominator > 0
                    AND trwmScoringUnitCode IS NOT NULL
                    AND trwmIntegerValue IS NULL
                    AND trwmBooleanValue IS NULL)
                OR
                (trwmKind = N'integer'
                    AND trwmInputNumerator IS NULL
                    AND trwmInputDenominator IS NULL
                    AND trwmInputUnitCode IS NULL
                    AND trwmCanonicalNumerator IS NULL
                    AND trwmCanonicalDenominator IS NULL
                    AND trwmCanonicalUnitCode IS NULL
                    AND trwmScoringNumerator IS NULL
                    AND trwmScoringDenominator IS NULL
                    AND trwmScoringUnitCode IS NULL
                    AND trwmIntegerValue IS NOT NULL
                    AND trwmBooleanValue IS NULL)
                OR
                (trwmKind = N'boolean'
                    AND trwmInputNumerator IS NULL
                    AND trwmInputDenominator IS NULL
                    AND trwmInputUnitCode IS NULL
                    AND trwmCanonicalNumerator IS NULL
                    AND trwmCanonicalDenominator IS NULL
                    AND trwmCanonicalUnitCode IS NULL
                    AND trwmScoringNumerator IS NULL
                    AND trwmScoringDenominator IS NULL
                    AND trwmScoringUnitCode IS NULL
                    AND trwmIntegerValue IS NULL
                    AND trwmBooleanValue IS NOT NULL)
            )
    );
END;
GO

PRINT 'Rowland Ward-trofeedatamodel is beskikbaar.';
GO
