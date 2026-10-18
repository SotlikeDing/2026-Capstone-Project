import 'dart:math' as math;

import '../models/smart_waypoint.dart';

/// Pure, isolate-safe utilities for turning a raw multi-hour GPS trace
/// into a lightweight, animation-ready keyframe list.
///
/// Pipeline order (see [optimizeRoute]):
///   1. [collapseStaticPoints]      -> merges "parked" clusters into a single Rest Stop
///   2. [simplifyDouglasPeucker]    -> removes redundant points on straight segments
///   3. [resampleToFixedCount]      -> guarantees exactly [targetCount] keyframes
class RouteOptimizer {
  const RouteOptimizer._();

  static const double _earthRadiusMeters = 6371000.0;

  // ---------------------------------------------------------------------
  // Public pipeline entry point
  // ---------------------------------------------------------------------

  /// Runs the full optimization pipeline on a raw GPS trace.
  ///
  /// [stopRadiusMeters]     — max drift allowed while still counting as "stopped".
  /// [minStopDuration]      — minimum dwell time before a cluster becomes a Rest Stop.
  /// [simplifyEpsilonMeters]— Douglas-Peucker tolerance; larger = more aggressive pruning.
  /// [targetCount]          — final number of keyframes (default 300).
  static List<SmartWaypoint> optimizeRoute(
    List<SmartWaypoint> rawRoute, {
    double stopRadiusMeters = 15,
    Duration minStopDuration = const Duration(minutes: 2),
    double simplifyEpsilonMeters = 8,
    int targetCount = 300,
  }) {
    if (rawRoute.isEmpty) return const [];

    final sorted = List<SmartWaypoint>.from(rawRoute)
      ..sort((a, b) => a.timestamp.compareTo(b.timestamp));

    final collapsed = collapseStaticPoints(
      sorted,
      stopRadiusMeters: stopRadiusMeters,
      minStopDuration: minStopDuration,
    );

    final simplified = simplifyDouglasPeucker(collapsed, simplifyEpsilonMeters);

    return resampleToFixedCount(simplified, targetCount);
  }

  // ---------------------------------------------------------------------
  // Step 1: Collapse consecutive static points into Rest Stop markers
  // ---------------------------------------------------------------------

  /// Scans the route for consecutive points that stay within
  /// [stopRadiusMeters] of each other for at least [minStopDuration],
  /// and collapses each such cluster into a single waypoint with
  /// `isStop = true` and an accurate [SmartWaypoint.stopDuration].
  ///
  /// The Rest Stop's coordinates are the centroid of the cluster.
  /// Any [SmartWaypoint.localPhotoPath] found within the cluster is
  /// preserved (first non-null one wins).
  static List<SmartWaypoint> collapseStaticPoints(
    List<SmartWaypoint> points, {
    double stopRadiusMeters = 15,
    Duration minStopDuration = const Duration(minutes: 2),
  }) {
    if (points.length < 2) return List.of(points);

    final result = <SmartWaypoint>[];
    var clusterStart = 0;

    for (var i = 1; i <= points.length; i++) {
      final isLast = i == points.length;

      // Distance from the first point in the current candidate cluster.
      final distanceFromClusterStart = isLast
          ? double.infinity
          : _haversineMeters(
              points[clusterStart].latitude,
              points[clusterStart].longitude,
              points[i].latitude,
              points[i].longitude,
            );

      final clusterBroken =
          isLast || distanceFromClusterStart > stopRadiusMeters;

      if (!clusterBroken) continue;

      final clusterEnd = i - 1; // inclusive
      final clusterLength = clusterEnd - clusterStart + 1;

      if (clusterLength <= 1) {
        // Nothing to collapse — just emit the single point and move on.
        result.add(points[clusterStart]);
        clusterStart = i;
        continue;
      }

      final clusterPoints = points.sublist(clusterStart, clusterEnd + 1);
      final duration = clusterPoints.last.timestamp.difference(
        clusterPoints.first.timestamp,
      );

      if (duration >= minStopDuration) {
        result.add(_buildRestStop(clusterPoints, duration));
      } else {
        // Too short to count as a "real" stop — keep endpoints only
        // to preserve path shape without bloating the array.
        result.add(clusterPoints.first);
        if (clusterPoints.length > 1) result.add(clusterPoints.last);
      }

      clusterStart = i;
    }

    return result;
  }

  static SmartWaypoint _buildRestStop(
    List<SmartWaypoint> cluster,
    Duration duration,
  ) {
    var latSum = 0.0;
    var lngSum = 0.0;
    String? photoPath;

    for (final p in cluster) {
      latSum += p.latitude;
      lngSum += p.longitude;
      photoPath ??= p.localPhotoPath;
    }

    return SmartWaypoint(
      latitude: latSum / cluster.length,
      longitude: lngSum / cluster.length,
      timestamp: cluster.first.timestamp,
      isStop: true,
      stopDuration: duration,
      localPhotoPath: photoPath,
    );
  }

  // ---------------------------------------------------------------------
  // Step 2: Douglas-Peucker geometric simplification
  // ---------------------------------------------------------------------

  /// Removes points that lie within [epsilonMeters] of the straight line
  /// connecting their neighbours, using the classic Douglas-Peucker
  /// algorithm. Rest Stop markers (`isStop == true`) are always preserved
  /// regardless of geometric redundancy, since they carry semantic meaning.
  static List<SmartWaypoint> simplifyDouglasPeucker(
    List<SmartWaypoint> points,
    double epsilonMeters,
  ) {
    if (points.length < 3) return List.of(points);

    final keep = List<bool>.filled(points.length, false);
    keep[0] = true;
    keep[points.length - 1] = true;

    _douglasPeuckerRecursive(points, 0, points.length - 1, epsilonMeters, keep);

    // Always keep Rest Stops so they aren't pruned away.
    for (var i = 0; i < points.length; i++) {
      if (points[i].isStop) keep[i] = true;
    }

    final result = <SmartWaypoint>[];
    for (var i = 0; i < points.length; i++) {
      if (keep[i]) result.add(points[i]);
    }
    return result;
  }

  static void _douglasPeuckerRecursive(
    List<SmartWaypoint> points,
    int startIndex,
    int endIndex,
    double epsilonMeters,
    List<bool> keep,
  ) {
    if (endIndex <= startIndex + 1) return;

    var maxDistance = -1.0;
    var maxIndex = -1;

    final start = points[startIndex];
    final end = points[endIndex];

    for (var i = startIndex + 1; i < endIndex; i++) {
      final distance = _perpendicularDistanceMeters(points[i], start, end);
      if (distance > maxDistance) {
        maxDistance = distance;
        maxIndex = i;
      }
    }

    if (maxDistance > epsilonMeters && maxIndex != -1) {
      keep[maxIndex] = true;
      _douglasPeuckerRecursive(
        points,
        startIndex,
        maxIndex,
        epsilonMeters,
        keep,
      );
      _douglasPeuckerRecursive(points, maxIndex, endIndex, epsilonMeters, keep);
    }
  }

  /// Approximate perpendicular distance (in meters) from [point] to the
  /// line segment [lineStart]–[lineEnd], using an equirectangular
  /// projection. Accurate enough for typical hike/hunt route scales
  /// (a few hundred meters to tens of kilometers).
  static double _perpendicularDistanceMeters(
    SmartWaypoint point,
    SmartWaypoint lineStart,
    SmartWaypoint lineEnd,
  ) {
    final refLatRad = _degToRad(lineStart.latitude);

    double x(double lng) =>
        _degToRad(lng) * math.cos(refLatRad) * _earthRadiusMeters;
    double y(double lat) => _degToRad(lat) * _earthRadiusMeters;

    final px = x(point.longitude);
    final py = y(point.latitude);
    final ax = x(lineStart.longitude);
    final ay = y(lineStart.latitude);
    final bx = x(lineEnd.longitude);
    final by = y(lineEnd.latitude);

    final dx = bx - ax;
    final dy = by - ay;

    if (dx == 0 && dy == 0) {
      return math.sqrt(math.pow(px - ax, 2) + math.pow(py - ay, 2));
    }

    final t = ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy);
    final clampedT = t.clamp(0.0, 1.0);

    final closestX = ax + clampedT * dx;
    final closestY = ay + clampedT * dy;

    return math.sqrt(math.pow(px - closestX, 2) + math.pow(py - closestY, 2));
  }

  // ---------------------------------------------------------------------
  // Step 3: Resample to a fixed keyframe count
  // ---------------------------------------------------------------------

  /// Produces exactly [targetCount] keyframes, evenly distributed across
  /// the timeline of [points]. Rest Stop markers are always preserved and
  /// injected at their nearest matching slot so no stop is ever lost,
  /// even if the route is longer than [targetCount] would naturally allow.
  ///
  /// If [points] already has fewer entries than [targetCount], the
  /// original list is returned unchanged (no need to pad — the playback
  /// engine can simply interpolate between them).
  static List<SmartWaypoint> resampleToFixedCount(
    List<SmartWaypoint> points,
    int targetCount,
  ) {
    if (points.isEmpty) return const [];
    if (points.length <= targetCount) return List.of(points);

    final stops = points.where((p) => p.isStop).toList(growable: false);
    final nonStopCount = targetCount - stops.length;

    if (nonStopCount <= 1) {
      // Extreme edge case: too many stops to fit target count.
      // Fall back to plain even sampling including stops.
      return _evenIndexSample(points, targetCount);
    }

    final movingPoints = points.where((p) => !p.isStop).toList(growable: false);
    final sampledMoving = _evenIndexSample(movingPoints, nonStopCount);

    final merged = <SmartWaypoint>[...sampledMoving, ...stops]
      ..sort((a, b) => a.timestamp.compareTo(b.timestamp));

    // Guard against float/edge rounding producing an off-by-one count.
    if (merged.length > targetCount) {
      return _evenIndexSample(merged, targetCount);
    }
    return merged;
  }

  static List<SmartWaypoint> _evenIndexSample(
    List<SmartWaypoint> points,
    int targetCount,
  ) {
    if (points.isEmpty || targetCount <= 0) return const [];
    if (points.length <= targetCount) return List.of(points);
    if (targetCount == 1) return [points.first];

    final result = <SmartWaypoint>[];
    final lastIndex = points.length - 1;

    for (var i = 0; i < targetCount; i++) {
      final index = (i * lastIndex / (targetCount - 1)).round();
      result.add(points[index]);
    }
    return result;
  }

  // ---------------------------------------------------------------------
  // Shared geometry helpers
  // ---------------------------------------------------------------------

  static double _degToRad(double degrees) => degrees * math.pi / 180.0;

  static double _haversineMeters(
    double lat1,
    double lon1,
    double lat2,
    double lon2,
  ) {
    final dLat = _degToRad(lat2 - lat1);
    final dLon = _degToRad(lon2 - lon1);

    final a =
        math.pow(math.sin(dLat / 2), 2) +
        math.cos(_degToRad(lat1)) *
            math.cos(_degToRad(lat2)) *
            math.pow(math.sin(dLon / 2), 2);

    final c = 2 * math.atan2(math.sqrt(a), math.sqrt(1 - a));
    return _earthRadiusMeters * c;
  }
}
