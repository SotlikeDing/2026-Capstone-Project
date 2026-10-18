-- PH/VG staff-assignment acceptance flow: previously PJ picking a PH/VG for a
-- booking instantly approved it, with no say from the PH/VG themselves. This adds
-- a per-role Pending/Accepted/Declined/Superseded history so the booking only
-- auto-approves once every assigned role has actually accepted, and the PJ can see
-- who declined (and reassign just that role) without losing the audit trail.
-- Idempotent: safe to re-run. Does not touch existing data.

USE Mikpunt;
GO

IF OBJECT_ID(N'dbo.bookings', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.bookings-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;
GO

IF OBJECT_ID(N'dbo.bookingStaffAssignments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bookingStaffAssignments (
        assignmentId INT IDENTITY(1,1) PRIMARY KEY,
        bookingId INT NOT NULL FOREIGN KEY REFERENCES bookings(bookingId),
        staffRole NVARCHAR(2) NOT NULL,                    -- 'PH' or 'VG'
        assigneeUserId INT NOT NULL,
        assignedByUserId INT NOT NULL,
        status NVARCHAR(20) NOT NULL DEFAULT 'Pending',    -- Pending/Accepted/Declined/Superseded
        declineReason NVARCHAR(255) NULL,
        assignedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        respondedAt DATETIME2 NULL
    );
    PRINT 'bookingStaffAssignments table created.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_bookingStaffAssignments_booking')
    CREATE INDEX IX_bookingStaffAssignments_booking ON dbo.bookingStaffAssignments (bookingId, staffRole);
GO

-- Defense-in-depth backstop: the app already prevents two non-terminal rows for the
-- same (bookingId, staffRole) from being created (AssignStaffRoleAsync supersedes the
-- old row before inserting a new one, within a transaction that's incidentally
-- serialized per-booking by the earlier UPDATE bookings row lock). This filtered
-- unique index makes that guarantee a hard DB-level constraint instead of relying
-- solely on application logic + incidental locking behaviour.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_bookingStaffAssignments_ActiveRole')
    CREATE UNIQUE INDEX UQ_bookingStaffAssignments_ActiveRole ON dbo.bookingStaffAssignments (bookingId, staffRole)
    WHERE status IN ('Pending','Accepted');
GO
