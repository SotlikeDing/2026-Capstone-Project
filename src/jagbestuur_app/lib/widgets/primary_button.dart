import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

class PrimaryButton extends StatelessWidget {
  final String text;
  final IconData? icon;
  final VoidCallback onPressed;
  final bool isLoading;
  final bool secondary;

  const PrimaryButton({
    super.key,
    required this.text,
    required this.onPressed,
    this.icon,
    this.isLoading = false,
    this.secondary = false,
  });

  @override
  Widget build(BuildContext context) {
    final backgroundColor = secondary ? AppTheme.surface : AppTheme.olive;
    final foregroundColor = secondary ? AppTheme.goldSoft : AppTheme.cream;

    return SizedBox(
      width: double.infinity,
      height: 54,
      child: ElevatedButton.icon(
        onPressed: isLoading ? null : onPressed,
        icon: isLoading
            ? SizedBox(
                width: 18,
                height: 18,
                child: CircularProgressIndicator(
                  color: secondary ? AppTheme.goldSoft : AppTheme.cream,
                  strokeWidth: 2,
                ),
              )
            : Icon(icon ?? Icons.arrow_forward_rounded, size: 20),
        label: Text(
          text,
          style: const TextStyle(
            fontWeight: FontWeight.w800,
            fontSize: 15,
            letterSpacing: 0.2,
          ),
        ),
        style: ElevatedButton.styleFrom(
          backgroundColor: backgroundColor,
          foregroundColor: foregroundColor,
          disabledBackgroundColor: AppTheme.disabledSurface,
          disabledForegroundColor: AppTheme.disabledText,
          elevation: 0,
          shadowColor: Colors.transparent,
          padding: const EdgeInsets.symmetric(horizontal: 20),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
            side: BorderSide(
              color: secondary
                  ? AppTheme.border
                  : AppTheme.olive.withValues(alpha: 0.92),
            ),
          ),
        ),
      ),
    );
  }
}
