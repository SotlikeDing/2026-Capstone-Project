-- Post-trip ratings system: Hunter, PH and VG rate each other after a completed
-- booking. Replaces the old unused `rating` table (rRater/rRatee had no booking
-- link, no timestamp, and ambiguous userId-vs-userRoleId semantics - nothing ever
-- wrote to it) with a proper, queryable, duplicate-safe shape.

IF OBJECT_ID('rating', 'U') IS NOT NULL
BEGIN
    DROP TABLE rating;
END

IF OBJECT_ID('ratings', 'U') IS NULL
BEGIN
    CREATE TABLE ratings (
        ratingId INT IDENTITY(1,1) PRIMARY KEY,
        bookingId INT NOT NULL FOREIGN KEY REFERENCES bookings(bookingId),
        raterUserId INT NOT NULL FOREIGN KEY REFERENCES users(userId),
        rateeUserId INT NOT NULL FOREIGN KEY REFERENCES users(userId),
        rateeRoleCode NVARCHAR(20) NOT NULL,
        stars INT NOT NULL CHECK (stars BETWEEN 1 AND 5),
        comment NVARCHAR(MAX) NULL,
        createdAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_ratings_booking_rater_ratee UNIQUE (bookingId, raterUserId, rateeUserId)
    );

    CREATE INDEX IX_ratings_rateeUserId ON ratings(rateeUserId);
END
