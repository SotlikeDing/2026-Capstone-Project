import 'package:flutter/material.dart';

import '../models/rowland_ward_species.dart';
import '../theme/app_theme.dart';

class RowlandWardTrophySummary extends StatelessWidget {
  const RowlandWardTrophySummary({
    super.key,
    required this.score,
    required this.worldRecord,
    required this.minimumReached,
    this.showScore = true,
  });

  final RowlandWardDisplayQuantity? score;
  final RowlandWardDisplayQuantity? worldRecord;
  final bool minimumReached;
  final bool showScore;

  @override
  Widget build(BuildContext context) {
    final record = worldRecord;
    final trophyScore = score;
    if (trophyScore == null && record == null && !minimumReached) {
      return const SizedBox.shrink();
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (showScore && trophyScore != null)
          Text(
            'Rowland Ward-telling: ${trophyScore.formattedWithUnit}',
            style: const TextStyle(
              color: AppTheme.goldSoft,
              fontSize: 9.5,
              fontWeight: FontWeight.w800,
              height: 1.3,
            ),
          ),
        if (showScore && trophyScore != null && record != null)
          const SizedBox(height: 4),
        if (record != null)
          Text(
            'Rowland Ward WR: ${record.formattedWithUnit}',
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 9.5,
              fontWeight: FontWeight.w700,
              height: 1.3,
            ),
          ),
        if (((showScore && trophyScore != null) || record != null) &&
            minimumReached)
          const SizedBox(height: 6),
        if (minimumReached)
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 5),
            decoration: BoxDecoration(
              color: AppTheme.olive.withValues(alpha: 0.22),
              borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
              border: Border.all(color: AppTheme.olive.withValues(alpha: 0.55)),
            ),
            child: const Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  Icons.emoji_events_rounded,
                  color: AppTheme.goldSoft,
                  size: 13,
                ),
                SizedBox(width: 5),
                Flexible(
                  child: Text(
                    'Rowland Ward',
                    style: TextStyle(
                      color: AppTheme.goldSoft,
                      fontSize: 9,
                      fontWeight: FontWeight.w900,
                      height: 1.2,
                    ),
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }
}
