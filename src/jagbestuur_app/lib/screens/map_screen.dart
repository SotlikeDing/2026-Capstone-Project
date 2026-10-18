import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:geolocator/geolocator.dart';
import 'package:latlong2/latlong.dart' as ll;

import '../models/hunt_pin.dart';
import '../models/hunt_session.dart';
import '../models/weather_info.dart';
import '../safety/app_error_barrier.dart';
import '../services/gunshot/gunshot_detector_service.dart';
import '../services/gunshot/gunshot_settings.dart';
import '../services/gunshot/shot_event_merger.dart';
import '../services/hunt_tracking_service.dart';
import '../services/location_service.dart';
import '../services/notification_permission.dart';
import '../services/weather_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../utils/hunt_pin_icons.dart';
import '../widgets/app_tile_layer.dart';
import '../widgets/gunshot_settings_sheet.dart';
import '../widgets/live_pin_list.dart';
import '../widgets/pin_drop_sheet.dart';
import '../widgets/sos_button.dart';
import 'hike_recap_screen.dart';

class MapScreen extends StatefulWidget {
  const MapScreen({super.key});

  @override
  State<MapScreen> createState() => _MapScreenState();
}

class _MapScreenState extends State<MapScreen> {
  final MapController _mapController = MapController();
  final HuntTrackingService _huntTrackingService = HuntTrackingService();
  final WeatherService _weatherService = WeatherService();

  // Outo-skoot: gunshot detection while a hunt runs.
  final GunshotDetectorService _gunshot = GunshotDetectorService();
  GunshotSettings _gunshotSettings = const GunshotSettings();
  StreamSubscription<ShotEventUpdate>? _gunshotSubscription;

  /// Shot event -> the pin it created (a Future, because follow-up shots can
  /// arrive while the first pin is still waiting for a GPS fix).
  final Map<String, Future<HuntPin?>> _shotEventPins = {};

  /// Pin id -> YAMNet embedding, kept in memory until the hunter confirms or
  /// removes the automatic pin (that judgement is the training label). Only
  /// pins still waiting for that judgement are in here.
  final Map<String, Float32List> _shotPinEmbeddings = {};

  /// Pin id -> the shot event that dropped it, to end the event when the pin goes.
  final Map<String, String> _shotPinEvents = {};

  StreamSubscription<Position>? _positionSubscription;
  Timer? _huntClockTimer;

  ll.LatLng? _currentLocation;
  Position? _lastPosition;

  bool _isHunting = false;
  bool _isStarting = false;
  bool _isStopping = false;
  bool _isLoadingWeather = false;

  /// Keep the map centred on the hunter until they pan away themselves.
  bool _followUser = true;

  double _huntSpeedKmh = 0;
  Duration _huntDuration = Duration.zero;

  WeatherInfo? _weather;

  bool get _isBusy => _isStarting || _isStopping;

  @override
  void initState() {
    super.initState();
    unawaited(
      _huntTrackingService.recoverOrphanedJournals().then<void>(
        (_) {},
        onError: (Object e) =>
            debugPrint('[MapScreen] Journal recovery failed: $e'),
      ),
    );
    _gunshotSubscription = _gunshot.events.listen(_onShotEvent);
    unawaited(
      GunshotSettings.load().then((s) {
        if (mounted) setState(() => _gunshotSettings = s);
      }),
    );
  }

  LocationSettings _trackingLocationSettings() {
    if (defaultTargetPlatform == TargetPlatform.android) {
      return AndroidSettings(
        accuracy: LocationAccuracy.bestForNavigation,
        distanceFilter: 1,
        intervalDuration: const Duration(seconds: 1),
        foregroundNotificationConfig: const ForegroundNotificationConfig(
          notificationTitle: 'Jagbestuur track jou jag',
          notificationText: 'Ligging word in die agtergrond aangeteken.',
          enableWakeLock: true,
        ),
      );
    }

    return const LocationSettings(
      accuracy: LocationAccuracy.bestForNavigation,
      distanceFilter: 1,
    );
  }

  void _showMessage(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  double _currentZoomOrDefault() {
    try {
      return _mapController.camera.zoom;
    } catch (_) {
      return 16;
    }
  }

  void _safeMoveMap(ll.LatLng point, double zoom) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) {
        return;
      }

      try {
        _mapController.move(point, zoom);
      } catch (_) {}
    });
  }

  void _startHuntClock() {
    _huntClockTimer?.cancel();

    _huntClockTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (!mounted || !_isHunting) {
        return;
      }

      final elapsed = _huntTrackingService.elapsedDuration;
      if (elapsed.inSeconds == _huntDuration.inSeconds) {
        return;
      }

      setState(() {
        _huntDuration = elapsed;
      });
    });
  }

  Future<void> _loadWeather() async {
    final location = _currentLocation;
    if (location == null) {
      _showMessage('Begin eers \'n jag om jou ligging en weer te laai.');
      return;
    }

    setState(() => _isLoadingWeather = true);

    try {
      final weather = await _weatherService.getWeatherForLocation(
        latitude: location.latitude,
        longitude: location.longitude,
      );

      if (!mounted) {
        return;
      }

      setState(() {
        _weather = weather;
        _isLoadingWeather = false;
      });
    } catch (e) {
      if (!mounted) {
        return;
      }

      setState(() => _isLoadingWeather = false);
      _showMessage(
        AppErrorMessage.from(e, fallback: 'Kon nie weerdata laai nie.'),
      );
    }
  }

  Future<void> _startHunt() async {
    if (_isBusy || _isHunting || _huntTrackingService.isTracking) {
      return;
    }

    // Set before the first await so a second tap can't start a second hunt.
    setState(() => _isStarting = true);

    try {
      final currentPosition = await LocationService.currentPosition(
        settings: _trackingLocationSettings(),
      );
      await NotificationPermission.request();

      await _huntTrackingService.startSession(initialPosition: currentPosition);

      if (!mounted) {
        return;
      }

      await _positionSubscription?.cancel();
      _positionSubscription = Geolocator.getPositionStream(
        locationSettings: _trackingLocationSettings(),
      ).listen(_handleTrackedPosition, onError: _handlePositionError);

      setState(() {
        _isHunting = true;
        _followUser = true;
        _currentLocation = ll.LatLng(
          currentPosition.latitude,
          currentPosition.longitude,
        );
        _lastPosition = currentPosition;
        _huntSpeedKmh = 0;
        _huntDuration = Duration.zero;
      });

      _safeMoveMap(_currentLocation!, 16);
      unawaited(_loadWeather());
      _startHuntClock();
      if (_gunshotSettings.enabled) {
        // Started here, while the map is on screen: Android only allows a
        // background microphone if it was opened while the app was visible.
        unawaited(_startListening());
      }
    } on LocationUnavailableException catch (e) {
      _showMessage(e.message);
    } catch (e, stack) {
      AppErrorBarrier.report('Start hunt', e, stack);
      _showMessage(AppErrorMessage.from(e, fallback: 'Kon nie jag begin nie.'));
    } finally {
      if (mounted) {
        setState(() => _isStarting = false);
      }
    }
  }

  void _handlePositionError(Object error) {
    AppErrorBarrier.report('Position stream', error);
    _showMessage(
      error is LocationServiceDisabledException
          ? 'GPS is afgeskakel. Skakel dit weer aan om die jag voort te sit.'
          : 'GPS-sein verloor. Die jag word steeds aangeteken.',
    );
  }

  void _handleTrackedPosition(Position position) {
    if (!mounted || !_isHunting) {
      return;
    }

    if (position.accuracy > 35) {
      return;
    }

    final previous = _lastPosition;
    if (previous != null) {
      final jumpMeters = Geolocator.distanceBetween(
        previous.latitude,
        previous.longitude,
        position.latitude,
        position.longitude,
      );

      final seconds =
          position.timestamp.difference(previous.timestamp).inMilliseconds /
          1000.0;

      if (seconds > 0 && (jumpMeters / seconds) * 3.6 > 220) {
        return;
      }
    }

    unawaited(
      _huntTrackingService
          .recordPosition(position)
          .then<void>(
            (_) {},
            onError: (Object e) => AppErrorBarrier.report('Record position', e),
          ),
    );

    setState(() {
      _currentLocation = ll.LatLng(position.latitude, position.longitude);
      _huntSpeedKmh = position.speed > 0 ? position.speed * 3.6 : 0;
      _lastPosition = position;
      _huntDuration = _huntTrackingService.elapsedDuration;
    });

    if (_followUser) {
      _safeMoveMap(_currentLocation!, _currentZoomOrDefault());
    }
  }

  Future<void> _stopHunt() async {
    if (!_isHunting || _isBusy) {
      return;
    }

    setState(() => _isStopping = true);

    _huntClockTimer?.cancel();
    _huntClockTimer = null;

    HuntSession? session;
    try {
      await _stopListening();
      await _positionSubscription?.cancel();
      _positionSubscription = null;
      session = await _huntTrackingService.stopSession();
    } catch (e, stack) {
      AppErrorBarrier.report('Stop hunt', e, stack);
      _showMessage(
        'Die jag kon nie behoorlik gestoor word nie. Die roete word herstel '
        'wanneer jy die kaart weer oopmaak.',
      );
    }

    if (!mounted) {
      return;
    }

    // Keep the UI in step with the service whatever happened above.
    setState(() {
      _isStopping = false;
      _isHunting = _huntTrackingService.isTracking;
      _huntSpeedKmh = 0;
      _lastPosition = null;
      _huntDuration = _huntTrackingService.elapsedDuration;
    });

    final finished = session;
    if (finished == null) {
      return;
    }

    if (finished.waypoints.isEmpty) {
      _showMessage(
        'Die jag is gestoor, maar geen roete-punte is opgeneem nie.',
      );
      return;
    }

    await Navigator.push(
      context,
      MaterialPageRoute(
        builder: (_) => HikeRecapScreen(
          keyframes: finished.waypoints,
          pins: finished.pins,
          sessionId: finished.id,
          sessionStartedAt: finished.startedAt,
        ),
      ),
    );
  }

  Future<void> _onDropPinPressed() async {
    if (!_isHunting) {
      _showMessage('Begin eers \'n jag om merkers te plaas.');
      return;
    }

    final result = await PinDropSheet.show(context);
    if (result == null) {
      return;
    }

    try {
      await _huntTrackingService.dropPin(type: result.type, note: result.note);
      if (!mounted) {
        return;
      }
      setState(() {});
    } catch (e) {
      _showMessage(AppErrorMessage.from(e, fallback: 'Kon nie pen los nie.'));
    }
  }

  // ------------------------------------------------------------------ Outo-skoot

  Future<void> _startListening() async {
    final error = await _gunshot.start(_gunshotSettings);
    if (error != null) _showMessage(error);
    if (mounted) setState(() {});
  }

  Future<void> _stopListening() async {
    await _gunshot.stop();
    _shotEventPins.clear();
    _shotPinEmbeddings.clear();
    _shotPinEvents.clear();
    if (mounted) setState(() {});
  }

  /// The pin goes where the phone is right after the shot: use the tracked fix
  /// when it is recent, otherwise ask the GPS for a fresh one (max 5 s).
  Future<Position?> _freshShotPosition() async {
    final latest = _huntTrackingService.latestPosition;
    if (latest != null && DateTime.now().difference(latest.timestamp) < const Duration(seconds: 10)) {
      return null;
    }
    try {
      return await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
          timeLimit: Duration(seconds: 5),
        ),
      );
    } catch (_) {
      return null; // fall back to the latest tracked fix
    }
  }

  void _onShotEvent(ShotEventUpdate update) {
    if (!_isHunting || !_huntTrackingService.isTracking) return;
    final event = update.event;

    if (update.isNew) {
      _shotEventPins[event.id] = _dropShotPin(event);
      return;
    }
    // Follow-up shot / echo: update the same pin ("2 skote").
    final pending = _shotEventPins[event.id];
    if (pending == null) return;
    unawaited(_updateShotPin(pending, event));
  }

  Future<void> _updateShotPin(Future<HuntPin?> pending, ShotEvent event) async {
    try {
      final pin = await pending;
      if (pin == null) return; // the drop failed; the event was ended there
      // Changed on the pin as stored when the write runs, so a confirmation
      // made a moment ago is kept.
      final updated = await _huntTrackingService.updatePinWith(
        pin.id,
        (current) => current.copyWith(note: event.note, confidence: event.confidence),
      );
      if (updated == null) {
        _gunshot.endEvent(event.id);
        return;
      }
      // The event's best embedding may have changed; only matters while the
      // hunter has not judged the pin yet.
      if (_shotPinEmbeddings.containsKey(pin.id)) _shotPinEmbeddings[pin.id] = event.embedding;
      if (mounted) setState(() {});
    } catch (e, stack) {
      AppErrorBarrier.report('Auto shot pin update', e, stack);
    }
  }

  Future<HuntPin?> _dropShotPin(ShotEvent event) async {
    try {
      final position = await _freshShotPosition();
      final pin = await _huntTrackingService.dropPin(
        type: HuntPinType.shotFired,
        note: event.note,
        position: position,
        timestamp: event.firstAt, // when the shot was heard, not after the GPS wait
        autoDetected: true,
        confidence: event.confidence,
      );
      _shotPinEmbeddings[pin.id] = event.embedding;
      _shotPinEvents[pin.id] = event.id;
      unawaited(HapticFeedback.heavyImpact());
      if (mounted) {
        setState(() {});
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Skoot gehoor - pen gelos (${(event.confidence * 100).round()}%).'),
            duration: const Duration(seconds: 6),
            action: SnackBarAction(label: 'Ongedaan maak', onPressed: () => _onRemovePin(pin.id)),
          ),
        );
      }
      return pin;
    } catch (e, stack) {
      AppErrorBarrier.report('Auto shot pin', e, stack);
      _gunshot.endEvent(event.id); // the next shot gets its own pin
      return null;
    }
  }

  Future<void> _onConfirmPin(String pinId) async {
    // Taken out first, so a follow-up shot arriving meanwhile cannot put it back.
    final embedding = _shotPinEmbeddings.remove(pinId);
    try {
      final updated = await _huntTrackingService.updatePinWith(pinId, (p) => p.copyWith(confirmed: true));
      if (updated != null && embedding != null) unawaited(_gunshot.label(embedding, isGunshot: true));
    } catch (e, stack) {
      if (embedding != null) _shotPinEmbeddings[pinId] = embedding; // not confirmed: still awaiting judgement
      AppErrorBarrier.report('Confirm pin', e, stack);
    }
    if (mounted) setState(() {});
  }

  Future<void> _openGunshotSettings() async {
    await _gunshot.loadLearning(); // show what was learned before the first listening hunt too
    if (!mounted) return;
    final result = await GunshotSettingsSheet.show(
      context,
      initial: _gunshotSettings,
      head: _gunshot.head,
      onForget: _gunshot.forgetLearning,
      stats: _gunshot.stats,
      listening: _gunshot.listening.value,
    );
    if (result == null || !mounted) return;
    setState(() => _gunshotSettings = result);
    try {
      await result.save();
    } catch (e) {
      debugPrint('[MapScreen] Could not save Outo-skoot settings: $e');
    }
    if (!_isHunting) {
      if (result.enabled) _showMessage('Outo-skoot begin luister sodra jy \'n jag begin.');
      return;
    }
    if (result.enabled && !_gunshot.listening.value) {
      await _startListening();
    } else if (!result.enabled && _gunshot.listening.value) {
      await _stopListening();
    } else {
      _gunshot.updateSettings(result);
    }
  }

  Future<void> _onRemovePin(String pinId) async {
    final removed = _huntTrackingService.livePins.where((p) => p.id == pinId).firstOrNull;
    final embedding = _shotPinEmbeddings.remove(pinId);
    final eventId = _shotPinEvents.remove(pinId);
    // Shots right after this one must not join an event whose pin is gone.
    if (eventId != null) _gunshot.endEvent(eventId);
    if (removed != null && removed.needsReview && embedding != null) {
      // Removing an automatic pin = "that was not a shot": a training label.
      unawaited(_gunshot.label(embedding, isGunshot: false));
    }
    try {
      await _huntTrackingService.removePin(pinId);
    } catch (e, stack) {
      AppErrorBarrier.report('Remove pin', e, stack);
      _showMessage('Kon nie pen verwyder nie.');
    }
    if (!mounted) {
      return;
    }
    setState(() {});
  }

  void _recenterMap() {
    final location = _currentLocation;
    if (location == null) {
      _showMessage('Geen aktiewe ligging nie. Begin eers \'n jag.');
      return;
    }

    setState(() => _followUser = true);
    _safeMoveMap(location, 16);
  }

  void _onMapPositionChanged(MapCamera camera, bool hasGesture) {
    // A drag or pinch by the user pauses follow mode until they recentre.
    if (hasGesture && _followUser && _isHunting) {
      setState(() => _followUser = false);
    }
  }

  String _formatDuration(Duration d) {
    String two(int n) => n.toString().padLeft(2, '0');
    final hours = two(d.inHours);
    final minutes = two(d.inMinutes.remainder(60));
    final seconds = two(d.inSeconds.remainder(60));
    return '$hours:$minutes:$seconds';
  }

  @override
  void dispose() {
    _huntClockTimer?.cancel();
    _positionSubscription?.cancel();
    _gunshotSubscription?.cancel();
    unawaited(_gunshot.dispose());
    // Finishes and saves an active hunt rather than abandoning it.
    _huntTrackingService.dispose();
    _mapController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final pins = _huntTrackingService.livePins;
    final bottomOffset = MediaQuery.of(context).padding.bottom + 96.0;

    return Scaffold(
      body: Stack(
        children: [
          FlutterMap(
            mapController: _mapController,
            options: MapOptions(
              initialCenter: const ll.LatLng(-25.7, 28.2),
              initialZoom: 15,
              onPositionChanged: _onMapPositionChanged,
            ),
            children: [
              const AppTileLayer(),
              MarkerLayer(
                markers: [
                  if (_currentLocation != null)
                    Marker(
                      point: _currentLocation!,
                      width: 36,
                      height: 36,
                      child: Container(
                        decoration: BoxDecoration(
                          color: AppTheme.olive,
                          shape: BoxShape.circle,
                          border: Border.all(color: AppTheme.gold, width: 2.5),
                        ),
                        child: const Icon(
                          Icons.navigation_rounded,
                          color: AppTheme.cream,
                          size: 18,
                        ),
                      ),
                    ),
                  ...pins.map(
                    (pin) => Marker(
                      point: ll.LatLng(pin.latitude, pin.longitude),
                      width: 34,
                      height: 34,
                      child: Tooltip(
                        message: pin.needsReview ? '${pin.type.label} (outomaties - bevestig of verwyder)' : pin.type.label,
                        child: Container(
                          decoration: BoxDecoration(
                            color: AppTheme.card,
                            shape: BoxShape.circle,
                            border: Border.all(
                              color: pin.needsReview ? AppTheme.dangerSoft : AppTheme.gold,
                              width: 2,
                            ),
                          ),
                          child: Icon(
                            iconForHuntPin(pin.type),
                            color: pin.needsReview ? AppTheme.dangerSoft : AppTheme.gold,
                            size: 16,
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ),
          Positioned(
            left: 16,
            right: 88,
            top: MediaQuery.of(context).padding.top + 8,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _InfoCard(
                  icon: Icons.terrain_rounded,
                  title: 'Jou ligging',
                  subtitle: _currentLocation == null
                      ? 'Begin \'n jag om jou ligging te wys.'
                      : '${_currentLocation!.latitude.toStringAsFixed(5)}, '
                            '${_currentLocation!.longitude.toStringAsFixed(5)}',
                  badgeText: !_isHunting
                      ? null
                      : !_gunshot.listening.value
                      ? 'JAG AKTIEF'
                      : _gunshot.screenOffListening.value
                      ? 'JAG AKTIEF • LUISTER VIR SKOTE'
                      : 'JAG AKTIEF • LUISTER (SKERM AAN)',
                ),
                const SizedBox(height: 10),
                if (_isLoadingWeather)
                  const _InfoCard(
                    icon: Icons.cloud_outlined,
                    title: 'Laai weer...',
                    subtitle: '',
                  )
                else if (_weather != null)
                  _InfoCard(
                    icon: Icons.wb_sunny_rounded,
                    title: '${_weather!.temperature.toStringAsFixed(0)}°C',
                    subtitle:
                        '${_weather!.condition} • Wind ${_weather!.windSpeed.toStringAsFixed(0)} km/h',
                  )
                else
                  const _InfoCard(
                    icon: Icons.wb_cloudy_outlined,
                    title: 'Weer nie gelaai nie',
                    subtitle: 'Begin \'n jag of druk verfris.',
                  ),
              ],
            ),
          ),
          Positioned(
            right: 16,
            top: MediaQuery.of(context).padding.top + 8,
            child: Column(
              children: [
                _ToolbarButton(
                  icon: _followUser
                      ? Icons.my_location_rounded
                      : Icons.location_searching_rounded,
                  onPressed: _recenterMap,
                  highlighted: _followUser,
                ),
                const SizedBox(height: 10),
                _ToolbarButton(
                  icon: Icons.add_location_alt_rounded,
                  onPressed: _onDropPinPressed,
                ),
                const SizedBox(height: 10),
                _ToolbarButton(
                  icon: Icons.refresh_rounded,
                  onPressed: _loadWeather,
                ),
                const SizedBox(height: 10),
                ValueListenableBuilder<bool>(
                  valueListenable: _gunshot.listening,
                  builder: (context, listening, _) => _ToolbarButton(
                    icon: listening || _gunshotSettings.enabled
                        ? Icons.hearing_rounded
                        : Icons.hearing_disabled_rounded,
                    onPressed: _openGunshotSettings,
                    highlighted: listening,
                  ),
                ),
              ],
            ),
          ),
          Positioned(
            left: 16,
            right: 16,
            bottom: bottomOffset,
            child: Column(
              children: [
                if (pins.isNotEmpty)
                  Container(
                    constraints: const BoxConstraints(maxHeight: 180),
                    margin: const EdgeInsets.only(bottom: 12),
                    padding: const EdgeInsets.all(8),
                    decoration: BoxDecoration(
                      color: AppTheme.card,
                      borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
                      border: Border.all(color: AppTheme.border),
                    ),
                    child: SingleChildScrollView(
                      child: LivePinList(pins: pins, onRemove: _onRemovePin, onConfirm: _onConfirmPin),
                    ),
                  ),
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 20,
                    vertical: 14,
                  ),
                  decoration: BoxDecoration(
                    color: AppTheme.card,
                    borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
                    border: Border.all(color: AppTheme.border),
                  ),
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      _StatItem(
                        icon: Icons.straighten_rounded,
                        value:
                            '${_huntTrackingService.totalDistanceMeters.toStringAsFixed(0)} m',
                        label: 'Afstand',
                      ),
                      _StatItem(
                        icon: Icons.speed_rounded,
                        value: '${_huntSpeedKmh.toStringAsFixed(1)} km/h',
                        label: 'Speed',
                      ),
                      _StatItem(
                        icon: Icons.timer_outlined,
                        value: _formatDuration(_huntDuration),
                        label: 'Jag tyd',
                      ),
                      _StatItem(
                        icon: Icons.location_on_outlined,
                        value: '${pins.length}',
                        label: 'Merkers',
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                Row(
                  children: [
                    Expanded(
                      child: ElevatedButton.icon(
                        onPressed: _isBusy
                            ? null
                            : (_isHunting ? _stopHunt : _startHunt),
                        icon: _isBusy
                            ? const SizedBox(
                                width: 18,
                                height: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  color: Colors.white,
                                ),
                              )
                            : Icon(
                                _isHunting
                                    ? Icons.stop_rounded
                                    : Icons.play_arrow_rounded,
                              ),
                        label: Text(
                          _isStarting
                              ? 'Soek GPS...'
                              : _isStopping
                              ? 'Stoor jag...'
                              : _isHunting
                              ? 'Eindig jag'
                              : 'Begin jag',
                        ),
                        style: ElevatedButton.styleFrom(
                          backgroundColor: _isHunting
                              ? AppTheme.dangerSoft
                              : AppTheme.olive,
                          foregroundColor: Colors.white,
                          padding: const EdgeInsets.symmetric(vertical: 14),
                        ),
                      ),
                    ),
                    const SizedBox(width: 12),
                    const Expanded(child: SosButton()),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _InfoCard extends StatelessWidget {
  const _InfoCard({
    required this.icon,
    required this.title,
    required this.subtitle,
    this.badgeText,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final String? badgeText;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      decoration: BoxDecoration(
        color: AppTheme.card.withValues(alpha: 0.95),
        borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
        border: Border.all(color: AppTheme.border),
      ),
      child: Row(
        children: [
          Icon(icon, color: AppTheme.gold, size: 22),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                if (badgeText != null)
                  Text(
                    badgeText!,
                    style: const TextStyle(
                      color: AppTheme.gold,
                      fontSize: 10,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 0.5,
                    ),
                  ),
                Text(
                  title,
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontWeight: FontWeight.w700,
                    fontSize: 15,
                  ),
                ),
                if (subtitle.isNotEmpty)
                  Text(
                    subtitle,
                    style: const TextStyle(color: AppTheme.muted, fontSize: 12),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _ToolbarButton extends StatelessWidget {
  const _ToolbarButton({
    required this.icon,
    required this.onPressed,
    this.highlighted = false,
  });

  final IconData icon;
  final VoidCallback? onPressed;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: highlighted ? AppTheme.olive : AppTheme.card,
      shape: const CircleBorder(),
      elevation: 2,
      child: InkWell(
        onTap: onPressed,
        customBorder: const CircleBorder(),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Icon(
            icon,
            color: onPressed == null
                ? AppTheme.muted
                : (highlighted ? AppTheme.cream : AppTheme.ink),
            size: 22,
          ),
        ),
      ),
    );
  }
}

class _StatItem extends StatelessWidget {
  const _StatItem({
    required this.icon,
    required this.value,
    required this.label,
  });

  final IconData icon;
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Icon(icon, color: AppTheme.gold, size: 18),
        const SizedBox(height: 4),
        Text(
          value,
          style: const TextStyle(
            color: AppTheme.ink,
            fontWeight: FontWeight.w800,
            fontSize: 13,
          ),
        ),
        Text(
          label,
          style: const TextStyle(color: AppTheme.muted, fontSize: 11),
        ),
      ],
    );
  }
}
