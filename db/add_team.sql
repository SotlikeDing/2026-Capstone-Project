DECLARE @UserId1 INT, @UserId2 INT, @UserId3 INT;
DECLARE @UserRoleId1 INT, @UserRoleId2 INT, @UserRoleId3 INT;

-- Insert User 1 (PH)
INSERT INTO users (uName, uSurname, uEmail, uPassword, uRegistrationDate, uPhone, uIsEmailVerified, uIsPhoneVerified)
VALUES ('Kobus', 'Wessels', 'kobus.wessels@example.com', 'password123', GETDATE(), '0821112222', 1, 1);
SET @UserId1 = SCOPE_IDENTITY();

-- Insert User 2 (FARMER)
INSERT INTO users (uName, uSurname, uEmail, uPassword, uRegistrationDate, uPhone, uIsEmailVerified, uIsPhoneVerified)
VALUES ('Piet', 'Botha', 'piet.botha@example.com', 'password123', GETDATE(), '0833334444', 1, 1);
SET @UserId2 = SCOPE_IDENTITY();

-- Insert User 3 (VG)
INSERT INTO users (uName, uSurname, uEmail, uPassword, uRegistrationDate, uPhone, uIsEmailVerified, uIsPhoneVerified)
VALUES ('Johan', 'Smit', 'johan.smit@example.com', 'password123', GETDATE(), '0845556666', 1, 1);
SET @UserId3 = SCOPE_IDENTITY();

-- Assign Roles
INSERT INTO userRoles (urUserId, urRoleId) VALUES (@UserId1, 2); -- PH
SET @UserRoleId1 = SCOPE_IDENTITY();

INSERT INTO userRoles (urUserId, urRoleId) VALUES (@UserId2, 4); -- FARMER
SET @UserRoleId2 = SCOPE_IDENTITY();

INSERT INTO userRoles (urUserId, urRoleId) VALUES (@UserId3, 5); -- VG
SET @UserRoleId3 = SCOPE_IDENTITY();

-- Add them to respective profile tables
INSERT INTO ph (phUserRoleId, phYearsOfExperience, phBio) VALUES (@UserRoleId1, 5, 'Ervare jagter');
INSERT INTO farmer (fUserRoleId, fBusinessName, fBusinessRegistrationNumber, fBio) VALUES (@UserRoleId2, 'Botha Boerdery', 'REG123', 'Lekker plaas');
INSERT INTO vg (vgUserRoleId, vgHasDrone, vgDroneLicence, vgCompanyName, vgCompanyRegistrationNumber, vgYearsOfExperience) VALUES (@UserRoleId3, 1, 'LIC123', 'Smit Media', 'REG123', 3);

-- Link them to helgardo (PJ)
-- helgardo's PJ userRoleId is 12
INSERT INTO team (teamPjId, teamMemberId, teamRow3Placeholder) VALUES (12, @UserRoleId1, NULL);
INSERT INTO team (teamPjId, teamMemberId, teamRow3Placeholder) VALUES (12, @UserRoleId2, NULL);
INSERT INTO team (teamPjId, teamMemberId, teamRow3Placeholder) VALUES (12, @UserRoleId3, NULL);
