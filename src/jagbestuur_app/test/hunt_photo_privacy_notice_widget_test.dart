import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/widgets/hunt_photo_privacy_notice.dart';

void main() {
  testWidgets('photo import notice explains local metadata and privacy', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(home: Scaffold(body: HuntPhotoPrivacyNotice())),
    );

    expect(find.text(huntPhotoPrivacyNoticeText), findsOneWidget);
    expect(find.textContaining('Foto’s bly privaat'), findsOneWidget);
    expect(find.textContaining('self plaas'), findsOneWidget);
  });
}
