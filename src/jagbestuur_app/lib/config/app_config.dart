class AppConfig {
  const AppConfig._();

  /// True in `flutter build --release`. A const so the URL defaults below can
  /// differ per build type.
  static const bool _isRelease = bool.fromEnvironment('dart.vm.product');

  /// Release builds default to HTTPS: passwords and tokens must never travel
  /// in plain text to a real server. Debug builds keep HTTP so the local dev
  /// stack (which has no certificate) still works. Override either with
  /// `--dart-define=API_BASE_URL=...`.
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: _isRelease
        ? 'https://mikpunt.ddns.net'
        : 'http://mobileapi.mikpunt.ddns.net',
    // defaultValue: 'http://10.0.2.2:8082',
  );

  static const String loginPath = '/api/auth/login';
  static const String refreshPath = '/api/auth/refresh';

  static const String webBaseUrl = String.fromEnvironment(
    'WEB_BASE_URL',
    defaultValue: _isRelease
        ? 'https://webapi.mikpunt.ddns.net'
        : 'http://webapi.mikpunt.ddns.net',
  );

  static const String webLoginUrl = '$webBaseUrl/teken-in';
  static const String webRegisterUrl = '$webBaseUrl/register';

  /// Identifies the app to the OpenStreetMap tile servers, as their usage
  /// policy requires. Must match `applicationId` in
  /// android/app/build.gradle.kts; update both together.
  static const String tileUserAgentPackageName = 'za.co.mikpunt.jagbestuur';

  /// Resolves API-owned relative resources, such as uploaded trophy photos,
  /// against the configured API host. Absolute legacy links are left intact.
  static String resolveApiUrl(String value) {
    final trimmed = value.trim();
    if (trimmed.isEmpty) return '';

    final uri = Uri.tryParse(trimmed);
    if (uri?.hasScheme == true) return trimmed;
    // A plain legacy file name is not an API resource and cannot be rendered
    // on another phone. Uploaded photos always use an absolute-path reference.
    if (!trimmed.startsWith('/')) return trimmed;

    final base = Uri.parse(
      apiBaseUrl.endsWith('/') ? apiBaseUrl : '$apiBaseUrl/',
    );
    final relative = trimmed.substring(1);
    return base.resolve(relative).toString();
  }

  /// OSM tile template. Override with `--dart-define=MAP_TILE_URL=...` to point
  /// at a commercial or self-hosted tile provider for production traffic.
  static const String mapTileUrl = String.fromEnvironment(
    'MAP_TILE_URL',
    defaultValue: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
  );
}
