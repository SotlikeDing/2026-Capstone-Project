import 'dart:math' as math;

import 'package:intl/intl.dart';

import '../models/hunt_photo.dart';
import '../models/hunt_pin.dart';
import '../models/smart_waypoint.dart';
import '../services/hunt_photo_matcher.dart';

/// Which screen of the recap a moment belongs to.
enum RecapPhase { intro, map, summary }

/// Why a moment is shown - drives the caption icon and wording.
enum RecapEventKind {
  start,
  travel,
  rest,
  manualPin,
  autoShot,
  autoShotConfirmed,
  photo,
  finish,
  overview,
}

class RecapCaption {
  const RecapCaption({
    required this.kind,
    required this.title,
    required this.detail,
    this.pinType,
  });

  final RecapEventKind kind;
  final String title;
  final String detail;
  final HuntPinType? pinType;
}

/// Everything needed to draw one instant of the recap.
class RecapFrame {
  const RecapFrame({
    required this.phase,
    required this.position,
    required this.caption,
    this.photo,
    this.photoOpacity = 0,
    this.captionOpacity = 1,
    this.overview = 0,
  });

  final RecapPhase phase;

  /// Fractional keyframe index: 3.25 is a quarter of the way from point 3 to 4.
  final double position;
  final RecapCaption? caption;

  /// Shown full screen over the map while [photoOpacity] > 0.
  final HuntPhoto? photo;
  final double photoOpacity;
  final double captionOpacity;

  /// 0 = camera follows the hunter, 1 = whole route in view.
  final double overview;

  /// Two frames with the same key render identically, so the exporter can
  /// reuse the previous capture instead of drawing it again.
  String get renderKey =>
      '${phase.name}|${position.toStringAsFixed(4)}|${photo?.id}|'
      '${photoOpacity.toStringAsFixed(2)}|${captionOpacity.toStringAsFixed(2)}|'
      '${overview.toStringAsFixed(3)}|${caption?.title}|${caption?.detail}';
}

enum _BeatKind { intro, hold, travel, overview, summary }

class _Beat {
  _Beat.intro(this.seconds)
    : kind = _BeatKind.intro,
      from = 0,
      to = 0,
      caption = null,
      photo = null;

  _Beat.summary(this.seconds, int last)
    : kind = _BeatKind.summary,
      from = last,
      to = last,
      caption = null,
      photo = null;

  _Beat.overview(this.seconds, int last, this.caption)
    : kind = _BeatKind.overview,
      from = last,
      to = last,
      photo = null;

  _Beat.hold(this.seconds, int index, this.caption, {this.photo})
    : kind = _BeatKind.hold,
      from = index,
      to = index;

  _Beat.travel(this.seconds, this.from, this.to)
    : kind = _BeatKind.travel,
      caption = null,
      photo = null;

  final _BeatKind kind;
  final double seconds;
  final int from;
  final int to;
  final RecapCaption? caption;
  final HuntPhoto? photo;
  double start = 0;
}

/// The script of a hunt recap: an intro card, the route drawn at a steady,
/// eased pace with pauses at every logged moment, a zoom-out over the whole
/// route and a summary card. The live preview and the video export both read
/// from it, so what plays on screen is exactly what gets exported.
class RecapTimeline {
  RecapTimeline._(this.keyframes, this._beats, this._cumulativeMeters)
    : totalSeconds = _beats.isEmpty
          ? 0
          : _beats.last.start + _beats.last.seconds;

  static const double introSeconds = 3;
  static const double startHoldSeconds = 2.2;
  static const double restHoldSeconds = 2.4;
  static const double pinHoldSeconds = 3;
  static const double finishHoldSeconds = 2.4;
  static const double overviewSeconds = 3.5;
  static const double summarySeconds = 4;
  static const double photoFadeSeconds = 0.45;
  static const double captionFadeSeconds = 0.3;

  /// Average time spent travelling between two route points, before the
  /// route-wide limits below are applied.
  static const double secondsPerSegment = 0.8;
  static const double minTravelSeconds = 10;
  static const double maxTravelSeconds = 60;

  /// Time to speed up from a standstill and slow down again around a pause.
  static const double easeSeconds = 0.9;

  final List<SmartWaypoint> keyframes;
  final List<_Beat> _beats;
  final List<double> _cumulativeMeters;
  final double totalSeconds;

  static final DateFormat _clock = DateFormat('HH:mm');

  double get totalMeters =>
      _cumulativeMeters.isEmpty ? 0 : _cumulativeMeters.last;

  /// Where the map part starts (after the intro card).
  double get mapStartSeconds => _beats.length > 1 ? _beats[1].start : 0;

  /// Where the map part ends (before the summary card).
  double get mapEndSeconds => _beats.isEmpty ? 0 : _beats.last.start;

  /// Start of the full-screen moment for [photoId], or null if it isn't shown.
  double? secondsForPhoto(String photoId) {
    for (final beat in _beats) {
      if (beat.photo?.id == photoId) return beat.start;
    }
    return null;
  }

  factory RecapTimeline.build({
    required List<SmartWaypoint> keyframes,
    List<HuntPin> pins = const <HuntPin>[],
    List<HuntPhoto> photos = const <HuntPhoto>[],
  }) {
    if (keyframes.isEmpty) {
      return RecapTimeline._(keyframes, const [], const []);
    }

    final cumulative = <double>[0];
    for (var i = 1; i < keyframes.length; i++) {
      cumulative.add(
        cumulative.last +
            HuntPhotoMatcher.distanceMeters(
              keyframes[i - 1].latitude,
              keyframes[i - 1].longitude,
              keyframes[i].latitude,
              keyframes[i].longitude,
            ),
      );
    }

    final last = keyframes.length - 1;
    final pinsAt = <int, List<HuntPin>>{};
    for (final pin in [
      ...pins,
    ]..sort((a, b) => a.timestamp.compareTo(b.timestamp))) {
      pinsAt
          .putIfAbsent(nearestIndex(keyframes, pin.timestamp), () => [])
          .add(pin);
    }
    final photosAt = <int, List<HuntPhoto>>{};
    for (final photo in [
      ...photos,
    ]..sort((a, b) => a.sortOrder.compareTo(b.sortOrder))) {
      final index = photo.matchedWaypointIndex;
      if (index == null || index < 0 || index > last) continue;
      photosAt.putIfAbsent(index, () => []).add(photo);
    }

    // Long recaps shorten each pause so the video stays within its limits.
    final pinHold = pins.length > 30 ? 1.8 : pinHoldSeconds;
    final photoHold = photos.length > 20 ? 3.0 : 4.2;

    final segmentWeights = <double>[
      // A couple of metres per segment keeps GPS jitter from stalling playback.
      for (var i = 1; i < keyframes.length; i++)
        cumulative[i] - cumulative[i - 1] + 2,
    ];
    final totalWeight = segmentWeights.fold<double>(0, (a, b) => a + b);
    final travelSeconds = (segmentWeights.length * secondsPerSegment)
        .clamp(minTravelSeconds, maxTravelSeconds)
        .toDouble();

    final beats = <_Beat>[_Beat.intro(introSeconds)];
    var runStart = 0;

    void closeRun(int end) {
      if (end <= runStart) return;
      var weight = 0.0;
      for (var i = runStart; i < end; i++) {
        weight += segmentWeights[i];
      }
      final seconds = math.max(
        travelSeconds * weight / totalWeight,
        // Even a short hop gets enough time to ease in and out visibly.
        math.min(1.2, travelSeconds),
      );
      beats.add(_Beat.travel(seconds, runStart, end));
    }

    for (var i = 0; i <= last; i++) {
      final holds = <_Beat>[];
      final point = keyframes[i];
      if (i == 0) {
        holds.add(
          _Beat.hold(
            startHoldSeconds,
            i,
            RecapCaption(
              kind: RecapEventKind.start,
              title: 'Die jag begin',
              detail:
                  'GPS-opname het om ${_clock.format(point.timestamp)} hier begin.',
            ),
          ),
        );
      }
      if (point.isStop) {
        holds.add(_Beat.hold(restHoldSeconds, i, _restCaption(point)));
      }
      for (final pin in pinsAt[i] ?? const <HuntPin>[]) {
        holds.add(_Beat.hold(pinHold, i, _pinCaption(pin)));
      }
      for (final photo in photosAt[i] ?? const <HuntPhoto>[]) {
        holds.add(
          _Beat.hold(photoHold, i, _photoCaption(photo, point), photo: photo),
        );
      }
      if (i == last && last > 0) {
        final start = keyframes.first.timestamp;
        holds.add(
          _Beat.hold(
            finishHoldSeconds,
            i,
            RecapCaption(
              kind: RecapEventKind.finish,
              title: 'Einde van die jag',
              detail:
                  'Opname om ${_clock.format(point.timestamp)} gestop: '
                  '${_km(cumulative.last)} km in '
                  '${_duration(point.timestamp.difference(start))}.',
            ),
          ),
        );
      }
      if (holds.isNotEmpty) {
        closeRun(i);
        beats.addAll(holds);
        runStart = i;
      }
    }
    closeRun(last);

    beats.add(
      _Beat.overview(
        overviewSeconds,
        last,
        RecapCaption(
          kind: RecapEventKind.overview,
          title: 'Die hele roete',
          detail:
              '${_km(cumulative.last)} km, ${pins.length} '
              '${pins.length == 1 ? 'merker' : 'merkers'} en ${photos.length} '
              '${photos.length == 1 ? 'foto' : 'foto’s'}.',
        ),
      ),
    );
    beats.add(_Beat.summary(summarySeconds, last));

    var clock = 0.0;
    for (final beat in beats) {
      beat.start = clock;
      clock += beat.seconds;
    }
    return RecapTimeline._(keyframes, beats, cumulative);
  }

  /// The frame shown [seconds] into the recap (clamped to the recap length).
  RecapFrame frameAt(double seconds) {
    if (_beats.isEmpty) {
      return const RecapFrame(
        phase: RecapPhase.map,
        position: 0,
        caption: null,
      );
    }
    final t = seconds.clamp(0, totalSeconds).toDouble();
    var beat = _beats.last;
    for (final candidate in _beats) {
      if (t < candidate.start + candidate.seconds) {
        beat = candidate;
        break;
      }
    }
    final local = (t - beat.start).clamp(0, beat.seconds).toDouble();

    switch (beat.kind) {
      case _BeatKind.intro:
        return const RecapFrame(
          phase: RecapPhase.intro,
          position: 0,
          caption: null,
        );
      case _BeatKind.summary:
        return RecapFrame(
          phase: RecapPhase.summary,
          position: beat.to.toDouble(),
          caption: null,
        );
      case _BeatKind.overview:
        return RecapFrame(
          phase: RecapPhase.map,
          position: beat.to.toDouble(),
          caption: beat.caption,
          captionOpacity: _fadeIn(local, captionFadeSeconds),
          // Zoom out during the first half, then hold the whole route.
          overview: _smoothStep((local / (beat.seconds * 0.5)).clamp(0, 1)),
        );
      case _BeatKind.hold:
        final photo = beat.photo;
        return RecapFrame(
          phase: RecapPhase.map,
          position: beat.from.toDouble(),
          caption: beat.caption,
          captionOpacity: photo != null
              ? 1
              : _fadeIn(local, captionFadeSeconds),
          photo: photo,
          photoOpacity: photo == null
              ? 0
              : math.min(
                  _fadeIn(local, photoFadeSeconds),
                  _fadeIn(beat.seconds - local, photoFadeSeconds),
                ),
        );
      case _BeatKind.travel:
        final position = _travelPosition(beat, local);
        return RecapFrame(
          phase: RecapPhase.map,
          position: position,
          caption: _travelCaption(position),
        );
    }
  }

  /// How many distinct images an export at [fps] needs; repeated frames
  /// (still photos, pauses) are linked rather than drawn again.
  int uniqueFrameCount(int fps) {
    final frames = frameCount(fps);
    var unique = 0;
    String? previous;
    for (var i = 0; i < frames; i++) {
      final key = frameAt(i / fps).renderKey;
      if (key != previous) unique++;
      previous = key;
    }
    return unique;
  }

  int frameCount(int fps) => (totalSeconds * fps).ceil();

  /// Constant speed along the run, with a gentle speed-up after a pause and a
  /// slow-down before the next one (a trapezoid speed profile).
  double _travelPosition(_Beat beat, double local) {
    final total = beat.seconds;
    final ramp = math.min(easeSeconds, total / 3);
    final peak = 1 / (total - ramp);
    final double fraction;
    if (local < ramp) {
      fraction = peak * local * local / (2 * ramp);
    } else if (local <= total - ramp) {
      fraction = peak * (local - ramp / 2);
    } else {
      final remaining = total - local;
      fraction = 1 - peak * remaining * remaining / (2 * ramp);
    }
    final clamped = fraction.clamp(0.0, 1.0);

    // Distance-based, so the marker moves at an even pace over long and
    // short GPS segments alike.
    final startMeters = _cumulativeMeters[beat.from] + beat.from * 2.0;
    final endMeters = _cumulativeMeters[beat.to] + beat.to * 2.0;
    final target = startMeters + (endMeters - startMeters) * clamped;
    for (var i = beat.from; i < beat.to; i++) {
      final a = _cumulativeMeters[i] + i * 2.0;
      final b = _cumulativeMeters[i + 1] + (i + 1) * 2.0;
      if (target <= b) {
        return i + (b > a ? (target - a) / (b - a) : 0);
      }
    }
    return beat.to.toDouble();
  }

  /// Interpolated time at [position].
  DateTime timeAt(double position) {
    final i = position.floor().clamp(0, keyframes.length - 1);
    final j = math.min(i + 1, keyframes.length - 1);
    final f = position - i;
    final a = keyframes[i].timestamp;
    final b = keyframes[j].timestamp;
    return a.add(
      Duration(milliseconds: (b.difference(a).inMilliseconds * f).round()),
    );
  }

  double metersAt(double position) {
    final i = position.floor().clamp(0, keyframes.length - 1);
    final j = math.min(i + 1, keyframes.length - 1);
    final f = position - i;
    return _cumulativeMeters[i] +
        (_cumulativeMeters[j] - _cumulativeMeters[i]) * f;
  }

  RecapCaption _travelCaption(double position) {
    return RecapCaption(
      kind: RecapEventKind.travel,
      title: 'Op die spoor',
      detail:
          '${_km(metersAt(position))} km afgelê · '
          '${_clock.format(timeAt(position))}',
    );
  }

  static RecapCaption _restCaption(SmartWaypoint point) {
    final minutes = point.stopDuration?.inMinutes ?? 0;
    final howLong = minutes >= 1
        ? '$minutes ${minutes == 1 ? 'minuut' : 'minute'}'
        : '’n rukkie';
    return RecapCaption(
      kind: RecapEventKind.rest,
      title: 'Ruspunt',
      detail:
          'Die GPS het opgemerk dat jy hier $howLong stilgestaan het '
          '(${_clock.format(point.timestamp)}).',
    );
  }

  static RecapCaption _pinCaption(HuntPin pin) {
    final time = _clock.format(pin.timestamp);
    final percent = pin.confidence == null
        ? ''
        : ' (${(pin.confidence! * 100).round()}% seker)';
    final note = pin.note?.trim() ?? '';
    final noteSuffix = note.isEmpty ? '' : ' “$note”';

    if (pin.autoDetected) {
      return RecapCaption(
        kind: pin.confirmed
            ? RecapEventKind.autoShotConfirmed
            : RecapEventKind.autoShot,
        pinType: pin.type,
        title: pin.confirmed ? 'Skoot herken' : 'Moontlike skoot',
        detail: pin.confirmed
            ? 'Outo-skoot het om $time ’n geweerskoot gehoor$percent en jy het '
                  'dit bevestig.$noteSuffix'
            : 'Outo-skoot het om $time ’n geweerskoot gehoor$percent; nog nie '
                  'deur jou bevestig nie.$noteSuffix',
      );
    }

    final what = switch (pin.type) {
      HuntPinType.shotFired =>
        'Jy het om $time aangeteken dat ’n skoot hier '
            'geskiet is.',
      HuntPinType.animalFound =>
        'Jy het om $time aangeteken dat die dier hier '
            'gevind is.',
      HuntPinType.campsite =>
        'Jy het om $time hierdie plek as kampplek gemerk.',
      HuntPinType.waterSource => 'Jy het om $time hier ’n waterbron gemerk.',
      HuntPinType.hazard => 'Jy het om $time hier ’n gevaar gemerk.',
      HuntPinType.custom => 'Jy het hierdie plek om $time self gemerk.',
    };
    return RecapCaption(
      kind: RecapEventKind.manualPin,
      pinType: pin.type,
      title: pin.type.label,
      detail: '$what$noteSuffix',
    );
  }

  static RecapCaption _photoCaption(HuntPhoto photo, SmartWaypoint point) {
    final taken = photo.adjustedTimestamp ?? photo.originalCapturedAt;
    final when = taken == null ? '' : 'Geneem om ${_clock.format(taken)}. ';
    final placed = switch (photo.matchMethod) {
      HuntPhotoMatchMethod.gps =>
        'Op die roete geplaas volgens die foto se GPS-ligging.',
      HuntPhotoMatchMethod.timestamp =>
        'Op die roete geplaas volgens die tyd waarop dit geneem is.',
      HuntPhotoMatchMethod.pin => 'Gekoppel aan ’n merker wat jy geplaas het.',
      HuntPhotoMatchMethod.manual => 'Deur jou self op die roete geplaas.',
      HuntPhotoMatchMethod.unmatched => '',
    };
    final caption = photo.caption.trim();
    return RecapCaption(
      kind: RecapEventKind.photo,
      title: caption.isEmpty ? 'Jagfoto' : caption,
      detail: '$when$placed'.trim(),
    );
  }

  static int nearestIndex(List<SmartWaypoint> points, DateTime time) {
    var best = 0;
    var bestGap = double.infinity;
    for (var i = 0; i < points.length; i++) {
      final gap = points[i].timestamp.difference(time).inMilliseconds.abs();
      if (gap < bestGap) {
        bestGap = gap.toDouble();
        best = i;
      }
    }
    return best;
  }

  static double _fadeIn(double local, double fade) =>
      fade <= 0 ? 1 : _smoothStep((local / fade).clamp(0, 1));

  static double _smoothStep(num x) {
    final v = x.toDouble();
    return v * v * (3 - 2 * v);
  }

  static String _km(double meters) =>
      (meters / 1000).toStringAsFixed(2).replaceAll('.', ',');

  static String _duration(Duration d) {
    final hours = d.inHours;
    final minutes = d.inMinutes.remainder(60);
    return hours > 0 ? '$hours h $minutes min' : '${d.inMinutes} min';
  }
}
