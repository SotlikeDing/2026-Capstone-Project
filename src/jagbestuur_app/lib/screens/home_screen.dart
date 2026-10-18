import 'package:flutter/material.dart';

import '../models/app_session.dart';
import '../models/hunt_session.dart';
import '../models/hunter_statistics.dart';
import '../models/trophy.dart';
import '../services/hunt_storage_service.dart';
import '../services/trophy_service.dart';
import '../theme/app_theme.dart';
import '../utils/measurement_format.dart';
import '../widgets/app_logo.dart';
import '../widgets/hunting_card.dart';

typedef TrophyLoader = Future<List<Trophy>> Function();
typedef HuntLoader = Future<List<HuntSession>> Function();

class HomeScreen extends StatefulWidget {
  const HomeScreen({
    super.key,
    this.displayName = '',
    this.session,
    this.activation = 0,
    this.trophyLoader,
    this.huntLoader,
  });

  final String displayName;

  /// The signed-in user. Roles without a trophy room (PH, VG and web-only
  /// roles) still get this screen, just without the trophy request.
  final AppSession? session;

  /// Changes when the user returns to this tab so fresh trophy/map data is
  /// reflected without rebuilding the complete navigation shell.
  final int activation;
  final TrophyLoader? trophyLoader;
  final HuntLoader? huntLoader;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  late Future<_DashboardData> _dashboardFuture;

  TrophyLoader get _loadTrophies {
    final custom = widget.trophyLoader;
    if (custom != null) return custom;
    final session = widget.session;
    if (session != null && !session.can(MobileCapabilities.viewTrophyRoom)) {
      return () async => const <Trophy>[];
    }
    return TrophyService().getMyTrophies;
  }

  HuntLoader get _loadHunts =>
      widget.huntLoader ?? const HuntStorageService().listSessions;

  @override
  void initState() {
    super.initState();
    _dashboardFuture = _loadDashboard();
  }

  @override
  void didUpdateWidget(covariant HomeScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.activation != widget.activation ||
        oldWidget.trophyLoader != widget.trophyLoader ||
        oldWidget.huntLoader != widget.huntLoader) {
      _dashboardFuture = _loadDashboard();
    }
  }

  Future<_DashboardData> _loadDashboard() async {
    final trophyFuture = _loadTrophyData();
    final huntFuture = _loadHuntData();
    return _DashboardData(
      trophies: await trophyFuture,
      hunts: await huntFuture,
    );
  }

  Future<List<Trophy>?> _loadTrophyData() async {
    try {
      return await _loadTrophies();
    } catch (_) {
      return null;
    }
  }

  Future<List<HuntSession>?> _loadHuntData() async {
    try {
      return await _loadHunts();
    } catch (_) {
      return null;
    }
  }

  Future<void> _refresh() async {
    final future = _loadDashboard();
    // Block body: an arrow would return the Future from the callback, which
    // setState rejects (it throws in debug builds on pull-to-refresh).
    setState(() {
      _dashboardFuture = future;
    });
    await future;
  }

  String get _firstName {
    final trimmed = widget.displayName.trim();
    if (trimmed.isEmpty) return '';
    return trimmed.split(RegExp(r'\s+')).first;
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: FutureBuilder<_DashboardData>(
            future: _dashboardFuture,
            builder: (context, snapshot) {
              final data = snapshot.data;
              final waiting =
                  snapshot.connectionState == ConnectionState.waiting &&
                  data == null;

              return RefreshIndicator(
                onRefresh: _refresh,
                child: ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 12, 16, 122),
                  children: [
                    const Center(
                      child: AppLogo(compact: true, showTagline: false),
                    ),
                    const SizedBox(height: 20),
                    _DashboardHeader(firstName: _firstName),
                    const SizedBox(height: 22),
                    if (waiting)
                      const _LoadingDashboard()
                    else if (data == null || !data.hasAnyData)
                      _DashboardError(onRetry: _refresh)
                    else
                      _DashboardContent(data: data, session: widget.session),
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

class _DashboardData {
  const _DashboardData({required this.trophies, required this.hunts});

  final List<Trophy>? trophies;
  final List<HuntSession>? hunts;

  bool get hasAnyData => trophies != null || hunts != null;
  bool get isPartial => trophies == null || hunts == null;

  HunterStatistics get statistics => HunterStatistics.calculate(
    trophies: trophies ?? const <Trophy>[],
    hunts: hunts ?? const <HuntSession>[],
  );
}

class _DashboardHeader extends StatelessWidget {
  const _DashboardHeader({required this.firstName});

  final String firstName;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'JOU PERSOONLIKE JAGSTORIEK',
                style: TextStyle(
                  color: AppTheme.gold,
                  fontSize: 10,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 1.05,
                ),
              ),
              const SizedBox(height: 7),
              const Text(
                'Tuisblad',
                style: TextStyle(
                  color: AppTheme.ink,
                  fontSize: 30,
                  fontWeight: FontWeight.w900,
                  letterSpacing: -0.55,
                  height: 1.05,
                ),
              ),
              const SizedBox(height: 7),
              Text(
                firstName.isEmpty
                    ? 'Jou beste oomblikke, roetes en trofeë op een plek.'
                    : 'Welkom terug, $firstName. Hier is jou jagverhaal tot dusver.',
                style: const TextStyle(
                  color: AppTheme.muted,
                  fontSize: 13.5,
                  height: 1.45,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(width: 12),
        Container(
          width: 48,
          height: 48,
          decoration: BoxDecoration(
            color: AppTheme.olive.withValues(alpha: 0.2),
            borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
            border: Border.all(color: AppTheme.olive.withValues(alpha: 0.48)),
          ),
          child: const Icon(
            Icons.insights_rounded,
            color: AppTheme.goldSoft,
            size: 25,
          ),
        ),
      ],
    );
  }
}

class _DashboardContent extends StatelessWidget {
  const _DashboardContent({required this.data, this.session});

  final _DashboardData data;
  final AppSession? session;

  @override
  Widget build(BuildContext context) {
    final stats = data.statistics;
    final hasHero = stats.lifetimeHunts > 0 || stats.totalTrophies > 0;
    final hasHighlights =
        stats.furthestShot != null ||
        stats.trophyHighlight != null ||
        stats.longestRoute != null ||
        stats.longestHunt != null;
    final hasProfile =
        stats.topSpecies != null ||
        stats.topLocation != null ||
        stats.favouriteFirearm != null ||
        stats.longestAntlers != null ||
        stats.heaviestCarcass != null;
    final hasMilestones =
        stats.totalTrophies > 0 ||
        stats.uniqueSpecies > 0 ||
        stats.uniqueLocations > 0 ||
        stats.publicTrophies > 0 ||
        stats.rowlandWardQualifiers > 0 ||
        stats.shotsMarked > 0 ||
        stats.animalsFound > 0 ||
        stats.totalDistanceMeters > 0 ||
        stats.totalFieldTime > Duration.zero;
    final sections = <Widget>[];

    void addSection(Widget section) {
      if (sections.isNotEmpty) sections.add(const SizedBox(height: 28));
      sections.add(section);
    }

    final isHunter = session == null || session!.isHunter;
    if (!isHunter) addSection(_RoleWelcomeCard(session: session!));
    if (hasHero) addSection(_YearHero(stats: stats));
    if (hasHighlights) {
      addSection(
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const _SectionHeading(
              eyebrow: 'PERSOONLIKE BESTES',
              title: 'Jou Hoogtepunte',
            ),
            const SizedBox(height: 12),
            _HighlightsGrid(stats: stats),
          ],
        ),
      );
    }
    if (stats.huntsThisYear > 0) {
      addSection(
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const _SectionHeading(
              eyebrow: 'AKTIWITEIT',
              title: 'Jou Jaar In Die Veld',
            ),
            const SizedBox(height: 12),
            _YearActivityCard(stats: stats),
          ],
        ),
      );
    }
    if (hasProfile) {
      addSection(
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const _SectionHeading(
              eyebrow: 'JAGPROFIEL',
              title: 'Wat Jou Storie Vertel',
            ),
            const SizedBox(height: 12),
            _HunterProfileCard(stats: stats),
          ],
        ),
      );
    }
    if (hasMilestones) {
      addSection(
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const _SectionHeading(eyebrow: 'VERSAMELING', title: 'Mylpale'),
            const SizedBox(height: 12),
            _MilestonesCard(stats: stats),
          ],
        ),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (data.isPartial) ...[
          _PartialDataNotice(trophyDataUnavailable: data.trophies == null),
          const SizedBox(height: 12),
        ],
        ...sections,
      ],
    );
  }
}

class _YearHero extends StatelessWidget {
  const _YearHero({required this.stats});

  final HunterStatistics stats;

  @override
  Widget build(BuildContext context) {
    final primaryIsCurrentHunts = stats.huntsThisYear > 0;
    final primaryIsCurrentTrophies =
        !primaryIsCurrentHunts && stats.trophiesThisYear > 0;
    final primaryIsLifetimeHunts =
        !primaryIsCurrentHunts &&
        !primaryIsCurrentTrophies &&
        stats.lifetimeHunts > 0;
    final primaryValue = primaryIsCurrentHunts
        ? stats.huntsThisYear
        : primaryIsCurrentTrophies
        ? stats.trophiesThisYear
        : primaryIsLifetimeHunts
        ? stats.lifetimeHunts
        : stats.totalTrophies;
    final primaryLabel = primaryIsCurrentHunts
        ? (primaryValue == 1 ? 'Jag Hierdie Jaar' : 'Jagte Hierdie Jaar')
        : primaryIsCurrentTrophies
        ? (primaryValue == 1 ? 'Trofee Hierdie Jaar' : 'Trofeë Hierdie Jaar')
        : primaryIsLifetimeHunts
        ? 'Jagte Oor Jou Leeftyd'
        : 'Trofeë Oor Jou Leeftyd';
    final String? summary =
        primaryIsCurrentHunts && stats.lifetimeHunts > stats.huntsThisYear
        ? '${stats.lifetimeHunts} Jagte Oor Jou Leeftyd'
        : primaryIsCurrentTrophies &&
              stats.totalTrophies > stats.trophiesThisYear
        ? '${stats.totalTrophies} Trofeë Oor Jou Leeftyd'
        : null;
    final metrics = <_HeroMetricData>[];
    if (stats.trophiesThisYear > 0 && !primaryIsCurrentTrophies) {
      metrics.add(
        _HeroMetricData(
          value: '${stats.trophiesThisYear}',
          label: 'Trofeë Hierdie Jaar',
        ),
      );
    } else if (stats.totalTrophies > 0 &&
        !primaryIsCurrentTrophies &&
        (primaryIsCurrentHunts || primaryIsLifetimeHunts)) {
      metrics.add(
        _HeroMetricData(
          value: '${stats.totalTrophies}',
          label: 'Trofeë Leeftyd',
        ),
      );
    }
    if (stats.distanceThisYearMeters > 0) {
      metrics.add(
        _HeroMetricData(
          value: _formatDistance(stats.distanceThisYearMeters, compact: true),
          label: 'Afstand Hierdie Jaar',
        ),
      );
    } else if (stats.totalDistanceMeters > 0) {
      metrics.add(
        _HeroMetricData(
          value: _formatDistance(stats.totalDistanceMeters, compact: true),
          label: 'Totale Afstand',
        ),
      );
    }
    if (stats.averageHuntTime > Duration.zero) {
      metrics.add(
        _HeroMetricData(
          value: _formatDuration(stats.averageHuntTime),
          label: 'Gemiddelde Tyd',
        ),
      );
    }

    return HuntingCard(
      padding: EdgeInsets.zero,
      radius: AppTheme.radiusExtraLarge,
      gradient: const LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [Color(0xFF343B23), Color(0xFF241F17)],
      ),
      child: SizedBox(
        height: metrics.isEmpty ? 190 : 252,
        child: Stack(
          clipBehavior: Clip.hardEdge,
          children: [
            const Positioned.fill(
              child: CustomPaint(painter: _ContourPainter()),
            ),
            Positioned(
              right: -30,
              top: -28,
              child: Icon(
                Icons.track_changes_rounded,
                size: 190,
                color: AppTheme.goldSoft.withValues(alpha: 0.055),
              ),
            ),
            Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 10,
                          vertical: 6,
                        ),
                        decoration: BoxDecoration(
                          color: AppTheme.background.withValues(alpha: 0.4),
                          borderRadius: BorderRadius.circular(
                            AppTheme.radiusSmall,
                          ),
                          border: Border.all(
                            color: AppTheme.gold.withValues(alpha: 0.3),
                          ),
                        ),
                        child: Text(
                          'Jagjaar ${stats.year}',
                          style: const TextStyle(
                            color: AppTheme.goldSoft,
                            fontSize: 10,
                            fontWeight: FontWeight.w900,
                            letterSpacing: 1,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      if (stats.latestActivity != null)
                        Expanded(
                          child: Text(
                            'Laaste Aktiwiteit  ${_formatDate(stats.latestActivity!)}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            textAlign: TextAlign.end,
                            style: const TextStyle(
                              color: AppTheme.muted,
                              fontSize: 10.5,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        )
                      else
                        const Spacer(),
                    ],
                  ),
                  const Spacer(),
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      Text(
                        '$primaryValue',
                        style: const TextStyle(
                          color: AppTheme.ink,
                          fontSize: 58,
                          height: 0.86,
                          fontWeight: FontWeight.w900,
                          letterSpacing: -2,
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Padding(
                          padding: const EdgeInsets.only(bottom: 4),
                          child: Text(
                            primaryLabel,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              color: AppTheme.goldSoft,
                              fontSize: 13,
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                  if (summary != null) ...[
                    const SizedBox(height: 8),
                    Text(
                      summary,
                      style: const TextStyle(
                        color: AppTheme.muted,
                        fontSize: 12,
                      ),
                    ),
                  ],
                  if (metrics.isNotEmpty) ...[
                    const SizedBox(height: 18),
                    Container(
                      padding: const EdgeInsets.symmetric(vertical: 11),
                      decoration: BoxDecoration(
                        color: AppTheme.background.withValues(alpha: 0.34),
                        borderRadius: BorderRadius.circular(
                          AppTheme.radiusMedium,
                        ),
                        border: Border.all(
                          color: AppTheme.cream.withValues(alpha: 0.08),
                        ),
                      ),
                      child: Row(
                        children: [
                          for (
                            var index = 0;
                            index < metrics.length;
                            index++
                          ) ...[
                            if (index > 0) const _VerticalDivider(),
                            Expanded(
                              child: _HeroMetric(
                                value: metrics[index].value,
                                label: metrics[index].label,
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _HeroMetricData {
  const _HeroMetricData({required this.value, required this.label});

  final String value;
  final String label;
}

class _HeroMetric extends StatelessWidget {
  const _HeroMetric({required this.value, required this.label});

  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Text(
          value,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            color: AppTheme.ink,
            fontSize: 17,
            fontWeight: FontWeight.w900,
          ),
        ),
        const SizedBox(height: 3),
        Text(
          label,
          style: const TextStyle(
            color: AppTheme.muted,
            fontSize: 8.5,
            fontWeight: FontWeight.w900,
            letterSpacing: 0.75,
          ),
        ),
      ],
    );
  }
}

class _VerticalDivider extends StatelessWidget {
  const _VerticalDivider();

  @override
  Widget build(BuildContext context) {
    return Container(width: 1, height: 34, color: AppTheme.border);
  }
}

class _HighlightsGrid extends StatelessWidget {
  const _HighlightsGrid({required this.stats});

  final HunterStatistics stats;

  @override
  Widget build(BuildContext context) {
    final furthestShot = stats.furthestShot;
    final longestRoute = stats.longestRoute;
    final longestHunt = stats.longestHunt;
    final highlight = stats.trophyHighlight;

    final items = <_HighlightData>[];
    if (furthestShot?.shotDistanceMeters != null) {
      items.add(
        _HighlightData(
          icon: Icons.gps_fixed_rounded,
          eyebrow: 'Verste Skoot',
          value: '${furthestShot!.shotDistanceMeters} m',
          detail: _trophyContext(furthestShot),
          accent: AppTheme.goldSoft,
        ),
      );
    }
    if (highlight != null) {
      items.add(
        _HighlightData(
          icon: Icons.workspace_premium_rounded,
          eyebrow: 'Grootste Trofee',
          value: highlight.trophy.displaySpecies,
          detail: _formatTrophyHighlight(highlight),
          accent: const Color(0xFFC99B5E),
        ),
      );
    }
    if (longestRoute != null) {
      items.add(
        _HighlightData(
          icon: Icons.route_rounded,
          eyebrow: 'Langste Roete',
          value: _formatDistance(longestRoute.totalDistanceMeters),
          detail: _formatDate(longestRoute.startedAt),
          accent: const Color(0xFF98A876),
        ),
      );
    }
    if (longestHunt != null) {
      items.add(
        _HighlightData(
          icon: Icons.schedule_rounded,
          eyebrow: 'Langste Veldtyd',
          value: _formatDuration(longestHunt.duration),
          detail: _formatDate(longestHunt.startedAt),
          accent: const Color(0xFFB7A48A),
        ),
      );
    }

    return LayoutBuilder(
      builder: (context, constraints) {
        final maximumColumns = constraints.maxWidth >= 720 ? 4 : 2;
        final columns = items.length < maximumColumns
            ? items.length
            : maximumColumns;
        return GridView.builder(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          itemCount: items.length,
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: columns,
            crossAxisSpacing: 10,
            mainAxisSpacing: 10,
            mainAxisExtent: 154,
          ),
          itemBuilder: (context, index) => _HighlightCard(data: items[index]),
        );
      },
    );
  }
}

class _HighlightData {
  const _HighlightData({
    required this.icon,
    required this.eyebrow,
    required this.value,
    required this.detail,
    required this.accent,
  });

  final IconData icon;
  final String eyebrow;
  final String value;
  final String detail;
  final Color accent;
}

class _HighlightCard extends StatelessWidget {
  const _HighlightCard({required this.data});

  final _HighlightData data;

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 36,
            height: 36,
            decoration: BoxDecoration(
              color: data.accent.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
            ),
            child: Icon(data.icon, color: data.accent, size: 19),
          ),
          const Spacer(),
          Text(
            data.eyebrow,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 8.5,
              fontWeight: FontWeight.w900,
              letterSpacing: 0.65,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            data.value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              color: AppTheme.ink,
              fontSize: 18,
              fontWeight: FontWeight.w900,
              letterSpacing: -0.2,
            ),
          ),
          const SizedBox(height: 3),
          Text(
            data.detail,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              color: data.accent.withValues(alpha: 0.88),
              fontSize: 10.5,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

class _YearActivityCard extends StatelessWidget {
  const _YearActivityCard({required this.stats});

  static const _monthInitials = <String>[
    'J',
    'F',
    'M',
    'A',
    'M',
    'J',
    'J',
    'A',
    'S',
    'O',
    'N',
    'D',
  ];

  final HunterStatistics stats;

  @override
  Widget build(BuildContext context) {
    final maximum = stats.huntsByMonth.fold<int>(
      0,
      (current, value) => value > current ? value : current,
    );
    final busiestIndex = maximum == 0
        ? null
        : stats.huntsByMonth.indexOf(maximum);

    return HuntingCard(
      padding: const EdgeInsets.fromLTRB(17, 17, 17, 15),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                width: 42,
                height: 42,
                decoration: BoxDecoration(
                  color: AppTheme.olive.withValues(alpha: 0.18),
                  borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
                ),
                child: const Icon(
                  Icons.bar_chart_rounded,
                  color: AppTheme.goldSoft,
                  size: 22,
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '${stats.year} Aktiwiteit',
                      style: const TextStyle(
                        color: AppTheme.ink,
                        fontSize: 16,
                        fontWeight: FontWeight.w900,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      'Mees Aktief In ${_monthName(busiestIndex! + 1)} • $maximum ${maximum == 1 ? 'Jag' : 'Jagte'}',
                      style: const TextStyle(
                        color: AppTheme.muted,
                        fontSize: 11.5,
                      ),
                    ),
                  ],
                ),
              ),
              if (stats.distanceThisYearMeters > 0) ...[
                const SizedBox(width: 8),
                Text(
                  _formatDistance(stats.distanceThisYearMeters),
                  style: const TextStyle(
                    color: AppTheme.goldSoft,
                    fontSize: 13,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ],
            ],
          ),
          const SizedBox(height: 22),
          SizedBox(
            height: 94,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                for (var index = 0; index < 12; index++)
                  Expanded(
                    child: _MonthBar(
                      label: _monthInitials[index],
                      count: stats.huntsByMonth[index],
                      maximum: maximum,
                      highlighted: index == busiestIndex,
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _MonthBar extends StatelessWidget {
  const _MonthBar({
    required this.label,
    required this.count,
    required this.maximum,
    required this.highlighted,
  });

  final String label;
  final int count;
  final int maximum;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    final barHeight = maximum == 0 ? 5.0 : 8 + (count / maximum * 49);
    final color = highlighted ? AppTheme.goldSoft : AppTheme.olive;

    return Semantics(
      label: '$label: $count Jagte',
      child: Column(
        mainAxisAlignment: MainAxisAlignment.end,
        children: [
          if (count > 0)
            Text(
              '$count',
              style: TextStyle(
                color: highlighted ? AppTheme.goldSoft : AppTheme.muted,
                fontSize: 8.5,
                fontWeight: FontWeight.w800,
              ),
            )
          else
            const SizedBox(height: 11),
          const SizedBox(height: 3),
          Container(
            width: 10,
            height: barHeight,
            decoration: BoxDecoration(
              color: color.withValues(alpha: count == 0 ? 0.18 : 0.82),
              borderRadius: BorderRadius.circular(99),
            ),
          ),
          const SizedBox(height: 7),
          Text(
            label,
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 8.5,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

class _HunterProfileCard extends StatelessWidget {
  const _HunterProfileCard({required this.stats});

  final HunterStatistics stats;

  @override
  Widget build(BuildContext context) {
    final antlers = stats.longestAntlers;
    final carcass = stats.heaviestCarcass;
    final items = <_ProfileData>[];
    if (stats.topSpecies case final value?) {
      items.add(
        _ProfileData(
          icon: Icons.pets_rounded,
          label: 'Mees Aangetekende Spesie',
          value: _countedValue(value),
        ),
      );
    }
    if (stats.topLocation case final value?) {
      items.add(
        _ProfileData(
          icon: Icons.place_rounded,
          label: 'Gewildste Jagplek',
          value: _countedValue(value),
        ),
      );
    }
    if (stats.favouriteFirearm case final value?) {
      items.add(
        _ProfileData(
          icon: Icons.adjust_rounded,
          label: 'Mees Gebruikte Vuurwapen',
          value: _countedValue(value),
        ),
      );
    }
    if (antlers?.antlerSizeCm case final value?) {
      items.add(
        _ProfileData(
          icon: Icons.straighten_rounded,
          label: 'Langste Horingmeting',
          value:
              '${formatCentimetresAsInches(value)} duim • ${antlers!.displaySpecies}',
        ),
      );
    }
    if (carcass?.carcassWeightKg case final value?) {
      items.add(
        _ProfileData(
          icon: Icons.scale_rounded,
          label: 'Swaarste Karkas',
          value:
              '${formatMeasurementNumber(value)} kg • ${carcass!.displaySpecies}',
        ),
      );
    }

    return HuntingCard(
      padding: EdgeInsets.zero,
      child: Column(
        children: [
          for (var index = 0; index < items.length; index++) ...[
            if (index > 0) const Divider(),
            _ProfileRow(
              icon: items[index].icon,
              label: items[index].label,
              value: items[index].value,
            ),
          ],
        ],
      ),
    );
  }
}

class _ProfileData {
  const _ProfileData({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;
}

class _ProfileRow extends StatelessWidget {
  const _ProfileRow({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 15, vertical: 14),
      child: Row(
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: AppTheme.gold.withValues(alpha: 0.1),
              borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
            ),
            child: Icon(icon, color: AppTheme.goldSoft, size: 19),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  label,
                  style: const TextStyle(
                    color: AppTheme.muted,
                    fontSize: 10.5,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  value,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontSize: 13.5,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _MilestonesCard extends StatelessWidget {
  const _MilestonesCard({required this.stats});

  final HunterStatistics stats;

  @override
  Widget build(BuildContext context) {
    final milestones = <_MilestoneData>[];
    if (stats.totalTrophies > 0) {
      milestones.add(
        _MilestoneData(
          icon: Icons.emoji_events_rounded,
          value: '${stats.totalTrophies}',
          label: 'Totale Trofeë',
        ),
      );
    }
    if (stats.uniqueSpecies > 0) {
      milestones.add(
        _MilestoneData(
          icon: Icons.category_rounded,
          value: '${stats.uniqueSpecies}',
          label: 'Unieke Spesies',
        ),
      );
    }
    if (stats.uniqueLocations > 0) {
      milestones.add(
        _MilestoneData(
          icon: Icons.landscape_rounded,
          value: '${stats.uniqueLocations}',
          label: 'Jagplekke',
        ),
      );
    }
    if (stats.publicTrophies > 0) {
      milestones.add(
        _MilestoneData(
          icon: Icons.public_rounded,
          value: '${stats.publicTrophies}',
          label: 'Openbare Trofeë',
        ),
      );
    }
    if (stats.rowlandWardQualifiers > 0) {
      milestones.add(
        _MilestoneData(
          icon: Icons.verified_rounded,
          value: '${stats.rowlandWardQualifiers}',
          label: 'Rowland Ward-Kwalifikasies',
        ),
      );
    }
    if (stats.shotsMarked > 0) {
      milestones.add(
        _MilestoneData(
          icon: Icons.gps_fixed_rounded,
          value: '${stats.shotsMarked}',
          label: 'Skote Gemerk',
        ),
      );
    }
    if (stats.animalsFound > 0) {
      milestones.add(
        _MilestoneData(
          icon: Icons.pets_rounded,
          value: '${stats.animalsFound}',
          label: 'Diere Gemerk',
        ),
      );
    }
    if (stats.totalDistanceMeters > 0) {
      milestones.add(
        _MilestoneData(
          icon: Icons.route_rounded,
          value: _formatDistance(stats.totalDistanceMeters, compact: true),
          label: 'Totale Afstand',
        ),
      );
    }
    if (stats.totalFieldTime > Duration.zero) {
      milestones.add(
        _MilestoneData(
          icon: Icons.timer_rounded,
          value: _formatDuration(stats.totalFieldTime),
          label: 'Totale Veldtyd',
        ),
      );
    }

    return HuntingCard(
      padding: const EdgeInsets.all(12),
      child: LayoutBuilder(
        builder: (context, constraints) {
          final itemWidth = constraints.maxWidth >= 560
              ? (constraints.maxWidth - 16) / 3
              : (constraints.maxWidth - 8) / 2;
          return Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final milestone in milestones)
                SizedBox(
                  width: itemWidth,
                  child: _Milestone(data: milestone),
                ),
            ],
          );
        },
      ),
    );
  }
}

class _MilestoneData {
  const _MilestoneData({
    required this.icon,
    required this.value,
    required this.label,
  });

  final IconData icon;
  final String value;
  final String label;
}

class _Milestone extends StatelessWidget {
  const _Milestone({required this.data});

  final _MilestoneData data;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 12),
      decoration: BoxDecoration(
        color: AppTheme.surface.withValues(alpha: 0.75),
        borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
        border: Border.all(color: AppTheme.border.withValues(alpha: 0.72)),
      ),
      child: Row(
        children: [
          const SizedBox(width: 1),
          Icon(data.icon, color: AppTheme.olive, size: 18),
          const SizedBox(width: 9),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  data.value,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontSize: 15,
                    fontWeight: FontWeight.w900,
                  ),
                ),
                Text(
                  data.label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: AppTheme.muted,
                    fontSize: 9.5,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _SectionHeading extends StatelessWidget {
  const _SectionHeading({required this.eyebrow, required this.title});

  final String eyebrow;
  final String title;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Container(
              width: 26,
              height: 2,
              decoration: BoxDecoration(
                color: AppTheme.gold,
                borderRadius: BorderRadius.circular(99),
              ),
            ),
            const SizedBox(width: 8),
            Text(
              eyebrow,
              style: const TextStyle(
                color: AppTheme.gold,
                fontSize: 10,
                fontWeight: FontWeight.w900,
                letterSpacing: 0.9,
              ),
            ),
          ],
        ),
        const SizedBox(height: 7),
        Text(
          title,
          style: const TextStyle(
            color: AppTheme.ink,
            fontSize: 21,
            fontWeight: FontWeight.w900,
            letterSpacing: -0.2,
          ),
        ),
      ],
    );
  }
}

/// What a non-hunter role can do in the app, shown at the top of home.
class _RoleWelcomeCard extends StatelessWidget {
  const _RoleWelcomeCard({required this.session});

  final AppSession session;

  @override
  Widget build(BuildContext context) {
    final (IconData icon, String title, String message) = session.isPh
        ? (
            Icons.verified_user_outlined,
            'Professionele Jagter',
            'Jou toegewysde jagte is onder Besprekings. Keur trofeë wat jagters '
                'indien onder Goedkeurings goed, en kies die roete vir die '
                'gedeelde jagvideo.',
          )
        : session.isVg
        ? (
            Icons.videocam_outlined,
            'Videograaf',
            'Laai jou foto’s en kort videogrepe onder Besprekings op sodra die '
                'jag verby is. Dit word deel van die jagstorie wat die span deel.',
          )
        : (
            Icons.public_rounded,
            'Webwerf-rol',
            'Jou rol (${session.roleCode.isEmpty ? 'onbekend' : session.roleCode}) '
                'word op die MikPunt-webwerf bestuur. Hier kan jy klets en jou '
                'profiel bywerk; jagfunksies is vir jagters, PH’s en videograwe.',
          );
    return HuntingCard(
      padding: const EdgeInsets.all(18),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: AppTheme.goldSoft, size: 28),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontSize: 16,
                    fontWeight: FontWeight.w900,
                  ),
                ),
                const SizedBox(height: 6),
                Text(
                  message,
                  style: const TextStyle(
                    color: AppTheme.muted,
                    fontSize: 12.5,
                    height: 1.45,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _LoadingDashboard extends StatelessWidget {
  const _LoadingDashboard();

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      padding: const EdgeInsets.symmetric(vertical: 52, horizontal: 24),
      child: Column(
        children: [
          const SizedBox(
            width: 30,
            height: 30,
            child: CircularProgressIndicator(strokeWidth: 2.4),
          ),
          const SizedBox(height: 16),
          const Text(
            'Jou jagstatistiek word saamgestel',
            textAlign: TextAlign.center,
            style: TextStyle(
              color: AppTheme.ink,
              fontSize: 16,
              fontWeight: FontWeight.w900,
            ),
          ),
          const SizedBox(height: 6),
          const Text(
            'Trofeë en voltooide kaartroetes word ingelees.',
            textAlign: TextAlign.center,
            style: TextStyle(color: AppTheme.muted, fontSize: 12),
          ),
        ],
      ),
    );
  }
}

class _DashboardError extends StatelessWidget {
  const _DashboardError({required this.onRetry});

  final Future<void> Function() onRetry;

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      padding: const EdgeInsets.symmetric(vertical: 38, horizontal: 24),
      child: Column(
        children: [
          Container(
            width: 58,
            height: 58,
            decoration: BoxDecoration(
              color: AppTheme.danger.withValues(alpha: 0.12),
              shape: BoxShape.circle,
            ),
            child: const Icon(
              Icons.cloud_off_rounded,
              color: AppTheme.dangerSoft,
              size: 28,
            ),
          ),
          const SizedBox(height: 15),
          const Text(
            'Statistiek is nie beskikbaar nie',
            textAlign: TextAlign.center,
            style: TextStyle(
              color: AppTheme.ink,
              fontSize: 17,
              fontWeight: FontWeight.w900,
            ),
          ),
          const SizedBox(height: 7),
          const Text(
            'Ons kon nie jou trofeë of kaartroetes lees nie.',
            textAlign: TextAlign.center,
            style: TextStyle(color: AppTheme.muted, fontSize: 12.5),
          ),
          const SizedBox(height: 18),
          OutlinedButton.icon(
            onPressed: onRetry,
            icon: const Icon(Icons.refresh_rounded, size: 18),
            label: const Text('Probeer weer'),
          ),
        ],
      ),
    );
  }
}

class _PartialDataNotice extends StatelessWidget {
  const _PartialDataNotice({required this.trophyDataUnavailable});

  final bool trophyDataUnavailable;

  @override
  Widget build(BuildContext context) {
    final missing = trophyDataUnavailable ? 'trofeë' : 'kaartroetes';
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 11),
      decoration: BoxDecoration(
        color: AppTheme.gold.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
        border: Border.all(color: AppTheme.gold.withValues(alpha: 0.22)),
      ),
      child: Row(
        children: [
          const Icon(
            Icons.info_outline_rounded,
            color: AppTheme.goldSoft,
            size: 19,
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              'Die $missing kon nie nou laai nie; beskikbare statistiek word steeds gewys.',
              style: const TextStyle(
                color: AppTheme.muted,
                fontSize: 11.5,
                height: 1.35,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ContourPainter extends CustomPainter {
  const _ContourPainter();

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = AppTheme.goldSoft.withValues(alpha: 0.07)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;
    final center = Offset(size.width * 0.86, size.height * 0.42);

    for (var index = 0; index < 6; index++) {
      canvas.drawOval(
        Rect.fromCenter(
          center: center,
          width: 76 + (index * 40),
          height: 50 + (index * 29),
        ),
        paint,
      );
    }

    final path = Path()
      ..moveTo(-20, size.height * 0.25)
      ..cubicTo(
        size.width * 0.18,
        size.height * 0.06,
        size.width * 0.34,
        size.height * 0.42,
        size.width * 0.55,
        size.height * 0.22,
      )
      ..cubicTo(
        size.width * 0.72,
        size.height * 0.08,
        size.width * 0.82,
        size.height * 0.2,
        size.width + 20,
        size.height * 0.06,
      );
    canvas.drawPath(path, paint);
  }

  @override
  bool shouldRepaint(covariant _ContourPainter oldDelegate) => false;
}

String _formatDistance(double metres, {bool compact = false}) {
  if (!metres.isFinite || metres <= 0) return '0 km';
  if (metres < 1000 && !compact) return '${metres.round()} m';
  final kilometres = metres / 1000;
  final decimals = kilometres >= 100 ? 0 : 1;
  return '${kilometres.toStringAsFixed(decimals).replaceAll('.', ',')} km';
}

String _formatDuration(Duration duration) {
  if (duration <= Duration.zero) return '0m';
  final hours = duration.inHours;
  final minutes = duration.inMinutes.remainder(60);
  if (hours == 0) return '${duration.inMinutes}m';
  if (minutes == 0) return '${hours}u';
  return '${hours}u ${minutes}m';
}

String _formatDate(DateTime date) {
  return '${date.day} ${_monthName(date.month).substring(0, 3)} ${date.year}';
}

String _monthName(int month) {
  const months = <String>[
    'Januarie',
    'Februarie',
    'Maart',
    'April',
    'Mei',
    'Junie',
    'Julie',
    'Augustus',
    'September',
    'Oktober',
    'November',
    'Desember',
  ];
  return months[month - 1];
}

String _trophyContext(Trophy trophy) {
  final location = trophy.locationText.trim();
  return location.isEmpty
      ? trophy.displaySpecies
      : '${trophy.displaySpecies} • $location';
}

String _formatTrophyHighlight(TrophyHighlight highlight) {
  switch (highlight.basis) {
    case TrophyHighlightBasis.rowlandWard:
      return '${highlight.value.toStringAsFixed(0)}% Van Wêreldrekord';
    case TrophyHighlightBasis.antlerSize:
      return '${formatCentimetresAsInches(highlight.value)} duim Horings';
    case TrophyHighlightBasis.carcassWeight:
      return '${formatMeasurementNumber(highlight.value)} kg Karkas';
    case TrophyHighlightBasis.species:
      final count = highlight.value.round();
      return '$count ${count == 1 ? 'Trofee' : 'Trofeë'} Aangeteken';
  }
}

String _countedValue(CountedLabel value) {
  return '${value.label} • ${value.count}×';
}
