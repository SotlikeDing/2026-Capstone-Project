-- Optional Trophy Room demonstration data.
-- Run this only after DB_create.sql and AddTrophyRoom.sql have been run.
-- The script adds no user data and is safe to run more than once.

USE Mikpunt;
GO

DECLARE @FirstUserId INT = (SELECT MIN(userId) FROM dbo.users);
DECLARE @SecondUserId INT = (
    SELECT MIN(userId) FROM dbo.users WHERE userId > @FirstUserId
);
DECLARE @ThirdUserId INT = (
    SELECT MIN(userId) FROM dbo.users WHERE userId > @SecondUserId
);

IF @FirstUserId IS NULL OR @SecondUserId IS NULL OR @ThirdUserId IS NULL
BEGIN
    RAISERROR('Minstens drie bestaande gebruikers word vir die demonstrasiedata benodig.', 16, 1);
    RETURN;
END;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.trophies
    WHERE tOwnerUserId = @FirstUserId
      AND tSpecies = 'Koedoe'
      AND tHarvestDate = '2026-06-18'
)
BEGIN
    INSERT INTO dbo.trophies (
        tOwnerUserId, tSpecies, tPhotoPath, tAntlerSize, tCarcassWeight,
        tFirearmType, tHarvestDate, tLocationText, tNotes, tIsPublic
    )
    VALUES (
        @FirstUserId, 'Koedoe', 'Media/trophies/koedoe-demo.jpg', 104.50, 166.80,
        '.308 Winchester', '2026-06-18', 'Waterberg-jaggebied',
        'Demonstrasie-trofee vir die Trofeekamer.', 1
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.trophies
    WHERE tOwnerUserId = @SecondUserId
      AND tSpecies = 'Springbok'
      AND tHarvestDate = '2026-05-09'
)
BEGIN
    INSERT INTO dbo.trophies (
        tOwnerUserId, tSpecies, tPhotoPath, tAntlerSize, tCarcassWeight,
        tFirearmType, tHarvestDate, tLocationText, tNotes, tIsPublic
    )
    VALUES (
        @SecondUserId, 'Springbok', 'Media/trophies/springbok-demo.jpg', 36.75, 21.40,
        '6.5 Creedmoor', '2026-05-09', 'Karoo-jaggebied',
        NULL, 1
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.trophies
    WHERE tOwnerUserId = @ThirdUserId
      AND tSpecies = 'Eland'
      AND tHarvestDate = '2026-04-02'
)
BEGIN
    INSERT INTO dbo.trophies (
        tOwnerUserId, tSpecies, tPhotoPath, tAntlerSize, tCarcassWeight,
        tFirearmType, tHarvestDate, tLocationText, tNotes, tIsPublic
    )
    VALUES (
        @ThirdUserId, 'Eland', 'Media/trophies/eland-demo.jpg', NULL, 282.40,
        '.375 H&H', '2026-04-02', 'Suidkus-jaggebied',
        'Privaat demonstrasie-trofee.', 0
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.trophies
    WHERE tOwnerUserId = @FirstUserId
      AND tSpecies = 'Rooibok'
      AND tHarvestDate = '2026-03-21'
)
BEGIN
    INSERT INTO dbo.trophies (
        tOwnerUserId, tSpecies, tPhotoPath, tAntlerSize, tCarcassWeight,
        tFirearmType, tHarvestDate, tLocationText, tNotes, tIsPublic
    )
    VALUES (
        @FirstUserId, 'Rooibok', 'Media/trophies/rooibok-demo.jpg', NULL, 18.50,
        '7x57 Mauser', '2026-03-21', 'Bosveld-jaggebied',
        NULL, 1
    );
END;
