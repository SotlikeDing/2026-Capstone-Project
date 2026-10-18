import 'package:flutter/material.dart';

import '../config/app_config.dart';
import '../models/trophy.dart';
import '../services/auth_service.dart';
import '../services/rowland_ward_preference_controller.dart';
import '../services/trophy_service.dart';
import '../theme/app_theme.dart';
import '../utils/measurement_format.dart';
import '../widgets/app_logo.dart';
import '../widgets/hunting_card.dart';
import '../widgets/recap_section.dart';
import '../widgets/recap_video_section.dart';
import '../widgets/rowland_ward_trophy_summary.dart';
import '../widgets/trophy_shot_distance.dart';
import 'add_trophy_screen.dart';
import 'trophy_hunt_media_screen.dart';
import 'leaderboard_filter_screen.dart';

class TrofeekamerScreen extends StatefulWidget {
  const TrofeekamerScreen({
    super.key,
    this.rowlandWardPreferenceController,
    this.trophyService,
    this.activation = 0,
  });

  final RowlandWardPreferenceController? rowlandWardPreferenceController;
  final TrophyService? trophyService;

  /// Changes each time the tab is reopened; triggers a reload.
  final int activation;

  @override
  State<TrofeekamerScreen> createState() => _TrofeekamerScreenState();
}

class TrophyRoomScreen extends TrofeekamerScreen {
  const TrophyRoomScreen({
    super.key,
    super.rowlandWardPreferenceController,
    super.trophyService,
    super.activation,
  });
}

class _TrofeekamerScreenState extends State<TrofeekamerScreen> {
  late final TrophyService _trophyService;
  late final RowlandWardPreferenceController _rowlandWardPreferenceController;
  late Future<List<Trophy>> _trophiesFuture;

  @override
  void initState() {
    super.initState();
    _rowlandWardPreferenceController =
        widget.rowlandWardPreferenceController ??
        RowlandWardPreferenceController.instance;
    _rowlandWardPreferenceController.addListener(
      _handleRowlandWardPreferenceChanged,
    );

    _trophyService = widget.trophyService ?? TrophyService();
    _trophiesFuture = _trophyService.getMyTrophies();
  }

  @override
  void didUpdateWidget(covariant TrofeekamerScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.activation != widget.activation) {
      _reloadTrophies();
    }
  }

  @override
  void dispose() {
    _rowlandWardPreferenceController.removeListener(
      _handleRowlandWardPreferenceChanged,
    );
    super.dispose();
  }

  void _handleRowlandWardPreferenceChanged() {
    if (!mounted) {
      return;
    }

    setState(() {});
  }

  void _reloadTrophies() {
    setState(() {
      _trophiesFuture = _trophyService.getMyTrophies();
    });
  }

  Future<void> _refreshTrophies() async {
    _reloadTrophies();
    try {
      await _trophiesFuture;
    } catch (_) {}
  }

  Future<void> _openEditTrophyForm(Trophy trophy) async {
    final result = await Navigator.of(context).push<TrophyFormResult>(
      MaterialPageRoute(builder: (_) => AddTrophyScreen(trophy: trophy)),
    );

    if (!mounted || result == null) {
      return;
    }

    _reloadTrophies();
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          result == TrophyFormResult.deleted
              ? 'Trofee is suksesvol verwyder.'
              : 'Trofee is suksesvol bygewerk.',
        ),
      ),
    );
  }

  Future<void> _openHuntMedia(Trophy trophy) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(builder: (_) => TrophyHuntMediaScreen(trophy: trophy)),
    );
    if (mounted) {
      setState(() {});
    }
  }

  void _openLeaderboard() {
    Navigator.of(
      context,
    ).push(MaterialPageRoute(builder: (_) => const LeaderboardFilterScreen()));
  }

  List<Widget> _buildWallItems(
    AsyncSnapshot<List<Trophy>> snapshot, {
    required double photoHeight,
    required bool showRowlandWard,
  }) {
    if (snapshot.connectionState == ConnectionState.waiting) {
      return const <Widget>[
        _WallStateTile(
          icon: Icons.hourglass_top_rounded,
          title: 'Trofeë word gelaai',
          message: 'Jou digitale trofeewall word voorberei.',
          showProgress: true,
        ),
      ];
    }

    if (snapshot.hasError) {
      return <Widget>[
        _WallStateTile(
          icon: Icons.cloud_off_rounded,
          title: 'Kon nie laai nie',
          message: 'Kon nie jou trofeë laai nie.',
          accentColor: AppTheme.dangerSoft,
          onRetry: _reloadTrophies,
        ),
      ];
    }

    final trophies = snapshot.data ?? const <Trophy>[];
    if (trophies.isEmpty) {
      return const <Widget>[
        _WallStateTile(
          icon: Icons.emoji_events_outlined,
          title: 'Nog geen trofeë nie',
          message:
              'Dien ’n trofee vanuit ’n voltooide bespreking in. Dit verskyn hier nadat die toegewysde PH dit goedgekeur het.',
        ),
      ];
    }

    return trophies
        .map(
          (trophy) => _TrophyWallTile(
            trophy: trophy,
            photoHeight: photoHeight,
            showRowlandWard: showRowlandWard,
            onEdit: () => _openEditTrophyForm(trophy),
            onMedia: () => _openHuntMedia(trophy),
          ),
        )
        .toList();
  }

  @override
  Widget build(BuildContext context) {
    final useTwoColumns = MediaQuery.sizeOf(context).width >= 360;
    final columnCount = useTwoColumns ? 2 : 1;
    final tileHeight = useTwoColumns ? 330.0 : 344.0;
    final photoHeight = useTwoColumns ? 158.0 : 178.0;

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: FutureBuilder<List<Trophy>>(
            future: _trophiesFuture,
            builder: (context, snapshot) {
              final isLoading =
                  snapshot.connectionState == ConnectionState.waiting;
              final trophies = snapshot.data ?? const <Trophy>[];
              final count = isLoading || snapshot.hasError
                  ? null
                  : trophies.length;
              final hasTrophies =
                  !isLoading && !snapshot.hasError && trophies.isNotEmpty;
              final showRowlandWard =
                  _rowlandWardPreferenceController.isEnabled;
              final hasRowlandWardSummary =
                  showRowlandWard &&
                  trophies.any(
                    (trophy) =>
                        trophy.rowlandWard?.hasTrophyRoomSummary == true,
                  );
              final gridColumnCount = hasTrophies ? columnCount : 1;
              final gridTileHeight = hasTrophies
                  ? tileHeight + (hasRowlandWardSummary ? 100 : 0)
                  : 278.0;
              final wallItems = _buildWallItems(
                snapshot,
                photoHeight: photoHeight,
                showRowlandWard: showRowlandWard,
              );

              return RefreshIndicator(
                onRefresh: _refreshTrophies,
                child: CustomScrollView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  slivers: [
                    SliverToBoxAdapter(
                      child: Padding(
                        padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            const Center(
                              child: AppLogo(compact: true, showTagline: false),
                            ),
                            const SizedBox(height: 20),
                            _TrophyRoomHeader(count: count),
                            const SizedBox(height: 22),
                            const _WallHeading(),
                          ],
                        ),
                      ),
                    ),
                    SliverPadding(
                      padding: const EdgeInsets.symmetric(horizontal: 16),
                      sliver: SliverGrid(
                        gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                          crossAxisCount: gridColumnCount,
                          crossAxisSpacing: 12,
                          mainAxisSpacing: 12,
                          mainAxisExtent: gridTileHeight,
                        ),
                        delegate: SliverChildBuilderDelegate(
                          (context, index) => wallItems[index],
                          childCount: wallItems.length,
                        ),
                      ),
                    ),
                    SliverToBoxAdapter(
                      child: Padding(
                        padding: const EdgeInsets.fromLTRB(16, 28, 16, 120),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            const Text(
                              'TROFEERANGLYS',
                              style: TextStyle(
                                color: AppTheme.gold,
                                fontSize: 10,
                                fontWeight: FontWeight.w900,
                                letterSpacing: 1,
                              ),
                            ),
                            const SizedBox(height: 9),
                            _LeaderboardFeature(onTap: _openLeaderboard),
                            const SizedBox(height: 24),
                            const Divider(),
                            const SizedBox(height: 16),
                            const Text(
                              'Jag Recaps',
                              style: TextStyle(
                                color: AppTheme.ink,
                                fontSize: 22,
                                fontWeight: FontWeight.w900,
                              ),
                            ),
                            const SizedBox(height: 6),
                            const Text(
                              'Kyk vorige terugblikke en terugblik-video’s hier onder.',
                              style: TextStyle(
                                color: AppTheme.muted,
                                fontSize: 13,
                              ),
                            ),
                            const SizedBox(height: 16),
                            const RecapSection(),
                            const SizedBox(height: 24),
                            const RecapVideoSection(),
                          ],
                        ),
                      ),
                    ),
                  ],
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}

class _TrophyRoomHeader extends StatelessWidget {
  const _TrophyRoomHeader({required this.count});

  final int? count;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.end,
      children: [
        const Expanded(
          child: Text(
            'Trofeekamer',
            style: TextStyle(
              color: AppTheme.ink,
              fontSize: 30,
              fontWeight: FontWeight.w900,
              letterSpacing: -0.45,
              height: 1.05,
            ),
          ),
        ),
        if (count != null)
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
            decoration: BoxDecoration(
              color: AppTheme.olive.withValues(alpha: 0.2),
              borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
              border: Border.all(color: AppTheme.olive.withValues(alpha: 0.5)),
            ),
            child: Text(
              '$count trofeë',
              style: const TextStyle(
                color: AppTheme.goldSoft,
                fontSize: 11,
                fontWeight: FontWeight.w900,
              ),
            ),
          ),
      ],
    );
  }
}

class _WallHeading extends StatelessWidget {
  const _WallHeading();

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Container(
          width: 42,
          height: 42,
          decoration: BoxDecoration(
            color: AppTheme.gold.withValues(alpha: 0.12),
            borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
          ),
          child: const Icon(
            Icons.workspace_premium_rounded,
            color: AppTheme.goldSoft,
            size: 22,
          ),
        ),
        const SizedBox(width: 12),
        const Expanded(
          child: Text(
            'MY TROFEË',
            style: TextStyle(
              color: AppTheme.gold,
              fontSize: 10,
              fontWeight: FontWeight.w900,
              letterSpacing: 0.9,
            ),
          ),
        ),
        const Chip(
          avatar: Icon(Icons.verified_user_outlined, size: 17),
          label: Text('Slegs PH-goedgekeur'),
        ),
      ],
    );
  }
}

class _TrophyWallTile extends StatelessWidget {
  const _TrophyWallTile({
    required this.trophy,
    required this.photoHeight,
    required this.showRowlandWard,
    required this.onEdit,
    required this.onMedia,
  });

  final Trophy trophy;
  final double photoHeight;
  final bool showRowlandWard;
  final VoidCallback onEdit;
  final VoidCallback onMedia;

  String? get _measurementValue {
    if (trophy.antlerSizeCm != null) {
      return '${formatCentimetresAsInches(trophy.antlerSizeCm!)} duim';
    }
    if (trophy.carcassWeightKg != null) {
      return '${formatMeasurementNumber(trophy.carcassWeightKg!)} kg';
    }
    return null;
  }

  String get _measurementLabel {
    if (trophy.antlerSizeCm != null) {
      return 'HORINGLENGTE';
    }
    if (trophy.carcassWeightKg != null) {
      return 'KARKASGEWIG';
    }
    return 'GEEN METING';
  }

  @override
  Widget build(BuildContext context) {
    final measurement = _measurementValue;

    return HuntingCard(
      padding: EdgeInsets.zero,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          ClipRRect(
            borderRadius: const BorderRadius.vertical(
              top: Radius.circular(AppTheme.radiusLarge),
            ),
            child: SizedBox(
              height: photoHeight,
              child: Stack(
                fit: StackFit.expand,
                children: [
                  _TrophyPhoto(trophy: trophy),
                  const IgnorePointer(
                    child: DecoratedBox(
                      decoration: BoxDecoration(
                        gradient: LinearGradient(
                          begin: Alignment.topCenter,
                          end: Alignment.bottomCenter,
                          colors: [Colors.transparent, Color(0x66000000)],
                        ),
                      ),
                    ),
                  ),
                  Positioned(
                    top: 10,
                    right: 10,
                    child: _PrivacyStatus(isPublic: trophy.isPublic),
                  ),
                ],
              ),
            ),
          ),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 11, 12, 10),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    trophy.displaySpecies,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: AppTheme.ink,
                      fontSize: 15.5,
                      fontWeight: FontWeight.w900,
                      height: 1.15,
                    ),
                  ),
                  const SizedBox(height: 7),
                  Text(
                    _measurementLabel,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: AppTheme.muted,
                      fontSize: 8.5,
                      fontWeight: FontWeight.w900,
                      letterSpacing: 0.65,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    measurement ?? '—',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: measurement == null
                          ? AppTheme.disabledText
                          : AppTheme.goldSoft,
                      fontSize: 17,
                      fontWeight: FontWeight.w900,
                    ),
                  ),
                  if (showRowlandWard &&
                      trophy.rowlandWard?.hasTrophyRoomSummary == true) ...[
                    const SizedBox(height: 8),
                    RowlandWardTrophySummary(
                      score: trophy.rowlandWard?.score,
                      worldRecord: trophy.rowlandWard?.currentWorldRecord,
                      minimumReached:
                          trophy.rowlandWard?.hasReachedMinimum == true,
                    ),
                  ],
                  if (trophy.shotDistanceMeters != null) ...[
                    const SizedBox(height: 7),
                    TrophyShotDistance(
                      meters: trophy.shotDistanceMeters,
                      color: AppTheme.muted,
                    ),
                  ],
                  const Spacer(),
                  const Divider(),
                  SizedBox(
                    height: 36,
                    child: Row(
                      children: [
                        Expanded(
                          child: TextButton.icon(
                            onPressed: onMedia,
                            icon: const Icon(
                              Icons.photo_library_outlined,
                              size: 15,
                            ),
                            label: const Text('Jagmedia'),
                          ),
                        ),
                        Expanded(
                          child: TextButton.icon(
                            onPressed: onEdit,
                            icon: const Icon(Icons.edit_outlined, size: 15),
                            label: const Text('Wysig'),
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _TrophyPhoto extends StatelessWidget {
  const _TrophyPhoto({required this.trophy});

  final Trophy trophy;

  String get _photoUrl => AppConfig.resolveApiUrl(trophy.photoPath);

  bool get _hasNetworkPhoto {
    final photoPath = _photoUrl;
    if (photoPath.isEmpty) {
      return false;
    }

    final uri = Uri.tryParse(photoPath);
    return uri != null && (uri.scheme == 'http' || uri.scheme == 'https');
  }

  Map<String, String>? get _photoRequestHeaders {
    final token = AuthService.instance.accessToken;
    if (token == null || token.isEmpty) {
      return null;
    }
    return {'Authorization': 'Bearer $token'};
  }

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: 'Trofeefoto van ${trophy.displaySpecies}',
      child: _hasNetworkPhoto
          ? Image.network(
              _photoUrl,
              headers: _photoRequestHeaders,
              fit: BoxFit.cover,
              // Decode at screen size: a 4000 px photo would otherwise take ~64 MB.
              cacheWidth: 1080,
              loadingBuilder: (context, child, loadingProgress) {
                if (loadingProgress == null) {
                  return child;
                }
                return const _PhotoLoading();
              },
              errorBuilder: (_, _, _) => const _PhotoUnavailable(),
            )
          : const _PhotoUnavailable(),
    );
  }
}

class _PhotoLoading extends StatelessWidget {
  const _PhotoLoading();

  @override
  Widget build(BuildContext context) {
    return Container(
      color: AppTheme.surface,
      alignment: Alignment.center,
      child: const SizedBox(
        width: 24,
        height: 24,
        child: CircularProgressIndicator(strokeWidth: 2),
      ),
    );
  }
}

class _PhotoUnavailable extends StatelessWidget {
  const _PhotoUnavailable();

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: const BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xFF302A23), Color(0xFF1B1814)],
        ),
      ),
      alignment: Alignment.center,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 48,
            height: 48,
            decoration: BoxDecoration(
              color: AppTheme.gold.withValues(alpha: 0.1),
              shape: BoxShape.circle,
            ),
            child: const Icon(
              Icons.emoji_events_outlined,
              color: AppTheme.goldSoft,
              size: 25,
            ),
          ),
          const SizedBox(height: 8),
          const Text(
            'Geen foto',
            style: TextStyle(
              color: AppTheme.muted,
              fontSize: 10,
              fontWeight: FontWeight.w800,
            ),
          ),
        ],
      ),
    );
  }
}

class _PrivacyStatus extends StatelessWidget {
  const _PrivacyStatus({required this.isPublic});

  final bool isPublic;

  @override
  Widget build(BuildContext context) {
    final color = isPublic ? AppTheme.olive : AppTheme.cardLight;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.92),
        borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
        border: Border.all(color: AppTheme.cream.withValues(alpha: 0.22)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            isPublic ? Icons.public_rounded : Icons.lock_outline_rounded,
            color: AppTheme.cream,
            size: 12,
          ),
          const SizedBox(width: 4),
          Text(
            isPublic ? 'Publiek' : 'Privaat',
            style: const TextStyle(
              color: AppTheme.cream,
              fontSize: 8.5,
              fontWeight: FontWeight.w900,
            ),
          ),
        ],
      ),
    );
  }
}

class _WallStateTile extends StatelessWidget {
  const _WallStateTile({
    required this.icon,
    required this.title,
    required this.message,
    this.accentColor = AppTheme.goldSoft,
    this.showProgress = false,
    this.onRetry,
  });

  final IconData icon;
  final String title;
  final String message;
  final Color accentColor;
  final bool showProgress;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      padding: const EdgeInsets.all(16),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Container(
            width: 56,
            height: 56,
            decoration: BoxDecoration(
              color: accentColor.withValues(alpha: 0.12),
              shape: BoxShape.circle,
            ),
            child: showProgress
                ? Padding(
                    padding: const EdgeInsets.all(16),
                    child: CircularProgressIndicator(
                      color: accentColor,
                      strokeWidth: 2.2,
                    ),
                  )
                : Icon(icon, color: accentColor, size: 27),
          ),
          const SizedBox(height: 14),
          Text(
            title,
            textAlign: TextAlign.center,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              color: AppTheme.ink,
              fontSize: 16,
              fontWeight: FontWeight.w900,
            ),
          ),
          const SizedBox(height: 7),
          Text(
            message,
            textAlign: TextAlign.center,
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 11,
              height: 1.4,
            ),
          ),
          if (onRetry != null) ...[
            const SizedBox(height: 14),
            OutlinedButton.icon(
              onPressed: onRetry,
              icon: const Icon(Icons.refresh_rounded, size: 17),
              label: const Text('Probeer weer'),
            ),
          ],
        ],
      ),
    );
  }
}

class _LeaderboardFeature extends StatelessWidget {
  const _LeaderboardFeature({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      onTap: onTap,
      padding: const EdgeInsets.all(16),
      gradient: const LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [Color(0xFF30271E), Color(0xFF222019)],
      ),
      child: Row(
        children: [
          Container(
            width: 52,
            height: 52,
            decoration: BoxDecoration(
              color: AppTheme.gold.withValues(alpha: 0.14),
              borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
              border: Border.all(color: AppTheme.gold.withValues(alpha: 0.34)),
            ),
            child: const Icon(
              Icons.leaderboard_rounded,
              color: AppTheme.goldSoft,
              size: 25,
            ),
          ),
          const SizedBox(width: 14),
          const Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Bekyk trofeeranglys',
                  style: TextStyle(
                    color: AppTheme.ink,
                    fontSize: 16,
                    fontWeight: FontWeight.w900,
                  ),
                ),
                SizedBox(height: 5),
                Text(
                  'Vergelyk bestaande openbare trofeë met die huidige filters.',
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    color: AppTheme.muted,
                    fontSize: 11.5,
                    height: 1.35,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 10),
          const Icon(Icons.chevron_right_rounded, color: AppTheme.goldSoft),
        ],
      ),
    );
  }
}
