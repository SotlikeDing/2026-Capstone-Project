import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/models/smart_waypoint.dart';
import 'package:jagbestuur_app/screens/hike_recap_screen.dart';
import 'package:jagbestuur_app/services/hunt_media_service.dart';
import 'package:jagbestuur_app/services/video_storage_service.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:video_player_platform_interface/video_player_platform_interface.dart';

class _FakeGalleryGateway implements GalleryGateway {
  bool wrote = false;

  @override
  Future<bool> hasAccess() async => true;

  @override
  Future<bool> requestAccess() async => true;

  @override
  Future<void> putVideo(String path) async {
    wrote = true;
  }
}

class _FakeVideoStorageService extends VideoStorageService {
  _FakeVideoStorageService({
    List<SavedRecapVideo> videos = const [],
    this.onGallerySave,
  }) : videos = List<SavedRecapVideo>.of(videos),
       super(testingEnvironmentOverride: true);

  final List<SavedRecapVideo> videos;
  final Future<void> Function(String path)? onGallerySave;

  @override
  Future<List<SavedRecapVideo>> listVideos() async =>
      List<SavedRecapVideo>.of(videos);

  @override
  Future<GallerySaveResult> saveVideoToGallery(SavedRecapVideo video) async {
    await onGallerySave?.call(video.path);
    return GallerySaveResult.saved;
  }
}

class _FakeVideoPlayerPlatform extends VideoPlayerPlatform {
  int _nextPlayerId = 0;

  @override
  Future<void> init() async {}

  @override
  Future<int?> createWithOptions(VideoCreationOptions options) async {
    return _nextPlayerId++;
  }

  @override
  Stream<VideoEvent> videoEventsFor(int playerId) => Stream<VideoEvent>.value(
    VideoEvent(
      eventType: VideoEventType.initialized,
      size: const Size(1280, 720),
      duration: const Duration(seconds: 12),
    ),
  );

  @override
  Future<void> setLooping(int playerId, bool looping) async {}

  @override
  Future<void> setVolume(int playerId, double volume) async {}

  @override
  Future<void> setPlaybackSpeed(int playerId, double speed) async {}

  @override
  Future<void> seekTo(int playerId, Duration position) async {}

  @override
  Future<Duration> getPosition(int playerId) async => Duration.zero;

  @override
  Future<void> play(int playerId) async {}

  @override
  Future<void> pause(int playerId) async {}

  @override
  Future<void> dispose(int playerId) async {}

  @override
  Widget buildViewWithOptions(VideoViewOptions options) =>
      const SizedBox.shrink();
}

SavedRecapVideo _savedVideoForSession(String sessionId) => SavedRecapVideo(
  id: 'recap-$sessionId',
  path: 'unused.mp4',
  title: 'Jag-terugblik-video',
  createdAt: DateTime(2026, 6, 1),
  sessionId: sessionId,
  durationSeconds: 12,
);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const pathProviderChannel = MethodChannel('plugins.flutter.io/path_provider');

  late Directory tempDir;
  late _FakeGalleryGateway fakeGallery;
  late VideoStorageService storage;
  late VideoPlayerPlatform previousVideoPlayerPlatform;

  final start = DateTime(2026, 6, 1, 7);
  final route = [
    for (var i = 0; i < 5; i++)
      SmartWaypoint(
        latitude: -25.0 + i * 0.001,
        longitude: 28.0 + i * 0.001,
        timestamp: start.add(Duration(minutes: i * 3)),
      ),
  ];

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    previousVideoPlayerPlatform = VideoPlayerPlatform.instance;
    VideoPlayerPlatform.instance = _FakeVideoPlayerPlatform();
    tempDir = await Directory.systemTemp.createTemp('recap_flow_test_');
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(pathProviderChannel, (call) async {
          switch (call.method) {
            case 'getApplicationCacheDirectory':
            case 'getApplicationDocumentsDirectory':
            case 'getApplicationSupportDirectory':
              return tempDir.path;
            default:
              throw MissingPluginException(
                'Unmocked path_provider method: ${call.method}',
              );
          }
        });
    fakeGallery = _FakeGalleryGateway();
    storage = VideoStorageService(
      rootOverride: tempDir,
      galleryGateway: fakeGallery,
      testingEnvironmentOverride: false,
    );
  });

  tearDown(() async {
    VideoPlayerPlatform.instance = previousVideoPlayerPlatform;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(pathProviderChannel, null);
    try {
      if (await tempDir.exists()) {
        await tempDir.delete(recursive: true);
      }
    } catch (_) {}
  });

  Future<File> createMockMp4(String name) async {
    final file = File('${tempDir.path}/$name');
    await file.writeAsBytes(<int>[
      0, 0, 0, 12, 0x66, 0x74, 0x79, 0x70, // 'ftyp'
      0, 0, 0, 0,
    ]);
    return file;
  }

  test(
    'VideoStorageService saves and retrieves recap video tagged with sessionId',
    () async {
      final mockMp4 = await createMockMp4('generated_recap.mp4');
      final saved = await storage.saveGeneratedVideo(
        generatedFile: mockMp4,
        durationSeconds: 15,
        sessionId: 'hunt-101',
        trophyId: 42,
        width: 1280,
        height: 720,
        quality: 'hd',
      );

      expect(saved.sessionId, 'hunt-101');
      expect(saved.trophyId, 42);
      expect(saved.durationSeconds, 15);
      expect(saved.resolution, '1280x720');

      final list = await storage.listVideos();
      expect(list.length, 1);
      expect(list.first.id, saved.id);
      expect(list.first.sessionId, 'hunt-101');
    },
  );

  testWidgets(
    'HikeRecapScreen does not display video action buttons if no video exists',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: HikeRecapScreen(
            keyframes: route,
            sessionId: 'hunt-101',
            videoStorageOverride: storage,
            mediaStorageOverride: HuntMediaService(rootOverride: tempDir),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.play_circle_fill_rounded), findsNothing);
      expect(find.text('Kyk terugblik-video'), findsNothing);

      await tester.pumpWidget(const SizedBox.shrink());
    },
  );

  testWidgets(
    'HikeRecapScreen discovers existing video and shows play buttons',
    (tester) async {
      final videoStorage = _FakeVideoStorageService(
        videos: [_savedVideoForSession('hunt-101')],
      );

      await tester.pumpWidget(
        MaterialApp(
          home: HikeRecapScreen(
            keyframes: route,
            sessionId: 'hunt-101',
            videoStorageOverride: videoStorage,
            mediaStorageOverride: HuntMediaService(rootOverride: tempDir),
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Verify both the AppBar action and bottom control button appear
      expect(
        find.byIcon(Icons.play_circle_fill_rounded),
        findsAtLeastNWidgets(1),
      );
      expect(find.text('Kyk terugblik-video'), findsOneWidget);
      expect(find.byTooltip('Kyk video'), findsOneWidget);
    },
  );

  testWidgets('HikeRecapScreen reacts dynamically when a new video is saved', (
    tester,
  ) async {
    final videoStorage = _FakeVideoStorageService();
    await tester.pumpWidget(
      MaterialApp(
        home: HikeRecapScreen(
          keyframes: route,
          sessionId: 'hunt-dynamic',
          videoStorageOverride: videoStorage,
          mediaStorageOverride: HuntMediaService(rootOverride: tempDir),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Kyk terugblik-video'), findsNothing);

    // Save a video for this session externally while the screen is open
    videoStorage.videos.add(_savedVideoForSession('hunt-dynamic'));
    VideoStorageService.libraryVersion.value++;
    await tester.pumpAndSettle();

    // The screen must now reactively show the video button!
    expect(find.text('Kyk terugblik-video'), findsOneWidget);
  });

  testWidgets(
    'RecapVideoPlayerScreen displays video screen and handles gallery consent and saving',
    (tester) async {
      final videoStorage = _FakeVideoStorageService(
        onGallerySave: (_) async => fakeGallery.wrote = true,
      );
      final savedVideo = _savedVideoForSession('hunt-player');

      await tester.pumpWidget(
        MaterialApp(
          home: RecapVideoPlayerScreen(
            video: savedVideo,
            storageOverride: videoStorage,
          ),
        ),
      );
      // Flush controller initialization without settling its playback timer.
      await tester.pump();
      await tester.pump();
      await tester.pump();

      // Verify AppBar title and gallery save button are present
      expect(find.text('Terugblik-video'), findsOneWidget);
      expect(find.text('Stoor in galery'), findsOneWidget);

      // Tap "Stoor in galery" to test permission flow
      await tester.tap(find.text('Stoor in galery'));
      await tester.pump();

      // Verify consent dialog is displayed with Afrikaans text
      expect(find.text('Stoor in jou galery?'), findsOneWidget);
      expect(find.text('Gee toestemming'), findsOneWidget);
      expect(find.text('Nie nou nie'), findsOneWidget);

      // Grant consent
      await tester.tap(find.text('Gee toestemming'));
      await tester.pump();
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 100)),
      );
      await tester.pump(const Duration(milliseconds: 500));

      // Fake gallery should now have written the video
      expect(fakeGallery.wrote, isTrue);

      // Verify success snackbar
      expect(find.text('Die video is in jou galery gestoor.'), findsOneWidget);
    },
  );

  testWidgets('Tapping Kyk terugblik-video opens RecapVideoPlayerScreen', (
    tester,
  ) async {
    final videoStorage = _FakeVideoStorageService(
      videos: [_savedVideoForSession('hunt-nav')],
    );

    await tester.pumpWidget(
      MaterialApp(
        home: HikeRecapScreen(
          keyframes: route,
          sessionId: 'hunt-nav',
          videoStorageOverride: videoStorage,
          mediaStorageOverride: HuntMediaService(rootOverride: tempDir),
        ),
      ),
    );
    await tester.pumpAndSettle();

    // Tap "Kyk terugblik-video"
    await tester.tap(find.text('Kyk terugblik-video'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));

    // RecapVideoPlayerScreen should now be on screen
    expect(find.text('Terugblik-video'), findsOneWidget);
  });
}
