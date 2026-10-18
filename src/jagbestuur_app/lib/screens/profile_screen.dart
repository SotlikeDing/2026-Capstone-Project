import 'package:flutter/material.dart';

import '../models/app_session.dart';
import '../models/permit_status.dart';
import '../services/auth_service.dart';
import '../services/hunt_tracking_service.dart';
import '../services/permit_status_service.dart';
import '../services/rowland_ward_preference_controller.dart';
import '../theme/app_theme.dart';
import '../widgets/app_logo.dart';
import '../widgets/hunting_card.dart';

class ProfileScreen extends StatefulWidget {
  const ProfileScreen({
    super.key,
    required this.session,
    this.preferenceController,
  });

  final AppSession session;
  final RowlandWardPreferenceController? preferenceController;

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  late final RowlandWardPreferenceController _preferenceController;

  Future<void> _confirmSignOut() async {
    final huntActive = HuntTrackingService.activeHunt.value;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Teken uit?'),
        content: Text(
          huntActive
              ? 'Jy is besig met ’n jag. As jy nou uitteken, word die jag '
                    'gestop en gestoor.'
              : 'Jou jagroetes en video’s bly op hierdie foon, maar net jy '
                    'kan dit sien wanneer jy weer inteken.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: const Text('Kanselleer'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: const Text('Teken uit'),
          ),
        ],
      ),
    );

    if (confirmed == true) {
      await AuthService.instance.signOut();
    }
  }

  @override
  void initState() {
    super.initState();
    _preferenceController =
        widget.preferenceController ?? RowlandWardPreferenceController.instance;
    if (widget.session.isHunter) {
      _preferenceController.loadForUser(widget.session.userId, force: true);
    }
  }

  @override
  void didUpdateWidget(covariant ProfileScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.session.userId != widget.session.userId) {
      _preferenceController.loadForUser(widget.session.userId, force: true);
    }
  }

  @override
  Widget build(BuildContext context) {
    final displayName = widget.session.displayName.isEmpty
        ? 'Gebruiker ${widget.session.userId}'
        : widget.session.displayName;
    final email = widget.session.email.isEmpty
        ? 'Geen e-pos beskikbaar nie'
        : widget.session.email;

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 120),
            children: [
              const AppLogo(compact: true, showTagline: false),
              const SizedBox(height: 20),
              const Text(
                'JOU REKENING',
                style: TextStyle(
                  color: AppTheme.gold,
                  fontSize: 10,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 1,
                ),
              ),
              const SizedBox(height: 5),
              const Text(
                'Profiel',
                style: TextStyle(
                  color: AppTheme.ink,
                  fontSize: 30,
                  fontWeight: FontWeight.w900,
                  letterSpacing: -0.4,
                ),
              ),
              const SizedBox(height: 6),
              const Text(
                'Jou rekeningbesonderhede en permitstatusse op een plek.',
                style: TextStyle(
                  color: AppTheme.muted,
                  fontSize: 14,
                  height: 1.4,
                ),
              ),
              const SizedBox(height: 18),
              _ProfileHeader(displayName: displayName, email: email),
              const SizedBox(height: 16),
              if (widget.session.isHunter)
                RowlandWardPreferenceCard(
                  controller: _preferenceController,
                  userId: widget.session.userId,
                ),
              const SizedBox(height: 16),
              _PermitStatusSection(userRoleId: widget.session.userRoleId),
              const SizedBox(height: 24),
              const Text(
                'SESSIE',
                style: TextStyle(
                  color: AppTheme.gold,
                  fontSize: 10,
                  fontWeight: FontWeight.w900,
                  letterSpacing: 1,
                ),
              ),
              const SizedBox(height: 8),
              SizedBox(
                width: double.infinity,
                child: OutlinedButton.icon(
                  onPressed: _confirmSignOut,
                  style: OutlinedButton.styleFrom(
                    foregroundColor: AppTheme.dangerSoft,
                    backgroundColor: AppTheme.danger.withValues(alpha: 0.08),
                    side: BorderSide(
                      color: AppTheme.danger.withValues(alpha: 0.52),
                    ),
                  ),
                  icon: const Icon(Icons.logout_rounded),
                  label: const Text('Teken uit'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class RowlandWardPreferenceCard extends StatelessWidget {
  const RowlandWardPreferenceCard({
    super.key,
    required this.controller,
    required this.userId,
  });

  final RowlandWardPreferenceController controller;
  final int userId;

  Future<void> _save(BuildContext context, bool enabled) async {
    final saved = await controller.setEnabled(enabled);
    if (!context.mounted || saved) return;

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text(
          'Die Rowland Ward-instelling kon nie gestoor word nie. Probeer weer.',
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      padding: const EdgeInsets.all(18),
      child: ValueListenableBuilder<RowlandWardPreferenceState>(
        valueListenable: controller,
        builder: (context, state, _) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.center,
                children: [
                  Container(
                    width: 42,
                    height: 42,
                    decoration: BoxDecoration(
                      color: AppTheme.gold.withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(
                        AppTheme.radiusMedium,
                      ),
                    ),
                    child: const Icon(
                      Icons.straighten_rounded,
                      color: AppTheme.goldSoft,
                      size: 22,
                    ),
                  ),
                  const SizedBox(width: 12),
                  const Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Rowland Ward-funksie',
                          style: TextStyle(
                            color: AppTheme.ink,
                            fontSize: 16,
                            fontWeight: FontWeight.w900,
                          ),
                        ),
                        SizedBox(height: 4),
                        Text(
                          'Gebruik Rowland Ward-metings en -standaarde vir jou trofeë.',
                          style: TextStyle(
                            color: AppTheme.muted,
                            fontSize: 12,
                            height: 1.35,
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 10),
                  if (state.isLoading || state.isSaving)
                    const SizedBox(
                      width: 24,
                      height: 24,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  else
                    Switch.adaptive(
                      value: state.enabled,
                      activeTrackColor: AppTheme.olive,
                      onChanged: (enabled) => _save(context, enabled),
                    ),
                ],
              ),
              if (state.loadFailed) ...[
                const SizedBox(height: 12),
                Text(
                  'Die Rowland Ward-instelling kon nie gelaai word nie.',
                  style: TextStyle(
                    color: AppTheme.dangerSoft,
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 6),
                TextButton.icon(
                  onPressed: () => controller.loadForUser(userId, force: true),
                  icon: const Icon(Icons.refresh_rounded, size: 17),
                  label: const Text('Probeer weer'),
                ),
              ],
            ],
          );
        },
      ),
    );
  }
}

class _ProfileHeader extends StatelessWidget {
  const _ProfileHeader({required this.displayName, required this.email});

  final String displayName;
  final String email;

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      padding: const EdgeInsets.all(20),
      gradient: const LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [Color(0xFF2B261F), Color(0xFF22251A)],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              Container(
                width: 68,
                height: 68,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: AppTheme.olive.withValues(alpha: 0.42),
                  shape: BoxShape.circle,
                  border: Border.all(
                    color: AppTheme.gold.withValues(alpha: 0.62),
                    width: 1.4,
                  ),
                ),
                child: Text(
                  _profileInitial(displayName),
                  style: const TextStyle(
                    color: AppTheme.cream,
                    fontSize: 27,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ),
              const SizedBox(width: 16),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Text(
                      'REKENINGHOUER',
                      style: TextStyle(
                        color: AppTheme.gold,
                        fontSize: 9,
                        fontWeight: FontWeight.w900,
                        letterSpacing: 0.9,
                      ),
                    ),
                    const SizedBox(height: 5),
                    Text(
                      displayName,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        color: AppTheme.ink,
                        fontSize: 23,
                        fontWeight: FontWeight.w900,
                        height: 1.12,
                        letterSpacing: -0.25,
                      ),
                    ),
                    const SizedBox(height: 7),
                    Row(
                      children: [
                        const Icon(
                          Icons.mail_outline_rounded,
                          color: AppTheme.muted,
                          size: 15,
                        ),
                        const SizedBox(width: 6),
                        Expanded(
                          child: Text(
                            email,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              color: AppTheme.muted,
                              fontSize: 12.5,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _PermitStatusSection extends StatefulWidget {
  const _PermitStatusSection({required this.userRoleId});

  final int userRoleId;

  @override
  State<_PermitStatusSection> createState() => _PermitStatusSectionState();
}

class _PermitStatusSectionState extends State<_PermitStatusSection> {
  late Future<List<PermitStatus>> _permitFuture;

  @override
  void initState() {
    super.initState();
    _loadPermits();
  }

  void _loadPermits() {
    _permitFuture = PermitStatusService().getUserPermitStatuses(
      widget.userRoleId,
    );
  }

  void _refreshPermits() {
    setState(_loadPermits);
  }

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      padding: EdgeInsets.zero,
      child: FutureBuilder<List<PermitStatus>>(
        future: _permitFuture,
        builder: (context, snapshot) {
          final isLoading = snapshot.connectionState == ConnectionState.waiting;
          final permits = snapshot.data ?? const <PermitStatus>[];

          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(18, 17, 12, 15),
                child: Row(
                  children: [
                    Container(
                      width: 42,
                      height: 42,
                      decoration: BoxDecoration(
                        color: AppTheme.gold.withValues(alpha: 0.12),
                        borderRadius: BorderRadius.circular(
                          AppTheme.radiusMedium,
                        ),
                      ),
                      child: const Icon(
                        Icons.assignment_turned_in_rounded,
                        color: AppTheme.goldSoft,
                        size: 22,
                      ),
                    ),
                    const SizedBox(width: 12),
                    const Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'PERMITTE',
                            style: TextStyle(
                              color: AppTheme.gold,
                              fontSize: 9,
                              fontWeight: FontWeight.w900,
                              letterSpacing: 0.9,
                            ),
                          ),
                          SizedBox(height: 3),
                          Text(
                            'Permit statusse',
                            style: TextStyle(
                              color: AppTheme.ink,
                              fontSize: 19,
                              fontWeight: FontWeight.w900,
                            ),
                          ),
                        ],
                      ),
                    ),
                    IconButton(
                      onPressed: isLoading ? null : _refreshPermits,
                      icon: isLoading
                          ? const SizedBox(
                              width: 19,
                              height: 19,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.refresh_rounded),
                      color: AppTheme.goldSoft,
                      tooltip: 'Herlaai permit statusse',
                    ),
                  ],
                ),
              ),
              const Divider(),
              if (isLoading)
                const _PermitStateRow(
                  icon: Icons.hourglass_top_rounded,
                  title: 'Permitstatusse word gelaai',
                  message: 'Wag net ’n oomblik.',
                  showLoader: true,
                )
              else if (snapshot.hasError)
                const _PermitStateRow(
                  icon: Icons.cloud_off_rounded,
                  title: 'Kon nie permitstatusse laai nie',
                  message: 'Gebruik die herlaai-knoppie om weer te probeer.',
                  accentColor: AppTheme.dangerSoft,
                )
              else if (permits.isEmpty)
                const _PermitStateRow(
                  icon: Icons.assignment_outlined,
                  title: 'Geen permitte beskikbaar nie',
                  message: 'Beskikbare permitstatusse sal hier verskyn.',
                )
              else
                for (var index = 0; index < permits.length; index++) ...[
                  if (index != 0)
                    const Divider(height: 1, indent: 18, endIndent: 18),
                  _PermitStatusTile(
                    title: permits[index].name,
                    status: permits[index].status,
                  ),
                ],
            ],
          );
        },
      ),
    );
  }
}

class _PermitStatusTile extends StatelessWidget {
  const _PermitStatusTile({required this.title, required this.status});

  final String title;
  final String status;

  Color get _statusColor {
    switch (status) {
      case 'Goedgekeur':
        return AppTheme.olive;
      case 'Afgekeur':
        return AppTheme.dangerSoft;
      case 'Wag Nog':
      default:
        return AppTheme.goldSoft;
    }
  }

  IconData get _statusIcon {
    switch (status) {
      case 'Goedgekeur':
        return Icons.check_circle_rounded;
      case 'Afgekeur':
        return Icons.cancel_rounded;
      case 'Wag Nog':
      default:
        return Icons.hourglass_top_rounded;
    }
  }

  @override
  Widget build(BuildContext context) {
    final statusColor = _statusColor;
    final isApproved = status == 'Goedgekeur';
    final statusForeground = isApproved ? AppTheme.cream : statusColor;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 14),
      child: Row(
        children: [
          Container(
            width: 36,
            height: 36,
            decoration: BoxDecoration(
              color: AppTheme.gold.withValues(alpha: 0.1),
              borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
            ),
            child: const Icon(
              Icons.description_outlined,
              color: AppTheme.goldSoft,
              size: 18,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              title,
              style: const TextStyle(
                color: AppTheme.ink,
                fontSize: 14,
                fontWeight: FontWeight.w800,
                height: 1.3,
              ),
            ),
          ),
          const SizedBox(width: 10),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 6),
            decoration: BoxDecoration(
              color: isApproved
                  ? statusColor.withValues(alpha: 0.9)
                  : statusColor.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
              border: Border.all(color: statusColor.withValues(alpha: 0.5)),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(_statusIcon, color: statusForeground, size: 14),
                const SizedBox(width: 5),
                Text(
                  status,
                  style: TextStyle(
                    color: statusForeground,
                    fontSize: 10,
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

class _PermitStateRow extends StatelessWidget {
  const _PermitStateRow({
    required this.icon,
    required this.title,
    required this.message,
    this.accentColor = AppTheme.goldSoft,
    this.showLoader = false,
  });

  final IconData icon;
  final String title;
  final String message;
  final Color accentColor;
  final bool showLoader;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(18),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 42,
            height: 42,
            decoration: BoxDecoration(
              color: accentColor.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
            ),
            child: showLoader
                ? Padding(
                    padding: const EdgeInsets.all(11),
                    child: CircularProgressIndicator(
                      color: accentColor,
                      strokeWidth: 2,
                    ),
                  )
                : Icon(icon, color: accentColor, size: 21),
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
                    fontSize: 14,
                    fontWeight: FontWeight.w900,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  message,
                  style: const TextStyle(
                    color: AppTheme.muted,
                    fontSize: 12,
                    height: 1.4,
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

String _profileInitial(String value) {
  final trimmed = value.trim();
  if (trimmed.isEmpty) return '#';
  return trimmed[0].toUpperCase();
}
