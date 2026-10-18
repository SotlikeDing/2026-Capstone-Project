class RowlandWardPreference {
  const RowlandWardPreference({required this.enabled});

  final bool enabled;

  factory RowlandWardPreference.fromJson(Map<String, dynamic> json) {
    return RowlandWardPreference(
      enabled: json['rowlandWardEnabled'] as bool? ?? false,
    );
  }
}
