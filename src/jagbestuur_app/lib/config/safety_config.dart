/// App-wide switches that keep test builds on physical devices contained.
library;

/// When true, generated recap videos stay inside the app's private temporary
/// directory and are never pushed to the public gallery.
///
/// Defaults to true so a build that forgets the flag stays sandboxed. Turn the
/// gallery export on for a real release with:
/// `flutter build apk --dart-define=IS_TESTING_ENV=false`
const bool isTestingEnv = bool.fromEnvironment(
  'IS_TESTING_ENV',
  defaultValue: true,
);

/// Largest payload (file, network body or decoded image) the app is allowed to
/// hold in memory in one piece. Anything bigger must be streamed or rejected.
const int maxInMemoryPayloadBytes = 20 * 1024 * 1024;

/// Upper bound for Flutter's decoded image cache. The default is 100 MB, which
/// is a lot on low-end devices that are also running FFmpeg and GPS tracking.
const int maxImageCacheBytes = 60 * 1024 * 1024;

/// Limits for private post-hunt media. They are centralized so production
/// builds can tune them without changing storage or UI code.
abstract final class HuntMediaLimits {
  static const int maxPhotoBytes = 15 * 1024 * 1024;
  static const int maxPhotosPerHunt = 100;
  static const int maxTotalPhotoBytes = 1024 * 1024 * 1024;
  static const int maxPhotoPixels = 50 * 1000 * 1000;

  static const int maxGeneratedVideoBytes = 300 * 1024 * 1024;
  static const Duration maxVideoDuration = Duration(minutes: 5);
  static const int maxPhotosPerVideo = 60;
  static const int minimumFreeDiskBytes = 500 * 1024 * 1024;
  static const int maxTemporaryExportBytes = 1536 * 1024 * 1024;

  /// Conservative estimates used before frame capture. Actual output is
  /// checked again before it is moved into permanent storage.
  static const int estimatedPngFrameBytes = 700 * 1024;
  static const int estimatedVideoBitsPerSecond = 4 * 1000 * 1000;
}
