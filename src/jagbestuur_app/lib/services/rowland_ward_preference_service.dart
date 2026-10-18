import 'package:dio/dio.dart';

import '../models/rowland_ward_preference.dart';
import 'api_client.dart';
import 'api_error.dart';

abstract interface class RowlandWardPreferenceApi {
  Future<bool> getEnabled();

  Future<bool> updateEnabled(bool enabled);
}

class RowlandWardPreferenceService implements RowlandWardPreferenceApi {
  RowlandWardPreferenceService({Dio? client})
    : _client = client ?? ApiClient.instance.dio;

  final Dio _client;

  static const _path = '/api/profile/rowland-ward-preference';

  @override
  Future<bool> getEnabled() {
    return guardApi(() async {
      final response = await _client.get(_path);
      return RowlandWardPreference.fromJson(requireMap(response.data)).enabled;
    }, fallback: 'Die Rowland Ward-instelling kon nie gelaai word nie.');
  }

  @override
  Future<bool> updateEnabled(bool enabled) {
    return guardApi(() async {
      final response = await _client.put(
        _path,
        data: {'rowlandWardEnabled': enabled},
      );
      return RowlandWardPreference.fromJson(requireMap(response.data)).enabled;
    }, fallback: 'Die Rowland Ward-instelling kon nie gestoor word nie.');
  }
}
