import 'dart:io';
import 'dart:ui' as ui;

import 'package:ffmpeg_kit_flutter_new/ffmpeg_kit.dart';
import 'package:ffmpeg_kit_flutter_new/return_code.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/widgets.dart';
import 'package:storage_space/storage_space.dart';

import '../config/safety_config.dart';
import 'user_storage.dart';

class VideoExportPlan {
  const VideoExportPlan({
    required this.frameCount,
    required this.durationSeconds,
    required this.estimatedTemporaryBytes,
    required this.estimatedOutputBytes,
  });

  final int frameCount;
  final int durationSeconds;
  final int estimatedTemporaryBytes;
  final int estimatedOutputBytes;
}

class VideoExportCancellationToken {
  bool _isCancelled = false;

  bool get isCancelled => _isCancelled;

  void cancel() {
    _isCancelled = true;
  }

  void throwIfCancelled() {
    if (_isCancelled) {
      throw VideoExportException('Video-uitvoer is gekanselleer.');
    }
  }
}

/// Handles capturing widget frames as PNGs and encoding them into a local
/// .mp4 file using FFmpeg. Fully offline, no cloud dependency, no OS-level
/// screen recording permissions required.
class VideoExportService {
  VideoExportService._();

  /// High enough for the map to glide rather than jump between points.
  static const int _framesPerSecond = 20;
  static String? _activeExportId;

  static int get framesPerSecond => _framesPerSecond;
  static bool get isExportActive => _activeExportId != null;

  static String beginExclusiveExport() {
    if (_activeExportId != null) {
      throw VideoExportException('’n Ander video word reeds gegenereer.');
    }
    final id = DateTime.now().microsecondsSinceEpoch.toString();
    _activeExportId = id;
    return id;
  }

  static void finishExclusiveExport(String id) {
    if (_activeExportId == id) {
      _activeExportId = null;
    }
  }

  /// [uniqueFrameCount] is how many frames are actually drawn; the rest are
  /// links to an earlier frame and take no real disk space.
  static VideoExportPlan buildPlan({
    required int frameCount,
    required int photoCount,
    int? uniqueFrameCount,
    int? freeDiskBytes,
  }) {
    if (frameCount <= 0) {
      throw VideoExportException('Geen video-rame is beskikbaar nie.');
    }
    if (photoCount > HuntMediaLimits.maxPhotosPerVideo) {
      throw VideoExportException(
        'Kies hoogstens ${HuntMediaLimits.maxPhotosPerVideo} foto’s per video.',
      );
    }
    final durationSeconds = (frameCount / _framesPerSecond).ceil();
    if (durationSeconds > HuntMediaLimits.maxVideoDuration.inSeconds) {
      throw VideoExportException('Die beraamde video is langer as die limiet.');
    }
    final temporaryBytes =
        (uniqueFrameCount ?? frameCount) *
        HuntMediaLimits.estimatedPngFrameBytes;
    final outputBytes =
        (durationSeconds * HuntMediaLimits.estimatedVideoBitsPerSecond) ~/ 8;
    if (temporaryBytes > HuntMediaLimits.maxTemporaryExportBytes) {
      throw VideoExportException(
        'Die beraamde tydelike rame is groter as die uitvoerlimiet.',
      );
    }
    if (outputBytes > HuntMediaLimits.maxGeneratedVideoBytes) {
      throw VideoExportException('Die beraamde video is groter as die limiet.');
    }
    if (freeDiskBytes != null) {
      final required =
          temporaryBytes + outputBytes + HuntMediaLimits.minimumFreeDiskBytes;
      if (freeDiskBytes < required) {
        throw VideoExportException(
          'Daar is nie genoeg vrye berging om die video veilig te voltooi nie.',
        );
      }
    }
    return VideoExportPlan(
      frameCount: frameCount,
      durationSeconds: durationSeconds,
      estimatedTemporaryBytes: temporaryBytes,
      estimatedOutputBytes: outputBytes,
    );
  }

  static Future<VideoExportPlan> validatePreflight({
    required int frameCount,
    required int photoCount,
    int? uniqueFrameCount,
  }) async {
    final storage = await getStorageSpace(
      lowOnSpaceThreshold: HuntMediaLimits.minimumFreeDiskBytes,
      fractionDigits: 1,
    );
    return buildPlan(
      frameCount: frameCount,
      photoCount: photoCount,
      uniqueFrameCount: uniqueFrameCount,
      freeDiskBytes: storage.free,
    );
  }

  /// Captures a single frame from the given [boundaryKey]'s RepaintBoundary
  /// and writes it to [framesDir] as a zero-padded PNG (frame_0001.png, ...).
  ///
  /// Returns the written file, or null if the boundary isn't ready yet.
  static Future<File?> captureFrame({
    required GlobalKey boundaryKey,
    required Directory framesDir,
    required int frameIndex,
    double pixelRatio = 1.5,
  }) async {
    final renderObject = boundaryKey.currentContext?.findRenderObject();
    if (renderObject is! RenderRepaintBoundary) return null;

    final ui.Image image = await renderObject.toImage(pixelRatio: pixelRatio);
    final ByteData? byteData;
    try {
      byteData = await image.toByteData(format: ui.ImageByteFormat.png);
    } finally {
      // Release the native image even if encoding throws.
      image.dispose();
    }

    if (byteData == null) return null;

    final file = File(
      '${framesDir.path}${Platform.pathSeparator}${frameFileName(frameIndex)}',
    );

    // flush: true so the frame is fully on disk before FFmpeg reads it.
    await file.writeAsBytes(byteData.buffer.asUint8List(), flush: true);
    return file;
  }

  static String frameFileName(int frameIndex) =>
      'frame_${frameIndex.toString().padLeft(5, '0')}.png';

  /// Makes frame [targetIndex] show the same image as [sourceIndex]. A
  /// symbolic link costs no disk space; a copy is the fallback.
  static Future<void> repeatFrame({
    required Directory framesDir,
    required int sourceIndex,
    required int targetIndex,
  }) async {
    if (sourceIndex == targetIndex) {
      return;
    }
    final sep = Platform.pathSeparator;
    final targetPath = '${framesDir.path}$sep${frameFileName(targetIndex)}';
    try {
      await Link(targetPath).create(frameFileName(sourceIndex));
    } catch (_) {
      await File(
        '${framesDir.path}$sep${frameFileName(sourceIndex)}',
      ).copy(targetPath);
    }
  }

  /// Creates a fresh temp directory to hold captured frames for this session.
  static Future<Directory> createFramesDirectory() async {
    final tempDir = await UserStorage.temporaryDir('recap_exports');
    final sessionDir = Directory(
      '${tempDir.path}${Platform.pathSeparator}export_${DateTime.now().millisecondsSinceEpoch}',
    );
    if (!sessionDir.existsSync()) {
      await sessionDir.create(recursive: true);
    }
    return sessionDir;
  }

  /// Encodes all frame_*.png files inside [framesDir] into an .mp4 in the
  /// app's temporary directory. The caller moves it to its final home, so a
  /// failed or abandoned export never leaves a stray video in Documents.
  ///
  /// Only returns once the FFmpeg session has finished and the output file is
  /// closed. Throws a [VideoExportException] if FFmpeg fails or is cancelled.
  static Future<File> encodeFramesToVideo({
    required Directory framesDir,
    int fps = _framesPerSecond,
  }) async {
    final outputFileName =
        'recap_pending_${DateTime.now().millisecondsSinceEpoch}.mp4';
    final outputFile = File(
      '${framesDir.parent.path}${Platform.pathSeparator}$outputFileName',
    );

    final inputPattern =
        '${framesDir.path}${Platform.pathSeparator}frame_%05d.png';

    // -y: overwrite, -framerate: input image sequence rate,
    // scale: yuv420p needs even dimensions, the captured widget may be odd,
    // -pix_fmt yuv420p: ensures broad player compatibility.
    final command =
        '-y -framerate $fps -i "$inputPattern" '
        '-vf "scale=trunc(iw/2)*2:trunc(ih/2)*2" '
        '-c:v mpeg4 -pix_fmt yuv420p -q:v 3 "${outputFile.path}"';

    final session = await FFmpegKit.execute(command);
    final returnCode = await session.getReturnCode();

    if (!ReturnCode.isSuccess(returnCode)) {
      await deleteQuietly(outputFile);

      if (ReturnCode.isCancel(returnCode)) {
        throw VideoExportException('Video-uitvoer is gekanselleer.');
      }

      // FFmpeg logs can be huge: keep them in the console, not in the UI.
      final logs = await session.getAllLogsAsString();
      debugPrint('[VideoExportService] FFmpeg failed ($returnCode):\n$logs');
      throw VideoExportException(
        'FFmpeg-uitvoer het misluk (code: $returnCode).',
      );
    }

    if (!await outputFile.exists() || await outputFile.length() == 0) {
      await deleteQuietly(outputFile);
      throw VideoExportException('FFmpeg het geen video geskryf nie.');
    }

    return outputFile;
  }

  /// Stops any FFmpeg session that is still running, e.g. when the recap
  /// screen is closed in the middle of an export.
  static Future<void> cancelActiveEncoding() async {
    try {
      await FFmpegKit.cancel();
    } catch (e) {
      debugPrint('[VideoExportService] Could not cancel FFmpeg: $e');
    }
  }

  /// Deletes the temporary frames directory and all PNGs inside it.
  /// Never throws: cleanup must not turn a finished export into a failure.
  static Future<void> cleanupFrames(Directory framesDir) async {
    try {
      if (await framesDir.exists()) {
        await framesDir.delete(recursive: true);
      }
    } catch (e) {
      debugPrint(
        '[VideoExportService] Could not delete frames (${e.runtimeType}).',
      );
    }
  }

  /// Startup recovery removes only this user's known `export_*` directories
  /// and `recap_pending_*` files. Saved videos live in Documents and are never
  /// touched here.
  static Future<void> cleanupAbandonedExports() async {
    final root = await UserStorage.temporaryDir('recap_exports');
    await for (final entity in root.list(followLinks: false)) {
      final name = entity.path.replaceAll('\\', '/').split('/').last;
      try {
        if (entity is Directory && name.startsWith('export_')) {
          await entity.delete(recursive: true);
        } else if (entity is File &&
            name.startsWith('recap_pending_') &&
            name.endsWith('.mp4')) {
          await entity.delete();
        }
      } catch (error) {
        debugPrint(
          '[VideoExportService] Abandoned export cleanup failed: $error',
        );
      }
    }
  }

  /// Deletes [file] if it exists, swallowing IO errors.
  static Future<void> deleteQuietly(File? file) async {
    if (file == null) return;
    try {
      if (await file.exists()) {
        await file.delete();
      }
    } catch (e) {
      debugPrint(
        '[VideoExportService] Could not delete temporary file '
        '(${e.runtimeType}).',
      );
    }
  }
}

class VideoExportException implements Exception {
  VideoExportException(this.message);
  final String message;

  @override
  String toString() => 'VideoExportException: $message';
}
