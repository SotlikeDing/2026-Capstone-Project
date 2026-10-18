import 'dart:typed_data';

import 'package:flutter/services.dart';
import 'package:tflite_flutter/tflite_flutter.dart';

import 'shot_classifier.dart';

/// Google's YAMNet (AudioSet, 521 classes, Apache-2.0) running on the phone via
/// TensorFlow Lite in a background isolate. The bundled model is the TF Hub
/// "lite-model/yamnet/tflite/1" variant: waveform in; class scores, the
/// 1024-number embedding and a log-mel spectrogram out.
class YamnetClassifier implements ShotClassifier {
  YamnetClassifier._(this._interpreter, this._isolate, this._labels, this._gunIndices);

  static const String modelAsset = 'lib/assets/models/yamnet.tflite';
  static const String classMapAsset = 'lib/assets/models/yamnet_class_map.csv';

  static const int _numClasses = 521;
  static const int _embeddingSize = 1024;

  final Interpreter _interpreter;
  final IsolateInterpreter _isolate;
  final List<String> _labels;
  final List<int> _gunIndices;
  bool _closed = false;

  static Future<YamnetClassifier> load() async {
    final labels = parseClassMap(await rootBundle.loadString(classMapAsset));
    final gunIndices = [
      for (var i = 0; i < labels.length; i++)
        if (kGunshotClassNames.contains(labels[i])) i,
    ];
    if (gunIndices.isEmpty) {
      throw StateError('YAMNet class map has no gunshot classes.');
    }

    final interpreter = await Interpreter.fromAsset(modelAsset);
    // The model accepts any length; fix it to one 0.975 s frame.
    interpreter.resizeInputTensor(0, [kYamnetWindowSamples]);
    interpreter.allocateTensors();
    final isolate = await IsolateInterpreter.create(address: interpreter.address);
    return YamnetClassifier._(interpreter, isolate, labels, gunIndices);
  }

  /// display_name column of yamnet_class_map.csv (index,mid,display_name; names may be quoted).
  static List<String> parseClassMap(String csv) {
    final names = <String>[];
    for (final line in csv.split('\n').skip(1)) {
      final trimmed = line.trim();
      if (trimmed.isEmpty) continue;
      final firstComma = trimmed.indexOf(',');
      final secondComma = trimmed.indexOf(',', firstComma + 1);
      var name = trimmed.substring(secondComma + 1);
      if (name.startsWith('"') && name.endsWith('"')) name = name.substring(1, name.length - 1);
      names.add(name);
    }
    return names;
  }

  @override
  Future<ShotClassification> classify(Float32List waveform) async {
    if (_closed) throw StateError('YamnetClassifier is closed');
    final outputs = <int, Object>{};
    final tensors = _interpreter.getOutputTensors();
    for (var i = 0; i < tensors.length; i++) {
      outputs[i] = Uint8List(tensors[i].numBytes());
    }
    await _isolate.runForMultipleInputs(
      [waveform.buffer.asUint8List(waveform.offsetInBytes, waveform.lengthInBytes)],
      outputs,
    );

    final scores = (outputs[0] as Uint8List).buffer.asFloat32List(0, _numClasses);
    final embedding = Float32List.fromList(
      (outputs[1] as Uint8List).buffer.asFloat32List(0, _embeddingSize),
    );

    var gun = 0.0;
    for (final i in _gunIndices) {
      if (scores[i] > gun) gun = scores[i];
    }
    var top = 0;
    for (var i = 1; i < scores.length; i++) {
      if (scores[i] > scores[top]) top = i;
    }
    return ShotClassification(
      gunScore: gun,
      embedding: embedding,
      topLabel: top < _labels.length ? _labels[top] : '#$top',
    );
  }

  @override
  /// Callers must not close while a [classify] is running (the native
  /// interpreter would be freed under it); GunshotPipeline.close waits for that.
  Future<void> close() async {
    if (_closed) return;
    _closed = true;
    await _isolate.close();
    _interpreter.close();
  }
}
