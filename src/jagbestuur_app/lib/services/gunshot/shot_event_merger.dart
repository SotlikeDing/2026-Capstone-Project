import 'dart:typed_data';

/// One sound the classifier accepted as a gunshot.
class ShotCandidate {
  const ShotCandidate({
    required this.at,
    required this.peakDbfs,
    required this.confidence,
    required this.near,
    required this.embedding,
  });

  final DateTime at;
  final double peakDbfs;

  /// 0-1 after the personal head; 0.5 is the firing line.
  final double confidence;
  final bool near;
  final Float32List embedding;
}

/// One or more shots close together in time: becomes ONE pin.
class ShotEvent {
  ShotEvent._(this.id, ShotCandidate first)
    : firstAt = first.at,
      lastAt = first.at,
      shots = 1,
      confidence = first.confidence,
      near = first.near,
      embedding = first.embedding;

  final String id;
  final DateTime firstAt;
  DateTime lastAt;
  int shots;

  /// Highest confidence among the merged shots.
  double confidence;
  bool near;

  /// Embedding of the most confident shot - used when the hunter confirms or
  /// removes the pin.
  Float32List embedding;

  String get note {
    final count = shots == 1 ? '1 skoot' : '$shots skote';
    return 'Outomaties: $count${near ? '' : ' (ver)'}';
  }
}

class ShotEventUpdate {
  const ShotEventUpdate(this.event, {required this.isNew});

  final ShotEvent event;

  /// true = drop a new pin; false = update the existing pin (more shots).
  final bool isNew;
}

/// Echoes and quick follow-up shots arrive as several candidates; anything
/// within [window] of the previous shot joins the same event.
class ShotEventMerger {
  ShotEventMerger({this.window = const Duration(seconds: 4)});

  final Duration window;
  ShotEvent? _current;
  int _counter = 0;

  ShotEventUpdate add(ShotCandidate c) {
    final current = _current;
    if (current != null && c.at.difference(current.lastAt) <= window) {
      current
        ..lastAt = c.at
        ..shots += 1
        ..near = current.near || c.near;
      if (c.confidence > current.confidence) {
        current
          ..confidence = c.confidence
          ..embedding = c.embedding;
      }
      return ShotEventUpdate(current, isNew: false);
    }
    final event = ShotEvent._('${c.at.microsecondsSinceEpoch}-${_counter++}', c);
    _current = event;
    return ShotEventUpdate(event, isNew: true);
  }

  /// The event's pin is gone (removed, undone or never dropped): the next shot
  /// starts a new event instead of joining one nobody can see.
  void endEvent(String id) {
    if (_current?.id == id) _current = null;
  }

  void reset() => _current = null;
}
