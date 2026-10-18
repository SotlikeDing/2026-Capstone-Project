import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';

import 'gunshot_settings.dart';
import 'onset_gate.dart';
import 'personal_head.dart';
import 'shot_classifier.dart';
import 'shot_event_merger.dart';

/// Live counters for the settings sheet / debugging. No audio.
class GunshotStats {
  int onsets = 0;
  int classified = 0;
  int accepted = 0;
  double? lastScore;
  String? lastLabel;
}

/// Audio in, shot events out. Pure Dart: the microphone, the foreground
/// service and the TFLite model are injected/wrapped elsewhere, so this whole
/// path is unit-tested with synthetic audio and a fake classifier.
///
///   PCM16 -> 20 ms frames -> OnsetGate -> (wait for the rest of the window)
///         -> classifier -> personal head -> near/far filter -> ShotEventMerger
class GunshotPipeline {
  GunshotPipeline({
    required ShotClassifier classifier,
    required PersonalHead head,
    required GunshotSettings settings,
    DateTime Function()? clock,
  }) : _classifier = classifier,
       _head = head,
       _settings = settings,
       _clock = clock ?? DateTime.now,
       _gate = OnsetGate(minPeakDbfs: settings.sensitivity.gatePeakDbfs);

  /// Audio kept before an onset so the start of the bang is in the window.
  static const int preRollSamples = kGunshotSampleRate ~/ 5; // 200 ms

  /// At most this many onsets wait for classification; older ones are dropped
  /// (a burst of shots is one event anyway).
  static const int maxPending = 3;

  final ShotClassifier _classifier;
  final PersonalHead _head;
  GunshotSettings _settings;
  final DateTime Function() _clock;
  final OnsetGate _gate;
  final AudioRingBuffer _ring = AudioRingBuffer(kGunshotSampleRate * 2);
  final ShotEventMerger _merger = ShotEventMerger();
  final StreamController<ShotEventUpdate> _events = StreamController<ShotEventUpdate>.broadcast();
  final List<({Onset onset, DateTime at})> _pending = [];
  final List<double> _frame = [];
  final GunshotStats stats = GunshotStats();

  /// The classification currently running, so [close] can wait for it before
  /// the model is freed.
  Future<ShotClassification>? _inFlight;
  bool _closed = false;

  Stream<ShotEventUpdate> get events => _events.stream;
  GunshotSettings get settings => _settings;

  void updateSettings(GunshotSettings settings) {
    _settings = settings;
    _gate.minPeakDbfs = settings.sensitivity.gatePeakDbfs;
  }

  /// Feed raw little-endian 16-bit mono PCM at 16 kHz, as the recorder delivers it.
  void addPcm16(Uint8List bytes) => addSamples(pcm16ToFloat(bytes));

  void addSamples(Float32List samples) {
    if (_closed) return;
    final startIndex = _ring.totalWritten;
    _ring.addAll(samples);
    for (var i = 0; i < samples.length; i++) {
      _frame.add(samples[i]);
      if (_frame.length == kGunshotFrameSamples) {
        final frameStart = startIndex + i + 1 - kGunshotFrameSamples;
        final onset = _gate.addFrame(Float32List.fromList(_frame), frameStart);
        _frame.clear();
        if (onset != null) {
          stats.onsets++;
          _pending.add((onset: onset, at: _clock()));
          if (_pending.length > maxPending) _pending.removeAt(0);
        }
      }
    }
    _drain();
  }

  /// Classifies pending onsets whose full window has arrived, one at a time.
  Future<void> _drain() async {
    if (_closed || _inFlight != null || _pending.isEmpty) return;
    final next = _pending.first;
    // An onset in the first 200 ms of listening has less pre-roll: the window
    // then starts at the first sample, and readiness is judged on that start.
    final start = math.max(0, next.onset.sampleIndex - preRollSamples);
    if (_ring.totalWritten < start + kYamnetWindowSamples) return; // rest of the window not here yet
    _pending.removeAt(0);
    final window = _ring.window(start, kYamnetWindowSamples);
    if (window == null) {
      unawaited(_drain()); // overwritten by newer audio (classification fell far behind)
      return;
    }

    final classification = _classifier.classify(window);
    _inFlight = classification;
    try {
      final result = await classification;
      stats
        ..classified += 1
        ..lastScore = result.gunScore
        ..lastLabel = result.topLabel;
      _consider(next.onset, next.at, result);
    } catch (e) {
      debugPrint('[GunshotPipeline] Classification failed: $e');
    } finally {
      _inFlight = null;
    }
    unawaited(_drain());
  }

  void _consider(Onset onset, DateTime at, ShotClassification result) {
    final threshold = _settings.sensitivity.modelThreshold;
    final confidence = _head.blend(scoreToConfidence(result.gunScore, threshold), result.embedding);
    if (confidence < 0.5) return;

    final near = onset.peakDbfs >= GunshotCalibration.nearPeakDbfs;
    if (!near && !_settings.pinDistant) return;

    stats.accepted++;
    if (_closed) return;
    _events.add(_merger.add(ShotCandidate(
      at: at,
      peakDbfs: onset.peakDbfs,
      confidence: confidence,
      near: near,
      embedding: result.embedding,
    )));
  }

  /// See [ShotEventMerger.endEvent].
  void endEvent(String eventId) => _merger.endEvent(eventId);

  /// Forget buffered audio (e.g. when listening restarts).
  void reset() {
    _ring.clear();
    _gate.reset();
    _frame.clear();
    _pending.clear();
    _merger.reset();
  }

  /// Stops accepting audio and waits for a running classification, so the
  /// caller may free the model afterwards.
  Future<void> close() async {
    if (_closed) return;
    _closed = true;
    reset();
    try {
      await _inFlight;
    } catch (_) {} // already logged in _drain
    await _events.close();
  }
}
