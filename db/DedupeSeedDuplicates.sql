/*
  The seed data ran more than once on some databases (staging): every document type,
  ten users (same e-mail, no roles) and fourteen user-role rows exist twice. Keeps the
  lowest id of each, re-points the rows that reference a duplicate where that's safe,
  removes the duplicates, then adds unique indexes so a re-run seed fails loudly instead
  of silently duplicating again. Idempotent; a duplicate that is still referenced by
  something we can't re-point is left alone (and its unique index is then skipped).
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- nvarchar(max) columns can't be indexed: narrow dtName / uEmail first (keeping their NULL-ability)
-- when every value fits. 320 is the longest legal e-mail address.
IF OBJECT_ID(N'dbo.documentType', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.documentType') AND name = N'dtName' AND max_length = -1)
   AND NOT EXISTS (SELECT 1 FROM dbo.documentType WHERE LEN(dtName) > 200)
BEGIN
    DECLARE @dtNull NVARCHAR(10) = (SELECT CASE is_nullable WHEN 1 THEN N'NULL' ELSE N'NOT NULL' END
                                    FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.documentType') AND name = N'dtName');
    EXEC (N'ALTER TABLE dbo.documentType ALTER COLUMN dtName NVARCHAR(200) ' + @dtNull);
END;
GO

IF OBJECT_ID(N'dbo.users', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.users') AND name = N'uEmail' AND max_length = -1)
   AND NOT EXISTS (SELECT 1 FROM dbo.users WHERE LEN(uEmail) > 320)
BEGIN
    DECLARE @mailNull NVARCHAR(10) = (SELECT CASE is_nullable WHEN 1 THEN N'NULL' ELSE N'NOT NULL' END
                                      FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.users') AND name = N'uEmail');
    EXEC (N'ALTER TABLE dbo.users ALTER COLUMN uEmail NVARCHAR(320) ' + @mailNull);
END;
GO

-- ------------------------------------------------------------------ document types
IF OBJECT_ID(N'dbo.documentType', N'U') IS NOT NULL
   AND EXISTS (SELECT dtName FROM dbo.documentType GROUP BY dtName HAVING COUNT(*) > 1)
BEGIN
    BEGIN TRANSACTION;

    ;WITH keep AS (SELECT dtName, MIN(dtId) AS keepId FROM dbo.documentType GROUP BY dtName)
    SELECT d.dtId AS oldId, k.keepId
    INTO #dtMap
    FROM dbo.documentType d JOIN keep k ON k.dtName = d.dtName
    WHERE d.dtId <> k.keepId;

    UPDATE ud SET udDtId = m.keepId
    FROM dbo.userDocumentation ud JOIN #dtMap m ON m.oldId = ud.udDtId;

    IF OBJECT_ID(N'dbo.roleDocumentRequirements', N'U') IS NOT NULL
    BEGIN
        UPDATE r SET documentTypeId = m.keepId
        FROM dbo.roleDocumentRequirements r JOIN #dtMap m ON m.oldId = r.documentTypeId
        WHERE NOT EXISTS (SELECT 1 FROM dbo.roleDocumentRequirements x
                          WHERE x.roleCode = r.roleCode AND x.documentTypeId = m.keepId);
        DELETE r FROM dbo.roleDocumentRequirements r JOIN #dtMap m ON m.oldId = r.documentTypeId;
    END;

    DELETE d FROM dbo.documentType d JOIN #dtMap m ON m.oldId = d.dtId;
    DROP TABLE #dtMap;

    COMMIT TRANSACTION;
END;
GO

IF OBJECT_ID(N'dbo.documentType', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_documentType_dtName' AND object_id = OBJECT_ID(N'dbo.documentType'))
   AND NOT EXISTS (SELECT dtName FROM dbo.documentType GROUP BY dtName HAVING COUNT(*) > 1)
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.documentType') AND name = N'dtName' AND max_length <> -1)
    CREATE UNIQUE INDEX UX_documentType_dtName ON dbo.documentType (dtName);
GO

-- ------------------------------------------------------------------ duplicate user roles
-- Same user + same role twice. Only unreferenced duplicates are removed: every table that
-- points at userRoles has a foreign key, so a referenced one makes its DELETE fail and is kept.
IF OBJECT_ID(N'dbo.userRoles', N'U') IS NOT NULL
BEGIN
    DECLARE @urId INT;
    DECLARE dup CURSOR LOCAL FAST_FORWARD FOR
        SELECT r.userRoleId FROM dbo.userRoles r
        WHERE EXISTS (SELECT 1 FROM dbo.userRoles k
                      WHERE k.urUserId = r.urUserId AND k.urRoleId = r.urRoleId AND k.userRoleId < r.userRoleId);
    OPEN dup;
    FETCH NEXT FROM dup INTO @urId;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            DELETE FROM dbo.userRoles WHERE userRoleId = @urId;
        END TRY
        BEGIN CATCH
            PRINT CONCAT('Kept duplicate userRoleId ', @urId, ': ', ERROR_MESSAGE());
        END CATCH;
        FETCH NEXT FROM dup INTO @urId;
    END;
    CLOSE dup;
    DEALLOCATE dup;
END;
GO

IF OBJECT_ID(N'dbo.userRoles', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_userRoles_user_role' AND object_id = OBJECT_ID(N'dbo.userRoles'))
   AND NOT EXISTS (SELECT 1 FROM dbo.userRoles GROUP BY urUserId, urRoleId HAVING COUNT(*) > 1)
    CREATE UNIQUE INDEX UX_userRoles_user_role ON dbo.userRoles (urUserId, urRoleId);
GO

-- ------------------------------------------------------------------ duplicate users
-- Same e-mail twice. The keeper is the account with roles (else the lowest id); a role-less
-- duplicate's chat memberships move to the keeper, then the duplicate is removed if nothing
-- else references it.
IF OBJECT_ID(N'dbo.users', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.users WHERE uEmail IS NOT NULL GROUP BY uEmail HAVING COUNT(*) > 1)
BEGIN
    ;WITH ranked AS (
        SELECT u.userId, u.uEmail,
               ROW_NUMBER() OVER (PARTITION BY u.uEmail
                                  ORDER BY CASE WHEN EXISTS (SELECT 1 FROM dbo.userRoles r WHERE r.urUserId = u.userId) THEN 0 ELSE 1 END,
                                           u.userId) AS rn
        FROM dbo.users u WHERE u.uEmail IS NOT NULL)
    SELECT d.userId AS oldId, k.userId AS keepId
    INTO #userMap
    FROM ranked d JOIN ranked k ON k.uEmail = d.uEmail AND k.rn = 1
    WHERE d.rn > 1
      AND NOT EXISTS (SELECT 1 FROM dbo.userRoles r WHERE r.urUserId = d.userId);   -- never drop an account that has roles

    IF OBJECT_ID(N'dbo.chatParticipants', N'U') IS NOT NULL
    BEGIN
        UPDATE p SET cpUserId = m.keepId
        FROM dbo.chatParticipants p JOIN #userMap m ON m.oldId = p.cpUserId
        WHERE NOT EXISTS (SELECT 1 FROM dbo.chatParticipants x WHERE x.cpChatId = p.cpChatId AND x.cpUserId = m.keepId);
        DELETE p FROM dbo.chatParticipants p JOIN #userMap m ON m.oldId = p.cpUserId;
    END;

    DECLARE @oldId INT;
    DECLARE dupUser CURSOR LOCAL FAST_FORWARD FOR SELECT oldId FROM #userMap;
    OPEN dupUser;
    FETCH NEXT FROM dupUser INTO @oldId;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            DELETE FROM dbo.users WHERE userId = @oldId;
        END TRY
        BEGIN CATCH
            PRINT CONCAT('Kept duplicate userId ', @oldId, ': ', ERROR_MESSAGE());
        END CATCH;
        FETCH NEXT FROM dupUser INTO @oldId;
    END;
    CLOSE dupUser;
    DEALLOCATE dupUser;
    DROP TABLE #userMap;
END;
GO

IF OBJECT_ID(N'dbo.users', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_users_uEmail' AND object_id = OBJECT_ID(N'dbo.users'))
   AND NOT EXISTS (SELECT 1 FROM dbo.users WHERE uEmail IS NOT NULL GROUP BY uEmail HAVING COUNT(*) > 1)
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.users') AND name = N'uEmail' AND max_length <> -1)
    CREATE UNIQUE INDEX UX_users_uEmail ON dbo.users (uEmail) WHERE uEmail IS NOT NULL;
GO
