import 'dart:io';

import 'package:exif/exif.dart';
import 'package:image_size_getter/file_input.dart';
import 'package:image_size_getter/image_size_getter.dart';

import '../config/safety_config.dart';
import '../models/hunt_photo.dart';
import '../safety/payload_guard.dart';

class HuntPhotoMetadataException implements Exception {
  const HuntPhotoMetadataException(this.message);
  final String message;

  @override
  String toString() => message;
}

/// Reads photo metadata locally. No metadata or image bytes leave the device.
class HuntPhotoMetadataService {
  const HuntPhotoMetadataService();

  static const Set<String> supportedExtensions = <String>{
    'jpg',
    'jpeg',
    'png',
    'webp',
  };

  Future<HuntPhotoMetadata> extract(File file) async {
    if (!await file.exists()) {
      throw const HuntPhotoMetadataException('Die gekose foto bestaan nie.');
    }

    final length = await file.length();
    if (length <= 0) {
      throw const HuntPhotoMetadataException('Die gekose foto is leeg.');
    }
    if (length > HuntMediaLimits.maxPhotoBytes) {
      throw HuntPhotoMetadataException(
        'Die foto is groter as ${HuntMediaLimits.maxPhotoBytes ~/ (1024 * 1024)} MB.',
      );
    }

    final extension = _extension(file.path);
    if (!supportedExtensions.contains(extension)) {
      throw const HuntPhotoMetadataException(
        'Hierdie fotoformaat word nie ondersteun nie. Gebruik JPG, PNG of WebP.',
      );
    }

    final size = _readDimensions(file);
    if (size.width <= 0 || size.height <= 0) {
      throw const HuntPhotoMetadataException(
        'Die foto se afmetings kon nie gelees word nie.',
      );
    }
    if (size.width * size.height > HuntMediaLimits.maxPhotoPixels) {
      throw const HuntPhotoMetadataException(
        'Die foto se resolusie is te groot om veilig te verwerk.',
      );
    }

    Map<String, IfdTag> tags = const <String, IfdTag>{};
    try {
      await PayloadGuard.ensureFileFits(
        file,
        limit: HuntMediaLimits.maxPhotoBytes,
      );
      tags = await readExifFromBytes(
        await file.readAsBytes(),
        strict: false,
        details: false,
      );
    } catch (_) {
      // Missing or malformed EXIF is expected and must not reject a valid
      // image. Dimensions above are the corruption/format safety gate.
      tags = const <String, IfdTag>{};
    }

    final capturedAt = _parseCapturedAt(tags);
    final latitude = _parseGps(tags, 'GPS GPSLatitude', 'GPS GPSLatitudeRef');
    final longitude = _parseGps(
      tags,
      'GPS GPSLongitude',
      'GPS GPSLongitudeRef',
    );
    final orientation = _firstInt(tags['Image Orientation']);

    return HuntPhotoMetadata(
      fileSizeBytes: length,
      width: size.needRotate ? size.height : size.width,
      height: size.needRotate ? size.width : size.height,
      fileType: extension == 'jpeg' ? 'jpg' : extension,
      metadataAvailable:
          capturedAt != null ||
          (latitude != null && longitude != null) ||
          orientation != null,
      capturedAt: capturedAt,
      latitude: latitude,
      longitude: longitude,
      orientation: orientation,
    );
  }

  Size _readDimensions(File file) {
    try {
      return ImageSizeGetter.getSizeResult(FileInput(file)).size;
    } catch (_) {
      throw const HuntPhotoMetadataException(
        'Die foto is beskadig of kan nie gelees word nie.',
      );
    }
  }

  DateTime? _parseCapturedAt(Map<String, IfdTag> tags) {
    final raw = _printable(
      tags['EXIF DateTimeOriginal'] ??
          tags['EXIF DateTimeDigitized'] ??
          tags['Image DateTime'],
    );
    if (raw == null) {
      return null;
    }

    final match = RegExp(
      r'^(\d{4}):(\d{2}):(\d{2})[ T](\d{2}):(\d{2}):(\d{2})',
    ).firstMatch(raw);
    if (match == null) {
      return null;
    }

    try {
      final parts = <int>[
        for (var index = 1; index <= 6; index++) int.parse(match.group(index)!),
      ];
      final offset = _printable(
        tags['EXIF OffsetTimeOriginal'] ?? tags['EXIF OffsetTime'],
      );
      final offsetMatch = offset == null
          ? null
          : RegExp(r'^([+-])(\d{2}):?(\d{2})$').firstMatch(offset);
      if (offsetMatch == null) {
        // EXIF without an offset describes the camera's wall clock. Treat it
        // as device-local and let the confirmation UI correct a bad clock.
        return DateTime(
          parts[0],
          parts[1],
          parts[2],
          parts[3],
          parts[4],
          parts[5],
        );
      }
      final offsetMinutes =
          int.parse(offsetMatch.group(2)!) * 60 +
          int.parse(offsetMatch.group(3)!);
      final signedOffset = offsetMatch.group(1) == '-'
          ? -offsetMinutes
          : offsetMinutes;
      return DateTime.utc(
        parts[0],
        parts[1],
        parts[2],
        parts[3],
        parts[4],
        parts[5],
      ).subtract(Duration(minutes: signedOffset)).toLocal();
    } catch (_) {
      return null;
    }
  }

  double? _parseGps(
    Map<String, IfdTag> tags,
    String coordinateKey,
    String referenceKey,
  ) {
    final tag = tags[coordinateKey];
    if (tag == null) {
      return null;
    }

    final parts = _numericParts(tag);
    if (parts.length < 3) {
      return null;
    }

    var value = parts[0] + (parts[1] / 60) + (parts[2] / 3600);
    final reference = _printable(tags[referenceKey])?.toUpperCase();
    if (reference == 'S' || reference == 'W') {
      value = -value;
    }
    if (!value.isFinite) {
      return null;
    }
    return value;
  }

  List<double> _numericParts(IfdTag tag) {
    final raw = tag.values.toString();
    final matches = RegExp(
      r'-?\d+(?:\.\d+)?(?:/-?\d+(?:\.\d+)?)?',
    ).allMatches(raw);
    return matches
        .map((match) => _parseNumber(match.group(0)!))
        .whereType<double>()
        .toList(growable: false);
  }

  double? _parseNumber(String value) {
    final slash = value.indexOf('/');
    if (slash == -1) {
      return double.tryParse(value);
    }
    final numerator = double.tryParse(value.substring(0, slash));
    final denominator = double.tryParse(value.substring(slash + 1));
    if (numerator == null || denominator == null || denominator == 0) {
      return null;
    }
    return numerator / denominator;
  }

  int? _firstInt(IfdTag? tag) {
    if (tag == null) {
      return null;
    }
    final match = RegExp(r'\d+').firstMatch(tag.values.toString());
    return match == null ? null : int.tryParse(match.group(0)!);
  }

  String? _printable(IfdTag? tag) {
    if (tag == null) {
      return null;
    }
    final value = tag.printable.trim();
    return value.isEmpty ? null : value;
  }

  String _extension(String path) {
    final name = path.replaceAll('\\', '/').split('/').last;
    final dot = name.lastIndexOf('.');
    return dot == -1 ? '' : name.substring(dot + 1).toLowerCase();
  }
}
