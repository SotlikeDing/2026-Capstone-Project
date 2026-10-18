import 'package:flutter/material.dart';

import '../models/hunt_pin.dart';
import '../theme/app_theme.dart';
import '../utils/hunt_pin_icons.dart';

/// Bottom sheet for choosing what kind of pin to drop during a hunt.
/// Returns the selected [HuntPinType] and optional note, or null if
/// the user cancels.
class PinDropSheet extends StatefulWidget {
  const PinDropSheet({super.key});

  static Future<({HuntPinType type, String? note})?> show(
    BuildContext context,
  ) {
    return showModalBottomSheet<({HuntPinType type, String? note})>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => const PinDropSheet(),
    );
  }

  @override
  State<PinDropSheet> createState() => _PinDropSheetState();
}

class _PinDropSheetState extends State<PinDropSheet> {
  HuntPinType? _selected;
  final TextEditingController _noteController = TextEditingController();

  @override
  void dispose() {
    _noteController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.only(
        left: 20,
        right: 20,
        top: 20,
        bottom: MediaQuery.of(context).viewInsets.bottom + 20,
      ),
      decoration: const BoxDecoration(
        color: AppTheme.card,
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Los \'n pen hier',
            style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 16),
          Wrap(
            spacing: 10,
            runSpacing: 10,
            children: HuntPinType.values.map((type) {
              final isSelected = _selected == type;
              return ChoiceChip(
                label: Text(type.label),
                avatar: Icon(
                  iconForHuntPin(type),
                  size: 18,
                  color: isSelected ? Colors.black : AppTheme.muted,
                ),
                selected: isSelected,
                selectedColor: AppTheme.gold,
                onSelected: (_) => setState(() => _selected = type),
              );
            }).toList(),
          ),
          const SizedBox(height: 16),
          TextField(
            controller: _noteController,
            decoration: const InputDecoration(
              labelText: 'Nota (opsioneel)',
              border: OutlineInputBorder(),
            ),
            maxLines: 2,
          ),
          const SizedBox(height: 20),
          SizedBox(
            width: double.infinity,
            child: ElevatedButton(
              onPressed: _selected == null
                  ? null
                  : () => Navigator.pop(context, (
                      type: _selected!,
                      note: _noteController.text.trim().isEmpty
                          ? null
                          : _noteController.text.trim(),
                    )),
              style: ElevatedButton.styleFrom(
                backgroundColor: AppTheme.gold,
                foregroundColor: Colors.black,
                padding: const EdgeInsets.symmetric(vertical: 14),
              ),
              child: const Text(
                'Los pen',
                style: TextStyle(fontWeight: FontWeight.w800),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
