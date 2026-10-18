import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';

import '../models/hunt_session.dart';
import '../safety/payload_guard.dart';
import 'hunt_media_service.dart';
import 'user_storage.dart';
import 'video_storage_service.dart';

/// Saved hunt sessions for the signed-in user.
class HuntStorageService {
  const HuntStorageService();

  static final ValueNotifier<int> sessionsVersion = ValueNotifier<int>(0);

  static void notifySessionsChanged() {
    sessionsVersion.value++;
  }

  Future<Directory> _sessionsDirectory() =>
      UserStorage.documentsDir('hunt_sessions');

  Future<List<HuntSession>> listSessions() async {
    final dir = await _sessionsDirectory();

    final files = await dir
        .list()
        .where((entity) => entity is File && entity.path.endsWith('.json'))
        .cast<File>()
        .toList();

    final sessions = <HuntSession>[];

    for (final file in files) {
      try {
        final raw = await PayloadGuard.readString(file);
        final json = jsonDecode(raw) as Map<String, dynamic>;
        sessions.add(HuntSession.fromJson(json));
      } catch (e) {
        debugPrint('[HuntStorageService] Skipped unreadable ${file.path}: $e');
      }
    }

    sessions.sort((a, b) => b.startedAt.compareTo(a.startedAt));
    return sessions;
  }

  /// Deletes a session together with the recap videos made from it.
  Future<void> deleteSession(String id) async {
    final dir = await _sessionsDirectory();
    final file = File('${dir.path}${Platform.pathSeparator}$id.json');

    await const VideoStorageService().deleteVideosForSession(id);
    await HuntMediaService().deleteMediaForSession(id);

    if (await file.exists()) {
      await file.delete();
    }
    notifySessionsChanged();
  }
}
