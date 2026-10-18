import 'package:flutter/foundation.dart';
import 'package:flutter_foreground_task/flutter_foreground_task.dart';

/// Keeps the microphone allowed while the screen is off / the app is in a
/// pocket. Android only lets an app use the microphone in the background from a
/// foreground service of type "microphone", and (Android 14+) that service may
/// only be STARTED while the app is visible - so it is started when listening
/// starts on the map screen, never later. No Dart task runs inside the service:
/// the audio keeps flowing in the app's own isolate, the service only keeps the
/// process and the microphone permission alive. It stops with the hunt, when the
/// app is swiped away, and is never restarted on its own.
class MicForegroundService {
  const MicForegroundService._();

  static bool _initialized = false;

  static void _init() {
    if (_initialized) return;
    FlutterForegroundTask.init(
      androidNotificationOptions: AndroidNotificationOptions(
        channelId: 'jagbestuur_gunshot',
        channelName: 'Outo-skoot',
        channelDescription: 'Wys wanneer Jagbestuur tydens \'n jag na skote luister.',
        onlyAlertOnce: true,
      ),
      iosNotificationOptions: const IOSNotificationOptions(showNotification: false),
      foregroundTaskOptions: ForegroundTaskOptions(
        eventAction: ForegroundTaskEventAction.nothing(),
        allowWakeLock: true,
        allowAutoRestart: false,
        stopWithTask: true,
      ),
    );
    _initialized = true;
  }

  static Future<bool> start() async {
    if (defaultTargetPlatform != TargetPlatform.android) return true;
    try {
      _init();
      if (await FlutterForegroundTask.isRunningService) return true;
      final result = await FlutterForegroundTask.startService(
        serviceTypes: [ForegroundServiceTypes.microphone],
        notificationTitle: 'Outo-skoot luister',
        notificationText: 'Jagbestuur luister vir skote. Geen klank word gestoor nie.',
      );
      return result is ServiceRequestSuccess;
    } catch (e) {
      debugPrint('[MicForegroundService] Could not start: $e');
      return false;
    }
  }

  static Future<void> stop() async {
    if (defaultTargetPlatform != TargetPlatform.android) return;
    try {
      if (await FlutterForegroundTask.isRunningService) {
        await FlutterForegroundTask.stopService();
      }
    } catch (e) {
      debugPrint('[MicForegroundService] Could not stop: $e');
    }
  }
}
