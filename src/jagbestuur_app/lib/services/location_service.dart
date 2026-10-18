import 'dart:async';

import 'package:geolocator/geolocator.dart';

/// Thrown with a user-facing Afrikaans message when a location can't be had.
class LocationUnavailableException implements Exception {
  const LocationUnavailableException(this.message);

  final String message;

  @override
  String toString() => message;
}

/// Shared GPS access: permission handling plus a bounded wait for a fix, so no
/// screen can hang forever when the sky is blocked.
class LocationService {
  const LocationService._();

  static const Duration defaultTimeout = Duration(seconds: 20);

  /// Checks that location services are on and permission is granted,
  /// requesting it when needed. Throws [LocationUnavailableException].
  static Future<void> ensurePermission() async {
    if (!await Geolocator.isLocationServiceEnabled()) {
      throw const LocationUnavailableException(
        'Skakel liggingdienste (GPS) aan en probeer weer.',
      );
    }

    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
    }

    if (permission == LocationPermission.deniedForever) {
      throw const LocationUnavailableException(
        'Liggingtoestemming is geweier. Gee toestemming in die foon se '
        'instellings.',
      );
    }
    if (permission != LocationPermission.always &&
        permission != LocationPermission.whileInUse) {
      throw const LocationUnavailableException(
        'Liggingtoestemming is nodig vir hierdie funksie.',
      );
    }
  }

  /// Permission check plus a current fix. When no fresh fix arrives within
  /// [timeout], falls back to the last known position if [allowLastKnown].
  static Future<Position> currentPosition({
    LocationSettings? settings,
    Duration timeout = defaultTimeout,
    bool allowLastKnown = false,
  }) async {
    await ensurePermission();

    try {
      return await Geolocator.getCurrentPosition(
        locationSettings: settings,
      ).timeout(timeout);
    } on TimeoutException {
      if (allowLastKnown) {
        final last = await Geolocator.getLastKnownPosition();
        if (last != null) return last;
      }
      throw const LocationUnavailableException(
        'Kon nie ’n GPS-sein kry nie. Gaan na ’n oop area en probeer weer.',
      );
    } on LocationServiceDisabledException {
      throw const LocationUnavailableException(
        'Skakel liggingdienste (GPS) aan en probeer weer.',
      );
    } on PermissionDeniedException {
      throw const LocationUnavailableException(
        'Liggingtoestemming is nodig vir hierdie funksie.',
      );
    }
  }
}
