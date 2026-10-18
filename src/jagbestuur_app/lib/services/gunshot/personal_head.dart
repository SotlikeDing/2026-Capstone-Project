import 'dart:math' as math;
import 'dart:typed_data';

/// One sound the hunter judged: confirmed shot (1) or removed false pin (0).
class LabelledEmbedding {
  const LabelledEmbedding(this.embedding, this.label);

  final List<double> embedding;
  final int label;

  Map<String, dynamic> toJson() => {
    // 4 decimals keep the file small; the model does not need more.
    'e': embedding.map((v) => double.parse(v.toStringAsFixed(4))).toList(),
    'y': label,
  };

  factory LabelledEmbedding.fromJson(Map<String, dynamic> json) => LabelledEmbedding(
    (json['e'] as List).map((v) => (v as num).toDouble()).toList(),
    (json['y'] as num).toInt(),
  );
}

/// A trained logistic regression over standardised embeddings.
class LogisticWeights {
  const LogisticWeights({required this.weights, required this.bias, required this.mean, required this.std, required this.samples});

  final List<double> weights;
  final double bias;
  final List<double> mean;
  final List<double> std;
  final int samples;

  double predict(List<double> x) {
    var z = bias;
    for (var i = 0; i < weights.length && i < x.length; i++) {
      z += weights[i] * ((x[i] - mean[i]) / std[i]);
    }
    return 1 / (1 + math.exp(-z));
  }
}

/// The hunter's own correction on top of YAMNet: learns from Bevestig /
/// Verwyder on automatic pins. Until it has enough evidence it changes nothing;
/// then it takes a share of the decision that grows with the evidence,
/// n / (n + 50), capped at 80% - the same rule the WebAPI AI Helper uses.
class PersonalHead {
  PersonalHead([List<LabelledEmbedding>? samples]) : _samples = [...?samples];

  static const int minSamples = 20;
  static const int minPerClass = 5;
  static const int maxSamples = 500;

  final List<LabelledEmbedding> _samples;
  LogisticWeights? _model;

  List<LabelledEmbedding> get samples => List.unmodifiable(_samples);
  int get sampleCount => _samples.length;
  int get confirmedCount => _samples.where((s) => s.label == 1).length;
  int get removedCount => _samples.where((s) => s.label == 0).length;
  bool get isActive => _model != null;

  double get share {
    final m = _model;
    if (m == null) return 0;
    return math.min(0.8, m.samples / (m.samples + 50));
  }

  void add(Float32List embedding, {required bool isGunshot}) {
    _samples.add(LabelledEmbedding(embedding.toList(), isGunshot ? 1 : 0));
    if (_samples.length > maxSamples) _samples.removeAt(0);
  }

  void setModel(LogisticWeights? model) => _model = model;

  void clear() {
    _samples.clear();
    _model = null;
  }

  /// Blends the model's confidence (0-1, 0.5 = threshold) with the personal
  /// head's probability that this sound is a shot the hunter would keep.
  double blend(double modelConfidence, Float32List embedding) {
    final m = _model;
    if (m == null) return modelConfidence;
    final s = share;
    return (1 - s) * modelConfidence + s * m.predict(embedding);
  }

  /// Trains on [samples]; null when there is not enough evidence yet. Pure
  /// function so it can run in a background isolate (compute()).
  static LogisticWeights? train(List<LabelledEmbedding> samples, {int epochs = 300, double lr = 0.1, double l2 = 0.05}) {
    final pos = samples.where((s) => s.label == 1).length;
    final neg = samples.length - pos;
    if (samples.length < minSamples || pos < minPerClass || neg < minPerClass) return null;

    final dim = samples.first.embedding.length;
    final mean = List<double>.filled(dim, 0);
    final std = List<double>.filled(dim, 0);
    for (final s in samples) {
      for (var i = 0; i < dim; i++) {
        mean[i] += s.embedding[i];
      }
    }
    for (var i = 0; i < dim; i++) {
      mean[i] /= samples.length;
    }
    for (final s in samples) {
      for (var i = 0; i < dim; i++) {
        final d = s.embedding[i] - mean[i];
        std[i] += d * d;
      }
    }
    for (var i = 0; i < dim; i++) {
      std[i] = math.sqrt(std[i] / samples.length) + 1e-6;
    }

    final xs = samples
        .map((s) => List<double>.generate(dim, (i) => (s.embedding[i] - mean[i]) / std[i]))
        .toList();
    final w = List<double>.filled(dim, 0);
    var b = 0.0;
    for (var epoch = 0; epoch < epochs; epoch++) {
      final grad = List<double>.filled(dim, 0);
      var gradB = 0.0;
      for (var n = 0; n < xs.length; n++) {
        var z = b;
        final x = xs[n];
        for (var i = 0; i < dim; i++) {
          z += w[i] * x[i];
        }
        final err = 1 / (1 + math.exp(-z)) - samples[n].label;
        for (var i = 0; i < dim; i++) {
          grad[i] += err * x[i];
        }
        gradB += err;
      }
      for (var i = 0; i < dim; i++) {
        w[i] -= lr * (grad[i] / xs.length + l2 * w[i]);
      }
      b -= lr * gradB / xs.length;
    }
    return LogisticWeights(weights: w, bias: b, mean: mean, std: std, samples: samples.length);
  }
}

/// Maps a raw YAMNet gun score onto 0-1 with [threshold] at exactly 0.5, so it
/// can be blended with the personal head's probability on the same scale.
double scoreToConfidence(double score, double threshold) {
  if (score >= threshold) {
    return 0.5 + 0.5 * ((score - threshold) / (1 - threshold)).clamp(0.0, 1.0);
  }
  return 0.5 * (score / threshold).clamp(0.0, 1.0);
}
