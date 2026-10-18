import '../utils/json_parsing.dart';

/// A single point captured during a hunt/hike trek.
///
/// Can represent either a moving GPS fix or a collapsed "Rest Stop"
/// (when [isStop] is true and [stopDuration] is set).
class SmartWaypoint {
  const SmartWaypoint({
    required this.latitude,
    required this.longitude,
    required this.timestamp,
    this.isStop = false,
    this.stopDuration,
    this.localPhotoPath,
  });

  final double latitude;
  final double longitude;
  final DateTime timestamp;
  final bool isStop;
  final Duration? stopDuration;
  final String? localPhotoPath;

  /// Convenience getter — true only when this waypoint carries a photo.
  bool get hasPhoto => localPhotoPath != null && localPhotoPath!.isNotEmpty;

  SmartWaypoint copyWith({
    double? latitude,
    double? longitude,
    DateTime? timestamp,
    bool? isStop,
    Duration? stopDuration,
    String? localPhotoPath,
  }) {
    return SmartWaypoint(
      latitude: latitude ?? this.latitude,
      longitude: longitude ?? this.longitude,
      timestamp: timestamp ?? this.timestamp,
      isStop: isStop ?? this.isStop,
      stopDuration: stopDuration ?? this.stopDuration,
      localPhotoPath: localPhotoPath ?? this.localPhotoPath,
    );
  }

  factory SmartWaypoint.fromJson(Map<String, dynamic> json) {
    return SmartWaypoint(
      latitude: (json['latitude'] as num).toDouble(),
      longitude: (json['longitude'] as num).toDouble(),
      timestamp: DateTime.parse(json['timestamp'] as String).toLocal(),
      isStop: json['isStop'] as bool? ?? false,
      stopDuration: json['stopDurationSeconds'] != null
          ? Duration(seconds: json['stopDurationSeconds'] as int)
          : null,
      localPhotoPath: json['localPhotoPath'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'latitude': latitude,
      'longitude': longitude,
      'timestamp': toUtcIso(timestamp),
      'isStop': isStop,
      if (stopDuration != null) 'stopDurationSeconds': stopDuration!.inSeconds,
      if (localPhotoPath != null) 'localPhotoPath': localPhotoPath,
    };
  }

  static List<SmartWaypoint> listFromJson(List<dynamic> jsonList) {
    return jsonList
        .map((e) => SmartWaypoint.fromJson(e as Map<String, dynamic>))
        .toList(growable: false);
  }

  static List<Map<String, dynamic>> listToJson(List<SmartWaypoint> points) {
    return points.map((p) => p.toJson()).toList(growable: false);
  }

  @override
  String toString() {
    return 'SmartWaypoint(lat: $latitude, lng: $longitude, t: $timestamp, '
        'isStop: $isStop, stopDuration: $stopDuration, photo: $localPhotoPath)';
  }

  @override
  bool operator ==(Object other) {
    if (identical(this, other)) return true;
    return other is SmartWaypoint &&
        other.latitude == latitude &&
        other.longitude == longitude &&
        other.timestamp == timestamp &&
        other.isStop == isStop &&
        other.stopDuration == stopDuration &&
        other.localPhotoPath == localPhotoPath;
  }

  @override
  int get hashCode => Object.hash(
    latitude,
    longitude,
    timestamp,
    isStop,
    stopDuration,
    localPhotoPath,
  );
}
