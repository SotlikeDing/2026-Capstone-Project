import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

class HuntingCard extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;
  final Color? backgroundColor;
  final Gradient? gradient;
  final double radius;

  const HuntingCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(18),
    this.onTap,
    this.backgroundColor,
    this.gradient,
    this.radius = AppTheme.radiusLarge,
  });

  @override
  Widget build(BuildContext context) {
    final borderRadius = BorderRadius.circular(radius);

    final decoration = BoxDecoration(
      color: gradient == null ? (backgroundColor ?? AppTheme.card) : null,
      gradient: gradient,
      borderRadius: borderRadius,
      border: Border.all(color: AppTheme.border.withValues(alpha: 0.82)),
      boxShadow: const [
        BoxShadow(
          color: Color(0x2E000000),
          blurRadius: 12,
          offset: Offset(0, 4),
        ),
      ],
    );

    final card = Ink(
      decoration: decoration,
      child: Container(width: double.infinity, padding: padding, child: child),
    );

    if (onTap == null) {
      return card;
    }

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        borderRadius: borderRadius,
        splashColor: AppTheme.gold.withValues(alpha: 0.10),
        highlightColor: AppTheme.gold.withValues(alpha: 0.05),
        child: card,
      ),
    );
  }
}
