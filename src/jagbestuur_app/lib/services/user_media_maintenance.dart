import 'package:flutter/foundation.dart';

import 'hunt_media_service.dart';
import 'video_export_service.dart';
import 'video_storage_service.dart';

class UserMediaMaintenance {
  const UserMediaMaintenance._();

  static Future<void> run() async {
    try {
      await VideoExportService.cleanupAbandonedExports();
      await HuntMediaService().repairManifest();
      await const VideoStorageService().pruneMissingVideos();
    } catch (error, stack) {
      debugPrint('[UserMediaMaintenance] Startup recovery failed: $error');
      debugPrint(stack.toString());
    }
  }
}
