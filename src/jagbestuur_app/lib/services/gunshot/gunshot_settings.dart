import 'package:shared_preferences/shared_preferences.dart';

import '../user_storage.dart';

/// How eager the detector is. Higher = fewer missed shots, more false pins.
enum GunshotSensitivity {
  low,
  medium,
  high;

  String get label {
    switch (this) {
      case GunshotSensitivity.low:
        return 'Laag';
      case GunshotSensitivity.medium:
        return 'Medium';
      case GunshotSensitivity.high:
        return 'Hoog';
    }
  }

  /// Loudest sample of a frame must reach this level before the model runs.
  double get gatePeakDbfs {
    switch (this) {
      case GunshotSensitivity.low:
        return -20;
      case GunshotSensitivity.medium:
        return -30;
      case GunshotSensitivity.high:
        return -40;
    }
  }

  /// YAMNet gun-class score that counts as a shot. Measured with
  /// tools/audio/calibrate_yamnet.py on labelled clips (see GunshotCalibration):
  /// high = keeps ~95% of shots, medium = best balance, low = almost no false pins.
  double get modelThreshold {
    switch (this) {
      case GunshotSensitivity.low:
        return GunshotCalibration.lowThreshold;
      case GunshotSensitivity.medium:
        return GunshotCalibration.mediumThreshold;
      case GunshotSensitivity.high:
        return GunshotCalibration.highThreshold;
    }
  }
}

/// Thresholds from the last calibration run (tools/audio/calibrate_yamnet.py,
/// 2026-10-08): 374 UrbanSound8K gunshots vs. 5410 other clips (UrbanSound8K
/// car horn / children / dog / engine + all of ESC-50). Per clip:
///   high   0.01 - 94% of shots caught, 4.8% false pins (lowest value tested;
///                 no threshold reached 95%)
///   medium 0.05 - 85% caught, 1.8% false pins (best F1, 0.81)
///   low    0.35 - 44% caught, 0.44% false pins
/// Worst false triggers at medium: can opening, fireworks, glass breaking,
/// clapping, footsteps. The near-shot loudness filter is not part of these
/// numbers (dataset levels are not phone levels) and removes more on a phone.
class GunshotCalibration {
  const GunshotCalibration._();

  static const double highThreshold = 0.01;
  static const double mediumThreshold = 0.05;
  static const double lowThreshold = 0.35;

  /// A frame peak at or above this nearly clips the microphone: the shot was
  /// fired close to the phone (the hunter or someone next to them).
  static const double nearPeakDbfs = -6;
}

/// The hunter's choices for automatic shot pins, stored per user on the device.
class GunshotSettings {
  const GunshotSettings({
    this.enabled = false,
    this.sensitivity = GunshotSensitivity.medium,
    this.pinDistant = false,
  });

  /// Opt-in: the microphone is only used when this is on and a hunt is running.
  final bool enabled;
  final GunshotSensitivity sensitivity;

  /// Also pin shots that were clearly fired far away (labelled "ver").
  final bool pinDistant;

  GunshotSettings copyWith({bool? enabled, GunshotSensitivity? sensitivity, bool? pinDistant}) =>
      GunshotSettings(
        enabled: enabled ?? this.enabled,
        sensitivity: sensitivity ?? this.sensitivity,
        pinDistant: pinDistant ?? this.pinDistant,
      );

  static String _key(String name) => 'gunshot.${UserStorage.requireUserId()}.$name';

  static Future<GunshotSettings> load() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      return GunshotSettings(
        enabled: prefs.getBool(_key('enabled')) ?? false,
        sensitivity: GunshotSensitivity.values.firstWhere(
          (s) => s.name == prefs.getString(_key('sensitivity')),
          orElse: () => GunshotSensitivity.medium,
        ),
        pinDistant: prefs.getBool(_key('pinDistant')) ?? false,
      );
    } on StateError {
      return const GunshotSettings();
    }
  }

  Future<void> save() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_key('enabled'), enabled);
    await prefs.setString(_key('sensitivity'), sensitivity.name);
    await prefs.setBool(_key('pinDistant'), pinDistant);
  }
}
