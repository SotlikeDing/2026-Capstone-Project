-- ============================================
-- TOETSDATA VIR MIKPUNT DATABASIS - SIMPEL WERKENDE WEEGAAWE
-- Gebruik veranderlikes vir alle FK verwysings
-- ============================================

USE Mikpunt;
GO

-- ============================================
-- 1. ADDRESSES
-- ============================================
INSERT INTO addresses (addressNumber, addressStreetName, addressCity, addressRegion, addressPostalCode, addressCountry, addressLatitude, addressLongitude) VALUES
('12', 'Hoofstraat', 'Pretoria', 'Gauteng', '0001', 'Suid-Afrika', -25.746111, 28.188056),
('45', 'Kerkstraat', 'Kaapstad', 'Wes-Kaap', '8001', 'Suid-Afrika', -33.924868, 18.424055),
('78', 'Langstraat', 'Bloemfontein', 'Vrystaat', '9301', 'Suid-Afrika', -29.121256, 26.214531),
('Plot 23', 'R512', 'Brits', 'Noordwes', '0250', 'Suid-Afrika', -25.634449, 27.780159),
('10', 'Wildtuinweg', 'Nelspruit', 'Mpumalanga', '1200', 'Suid-Afrika', -25.474644, 30.970850),
('5', 'Safaristraat', 'Polokwane', 'Limpopo', '0699', 'Suid-Afrika', -23.903786, 29.444786),
('22', 'Jagterslaan', 'Kimberley', 'Noord-Kaap', '8300', 'Suid-Afrika', -28.728238, 24.749363),
('7', 'Bosveldweg', 'Rustenburg', 'Noordwes', '0299', 'Suid-Afrika', -25.667574, 27.242674),
('15', 'Waterbergpad', 'Modimolle', 'Limpopo', '0510', 'Suid-Afrika', -24.695879, 28.406912),
('33', 'Olifantsrivierstraat', 'Hoedspruit', 'Limpopo', '1380', 'Suid-Afrika', -24.357985, 30.950358);
GO

-- ============================================
-- 2. DOCUMENT TYPE
-- ============================================
INSERT INTO documentType (dtName, dtNumber) VALUES
('ID Dokument', 'RSA-ID-DL'),
('Paspoort', 'PASSPORT'),
('Drewwerslisensie', 'DRIVER-LIC'),
('PH Lisensie', 'PH-LIC'),
('Vuurwapenlisensie', 'FIREARM-LIC'),
('Besigheidsregistrasie', 'BUS-REG'),
('Drone Lisensie', 'DRONE-LIC'),
('Eerstehulp Sertifikaat', 'FIRST-AID');
GO

-- ============================================
-- 3. ROLES
-- ============================================
INSERT INTO roles (roleCode, roleName) VALUES
('HUNTER', 'Jagter'),
('PH', 'Professionele Jagter'),
('PJ', 'Toerusting Verskaffer'),
('FARMER', 'Boer/Grondeienaar'),
('VG', 'Videograaf'),
('AP', 'Advertensie Plaser'),
('ADMIN', 'Administrateur');
GO

-- ============================================
-- 4. PROVINCES
-- ============================================
INSERT INTO provinces (provName) VALUES
('Gauteng'),
('Wes-Kaap'),
('Oos-Kaap'),
('KwaZulu-Natal'),
('Vrystaat'),
('Noordwes'),
('Mpumalanga'),
('Limpopo'),
('Noord-Kaap');
GO

-- ============================================
-- 5. USERS
-- ============================================
INSERT INTO users (uName, uSurname, uEmail, uPassword, uRegistrationDate, uPhone, uIsEmailVerified, uIsPhoneVerified) VALUES
('Johan', 'van der Merwe', 'johan@mikpunt.com', 'hash123', GETDATE(), '0821234567', 1, 1),
('Pieter', 'Swanepoel', 'pieter@mikpunt.com', 'hash456', GETDATE(), '0832345678', 1, 0),
('Hannes', 'Visser', 'hannes@mikpunt.com', 'hash789', GETDATE(), '0843456789', 0, 0),
('Maria', 'Joubert', 'maria@mikpunt.com', 'hashabc', GETDATE(), '0824567890', 1, 1),
('Koos', 'Pretorius', 'koos@mikpunt.com', 'hashdef', GETDATE(), '0835678901', 1, 0),
('Annemarie', 'Coetzee', 'annemarie@mikpunt.com', 'hashghi', GETDATE(), '0846789012', 1, 1),
('Sarel', 'Botes', 'sarel@mikpunt.com', 'hashjkl', GETDATE(), '0827890123', 0, 0),
('Elsa', 'Kruger', 'elsa@mikpunt.com', 'hashmno', GETDATE(), '0838901234', 1, 1),
('Thabo', 'Nkosi', 'thabo@mikpunt.com', 'hashpqr', GETDATE(), '0849012345', 1, 0),
('Peter', 'Smith', 'peter@mikpunt.com', 'hashstu', GETDATE(), '0820123456', 1, 1);
GO

-- ============================================
-- 6. USER ROLES
-- ============================================
INSERT INTO userRoles (urUserId, urRoleId) VALUES
(1, 1), (2, 2), (3, 3), (4, 4), (5, 5), (6, 6), (7, 7),
(1, 2), (2, 1), (3, 5), (4, 1), (8, 1), (9, 4), (10, 3);
GO

-- ============================================
-- STOOR VERANDERLIKES VIR AL DIE ID'S
-- ============================================

-- Kry role IDs
DECLARE @HUNTER_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'HUNTER');
DECLARE @PH_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'PH');
DECLARE @PJ_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'PJ');
DECLARE @FARMER_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'FARMER');
DECLARE @VG_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'VG');
DECLARE @AP_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'AP');
DECLARE @ADMIN_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'ADMIN');

-- Kry userRole IDs vir elke gebruiker se rolle
DECLARE @Johan_HUNTER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 1 AND urRoleId = @HUNTER_ROLE_ID);
DECLARE @Johan_PH_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 1 AND urRoleId = @PH_ROLE_ID);
DECLARE @Pieter_PH_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 2 AND urRoleId = @PH_ROLE_ID);
DECLARE @Pieter_HUNTER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 2 AND urRoleId = @HUNTER_ROLE_ID);
DECLARE @Hannes_PJ_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 3 AND urRoleId = @PJ_ROLE_ID);
DECLARE @Hannes_VG_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 3 AND urRoleId = @VG_ROLE_ID);
DECLARE @Maria_FARMER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 4 AND urRoleId = @FARMER_ROLE_ID);
DECLARE @Maria_HUNTER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 4 AND urRoleId = @HUNTER_ROLE_ID);
DECLARE @Koos_VG_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 5 AND urRoleId = @VG_ROLE_ID);
DECLARE @Annemarie_AP_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 6 AND urRoleId = @AP_ROLE_ID);
DECLARE @Sarel_ADMIN_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 7 AND urRoleId = @ADMIN_ROLE_ID);
DECLARE @Elsa_HUNTER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 8 AND urRoleId = @HUNTER_ROLE_ID);
DECLARE @Thabo_FARMER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 9 AND urRoleId = @FARMER_ROLE_ID);
DECLARE @Peter_PJ_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = 10 AND urRoleId = @PJ_ROLE_ID);

-- ============================================
-- 7. ADMIN
-- ============================================
INSERT INTO admin (adminUserRoleId) VALUES (@Sarel_ADMIN_URID);
GO

-- ============================================
-- 8. AP (Advertensie Plaser)
-- ============================================
INSERT INTO ap (apUserRoleId) VALUES (@Annemarie_AP_URID);
GO

-- ============================================
-- 9. FARMER (Boere)
-- ============================================
INSERT INTO farmer (fUserRoleId, fBusinessName, fBusinessRegistrationNumber, fPubliclyVisibleTill, fBio) VALUES
(@Maria_FARMER_URID, 'Kudu Bosveld Plase', 'REG12345', '2026-12-31', 'Gesinsplaas met meer as 20 jaar ondervinding'),
(@Thabo_FARMER_URID, 'Limpopo Wilds Plase', 'REG67890', '2026-10-31', 'Spesialiseer in buffel en kudu jagte');
GO

-- Kry farmer IDs
DECLARE @KuduBosveldFarmerId INT = (SELECT farmerId FROM farmer WHERE fBusinessName = 'Kudu Bosveld Plase');
DECLARE @LimpopoWildsFarmerId INT = (SELECT farmerId FROM farmer WHERE fBusinessName = 'Limpopo Wilds Plase');

-- ============================================
-- 10. PJ (Outfitter / Permit Jagger)
-- ============================================
INSERT INTO pj (pjUserRoleId, pjBusinessName, pjBusinessRegistrationNumber, pjPubliclyVisibleTill, pjYearsOfExperience, pjBio) VALUES
(@Hannes_PJ_URID, 'Afrika Jag Uitrusters', 'OUT12345', '2026-12-31', 15, 'Ten volle gelisensieerde jaguitrusters'),
(@Peter_PJ_URID, 'Bosveld Jag Aanbiedings', 'OUT67890', '2026-11-30', 8, 'Spesialiseer in trofee jagte');
GO

-- Kry PJ IDs
DECLARE @AfrikaPjId INT = (SELECT pjId FROM pj WHERE pjBusinessName = 'Afrika Jag Uitrusters');
DECLARE @BosveldPjId INT = (SELECT pjId FROM pj WHERE pjBusinessName = 'Bosveld Jag Aanbiedings');

-- ============================================
-- 11. PH (Professional Hunter)
-- ============================================
INSERT INTO ph (phUserRoleId, phPubliclyVisibleTill, phYearsOfExperience, phBio) VALUES
(@Pieter_PH_URID, '2026-12-31', 20, 'Ervare PH met meer as 500 suksesvolle jagte'),
(@Johan_PH_URID, '2026-09-30', 12, 'Spesialiseer in buitelandse jagters');
GO

-- Kry PH IDs
DECLARE @PieterPhId INT = (SELECT phId FROM ph WHERE phUserRoleId = @Pieter_PH_URID);
DECLARE @JohanPhId INT = (SELECT phId FROM ph WHERE phUserRoleId = @Johan_PH_URID);

-- ============================================
-- 12. VG (Videographer)
-- ============================================
INSERT INTO vg (vgUserRoleId, vgHasDrone, vgDroneLicence, vgPubliclyVisibleTill, vgCompanyName, vgCompanyRegistrationNumber, vgYearsOfExperience) VALUES
(@Koos_VG_URID, 1, 'DRN45678', '2026-12-31', 'WildsVideo Produksies', 'VID12345', 10),
(@Hannes_VG_URID, 0, NULL, '2026-08-31', NULL, NULL, 5);
GO

-- Kry VG IDs
DECLARE @KoosVgId INT = (SELECT vgId FROM vg WHERE vgUserRoleId = @Koos_VG_URID);
DECLARE @HannesVgId INT = (SELECT vgId FROM vg WHERE vgUserRoleId = @Hannes_VG_URID);

-- ============================================
-- 13. HUNTER (Jagters)
-- ============================================
INSERT INTO hunter (hUserRoleId, hCountry, hUsername, hBio) VALUES
(@Johan_HUNTER_URID, 'Suid-Afrika', 'johanjagter', 'Passievolle jagter met 10 jaar ervaring'),
(@Pieter_HUNTER_URID, 'Nederland', 'pieterhunter', 'Internasionale jagter'),
(@Maria_HUNTER_URID, 'Suid-Afrika', 'mariajag', 'Jagter en boer'),
(@Elsa_HUNTER_URID, 'Duitsland', 'elsa_wild', 'Europese jagter wat Afrika verken');
GO

-- Kry Hunter IDs
DECLARE @JohanHunterId INT = (SELECT hunterId FROM hunter WHERE hUserRoleId = @Johan_HUNTER_URID);
DECLARE @PieterHunterId INT = (SELECT hunterId FROM hunter WHERE hUserRoleId = @Pieter_HUNTER_URID);
DECLARE @ElsaHunterId INT = (SELECT hunterId FROM hunter WHERE hUserRoleId = @Elsa_HUNTER_URID);

-- ============================================
-- 14. FARM (Plase)
-- ============================================
INSERT INTO farm (farmFarmerId, farmDescription, farmMediaFolder, farmName, farmSize, farmAddressId, farmTerrainType, farmFencingType, farmStatus) VALUES
(@KuduBosveldFarmerId, 'Uitgestrekte bosveld plase met diverse wild', '/media/farm1/', 'Kudu Bosveld', 2500.50, 3, 'Bosveld', 'Wilddig', 'Active'),
(@LimpopoWildsFarmerId, 'Waterberg bergwild plase', '/media/farm2/', 'Limpopo Wilds', 1800.00, 9, 'Bergveld', 'Wilddig', 'Active');
GO

-- Kry Farm IDs
DECLARE @KuduFarmId INT = (SELECT farmId FROM farm WHERE farmName = 'Kudu Bosveld');
DECLARE @LimpopoFarmId INT = (SELECT farmId FROM farm WHERE farmName = 'Limpopo Wilds');

-- ============================================
-- 15. FARM LODGE
-- ============================================
INSERT INTO farmLodge (flFarmId, flPrice, flRooms, flCateringIncluded, flFacilities) VALUES
(@KuduFarmId, 2500.00, 8, 1, 'Swembad, braai area, lugversorging'),
(@KuduFarmId, 1500.00, 4, 0, 'Basiese chalet'),
(@LimpopoFarmId, 3500.00, 6, 1, 'Luukse verblyf, spa, restaurant');
GO

-- ============================================
-- 16. AVAILABLE ANIMALS
-- ============================================
INSERT INTO availableAnimals (aaFarmId, aaName, aaQuota, aaEntityPrice) VALUES
(@KuduFarmId, 'Kudu', 20, 8500.00),
(@KuduFarmId, 'Springbok', 50, 2500.00),
(@KuduFarmId, 'Wildebees', 15, 7500.00),
(@LimpopoFarmId, 'Buffel', 5, 45000.00),
(@LimpopoFarmId, 'Olifant', 1, 150000.00),
(@LimpopoFarmId, 'Leeu', 2, 85000.00);
GO

-- ============================================
-- 17. PACKAGES (Pakkette)
-- ============================================
INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pDescription, pTitle, pAvailableFrom, pMinExpeditionLength, pMinGuests, pMaxGuests, pStatus) VALUES
(GETDATE(), '2026-12-31', @KuduFarmId, @AfrikaPjId, 25000.00, '5-dag jagpakket insluitend kudu en springbok', 'Kudu Bosveld Pakket', GETDATE(), 5, 1, 4, 'Active'),
(GETDATE(), '2026-10-31', @LimpopoFarmId, @BosveldPjId, 125000.00, '7-dag buffel jagpakket', 'Buffel Trofee Pakket', GETDATE(), 7, 1, 2, 'Active'),
(GETDATE(), '2026-11-30', @KuduFarmId, @AfrikaPjId, 15000.00, '3-dag wilds jagpakket', 'Naweek Jag Pakket', GETDATE(), 3, 2, 6, 'Active');
GO

-- Kry Package IDs
DECLARE @KuduPakketId INT = (SELECT packageId FROM packages WHERE pTitle = 'Kudu Bosveld Pakket');
DECLARE @BuffelPakketId INT = (SELECT packageId FROM packages WHERE pTitle = 'Buffel Trofee Pakket');
DECLARE @NaweekPakketId INT = (SELECT packageId FROM packages WHERE pTitle = 'Naweek Jag Pakket');

-- ============================================
-- 18. PH PRICING
-- ============================================
INSERT INTO phPricing (phpPhId, phpGameType, phpHunterCount, phpPrice) VALUES
(@PieterPhId, 'Kudu', 1, 3500.00),
(@PieterPhId, 'Springbok', 2, 1500.00),
(@JohanPhId, 'Buffel', 1, 5000.00),
(@JohanPhId, 'Rooibok', 1, 2000.00);
GO

-- ============================================
-- 19. VG PACKAGES
-- ============================================
INSERT INTO vgPackages (vgId, vgpDescription, vgpPrice) VALUES
(@KoosVgId, 'Basiese video opname vir 3 dae', 5000.00),
(@KoosVgId, 'Premium video met drone materiaal', 12000.00),
(@KoosVgId, 'Volle produksie pakket', 25000.00);
GO

-- ============================================
-- 20. VG AVAILABLE PROVINCES
-- ============================================
DECLARE @GautengProvId INT = (SELECT provId FROM provinces WHERE provName = 'Gauteng');
DECLARE @WesKaapProvId INT = (SELECT provId FROM provinces WHERE provName = 'Wes-Kaap');
DECLARE @VrystaatProvId INT = (SELECT provId FROM provinces WHERE provName = 'Vrystaat');
DECLARE @NoordwesProvId INT = (SELECT provId FROM provinces WHERE provName = 'Noordwes');

INSERT INTO vgAvailableProvinces (vgapVgId, vgapProvId) VALUES
(@KoosVgId, @GautengProvId), (@KoosVgId, @WesKaapProvId), (@KoosVgId, @VrystaatProvId),
(@HannesVgId, @GautengProvId), (@HannesVgId, @NoordwesProvId);
GO

-- 21. ADVERTISEMENTS - No dummy seed data (clean start)
-- ============================================


-- ============================================
-- 22. BOOKINGS (Besprekings)
-- ============================================
INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests) VALUES
(@JohanHunterId, @KuduPakketId, '2026-06-01', '2026-06-05', 'Confirmed', 2, 1),
(@ElsaHunterId, @BuffelPakketId, '2026-07-10', '2026-07-16', 'Pending', 1, 0),
(@PieterHunterId, @NaweekPakketId, '2026-08-15', '2026-08-17', 'Completed', 3, 2);
GO

-- Kry Booking IDs
DECLARE @Booking1Id INT = (SELECT bookingId FROM bookings WHERE bookHunterId = @JohanHunterId AND bookPackageId = @KuduPakketId);
DECLARE @Booking3Id INT = (SELECT bookingId FROM bookings WHERE bookHunterId = @PieterHunterId AND bookPackageId = @NaweekPakketId);

-- ============================================
-- 23. STORY DATA
-- ============================================
INSERT INTO storyData (sdBookingId, sdExpeditionNum) VALUES
(@Booking1Id, 1),
(@Booking3Id, 1),
(@Booking3Id, 2);
GO

-- Kry StoryData IDs
DECLARE @StoryData1Id INT = (SELECT storyDataId FROM storyData WHERE sdBookingId = @Booking1Id);
DECLARE @StoryData2Id INT = (SELECT storyDataId FROM storyData WHERE sdBookingId = @Booking3Id AND sdExpeditionNum = 1);

-- ============================================
-- 24. STORY DATA POINTS
-- ============================================
INSERT INTO storyDataPoint (sdpStoryDataId, sdpGpsPos, sdpTimestamp, sdpIsCriticalPoint, sdpMediaFolderPath) VALUES
(@StoryData1Id, '-25.746111, 28.188056', '2026-06-01 08:00:00', 0, '/media/story1/'),
(@StoryData1Id, '-25.700000, 28.200000', '2026-06-01 14:30:00', 1, '/media/story1/'),
(@StoryData2Id, '-24.695879, 28.406912', '2026-08-15 06:00:00', 0, '/media/story2/');
GO

-- ============================================
-- 25. CHAT
-- ============================================
INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES
(GETDATE(), 'Jagbespreking - Kudu Jag'),
(GETDATE(), 'Navraag oor Buffel Pakket');
GO

-- Kry Chat IDs
DECLARE @Chat1Id INT = (SELECT chatId FROM chat WHERE cTitle = 'Jagbespreking - Kudu Jag');
DECLARE @Chat2Id INT = (SELECT chatId FROM chat WHERE cTitle = 'Navraag oor Buffel Pakket');

-- ============================================
-- 26. CHAT MESSAGES
-- ============================================
INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp) VALUES
(@Chat1Id, 1, 'Hallo, wanneer is die beste tyd vir kudu?', GETDATE()),
(@Chat1Id, 2, 'Junie is perfek, die weer is goed.', GETDATE()),
(@Chat2Id, 8, 'Is die buffel pakket nog beskikbaar?', GETDATE()),
(@Chat2Id, 10, 'Ja, kontak my gerus vir meer besonderhede.', GETDATE());
GO

-- Kry ChatMessage IDs
DECLARE @ChatMsg1Id INT = (SELECT chatMessageId FROM chatMessages WHERE cmChatId = @Chat1Id AND cmSenderId = 1);
DECLARE @ChatMsg2Id INT = (SELECT chatMessageId FROM chatMessages WHERE cmChatId = @Chat1Id AND cmSenderId = 2);
DECLARE @ChatMsg3Id INT = (SELECT chatMessageId FROM chatMessages WHERE cmChatId = @Chat2Id AND cmSenderId = 8);
DECLARE @ChatMsg4Id INT = (SELECT chatMessageId FROM chatMessages WHERE cmChatId = @Chat2Id AND cmSenderId = 10);

-- ============================================
-- 27. CHAT PARTICIPANTS
-- ============================================
INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp, cpLeftAtTimestamp, cpLastReadMessage) VALUES
(@Chat1Id, 1, GETDATE(), NULL, @ChatMsg1Id),
(@Chat1Id, 2, GETDATE(), NULL, @ChatMsg2Id),
(@Chat2Id, 8, GETDATE(), NULL, @ChatMsg3Id),
(@Chat2Id, 10, GETDATE(), NULL, @ChatMsg4Id);
GO

-- ============================================
-- 28. RATINGS
-- Only @Booking3Id (Pieter's Naweek Pakket) has bookStatus = 'Completed' - ratings
-- are tied to a specific completed trip, so that's the only booking seeded here.
-- ============================================
INSERT INTO ratings (bookingId, raterUserId, rateeUserId, rateeRoleCode, stars, comment) VALUES
(@Booking3Id, 2, 1, 'PH', 5, 'Uitstekende PH! Baie professioneel.'),
(@Booking3Id, 1, 2, 'HUNTER', 4, 'Goeie jagter, hou by die reëls en tydskedule.');
GO

-- ============================================
-- 29. CONTACT US
-- ============================================
INSERT INTO contactUs (cuSubject, cuContent, cuSenderName, cuSenderSurname, cuSenderEmail, cuPhoneNumber, cuType, cuIsResolved) VALUES
('Navraag oor jag pakkette', 'Ek stel belang in n buffel jag...', 'Jan', 'Botha', 'jan@email.com', '0821111111', 'Enquiry', 0),
('Probleem met registrasie', 'Ek kan nie my profiel update nie.', 'Susan', 'Pienaar', 'susan@email.com', '0832222222', 'Support', 1);
GO

-- ============================================
-- 30. USER DOCUMENTATION
-- ============================================
DECLARE @PH_DocTypeId INT = (SELECT dtId FROM documentType WHERE dtName = 'PH Lisensie');
DECLARE @BUS_DocTypeId INT = (SELECT dtId FROM documentType WHERE dtName = 'Besigheidsregistrasie');
DECLARE @DRONE_DocTypeId INT = (SELECT dtId FROM documentType WHERE dtName = 'Drone Lisensie');

INSERT INTO user_documentation (udUserRole, udName, udFilePath, udIssuedTimestamp, udExpirationTimestamp, udStatus, udUploadedTimestamp, udDtId) VALUES
(@Pieter_PH_URID, 'PH Lisensie 2025', '/docs/ph_licence_2025.pdf', '2025-01-01', '2026-12-31', 'Approved', GETDATE(), @PH_DocTypeId),
(@Hannes_PJ_URID, 'Besigheidsregistrasie', '/docs/business_reg.pdf', '2020-03-15', NULL, 'Approved', GETDATE(), @BUS_DocTypeId),
(@Koos_VG_URID, 'Drone Lisensie', '/docs/drone_licence.pdf', '2024-06-01', '2027-05-31', 'Pending', GETDATE(), @DRONE_DocTypeId);
GO

-- ============================================
-- 31. TEAM
-- ============================================
INSERT INTO team (teamPjId, teamMemberId, teamRow3Placeholder) VALUES
(@Hannes_PJ_URID, @Koos_VG_URID, NULL),
(@Hannes_PJ_URID, @Pieter_PH_URID, NULL);
GO

-- ============================================
-- BEOORDELING / VIEW DATA
-- ============================================
PRINT '==========================================';
PRINT '✅ Toetsdata suksesvol ingevoer in Mikpunt!';
PRINT '==========================================';

SELECT 'users' AS Tabel, COUNT(*) AS AantalRye FROM users
UNION SELECT 'userRoles', COUNT(*) FROM userRoles
UNION SELECT 'roles', COUNT(*) FROM roles
UNION SELECT 'hunter', COUNT(*) FROM hunter
UNION SELECT 'ph', COUNT(*) FROM ph
UNION SELECT 'pj', COUNT(*) FROM pj
UNION SELECT 'farmer', COUNT(*) FROM farmer
UNION SELECT 'vg', COUNT(*) FROM vg
UNION SELECT 'packages', COUNT(*) FROM packages
UNION SELECT 'bookings', COUNT(*) FROM bookings
UNION SELECT 'addresses', COUNT(*) FROM addresses
UNION SELECT 'advertisements', COUNT(*) FROM advertisements
ORDER BY Tabel;
GO