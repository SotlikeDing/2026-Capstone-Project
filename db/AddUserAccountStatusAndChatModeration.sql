-- Run this script against the existing Mikpunt database.
-- Adds:
--   users.uAccountStatus / users.uStatusReason  (real Suspend/Ban persistence for the Admin Dashboard)
--   flaggedChatMessages                          (real chat moderation queue)
-- Idempotent: safe to re-run. Does not touch existing data.
-- Note: the application also applies these same changes defensively at runtime
-- (User_db.EnsureAccountStatusColumnsAsync / Chats_db's message-flagging insert), so
-- running this script is a convenience, not a hard requirement.

USE Mikpunt;
GO

IF OBJECT_ID(N'dbo.users', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.users-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;
GO

-- ============================================================
-- users.uAccountStatus / uStatusReason
-- ============================================================
IF COL_LENGTH('dbo.users', 'uAccountStatus') IS NULL
BEGIN
    ALTER TABLE dbo.users ADD uAccountStatus NVARCHAR(20) NOT NULL DEFAULT 'Active';
    PRINT 'users.uAccountStatus added.';
END
ELSE
BEGIN
    PRINT 'users.uAccountStatus already exists.';
END;
GO

IF COL_LENGTH('dbo.users', 'uStatusReason') IS NULL
BEGIN
    ALTER TABLE dbo.users ADD uStatusReason NVARCHAR(MAX) NULL;
    PRINT 'users.uStatusReason added.';
END
ELSE
BEGIN
    PRINT 'users.uStatusReason already exists.';
END;
GO

-- ============================================================
-- flaggedChatMessages: real chat-moderation queue
-- ============================================================
IF OBJECT_ID(N'dbo.chatMessages', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.chatMessages-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;
GO

IF OBJECT_ID(N'dbo.flaggedChatMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.flaggedChatMessages (
        flagId INT IDENTITY(1,1) PRIMARY KEY,
        fcmMessageId INT NOT NULL FOREIGN KEY REFERENCES dbo.chatMessages(chatMessageId),
        fcmChatId INT NOT NULL,
        fcmSenderId INT NOT NULL,
        fcmMessageText NVARCHAR(MAX) NOT NULL,
        fcmFlaggedKeywords NVARCHAR(500) NOT NULL,
        fcmSentAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        fcmStatus NVARCHAR(30) NOT NULL DEFAULT 'PendingReview',
        fcmReviewedBy INT NULL,
        fcmReviewedAt DATETIME2 NULL
    );

    PRINT 'flaggedChatMessages table created.';
END
ELSE
BEGIN
    PRINT 'flaggedChatMessages table already exists.';
END;
GO
