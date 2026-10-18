import 'dart:async';
import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:jagbestuur_app/models/hunt_pin.dart';
import 'package:jagbestuur_app/models/hunt_session.dart';
import 'package:jagbestuur_app/models/hunter_statistics.dart';
import 'package:jagbestuur_app/services/gunshot/gunshot_pipeline.dart';
import 'package:jagbestuur_app/services/gunshot/gunshot_settings.dart';
import 'package:jagbestuur_app/services/gunshot/onset_gate.dart';
import 'package:jagbestuur_app/services/gunshot/personal_head.dart';
import 'package:jagbestuur_app/services/gunshot/shot_classifier.dart';
import 'package:jagbestuur_app/services/gunshot/shot_event_merger.dart';
import 'package:jagbestuur_app/services/gunshot/yamnet_classifier.dart';

const int sr = kGunshotSampleRate;

/// Background noise at a fixed level.
Float32List noise(double seconds, double amplitude, math.Random rng) =>
    Float32List.fromList(List.generate((seconds * sr).round(), (_) => (rng.nextDouble() * 2 - 1) * amplitude));

/// A gunshot-like impulse: instant attack, ~50 ms exponential decay.
Float32List bang(double seconds, double amplitude, math.Random rng) => Float32List.fromList(
  List.generate((seconds * sr).round(), (i) => (rng.nextDouble() * 2 - 1) * amplitude * math.exp(-(i / sr) / 0.05)),
);

Float32List concat(List<Float32List> parts) {
  final out = Float32List(parts.fold(0, (n, p) => n + p.length));
  var o = 0;
  for (final p in parts) {
    out.setAll(o, p);
    o += p.length;
  }
  return out;
}

List<Onset> onsetsIn(Float32List audio, OnsetGate gate) {
  final found = <Onset>[];
  for (var i = 0; i + kGunshotFrameSamples <= audio.length; i += kGunshotFrameSamples) {
    final o = gate.addFrame(Float32List.sublistView(audio, i, i + kGunshotFrameSamples), i);
    if (o != null) found.add(o);
  }
  return found;
}

/// Classification that only finishes when the test says so.
class SlowClassifier implements ShotClassifier {
  final Completer<void> release = Completer<void>();
  bool running = false;
  bool closed = false;

  @override
  Future<ShotClassification> classify(Float32List waveform) async {
    running = true;
    await release.future;
    running = false;
    if (closed) throw StateError('classify ran on a closed model');
    return ShotClassification(gunScore: 0.9, embedding: Float32List(4), topLabel: 'Gunshot, gunfire');
  }

  @override
  Future<void> close() async => closed = true;
}

class FakeClassifier implements ShotClassifier {
  FakeClassifier(this.score);
  double score;
  int calls = 0;

  @override
  Future<ShotClassification> classify(Float32List waveform) async {
    calls++;
    expect(waveform.length, kYamnetWindowSamples);
    return ShotClassification(gunScore: score, embedding: Float32List(4)..[0] = score, topLabel: 'Gunshot, gunfire');
  }

  @override
  Future<void> close() async {}
}

void main() {
  group('PCM and onset gate', () {
    test('16-bit PCM becomes floats in [-1, 1]', () {
      final bytes = Uint8List(6);
      ByteData.sublistView(bytes)
        ..setInt16(0, 32767, Endian.little)
        ..setInt16(2, -32768, Endian.little)
        ..setInt16(4, 0, Endian.little);
      final f = pcm16ToFloat(bytes);
      expect(f[0], closeTo(1, 1e-4));
      expect(f[1], -1);
      expect(f[2], 0);
    });

    test('a sudden bang after quiet bush triggers once, at the bang', () {
      final rng = math.Random(1);
      final audio = concat([noise(1, 0.003, rng), bang(0.5, 0.9, rng), noise(1, 0.003, rng)]);
      final onsets = onsetsIn(audio, OnsetGate());
      expect(onsets, hasLength(1));
      expect(onsets.single.sampleIndex, closeTo(sr, kGunshotFrameSamples));
      expect(onsets.single.peakDbfs, greaterThan(-3));
    });

    test('steady loud wind or an engine does not trigger', () {
      final rng = math.Random(2);
      expect(onsetsIn(noise(3, 0.3, rng), OnsetGate()), isEmpty);
    });

    test('a slow rise (vehicle approaching) does not trigger', () {
      final rng = math.Random(3);
      final ramp = Float32List.fromList(
        List.generate(3 * sr, (i) => (rng.nextDouble() * 2 - 1) * (0.002 + 0.5 * i / (3 * sr))),
      );
      expect(onsetsIn(ramp, OnsetGate()), isEmpty);
    });

    test('a faint click below the level threshold is ignored, but found on high sensitivity', () {
      final rng = math.Random(4);
      final audio = concat([noise(1, 0.0003, rng), bang(0.3, 0.02, rng), noise(0.5, 0.0003, rng)]); // ~ -34 dBFS peak
      expect(onsetsIn(audio, OnsetGate(minPeakDbfs: GunshotSensitivity.medium.gatePeakDbfs)), isEmpty);
      expect(onsetsIn(audio, OnsetGate(minPeakDbfs: GunshotSensitivity.high.gatePeakDbfs)), hasLength(1));
    });

    test('ring buffer returns the audio before an onset and refuses ranges it no longer has', () {
      final ring = AudioRingBuffer(100);
      ring.addAll(Float32List.fromList(List.generate(250, (i) => i.toDouble())));
      expect(ring.window(200, 10)!.first, 200);
      expect(ring.window(100, 10), isNull); // overwritten
      expect(ring.window(245, 10), isNull); // not recorded yet
    });
  });

  group('Shot event merger', () {
    ShotCandidate c(int seconds, {bool near = true, double conf = 0.7}) => ShotCandidate(
      at: DateTime(2026, 6, 1, 7, 0, seconds),
      peakDbfs: near ? -1 : -25,
      confidence: conf,
      near: near,
      embedding: Float32List(4)..[0] = conf,
    );

    test('echoes and follow-up shots within 4 s become one event with a shot count', () {
      final m = ShotEventMerger();
      final first = m.add(c(0));
      final second = m.add(c(3, conf: 0.9));
      expect(first.isNew, isTrue);
      expect(second.isNew, isFalse);
      expect(second.event.id, first.event.id);
      expect(second.event.shots, 2);
      expect(second.event.confidence, 0.9);
      expect(second.event.embedding[0], closeTo(0.9, 1e-6));
      expect(second.event.note, 'Outomaties: 2 skote');
    });

    test('a shot 5 s after the last one is a new event; distant shots are labelled', () {
      final m = ShotEventMerger();
      m.add(c(0));
      final later = m.add(c(5, near: false));
      expect(later.isNew, isTrue);
      expect(later.event.note, 'Outomaties: 1 skoot (ver)');
    });

    test('after the event pin is removed, the next shot gets its own event', () {
      final m = ShotEventMerger();
      final first = m.add(c(0));
      m.endEvent('some-other-event'); // only the current event is ended
      expect(m.add(c(1)).isNew, isFalse);
      m.endEvent(first.event.id);
      final next = m.add(c(2));
      expect(next.isNew, isTrue);
      expect(next.event.shots, 1);
    });
  });

  group('Personal head', () {
    Float32List vec(double a, double b) => Float32List.fromList([a, b, 0.5]);

    test('score mapping puts the threshold at exactly 0.5 and stays monotonic', () {
      expect(scoreToConfidence(0.2, 0.2), 0.5);
      expect(scoreToConfidence(0.1, 0.2), lessThan(0.5));
      expect(scoreToConfidence(0.6, 0.2), greaterThan(scoreToConfidence(0.3, 0.2)));
      expect(scoreToConfidence(1, 0.2), 1);
    });

    test('does nothing until it has 20 labels with at least 5 of each', () {
      final head = PersonalHead();
      for (var i = 0; i < 19; i++) {
        head.add(vec(1, 0), isGunshot: i.isEven);
      }
      expect(PersonalHead.train(head.samples), isNull);
      head.add(vec(1, 0), isGunshot: true);
      final allPositive = PersonalHead();
      for (var i = 0; i < 30; i++) {
        allPositive.add(vec(1, 0), isGunshot: i < 27);
      }
      expect(PersonalHead.train(allPositive.samples), isNull); // only 3 removed
      expect(head.blend(0.4, vec(1, 0)), 0.4);
    });

    test('learns the hunter\'s rifle vs. the truck door and takes a growing share', () {
      final rng = math.Random(7);
      final head = PersonalHead();
      for (var i = 0; i < 30; i++) {
        head.add(vec(1 + rng.nextDouble() * 0.1, rng.nextDouble() * 0.1), isGunshot: true); // rifle
        head.add(vec(rng.nextDouble() * 0.1, 1 + rng.nextDouble() * 0.1), isGunshot: false); // door
      }
      final model = PersonalHead.train(head.samples);
      expect(model, isNotNull);
      head.setModel(model);
      expect(head.share, closeTo(60 / 110, 1e-9));
      // A borderline YAMNet score: the head pushes the rifle up and the door down.
      expect(head.blend(0.5, vec(1.05, 0.05)), greaterThan(0.7));
      expect(head.blend(0.5, vec(0.05, 1.05)), lessThan(0.3));
    });

    test('labels survive a JSON round trip', () {
      final s = LabelledEmbedding([0.123456, 2], 1);
      final back = LabelledEmbedding.fromJson(s.toJson());
      expect(back.label, 1);
      expect(back.embedding.first, closeTo(0.1235, 1e-9));
    });
  });

  group('Pipeline (fake classifier)', () {
    late DateTime now;
    late List<ShotEventUpdate> events;

    GunshotPipeline make(FakeClassifier classifier, {GunshotSettings settings = const GunshotSettings(enabled: true)}) {
      now = DateTime(2026, 6, 1, 7);
      events = [];
      final p = GunshotPipeline(classifier: classifier, head: PersonalHead(), settings: settings, clock: () => now);
      p.events.listen(events.add);
      return p;
    }

    /// Feeds audio in 100 ms chunks, like the recorder does, advancing the clock.
    Future<void> feed(GunshotPipeline p, Float32List audio) async {
      for (var i = 0; i < audio.length; i += 1600) {
        p.addSamples(Float32List.sublistView(audio, i, math.min(audio.length, i + 1600)));
        now = now.add(const Duration(milliseconds: 100));
        await pumpEventQueue();
      }
    }

    test('a close shot becomes a near event; a second shot 1 s later updates it', () async {
      final rng = math.Random(11);
      final classifier = FakeClassifier(0.6);
      final p = make(classifier);
      await feed(p, concat([noise(1, 0.003, rng), bang(0.4, 0.95, rng), noise(0.6, 0.003, rng), bang(0.4, 0.95, rng), noise(1.5, 0.003, rng)]));

      expect(classifier.calls, 2);
      expect(events, hasLength(2));
      expect(events[0].isNew, isTrue);
      expect(events[0].event.near, isTrue);
      expect(events[1].isNew, isFalse);
      expect(events[1].event.shots, 2);
      expect(events[0].event.confidence, greaterThan(0.5));
      expect(p.stats.accepted, 2);
    });

    test('a bang the model does not think is a gun gives no pin', () async {
      final rng = math.Random(12);
      final classifier = FakeClassifier(GunshotCalibration.mediumThreshold / 2);
      final p = make(classifier);
      await feed(p, concat([noise(1, 0.003, rng), bang(0.4, 0.95, rng), noise(1.5, 0.003, rng)]));
      expect(classifier.calls, 1);
      expect(events, isEmpty);
    });

    test('a distant shot is ignored by default and pinned as "ver" when asked for', () async {
      final rng = math.Random(13);
      final audio = concat([noise(1, 0.0005, rng), bang(0.4, 0.06, rng), noise(1.5, 0.0005, rng)]); // ~ -25 dBFS
      final quiet = make(FakeClassifier(0.6));
      await feed(quiet, audio);
      expect(events, isEmpty);

      final distant = make(FakeClassifier(0.6), settings: const GunshotSettings(enabled: true, pinDistant: true));
      await feed(distant, audio);
      expect(events.single.event.near, isFalse);
      expect(events.single.event.note, contains('(ver)'));
    });

    test('a shot in the first 200 ms of listening is still classified', () async {
      final rng = math.Random(15);
      final classifier = FakeClassifier(0.6);
      final p = make(classifier);
      // Onset ~120 ms in: less than the 200 ms pre-roll exists before it.
      await feed(p, concat([noise(0.12, 0.003, rng), bang(0.4, 0.95, rng), noise(1.5, 0.003, rng)]));
      expect(classifier.calls, 1);
      expect(events.single.isNew, isTrue);
    });

    test('close waits for a running classification before the model may be freed', () async {
      final rng = math.Random(16);
      final classifier = SlowClassifier();
      final p = GunshotPipeline(classifier: classifier, head: PersonalHead(), settings: const GunshotSettings(enabled: true));
      p.addSamples(concat([noise(1, 0.003, rng), bang(0.4, 0.95, rng), noise(1, 0.003, rng)]));
      await pumpEventQueue();
      expect(classifier.running, isTrue);

      var closed = false;
      final closing = p.close().then((_) => closed = true);
      await pumpEventQueue();
      expect(closed, isFalse, reason: 'close must not finish while classify runs');

      classifier.release.complete();
      await closing;
      await classifier.close(); // what the detector service does next
      expect(classifier.running, isFalse);
    });

    test('quiet bush never wakes the model', () async {
      final classifier = FakeClassifier(0.9);
      final p = make(classifier);
      await feed(p, noise(3, 0.003, math.Random(14)));
      expect(classifier.calls, 0);
      expect(events, isEmpty);
    });
  });

  group('Model assets and saved pins', () {
    test('class map names are read by name, including quoted names with commas', () {
      const csv = 'index,mid,display_name\n0,/m/09x0r,Speech\n421,/m/032s66,"Gunshot, gunfire"\n';
      expect(YamnetClassifier.parseClassMap(csv), ['Speech', 'Gunshot, gunfire']);
    });

    test('pins saved before Outo-skoot still load, as manual pins', () {
      final pin = HuntPin.fromJson({
        'id': '1',
        'type': 'shotFired',
        'latitude': -24.5,
        'longitude': 28.1,
        'timestamp': '2026-06-01T05:00:00Z',
        'note': null,
        'photoPath': null,
      });
      expect(pin.autoDetected, isFalse);
      expect(pin.confirmed, isFalse);
      expect(pin.needsReview, isFalse);
      expect(pin.toJson().containsKey('autoDetected'), isFalse);
    });

    test('automatic pins keep their flags through save and confirm', () {
      final pin = HuntPin(
        id: '2',
        type: HuntPinType.shotFired,
        latitude: -24.5,
        longitude: 28.1,
        timestamp: DateTime.utc(2026, 6, 1, 5),
        note: 'Outomaties: 1 skoot',
        autoDetected: true,
        confidence: 0.82,
      );
      final back = HuntPin.fromJson(pin.toJson());
      expect(back.needsReview, isTrue);
      expect(back.confidence, 0.82);
      final confirmed = back.copyWith(confirmed: true);
      expect(confirmed.needsReview, isFalse);
      expect(HuntPin.fromJson(confirmed.toJson()).confirmed, isTrue);
    });

    test('shot statistics count manual and confirmed pins, not unreviewed automatic ones', () {
      HuntPin pin(String id, {bool auto = false, bool confirmed = false}) => HuntPin(
        id: id,
        type: HuntPinType.shotFired,
        latitude: -24.5,
        longitude: 28.1,
        timestamp: DateTime(2026, 6, 1, 7),
        autoDetected: auto,
        confirmed: confirmed,
      );
      final hunt = HuntSession(
        id: 'h',
        startedAt: DateTime(2026, 6, 1, 6),
        endedAt: DateTime(2026, 6, 1, 9),
        totalDistanceMeters: 1000,
        waypoints: const [],
        pins: [pin('manual'), pin('confirmed', auto: true, confirmed: true), pin('unreviewed', auto: true)],
      );
      final stats = HunterStatistics.calculate(trophies: const [], hunts: [hunt], now: DateTime(2026, 10, 8));
      expect(stats.shotsMarked, 2);
    });
  });
}
