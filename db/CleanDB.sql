-- ============================================
-- SKOONMAAK ALLE TABELLE MET DELETE (TRANSACTION)
-- Gebruik DELETE as jy triggers wil aktiveer
-- ============================================

USE Mikpunt;
GO

BEGIN TRANSACTION;
GO

-- Skakel constraint checks af
EXEC sp_MSforeachtable "ALTER TABLE ? NOCHECK CONSTRAINT all";
GO

-- Delete in korrekte volgorde
DELETE FROM storyDataPoint;
DELETE FROM storyData;
DELETE FROM bookings;
DELETE FROM packages;
DELETE FROM vgAvailableProvinces;
DELETE FROM vgPackages;
DELETE FROM phPricing;
DELETE FROM availableAnimals;
DELETE FROM farmLodge;
DELETE FROM advertisements;
DELETE FROM user_documentation;
DELETE FROM ratings;
DELETE FROM team;
DELETE FROM chatMessages;
DELETE FROM chatParticipants;
DELETE FROM chat;
DELETE FROM farm;
DELETE FROM hunter;
DELETE FROM vg;
DELETE FROM ph;
DELETE FROM pj;
DELETE FROM farmer;
DELETE FROM ap;
DELETE FROM admin;
DELETE FROM contactUs;
DELETE FROM userRoles;
DELETE FROM addresses;
DELETE FROM documentType;
DELETE FROM provinces;
DELETE FROM users;
DELETE FROM roles;
GO

-- Herstel constraint checks
EXEC sp_MSforeachtable "ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all";
GO

-- Reset identity waardes
DBCC CHECKIDENT ('addresses', RESEED, 0);
DBCC CHECKIDENT ('documentType', RESEED, 0);
DBCC CHECKIDENT ('roles', RESEED, 0);
DBCC CHECKIDENT ('provinces', RESEED, 0);
DBCC CHECKIDENT ('users', RESEED, 0);
DBCC CHECKIDENT ('userRoles', RESEED, 0);
DBCC CHECKIDENT ('admin', RESEED, 0);
DBCC CHECKIDENT ('ap', RESEED, 0);
DBCC CHECKIDENT ('farmer', RESEED, 0);
DBCC CHECKIDENT ('pj', RESEED, 0);
DBCC CHECKIDENT ('ph', RESEED, 0);
DBCC CHECKIDENT ('vg', RESEED, 0);
DBCC CHECKIDENT ('hunter', RESEED, 0);
DBCC CHECKIDENT ('advertisements', RESEED, 0);
DBCC CHECKIDENT ('contactUs', RESEED, 0);
DBCC CHECKIDENT ('ratings', RESEED, 0);
DBCC CHECKIDENT ('user_documentation', RESEED, 0);
DBCC CHECKIDENT ('chat', RESEED, 0);
DBCC CHECKIDENT ('chatParticipants', RESEED, 0);
DBCC CHECKIDENT ('chatMessages', RESEED, 0);
DBCC CHECKIDENT ('farm', RESEED, 0);
DBCC CHECKIDENT ('farmLodge', RESEED, 0);
DBCC CHECKIDENT ('packages', RESEED, 0);
DBCC CHECKIDENT ('bookings', RESEED, 0);
DBCC CHECKIDENT ('storyData', RESEED, 0);
DBCC CHECKIDENT ('storyDataPoint', RESEED, 0);
DBCC CHECKIDENT ('vgAvailableProvinces', RESEED, 0);
DBCC CHECKIDENT ('vgPackages', RESEED, 0);
DBCC CHECKIDENT ('availableAnimals', RESEED, 0);
DBCC CHECKIDENT ('phPricing', RESEED, 0);
DBCC CHECKIDENT ('team', RESEED, 0);
GO

COMMIT TRANSACTION;
GO

PRINT '==========================================';
PRINT '✅ Alle tabelle is suksesvol skoongemaak!';
PRINT '==========================================';
GO