import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../models/hunt_pin.dart';
import '../theme/app_theme.dart';
import '../utils/hunt_pin_icons.dart';

class LivePinList extends StatelessWidget {
  const LivePinList({super.key, required this.pins, required this.onRemove, this.onConfirm});

  final List<HuntPin> pins;
  final ValueChanged<String> onRemove;

  /// Confirms an automatic (Outo-skoot) pin. Only shown on pins that need review.
  final ValueChanged<String>? onConfirm;

  @override
  Widget build(BuildContext context) {
    if (pins.isEmpty) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 12),
        child: Text(
          'Nog geen merkers geplaas nie.',
          style: TextStyle(color: AppTheme.muted),
        ),
      );
    }

    final timeFormat = DateFormat('HH:mm:ss');

    return ListView.separated(
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      itemCount: pins.length,
      separatorBuilder: (_, _) => const Divider(height: 1),
      itemBuilder: (context, index) {
        final pin = pins[pins.length - 1 - index];
        final auto = pin.autoDetected
            ? ' • Outo${pin.confidence != null ? ' ${(pin.confidence! * 100).round()}%' : ''}${pin.confirmed ? ' ✓' : ''}'
            : '';

        return ListTile(
          dense: true,
          leading: Icon(
            iconForHuntPin(pin.type),
            color: pin.needsReview ? AppTheme.dangerSoft : AppTheme.gold,
          ),
          title: Text(
            pin.type.label + (pin.needsReview ? ' ?' : ''),
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
          subtitle: Text(
            '${timeFormat.format(pin.timestamp)}$auto'
            '${pin.note != null ? ' • ${pin.note}' : ''}\n'
            '${pin.latitude.toStringAsFixed(5)}, ${pin.longitude.toStringAsFixed(5)}',
          ),
          isThreeLine: pin.note != null,
          trailing: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (pin.needsReview && onConfirm != null)
                IconButton(
                  tooltip: 'Bevestig skoot',
                  icon: const Icon(Icons.check_circle_outline_rounded, color: AppTheme.gold),
                  onPressed: () => onConfirm!(pin.id),
                ),
              IconButton(
                tooltip: pin.needsReview ? 'Verwyder (nie \'n skoot nie)' : 'Verwyder',
                icon: const Icon(
                  Icons.delete_outline_rounded,
                  color: AppTheme.dangerSoft,
                ),
                onPressed: () => onRemove(pin.id),
              ),
            ],
          ),
        );
      },
    );
  }
}
