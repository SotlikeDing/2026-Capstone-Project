import 'package:flutter/services.dart';

import '../models/rowland_ward_measurement.dart';

final _centimetresPerInch = RowlandWardRational(
  BigInt.from(127),
  BigInt.from(50),
);

String formatMeasurementNumber(num value) {
  final rational = RowlandWardRational.tryParse(value.toString());
  if (rational != null) return rational.formatDecimal();

  final fixed = value.toDouble().toStringAsFixed(2);
  return fixed.replaceFirst(RegExp(r'\.?0+$'), '');
}

String formatCentimetresAsInches(double centimetres) {
  final centimetreValue = RowlandWardRational.tryParse(centimetres.toString());
  if (centimetreValue == null) return formatMeasurementNumber(centimetres);

  return centimetreValue.divide(_centimetresPerInch).formatDecimal();
}

double? parseInchesAsCentimetres(String input) {
  final inches = RowlandWardRational.tryParse(input);
  if (inches == null) return null;

  final centimetres = inches.multiply(_centimetresPerInch);
  return centimetres.numerator.toDouble() / centimetres.denominator.toDouble();
}

class TwoDecimalTextInputFormatter extends TextInputFormatter {
  const TwoDecimalTextInputFormatter();

  static final RegExp _validInput = RegExp(r'^\d*(?:[\.,]\d{0,2})?$');

  @override
  TextEditingValue formatEditUpdate(
    TextEditingValue oldValue,
    TextEditingValue newValue,
  ) {
    return _validInput.hasMatch(newValue.text) ? newValue : oldValue;
  }
}
