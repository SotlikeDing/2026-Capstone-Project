import '../utils/json_parsing.dart';

enum HuntPhotoMatchMethod { gps, timestamp, pin, manual, unmatched }

enum HuntPhotoMatchConfidence { high, medium, low, none }

/// A private on-device hunt photo. The image bytes live at [localPath]; this
/// record is deliberately metadata-only so manifests stay small and safe.
class HuntPhoto {
  const HuntPhoto({
    required this.id,
    required this.sessionId,
    required this.localPath,
    required this.caption,
    required this.sortOrder,
    required this.fileSizeBytes,
    required this.width,
    required this.height,
    required this.fileType,
    required this.metadataAvailable,
    required this.exifStripped,
    required this.matchMethod,
    required this.matchConfidence,
    required this.needsConfirmation,
    required this.createdAt,
    required this.updatedAt,
    this.trophyId,
    this.originalCapturedAt,
    this.adjustedTimestamp,
    this.latitude,
    this.longitude,
    this.orientation,
    this.matchedWaypointIndex,
    this.matchedPinId,
  });

  final String id;
  final String sessionId;
  final int? trophyId;
  final String localPath;
  final DateTime? originalCapturedAt;
  final DateTime? adjustedTimestamp;
  final double? latitude;
  final double? longitude;
  final int? orientation;
  final int? matchedWaypointIndex;
  final String? matchedPinId;
  final String caption;
  final int sortOrder;
  final int fileSizeBytes;
  final int width;
  final int height;
  final String fileType;
  final bool metadataAvailable;
  final bool exifStripped;
  final HuntPhotoMatchMethod matchMethod;
  final HuntPhotoMatchConfidence matchConfidence;
  final bool needsConfirmation;
  final DateTime createdAt;
  final DateTime updatedAt;

  DateTime? get effectiveTimestamp => adjustedTimestamp ?? originalCapturedAt;

  HuntPhoto copyWith({
    int? trophyId,
    bool clearTrophyId = false,
    DateTime? adjustedTimestamp,
    bool clearAdjustedTimestamp = false,
    double? latitude,
    double? longitude,
    int? matchedWaypointIndex,
    bool clearMatchedWaypoint = false,
    String? matchedPinId,
    bool clearMatchedPin = false,
    String? caption,
    int? sortOrder,
    HuntPhotoMatchMethod? matchMethod,
    HuntPhotoMatchConfidence? matchConfidence,
    bool? needsConfirmation,
    DateTime? updatedAt,
  }) {
    return HuntPhoto(
      id: id,
      sessionId: sessionId,
      trophyId: clearTrophyId ? null : trophyId ?? this.trophyId,
      localPath: localPath,
      originalCapturedAt: originalCapturedAt,
      adjustedTimestamp: clearAdjustedTimestamp
          ? null
          : adjustedTimestamp ?? this.adjustedTimestamp,
      latitude: latitude ?? this.latitude,
      longitude: longitude ?? this.longitude,
      orientation: orientation,
      matchedWaypointIndex: clearMatchedWaypoint
          ? null
          : matchedWaypointIndex ?? this.matchedWaypointIndex,
      matchedPinId: clearMatchedPin ? null : matchedPinId ?? this.matchedPinId,
      caption: caption ?? this.caption,
      sortOrder: sortOrder ?? this.sortOrder,
      fileSizeBytes: fileSizeBytes,
      width: width,
      height: height,
      fileType: fileType,
      metadataAvailable: metadataAvailable,
      exifStripped: exifStripped,
      matchMethod: matchMethod ?? this.matchMethod,
      matchConfidence: matchConfidence ?? this.matchConfidence,
      needsConfirmation: needsConfirmation ?? this.needsConfirmation,
      createdAt: createdAt,
      updatedAt: updatedAt ?? DateTime.now(),
    );
  }

  Map<String, dynamic> toJson() => <String, dynamic>{
    'id': id,
    'sessionId': sessionId,
    'trophyId': trophyId,
    'localPath': localPath,
    'originalCapturedAt': originalCapturedAt == null
        ? null
        : toUtcIso(originalCapturedAt!),
    'adjustedTimestamp': adjustedTimestamp == null
        ? null
        : toUtcIso(adjustedTimestamp!),
    'latitude': latitude,
    'longitude': longitude,
    'orientation': orientation,
    'matchedWaypointIndex': matchedWaypointIndex,
    'matchedPinId': matchedPinId,
    'caption': caption,
    'sortOrder': sortOrder,
    'fileSizeBytes': fileSizeBytes,
    'width': width,
    'height': height,
    'fileType': fileType,
    'metadataAvailable': metadataAvailable,
    'exifStripped': exifStripped,
    'matchMethod': matchMethod.name,
    'matchConfidence': matchConfidence.name,
    'needsConfirmation': needsConfirmation,
    'createdAt': toUtcIso(createdAt),
    'updatedAt': toUtcIso(updatedAt),
  };

  factory HuntPhoto.fromJson(Map<String, dynamic> json) {
    return HuntPhoto(
      id: json['id'] as String,
      sessionId: json['sessionId'] as String,
      trophyId: (json['trophyId'] as num?)?.toInt(),
      localPath: json['localPath'] as String,
      originalCapturedAt: tryParseDate(json['originalCapturedAt']),
      adjustedTimestamp: tryParseDate(json['adjustedTimestamp']),
      latitude: (json['latitude'] as num?)?.toDouble(),
      longitude: (json['longitude'] as num?)?.toDouble(),
      orientation: (json['orientation'] as num?)?.toInt(),
      matchedWaypointIndex: (json['matchedWaypointIndex'] as num?)?.toInt(),
      matchedPinId: json['matchedPinId'] as String?,
      caption: json['caption'] as String? ?? '',
      sortOrder: (json['sortOrder'] as num?)?.toInt() ?? 0,
      fileSizeBytes: (json['fileSizeBytes'] as num?)?.toInt() ?? 0,
      width: (json['width'] as num?)?.toInt() ?? 0,
      height: (json['height'] as num?)?.toInt() ?? 0,
      fileType: json['fileType'] as String? ?? 'unknown',
      metadataAvailable: json['metadataAvailable'] == true,
      exifStripped: json['exifStripped'] == true,
      matchMethod: HuntPhotoMatchMethod.values.firstWhere(
        (value) => value.name == json['matchMethod'],
        orElse: () => HuntPhotoMatchMethod.unmatched,
      ),
      matchConfidence: HuntPhotoMatchConfidence.values.firstWhere(
        (value) => value.name == json['matchConfidence'],
        orElse: () => HuntPhotoMatchConfidence.none,
      ),
      needsConfirmation: json['needsConfirmation'] == true,
      createdAt: tryParseDate(json['createdAt']) ?? DateTime.now(),
      updatedAt:
          tryParseDate(json['updatedAt']) ??
          tryParseDate(json['createdAt']) ??
          DateTime.now(),
    );
  }
}

class HuntPhotoMetadata {
  const HuntPhotoMetadata({
    required this.fileSizeBytes,
    required this.width,
    required this.height,
    required this.fileType,
    required this.metadataAvailable,
    this.capturedAt,
    this.latitude,
    this.longitude,
    this.orientation,
  });

  final int fileSizeBytes;
  final int width;
  final int height;
  final String fileType;
  final bool metadataAvailable;
  final DateTime? capturedAt;
  final double? latitude;
  final double? longitude;
  final int? orientation;
}

class HuntPhotoMatch {
  const HuntPhotoMatch({
    required this.method,
    required this.confidence,
    required this.needsConfirmation,
    this.waypointIndex,
    this.pinId,
  });

  const HuntPhotoMatch.unmatched()
    : method = HuntPhotoMatchMethod.unmatched,
      confidence = HuntPhotoMatchConfidence.none,
      needsConfirmation = true,
      waypointIndex = null,
      pinId = null;

  final HuntPhotoMatchMethod method;
  final HuntPhotoMatchConfidence confidence;
  final bool needsConfirmation;
  final int? waypointIndex;
  final String? pinId;
}

/// Future narrative providers can consume this local recap input. Deliberately
/// contains no provider, prompt, API key, network call, or generated story.
class HuntRecapStoryInput {
  const HuntRecapStoryInput({required this.sessionId, required this.photoIds});

  final String sessionId;
  final List<String> photoIds;
}
