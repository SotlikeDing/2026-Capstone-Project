import 'rowland_ward_species.dart';

class RowlandWardPublicSummary {
  const RowlandWardPublicSummary({
    required this.classId,
    required this.displayNameAf,
    required this.methodCode,
    required this.sourceVersionId,
    required this.minimumReached,
    this.score,
    this.worldRecord,
  });

  final String classId;
  final String displayNameAf;
  final String methodCode;
  final String sourceVersionId;
  final bool minimumReached;
  final RowlandWardDisplayQuantity? score;
  final RowlandWardDisplayQuantity? worldRecord;

  bool get isValidLinkedClass =>
      classId.trim().isNotEmpty && displayNameAf.trim().isNotEmpty;

  bool get hasDisplaySummary =>
      isValidLinkedClass &&
      (score != null || worldRecord != null || minimumReached);

  factory RowlandWardPublicSummary.fromJson(Map<String, dynamic> json) {
    return RowlandWardPublicSummary(
      classId: json['classId']?.toString() ?? '',
      displayNameAf: json['displayNameAf']?.toString() ?? '',
      methodCode: json['methodCode']?.toString() ?? '',
      sourceVersionId: json['sourceVersionId']?.toString() ?? '',
      minimumReached: json['minimumReached'] == true,
      score: RowlandWardDisplayQuantity.fromQuantitySnapshot(json['score']),
      worldRecord: RowlandWardDisplayQuantity.fromQuantitySnapshot(
        json['worldRecord'],
      ),
    );
  }
}
