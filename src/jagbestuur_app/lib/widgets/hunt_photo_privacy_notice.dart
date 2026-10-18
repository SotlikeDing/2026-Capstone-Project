import 'package:flutter/material.dart';

const String huntPhotoPrivacyNoticeText =
    'Foto’s kan die plek en datum bevat waar hulle geneem is. Dit word gebruik '
    'om foto’s op jou jagroete te plaas. As dié inligting ontbreek, verkeerd is '
    'of deur ’n ander app verwyder is, moet jy die foto dalk self plaas. '
    'Foto’s bly privaat tensy jy dit self uitvoer of deel.';

class HuntPhotoPrivacyNotice extends StatelessWidget {
  const HuntPhotoPrivacyNotice({super.key});

  @override
  Widget build(BuildContext context) {
    return const Text(huntPhotoPrivacyNoticeText);
  }
}
