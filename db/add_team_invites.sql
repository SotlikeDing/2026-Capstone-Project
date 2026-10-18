-- ============================================================
-- Team Invites Table + Browse Support
-- Run this against the Mikpunt database
-- ============================================================

-- Create teamInvites table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='teamInvites' AND xtype='U')
BEGIN
    CREATE TABLE teamInvites (
        inviteId     INT IDENTITY(1,1) PRIMARY KEY,
        invitePjId   INT NOT NULL FOREIGN KEY REFERENCES userRoles(userRoleId),
        inviteMemberId INT NOT NULL FOREIGN KEY REFERENCES userRoles(userRoleId),
        inviteStatus NVARCHAR(20) NOT NULL DEFAULT 'Pending',  -- Pending / Accepted / Rejected
        inviteDate   DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_TeamInvite UNIQUE (invitePjId, inviteMemberId)  -- prevent duplicate invites
    );
    PRINT 'Created teamInvites table.';
END
ELSE
BEGIN
    PRINT 'teamInvites table already exists, skipping.';
END
