import '../config/app_config.dart';
import '../utils/json_parsing.dart';

class SharedBookingMedia {
  const SharedBookingMedia({
    required this.id,
    required this.bookingId,
    required this.uploaderUserId,
    required this.uploaderRole,
    required this.originalFileName,
    required this.contentType,
    required this.fileSizeBytes,
    required this.width,
    required this.height,
    required this.matchMethod,
    required this.matchConfidence,
    required this.inclusionStatus,
    required this.createdAt,
    required this.downloadPath,
    this.originalCaptureTimestamp,
    this.adjustedCaptureTimestamp,
    this.latitude,
    this.longitude,
    this.orientation,
    this.caption,
    this.matchedRoutePointIndex,
    this.matchedPinId,
    this.sourceType = 'Photo',
    this.durationSeconds,
  });

  final int id;
  final int bookingId;
  final int uploaderUserId;
  final String uploaderRole;
  final String originalFileName;
  final String contentType;
  final int fileSizeBytes;
  final int width;
  final int height;
  final DateTime? originalCaptureTimestamp;
  final DateTime? adjustedCaptureTimestamp;
  final double? latitude;
  final double? longitude;
  final int? orientation;
  final String? caption;
  final String matchMethod;
  final String matchConfidence;
  final int? matchedRoutePointIndex;
  final String? matchedPinId;
  final String inclusionStatus;
  final DateTime createdAt;
  final String downloadPath;

  /// 'Photo' or 'Video' (a short VG clip that plays, muted, inside the recap).
  final String sourceType;
  final double? durationSeconds;

  bool get isVideo => sourceType == 'Video';
  String get imageUrl => AppConfig.resolveApiUrl(downloadPath);
  bool get needsConfirmation => matchConfidence != 'High';

  factory SharedBookingMedia.fromJson(
    Map<String, dynamic> json,
  ) => SharedBookingMedia(
    id:
        tryParseInt(json['mediaId']) ??
        (throw const FormatException('mediaId')),
    bookingId: tryParseInt(json['bookingId']) ?? 0,
    uploaderUserId: tryParseInt(json['uploaderUserId']) ?? 0,
    uploaderRole: json['uploaderRole'] as String? ?? '',
    originalFileName: json['originalFileName'] as String? ?? 'photo',
    contentType: json['contentType'] as String? ?? 'image/jpeg',
    fileSizeBytes: tryParseInt(json['fileSizeBytes']) ?? 0,
    width: tryParseInt(json['width']) ?? 0,
    height: tryParseInt(json['height']) ?? 0,
    originalCaptureTimestamp: tryParseDate(json['originalCaptureTimestamp']),
    adjustedCaptureTimestamp: tryParseDate(json['adjustedCaptureTimestamp']),
    latitude: (json['latitude'] as num?)?.toDouble(),
    longitude: (json['longitude'] as num?)?.toDouble(),
    orientation: tryParseInt(json['orientation']),
    caption: json['caption'] as String?,
    matchMethod: json['matchMethod'] as String? ?? 'Unmatched',
    matchConfidence: json['matchConfidence'] as String? ?? 'None',
    matchedRoutePointIndex: tryParseInt(json['matchedRoutePointIndex']),
    matchedPinId: json['matchedPinId'] as String?,
    inclusionStatus: json['inclusionStatus'] as String? ?? 'Included',
    createdAt: tryParseDate(json['createdAt']) ?? DateTime.now(),
    downloadPath: json['downloadPath'] as String? ?? '',
    sourceType: json['sourceType'] as String? ?? 'Photo',
    durationSeconds: (json['durationSeconds'] as num?)?.toDouble(),
  );
}

class BookingRouteSummary {
  const BookingRouteSummary({
    required this.id,
    required this.bookingId,
    required this.ownerUserId,
    required this.sourceHuntSessionId,
    required this.pointCount,
    required this.distanceMeters,
    required this.durationSeconds,
    required this.routeVersion,
    required this.status,
    required this.submittedAt,
    required this.selectedForRecap,
    required this.pins,
  });

  final int id;
  final int bookingId;
  final int ownerUserId;
  final String sourceHuntSessionId;
  final int pointCount;
  final double distanceMeters;
  final int durationSeconds;
  final int routeVersion;
  final String status;
  final DateTime submittedAt;
  final bool selectedForRecap;
  final List<BookingRoutePin> pins;

  factory BookingRouteSummary.fromJson(Map<String, dynamic> json) =>
      BookingRouteSummary(
        id:
            tryParseInt(json['routeId']) ??
            (throw const FormatException('routeId')),
        bookingId: tryParseInt(json['bookingId']) ?? 0,
        ownerUserId: tryParseInt(json['ownerUserId']) ?? 0,
        sourceHuntSessionId: json['sourceHuntSessionId'] as String? ?? '',
        pointCount: tryParseInt(json['pointCount']) ?? 0,
        distanceMeters: (json['distanceMeters'] as num?)?.toDouble() ?? 0,
        durationSeconds: tryParseInt(json['durationSeconds']) ?? 0,
        routeVersion: tryParseInt(json['routeVersion']) ?? 1,
        status: json['status'] as String? ?? 'Submitted',
        submittedAt: tryParseDate(json['submittedAt']) ?? DateTime.now(),
        selectedForRecap: json['selectedForRecap'] as bool? ?? false,
        pins: (json['pins'] as List<dynamic>? ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(BookingRoutePin.fromJson)
            .toList(growable: false),
      );
}

class BookingRoutePin {
  const BookingRoutePin({
    required this.id,
    required this.type,
    required this.timestamp,
    this.note,
  });

  final String id;
  final String type;
  final DateTime timestamp;
  final String? note;

  factory BookingRoutePin.fromJson(Map<String, dynamic> json) =>
      BookingRoutePin(
        id: json['id'] as String? ?? '',
        type: json['type'] as String? ?? 'Pin',
        timestamp: tryParseDate(json['timestamp']) ?? DateTime.now(),
        note: json['note'] as String?,
      );
}

class BookingRecap {
  const BookingRecap({
    required this.id,
    required this.bookingId,
    required this.status,
    required this.progress,
    required this.title,
    required this.uploadStatus,
    required this.uploadProgress,
    required this.deletionStatus,
    required this.createdAt,
    this.failureReason,
    this.youtubeUrl,
    this.youtubePrivacyStatus,
    this.youtubeProcessingStatus,
    this.youtubeVerifiedAt,
    this.playbackPath,
    this.completedAt,
  });

  final int id;
  final int bookingId;
  final String status;
  final int progress;
  final String title;
  final String uploadStatus;
  final int uploadProgress;
  final String deletionStatus;
  final String? failureReason;
  final String? youtubeUrl;
  final String? youtubePrivacyStatus;
  final String? youtubeProcessingStatus;
  final DateTime? youtubeVerifiedAt;
  final String? playbackPath;
  final DateTime createdAt;
  final DateTime? completedAt;

  bool get isBusy =>
      const {'Queued', 'Processing', 'Uploading'}.contains(status) ||
      const {
        'Queued',
        'Uploading',
        'Uploaded',
        'Processing',
      }.contains(uploadStatus) ||
      deletionStatus == 'CleanupPending';
  bool get canPublish =>
      status == 'Completed' && uploadStatus == 'NotRequested';
  bool get isPublished => uploadStatus == 'Published';
  String? get playbackUrl =>
      playbackPath == null ? null : AppConfig.resolveApiUrl(playbackPath!);

  factory BookingRecap.fromJson(Map<String, dynamic> json) => BookingRecap(
    id:
        tryParseInt(json['recapId']) ??
        (throw const FormatException('recapId')),
    bookingId: tryParseInt(json['bookingId']) ?? 0,
    status: json['status'] as String? ?? 'Ready',
    progress: tryParseInt(json['progress']) ?? 0,
    title: json['title'] as String? ?? 'Jag-terugblik',
    uploadStatus: json['uploadStatus'] as String? ?? 'NotRequested',
    uploadProgress: tryParseInt(json['uploadProgress']) ?? 0,
    deletionStatus: json['deletionStatus'] as String? ?? 'NotRequested',
    failureReason: json['failureReason'] as String?,
    youtubeUrl: json['youtubeUrl'] as String?,
    youtubePrivacyStatus: json['youtubePrivacyStatus'] as String?,
    youtubeProcessingStatus: json['youtubeProcessingStatus'] as String?,
    youtubeVerifiedAt: tryParseDate(json['youtubeVerifiedAt']),
    playbackPath: json['playbackPath'] as String?,
    createdAt: tryParseDate(json['createdAt']) ?? DateTime.now(),
    completedAt: tryParseDate(json['completedAt']),
  );
}

class RecapCleanupStatus {
  const RecapCleanupStatus({
    required this.status,
    required this.notBefore,
    required this.attemptCount,
    this.failureReason,
    this.cleanupStartedAt,
    this.cleanupCompletedAt,
    this.sourceFilesDeletedAt,
  });

  final String status;
  final DateTime notBefore;
  final int attemptCount;
  final String? failureReason;
  final DateTime? cleanupStartedAt;
  final DateTime? cleanupCompletedAt;
  final DateTime? sourceFilesDeletedAt;

  bool get isComplete => sourceFilesDeletedAt != null || status == 'Completed';

  factory RecapCleanupStatus.fromJson(Map<String, dynamic> json) =>
      RecapCleanupStatus(
        status: json['status'] as String? ?? 'CleanupPending',
        notBefore:
            tryParseDate(json['notBefore']) ??
            (throw const FormatException('notBefore')),
        attemptCount: tryParseInt(json['attemptCount']) ?? 0,
        failureReason: json['failureReason'] as String?,
        cleanupStartedAt: tryParseDate(json['cleanupStartedAt']),
        cleanupCompletedAt: tryParseDate(json['cleanupCompletedAt']),
        sourceFilesDeletedAt: tryParseDate(json['sourceFilesDeletedAt']),
      );
}

class TrophySubmission {
  const TrophySubmission({
    required this.id,
    required this.bookingId,
    required this.bookingTitle,
    required this.hunterUserId,
    required this.hunterName,
    required this.species,
    required this.trophyPhotoPath,
    required this.harvestDate,
    required this.locationText,
    required this.firearmType,
    required this.status,
    required this.supportingPhotoCount,
    required this.supportingMediaIds,
    required this.createdAt,
    this.shotDistanceMeters,
    this.antlerSizeCm,
    this.carcassWeightKg,
    this.notes,
    this.reviewNotes,
  });

  final int id;
  final int bookingId;
  final String bookingTitle;
  final int hunterUserId;
  final String hunterName;
  final String species;
  final String trophyPhotoPath;
  final double? antlerSizeCm;
  final double? carcassWeightKg;
  final DateTime harvestDate;
  final String locationText;
  final int? shotDistanceMeters;
  final String firearmType;
  final String? notes;
  final String status;
  final String? reviewNotes;
  final int supportingPhotoCount;
  final List<int> supportingMediaIds;
  final DateTime createdAt;

  String get photoUrl => AppConfig.resolveApiUrl(trophyPhotoPath);

  factory TrophySubmission.fromJson(Map<String, dynamic> json) =>
      TrophySubmission(
        id:
            tryParseInt(json['submissionId']) ??
            (throw const FormatException('submissionId')),
        bookingId: tryParseInt(json['bookingId']) ?? 0,
        bookingTitle: json['bookingTitle'] as String? ?? 'Bespreking',
        hunterUserId: tryParseInt(json['hunterUserId']) ?? 0,
        hunterName: json['hunterName'] as String? ?? 'Jagter',
        species: json['species'] as String? ?? '',
        trophyPhotoPath: json['trophyPhotoPath'] as String? ?? '',
        antlerSizeCm: (json['antlerSizeCm'] as num?)?.toDouble(),
        carcassWeightKg: (json['carcassWeightKg'] as num?)?.toDouble(),
        harvestDate: tryParseDate(json['harvestDate']) ?? DateTime.now(),
        locationText: json['locationText'] as String? ?? '',
        shotDistanceMeters: tryParseInt(json['shotDistanceMeters']),
        firearmType: json['firearmType'] as String? ?? '',
        notes: json['notes'] as String?,
        status: json['status'] as String? ?? 'Pending',
        reviewNotes: json['reviewNotes'] as String?,
        supportingPhotoCount: tryParseInt(json['supportingPhotoCount']) ?? 0,
        supportingMediaIds:
            (json['supportingMediaIds'] as List<dynamic>? ?? const [])
                .map(tryParseInt)
                .whereType<int>()
                .toList(growable: false),
        createdAt: tryParseDate(json['createdAt']) ?? DateTime.now(),
      );
}

/// One person the signed-in participant can rate on a finished booking.
class BookingTeamMember {
  const BookingTeamMember({
    required this.userId,
    required this.name,
    required this.roleCode,
    this.myStars,
    this.myComment,
  });

  final int userId;
  final String name;

  /// HUNTER, PH or VG - the capacity they were on the booking in.
  final String roleCode;
  final int? myStars;
  final String? myComment;

  String get roleLabel => switch (roleCode) {
    'HUNTER' => 'Jagter',
    'PH' => 'Professionele jagter',
    'VG' => 'Videograaf',
    _ => roleCode,
  };

  factory BookingTeamMember.fromJson(Map<String, dynamic> json) =>
      BookingTeamMember(
        userId:
            tryParseInt(json['userId']) ??
            (throw const FormatException('userId')),
        name: json['name'] as String? ?? '',
        roleCode: json['roleCode'] as String? ?? '',
        myStars: tryParseInt(json['myStars']),
        myComment: json['myComment'] as String?,
      );
}

/// The "rate your team" view of one booking (relayed from the website's rating rules).
class BookingTeamRating {
  const BookingTeamRating({
    required this.bookingId,
    required this.canRate,
    required this.members,
    this.reason,
  });

  final int bookingId;
  final bool canRate;
  final String? reason;
  final List<BookingTeamMember> members;

  factory BookingTeamRating.fromJson(Map<String, dynamic> json) =>
      BookingTeamRating(
        bookingId: tryParseInt(json['bookingId']) ?? 0,
        canRate: json['canRate'] == true,
        reason: json['reason'] as String?,
        members: [
          for (final item in (json['members'] as List<dynamic>? ?? const []))
            if (item is Map<String, dynamic>) BookingTeamMember.fromJson(item),
        ],
      );
}
