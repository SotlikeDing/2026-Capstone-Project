-- Multi-hunter bookings: the main hunter (bookings.bookHunterId, who pays) can add
-- the other hunters of a confirmed booking, up to the booking's hunter count
-- (bookHuntGuests includes the main hunter). Each row is one added hunter:
--   Linked  - an existing Hunter account (userRoleId set)
--   Pending - an emailed invite; linked automatically once that email is verified
--             on a Hunter account (userRoleId NULL until then)
-- Non-hunting guests (bookNonHuntGuests) are never added here.
-- Idempotent: safe to re-run. Does not touch existing data.

USE Mikpunt;
GO

IF OBJECT_ID(N'dbo.bookings', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.bookings-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;
GO

IF OBJECT_ID(N'dbo.bookingCompanions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bookingCompanions (
        companionId INT IDENTITY(1,1) PRIMARY KEY,
        bookingId INT NOT NULL FOREIGN KEY REFERENCES bookings(bookingId) ON DELETE CASCADE,
        userRoleId INT NULL FOREIGN KEY REFERENCES userRoles(userRoleId), -- the companion's Hunter role, once linked
        inviteName NVARCHAR(200) NOT NULL,
        inviteEmail NVARCHAR(255) NOT NULL,                               -- trimmed + lower-case
        status NVARCHAR(20) NOT NULL DEFAULT 'Pending',                   -- Pending / Linked
        addedByUserId INT NOT NULL,
        addedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        linkedAt DATETIME2 NULL
    );
    PRINT 'bookingCompanions table created.';
END;
GO

-- One slot per person per booking, whether invited or linked (the email is kept on
-- linked rows too), so a double click or two devices can never add someone twice.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_bookingCompanions_Email')
    CREATE UNIQUE INDEX UQ_bookingCompanions_Email ON dbo.bookingCompanions (bookingId, inviteEmail);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_bookingCompanions_UserRole')
    CREATE UNIQUE INDEX UQ_bookingCompanions_UserRole ON dbo.bookingCompanions (bookingId, userRoleId)
    WHERE userRoleId IS NOT NULL;
GO

-- "Which trips am I on?" and "claim my invites" look up by user / by email.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_bookingCompanions_UserRole')
    CREATE INDEX IX_bookingCompanions_UserRole ON dbo.bookingCompanions (userRoleId) WHERE userRoleId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_bookingCompanions_PendingEmail')
    CREATE INDEX IX_bookingCompanions_PendingEmail ON dbo.bookingCompanions (inviteEmail) WHERE status = 'Pending';
GO
