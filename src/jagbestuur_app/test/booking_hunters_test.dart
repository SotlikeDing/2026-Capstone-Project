import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/models/booking.dart';
import 'package:jagbestuur_app/models/booking_hunter.dart';
import 'package:jagbestuur_app/services/booking_service.dart';
import 'package:jagbestuur_app/widgets/booking_hunters_sheet.dart';

Map<String, dynamic> bookingJson([Map<String, dynamic> extra = const {}]) => {
  'id': 5,
  'title': 'Koedoe Safari',
  'startDate': '2026-11-01T00:00:00',
  'endDate': '2026-11-05T00:00:00',
  'hunterName': 'Piet Pompies',
  'huntGuests': 3,
  ...extra,
};

class FakeBookingService extends BookingService {
  FakeBookingService(this.roster) : super(client: Dio());

  BookingHunters roster;
  final added = <String>[];
  final removed = <int>[];

  @override
  Future<BookingHunters> getHunters(int bookingId) async => roster;

  @override
  Future<String> addHunter(
    int bookingId, {
    required String name,
    required String email,
  }) async {
    added.add('$name <$email>');
    return 'Uitnodiging gestuur.';
  }

  @override
  Future<void> removeHunter(int bookingId, int companionId) async =>
      removed.add(companionId);
}

BookingHunters roster({
  required bool canManage,
  required bool callerIsMain,
  int openSlots = 1,
  bool youAreCompanion = false,
}) {
  return BookingHunters(
    bookingId: 5,
    maxHunters: 3,
    openSlots: openSlots,
    canManage: canManage,
    callerIsMainHunter: callerIsMain,
    hunters: [
      BookingHunter(
        companionId: 0,
        name: 'Piet Pompies',
        status: 'Main',
        isMainHunter: true,
        isYou: callerIsMain,
      ),
      BookingHunter(
        companionId: 7,
        name: 'Sarel Seun',
        status: 'Linked',
        isYou: youAreCompanion,
      ),
    ],
  );
}

Future<void> openSheet(WidgetTester tester, FakeBookingService service) async {
  final booking = Booking.fromJson(bookingJson());
  await tester.pumpWidget(
    MaterialApp(
      home: Scaffold(
        body: Builder(
          builder: (context) => TextButton(
            onPressed: () =>
                BookingHuntersSheet.show(context, booking, service: service),
            child: const Text('open'),
          ),
        ),
      ),
    ),
  );
  await tester.tap(find.text('open'));
  await tester.pumpAndSettle();
}

void main() {
  group('Booking JSON', () {
    test(
      'bookings from a server without multi-hunter support are the hunter\'s own',
      () {
        final b = Booking.fromJson(bookingJson());
        expect(b.isMainHunter, isTrue);
        expect(b.canManageHunters, isFalse);
        expect(b.maxHunters, 1);
        expect(b.hunterCount, 1);
      },
    );

    test('a trip the hunter was added to is not theirs to manage', () {
      final b = Booking.fromJson(
        bookingJson({
          'isMainHunter': false,
          'maxHunters': 3,
          'hunterCount': 2,
          'canManageHunters': false,
        }),
      );
      expect(b.isMainHunter, isFalse);
      expect(b.maxHunters, 3);
      expect(b.hunterCount, 2);
    });

    test('roster JSON parses hunters, skipping malformed entries', () {
      final r = BookingHunters.fromJson({
        'bookingId': 5,
        'maxHunters': 3,
        'openSlots': 1,
        'canManage': true,
        'callerIsMainHunter': true,
        'hunters': [
          {
            'companionId': 0,
            'name': 'Piet',
            'status': 'Main',
            'isMainHunter': true,
            'isYou': true,
          },
          {
            'companionId': 7,
            'name': 'Sarel',
            'email': 'sarel@x.co',
            'status': 'Pending',
          },
          'not a hunter',
        ],
      });
      expect(r.hunters, hasLength(2));
      expect(r.hunters[1].isPending, isTrue);
      expect(r.hunters[1].email, 'sarel@x.co');
    });
  });

  group('Hunters sheet', () {
    testWidgets(
      'the main hunter of a confirmed booking can add and remove hunters',
      (tester) async {
        final service = FakeBookingService(
          roster(canManage: true, callerIsMain: true),
        );
        await openSheet(tester, service);

        expect(find.text('Voeg jagter by'), findsOneWidget);
        await tester.enterText(
          find.widgetWithText(TextFormField, 'Naam en van'),
          'Koos Kok',
        );
        await tester.enterText(
          find.widgetWithText(TextFormField, 'E-posadres'),
          'koos@x.co',
        );
        await tester.tap(find.text('Voeg jagter by'));
        await tester.pumpAndSettle();
        expect(service.added, ['Koos Kok <koos@x.co>']);
        expect(find.text('Uitnodiging gestuur.'), findsOneWidget);

        await tester.tap(find.byTooltip('Verwyder'));
        await tester.pumpAndSettle();
        await tester.tap(find.widgetWithText(TextButton, 'Verwyder'));
        await tester.pumpAndSettle();
        expect(service.removed, [7]);
      },
    );

    testWidgets('an invalid email is caught before calling the server', (
      tester,
    ) async {
      final service = FakeBookingService(
        roster(canManage: true, callerIsMain: true),
      );
      await openSheet(tester, service);
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Naam en van'),
        'Koos',
      );
      await tester.enterText(
        find.widgetWithText(TextFormField, 'E-posadres'),
        'koos-at-x',
      );
      await tester.tap(find.text('Voeg jagter by'));
      await tester.pumpAndSettle();
      expect(service.added, isEmpty);
      expect(find.text('Voer ’n geldige e-posadres in.'), findsOneWidget);
    });

    testWidgets('before confirmation the main hunter is told to wait', (
      tester,
    ) async {
      await openSheet(
        tester,
        FakeBookingService(roster(canManage: false, callerIsMain: true)),
      );
      expect(find.text('Voeg jagter by'), findsNothing);
      expect(
        find.text(
          'Jy kan die ander jagters byvoeg sodra die bespreking bevestig is.',
        ),
        findsOneWidget,
      );
    });

    testWidgets('an added hunter sees the roster and can only leave', (
      tester,
    ) async {
      final service = FakeBookingService(
        roster(canManage: false, callerIsMain: false, youAreCompanion: true),
      );
      await openSheet(tester, service);
      expect(find.text('Voeg jagter by'), findsNothing);
      expect(find.byTooltip('Verwyder'), findsNothing);
      expect(find.text('Sarel Seun (jy)'), findsOneWidget);

      await tester.tap(find.byTooltip('Verlaat jag'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(TextButton, 'Verlaat'));
      await tester.pumpAndSettle();
      expect(service.removed, [7]);
      expect(find.text('Sarel Seun (jy)'), findsNothing); // sheet closed
    });
  });
}
