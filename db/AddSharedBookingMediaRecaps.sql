/*
  Shared completed-booking media, route, trophy approval, recap, YouTube and
  cleanup state. This migration is idempotent and intentionally stores only
  private storage references; large media bytes never enter SQL Server.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.bookingMedia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bookingMedia (
        mediaId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_bookingMedia PRIMARY KEY,
        bookingId INT NOT NULL CONSTRAINT FK_bookingMedia_bookings REFERENCES dbo.bookings(bookingId),
        uploaderUserId INT NOT NULL CONSTRAINT FK_bookingMedia_users REFERENCES dbo.users(userId),
        uploaderUserRoleId INT NOT NULL CONSTRAINT FK_bookingMedia_userRoles REFERENCES dbo.userRoles(userRoleId),
        uploaderRole NVARCHAR(20) NOT NULL,
        originalFileName NVARCHAR(255) NOT NULL,
        privateStorageReference NVARCHAR(700) NOT NULL,
        contentType NVARCHAR(80) NOT NULL,
        fileSizeBytes BIGINT NOT NULL,
        width INT NOT NULL,
        height INT NOT NULL,
        originalCaptureTimestamp DATETIME2 NULL,
        adjustedCaptureTimestamp DATETIME2 NULL,
        latitude DECIMAL(9,6) NULL,
        longitude DECIMAL(9,6) NULL,
        orientation INT NULL,
        caption NVARCHAR(500) NULL,
        sourceType NVARCHAR(30) NOT NULL CONSTRAINT DF_bookingMedia_sourceType DEFAULT N'Photo',
        matchMethod NVARCHAR(30) NOT NULL CONSTRAINT DF_bookingMedia_matchMethod DEFAULT N'Unmatched',
        matchConfidence NVARCHAR(20) NOT NULL CONSTRAINT DF_bookingMedia_matchConfidence DEFAULT N'None',
        matchedRoutePointIndex INT NULL,
        matchedPinId NVARCHAR(120) NULL,
        inclusionStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_bookingMedia_inclusion DEFAULT N'Included',
        reviewStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_bookingMedia_review DEFAULT N'NotRequired',
        mediaVersion INT NOT NULL CONSTRAINT DF_bookingMedia_version DEFAULT 1,
        createdAt DATETIME2 NOT NULL CONSTRAINT DF_bookingMedia_created DEFAULT SYSUTCDATETIME(),
        updatedAt DATETIME2 NOT NULL CONSTRAINT DF_bookingMedia_updated DEFAULT SYSUTCDATETIME(),
        deletedAt DATETIME2 NULL,
        deletionReason NVARCHAR(300) NULL,
        CONSTRAINT CK_bookingMedia_role CHECK (uploaderRole IN (N'Hunter', N'PH', N'VG')),
        CONSTRAINT CK_bookingMedia_size CHECK (fileSizeBytes > 0),
        CONSTRAINT CK_bookingMedia_dimensions CHECK (width > 0 AND height > 0),
        CONSTRAINT CK_bookingMedia_lat CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
        CONSTRAINT CK_bookingMedia_lon CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180)
    );
    CREATE INDEX IX_bookingMedia_booking_status ON dbo.bookingMedia(bookingId, deletedAt, inclusionStatus, mediaVersion);
    CREATE INDEX IX_bookingMedia_uploader ON dbo.bookingMedia(uploaderUserId, bookingId);
END;

IF OBJECT_ID(N'dbo.bookingRoutes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bookingRoutes (
        routeId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_bookingRoutes PRIMARY KEY,
        bookingId INT NOT NULL CONSTRAINT FK_bookingRoutes_bookings REFERENCES dbo.bookings(bookingId),
        ownerUserId INT NOT NULL CONSTRAINT FK_bookingRoutes_users REFERENCES dbo.users(userId),
        ownerUserRoleId INT NOT NULL CONSTRAINT FK_bookingRoutes_userRoles REFERENCES dbo.userRoles(userRoleId),
        sourceHuntSessionId NVARCHAR(120) NOT NULL,
        routePointsJson NVARCHAR(MAX) NOT NULL,
        pinsJson NVARCHAR(MAX) NULL,
        pointCount INT NOT NULL,
        distanceMeters DECIMAL(14,2) NOT NULL,
        durationSeconds INT NOT NULL,
        routeVersion INT NOT NULL,
        status NVARCHAR(30) NOT NULL CONSTRAINT DF_bookingRoutes_status DEFAULT N'Submitted',
        submittedAt DATETIME2 NOT NULL CONSTRAINT DF_bookingRoutes_submitted DEFAULT SYSUTCDATETIME(),
        updatedAt DATETIME2 NOT NULL CONSTRAINT DF_bookingRoutes_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_bookingRoutes_source UNIQUE(bookingId, ownerUserId, sourceHuntSessionId, routeVersion),
        CONSTRAINT CK_bookingRoutes_points CHECK (pointCount BETWEEN 2 AND 50000),
        CONSTRAINT CK_bookingRoutes_distance CHECK (distanceMeters >= 0),
        CONSTRAINT CK_bookingRoutes_duration CHECK (durationSeconds >= 0),
        CONSTRAINT CK_bookingRoutes_json CHECK (ISJSON(routePointsJson) = 1),
        CONSTRAINT CK_bookingRoutes_pinsJson CHECK (pinsJson IS NULL OR ISJSON(pinsJson) = 1)
    );
    CREATE INDEX IX_bookingRoutes_booking ON dbo.bookingRoutes(bookingId, status, submittedAt DESC);
END;

IF OBJECT_ID(N'dbo.bookingRouteSelections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bookingRouteSelections (
        bookingRouteSelectionId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_bookingRouteSelections PRIMARY KEY,
        bookingId INT NOT NULL CONSTRAINT FK_bookingRouteSelections_booking REFERENCES dbo.bookings(bookingId),
        routeId BIGINT NOT NULL CONSTRAINT FK_bookingRouteSelections_route REFERENCES dbo.bookingRoutes(routeId),
        selectedByUserId INT NOT NULL CONSTRAINT FK_bookingRouteSelections_user REFERENCES dbo.users(userId),
        selectedAt DATETIME2 NOT NULL CONSTRAINT DF_bookingRouteSelections_selected DEFAULT SYSUTCDATETIME(),
        supersededAt DATETIME2 NULL
    );
    CREATE UNIQUE INDEX UX_bookingRouteSelections_active ON dbo.bookingRouteSelections(bookingId) WHERE supersededAt IS NULL;
END;

IF OBJECT_ID(N'dbo.bookingRecaps', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bookingRecaps (
        recapId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_bookingRecaps PRIMARY KEY,
        bookingId INT NOT NULL CONSTRAINT FK_bookingRecaps_booking REFERENCES dbo.bookings(bookingId),
        selectedRouteId BIGINT NOT NULL CONSTRAINT FK_bookingRecaps_route REFERENCES dbo.bookingRoutes(routeId),
        requestedByUserId INT NOT NULL CONSTRAINT FK_bookingRecaps_user REFERENCES dbo.users(userId),
        status NVARCHAR(30) NOT NULL CONSTRAINT DF_bookingRecaps_status DEFAULT N'Queued',
        progress INT NOT NULL CONSTRAINT DF_bookingRecaps_progress DEFAULT 0,
        sourceMediaVersion INT NOT NULL,
        outputPrivateStorageReference NVARCHAR(700) NULL,
        outputFileSize BIGINT NULL,
        durationSeconds INT NULL,
        title NVARCHAR(180) NOT NULL,
        failureReason NVARCHAR(1000) NULL,
        version INT NOT NULL,
        uploadStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_bookingRecaps_upload DEFAULT N'NotRequested',
        deletionStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_bookingRecaps_deletion DEFAULT N'NotRequested',
        youtubeVideoId NVARCHAR(40) NULL,
        youtubeUrl NVARCHAR(500) NULL,
        youtubePrivacyStatus NVARCHAR(30) NULL,
        youtubeProcessingStatus NVARCHAR(60) NULL,
        createdAt DATETIME2 NOT NULL CONSTRAINT DF_bookingRecaps_created DEFAULT SYSUTCDATETIME(),
        startedAt DATETIME2 NULL,
        completedAt DATETIME2 NULL,
        CONSTRAINT UQ_bookingRecaps_version UNIQUE(bookingId, version),
        CONSTRAINT CK_bookingRecaps_progress CHECK (progress BETWEEN 0 AND 100)
    );
    CREATE INDEX IX_bookingRecaps_booking_status ON dbo.bookingRecaps(bookingId, status, createdAt DESC);
END;

IF OBJECT_ID(N'dbo.recapProcessingJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.recapProcessingJobs (
        recapProcessingJobId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_recapProcessingJobs PRIMARY KEY,
        recapId BIGINT NOT NULL CONSTRAINT FK_recapProcessingJobs_recap REFERENCES dbo.bookingRecaps(recapId),
        idempotencyKey UNIQUEIDENTIFIER NOT NULL,
        status NVARCHAR(30) NOT NULL CONSTRAINT DF_recapJobs_status DEFAULT N'Queued',
        attemptCount INT NOT NULL CONSTRAINT DF_recapJobs_attempts DEFAULT 0,
        leaseOwner NVARCHAR(120) NULL,
        leaseExpiresAt DATETIME2 NULL,
        nextAttemptAt DATETIME2 NOT NULL CONSTRAINT DF_recapJobs_next DEFAULT SYSUTCDATETIME(),
        lastError NVARCHAR(1000) NULL,
        createdAt DATETIME2 NOT NULL CONSTRAINT DF_recapJobs_created DEFAULT SYSUTCDATETIME(),
        updatedAt DATETIME2 NOT NULL CONSTRAINT DF_recapJobs_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_recapJobs_key UNIQUE(idempotencyKey),
        CONSTRAINT UQ_recapJobs_recap UNIQUE(recapId)
    );
    CREATE INDEX IX_recapJobs_queue ON dbo.recapProcessingJobs(status, nextAttemptAt, leaseExpiresAt);
END;

IF OBJECT_ID(N'dbo.trophySubmissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.trophySubmissions (
        submissionId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_trophySubmissions PRIMARY KEY,
        bookingId INT NOT NULL CONSTRAINT FK_trophySubmissions_booking REFERENCES dbo.bookings(bookingId),
        hunterUserId INT NOT NULL CONSTRAINT FK_trophySubmissions_user REFERENCES dbo.users(userId),
        hunterUserRoleId INT NOT NULL CONSTRAINT FK_trophySubmissions_role REFERENCES dbo.userRoles(userRoleId),
        species NVARCHAR(120) NOT NULL,
        trophyPhotoStorageReference NVARCHAR(700) NOT NULL,
        antlerSizeCm DECIMAL(10,2) NULL,
        carcassWeightKg DECIMAL(10,2) NULL,
        harvestDate DATE NOT NULL,
        locationText NVARCHAR(200) NOT NULL,
        shotDistanceMeters INT NULL,
        firearmType NVARCHAR(120) NOT NULL,
        notes NVARCHAR(MAX) NULL,
        supportingMediaIdsJson NVARCHAR(MAX) NULL,
        rowlandWardRequestJson NVARCHAR(MAX) NULL,
        status NVARCHAR(30) NOT NULL CONSTRAINT DF_trophySubmissions_status DEFAULT N'Pending',
        reviewNotes NVARCHAR(1000) NULL,
        approvedTrophyId INT NULL CONSTRAINT FK_trophySubmissions_trophy REFERENCES dbo.trophies(trophyId),
        createdAt DATETIME2 NOT NULL CONSTRAINT DF_trophySubmissions_created DEFAULT SYSUTCDATETIME(),
        updatedAt DATETIME2 NOT NULL CONSTRAINT DF_trophySubmissions_updated DEFAULT SYSUTCDATETIME(),
        rowVersion ROWVERSION,
        CONSTRAINT CK_trophySubmissions_status CHECK (status IN (N'Pending', N'UnderReview', N'Approved', N'Rejected', N'CorrectionRequested', N'Resubmitted')),
        CONSTRAINT CK_trophySubmissions_mediaJson CHECK (supportingMediaIdsJson IS NULL OR ISJSON(supportingMediaIdsJson) = 1),
        CONSTRAINT CK_trophySubmissions_rwJson CHECK (rowlandWardRequestJson IS NULL OR ISJSON(rowlandWardRequestJson) = 1)
    );
    CREATE INDEX IX_trophySubmissions_booking_status ON dbo.trophySubmissions(bookingId, status, createdAt DESC);
    CREATE INDEX IX_trophySubmissions_hunter ON dbo.trophySubmissions(hunterUserId, status, createdAt DESC);
END;

IF OBJECT_ID(N'dbo.trophyReviewActions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.trophyReviewActions (
        trophyReviewActionId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_trophyReviewActions PRIMARY KEY,
        submissionId BIGINT NOT NULL CONSTRAINT FK_trophyReviewActions_submission REFERENCES dbo.trophySubmissions(submissionId),
        reviewerUserId INT NOT NULL CONSTRAINT FK_trophyReviewActions_user REFERENCES dbo.users(userId),
        reviewerUserRoleId INT NOT NULL CONSTRAINT FK_trophyReviewActions_role REFERENCES dbo.userRoles(userRoleId),
        reviewerRole NVARCHAR(20) NOT NULL,
        action NVARCHAR(30) NOT NULL,
        reason NVARCHAR(1000) NULL,
        actionKey UNIQUEIDENTIFIER NOT NULL,
        createdAt DATETIME2 NOT NULL CONSTRAINT DF_trophyReviewActions_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_trophyReviewActions_key UNIQUE(actionKey),
        CONSTRAINT CK_trophyReviewActions_role CHECK (reviewerRole = N'PH')
    );
    CREATE INDEX IX_trophyReviewActions_submission ON dbo.trophyReviewActions(submissionId, createdAt);
END;

IF OBJECT_ID(N'dbo.youtubeChannelConnections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.youtubeChannelConnections (
        youtubeChannelConnectionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_youtubeChannelConnections PRIMARY KEY,
        channelId NVARCHAR(80) NOT NULL,
        channelTitle NVARCHAR(180) NOT NULL,
        encryptedRefreshToken VARBINARY(MAX) NOT NULL,
        tokenProtectionKeyId NVARCHAR(120) NOT NULL,
        connectedByUserId INT NOT NULL CONSTRAINT FK_youtubeChannelConnections_user REFERENCES dbo.users(userId),
        status NVARCHAR(30) NOT NULL CONSTRAINT DF_youtubeChannelConnections_status DEFAULT N'Active',
        connectedAt DATETIME2 NOT NULL CONSTRAINT DF_youtubeChannelConnections_connected DEFAULT SYSUTCDATETIME(),
        lastVerifiedAt DATETIME2 NULL,
        revokedAt DATETIME2 NULL,
        CONSTRAINT UQ_youtubeChannelConnections_channel UNIQUE(channelId)
    );
END;

IF OBJECT_ID(N'dbo.youtubePublishJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.youtubePublishJobs (
        youtubePublishJobId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_youtubePublishJobs PRIMARY KEY,
        recapId BIGINT NOT NULL CONSTRAINT FK_youtubePublishJobs_recap REFERENCES dbo.bookingRecaps(recapId),
        channelConnectionId INT NOT NULL CONSTRAINT FK_youtubePublishJobs_channel REFERENCES dbo.youtubeChannelConnections(youtubeChannelConnectionId),
        requestedByUserId INT NOT NULL CONSTRAINT FK_youtubePublishJobs_user REFERENCES dbo.users(userId),
        idempotencyKey UNIQUEIDENTIFIER NOT NULL,
        status NVARCHAR(30) NOT NULL CONSTRAINT DF_youtubePublishJobs_status DEFAULT N'Queued',
        uploadProgress INT NOT NULL CONSTRAINT DF_youtubePublishJobs_progress DEFAULT 0,
        resumableSessionUriEncrypted VARBINARY(MAX) NULL,
        youtubeVideoId NVARCHAR(40) NULL,
        expectedPrivacyStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_youtubePublishJobs_privacy DEFAULT N'private',
        processingStatus NVARCHAR(60) NULL,
        attemptCount INT NOT NULL CONSTRAINT DF_youtubePublishJobs_attempts DEFAULT 0,
        nextAttemptAt DATETIME2 NOT NULL CONSTRAINT DF_youtubePublishJobs_next DEFAULT SYSUTCDATETIME(),
        leaseOwner NVARCHAR(120) NULL,
        leaseExpiresAt DATETIME2 NULL,
        lastError NVARCHAR(1000) NULL,
        createdAt DATETIME2 NOT NULL CONSTRAINT DF_youtubePublishJobs_created DEFAULT SYSUTCDATETIME(),
        updatedAt DATETIME2 NOT NULL CONSTRAINT DF_youtubePublishJobs_updated DEFAULT SYSUTCDATETIME(),
        verifiedAt DATETIME2 NULL,
        CONSTRAINT UQ_youtubePublishJobs_key UNIQUE(idempotencyKey),
        CONSTRAINT UQ_youtubePublishJobs_recap UNIQUE(recapId),
        CONSTRAINT CK_youtubePublishJobs_progress CHECK (uploadProgress BETWEEN 0 AND 100)
    );
    CREATE INDEX IX_youtubePublishJobs_queue ON dbo.youtubePublishJobs(status, nextAttemptAt, leaseExpiresAt);
END;

-- Private is the only permitted publication mode for the initial rollout.
-- Replace an earlier unlisted default when this migration is rerun, and make
-- any job that has not already published fail closed to private.
IF OBJECT_ID(N'dbo.youtubePublishJobs', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.youtubePublishJobs')
          AND c.name = N'expectedPrivacyStatus'
          AND REPLACE(REPLACE(REPLACE(dc.definition, N'(', N''), N')', N''), N' ', N'') = N'N''private'''
    )
    BEGIN
        DECLARE @YoutubePrivacyDefaultName SYSNAME;
        SELECT @YoutubePrivacyDefaultName = dc.name
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.youtubePublishJobs')
          AND c.name = N'expectedPrivacyStatus';

        IF @YoutubePrivacyDefaultName IS NOT NULL
        BEGIN
            DECLARE @YoutubePrivacyDropSql NVARCHAR(MAX) =
                N'ALTER TABLE dbo.youtubePublishJobs DROP CONSTRAINT ' + QUOTENAME(@YoutubePrivacyDefaultName) + N';';
            EXEC sys.sp_executesql @YoutubePrivacyDropSql;
        END;

        ALTER TABLE dbo.youtubePublishJobs
            ADD CONSTRAINT DF_youtubePublishJobs_privacy DEFAULT N'private' FOR expectedPrivacyStatus;
    END;

    UPDATE dbo.youtubePublishJobs
    SET expectedPrivacyStatus = N'private',
        updatedAt = SYSUTCDATETIME()
    WHERE status <> N'Published'
      AND expectedPrivacyStatus <> N'private';
END;

IF OBJECT_ID(N'dbo.bookingVideoArchives', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bookingVideoArchives (
        archiveId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_bookingVideoArchives PRIMARY KEY,
        bookingId INT NOT NULL CONSTRAINT FK_bookingVideoArchives_booking REFERENCES dbo.bookings(bookingId),
        recapId BIGINT NOT NULL CONSTRAINT FK_bookingVideoArchives_recap REFERENCES dbo.bookingRecaps(recapId),
        youtubeVideoId NVARCHAR(40) NOT NULL,
        youtubeUrl NVARCHAR(500) NOT NULL,
        youtubeChannelId NVARCHAR(80) NOT NULL,
        title NVARCHAR(180) NOT NULL,
        privacyStatus NVARCHAR(30) NOT NULL,
        uploadedAt DATETIME2 NOT NULL,
        verifiedAt DATETIME2 NOT NULL,
        sourceMediaCount INT NOT NULL,
        hunterPhotoCount INT NOT NULL,
        phPhotoCount INT NOT NULL,
        vgPhotoCount INT NOT NULL,
        sourceRouteId BIGINT NOT NULL,
        generatedVideoDurationSeconds INT NOT NULL,
        generatedVideoSize BIGINT NOT NULL,
        cleanupStatus NVARCHAR(30) NOT NULL CONSTRAINT DF_bookingVideoArchives_cleanup DEFAULT N'Retained',
        cleanupStartedAt DATETIME2 NULL,
        cleanupCompletedAt DATETIME2 NULL,
        sourceFilesDeletedAt DATETIME2 NULL,
        archiveVersion INT NOT NULL,
        createdAt DATETIME2 NOT NULL CONSTRAINT DF_bookingVideoArchives_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_bookingVideoArchives_recap UNIQUE(recapId),
        CONSTRAINT UQ_bookingVideoArchives_video UNIQUE(youtubeVideoId)
    );
    CREATE INDEX IX_bookingVideoArchives_booking ON dbo.bookingVideoArchives(bookingId, createdAt DESC);
END;

IF OBJECT_ID(N'dbo.mediaCleanupJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.mediaCleanupJobs (
        mediaCleanupJobId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_mediaCleanupJobs PRIMARY KEY,
        bookingId INT NOT NULL CONSTRAINT FK_mediaCleanupJobs_booking REFERENCES dbo.bookings(bookingId),
        recapId BIGINT NOT NULL CONSTRAINT FK_mediaCleanupJobs_recap REFERENCES dbo.bookingRecaps(recapId),
        archiveId BIGINT NOT NULL CONSTRAINT FK_mediaCleanupJobs_archive REFERENCES dbo.bookingVideoArchives(archiveId),
        requestedByUserId INT NULL CONSTRAINT FK_mediaCleanupJobs_user REFERENCES dbo.users(userId),
        idempotencyKey UNIQUEIDENTIFIER NOT NULL,
        status NVARCHAR(30) NOT NULL CONSTRAINT DF_mediaCleanupJobs_status DEFAULT N'CleanupPending',
        notBefore DATETIME2 NOT NULL,
        attemptCount INT NOT NULL CONSTRAINT DF_mediaCleanupJobs_attempts DEFAULT 0,
        leaseOwner NVARCHAR(120) NULL,
        leaseExpiresAt DATETIME2 NULL,
        remainingFilesJson NVARCHAR(MAX) NULL,
        lastError NVARCHAR(1000) NULL,
        createdAt DATETIME2 NOT NULL CONSTRAINT DF_mediaCleanupJobs_created DEFAULT SYSUTCDATETIME(),
        updatedAt DATETIME2 NOT NULL CONSTRAINT DF_mediaCleanupJobs_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_mediaCleanupJobs_key UNIQUE(idempotencyKey),
        CONSTRAINT UQ_mediaCleanupJobs_recap UNIQUE(recapId),
        CONSTRAINT CK_mediaCleanupJobs_files CHECK (remainingFilesJson IS NULL OR ISJSON(remainingFilesJson) = 1)
    );
    CREATE INDEX IX_mediaCleanupJobs_queue ON dbo.mediaCleanupJobs(status, notBefore);
END;

IF COL_LENGTH(N'dbo.trophySubmissions', N'antlerSizeCm') IS NULL
    ALTER TABLE dbo.trophySubmissions ADD antlerSizeCm DECIMAL(10,2) NULL;
IF COL_LENGTH(N'dbo.trophySubmissions', N'carcassWeightKg') IS NULL
    ALTER TABLE dbo.trophySubmissions ADD carcassWeightKg DECIMAL(10,2) NULL;
IF COL_LENGTH(N'dbo.mediaCleanupJobs', N'leaseOwner') IS NULL
    ALTER TABLE dbo.mediaCleanupJobs ADD leaseOwner NVARCHAR(120) NULL;
IF COL_LENGTH(N'dbo.mediaCleanupJobs', N'leaseExpiresAt') IS NULL
    ALTER TABLE dbo.mediaCleanupJobs ADD leaseExpiresAt DATETIME2 NULL;

IF COL_LENGTH(N'dbo.trophies', N'tApprovalStatus') IS NULL
    ALTER TABLE dbo.trophies ADD tApprovalStatus NVARCHAR(30) NULL;
GO

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.trophies')
      AND name = N'tApprovalStatus'
      AND is_nullable = 1
)
BEGIN
    UPDATE dbo.trophies SET tApprovalStatus = N'Approved' WHERE tApprovalStatus IS NULL;
    ALTER TABLE dbo.trophies ALTER COLUMN tApprovalStatus NVARCHAR(30) NOT NULL;
END;
IF OBJECT_ID(N'dbo.DF_trophies_approvalStatus', N'D') IS NULL
    ALTER TABLE dbo.trophies ADD CONSTRAINT DF_trophies_approvalStatus DEFAULT N'Approved' FOR tApprovalStatus;
IF OBJECT_ID(N'dbo.CK_trophies_approvalStatus', N'C') IS NULL
    ALTER TABLE dbo.trophies ADD CONSTRAINT CK_trophies_approvalStatus CHECK (tApprovalStatus IN (N'Approved', N'Archived'));

IF COL_LENGTH(N'dbo.trophies', N'tSourceSubmissionId') IS NULL
    ALTER TABLE dbo.trophies ADD tSourceSubmissionId BIGINT NULL;
GO

IF OBJECT_ID(N'dbo.FK_trophies_sourceSubmission', N'F') IS NULL
    ALTER TABLE dbo.trophies ADD CONSTRAINT FK_trophies_sourceSubmission FOREIGN KEY (tSourceSubmissionId) REFERENCES dbo.trophySubmissions(submissionId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.trophies') AND name = N'UX_trophies_sourceSubmission')
    CREATE UNIQUE INDEX UX_trophies_sourceSubmission ON dbo.trophies(tSourceSubmissionId) WHERE tSourceSubmissionId IS NOT NULL;

COMMIT TRANSACTION;
