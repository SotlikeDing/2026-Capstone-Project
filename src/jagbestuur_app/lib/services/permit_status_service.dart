import 'package:dio/dio.dart';

import '../models/permit_status.dart';
import 'api_client.dart';
import 'api_error.dart';

class PermitStatusService {
  PermitStatusService({Dio? client})
    : _client = client ?? ApiClient.instance.dio;

  final Dio _client;

  Future<List<PermitStatus>> getUserPermitStatuses(int userRoleId) {
    return guardApi(() async {
      final response = await _client.get('/api/permits/user/$userRoleId');
      return parseEach(
        requireList(response.data),
        PermitStatus.fromJson,
        label: 'permit',
      );
    }, fallback: 'Kon nie permit statusse laai nie.');
  }
}
