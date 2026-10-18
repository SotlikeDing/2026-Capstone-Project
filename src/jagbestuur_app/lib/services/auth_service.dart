import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:url_launcher/url_launcher.dart';

import '../config/app_config.dart';
import '../models/app_session.dart';
import '../safety/payload_guard.dart';
import '../utils/app_error_message.dart';
import '../utils/json_parsing.dart';
import 'http_security.dart';

enum RefreshOutcome { refreshed, rejected, unavailable }

class AuthService {
  AuthService._() {
    applyTlsPolicy(_dio);
  }

  static final AuthService instance = AuthService._();

  final ValueNotifier<AppSession?> sessionNotifier = ValueNotifier<AppSession?>(
    null,
  );

  final Dio _dio = Dio(
    BaseOptions(
      baseUrl: AppConfig.apiBaseUrl,
      connectTimeout: const Duration(seconds: 20),
      receiveTimeout: const Duration(seconds: 20),
      headers: const {'Content-Type': 'application/json'},
    ),
  )..interceptors.add(PayloadGuardInterceptor());

  final FlutterSecureStorage _secureStorage = const FlutterSecureStorage();

  static const String _sessionKey = 'app_session';
  static const String _rememberMeKey = 'remember_me';
  static const String _accessTokenKey = 'access_token';
  static const String _refreshTokenKey = 'refresh_token';

  String? _accessToken;
  String? _refreshToken;
  bool _initialized = false;

  AppSession? get currentSession => sessionNotifier.value;
  String? get accessToken => _accessToken;
  String? get refreshToken => _refreshToken;

  Future<void> initialize() async {
    if (_initialized) {
      return;
    }

    _initialized = true;

    final prefs = await SharedPreferences.getInstance();
    final rememberMe = prefs.getBool(_rememberMeKey) ?? false;

    if (!rememberMe) {
      await _clearPersistentStorage();
      return;
    }

    final rawSession = prefs.getString(_sessionKey);
    if (rawSession == null || rawSession.isEmpty) {
      await _clearPersistentStorage();
      return;
    }

    try {
      final sessionMap = jsonDecode(rawSession) as Map<String, dynamic>;
      sessionNotifier.value = AppSession.fromJson(sessionMap);
      _accessToken = await _secureStorage.read(key: _accessTokenKey);
      _refreshToken = await _secureStorage.read(key: _refreshTokenKey);
    } catch (_) {
      await _clearPersistentStorage();
    }
  }

  Future<void> signIn({
    required String email,
    required String password,
    required bool rememberMe,
  }) async {
    final AppSession session;
    final String accessToken;
    final String? refreshToken;

    try {
      final response = await _dio.post(
        AppConfig.loginPath,
        // No role is sent: the API reads the user's role from the database.
        data: {'email': email.trim(), 'password': password},
      );

      final data = _requireMap(response.data);
      session = _extractSession(
        data,
        fallbackEmail: email.trim(),
        rememberMe: rememberMe,
      );

      final token = _readString(data, const ['accessToken', 'access_token']);
      if (token == null) {
        // Without a token every later call would fail with 401.
        throw const FormatException('Geen toegangsteken ontvang nie.');
      }
      accessToken = token;
      refreshToken = _readString(data, const ['refreshToken', 'refresh_token']);
    } on DioException catch (e) {
      final responseData = e.response?.data;
      final apiMessage = _readApiMessage(responseData);
      final diagnosticMessage = apiMessage ?? e.error ?? e.message;

      debugPrint(
        '[AuthService] Login request failed: '
        'type=${e.type}, status=${e.response?.statusCode}, '
        'message=${apiMessage ?? e.message ?? e.error}',
      );

      throw Exception(
        AppErrorMessage.from(
          diagnosticMessage,
          fallback: 'Kon nie inteken nie.',
        ),
      );
    } on FormatException catch (e) {
      debugPrint('[AuthService] Invalid login response: $e');
      throw Exception('Die bediener het ’n ongeldige antwoord gestuur.');
    } on TypeError catch (e) {
      debugPrint('[AuthService] Invalid login response: $e');
      throw Exception('Die bediener het ’n ongeldige antwoord gestuur.');
    }

    _accessToken = accessToken;
    _refreshToken = refreshToken;

    if (rememberMe) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setBool(_rememberMeKey, true);
      await prefs.setString(_sessionKey, jsonEncode(session.toJson()));
      await _secureStorage.write(key: _accessTokenKey, value: accessToken);

      if (refreshToken != null && refreshToken.isNotEmpty) {
        await _secureStorage.write(key: _refreshTokenKey, value: refreshToken);
      } else {
        await _secureStorage.delete(key: _refreshTokenKey);
      }
    } else {
      await _clearPersistentStorage();
    }

    // Publish last, once tokens and storage are consistent.
    sessionNotifier.value = session;
  }

  /// [RefreshOutcome.unavailable] means the server could not be reached or was
  /// briefly unable to answer (timeout, 5xx, 429): the session is still valid
  /// and must not be signed out for it.
  Future<RefreshOutcome> refreshTokens() async {
    if (_refreshToken == null || _refreshToken!.isEmpty) {
      return RefreshOutcome.rejected;
    }

    try {
      final response = await _dio.post(
        AppConfig.refreshPath,
        data: {'accessToken': _accessToken, 'refreshToken': _refreshToken},
      );

      final data = _requireMap(response.data);

      final newAccessToken = _readString(data, const [
        'accessToken',
        'access_token',
      ]);

      final newRefreshToken = _readString(data, const [
        'refreshToken',
        'refresh_token',
      ]);

      if (newAccessToken == null || newAccessToken.isEmpty) {
        return RefreshOutcome.rejected;
      }

      _accessToken = newAccessToken;
      _refreshToken = newRefreshToken ?? _refreshToken;
      await _adoptRefreshedRole(data);

      if (sessionNotifier.value?.rememberMe == true) {
        await _secureStorage.write(key: _accessTokenKey, value: _accessToken);

        if (_refreshToken != null && _refreshToken!.isNotEmpty) {
          await _secureStorage.write(
            key: _refreshTokenKey,
            value: _refreshToken,
          );
        }
      }

      return RefreshOutcome.refreshed;
    } on DioException catch (e) {
      final status = e.response?.statusCode;
      debugPrint(
        '[AuthService] Refresh failed: type=${e.type}, status=$status',
      );
      if (status == null || status >= 500 || status == 429) {
        return RefreshOutcome.unavailable;
      }
      return RefreshOutcome.rejected;
    } catch (_) {
      return RefreshOutcome.rejected;
    }
  }

  /// The API re-reads the role from the database on every refresh, so a role
  /// added or removed on the website reaches the app without a new sign-in.
  Future<void> _adoptRefreshedRole(Map<String, dynamic> data) async {
    final current = sessionNotifier.value;
    if (current == null) return;
    final AppSession refreshed;
    try {
      refreshed = _extractSession(
        data,
        fallbackEmail: current.email,
        rememberMe: current.rememberMe,
      );
    } on FormatException {
      return;
    }
    if (refreshed.userId != current.userId ||
        (refreshed.userRoleId == current.userRoleId &&
            refreshed.role == current.role &&
            refreshed.capabilities.length == current.capabilities.length &&
            refreshed.capabilities.containsAll(current.capabilities))) {
      return;
    }
    if (refreshed.rememberMe) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_sessionKey, jsonEncode(refreshed.toJson()));
    }
    sessionNotifier.value = refreshed;
  }

  Future<void> signOut() async {
    sessionNotifier.value = null;
    _accessToken = null;
    _refreshToken = null;
    await _clearPersistentStorage();
  }

  Future<void> _clearPersistentStorage() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_sessionKey);
    await prefs.remove(_rememberMeKey);
    await _secureStorage.delete(key: _accessTokenKey);
    await _secureStorage.delete(key: _refreshTokenKey);
  }

  /// The login endpoint returns a flat object with `userId` and `userRoleId`.
  /// Only top-level fields are read, so a nested `id` (for example of a role
  /// or farm) can never be mistaken for the user's id.
  AppSession _extractSession(
    Map<String, dynamic> data, {
    required String fallbackEmail,
    required bool rememberMe,
  }) {
    final userId = tryParseInt(data['userId']);
    final userRoleId = tryParseInt(data['userRoleId']);

    if (userId == null ||
        userId <= 0 ||
        userRoleId == null ||
        userRoleId <= 0) {
      throw const FormatException('userId of userRoleId ontbreek.');
    }

    final email = _readString(data, const ['email']) ?? fallbackEmail;
    final displayName = _readString(data, const ['displayName']) ?? email;
    final role = _readString(data, const ['role']) ?? 'Restricted';
    final roleCode = _readString(data, const ['roleCode']) ?? '';
    final capabilities = (data['capabilities'] as List<dynamic>? ?? const [])
        .whereType<String>()
        .toSet();

    return AppSession(
      userId: userId,
      userRoleId: userRoleId,
      email: email,
      displayName: displayName,
      rememberMe: rememberMe,
      role: role,
      roleCode: roleCode,
      capabilities: capabilities,
    );
  }

  Map<String, dynamic> _requireMap(dynamic data) {
    if (data is Map<String, dynamic>) {
      return data;
    }

    if (data is Map) {
      return Map<String, dynamic>.from(data);
    }

    if (data is String && data.isNotEmpty) {
      final decoded = jsonDecode(data);
      if (decoded is Map<String, dynamic>) {
        return decoded;
      }
      if (decoded is Map) {
        return Map<String, dynamic>.from(decoded);
      }
    }

    throw const FormatException('Ongeldige API antwoord.');
  }

  String? _readString(Map<String, dynamic> data, List<String> keys) {
    for (final key in keys) {
      final value = data[key];
      if (value is String && value.trim().isNotEmpty) {
        return value.trim();
      }
    }
    return null;
  }

  String? _readApiMessage(dynamic data) {
    if (data is String && data.trim().isNotEmpty) {
      return data;
    }

    try {
      final map = _requireMap(data);
      return _readString(map, const ['message', 'error', 'detail', 'title']);
    } catch (_) {
      return null;
    }
  }

  Future<void> openWebSignIn() => _openInBrowser(
    AppConfig.webLoginUrl,
    'Kon nie webinteken bladsy oopmaak nie.',
  );

  Future<void> openWebRegister() => _openInBrowser(
    AppConfig.webRegisterUrl,
    'Kon nie webregistrasie bladsy oopmaak nie.',
  );

  Future<void> _openInBrowser(String url, String failureMessage) async {
    // launchUrl reports failure itself; canLaunchUrl needs <queries> entries
    // and returns false on Android 11+ without them.
    final opened = await launchUrl(
      Uri.parse(url),
      mode: LaunchMode.externalApplication,
    );
    if (!opened) {
      throw Exception(failureMessage);
    }
  }
}
