-- Hardens the ratings system to the same enterprise bar as the admin dashboard:
-- a moderation queue for rating comments (mirrors flaggedChatMessages) and the two
-- system settings the new rating-submission rate limiter reads live.
-- Idempotent: safe to re-run.

USE Mikpunt;
GO

IF OBJECT_ID('flaggedRatingComments', 'U') IS NULL
BEGIN
    CREATE TABLE flaggedRatingComments (
        flagId INT IDENTITY(1,1) PRIMARY KEY,
        frcRatingId INT NOT NULL FOREIGN KEY REFERENCES ratings(ratingId),
        frcRaterUserId INT NOT NULL,
        frcRateeUserId INT NOT NULL,
        frcCommentText NVARCHAR(MAX) NOT NULL,
        frcFlaggedKeywords NVARCHAR(500) NOT NULL,
        frcCreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        frcStatus NVARCHAR(30) NOT NULL DEFAULT 'PendingReview',
        frcReviewedBy INT NULL,
        frcReviewedAt DATETIME2 NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM systemSettings WHERE settingKey = 'RatingRateLimitPermitLimit')
    INSERT INTO systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('RatingRateLimitPermitLimit', '10', 'Maksimum gradering-indienings binne die tydvenster, per gebruiker', 0);
GO

IF NOT EXISTS (SELECT 1 FROM systemSettings WHERE settingKey = 'RatingRateLimitWindowSeconds')
    INSERT INTO systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('RatingRateLimitWindowSeconds', '60', 'Tydvenster (sekondes) vir die gradering-indien tempoperk', 0);
GO
