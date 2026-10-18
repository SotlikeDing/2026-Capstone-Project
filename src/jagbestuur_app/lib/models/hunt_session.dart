import '../utils/json_parsing.dart';
import 'hunt_pin.dart';
import 'smart_waypoint.dart';

/// A finalized, optimized recording of a single hunt/hike.
///
/// This is what gets written to disk once tracking stops, and what
/// [HikeRecapScreen] consumes to render the recap animation.
class HuntSession {
  const HuntSession({
    required this.id,
    required this.startedAt,
    required this.endedAt,
    required this.totalDistanceMeters,
    required this.waypoints,
    this.pins = const [],
  });

  /// Unique id — also used as the filename (millis since epoch of start).
  final String id;

  final DateTime startedAt;
  final DateTime endedAt;

  /// Total distance walked, in meters (computed live during tracking).
  final double totalDistanceMeters;

  /// The OPTIMIZED keyframe list (already passed through RouteOptimizer).
  /// Ready to feed directly into HikeRecapScreen.
  final List<SmartWaypoint> waypoints;

  /// Manually-dropped pins (shot fired, animal found, etc.), never
  /// simplified/optimized — preserved exactly as placed by the user.
  final List<HuntPin> pins;

  Duration get duration => endedAt.difference(startedAt);

  Map<String, dynamic> toJson() => {
    'id': id,
    'startedAt': toUtcIso(startedAt),
    'endedAt': toUtcIso(endedAt),
    'totalDistanceMeters': totalDistanceMeters,
    'waypoints': waypoints.map((w) => w.toJson()).toList(),
    'pins': pins.map((p) => p.toJson()).toList(),
  };

  factory HuntSession.fromJson(Map<String, dynamic> json) {
    final waypointsJson = json['waypoints'] as List<dynamic>;
    final pinsJson = (json['pins'] as List<dynamic>?) ?? const [];
    return HuntSession(
      id: json['id'] as String,
      startedAt: DateTime.parse(json['startedAt'] as String).toLocal(),
      endedAt: DateTime.parse(json['endedAt'] as String).toLocal(),
      totalDistanceMeters: (json['totalDistanceMeters'] as num).toDouble(),
      waypoints: waypointsJson
          .map((w) => SmartWaypoint.fromJson(w as Map<String, dynamic>))
          .toList(),
      pins: pinsJson
          .map((p) => HuntPin.fromJson(p as Map<String, dynamic>))
          .toList(),
    );
  }
}
