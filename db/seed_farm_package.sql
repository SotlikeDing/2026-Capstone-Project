USE Mikpunt;
GO

DECLARE @farmerId INT = (SELECT TOP 1 farmerId FROM farmer JOIN userRoles ON fUserRoleId = userRoleId JOIN users ON urUserId = userId WHERE uEmail = 'boer@test.com');
DECLARE @pjId INT = (SELECT TOP 1 pjId FROM pj JOIN userRoles ON pjUserRoleId = userRoleId JOIN users ON urUserId = userId WHERE uEmail = 'pj@test.com');

IF NOT EXISTS(SELECT 1 FROM farm WHERE farmFarmerId = @farmerId)
BEGIN
    INSERT INTO farm (farmFarmerId, farmName, farmDescription, farmSize, farmTerrainType, farmFencingType, farmStatus)
    VALUES (@farmerId, 'Bennie se Bosveld', 'Ervaar die regte bosveld in die hart van die land. Groot verskeidenheid bokke en uitstekende verblyf.', 5000, 'Bosveld', 'Wildsheining', 'ACTIVE');
END

DECLARE @farmId INT = (SELECT TOP 1 farmId FROM farm WHERE farmFarmerId = @farmerId);

IF NOT EXISTS(SELECT 1 FROM packages WHERE pPjId = @pjId)
BEGIN
    INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pDescription, pTitle, pAvailableFrom, pStatus, pDurationDays, pMinGuests, pMaxGuests)
    VALUES (GETUTCDATE(), '2027-12-31', @farmId, @pjId, 15000, 'Kudu en Rooibok Naweek Jagpakket met vol spyseniering en gidse ingesluit.', 'Naweek Bosveld Jag Safari', GETUTCDATE(), 'ACTIVE', 3, 2, 4);
END
GO
