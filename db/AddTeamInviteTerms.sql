/*
  The PJ's invite form asks for an expedition name and the offered terms, but
  dbo.teamInvites had nowhere to keep them, so the invited PH/VG never saw the offer.
  Idempotent.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.teamInvites', N'U') IS NULL
BEGIN
    RAISERROR('Die dbo.teamInvites-tabel bestaan nie in hierdie databasis nie.', 16, 1);
    RETURN;
END;
GO

IF COL_LENGTH(N'dbo.teamInvites', N'inviteExpeditionName') IS NULL
    ALTER TABLE dbo.teamInvites ADD inviteExpeditionName NVARCHAR(200) NULL;
GO

IF COL_LENGTH(N'dbo.teamInvites', N'inviteTerms') IS NULL
    ALTER TABLE dbo.teamInvites ADD inviteTerms NVARCHAR(1000) NULL;
GO
