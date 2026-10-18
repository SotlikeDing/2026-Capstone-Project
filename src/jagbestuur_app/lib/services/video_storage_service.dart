import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:gal/gal.dart';

import '../config/safety_config.dart';
import '../safety/payload_guard.dart';
import 'user_storage.dart';

/// Outcome of [VideoStorageService.saveVideoToGallery].
enum GallerySaveResult { saved, skippedTestingEnv, permissionDenied, failed }

abstract interface class GalleryGateway {
  Future<bool> hasAccess();
  Future<bool> requestAccess();
  Future<void> putVideo(String path);
}

class GalGalleryGateway implements GalleryGateway {
  const GalGalleryGateway();

  @override
  Future<bool> hasAccess() => Gal.hasAccess(toAlbum: true);

  @override
  Future<bool> requestAccess() => Gal.requestAccess(toAlbum: true);

  @override
  Future<void> putVideo(String path) => Gal.putVideo(path, album: 'Jagbestuur');
}

class SavedRecapVideo {
  const SavedRecapVideo({
    required this.id,
    required this.path,
    required this.title,
    required this.createdAt,
    this.fileSizeBytes = 0,
    this.durationSeconds = 0,
    this.width = 0,
    this.height = 0,
    this.quality = 'standaard',
    this.sessionId,
    this.trophyId,
    this.galleryExportedAt,
  });

  final String id;
  final String path;
  final String title;
  final DateTime createdAt;
  final String? sessionId;
  final int? trophyId;
  final int fileSizeBytes;
  final int durationSeconds;
  final int width;
  final int height;
  final String quality;
  final DateTime? galleryExportedAt;

  String get resolution =>
      width > 0 && height > 0 ? '${width}x$height' : quality;

  SavedRecapVideo copyWith({DateTime? galleryExportedAt}) => SavedRecapVideo(
    id: id,
    path: path,
    title: title,
    createdAt: createdAt,
    sessionId: sessionId,
    trophyId: trophyId,
    fileSizeBytes: fileSizeBytes,
    durationSeconds: durationSeconds,
    width: width,
    height: height,
    quality: quality,
    galleryExportedAt: galleryExportedAt ?? this.galleryExportedAt,
  );

  Map<String, dynamic> toJson() => {
    'id': id,
    'path': path,
    'title': title,
    'createdAt': createdAt.toIso8601String(),
    'sessionId': sessionId,
    'trophyId': trophyId,
    'fileSizeBytes': fileSizeBytes,
    'durationSeconds': durationSeconds,
    'width': width,
    'height': height,
    'quality': quality,
    'galleryExportedAt': galleryExportedAt?.toIso8601String(),
  };

  factory SavedRecapVideo.fromJson(Map<String, dynamic> json) {
    return SavedRecapVideo(
      id: json['id'] as String,
      path: json['path'] as String,
      title: json['title'] as String? ?? 'Jag-terugblik-video',
      createdAt: DateTime.parse(json['createdAt'] as String),
      sessionId: json['sessionId'] as String?,
      trophyId: (json['trophyId'] as num?)?.toInt(),
      fileSizeBytes: (json['fileSizeBytes'] as num?)?.toInt() ?? 0,
      durationSeconds: (json['durationSeconds'] as num?)?.toInt() ?? 0,
      width: (json['width'] as num?)?.toInt() ?? 0,
      height: (json['height'] as num?)?.toInt() ?? 0,
      quality: json['quality'] as String? ?? 'standaard',
      galleryExportedAt: DateTime.tryParse(
        json['galleryExportedAt'] as String? ?? '',
      ),
    );
  }
}

class VideoStorageService {
  const VideoStorageService({
    Directory? rootOverride,
    GalleryGateway galleryGateway = const GalGalleryGateway(),
    bool? testingEnvironmentOverride,
  }) : _rootOverride = rootOverride,
       _galleryGateway = galleryGateway,
       _testingEnvironmentOverride = testingEnvironmentOverride;

  static final ValueNotifier<int> libraryVersion = ValueNotifier<int>(0);

  final Directory? _rootOverride;
  final GalleryGateway _galleryGateway;
  final bool? _testingEnvironmentOverride;

  Future<List<SavedRecapVideo>> listVideos() async {
    final videos = await _readManifest();
    final existing = <SavedRecapVideo>[];
    var changed = false;

    for (final video in videos) {
      if (await File(video.path).exists()) {
        existing.add(video);
      } else {
        changed = true;
      }
    }

    existing.sort((a, b) => b.createdAt.compareTo(a.createdAt));

    if (changed) {
      await _writeManifest(existing, notify: false);
    }

    return existing;
  }

  Future<SavedRecapVideo> saveGeneratedVideo({
    required File generatedFile,
    required int durationSeconds,
    String? sessionId,
    int? trophyId,
    String? title,
    DateTime? createdAt,
    int width = 0,
    int height = 0,
    String quality = 'standaard',
  }) async {
    if (!await generatedFile.exists()) {
      throw StateError('Die gegenereerde video bestaan nie.');
    }

    final generatedLength = await generatedFile.length();
    if (generatedLength <= 0 ||
        generatedLength > HuntMediaLimits.maxGeneratedVideoBytes) {
      throw StateError('Die video is leeg of groter as die veilige limiet.');
    }
    if (durationSeconds <= 0 ||
        durationSeconds > HuntMediaLimits.maxVideoDuration.inSeconds) {
      throw StateError('Die video se duur is buite die veilige limiet.');
    }
    RandomAccessFile? handle;
    try {
      handle = await generatedFile.open(mode: FileMode.read);
      final header = await handle.read(
        generatedLength < 12 ? generatedLength : 12,
      );
      final hasMp4Header =
          header.length >= 8 &&
          header[4] == 0x66 &&
          header[5] == 0x74 &&
          header[6] == 0x79 &&
          header[7] == 0x70;
      if (!hasMp4Header) {
        throw StateError(
          'Die gegenereerde videolêer is nie ’n geldige MP4 nie.',
        );
      }
    } finally {
      await handle?.close();
    }

    final timestamp = createdAt ?? DateTime.now();
    final id = DateTime.now().microsecondsSinceEpoch.toString();
    final extension = _extensionFor(generatedFile.path);
    final videosDir = await _videosDirectory();
    final storedPath =
        '${videosDir.path}${Platform.pathSeparator}recap_$id$extension';

    final storedFile = await _moveFile(generatedFile, storedPath);

    if (!await storedFile.exists() ||
        await storedFile.length() != generatedLength) {
      throw StateError('Die private videokopie kon nie bevestig word nie.');
    }

    final savedVideo = SavedRecapVideo(
      id: id,
      path: storedFile.path,
      title: title ?? _defaultTitle(timestamp),
      createdAt: timestamp,
      sessionId: sessionId,
      trophyId: trophyId,
      fileSizeBytes: await storedFile.length(),
      durationSeconds: durationSeconds,
      width: width,
      height: height,
      quality: quality,
    );

    try {
      final videos = await _readManifest();
      videos.add(savedVideo);
      videos.sort((a, b) => b.createdAt.compareTo(a.createdAt));
      await _writeManifest(videos);
    } catch (_) {
      try {
        await storedFile.delete();
      } catch (_) {}
      rethrow;
    }

    return savedVideo;
  }

  Future<void> deleteVideo(String id) async {
    final videos = await _readManifest();
    final index = videos.indexWhere((video) => video.id == id);

    if (index == -1) {
      return;
    }

    final video = videos.removeAt(index);
    final file = File(video.path);

    if (await file.exists()) {
      await file.delete();
    }

    await _writeManifest(videos);
  }

  /// Removes every recap video generated from hunt session [sessionId].
  Future<void> deleteVideosForSession(String sessionId) async {
    final videos = await _readManifest();
    final keep = <SavedRecapVideo>[];
    var removed = false;

    for (final video in videos) {
      if (video.sessionId != sessionId) {
        keep.add(video);
        continue;
      }
      removed = true;
      try {
        final file = File(video.path);
        if (await file.exists()) {
          await file.delete();
        }
      } catch (e) {
        debugPrint(
          '[VideoStorageService] Could not delete private video '
          '(${e.runtimeType}).',
        );
      }
    }

    if (removed) {
      await _writeManifest(keep);
    }
  }

  Future<void> pruneMissingVideos() async {
    final videos = await _readManifest();
    final existing = <SavedRecapVideo>[];

    for (final video in videos) {
      if (await File(video.path).exists()) {
        existing.add(video);
      }
    }

    await _writeManifest(existing);
  }

  Future<List<SavedRecapVideo>> _readManifest() async {
    final manifest = await _manifestFile();

    if (!await manifest.exists()) {
      return <SavedRecapVideo>[];
    }

    try {
      final raw = await PayloadGuard.readString(manifest);
      if (raw.trim().isEmpty) {
        return <SavedRecapVideo>[];
      }

      final decoded = jsonDecode(raw);
      if (decoded is! List) {
        return <SavedRecapVideo>[];
      }

      return decoded
          .whereType<Map>()
          .map(
            (item) => SavedRecapVideo.fromJson(Map<String, dynamic>.from(item)),
          )
          .toList();
    } catch (_) {
      return <SavedRecapVideo>[];
    }
  }

  Future<void> _writeManifest(
    List<SavedRecapVideo> videos, {
    bool notify = true,
  }) async {
    final manifest = await _manifestFile();
    final encoded = jsonEncode(videos.map((video) => video.toJson()).toList());
    final temporary = File('${manifest.path}.tmp');
    await temporary.writeAsString(encoded, flush: true);
    try {
      await temporary.rename(manifest.path);
    } on FileSystemException {
      await temporary.copy(manifest.path);
      await temporary.delete();
    }

    if (notify) {
      libraryVersion.value++;
    }
  }

  Future<File> _manifestFile() async {
    final root = await _rootDirectory();
    return File('${root.path}${Platform.pathSeparator}videos.json');
  }

  Future<Directory> _videosDirectory() async {
    final root = await _rootDirectory();
    final dir = Directory('${root.path}${Platform.pathSeparator}files');

    if (!await dir.exists()) {
      await dir.create(recursive: true);
    }

    return dir;
  }

  /// Videos belong to the signed-in user and remain in permanent private app
  /// storage in every environment. Testing mode only suppresses gallery
  /// export; it must not make the app's own copy disposable.
  Future<Directory> _rootDirectory() => _rootOverride == null
      ? UserStorage.documentsDir('recap_videos')
      : _ensureOverrideRoot();

  Future<Directory> _ensureOverrideRoot() async {
    final root = _rootOverride!;
    if (!await root.exists()) {
      await root.create(recursive: true);
    }
    return root;
  }

  /// Moves [source] to [targetPath] so the intermediate encoder output does
  /// not pile up. Falls back to copy + delete across file systems.
  Future<File> _moveFile(File source, String targetPath) async {
    try {
      return await source.rename(targetPath);
    } on FileSystemException {
      final copied = await source.copy(targetPath);
      try {
        await source.delete();
      } catch (e) {
        debugPrint(
          '[VideoStorageService] Could not delete encoder output '
          '(${e.runtimeType}).',
        );
      }
      return copied;
    }
  }

  String _extensionFor(String path) {
    final dotIndex = path.lastIndexOf('.');
    if (dotIndex == -1) {
      return '.mp4';
    }

    return path.substring(dotIndex);
  }

  String _defaultTitle(DateTime timestamp) {
    String two(int value) => value.toString().padLeft(2, '0');

    return 'Jag-terugblik ${timestamp.year}-${two(timestamp.month)}-${two(timestamp.day)} '
        '${two(timestamp.hour)}:${two(timestamp.minute)}';
  }

  /// Hands the video to the public gallery by file path only. The file is
  /// never read into memory here; `gal` streams it from disk natively.
  ///
  /// Never throws. Returns [GallerySaveResult.skippedTestingEnv] without
  /// touching the gallery when [isTestingEnv] is true.
  Future<GallerySaveResult> saveVideoToGallery(SavedRecapVideo video) async {
    if (_testingEnvironmentOverride ?? isTestingEnv) {
      debugPrint('[VideoStorageService] isTestingEnv: gallery export skipped.');
      return GallerySaveResult.skippedTestingEnv;
    }

    try {
      if (!await File(video.path).exists()) {
        return GallerySaveResult.failed;
      }

      final hasAccess = await _galleryGateway.hasAccess();
      if (!hasAccess && !await _galleryGateway.requestAccess()) {
        return GallerySaveResult.permissionDenied;
      }

      await _galleryGateway.putVideo(video.path);
      await _markGalleryExported(video.id);
      return GallerySaveResult.saved;
    } on GalException catch (e, stack) {
      debugPrint('[VideoStorageService] Gallery save failed (${e.type}): $e');
      debugPrint(stack.toString());
      return e.type == GalExceptionType.accessDenied
          ? GallerySaveResult.permissionDenied
          : GallerySaveResult.failed;
    } catch (e, stack) {
      debugPrint('[VideoStorageService] Gallery save failed: $e');
      debugPrint(stack.toString());
      return GallerySaveResult.failed;
    }
  }

  Future<void> _markGalleryExported(String id) async {
    final videos = await _readManifest();
    final index = videos.indexWhere((video) => video.id == id);
    if (index == -1) {
      return;
    }
    videos[index] = videos[index].copyWith(galleryExportedAt: DateTime.now());
    await _writeManifest(videos);
  }
}
