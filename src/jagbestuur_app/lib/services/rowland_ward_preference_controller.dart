import 'package:flutter/foundation.dart';

import 'rowland_ward_preference_service.dart';

class RowlandWardPreferenceState {
  const RowlandWardPreferenceState({
    this.enabled = false,
    this.isLoading = false,
    this.isSaving = false,
    this.loadFailed = false,
  });

  final bool enabled;
  final bool isLoading;
  final bool isSaving;
  final bool loadFailed;

  RowlandWardPreferenceState copyWith({
    bool? enabled,
    bool? isLoading,
    bool? isSaving,
    bool? loadFailed,
  }) {
    return RowlandWardPreferenceState(
      enabled: enabled ?? this.enabled,
      isLoading: isLoading ?? this.isLoading,
      isSaving: isSaving ?? this.isSaving,
      loadFailed: loadFailed ?? this.loadFailed,
    );
  }
}

class RowlandWardPreferenceController
    extends ValueNotifier<RowlandWardPreferenceState> {
  RowlandWardPreferenceController({RowlandWardPreferenceApi? api})
    : _api = api ?? RowlandWardPreferenceService(),
      super(const RowlandWardPreferenceState());

  static final RowlandWardPreferenceController instance =
      RowlandWardPreferenceController();

  final RowlandWardPreferenceApi _api;

  int? _activeUserId;
  bool _hasLoaded = false;
  Future<void>? _loadFuture;

  bool get isEnabled => value.enabled;

  Future<void> loadForUser(int userId, {bool force = false}) {
    if (_activeUserId != userId) {
      _activeUserId = userId;
      _hasLoaded = false;
      _loadFuture = null;
      value = const RowlandWardPreferenceState();
    }

    if (_hasLoaded && !force) {
      return Future<void>.value();
    }

    final activeLoad = _loadFuture;
    if (activeLoad != null) {
      return activeLoad;
    }

    final requestedUserId = userId;
    final load = _load(requestedUserId);
    _loadFuture = load;
    return load;
  }

  Future<void> _load(int requestedUserId) async {
    value = value.copyWith(isLoading: true, loadFailed: false);

    try {
      final enabled = await _api.getEnabled();
      if (_activeUserId != requestedUserId) return;

      _hasLoaded = true;
      value = RowlandWardPreferenceState(enabled: enabled);
    } catch (_) {
      if (_activeUserId != requestedUserId) return;

      value = RowlandWardPreferenceState(
        enabled: value.enabled,
        loadFailed: true,
      );
    } finally {
      if (_activeUserId == requestedUserId) {
        _loadFuture = null;
      }
    }
  }

  Future<bool> setEnabled(bool enabled) async {
    if (value.isLoading || value.isSaving || _activeUserId == null) {
      return false;
    }

    final requestedUserId = _activeUserId;
    final previousEnabled = value.enabled;
    value = value.copyWith(isSaving: true, loadFailed: false);

    try {
      final savedEnabled = await _api.updateEnabled(enabled);
      if (_activeUserId != requestedUserId) return false;

      _hasLoaded = true;
      value = RowlandWardPreferenceState(enabled: savedEnabled);
      return true;
    } catch (_) {
      if (_activeUserId == requestedUserId) {
        value = RowlandWardPreferenceState(enabled: previousEnabled);
      }
      return false;
    }
  }

  void reset() {
    _activeUserId = null;
    _hasLoaded = false;
    _loadFuture = null;
    value = const RowlandWardPreferenceState();
  }
}
