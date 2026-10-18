import 'package:flutter/material.dart';

import '../services/location_service.dart';
import '../services/sos_actions.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';

class SosButton extends StatelessWidget {
  final String label;
  final bool expanded;
  final double height;

  const SosButton({
    super.key,
    this.label = 'SOS',
    this.expanded = true,
    this.height = 70,
  });

  @override
  Widget build(BuildContext context) {
    final button = Material(
      color: Colors.transparent,
      borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
      child: InkWell(
        borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
        onTap: () => showSosSheet(context),
        child: Ink(
          height: height,
          decoration: BoxDecoration(
            gradient: const LinearGradient(
              colors: [AppTheme.danger, Color(0xFF78392E)],
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
            ),
            borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
            border: Border.all(
              color: AppTheme.dangerSoft.withValues(alpha: 0.32),
            ),
            boxShadow: const [
              BoxShadow(
                color: Color(0x4078392E),
                blurRadius: 14,
                offset: Offset(0, 5),
              ),
            ],
          ),
          padding: const EdgeInsets.symmetric(horizontal: 22),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              const Icon(
                Icons.warning_rounded,
                color: AppTheme.cream,
                size: 28,
              ),
              const SizedBox(width: 12),
              Text(
                label,
                style: const TextStyle(
                  color: AppTheme.cream,
                  fontSize: 18,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0.4,
                ),
              ),
            ],
          ),
        ),
      ),
    );

    return expanded ? SizedBox(width: double.infinity, child: button) : button;
  }
}

Future<void> showSosSheet(BuildContext context) async {
  final rootContext = context;

  await showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    backgroundColor: AppTheme.card,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(
        top: Radius.circular(AppTheme.radiusExtraLarge),
      ),
      side: BorderSide(color: AppTheme.border),
    ),
    builder: (sheetContext) {
      return SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Center(
                child: Container(
                  width: 44,
                  height: 4,
                  decoration: BoxDecoration(
                    color: AppTheme.border,
                    borderRadius: BorderRadius.circular(99),
                  ),
                ),
              ),
              const SizedBox(height: 18),
              const Text(
                'SOS',
                style: TextStyle(
                  color: AppTheme.dangerSoft,
                  fontSize: 28,
                  fontWeight: FontWeight.w800,
                ),
              ),
              const SizedBox(height: 6),
              const Text(
                'Kies vinnig wat jy wil doen. Oproepe en SMS’e werk ook sonder '
                'mobiele data.',
                style: TextStyle(
                  color: AppTheme.muted,
                  fontSize: 14,
                  height: 1.4,
                ),
              ),
              const SizedBox(height: 20),
              _SosActionTile(
                icon: Icons.call_rounded,
                title: 'Bel nooddienste (${SosActions.emergencyNumber})',
                subtitle: 'Maak die foon oop met die noodnommer ingevul.',
                color: AppTheme.dangerSoft,
                onTap: () => _runSosAction(
                  sheetContext,
                  rootContext,
                  SosActions.callEmergencyServices,
                ),
              ),
              const SizedBox(height: 12),
              _SosActionTile(
                icon: Icons.location_on_rounded,
                title: 'Deel ligging',
                subtitle: 'Stuur jou GPS-koördinate en kaartskakel per SMS.',
                color: AppTheme.goldSoft,
                onTap: () => _runSosAction(
                  sheetContext,
                  rootContext,
                  SosActions.shareLocation,
                  pendingMessage: 'Kry jou ligging...',
                ),
              ),
              const SizedBox(height: 12),
              _SosActionTile(
                icon: Icons.sms_rounded,
                title: 'Stuur noodboodskap',
                subtitle: 'SMS ’n hulpversoek met jou posisie.',
                color: AppTheme.ink,
                onTap: () => _runSosAction(
                  sheetContext,
                  rootContext,
                  SosActions.sendEmergencyMessage,
                  pendingMessage: 'Kry jou ligging...',
                ),
              ),
              const SizedBox(height: 18),
              SizedBox(
                width: double.infinity,
                child: OutlinedButton(
                  onPressed: () => Navigator.of(sheetContext).pop(),
                  style: OutlinedButton.styleFrom(
                    foregroundColor: AppTheme.muted,
                    side: const BorderSide(color: AppTheme.border),
                    padding: const EdgeInsets.symmetric(vertical: 16),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(
                        AppTheme.radiusMedium,
                      ),
                    ),
                  ),
                  child: const Text('Kanselleer'),
                ),
              ),
            ],
          ),
        ),
      );
    },
  );
}

Future<void> _runSosAction(
  BuildContext sheetContext,
  BuildContext rootContext,
  Future<void> Function() action, {
  String? pendingMessage,
}) async {
  Navigator.of(sheetContext).pop();
  final messenger = ScaffoldMessenger.of(rootContext);

  if (pendingMessage != null) {
    messenger.showSnackBar(
      SnackBar(
        content: Text(pendingMessage),
        duration: const Duration(seconds: 15),
      ),
    );
  }

  try {
    await action();
    messenger.hideCurrentSnackBar();
  } on LocationUnavailableException catch (e) {
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(e.message)));
  } catch (e) {
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text(
            AppErrorMessage.from(
              e,
              fallback:
                  'Die aksie het misluk. Bel ${SosActions.emergencyNumber} self.',
            ),
          ),
        ),
      );
  }
}

class _SosActionTile extends StatelessWidget {
  final IconData icon;
  final String title;
  final String subtitle;
  final Color color;
  final VoidCallback onTap;

  const _SosActionTile({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.color,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Material(
      color: AppTheme.surface,
      borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
        splashColor: color.withValues(alpha: 0.10),
        highlightColor: color.withValues(alpha: 0.05),
        child: Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
            border: Border.all(color: AppTheme.border.withValues(alpha: 0.86)),
          ),
          child: Row(
            children: [
              Container(
                width: 48,
                height: 48,
                decoration: BoxDecoration(
                  color: color.withValues(alpha: 0.18),
                  borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
                ),
                child: Icon(icon, color: color),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      style: const TextStyle(
                        color: AppTheme.ink,
                        fontSize: 15,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      subtitle,
                      style: const TextStyle(
                        color: AppTheme.muted,
                        fontSize: 13,
                        height: 1.35,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
