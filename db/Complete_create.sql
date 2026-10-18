  -- ============================================================
    -- MIKPUNT DATABASE - COMPLETE SCHEMA AND FULL TEST DATA
    -- Corrected version: no GO inside data insertion, and all
    -- INSERT ... WHERE are replaced with IF NOT EXISTS blocks.
    -- ============================================================

    IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'Mikpunt')
    BEGIN
        CREATE DATABASE Mikpunt;
    END
    GO

    USE Mikpunt;
    GO

    -- ============================================================
    -- 1. SCHEMA: All tables (including trophy room and team invites)
    -- ============================================================

    CREATE TABLE addresses (
        addressId INT IDENTITY(1,1) PRIMARY KEY,
        addressNumber NVARCHAR(MAX),
        addressStreetName NVARCHAR(MAX),
        addressCity NVARCHAR(MAX),
        addressRegion NVARCHAR(MAX),
        addressPostalCode NVARCHAR(MAX),
        addressCountry NVARCHAR(MAX),
        addressLatitude DECIMAL(38, 5),
        addressLongitude DECIMAL(38, 5)
    );

    CREATE TABLE documentType (
        dtId INT IDENTITY(1,1) PRIMARY KEY,
        dtName NVARCHAR(MAX),
        dtNumber NVARCHAR(MAX)
    );

    CREATE TABLE roles (
        roleId INT IDENTITY(1,1) PRIMARY KEY,
        roleCode NVARCHAR(MAX),
        roleName NVARCHAR(MAX)
    );

    CREATE TABLE provinces (
        provId INT IDENTITY(1,1) PRIMARY KEY,
        provName NVARCHAR(MAX)
    );

    CREATE TABLE users (
        userId INT IDENTITY(1,1) PRIMARY KEY,
        uName NVARCHAR(MAX),
        uSurname NVARCHAR(MAX),
        uEmail NVARCHAR(MAX),
        uPassword NVARCHAR(MAX),
        uRegistrationDate DATETIME2,
        uPhone NVARCHAR(15),
        uIsEmailVerified BIT,
        uIsPhoneVerified BIT,
        uIsSuspended BIT DEFAULT 0,
        uRowlandWardEnabled BIT NOT NULL CONSTRAINT DF_users_uRowlandWardEnabled DEFAULT (0)
    );

    CREATE TABLE userRoles (
        userRoleId INT IDENTITY(1,1) PRIMARY KEY,
        urUserId INT FOREIGN KEY REFERENCES users(userId),
        urRoleId INT FOREIGN KEY REFERENCES roles(roleId)
    );

    CREATE TABLE admin (
        adminId INT IDENTITY(1,1) PRIMARY KEY,
        adminUserRoleId INT FOREIGN KEY REFERENCES userRoles(userRoleId)
    );

    CREATE TABLE ap (
        apId INT IDENTITY(1,1) PRIMARY KEY,
        apUserRoleId INT FOREIGN KEY REFERENCES userRoles(userRoleId)
    );

    CREATE TABLE farmer (
        farmerId INT IDENTITY(1,1) PRIMARY KEY,
        fUserRoleId INT FOREIGN KEY REFERENCES userRoles(userRoleId),
        fBusinessName NVARCHAR(MAX),
        fBusinessRegistrationNumber NVARCHAR(MAX),
        fPubliclyVisibleTill DATETIME2,
        fBio NVARCHAR(MAX)
    );

    CREATE TABLE pj (
        pjId INT IDENTITY(1,1) PRIMARY KEY,
        pjUserRoleId INT FOREIGN KEY REFERENCES userRoles(userRoleId),
        pjBusinessName NVARCHAR(MAX),
        pjBusinessRegistrationNumber NVARCHAR(MAX),
        pjPubliclyVisibleTill DATETIME2,
        pjYearsOfExperience BIGINT,
        pjBio NVARCHAR(MAX)
    );

    CREATE TABLE ph (
        phId INT IDENTITY(1,1) PRIMARY KEY,
        phUserRoleId INT FOREIGN KEY REFERENCES userRoles(userRoleId),
        phPubliclyVisibleTill DATETIME2,
        phYearsOfExperience BIGINT,
        phBio NVARCHAR(MAX)
    );

    CREATE TABLE vg (
        vgId INT IDENTITY(1,1) PRIMARY KEY,
        vgUserRoleId INT FOREIGN KEY REFERENCES userRoles(userRoleId),
        vgHasDrone BIT,
        vgDroneLicence NVARCHAR(MAX),
        vgPubliclyVisibleTill DATETIME2,
        vgCompanyName NVARCHAR(MAX),
        vgCompanyRegistrationNumber NVARCHAR(MAX),
        vgYearsOfExperience BIGINT
    );

    CREATE TABLE hunter (
        hunterId INT IDENTITY(1,1) PRIMARY KEY,
        hUserRoleId INT FOREIGN KEY REFERENCES userRoles(userRoleId),
        hCountry NVARCHAR(MAX),
        hUsername NVARCHAR(MAX),
        hBio NVARCHAR(MAX)
    );

    CREATE TABLE advertisements (
        advertisementId INT IDENTITY(1,1) PRIMARY KEY,
        adApId INT FOREIGN KEY REFERENCES ap(apId),
        adTitle NVARCHAR(MAX),
        adShortDescription NVARCHAR(MAX),
        adLongDescription NVARCHAR(MAX),
        adType NVARCHAR(MAX),
        adCategory NVARCHAR(MAX),
        adStatus NVARCHAR(MAX),
        adPrice DECIMAL(38, 2),
        adCreatedTimestamp DATETIME2,
        adExpirationTimestamp DATETIME2,
        adContactNumber NVARCHAR(MAX),
        adAddressId INT FOREIGN KEY REFERENCES addresses(addressId),
        adViews INT DEFAULT 0,
        adClicks INT DEFAULT 0,
        adImagesJson NVARCHAR(MAX),
        adTargetUrl NVARCHAR(500),
        adPaymentRef NVARCHAR(100),
        adLastPaymentMonths INT,
        adLastPaymentAmount DECIMAL(18,2),
        adLastPaymentDate DATETIME2,
        adAdvertiserName NVARCHAR(200)
    );

    CREATE TABLE adPayments (
        adPaymentId INT IDENTITY(1,1) PRIMARY KEY,
        adId INT NOT NULL FOREIGN KEY REFERENCES advertisements(advertisementId) ON DELETE CASCADE,
        paymentRef NVARCHAR(100) NOT NULL,
        paymentDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        months INT NOT NULL,
        amount DECIMAL(18,2) NOT NULL,
        periodStartDate DATETIME2 NOT NULL,
        periodEndDate DATETIME2 NOT NULL,
        paymentMethod NVARCHAR(50) DEFAULT 'Simulated Gateway',
        createdTimestamp DATETIME2 NOT NULL DEFAULT GETDATE()
    );

    CREATE TABLE contactUs (
        contactUsId INT IDENTITY(1,1) PRIMARY KEY,
        cuSubject NVARCHAR(MAX),
        cuContent NVARCHAR(MAX),
        cuSenderName NVARCHAR(MAX),
        cuSenderSurname NVARCHAR(MAX),
        cuSenderEmail NVARCHAR(MAX),
        cuPhoneNumber NVARCHAR(15),
        cuType NVARCHAR(MAX),
        cuIsResolved BIT
    );

    CREATE TABLE userDocumentation (
        udId INT IDENTITY(1,1) PRIMARY KEY,
        udUserRole INT FOREIGN KEY REFERENCES userRoles(userRoleId),
        udName NVARCHAR(MAX),
        udFilePath NVARCHAR(MAX),
        udIssuedTimestamp DATETIME2,
        udExpirationTimestamp DATETIME2,
        udStatus NVARCHAR(MAX),
        udUploadedTimestamp DATETIME2,
        udDtId INT FOREIGN KEY REFERENCES documentType(dtId)
    );

    CREATE TABLE chat (
        chatId INT IDENTITY(1,1) PRIMARY KEY,
        cCreatedTimestamp DATETIME2,
        cTitle NVARCHAR(MAX)
    );

    CREATE TABLE chatParticipants (
        chatParticipantId INT IDENTITY(1,1) PRIMARY KEY,
        cpChatId INT FOREIGN KEY REFERENCES chat(chatId),
        cpUserId INT FOREIGN KEY REFERENCES users(userId),
        cpJoinedTimestamp DATETIME2,
        cpLeftAtTimestamp DATETIME2,
        cpLastReadMessage INT
    );

    CREATE TABLE chatMessages (
        chatMessageId INT IDENTITY(1,1) PRIMARY KEY,
        cmChatId INT FOREIGN KEY REFERENCES chat(chatId),
        cmSenderId INT FOREIGN KEY REFERENCES users(userId),
        cmContent NVARCHAR(MAX),
        cmSentTimestamp DATETIME2
    );

    ALTER TABLE chatParticipants ADD CONSTRAINT FK_chatParticipants_lastReadMessage
    FOREIGN KEY (cpLastReadMessage) REFERENCES chatMessages(chatMessageId);

    CREATE TABLE farm (
        farmId INT IDENTITY(1,1) PRIMARY KEY,
        farmFarmerId INT FOREIGN KEY REFERENCES farmer(farmerId),
        farmDescription NVARCHAR(MAX),
        farmMediaFolder NVARCHAR(MAX),
        farmName NVARCHAR(MAX),
        farmSize DECIMAL(38, 18),
        farmAddressId INT FOREIGN KEY REFERENCES addresses(addressId),
        farmTerrainType NVARCHAR(MAX),
        farmFencingType NVARCHAR(MAX),
        farmStatus NVARCHAR(MAX)
    );

    CREATE TABLE farmLodge (
        farmLodgeId INT IDENTITY(1,1) PRIMARY KEY,
        flFarmId INT FOREIGN KEY REFERENCES farm(farmId),
        flPrice DECIMAL(38, 2),
        flRooms BIGINT,
        flCateringIncluded BIT,
        flFacilities NVARCHAR(MAX)
    );

    CREATE TABLE packages (
        packageId INT IDENTITY(1,1) PRIMARY KEY,
        pCreatedTimestamp DATETIME2,
        pAvailableTill DATETIME2,
        pFarmId INT FOREIGN KEY REFERENCES farm(farmId),
        pPjId INT FOREIGN KEY REFERENCES pj(pjId),
        pStockPrice DECIMAL(38, 2),
        pDescription NVARCHAR(MAX),
        pTitle NVARCHAR(MAX),
        pAvailableFrom DATETIME2,
        pMinExpeditionLength BIGINT,
        pMinGuests BIGINT,
        pMaxGuests BIGINT,
        pDurationDays BIGINT,
        pMaxParticipants BIGINT,
        pIncludedItems NVARCHAR(MAX),
        pStatus NVARCHAR(MAX)
    );

    CREATE TABLE bookings (
        bookingId INT IDENTITY(1,1) PRIMARY KEY,
        bookHunterId INT FOREIGN KEY REFERENCES userRoles(userRoleId),
        bookPackageId INT FOREIGN KEY REFERENCES packages(packageId),
        bookStartDate DATE,
        bookEndDate DATE,
        bookStatus NVARCHAR(MAX),
        bookHuntGuests BIGINT,
        bookNonHuntGuests BIGINT,
        bookPhId INT FOREIGN KEY REFERENCES ph(phId),
        bookVgId INT FOREIGN KEY REFERENCES vg(vgId),
        bookPaymentStatus NVARCHAR(MAX)
    );

    CREATE TABLE ratings (
        ratingId INT IDENTITY(1,1) PRIMARY KEY,
        bookingId INT NOT NULL FOREIGN KEY REFERENCES bookings(bookingId),
        raterUserId INT NOT NULL FOREIGN KEY REFERENCES users(userId),
        rateeUserId INT NOT NULL FOREIGN KEY REFERENCES users(userId),
        rateeRoleCode NVARCHAR(20) NOT NULL,
        stars INT NOT NULL CHECK (stars BETWEEN 1 AND 5),
        comment NVARCHAR(MAX) NULL,
        createdAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_ratings_booking_rater_ratee UNIQUE (bookingId, raterUserId, rateeUserId)
    );

    CREATE INDEX IX_ratings_rateeUserId ON ratings(rateeUserId);

    CREATE TABLE storyData (
        storyDataId INT IDENTITY(1,1) PRIMARY KEY,
        sdBookingId INT FOREIGN KEY REFERENCES bookings(bookingId),
        sdExpeditionNum BIGINT
    );

    CREATE TABLE storyDataPoint (
        dataPointId INT IDENTITY(1,1) PRIMARY KEY,
        sdpStoryDataId INT FOREIGN KEY REFERENCES storyData(storyDataId),
        sdpGpsPos NVARCHAR(MAX),
        sdpTimestamp DATETIME2,
        sdpIsCriticalPoint BIT,
        sdpMediaFolderPath NVARCHAR(MAX)
    );

    CREATE TABLE trophies (
        trophyId INT IDENTITY(1,1) PRIMARY KEY,
        tOwnerUserId INT NOT NULL FOREIGN KEY REFERENCES users(userId),
        tSpecies NVARCHAR(120) NOT NULL,
        tPhotoPath NVARCHAR(500) NOT NULL,
        tAntlerSize DECIMAL(10, 2) NULL,
        tCarcassWeight DECIMAL(10, 2) NULL,
        tFirearmType NVARCHAR(120) NOT NULL,
        tHarvestDate DATE NOT NULL,
        tLocationText NVARCHAR(200) NOT NULL,
        tNotes NVARCHAR(MAX) NULL,
        tIsPublic BIT NOT NULL DEFAULT 0,
        tShotDistanceMeters INT NULL,
        tRowlandWardClassId NVARCHAR(160) NULL,
        tRowlandWardSpeciesNameAf NVARCHAR(160) NULL,
        tCreatedTimestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        tUpdatedTimestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_trophies_tShotDistanceMeters_NonNegative
            CHECK (tShotDistanceMeters IS NULL OR tShotDistanceMeters >= 0)
    );

    CREATE TABLE trophyRowlandWardEvaluations (
        trophyRowlandWardEvaluationId BIGINT IDENTITY(1,1) PRIMARY KEY,
        trweTrophyId INT NOT NULL,
        trweIsCurrent BIT NOT NULL CONSTRAINT DF_trophyRowlandWardEvaluations_IsCurrent DEFAULT (1),
        trweSchemaVersion INT NOT NULL,
        trweClassId NVARCHAR(160) NOT NULL,
        trweDisplayNameAf NVARCHAR(160) NOT NULL,
        trweMethodCode NVARCHAR(20) NOT NULL,
        trweMeasurementProfileId NVARCHAR(80) NOT NULL,
        trweSourceVersionId NVARCHAR(120) NOT NULL,
        trweEditionReleaseIdentity NVARCHAR(160) NOT NULL,
        trweSourceMasterSha256 NVARCHAR(64) NULL,
        trweEvaluatedAtUtc DATETIME2(7) NOT NULL,
        trweScoreCanonicalNumerator BIGINT NULL,
        trweScoreCanonicalDenominator BIGINT NULL,
        trweScoreCanonicalUnitCode NVARCHAR(20) NULL,
        trweScoreDisplayNumerator BIGINT NULL,
        trweScoreDisplayDenominator BIGINT NULL,
        trweScoreDisplayUnitCode NVARCHAR(20) NULL,
        trweMinimumState NVARCHAR(40) NOT NULL,
        trweMinimumCanonicalNumerator BIGINT NULL,
        trweMinimumCanonicalDenominator BIGINT NULL,
        trweMinimumCanonicalUnitCode NVARCHAR(20) NULL,
        trweMinimumDisplayNumerator BIGINT NULL,
        trweMinimumDisplayDenominator BIGINT NULL,
        trweMinimumDisplayUnitCode NVARCHAR(20) NULL,
        trweWorldRecordState NVARCHAR(40) NOT NULL,
        trweWorldRecordCanonicalNumerator BIGINT NULL,
        trweWorldRecordCanonicalDenominator BIGINT NULL,
        trweWorldRecordCanonicalUnitCode NVARCHAR(20) NULL,
        trweWorldRecordDisplayNumerator BIGINT NULL,
        trweWorldRecordDisplayDenominator BIGINT NULL,
        trweWorldRecordDisplayUnitCode NVARCHAR(20) NULL,
        trweQualificationState NVARCHAR(60) NOT NULL,
        trweMinimumReached BIT NULL,
        trweScoreExpressionJson NVARCHAR(MAX) NOT NULL,
        trweMinimumExpressionJson NVARCHAR(MAX) NOT NULL,
        trweMinimumComparisonOperator NVARCHAR(40) NOT NULL,
        trweCreatedTimestamp DATETIME2(7) NOT NULL
            CONSTRAINT DF_trophyRowlandWardEvaluations_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_trophyRowlandWardEvaluations_Trophy
            FOREIGN KEY (trweTrophyId) REFERENCES trophies(trophyId) ON DELETE CASCADE,
        CONSTRAINT CK_trophyRowlandWardEvaluations_SchemaVersion CHECK (trweSchemaVersion > 0),
        CONSTRAINT CK_trophyRowlandWardEvaluations_Score CHECK (
            (trweScoreCanonicalNumerator IS NULL AND trweScoreCanonicalDenominator IS NULL
                AND trweScoreCanonicalUnitCode IS NULL AND trweScoreDisplayNumerator IS NULL
                AND trweScoreDisplayDenominator IS NULL AND trweScoreDisplayUnitCode IS NULL)
            OR
            (trweScoreCanonicalNumerator IS NOT NULL AND trweScoreCanonicalDenominator > 0
                AND trweScoreCanonicalUnitCode IS NOT NULL AND trweScoreDisplayNumerator IS NOT NULL
                AND trweScoreDisplayDenominator > 0 AND trweScoreDisplayUnitCode IS NOT NULL)
        ),
        CONSTRAINT CK_trophyRowlandWardEvaluations_Minimum CHECK (
            (trweMinimumCanonicalNumerator IS NULL AND trweMinimumCanonicalDenominator IS NULL
                AND trweMinimumCanonicalUnitCode IS NULL AND trweMinimumDisplayNumerator IS NULL
                AND trweMinimumDisplayDenominator IS NULL AND trweMinimumDisplayUnitCode IS NULL)
            OR
            (trweMinimumCanonicalNumerator IS NOT NULL AND trweMinimumCanonicalDenominator > 0
                AND trweMinimumCanonicalUnitCode IS NOT NULL AND trweMinimumDisplayNumerator IS NOT NULL
                AND trweMinimumDisplayDenominator > 0 AND trweMinimumDisplayUnitCode IS NOT NULL)
        ),
        CONSTRAINT CK_trophyRowlandWardEvaluations_WorldRecord CHECK (
            (trweWorldRecordCanonicalNumerator IS NULL AND trweWorldRecordCanonicalDenominator IS NULL
                AND trweWorldRecordCanonicalUnitCode IS NULL AND trweWorldRecordDisplayNumerator IS NULL
                AND trweWorldRecordDisplayDenominator IS NULL AND trweWorldRecordDisplayUnitCode IS NULL)
            OR
            (trweWorldRecordCanonicalNumerator IS NOT NULL AND trweWorldRecordCanonicalDenominator > 0
                AND trweWorldRecordCanonicalUnitCode IS NOT NULL AND trweWorldRecordDisplayNumerator IS NOT NULL
                AND trweWorldRecordDisplayDenominator > 0 AND trweWorldRecordDisplayUnitCode IS NOT NULL)
        )
    );

    CREATE UNIQUE INDEX UX_trophyRowlandWardEvaluations_Current
        ON trophyRowlandWardEvaluations(trweTrophyId)
        WHERE trweIsCurrent = 1;

    CREATE INDEX IX_trophyRowlandWardEvaluations_Trophy
        ON trophyRowlandWardEvaluations(trweTrophyId, trweEvaluatedAtUtc DESC);

    CREATE TABLE trophyRowlandWardMeasurements (
        trophyRowlandWardMeasurementId BIGINT IDENTITY(1,1) PRIMARY KEY,
        trwmEvaluationId BIGINT NOT NULL,
        trwmFieldId NVARCHAR(100) NOT NULL,
        trwmLabelAf NVARCHAR(160) NOT NULL,
        trwmKind NVARCHAR(20) NOT NULL,
        trwmRole NVARCHAR(60) NOT NULL,
        trwmInputNumerator BIGINT NULL,
        trwmInputDenominator BIGINT NULL,
        trwmInputUnitCode NVARCHAR(20) NULL,
        trwmCanonicalNumerator BIGINT NULL,
        trwmCanonicalDenominator BIGINT NULL,
        trwmCanonicalUnitCode NVARCHAR(20) NULL,
        trwmScoringNumerator BIGINT NULL,
        trwmScoringDenominator BIGINT NULL,
        trwmScoringUnitCode NVARCHAR(20) NULL,
        trwmIntegerValue BIGINT NULL,
        trwmBooleanValue BIT NULL,
        CONSTRAINT FK_trophyRowlandWardMeasurements_Evaluation
            FOREIGN KEY (trwmEvaluationId)
            REFERENCES trophyRowlandWardEvaluations(trophyRowlandWardEvaluationId)
            ON DELETE CASCADE,
        CONSTRAINT UQ_trophyRowlandWardMeasurements_EvaluationField
            UNIQUE (trwmEvaluationId, trwmFieldId),
        CONSTRAINT CK_trophyRowlandWardMeasurements_ValueShape CHECK (
            (trwmKind = N'rational' AND trwmInputNumerator IS NOT NULL
                AND trwmInputDenominator > 0 AND trwmInputUnitCode IS NOT NULL
                AND trwmCanonicalNumerator IS NOT NULL AND trwmCanonicalDenominator > 0
                AND trwmCanonicalUnitCode IS NOT NULL AND trwmScoringNumerator IS NOT NULL
                AND trwmScoringDenominator > 0 AND trwmScoringUnitCode IS NOT NULL
                AND trwmIntegerValue IS NULL AND trwmBooleanValue IS NULL)
            OR
            (trwmKind = N'integer' AND trwmInputNumerator IS NULL
                AND trwmInputDenominator IS NULL AND trwmInputUnitCode IS NULL
                AND trwmCanonicalNumerator IS NULL AND trwmCanonicalDenominator IS NULL
                AND trwmCanonicalUnitCode IS NULL AND trwmScoringNumerator IS NULL
                AND trwmScoringDenominator IS NULL AND trwmScoringUnitCode IS NULL
                AND trwmIntegerValue IS NOT NULL AND trwmBooleanValue IS NULL)
            OR
            (trwmKind = N'boolean' AND trwmInputNumerator IS NULL
                AND trwmInputDenominator IS NULL AND trwmInputUnitCode IS NULL
                AND trwmCanonicalNumerator IS NULL AND trwmCanonicalDenominator IS NULL
                AND trwmCanonicalUnitCode IS NULL AND trwmScoringNumerator IS NULL
                AND trwmScoringDenominator IS NULL AND trwmScoringUnitCode IS NULL
                AND trwmIntegerValue IS NULL AND trwmBooleanValue IS NOT NULL)
        )
    );

    CREATE TABLE vgAvailableProvinces (
        vgAvailableProvinceId INT IDENTITY(1,1) PRIMARY KEY,
        vgapVgId INT FOREIGN KEY REFERENCES vg(vgId),
        vgapProvId INT FOREIGN KEY REFERENCES provinces(provId)
    );

    CREATE TABLE vgPackages (
        vgPackageId INT IDENTITY(1,1) PRIMARY KEY,
        vgId INT FOREIGN KEY REFERENCES vg(vgId),
        vgpTitle NVARCHAR(MAX),
        vgpRegionCovered NVARCHAR(MAX),
        vgpEquipmentProvided NVARCHAR(MAX),
        vgpDescription NVARCHAR(MAX),
        vgpPrice DECIMAL(38, 2)
    );

    CREATE TABLE availableAnimals (
        aaId INT IDENTITY(1,1) PRIMARY KEY,
        aaFarmId INT FOREIGN KEY REFERENCES farm(farmId),
        aaName NVARCHAR(MAX),
        aaQuota BIGINT,
        aaEntityPrice DECIMAL(38, 2)
    );

    CREATE TABLE phPricing (
        phPricingId INT IDENTITY(1,1) PRIMARY KEY,
        phpPhId INT FOREIGN KEY REFERENCES ph(phId),
        phpGameType NVARCHAR(MAX),
        phpHunterCount BIGINT,
        phpPrice DECIMAL(38, 2)
    );

    CREATE TABLE team (
        teamId INT IDENTITY(1,1) PRIMARY KEY,
        teamPjId INT FOREIGN KEY REFERENCES userRoles(userRoleId),
        teamMemberId INT FOREIGN KEY REFERENCES userRoles(userRoleId),
        teamRow3Placeholder NVARCHAR(MAX)
    );

    CREATE TABLE AuthTokens (
        atId INT IDENTITY(1,1) PRIMARY KEY,
        atToken VARCHAR(255) NOT NULL,
        atCreated DATETIME2 NOT NULL DEFAULT GETDATE(),
        atExpired DATETIME2 NOT NULL,
        atUserId INT NOT NULL,
        atIP VARCHAR(255),
        CONSTRAINT UQ_AuthTokens_Token UNIQUE (atToken),
        CONSTRAINT FK_AuthTokens_User FOREIGN KEY (atUserId) REFERENCES users(userId)
    );

    CREATE TABLE teamInvites (
        inviteId INT IDENTITY(1,1) PRIMARY KEY,
        invitePjId INT NOT NULL FOREIGN KEY REFERENCES userRoles(userRoleId),
        inviteMemberId INT NOT NULL FOREIGN KEY REFERENCES userRoles(userRoleId),
        inviteStatus NVARCHAR(20) NOT NULL DEFAULT 'Pending',
        inviteDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT UQ_TeamInvite UNIQUE (invitePjId, inviteMemberId)
    );

    -- ============================================================
    -- 2. BASE DATA (addresses, document types, roles, provinces)
    -- ============================================================

    IF NOT EXISTS (SELECT 1 FROM addresses)
    BEGIN
        INSERT INTO addresses (addressNumber, addressStreetName, addressCity, addressRegion, addressPostalCode, addressCountry,
  addressLatitude, addressLongitude)
        VALUES
        ('12', 'Hoofstraat', 'Pretoria', 'Gauteng', '0001', 'Suid-Afrika', -25.746111, 28.188056),
        ('45', 'Kerkstraat', 'Kaapstad', 'Wes-Kaap', '8001', 'Suid-Afrika', -33.924868, 18.424055),
        ('78', 'Langstraat', 'Bloemfontein', 'Vrystaat', '9301', 'Suid-Afrika', -29.121256, 26.214531),
        ('Plot 23', 'R512', 'Brits', 'Noordwes', '0250', 'Suid-Afrika', -25.634449, 27.780159),
        ('10', 'Wildtuinweg', 'Nelspruit', 'Mpumalanga', '1200', 'Suid-Afrika', -25.474644, 30.970850),
        ('5', 'Safaristraat', 'Polokwane', 'Limpopo', '0699', 'Suid-Afrika', -23.903786, 29.444786),
        ('22', 'Jagterslaan', 'Kimberley', 'Noord-Kaap', '8300', 'Suid-Afrika', -28.728238, 24.749363),
        ('7', 'Bosveldweg', 'Rustenburg', 'Noordwes', '0299', 'Suid-Afrika', -25.667574, 27.242674),
        ('15', 'Waterbergpad', 'Modimolle', 'Limpopo', '0510', 'Suid-Afrika', -24.695879, 28.406912),
        ('33', 'Olifantsrivierstraat', 'Hoedspruit', 'Limpopo', '1380', 'Suid-Afrika', -24.357985, 30.950358);
    END

    IF NOT EXISTS (SELECT 1 FROM documentType)
    BEGIN
        INSERT INTO documentType (dtName, dtNumber)
        VALUES
        ('ID Dokument', 'RSA-ID-DL'),
        ('Paspoort', 'PASSPORT'),
        ('Drewwerslisensie', 'DRIVER-LIC'),
        ('PH Lisensie', 'PH-LIC'),
        ('Vuurwapenlisensie', 'FIREARM-LIC'),
        ('Besigheidsregistrasie', 'BUS-REG'),
        ('Drone Lisensie', 'DRONE-LIC'),
        ('Eerstehulp Sertifikaat', 'FIRST-AID');
    END

    IF NOT EXISTS (SELECT 1 FROM roles)
    BEGIN
        INSERT INTO roles (roleCode, roleName)
        VALUES
        ('HUNTER', 'Jagter'),
        ('PH', 'Professionele Jagter'),
        ('PJ', 'Toerusting Verskaffer'),
        ('FARMER', 'Boer/Grondeienaar'),
        ('VG', 'Videograaf'),
        ('AP', 'Advertensie Plaser'),
        ('ADMIN', 'Administrateur');
    END

    IF NOT EXISTS (SELECT 1 FROM provinces)
    BEGIN
        INSERT INTO provinces (provName)
        VALUES
        ('Gauteng'), ('Wes-Kaap'), ('Oos-Kaap'), ('KwaZulu-Natal'),
        ('Vrystaat'), ('Noordwes'), ('Mpumalanga'), ('Limpopo'), ('Noord-Kaap');
    END

    -- ============================================================
    -- 3. USERS (from ToetsData.sql) – 10 users
    -- ============================================================
    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'johan@mikpunt.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uRegistrationDate, uPhone, uIsEmailVerified, uIsPhoneVerified)
        VALUES
        ('Johan', 'van der Merwe', 'johan@mikpunt.com', 'hash123', GETDATE(), '0821234567', 1, 1),
        ('Pieter', 'Swanepoel', 'pieter@mikpunt.com', 'hash456', GETDATE(), '0832345678', 1, 0),
        ('Hannes', 'Visser', 'hannes@mikpunt.com', 'hash789', GETDATE(), '0843456789', 0, 0),
        ('Maria', 'Joubert', 'maria@mikpunt.com', 'hashabc', GETDATE(), '0824567890', 1, 1),
        ('Koos', 'Pretorius', 'koos@mikpunt.com', 'hashdef', GETDATE(), '0835678901', 1, 0),
        ('Annemarie', 'Coetzee', 'annemarie@mikpunt.com', 'hashghi', GETDATE(), '0846789012', 1, 1),
        ('Sarel', 'Botes', 'sarel@mikpunt.com', 'hashjkl', GETDATE(), '0827890123', 0, 0),
        ('Elsa', 'Kruger', 'elsa@mikpunt.com', 'hashmno', GETDATE(), '0838901234', 1, 1),
        ('Thabo', 'Nkosi', 'thabo@mikpunt.com', 'hashpqr', GETDATE(), '0849012345', 1, 0),
        ('Peter', 'Smith', 'peter@mikpunt.com', 'hashstu', GETDATE(), '0820123456', 1, 1);
    END

    -- ============================================================
    -- 4. USER ROLES (from ToetsData)
    -- ============================================================
    DECLARE @HUNTER_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'HUNTER');
    DECLARE @PH_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'PH');
    DECLARE @PJ_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'PJ');
    DECLARE @FARMER_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'FARMER');
    DECLARE @VG_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'VG');
    DECLARE @AP_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'AP');
    DECLARE @ADMIN_ROLE_ID INT = (SELECT roleId FROM roles WHERE roleCode = 'ADMIN');

    INSERT INTO userRoles (urUserId, urRoleId)
    SELECT u.userId, r.roleId
    FROM (VALUES
        ('johan@mikpunt.com', 'HUNTER'), ('johan@mikpunt.com', 'PH'),
        ('pieter@mikpunt.com', 'PH'), ('pieter@mikpunt.com', 'HUNTER'),
        ('hannes@mikpunt.com', 'PJ'), ('hannes@mikpunt.com', 'VG'),
        ('maria@mikpunt.com', 'FARMER'), ('maria@mikpunt.com', 'HUNTER'),
        ('koos@mikpunt.com', 'VG'),
        ('annemarie@mikpunt.com', 'AP'),
        ('sarel@mikpunt.com', 'ADMIN'),
        ('elsa@mikpunt.com', 'HUNTER'),
        ('thabo@mikpunt.com', 'FARMER'),
        ('peter@mikpunt.com', 'PJ')
    ) AS v(email, roleCode)
    JOIN users u ON u.uEmail = v.email
    JOIN roles r ON r.roleCode = v.roleCode
    WHERE NOT EXISTS (
        SELECT 1 FROM userRoles ur WHERE ur.urUserId = u.userId AND ur.urRoleId = r.roleId
    );

    DECLARE @Johan_HUNTER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'johan@mikpunt.com') AND urRoleId = @HUNTER_ROLE_ID);
    DECLARE @Johan_PH_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'johan@mikpunt.com') AND urRoleId = @PH_ROLE_ID);
    DECLARE @Pieter_PH_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'pieter@mikpunt.com') AND urRoleId = @PH_ROLE_ID);
    DECLARE @Pieter_HUNTER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'pieter@mikpunt.com') AND urRoleId = @HUNTER_ROLE_ID);
    DECLARE @Hannes_PJ_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'hannes@mikpunt.com') AND urRoleId = @PJ_ROLE_ID);
    DECLARE @Hannes_VG_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'hannes@mikpunt.com') AND urRoleId = @VG_ROLE_ID);
    DECLARE @Maria_FARMER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'maria@mikpunt.com') AND urRoleId = @FARMER_ROLE_ID);
    DECLARE @Maria_HUNTER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'maria@mikpunt.com') AND urRoleId = @HUNTER_ROLE_ID);
    DECLARE @Koos_VG_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'koos@mikpunt.com') AND urRoleId = @VG_ROLE_ID);
    DECLARE @Annemarie_AP_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'annemarie@mikpunt.com') AND urRoleId = @AP_ROLE_ID);
    DECLARE @Sarel_ADMIN_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'sarel@mikpunt.com') AND urRoleId = @ADMIN_ROLE_ID);
    DECLARE @Elsa_HUNTER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'elsa@mikpunt.com') AND urRoleId = @HUNTER_ROLE_ID);
    DECLARE @Thabo_FARMER_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'thabo@mikpunt.com') AND urRoleId = @FARMER_ROLE_ID);
    DECLARE @Peter_PJ_URID INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'peter@mikpunt.com') AND urRoleId = @PJ_ROLE_ID);

    -- ============================================================
    -- 5. PROFILE TABLES (admin, ap, farmer, pj, ph, vg, hunter)
    -- ============================================================
    IF NOT EXISTS (SELECT 1 FROM admin WHERE adminUserRoleId = @Sarel_ADMIN_URID)
        INSERT INTO admin (adminUserRoleId) VALUES (@Sarel_ADMIN_URID);

    IF NOT EXISTS (SELECT 1 FROM ap WHERE apUserRoleId = @Annemarie_AP_URID)
        INSERT INTO ap (apUserRoleId) VALUES (@Annemarie_AP_URID);

    IF NOT EXISTS (SELECT 1 FROM farmer WHERE fUserRoleId = @Maria_FARMER_URID)
        INSERT INTO farmer (fUserRoleId, fBusinessName, fBusinessRegistrationNumber, fPubliclyVisibleTill, fBio)
        VALUES (@Maria_FARMER_URID, 'Kudu Bosveld Plase', 'REG12345', '2026-12-31', 'Gesinsplaas met meer as 20 jaar ondervinding');

    IF NOT EXISTS (SELECT 1 FROM farmer WHERE fUserRoleId = @Thabo_FARMER_URID)
        INSERT INTO farmer (fUserRoleId, fBusinessName, fBusinessRegistrationNumber, fPubliclyVisibleTill, fBio)
        VALUES (@Thabo_FARMER_URID, 'Limpopo Wilds Plase', 'REG67890', '2026-10-31', 'Spesialiseer in buffel en kudu jagte');

    DECLARE @KuduBosveldFarmerId INT = (SELECT farmerId FROM farmer WHERE fUserRoleId = @Maria_FARMER_URID);
    DECLARE @LimpopoWildsFarmerId INT = (SELECT farmerId FROM farmer WHERE fUserRoleId = @Thabo_FARMER_URID);

    IF NOT EXISTS (SELECT 1 FROM pj WHERE pjUserRoleId = @Hannes_PJ_URID)
        INSERT INTO pj (pjUserRoleId, pjBusinessName, pjBusinessRegistrationNumber, pjPubliclyVisibleTill, pjYearsOfExperience, pjBio)
        VALUES (@Hannes_PJ_URID, 'Afrika Jag Uitrusters', 'OUT12345', '2026-12-31', 15, 'Ten volle gelisensieerde jaguitrusters');

    IF NOT EXISTS (SELECT 1 FROM pj WHERE pjUserRoleId = @Peter_PJ_URID)
        INSERT INTO pj (pjUserRoleId, pjBusinessName, pjBusinessRegistrationNumber, pjPubliclyVisibleTill, pjYearsOfExperience, pjBio)
        VALUES (@Peter_PJ_URID, 'Bosveld Jag Aanbiedings', 'OUT67890', '2026-11-30', 8, 'Spesialiseer in trofee jagte');

    DECLARE @AfrikaPjId INT = (SELECT pjId FROM pj WHERE pjUserRoleId = @Hannes_PJ_URID);
    DECLARE @BosveldPjId INT = (SELECT pjId FROM pj WHERE pjUserRoleId = @Peter_PJ_URID);

    IF NOT EXISTS (SELECT 1 FROM ph WHERE phUserRoleId = @Pieter_PH_URID)
        INSERT INTO ph (phUserRoleId, phPubliclyVisibleTill, phYearsOfExperience, phBio)
        VALUES (@Pieter_PH_URID, '2026-12-31', 20, 'Ervare PH met meer as 500 suksesvolle jagte');

    IF NOT EXISTS (SELECT 1 FROM ph WHERE phUserRoleId = @Johan_PH_URID)
        INSERT INTO ph (phUserRoleId, phPubliclyVisibleTill, phYearsOfExperience, phBio)
        VALUES (@Johan_PH_URID, '2026-09-30', 12, 'Spesialiseer in buitelandse jagters');

    DECLARE @PieterPhId INT = (SELECT phId FROM ph WHERE phUserRoleId = @Pieter_PH_URID);
    DECLARE @JohanPhId INT = (SELECT phId FROM ph WHERE phUserRoleId = @Johan_PH_URID);

    IF NOT EXISTS (SELECT 1 FROM vg WHERE vgUserRoleId = @Koos_VG_URID)
        INSERT INTO vg (vgUserRoleId, vgHasDrone, vgDroneLicence, vgPubliclyVisibleTill, vgCompanyName, vgCompanyRegistrationNumber,
  vgYearsOfExperience)
        VALUES (@Koos_VG_URID, 1, 'DRN45678', '2026-12-31', 'WildsVideo Produksies', 'VID12345', 10);

    IF NOT EXISTS (SELECT 1 FROM vg WHERE vgUserRoleId = @Hannes_VG_URID)
        INSERT INTO vg (vgUserRoleId, vgHasDrone, vgDroneLicence, vgPubliclyVisibleTill, vgCompanyName, vgCompanyRegistrationNumber,
  vgYearsOfExperience)
        VALUES (@Hannes_VG_URID, 0, NULL, '2026-08-31', NULL, NULL, 5);

    DECLARE @KoosVgId INT = (SELECT vgId FROM vg WHERE vgUserRoleId = @Koos_VG_URID);
    DECLARE @HannesVgId INT = (SELECT vgId FROM vg WHERE vgUserRoleId = @Hannes_VG_URID);

    IF NOT EXISTS (SELECT 1 FROM hunter WHERE hUserRoleId = @Johan_HUNTER_URID)
        INSERT INTO hunter (hUserRoleId, hCountry, hUsername, hBio)
        VALUES (@Johan_HUNTER_URID, 'Suid-Afrika', 'johanjagter', 'Passievolle jagter met 10 jaar ervaring');

    IF NOT EXISTS (SELECT 1 FROM hunter WHERE hUserRoleId = @Pieter_HUNTER_URID)
        INSERT INTO hunter (hUserRoleId, hCountry, hUsername, hBio)
        VALUES (@Pieter_HUNTER_URID, 'Nederland', 'pieterhunter', 'Internasionale jagter');

    IF NOT EXISTS (SELECT 1 FROM hunter WHERE hUserRoleId = @Maria_HUNTER_URID)
        INSERT INTO hunter (hUserRoleId, hCountry, hUsername, hBio)
        VALUES (@Maria_HUNTER_URID, 'Suid-Afrika', 'mariajag', 'Jagter en boer');

    IF NOT EXISTS (SELECT 1 FROM hunter WHERE hUserRoleId = @Elsa_HUNTER_URID)
        INSERT INTO hunter (hUserRoleId, hCountry, hUsername, hBio)
        VALUES (@Elsa_HUNTER_URID, 'Duitsland', 'elsa_wild', 'Europese jagter wat Afrika verken');

    DECLARE @JohanHunterId INT = (SELECT hunterId FROM hunter WHERE hUserRoleId = @Johan_HUNTER_URID);
    DECLARE @PieterHunterId INT = (SELECT hunterId FROM hunter WHERE hUserRoleId = @Pieter_HUNTER_URID);
    DECLARE @ElsaHunterId INT = (SELECT hunterId FROM hunter WHERE hUserRoleId = @Elsa_HUNTER_URID);

    -- ============================================================
    -- 6. FARMS & LODGES, AVAILABLE ANIMALS
    -- ============================================================
    IF NOT EXISTS (SELECT 1 FROM farm WHERE farmName = 'Kudu Bosveld')
        INSERT INTO farm (farmFarmerId, farmDescription, farmMediaFolder, farmName, farmSize, farmAddressId, farmTerrainType,
  farmFencingType, farmStatus)
        VALUES (@KuduBosveldFarmerId, 'Uitgestrekte bosveld plase met diverse wild', '/media/farm1/', 'Kudu Bosveld', 2500.50, 3,
  'Bosveld', 'Wilddig', 'Active');

    IF NOT EXISTS (SELECT 1 FROM farm WHERE farmName = 'Limpopo Wilds')
        INSERT INTO farm (farmFarmerId, farmDescription, farmMediaFolder, farmName, farmSize, farmAddressId, farmTerrainType,
  farmFencingType, farmStatus)
        VALUES (@LimpopoWildsFarmerId, 'Waterberg bergwild plase', '/media/farm2/', 'Limpopo Wilds', 1800.00, 9, 'Bergveld', 'Wilddig',
  'Active');

    DECLARE @KuduFarmId INT = (SELECT farmId FROM farm WHERE farmName = 'Kudu Bosveld');
    DECLARE @LimpopoFarmId INT = (SELECT farmId FROM farm WHERE farmName = 'Limpopo Wilds');

    IF NOT EXISTS (SELECT 1 FROM farmLodge WHERE flFarmId = @KuduFarmId AND flPrice = 2500)
        INSERT INTO farmLodge (flFarmId, flPrice, flRooms, flCateringIncluded, flFacilities)
        VALUES (@KuduFarmId, 2500.00, 8, 1, 'Swembad, braai area, lugversorging');

    IF NOT EXISTS (SELECT 1 FROM farmLodge WHERE flFarmId = @KuduFarmId AND flPrice = 1500)
        INSERT INTO farmLodge (flFarmId, flPrice, flRooms, flCateringIncluded, flFacilities)
        VALUES (@KuduFarmId, 1500.00, 4, 0, 'Basiese chalet');

    IF NOT EXISTS (SELECT 1 FROM farmLodge WHERE flFarmId = @LimpopoFarmId AND flPrice = 3500)
        INSERT INTO farmLodge (flFarmId, flPrice, flRooms, flCateringIncluded, flFacilities)
        VALUES (@LimpopoFarmId, 3500.00, 6, 1, 'Luukse verblyf, spa, restaurant');

    IF NOT EXISTS (SELECT 1 FROM availableAnimals WHERE aaFarmId = @KuduFarmId AND aaName = 'Kudu')
        INSERT INTO availableAnimals (aaFarmId, aaName, aaQuota, aaEntityPrice) VALUES (@KuduFarmId, 'Kudu', 20, 8500.00);

    IF NOT EXISTS (SELECT 1 FROM availableAnimals WHERE aaFarmId = @KuduFarmId AND aaName = 'Springbok')
        INSERT INTO availableAnimals (aaFarmId, aaName, aaQuota, aaEntityPrice) VALUES (@KuduFarmId, 'Springbok', 50, 2500.00);

    IF NOT EXISTS (SELECT 1 FROM availableAnimals WHERE aaFarmId = @KuduFarmId AND aaName = 'Wildebees')
        INSERT INTO availableAnimals (aaFarmId, aaName, aaQuota, aaEntityPrice) VALUES (@KuduFarmId, 'Wildebees', 15, 7500.00);

    IF NOT EXISTS (SELECT 1 FROM availableAnimals WHERE aaFarmId = @LimpopoFarmId AND aaName = 'Buffel')
        INSERT INTO availableAnimals (aaFarmId, aaName, aaQuota, aaEntityPrice) VALUES (@LimpopoFarmId, 'Buffel', 5, 45000.00);

    IF NOT EXISTS (SELECT 1 FROM availableAnimals WHERE aaFarmId = @LimpopoFarmId AND aaName = 'Olifant')
        INSERT INTO availableAnimals (aaFarmId, aaName, aaQuota, aaEntityPrice) VALUES (@LimpopoFarmId, 'Olifant', 1, 150000.00);

    IF NOT EXISTS (SELECT 1 FROM availableAnimals WHERE aaFarmId = @LimpopoFarmId AND aaName = 'Leeu')
        INSERT INTO availableAnimals (aaFarmId, aaName, aaQuota, aaEntityPrice) VALUES (@LimpopoFarmId, 'Leeu', 2, 85000.00);

    -- ============================================================
    -- 7. PACKAGES (from ToetsData – 3 packages)
    -- ============================================================
    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Kudu Bosveld Pakket')
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pDescription, pTitle, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pStatus)
        VALUES (GETDATE(), '2026-12-31', @KuduFarmId, @AfrikaPjId, 25000.00, '5-dag jagpakket insluitend kudu en springbok', 'Kudu
  Bosveld Pakket', GETDATE(), 5, 1, 4, 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Buffel Trofee Pakket')
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pDescription, pTitle, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pStatus)
        VALUES (GETDATE(), '2026-10-31', @LimpopoFarmId, @BosveldPjId, 125000.00, '7-dag buffel jagpakket', 'Buffel Trofee Pakket',
  GETDATE(), 7, 1, 2, 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Naweek Jag Pakket')
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pDescription, pTitle, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pStatus)
        VALUES (GETDATE(), '2026-11-30', @KuduFarmId, @AfrikaPjId, 15000.00, '3-dag wilds jagpakket', 'Naweek Jag Pakket', GETDATE(), 3,
  2, 6, 'Active');

    DECLARE @KuduPakketId INT = (SELECT packageId FROM packages WHERE pTitle = 'Kudu Bosveld Pakket');
    DECLARE @BuffelPakketId INT = (SELECT packageId FROM packages WHERE pTitle = 'Buffel Trofee Pakket');
    DECLARE @NaweekPakketId INT = (SELECT packageId FROM packages WHERE pTitle = 'Naweek Jag Pakket');

    -- ============================================================
    -- 8. PH PRICING, VG PACKAGES, VG AVAILABLE PROVINCES
    -- ============================================================
    IF NOT EXISTS (SELECT 1 FROM phPricing WHERE phpPhId = @PieterPhId AND phpGameType = 'Kudu')
        INSERT INTO phPricing (phpPhId, phpGameType, phpHunterCount, phpPrice) VALUES (@PieterPhId, 'Kudu', 1, 3500.00);

    IF NOT EXISTS (SELECT 1 FROM phPricing WHERE phpPhId = @PieterPhId AND phpGameType = 'Springbok')
        INSERT INTO phPricing (phpPhId, phpGameType, phpHunterCount, phpPrice) VALUES (@PieterPhId, 'Springbok', 2, 1500.00);

    IF NOT EXISTS (SELECT 1 FROM phPricing WHERE phpPhId = @JohanPhId AND phpGameType = 'Buffel')
        INSERT INTO phPricing (phpPhId, phpGameType, phpHunterCount, phpPrice) VALUES (@JohanPhId, 'Buffel', 1, 5000.00);

    IF NOT EXISTS (SELECT 1 FROM phPricing WHERE phpPhId = @JohanPhId AND phpGameType = 'Rooibok')
        INSERT INTO phPricing (phpPhId, phpGameType, phpHunterCount, phpPrice) VALUES (@JohanPhId, 'Rooibok', 1, 2000.00);

    IF NOT EXISTS (SELECT 1 FROM vgPackages WHERE vgId = @KoosVgId AND vgpDescription = 'Basiese video opname vir 3 dae')
        INSERT INTO vgPackages (vgId, vgpDescription, vgpPrice) VALUES (@KoosVgId, 'Basiese video opname vir 3 dae', 5000.00);

    IF NOT EXISTS (SELECT 1 FROM vgPackages WHERE vgId = @KoosVgId AND vgpDescription = 'Premium video met drone materiaal')
        INSERT INTO vgPackages (vgId, vgpDescription, vgpPrice) VALUES (@KoosVgId, 'Premium video met drone materiaal', 12000.00);

    IF NOT EXISTS (SELECT 1 FROM vgPackages WHERE vgId = @KoosVgId AND vgpDescription = 'Volle produksie pakket')
        INSERT INTO vgPackages (vgId, vgpDescription, vgpPrice) VALUES (@KoosVgId, 'Volle produksie pakket', 25000.00);

    DECLARE @GautengProvId INT = (SELECT provId FROM provinces WHERE provName = 'Gauteng');
    DECLARE @WesKaapProvId INT = (SELECT provId FROM provinces WHERE provName = 'Wes-Kaap');
    DECLARE @VrystaatProvId INT = (SELECT provId FROM provinces WHERE provName = 'Vrystaat');
    DECLARE @NoordwesProvId INT = (SELECT provId FROM provinces WHERE provName = 'Noordwes');

    IF NOT EXISTS (SELECT 1 FROM vgAvailableProvinces WHERE vgapVgId = @KoosVgId AND vgapProvId = @GautengProvId)
        INSERT INTO vgAvailableProvinces (vgapVgId, vgapProvId) VALUES (@KoosVgId, @GautengProvId);

    IF NOT EXISTS (SELECT 1 FROM vgAvailableProvinces WHERE vgapVgId = @KoosVgId AND vgapProvId = @WesKaapProvId)
        INSERT INTO vgAvailableProvinces (vgapVgId, vgapProvId) VALUES (@KoosVgId, @WesKaapProvId);

    IF NOT EXISTS (SELECT 1 FROM vgAvailableProvinces WHERE vgapVgId = @KoosVgId AND vgapProvId = @VrystaatProvId)
        INSERT INTO vgAvailableProvinces (vgapVgId, vgapProvId) VALUES (@KoosVgId, @VrystaatProvId);

    IF NOT EXISTS (SELECT 1 FROM vgAvailableProvinces WHERE vgapVgId = @HannesVgId AND vgapProvId = @GautengProvId)
        INSERT INTO vgAvailableProvinces (vgapVgId, vgapProvId) VALUES (@HannesVgId, @GautengProvId);

    IF NOT EXISTS (SELECT 1 FROM vgAvailableProvinces WHERE vgapVgId = @HannesVgId AND vgapProvId = @NoordwesProvId)
        INSERT INTO vgAvailableProvinces (vgapVgId, vgapProvId) VALUES (@HannesVgId, @NoordwesProvId);

    -- ============================================================
    -- 9. ADVERTISEMENTS, BOOKINGS, STORY DATA, CHAT (from ToetsData)
    -- ============================================================

    IF NOT EXISTS (SELECT 1 FROM bookings WHERE bookHunterId = @JohanHunterId AND bookPackageId = @KuduPakketId)
        INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests)
        VALUES (@JohanHunterId, @KuduPakketId, '2026-06-01', '2026-06-05', 'Confirmed', 2, 1);

    IF NOT EXISTS (SELECT 1 FROM bookings WHERE bookHunterId = @ElsaHunterId AND bookPackageId = @BuffelPakketId)
        INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests)
        VALUES (@ElsaHunterId, @BuffelPakketId, '2026-07-10', '2026-07-16', 'Pending', 1, 0);

    IF NOT EXISTS (SELECT 1 FROM bookings WHERE bookHunterId = @PieterHunterId AND bookPackageId = @NaweekPakketId)
        INSERT INTO bookings (bookHunterId, bookPackageId, bookStartDate, bookEndDate, bookStatus, bookHuntGuests, bookNonHuntGuests)
        VALUES (@PieterHunterId, @NaweekPakketId, '2026-08-15', '2026-08-17', 'Completed', 3, 2);

    DECLARE @Booking1Id INT = (SELECT bookingId FROM bookings WHERE bookHunterId = @JohanHunterId AND bookPackageId = @KuduPakketId);
    DECLARE @Booking3Id INT = (SELECT bookingId FROM bookings WHERE bookHunterId = @PieterHunterId AND bookPackageId = @NaweekPakketId);

    IF NOT EXISTS (SELECT 1 FROM storyData WHERE sdBookingId = @Booking1Id AND sdExpeditionNum = 1)
        INSERT INTO storyData (sdBookingId, sdExpeditionNum) VALUES (@Booking1Id, 1);

    IF NOT EXISTS (SELECT 1 FROM storyData WHERE sdBookingId = @Booking3Id AND sdExpeditionNum = 1)
        INSERT INTO storyData (sdBookingId, sdExpeditionNum) VALUES (@Booking3Id, 1);

    IF NOT EXISTS (SELECT 1 FROM storyData WHERE sdBookingId = @Booking3Id AND sdExpeditionNum = 2)
        INSERT INTO storyData (sdBookingId, sdExpeditionNum) VALUES (@Booking3Id, 2);

    DECLARE @StoryData1Id INT = (SELECT storyDataId FROM storyData WHERE sdBookingId = @Booking1Id AND sdExpeditionNum = 1);
    DECLARE @StoryData2Id INT = (SELECT storyDataId FROM storyData WHERE sdBookingId = @Booking3Id AND sdExpeditionNum = 1);

    IF NOT EXISTS (SELECT 1 FROM storyDataPoint WHERE sdpStoryDataId = @StoryData1Id AND sdpGpsPos = '-25.746111, 28.188056')
        INSERT INTO storyDataPoint (sdpStoryDataId, sdpGpsPos, sdpTimestamp, sdpIsCriticalPoint, sdpMediaFolderPath)
        VALUES (@StoryData1Id, '-25.746111, 28.188056', '2026-06-01 08:00:00', 0, '/media/story1/');

    IF NOT EXISTS (SELECT 1 FROM storyDataPoint WHERE sdpStoryDataId = @StoryData1Id AND sdpGpsPos = '-25.700000, 28.200000')
        INSERT INTO storyDataPoint (sdpStoryDataId, sdpGpsPos, sdpTimestamp, sdpIsCriticalPoint, sdpMediaFolderPath)
        VALUES (@StoryData1Id, '-25.700000, 28.200000', '2026-06-01 14:30:00', 1, '/media/story1/');

    IF NOT EXISTS (SELECT 1 FROM storyDataPoint WHERE sdpStoryDataId = @StoryData2Id AND sdpGpsPos = '-24.695879, 28.406912')
        INSERT INTO storyDataPoint (sdpStoryDataId, sdpGpsPos, sdpTimestamp, sdpIsCriticalPoint, sdpMediaFolderPath)
        VALUES (@StoryData2Id, '-24.695879, 28.406912', '2026-08-15 06:00:00', 0, '/media/story2/');

    -- Chats from ToetsData
    IF NOT EXISTS (SELECT 1 FROM chat WHERE cTitle = 'Jagbespreking - Kudu Jag')
        INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (GETDATE(), 'Jagbespreking - Kudu Jag');

    IF NOT EXISTS (SELECT 1 FROM chat WHERE cTitle = 'Navraag oor Buffel Pakket')
        INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (GETDATE(), 'Navraag oor Buffel Pakket');

    DECLARE @Chat1Id INT = (SELECT chatId FROM chat WHERE cTitle = 'Jagbespreking - Kudu Jag');
    DECLARE @Chat2Id INT = (SELECT chatId FROM chat WHERE cTitle = 'Navraag oor Buffel Pakket');

    IF NOT EXISTS (SELECT 1 FROM chatMessages WHERE cmChatId = @Chat1Id AND cmSenderId = 1 AND cmContent LIKE '%kudu%')
        INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp) VALUES (@Chat1Id, 1, 'Hallo, wanneer is die beste
  tyd vir kudu?', GETDATE());

    IF NOT EXISTS (SELECT 1 FROM chatMessages WHERE cmChatId = @Chat1Id AND cmSenderId = 2 AND cmContent LIKE '%Junie%')
        INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp) VALUES (@Chat1Id, 2, 'Junie is perfek, die weer is
  goed.', GETDATE());

    IF NOT EXISTS (SELECT 1 FROM chatMessages WHERE cmChatId = @Chat2Id AND cmSenderId = 8 AND cmContent LIKE '%buffel%')
        INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp) VALUES (@Chat2Id, 8, 'Is die buffel pakket nog
  beskikbaar?', GETDATE());

    IF NOT EXISTS (SELECT 1 FROM chatMessages WHERE cmChatId = @Chat2Id AND cmSenderId = 10 AND cmContent LIKE '%besonderhede%')
        INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp) VALUES (@Chat2Id, 10, 'Ja, kontak my gerus vir meer
  besonderhede.', GETDATE());

    DECLARE @ChatMsg1Id INT = (SELECT chatMessageId FROM chatMessages WHERE cmChatId = @Chat1Id AND cmSenderId = 1 AND cmContent LIKE
  '%kudu%');
    DECLARE @ChatMsg2Id INT = (SELECT chatMessageId FROM chatMessages WHERE cmChatId = @Chat1Id AND cmSenderId = 2 AND cmContent LIKE
  '%Junie%');
    DECLARE @ChatMsg3Id INT = (SELECT chatMessageId FROM chatMessages WHERE cmChatId = @Chat2Id AND cmSenderId = 8 AND cmContent LIKE
  '%buffel%');
    DECLARE @ChatMsg4Id INT = (SELECT chatMessageId FROM chatMessages WHERE cmChatId = @Chat2Id AND cmSenderId = 10 AND cmContent LIKE
  '%besonderhede%');

    IF NOT EXISTS (SELECT 1 FROM chatParticipants WHERE cpChatId = @Chat1Id AND cpUserId = 1)
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp, cpLeftAtTimestamp, cpLastReadMessage)
        VALUES (@Chat1Id, 1, GETDATE(), NULL, @ChatMsg1Id);

    IF NOT EXISTS (SELECT 1 FROM chatParticipants WHERE cpChatId = @Chat1Id AND cpUserId = 2)
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp, cpLeftAtTimestamp, cpLastReadMessage)
        VALUES (@Chat1Id, 2, GETDATE(), NULL, @ChatMsg2Id);

    IF NOT EXISTS (SELECT 1 FROM chatParticipants WHERE cpChatId = @Chat2Id AND cpUserId = 8)
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp, cpLeftAtTimestamp, cpLastReadMessage)
        VALUES (@Chat2Id, 8, GETDATE(), NULL, @ChatMsg3Id);

    IF NOT EXISTS (SELECT 1 FROM chatParticipants WHERE cpChatId = @Chat2Id AND cpUserId = 10)
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp, cpLeftAtTimestamp, cpLastReadMessage)
        VALUES (@Chat2Id, 10, GETDATE(), NULL, @ChatMsg4Id);

    -- Ratings, ContactUs, UserDocumentation, Team
    -- Only @Booking3Id (Pieter's Naweek Pakket) has bookStatus = 'Completed' - ratings
    -- are tied to a specific completed trip, so that's the only booking seeded here.
    IF NOT EXISTS (SELECT 1 FROM ratings WHERE bookingId = @Booking3Id AND raterUserId = 2 AND rateeUserId = 1)
        INSERT INTO ratings (bookingId, raterUserId, rateeUserId, rateeRoleCode, stars, comment)
        VALUES (@Booking3Id, 2, 1, 'PH', 5, 'Uitstekende PH! Baie professioneel.');

    IF NOT EXISTS (SELECT 1 FROM ratings WHERE bookingId = @Booking3Id AND raterUserId = 1 AND rateeUserId = 2)
        INSERT INTO ratings (bookingId, raterUserId, rateeUserId, rateeRoleCode, stars, comment)
        VALUES (@Booking3Id, 1, 2, 'HUNTER', 4, 'Goeie jagter, hou by die reëls en tydskedule.');

    IF NOT EXISTS (SELECT 1 FROM contactUs WHERE cuSenderEmail = 'jan@email.com' AND cuSubject = 'Navraag oor jag pakkette')
        INSERT INTO contactUs (cuSubject, cuContent, cuSenderName, cuSenderSurname, cuSenderEmail, cuPhoneNumber, cuType, cuIsResolved)
        VALUES ('Navraag oor jag pakkette', 'Ek stel belang in n buffel jag...', 'Jan', 'Botha', 'jan@email.com', '0821111111',
  'Enquiry', 0);

    IF NOT EXISTS (SELECT 1 FROM contactUs WHERE cuSenderEmail = 'susan@email.com' AND cuSubject = 'Probleem met registrasie')
        INSERT INTO contactUs (cuSubject, cuContent, cuSenderName, cuSenderSurname, cuSenderEmail, cuPhoneNumber, cuType, cuIsResolved)
        VALUES ('Probleem met registrasie', 'Ek kan nie my profiel update nie.', 'Susan', 'Pienaar', 'susan@email.com', '0832222222',
  'Support', 1);

    DECLARE @PH_DocTypeId INT = (SELECT dtId FROM documentType WHERE dtName = 'PH Lisensie');
    DECLARE @BUS_DocTypeId INT = (SELECT dtId FROM documentType WHERE dtName = 'Besigheidsregistrasie');
    DECLARE @DRONE_DocTypeId INT = (SELECT dtId FROM documentType WHERE dtName = 'Drone Lisensie');

    IF NOT EXISTS (SELECT 1 FROM userDocumentation WHERE udUserRole = @Pieter_PH_URID AND udName = 'PH Lisensie 2025')
        INSERT INTO userDocumentation (udUserRole, udName, udFilePath, udIssuedTimestamp, udExpirationTimestamp, udStatus,
  udUploadedTimestamp, udDtId)
        VALUES (@Pieter_PH_URID, 'PH Lisensie 2025', '/docs/ph_licence_2025.pdf', '2025-01-01', '2026-12-31', 'Approved', GETDATE(),
  @PH_DocTypeId);

    IF NOT EXISTS (SELECT 1 FROM userDocumentation WHERE udUserRole = @Hannes_PJ_URID AND udName = 'Besigheidsregistrasie')
        INSERT INTO userDocumentation (udUserRole, udName, udFilePath, udIssuedTimestamp, udExpirationTimestamp, udStatus,
  udUploadedTimestamp, udDtId)
        VALUES (@Hannes_PJ_URID, 'Besigheidsregistrasie', '/docs/business_reg.pdf', '2020-03-15', NULL, 'Approved', GETDATE(),
  @BUS_DocTypeId);

    IF NOT EXISTS (SELECT 1 FROM userDocumentation WHERE udUserRole = @Koos_VG_URID AND udName = 'Drone Lisensie')
        INSERT INTO userDocumentation (udUserRole, udName, udFilePath, udIssuedTimestamp, udExpirationTimestamp, udStatus,
  udUploadedTimestamp, udDtId)
        VALUES (@Koos_VG_URID, 'Drone Lisensie', '/docs/drone_licence.pdf', '2024-06-01', '2027-05-31', 'Pending', GETDATE(),
  @DRONE_DocTypeId);

    IF NOT EXISTS (SELECT 1 FROM team WHERE teamPjId = @Hannes_PJ_URID AND teamMemberId = @Koos_VG_URID)
        INSERT INTO team (teamPjId, teamMemberId, teamRow3Placeholder) VALUES (@Hannes_PJ_URID, @Koos_VG_URID, NULL);

    IF NOT EXISTS (SELECT 1 FROM team WHERE teamPjId = @Hannes_PJ_URID AND teamMemberId = @Pieter_PH_URID)
        INSERT INTO team (teamPjId, teamMemberId, teamRow3Placeholder) VALUES (@Hannes_PJ_URID, @Pieter_PH_URID, NULL);

    -- ============================================================
    -- 10. ADDITIONAL DATA FROM OTHER SCRIPTS
    -- ============================================================

    -- 10.1 Trophy Room demo data (SeedTrophyRoom.sql)
    DECLARE @FirstUserId INT = (SELECT MIN(userId) FROM users);
    DECLARE @SecondUserId INT = (SELECT MIN(userId) FROM users WHERE userId > @FirstUserId);
    DECLARE @ThirdUserId INT = (SELECT MIN(userId) FROM users WHERE userId > @SecondUserId);

    IF @FirstUserId IS NOT NULL AND @SecondUserId IS NOT NULL AND @ThirdUserId IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM trophies WHERE tOwnerUserId = @FirstUserId AND tSpecies = 'Koedoe' AND tHarvestDate = '2026-06-18')
            INSERT INTO trophies (tOwnerUserId, tSpecies, tPhotoPath, tAntlerSize, tCarcassWeight, tFirearmType, tHarvestDate,
  tLocationText, tNotes, tIsPublic)
            VALUES (@FirstUserId, 'Koedoe', 'Media/trophies/koedoe-demo.jpg', 104.50, 166.80, '.308 Winchester', '2026-06-18',
  'Waterberg-jaggebied', 'Demonstrasie-trofee vir die Trofeekamer.', 1);

        IF NOT EXISTS (SELECT 1 FROM trophies WHERE tOwnerUserId = @SecondUserId AND tSpecies = 'Springbok' AND tHarvestDate = '2026-05-
  09')
            INSERT INTO trophies (tOwnerUserId, tSpecies, tPhotoPath, tAntlerSize, tCarcassWeight, tFirearmType, tHarvestDate,
  tLocationText, tNotes, tIsPublic)
            VALUES (@SecondUserId, 'Springbok', 'Media/trophies/springbok-demo.jpg', 36.75, 21.40, '6.5 Creedmoor', '2026-05-09',
  'Karoo-jaggebied', NULL, 1);

        IF NOT EXISTS (SELECT 1 FROM trophies WHERE tOwnerUserId = @ThirdUserId AND tSpecies = 'Eland' AND tHarvestDate = '2026-04-02')
            INSERT INTO trophies (tOwnerUserId, tSpecies, tPhotoPath, tAntlerSize, tCarcassWeight, tFirearmType, tHarvestDate,
  tLocationText, tNotes, tIsPublic)
            VALUES (@ThirdUserId, 'Eland', 'Media/trophies/eland-demo.jpg', NULL, 282.40, '.375 H&H', '2026-04-02', 'Suidkus-jaggebied',
  'Privaat demonstrasie-trofee.', 0);

        IF NOT EXISTS (SELECT 1 FROM trophies WHERE tOwnerUserId = @FirstUserId AND tSpecies = 'Rooibok' AND tHarvestDate = '2026-03-
  21')
            INSERT INTO trophies (tOwnerUserId, tSpecies, tPhotoPath, tAntlerSize, tCarcassWeight, tFirearmType, tHarvestDate,
  tLocationText, tNotes, tIsPublic)
            VALUES (@FirstUserId, 'Rooibok', 'Media/trophies/rooibok-demo.jpg', NULL, 18.50, '7x57 Mauser', '2026-03-21', 'Bosveld-
  jaggebied', NULL, 1);
    END

    -- 10.2 seed_users.sql – add six extra users with roles
    DECLARE @hash VARCHAR(255) = 'ef92b778bafe771e89245b89ecbc08a44a4e166c06659911881f383d4473e94f'; -- 'password123'

    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'jagter@test.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
        VALUES ('Jan', 'Jagter', 'jagter@test.com', @hash, '0821111111', 1, 1, GETUTCDATE());
        DECLARE @jagterUserId INT = SCOPE_IDENTITY();
        INSERT INTO userRoles (urUserId, urRoleId) VALUES (@jagterUserId, @HUNTER_ROLE_ID);
        DECLARE @jagterURID INT = SCOPE_IDENTITY();
        INSERT INTO hunter (hUserRoleId, hCountry, hUsername, hBio) VALUES (@jagterURID, 'South Africa', 'janjagter', 'Ek is lief vir
  die natuur en biltong.');
    END

    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'boer@test.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
        VALUES ('Bennie', 'Boer', 'boer@test.com', @hash, '0822222222', 1, 1, GETUTCDATE());
        DECLARE @boerUserId INT = SCOPE_IDENTITY();
        INSERT INTO userRoles (urUserId, urRoleId) VALUES (@boerUserId, @FARMER_ROLE_ID);
        DECLARE @boerURID INT = SCOPE_IDENTITY();
        INSERT INTO farmer (fUserRoleId, fBusinessName, fBusinessRegistrationNumber, fBio) VALUES (@boerURID, 'Bennie se Plaas',
  '123456789', 'Beste wildsplaas in die bosveld.');
    END

    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'ph@test.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
        VALUES ('Piet', 'Professioneel', 'ph@test.com', @hash, '0823333333', 1, 1, GETUTCDATE());
        DECLARE @phUserId INT = SCOPE_IDENTITY();
        INSERT INTO userRoles (urUserId, urRoleId) VALUES (@phUserId, @PH_ROLE_ID);
        DECLARE @phURID INT = SCOPE_IDENTITY();
        INSERT INTO ph (phUserRoleId, phYearsOfExperience, phBio) VALUES (@phURID, 10, 'Ek jag al vir 10 jaar en ken elke bok.');
    END

    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'pj@test.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
        VALUES ('Paul', 'Uitruster', 'pj@test.com', @hash, '0824444444', 1, 1, GETUTCDATE());
        DECLARE @pjUserId INT = SCOPE_IDENTITY();
        INSERT INTO userRoles (urUserId, urRoleId) VALUES (@pjUserId, @PJ_ROLE_ID);
        DECLARE @pjURID INT = SCOPE_IDENTITY();
        INSERT INTO pj (pjUserRoleId, pjBusinessName, pjBusinessRegistrationNumber, pjYearsOfExperience, pjBio) VALUES (@pjURID, 'Paul
  Safaris', '987654321', 15, 'Ons maak drome waar.');
    END

    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'vg@test.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
        VALUES ('Vicus', 'Videograaf', 'vg@test.com', @hash, '0825555555', 1, 1, GETUTCDATE());
        DECLARE @vgUserId INT = SCOPE_IDENTITY();
        INSERT INTO userRoles (urUserId, urRoleId) VALUES (@vgUserId, @VG_ROLE_ID);
        DECLARE @vgURID INT = SCOPE_IDENTITY();
        INSERT INTO vg (vgUserRoleId, vgHasDrone, vgDroneLicence, vgCompanyName, vgCompanyRegistrationNumber, vgYearsOfExperience)
  VALUES (@vgURID, 1, 'DRN-1234', 'Vicus Videos', '55555555', 5);
    END

    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'admin@test.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate)
        VALUES ('Adam', 'Admin', 'admin@test.com', @hash, '0826666666', 1, 1, GETUTCDATE());
        DECLARE @adminUserId INT = SCOPE_IDENTITY();
        INSERT INTO userRoles (urUserId, urRoleId) VALUES (@adminUserId, @ADMIN_ROLE_ID);
    END

    -- 10.3 seed_farm_package.sql
    DECLARE @boerUserId2 INT = (SELECT userId FROM users WHERE uEmail = 'boer@test.com');
    DECLARE @boerURID2 INT = (SELECT userRoleId FROM userRoles WHERE urUserId = @boerUserId2 AND urRoleId = @FARMER_ROLE_ID);
    DECLARE @boerFarmerId INT = (SELECT farmerId FROM farmer WHERE fUserRoleId = @boerURID2);

    IF @boerFarmerId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM farm WHERE farmFarmerId = @boerFarmerId)
    BEGIN
        INSERT INTO farm (farmFarmerId, farmName, farmDescription, farmSize, farmTerrainType, farmFencingType, farmStatus)
        VALUES (@boerFarmerId, 'Bennie se Bosveld', 'Ervaar die regte bosveld in die hart van die land. Groot verskeidenheid bokke en
  uitstekende verblyf.', 5000, 'Bosveld', 'Wildsheining', 'ACTIVE');
    END

    DECLARE @boerFarmId INT = (SELECT farmId FROM farm WHERE farmFarmerId = @boerFarmerId);
    DECLARE @pjURID2 INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail = 'pj@test.com')
  AND urRoleId = @PJ_ROLE_ID);
    DECLARE @pjId2 INT = (SELECT pjId FROM pj WHERE pjUserRoleId = @pjURID2);

    IF @boerFarmId IS NOT NULL AND @pjId2 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM packages WHERE pPjId = @pjId2 AND pFarmId =
  @boerFarmId)
    BEGIN
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pDescription, pTitle, pAvailableFrom,
  pStatus, pDurationDays, pMinGuests, pMaxGuests)
        VALUES (GETUTCDATE(), '2027-12-31', @boerFarmId, @pjId2, 15000, 'Kudu en Rooibok Naweek Jagpakket met vol spyseniering en gidse
  ingesluit.', 'Naweek Bosveld Jag Safari', GETUTCDATE(), 'ACTIVE', 3, 2, 4);
    END

    -- 10.4 add_team.sql
    DECLARE @pjURID_team INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'hannes@mikpunt.com') AND urRoleId = @PJ_ROLE_ID);

    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'kobus.wessels@example.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uRegistrationDate, uPhone, uIsEmailVerified, uIsPhoneVerified)
        VALUES ('Kobus', 'Wessels', 'kobus.wessels@example.com', 'password123', GETDATE(), '0821112222', 1, 1);
        DECLARE @kobusUserId INT = SCOPE_IDENTITY();
        INSERT INTO userRoles (urUserId, urRoleId) VALUES (@kobusUserId, @PH_ROLE_ID);
        DECLARE @kobusURID INT = SCOPE_IDENTITY();
        INSERT INTO ph (phUserRoleId, phYearsOfExperience, phBio) VALUES (@kobusURID, 5, 'Ervare jagter');
        IF @pjURID_team IS NOT NULL
            INSERT INTO team (teamPjId, teamMemberId) VALUES (@pjURID_team, @kobusURID);
    END

    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'piet.botha@example.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uRegistrationDate, uPhone, uIsEmailVerified, uIsPhoneVerified)
        VALUES ('Piet', 'Botha', 'piet.botha@example.com', 'password123', GETDATE(), '0833334444', 1, 1);
        DECLARE @pietUserId INT = SCOPE_IDENTITY();
        INSERT INTO userRoles (urUserId, urRoleId) VALUES (@pietUserId, @FARMER_ROLE_ID);
        DECLARE @pietURID INT = SCOPE_IDENTITY();
        INSERT INTO farmer (fUserRoleId, fBusinessName, fBusinessRegistrationNumber, fBio) VALUES (@pietURID, 'Botha Boerdery', 'REG123',
  'Lekker plaas');
        IF @pjURID_team IS NOT NULL
            INSERT INTO team (teamPjId, teamMemberId) VALUES (@pjURID_team, @pietURID);
    END

    IF NOT EXISTS (SELECT 1 FROM users WHERE uEmail = 'johan.smit@example.com')
    BEGIN
        INSERT INTO users (uName, uSurname, uEmail, uPassword, uRegistrationDate, uPhone, uIsEmailVerified, uIsPhoneVerified)
        VALUES ('Johan', 'Smit', 'johan.smit@example.com', 'password123', GETDATE(), '0845556666', 1, 1);
        DECLARE @johanUserId INT = SCOPE_IDENTITY();
        INSERT INTO userRoles (urUserId, urRoleId) VALUES (@johanUserId, @VG_ROLE_ID);
        DECLARE @johanURID INT = SCOPE_IDENTITY();
        INSERT INTO vg (vgUserRoleId, vgHasDrone, vgDroneLicence, vgCompanyName, vgCompanyRegistrationNumber, vgYearsOfExperience)
  VALUES (@johanURID, 1, 'LIC123', 'Smit Media', 'REG123', 3);
        IF @pjURID_team IS NOT NULL
            INSERT INTO team (teamPjId, teamMemberId) VALUES (@pjURID_team, @johanURID);
    END

    -- 10.5 insert_packages.sql
    DECLARE @pj1 INT = @AfrikaPjId;
    DECLARE @pj2 INT = @BosveldPjId;
    DECLARE @pj3 INT = (SELECT pjId FROM pj WHERE pjUserRoleId = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM
  users WHERE uEmail = 'pj@test.com') AND urRoleId = @PJ_ROLE_ID));
    IF @pj3 IS NULL SET @pj3 = @pj1;
    DECLARE @pj4 INT = (SELECT pjId FROM pj WHERE pjUserRoleId = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM
  users WHERE uEmail = 'peter@mikpunt.com') AND urRoleId = @PJ_ROLE_ID));
    IF @pj4 IS NULL SET @pj4 = @pj1;

    DECLARE @farmForPackages INT = @KuduFarmId;

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Kudu en Rooibok Pakket' AND pPjId = @pj1)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj1, 15000.00, 'Kudu en Rooibok Pakket', 'Ideale naweek
  wegbreek vir die gesin. Groot trofee koedoes.', GETDATE(), 3, 2, 6, 4, 4, '1 Kudu, 2 Rooibokke, Verblyf', 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Biltong Jag Naweek' AND pPjId = @pj1)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj1, 8500.00, 'Biltong Jag Naweek', 'Genoeg biltong vir die
  res van die jaar!', GETDATE(), 2, 2, 4, 3, 4, '4 Rooibokke, 1 Blesbok, Verblyf', 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Groot 5 Buffel Ervaring' AND pPjId = @pj1)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj1, 35000.00, 'Groot 5 Buffel Ervaring', 'Ervaar die
  adrenalien om te voet in te stap op n buffel bul.', GETDATE(), 5, 1, 4, 7, 2, '1 Kaapse Buffel, Vol spyseniering', 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Oos-Kaap Vlakte Jag' AND pPjId = @pj2)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj2, 12000.00, 'Oos-Kaap Vlakte Jag', 'Oop vlaktes, lang
  skote. Ideaal vir die ervare skut.', GETDATE(), 3, 2, 8, 5, 6, '1 Springbok, 1 Gemsbok, Verblyf', 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Waterbok & Nyala Combo' AND pPjId = @pj2)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj2, 22000.00, 'Waterbok & Nyala Combo', 'Pragtige diere in
  bosveld habitat.', GETDATE(), 4, 2, 6, 5, 4, '1 Nyala, 1 Waterbok, Slagfooi ingesluit', 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Vark Jag Spesesiaal' AND pPjId = @pj3)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj3, 5000.00, 'Vark Jag Spesesiaal', 'Bosvarke in die nag en
  vlakvarke bedags.', GETDATE(), 2, 2, 6, 3, 6, 'Onbeperk Vlakvarke, 1 Bosvark', 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Eland Bul Pakket' AND pPjId = @pj3)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj3, 18500.00, 'Eland Bul Pakket', 'Jag die magtige eland in
  die klowe van die Karoo.', GETDATE(), 3, 2, 6, 4, 4, '1 Eland Bul, Verblyf', 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Vader en Seun Pakket' AND pPjId = @pj3)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj3, 28000.00, 'Vader en Seun Pakket', 'Bring die laaitie
  saam vir sy eerste bok!', GETDATE(), 2, 2, 4, 4, 2, '1 Kudu koei, 2 Springbokke, Veldgids ingesluit', 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Premium Bosveld Safari' AND pPjId = @pj4)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj4, 42000.00, 'Premium Bosveld Safari', 'Luukse verblyf,
  professionele fotos, en top kwaliteit diere.', GETDATE(), 7, 2, 10, 8, 8, '1 Sable, 1 Kudu, 2 Rooibokke, Etes ingesluit', 'Active');

    IF NOT EXISTS (SELECT 1 FROM packages WHERE pTitle = 'Boogjag Voorsit Pakket' AND pPjId = @pj4)
        INSERT INTO packages (pCreatedTimestamp, pAvailableTill, pFarmId, pPjId, pStockPrice, pTitle, pDescription, pAvailableFrom,
  pMinExpeditionLength, pMinGuests, pMaxGuests, pDurationDays, pMaxParticipants, pIncludedItems, pStatus)
        VALUES (GETDATE(), DATEADD(year, 1, GETDATE()), @farmForPackages, @pj4, 9500.00, 'Boogjag Voorsit Pakket', 'Spesifiek opgestel
  vir boogjagters met skuilings by watergate.', GETDATE(), 3, 1, 4, 4, 4, '1 Blouwildebees, 1 Vlakvark, Skuiling huur', 'Active');

    -- 10.6 SeedChats.sql
    DECLARE @user12 INT = (SELECT userId FROM users WHERE uEmail = 'hannes@mikpunt.com');
    DECLARE @user7 INT = (SELECT userId FROM users WHERE uEmail = 'sarel@mikpunt.com');
    DECLARE @user2 INT = (SELECT userId FROM users WHERE uEmail = 'pieter@mikpunt.com');
    DECLARE @user1 INT = (SELECT userId FROM users WHERE uEmail = 'johan@mikpunt.com');
    DECLARE @user6 INT = (SELECT userId FROM users WHERE uEmail = 'annemarie@mikpunt.com');

    IF @user12 IS NOT NULL AND @user7 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM chat WHERE cTitle = 'Jag Naweek - 15 Augustus')
    BEGIN
        INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (GETDATE(), 'Jag Naweek - 15 Augustus');
        DECLARE @chat3Id INT = SCOPE_IDENTITY();
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@chat3Id, @user12, GETDATE());
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@chat3Id, @user7, GETDATE());
        INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
        VALUES (@chat3Id, @user7, 'More Helgard! Is jy nog reg vir die jagnaweek by my plaas in die Vrystaat?', DATEADD(MINUTE, -5,
  GETDATE()));
    END

    IF @user12 IS NOT NULL AND @user2 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM chat WHERE cTitle = 'Navrae oor Pakkette')
    BEGIN
        INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (DATEADD(MINUTE, -45, GETDATE()), 'Navrae oor Pakkette');
        DECLARE @chat4Id INT = SCOPE_IDENTITY();
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@chat4Id, @user12, GETDATE());
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@chat4Id, @user2, GETDATE());
        INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
        VALUES (@chat4Id, @user2, 'Goeiedag, kan u asb die nuutste pryse vir Blesbokke en Springbokke vir my aanstuur?', DATEADD(MINUTE,
  -45, GETDATE()));
    END

    IF @user12 IS NOT NULL AND @user1 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM chat WHERE cTitle = 'Vervoer Reëlings')
    BEGIN
        INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (DATEADD(HOUR, -3, GETDATE()), 'Vervoer Reëlings');
        DECLARE @chat5Id INT = SCOPE_IDENTITY();
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@chat5Id, @user12, GETDATE());
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@chat5Id, @user1, GETDATE());
        INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
        VALUES (@chat5Id, @user12, 'John, hoe laat ry ons Vrydag oggend? Ek wil net my bakkie gereed kry.', DATEADD(HOUR, -3,
  GETDATE()));
    END

    IF @user12 IS NOT NULL AND @user6 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM chat WHERE cTitle = 'Algemene Gesels')
    BEGIN
        INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (DATEADD(DAY, -1, GETDATE()), 'Algemene Gesels');
        DECLARE @chat6Id INT = SCOPE_IDENTITY();
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@chat6Id, @user12, GETDATE());
        INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@chat6Id, @user6, GETDATE());
        INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
        VALUES (@chat6Id, @user6, 'Hallo Helgard, jou profiel verifikasie is suksesvol voltooi. Welkom by MikPunt!', DATEADD(DAY, -1,
  GETDATE()));
    END

    -- 10.7 add_team_invites.sql
    DECLARE @pjInviteId INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail =
  'hannes@mikpunt.com') AND urRoleId = @PJ_ROLE_ID);
    DECLARE @phInviteId INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail = 'ph@test.com')
  AND urRoleId = @PH_ROLE_ID);
    DECLARE @vgInviteId INT = (SELECT userRoleId FROM userRoles WHERE urUserId = (SELECT userId FROM users WHERE uEmail = 'vg@test.com')
  AND urRoleId = @VG_ROLE_ID);

    IF @pjInviteId IS NOT NULL AND @phInviteId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM teamInvites WHERE invitePjId = @pjInviteId AND
  inviteMemberId = @phInviteId)
        INSERT INTO teamInvites (invitePjId, inviteMemberId, inviteStatus) VALUES (@pjInviteId, @phInviteId, 'Pending');

    IF @pjInviteId IS NOT NULL AND @vgInviteId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM teamInvites WHERE invitePjId = @pjInviteId AND
  inviteMemberId = @vgInviteId)
        INSERT INTO teamInvites (invitePjId, inviteMemberId, inviteStatus) VALUES (@pjInviteId, @vgInviteId, 'Accepted');

    -- ============================================================
    -- 11. FINAL VERIFICATION
    -- ============================================================
    PRINT '==========================================';
    PRINT '✅ Volledige databasis en toetsdata suksesvol ingevoer!';
    PRINT '==========================================';

    SELECT 'users' AS Tabel, COUNT(*) AS AantalRye FROM users
    UNION SELECT 'userRoles', COUNT(*) FROM userRoles
    UNION SELECT 'roles', COUNT(*) FROM roles
    UNION SELECT 'hunter', COUNT(*) FROM hunter
    UNION SELECT 'ph', COUNT(*) FROM ph
    UNION SELECT 'pj', COUNT(*) FROM pj
    UNION SELECT 'farmer', COUNT(*) FROM farmer
    UNION SELECT 'vg', COUNT(*) FROM vg
    UNION SELECT 'packages', COUNT(*) FROM packages
    UNION SELECT 'bookings', COUNT(*) FROM bookings
    UNION SELECT 'addresses', COUNT(*) FROM addresses
    UNION SELECT 'advertisements', COUNT(*) FROM advertisements
    UNION SELECT 'trophies', COUNT(*) FROM trophies
    UNION SELECT 'team', COUNT(*) FROM team
    UNION SELECT 'teamInvites', COUNT(*) FROM teamInvites
    UNION SELECT 'chat', COUNT(*) FROM chat
    ORDER BY Tabel;
