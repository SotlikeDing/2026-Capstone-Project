import 'package:dio/dio.dart';

import '../models/trophy.dart';
import 'api_client.dart';
import 'api_error.dart';

class TrophyService {
  TrophyService({Dio? client}) : _client = client ?? ApiClient.instance.dio;

  final Dio _client;

  static const int maxPhotoBytes = 10 * 1024 * 1024;

  Future<List<Trophy>> getMyTrophies() {
    return guardApi(() async {
      final response = await _client.get('/api/trophies');
      return parseEach(
        requireList(response.data),
        Trophy.fromJson,
        label: 'trophy',
      );
    }, fallback: 'Kon nie jou trofeë laai nie.');
  }

  Future<Trophy> createTrophy(TrophySaveRequest request) {
    return guardApi(() async {
      final response = await _client.post(
        '/api/trophies',
        data: request.toJson(),
      );
      final data = requireMap(response.data);
      return Trophy.fromJson(requireMap(data['trophy']));
    }, fallback: 'Die trofee kon nie bygevoeg word nie.');
  }

  Future<String> uploadPhoto(String filePath) {
    return guardApi(() async {
      final fileName = filePath
          .replaceAll('\\', '/')
          .split('/')
          .where((part) => part.isNotEmpty)
          .last;
      final response = await _client.post(
        '/api/trophies/photos',
        data: FormData.fromMap({
          'photo': await MultipartFile.fromFile(filePath, filename: fileName),
        }),
      );
      final data = requireMap(response.data);
      final photoPath = data['photoPath'] as String?;
      if (photoPath == null || photoPath.trim().isEmpty) {
        throw const FormatException('photoPath ontbreek.');
      }
      return photoPath.trim();
    }, fallback: 'Die trofeefoto kon nie opgelaai word nie.');
  }

  /// Removes an upload that could not be attached to a trophy. Failures are
  /// intentionally allowed to bubble so callers can choose best-effort use.
  Future<void> deleteUploadedPhoto(String photoPath) {
    return guardApi(() async {
      await _client.delete(
        '/api/trophies/photos',
        queryParameters: {'photoPath': photoPath},
      );
    }, fallback: 'Die ongebruikte trofeefoto kon nie verwyder word nie.');
  }

  Future<void> updateTrophy({
    required int trophyId,
    required TrophySaveRequest request,
  }) {
    return guardApi(() async {
      await _client.put('/api/trophies/$trophyId', data: request.toJson());
    }, fallback: 'Die trofee kon nie bygewerk word nie.');
  }

  Future<void> deleteTrophy(int trophyId) {
    return guardApi(() async {
      await _client.delete('/api/trophies/$trophyId');
    }, fallback: 'Die trofee kon nie verwyder word nie.');
  }
}
