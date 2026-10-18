import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/config/safety_config.dart';
import 'package:jagbestuur_app/services/video_export_service.dart';
import 'package:jagbestuur_app/services/video_storage_service.dart';

class _FakeGallery implements GalleryGateway {
  _FakeGallery({
    required this.hasPermission,
    required this.grantsPermission,
    this.fail = false,
  });

  final bool hasPermission;
  final bool grantsPermission;
  final bool fail;
  bool wrote = false;

  @override
  Future<bool> hasAccess() async => hasPermission;

  @override
  Future<bool> requestAccess() async => grantsPermission;

  @override
  Future<void> putVideo(String path) async {
    if (fail) {
      throw FileSystemException('gallery failed');
    }
    wrote = true;
  }
}

void main() {
  test(
    'preflight enforces photo, duration, temporary and free-space limits',
    () {
      expect(
        () => VideoExportService.buildPlan(
          frameCount: 80,
          photoCount: HuntMediaLimits.maxPhotosPerVideo + 1,
          freeDiskBytes: 10 * 1024 * 1024 * 1024,
        ),
        throwsA(isA<VideoExportException>()),
      );
      expect(
        () => VideoExportService.buildPlan(
          frameCount:
              (HuntMediaLimits.maxVideoDuration.inSeconds + 1) *
              VideoExportService.framesPerSecond,
          photoCount: 0,
          freeDiskBytes: 10 * 1024 * 1024 * 1024,
        ),
        throwsA(isA<VideoExportException>()),
      );
      expect(
        () => VideoExportService.buildPlan(
          frameCount: 80,
          photoCount: 0,
          freeDiskBytes: 1,
        ),
        throwsA(isA<VideoExportException>()),
      );
    },
  );

  test(
    'global export lease prevents concurrent exports and releases cleanly',
    () {
      final lease = VideoExportService.beginExclusiveExport();
      addTearDown(() => VideoExportService.finishExclusiveExport(lease));
      expect(
        VideoExportService.beginExclusiveExport,
        throwsA(isA<VideoExportException>()),
      );
      VideoExportService.finishExclusiveExport(lease);
      final next = VideoExportService.beginExclusiveExport();
      VideoExportService.finishExclusiveExport(next);
    },
  );

  test('export cancellation token stops frame work deterministically', () {
    final token = VideoExportCancellationToken();
    expect(token.isCancelled, isFalse);
    token.cancel();
    expect(token.isCancelled, isTrue);
    expect(token.throwIfCancelled, throwsA(isA<VideoExportException>()));
  });

  group('saved video validation and manifest recovery', () {
    late Directory root;

    setUp(() async {
      root = await Directory.systemTemp.createTemp('saved_video_validation');
    });

    tearDown(() async {
      await root.delete(recursive: true);
    });

    test('rejects invalid MP4 and actual files over the size limit', () async {
      final storage = VideoStorageService(rootOverride: root);
      final invalid = File('${root.path}/invalid.mp4')
        ..writeAsBytesSync(<int>[1, 2, 3, 4, 5, 6, 7, 8]);
      expect(
        () => storage.saveGeneratedVideo(
          generatedFile: invalid,
          durationSeconds: 1,
        ),
        throwsStateError,
      );

      final oversized = File('${root.path}/oversized.mp4');
      final handle = await oversized.open(mode: FileMode.write);
      await handle.writeFrom(<int>[0, 0, 0, 12, 0x66, 0x74, 0x79, 0x70]);
      await handle.truncate(HuntMediaLimits.maxGeneratedVideoBytes + 1);
      await handle.close();
      expect(
        () => storage.saveGeneratedVideo(
          generatedFile: oversized,
          durationSeconds: 1,
        ),
        throwsStateError,
      );
    });

    test(
      'stores valid metadata atomically and prunes a deleted file',
      () async {
        final storage = VideoStorageService(rootOverride: root);
        final encoded = File('${root.path}/encoded.mp4')
          ..writeAsBytesSync(<int>[
            0,
            0,
            0,
            12,
            0x66,
            0x74,
            0x79,
            0x70,
            0,
            0,
            0,
            0,
          ]);
        final saved = await storage.saveGeneratedVideo(
          generatedFile: encoded,
          durationSeconds: 2,
          sessionId: 'hunt-1',
          trophyId: 4,
          width: 1280,
          height: 720,
          quality: 'test',
        );
        expect(saved.sessionId, 'hunt-1');
        expect(saved.trophyId, 4);
        expect(saved.resolution, '1280x720');
        expect(await storage.listVideos(), hasLength(1));

        await File(saved.path).delete();
        expect(await storage.listVideos(), isEmpty);
      },
    );
  });

  group('explicit gallery permission outcomes', () {
    late Directory root;
    late File videoFile;
    late SavedRecapVideo video;

    setUp(() async {
      root = await Directory.systemTemp.createTemp('gallery_outcome');
      videoFile = File('${root.path}/video.mp4')
        ..writeAsBytesSync(<int>[1, 2, 3]);
      video = SavedRecapVideo(
        id: 'video-1',
        path: videoFile.path,
        title: 'Recap',
        createdAt: DateTime.now(),
        fileSizeBytes: 3,
        durationSeconds: 1,
        width: 100,
        height: 100,
        quality: 'test',
      );
    });

    tearDown(() async {
      await root.delete(recursive: true);
    });

    test('denied permission keeps the private file', () async {
      final gateway = _FakeGallery(
        hasPermission: false,
        grantsPermission: false,
      );
      final storage = VideoStorageService(
        rootOverride: root,
        galleryGateway: gateway,
        testingEnvironmentOverride: false,
      );
      expect(
        await storage.saveVideoToGallery(video),
        GallerySaveResult.permissionDenied,
      );
      expect(await videoFile.exists(), isTrue);
      expect(gateway.wrote, isFalse);
    });

    test('success streams by path and keeps the private file', () async {
      final gateway = _FakeGallery(
        hasPermission: true,
        grantsPermission: false,
      );
      final storage = VideoStorageService(
        rootOverride: root,
        galleryGateway: gateway,
        testingEnvironmentOverride: false,
      );
      expect(await storage.saveVideoToGallery(video), GallerySaveResult.saved);
      expect(await videoFile.exists(), isTrue);
      expect(gateway.wrote, isTrue);
    });

    test('gallery failure is reported without deleting private copy', () async {
      final storage = VideoStorageService(
        rootOverride: root,
        galleryGateway: _FakeGallery(
          hasPermission: true,
          grantsPermission: false,
          fail: true,
        ),
        testingEnvironmentOverride: false,
      );
      expect(await storage.saveVideoToGallery(video), GallerySaveResult.failed);
      expect(await videoFile.exists(), isTrue);
    });
  });
}
