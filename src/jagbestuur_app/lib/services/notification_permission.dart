import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

/// Requests Android 13+'s runtime notification permission so the hunt's
/// foreground-service notification is visible. No-op elsewhere.
class NotificationPermission {
  const NotificationPermission._();

  static const MethodChannel _channel = MethodChannel('jagbestuur/permissions');

  static Future<void> request() async {
    if (defaultTargetPlatform != TargetPlatform.android) {
      return;
    }
    try {
      await _channel.invokeMethod<bool>('requestNotifications');
    } catch (e) {
      // Tracking still works without the notification; don't block the hunt.
      debugPrint('[NotificationPermission] Request failed: $e');
    }
  }
}
