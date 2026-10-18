import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/models/trophy.dart';
import 'package:jagbestuur_app/screens/add_trophy_screen.dart';
import 'package:jagbestuur_app/services/public_trophy_service.dart';
import 'package:jagbestuur_app/theme/app_theme.dart';
import 'package:jagbestuur_app/widgets/trophy_shot_distance.dart';

void main() {
  group('Trofee-skootafstand modelle', () {
    test('bestaande trofee sonder veld bly geldig', () {
      final trophy = Trophy.fromJson(_trophyJson());

      expect(trophy.shotDistanceMeters, isNull);
    });

    test('eienaarstrofee lees die afstand', () {
      final trophy = Trophy.fromJson(
        _trophyJson()..['shotDistanceMeters'] = 185,
      );

      expect(trophy.shotDistanceMeters, 185);
    });

    test('stoorversoek stuur null wanneer die afstand verwyder is', () {
      final json = _saveRequest(shotDistanceMeters: null).toJson();

      expect(json['shotDistanceMeters'], isNull);
    });

    test('stoorversoek stuur die hele meteraantal', () {
      final json = _saveRequest(shotDistanceMeters: 212).toJson();

      expect(json['shotDistanceMeters'], 212);
    });

    test(
      'openbare ranglysmodel lees die afstand sonder om rangdata te verander',
      () {
        final trophy = LeaderboardTrophy.fromJson({
          'id': 8,
          'species': 'Springbok',
          'shotDistanceMeters': 212,
          'selectedSortValue': 36.75,
        });

        expect(trophy.shotDistanceMeters, 212);
        expect(trophy.selectedSortValue, 36.75);
      },
    );
  });

  group('Trofee-skootafstand vertoon', () {
    testWidgets('wys die afstand wanneer dit bestaan', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(body: TrophyShotDistance(meters: 185)),
        ),
      );

      expect(find.text('Skootafstand: 185 m'), findsOneWidget);
    });

    testWidgets('laat die afstand heeltemal weg wanneer dit null is', (
      tester,
    ) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(body: TrophyShotDistance(meters: null)),
        ),
      );

      expect(find.textContaining('Skootafstand'), findsNothing);
      expect(find.textContaining('0 m'), findsNothing);
    });
  });

  group('Trofee-skootafstand vorm', () {
    testWidgets('nuwe trofee se opsionele veld begin leeg', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.darkHuntingTheme,
          home: const AddTrophyScreen(),
        ),
      );

      final finder = find.widgetWithText(TextFormField, 'Skootafstand (m)');
      await tester.scrollUntilVisible(
        finder,
        350,
        scrollable: find.byType(Scrollable).first,
      );
      expect(finder, findsOneWidget);
      expect(tester.widget<TextFormField>(finder).controller?.text, isEmpty);
    });

    testWidgets("wys 'n bestaande afstand wanneer die trofee gewysig word", (
      tester,
    ) async {
      final trophy = Trophy.fromJson(
        _trophyJson()..['shotDistanceMeters'] = 212,
      );

      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.darkHuntingTheme,
          home: AddTrophyScreen(trophy: trophy),
        ),
      );

      final finder = find.widgetWithText(TextFormField, 'Skootafstand (m)');
      await tester.scrollUntilVisible(
        finder,
        350,
        scrollable: find.byType(Scrollable).first,
      );
      expect(finder, findsOneWidget);
      expect(tester.widget<TextFormField>(finder).controller?.text, '212');
    });
  });
}

Map<String, dynamic> _trophyJson() {
  return {
    'id': 1,
    'ownerUserId': 3,
    'species': 'Koedoe',
    'photoPath': '',
    'antlerSizeCm': 104.50,
    'carcassWeightKg': 166.80,
    'firearmType': '.308 Winchester',
    'harvestDate': '2026-06-18',
    'locationText': 'Waterberg-jaggebied',
    'notes': null,
    'isPublic': true,
    'createdTimestamp': '2026-06-18T10:00:00Z',
    'updatedTimestamp': '2026-06-18T10:00:00Z',
  };
}

TrophySaveRequest _saveRequest({required int? shotDistanceMeters}) {
  return TrophySaveRequest(
    species: 'Koedoe',
    photoPath: '',
    antlerSizeCm: 104.50,
    carcassWeightKg: 166.80,
    shotDistanceMeters: shotDistanceMeters,
    firearmType: '.308 Winchester',
    harvestDate: DateTime(2026, 6, 18),
    locationText: 'Waterberg-jaggebied',
    notes: null,
    isPublic: true,
  );
}
