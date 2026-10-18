USE Mikpunt;
SET QUOTED_IDENTIFIER ON;   -- users has a filtered unique index on uEmail
GO

DECLARE @hash VARCHAR(255) = 'ef92b778bafe771e89245b89ecbc08a44a4e166c06659911881f383d4473e94f'; -- 'password123'
DECLARE @userId INT;
DECLARE @roleId INT;
DECLARE @urId INT;

-- 1. Jagter
IF NOT EXISTS(SELECT 1 FROM users WHERE uEmail = 'jagter@test.com')
BEGIN
    INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
    VALUES ('Jan', 'Jagter', 'jagter@test.com', @hash, '0821111111', 1, 1, GETUTCDATE());
    SET @userId = SCOPE_IDENTITY();
    SELECT @roleId = roleId FROM roles WHERE roleCode = 'HUNTER';
    INSERT INTO userRoles (urUserId, urRoleId) VALUES (@userId, @roleId);
    SET @urId = SCOPE_IDENTITY();
    INSERT INTO hunter (hUserRoleId, hCountry, hUsername, hBio) VALUES (@urId, 'South Africa', 'janjagter', 'Ek is lief vir die natuur en biltong.');
END

-- 2. Boer
IF NOT EXISTS(SELECT 1 FROM users WHERE uEmail = 'boer@test.com')
BEGIN
    INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
    VALUES ('Bennie', 'Boer', 'boer@test.com', @hash, '0822222222', 1, 1, GETUTCDATE());
    SET @userId = SCOPE_IDENTITY();
    SELECT @roleId = roleId FROM roles WHERE roleCode = 'FARMER';
    INSERT INTO userRoles (urUserId, urRoleId) VALUES (@userId, @roleId);
    SET @urId = SCOPE_IDENTITY();
    INSERT INTO farmer (fUserRoleId, fBusinessName, fBusinessRegistrationNumber, fBio) VALUES (@urId, 'Bennie se Plaas', '123456789', 'Beste wildsplaas in die bosveld.');
END

-- 3. PH
IF NOT EXISTS(SELECT 1 FROM users WHERE uEmail = 'ph@test.com')
BEGIN
    INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
    VALUES ('Piet', 'Professioneel', 'ph@test.com', @hash, '0823333333', 1, 1, GETUTCDATE());
    SET @userId = SCOPE_IDENTITY();
    SELECT @roleId = roleId FROM roles WHERE roleCode = 'PH';
    INSERT INTO userRoles (urUserId, urRoleId) VALUES (@userId, @roleId);
    SET @urId = SCOPE_IDENTITY();
    INSERT INTO ph (phUserRoleId, phYearsOfExperience, phBio) VALUES (@urId, 10, 'Ek jag al vir 10 jaar en ken elke bok.');
END

-- 4. PJ
IF NOT EXISTS(SELECT 1 FROM users WHERE uEmail = 'pj@test.com')
BEGIN
    INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
    VALUES ('Paul', 'Uitruster', 'pj@test.com', @hash, '0824444444', 1, 1, GETUTCDATE());
    SET @userId = SCOPE_IDENTITY();
    SELECT @roleId = roleId FROM roles WHERE roleCode = 'PJ';
    INSERT INTO userRoles (urUserId, urRoleId) VALUES (@userId, @roleId);
    SET @urId = SCOPE_IDENTITY();
    INSERT INTO pj (pjUserRoleId, pjBusinessName, pjBusinessRegistrationNumber, pjYearsOfExperience, pjBio) VALUES (@urId, 'Paul Safaris', '987654321', 15, 'Ons maak drome waar.');
END

-- 5. VG
IF NOT EXISTS(SELECT 1 FROM users WHERE uEmail = 'vg@test.com')
BEGIN
    INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
    VALUES ('Vicus', 'Videograaf', 'vg@test.com', @hash, '0825555555', 1, 1, GETUTCDATE());
    SET @userId = SCOPE_IDENTITY();
    SELECT @roleId = roleId FROM roles WHERE roleCode = 'VG';
    INSERT INTO userRoles (urUserId, urRoleId) VALUES (@userId, @roleId);
    SET @urId = SCOPE_IDENTITY();
    INSERT INTO vg (vgUserRoleId, vgHasDrone, vgDroneLicence, vgCompanyName, vgCompanyRegistrationNumber, vgYearsOfExperience) VALUES (@urId, 1, 'DRN-1234', 'Vicus Videos', '55555555', 5);
END

-- 6. ADMIN
IF NOT EXISTS(SELECT 1 FROM users WHERE uEmail = 'admin@test.com')
BEGIN
    INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
    VALUES ('Adam', 'Admin', 'admin@test.com', @hash, '0826666666', 1, 1, GETUTCDATE());
    SET @userId = SCOPE_IDENTITY();
    SELECT @roleId = roleId FROM roles WHERE roleCode = 'ADMIN';
    INSERT INTO userRoles (urUserId, urRoleId) VALUES (@userId, @roleId);
END
GO
