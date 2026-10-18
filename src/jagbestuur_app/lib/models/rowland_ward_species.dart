import 'rowland_ward_measurement.dart';

class RowlandWardDisplayQuantity {
  const RowlandWardDisplayQuantity({
    required this.value,
    required this.unitCode,
    required this.unitLabelAf,
  });

  final RowlandWardRational value;
  final String unitCode;
  final String unitLabelAf;

  String get formattedValue => value.formatDecimal();

  String get formattedWithUnit {
    final label = unitLabelAf.trim().isEmpty
        ? _fallbackUnitLabelAf(unitCode)
        : unitLabelAf.trim();
    return label.isEmpty ? formattedValue : '$formattedValue $label';
  }

  static RowlandWardDisplayQuantity? fromReferenceSnapshot(dynamic snapshot) {
    if (snapshot is! Map) return null;
    final reference = Map<String, dynamic>.from(snapshot);
    final rawQuantity = reference['quantity'];
    if (rawQuantity is! Map) return null;
    final quantity = Map<String, dynamic>.from(rawQuantity);
    return fromQuantitySnapshot(quantity['display']);
  }

  static RowlandWardDisplayQuantity? fromQuantitySnapshot(dynamic snapshot) {
    if (snapshot is! Map) return null;
    final quantity = Map<String, dynamic>.from(snapshot);
    final rawValue = quantity['value'];
    if (rawValue is! Map) return null;

    try {
      return RowlandWardDisplayQuantity(
        value: RowlandWardRational.fromJson(
          Map<String, dynamic>.from(rawValue),
        ),
        unitCode: quantity['unitCode']?.toString() ?? '',
        unitLabelAf: quantity['unitLabelAf']?.toString() ?? '',
      );
    } on FormatException {
      return null;
    }
  }

  static RowlandWardDisplayQuantity? fromEvaluatedQuantitySnapshot(
    dynamic snapshot,
  ) {
    if (snapshot is! Map) return null;
    final evaluated = Map<String, dynamic>.from(snapshot);
    return fromQuantitySnapshot(evaluated['display']);
  }

  static String _fallbackUnitLabelAf(String unitCode) {
    return switch (unitCode) {
      'inch' => 'duim',
      'kilogram' => 'kg',
      'pound' => 'pond',
      'foot' => 'voet',
      'count' => 'aantal',
      _ => unitCode,
    };
  }
}

class RowlandWardSpecies {
  const RowlandWardSpecies({
    required this.classId,
    required this.displayNameAf,
    required this.latinName,
    this.categoryId,
    this.methodCode,
    this.confidence,
    this.sourceVersionId,
    this.qualificationState,
    this.minimumReached,
    this.score,
    this.currentWorldRecord,
    this.measurements = const [],
  });

  final String classId;
  final String displayNameAf;
  final String latinName;
  final String? categoryId;
  final String? methodCode;
  final double? confidence;
  final String? sourceVersionId;
  final String? qualificationState;
  final bool? minimumReached;
  final RowlandWardDisplayQuantity? score;
  final RowlandWardDisplayQuantity? currentWorldRecord;
  final List<RowlandWardMeasurementValue> measurements;

  bool get isValidLinkedClass =>
      classId.trim().isNotEmpty && displayNameAf.trim().isNotEmpty;

  bool get hasReachedMinimum =>
      isValidLinkedClass &&
      (minimumReached == true || qualificationState == 'MINIMUM_REACHED');

  bool get hasTrophyRoomSummary =>
      isValidLinkedClass &&
      (score != null || currentWorldRecord != null || hasReachedMinimum);

  factory RowlandWardSpecies.fromSuggestionJson(Map<String, dynamic> json) {
    return RowlandWardSpecies(
      classId: json['classId']?.toString() ?? '',
      displayNameAf: json['displayNameAf']?.toString() ?? '',
      latinName: json['latinName']?.toString() ?? '',
      categoryId: json['categoryId']?.toString(),
      methodCode: json['methodCode']?.toString(),
      confidence: _asDouble(json['confidence']),
    );
  }

  factory RowlandWardSpecies.fromMatchJson(Map<String, dynamic> json) {
    final rawMeasurements = json['measurements'];

    return RowlandWardSpecies(
      classId: (json['id'] ?? json['classId'])?.toString() ?? '',
      displayNameAf: json['displayNameAf']?.toString() ?? '',
      latinName: json['latinName']?.toString() ?? '',
      categoryId: json['categoryId']?.toString(),
      methodCode: json['methodCode']?.toString(),
      sourceVersionId: json['sourceVersionId']?.toString(),
      qualificationState: json['qualificationState']?.toString(),
      minimumReached: json['minimumReached'] as bool?,
      score: RowlandWardDisplayQuantity.fromEvaluatedQuantitySnapshot(
        json['score'],
      ),
      currentWorldRecord: RowlandWardDisplayQuantity.fromReferenceSnapshot(
        json['currentWorldRecord'],
      ),
      measurements: rawMeasurements is List
          ? rawMeasurements
                .whereType<Map>()
                .map(
                  (measurement) => RowlandWardMeasurementValue.fromSnapshotJson(
                    Map<String, dynamic>.from(measurement),
                  ),
                )
                .where((measurement) => measurement.fieldId.isNotEmpty)
                .toList(growable: false)
          : const [],
    );
  }

  static double? _asDouble(dynamic value) {
    if (value is num) return value.toDouble();
    return double.tryParse(value?.toString() ?? '');
  }
}

class RowlandWardSpeciesMatchResult {
  const RowlandWardSpeciesMatchResult({
    required this.outcome,
    required this.normalizedInput,
    required this.suggestions,
    required this.requiresSelection,
    this.match,
    this.messageAf,
  });

  final String outcome;
  final String normalizedInput;
  final RowlandWardSpecies? match;
  final List<RowlandWardSpecies> suggestions;
  final bool requiresSelection;
  final String? messageAf;

  bool get canAcceptWithoutSelection {
    return match != null && (outcome == 'EXACT' || outcome == 'ALIAS');
  }

  bool get isHighConfidenceSuggestion {
    return outcome == 'HIGH_CONFIDENCE_FUZZY';
  }

  factory RowlandWardSpeciesMatchResult.fromJson(Map<String, dynamic> json) {
    final rawMatch = json['match'];
    final rawSuggestions = json['suggestions'];

    return RowlandWardSpeciesMatchResult(
      outcome: json['outcome']?.toString() ?? '',
      normalizedInput: json['normalizedInput']?.toString() ?? '',
      match: rawMatch is Map
          ? RowlandWardSpecies.fromMatchJson(
              Map<String, dynamic>.from(rawMatch),
            )
          : null,
      suggestions: rawSuggestions is List
          ? rawSuggestions
                .whereType<Map>()
                .map(
                  (item) => RowlandWardSpecies.fromSuggestionJson(
                    Map<String, dynamic>.from(item),
                  ),
                )
                .where(
                  (item) =>
                      item.classId.isNotEmpty && item.displayNameAf.isNotEmpty,
                )
                .toList(growable: false)
          : const [],
      requiresSelection: json['requiresSelection'] == true,
      messageAf: json['messageAf']?.toString(),
    );
  }
}
