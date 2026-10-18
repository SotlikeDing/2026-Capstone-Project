import '../utils/json_parsing.dart';

/// The kind of event a manually-dropped pin represents during a hunt.
enum HuntPinType {
  shotFired,
  animalFound,
  campsite,
  waterSource,
  hazard,
  custom;

  String get label {
    switch (this) {
      case HuntPinType.shotFired:
        return 'Skoot geskiet';
      case HuntPinType.animalFound:
        return 'Dier gevind';
      case HuntPinType.campsite:
        return 'Kampplek';
      case HuntPinType.waterSource:
        return 'Water';
      case HuntPinType.hazard:
        return 'Gevaar';
      case HuntPinType.custom:
        return 'Ander';
    }
  }

  /// Material icon codepoint name used for map markers / list rows.
  String get iconName {
    switch (this) {
      case HuntPinType.shotFired:
        return 'gps_fixed';
      case HuntPinType.animalFound:
        return 'pets';
      case HuntPinType.campsite:
        return 'cabin';
      case HuntPinType.waterSource:
        return 'water_drop';
      case HuntPinType.hazard:
        return 'warning_amber_rounded';
      case HuntPinType.custom:
        return 'push_pin';
    }
  }
}

/// A pin marking a specific event/location during a hunt
/// (e.g. "shot fired here", "animal found here"). Unlike [SmartWaypoint]
/// route points, pins are never simplified/optimized away — they represent
/// deliberate user intent and must remain exactly where placed.
class HuntPin {
  const HuntPin({
    required this.id,
    required this.type,
    required this.latitude,
    required this.longitude,
    required this.timestamp,
    this.note,
    this.photoPath,
    this.autoDetected = false,
    this.confidence,
    this.confirmed = false,
  });

  final String id;
  final HuntPinType type;
  final double latitude;
  final double longitude;
  final DateTime timestamp;
  final String? note;
  final String? photoPath;

  /// Dropped by Outo-skoot (gunshot detection) rather than by the hunter.
  final bool autoDetected;

  /// Outo-skoot's confidence (0-1) for an automatic pin.
  final double? confidence;

  /// The hunter confirmed an automatic pin (manual pins never need it).
  final bool confirmed;

  /// An automatic pin the hunter has not judged yet - drawn differently.
  bool get needsReview => autoDetected && !confirmed;

  HuntPin copyWith({String? note, String? photoPath, double? confidence, bool? confirmed}) {
    return HuntPin(
      id: id,
      type: type,
      latitude: latitude,
      longitude: longitude,
      timestamp: timestamp,
      note: note ?? this.note,
      photoPath: photoPath ?? this.photoPath,
      autoDetected: autoDetected,
      confidence: confidence ?? this.confidence,
      confirmed: confirmed ?? this.confirmed,
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'type': type.name,
    'latitude': latitude,
    'longitude': longitude,
    'timestamp': toUtcIso(timestamp),
    'note': note,
    'photoPath': photoPath,
    if (autoDetected) 'autoDetected': true,
    if (confidence != null) 'confidence': confidence,
    if (confirmed) 'confirmed': true,
  };

  factory HuntPin.fromJson(Map<String, dynamic> json) {
    return HuntPin(
      id: json['id'] as String,
      type: HuntPinType.values.firstWhere(
        (t) => t.name == json['type'],
        orElse: () => HuntPinType.custom,
      ),
      latitude: (json['latitude'] as num).toDouble(),
      longitude: (json['longitude'] as num).toDouble(),
      timestamp: DateTime.parse(json['timestamp'] as String).toLocal(),
      note: json['note'] as String?,
      photoPath: json['photoPath'] as String?,
      // Absent in pins saved before Outo-skoot existed.
      autoDetected: json['autoDetected'] == true,
      confidence: (json['confidence'] as num?)?.toDouble(),
      confirmed: json['confirmed'] == true,
    );
  }
}
