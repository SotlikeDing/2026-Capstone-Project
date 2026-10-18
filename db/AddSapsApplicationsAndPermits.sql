/*
  SAPS 520 applications get their own table. They used to be written into
  dbo.userDocumentation with the hunter's *userId* in the udUserRole column (so they
  landed on whatever role happened to have that id - another user's profile) and as
  document type 1 "ID Dokument", which collided with the one-active-document-per-type
  index on a second submission.

  Also creates dbo.permits (the hunter's permit wallet, read by api/Permits) which only
  existed in a loose create_permits.sql that was never part of the migrations.
  Idempotent.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.sapsApplications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.sapsApplications (
        sapsApplicationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_sapsApplications PRIMARY KEY,
        userId INT NOT NULL CONSTRAINT FK_sapsApplications_users REFERENCES dbo.users(userId),
        userRoleId INT NULL CONSTRAINT FK_sapsApplications_userRoles REFERENCES dbo.userRoles(userRoleId),
        formJson NVARCHAR(MAX) NOT NULL,
        status NVARCHAR(30) NOT NULL CONSTRAINT DF_sapsApplications_status DEFAULT N'Submitted',
        submittedAt DATETIME2 NOT NULL CONSTRAINT DF_sapsApplications_submitted DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_sapsApplications_user ON dbo.sapsApplications(userId, submittedAt);
END;
GO

-- Repair: copy misfiled SAPS rows to the right user (the old code stored the userId in
-- udUserRole) and retire them from the wrong profile. Only rows that still look exactly
-- like the old writer's output are touched.
IF COL_LENGTH(N'dbo.userDocumentation', N'udIsDeleted') IS NOT NULL
BEGIN
    INSERT dbo.sapsApplications (userId, userRoleId, formJson, status, submittedAt)
    SELECT d.udUserRole,
           (SELECT TOP 1 ur.userRoleId FROM dbo.userRoles ur JOIN dbo.roles r ON r.roleId = ur.urRoleId
            WHERE ur.urUserId = d.udUserRole AND UPPER(r.roleCode) IN (N'HUNTER', N'JAGTER') ORDER BY ur.userRoleId),
           ISNULL(d.udFilePath, N'{}'), N'Submitted', ISNULL(d.udUploadedTimestamp, SYSUTCDATETIME())
    FROM dbo.userDocumentation d
    WHERE d.udName LIKE N'SAPS 520 Permit - %' AND d.udDtId = 1 AND ISNULL(d.udIsDeleted, 0) = 0
      AND EXISTS (SELECT 1 FROM dbo.users u WHERE u.userId = d.udUserRole)
      AND NOT EXISTS (SELECT 1 FROM dbo.sapsApplications s WHERE s.userId = d.udUserRole AND s.formJson = ISNULL(d.udFilePath, N'{}'));

    UPDATE dbo.userDocumentation SET udIsDeleted = 1
    WHERE udName LIKE N'SAPS 520 Permit - %' AND udDtId = 1 AND ISNULL(udIsDeleted, 0) = 0;
END;
GO

IF OBJECT_ID(N'dbo.permits', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.permits (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL,
        PermitNumber NVARCHAR(100) NOT NULL,
        PermitType NVARCHAR(100) NOT NULL,
        IssueDate DATETIME NOT NULL,
        ExpiryDate DATETIME NOT NULL,
        DocumentUrl NVARCHAR(500) NOT NULL,
        Province NVARCHAR(50) NOT NULL
    );
END;
GO
