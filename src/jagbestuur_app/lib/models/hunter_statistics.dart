import 'hunt_pin.dart';
import 'hunt_session.dart';
import 'trophy.dart';

class CountedLabel {
  const CountedLabel({required this.label, required this.count});

  final String label;
  final int count;
}

enum TrophyHighlightBasis { rowlandWard, antlerSize, carcassWeight, species }

class TrophyHighlight {
  const TrophyHighlight({
    required this.trophy,
    required this.basis,
    required this.value,
  });

  final Trophy trophy;
  final TrophyHighlightBasis basis;

  /// Rowland Ward uses a percentage; measurements retain their source unit.
  final double value;
}

/// A read-only snapshot calculated from the hunter's trophy room and locally
/// recorded map sessions. Keeping this calculation outside the UI makes the
/// dashboard predictable and easy to verify.
class HunterStatistics {
  const HunterStatistics({
    required this.year,
    required this.huntsThisYear,
    required this.lifetimeHunts,
    required this.trophiesThisYear,
    required this.totalTrophies,
    required this.distanceThisYearMeters,
    required this.totalDistanceMeters,
    required this.totalFieldTime,
    required this.averageHuntTime,
    required this.huntsByMonth,
    required this.uniqueSpecies,
    required this.uniqueLocations,
    required this.publicTrophies,
    required this.rowlandWardQualifiers,
    required this.shotsMarked,
    required this.animalsFound,
    this.furthestShot,
    this.longestRoute,
    this.longestHunt,
    this.longestAntlers,
    this.heaviestCarcass,
    this.topSpecies,
    this.topLocation,
    this.favouriteFirearm,
    this.trophyHighlight,
    this.latestActivity,
  });

  final int year;
  final int huntsThisYear;
  final int lifetimeHunts;
  final int trophiesThisYear;
  final int totalTrophies;
  final double distanceThisYearMeters;
  final double totalDistanceMeters;
  final Duration totalFieldTime;
  final Duration averageHuntTime;
  final List<int> huntsByMonth;
  final int uniqueSpecies;
  final int uniqueLocations;
  final int publicTrophies;
  final int rowlandWardQualifiers;
  final int shotsMarked;
  final int animalsFound;
  final Trophy? furthestShot;
  final HuntSession? longestRoute;
  final HuntSession? longestHunt;
  final Trophy? longestAntlers;
  final Trophy? heaviestCarcass;
  final CountedLabel? topSpecies;
  final CountedLabel? topLocation;
  final CountedLabel? favouriteFirearm;
  final TrophyHighlight? trophyHighlight;
  final DateTime? latestActivity;

  factory HunterStatistics.calculate({
    required List<Trophy> trophies,
    required List<HuntSession> hunts,
    DateTime? now,
  }) {
    final currentYear = (now ?? DateTime.now()).year;
    final huntsThisYear = hunts
        .where((hunt) => hunt.startedAt.year == currentYear)
        .toList(growable: false);
    final trophiesThisYear = trophies
        .where((trophy) => trophy.harvestDate.year == currentYear)
        .toList(growable: false);

    final huntsByMonth = List<int>.filled(12, 0);
    for (final hunt in huntsThisYear) {
      huntsByMonth[hunt.startedAt.month - 1]++;
    }

    final totalDuration = hunts.fold<Duration>(
      Duration.zero,
      (total, hunt) => total + _safeDuration(hunt.duration),
    );
    final averageDuration = hunts.isEmpty
        ? Duration.zero
        : Duration(milliseconds: totalDuration.inMilliseconds ~/ hunts.length);

    return HunterStatistics(
      year: currentYear,
      huntsThisYear: huntsThisYear.length,
      lifetimeHunts: hunts.length,
      trophiesThisYear: trophiesThisYear.length,
      totalTrophies: trophies.length,
      distanceThisYearMeters: huntsThisYear.fold<double>(
        0,
        (total, hunt) => total + _safeDistance(hunt.totalDistanceMeters),
      ),
      totalDistanceMeters: hunts.fold<double>(
        0,
        (total, hunt) => total + _safeDistance(hunt.totalDistanceMeters),
      ),
      totalFieldTime: totalDuration,
      averageHuntTime: averageDuration,
      huntsByMonth: List.unmodifiable(huntsByMonth),
      uniqueSpecies: _uniqueCount(
        trophies.map((trophy) => trophy.displaySpecies),
      ),
      uniqueLocations: _uniqueCount(
        trophies.map((trophy) => trophy.locationText),
      ),
      publicTrophies: trophies.where((trophy) => trophy.isPublic).length,
      rowlandWardQualifiers: trophies
          .where((trophy) => trophy.rowlandWard?.hasReachedMinimum == true)
          .length,
      shotsMarked: hunts.fold<int>(
        0,
        (total, hunt) =>
            total +
            // Automatic shot pins the hunter never confirmed are not counted.
            hunt.pins
                .where(
                  (pin) =>
                      pin.type == HuntPinType.shotFired && !pin.needsReview,
                )
                .length,
      ),
      animalsFound: hunts.fold<int>(
        0,
        (total, hunt) =>
            total +
            hunt.pins
                .where((pin) => pin.type == HuntPinType.animalFound)
                .length,
      ),
      furthestShot: _maxTrophy(
        trophies,
        (trophy) => trophy.shotDistanceMeters?.toDouble(),
      ),
      longestRoute: _maxHunt(
        hunts,
        (hunt) => _safeDistance(hunt.totalDistanceMeters),
      ),
      longestHunt: _maxHunt(
        hunts,
        (hunt) => _safeDuration(hunt.duration).inMilliseconds.toDouble(),
      ),
      longestAntlers: _maxTrophy(trophies, (trophy) => trophy.antlerSizeCm),
      heaviestCarcass: _maxTrophy(trophies, (trophy) => trophy.carcassWeightKg),
      topSpecies: _mostCommon(trophies.map((trophy) => trophy.displaySpecies)),
      topLocation: _mostCommon(trophies.map((trophy) => trophy.locationText)),
      favouriteFirearm: _mostCommon(
        trophies.map((trophy) => trophy.firearmType),
      ),
      trophyHighlight: _findTrophyHighlight(trophies),
      latestActivity: _latestActivity(trophies, hunts),
    );
  }

  static Duration _safeDuration(Duration value) {
    return value.isNegative ? Duration.zero : value;
  }

  static double _safeDistance(double value) {
    return value.isFinite && value > 0 ? value : 0;
  }

  static int _uniqueCount(Iterable<String> values) {
    return values
        .map((value) => value.trim().toLowerCase())
        .where((value) => value.isNotEmpty)
        .toSet()
        .length;
  }

  static CountedLabel? _mostCommon(Iterable<String> values) {
    final counts = <String, int>{};
    final labels = <String, String>{};

    for (final rawValue in values) {
      final label = rawValue.trim();
      if (label.isEmpty) continue;
      final key = label.toLowerCase();
      labels.putIfAbsent(key, () => label);
      counts[key] = (counts[key] ?? 0) + 1;
    }

    if (counts.isEmpty) return null;
    final keys = counts.keys.toList()
      ..sort((a, b) {
        final countComparison = counts[b]!.compareTo(counts[a]!);
        return countComparison != 0 ? countComparison : a.compareTo(b);
      });
    final key = keys.first;
    return CountedLabel(label: labels[key]!, count: counts[key]!);
  }

  static Trophy? _maxTrophy(
    Iterable<Trophy> trophies,
    double? Function(Trophy trophy) valueOf,
  ) {
    Trophy? best;
    double? bestValue;
    for (final trophy in trophies) {
      final value = valueOf(trophy);
      if (value == null || !value.isFinite || value <= 0) continue;
      if (bestValue == null || value > bestValue) {
        best = trophy;
        bestValue = value;
      }
    }
    return best;
  }

  static HuntSession? _maxHunt(
    Iterable<HuntSession> hunts,
    double Function(HuntSession hunt) valueOf,
  ) {
    HuntSession? best;
    double? bestValue;
    for (final hunt in hunts) {
      final value = valueOf(hunt);
      if (!value.isFinite || value <= 0) continue;
      if (bestValue == null || value > bestValue) {
        best = hunt;
        bestValue = value;
      }
    }
    return best;
  }

  static TrophyHighlight? _findTrophyHighlight(List<Trophy> trophies) {
    Trophy? bestRowlandWard;
    double? bestPercentage;

    for (final trophy in trophies) {
      final score = trophy.rowlandWard?.score;
      final record = trophy.rowlandWard?.currentWorldRecord;
      if (score == null ||
          record == null ||
          score.unitCode != record.unitCode ||
          record.value.numerator <= BigInt.zero) {
        continue;
      }

      final scoreValue =
          score.value.numerator.toDouble() / score.value.denominator.toDouble();
      final recordValue =
          record.value.numerator.toDouble() /
          record.value.denominator.toDouble();
      final percentage = (scoreValue / recordValue) * 100;
      if (!percentage.isFinite || percentage < 0) continue;
      if (bestPercentage == null || percentage > bestPercentage) {
        bestRowlandWard = trophy;
        bestPercentage = percentage;
      }
    }

    if (bestRowlandWard != null && bestPercentage != null) {
      return TrophyHighlight(
        trophy: bestRowlandWard,
        basis: TrophyHighlightBasis.rowlandWard,
        value: bestPercentage,
      );
    }

    final longestAntlers = _maxTrophy(
      trophies,
      (trophy) => trophy.antlerSizeCm,
    );
    if (longestAntlers?.antlerSizeCm case final value?) {
      return TrophyHighlight(
        trophy: longestAntlers!,
        basis: TrophyHighlightBasis.antlerSize,
        value: value,
      );
    }

    final heaviestCarcass = _maxTrophy(
      trophies,
      (trophy) => trophy.carcassWeightKg,
    );
    if (heaviestCarcass?.carcassWeightKg case final value?) {
      return TrophyHighlight(
        trophy: heaviestCarcass!,
        basis: TrophyHighlightBasis.carcassWeight,
        value: value,
      );
    }

    final topSpecies = _mostCommon(
      trophies.map((trophy) => trophy.displaySpecies),
    );
    if (topSpecies == null) return null;
    final trophy = trophies.firstWhere(
      (item) =>
          item.displaySpecies.trim().toLowerCase() ==
          topSpecies.label.toLowerCase(),
    );
    return TrophyHighlight(
      trophy: trophy,
      basis: TrophyHighlightBasis.species,
      value: topSpecies.count.toDouble(),
    );
  }

  static DateTime? _latestActivity(
    List<Trophy> trophies,
    List<HuntSession> hunts,
  ) {
    DateTime? latest;
    for (final trophy in trophies) {
      if (latest == null || trophy.harvestDate.isAfter(latest)) {
        latest = trophy.harvestDate;
      }
    }
    for (final hunt in hunts) {
      if (latest == null || hunt.startedAt.isAfter(latest)) {
        latest = hunt.startedAt;
      }
    }
    return latest;
  }
}
