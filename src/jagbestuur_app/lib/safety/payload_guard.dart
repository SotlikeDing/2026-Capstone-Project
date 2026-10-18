import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:http/http.dart' as http;

import '../config/safety_config.dart';

/// Thrown when a file, response body or image would exceed
/// [maxInMemoryPayloadBytes] if it were loaded into memory in one piece.
class PayloadTooLargeException implements Exception {
  PayloadTooLargeException({
    required this.source,
    required this.bytes,
    this.limit = maxInMemoryPayloadBytes,
  });

  final String source;

  /// Size that tripped the guard. For streamed bodies this is the number of
  /// bytes received when the transfer was aborted, not the full size.
  final int bytes;
  final int limit;

  @override
  String toString() =>
      'PayloadTooLargeException: $source is ${_mb(bytes)} MB '
      '(limit ${_mb(limit)} MB).';

  static String _mb(int bytes) => (bytes / (1024 * 1024)).toStringAsFixed(1);
}

/// Size checks to run before anything is read fully into memory.
class PayloadGuard {
  const PayloadGuard._();

  /// Throws [PayloadTooLargeException] when [file] is larger than [limit].
  /// Call this before `readAsBytes`, `readAsString` or `readAsLines`.
  static Future<void> ensureFileFits(
    File file, {
    int limit = maxInMemoryPayloadBytes,
  }) async {
    final length = await file.length();
    if (length > limit) {
      throw PayloadTooLargeException(
        source: file.path,
        bytes: length,
        limit: limit,
      );
    }
  }

  /// Reads a text file after checking its size.
  static Future<String> readString(
    File file, {
    int limit = maxInMemoryPayloadBytes,
  }) async {
    await ensureFileFits(file, limit: limit);
    return file.readAsString();
  }

  /// Reads a text file line by line after checking its size.
  static Future<List<String>> readLines(
    File file, {
    int limit = maxInMemoryPayloadBytes,
  }) async {
    await ensureFileFits(file, limit: limit);
    return file.readAsLines();
  }

  /// Collects a streamed `package:http` response, aborting as soon as the
  /// advertised or received size passes [limit].
  static Future<Uint8List> readHttpBody(
    http.StreamedResponse response, {
    int limit = maxInMemoryPayloadBytes,
  }) async {
    final source = response.request?.url.toString() ?? 'HTTP response';
    final advertised = response.contentLength;
    if (advertised != null && advertised > limit) {
      // Drain so the underlying connection is released.
      unawaited(response.stream.listen(null).cancel());
      throw PayloadTooLargeException(
        source: source,
        bytes: advertised,
        limit: limit,
      );
    }

    final builder = BytesBuilder(copy: false);
    await for (final chunk in response.stream) {
      builder.add(chunk);
      if (builder.length > limit) {
        throw PayloadTooLargeException(
          source: source,
          bytes: builder.length,
          limit: limit,
        );
      }
    }
    return builder.takeBytes();
  }

  /// `http.get` replacement that refuses bodies over [limit].
  static Future<GuardedHttpResponse> httpGet(
    Uri uri, {
    Map<String, String>? headers,
    int limit = maxInMemoryPayloadBytes,
    Duration timeout = const Duration(seconds: 20),
  }) async {
    final client = http.Client();
    try {
      final request = http.Request('GET', uri);
      if (headers != null) {
        request.headers.addAll(headers);
      }
      final response = await client.send(request).timeout(timeout);
      final bytes = await readHttpBody(response, limit: limit).timeout(timeout);
      return GuardedHttpResponse(response.statusCode, bytes);
    } finally {
      client.close();
    }
  }
}

class GuardedHttpResponse {
  GuardedHttpResponse(this.statusCode, this.bodyBytes);

  final int statusCode;
  final Uint8List bodyBytes;

  String get body => utf8.decode(bodyBytes, allowMalformed: true);
}

/// Dio interceptor that cancels a download mid-stream once it passes
/// [limit], instead of letting Dio buffer the whole body first.
class PayloadGuardInterceptor extends Interceptor {
  PayloadGuardInterceptor({this.limit = maxInMemoryPayloadBytes});

  final int limit;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    // Streamed responses are consumed by the caller chunk by chunk, so the
    // whole body is never held in memory and does not need the guard.
    if (options.responseType == ResponseType.stream) {
      handler.next(options);
      return;
    }

    final cancelToken = options.cancelToken ?? CancelToken();
    options.cancelToken = cancelToken;

    final callerProgress = options.onReceiveProgress;
    options.onReceiveProgress = (received, total) {
      final observed = total > received ? total : received;
      if (observed > limit && !cancelToken.isCancelled) {
        cancelToken.cancel(
          PayloadTooLargeException(
            source: options.uri.toString(),
            bytes: observed,
            limit: limit,
          ),
        );
        return;
      }
      callerProgress?.call(received, total);
    };

    handler.next(options);
  }

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    final reason = err.error;
    if (err.type == DioExceptionType.cancel &&
        reason is PayloadTooLargeException) {
      handler.next(
        DioException(
          requestOptions: err.requestOptions,
          type: DioExceptionType.unknown,
          error: reason,
          message: reason.toString(),
        ),
      );
      return;
    }
    handler.next(err);
  }
}
