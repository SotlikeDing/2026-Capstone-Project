import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/models/hunt_photo.dart';
import 'package:jagbestuur_app/models/hunt_pin.dart';
import 'package:jagbestuur_app/models/hunt_session.dart';
import 'package:jagbestuur_app/models/smart_waypoint.dart';
import 'package:jagbestuur_app/services/hunt_photo_matcher.dart';

void main() {
  final start = DateTime(2026, 5, 10, 8);

  HuntSession session({List<HuntPin> pins = const <HuntPin>[]}) => HuntSession(
    id: 'hunt-1',
    startedAt: start,
    endedAt: start.add(const Duration(hours: 2)),
    totalDistanceMeters: 1000,
    waypoints: <SmartWaypoint>[
      SmartWaypoint(latitude: -25, longitude: 28, timestamp: start),
      SmartWaypoint(
        latitude: -25.001,
        longitude: 28.001,
        timestamp: start.add(const Duration(minutes: 30)),
      ),
    ],
    pins: pins,
  );

  HuntPhotoMetadata metadata({
    DateTime? capturedAt,
    double? latitude,
    double? longitude,
  }) => HuntPhotoMetadata(
    fileSizeBytes: 100,
    width: 10,
    height: 10,
    fileType: 'jpg',
    metadataAvailable:
        capturedAt != null || latitude != null || longitude != null,
    capturedAt: capturedAt,
    latitude: latitude,
    longitude: longitude,
  );

  const matcher = HuntPhotoMatcher();

  test('GPS matching takes priority and is high confidence nearby', () {
    final result = matcher.match(
      session(),
      metadata(
        capturedAt: start.add(const Duration(days: 2)),
        latitude: -25.00101,
        longitude: 28.00101,
      ),
    );
    expect(result.method, HuntPhotoMatchMethod.gps);
    expect(result.waypointIndex, 1);
    expect(result.confidence, HuntPhotoMatchConfidence.high);
    expect(result.needsConfirmation, isFalse);
  });

  test('timestamp matching works when GPS is absent', () {
    final result = matcher.match(
      session(),
      metadata(capturedAt: start.add(const Duration(minutes: 29))),
    );
    expect(result.method, HuntPhotoMatchMethod.timestamp);
    expect(result.waypointIndex, 1);
    expect(result.confidence, HuntPhotoMatchConfidence.high);
  });

  test('nearby pin is associated after timestamp matching', () {
    final pin = HuntPin(
      id: 'pin-1',
      type: HuntPinType.animalFound,
      latitude: -25.001,
      longitude: 28.001,
      timestamp: start.add(const Duration(minutes: 29)),
    );
    final result = matcher.match(
      session(pins: <HuntPin>[pin]),
      metadata(capturedAt: start.add(const Duration(minutes: 30))),
    );
    expect(result.method, HuntPhotoMatchMethod.pin);
    expect(result.pinId, 'pin-1');
  });

  test('missing or implausible metadata requires manual placement', () {
    expect(
      matcher.match(session(), metadata()).method,
      HuntPhotoMatchMethod.unmatched,
    );
    final wrongClock = matcher.match(
      session(),
      metadata(capturedAt: start.add(const Duration(days: 1))),
    );
    expect(wrongClock.method, HuntPhotoMatchMethod.unmatched);
    expect(wrongClock.needsConfirmation, isTrue);
  });

  test('manual override records adjusted time and confirmed waypoint', () {
    final now = DateTime(2026, 5, 10, 9);
    final photo = HuntPhoto(
      id: 'photo-1',
      sessionId: 'hunt-1',
      localPath: 'private.jpg',
      caption: '',
      sortOrder: 0,
      fileSizeBytes: 100,
      width: 10,
      height: 10,
      fileType: 'jpg',
      metadataAvailable: false,
      exifStripped: false,
      matchMethod: HuntPhotoMatchMethod.unmatched,
      matchConfidence: HuntPhotoMatchConfidence.none,
      needsConfirmation: true,
      createdAt: now,
      updatedAt: now,
    );
    final adjusted = photo.copyWith(
      matchedWaypointIndex: 1,
      adjustedTimestamp: start.add(const Duration(minutes: 30)),
      matchMethod: HuntPhotoMatchMethod.manual,
      matchConfidence: HuntPhotoMatchConfidence.high,
      needsConfirmation: false,
    );
    expect(adjusted.matchedWaypointIndex, 1);
    expect(adjusted.effectiveTimestamp, start.add(const Duration(minutes: 30)));
    expect(adjusted.needsConfirmation, isFalse);
  });
}
