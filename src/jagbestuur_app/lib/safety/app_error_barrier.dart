import 'dart:ui';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../config/safety_config.dart';
import '../theme/app_theme.dart';

/// Global catch-all for errors that nothing else handled.
///
/// Framework errors (build/layout/paint) go through [FlutterError.onError].
/// Uncaught async and platform-channel errors go through
/// [PlatformDispatcher.onError]. Both are logged and turned into a calm
/// message for the user instead of a red screen or a dead app.
class AppErrorBarrier {
  const AppErrorBarrier._();

  /// Lets the barrier show a SnackBar without a [BuildContext].
  static final GlobalKey<ScaffoldMessengerState> messengerKey =
      GlobalKey<ScaffoldMessengerState>();

  static DateTime? _lastNoticeAt;

  /// Call once from `main()` after `WidgetsFlutterBinding.ensureInitialized()`.
  static void install() {
    FlutterError.onError = (details) {
      _log('FlutterError', details.exception, details.stack);
      if (kDebugMode) {
        // Keep the normal debug console dump while developing.
        FlutterError.presentError(details);
      }
    };

    PlatformDispatcher.instance.onError = (error, stack) {
      _log('Uncaught async/platform error', error, stack);
      _notifyUser();
      // true = handled, so the engine does not treat it as fatal.
      return true;
    };

    // Replaces the red "error in build" box with a safe fallback widget.
    ErrorWidget.builder = (details) => _BuildErrorFallback(details: details);

    PaintingBinding.instance.imageCache.maximumSizeBytes = maxImageCacheBytes;
  }

  /// Logs an error that was caught locally but should still be recorded.
  static void report(String context, Object error, [StackTrace? stack]) {
    _log(context, error, stack);
  }

  static void _log(String context, Object error, StackTrace? stack) {
    debugPrint('[AppErrorBarrier] $context: $error');
    if (stack != null) {
      debugPrint(stack.toString());
    }
  }

  static void _notifyUser() {
    // Throttle so an error inside a periodic timer can't flood the screen.
    final now = DateTime.now();
    final last = _lastNoticeAt;
    if (last != null && now.difference(last) < const Duration(seconds: 5)) {
      return;
    }
    _lastNoticeAt = now;

    final messenger = messengerKey.currentState;
    if (messenger == null) {
      return;
    }

    try {
      messenger.showSnackBar(
        const SnackBar(
          content: Text(
            'Iets het onverwags fout gegaan. Die aksie is gestop, maar die app '
            'werk nog.',
          ),
          duration: Duration(seconds: 4),
        ),
      );
    } catch (_) {
      // Never let the error reporter itself throw.
    }
  }
}

class _BuildErrorFallback extends StatelessWidget {
  const _BuildErrorFallback({required this.details});

  final FlutterErrorDetails details;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: AppTheme.card,
      child: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(
                Icons.report_problem_outlined,
                color: AppTheme.gold,
                size: 40,
              ),
              const SizedBox(height: 12),
              const Text(
                'Hierdie deel kon nie gelaai word nie.',
                textAlign: TextAlign.center,
                style: TextStyle(
                  color: AppTheme.ink,
                  fontWeight: FontWeight.w800,
                  fontSize: 16,
                ),
              ),
              const SizedBox(height: 6),
              const Text(
                'Gaan terug en probeer weer.',
                textAlign: TextAlign.center,
                style: TextStyle(color: AppTheme.muted),
              ),
              if (kDebugMode) ...[
                const SizedBox(height: 12),
                Text(
                  details.exceptionAsString(),
                  textAlign: TextAlign.center,
                  maxLines: 6,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(color: AppTheme.muted, fontSize: 12),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
