DECLARE @pjTable TABLE (Id INT);
DECLARE @farmTable TABLE (Id INT);
DECLARE @userIds TABLE (Id INT);
DECLARE @pjRoleId INT = (SELECT roleId FROM roles WHERE roleCode = 'PJ');

-- Get 4 users, or create them
INSERT INTO @userIds SELECT userId FROM users;
IF (SELECT COUNT(*) FROM @userIds) < 4
BEGIN
    INSERT INTO users (uName, uSurname, uEmail, uPassword, uRegistrationDate, uIsEmailVerified, uIsSuspended)
    VALUES 
    ('Jan', 'Uitruster', 'jan@uitruster.co.za', 'password123', GETDATE(), 1, 0),
    ('Piet', 'Botha', 'piet@uitruster.co.za', 'password123', GETDATE(), 1, 0),
    ('Kobus', 'Venter', 'kobus@uitruster.co.za', 'password123', GETDATE(), 1, 0),
    ('Sarel', 'Nel', 'sarel@uitruster.co.za', 'password123', GETDATE(), 1, 0);
END

-- Ensure they have PJ userRoles and exist in pj table
INSERT INTO userRoles (urUserId, urRoleId)
SELECT u.userId, @pjRoleId FROM users u
WHERE u.userId NOT IN (SELECT urUserId FROM userRoles WHERE urRoleId = @pjRoleId)
AND u.userId IN (SELECT TOP 4 userId FROM users);

INSERT INTO pj (pjUserRoleId, pjBusinessName, pjYearsOfExperience, pjBio)
SELECT ur.userRoleId, u.uName + '''s Safaris', 10, 'Ervare uitruster' 
FROM userRoles ur JOIN users u ON ur.urUserId = u.userId
WHERE ur.urRoleId = @pjRoleId AND ur.userRoleId NOT IN (SELECT pjUserRoleId FROM pj);

-- Ensure there's at least one farm
IF (SELECT COUNT(*) FROM farm) = 0
BEGIN
    -- Need a farmer first. For simplicity, just use the first userRole.
    DECLARE @farmerRoleId INT = (SELECT roleId FROM roles WHERE roleCode = 'Boer');
    IF @farmerRoleId IS NULL BEGIN INSERT INTO roles (roleCode, roleName) VALUES ('Boer', 'Plaas eienaar'); SET @farmerRoleId = SCOPE_IDENTITY(); END
    
    DECLARE @urId INT = (SELECT TOP 1 userRoleId FROM userRoles);
    INSERT INTO farmer (fUserRoleId, fBusinessName) VALUES (@urId, 'Test Boerdery');
    
    INSERT INTO farm (farmFarmerId, farmName, farmSize, farmStatus)
    VALUES (SCOPE_IDENTITY(), 'Krokodilrivier Plaas', 5000, 'Active');
END

DECLARE @farmId INT = (SELECT TOP 1 farmId FROM farm);

-- Get 4 PJ IDs
DECLARE @pj1 INT = (SELECT pjId FROM pj ORDER BY pjId OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY);
DECLARE @pj2 INT = (SELECT pjId FROM pj ORDER BY pjId OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY);
DECLARE @pj3 INT = (SELECT pjId FROM pj ORDER BY pjId OFFSET 2 ROWS FETCH NEXT 1 ROWS ONLY);
DECLARE @pj4 INT = (SELECT pjId FROM pj ORDER BY pjId OFFSET 3 ROWS FETCH NEXT 1 ROWS ONLY);

-- Fallbacks just in case
IF @pj2 IS NULL SET @pj2 = @pj1;
IF @pj3 IS NULL SET @pj3 = @pj1;
IF @pj4 IS NULL SET @pj4 = @pj1;

-- Delete old demo packages if any
DELETE FROM packages WHERE pTitle LIKE 'Kudu %' OR pTitle LIKE '%Safari%';

-- Insert 10 hunting packages
INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom, pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
VALUES
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj1, 15000.00, 'Kudu en Rooibok Pakket', 'Ideale naweek wegbreek vir die gesin. Groot trofee koedoes.', GETDATE(), 3, 2, 6, 4, 4, '1 Kudu, 2 Rooibokke, Verblyf', 'Active'),
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj1, 8500.00, 'Biltong Jag Naweek', 'Genoeg biltong vir die res van die jaar!', GETDATE(), 2, 2, 4, 3, 4, '4 Rooibokke, 1 Blesbok, Verblyf', 'Active'),
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj1, 35000.00, 'Groot 5 Buffel Ervaring', 'Ervaar die adrenalien om te voet in te stap op n buffel bul.', GETDATE(), 5, 1, 4, 7, 2, '1 Kaapse Buffel, Vol spyseniering', 'Active'),
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj2, 12000.00, 'Oos-Kaap Vlakte Jag', 'Oop vlaktes, lang skote. Ideaal vir die ervare skut.', GETDATE(), 3, 2, 8, 5, 6, '1 Springbok, 1 Gemsbok, Verblyf', 'Active'),
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj2, 22000.00, 'Waterbok & Nyala Combo', 'Pragtige diere in bosveld habitat.', GETDATE(), 4, 2, 6, 5, 4, '1 Nyala, 1 Waterbok, Slagfooi ingesluit', 'Active'),
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj3, 5000.00, 'Vark Jag Spesesiaal', 'Bosvarke in die nag en vlakvarke bedags.', GETDATE(), 2, 2, 6, 3, 6, 'Onbeperk Vlakvarke, 1 Bosvark', 'Active'),
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj3, 18500.00, 'Eland Bul Pakket', 'Jag die magtige eland in die klowe van die Karoo.', GETDATE(), 3, 2, 6, 4, 4, '1 Eland Bul, Verblyf', 'Active'),
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj3, 28000.00, 'Vader en Seun Pakket', 'Bring die laaitie saam vir sy eerste bok!', GETDATE(), 2, 2, 4, 4, 2, '1 Kudu koei, 2 Springbokke, Veldgids ingesluit', 'Active'),
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj4, 42000.00, 'Premium Bosveld Safari', 'Luukse verblyf, professionele fotos, en top kwaliteit diere.', GETDATE(), 7, 2, 10, 8, 8, '1 Sable, 1 Kudu, 2 Rooibokke, Etes ingesluit', 'Active'),
(GETDATE(), DATEADD(year, 1, GETDATE()), @farmId, @pj4, 9500.00, 'Boogjag Voorsit Pakket', 'Spesifiek opgestel vir boogjagters met skuilings by watergate.', GETDATE(), 3, 1, 4, 4, 4, '1 Blouwildebees, 1 Vlakvark, Skuiling huur', 'Active');

SELECT 'Successfully inserted 10 packages!' AS Result;
