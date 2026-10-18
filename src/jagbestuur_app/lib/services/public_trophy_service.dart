import 'package:dio/dio.dart';

import '../models/rowland_ward_public_summary.dart';
import '../utils/json_parsing.dart';
import 'api_client.dart';
import 'api_error.dart';

abstract class PublicTrophyApi {
  Future<LeaderboardFilterOptions> getLeaderboardFilterOptions();

  Future<List<LeaderboardTrophy>> getLeaderboardTrophies(
    LeaderboardQuery query,
  );

  Future<PublicTrophyDetail> getPublicTrophyDetail(int trophyId);
}

class PublicTrophyService implements PublicTrophyApi {
  /// Mirrors the API's row cap (PublicTrophies_db.LeaderboardMaxRows).
  static const int leaderboardMaxRows = 500;

  PublicTrophyService({Dio? client})
    : _client = client ?? ApiClient.instance.dio;

  final Dio _client;

  @override
  Future<LeaderboardFilterOptions> getLeaderboardFilterOptions() {
    return guardApi(() async {
      final responses = await Future.wait([
        _client.get<dynamic>('/api/rowland-ward/categories'),
        _client.get<dynamic>('/api/rowland-ward/classes'),
        _client.get<dynamic>('/api/public-trophies/legacy-species'),
        _client.get<dynamic>('/api/public-trophies/firearm-types'),
      ]);

      return LeaderboardFilterOptions.fromApiResponses(
        categoriesData: responses[0].data,
        classesData: responses[1].data,
        legacySpeciesData: responses[2].data,
        firearmTypesData: responses[3].data,
      );
    }, fallback: 'Kon nie filteropsies laai nie.');
  }

  @override
  Future<List<LeaderboardTrophy>> getLeaderboardTrophies(
    LeaderboardQuery query,
  ) {
    return guardApi(() async {
      final response = await _client.get<dynamic>(
        '/api/public-trophies/leaderboard',
        queryParameters: query.toQueryParameters(),
      );
      return parseEach(
        requireList(response.data),
        LeaderboardTrophy.fromJson,
        label: 'leaderboard trophy',
      );
    }, fallback: 'Kon nie die trofeeranglys laai nie.');
  }

  @override
  Future<PublicTrophyDetail> getPublicTrophyDetail(int trophyId) {
    return guardApi(() async {
      final response = await _client.get<dynamic>(
        '/api/public-trophies/$trophyId',
      );
      return PublicTrophyDetail.fromJson(requireMap(response.data));
    }, fallback: 'Kon nie die trofeebesonderhede laai nie.');
  }
}

class LeaderboardFilterOptions {
  const LeaderboardFilterOptions({
    required this.categories,
    required this.legacySpecies,
    required this.firearmTypes,
  });

  final List<LeaderboardCategoryOption> categories;
  final List<String> legacySpecies;
  final List<String> firearmTypes;

  factory LeaderboardFilterOptions.fromApiResponses({
    required dynamic categoriesData,
    required dynamic classesData,
    required dynamic legacySpeciesData,
    required dynamic firearmTypesData,
  }) {
    final categories = _requireMapList(
      categoriesData,
    ).map(LeaderboardCategoryOption.fromJson).toList();
    final species = _requireMapList(
      classesData,
    ).map(LeaderboardSpeciesOption.fromJson).toList();

    return LeaderboardFilterOptions(
      categories: [
        for (final category in categories)
          category.copyWith(
            species: species
                .where((candidate) => candidate.categoryId == category.id)
                .toList(),
          ),
      ],
      legacySpecies: _requireStringList(legacySpeciesData),
      firearmTypes: _requireStringList(firearmTypesData),
    );
  }
}

class LeaderboardCategoryOption {
  const LeaderboardCategoryOption({
    required this.id,
    required this.displayNameAf,
    this.species = const [],
  });

  final String id;
  final String displayNameAf;
  final List<LeaderboardSpeciesOption> species;

  factory LeaderboardCategoryOption.fromJson(Map<String, dynamic> json) {
    return LeaderboardCategoryOption(
      id: json['id'] as String? ?? '',
      displayNameAf: json['displayNameAf'] as String? ?? '',
    );
  }

  LeaderboardCategoryOption copyWith({
    List<LeaderboardSpeciesOption>? species,
  }) {
    return LeaderboardCategoryOption(
      id: id,
      displayNameAf: displayNameAf,
      species: species ?? this.species,
    );
  }
}

class LeaderboardSpeciesOption {
  const LeaderboardSpeciesOption({
    required this.classId,
    required this.displayNameAf,
    required this.categoryId,
  });

  final String classId;
  final String displayNameAf;
  final String categoryId;

  factory LeaderboardSpeciesOption.fromJson(Map<String, dynamic> json) {
    return LeaderboardSpeciesOption(
      classId: json['id'] as String? ?? '',
      displayNameAf: json['displayNameAf'] as String? ?? '',
      categoryId: json['categoryId'] as String? ?? '',
    );
  }
}

class LeaderboardQuery {
  const LeaderboardQuery({
    required this.categoryLabel,
    required this.speciesLabel,
    this.categoryId,
    this.classId,
    this.legacyOnly = false,
    this.legacySpecies,
    this.firearmType,
  });

  final String categoryLabel;
  final String speciesLabel;
  final String? categoryId;
  final String? classId;
  final bool legacyOnly;
  final String? legacySpecies;
  final String? firearmType;

  Map<String, dynamic> toQueryParameters() => {
    'categoryId': ?categoryId,
    'classId': ?classId,
    if (legacyOnly) 'legacyOnly': true,
    'legacySpecies': ?legacySpecies,
    'firearmType': ?firearmType,
  };
}

enum LeaderboardMeasurementKind { rowlandWard, antlerSize, carcassWeight, none }

extension LeaderboardTrophyRanking on LeaderboardTrophy {
  LeaderboardMeasurementKind get rankingMeasurementKind {
    if (rowlandWard?.isValidLinkedClass == true) {
      return rowlandWard?.score == null
          ? LeaderboardMeasurementKind.none
          : LeaderboardMeasurementKind.rowlandWard;
    }
    if (antlerSizeCm != null) {
      return LeaderboardMeasurementKind.antlerSize;
    }
    if (carcassWeightKg != null) {
      return LeaderboardMeasurementKind.carcassWeight;
    }
    return LeaderboardMeasurementKind.none;
  }

  String get leaderboardGroupKey => rowlandWard?.isValidLinkedClass == true
      ? 'rowland-ward:${rowlandWard!.classId}'
      : 'legacy:${species.trim().toLowerCase()}';
}

class LeaderboardTrophy {
  const LeaderboardTrophy({
    required this.id,
    required this.photoPath,
    required this.species,
    required this.antlerSizeCm,
    required this.carcassWeightKg,
    required this.shotDistanceMeters,
    required this.firearmType,
    required this.ownerDisplayName,
    required this.selectedSortValue,
    this.rowlandWard,
  });

  final int id;
  final String photoPath;
  final String species;
  final double? antlerSizeCm;
  final double? carcassWeightKg;
  final int? shotDistanceMeters;
  final String firearmType;
  final String? ownerDisplayName;
  final double? selectedSortValue;
  final RowlandWardPublicSummary? rowlandWard;

  String get displaySpecies => rowlandWard?.isValidLinkedClass == true
      ? rowlandWard!.displayNameAf
      : species;

  factory LeaderboardTrophy.fromJson(Map<String, dynamic> json) {
    final rawRowlandWard = json['rowlandWard'];

    return LeaderboardTrophy(
      id: (json['id'] as num?)?.toInt() ?? 0,
      photoPath: json['photoPath'] as String? ?? '',
      species: json['species'] as String? ?? '',
      antlerSizeCm: (json['antlerSizeCm'] as num?)?.toDouble(),
      carcassWeightKg: (json['carcassWeightKg'] as num?)?.toDouble(),
      shotDistanceMeters: (json['shotDistanceMeters'] as num?)?.toInt(),
      firearmType: json['firearmType'] as String? ?? '',
      ownerDisplayName: json['ownerDisplayName'] as String?,
      selectedSortValue: (json['selectedSortValue'] as num?)?.toDouble(),
      rowlandWard: rawRowlandWard is Map
          ? RowlandWardPublicSummary.fromJson(
              Map<String, dynamic>.from(rawRowlandWard),
            )
          : null,
    );
  }
}

class PublicTrophyDetail {
  const PublicTrophyDetail({
    required this.id,
    required this.photoPath,
    required this.species,
    required this.antlerSizeCm,
    required this.carcassWeightKg,
    required this.shotDistanceMeters,
    required this.firearmType,
    required this.harvestDate,
    required this.ownerDisplayName,
    required this.notes,
    this.rowlandWard,
  });

  final int id;
  final String photoPath;
  final String species;
  final double? antlerSizeCm;
  final double? carcassWeightKg;
  final int? shotDistanceMeters;
  final String firearmType;
  final DateTime harvestDate;
  final String? ownerDisplayName;
  final String? notes;
  final RowlandWardPublicSummary? rowlandWard;

  String get displaySpecies => rowlandWard?.isValidLinkedClass == true
      ? rowlandWard!.displayNameAf
      : species;

  factory PublicTrophyDetail.fromJson(Map<String, dynamic> json) {
    final rawRowlandWard = json['rowlandWard'];

    return PublicTrophyDetail(
      id: (json['id'] as num?)?.toInt() ?? 0,
      photoPath: json['photoPath'] as String? ?? '',
      species: json['species'] as String? ?? '',
      antlerSizeCm: (json['antlerSizeCm'] as num?)?.toDouble(),
      carcassWeightKg: (json['carcassWeightKg'] as num?)?.toDouble(),
      shotDistanceMeters: (json['shotDistanceMeters'] as num?)?.toInt(),
      firearmType: json['firearmType'] as String? ?? '',
      harvestDate: requireDate(json['harvestDate'], 'harvestDate'),
      ownerDisplayName: json['ownerDisplayName'] as String?,
      notes: json['notes'] as String?,
      rowlandWard: rawRowlandWard is Map
          ? RowlandWardPublicSummary.fromJson(
              Map<String, dynamic>.from(rawRowlandWard),
            )
          : null,
    );
  }
}

List<String> _requireStringList(dynamic data) {
  if (data is! List) {
    throw const FormatException('Ongeldige lys antwoord.');
  }

  return data.map((value) {
    if (value is! String) {
      throw const FormatException('Ongeldige lys antwoord.');
    }
    return value;
  }).toList();
}

List<Map<String, dynamic>> _requireMapList(dynamic data) {
  if (data is! List) {
    throw const FormatException('Ongeldige lys antwoord.');
  }

  return data.map((value) {
    if (value is! Map) {
      throw const FormatException('Ongeldige lys antwoord.');
    }
    return Map<String, dynamic>.from(value);
  }).toList();
}
