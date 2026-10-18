import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:jagbestuur_app/safety/payload_guard.dart';

void main() {
  group('PayloadGuard lêers', () {
    late Directory dir;

    setUp(() async {
      dir = await Directory.systemTemp.createTemp('payload_guard_test');
    });

    tearDown(() async {
      await dir.delete(recursive: true);
    });

    test('lees ’n lêer onder die limiet', () async {
      final file = File('${dir.path}/small.txt')..writeAsStringSync('a\nb');
      expect(await PayloadGuard.readLines(file, limit: 10), ['a', 'b']);
    });

    test('weier ’n lêer bo die limiet sonder om dit te lees', () async {
      final file = File('${dir.path}/big.txt')
        ..writeAsBytesSync(List.filled(11, 65));
      expect(
        () => PayloadGuard.readString(file, limit: 10),
        throwsA(isA<PayloadTooLargeException>()),
      );
    });
  });

  group('PayloadGuard HTTP', () {
    http.StreamedResponse response(List<List<int>> chunks, {int? length}) {
      return http.StreamedResponse(
        Stream.fromIterable(chunks),
        200,
        contentLength: length,
      );
    }

    test('versamel ’n klein liggaam', () async {
      final bytes = await PayloadGuard.readHttpBody(
        response([
          [1, 2],
          [3],
        ]),
        limit: 10,
      );
      expect(bytes, [1, 2, 3]);
    });

    test('weier op grond van Content-Length', () async {
      expect(
        () => PayloadGuard.readHttpBody(response([], length: 11), limit: 10),
        throwsA(isA<PayloadTooLargeException>()),
      );
    });

    test('stop ’n stroom sonder Content-Length sodra dit te groot word', () {
      expect(
        () => PayloadGuard.readHttpBody(
          response([List.filled(6, 0), List.filled(6, 0)]),
          limit: 10,
        ),
        throwsA(isA<PayloadTooLargeException>()),
      );
    });
  });
}
