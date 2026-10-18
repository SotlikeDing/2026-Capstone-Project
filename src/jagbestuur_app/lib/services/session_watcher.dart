import 'dart:async';

import 'package:flutter/widgets.dart';

import 'api_client.dart';
import 'auth_service.dart';

/// Checks every minute, while the app is open and someone is signed in, that the
/// server still accepts the session. When an admin suspends the account the
/// server refuses the token and the refresh, and [ApiClient] signs the user out -
/// so the app returns to the sign-in screen without the user doing anything.
class SessionWatcher with WidgetsBindingObserver {
  SessionWatcher._();

  static final SessionWatcher instance = SessionWatcher._();
  static const Duration interval = Duration(seconds: 60);

  Timer? _timer;
  bool _started = false;

  void start() {
    if (_started) return;
    _started = true;
    WidgetsBinding.instance.addObserver(this);
    AuthService.instance.sessionNotifier.addListener(_reschedule);
    _reschedule();
  }

  void _reschedule() {
    _timer?.cancel();
    _timer = null;
    if (AuthService.instance.sessionNotifier.value != null) {
      _timer = Timer.periodic(interval, (_) => unawaited(check()));
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(check());
      _reschedule();
    } else if (state == AppLifecycleState.paused) {
      _timer?.cancel();
      _timer = null;
    }
  }

  /// One cheap authenticated request; a refused session is handled by the
  /// ApiClient interceptor (refresh, then sign-out). Network errors are ignored.
  Future<void> check() async {
    if (AuthService.instance.sessionNotifier.value == null) return;
    try {
      await ApiClient.instance.dio.get<dynamic>('/api/mobile-access/me');
    } catch (_) {
      // Offline, maintenance (503) or a refused session - the last one already signed out.
    }
  }
}
