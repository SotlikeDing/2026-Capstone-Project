import '../utils/json_parsing.dart';

class Booking {
  final int id;
  final String title;
  final String farmName;
  final String location;
  final DateTime startDate;
  final DateTime endDate;
  final String status;
  final String hunterName;
  final int huntGuests;
  final int nonHuntGuests;
  final int? farmId;
  final double? farmAverageRating;
  final int farmReviewCount;
  final bool canRateFarm;
  final int? myFarmRatingStars;
  final String? myFarmRatingDescription;

  /// User id of the farm's owner, used to start a chat about this booking.
  /// Null when the booking's package isn't linked to a farmer.
  final int? farmOwnerUserId;

  /// True when this hunter booked (and pays for) the trip; false when the main
  /// hunter added them to it - [hunterName] is then the main hunter.
  final bool isMainHunter;

  /// The booking's hunter count (main hunter included) and how many are on it.
  final int maxHunters;
  final int hunterCount;

  /// This hunter is the main hunter and the booking is confirmed, so they may
  /// add or remove the other hunters.
  final bool canManageHunters;

  const Booking({
    required this.id,
    required this.title,
    required this.farmName,
    required this.location,
    required this.startDate,
    required this.endDate,
    required this.status,
    required this.hunterName,
    required this.huntGuests,
    required this.nonHuntGuests,
    this.farmId,
    this.farmAverageRating,
    this.farmReviewCount = 0,
    this.canRateFarm = false,
    this.myFarmRatingStars,
    this.myFarmRatingDescription,
    this.farmOwnerUserId,
    this.isMainHunter = true,
    this.maxHunters = 1,
    this.hunterCount = 1,
    this.canManageHunters = false,
  });

  factory Booking.fromJson(Map<String, dynamic> json) {
    return Booking(
      id: tryParseInt(json['id']) ?? (throw const FormatException('id')),
      title: json['title'] as String? ?? 'Bespreking',
      farmName: json['farmName'] as String? ?? 'Jagplaas',
      location: json['location'] as String? ?? 'Ligging onbekend',
      startDate: requireDate(json['startDate'], 'startDate'),
      endDate: requireDate(json['endDate'], 'endDate'),
      status: json['status'] as String? ?? 'Wag Nog',
      hunterName: json['hunterName'] as String? ?? 'Jagter',
      huntGuests: tryParseInt(json['huntGuests']) ?? 0,
      nonHuntGuests: tryParseInt(json['nonHuntGuests']) ?? 0,
      farmId: tryParseInt(json['farmId']),
      farmAverageRating: (json['farmAverageRating'] as num?)?.toDouble(),
      farmReviewCount: tryParseInt(json['farmReviewCount']) ?? 0,
      canRateFarm: json['canRateFarm'] as bool? ?? false,
      myFarmRatingStars: tryParseInt(json['myFarmRatingStars']),
      myFarmRatingDescription: json['myFarmRatingDescription'] as String?,
      farmOwnerUserId: tryParseInt(json['farmOwnerUserId']),
      // Absent from servers that predate multi-hunter bookings.
      isMainHunter: json['isMainHunter'] as bool? ?? true,
      maxHunters: tryParseInt(json['maxHunters']) ?? 1,
      hunterCount: tryParseInt(json['hunterCount']) ?? 1,
      canManageHunters: json['canManageHunters'] as bool? ?? false,
    );
  }

  bool get hasFarmReviews => farmAverageRating != null && farmReviewCount > 0;

  bool get isCompleted => status.trim().toLowerCase() == 'voltooi';

  bool get isPast {
    if (isCompleted) return true;

    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final bookingEndDay = DateTime(endDate.year, endDate.month, endDate.day);

    return bookingEndDay.isBefore(today);
  }

  String get date {
    final start = _formatDate(startDate);
    final end = _formatDate(endDate);

    if (start == end) {
      return start;
    }

    return '$start - $end';
  }

  static String _formatDate(DateTime value) {
    const months = [
      'Jan',
      'Feb',
      'Mrt',
      'Apr',
      'Mei',
      'Jun',
      'Jul',
      'Aug',
      'Sep',
      'Okt',
      'Nov',
      'Des',
    ];

    return '${value.day} ${months[value.month - 1]} ${value.year}';
  }
}
