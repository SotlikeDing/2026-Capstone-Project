USE Mikpunt;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE packages ADD pDurationDays BIGINT;
ALTER TABLE packages ADD pMaxParticipants BIGINT;
ALTER TABLE packages ADD pIncludedItems NVARCHAR(MAX);

ALTER TABLE bookings ADD bookPhId INT FOREIGN KEY REFERENCES ph(phId);
ALTER TABLE bookings ADD bookVgId INT FOREIGN KEY REFERENCES vg(vgId);
ALTER TABLE bookings ADD bookPaymentStatus NVARCHAR(MAX);
GO

COMMIT TRANSACTION;
GO

PRINT 'Schema updated successfully!';
GO
