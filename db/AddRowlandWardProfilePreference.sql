USE Mikpunt;
GO

SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.users', N'U') IS NULL
    BEGIN
        THROW 50000, N'Die dbo.users-tabel bestaan nie in hierdie databasis nie.', 1;
    END;

    IF COL_LENGTH(N'dbo.users', N'uRowlandWardEnabled') IS NULL
    BEGIN
        ALTER TABLE dbo.users
            ADD uRowlandWardEnabled BIT NOT NULL
                CONSTRAINT DF_users_uRowlandWardEnabled DEFAULT (0) WITH VALUES;
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE dbo.users
            SET uRowlandWardEnabled = 0
            WHERE uRowlandWardEnabled IS NULL;
        ';

        IF EXISTS (
            SELECT 1
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.users')
              AND name = N'uRowlandWardEnabled'
              AND is_nullable = 1
        )
        BEGIN
            EXEC sys.sp_executesql N'
                ALTER TABLE dbo.users
                    ALTER COLUMN uRowlandWardEnabled BIT NOT NULL;
            ';
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.default_constraints dc
            INNER JOIN sys.columns c
                ON c.object_id = dc.parent_object_id
               AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'dbo.users')
              AND c.name = N'uRowlandWardEnabled'
        )
        BEGIN
            EXEC sys.sp_executesql N'
                ALTER TABLE dbo.users
                    ADD CONSTRAINT DF_users_uRowlandWardEnabled
                        DEFAULT (0) FOR uRowlandWardEnabled;
            ';
        END;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

PRINT N'Rowland Ward-profielvoorkeur is beskikbaar en verstek na AF.';
GO
