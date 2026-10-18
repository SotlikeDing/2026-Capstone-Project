import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/models/booking.dart';
import 'package:jagbestuur_app/models/chat_models.dart';
import 'package:jagbestuur_app/models/smart_waypoint.dart';
import 'package:jagbestuur_app/screens/leaderboard_results_screen.dart';
import 'package:jagbestuur_app/services/api_error.dart';
import 'package:jagbestuur_app/services/legacy_storage_cleanup.dart';
import 'package:jagbestuur_app/services/public_trophy_service.dart';

LeaderboardTrophy _trophy(
  int id,
  String species, {
  double? sort,
  double? antler,
}) {
  return LeaderboardTrophy(
    id: id,
    photoPath: '',
    species: species,
    antlerSizeCm: antler,
    carcassWeightKg: null,
    shotDistanceMeters: null,
    firearmType: '',
    ownerDisplayName: null,
    selectedSortValue: sort,
  );
}

void main() {
  group('guardApi', () {
    test('turns a malformed response into the fallback message', () async {
      expect(
        () => guardApi<int>(
          () async => throw const FormatException('bad'),
          fallback: 'Kon nie laai nie.',
        ),
        throwsA(
          isA<Exception>().having(
            (e) => e.toString(),
            'message',
            contains('Kon nie laai nie.'),
          ),
        ),
      );
    });

    test('uses the server message for HTTP errors', () async {
      final request = RequestOptions(path: '/x');
      expect(
        () => guardApi<int>(
          () async => throw DioException(
            requestOptions: request,
            response: Response(
              requestOptions: request,
              statusCode: 400,
              data: {'message': 'Ongeldige versoek.'},
            ),
          ),
          fallback: 'fallback',
        ),
        throwsA(
          isA<Exception>().having(
            (e) => e.toString(),
            'message',
            contains('Ongeldige versoek.'),
          ),
        ),
      );
    });
  });

  test('parseEach skips bad records instead of failing the list', () {
    final bookings = parseEach(
      [
        {'id': 1, 'startDate': '2026-05-01', 'endDate': '2026-05-03'},
        {'id': 2, 'startDate': null, 'endDate': '2026-05-03'},
        'not an object',
      ],
      Booking.fromJson,
      label: 'booking',
    );
    expect(bookings.map((b) => b.id), [1]);
  });

  test('Booking reads the farm owner id when present', () {
    final booking = Booking.fromJson({
      'id': 5,
      'startDate': '2026-05-01T00:00:00',
      'endDate': '2026-05-02T00:00:00',
      'farmOwnerUserId': 42,
    });
    expect(booking.farmOwnerUserId, 42);
  });

  test('Booking reads farm rating summary and the hunter existing rating', () {
    final booking = Booking.fromJson({
      'id': 6,
      'startDate': '2026-11-01T00:00:00',
      'endDate': '2026-11-03T00:00:00',
      'farmId': 12,
      'farmAverageRating': 3.75,
      'farmReviewCount': 4,
      'canRateFarm': true,
      'myFarmRatingStars': 0,
      'myFarmRatingDescription': 'Kon beter wees.',
    });

    expect(booking.farmId, 12);
    expect(booking.farmAverageRating, 3.75);
    expect(booking.farmReviewCount, 4);
    expect(booking.hasFarmReviews, isTrue);
    expect(booking.canRateFarm, isTrue);
    expect(booking.myFarmRatingStars, 0);
    expect(booking.myFarmRatingDescription, 'Kon beter wees.');
  });

  test('A completed booking is grouped with past bookings', () {
    final booking = Booking.fromJson({
      'id': 7,
      'startDate': '2099-05-01T00:00:00',
      'endDate': '2099-05-02T00:00:00',
      'status': 'Voltooi',
    });

    expect(booking.isCompleted, isTrue);
    expect(booking.isPast, isTrue);
  });

  test('ChatSummary exposes active participant user ids', () {
    final chat = ChatSummary.fromJson({
      'id': 8,
      'participants': [
        {'userId': 10, 'leftAtTimestamp': null},
        {'UserId': 20, 'LeftAtTimestamp': null},
        {'userId': 30, 'leftAtTimestamp': '2026-09-01T10:00:00Z'},
      ],
    });

    expect(chat.participantUserIds, containsAll(<int>[10, 20]));
    expect(chat.participantUserIds, isNot(contains(30)));
    expect(chat.isDirectChatBetween(10, 20), isTrue);
    expect(chat.isDirectChatBetween(10, 30), isFalse);
  });

  test('legacy cleanup deletes only the old shared hunt folders', () async {
    final root = await Directory.systemTemp.createTemp('mikpunt_cleanup_');
    final documents = Directory('${root.path}${Platform.pathSeparator}docs');
    final temporary = Directory('${root.path}${Platform.pathSeparator}temp');

    try {
      final legacyHunts = Directory(
        '${documents.path}${Platform.pathSeparator}hunt_sessions',
      );
      final legacyVideos = Directory(
        '${temporary.path}${Platform.pathSeparator}recap_videos',
      );
      final userData = Directory(
        '${documents.path}${Platform.pathSeparator}users${Platform.pathSeparator}7${Platform.pathSeparator}hunt_sessions',
      );
      await legacyHunts.create(recursive: true);
      await legacyVideos.create(recursive: true);
      await userData.create(recursive: true);
      await File(
        '${legacyHunts.path}${Platform.pathSeparator}old.json',
      ).writeAsString('{}');
      await File(
        '${userData.path}${Platform.pathSeparator}current.json',
      ).writeAsString('{}');

      await LegacyStorageCleanup.deleteSharedHuntDataIn(
        documentsDirectory: documents,
        temporaryDirectory: temporary,
      );

      expect(await legacyHunts.exists(), isFalse);
      expect(await legacyVideos.exists(), isFalse);
      expect(await userData.exists(), isTrue);
    } finally {
      if (await root.exists()) {
        await root.delete(recursive: true);
      }
    }
  });

  test('waypoint timestamps round-trip through UTC', () {
    final local = DateTime(2026, 9, 23, 14, 30);
    final json = SmartWaypoint(
      latitude: 1,
      longitude: 2,
      timestamp: local,
    ).toJson();

    expect(json['timestamp'], endsWith('Z'));
    final parsed = SmartWaypoint.fromJson(json);
    expect(parsed.timestamp.isUtc, isFalse);
    expect(parsed.timestamp, local);
  });

  group('leaderboard', () {
    test('groups legacy species case-insensitively, like the server', () {
      final groups = buildLeaderboardGroups([
        _trophy(1, 'Kudu', sort: 10, antler: 10),
        _trophy(2, 'kudu ', sort: 8, antler: 8),
      ]);
      expect(groups, hasLength(1));
      expect(groups.single.trophies, hasLength(2));
    });

    test('only numbers trophies that have a value; ties share a rank', () {
      final group = buildLeaderboardGroups([
        _trophy(1, 'Kudu', sort: 50, antler: 50),
        _trophy(2, 'Kudu', sort: 50, antler: 50),
        _trophy(3, 'Kudu', sort: 40, antler: 40),
        _trophy(4, 'Kudu'),
      ]).single;

      expect(rankGroup(group).map((r) => r.rank), [1, 1, 3, null]);
    });
  });
}
