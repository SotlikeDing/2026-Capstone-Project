/*
  Hunting-permit applications (api/huntingpermit/newApplication) were stored in
  dbo.userDocumentation as document type 1 ("ID Dokument" - the code assumed 1 meant
  SAPS 520). That collided with the one-active-document-per-type index (a second
  application failed, and a hunter's real ID document blocked it) and mixed
  applications into the verification queue. They now live in dbo.sapsApplications
  next to the SAPS 520 forms. This moves the existing rows across. Idempotent.
  Requires AddSapsApplicationsAndPermits.sql.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.sapsApplications', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.sapsApplications', N'applicationType') IS NULL
    ALTER TABLE dbo.sapsApplications ADD applicationType NVARCHAR(30) NOT NULL
        CONSTRAINT DF_sapsApplications_type DEFAULT N'SAPS520';
GO

IF OBJECT_ID(N'dbo.sapsApplications', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.userDocumentation', N'udIsDeleted') IS NOT NULL
BEGIN
    INSERT dbo.sapsApplications (userId, userRoleId, formJson, status, submittedAt, applicationType)
    SELECT ur.urUserId, d.udUserRole, ISNULL(NULLIF(d.udFilePath, N''), N'{}'), ISNULL(d.udStatus, N'Saved'),
           ISNULL(d.udUploadedTimestamp, SYSUTCDATETIME()), N'HuntingPermit'
    FROM dbo.userDocumentation d
    JOIN dbo.userRoles ur ON ur.userRoleId = d.udUserRole
    WHERE d.udName LIKE N'SAPS 520 - %' AND d.udDtId = 1 AND ISNULL(d.udIsDeleted, 0) = 0;

    UPDATE dbo.userDocumentation SET udIsDeleted = 1
    WHERE udName LIKE N'SAPS 520 - %' AND udDtId = 1 AND ISNULL(udIsDeleted, 0) = 0;
END;
GO
