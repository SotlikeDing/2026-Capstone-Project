class AppSession {
  const AppSession({
    required this.userId,
    required this.userRoleId,
    required this.email,
    required this.displayName,
    required this.rememberMe,
    required this.role,
    required this.roleCode,
    required this.capabilities,
  });

  final int userId;
  final int userRoleId;
  final String email;
  final String displayName;
  final bool rememberMe;
  final String role;
  final String roleCode;
  final Set<String> capabilities;

  bool get isSupportedRole => role == 'Hunter' || role == 'PH' || role == 'VG';
  bool get isHunter => role == 'Hunter';
  bool get isPh => role == 'PH';
  bool get isVg => role == 'VG';
  bool can(String capability) =>
      isSupportedRole && capabilities.contains(capability);
  bool get canPublishPrivateRecap =>
      isPh && can(MobileCapabilities.publishRecap);

  Map<String, dynamic> toJson() {
    return {
      'userId': userId,
      'userRoleId': userRoleId,
      'email': email,
      'displayName': displayName,
      'rememberMe': rememberMe,
      'role': role,
      'roleCode': roleCode,
      'capabilities': capabilities.toList()..sort(),
    };
  }

  factory AppSession.fromJson(Map<String, dynamic> json) {
    return AppSession(
      userId: (json['userId'] as num?)?.toInt() ?? 0,
      userRoleId: (json['userRoleId'] as num?)?.toInt() ?? 0,
      email: json['email'] as String? ?? '',
      displayName: json['displayName'] as String? ?? '',
      rememberMe: json['rememberMe'] as bool? ?? false,
      role: json['role'] as String? ?? 'Restricted',
      roleCode: json['roleCode'] as String? ?? '',
      capabilities: ((json['capabilities'] as List<dynamic>?) ?? const [])
          .whereType<String>()
          .toSet(),
    );
  }
}

abstract final class MobileCapabilities {
  static const viewTrophyRoom = 'canViewTrophyRoom';
  static const submitTrophy = 'canSubmitTrophy';
  static const reviewTrophies = 'canReviewTrophies';
  static const viewBookingMedia = 'canViewBookingMedia';
  static const uploadBookingMedia = 'canUploadBookingMedia';
  static const uploadBookingVideo = 'canUploadBookingVideo';
  static const editOwnBookingMedia = 'canEditOwnBookingMedia';
  static const moderateBookingMedia = 'canModerateBookingMedia';
  static const submitRoute = 'canSubmitRoute';
  static const selectRoute = 'canSelectRoute';
  static const generateRecap = 'canGenerateRecap';
  static const publishRecap = 'canPublishRecap';
  static const viewRecap = 'canViewRecap';
}
