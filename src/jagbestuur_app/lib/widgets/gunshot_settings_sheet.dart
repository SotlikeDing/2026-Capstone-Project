import 'package:flutter/material.dart';

import '../services/gunshot/gunshot_pipeline.dart';
import '../services/gunshot/gunshot_settings.dart';
import '../services/gunshot/personal_head.dart';
import '../theme/app_theme.dart';

/// Settings for Outo-skoot (automatic gunshot pins), opened from the map's
/// microphone button. Returns the new settings, or null when dismissed.
class GunshotSettingsSheet extends StatefulWidget {
  const GunshotSettingsSheet({
    super.key,
    required this.initial,
    required this.head,
    required this.onForget,
    this.stats,
    this.listening = false,
  });

  final GunshotSettings initial;
  final PersonalHead head;
  final Future<void> Function() onForget;
  final GunshotStats? stats;
  final bool listening;

  static Future<GunshotSettings?> show(
    BuildContext context, {
    required GunshotSettings initial,
    required PersonalHead head,
    required Future<void> Function() onForget,
    GunshotStats? stats,
    bool listening = false,
  }) {
    return showModalBottomSheet<GunshotSettings>(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.card,
      builder: (_) => GunshotSettingsSheet(
        initial: initial,
        head: head,
        onForget: onForget,
        stats: stats,
        listening: listening,
      ),
    );
  }

  @override
  State<GunshotSettingsSheet> createState() => _GunshotSettingsSheetState();
}

class _GunshotSettingsSheetState extends State<GunshotSettingsSheet> {
  late GunshotSettings _settings = widget.initial;

  String _learningText() {
    final h = widget.head;
    if (h.sampleCount == 0) {
      return 'Nog niks geleer nie. Bevestig of verwyder outomatiese merkers en die app pas by jou geweer en omgewing aan.';
    }
    final base = 'Geleer uit ${h.confirmedCount} bevestig en ${h.removedCount} verwyder.';
    if (h.isActive) {
      return '$base Jou persoonlike model tel ${(h.share * 100).round()}% van die besluit.';
    }
    final needTotal = (PersonalHead.minSamples - h.sampleCount).clamp(0, PersonalHead.minSamples);
    final needConfirmed = (PersonalHead.minPerClass - h.confirmedCount).clamp(0, PersonalHead.minPerClass);
    final needRemoved = (PersonalHead.minPerClass - h.removedCount).clamp(0, PersonalHead.minPerClass);
    return '$base Nog nodig voor dit begin leer: $needTotal oordele '
        '(minstens $needConfirmed bevestig en $needRemoved verwyder).';
  }

  @override
  Widget build(BuildContext context) {
    final stats = widget.stats;
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 16, 20, 20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Outo-skoot',
              style: TextStyle(color: AppTheme.ink, fontSize: 18, fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 4),
            const Text(
              'Luister tydens \'n jag vir skote en los outomaties \'n "Skoot geskiet"-pen waar jy is. '
              'Die klank word net vir \'n oomblik in die foon se geheue ontleed en nooit gestoor of gestuur nie.',
              style: TextStyle(color: AppTheme.muted, fontSize: 12),
            ),
            SwitchListTile.adaptive(
              contentPadding: EdgeInsets.zero,
              value: _settings.enabled,
              activeThumbColor: AppTheme.gold,
              title: const Text('Outo-skoot aan', style: TextStyle(color: AppTheme.ink)),
              subtitle: Text(
                widget.listening ? 'Luister tans' : 'Luister net terwyl \'n jag aktief is',
                style: const TextStyle(color: AppTheme.muted, fontSize: 12),
              ),
              onChanged: (v) => setState(() => _settings = _settings.copyWith(enabled: v)),
            ),
            const SizedBox(height: 8),
            const Text('Sensitiwiteit', style: TextStyle(color: AppTheme.ink, fontWeight: FontWeight.w700)),
            const SizedBox(height: 6),
            SegmentedButton<GunshotSensitivity>(
              segments: [
                for (final s in GunshotSensitivity.values) ButtonSegment(value: s, label: Text(s.label)),
              ],
              selected: {_settings.sensitivity},
              onSelectionChanged: (sel) => setState(() => _settings = _settings.copyWith(sensitivity: sel.first)),
            ),
            const SizedBox(height: 4),
            const Text(
              'Hoog mis minder skote maar plaas meer verkeerde merkers; Laag is die teenoorgestelde.',
              style: TextStyle(color: AppTheme.muted, fontSize: 12),
            ),
            SwitchListTile.adaptive(
              contentPadding: EdgeInsets.zero,
              value: _settings.pinDistant,
              activeThumbColor: AppTheme.gold,
              title: const Text('Pen ook verre skote', style: TextStyle(color: AppTheme.ink)),
              subtitle: const Text(
                'Die pen wys waar JY was toe die skoot gehoor is, nie waar dit geskiet is nie.',
                style: TextStyle(color: AppTheme.muted, fontSize: 12),
              ),
              onChanged: (v) => setState(() => _settings = _settings.copyWith(pinDistant: v)),
            ),
            const Divider(),
            const Text('Wat die app geleer het', style: TextStyle(color: AppTheme.ink, fontWeight: FontWeight.w700)),
            const SizedBox(height: 4),
            Text(_learningText(), style: const TextStyle(color: AppTheme.muted, fontSize: 12)),
            if (widget.head.sampleCount > 0)
              TextButton(
                onPressed: () async {
                  await widget.onForget();
                  if (mounted) setState(() {});
                },
                child: const Text('Vergeet wat geleer is'),
              ),
            if (stats != null && widget.listening)
              Text(
                'Hierdie jag: ${stats.onsets} harde klanke, ${stats.classified} ontleed, ${stats.accepted} as skote herken'
                '${stats.lastLabel != null ? ' • laaste: ${stats.lastLabel}' : ''}',
                style: const TextStyle(color: AppTheme.muted, fontSize: 11),
              ),
            const SizedBox(height: 12),
            SizedBox(
              width: double.infinity,
              child: ElevatedButton(
                onPressed: () => Navigator.pop(context, _settings),
                style: ElevatedButton.styleFrom(backgroundColor: AppTheme.olive, foregroundColor: Colors.white),
                child: const Text('Stoor'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
