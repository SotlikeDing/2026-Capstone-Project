import '../utils/json_parsing.dart';

/// One hunter on a booking: the main hunter (who pays) or an added hunter.
class BookingHunter {
  const BookingHunter({
    required this.companionId,
    required this.name,
    required this.status,
    this.email,
    this.isMainHunter = false,
    this.isYou = false,
  });

  /// 0 for the main hunter.
  final int companionId;
  final String name;

  /// Only sent to the main hunter.
  final String? email;

  /// 'Main', 'Linked' or 'Pending' (invited, not registered/verified yet).
  final String status;
  final bool isMainHunter;
  final bool isYou;

  bool get isPending => status == 'Pending';

  factory BookingHunter.fromJson(Map<String, dynamic> json) {
    return BookingHunter(
      companionId: tryParseInt(json['companionId']) ?? 0,
      name: json['name'] as String? ?? 'Jagter',
      email: json['email'] as String?,
      status: json['status'] as String? ?? 'Linked',
      isMainHunter: json['isMainHunter'] as bool? ?? false,
      isYou: json['isYou'] as bool? ?? false,
    );
  }
}

/// The hunters on one booking as the signed-in hunter may see them.
class BookingHunters {
  const BookingHunters({
    required this.bookingId,
    required this.maxHunters,
    required this.openSlots,
    required this.canManage,
    required this.callerIsMainHunter,
    required this.hunters,
  });

  final int bookingId;
  final int maxHunters;
  final int openSlots;

  /// The signed-in hunter is the main hunter and the booking is confirmed.
  final bool canManage;
  final bool callerIsMainHunter;
  final List<BookingHunter> hunters;

  factory BookingHunters.fromJson(Map<String, dynamic> json) {
    final raw = json['hunters'];
    return BookingHunters(
      bookingId: tryParseInt(json['bookingId']) ?? 0,
      maxHunters: tryParseInt(json['maxHunters']) ?? 1,
      openSlots: tryParseInt(json['openSlots']) ?? 0,
      canManage: json['canManage'] as bool? ?? false,
      callerIsMainHunter: json['callerIsMainHunter'] as bool? ?? false,
      hunters: raw is List
          ? raw
                .whereType<Map>()
                .map(
                  (h) => BookingHunter.fromJson(Map<String, dynamic>.from(h)),
                )
                .toList()
          : const [],
    );
  }
}
