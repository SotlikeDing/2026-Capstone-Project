import 'package:flutter/material.dart';

import '../config/app_config.dart';
import '../services/public_trophy_service.dart';
import '../theme/app_theme.dart';
import '../utils/measurement_format.dart';
import '../widgets/hunting_card.dart';
import '../widgets/primary_button.dart';
import '../widgets/rowland_ward_trophy_summary.dart';

class PublicTrophyDetailScreen extends StatefulWidget {
  const PublicTrophyDetailScreen({super.key, required this.trophyId});

  final int trophyId;

  @override
  State<PublicTrophyDetailScreen> createState() =>
      _PublicTrophyDetailScreenState();
}

class _PublicTrophyDetailScreenState extends State<PublicTrophyDetailScreen> {
  final PublicTrophyService _publicTrophyService = PublicTrophyService();
  late Future<PublicTrophyDetail> _detailFuture;

  @override
  void initState() {
    super.initState();
    _detailFuture = _loadTrophyDetail();
  }

  Future<PublicTrophyDetail> _loadTrophyDetail() {
    return _publicTrophyService.getPublicTrophyDetail(widget.trophyId);
  }

  void _retry() {
    setState(() {
      _detailFuture = _loadTrophyDetail();
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Trofeebesonderhede')),
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: FutureBuilder<PublicTrophyDetail>(
            future: _detailFuture,
            builder: (context, snapshot) {
              if (snapshot.connectionState == ConnectionState.waiting) {
                return const Padding(
                  padding: EdgeInsets.all(16),
                  child: HuntingCard(
                    child: _DetailStateCard(
                      icon: Icons.hourglass_top_rounded,
                      title: 'Trofeebesonderhede word gelaai',
                      message: "Wag net 'n oomblik terwyl die trofee laai.",
                      showProgress: true,
                    ),
                  ),
                );
              }

              if (snapshot.hasError) {
                return Padding(
                  padding: const EdgeInsets.all(16),
                  child: HuntingCard(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const _DetailStateCard(
                          icon: Icons.lock_outline_rounded,
                          title: 'Trofee nie beskikbaar nie',
                          message:
                              'Hierdie trofee is nie beskikbaar vir openbare besigtiging nie.',
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
                );
              }

              return _TrophyDetailContent(trophy: snapshot.data!);
            },
          ),
        ),
      ),
    );
  }
}

class _TrophyDetailContent extends StatelessWidget {
  const _TrophyDetailContent({required this.trophy});

  final PublicTrophyDetail trophy;

  bool get _hasNetworkPhoto {
    final uri = Uri.tryParse(_photoUrl);
    return uri != null && (uri.scheme == 'http' || uri.scheme == 'https');
  }

  String get _photoUrl => AppConfig.resolveApiUrl(trophy.photoPath);

  bool get _hasMeasurements =>
      trophy.antlerSizeCm != null || trophy.carcassWeightKg != null;

  String get _harvestDate {
    final day = trophy.harvestDate.day.toString().padLeft(2, '0');
    final month = trophy.harvestDate.month.toString().padLeft(2, '0');
    return '$day/$month/${trophy.harvestDate.year}';
  }

  @override
  Widget build(BuildContext context) {
    final hasOwner = trophy.ownerDisplayName?.trim().isNotEmpty ?? false;
    final hasFirearm = trophy.firearmType.trim().isNotEmpty;
    final hasShotDistance = trophy.shotDistanceMeters != null;
    final hasNotes = trophy.notes?.trim().isNotEmpty ?? false;

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 36),
      children: [
        _TrophyPhoto(photoPath: _photoUrl, hasNetworkPhoto: _hasNetworkPhoto),
        const SizedBox(height: 18),
        const Text(
          'OPENBARE TROFEE',
          style: TextStyle(
            color: AppTheme.gold,
            fontSize: 10,
            fontWeight: FontWeight.w900,
            letterSpacing: 1,
          ),
        ),
        const SizedBox(height: 5),
        Text(
          trophy.displaySpecies,
          style: const TextStyle(
            color: AppTheme.ink,
            fontSize: 28,
            fontWeight: FontWeight.w900,
            letterSpacing: -0.35,
            height: 1.08,
          ),
        ),
        if (hasOwner) ...[
          const SizedBox(height: 10),
          Row(
            children: [
              const Icon(
                Icons.person_outline_rounded,
                color: AppTheme.goldSoft,
                size: 17,
              ),
              const SizedBox(width: 7),
              Expanded(
                child: Text(
                  trophy.ownerDisplayName!.trim(),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: AppTheme.muted,
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ),
        ],
        if (trophy.rowlandWard?.hasDisplaySummary == true) ...[
          const SizedBox(height: 24),
          const _SectionLabel(
            icon: Icons.workspace_premium_rounded,
            label: 'ROWLAND WARD',
          ),
          const SizedBox(height: 9),
          HuntingCard(
            padding: const EdgeInsets.all(16),
            child: RowlandWardTrophySummary(
              score: trophy.rowlandWard?.score,
              worldRecord: trophy.rowlandWard?.worldRecord,
              minimumReached: trophy.rowlandWard?.minimumReached == true,
            ),
          ),
        ],
        if (_hasMeasurements) ...[
          const SizedBox(height: 24),
          const _SectionLabel(icon: Icons.straighten_rounded, label: 'METINGS'),
          const SizedBox(height: 9),
          _MeasurementsCard(trophy: trophy),
        ],
        const SizedBox(height: 24),
        const _SectionLabel(
          icon: Icons.landscape_outlined,
          label: 'JAGBESONDERHEDE',
        ),
        const SizedBox(height: 9),
        HuntingCard(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 7),
          child: Column(
            children: [
              if (hasFirearm) ...[
                _DetailInfoRow(
                  icon: Icons.gps_fixed_rounded,
                  label: 'Vuurwapentipe',
                  value: trophy.firearmType,
                ),
                const Divider(),
              ],
              if (hasShotDistance) ...[
                _DetailInfoRow(
                  icon: Icons.social_distance_rounded,
                  label: 'Skootafstand',
                  value: '${trophy.shotDistanceMeters} m',
                ),
                const Divider(),
              ],
              _DetailInfoRow(
                icon: Icons.calendar_month_outlined,
                label: 'Jagdatum',
                value: _harvestDate,
              ),
            ],
          ),
        ),
        if (hasNotes) ...[
          const SizedBox(height: 24),
          const _SectionLabel(icon: Icons.notes_rounded, label: 'NOTAS'),
          const SizedBox(height: 9),
          HuntingCard(
            padding: const EdgeInsets.all(17),
            child: Text(
              trophy.notes!.trim(),
              style: const TextStyle(
                color: AppTheme.ink,
                fontSize: 14,
                height: 1.5,
              ),
            ),
          ),
        ],
      ],
    );
  }
}

class _TrophyPhoto extends StatelessWidget {
  const _TrophyPhoto({required this.photoPath, required this.hasNetworkPhoto});

  final String photoPath;
  final bool hasNetworkPhoto;

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(AppTheme.radiusExtraLarge),
      child: AspectRatio(
        aspectRatio: 4 / 3,
        child: hasNetworkPhoto
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
      ),
    );
  }
}

class _MeasurementsCard extends StatelessWidget {
  const _MeasurementsCard({required this.trophy});

  final PublicTrophyDetail trophy;

  @override
  Widget build(BuildContext context) {
    final hasAntlerSize = trophy.antlerSizeCm != null;
    final hasCarcassWeight = trophy.carcassWeightKg != null;

    return HuntingCard(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 17),
      gradient: const LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [Color(0xFF30281E), Color(0xFF231E19)],
      ),
      child: Row(
        children: [
          if (hasAntlerSize)
            Expanded(
              child: _MeasurementValue(
                label: 'HORINGLENGTE',
                value:
                    '${formatCentimetresAsInches(trophy.antlerSizeCm!)} duim',
              ),
            ),
          if (hasAntlerSize && hasCarcassWeight)
            Container(
              width: 1,
              height: 52,
              margin: const EdgeInsets.symmetric(horizontal: 14),
              color: AppTheme.border,
            ),
          if (hasCarcassWeight)
            Expanded(
              child: _MeasurementValue(
                label: 'KARKASGEWIG',
                value: '${formatMeasurementNumber(trophy.carcassWeightKg!)} kg',
              ),
            ),
        ],
      ),
    );
  }
}

class _MeasurementValue extends StatelessWidget {
  const _MeasurementValue({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            color: AppTheme.muted,
            fontSize: 8.5,
            fontWeight: FontWeight.w900,
            letterSpacing: 0.7,
          ),
        ),
        const SizedBox(height: 5),
        Text(
          value,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            color: AppTheme.goldSoft,
            fontSize: 20,
            fontWeight: FontWeight.w900,
          ),
        ),
      ],
    );
  }
}

class _SectionLabel extends StatelessWidget {
  const _SectionLabel({required this.icon, required this.label});

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Icon(icon, color: AppTheme.goldSoft, size: 17),
        const SizedBox(width: 8),
        Text(
          label,
          style: const TextStyle(
            color: AppTheme.gold,
            fontSize: 10,
            fontWeight: FontWeight.w900,
            letterSpacing: 0.9,
          ),
        ),
      ],
    );
  }
}

class _DetailInfoRow extends StatelessWidget {
  const _DetailInfoRow({
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
      padding: const EdgeInsets.symmetric(vertical: 11),
      child: Row(
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: AppTheme.olive.withValues(alpha: 0.16),
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
                    fontSize: 10,
                    fontWeight: FontWeight.w800,
                    letterSpacing: 0.3,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  value,
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontSize: 15,
                    fontWeight: FontWeight.w800,
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

class _PhotoLoading extends StatelessWidget {
  const _PhotoLoading();

  @override
  Widget build(BuildContext context) {
    return const ColoredBox(
      color: AppTheme.surface,
      child: Center(
        child: SizedBox(
          width: 30,
          height: 30,
          child: CircularProgressIndicator(strokeWidth: 2.4),
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
      child: const Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.emoji_events_outlined, color: AppTheme.gold, size: 46),
          SizedBox(height: 10),
          Text(
            'Geen trofeefoto beskikbaar nie',
            style: TextStyle(
              color: AppTheme.muted,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

class _DetailStateCard extends StatelessWidget {
  const _DetailStateCard({
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
