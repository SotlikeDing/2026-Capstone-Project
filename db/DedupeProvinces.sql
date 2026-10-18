/*
  The province seed ran more than once on some databases (staging had every province
  three times), so every province dropdown listed each one repeatedly. Keeps the lowest
  provId per name, re-points vgAvailableProvinces (the only table that references it),
  drops VG rows that become duplicates, then removes the extra provinces. Idempotent.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.provinces', N'U') IS NOT NULL
   AND EXISTS (SELECT provName FROM dbo.provinces GROUP BY provName HAVING COUNT(*) > 1)
BEGIN
    BEGIN TRANSACTION;

    ;WITH keep AS (SELECT provName, MIN(provId) AS keepId FROM dbo.provinces GROUP BY provName)
    SELECT p.provId AS oldId, k.keepId
    INTO #provMap
    FROM dbo.provinces p JOIN keep k ON k.provName = p.provName
    WHERE p.provId <> k.keepId;

    IF OBJECT_ID(N'dbo.vgAvailableProvinces', N'U') IS NOT NULL
    BEGIN
        UPDATE v SET vgapProvId = m.keepId
        FROM dbo.vgAvailableProvinces v JOIN #provMap m ON m.oldId = v.vgapProvId;

        ;WITH dupes AS (
            SELECT ROW_NUMBER() OVER (PARTITION BY vgapVgId, vgapProvId ORDER BY vgAvailableProvinceId) AS rn
            FROM dbo.vgAvailableProvinces)
        DELETE FROM dupes WHERE rn > 1;
    END;

    DELETE p FROM dbo.provinces p JOIN #provMap m ON m.oldId = p.provId;
    DROP TABLE #provMap;

    COMMIT TRANSACTION;
END;
GO
