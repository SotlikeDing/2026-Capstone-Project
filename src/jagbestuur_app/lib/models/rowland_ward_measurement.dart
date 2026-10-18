class RowlandWardRational {
  RowlandWardRational(BigInt numerator, BigInt denominator)
    : assert(denominator != BigInt.zero),
      numerator = _normalisedNumerator(numerator, denominator),
      denominator = _normalisedDenominator(numerator, denominator);

  final BigInt numerator;
  final BigInt denominator;

  factory RowlandWardRational.fromJson(Map<String, dynamic> json) {
    final numerator = BigInt.tryParse(json['numerator']?.toString() ?? '');
    final denominator = BigInt.tryParse(json['denominator']?.toString() ?? '');
    if (numerator == null ||
        denominator == null ||
        denominator == BigInt.zero) {
      throw const FormatException('Ongeldige breukwaarde.');
    }
    return RowlandWardRational(numerator, denominator);
  }

  static RowlandWardRational? tryParse(String input) {
    final value = input.trim().replaceAll(',', '.');
    if (value.isEmpty) return null;

    final mixedMatch = RegExp(r'^(\d+)\s+(\d+)\/(\d+)$').firstMatch(value);
    if (mixedMatch != null) {
      final whole = BigInt.parse(mixedMatch.group(1)!);
      final fractionNumerator = BigInt.parse(mixedMatch.group(2)!);
      final fractionDenominator = BigInt.parse(mixedMatch.group(3)!);
      if (fractionDenominator == BigInt.zero ||
          fractionNumerator >= fractionDenominator) {
        return null;
      }
      return RowlandWardRational(
        whole * fractionDenominator + fractionNumerator,
        fractionDenominator,
      );
    }

    final fractionMatch = RegExp(r'^(\d+)\/(\d+)$').firstMatch(value);
    if (fractionMatch != null) {
      final fractionNumerator = BigInt.parse(fractionMatch.group(1)!);
      final fractionDenominator = BigInt.parse(fractionMatch.group(2)!);
      if (fractionDenominator == BigInt.zero) return null;
      return RowlandWardRational(fractionNumerator, fractionDenominator);
    }

    final decimalMatch = RegExp(r'^(\d+)(?:\.(\d+))?$').firstMatch(value);
    if (decimalMatch == null) return null;

    final wholeDigits = decimalMatch.group(1)!;
    final decimalDigits = decimalMatch.group(2);
    if (decimalDigits == null) {
      return RowlandWardRational(BigInt.parse(wholeDigits), BigInt.one);
    }

    final denominator = BigInt.from(10).pow(decimalDigits.length);
    return RowlandWardRational(
      BigInt.parse('$wholeDigits$decimalDigits'),
      denominator,
    );
  }

  bool isWholeMultipleOf(RowlandWardRational increment) {
    if (increment.numerator == BigInt.zero) return false;
    final scaledNumerator = numerator * increment.denominator;
    final scaledDenominator = denominator * increment.numerator;
    return scaledNumerator.remainder(scaledDenominator) == BigInt.zero;
  }

  RowlandWardRational multiply(RowlandWardRational other) {
    return RowlandWardRational(
      numerator * other.numerator,
      denominator * other.denominator,
    );
  }

  RowlandWardRational divide(RowlandWardRational other) {
    if (other.numerator == BigInt.zero) {
      throw ArgumentError.value(other, 'other', 'Kan nie deur nul deel nie.');
    }
    return RowlandWardRational(
      numerator * other.denominator,
      denominator * other.numerator,
    );
  }

  RowlandWardRational roundToNearestMultiple(RowlandWardRational increment) {
    if (increment.numerator <= BigInt.zero || numerator < BigInt.zero) {
      throw ArgumentError('Slegs nie-negatiewe waardes word ondersteun.');
    }

    final scaledNumerator = numerator * increment.denominator;
    final scaledDenominator = denominator * increment.numerator;
    var roundedUnits = scaledNumerator ~/ scaledDenominator;
    final remainder = scaledNumerator.remainder(scaledDenominator);
    if (remainder * BigInt.two >= scaledDenominator) {
      roundedUnits += BigInt.one;
    }

    return RowlandWardRational(
      increment.numerator * roundedUnits,
      increment.denominator,
    );
  }

  String formatDecimal({int maxDecimalPlaces = 2}) {
    if (maxDecimalPlaces < 0) {
      throw ArgumentError.value(
        maxDecimalPlaces,
        'maxDecimalPlaces',
        'Die aantal desimale plekke kan nie negatief wees nie.',
      );
    }

    final scale = BigInt.from(10).pow(maxDecimalPlaces);
    final absoluteNumerator = numerator.abs();
    final scaledNumerator = absoluteNumerator * scale;
    var rounded = scaledNumerator ~/ denominator;
    final remainder = scaledNumerator.remainder(denominator);
    if (remainder * BigInt.two >= denominator) {
      rounded += BigInt.one;
    }

    final sign = numerator.isNegative && rounded != BigInt.zero ? '-' : '';
    if (maxDecimalPlaces == 0) return '$sign$rounded';

    final digits = rounded.toString().padLeft(maxDecimalPlaces + 1, '0');
    final wholeLength = digits.length - maxDecimalPlaces;
    final whole = digits.substring(0, wholeLength);
    final fraction = digits
        .substring(wholeLength)
        .replaceFirst(RegExp(r'0+$'), '');
    return fraction.isEmpty ? '$sign$whole' : '$sign$whole.$fraction';
  }

  String formatMixedFraction() {
    final whole = numerator ~/ denominator;
    final remainder = numerator.remainder(denominator);
    if (remainder == BigInt.zero) return whole.toString();
    if (whole == BigInt.zero) return '$remainder/$denominator';
    return '$whole $remainder/$denominator';
  }

  String formatDecimalWhenPossible() {
    var remainingDenominator = denominator;
    var twos = 0;
    var fives = 0;
    while (remainingDenominator.remainder(BigInt.two) == BigInt.zero) {
      remainingDenominator ~/= BigInt.two;
      twos++;
    }
    while (remainingDenominator.remainder(BigInt.from(5)) == BigInt.zero) {
      remainingDenominator ~/= BigInt.from(5);
      fives++;
    }
    if (remainingDenominator != BigInt.one) return formatMixedFraction();

    final decimalPlaces = twos > fives ? twos : fives;
    if (decimalPlaces == 0) return numerator.toString();
    final scale = BigInt.from(10).pow(decimalPlaces);
    final scaled = numerator * (scale ~/ denominator);
    final digits = scaled.toString().padLeft(decimalPlaces + 1, '0');
    final wholeLength = digits.length - decimalPlaces;
    final whole = digits.substring(0, wholeLength);
    final fraction = digits
        .substring(wholeLength)
        .replaceFirst(RegExp(r'0+$'), '');
    return fraction.isEmpty ? whole : '$whole,$fraction';
  }

  Map<String, int> toJson() => {
    'numerator': numerator.toInt(),
    'denominator': denominator.toInt(),
  };

  static BigInt _normalisedNumerator(BigInt numerator, BigInt denominator) {
    final sign = denominator.isNegative ? -BigInt.one : BigInt.one;
    final divisor = numerator.gcd(denominator);
    return numerator * sign ~/ divisor;
  }

  static BigInt _normalisedDenominator(BigInt numerator, BigInt denominator) {
    final divisor = numerator.gcd(denominator);
    return denominator.abs() ~/ divisor;
  }
}

class RowlandWardMeasurementField {
  const RowlandWardMeasurementField({
    required this.id,
    required this.labelAf,
    required this.kind,
    required this.role,
    required this.unitCode,
    required this.inputUnitCode,
    required this.inputUnitLabelAf,
    this.increment,
  });

  final String id;
  final String labelAf;
  final String kind;
  final String role;
  final String unitCode;
  final String inputUnitCode;
  final String inputUnitLabelAf;
  final RowlandWardRational? increment;

  bool get isRequired => role.startsWith('required_');

  RowlandWardRational? parseInputValue(String input) {
    final parsed = RowlandWardRational.tryParse(input);
    if (parsed == null) return null;

    final requiredIncrement = increment;
    if (requiredIncrement == null || inputUnitCode != unitCode) {
      return parsed;
    }
    if (parsed.isWholeMultipleOf(requiredIncrement)) {
      return parsed;
    }

    final exactValue = parsed.roundToNearestMultiple(requiredIncrement);
    return exactValue.formatDecimal() == parsed.formatDecimal()
        ? exactValue
        : null;
  }

  factory RowlandWardMeasurementField.fromJson(Map<String, dynamic> json) {
    final rawIncrement = json['increment'];
    final rawApplicationInput = json['applicationInput'];
    final applicationInput = rawApplicationInput is Map
        ? Map<String, dynamic>.from(rawApplicationInput)
        : const <String, dynamic>{};
    final inputUnitCode =
        json['inputUnitCode']?.toString() ??
        applicationInput['unitCode']?.toString() ??
        json['unitCode']?.toString() ??
        '';
    final suppliedLabel = json['inputUnitLabelAf']?.toString() ?? '';

    return RowlandWardMeasurementField(
      id: json['id']?.toString() ?? '',
      labelAf: json['labelAf']?.toString() ?? '',
      kind: json['kind']?.toString() ?? '',
      role: json['role']?.toString() ?? '',
      unitCode: json['unitCode']?.toString() ?? '',
      inputUnitCode: inputUnitCode,
      inputUnitLabelAf: suppliedLabel.isNotEmpty
          ? suppliedLabel
          : _unitLabelAf(inputUnitCode),
      increment: rawIncrement is Map
          ? RowlandWardRational.fromJson(
              Map<String, dynamic>.from(rawIncrement),
            )
          : null,
    );
  }

  static String _unitLabelAf(String unitCode) {
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

class RowlandWardMeasurementRequirements {
  const RowlandWardMeasurementRequirements({
    required this.classId,
    required this.displayNameAf,
    required this.methodCode,
    required this.profileId,
    required this.fields,
  });

  final String classId;
  final String displayNameAf;
  final String methodCode;
  final String profileId;
  final List<RowlandWardMeasurementField> fields;

  List<RowlandWardMeasurementField> get requiredFields =>
      fields.where((field) => field.isRequired).toList(growable: false);

  factory RowlandWardMeasurementRequirements.fromJson(
    Map<String, dynamic> json,
  ) {
    final rawProfile = json['profile'];
    final profile = rawProfile is Map
        ? Map<String, dynamic>.from(rawProfile)
        : const <String, dynamic>{};
    final rawFields = profile['fields'];

    return RowlandWardMeasurementRequirements(
      classId: json['classId']?.toString() ?? '',
      displayNameAf: json['displayNameAf']?.toString() ?? '',
      methodCode: json['methodCode']?.toString() ?? '',
      profileId: profile['id']?.toString() ?? '',
      fields: rawFields is List
          ? rawFields
                .whereType<Map>()
                .map(
                  (field) => RowlandWardMeasurementField.fromJson(
                    Map<String, dynamic>.from(field),
                  ),
                )
                .where(
                  (field) => field.id.isNotEmpty && field.labelAf.isNotEmpty,
                )
                .toList(growable: false)
          : const [],
    );
  }
}

class RowlandWardMeasurementValue {
  const RowlandWardMeasurementValue({
    required this.fieldId,
    required this.kind,
    this.unitCode,
    this.rational,
    this.integerValue,
    this.booleanValue,
  });

  final String fieldId;
  final String kind;
  final String? unitCode;
  final RowlandWardRational? rational;
  final int? integerValue;
  final bool? booleanValue;

  factory RowlandWardMeasurementValue.fromSnapshotJson(
    Map<String, dynamic> json,
  ) {
    final rawInputQuantity = json['inputQuantity'];
    final inputQuantity = rawInputQuantity is Map
        ? Map<String, dynamic>.from(rawInputQuantity)
        : null;
    final rawValue = inputQuantity?['value'];

    return RowlandWardMeasurementValue(
      fieldId: json['fieldId']?.toString() ?? '',
      kind: json['kind']?.toString() ?? '',
      unitCode: inputQuantity?['unitCode']?.toString(),
      rational: rawValue is Map
          ? RowlandWardRational.fromJson(Map<String, dynamic>.from(rawValue))
          : null,
      integerValue: (json['integerValue'] as num?)?.toInt(),
      booleanValue: json['booleanValue'] as bool?,
    );
  }

  String inputTextFor(RowlandWardMeasurementField field) {
    final value = rational;
    if (value != null) {
      return value.formatDecimal();
    }
    return integerValue?.toString() ?? '';
  }

  Map<String, dynamic> toRequestJson() {
    return switch (kind) {
      'rational' => {'unitCode': unitCode, 'value': rational?.toJson()},
      'integer' => {'integerValue': integerValue},
      'boolean' => {'booleanValue': booleanValue},
      _ => const <String, dynamic>{},
    };
  }
}

class RowlandWardEvaluationSaveRequest {
  const RowlandWardEvaluationSaveRequest({
    required this.classId,
    required this.measurements,
    this.sourceVersionId,
  });

  final String classId;
  final String? sourceVersionId;
  final Map<String, RowlandWardMeasurementValue> measurements;

  Map<String, dynamic> toJson() => {
    'classId': classId,
    if (sourceVersionId?.trim().isNotEmpty == true)
      'sourceVersionId': sourceVersionId,
    'measurements': {
      for (final entry in measurements.entries)
        entry.key: entry.value.toRequestJson(),
    },
  };
}
