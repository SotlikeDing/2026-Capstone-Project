import '../models/rowland_ward_species.dart';
import '../models/rowland_ward_measurement.dart';
import 'api_client.dart';
import 'api_error.dart';

abstract interface class RowlandWardSpeciesApi {
  Future<List<RowlandWardSpecies>> search(String query, {int limit = 8});

  Future<RowlandWardSpeciesMatchResult> match(String input, {int limit = 8});

  Future<RowlandWardMeasurementRequirements> getMeasurementRequirements(
    String classId,
  );
}

class RowlandWardSpeciesService implements RowlandWardSpeciesApi {
  RowlandWardSpeciesService({ApiClient? apiClient})
    : _api = apiClient ?? ApiClient.instance;

  final ApiClient _api;

  @override
  Future<List<RowlandWardSpecies>> search(String query, {int limit = 8}) {
    return guardApi(() async {
      final response = await _api.dio.get(
        '/api/rowland-ward/search',
        queryParameters: {'query': query, 'limit': limit},
      );
      final data = response.data;
      if (data is! List) return const [];

      return data
          .whereType<Map>()
          .map(
            (item) => RowlandWardSpecies.fromSuggestionJson(
              Map<String, dynamic>.from(item),
            ),
          )
          .where(
            (item) => item.classId.isNotEmpty && item.displayNameAf.isNotEmpty,
          )
          .toList(growable: false);
    }, fallback: _fallbackMessage);
  }

  @override
  Future<RowlandWardSpeciesMatchResult> match(String input, {int limit = 8}) {
    return guardApi(() async {
      final response = await _api.dio.get(
        '/api/rowland-ward/match',
        queryParameters: {'input': input, 'limit': limit},
      );
      final data = response.data;
      if (data is! Map) {
        throw Exception('Die wildspesie kon nie nagegaan word nie.');
      }

      return RowlandWardSpeciesMatchResult.fromJson(
        Map<String, dynamic>.from(data),
      );
    }, fallback: _fallbackMessage);
  }

  @override
  Future<RowlandWardMeasurementRequirements> getMeasurementRequirements(
    String classId,
  ) {
    return guardApi(() async {
      final response = await _api.dio.get(
        '/api/rowland-ward/classes/${Uri.encodeComponent(classId)}/measurement-requirements',
      );
      final data = response.data;
      if (data is! Map) {
        throw Exception('Die Rowland Ward-metings kon nie gelaai word nie.');
      }

      return RowlandWardMeasurementRequirements.fromJson(
        Map<String, dynamic>.from(data),
      );
    }, fallback: _fallbackMessage);
  }

  static const _fallbackMessage =
      'Die wildspesies kon nie gelaai word nie. Probeer weer.';
}
