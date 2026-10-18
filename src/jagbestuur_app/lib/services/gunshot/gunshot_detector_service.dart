import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:record/record.dart';

import '../user_storage.dart';
import 'gunshot_pipeline.dart';
import 'gunshot_settings.dart';
import 'mic_foreground_service.dart';
import 'onset_gate.dart';
import 'personal_head.dart';
import 'shot_classifier.dart';
import 'shot_event_merger.dart';
import 'yamnet_classifier.dart';

/// Listens for gunshots while a hunt runs: microphone -> [GunshotPipeline] ->
/// [events]. Owns the recorder, the foreground service, the TFLite model and the
/// hunter's personal head (stored on the device). Audio is never written to disk.
class GunshotDetectorService {
  GunshotDetectorService();

  /// True while the microphone is open for shot detection.
  final ValueNotifier<bool> listening = ValueNotifier<bool>(false);

  /// True when listening also survives the screen turning off (the microphone
  /// foreground service is running). False while [listening] means detection
  /// stops as soon as the phone is locked.
  final ValueNotifier<bool> screenOffListening = ValueNotifier<bool>(false);

  final StreamController<ShotEventUpdate> _events = StreamController<ShotEventUpdate>.broadcast();
  final PersonalHead head = PersonalHead();

  AudioRecorder? _recorder;
  StreamSubscription<Uint8List>? _audioSub;
  StreamSubscription<ShotEventUpdate>? _pipelineSub;
  GunshotPipeline? _pipeline;
  ShotClassifier? _classifier;

  /// start / stop / dispose run one at a time, in call order.
  Future<void> _lifecycle = Future<void>.value();

  /// Bumped by [stop] and [dispose] the moment they are called: a [start] that
  /// is still waiting (permission dialog, model load) sees the change and gives
  /// up instead of opening the microphone after the hunt has ended.
  int _generation = 0;
  bool _disposed = false;

  Future<void>? _headLoad;

  /// Bumped on every change to the head's samples; a training pass only
  /// installs its model when the samples did not change while it ran.
  int _headVersion = 0;
  Future<void>? _training;
  Future<void> _saving = Future<void>.value();

  Stream<ShotEventUpdate> get events => _events.stream;
  GunshotStats? get stats => _pipeline?.stats;

  Future<T> _serialized<T>(Future<T> Function() action) {
    final result = _lifecycle.then((_) => action());
    _lifecycle = result.then<void>((_) {}, onError: (_) {});
    return result;
  }

  /// Opens the microphone. Returns an Afrikaans message for the hunter when
  /// listening could not start (no permission, model failed to load, microphone
  /// busy) or only works while the screen is on; null when all is well or when
  /// a [stop] cancelled this start. Calling it while already listening only
  /// updates the settings. Must be called while the app is visible (Android 14
  /// microphone rule).
  Future<String?> start(GunshotSettings settings) {
    final generation = _generation;
    return _serialized(() => _start(settings, generation));
  }

  Future<String?> _start(GunshotSettings settings, int generation) async {
    bool cancelled() => _disposed || generation != _generation;
    if (cancelled()) return null;
    if (listening.value) {
      _pipeline?.updateSettings(settings);
      return null;
    }

    final recorder = _recorder ??= AudioRecorder();
    final permitted = await recorder.hasPermission();
    if (cancelled()) return null;
    if (!permitted) return 'Mikrofoon-toestemming is nodig vir Outo-skoot.';

    try {
      _classifier ??= await YamnetClassifier.load();
    } catch (e, stack) {
      debugPrint('[GunshotDetector] Model load failed: $e\n$stack');
      return cancelled() ? null : 'Die skoot-herkenningsmodel kon nie laai nie.';
    }
    await loadLearning();
    if (cancelled()) return null;

    final screenOff = await MicForegroundService.start();
    if (cancelled()) {
      await MicForegroundService.stop();
      return null;
    }

    final Stream<Uint8List> stream;
    try {
      stream = await _openStream(recorder);
    } catch (e) {
      await MicForegroundService.stop();
      debugPrint('[GunshotDetector] Microphone failed: $e');
      return cancelled() ? null : 'Kon nie die mikrofoon oopmaak nie (word dit dalk deur \'n ander app gebruik?).';
    }
    if (cancelled()) {
      try {
        await recorder.stop();
      } catch (_) {}
      await MicForegroundService.stop();
      return null;
    }

    final pipeline = GunshotPipeline(classifier: _classifier!, head: head, settings: settings);
    _pipeline = pipeline;
    _pipelineSub = pipeline.events.listen(_events.add);
    _audioSub = stream.listen(
      pipeline.addPcm16,
      onError: (Object e) => debugPrint('[GunshotDetector] Audio stream error: $e'),
    );
    screenOffListening.value = screenOff;
    listening.value = true;
    return screenOff
        ? null
        : 'Outo-skoot luister net terwyl die skerm aan is - die agtergronddiens kon nie begin nie '
              '(laat kennisgewings vir Jagbestuur toe).';
  }

  /// Phones squash a bang with automatic gain and noise suppression, so ask for
  /// the unprocessed microphone first and fall back when a phone does not offer it.
  Future<Stream<Uint8List>> _openStream(AudioRecorder recorder) async {
    Object? lastError;
    for (final source in [AndroidAudioSource.unprocessed, AndroidAudioSource.voiceRecognition, AndroidAudioSource.mic]) {
      try {
        return await recorder.startStream(RecordConfig(
          encoder: AudioEncoder.pcm16bits,
          sampleRate: kGunshotSampleRate,
          numChannels: 1,
          autoGain: false,
          echoCancel: false,
          noiseSuppress: false,
          androidConfig: AndroidRecordConfig(audioSource: source),
        ));
      } catch (e) {
        lastError = e;
        try {
          await recorder.stop();
        } catch (_) {}
      }
    }
    throw StateError('Geen mikrofoonbron beskikbaar nie: $lastError');
  }

  void updateSettings(GunshotSettings settings) => _pipeline?.updateSettings(settings);

  /// The pin of [eventId] is gone: later shots start a new event (and pin).
  void endEvent(String eventId) => _pipeline?.endEvent(eventId);

  /// Closes the microphone, also cancelling a [start] that is still under way.
  Future<void> stop() {
    _generation++;
    return _serialized(_stop);
  }

  Future<void> _stop() async {
    await _audioSub?.cancel();
    _audioSub = null;
    try {
      await _recorder?.stop();
    } catch (_) {}
    await _pipelineSub?.cancel();
    _pipelineSub = null;
    await _pipeline?.close(); // waits for a running classification
    _pipeline = null;
    await MicForegroundService.stop();
    listening.value = false;
    screenOffListening.value = false;
  }

  // ------------------------------------------------------------------ learning

  /// Reads the hunter's saved labels (once). Called before showing the
  /// learning state, so it is right before the first listening hunt too.
  Future<void> loadLearning() => _headLoad ??= _readHead();

  /// The hunter confirmed ([isGunshot] true) or removed (false) an automatic pin.
  Future<void> label(Float32List embedding, {required bool isGunshot}) async {
    await loadLearning();
    head.add(embedding, isGunshot: isGunshot);
    _headVersion++;
    _retrain();
    await _saveHead();
  }

  Future<void> forgetLearning() async {
    await loadLearning();
    head.clear();
    _headVersion++; // a training pass still running must not bring the model back
    await _saveHead();
  }

  /// At most one training pass runs; when the samples changed while it ran,
  /// its model is thrown away and one new pass trains on the latest samples.
  void _retrain() {
    if (_training != null) return;
    final version = _headVersion;
    _training = compute(_trainIsolate, head.samples)
        .then<void>(
          (model) {
            if (version == _headVersion) head.setModel(model);
          },
          onError: (Object e) => debugPrint('[GunshotDetector] Training failed: $e'),
        )
        .whenComplete(() {
          _training = null;
          if (version != _headVersion) _retrain();
        });
  }

  /// Completes when no training pass is running (tests / before showing stats).
  Future<void> get trainingDone async {
    while (_training != null) {
      await _training;
    }
  }

  static Future<File> _headFile() async {
    final dir = await UserStorage.documentsDir('gunshot_learning');
    return File('${dir.path}${Platform.pathSeparator}labels.json');
  }

  Future<void> _readHead() async {
    try {
      final file = await _headFile();
      if (!await file.exists()) return;
      final list = (jsonDecode(await file.readAsString()) as List)
          .map((e) => LabelledEmbedding.fromJson(e as Map<String, dynamic>));
      head.clear();
      for (final s in list) {
        head.add(Float32List.fromList(s.embedding), isGunshot: s.label == 1);
      }
      _headVersion++;
      _retrain();
    } catch (e) {
      debugPrint('[GunshotDetector] Could not read learning data: $e');
    }
  }

  /// Writes one at a time (two quick labels must not interleave in the file);
  /// each write saves the samples as they are when it runs.
  Future<void> _saveHead() {
    final write = _saving.then((_) async {
      try {
        final file = await _headFile();
        await file.writeAsString(jsonEncode(head.samples.map((s) => s.toJson()).toList()), flush: true);
      } catch (e) {
        debugPrint('[GunshotDetector] Could not save learning data: $e');
      }
    });
    _saving = write;
    return write;
  }

  Future<void> dispose() {
    _generation++;
    _disposed = true;
    return _serialized(() async {
      await _stop(); // closes the pipeline, so no classification is running below
      await _recorder?.dispose();
      _recorder = null;
      await _classifier?.close();
      _classifier = null;
      await _events.close();
      listening.dispose();
      screenOffListening.dispose();
    });
  }
}

LogisticWeights? _trainIsolate(List<LabelledEmbedding> samples) => PersonalHead.train(samples);
