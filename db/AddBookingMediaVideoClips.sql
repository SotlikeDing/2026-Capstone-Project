/*
  Short VG video clips on shared booking media. Clips live in the same private
  storage and dbo.bookingMedia rows as photos; sourceType distinguishes them and
  durationSeconds lets the recap planner budget video length before rendering.
  Idempotent - safe to run more than once. Requires AddSharedBookingMediaRecaps.sql.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.bookingMedia', N'durationSeconds') IS NULL
    ALTER TABLE dbo.bookingMedia ADD durationSeconds DECIMAL(9,3) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_bookingMedia_sourceType')
    ALTER TABLE dbo.bookingMedia WITH CHECK ADD CONSTRAINT CK_bookingMedia_sourceType
        CHECK (sourceType IN (N'Photo', N'Video'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_bookingMedia_videoDuration')
    ALTER TABLE dbo.bookingMedia WITH CHECK ADD CONSTRAINT CK_bookingMedia_videoDuration
        CHECK ((sourceType = N'Photo' AND durationSeconds IS NULL)
            OR (sourceType = N'Video' AND durationSeconds IS NOT NULL AND durationSeconds > 0));
GO
