-- Run this script against the existing Mikpunt database.
-- Adds a generic key/value settings table so business & operational constants
-- (ad price, subscription tiers, booking deposit %, JWT/rate-limit tuning) are
-- admin-configurable at runtime instead of hardcoded in the C#/Razor source.
-- Idempotent: safe to re-run. Does not touch existing data (seed rows only inserted
-- if the table itself is being created for the first time).

USE Mikpunt;
GO

IF OBJECT_ID(N'dbo.systemSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.systemSettings (
        settingKey NVARCHAR(100) PRIMARY KEY,
        settingValue NVARCHAR(200) NOT NULL,
        settingDescription NVARCHAR(500) NOT NULL,
        isPublic BIT NOT NULL DEFAULT 0,
        lastUpdated DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updatedBy NVARCHAR(200) NOT NULL DEFAULT 'System'
    );

    INSERT INTO dbo.systemSettings (settingKey, settingValue, settingDescription, isPublic) VALUES
        ('AdMonthlyPrice', '100', 'Advertensiefooi per maand (R)', 1),
        ('SubscriptionPrice_PH', '299', 'Maandelikse intekenfooi vir PH-rol (R)', 1),
        ('SubscriptionPrice_VG', '249', 'Maandelikse intekenfooi vir VG-rol (R)', 1),
        ('SubscriptionPrice_Boer', '499', 'Maandelikse intekenfooi vir Boer-rol (R)', 1),
        ('SubscriptionPrice_PJ', '699', 'Maandelikse intekenfooi vir PJ-rol (R)', 1),
        ('BookingDepositPercentage', '100', 'Persentasie van die totale prys wat as deposito/betaling verskuldig is', 1),
        ('JwtExpiryHours', '24', 'Hoe lank (ure) is ''n JWT toegangstoken geldig', 0),
        ('RefreshTokenExpiryDays', '7', 'Hoe lank (dae) is ''n verfristoken geldig', 0),
        ('LoginLockoutMaxAttempts', '5', 'Aantal mislukte aanmeldpogings voor rekeningslot', 0),
        ('LoginLockoutDurationMinutes', '15', 'Duur (minute) van rekeningslot na te veel mislukte pogings', 0),
        ('SignInRateLimitPerMinute', '15', 'Maksimum aanmeldpogings per minuut per IP-adres', 0),
        ('AdminRateLimitPermitLimit', '30', 'Maksimum admin-API versoeke binne die tydvenster', 0),
        ('AdminRateLimitWindowSeconds', '10', 'Tydvenster (sekondes) vir die admin-API tempoperk', 0),
        ('RatingRateLimitPermitLimit', '10', 'Maksimum gradering-indienings binne die tydvenster, per gebruiker', 0),
        ('RatingRateLimitWindowSeconds', '60', 'Tydvenster (sekondes) vir die gradering-indien tempoperk', 0),
        ('RatingReadRateLimitPermitLimit', '60', 'Maksimum gradering-leesversoeke (publiek) binne die tydvenster, per IP', 0),
        ('RatingReadRateLimitWindowSeconds', '60', 'Tydvenster (sekondes) vir die gradering-lees tempoperk', 0);

    PRINT 'systemSettings table created and seeded with 17 settings.';
END
ELSE
BEGIN
    PRINT 'systemSettings table already exists.';
END;
GO
