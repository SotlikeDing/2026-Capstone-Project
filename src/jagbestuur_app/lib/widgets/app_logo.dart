import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

class AppLogo extends StatelessWidget {
  final bool compact;
  final bool showTagline;

  const AppLogo({super.key, this.compact = false, this.showTagline = true});

  static const String _logoAsset = 'lib/assets/logos/logo.png';

  @override
  Widget build(BuildContext context) {
    final logoWidth = compact ? 170.0 : 240.0;
    final titleSize = compact ? 28.0 : 40.0;
    final subtitleSize = compact ? 12.0 : 15.0;

    return Column(
      children: [
        Container(
          padding: EdgeInsets.symmetric(
            horizontal: compact ? 10 : 14,
            vertical: compact ? 6 : 10,
          ),
          decoration: BoxDecoration(
            color: AppTheme.surface.withValues(alpha: 0.88),
            borderRadius: BorderRadius.circular(
              compact ? AppTheme.radiusMedium : AppTheme.radiusLarge,
            ),
            border: Border.all(color: AppTheme.border.withValues(alpha: 0.82)),
            boxShadow: const [
              BoxShadow(
                color: Color(0x28000000),
                blurRadius: 12,
                offset: Offset(0, 4),
              ),
            ],
          ),
          child: Image.asset(
            _logoAsset,
            width: logoWidth,
            fit: BoxFit.contain,
            errorBuilder: (context, error, stackTrace) {
              return Column(
                children: [
                  Icon(
                    Icons.image_not_supported_rounded,
                    size: compact ? 36 : 46,
                    color: AppTheme.gold,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Logo nie gevind nie',
                    style: TextStyle(
                      color: AppTheme.muted,
                      fontSize: compact ? 12 : 13,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ],
              );
            },
          ),
        ),
        if (!compact) ...[
          const SizedBox(height: 14),
          Text(
            'Jagbestuur',
            textAlign: TextAlign.center,
            style: TextStyle(
              color: AppTheme.ink,
              fontSize: titleSize,
              fontWeight: FontWeight.w900,
              letterSpacing: 0.6,
            ),
          ),
          if (showTagline) ...[
            const SizedBox(height: 4),
            Text(
              'Slim jagbeplanning, eenvoudig bestuur',
              textAlign: TextAlign.center,
              style: TextStyle(
                color: AppTheme.gold,
                fontSize: subtitleSize,
                fontWeight: FontWeight.w800,
                letterSpacing: 0.3,
              ),
            ),
          ],
        ],
      ],
    );
  }
}
