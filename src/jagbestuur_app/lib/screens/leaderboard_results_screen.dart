import 'package:flutter/material.dart';

import '../config/app_config.dart';
import '../services/public_trophy_service.dart';
import '../theme/app_theme.dart';
import '../utils/measurement_format.dart';
import '../widgets/hunting_card.dart';
import '../widgets/primary_button.dart';
import '../widgets/rowland_ward_trophy_summary.dart';
import '../widgets/trophy_shot_distance.dart';
import 'public_trophy_detail_screen.dart';

class LeaderboardResultsScreen extends StatefulWidget {
  const LeaderboardResultsScreen({
    super.key,
    required this.query,
    this.publicTrophyApi,
  });

  final LeaderboardQuery query;
  final PublicTrophyApi? publicTrophyApi;

  @override
  State<LeaderboardResultsScreen> createState() =>
      _LeaderboardResultsScreenState();
}

class _LeaderboardResultsScreenState extends State<LeaderboardResultsScreen> {
  late final PublicTrophyApi _publicTrophyService;
  late Future<List<LeaderboardTrophy>> _leaderboardFuture;

  @override
  void initState() {
    super.initState();
    _publicTrophyService = widget.publicTrophyApi ?? PublicTrophyService();
    _leaderboardFuture = _loadLeaderboard();
  }

  Future<List<LeaderboardTrophy>> _loadLeaderboard() {
    return _publicTrophyService.getLeaderboardTrophies(widget.query);
  }

  void _retry() {
    setState(() {
      _leaderboardFuture = _loadLeaderboard();
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Trofeeranglys'),
        actions: [
          IconButton(
            tooltip: 'Verander filters',
            icon: const Icon(Icons.tune_rounded),
            onPressed: () => Navigator.of(context).pop(),
          ),
        ],
      ),
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: FutureBuilder<List<LeaderboardTrophy>>(
            future: _leaderboardFuture,
            builder: (context, snapshot) {
              final trophies = snapshot.data ?? const <LeaderboardTrophy>[];

              return ListView(
                padding: const EdgeInsets.fromLTRB(16, 14, 16, 36),
                children: [
                  _LeaderboardHeader(
                    resultCount: snapshot.hasData ? trophies.length : null,
                  ),
                  const SizedBox(height: 18),
                  _AppliedFiltersCard(
                    category: widget.query.categoryLabel,
                    species: widget.query.speciesLabel,
                    firearmType:
                        widget.query.firearmType ?? 'Alle vuurwapentipes',
                  ),
                  const SizedBox(height: 18),
                  ..._buildResults(snapshot),
                  const SizedBox(height: 8),
                  TextButton.icon(
                    onPressed: () => Navigator.of(context).pop(),
                    icon: const Icon(Icons.tune_rounded, size: 18),
                    label: const Text('Verander filters'),
                  ),
                ],
              );
            },
          ),
        ),
      ),
    );
  }

  List<Widget> _buildResults(AsyncSnapshot<List<LeaderboardTrophy>> snapshot) {
    if (snapshot.connectionState == ConnectionState.waiting) {
      return const [
        HuntingCard(
          child: _LeaderboardStateCard(
            icon: Icons.hourglass_top_rounded,
            title: 'Trofeeranglys word gelaai',
            message: "Wag net 'n oomblik terwyl die resultate laai.",
            showProgress: true,
          ),
        ),
      ];
    }

    if (snapshot.hasError) {
      return [
        HuntingCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const _LeaderboardStateCard(
                icon: Icons.cloud_off_rounded,
                title: 'Kon nie laai nie',
                message: 'Kon nie die trofeeranglys laai nie.',
                accentColor: AppTheme.dangerSoft,
              ),
              const SizedBox(height: 18),
              PrimaryButton(
                text: 'Probeer weer',
                icon: Icons.refresh_rounded,
                secondary: true,
                onPressed: _retry,
              ),
            ],
          ),
        ),
      ];
    }

    final trophies = snapshot.data ?? const <LeaderboardTrophy>[];
    if (trophies.isEmpty) {
      return const [
        HuntingCard(
          child: _LeaderboardStateCard(
            icon: Icons.emoji_events_outlined,
            title: 'Geen resultate nie',
            message: 'Geen openbare trofeë voldoen aan die gekose filters nie.',
          ),
        ),
      ];
    }

    final groups = buildLeaderboardGroups(trophies);
    final truncated = trophies.length >= PublicTrophyService.leaderboardMaxRows;
    return [
      if (truncated) ...[
        const HuntingCard(
          child: _LeaderboardStateCard(
            icon: Icons.filter_alt_outlined,
            title: 'Net die eerste resultate word gewys',
            message:
                'Daar is baie trofeë. Verfyn die filters om al die resultate '
                'te sien.',
          ),
        ),
        const SizedBox(height: 12),
      ],
      for (final group in groups) ...[
        _LeaderboardGroupHeader(group: group),
        const SizedBox(height: 10),
        for (final ranked in rankGroup(group)) ...[
          _LeaderboardTrophyCard(
            trophy: ranked.trophy,
            rank: ranked.rank,
            measurementKind: group.measurementKind,
            onTap: () => _openTrophyDetails(ranked.trophy.id),
          ),
          const SizedBox(height: 12),
        ],
        const SizedBox(height: 6),
      ],
    ];
  }

  void _openTrophyDetails(int trophyId) {
    Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => PublicTrophyDetailScreen(trophyId: trophyId),
      ),
    );
  }
}

class _LeaderboardHeader extends StatelessWidget {
  const _LeaderboardHeader({required this.resultCount});

  final int? resultCount;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          width: 50,
          height: 50,
          decoration: BoxDecoration(
            color: AppTheme.gold.withValues(alpha: 0.13),
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
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'TROFEERANGLYS',
                style: TextStyle(
                  color: AppTheme.gold,
                  fontSize: 9,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 0.9,
                ),
              ),
              const SizedBox(height: 4),
              const Text(
                'Openbare trofeë',
                style: TextStyle(
                  color: AppTheme.ink,
                  fontSize: 23,
                  fontWeight: FontWeight.w900,
                  height: 1.1,
                ),
              ),
              if (resultCount != null) ...[
                const SizedBox(height: 5),
                Text(
                  '$resultCount ${resultCount == 1 ? 'trofee' : 'trofeë'} in hierdie ranglys',
                  style: const TextStyle(
                    color: AppTheme.muted,
                    fontSize: 12,
                    height: 1.3,
                  ),
                ),
              ],
            ],
          ),
        ),
      ],
    );
  }
}

class _AppliedFiltersCard extends StatelessWidget {
  const _AppliedFiltersCard({
    required this.category,
    required this.species,
    required this.firearmType,
  });

  final String category;
  final String species;
  final String firearmType;

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      padding: const EdgeInsets.all(15),
      gradient: const LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [Color(0xFF2D271F), Color(0xFF201C17)],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(
            children: [
              Icon(Icons.tune_rounded, color: AppTheme.goldSoft, size: 18),
              SizedBox(width: 8),
              Text(
                'TOEGEPASTE FILTERS',
                style: TextStyle(
                  color: AppTheme.gold,
                  fontSize: 10,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 0.8,
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              _AppliedFilterChip(
                icon: Icons.category_outlined,
                label: category,
              ),
              _AppliedFilterChip(icon: Icons.pets_outlined, label: species),
              _AppliedFilterChip(
                icon: Icons.gps_fixed_rounded,
                label: firearmType,
              ),
            ],
          ),
          const SizedBox(height: 12),
          Divider(color: AppTheme.border.withValues(alpha: 0.75)),
          const SizedBox(height: 10),
          Row(
            children: [
              const Icon(Icons.sort_rounded, color: AppTheme.muted, size: 17),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  'Elke spesie word outomaties volgens sy toepaslike meting gerangskik.',
                  style: const TextStyle(
                    color: AppTheme.muted,
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _AppliedFilterChip extends StatelessWidget {
  const _AppliedFilterChip({required this.icon, required this.label});

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      constraints: const BoxConstraints(maxWidth: 260),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: AppTheme.olive.withValues(alpha: 0.18),
        borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
        border: Border.all(color: AppTheme.olive.withValues(alpha: 0.4)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, color: AppTheme.goldSoft, size: 15),
          const SizedBox(width: 6),
          Flexible(
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                color: AppTheme.ink,
                fontSize: 11.5,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class LeaderboardGroup {
  const LeaderboardGroup({
    required this.displayName,
    required this.trophies,
    required this.measurementKind,
  });

  final String displayName;
  final List<LeaderboardTrophy> trophies;
  final LeaderboardMeasurementKind measurementKind;

  String get measurementDescription {
    switch (measurementKind) {
      case LeaderboardMeasurementKind.rowlandWard:
        String? unitLabel;
        for (final trophy in trophies) {
          unitLabel = trophy.rowlandWard?.score?.unitLabelAf;
          if (unitLabel != null && unitLabel.isNotEmpty) {
            break;
          }
        }
        return unitLabel == null || unitLabel.isEmpty
            ? 'Volgens Rowland Ward-telling'
            : 'Volgens Rowland Ward-telling ($unitLabel)';
      case LeaderboardMeasurementKind.antlerSize:
        return 'Volgens horinglengte (duim)';
      case LeaderboardMeasurementKind.carcassWeight:
        return 'Volgens karkasgewig (kg)';
      case LeaderboardMeasurementKind.none:
        return 'Geen ranglysmeting aangeteken nie';
    }
  }
}

/// A trophy with its position in its group; [rank] is null when the trophy
/// has no value for the group's measurement and so isn't ranked.
typedef RankedTrophy = ({LeaderboardTrophy trophy, int? rank});

/// Numbers only trophies that have the group's measurement, in server order
/// (best first, unmeasured last). Ties share a rank.
List<RankedTrophy> rankGroup(LeaderboardGroup group) {
  final result = <RankedTrophy>[];
  var position = 0;
  int? previousRank;
  Object? previousValue;

  for (final trophy in group.trophies) {
    final value =
        group.measurementKind == LeaderboardMeasurementKind.rowlandWard
        ? trophy.selectedSortValue ?? trophy.rowlandWard?.score
        : trophy.selectedSortValue;
    if (value == null ||
        group.measurementKind == LeaderboardMeasurementKind.none) {
      result.add((trophy: trophy, rank: null));
      continue;
    }

    position++;
    final rank = value == previousValue ? previousRank! : position;
    previousRank = rank;
    previousValue = value;
    result.add((trophy: trophy, rank: rank));
  }
  return result;
}

List<LeaderboardGroup> buildLeaderboardGroups(
  List<LeaderboardTrophy> trophies,
) {
  final grouped = <String, List<LeaderboardTrophy>>{};
  for (final trophy in trophies) {
    grouped.putIfAbsent(trophy.leaderboardGroupKey, () => []).add(trophy);
  }

  return grouped.values.map((groupTrophies) {
    final hasRowlandWardClass = groupTrophies.any(
      (trophy) => trophy.rowlandWard?.isValidLinkedClass == true,
    );
    final measurementKind = hasRowlandWardClass
        ? (groupTrophies.any((trophy) => trophy.rowlandWard?.score != null)
              ? LeaderboardMeasurementKind.rowlandWard
              : LeaderboardMeasurementKind.none)
        : (groupTrophies.any((trophy) => trophy.antlerSizeCm != null)
              ? LeaderboardMeasurementKind.antlerSize
              : groupTrophies.any((trophy) => trophy.carcassWeightKg != null)
              ? LeaderboardMeasurementKind.carcassWeight
              : LeaderboardMeasurementKind.none);

    return LeaderboardGroup(
      displayName: groupTrophies.first.displaySpecies,
      trophies: groupTrophies,
      measurementKind: measurementKind,
    );
  }).toList();
}

class _LeaderboardGroupHeader extends StatelessWidget {
  const _LeaderboardGroupHeader({required this.group});

  final LeaderboardGroup group;

  @override
  Widget build(BuildContext context) {
    final trophyCount = group.trophies.length;
    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 4, 4, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            group.displayName,
            style: const TextStyle(
              color: AppTheme.ink,
              fontSize: 19,
              fontWeight: FontWeight.w900,
            ),
          ),
          const SizedBox(height: 3),
          Text(
            '$trophyCount ${trophyCount == 1 ? 'trofee' : 'trofeë'} · '
            '${group.measurementDescription}',
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 11.5,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

class _LeaderboardTrophyCard extends StatelessWidget {
  const _LeaderboardTrophyCard({
    required this.trophy,
    required this.rank,
    required this.measurementKind,
    required this.onTap,
  });

  final LeaderboardTrophy trophy;

  /// Null when the trophy has no value for the group's measurement.
  final int? rank;
  final LeaderboardMeasurementKind measurementKind;
  final VoidCallback onTap;

  bool get _hasNetworkPhoto {
    final uri = Uri.tryParse(_photoUrl);
    return uri != null && (uri.scheme == 'http' || uri.scheme == 'https');
  }

  String get _photoUrl => AppConfig.resolveApiUrl(trophy.photoPath);

  String get _measurementLabel {
    switch (measurementKind) {
      case LeaderboardMeasurementKind.rowlandWard:
        return 'ROWLAND WARD-TELLING';
      case LeaderboardMeasurementKind.antlerSize:
        return 'HORINGLENGTE';
      case LeaderboardMeasurementKind.carcassWeight:
        return 'KARKASGEWIG';
      case LeaderboardMeasurementKind.none:
        return 'RANGLYSMETING';
    }
  }

  String get _measurementValue {
    switch (measurementKind) {
      case LeaderboardMeasurementKind.rowlandWard:
        return trophy.rowlandWard?.score?.formattedWithUnit ??
            'Nie aangeteken nie';
      case LeaderboardMeasurementKind.antlerSize:
        final selectedValue = trophy.selectedSortValue;
        return selectedValue == null
            ? 'Nie aangeteken nie'
            : '${formatCentimetresAsInches(selectedValue)} duim';
      case LeaderboardMeasurementKind.carcassWeight:
        final selectedValue = trophy.selectedSortValue;
        return selectedValue == null
            ? 'Nie aangeteken nie'
            : '${formatMeasurementNumber(selectedValue)} kg';
      case LeaderboardMeasurementKind.none:
        return 'Nie aangeteken nie';
    }
  }

  bool get _hasRankingValue =>
      measurementKind == LeaderboardMeasurementKind.rowlandWard
      ? trophy.rowlandWard?.score != null
      : trophy.selectedSortValue != null;

  Color get _rankAccent {
    switch (rank) {
      case 1:
        return AppTheme.goldSoft;
      case 2:
        return const Color(0xFFC0C5C7);
      case 3:
        return const Color(0xFFC58B68);
      default:
        return AppTheme.muted;
    }
  }

  Color get _measurementAccent =>
      (rank ?? 99) <= 3 ? _rankAccent : AppTheme.goldSoft;

  @override
  Widget build(BuildContext context) {
    final hasOwner = trophy.ownerDisplayName?.trim().isNotEmpty ?? false;
    final hasFirearm = trophy.firearmType.trim().isNotEmpty;
    final hasShotDistance = trophy.shotDistanceMeters != null;
    final hasRowlandWardSummary = trophy.rowlandWard?.hasDisplaySummary == true;
    final cardHeight =
        (hasShotDistance ? 158.0 : 136.0) + (hasRowlandWardSummary ? 94.0 : 0);

    return HuntingCard(
      padding: const EdgeInsets.all(10),
      onTap: onTap,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _LeaderboardPhoto(
            hasNetworkPhoto: _hasNetworkPhoto,
            photoPath: _photoUrl,
            rank: rank,
            rankAccent: _rankAccent,
            height: cardHeight,
          ),
          const SizedBox(width: 13),
          Expanded(
            child: SizedBox(
              height: cardHeight,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    trophy.displaySpecies,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: AppTheme.ink,
                      fontSize: 18,
                      fontWeight: FontWeight.w900,
                      height: 1.13,
                    ),
                  ),
                  const SizedBox(height: 10),
                  Text(
                    _measurementLabel,
                    style: const TextStyle(
                      color: AppTheme.muted,
                      fontSize: 8.5,
                      fontWeight: FontWeight.w900,
                      letterSpacing: 0.7,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    _measurementValue,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: !_hasRankingValue
                          ? AppTheme.disabledText
                          : _measurementAccent,
                      fontSize: !_hasRankingValue ? 15 : 23,
                      fontWeight: FontWeight.w900,
                      height: 1.1,
                    ),
                  ),
                  if (hasRowlandWardSummary) ...[
                    const SizedBox(height: 8),
                    RowlandWardTrophySummary(
                      score: trophy.rowlandWard?.score,
                      worldRecord: trophy.rowlandWard?.worldRecord,
                      minimumReached:
                          trophy.rowlandWard?.minimumReached == true,
                      showScore: false,
                    ),
                  ],
                  const Spacer(),
                  if (hasShotDistance)
                    TrophyShotDistance(
                      meters: trophy.shotDistanceMeters,
                      color: AppTheme.muted,
                      fontSize: 10,
                    ),
                  if (hasShotDistance && (hasOwner || hasFirearm))
                    const SizedBox(height: 5),
                  if (hasOwner)
                    _MetadataLine(
                      icon: Icons.person_outline_rounded,
                      value: trophy.ownerDisplayName!.trim(),
                    ),
                  if (hasOwner && hasFirearm) const SizedBox(height: 5),
                  if (hasFirearm)
                    _MetadataLine(
                      icon: Icons.gps_fixed_rounded,
                      value: trophy.firearmType,
                    ),
                ],
              ),
            ),
          ),
          const SizedBox(width: 2),
          const Padding(
            padding: EdgeInsets.only(top: 4),
            child: Icon(
              Icons.chevron_right_rounded,
              color: AppTheme.muted,
              size: 22,
            ),
          ),
        ],
      ),
    );
  }
}

class _LeaderboardPhoto extends StatelessWidget {
  const _LeaderboardPhoto({
    required this.hasNetworkPhoto,
    required this.photoPath,
    required this.rank,
    required this.rankAccent,
    required this.height,
  });

  final bool hasNetworkPhoto;
  final String photoPath;

  /// Null when the trophy has no value for the group's measurement.
  final int? rank;
  final Color rankAccent;
  final double height;

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
      child: SizedBox(
        width: 104,
        height: height,
        child: Stack(
          fit: StackFit.expand,
          children: [
            hasNetworkPhoto
                ? Image.network(
                    photoPath,
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
            const DecoratedBox(
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [Color(0x40000000), Colors.transparent],
                ),
              ),
            ),
            Positioned(
              top: 8,
              left: 8,
              child: _RankBadge(rank: rank, accent: rankAccent),
            ),
          ],
        ),
      ),
    );
  }
}

class _RankBadge extends StatelessWidget {
  const _RankBadge({required this.rank, required this.accent});

  /// Null when the trophy has no value for the group's measurement.
  final int? rank;
  final Color accent;

  @override
  Widget build(BuildContext context) {
    return Container(
      constraints: const BoxConstraints(minWidth: 34),
      height: 30,
      padding: const EdgeInsets.symmetric(horizontal: 7),
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: AppTheme.background.withValues(alpha: 0.85),
        borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
        border: Border.all(color: accent.withValues(alpha: 0.72)),
      ),
      child: Text(
        rank == null ? '–' : '#$rank',
        style: TextStyle(
          color: accent,
          fontSize: 11,
          fontWeight: FontWeight.w900,
        ),
      ),
    );
  }
}

class _MetadataLine extends StatelessWidget {
  const _MetadataLine({required this.icon, required this.value});

  final IconData icon;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Icon(icon, color: AppTheme.muted, size: 14),
        const SizedBox(width: 6),
        Expanded(
          child: Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              color: AppTheme.muted,
              fontSize: 11.5,
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
      ],
    );
  }
}

class _PhotoLoading extends StatelessWidget {
  const _PhotoLoading();

  @override
  Widget build(BuildContext context) {
    return const ColoredBox(
      color: AppTheme.surface,
      child: Center(
        child: SizedBox(
          width: 24,
          height: 24,
          child: CircularProgressIndicator(strokeWidth: 2.2),
        ),
      ),
    );
  }
}

class _PhotoUnavailable extends StatelessWidget {
  const _PhotoUnavailable();

  @override
  Widget build(BuildContext context) {
    return Container(
      color: AppTheme.surface,
      alignment: Alignment.center,
      child: const Icon(
        Icons.emoji_events_outlined,
        color: AppTheme.gold,
        size: 32,
      ),
    );
  }
}

class _LeaderboardStateCard extends StatelessWidget {
  const _LeaderboardStateCard({
    required this.icon,
    required this.title,
    required this.message,
    this.accentColor = AppTheme.goldSoft,
    this.showProgress = false,
  });

  final IconData icon;
  final String title;
  final String message;
  final Color accentColor;
  final bool showProgress;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          width: 48,
          height: 48,
          decoration: BoxDecoration(
            color: accentColor.withValues(alpha: 0.12),
            borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
          ),
          child: showProgress
              ? Padding(
                  padding: const EdgeInsets.all(13),
                  child: CircularProgressIndicator(
                    color: accentColor,
                    strokeWidth: 2.3,
                  ),
                )
              : Icon(icon, color: accentColor, size: 25),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: const TextStyle(
                  color: AppTheme.ink,
                  fontSize: 18,
                  fontWeight: FontWeight.w900,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                message,
                style: const TextStyle(
                  color: AppTheme.muted,
                  fontWeight: FontWeight.w700,
                  height: 1.4,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}
