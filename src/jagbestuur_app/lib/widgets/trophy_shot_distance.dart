import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

class TrophyShotDistance extends StatelessWidget {
  const TrophyShotDistance({
    super.key,
    required this.meters,
    this.color = AppTheme.muted,
    this.fontSize = 10.5,
  });

  final int? meters;
  final Color color;
  final double fontSize;

  @override
  Widget build(BuildContext context) {
    final distance = meters;
    if (distance == null) {
      return const SizedBox.shrink();
    }

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(Icons.social_distance_rounded, color: color, size: 14),
        const SizedBox(width: 5),
        Flexible(
          child: Text(
            'Skootafstand: $distance m',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              color: color,
              fontSize: fontSize,
              fontWeight: FontWeight.w700,
              height: 1.2,
            ),
          ),
        ),
      ],
    );
  }
}
