import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/models/hunt_pin.dart';
import 'package:jagbestuur_app/models/hunt_session.dart';
import 'package:jagbestuur_app/models/hunter_statistics.dart';
import 'package:jagbestuur_app/models/trophy.dart';
import 'package:jagbestuur_app/screens/home_screen.dart';
import 'package:jagbestuur_app/theme/app_theme.dart';

void main() {
  group('HunterStatistics', () {
    test('combines trophy-room and map-session statistics', () {
      final trophies = <Trophy>[
        _trophy(
          id: 1,
          species: 'Koedoe',
          harvestDate: DateTime(2026, 5, 3),
          location: 'Bosveld',
          firearm: '.308',
          shotDistance: 185,
          antlerSize: 121.92,
          isPublic: true,
        ),
        _trophy(
          id: 2,
          species: 'Koedoe',
          harvestDate: DateTime(2025, 8, 10),
          location: 'Bosveld',
          firearm: '.308',
          shotDistance: 240,
          carcassWeight: 96,
        ),
        _trophy(
          id: 3,
          species: 'Springbok',
          harvestDate: DateTime(2026, 7, 1),
          location: 'Karoo',
          firearm: '.243',
          shotDistance: 130,
        ),
      ];
      final hunts = <HuntSession>[
        _hunt(
          id: 'one',
          startedAt: DateTime(2026, 5, 3, 6),
          duration: const Duration(hours: 2),
          distance: 4500,
          pins: [
            _pin('shot', HuntPinType.shotFired),
            _pin('animal', HuntPinType.animalFound),
          ],
        ),
        _hunt(
          id: 'two',
          startedAt: DateTime(2026, 5, 18, 6),
          duration: const Duration(hours: 4),
          distance: 7200,
          pins: [_pin('shot-2', HuntPinType.shotFired)],
        ),
        _hunt(
          id: 'old',
          startedAt: DateTime(2025, 4, 2, 6),
          duration: const Duration(hours: 3),
          distance: 3000,
        ),
      ];

      final stats = HunterStatistics.calculate(
        trophies: trophies,
        hunts: hunts,
        now: DateTime(2026, 10, 8),
      );

      expect(stats.huntsThisYear, 2);
      expect(stats.lifetimeHunts, 3);
      expect(stats.trophiesThisYear, 2);
      expect(stats.totalTrophies, 3);
      expect(stats.distanceThisYearMeters, 11700);
      expect(stats.totalDistanceMeters, 14700);
      expect(stats.averageHuntTime, const Duration(hours: 3));
      expect(stats.huntsByMonth[4], 2);
      expect(stats.furthestShot?.id, 2);
      expect(stats.longestRoute?.id, 'two');
      expect(stats.longestHunt?.id, 'two');
      expect(stats.longestAntlers?.id, 1);
      expect(stats.heaviestCarcass?.id, 2);
      expect(stats.topSpecies?.label, 'Koedoe');
      expect(stats.topSpecies?.count, 2);
      expect(stats.topLocation?.label, 'Bosveld');
      expect(stats.favouriteFirearm?.label, '.308');
      expect(stats.uniqueSpecies, 2);
      expect(stats.uniqueLocations, 2);
      expect(stats.publicTrophies, 1);
      expect(stats.shotsMarked, 2);
      expect(stats.animalsFound, 1);
      expect(stats.latestActivity, DateTime(2026, 7, 1));
    });

    test('returns a safe empty snapshot', () {
      final stats = HunterStatistics.calculate(
        trophies: const [],
        hunts: const [],
        now: DateTime(2026, 10, 8),
      );

      expect(stats.year, 2026);
      expect(stats.huntsThisYear, 0);
      expect(stats.totalTrophies, 0);
      expect(stats.huntsByMonth, List<int>.filled(12, 0));
      expect(stats.furthestShot, isNull);
      expect(stats.trophyHighlight, isNull);
    });
  });

  testWidgets('Tuisblad replaces quick links with real statistics', (
    tester,
  ) async {
    // A narrow handset catches horizontal overflow regressions in the cards.
    tester.view.physicalSize = const Size(320, 720);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.darkHuntingTheme,
        home: HomeScreen(
          displayName: 'Pieter Pretorius',
          trophyLoader: () async => [
            _trophy(
              id: 1,
              species: 'Koedoe',
              harvestDate: DateTime(2026, 5, 3),
              location: 'Bosveld',
              firearm: '.308',
              shotDistance: 185,
            ),
          ],
          huntLoader: () async => [
            _hunt(
              id: 'one',
              startedAt: DateTime(2026, 5, 3, 6),
              duration: const Duration(hours: 2),
              distance: 4500,
            ),
          ],
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Tuisblad'), findsOneWidget);
    expect(find.textContaining('Welkom terug, Pieter'), findsOneWidget);
    expect(find.text('Verste Skoot'), findsOneWidget);
    expect(find.text('185 m'), findsOneWidget);
    expect(find.text('Unieke Spesies'), findsOneWidget);
    expect(find.text('Langste Horingmeting'), findsNothing);
    expect(find.text('Swaarste Karkas'), findsNothing);
    expect(find.textContaining('Nog geen'), findsNothing);
    expect(find.text('Maak die kaart oop'), findsNothing);
    expect(find.text('Besprekings'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('Tuisblad hides every statistic that has no source data', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.darkHuntingTheme,
        home: HomeScreen(
          trophyLoader: () async => const [],
          huntLoader: () async => const [],
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Tuisblad'), findsOneWidget);
    expect(find.text('Jou Hoogtepunte'), findsNothing);
    expect(find.text('Jou Jaar In Die Veld'), findsNothing);
    expect(find.text('Wat Jou Storie Vertel'), findsNothing);
    expect(find.text('Mylpale'), findsNothing);
    expect(find.text('0 km'), findsNothing);
    expect(find.text('0m'), findsNothing);
    expect(find.text('—'), findsNothing);
    expect(find.textContaining('Nog geen'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}

Trophy _trophy({
  required int id,
  required String species,
  required DateTime harvestDate,
  required String location,
  required String firearm,
  int? shotDistance,
  double? antlerSize,
  double? carcassWeight,
  bool isPublic = false,
}) {
  return Trophy(
    id: id,
    ownerUserId: 7,
    species: species,
    photoPath: '',
    antlerSizeCm: antlerSize,
    carcassWeightKg: carcassWeight,
    shotDistanceMeters: shotDistance,
    firearmType: firearm,
    harvestDate: harvestDate,
    locationText: location,
    notes: null,
    isPublic: isPublic,
    createdTimestamp: harvestDate,
    updatedTimestamp: harvestDate,
  );
}

HuntSession _hunt({
  required String id,
  required DateTime startedAt,
  required Duration duration,
  required double distance,
  List<HuntPin> pins = const [],
}) {
  return HuntSession(
    id: id,
    startedAt: startedAt,
    endedAt: startedAt.add(duration),
    totalDistanceMeters: distance,
    waypoints: const [],
    pins: pins,
  );
}

HuntPin _pin(String id, HuntPinType type) {
  return HuntPin(
    id: id,
    type: type,
    latitude: -25,
    longitude: 28,
    timestamp: DateTime(2026, 5, 3, 8),
  );
}
