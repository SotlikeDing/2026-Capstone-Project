/*
  The admin's "Aktiveer" button used to store 'Approved' instead of 'Active', which the user list
  then showed as a third state. Accounts now have two states, Active and Suspended. Also describes
  each feature flag by what it actually does. Idempotent.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.users', 'uAccountStatus') IS NOT NULL
BEGIN
    -- uIsSuspended is what sign-in checks; make the status the admin sees agree with it.
    EXEC (N'UPDATE dbo.users SET uAccountStatus = ''Active''
            WHERE (uAccountStatus IS NULL OR uAccountStatus = ''Approved'') AND ISNULL(uIsSuspended, 0) = 0;
          UPDATE dbo.users SET uAccountStatus = ''Suspended''
            WHERE ISNULL(uIsSuspended, 0) = 1 AND (uAccountStatus IS NULL OR uAccountStatus IN (''Active'', ''Approved''));');
END;
GO

IF OBJECT_ID(N'dbo.featureFlags', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.featureFlags SET flagDescription = N'Onderhoudmodus: net administrateurs kan die webwerf en app gebruik; almal anders sien ''n onderhoudboodskap.'
        WHERE flagKey = 'Enable_Maintenance_Mode';
    UPDATE dbo.featureFlags SET flagDescription = N'Nuwe registrasies: AF = niemand kan ''n nuwe rekening skep nie (bestaande gebruikers kan steeds aanmeld).'
        WHERE flagKey = 'Enable_New_User_Registrations';
    UPDATE dbo.featureFlags SET flagDescription = N'PayFast-toetsomgewing: AAN = betalings gaan na PayFast se sandbox; AF = regte betalings (vereis regte handelaarbesonderhede).'
        WHERE flagKey = 'Enable_PayFast_Sandbox';
END;
GO
