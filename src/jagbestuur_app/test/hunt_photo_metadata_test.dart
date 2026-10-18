import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/config/safety_config.dart';
import 'package:jagbestuur_app/services/hunt_photo_metadata_service.dart';

void main() {
  late Directory directory;

  setUp(() async {
    directory = await Directory.systemTemp.createTemp('hunt_photo_metadata');
  });

  tearDown(() async {
    await directory.delete(recursive: true);
  });

  test('extracts dimensions and tolerates missing EXIF', () async {
    final bytes = base64Decode(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
    );
    final file = File('${directory.path}/plain.png')
      ..writeAsBytesSync(bytes, flush: true);
    final metadata = await const HuntPhotoMetadataService().extract(file);
    expect(metadata.width, 1);
    expect(metadata.height, 1);
    expect(metadata.fileType, 'png');
    expect(metadata.metadataAvailable, isFalse);
  });

  test('rejects corrupt and unsupported files without crashing', () async {
    final corrupt = File('${directory.path}/bad.jpg')
      ..writeAsStringSync('not an image');
    expect(
      () => const HuntPhotoMetadataService().extract(corrupt),
      throwsA(isA<HuntPhotoMetadataException>()),
    );

    final unsupported = File('${directory.path}/photo.heic')
      ..writeAsBytesSync(<int>[1, 2, 3]);
    expect(
      () => const HuntPhotoMetadataService().extract(unsupported),
      throwsA(isA<HuntPhotoMetadataException>()),
    );
  });

  test('rejects an oversized file before reading it into memory', () async {
    final file = File('${directory.path}/large.jpg');
    await file.open(mode: FileMode.write).then((handle) async {
      await handle.truncate(HuntMediaLimits.maxPhotoBytes + 1);
      await handle.close();
    });
    expect(
      () => const HuntPhotoMetadataService().extract(file),
      throwsA(isA<HuntPhotoMetadataException>()),
    );
  });
}
