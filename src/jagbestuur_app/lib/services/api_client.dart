import 'package:dio/dio.dart';

import '../config/app_config.dart';
import '../safety/payload_guard.dart';
import 'auth_service.dart';
import 'http_security.dart';

class ApiClient {
  ApiClient._() {
    _dio = Dio(
      BaseOptions(
        baseUrl: AppConfig.apiBaseUrl,
        connectTimeout: const Duration(seconds: 20),
        receiveTimeout: const Duration(seconds: 20),
        headers: const {'Content-Type': 'application/json'},
      ),
    );

    applyTlsPolicy(_dio);
    _dio.interceptors.add(PayloadGuardInterceptor());
    _dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) {
          if (_shouldAttachToken(options)) {
            final token = AuthService.instance.accessToken;
            if (token != null && token.isNotEmpty) {
              options.headers['Authorization'] = 'Bearer $token';
            }
          }

          handler.next(options);
        },
        onError: (error, handler) async {
          final request = error.requestOptions;

          if (!_shouldTryRefresh(error)) {
            handler.next(error);
            return;
          }

          final outcome = await _refreshTokenLocked();
          if (outcome == RefreshOutcome.unavailable) {
            // The session is fine; the server just couldn't answer. Keep the
            // user signed in and report a "try again" instead of the 401.
            handler.next(
              DioException(
                requestOptions: request,
                type: DioExceptionType.unknown,
                error: 'Kon nie met die bediener verbind nie. Probeer weer.',
              ),
            );
            return;
          }
          if (outcome == RefreshOutcome.rejected) {
            await AuthService.instance.signOut();
            handler.next(error);
            return;
          }

          final token = AuthService.instance.accessToken;
          if (token == null || token.isEmpty) {
            handler.next(error);
            return;
          }

          try {
            final response = await _dio.request<dynamic>(
              request.path,
              // A FormData body is a one-shot stream; resend a copy.
              data: request.data is FormData
                  ? (request.data as FormData).clone()
                  : request.data,
              queryParameters: request.queryParameters,
              cancelToken: request.cancelToken,
              onReceiveProgress: request.onReceiveProgress,
              onSendProgress: request.onSendProgress,
              options: Options(
                method: request.method,
                headers: Map<String, dynamic>.from(request.headers)
                  ..['Authorization'] = 'Bearer $token',
                responseType: request.responseType,
                contentType: request.contentType,
                extra: Map<String, dynamic>.from(request.extra)
                  ..['retried'] = true,
                followRedirects: request.followRedirects,
                receiveDataWhenStatusError: request.receiveDataWhenStatusError,
                sendTimeout: request.sendTimeout,
                receiveTimeout: request.receiveTimeout,
                validateStatus: request.validateStatus,
              ),
            );

            handler.resolve(response);
          } catch (retryError) {
            handler.next(retryError is DioException ? retryError : error);
          }
        },
      ),
    );
  }

  static final ApiClient instance = ApiClient._();

  late final Dio _dio;
  Future<RefreshOutcome>? _refreshFuture;

  Dio get dio => _dio;

  bool _shouldAttachToken(RequestOptions options) {
    final requiresAuth = options.extra['requiresAuth'] != false;
    return requiresAuth && !_isAuthPath(options.path);
  }

  bool _shouldTryRefresh(DioException error) {
    final request = error.requestOptions;

    return error.response?.statusCode == 401 &&
        _shouldAttachToken(request) &&
        request.extra['retried'] != true;
  }

  bool _isAuthPath(String path) {
    return path.contains(AppConfig.loginPath) ||
        path.contains(AppConfig.refreshPath);
  }

  Future<RefreshOutcome> _refreshTokenLocked() {
    final existing = _refreshFuture;
    if (existing != null) {
      return existing;
    }

    final future = AuthService.instance.refreshTokens().whenComplete(() {
      _refreshFuture = null;
    });

    _refreshFuture = future;
    return future;
  }
}
