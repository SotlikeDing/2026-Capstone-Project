import 'package:flutter/material.dart';

import 'models/app_session.dart';
import 'safety/app_error_barrier.dart';
import 'screens/loading_screen.dart';
import 'screens/main_shell.dart';
import 'screens/sign_in_screen.dart';
import 'services/auth_service.dart';
import 'services/legacy_storage_cleanup.dart';
import 'services/session_watcher.dart';
import 'theme/app_theme.dart';
import 'widgets/app_feedback_panel.dart';
import 'widgets/app_logo.dart';
import 'widgets/app_tile_layer.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  AppErrorBarrier.install();
  // Pre-account versions stored GPS routes and recap videos in shared app
  // folders. The team elected to delete that test data rather than guess an
  // owner and risk exposing it to the next person who signs in.
  await LegacyStorageCleanup.deleteSharedHuntData();
  // Must run before the first map is built; it fixes the cache location.
  await AppTileLayer.configureTileCache();
  runApp(const JagbestuurApp());
}

class JagbestuurApp extends StatefulWidget {
  const JagbestuurApp({super.key});

  @override
  State<JagbestuurApp> createState() => _JagbestuurAppState();
}

class _JagbestuurAppState extends State<JagbestuurApp> {
  late final Future<void> _startupFuture;
  final GlobalKey<NavigatorState> _navigatorKey = GlobalKey<NavigatorState>();

  @override
  void initState() {
    super.initState();
    _startupFuture = AuthService.instance.initialize();
    AuthService.instance.sessionNotifier.addListener(_onSessionChanged);
    // Signs out within a minute when an admin suspends the account.
    SessionWatcher.instance.start();
  }

  @override
  void dispose() {
    AuthService.instance.sessionNotifier.removeListener(_onSessionChanged);
    super.dispose();
  }

  /// On sign-in and on sign-out (including a forced sign-out after a failed
  /// token refresh) close every pushed page, so nobody is left on a screen
  /// that belongs to a session that no longer exists.
  void _onSessionChanged() {
    _navigatorKey.currentState?.popUntil((route) => route.isFirst);
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      navigatorKey: _navigatorKey,
      scaffoldMessengerKey: AppErrorBarrier.messengerKey,
      title: 'Jagbestuur',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.darkHuntingTheme,
      home: LoadingScreen(
        ready: _startupFuture,
        nextScreen: _AppEntry(startupFuture: _startupFuture),
      ),
    );
  }
}

class _AppEntry extends StatelessWidget {
  const _AppEntry({required this.startupFuture});

  final Future<void> startupFuture;

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<void>(
      future: startupFuture,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const _StartupScreen();
        }

        return ValueListenableBuilder<AppSession?>(
          valueListenable: AuthService.instance.sessionNotifier,
          builder: (context, session, _) {
            if (session == null) {
              return const SignInScreen();
            }

            // Every role gets the shell (and its home screen); the tabs
            // inside depend on the role the API read from the database.
            return MainShell(session: session);
          },
        );
      },
    );
  }
}

class _StartupScreen extends StatelessWidget {
  const _StartupScreen();

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 360),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: const [
                    AppLogo(compact: true, showTagline: false),
                    SizedBox(height: 24),
                    AppFeedbackPanel(
                      icon: Icons.terrain_rounded,
                      title: 'Jagbestuur word voorberei',
                      message: 'Jou rekening en jagdata word nagegaan.',
                      isLoading: true,
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
