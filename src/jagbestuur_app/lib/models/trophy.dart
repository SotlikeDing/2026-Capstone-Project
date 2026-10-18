import '../utils/json_parsing.dart';
import 'rowland_ward_measurement.dart';
import 'rowland_ward_species.dart';

class Trophy {
  const Trophy({
    required this.id,
    required this.ownerUserId,
    required this.species,
    required this.photoPath,
    required this.antlerSizeCm,
    required this.carcassWeightKg,
    required this.shotDistanceMeters,
    required this.firearmType,
    required this.harvestDate,
    required this.locationText,
    required this.notes,
    required this.isPublic,
    required this.createdTimestamp,
    required this.updatedTimestamp,
    this.rowlandWard,
  });

  final int id;
  final int ownerUserId;
  final String species;
  final String photoPath;
  final double? antlerSizeCm;
  final double? carcassWeightKg;
  final int? shotDistanceMeters;
  final String firearmType;
  final DateTime harvestDate;
  final String locationText;
  final String? notes;
  final bool isPublic;
  final DateTime createdTimestamp;
  final DateTime updatedTimestamp;
  final RowlandWardSpecies? rowlandWard;

  String get displaySpecies {
    final canonical = rowlandWard;
    return canonical?.isValidLinkedClass == true
        ? canonical!.displayNameAf
        : species;
  }

  factory Trophy.fromJson(Map<String, dynamic> json) {
    final rawRowlandWard = json['rowlandWard'];

    return Trophy(
      id: (json['id'] as num?)?.toInt() ?? 0,
      ownerUserId: (json['ownerUserId'] as num?)?.toInt() ?? 0,
      species: json['species'] as String? ?? '',
      photoPath: json['photoPath'] as String? ?? '',
      antlerSizeCm: (json['antlerSizeCm'] as num?)?.toDouble(),
      carcassWeightKg: (json['carcassWeightKg'] as num?)?.toDouble(),
      shotDistanceMeters: (json['shotDistanceMeters'] as num?)?.toInt(),
      firearmType: json['firearmType'] as String? ?? '',
      harvestDate: requireDate(json['harvestDate'], 'harvestDate'),
      locationText: json['locationText'] as String? ?? '',
      notes: json['notes'] as String?,
      isPublic: json['isPublic'] as bool? ?? false,
      createdTimestamp:
          tryParseDate(json['createdTimestamp']) ??
          requireDate(json['harvestDate'], 'harvestDate'),
      updatedTimestamp:
          tryParseDate(json['updatedTimestamp']) ??
          tryParseDate(json['createdTimestamp']) ??
          requireDate(json['harvestDate'], 'harvestDate'),
      rowlandWard: rawRowlandWard is Map
          ? RowlandWardSpecies.fromMatchJson(
              Map<String, dynamic>.from(rawRowlandWard),
            )
          : null,
    );
  }
}

class TrophySaveRequest {
  const TrophySaveRequest({
    required this.species,
    required this.photoPath,
    required this.antlerSizeCm,
    required this.carcassWeightKg,
    required this.shotDistanceMeters,
    required this.firearmType,
    required this.harvestDate,
    required this.locationText,
    required this.notes,
    required this.isPublic,
    this.rowlandWard,
  });

  final String species;
  final String photoPath;
  final double? antlerSizeCm;
  final double? carcassWeightKg;
  final int? shotDistanceMeters;
  final String firearmType;
  final DateTime harvestDate;
  final String locationText;
  final String? notes;
  final bool isPublic;
  final RowlandWardEvaluationSaveRequest? rowlandWard;

  Map<String, dynamic> toJson() {
    final month = harvestDate.month.toString().padLeft(2, '0');
    final day = harvestDate.day.toString().padLeft(2, '0');
    final trimmedNotes = notes?.trim();

    return {
      'species': species.trim(),
      'photoPath': photoPath.trim(),
      'antlerSizeCm': antlerSizeCm,
      'carcassWeightKg': carcassWeightKg,
      'shotDistanceMeters': shotDistanceMeters,
      'firearmType': firearmType.trim(),
      'harvestDate': '${harvestDate.year}-$month-$day',
      'locationText': locationText.trim(),
      'notes': trimmedNotes == null || trimmedNotes.isEmpty
          ? null
          : trimmedNotes,
      'isPublic': isPublic,
      if (rowlandWard != null) 'rowlandWard': rowlandWard!.toJson(),
    };
  }
}
