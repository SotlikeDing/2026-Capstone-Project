import 'package:dio/dio.dart';

import 'api_client.dart';
import 'api_error.dart';

class FarmRatingService {
  FarmRatingService({Dio? client}) : _client = client ?? ApiClient.instance.dio;

  final Dio _client;

  Future<void> saveRating({
    required int bookingId,
    required int stars,
    String? description,
  }) {
    return guardApi(() async {
      await _client.post(
        '/api/farm-ratings/bookings/$bookingId',
        data: {
          'stars': stars,
          'description': description?.trim().isEmpty == true
              ? null
              : description?.trim(),
        },
      );
    }, fallback: 'Kon nie die plaasgradering stoor nie.');
  }
}
