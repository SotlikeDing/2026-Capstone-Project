import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:path_provider/path_provider.dart';

/// Deletes hunt data written by app versions that stored every user's files
/// in shared, phone-wide application folders.
///
/// Current data lives below `users/<userId>/`, so these exact legacy folder
/// names can be removed without touching any signed-in user's new data.
class LegacyStorageCleanup {
  const LegacyStorageCleanup._();

  static const List<String> _legacyDirectoryNames = <String>[
    'hunt_sessions',
    'recap_videos',
  ];

  static Future<void> deleteSharedHuntData() async {
    try {
      final documents = await getApplicationDocumentsDirectory();
      final temporary = await getTemporaryDirectory();
      await deleteSharedHuntDataIn(
        documentsDirectory: documents,
        temporaryDirectory: temporary,
      );
    } catch (error, stack) {
      // Cleanup must never prevent the app from starting. A later launch will
      // try the idempotent deletion again.
      debugPrint('[LegacyStorageCleanup] Cleanup failed: $error');
      debugPrint(stack.toString());
    }
  }

  @visibleForTesting
  static Future<void> deleteSharedHuntDataIn({
    required Directory documentsDirectory,
    Directory? temporaryDirectory,
  }) async {
    final roots = <Directory>[documentsDirectory, ?temporaryDirectory];

    for (final root in roots) {
      for (final name in _legacyDirectoryNames) {
        final directory = Directory(
          '${root.path}${Platform.pathSeparator}$name',
        );
        if (await directory.exists()) {
          await directory.delete(recursive: true);
        }
      }
    }
  }
}
