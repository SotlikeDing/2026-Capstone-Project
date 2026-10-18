-- Closes three remaining enterprise-readiness gaps on the ratings system:
--   ratingHistory   - immutable/append-only edit trail (every create/update of a
--                     rating is recorded with its before/after value; UPDATE/DELETE
--                     on this table itself is blocked by triggers, same pattern as
--                     adminAuditLog).
--   ratingDisputes  - lets the person who was rated formally dispute a rating; an
--                     admin can uphold it, which excludes that rating from public
--                     averages/reviews (ratings.isExcluded) without deleting the
--                     underlying row - the history above still shows what happened.
--   RatingRead*     - rate-limit settings for the two public GET endpoints, which
--                     were unauthenticated AND unthrottled (scrapable at any volume).
-- Idempotent: safe to re-run.

USE Mikpunt;
GO

-- ============================================================
-- ratingHistory: immutable/append-only rating edit trail
-- ============================================================
IF OBJECT_ID(N'dbo.ratingHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ratingHistory (
        historyId INT IDENTITY(1,1) PRIMARY KEY,
        changeType NVARCHAR(10) NOT NULL, -- 'INSERT' or 'UPDATE', from MERGE's $action
        ratingId INT NOT NULL FOREIGN KEY REFERENCES dbo.ratings(ratingId),
        bookingId INT NOT NULL,
        raterUserId INT NOT NULL,
        rateeUserId INT NOT NULL,
        previousStars INT NULL,       -- NULL when changeType = 'INSERT' (no prior value)
        previousComment NVARCHAR(MAX) NULL,
        newStars INT NOT NULL,
        newComment NVARCHAR(MAX) NULL,
        changedAt DATETIME2 NOT NULL
    );
    PRINT 'ratingHistory table created.';
END
ELSE
BEGIN
    PRINT 'ratingHistory table already exists.';
END;
GO

IF OBJECT_ID(N'dbo.trg_ratingHistory_NoUpdate', N'TR') IS NULL
BEGIN
    EXEC('CREATE TRIGGER dbo.trg_ratingHistory_NoUpdate ON dbo.ratingHistory
          INSTEAD OF UPDATE AS
          BEGIN
              RAISERROR(''ratingHistory is append-only: UPDATE is not permitted.'', 16, 1);
              ROLLBACK TRANSACTION;
          END');
    PRINT 'trg_ratingHistory_NoUpdate created.';
END;
GO

IF OBJECT_ID(N'dbo.trg_ratingHistory_NoDelete', N'TR') IS NULL
BEGIN
    EXEC('CREATE TRIGGER dbo.trg_ratingHistory_NoDelete ON dbo.ratingHistory
          INSTEAD OF DELETE AS
          BEGIN
              RAISERROR(''ratingHistory is append-only: DELETE is not permitted.'', 16, 1);
              ROLLBACK TRANSACTION;
          END');
    PRINT 'trg_ratingHistory_NoDelete created.';
END;
GO

-- ============================================================
-- ratings.isExcluded: set when a dispute against a rating is upheld. The row (and
-- its full history) stays for the record; it just stops counting toward averages
-- and stops appearing in the public reviews list.
-- ============================================================
IF COL_LENGTH('dbo.ratings', 'isExcluded') IS NULL
BEGIN
    ALTER TABLE dbo.ratings ADD isExcluded BIT NOT NULL DEFAULT 0;
    PRINT 'ratings.isExcluded column added.';
END
ELSE
BEGIN
    PRINT 'ratings.isExcluded column already exists.';
END;
GO

-- ============================================================
-- ratingDisputes: the ratee contesting a rating they received
-- ============================================================
IF OBJECT_ID(N'dbo.ratingDisputes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ratingDisputes (
        disputeId INT IDENTITY(1,1) PRIMARY KEY,
        ratingId INT NOT NULL FOREIGN KEY REFERENCES dbo.ratings(ratingId),
        disputedByUserId INT NOT NULL,
        reason NVARCHAR(MAX) NOT NULL,
        status NVARCHAR(30) NOT NULL DEFAULT 'PendingReview', -- PendingReview, Upheld, Denied
        createdAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        reviewedBy INT NULL,
        reviewedAt DATETIME2 NULL,
        resolutionNotes NVARCHAR(MAX) NULL,
        CONSTRAINT UQ_ratingDisputes_ratingId UNIQUE (ratingId)
    );
    PRINT 'ratingDisputes table created.';
END
ELSE
BEGIN
    PRINT 'ratingDisputes table already exists.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM systemSettings WHERE settingKey = 'RatingReadRateLimitPermitLimit')
    INSERT INTO systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('RatingReadRateLimitPermitLimit', '60', 'Maksimum gradering-leesversoeke (publiek) binne die tydvenster, per IP', 0);
GO

IF NOT EXISTS (SELECT 1 FROM systemSettings WHERE settingKey = 'RatingReadRateLimitWindowSeconds')
    INSERT INTO systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('RatingReadRateLimitWindowSeconds', '60', 'Tydvenster (sekondes) vir die gradering-lees tempoperk', 0);
GO
