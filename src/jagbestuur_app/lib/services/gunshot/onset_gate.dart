import 'dart:math' as math;
import 'dart:typed_data';

/// Sample rate the whole gunshot pipeline works at (YAMNet's input rate).
const int kGunshotSampleRate = 16000;

/// 20 ms analysis frames.
const int kGunshotFrameSamples = 320;

/// Converts little-endian 16-bit PCM bytes to floats in [-1, 1].
Float32List pcm16ToFloat(Uint8List bytes) {
  final data = ByteData.sublistView(bytes);
  final out = Float32List(bytes.length ~/ 2);
  for (var i = 0; i < out.length; i++) {
    out[i] = data.getInt16(i * 2, Endian.little) / 32768.0;
  }
  return out;
}

double toDbfs(double amplitude) =>
    20 * math.log(math.max(amplitude, 1e-9)) / math.ln10;

/// A sudden loud sound found by [OnsetGate].
class Onset {
  const Onset({required this.sampleIndex, required this.peakDbfs, required this.riseDb});

  /// Absolute index (since the stream started) of the first sample of the onset frame.
  final int sampleIndex;
  final double peakDbfs;

  /// How far this frame's energy jumped above the recent background.
  final double riseDb;
}

/// The cheap first stage of gunshot detection, run on every 20 ms frame.
///
/// A gunshot is an impulse: within a few milliseconds the level jumps far above
/// whatever came before. Wind, talking and engines rise slowly or stay loud,
/// so requiring both a loud peak AND a big jump over the last ~200 ms lets the
/// neural network sleep almost all of the time.
class OnsetGate {
  OnsetGate({
    this.minPeakDbfs = -30,
    this.minRiseDb = 18,
    this.historyFrames = 10,
    this.refractoryFrames = 15,
  });

  /// The frame's loudest sample must reach this level.
  double minPeakDbfs;

  /// The frame's energy must be this many dB above the recent background.
  final double minRiseDb;

  /// Background window: 10 frames = 200 ms.
  final int historyFrames;

  /// After a trigger, ignore new onsets for this many frames (300 ms) - the
  /// same bang keeps ringing for a while.
  final int refractoryFrames;

  final List<double> _history = [];
  int _cooldown = 0;

  void reset() {
    _history.clear();
    _cooldown = 0;
  }

  /// Analyses one frame that starts at absolute sample [startSample]; returns
  /// an [Onset] when this frame is the start of an impulse.
  Onset? addFrame(Float32List frame, int startSample) {
    var peak = 0.0;
    var sumSquares = 0.0;
    for (final s in frame) {
      final a = s.abs();
      if (a > peak) peak = a;
      sumSquares += s * s;
    }
    final rms = math.sqrt(sumSquares / math.max(1, frame.length));
    final rmsDb = toDbfs(rms);

    Onset? onset;
    if (_cooldown > 0) {
      _cooldown--;
    } else if (_history.length >= historyFrames ~/ 2) {
      final background = _history.reduce((a, b) => a + b) / _history.length;
      final rise = rmsDb - toDbfs(background);
      final peakDb = toDbfs(peak);
      if (peakDb >= minPeakDbfs && rise >= minRiseDb) {
        onset = Onset(sampleIndex: startSample, peakDbfs: peakDb, riseDb: rise);
        _cooldown = refractoryFrames;
      }
    }

    _history.add(rms);
    if (_history.length > historyFrames) _history.removeAt(0);
    return onset;
  }
}

/// Keeps the last [capacity] samples so the audio just BEFORE an onset can be
/// handed to the classifier. Audio only ever lives here, in memory.
class AudioRingBuffer {
  AudioRingBuffer(this.capacity) : _data = Float32List(capacity);

  final int capacity;
  final Float32List _data;
  int _written = 0;

  /// Total samples ever written (absolute index of the next sample).
  int get totalWritten => _written;

  void addAll(Float32List samples) {
    for (final s in samples) {
      _data[_written % capacity] = s;
      _written++;
    }
  }

  /// Copies [length] samples starting at absolute index [start]; null when
  /// that range is not (or no longer) in the buffer.
  Float32List? window(int start, int length) {
    if (start < 0 || length > capacity) return null;
    if (start < _written - capacity || start + length > _written) return null;
    final out = Float32List(length);
    for (var i = 0; i < length; i++) {
      out[i] = _data[(start + i) % capacity];
    }
    return out;
  }

  void clear() {
    _written = 0;
    _data.fillRange(0, capacity, 0);
  }
}
