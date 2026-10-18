import 'dart:async';

import 'package:flutter/material.dart';

import '../models/app_session.dart';
import '../services/rowland_ward_preference_controller.dart';
import '../services/user_media_maintenance.dart';
import '../widgets/bottom_nav.dart';
import 'bookings_screen.dart';
import 'home_screen.dart';
import 'klets_screen.dart';
import 'map_screen.dart';
import 'profile_screen.dart';
import 'trophy_approvals_screen.dart';
import 'trophy_room_screen.dart';

class MainShell extends StatefulWidget {
  const MainShell({super.key, required this.session});
  final AppSession session;
  @override
  State<MainShell> createState() => _MainShellState();
}

enum _ShellTab { home, bookings, map, chats, trophyRoom, approvals, profile }

class _MainShellState extends State<MainShell> {
  int _selectedIndex = 0;
  final Set<int> _visited = {0};
  late List<_ShellTab> _tabs;
  late List<int> _activations;
  final RowlandWardPreferenceController _rowland =
      RowlandWardPreferenceController.instance;

  @override
  void initState() {
    super.initState();
    _configure();
    if (widget.session.isHunter) {
      unawaited(_rowland.loadForUser(widget.session.userId, force: true));
      unawaited(UserMediaMaintenance.run());
    }
  }

  void _configure() {
    // Every role starts on the home screen.
    final session = widget.session;
    _tabs = session.isHunter
        ? [
            _ShellTab.home,
            _ShellTab.bookings,
            _ShellTab.map,
            _ShellTab.chats,
            _ShellTab.trophyRoom,
            _ShellTab.profile,
          ]
        : session.isPh
        ? [
            _ShellTab.home,
            _ShellTab.bookings,
            _ShellTab.approvals,
            _ShellTab.chats,
            _ShellTab.profile,
          ]
        : session.isVg
        ? [
            _ShellTab.home,
            _ShellTab.bookings,
            _ShellTab.chats,
            _ShellTab.profile,
          ]
        : [_ShellTab.home, _ShellTab.chats, _ShellTab.profile];
    _activations = List<int>.filled(_tabs.length, 0);
  }

  @override
  void didUpdateWidget(covariant MainShell oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.session.userId != widget.session.userId ||
        oldWidget.session.role != widget.session.role) {
      setState(() {
        _selectedIndex = 0;
        _visited
          ..clear()
          ..add(0);
        _configure();
      });
      if (widget.session.isHunter) {
        unawaited(_rowland.loadForUser(widget.session.userId, force: true));
        unawaited(UserMediaMaintenance.run());
      } else {
        _rowland.reset();
      }
    }
  }

  @override
  void dispose() {
    _rowland.reset();
    super.dispose();
  }

  Widget _build(int index) {
    if (!_visited.contains(index)) return const SizedBox.shrink();
    final user = widget.session.userId;
    final activation = _activations[index];
    return switch (_tabs[index]) {
      _ShellTab.home => HomeScreen(
        key: ValueKey('home-$user-${widget.session.role}'),
        displayName: widget.session.displayName,
        session: widget.session,
        activation: activation,
      ),
      _ShellTab.bookings => BookingsScreen(
        key: ValueKey('bookings-$user-${widget.session.userRoleId}'),
        userRoleId: widget.session.userRoleId,
        currentUserId: user,
        session: widget.session,
        activation: activation,
      ),
      _ShellTab.map => MapScreen(key: ValueKey('map-$user')),
      _ShellTab.chats => KletsScreen(
        key: ValueKey('klets-$user'),
        currentUserId: user,
        activation: activation,
      ),
      _ShellTab.trophyRoom => TrophyRoomScreen(
        key: ValueKey('trophies-$user'),
        activation: activation,
      ),
      _ShellTab.approvals => TrophyApprovalsScreen(
        key: ValueKey('approvals-$user'),
        activation: activation,
      ),
      _ShellTab.profile => ProfileScreen(
        key: ValueKey('profile-$user'),
        session: widget.session,
        preferenceController: _rowland,
      ),
    };
  }

  AppBottomNavItem _nav(_ShellTab tab) => switch (tab) {
    _ShellTab.home => const AppBottomNavItem(
      icon: Icons.home_outlined,
      activeIcon: Icons.home_rounded,
      label: 'Tuisblad',
    ),
    _ShellTab.bookings => const AppBottomNavItem(
      icon: Icons.event_note_outlined,
      activeIcon: Icons.event_note_rounded,
      label: 'Besprekings',
    ),
    _ShellTab.map => const AppBottomNavItem(
      icon: Icons.map_outlined,
      activeIcon: Icons.map_rounded,
      label: 'Kaart',
    ),
    _ShellTab.chats => const AppBottomNavItem(
      icon: Icons.chat_bubble_outline_rounded,
      activeIcon: Icons.chat_bubble_rounded,
      label: 'Klets',
    ),
    _ShellTab.trophyRoom => const AppBottomNavItem(
      icon: Icons.workspace_premium_outlined,
      activeIcon: Icons.workspace_premium_rounded,
      label: 'Trofeekamer',
    ),
    _ShellTab.approvals => const AppBottomNavItem(
      icon: Icons.fact_check_outlined,
      activeIcon: Icons.fact_check_rounded,
      label: 'Goedkeurings',
    ),
    _ShellTab.profile => const AppBottomNavItem(
      icon: Icons.person_outline,
      activeIcon: Icons.person_rounded,
      label: 'Profiel',
    ),
  };

  void _tap(int index) {
    setState(() {
      if (index != _selectedIndex && _visited.contains(index)) {
        _activations[index]++;
      }
      _visited.add(index);
      _selectedIndex = index;
    });
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    extendBody: true,
    body: IndexedStack(
      index: _selectedIndex,
      children: [for (var i = 0; i < _tabs.length; i++) _build(i)],
    ),
    bottomNavigationBar: AppBottomNav(
      currentIndex: _selectedIndex,
      items: [for (final tab in _tabs) _nav(tab)],
      onTap: _tap,
    ),
  );
}
