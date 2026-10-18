-- Farm ratings submitted by the booked hunter after a hunt has happened.
-- Separate from `ratings`, which rates individual Hunter/PH/VG users.
-- Idempotent: safe to run on every WebAPI startup.

USE Mikpunt;
GO

IF OBJECT_ID(N'dbo.farmRatings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.farmRatings (
        farmRatingId INT IDENTITY(1,1) PRIMARY KEY,
        bookingId INT NOT NULL FOREIGN KEY REFERENCES dbo.bookings(bookingId),
        farmId INT NOT NULL FOREIGN KEY REFERENCES dbo.farm(farmId),
        raterUserId INT NOT NULL FOREIGN KEY REFERENCES dbo.users(userId),
        stars INT NOT NULL,
        description NVARCHAR(2000) NULL,
        createdAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_farmRatings_stars CHECK (stars BETWEEN 0 AND 5),
        CONSTRAINT UQ_farmRatings_booking_rater_farm UNIQUE (bookingId, raterUserId, farmId)
    );

    CREATE INDEX IX_farmRatings_farmId ON dbo.farmRatings(farmId);
    PRINT 'farmRatings table created.';
END
ELSE
BEGIN
    PRINT 'farmRatings table already exists.';
END;
GO
