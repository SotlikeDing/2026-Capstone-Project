class PermitStatus {
  final int id;
  final String name;
  final String status;
  final DateTime? issued;
  final DateTime? expires;

  const PermitStatus({
    required this.id,
    required this.name,
    required this.status,
    this.issued,
    this.expires,
  });

  factory PermitStatus.fromJson(Map<String, dynamic> json) {
    return PermitStatus(
      id: json['id'] as int,
      name: json['name'] as String? ?? 'Permit',
      status: json['status'] as String? ?? 'Wag Nog',
      issued: json['issued'] == null
          ? null
          : DateTime.tryParse(json['issued'] as String),
      expires: json['expires'] == null
          ? null
          : DateTime.tryParse(json['expires'] as String),
    );
  }
}
