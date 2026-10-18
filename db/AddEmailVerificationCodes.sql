/*
  Pending e-mail verification codes. Previously these lived in a static in-memory
  dictionary, so every deploy/restart silently invalidated all outstanding codes and
  wrong guesses were unlimited. Codes are stored hashed (SHA-256), expire, and lock
  after too many wrong attempts. Idempotent.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.emailVerificationCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.emailVerificationCodes (
        email NVARCHAR(320) NOT NULL CONSTRAINT PK_emailVerificationCodes PRIMARY KEY,
        codeHash VARBINARY(32) NOT NULL,
        expiresAt DATETIME2 NOT NULL,
        failedAttempts INT NOT NULL CONSTRAINT DF_emailVerificationCodes_failed DEFAULT 0,
        createdAt DATETIME2 NOT NULL CONSTRAINT DF_emailVerificationCodes_created DEFAULT SYSUTCDATETIME()
    );
END;
GO
