import 'package:flutter/widgets.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:path_provider/path_provider.dart';

import '../config/app_config.dart';

/// Base map tiles for every map in the app.
///
/// Tiles go through flutter_map's built-in cache, which [configureTileCache]
/// points at durable storage: areas viewed while online keep rendering in the
/// field without signal, and repeat views don't re-download from the tile
/// server.
class AppTileLayer extends StatelessWidget {
  const AppTileLayer({super.key});

  /// Call once at startup, before any map is built. The first call to
  /// [BuiltInMapCachingProvider.getOrCreateInstance] fixes the configuration.
  static Future<void> configureTileCache() async {
    try {
      final supportDir = await getApplicationSupportDirectory();
      BuiltInMapCachingProvider.getOrCreateInstance(
        cacheDirectory: '${supportDir.path}/map_tiles',
        maxCacheSize: 300 * 1024 * 1024,
      );
    } catch (e) {
      // Falls back to flutter_map's default (OS-managed) cache location.
      debugPrint('[AppTileLayer] Could not configure tile cache: $e');
    }
  }

  @override
  Widget build(BuildContext context) {
    return TileLayer(
      urlTemplate: AppConfig.mapTileUrl,
      userAgentPackageName: AppConfig.tileUserAgentPackageName,
    );
  }
}
