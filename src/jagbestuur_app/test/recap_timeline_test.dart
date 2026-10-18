import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/models/hunt_photo.dart';
import 'package:jagbestuur_app/models/hunt_pin.dart';
import 'package:jagbestuur_app/models/smart_waypoint.dart';
import 'package:jagbestuur_app/utils/recap_timeline.dart';

void main() {
  final start = DateTime(2026, 6, 1, 7);
  final route = [
    for (var i = 0; i < 20; i++)
      SmartWaypoint(
        latitude: -25.0 + i * 0.0005,
        longitude: 28.0,
        timestamp: start.add(Duration(minutes: i * 3)),
        isStop: i == 8,
        stopDuration: i == 8 ? const Duration(minutes: 12) : null,
      ),
  ];

  HuntPhoto photoAt(int index) => HuntPhoto(
    id: 'p$index',
    sessionId: 's',
    localPath: '/tmp/p$index.jpg',
    caption: 'Koedoe in die kloof',
    sortOrder: index,
    fileSizeBytes: 1,
    width: 1,
    height: 1,
    fileType: 'jpg',
    metadataAvailable: true,
    exifStripped: false,
    matchMethod: HuntPhotoMatchMethod.gps,
    matchConfidence: HuntPhotoMatchConfidence.high,
    needsConfirmation: false,
    createdAt: start,
    updatedAt: start,
    matchedWaypointIndex: index,
    originalCapturedAt: route[index].timestamp,
  );

  test('the route moves forward smoothly and never jumps backwards', () {
    final timeline = RecapTimeline.build(keyframes: route);
    var previous = -1.0;
    var biggestStep = 0.0;
    for (var i = 0; i < timeline.frameCount(20); i++) {
      final frame = timeline.frameAt(i / 20);
      if (frame.phase != RecapPhase.map) continue;
      expect(frame.position, greaterThanOrEqualTo(previous - 1e-9));
      if (previous >= 0) {
        final step = frame.position - previous;
        if (step > biggestStep) biggestStep = step;
      }
      previous = frame.position;
    }
    expect(previous, route.length - 1);
    // At 20 fps the marker never skips a whole route point in one frame.
    expect(biggestStep, lessThan(0.5));
  });

  test('plays slower than the old 0.375 s per route point', () {
    final timeline = RecapTimeline.build(keyframes: route);
    final travel = timeline.mapEndSeconds - timeline.mapStartSeconds;
    expect(travel / (route.length - 1), greaterThan(0.375));
  });

  test('captions say why a point was logged', () {
    final timeline = RecapTimeline.build(
      keyframes: route,
      pins: [
        HuntPin(
          id: 'auto',
          type: HuntPinType.shotFired,
          latitude: route[4].latitude,
          longitude: 28,
          timestamp: route[4].timestamp,
          autoDetected: true,
          confidence: 0.87,
        ),
        HuntPin(
          id: 'manual',
          type: HuntPinType.animalFound,
          latitude: route[12].latitude,
          longitude: 28,
          timestamp: route[12].timestamp,
        ),
      ],
    );
    final captions = <String>{};
    for (var i = 0; i < timeline.frameCount(10); i++) {
      final caption = timeline.frameAt(i / 10).caption;
      if (caption != null) captions.add('${caption.title}: ${caption.detail}');
    }
    expect(captions.any((c) => c.startsWith('Die jag begin')), isTrue);
    expect(
      captions.any(
        (c) => c.startsWith('Moontlike skoot') && c.contains('87% seker'),
      ),
      isTrue,
    );
    expect(
      captions.any((c) => c.startsWith('Dier gevind') && c.contains('Jy het')),
      isTrue,
    );
    expect(
      captions.any((c) => c.startsWith('Ruspunt') && c.contains('12 minute')),
      isTrue,
    );
    expect(captions.any((c) => c.startsWith('Einde van die jag')), isTrue);
  });

  test('a photo is held full screen and fades in and out', () {
    final timeline = RecapTimeline.build(
      keyframes: route,
      photos: [photoAt(6)],
    );
    final at = timeline.secondsForPhoto('p6')!;
    expect(timeline.frameAt(at).photoOpacity, 0);
    final middle = timeline.frameAt(at + 2);
    expect(middle.photo?.id, 'p6');
    expect(middle.photoOpacity, 1);
    expect(middle.caption?.title, 'Koedoe in die kloof');
    expect(middle.caption?.detail, contains('GPS-ligging'));
  });

  test('still moments are drawn once and repeated', () {
    final timeline = RecapTimeline.build(
      keyframes: route,
      photos: [photoAt(3), photoAt(10)],
    );
    expect(
      timeline.uniqueFrameCount(20),
      lessThan(timeline.frameCount(20) * 0.8),
    );
  });
}
