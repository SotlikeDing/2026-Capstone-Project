DECLARE @Chat1Id INT, @Chat2Id INT, @Chat3Id INT, @Chat4Id INT;

-- Chat 1: Jag Naweek (User 12 & 7)
INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (GETDATE(), 'Jag Naweek - 15 Augustus');
SET @Chat1Id = SCOPE_IDENTITY();

INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@Chat1Id, 12, GETDATE());
INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@Chat1Id, 7, GETDATE());

INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
VALUES (@Chat1Id, 7, 'More Helgard! Is jy nog reg vir die jagnaweek by my plaas in die Vrystaat?', DATEADD(MINUTE, -5, GETDATE()));


-- Chat 2: Navrae oor Pakkette (User 12 & 2)
INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (DATEADD(MINUTE, -45, GETDATE()), 'Navrae oor Pakkette');
SET @Chat2Id = SCOPE_IDENTITY();

INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@Chat2Id, 12, GETDATE());
INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@Chat2Id, 2, GETDATE());

INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
VALUES (@Chat2Id, 2, 'Goeiedag, kan u asb die nuutste pryse vir Blesbokke en Springbokke vir my aanstuur?', DATEADD(MINUTE, -45, GETDATE()));


-- Chat 3: Vervoer Reëlings (User 12 & 1)
INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (DATEADD(HOUR, -3, GETDATE()), 'Vervoer Reëlings');
SET @Chat3Id = SCOPE_IDENTITY();

INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@Chat3Id, 12, GETDATE());
INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@Chat3Id, 1, GETDATE());

INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
VALUES (@Chat3Id, 12, 'John, hoe laat ry ons Vrydag oggend? Ek wil net my bakkie gereed kry.', DATEADD(HOUR, -3, GETDATE()));


-- Chat 4: Algemene Gesels (User 12 & 6)
INSERT INTO chat (cCreatedTimestamp, cTitle) VALUES (DATEADD(DAY, -1, GETDATE()), 'Algemene Gesels');
SET @Chat4Id = SCOPE_IDENTITY();

INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@Chat4Id, 12, GETDATE());
INSERT INTO chatParticipants (cpChatId, cpUserId, cpJoinedTimestamp) VALUES (@Chat4Id, 6, GETDATE());

INSERT INTO chatMessages (cmChatId, cmSenderId, cmContent, cmSentTimestamp)
VALUES (@Chat4Id, 6, 'Hallo Helgard, jou profiel verifikasie is suksesvol voltooi. Welkom by MikPunt!', DATEADD(DAY, -1, GETDATE()));

PRINT 'Successfully inserted dummy chats!';
