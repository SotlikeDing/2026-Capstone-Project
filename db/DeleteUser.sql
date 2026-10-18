DECLARE @Email NVARCHAR(MAX) = 'oosthuysensolutions@gmail.com';

SELECT userId INTO #UsersToDelete FROM users WHERE uEmail = @Email;

IF EXISTS (SELECT 1 FROM #UsersToDelete)
BEGIN
    BEGIN TRAN;

    -- Delete AuthTokens
    DELETE FROM AuthTokens WHERE atUserId IN (SELECT userId FROM #UsersToDelete);

    -- Delete Chat Participants
    DELETE FROM chatParticipants WHERE cpUserId IN (SELECT userId FROM #UsersToDelete);

    -- Chat Messages: set cpLastReadMessage to null first
    UPDATE chatParticipants SET cpLastReadMessage = NULL 
    WHERE cpLastReadMessage IN (
        SELECT chatMessageId FROM chatMessages WHERE cmSenderId IN (SELECT userId FROM #UsersToDelete)
    );
    DELETE FROM chatMessages WHERE cmSenderId IN (SELECT userId FROM #UsersToDelete);

    -- Find UserRoles
    SELECT userRoleId INTO #RolesToDelete FROM userRoles WHERE urUserId IN (SELECT userId FROM #UsersToDelete);
    
    IF EXISTS (SELECT 1 FROM #RolesToDelete)
    BEGIN
        -- Team Invites
        DELETE FROM teamInvites WHERE invitePjId IN (SELECT userRoleId FROM #RolesToDelete) OR inviteMemberId IN (SELECT userRoleId FROM #RolesToDelete);

        -- Team
        DELETE FROM team WHERE teamPjId IN (SELECT userRoleId FROM #RolesToDelete) OR teamMemberId IN (SELECT userRoleId FROM #RolesToDelete);

        -- User Documentation
        DELETE FROM userDocumentation WHERE udUserRole IN (SELECT userRoleId FROM #RolesToDelete);

        -- Collect all Bookings to delete
        SELECT bookingId INTO #BookingsToDelete FROM bookings 
        WHERE bookHunterId IN (SELECT userRoleId FROM #RolesToDelete)
           OR bookPhId IN (SELECT phId FROM ph WHERE phUserRoleId IN (SELECT userRoleId FROM #RolesToDelete))
           OR bookVgId IN (SELECT vgId FROM vg WHERE vgUserRoleId IN (SELECT userRoleId FROM #RolesToDelete));

        -- Delete Story Data
        DELETE FROM storyDataPoint WHERE sdpStoryDataId IN (SELECT storyDataId FROM storyData WHERE sdBookingId IN (SELECT bookingId FROM #BookingsToDelete));
        DELETE FROM storyData WHERE sdBookingId IN (SELECT bookingId FROM #BookingsToDelete);

        -- Ratings (must go before Bookings below - ratings FK references bookings).
        -- Covers both ratings tied to a booking being deleted, and ratings the
        -- deleted user gave/received on a booking that isn't otherwise being deleted.
        DELETE FROM ratings
        WHERE bookingId IN (SELECT bookingId FROM #BookingsToDelete)
           OR raterUserId IN (SELECT userId FROM #UsersToDelete)
           OR rateeUserId IN (SELECT userId FROM #UsersToDelete);

        -- Delete Bookings
        DELETE FROM bookings WHERE bookingId IN (SELECT bookingId FROM #BookingsToDelete);

        -- Admins
        DELETE FROM admin WHERE adminUserRoleId IN (SELECT userRoleId FROM #RolesToDelete);

        -- AP
        DELETE FROM advertisements WHERE adApId IN (SELECT apId FROM ap WHERE apUserRoleId IN (SELECT userRoleId FROM #RolesToDelete));
        DELETE FROM ap WHERE apUserRoleId IN (SELECT userRoleId FROM #RolesToDelete);

        -- Farmer
        SELECT farmId INTO #FarmsToDelete FROM farm WHERE farmFarmerId IN (SELECT farmerId FROM farmer WHERE fUserRoleId IN (SELECT userRoleId FROM #RolesToDelete));
        DELETE FROM availableAnimals WHERE aaFarmId IN (SELECT farmId FROM #FarmsToDelete);
        
        -- Delete packages for farm OR PJ
        DELETE FROM packages WHERE pFarmId IN (SELECT farmId FROM #FarmsToDelete) OR pPjId IN (SELECT pjId FROM pj WHERE pjUserRoleId IN (SELECT userRoleId FROM #RolesToDelete));
        
        DELETE FROM farmLodge WHERE flFarmId IN (SELECT farmId FROM #FarmsToDelete);
        DELETE FROM farm WHERE farmId IN (SELECT farmId FROM #FarmsToDelete);
        
        DELETE FROM farmer WHERE fUserRoleId IN (SELECT userRoleId FROM #RolesToDelete);

        -- PJ
        DELETE FROM pj WHERE pjUserRoleId IN (SELECT userRoleId FROM #RolesToDelete);

        -- PH
        DELETE FROM phPricing WHERE phpPhId IN (SELECT phId FROM ph WHERE phUserRoleId IN (SELECT userRoleId FROM #RolesToDelete));
        DELETE FROM ph WHERE phUserRoleId IN (SELECT userRoleId FROM #RolesToDelete);

        -- VG
        DELETE FROM vgAvailableProvinces WHERE vgapVgId IN (SELECT vgId FROM vg WHERE vgUserRoleId IN (SELECT userRoleId FROM #RolesToDelete));
        -- DELETE FROM vgPackages WHERE vgId IN (SELECT vgId FROM vg WHERE vgUserRoleId IN (SELECT userRoleId FROM #RolesToDelete));
        DELETE FROM vg WHERE vgUserRoleId IN (SELECT userRoleId FROM #RolesToDelete);

        -- Hunter
        DELETE FROM hunter WHERE hUserRoleId IN (SELECT userRoleId FROM #RolesToDelete);

        -- Delete userRoles
        DELETE FROM userRoles WHERE urUserId IN (SELECT userId FROM #UsersToDelete);
    END
    
    -- Delete Users
    DELETE FROM users WHERE userId IN (SELECT userId FROM #UsersToDelete);
    
    COMMIT TRAN;
END
