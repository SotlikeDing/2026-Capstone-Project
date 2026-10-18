import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/config/safety_config.dart';
import 'package:jagbestuur_app/models/hunt_photo.dart';
import 'package:jagbestuur_app/models/hunt_session.dart';
import 'package:jagbestuur_app/models/smart_waypoint.dart';
import 'package:jagbestuur_app/services/hunt_media_service.dart';

void main() {
  late Directory root;
  late File source;
  late HuntSession session;

  setUp(() async {
    root = await Directory.systemTemp.createTemp('hunt_media_storage');
    source = File('${root.path}/source.jpg')
      ..writeAsBytesSync(<int>[1, 2, 3, 4], flush: true);
    final start = DateTime(2026, 6, 1, 8);
    session = HuntSession(
      id: 'session-1',
      startedAt: start,
      endedAt: start.add(const Duration(hours: 1)),
      totalDistanceMeters: 100,
      waypoints: <SmartWaypoint>[
        SmartWaypoint(latitude: -25, longitude: 28, timestamp: start),
      ],
    );
  });

  tearDown(() async {
    await root.delete(recursive: true);
  });

  HuntMediaService service() => HuntMediaService(
    rootOverride: root,
    metadataReader: (file) async => HuntPhotoMetadata(
      fileSizeBytes: await file.length(),
      width: 10,
      height: 10,
      fileType: 'jpg',
      metadataAvailable: false,
    ),
  );

  test('persists private file references across service restart', () async {
    final first = service();
    final imported = await first.importPhotos(
      session: session,
      sourcePaths: <String>[source.path],
      trophyId: 7,
    );
    expect(imported, hasLength(1));
    expect(await File(imported.single.localPath).exists(), isTrue);

    final afterRestart = await service().listPhotosForSession(session.id);
    expect(afterRestart.single.id, imported.single.id);
    expect(await service().linkedSessionIds(7), <String>[session.id]);
  });

  test('trophy deletion unlinks but preserves hunt and photo', () async {
    final media = service();
    final photo = (await media.importPhotos(
      session: session,
      sourcePaths: <String>[source.path],
      trophyId: 7,
    )).single;
    await media.unlinkDeletedTrophy(7);
    final remaining = await media.listPhotosForSession(session.id);
    expect(remaining.single.trophyId, isNull);
    expect(await File(photo.localPath).exists(), isTrue);
    expect(await media.linkedSessionIds(7), isEmpty);
  });

  test(
    'hunt deletion removes its photos while video-independent rules remain',
    () async {
      final media = service();
      final photo = (await media.importPhotos(
        session: session,
        sourcePaths: <String>[source.path],
      )).single;
      await media.deleteMediaForSession(session.id);
      expect(await File(photo.localPath).exists(), isFalse);
      expect(await media.listPhotosForSession(session.id), isEmpty);
    },
  );

  test(
    'repair removes missing-file entries and survives invalid records',
    () async {
      final media = service();
      final photo = (await media.importPhotos(
        session: session,
        sourcePaths: <String>[source.path],
      )).single;
      await File(photo.localPath).delete();
      await media.repairManifest();
      expect(await media.listPhotosForSession(session.id), isEmpty);

      final manifest = File('${root.path}/manifest.json');
      await manifest.writeAsString(
        jsonEncode(<String, dynamic>{
          'version': 1,
          'photos': <dynamic>[
            <String, dynamic>{'bad': true},
          ],
          'trophyLinks': <String, dynamic>{},
        }),
        flush: true,
      );
      expect(await media.listPhotosForSession(session.id), isEmpty);
    },
  );

  test('photo count and total storage limits refuse imports early', () async {
    final media = service();
    expect(
      () => media.importPhotos(
        session: session,
        sourcePaths: List<String>.filled(
          HuntMediaLimits.maxPhotosPerHunt + 1,
          'never-read.jpg',
        ),
      ),
      throwsA(isA<HuntMediaException>()),
    );

    final manifest = File('${root.path}/manifest.json');
    await manifest.writeAsString(
      jsonEncode(<String, dynamic>{
        'version': 1,
        'photos': <dynamic>[
          <String, dynamic>{
            'id': 'existing',
            'sessionId': 'other',
            'localPath': 'missing.jpg',
            'caption': '',
            'sortOrder': 0,
            'fileSizeBytes': HuntMediaLimits.maxTotalPhotoBytes,
            'width': 1,
            'height': 1,
            'fileType': 'jpg',
            'metadataAvailable': false,
            'exifStripped': false,
            'matchMethod': 'unmatched',
            'matchConfidence': 'none',
            'needsConfirmation': true,
            'createdAt': DateTime.now().toIso8601String(),
            'updatedAt': DateTime.now().toIso8601String(),
          },
        ],
        'trophyLinks': <String, dynamic>{},
      }),
      flush: true,
    );
    expect(
      () => media.importPhotos(
        session: session,
        sourcePaths: <String>[source.path],
      ),
      throwsA(isA<HuntMediaException>()),
    );
  });
}
