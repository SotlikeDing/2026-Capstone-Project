import 'dart:math' as math;

import '../models/hunt_photo.dart';
import '../models/hunt_pin.dart';
import '../models/hunt_session.dart';

/// Deterministic, fully local photo-to-route matching.
class HuntPhotoMatcher {
  const HuntPhotoMatcher();

  static const double maxGpsRouteDistanceMeters = 500;
  static const Duration maxTimestampDifference = Duration(hours: 4);
  static const double nearbyPinDistanceMeters = 100;
  static const Duration nearbyPinTimeDifference = Duration(minutes: 20);

  HuntPhotoMatch match(HuntSession session, HuntPhotoMetadata metadata) {
    if (session.waypoints.isEmpty) {
      return const HuntPhotoMatch.unmatched();
    }

    final latitude = metadata.latitude;
    final longitude = metadata.longitude;
    if (latitude != null && longitude != null) {
      final nearest = _nearestWaypoint(session, latitude, longitude);
      if (nearest.distanceMeters <= maxGpsRouteDistanceMeters) {
        final confidence = nearest.distanceMeters <= 50
            ? HuntPhotoMatchConfidence.high
            : nearest.distanceMeters <= 150
            ? HuntPhotoMatchConfidence.medium
            : HuntPhotoMatchConfidence.low;
        final pin = _nearestPinByDistance(session, latitude, longitude);
        return HuntPhotoMatch(
          method: HuntPhotoMatchMethod.gps,
          confidence: confidence,
          needsConfirmation: confidence != HuntPhotoMatchConfidence.high,
          waypointIndex: nearest.index,
          pinId: pin?.id,
        );
      }
    }

    final capturedAt = metadata.capturedAt;
    if (capturedAt != null) {
      final nearest = _nearestWaypointByTime(session, capturedAt);
      if (nearest.difference <= maxTimestampDifference) {
        final confidence = nearest.difference <= const Duration(minutes: 2)
            ? HuntPhotoMatchConfidence.high
            : nearest.difference <= const Duration(minutes: 20)
            ? HuntPhotoMatchConfidence.medium
            : HuntPhotoMatchConfidence.low;
        final pin = _nearestPinByTime(session, capturedAt);
        return HuntPhotoMatch(
          method: pin == null
              ? HuntPhotoMatchMethod.timestamp
              : HuntPhotoMatchMethod.pin,
          confidence: confidence,
          needsConfirmation: confidence != HuntPhotoMatchConfidence.high,
          waypointIndex: nearest.index,
          pinId: pin?.id,
        );
      }

      final pin = _nearestPinByTime(session, capturedAt, allowWideWindow: true);
      if (pin != null) {
        final waypoint = _nearestWaypoint(session, pin.latitude, pin.longitude);
        return HuntPhotoMatch(
          method: HuntPhotoMatchMethod.pin,
          confidence: HuntPhotoMatchConfidence.low,
          needsConfirmation: true,
          waypointIndex: waypoint.index,
          pinId: pin.id,
        );
      }
    }

    return const HuntPhotoMatch.unmatched();
  }

  ({int index, double distanceMeters}) _nearestWaypoint(
    HuntSession session,
    double latitude,
    double longitude,
  ) {
    var bestIndex = 0;
    var bestDistance = double.infinity;
    for (var index = 0; index < session.waypoints.length; index++) {
      final waypoint = session.waypoints[index];
      final distance = distanceMeters(
        latitude,
        longitude,
        waypoint.latitude,
        waypoint.longitude,
      );
      if (distance < bestDistance) {
        bestDistance = distance;
        bestIndex = index;
      }
    }
    return (index: bestIndex, distanceMeters: bestDistance);
  }

  ({int index, Duration difference}) _nearestWaypointByTime(
    HuntSession session,
    DateTime timestamp,
  ) {
    var bestIndex = 0;
    var bestDifference = const Duration(days: 36500);
    for (var index = 0; index < session.waypoints.length; index++) {
      final difference = _absoluteDifference(
        session.waypoints[index].timestamp,
        timestamp,
      );
      if (difference < bestDifference) {
        bestDifference = difference;
        bestIndex = index;
      }
    }
    return (index: bestIndex, difference: bestDifference);
  }

  HuntPin? _nearestPinByDistance(
    HuntSession session,
    double latitude,
    double longitude,
  ) {
    if (session.pins.isEmpty) {
      return null;
    }
    HuntPin? best;
    var bestDistance = nearbyPinDistanceMeters;
    for (final pin in session.pins) {
      final distance = distanceMeters(
        latitude,
        longitude,
        pin.latitude,
        pin.longitude,
      );
      if (distance <= bestDistance) {
        bestDistance = distance;
        best = pin;
      }
    }
    return best;
  }

  HuntPin? _nearestPinByTime(
    HuntSession session,
    DateTime timestamp, {
    bool allowWideWindow = false,
  }) {
    HuntPin? best;
    var bestDifference = allowWideWindow
        ? maxTimestampDifference
        : nearbyPinTimeDifference;
    for (final pin in session.pins) {
      final difference = _absoluteDifference(pin.timestamp, timestamp);
      if (difference <= bestDifference) {
        bestDifference = difference;
        best = pin;
      }
    }
    return best;
  }

  Duration _absoluteDifference(DateTime first, DateTime second) {
    final difference = first.difference(second);
    return difference.isNegative ? -difference : difference;
  }

  static double distanceMeters(
    double latitude1,
    double longitude1,
    double latitude2,
    double longitude2,
  ) {
    const earthRadiusMeters = 6371000.0;
    double radians(double degrees) => degrees * math.pi / 180;
    final dLat = radians(latitude2 - latitude1);
    final dLon = radians(longitude2 - longitude1);
    final a =
        math.sin(dLat / 2) * math.sin(dLat / 2) +
        math.cos(radians(latitude1)) *
            math.cos(radians(latitude2)) *
            math.sin(dLon / 2) *
            math.sin(dLon / 2);
    return earthRadiusMeters * 2 * math.atan2(math.sqrt(a), math.sqrt(1 - a));
  }
}
