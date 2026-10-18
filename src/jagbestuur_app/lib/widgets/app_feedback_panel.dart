import 'package:flutter/material.dart';

import '../theme/app_theme.dart';
import 'hunting_card.dart';

/// A shared, quiet presentation for non-content states such as loading and
/// recoverable errors. Screen-specific actions remain owned by their screens.
class AppFeedbackPanel extends StatelessWidget {
  const AppFeedbackPanel({
    super.key,
    required this.icon,
    required this.title,
    this.message,
    this.isLoading = false,
    this.action,
  });

  final IconData icon;
  final String title;
  final String? message;
  final bool isLoading;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      backgroundColor: AppTheme.surface,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Container(
            width: 48,
            height: 48,
            decoration: BoxDecoration(
              color: AppTheme.gold.withValues(alpha: 0.12),
              shape: BoxShape.circle,
              border: Border.all(color: AppTheme.gold.withValues(alpha: 0.26)),
            ),
            child: isLoading
                ? const Padding(
                    padding: EdgeInsets.all(13),
                    child: CircularProgressIndicator(strokeWidth: 2.4),
                  )
                : Icon(icon, color: AppTheme.goldSoft, size: 25),
          ),
          const SizedBox(height: 16),
          Text(
            title,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.titleLarge,
          ),
          if (message != null) ...[
            const SizedBox(height: 8),
            Text(
              message!,
              textAlign: TextAlign.center,
              style: Theme.of(
                context,
              ).textTheme.bodyMedium?.copyWith(color: AppTheme.muted),
            ),
          ],
          if (action != null) ...[const SizedBox(height: 20), action!],
        ],
      ),
    );
  }
}
