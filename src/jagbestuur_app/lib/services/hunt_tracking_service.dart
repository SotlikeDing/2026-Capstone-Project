import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:geolocator/geolocator.dart';

import '../models/hunt_pin.dart';
import '../models/hunt_session.dart';
import '../models/smart_waypoint.dart';
import '../utils/route_optimizer.dart';
import 'hunt_storage_service.dart';
import 'user_storage.dart';

/// Records a hunt to an append-only journal on disk so nothing is lost if the
/// app is killed, then turns it into a [HuntSession] when the hunt ends.
///
/// All file IO runs through a single queue: a write can never overlap a flush,
/// a pin rewrite, or the final close. Overlapping IOSink operations throw
/// "StreamSink is bound to a stream".
class HuntTrackingService {
  HuntTrackingService();

  /// True while any hunt is being recorded. Lets other screens (e.g. sign-out)
  /// warn the user before ending it.
  static final ValueNotifier<bool> activeHunt = ValueNotifier<bool>(false);

  /// Minimum movement counted towards distance. GPS fixes wander a few metres
  /// while standing still; without this a stationary hunter "walks".
  static const double _minCountedStepMeters = 5;

  IOSink? _journalSink;
  IOSink? _pinsSink;

  DateTime? _sessionStart;
  String? _sessionId;
  File? _journalFile;
  File? _pinsFile;
  Directory? _sessionsDir;

  Position? _latestPosition;
  Position? _lastRecordedPosition;
  DateTime? _lastRecordedAt;
  Position? _distanceAnchor;
  double _totalDistanceMeters = 0;

  final List<HuntPin> _livePins = [];
  final Stopwatch _sessionStopwatch = Stopwatch();

  bool _isTracking = false;
  Future<void> _ioQueue = Future<void>.value();
  Future<List<HuntSession>>? _recovery;

  bool get isTracking => _isTracking;
  Duration get elapsedDuration => _sessionStopwatch.elapsed;
  int get elapsedSeconds => _sessionStopwatch.elapsed.inSeconds;
  List<HuntPin> get livePins => List.unmodifiable(_livePins);
  Position? get latestPosition => _latestPosition;
  DateTime? get sessionStart => _sessionStart;
  String? get sessionId => _sessionId;
  double get totalDistanceMeters => _totalDistanceMeters;

  /// Runs [action] after every earlier IO operation has finished.
  Future<T> _serialized<T>(Future<T> Function() action) {
    final result = _ioQueue.then((_) => action());
    _ioQueue = result.then<void>((_) {}, onError: (_) {});
    return result;
  }

  Future<void> startSession({required Position initialPosition}) async {
    if (_isTracking) {
      return;
    }

    // Never let orphan recovery run against the journal we are about to open.
    final recovery = _recovery;
    if (recovery != null) {
      try {
        await recovery;
      } catch (_) {}
    }

    await _serialized(() async {
      if (_isTracking) return;

      final userId = UserStorage.requireUserId();
      final journalDir = await UserStorage.documentsDir(
        'hunt_journals',
        userId: userId,
      );
      final sessionsDir = await UserStorage.documentsDir(
        'hunt_sessions',
        userId: userId,
      );

      final start = DateTime.now();
      final sessionId = start.millisecondsSinceEpoch.toString();
      final sep = Platform.pathSeparator;
      final journalFile = File('${journalDir.path}$sep$sessionId.jsonl');
      final pinsFile = File('${journalDir.path}$sep$sessionId.pins.jsonl');

      // Both sinks are owned by the service and closed in _closeSinks.
      // ignore: close_sinks
      final journalSink = journalFile.openWrite(mode: FileMode.append);
      // ignore: close_sinks
      final IOSink pinsSink;
      try {
        pinsSink = pinsFile.openWrite(mode: FileMode.append);
      } catch (_) {
        await journalSink.close();
        rethrow;
      }

      // Only now is the session real.
      _journalSink = journalSink;
      _pinsSink = pinsSink;
      _journalFile = journalFile;
      _pinsFile = pinsFile;
      _sessionsDir = sessionsDir;
      _sessionId = sessionId;
      _sessionStart = start;
      _latestPosition = initialPosition;
      _lastRecordedPosition = null;
      _lastRecordedAt = null;
      _distanceAnchor = initialPosition;
      _totalDistanceMeters = 0;
      _livePins.clear();
      _sessionStopwatch
        ..stop()
        ..reset()
        ..start();
      _isTracking = true;
      activeHunt.value = true;

      await _writeWaypoint(initialPosition, start);
    });
  }

  /// Records a GPS fix. Returns quickly; the disk write is queued.
  Future<void> recordPosition(Position position) {
    if (!_isTracking) {
      return Future<void>.value();
    }

    _latestPosition = position;
    _accumulateDistance(position);

    return _serialized(() async {
      if (!_isTracking || _journalSink == null) return;

      final now = DateTime.now();
      final last = _lastRecordedPosition;
      final lastAt = _lastRecordedAt;
      if (last != null && lastAt != null) {
        final elapsed = now.difference(lastAt);
        final distance = Geolocator.distanceBetween(
          last.latitude,
          last.longitude,
          position.latitude,
          position.longitude,
        );
        if (elapsed < const Duration(seconds: 2) && distance < 2) {
          return;
        }
      }

      await _writeWaypoint(position, now);
    });
  }

  void _accumulateDistance(Position position) {
    final anchor = _distanceAnchor;
    if (anchor == null) {
      _distanceAnchor = position;
      return;
    }

    final step = Geolocator.distanceBetween(
      anchor.latitude,
      anchor.longitude,
      position.latitude,
      position.longitude,
    );

    // Count a step only once it is clearly larger than the GPS noise.
    final threshold = position.accuracy > _minCountedStepMeters
        ? position.accuracy
        : _minCountedStepMeters;
    if (step >= threshold) {
      _totalDistanceMeters += step;
      _distanceAnchor = position;
    }
  }

  Future<void> _writeWaypoint(Position position, DateTime at) async {
    final waypoint = SmartWaypoint(
      latitude: position.latitude,
      longitude: position.longitude,
      timestamp: at,
    );
    _journalSink!.writeln(jsonEncode(waypoint.toJson()));
    await _journalSink!.flush();
    _lastRecordedPosition = position;
    _lastRecordedAt = at;
  }

  /// Drops a pin at the latest GPS fix, or at [position] when the caller has a
  /// fresher one (Outo-skoot asks for a fresh fix right after a shot).
  /// [timestamp] defaults to now; Outo-skoot passes the moment the shot was heard.
  Future<HuntPin> dropPin({
    required HuntPinType type,
    String? note,
    Position? position,
    DateTime? timestamp,
    bool autoDetected = false,
    double? confidence,
  }) {
    return _serialized(() async {
      if (!_isTracking) {
        throw StateError('Geen aktiewe jag-sessie nie — kan nie pen los nie.');
      }
      final sink = _pinsSink;
      if (sink == null) {
        throw StateError('Merker-joernaal is nie beskikbaar nie.');
      }
      final at = position ?? _latestPosition;
      if (at == null) {
        throw StateError('Wag vir GPS-sein voor jy \'n pen kan los.');
      }

      final pin = HuntPin(
        id: DateTime.now().microsecondsSinceEpoch.toString(),
        type: type,
        latitude: at.latitude,
        longitude: at.longitude,
        timestamp: timestamp ?? DateTime.now(),
        note: note,
        autoDetected: autoDetected,
        confidence: confidence,
      );

      sink.writeln(jsonEncode(pin.toJson()));
      await sink.flush();
      _livePins.add(pin);
      return pin;
    });
  }

  Future<void> removePin(String pinId) {
    return _serialized(() async {
      _livePins.removeWhere((p) => p.id == pinId);
      await _rewritePinsJournal();
    });
  }

  Future<void> updatePin(HuntPin updated) {
    return _serialized(() async {
      final index = _livePins.indexWhere((p) => p.id == updated.id);
      if (index == -1) return;
      _livePins[index] = updated;
      await _rewritePinsJournal();
    });
  }

  /// Applies [change] to the pin as it is when the write runs (not as it was
  /// when the caller looked), so two quick edits to one pin - e.g. the hunter
  /// confirming it while a follow-up shot updates its note - both survive.
  /// Returns the updated pin, or null when it no longer exists.
  Future<HuntPin?> updatePinWith(String pinId, HuntPin Function(HuntPin current) change) {
    return _serialized(() async {
      final index = _livePins.indexWhere((p) => p.id == pinId);
      if (index == -1) return null;
      final updated = change(_livePins[index]);
      _livePins[index] = updated;
      await _rewritePinsJournal();
      return updated;
    });
  }

  /// Must run inside [_serialized].
  Future<void> _rewritePinsJournal() async {
    final pinsFile = _pinsFile;
    if (pinsFile == null) return;

    final oldSink = _pinsSink;
    if (oldSink != null) {
      await oldSink.flush();
      await oldSink.close();
    }

    // Owned by the service; closed in _closeSinks or the next rewrite.
    // ignore: close_sinks
    final sink = pinsFile.openWrite(mode: FileMode.write);
    _pinsSink = sink;
    for (final pin in _livePins) {
      sink.writeln(jsonEncode(pin.toJson()));
    }
    await sink.flush();
  }

  /// Ends the hunt, saves the optimised session and removes the journal.
  Future<HuntSession> stopSession() {
    return _serialized(() async {
      if (!_isTracking) {
        throw StateError('Geen aktiewe jag om te stop nie.');
      }

      final journalFile = _journalFile!;
      final pinsFile = _pinsFile!;
      final sessionId = _sessionId!;
      final sessionStart = _sessionStart!;
      final sessionsDir = _sessionsDir!;

      _sessionStopwatch.stop();
      _isTracking = false;
      activeHunt.value = false;

      await _closeSinks();

      final rawWaypoints = await _readJournal(journalFile);
      final optimized = await compute(_optimize, rawWaypoints);

      final session = HuntSession(
        id: sessionId,
        startedAt: sessionStart,
        endedAt: DateTime.now(),
        totalDistanceMeters: _totalDistanceMeters,
        waypoints: optimized,
        pins: List<HuntPin>.of(_livePins),
      );

      // Save first; the journal is only deleted once the session is on disk.
      await _saveFinalSession(sessionsDir, session);
      await _deleteIfExists(journalFile);
      await _deleteIfExists(pinsFile);

      _resetActiveState();
      HuntStorageService.notifySessionsChanged();
      return session;
    });
  }

  /// Turns journals left behind by a crash or a killed app into sessions.
  /// The journal of the hunt currently being recorded is never touched.
  Future<List<HuntSession>> recoverOrphanedJournals() {
    return _recovery ??= _serialized(_recover).whenComplete(() {
      _recovery = null;
    });
  }

  Future<List<HuntSession>> _recover() async {
    final int userId;
    try {
      userId = UserStorage.requireUserId();
    } on StateError {
      return const [];
    }

    final journalDir = await UserStorage.documentsDir(
      'hunt_journals',
      userId: userId,
    );
    final sessionsDir = await UserStorage.documentsDir(
      'hunt_sessions',
      userId: userId,
    );

    final activeId = _isTracking ? _sessionId : null;
    final sep = Platform.pathSeparator;
    final recovered = <HuntSession>[];

    final files = await journalDir.list().where((e) => e is File).toList();
    final journalIds = <String>{};
    final pinIds = <String>{};
    for (final entity in files) {
      final name = entity.uri.pathSegments.last;
      if (name.endsWith('.pins.jsonl')) {
        pinIds.add(name.substring(0, name.length - '.pins.jsonl'.length));
      } else if (name.endsWith('.jsonl')) {
        journalIds.add(name.substring(0, name.length - '.jsonl'.length));
      }
    }

    for (final id in {...journalIds, ...pinIds}) {
      if (id == activeId) continue;

      final journalFile = File('${journalDir.path}$sep$id.jsonl');
      final pinsFile = File('${journalDir.path}$sep$id.pins.jsonl');

      try {
        final rawWaypoints = await _readJournal(journalFile);
        if (rawWaypoints.isNotEmpty) {
          final pins = await _readPinsJournal(pinsFile);
          final optimized = await compute(_optimize, rawWaypoints);
          final session = HuntSession(
            id: id,
            startedAt: rawWaypoints.first.timestamp,
            endedAt: rawWaypoints.last.timestamp,
            totalDistanceMeters: _estimateDistance(rawWaypoints),
            waypoints: optimized,
            pins: pins,
          );
          await _saveFinalSession(sessionsDir, session);
          recovered.add(session);
        }

        // Journal with no route, or a pins file whose journal is gone: there
        // is nothing to replay, so clean both up instead of leaking them.
        await _deleteIfExists(journalFile);
        await _deleteIfExists(pinsFile);
      } catch (e, stack) {
        debugPrint('[HuntTrackingService] Could not recover $id: $e\n$stack');
      }
    }

    if (recovered.isNotEmpty) {
      HuntStorageService.notifySessionsChanged();
    }
    return recovered;
  }

  double _estimateDistance(List<SmartWaypoint> points) {
    double total = 0;
    for (var i = 1; i < points.length; i++) {
      total += Geolocator.distanceBetween(
        points[i - 1].latitude,
        points[i - 1].longitude,
        points[i].latitude,
        points[i].longitude,
      );
    }
    return total;
  }

  /// Streams the journal line by line so a long hunt is never loaded into
  /// memory as one string.
  Future<List<SmartWaypoint>> _readJournal(File journalFile) async {
    if (!await journalFile.exists()) {
      return [];
    }

    final waypoints = <SmartWaypoint>[];
    await for (final line
        in journalFile
            .openRead()
            .transform(utf8.decoder)
            .transform(const LineSplitter())) {
      if (line.trim().isEmpty) continue;
      try {
        waypoints.add(
          SmartWaypoint.fromJson(jsonDecode(line) as Map<String, dynamic>),
        );
      } catch (_) {
        // A torn last line after a crash is expected; skip it.
      }
    }
    return waypoints;
  }

  Future<List<HuntPin>> _readPinsJournal(File pinsFile) async {
    if (!await pinsFile.exists()) {
      return [];
    }

    final pins = <HuntPin>[];
    await for (final line
        in pinsFile
            .openRead()
            .transform(utf8.decoder)
            .transform(const LineSplitter())) {
      if (line.trim().isEmpty) continue;
      try {
        pins.add(HuntPin.fromJson(jsonDecode(line) as Map<String, dynamic>));
      } catch (_) {}
    }
    return pins;
  }

  Future<void> _saveFinalSession(Directory sessionsDir, HuntSession session) {
    final file = File(
      '${sessionsDir.path}${Platform.pathSeparator}${session.id}.json',
    );
    return file.writeAsString(jsonEncode(session.toJson()), flush: true);
  }

  Future<void> cancelSession() {
    return _serialized(() async {
      _sessionStopwatch.stop();
      _isTracking = false;
      activeHunt.value = false;
      await _closeSinks();
      await _deleteIfExists(_journalFile);
      await _deleteIfExists(_pinsFile);
      _resetActiveState();
    });
  }

  Future<void> _closeSinks() async {
    final journal = _journalSink;
    final pins = _pinsSink;
    _journalSink = null;
    _pinsSink = null;
    for (final sink in [journal, pins]) {
      if (sink == null) continue;
      try {
        await sink.flush();
        await sink.close();
      } catch (e) {
        debugPrint('[HuntTrackingService] Could not close journal: $e');
      }
    }
  }

  Future<void> _deleteIfExists(File? file) async {
    if (file != null && await file.exists()) {
      await file.delete();
    }
  }

  void _resetActiveState() {
    _sessionId = null;
    _sessionStart = null;
    _journalFile = null;
    _pinsFile = null;
    _sessionsDir = null;
    _latestPosition = null;
    _lastRecordedPosition = null;
    _lastRecordedAt = null;
    _distanceAnchor = null;
    _livePins.clear();
  }

  /// Called when the owning screen goes away (sign-out, app shutdown). An
  /// active hunt is finished and saved instead of being left as an orphan.
  void dispose() {
    if (_isTracking) {
      unawaited(
        stopSession().then<void>(
          (_) {},
          onError: (Object e) {
            debugPrint(
              '[HuntTrackingService] Could not save hunt on dispose: $e',
            );
          },
        ),
      );
      return;
    }
    _sessionStopwatch.stop();
    unawaited(_serialized(_closeSinks));
  }
}

List<SmartWaypoint> _optimize(List<SmartWaypoint> raw) =>
    RouteOptimizer.optimizeRoute(raw);
