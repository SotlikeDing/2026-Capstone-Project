import 'package:dio/dio.dart';

import '../models/hunt_photo.dart';
import '../models/hunt_session.dart';
import '../models/shared_booking.dart';
import 'api_client.dart';
import 'api_error.dart';

class BookingFeatureService {
  BookingFeatureService({Dio? client})
    : _client = client ?? ApiClient.instance.dio;
  final Dio _client;

  Future<List<SharedBookingMedia>> listMedia(int bookingId) => guardApi(
    () async => parseEach(
      requireList((await _client.get('/api/bookings/$bookingId/media')).data),
      SharedBookingMedia.fromJson,
      label: 'booking media',
    ),
    fallback: 'Kon nie gedeelde jagmedia laai nie.',
  );

  Future<SharedBookingMedia> uploadPhoto(
    int bookingId,
    String path,
    HuntPhotoMetadata metadata, {
    ProgressCallback? onProgress,
  }) => guardApi(() async {
    final response = await _client.post(
      '/api/bookings/$bookingId/media',
      data: FormData.fromMap({
        'photo': await MultipartFile.fromFile(path),
        if (metadata.capturedAt != null)
          'originalCaptureTimestamp': metadata.capturedAt!
              .toUtc()
              .toIso8601String(),
        if (metadata.latitude != null) 'latitude': metadata.latitude,
        if (metadata.longitude != null) 'longitude': metadata.longitude,
        if (metadata.orientation != null) 'orientation': metadata.orientation,
      }),
      onSendProgress: onProgress,
    );
    return SharedBookingMedia.fromJson(requireMap(response.data));
  }, fallback: 'Kon nie die foto oplaai nie. Probeer gerus weer.');

  /// Short VG clip (MP4/MOV, max 30 s) for the recap. The server validates it with
  /// ffprobe and never uses its audio.
  Future<SharedBookingMedia> uploadVideo(
    int bookingId,
    String path, {
    DateTime? capturedAt,
    String? caption,
    ProgressCallback? onProgress,
  }) => guardApi(() async {
    final response = await _client.post(
      '/api/bookings/$bookingId/media/videos',
      data: FormData.fromMap({
        'video': await MultipartFile.fromFile(path),
        if (capturedAt != null)
          'originalCaptureTimestamp': capturedAt.toUtc().toIso8601String(),
        if (caption != null && caption.trim().isNotEmpty)
          'caption': caption.trim(),
      }),
      onSendProgress: onProgress,
    );
    return SharedBookingMedia.fromJson(requireMap(response.data));
  }, fallback: 'Kon nie die videogreep oplaai nie. Probeer gerus weer.');

  /// The other participants the signed-in user can rate on a finished booking.
  Future<BookingTeamRating> getTeamRatings(int bookingId) => guardApi(
    () async => BookingTeamRating.fromJson(
      requireMap(
        (await _client.get('/api/bookings/$bookingId/team-ratings')).data,
      ),
    ),
    fallback: 'Kon nie die span vir gradering laai nie.',
  );

  /// Saves (or corrects) the signed-in user's rating of one participant.
  Future<void> rateTeamMember(
    int bookingId, {
    required int rateeUserId,
    required String rateeRoleCode,
    required int stars,
    String? comment,
  }) => guardApi(
    () async => _client.post(
      '/api/bookings/$bookingId/team-ratings',
      data: {
        'rateeUserId': rateeUserId,
        'rateeRoleCode': rateeRoleCode,
        'stars': stars,
        if (comment != null && comment.trim().isNotEmpty)
          'comment': comment.trim(),
      },
    ),
    fallback: 'Kon nie die gradering stoor nie.',
  );

  Future<void> updateMedia(
    int bookingId,
    int mediaId,
    Map<String, dynamic> data,
  ) => guardApi(
    () async =>
        _client.patch('/api/bookings/$bookingId/media/$mediaId', data: data),
    fallback: 'Kon nie die foto se plasing bywerk nie.',
  );

  Future<void> deleteMedia(int bookingId, int mediaId) => guardApi(
    () async => _client.delete('/api/bookings/$bookingId/media/$mediaId'),
    fallback: 'Kon nie die foto verwyder nie.',
  );

  Future<void> moderateMedia(
    int bookingId,
    int mediaId,
    String inclusionStatus,
    String? reason,
  ) => guardApi(
    () async => _client.post(
      '/api/bookings/$bookingId/media/$mediaId/moderation',
      data: {'inclusionStatus': inclusionStatus, 'reason': reason},
    ),
    fallback: 'Kon nie die foto se insluitingstatus verander nie.',
  );

  Future<List<BookingRouteSummary>> listRoutes(int bookingId) => guardApi(
    () async => parseEach(
      requireList((await _client.get('/api/bookings/$bookingId/routes')).data),
      BookingRouteSummary.fromJson,
      label: 'booking route',
    ),
    fallback: 'Kon nie die gedeelde roetes laai nie.',
  );

  Future<void> submitRoute(int bookingId, HuntSession session) => guardApi(
    () async => _client.post(
      '/api/bookings/$bookingId/routes',
      data: {
        'sourceHuntSessionId': session.id,
        'points': [
          for (final point in session.waypoints)
            {
              'latitude': point.latitude,
              'longitude': point.longitude,
              'timestamp': point.timestamp.toUtc().toIso8601String(),
            },
        ],
        'pins': [
          for (final pin in session.pins)
            {
              'id': pin.id,
              'type': pin.type.name,
              'latitude': pin.latitude,
              'longitude': pin.longitude,
              'timestamp': pin.timestamp.toUtc().toIso8601String(),
              'note': pin.note,
            },
        ],
        'distanceMeters': session.totalDistanceMeters,
        'durationSeconds': session.endedAt
            .difference(session.startedAt)
            .inSeconds,
        'routeVersion': 1,
      },
    ),
    fallback: 'Kon nie die jagroete indien nie.',
  );

  Future<void> selectRoute(int bookingId, int routeId) => guardApi(
    () async =>
        _client.put('/api/bookings/$bookingId/routes/$routeId/selection'),
    fallback: 'Kon nie die roete vir die terugblik kies nie.',
  );

  Future<List<BookingRecap>> listRecaps(int bookingId) => guardApi(
    () async => parseEach(
      requireList((await _client.get('/api/bookings/$bookingId/recaps')).data),
      BookingRecap.fromJson,
      label: 'booking recap',
    ),
    fallback: 'Kon nie recapstatus laai nie.',
  );

  Future<void> requestRecap(int bookingId, String title) => guardApi(
    () async =>
        _client.post('/api/bookings/$bookingId/recaps', data: {'title': title}),
    fallback: 'Kon nie recapgenerering begin nie.',
  );

  Future<void> retryRecap(int bookingId, int recapId) => guardApi(
    () async => _client.post('/api/bookings/$bookingId/recaps/$recapId/retry'),
    fallback: 'Kon nie die terugblik weer probeer nie.',
  );

  Future<void> publishRecap(int bookingId, int recapId) => guardApi(
    () async => _client.post(
      '/api/bookings/$bookingId/recaps/$recapId/publish',
      data: {'idempotencyKey': _uuid(), 'privacyStatus': 'private'},
    ),
    fallback: 'Kon nie YouTube-publikasie begin nie.',
  );

  Future<void> retryPublish(int bookingId, int recapId) => guardApi(
    () async =>
        _client.post('/api/bookings/$bookingId/recaps/$recapId/publish/retry'),
    fallback: 'Kon nie YouTube-publikasie weer probeer nie.',
  );

  Future<void> requestImmediateCleanup(int bookingId, int recapId) => guardApi(
    () async => _client.post(
      '/api/bookings/$bookingId/recaps/$recapId/cleanup',
      data: {'confirmImmediateCleanup': true, 'idempotencyKey': _uuid()},
    ),
    fallback: 'Kon nie veilige skoonmaak versoek nie.',
  );

  Future<RecapCleanupStatus> getCleanupStatus(int bookingId, int recapId) =>
      guardApi(
        () async => RecapCleanupStatus.fromJson(
          requireMap(
            (await _client.get(
              '/api/bookings/$bookingId/recaps/$recapId/cleanup',
            )).data,
          ),
        ),
        fallback: 'Kon nie die skoonmaaktyd laai nie.',
      );

  Future<List<TrophySubmission>> listTrophySubmissions() => guardApi(
    () async => parseEach(
      requireList((await _client.get('/api/trophy-submissions')).data),
      TrophySubmission.fromJson,
      label: 'trophy submission',
    ),
    fallback: 'Kon nie trofee-indienings laai nie.',
  );

  Future<void> submitTrophy(Map<String, dynamic> data) => guardApi(
    () async => _client.post('/api/trophy-submissions', data: data),
    fallback: 'Kon nie die trofee vir PH-goedkeuring indien nie.',
  );

  Future<void> reviewTrophy(int submissionId, String action, String? reason) =>
      guardApi(
        () async => _client.post(
          '/api/trophy-submissions/$submissionId/review',
          data: {'action': action, 'reason': reason, 'actionKey': _uuid()},
        ),
        fallback: 'Kon nie die trofee-hersiening stoor nie.',
      );

  Future<void> resubmitTrophy(
    int submissionId,
    String notes,
    String? trophyPhotoPath,
  ) => guardApi(
    () async => _client.post(
      '/api/trophy-submissions/$submissionId/resubmit',
      data: {'notes': notes, 'trophyPhotoPath': ?trophyPhotoPath},
    ),
    fallback: 'Kon nie die trofee-regstelling weer indien nie.',
  );

  static String _uuid() {
    final micros = DateTime.now().microsecondsSinceEpoch
        .toRadixString(16)
        .padLeft(16, '0');
    final hash = Object().hashCode.toRadixString(16).padLeft(8, '0');
    final raw = '$micros$hash${micros.substring(0, 8)}'
        .padRight(32, '0')
        .substring(0, 32);
    return '${raw.substring(0, 8)}-${raw.substring(8, 12)}-${raw.substring(12, 16)}-${raw.substring(16, 20)}-${raw.substring(20)}';
  }
}
