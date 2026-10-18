import 'dart:async';

import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

class LoadingScreen extends StatefulWidget {
  const LoadingScreen({super.key, required this.nextScreen, this.ready});

  final Widget nextScreen;

  /// Startup work to wait for. The splash leaves as soon as this completes
  /// (after a short minimum so it doesn't flash), not after a fixed delay.
  final Future<void>? ready;

  @override
  State<LoadingScreen> createState() => _LoadingScreenState();
}

class _LoadingScreenState extends State<LoadingScreen>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  late final Animation<double> _animation;
  Timer? _navigationTimer;

  @override
  void initState() {
    super.initState();

    _controller = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 3),
    )..repeat(reverse: true);

    _animation = Tween<double>(
      begin: 0.8,
      end: 1.2,
    ).animate(CurvedAnimation(parent: _controller, curve: Curves.easeInOut));

    unawaited(_navigateWhenReady());
  }

  static const Duration _minimumSplash = Duration(milliseconds: 900);

  Future<void> _navigateWhenReady() async {
    final minimum = Completer<void>();
    _navigationTimer = Timer(_minimumSplash, minimum.complete);

    await Future.wait<void>([
      minimum.future,
      // Errors are handled by the next screen's FutureBuilder.
      (widget.ready ?? Future<void>.value()).catchError((_) {}),
    ]);

    if (!mounted) {
      return;
    }

    unawaited(
      Navigator.of(
        context,
      ).pushReplacement(MaterialPageRoute(builder: (_) => widget.nextScreen)),
    );
  }

  @override
  void dispose() {
    _navigationTimer?.cancel();
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF2B2118),
      body: Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            ScaleTransition(
              scale: _animation,
              child: ClipRRect(
                borderRadius: BorderRadius.circular(24),
                child: Image.asset(
                  'lib/assets/logos/logo.png',
                  width: 180,
                  errorBuilder: (_, _, _) => const Icon(
                    Icons.forest_rounded,
                    size: 120,
                    color: AppTheme.goldSoft,
                  ),
                ),
              ),
            ),
            const SizedBox(height: 30),
            const Text(
              'Laai jou mikpunt...',
              style: TextStyle(
                color: Colors.white,
                fontSize: 22,
                fontWeight: FontWeight.bold,
                letterSpacing: 2,
              ),
            ),
            const SizedBox(height: 20),
            SizedBox(
              width: 220,
              child: ClipRRect(
                borderRadius: BorderRadius.circular(999),
                child: LinearProgressIndicator(
                  minHeight: 8,
                  color: AppTheme.olive,
                  backgroundColor: AppTheme.cream,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
