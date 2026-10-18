/*
  Package prices (and outfitter quotes by default) in rand: 1 = ZAR, 0 = US$ (international
  hunters). The website shows every package price, label and quote in this currency, the AI
  helper advises in it, and PayFast charges a booking without its own currency in it. Idempotent;
  starts at ZAR for the South African market.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.systemSettings', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.systemSettings WHERE settingKey = 'PackagePricesInZar')
    INSERT INTO dbo.systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('PackagePricesInZar', '1', 'Pakketpryse in rand: 1 = ZAR (R), 0 = Amerikaanse dollar (US$) vir internasionale jagters.', 1);
GO
