import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/models/app_session.dart';
import 'package:jagbestuur_app/models/shared_booking.dart';

void main() {
  test('unsupported mobile role remains restricted even with capabilities', () {
    final session = AppSession.fromJson({
      'userId': 7,
      'userRoleId': 9,
      'role': 'Farmer',
      'roleCode': 'FARMER',
      'capabilities': [MobileCapabilities.viewBookingMedia],
    });

    expect(session.isSupportedRole, isFalse);
    expect(session.can(MobileCapabilities.viewBookingMedia), isFalse);
  });

  test('supported role uses only server-issued capabilities', () {
    final session = AppSession.fromJson({
      'userId': 7,
      'userRoleId': 9,
      'role': 'VG',
      'roleCode': 'VG',
      'capabilities': [MobileCapabilities.viewRecap],
    });

    expect(session.isVg, isTrue);
    expect(session.can(MobileCapabilities.viewRecap), isTrue);
    expect(session.can(MobileCapabilities.publishRecap), isFalse);
  });

  test('only a PH can use the private recap publishing capability', () {
    AppSession session(String role) => AppSession.fromJson({
      'userId': 7,
      'userRoleId': 9,
      'role': role,
      'roleCode': role,
      'capabilities': [MobileCapabilities.publishRecap],
    });

    expect(session('Hunter').canPublishPrivateRecap, isFalse);
    expect(session('VG').canPublishPrivateRecap, isFalse);
    expect(session('PH').canPublishPrivateRecap, isTrue);
  });

  test('trophy submission keeps its explicit supporting media ids', () {
    final submission = TrophySubmission.fromJson({
      'submissionId': 12,
      'bookingId': 5,
      'species': 'Kudu',
      'supportingMediaIds': [91, '92', null],
      'supportingPhotoCount': 2,
      'createdAt': '2026-10-11T10:00:00Z',
    });

    expect(submission.supportingMediaIds, [91, 92]);
    expect(submission.supportingPhotoCount, 2);
    expect(submission.status, 'Pending');
  });

  test('booking media distinguishes VG video clips from photos', () {
    final clip = SharedBookingMedia.fromJson({
      'mediaId': 31,
      'bookingId': 5,
      'uploaderRole': 'VG',
      'contentType': 'video/mp4',
      'sourceType': 'Video',
      'durationSeconds': 12.5,
      'createdAt': '2026-10-11T10:00:00Z',
    });
    final legacyPhoto = SharedBookingMedia.fromJson({
      'mediaId': 32,
      'createdAt': '2026-10-11T10:00:00Z',
    });

    expect(clip.isVideo, isTrue);
    expect(clip.durationSeconds, 12.5);
    expect(legacyPhoto.isVideo, isFalse);
    expect(legacyPhoto.sourceType, 'Photo');
  });

  test('team rating view parses members and skips malformed rows', () {
    final team = BookingTeamRating.fromJson({
      'bookingId': 5,
      'canRate': true,
      'members': [
        {'userId': 2, 'name': 'Piet', 'roleCode': 'PH', 'myStars': 4},
        {'userId': 5, 'name': 'Vera', 'roleCode': 'VG'},
        'not a member',
      ],
    });

    expect(team.canRate, isTrue);
    expect(team.members.map((m) => m.userId), [2, 5]);
    expect(team.members.first.myStars, 4);
    expect(team.members.first.roleLabel, 'Professionele jagter');
    expect(team.members.last.myStars, isNull);
  });

  test('cleanup status exposes the configured retention deadline', () {
    final status = RecapCleanupStatus.fromJson({
      'status': 'CleanupPending',
      'notBefore': '2026-10-14T10:00:00Z',
      'attemptCount': 0,
    });

    expect(status.notBefore, DateTime.utc(2026, 10, 14, 10));
    expect(status.isComplete, isFalse);
  });
}
