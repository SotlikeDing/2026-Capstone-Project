import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../safety/payload_guard.dart';
import '../utils/app_error_message.dart';

/// Runs an API call and turns every failure into an [Exception] with a
/// user-facing Afrikaans message.
///
/// Network errors use the server's message when it sends one. Malformed
/// responses (a missing field, a wrong type) no longer leak as raw
/// `FormatException` / `TypeError` text; they get [fallback] instead.
Future<T> guardApi<T>(
  Future<T> Function() call, {
  required String fallback,
}) async {
  try {
    return await call();
  } on DioException catch (error) {
    final payloadError = error.error;
    if (payloadError is PayloadTooLargeException) {
      throw Exception('Die bediener se antwoord is te groot om te laai.');
    }
    throw Exception(
      AppErrorMessage.from(
        readApiMessage(error.response?.data),
        fallback: fallback,
      ),
    );
  } on FormatException catch (error, stack) {
    debugPrint('[guardApi] Invalid response: $error\n$stack');
    throw Exception(fallback);
  } on TypeError catch (error, stack) {
    debugPrint('[guardApi] Unexpected response shape: $error\n$stack');
    throw Exception(fallback);
  }
}

/// Extracts a human message from an error response body.
String? readApiMessage(dynamic data) {
  if (data is String && data.trim().isNotEmpty) {
    return data;
  }

  if (data is Map) {
    for (final key in const ['message', 'error', 'detail', 'title']) {
      final value = data[key];
      if (value is String && value.trim().isNotEmpty) {
        return value;
      }
    }
  }

  return null;
}

/// Requires a JSON list response.
List<dynamic> requireList(dynamic data) {
  if (data is List) {
    return data;
  }
  throw const FormatException('Ongeldige lys antwoord.');
}

/// Requires a JSON object response.
Map<String, dynamic> requireMap(dynamic data) {
  if (data is Map<String, dynamic>) {
    return data;
  }
  if (data is Map) {
    return Map<String, dynamic>.from(data);
  }
  throw const FormatException('Ongeldige objek antwoord.');
}

/// Parses each list item with [parse], skipping (and logging) items that are
/// malformed so one bad record can't take down a whole screen.
List<T> parseEach<T>(
  List<dynamic> items,
  T Function(Map<String, dynamic> json) parse, {
  required String label,
}) {
  final result = <T>[];
  for (final item in items) {
    try {
      if (item is! Map) {
        throw const FormatException('Item is not an object.');
      }
      result.add(parse(Map<String, dynamic>.from(item)));
    } catch (error) {
      debugPrint('[parseEach] Skipped invalid $label: $error');
    }
  }
  return result;
}
