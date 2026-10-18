import 'dart:typed_data';

/// YAMNet's input: 0.975 s of 16 kHz mono audio.
const int kYamnetWindowSamples = 15600;

/// The AudioSet classes that count as a gunshot (matched by display name in
/// the bundled class map, never by index).
const Set<String> kGunshotClassNames = {
  'Gunshot, gunfire',
  'Machine gun',
  'Fusillade',
  'Cap gun',
};

class ShotClassification {
  const ShotClassification({required this.gunScore, required this.embedding, required this.topLabel});

  /// Highest score among [kGunshotClassNames] (0-1, multi-label sigmoid).
  final double gunScore;

  /// YAMNet's 1024-number summary of the sound - what the personal head learns
  /// from. Not audio: the sound cannot be reconstructed from it.
  final Float32List embedding;

  /// Best class overall, for the debug view.
  final String topLabel;
}

/// The neural-network stage. Implemented by YamnetClassifier on the phone and
/// by fakes in tests.
abstract class ShotClassifier {
  Future<ShotClassification> classify(Float32List waveform);
  Future<void> close();
}
