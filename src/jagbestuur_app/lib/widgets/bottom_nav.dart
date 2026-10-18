import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

class AppBottomNav extends StatelessWidget {
  final int currentIndex;
  final ValueChanged<int> onTap;
  final List<AppBottomNavItem> items;

  const AppBottomNav({
    super.key,
    required this.currentIndex,
    required this.onTap,
    required this.items,
  });

  @override
  Widget build(BuildContext context) {
    const borderRadius = BorderRadius.vertical(
      top: Radius.circular(AppTheme.radiusLarge),
    );

    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: AppTheme.surface,
        borderRadius: borderRadius,
        border: Border.all(color: AppTheme.border.withValues(alpha: 0.82)),
        boxShadow: const [
          BoxShadow(
            color: Color(0x52000000),
            blurRadius: 18,
            offset: Offset(0, -4),
          ),
        ],
      ),
      child: SafeArea(
        top: false,
        minimum: const EdgeInsets.only(top: 3),
        child: BottomNavigationBar(
          currentIndex: currentIndex,
          onTap: onTap,
          backgroundColor: Colors.transparent,
          selectedItemColor: AppTheme.goldSoft,
          unselectedItemColor: AppTheme.muted.withValues(alpha: 0.72),
          showSelectedLabels: true,
          showUnselectedLabels: true,
          type: BottomNavigationBarType.fixed,
          elevation: 0,
          selectedFontSize: 10.5,
          unselectedFontSize: 9.5,
          selectedLabelStyle: const TextStyle(
            fontWeight: FontWeight.w700,
            letterSpacing: 0.1,
            height: 1.15,
          ),
          unselectedLabelStyle: const TextStyle(
            fontWeight: FontWeight.w600,
            height: 1.15,
          ),
          unselectedIconTheme: const IconThemeData(size: 21),
          items: [
            for (final item in items)
              BottomNavigationBarItem(
                icon: Icon(item.icon),
                activeIcon: _SelectedNavIcon(icon: item.activeIcon),
                label: item.label,
              ),
          ],
        ),
      ),
    );
  }
}

class AppBottomNavItem {
  const AppBottomNavItem({
    required this.icon,
    required this.activeIcon,
    required this.label,
  });
  final IconData icon;
  final IconData activeIcon;
  final String label;
}

class _SelectedNavIcon extends StatelessWidget {
  const _SelectedNavIcon({required this.icon});

  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 40,
      height: 27,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: AppTheme.olive.withValues(alpha: 0.32),
        borderRadius: BorderRadius.circular(99),
        border: Border.all(color: AppTheme.gold.withValues(alpha: 0.24)),
      ),
      child: Icon(icon, size: 20),
    );
  }
}
