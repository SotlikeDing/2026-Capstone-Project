/*
  Outfitters quote bookings in US$ (the norm for foreign hunters) or in rand, and the choice is
  stored in the booking's price breakdown. PayFast only takes rand, so a US$ quote is converted at
  this admin-editable rate when the hunter pays; the website also shows "~ R ..." next to US$
  amounts. Idempotent.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.systemSettings', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.systemSettings WHERE settingKey = 'UsdToZarRate')
    INSERT INTO dbo.systemSettings (settingKey, settingValue, settingDescription, isPublic)
    VALUES ('UsdToZarRate', '18.00', 'Wisselkoers: rand per 1 US-dollar. Uitrusterkwotasies in US$ word teen hierdie koers in rand by PayFast betaal.', 1);
GO
