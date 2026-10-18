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

CREATE TABLE ap (  -- Advertisement Placer
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

CREATE TABLE pj (  -- Outfitter
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

CREATE TABLE vg (  -- Videographer
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
    adLastPaymentDate DATETIME2
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
    cpLastReadMessage INT  -- FK to chatMessages.chatMessageId (defined below)
);

CREATE TABLE chatMessages (
    chatMessageId INT IDENTITY(1,1) PRIMARY KEY,
    cmChatId INT FOREIGN KEY REFERENCES chat(chatId),
    cmSenderId INT FOREIGN KEY REFERENCES users(userId),
    cmContent NVARCHAR(MAX),
    cmSentTimestamp DATETIME2
);

-- Add FK for chatParticipants.cpLastReadMessage after chatMessages is created
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

CREATE TABLE dbo.trophies (
    trophyId INT IDENTITY(1,1) PRIMARY KEY,
    tOwnerUserId INT NOT NULL FOREIGN KEY REFERENCES dbo.users(userId),
    tSpecies NVARCHAR(120) NOT NULL,
    tPhotoPath NVARCHAR(500) NOT NULL,
    tAntlerSize DECIMAL(10, 2) NULL, -- centimetres
    tCarcassWeight DECIMAL(10, 2) NULL, -- kilograms
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

CREATE TABLE dbo.trophyRowlandWardEvaluations (
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
        FOREIGN KEY (trweTrophyId) REFERENCES dbo.trophies(trophyId) ON DELETE CASCADE,
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
    ON dbo.trophyRowlandWardEvaluations(trweTrophyId)
    WHERE trweIsCurrent = 1;

CREATE INDEX IX_trophyRowlandWardEvaluations_Trophy
    ON dbo.trophyRowlandWardEvaluations(trweTrophyId, trweEvaluatedAtUtc DESC);

CREATE TABLE dbo.trophyRowlandWardMeasurements (
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
        REFERENCES dbo.trophyRowlandWardEvaluations(trophyRowlandWardEvaluationId)
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
    teamRow3Placeholder NVARCHAR(MAX)  -- Placeholder for "Row 3" from diagram
);

CREATE TABLE AuthTokens (
atId INT IDENTITY(1,1) PRIMARY KEY,
atToken VARCHAR(255) NOT NULL,
atCreated DATETIME2 NOT NULL DEFAULT GETDATE(),
atExpired DATETIME2 NOT NULL,
atUserId INT NOT NULL,
atIP VARCHAR(255),

CONSTRAINT UQ_AuthTokens_Token UNIQUE (atToken),

CONSTRAINT FK_AuthTokens_User
FOREIGN KEY (atUserId)
REFERENCES [Users](userId)
);

INSERT INTO roles (roleCode, roleName) VALUES
('HUNTER', 'Jagter'),
('PH', 'Professionele Jagter'),
('PJ', 'Toerusting Verskaffer'),
('FARMER', 'Boer/Grondeienaar'),
('VG', 'Videograaf'),
('AP', 'Advertensie Plaser'),
('ADMIN', 'Administrateur');
GO

INSERT INTO provinces (provName) VALUES
('Gauteng'),
('Wes-Kaap'),
('Oos-Kaap'),
('KwaZulu-Natal'),
('Vrystaat'),
('Noordwes'),
('Mpumalanga'),
('Limpopo'),
('Noord-Kaap');
GO
