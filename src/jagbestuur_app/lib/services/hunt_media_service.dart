import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';

import '../config/safety_config.dart';
import '../models/hunt_photo.dart';
import '../models/hunt_session.dart';
import '../safety/payload_guard.dart';
import 'hunt_photo_matcher.dart';
import 'hunt_photo_metadata_service.dart';
import 'user_storage.dart';

typedef HuntPhotoMetadataReader = Future<HuntPhotoMetadata> Function(File file);

class HuntMediaException implements Exception {
  const HuntMediaException(this.message);
  final String message;

  @override
  String toString() => message;
}

class _HuntMediaManifest {
  const _HuntMediaManifest({required this.photos, required this.trophyLinks});

  final List<HuntPhoto> photos;
  final Map<int, List<String>> trophyLinks;

  Map<String, dynamic> toJson() => <String, dynamic>{
    'version': 1,
    'photos': photos.map((photo) => photo.toJson()).toList(growable: false),
    'trophyLinks': trophyLinks.map(
      (key, value) => MapEntry(key.toString(), value),
    ),
  };
}

/// Owns private hunt-photo files, their atomic manifest, and local
/// trophy-to-hunt relationships. It never performs a network request.
class HuntMediaService {
  HuntMediaService({
    Directory? rootOverride,
    HuntPhotoMetadataReader? metadataReader,
    HuntPhotoMatcher matcher = const HuntPhotoMatcher(),
  }) : _rootOverride = rootOverride,
       _metadataReader =
           metadataReader ?? const HuntPhotoMetadataService().extract,
       _matcher = matcher;

  static final ValueNotifier<int> mediaVersion = ValueNotifier<int>(0);
  static Future<void> _mutationTail = Future<void>.value();

  final Directory? _rootOverride;
  final HuntPhotoMetadataReader _metadataReader;
  final HuntPhotoMatcher _matcher;

  Future<List<HuntPhoto>> listPhotosForSession(String sessionId) async {
    final manifest = await _readManifest();
    final result = manifest.photos
        .where((photo) => photo.sessionId == sessionId)
        .toList();
    result.sort((first, second) {
      final order = first.sortOrder.compareTo(second.sortOrder);
      if (order != 0) {
        return order;
      }
      return first.createdAt.compareTo(second.createdAt);
    });
    return result;
  }

  Future<List<HuntPhoto>> listPhotosForTrophy(int trophyId) async {
    final manifest = await _readManifest();
    final sessions = manifest.trophyLinks[trophyId]?.toSet() ?? <String>{};
    final result = manifest.photos
        .where(
          (photo) =>
              photo.trophyId == trophyId || sessions.contains(photo.sessionId),
        )
        .toList();
    result.sort((a, b) => a.sortOrder.compareTo(b.sortOrder));
    return result;
  }

  Future<List<String>> linkedSessionIds(int trophyId) async {
    final manifest = await _readManifest();
    return List<String>.unmodifiable(
      manifest.trophyLinks[trophyId] ?? const <String>[],
    );
  }

  Future<void> linkTrophyToSession(int trophyId, String sessionId) {
    return _mutate((manifest) {
      final links = manifest.trophyLinks.putIfAbsent(
        trophyId,
        () => <String>[],
      );
      if (!links.contains(sessionId)) {
        links.add(sessionId);
      }
    });
  }

  Future<void> unlinkTrophyFromSession(int trophyId, String sessionId) {
    return _mutate((manifest) {
      manifest.trophyLinks[trophyId]?.remove(sessionId);
      if (manifest.trophyLinks[trophyId]?.isEmpty == true) {
        manifest.trophyLinks.remove(trophyId);
      }
      for (var index = 0; index < manifest.photos.length; index++) {
        final photo = manifest.photos[index];
        if (photo.sessionId == sessionId && photo.trophyId == trophyId) {
          manifest.photos[index] = photo.copyWith(clearTrophyId: true);
        }
      }
    });
  }

  /// Removes only the relationship. Hunt sessions and photo files remain.
  Future<void> unlinkDeletedTrophy(int trophyId) {
    return _mutate((manifest) {
      manifest.trophyLinks.remove(trophyId);
      for (var index = 0; index < manifest.photos.length; index++) {
        final photo = manifest.photos[index];
        if (photo.trophyId == trophyId) {
          manifest.photos[index] = photo.copyWith(clearTrophyId: true);
        }
      }
    });
  }

  /// Imports one photo at a time so selected full-resolution files are never
  /// all resident in memory together. The operation is transactional: if one
  /// file fails, staged private copies are removed and the manifest is intact.
  Future<List<HuntPhoto>> importPhotos({
    required HuntSession session,
    required List<String> sourcePaths,
    int? trophyId,
  }) async {
    if (sourcePaths.isEmpty) {
      return const <HuntPhoto>[];
    }

    final completion = Completer<List<HuntPhoto>>();
    _mutationTail = _mutationTail.then((_) async {
      final stagedFiles = <File>[];
      try {
        final manifest = await _readManifest();
        final existing = manifest.photos
            .where((photo) => photo.sessionId == session.id)
            .length;
        if (existing + sourcePaths.length > HuntMediaLimits.maxPhotosPerHunt) {
          throw const HuntMediaException(
            'Hierdie jag het die maksimum aantal foto’s bereik.',
          );
        }

        var totalBytes = manifest.photos.fold<int>(
          0,
          (total, photo) => total + photo.fileSizeBytes,
        );
        final nextOrder = existing;
        final imported = <HuntPhoto>[];
        final sessionDir = await _sessionPhotosDirectory(session.id);

        for (var index = 0; index < sourcePaths.length; index++) {
          final source = File(sourcePaths[index]);
          final metadata = await _metadataReader(source);
          totalBytes += metadata.fileSizeBytes;
          if (totalBytes > HuntMediaLimits.maxTotalPhotoBytes) {
            throw const HuntMediaException(
              'Jou totale private jagfoto-berging het sy limiet bereik.',
            );
          }

          final now = DateTime.now();
          final id = '${now.microsecondsSinceEpoch}_$index';
          final target = File(
            '${sessionDir.path}${Platform.pathSeparator}$id.${metadata.fileType}',
          );
          await source.openRead().pipe(target.openWrite());
          stagedFiles.add(target);
          if (!await target.exists() ||
              await target.length() != metadata.fileSizeBytes) {
            throw const HuntMediaException(
              'Die private fotokopie kon nie bevestig word nie.',
            );
          }

          final match = _matcher.match(session, metadata);
          imported.add(
            HuntPhoto(
              id: id,
              sessionId: session.id,
              trophyId: trophyId,
              localPath: target.path,
              originalCapturedAt: metadata.capturedAt,
              latitude: metadata.latitude,
              longitude: metadata.longitude,
              orientation: metadata.orientation,
              matchedWaypointIndex: match.waypointIndex,
              matchedPinId: match.pinId,
              caption: '',
              sortOrder: nextOrder + index,
              fileSizeBytes: metadata.fileSizeBytes,
              width: metadata.width,
              height: metadata.height,
              fileType: metadata.fileType,
              metadataAvailable: metadata.metadataAvailable,
              // The original is kept privately so matching can be corrected.
              // Exported video frames are rendered copies with no EXIF.
              exifStripped: false,
              matchMethod: match.method,
              matchConfidence: match.confidence,
              needsConfirmation: match.needsConfirmation,
              createdAt: now,
              updatedAt: now,
            ),
          );
        }

        manifest.photos.addAll(imported);
        if (trophyId != null) {
          final links = manifest.trophyLinks.putIfAbsent(
            trophyId,
            () => <String>[],
          );
          if (!links.contains(session.id)) {
            links.add(session.id);
          }
        }
        await _writeManifest(manifest);
        completion.complete(List<HuntPhoto>.unmodifiable(imported));
      } catch (error, stack) {
        for (final file in stagedFiles) {
          try {
            if (await file.exists()) {
              await file.delete();
            }
          } catch (_) {}
        }
        completion.completeError(error, stack);
      }
    });
    return completion.future;
  }

  Future<void> updatePhoto(HuntPhoto updated) {
    return _mutate((manifest) {
      final index = manifest.photos.indexWhere(
        (photo) => photo.id == updated.id,
      );
      if (index == -1) {
        throw const HuntMediaException('Die jagfoto bestaan nie meer nie.');
      }
      manifest.photos[index] = updated.copyWith(updatedAt: DateTime.now());
    });
  }

  Future<void> deletePhoto(String photoId) async {
    File? file;
    await _mutate((manifest) {
      final index = manifest.photos.indexWhere((photo) => photo.id == photoId);
      if (index == -1) {
        return;
      }
      file = File(manifest.photos.removeAt(index).localPath);
    });
    await _deleteFileQuietly(file);
  }

  /// Hunt deletion owns its photos. Videos are deleted by VideoStorageService.
  Future<void> deleteMediaForSession(String sessionId) async {
    final files = <File>[];
    await _mutate((manifest) {
      for (final photo in manifest.photos) {
        if (photo.sessionId == sessionId) {
          files.add(File(photo.localPath));
        }
      }
      manifest.photos.removeWhere((photo) => photo.sessionId == sessionId);
      for (final links in manifest.trophyLinks.values) {
        links.remove(sessionId);
      }
      manifest.trophyLinks.removeWhere((_, links) => links.isEmpty);
    });
    for (final file in files) {
      await _deleteFileQuietly(file);
    }
    final dir = await _sessionPhotosDirectory(sessionId, create: false);
    if (await dir.exists()) {
      try {
        await dir.delete(recursive: true);
      } catch (error) {
        debugPrint(
          '[HuntMediaService] Could not delete session photo directory '
          '(${error.runtimeType}).',
        );
      }
    }
  }

  /// Repairs entries that point at deleted files and invalid link duplicates.
  /// It never deletes an unreferenced photo because a damaged manifest should
  /// not turn startup recovery into destructive cleanup.
  Future<void> repairManifest() async {
    await _mutate((manifest) async {
      final valid = <HuntPhoto>[];
      for (final photo in manifest.photos) {
        try {
          if (await File(photo.localPath).exists()) {
            valid.add(photo);
          }
        } catch (_) {}
      }
      manifest.photos
        ..clear()
        ..addAll(valid);
      for (final entry in manifest.trophyLinks.entries) {
        entry.value
          ..retainWhere((sessionId) => sessionId.trim().isNotEmpty)
          ..sort();
        final unique = entry.value.toSet().toList()..sort();
        entry.value
          ..clear()
          ..addAll(unique);
      }
      manifest.trophyLinks.removeWhere((_, links) => links.isEmpty);
    });
  }

  Future<void> _mutate(
    FutureOr<void> Function(_HuntMediaManifest manifest) change,
  ) {
    final completion = Completer<void>();
    _mutationTail = _mutationTail.then((_) async {
      try {
        final manifest = await _readManifest();
        await change(manifest);
        await _writeManifest(manifest);
        completion.complete();
      } catch (error, stack) {
        completion.completeError(error, stack);
      }
    });
    return completion.future;
  }

  Future<_HuntMediaManifest> _readManifest() async {
    final file = await _manifestFile();
    if (!await file.exists()) {
      return _HuntMediaManifest(
        photos: <HuntPhoto>[],
        trophyLinks: <int, List<String>>{},
      );
    }

    try {
      final raw = await PayloadGuard.readString(file);
      final decoded = jsonDecode(raw);
      if (decoded is! Map) {
        throw const FormatException('Manifest is not an object.');
      }
      final map = Map<String, dynamic>.from(decoded);
      final rawPhotos = map['photos'];
      final photos = <HuntPhoto>[];
      if (rawPhotos is List) {
        for (final item in rawPhotos.whereType<Map>()) {
          try {
            photos.add(HuntPhoto.fromJson(Map<String, dynamic>.from(item)));
          } catch (error) {
            debugPrint(
              '[HuntMediaService] Skipped invalid photo record: $error',
            );
          }
        }
      }

      final trophyLinks = <int, List<String>>{};
      final rawLinks = map['trophyLinks'];
      if (rawLinks is Map) {
        for (final entry in rawLinks.entries) {
          final trophyId = int.tryParse(entry.key.toString());
          if (trophyId == null || entry.value is! List) {
            continue;
          }
          trophyLinks[trophyId] = (entry.value as List)
              .whereType<String>()
              .toList();
        }
      }
      return _HuntMediaManifest(photos: photos, trophyLinks: trophyLinks);
    } catch (error, stack) {
      debugPrint('[HuntMediaService] Invalid manifest: $error');
      debugPrint(stack.toString());
      // Keep the broken file for diagnosis and avoid deleting any photo files.
      return _HuntMediaManifest(
        photos: <HuntPhoto>[],
        trophyLinks: <int, List<String>>{},
      );
    }
  }

  Future<void> _writeManifest(_HuntMediaManifest manifest) async {
    final file = await _manifestFile();
    final temporary = File('${file.path}.tmp');
    await temporary.writeAsString(jsonEncode(manifest.toJson()), flush: true);
    try {
      await temporary.rename(file.path);
    } on FileSystemException {
      await temporary.copy(file.path);
      await temporary.delete();
    }
    mediaVersion.value++;
  }

  Future<File> _manifestFile() async {
    final root = await _rootDirectory();
    return File('${root.path}${Platform.pathSeparator}manifest.json');
  }

  Future<Directory> _rootDirectory() async {
    final override = _rootOverride;
    if (override != null) {
      if (!await override.exists()) {
        await override.create(recursive: true);
      }
      return override;
    }
    return UserStorage.documentsDir('hunt_media');
  }

  Future<Directory> _sessionPhotosDirectory(
    String sessionId, {
    bool create = true,
  }) async {
    final safeId = sessionId.replaceAll(RegExp('[^A-Za-z0-9_-]'), '_');
    final root = await _rootDirectory();
    final dir = Directory(
      '${root.path}${Platform.pathSeparator}photos${Platform.pathSeparator}$safeId',
    );
    if (create && !await dir.exists()) {
      await dir.create(recursive: true);
    }
    return dir;
  }

  Future<void> _deleteFileQuietly(File? file) async {
    if (file == null) {
      return;
    }
    try {
      if (await file.exists()) {
        await file.delete();
      }
    } catch (error) {
      debugPrint(
        '[HuntMediaService] Could not delete private photo '
        '(${error.runtimeType}).',
      );
    }
  }
}
