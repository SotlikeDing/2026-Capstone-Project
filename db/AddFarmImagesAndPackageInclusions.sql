/*
  Farm photos and what a hunting package does and does not include.
  - farm.farmImagesJson: JSON array of image data URLs (the same format as advertisements.adImagesJson),
    served one by one through api/farmer/{farmId}/images/{index} so lists never carry the bytes.
  - packages.pInclusions / pExclusions: one item per line, shown as "Ingesluit" / "Nie ingesluit nie".
    pIncludedItems keeps its existing meaning (huntable species + observer price).
  Idempotent.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.farm', N'farmImagesJson') IS NULL
    ALTER TABLE dbo.farm ADD farmImagesJson NVARCHAR(MAX) NULL;
GO

IF COL_LENGTH(N'dbo.packages', N'pInclusions') IS NULL
    ALTER TABLE dbo.packages ADD pInclusions NVARCHAR(2000) NULL;
GO

IF COL_LENGTH(N'dbo.packages', N'pExclusions') IS NULL
    ALTER TABLE dbo.packages ADD pExclusions NVARCHAR(2000) NULL;
GO
