import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../models/rowland_ward_measurement.dart';
import '../theme/app_theme.dart';
import '../utils/measurement_format.dart';

class RowlandWardMeasurementForm extends StatefulWidget {
  const RowlandWardMeasurementForm({
    super.key,
    required this.requirements,
    required this.initialValues,
    required this.onChanged,
  });

  final RowlandWardMeasurementRequirements requirements;
  final Map<String, RowlandWardMeasurementValue> initialValues;
  final ValueChanged<Map<String, RowlandWardMeasurementValue>> onChanged;

  @override
  State<RowlandWardMeasurementForm> createState() =>
      _RowlandWardMeasurementFormState();
}

class _RowlandWardMeasurementFormState
    extends State<RowlandWardMeasurementForm> {
  final Map<String, TextEditingController> _controllers = {};
  final Map<String, bool?> _booleanValues = {};

  List<RowlandWardMeasurementField> get _fields =>
      widget.requirements.requiredFields;

  @override
  void initState() {
    super.initState();
    for (final field in _fields) {
      final initial = widget.initialValues[field.id];
      if (field.kind == 'boolean') {
        _booleanValues[field.id] = initial?.booleanValue;
        continue;
      }

      final controller = TextEditingController(
        text: initial?.inputTextFor(field) ?? '',
      );
      controller.addListener(_notifyValuesChanged);
      _controllers[field.id] = controller;
    }
  }

  @override
  void dispose() {
    for (final controller in _controllers.values) {
      controller.removeListener(_notifyValuesChanged);
      controller.dispose();
    }
    super.dispose();
  }

  void _notifyValuesChanged() {
    final requiredFieldIds = _fields.map((field) => field.id).toSet();
    final values = <String, RowlandWardMeasurementValue>{
      for (final entry in widget.initialValues.entries)
        if (!requiredFieldIds.contains(entry.key)) entry.key: entry.value,
    };
    for (final field in _fields) {
      if (field.kind == 'rational') {
        final rational = field.parseInputValue(
          _controllers[field.id]?.text ?? '',
        );
        if (rational != null) {
          values[field.id] = RowlandWardMeasurementValue(
            fieldId: field.id,
            kind: field.kind,
            unitCode: field.inputUnitCode,
            rational: rational,
          );
        }
      } else if (field.kind == 'integer') {
        final integer = int.tryParse(_controllers[field.id]?.text ?? '');
        if (integer != null) {
          values[field.id] = RowlandWardMeasurementValue(
            fieldId: field.id,
            kind: field.kind,
            integerValue: integer,
          );
        }
      } else if (field.kind == 'boolean') {
        final boolean = _booleanValues[field.id];
        if (boolean != null) {
          values[field.id] = RowlandWardMeasurementValue(
            fieldId: field.id,
            kind: field.kind,
            booleanValue: boolean,
          );
        }
      }
    }
    widget.onChanged(Map.unmodifiable(values));
  }

  String _fieldLabel(RowlandWardMeasurementField field) {
    final unit = field.inputUnitLabelAf.trim();
    return unit.isEmpty ? field.labelAf : '${field.labelAf} ($unit)';
  }

  String? _rationalValidator(String? input, RowlandWardMeasurementField field) {
    final text = input?.trim() ?? '';
    if (text.isEmpty) return 'Vul asseblief ${field.labelAf} in.';

    if (!RegExp(r'^\d+(?:[\.,]\d{1,2})?$').hasMatch(text)) {
      return 'Gebruik ’n geldige nie-negatiewe getal met hoogstens twee desimale plekke.';
    }

    final value = field.parseInputValue(text);
    if (value == null) {
      return '${field.labelAf} moet volgens die aangeduide meetstap afgerond word.';
    }

    return null;
  }

  String? _integerValidator(String? input, RowlandWardMeasurementField field) {
    final text = input?.trim() ?? '';
    if (text.isEmpty) return 'Vul asseblief ${field.labelAf} in.';
    if (!RegExp(r'^\d+$').hasMatch(text)) {
      return 'Gebruik ’n geldige nie-negatiewe heelgetal.';
    }
    return null;
  }

  String? _precisionText(RowlandWardMeasurementField field) {
    final increment = field.increment;
    if (increment == null || field.inputUnitCode != field.unitCode) {
      return 'Hoogstens twee desimale plekke';
    }
    return 'Stappe van ${increment.formatDecimal()} '
        '${field.inputUnitLabelAf}';
  }

  String? _inputHint(RowlandWardMeasurementField field) {
    if (field.inputUnitCode == 'kilogram') return 'Byvoorbeeld 45.5';
    if (field.kind == 'rational' &&
        field.increment?.denominator != BigInt.one) {
      return 'Byvoorbeeld 24.38';
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var index = 0; index < _fields.length; index++) ...[
          if (index > 0) const SizedBox(height: 14),
          _buildField(_fields[index]),
        ],
      ],
    );
  }

  Widget _buildField(RowlandWardMeasurementField field) {
    if (field.kind == 'boolean') return _buildBooleanField(field);

    final isInteger = field.kind == 'integer';
    return TextFormField(
      controller: _controllers[field.id],
      keyboardType: isInteger
          ? TextInputType.number
          : const TextInputType.numberWithOptions(decimal: true),
      textInputAction: TextInputAction.next,
      inputFormatters: isInteger
          ? [FilteringTextInputFormatter.digitsOnly]
          : const [TwoDecimalTextInputFormatter()],
      autocorrect: false,
      decoration: InputDecoration(
        labelText: _fieldLabel(field),
        hintText: _inputHint(field),
        helperText: _precisionText(field),
        prefixIcon: Icon(
          isInteger ? Icons.numbers_rounded : Icons.straighten_rounded,
        ),
      ),
      validator: (value) => isInteger
          ? _integerValidator(value, field)
          : _rationalValidator(value, field),
    );
  }

  Widget _buildBooleanField(RowlandWardMeasurementField field) {
    return FormField<bool>(
      initialValue: _booleanValues[field.id],
      validator: (value) => value == null ? 'Kies asseblief Ja of Nee.' : null,
      builder: (state) {
        return InputDecorator(
          decoration: InputDecoration(
            labelText: field.labelAf,
            prefixIcon: const Icon(Icons.rule_rounded),
            errorText: state.errorText,
          ),
          child: Row(
            children: [
              Expanded(
                child: _BooleanChoice(
                  label: 'Ja',
                  selected: state.value == true,
                  onTap: () {
                    setState(() {
                      _booleanValues[field.id] = true;
                    });
                    state.didChange(true);
                    _notifyValuesChanged();
                  },
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: _BooleanChoice(
                  label: 'Nee',
                  selected: state.value == false,
                  onTap: () {
                    setState(() {
                      _booleanValues[field.id] = false;
                    });
                    state.didChange(false);
                    _notifyValuesChanged();
                  },
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

class _BooleanChoice extends StatelessWidget {
  const _BooleanChoice({
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 160),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        decoration: BoxDecoration(
          color: selected
              ? AppTheme.olive.withValues(alpha: 0.24)
              : AppTheme.surface,
          borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
          border: Border.all(
            color: selected ? AppTheme.goldSoft : AppTheme.border,
          ),
        ),
        alignment: Alignment.center,
        child: Text(
          label,
          style: TextStyle(
            color: selected ? AppTheme.ink : AppTheme.muted,
            fontWeight: FontWeight.w800,
          ),
        ),
      ),
    );
  }
}
