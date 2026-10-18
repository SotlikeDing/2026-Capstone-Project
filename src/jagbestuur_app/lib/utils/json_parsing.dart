/// Tolerant readers for API and on-disk JSON values.
library;

/// Parses an ISO-8601 date. Returns null for missing or malformed values.
DateTime? tryParseDate(Object? value) {
  if (value is! String || value.trim().isEmpty) {
    return null;
  }
  return DateTime.tryParse(value.trim());
}

/// Parses a required ISO-8601 date, throwing [FormatException] (not a
/// [TypeError]) so API guards can turn it into a friendly message.
DateTime requireDate(Object? value, String field) {
  final parsed = tryParseDate(value);
  if (parsed == null) {
    throw FormatException('Ongeldige of ontbrekende datum: $field');
  }
  return parsed;
}

/// Serialises a timestamp as UTC so it round-trips across time zone changes.
String toUtcIso(DateTime value) => value.toUtc().toIso8601String();

/// Reads an integer that may arrive as int, double or numeric string.
int? tryParseInt(Object? value) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  if (value is String) return int.tryParse(value.trim());
  return null;
}
