import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart' as ll;
import 'package:shared_preferences/shared_preferences.dart';
import 'package:video_player/video_player.dart';

import '../config/app_config.dart';
import '../models/hunt_photo.dart';
import '../models/hunt_pin.dart';
import '../models/hunt_session.dart';
import '../models/smart_waypoint.dart';
import '../safety/app_error_barrier.dart';
import '../services/auth_service.dart';
import '../services/hunt_media_service.dart';
import '../services/hunt_photo_matcher.dart';
import '../services/user_storage.dart';
import '../services/video_export_service.dart';
import '../services/video_storage_service.dart';
import '../theme/app_theme.dart';
import '../utils/hunt_pin_icons.dart';
import '../utils/recap_timeline.dart';
import '../widgets/app_tile_layer.dart';
import 'hunt_photo_manager_screen.dart';

class HikeRecapScreen extends StatefulWidget {
  const HikeRecapScreen({
    super.key,
    required this.keyframes,
    this.pins = const <HuntPin>[],
    this.sessionId,
    this.sessionStartedAt,
    this.trophyId,
    this.trophyName,
    this.trophyPhotoPath,
    this.autoStartVideoExport = false,
    this.videoStorageOverride,
    this.mediaStorageOverride,
  });

  final List<SmartWaypoint> keyframes;
  final List<HuntPin> pins;
  final String? sessionId;
  final DateTime? sessionStartedAt;
  final int? trophyId;
  final String? trophyName;
  final String? trophyPhotoPath;
  final bool autoStartVideoExport;
  final VideoStorageService? videoStorageOverride;
  final HuntMediaService? mediaStorageOverride;

  @override
  State<HikeRecapScreen> createState() => _HikeRecapScreenState();
}

class _HikeRecapScreenState extends State<HikeRecapScreen>
    with SingleTickerProviderStateMixin {
  static const double _defaultZoom = 16.0;
  static const int _firstFrameSettleMs = 1500;
  static const int _overviewSettleMs = 1000;
  static const int _zoomFrameSettleMs = 120;
  static const int _panSettleMs = 220;

  /// The camera may drift this far before the exporter waits for new tiles.
  static const double _panSettleMeters = 60;
  static const int _photoCacheWidth = 1080;

  final MapController _mapController = MapController();
  final GlobalKey _repaintKey = GlobalKey();
  late final VideoStorageService _videoStorage =
      widget.videoStorageOverride ?? const VideoStorageService();
  late final HuntMediaService _mediaStorage =
      widget.mediaStorageOverride ?? HuntMediaService();

  Ticker? _ticker;
  late RecapTimeline _timeline = _buildTimeline();
  late final List<ll.LatLng> _routePoints = [
    for (final point in widget.keyframes)
      ll.LatLng(point.latitude, point.longitude),
  ];
  late final Map<String, int> _pinIndex = {
    for (final pin in widget.pins)
      pin.id: RecapTimeline.nearestIndex(widget.keyframes, pin.timestamp),
  };

  bool _isPlaying = false;
  bool _isExporting = false;
  bool _isDisposed = false;
  double _exportProgress = 0;
  double _seconds = 0;
  double _playStartSeconds = 0;
  List<HuntPhoto> _photos = const <HuntPhoto>[];
  bool _photosLoading = true;
  bool _cancelRequested = false;
  VideoExportCancellationToken? _cancellationToken;
  bool _autoExportStarted = false;
  SavedRecapVideo? _latestSavedVideo;

  bool get _hasFrames => widget.keyframes.isNotEmpty;

  RecapFrame get _frame => _timeline.frameAt(_seconds);

  RecapTimeline _buildTimeline() => RecapTimeline.build(
    keyframes: widget.keyframes,
    pins: widget.pins,
    photos: _photos,
  );

  @override
  void initState() {
    super.initState();
    _seconds = _timeline.mapStartSeconds;
    _loadPhotos();
    _loadSavedVideo();
    HuntMediaService.mediaVersion.addListener(_onMediaChanged);
    VideoStorageService.libraryVersion.addListener(_onVideosChanged);

    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_hasFrames && mounted) {
        _applyCamera(_frame);
      }
    });
  }

  @override
  void dispose() {
    _isDisposed = true;
    _ticker?.dispose();
    VideoStorageService.libraryVersion.removeListener(_onVideosChanged);
    HuntMediaService.mediaVersion.removeListener(_onMediaChanged);

    // Leaving mid-export: stop FFmpeg so it doesn't keep burning CPU and
    // writing to disk for a screen that no longer exists. The export's own
    // finally block deletes the frames and any partial output.
    if (_isExporting) {
      unawaited(VideoExportService.cancelActiveEncoding());
    }

    _mapController.dispose();
    super.dispose();
  }

  void _onMediaChanged() {
    if (mounted && !_isExporting) {
      _loadPhotos();
      _loadSavedVideo();
    }
  }

  void _onVideosChanged() {
    if (mounted && !_isExporting) {
      _loadSavedVideo();
    }
  }

  Future<void> _loadSavedVideo() async {
    final sessionId = widget.sessionId;
    final trophyId = widget.trophyId;
    if (sessionId == null && trophyId == null) {
      return;
    }
    try {
      final videos = await _videoStorage.listVideos();
      final match = videos.where((v) {
        if (sessionId != null && v.sessionId == sessionId) {
          return true;
        }
        if (trophyId != null && v.trophyId == trophyId) {
          return true;
        }
        return false;
      }).firstOrNull;
      if (mounted && match != null) {
        setState(() => _latestSavedVideo = match);
      }
    } catch (_) {}
  }

  void _checkAutoExport() {
    if (widget.autoStartVideoExport && !_autoExportStarted) {
      _autoExportStarted = true;
      WidgetsBinding.instance.addPostFrameCallback((_) => _generateVideo());
    }
  }

  Future<void> _loadPhotos() async {
    final sessionId = widget.sessionId;
    if (sessionId == null) {
      if (mounted) {
        setState(() {
          _photos = const <HuntPhoto>[];
          _photosLoading = false;
        });
      }
      _checkAutoExport();
      return;
    }
    try {
      final photos = await _mediaStorage.listPhotosForSession(sessionId);
      if (!mounted) {
        return;
      }
      setState(() {
        _photos = photos;
        _photosLoading = false;
        _timeline = _buildTimeline();
        _seconds = _seconds.clamp(
          _timeline.mapStartSeconds,
          _timeline.mapEndSeconds,
        );
      });
      _checkAutoExport();
    } catch (error, stack) {
      AppErrorBarrier.report('Load recap photos', error, stack);
      if (mounted) {
        setState(() => _photosLoading = false);
      }
    }
  }

  Future<void> _openPhotoManager() async {
    final sessionId = widget.sessionId;
    if (sessionId == null || widget.keyframes.isEmpty) {
      _showExportError(
        'Hierdie terugblik is nie aan ’n gestoorde jag gekoppel nie.',
      );
      return;
    }
    final session = await _loadSessionForPhotoManager(sessionId);
    if (!mounted || session == null) {
      return;
    }
    _pause();
    await Navigator.push(
      context,
      MaterialPageRoute(
        builder: (_) =>
            HuntPhotoManagerScreen(session: session, trophyId: widget.trophyId),
      ),
    );
    await _loadPhotos();
  }

  Future<HuntSession?> _loadSessionForPhotoManager(String sessionId) async {
    // The recap already has the authoritative route/pin data; reconstructing
    // the session avoids a second parser and still writes against the same id.
    final start = widget.sessionStartedAt ?? widget.keyframes.first.timestamp;
    return HuntSession(
      id: sessionId,
      startedAt: start,
      endedAt: widget.keyframes.last.timestamp,
      totalDistanceMeters: _timeline.totalMeters,
      waypoints: widget.keyframes,
      pins: widget.pins,
    );
  }

  // --- Camera -------------------------------------------------------------

  ll.LatLng _pointAt(double position) {
    final last = _routePoints.length - 1;
    final i = position.floor().clamp(0, last);
    final j = (i + 1).clamp(0, last);
    final f = (position - i).clamp(0.0, 1.0);
    final a = _routePoints[i];
    final b = _routePoints[j];
    return ll.LatLng(
      a.latitude + (b.latitude - a.latitude) * f,
      a.longitude + (b.longitude - a.longitude) * f,
    );
  }

  /// The camera that shows the whole route, or null before the map has a size.
  MapCamera? _overviewCamera() {
    try {
      final camera = CameraFit.bounds(
        bounds: LatLngBounds.fromPoints(_routePoints),
        padding: const EdgeInsets.fromLTRB(40, 60, 40, 150),
        maxZoom: _defaultZoom,
      ).fit(_mapController.camera);
      return camera.zoom.isFinite ? camera : null;
    } catch (_) {
      return null;
    }
  }

  /// Follows the hunter, or blends towards the whole-route view.
  void _applyCamera(RecapFrame frame) {
    if (frame.phase != RecapPhase.map) {
      return;
    }
    var center = _pointAt(frame.position);
    var zoom = _defaultZoom;
    if (frame.overview > 0) {
      final fit = _overviewCamera();
      if (fit != null) {
        final o = frame.overview;
        center = ll.LatLng(
          center.latitude + (fit.center.latitude - center.latitude) * o,
          center.longitude + (fit.center.longitude - center.longitude) * o,
        );
        zoom = zoom + (fit.zoom - zoom) * o;
      }
    }
    try {
      _mapController.move(center, zoom);
    } catch (_) {
      // The map is not laid out yet; the next frame moves it.
    }
  }

  // --- Live preview -------------------------------------------------------

  void _seek(double seconds) {
    setState(() {
      _seconds = seconds.clamp(
        _timeline.mapStartSeconds,
        _timeline.mapEndSeconds,
      );
    });
    _applyCamera(_frame);
  }

  void _onTick(Duration elapsed) {
    if (!mounted) {
      return;
    }
    final t = _playStartSeconds + elapsed.inMicroseconds / 1e6;
    if (t >= _timeline.mapEndSeconds) {
      _seek(_timeline.mapEndSeconds);
      _pause();
      return;
    }
    _seek(t);
  }

  void _play() {
    if (!_hasFrames || _isPlaying || _isExporting) {
      return;
    }
    if (_seconds >= _timeline.mapEndSeconds - 0.05) {
      _seconds = _timeline.mapStartSeconds;
    }
    _playStartSeconds = _seconds;
    setState(() => _isPlaying = true);
    final ticker = _ticker ??= createTicker(_onTick);
    ticker.start();
  }

  void _pause() {
    final ticker = _ticker;
    if (ticker != null && ticker.isActive) {
      ticker.stop();
    }
    if (!mounted || !_isPlaying) {
      return;
    }
    setState(() => _isPlaying = false);
  }

  void _restart() {
    _pause();
    if (_hasFrames) {
      _seek(_timeline.mapStartSeconds);
    }
  }

  // --- Video export -------------------------------------------------------

  Future<void> _captureFrameOrThrow(Directory framesDir, int frameIndex) async {
    for (var attempt = 0; attempt < 4; attempt++) {
      await WidgetsBinding.instance.endOfFrame;

      final file = await VideoExportService.captureFrame(
        boundaryKey: _repaintKey,
        framesDir: framesDir,
        frameIndex: frameIndex,
      );

      if (file != null) {
        return;
      }

      await Future.delayed(const Duration(milliseconds: 60));
    }

    throw VideoExportException('Kon nie video-raam vasvang nie.');
  }

  ImageProvider<Object> _photoProvider(HuntPhoto photo) =>
      ResizeImage(FileImage(File(photo.localPath)), width: _photoCacheWidth);

  Future<void> _precachePhoto(HuntPhoto photo) async {
    if (!mounted) {
      return;
    }
    try {
      await precacheImage(_photoProvider(photo), context);
    } catch (_) {
      // The full-screen photo has its own error fallback.
    }
  }

  Future<void> _generateVideo() async {
    if (!_hasFrames || _isExporting || _photosLoading) {
      return;
    }

    _pause();
    Directory? framesDir;
    File? encodedFile;
    String? exportId;

    try {
      exportId = VideoExportService.beginExclusiveExport();
      final ownerUserId = UserStorage.requireUserId();
      for (final photo in _photos.take(60)) {
        if (!await File(photo.localPath).exists()) {
          throw VideoExportException(
            '’n Jagfoto ontbreek. Verwyder of vervang dit voor die video gemaak word.',
          );
        }
      }
      final fps = VideoExportService.framesPerSecond;
      await VideoExportService.validatePreflight(
        frameCount: _timeline.frameCount(fps),
        uniqueFrameCount: _timeline.uniqueFrameCount(fps),
        photoCount: _photos.length,
      );

      if (!mounted) {
        return;
      }
      setState(() {
        _isExporting = true;
        _cancelRequested = false;
        _cancellationToken = VideoExportCancellationToken();
        _exportProgress = 0;
        _seconds = 0;
      });

      await _precacheTrophyImage();
      framesDir = await VideoExportService.createFramesDirectory();
      final frameCount = await _captureAllFrames(framesDir);
      if (frameCount == null || _isDisposed || _cancelRequested) {
        return;
      }

      encodedFile = await VideoExportService.encodeFramesToVideo(
        framesDir: framesDir,
      );
      if (_isDisposed || _cancelRequested) {
        return;
      }
      if (UserStorage.requireUserId() != ownerUserId) {
        throw VideoExportException(
          'Die gebruiker het verander terwyl die video gemaak is.',
        );
      }

      final savedVideo = await _videoStorage.saveGeneratedVideo(
        generatedFile: encodedFile,
        durationSeconds: (frameCount / fps).ceil(),
        sessionId: widget.sessionId,
        trophyId: widget.trophyId,
        createdAt: widget.sessionStartedAt ?? DateTime.now(),
        quality: 'standaard',
      );
      encodedFile = null;
      if (mounted) {
        setState(() {
          _latestSavedVideo = savedVideo;
        });
      }
      _showExportResult(savedVideo);
    } on VideoExportException catch (e, stack) {
      AppErrorBarrier.report('Recap video export', e, stack);
      _showExportError(e.message);
    } catch (e, stack) {
      AppErrorBarrier.report('Recap video export', e, stack);
      _showExportError('Kon nie die video maak nie. Probeer asseblief weer.');
    } finally {
      await VideoExportService.deleteQuietly(encodedFile);
      if (framesDir != null) {
        await VideoExportService.cleanupFrames(framesDir);
      }
      if (exportId != null) {
        VideoExportService.finishExclusiveExport(exportId);
      }

      if (mounted) {
        setState(() {
          _isExporting = false;
          _cancellationToken = null;
          _seconds = _timeline.mapStartSeconds;
        });
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted) _applyCamera(_frame);
        });
      }
    }
  }

  /// Renders the timeline frame by frame. Frames identical to the previous
  /// one (still photos, the intro and summary cards) are linked instead of
  /// drawn again, and the camera only waits for map tiles when it has moved
  /// far enough to need new ones.
  Future<int?> _captureAllFrames(Directory framesDir) async {
    final fps = VideoExportService.framesPerSecond;
    final total = _timeline.frameCount(fps);
    String? lastKey;
    var lastCaptured = -1;
    RecapPhase? lastPhase;
    var lastOverview = 0.0;
    String? lastPhotoId;
    ll.LatLng? lastSettledCenter;

    for (var index = 0; index < total; index++) {
      if (!mounted || _cancelRequested) {
        _cancellationToken?.throwIfCancelled();
        throw VideoExportException('Video-uitvoer is gekanselleer.');
      }

      final seconds = index / fps;
      final frame = _timeline.frameAt(seconds);
      final key = frame.renderKey;

      if (key == lastKey && lastCaptured >= 0) {
        await VideoExportService.repeatFrame(
          framesDir: framesDir,
          sourceIndex: lastCaptured,
          targetIndex: index,
        );
      } else {
        final photo = frame.photo;
        if (photo != null && photo.id != lastPhotoId) {
          await _precachePhoto(photo);
          lastPhotoId = photo.id;
        }

        setState(() => _seconds = seconds);

        var settleMs = 0;
        if (frame.phase == RecapPhase.map) {
          _applyCamera(frame);
          final center = _cameraCenter();
          if (lastPhase != RecapPhase.map) {
            // The whole viewport is loading tiles for the first time.
            settleMs = _firstFrameSettleMs;
          } else if (frame.overview >= 1 && lastOverview < 1) {
            settleMs = _overviewSettleMs;
          } else if (frame.overview > 0 && frame.overview < 1) {
            settleMs = _zoomFrameSettleMs;
          } else if (center != null &&
              (lastSettledCenter == null ||
                  HuntPhotoMatcher.distanceMeters(
                        lastSettledCenter.latitude,
                        lastSettledCenter.longitude,
                        center.latitude,
                        center.longitude,
                      ) >
                      _panSettleMeters)) {
            settleMs = _panSettleMs;
          }
          if (settleMs > 0) {
            lastSettledCenter = center;
          }
        }
        if (settleMs > 0) {
          await Future.delayed(Duration(milliseconds: settleMs));
        }

        await _captureFrameOrThrow(framesDir, index);
        lastCaptured = index;
        lastKey = key;
        lastPhase = frame.phase;
        lastOverview = frame.overview;
      }

      if (mounted && (index % 10 == 0 || index == total - 1)) {
        setState(() => _exportProgress = (index + 1) / total);
      }
    }

    return total;
  }

  ll.LatLng? _cameraCenter() {
    try {
      return _mapController.camera.center;
    } catch (_) {
      return null;
    }
  }

  Future<void> _cancelVideo() async {
    if (!_isExporting) {
      return;
    }
    setState(() => _cancelRequested = true);
    _cancellationToken?.cancel();
    await VideoExportService.cancelActiveEncoding();
  }

  void _openVideoPlayer(SavedRecapVideo video) {
    if (!mounted) {
      return;
    }
    _pause();
    Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => RecapVideoPlayerScreen(
          video: video,
          storageOverride: _videoStorage,
        ),
      ),
    );
  }

  void _showExportResult(SavedRecapVideo savedVideo) {
    if (!mounted) {
      return;
    }

    _openVideoPlayer(savedVideo);

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text(
          'Die video is privaat in die app gestoor. Gebruik “Stoor in galery” om dit op jou foon te bewaar.',
        ),
        duration: Duration(seconds: 4),
      ),
    );
  }

  void _showExportError(String message) {
    if (!mounted) {
      return;
    }

    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  // --- Drawing ------------------------------------------------------------

  Widget _buildRecapCanvas(RecapFrame frame) {
    if (frame.phase == RecapPhase.intro) {
      return _buildTitleCard();
    }
    if (frame.phase == RecapPhase.summary) {
      return _buildSummaryCard();
    }

    final position = frame.position;
    final current = _pointAt(position);
    final reached = position.floor();
    final progress = <ll.LatLng>[
      ..._routePoints.take(reached + 1),
      if (position > reached) current,
    ];
    final visiblePins = widget.pins
        .where((pin) => (_pinIndex[pin.id] ?? 0) <= position + 1e-6)
        .toList(growable: false);
    final visiblePhotos = _photos
        .where((photo) {
          final index = photo.matchedWaypointIndex;
          return index != null && index >= 0 && index <= position + 1e-6;
        })
        .toList(growable: false);
    final caption = frame.caption;
    final photo = frame.photo;

    return Stack(
      fit: StackFit.expand,
      children: [
        FlutterMap(
          mapController: _mapController,
          options: MapOptions(
            initialCenter: _routePoints.first,
            initialZoom: _defaultZoom,
            interactionOptions: InteractionOptions(
              flags: _isExporting || _isPlaying
                  ? InteractiveFlag.none
                  : InteractiveFlag.all & ~InteractiveFlag.rotate,
            ),
          ),
          children: [
            const AppTileLayer(),
            PolylineLayer(
              polylines: [
                // The whole route, faint, so the viewer sees where it goes.
                if (_routePoints.length >= 2)
                  Polyline(
                    points: _routePoints,
                    strokeWidth: 3,
                    color: AppTheme.cream.withValues(alpha: 0.35),
                  ),
                if (progress.length >= 2)
                  Polyline(
                    points: progress,
                    strokeWidth: 5,
                    color: AppTheme.gold,
                    borderStrokeWidth: 1.5,
                    borderColor: Colors.black.withValues(alpha: 0.45),
                  ),
              ],
            ),
            MarkerLayer(
              markers: [
                ...visiblePins.map(
                  (pin) => Marker(
                    point: ll.LatLng(pin.latitude, pin.longitude),
                    width: 34,
                    height: 34,
                    child: _MapBadge(
                      icon: pin.autoDetected && !pin.confirmed
                          ? Icons.hearing_rounded
                          : iconForHuntPin(pin.type),
                      color: pin.needsReview
                          ? AppTheme.dangerSoft
                          : AppTheme.gold,
                    ),
                  ),
                ),
                ...visiblePhotos.map((photo) {
                  final waypoint =
                      widget.keyframes[photo.matchedWaypointIndex!];
                  return Marker(
                    point: ll.LatLng(waypoint.latitude, waypoint.longitude),
                    width: 34,
                    height: 34,
                    child: const _MapBadge(
                      icon: Icons.photo_camera_outlined,
                      color: AppTheme.gold,
                      borderColor: Colors.white,
                    ),
                  );
                }),
                Marker(
                  point: current,
                  width: 40,
                  height: 40,
                  child: Container(
                    decoration: BoxDecoration(
                      color: AppTheme.olive,
                      shape: BoxShape.circle,
                      border: Border.all(color: AppTheme.gold, width: 2.5),
                      boxShadow: const [
                        BoxShadow(color: Color(0x66000000), blurRadius: 8),
                      ],
                    ),
                    child: const Icon(
                      Icons.navigation_rounded,
                      color: AppTheme.cream,
                      size: 19,
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
        if (caption != null)
          Positioned(
            left: 12,
            right: 12,
            bottom: 14,
            child: Opacity(
              opacity: frame.captionOpacity,
              child: _RecapCaptionCard(caption: caption),
            ),
          ),
        if (photo != null && frame.photoOpacity > 0)
          Positioned.fill(
            child: Opacity(
              opacity: frame.photoOpacity,
              child: _FullScreenPhoto(
                image: _photoProvider(photo),
                caption: caption,
              ),
            ),
          ),
      ],
    );
  }

  Widget _buildTitleCard() {
    final title = widget.trophyName?.trim().isNotEmpty == true
        ? widget.trophyName!
        : 'Jag-terugblik';
    final date = widget.sessionStartedAt ?? widget.keyframes.first.timestamp;
    return ColoredBox(
      color: AppTheme.background,
      child: Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              _buildTrophyImage(),
              const SizedBox(height: 20),
              Text(
                title,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: AppTheme.goldSoft,
                  fontSize: 32,
                  fontWeight: FontWeight.w900,
                ),
              ),
              const SizedBox(height: 10),
              Text(
                '${date.year}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}',
                style: const TextStyle(color: AppTheme.cream, fontSize: 18),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildTrophyImage() {
    final provider = _trophyImageProvider();
    if (provider == null) {
      return const Icon(Icons.terrain_rounded, size: 96, color: AppTheme.gold);
    }
    return ClipRRect(
      borderRadius: BorderRadius.circular(16),
      child: Image(
        image: provider,
        width: 180,
        height: 140,
        fit: BoxFit.cover,
        errorBuilder: (_, _, _) => const Icon(
          Icons.workspace_premium_outlined,
          size: 96,
          color: AppTheme.gold,
        ),
      ),
    );
  }

  ImageProvider<Object>? _trophyImageProvider() {
    final rawPath = widget.trophyPhotoPath?.trim() ?? '';
    if (rawPath.isEmpty) {
      return null;
    }
    final resolved = AppConfig.resolveApiUrl(rawPath);
    final uri = Uri.tryParse(resolved);
    if (uri == null || (uri.scheme != 'http' && uri.scheme != 'https')) {
      return null;
    }
    final token = AuthService.instance.accessToken;
    return ResizeImage(
      NetworkImage(
        resolved,
        headers: token == null || token.isEmpty
            ? null
            : <String, String>{'Authorization': 'Bearer $token'},
      ),
      width: 540,
    );
  }

  Future<void> _precacheTrophyImage() async {
    final provider = _trophyImageProvider();
    if (provider == null || !mounted) {
      return;
    }
    try {
      await precacheImage(provider, context);
    } catch (_) {
      // The title card has a private-safe icon fallback; a trophy thumbnail
      // failure must not block an otherwise valid local recap export.
    }
  }

  Widget _buildSummaryCard() {
    final start = widget.sessionStartedAt ?? widget.keyframes.first.timestamp;
    final end = widget.keyframes.last.timestamp;
    final duration = end.difference(start);
    final km = (_timeline.totalMeters / 1000)
        .toStringAsFixed(2)
        .replaceAll('.', ',');
    return ColoredBox(
      color: AppTheme.background,
      child: Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(
                Icons.check_circle_outline,
                color: AppTheme.gold,
                size: 72,
              ),
              const SizedBox(height: 18),
              const Text(
                'Jag voltooi',
                style: TextStyle(
                  color: AppTheme.goldSoft,
                  fontSize: 30,
                  fontWeight: FontWeight.w900,
                ),
              ),
              const SizedBox(height: 20),
              Text(
                '$km km  •  ${duration.inMinutes} min\n'
                '${widget.pins.length} ${widget.pins.length == 1 ? 'merker' : 'merkers'}  •  '
                '${_photos.length} ${_photos.length == 1 ? 'foto' : 'foto’s'}',
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: AppTheme.cream,
                  fontSize: 18,
                  height: 1.5,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  static String _clock(double seconds) {
    final total = seconds.round();
    return '${total ~/ 60}:${(total % 60).toString().padLeft(2, '0')}';
  }

  @override
  Widget build(BuildContext context) {
    final frame = _frame;
    final mapStart = _timeline.mapStartSeconds;
    final mapEnd = _timeline.mapEndSeconds;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Jag-terugblik'),
        actions: [
          if (_latestSavedVideo != null && !_isExporting)
            IconButton(
              onPressed: () => _openVideoPlayer(_latestSavedVideo!),
              icon: const Icon(Icons.play_circle_fill_rounded),
              tooltip: 'Kyk video',
            ),
          IconButton(
            onPressed: _isExporting ? null : _generateVideo,
            icon: const Icon(Icons.movie_creation_outlined),
            tooltip: 'Maak video',
          ),
        ],
      ),
      body: !_hasFrames
          ? const Center(
              child: Text(
                'Geen roete is vir hierdie terugblik beskikbaar nie.',
                style: TextStyle(color: AppTheme.muted),
              ),
            )
          : Column(
              children: [
                Expanded(
                  child: Stack(
                    fit: StackFit.expand,
                    children: [
                      RepaintBoundary(
                        key: _repaintKey,
                        child: _buildRecapCanvas(frame),
                      ),
                      if (_isExporting)
                        Positioned(
                          left: 16,
                          right: 16,
                          top: 16,
                          child: Container(
                            padding: const EdgeInsets.all(12),
                            decoration: BoxDecoration(
                              color: AppTheme.card.withValues(alpha: 0.96),
                              borderRadius: BorderRadius.circular(
                                AppTheme.radiusLarge,
                              ),
                              border: Border.all(color: AppTheme.border),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Row(
                                  children: [
                                    const Expanded(
                                      child: Text(
                                        'Video word gemaak...',
                                        style: TextStyle(
                                          fontWeight: FontWeight.w800,
                                          color: AppTheme.ink,
                                        ),
                                      ),
                                    ),
                                    TextButton(
                                      onPressed: _cancelVideo,
                                      child: const Text('Kanselleer'),
                                    ),
                                  ],
                                ),
                                const SizedBox(height: 8),
                                LinearProgressIndicator(value: _exportProgress),
                              ],
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
                // Hidden while exporting so the video uses the whole screen.
                if (!_isExporting)
                  Container(
                    width: double.infinity,
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                    decoration: BoxDecoration(
                      color: AppTheme.card,
                      border: Border(top: BorderSide(color: AppTheme.border)),
                    ),
                    child: SafeArea(
                      top: false,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Text(
                                _clock(_seconds - mapStart),
                                style: const TextStyle(color: AppTheme.muted),
                              ),
                              Expanded(
                                child: Slider(
                                  value: _seconds.clamp(mapStart, mapEnd),
                                  min: mapStart,
                                  max: mapEnd > mapStart
                                      ? mapEnd
                                      : mapStart + 1,
                                  onChanged: (value) {
                                    _pause();
                                    _seek(value);
                                  },
                                ),
                              ),
                              Text(
                                _clock(mapEnd - mapStart),
                                style: const TextStyle(color: AppTheme.muted),
                              ),
                            ],
                          ),
                          if (_photos.isNotEmpty) ...[
                            SizedBox(
                              height: 58,
                              child: ListView.separated(
                                scrollDirection: Axis.horizontal,
                                itemCount: _photos.length,
                                separatorBuilder: (_, _) =>
                                    const SizedBox(width: 7),
                                itemBuilder: (context, index) {
                                  final photo = _photos[index];
                                  final at = _timeline.secondsForPhoto(
                                    photo.id,
                                  );
                                  return InkWell(
                                    onTap: at == null
                                        ? null
                                        : () {
                                            _pause();
                                            // Land just after the fade-in.
                                            _seek(
                                              at +
                                                  RecapTimeline
                                                      .photoFadeSeconds,
                                            );
                                          },
                                    child: ClipRRect(
                                      borderRadius: BorderRadius.circular(8),
                                      child: Image.file(
                                        File(photo.localPath),
                                        width: 58,
                                        height: 58,
                                        cacheWidth: 174,
                                        fit: BoxFit.cover,
                                        errorBuilder: (_, _, _) =>
                                            const SizedBox(
                                              width: 58,
                                              child: Icon(
                                                Icons.broken_image_outlined,
                                              ),
                                            ),
                                      ),
                                    ),
                                  );
                                },
                              ),
                            ),
                            const SizedBox(height: 10),
                          ],
                          if (_latestSavedVideo != null) ...[
                            SizedBox(
                              width: double.infinity,
                              child: FilledButton.icon(
                                onPressed: () =>
                                    _openVideoPlayer(_latestSavedVideo!),
                                icon: const Icon(Icons.play_circle_fill_rounded),
                                label: const Text('Kyk terugblik-video'),
                                style: FilledButton.styleFrom(
                                  backgroundColor: AppTheme.gold,
                                  foregroundColor: Colors.black,
                                ),
                              ),
                            ),
                            const SizedBox(height: 10),
                          ],
                          Row(
                            children: [
                              Expanded(
                                child: OutlinedButton.icon(
                                  onPressed: _restart,
                                  icon: const Icon(Icons.restart_alt_rounded),
                                  label: const Text('Van voor af'),
                                ),
                              ),
                              const SizedBox(width: 12),
                              Expanded(
                                child: ElevatedButton.icon(
                                  onPressed: _isPlaying ? _pause : _play,
                                  icon: Icon(
                                    _isPlaying
                                        ? Icons.pause_rounded
                                        : Icons.play_arrow_rounded,
                                  ),
                                  label: Text(_isPlaying ? 'Pouseer' : 'Speel'),
                                  style: ElevatedButton.styleFrom(
                                    backgroundColor: AppTheme.olive,
                                    foregroundColor: Colors.white,
                                  ),
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 8),
                          SizedBox(
                            width: double.infinity,
                            child: OutlinedButton.icon(
                              onPressed: widget.sessionId == null
                                  ? null
                                  : _openPhotoManager,
                              icon: const Icon(
                                Icons.add_photo_alternate_outlined,
                              ),
                              label: const Text('Laai jagfoto’s op'),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
              ],
            ),
    );
  }
}

class _MapBadge extends StatelessWidget {
  const _MapBadge({required this.icon, required this.color, this.borderColor});

  final IconData icon;
  final Color color;
  final Color? borderColor;

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.card,
        shape: BoxShape.circle,
        border: Border.all(color: borderColor ?? color, width: 2),
      ),
      child: Icon(icon, color: color, size: 17),
    );
  }
}

/// Why the current moment is in the recap: started, walking, resting, a pin
/// the hunter dropped, a shot Outo-skoot heard, a photo, the end.
class _RecapCaptionCard extends StatelessWidget {
  const _RecapCaptionCard({required this.caption});

  final RecapCaption caption;

  IconData get _icon => switch (caption.kind) {
    RecapEventKind.start => Icons.flag_outlined,
    RecapEventKind.travel => Icons.directions_walk_rounded,
    RecapEventKind.rest => Icons.coffee_outlined,
    RecapEventKind.manualPin => iconForHuntPin(
      caption.pinType ?? HuntPinType.custom,
    ),
    RecapEventKind.autoShot => Icons.hearing_rounded,
    RecapEventKind.autoShotConfirmed => Icons.gps_fixed_rounded,
    RecapEventKind.photo => Icons.photo_camera_outlined,
    RecapEventKind.finish => Icons.sports_score_rounded,
    RecapEventKind.overview => Icons.map_outlined,
  };

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(12, 10, 14, 12),
      decoration: BoxDecoration(
        color: AppTheme.card.withValues(alpha: 0.94),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppTheme.gold.withValues(alpha: 0.7)),
        boxShadow: const [BoxShadow(color: Color(0x55000000), blurRadius: 10)],
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 36,
            height: 36,
            decoration: BoxDecoration(
              color: AppTheme.olive.withValues(alpha: 0.35),
              shape: BoxShape.circle,
            ),
            child: Icon(_icon, color: AppTheme.goldSoft, size: 20),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  caption.title,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    color: AppTheme.ink,
                    fontSize: 16,
                    fontWeight: FontWeight.w900,
                  ),
                ),
                if (caption.detail.isNotEmpty) ...[
                  const SizedBox(height: 3),
                  Text(
                    caption.detail,
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      color: AppTheme.cream,
                      fontSize: 13,
                      height: 1.35,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// A hunt photo filling the whole recap frame, with its caption on top.
class _FullScreenPhoto extends StatelessWidget {
  const _FullScreenPhoto({required this.image, this.caption});

  final ImageProvider<Object> image;
  final RecapCaption? caption;

  @override
  Widget build(BuildContext context) {
    final caption = this.caption;
    return ColoredBox(
      color: Colors.black,
      child: Stack(
        fit: StackFit.expand,
        children: [
          Image(
            image: image,
            fit: BoxFit.contain,
            errorBuilder: (_, _, _) => const Center(
              child: Icon(
                Icons.broken_image_outlined,
                color: AppTheme.muted,
                size: 64,
              ),
            ),
          ),
          if (caption != null)
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: Container(
                padding: const EdgeInsets.fromLTRB(20, 48, 20, 22),
                decoration: const BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.bottomCenter,
                    end: Alignment.topCenter,
                    colors: [Color(0xE6000000), Color(0x00000000)],
                  ),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      caption.title,
                      style: const TextStyle(
                        color: Colors.white,
                        fontSize: 22,
                        fontWeight: FontWeight.w900,
                      ),
                    ),
                    if (caption.detail.isNotEmpty) ...[
                      const SizedBox(height: 5),
                      Text(
                        caption.detail,
                        style: const TextStyle(
                          color: Color(0xDDFFFFFF),
                          fontSize: 14,
                          height: 1.35,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class RecapVideoPlayerScreen extends StatefulWidget {
  const RecapVideoPlayerScreen({
    super.key,
    required this.video,
    this.storageOverride,
  });

  final SavedRecapVideo video;
  final VideoStorageService? storageOverride;

  @override
  State<RecapVideoPlayerScreen> createState() => _RecapVideoPlayerScreenState();
}

class _RecapVideoPlayerScreenState extends State<RecapVideoPlayerScreen> {
  late final VideoStorageService _storage =
      widget.storageOverride ?? const VideoStorageService();
  VideoPlayerController? _controller;
  bool _loading = true;
  bool _savingToGallery = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _initVideo();
  }

  Future<void> _initVideo() async {
    try {
      final controller = VideoPlayerController.file(File(widget.video.path));
      await controller.initialize();
      await controller.setLooping(true);
      await controller.play();

      if (!mounted) {
        await controller.dispose();
        return;
      }

      controller.addListener(_onVideoTick);

      setState(() {
        _controller = controller;
        _loading = false;
      });
    } catch (e) {
      if (!mounted) {
        return;
      }

      setState(() {
        _error = 'Kon nie die video oopmaak nie: $e';
        _loading = false;
      });
    }
  }

  void _onVideoTick() {
    if (mounted) {
      setState(() {});
    }
  }

  @override
  void dispose() {
    _controller?.removeListener(_onVideoTick);
    _controller?.dispose();
    super.dispose();
  }

  String _formatDuration(Duration d) {
    final total = d.inSeconds;
    return '${total ~/ 60}:${(total % 60).toString().padLeft(2, '0')}';
  }

  /// Asks before anything is written to the phone's gallery. The answer is
  /// remembered per user; the phone may then show its own permission prompt.
  Future<bool> _askGalleryPermission() async {
    final userId = AuthService.instance.currentSession?.userId;
    final key = userId != null
        ? 'recap_gallery_consent_$userId'
        : 'recap_gallery_consent_guest';
    final prefs = await SharedPreferences.getInstance();
    if (prefs.getBool(key) == true) {
      return true;
    }
    if (!mounted) {
      return false;
    }
    final allowed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        icon: const Icon(Icons.photo_library_outlined),
        title: const Text('Stoor in jou galery?'),
        content: const Text(
          'Mikpunt het jou toestemming nodig om hierdie video in jou foon se '
          'galery (album “Jagbestuur”) te stoor. Jou foon kan jou daarna ook '
          'vra om toegang te gee. Die private kopie in die app bly in elk '
          'geval behoue.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Nie nou nie'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Gee toestemming'),
          ),
        ],
      ),
    );
    if (allowed == true) {
      await prefs.setBool(key, true);
    }
    return allowed == true;
  }

  Future<void> _saveToGallery() async {
    if (_savingToGallery) {
      return;
    }
    if (!await _askGalleryPermission()) {
      return;
    }
    if (!mounted) {
      return;
    }
    setState(() => _savingToGallery = true);
    final GallerySaveResult result;
    try {
      result = await _storage.saveVideoToGallery(widget.video);
    } catch (error, stack) {
      AppErrorBarrier.report('Save recap video to gallery', error, stack);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Kon nie die video in jou galery stoor nie.'),
          ),
        );
      }
      return;
    } finally {
      if (mounted) {
        setState(() => _savingToGallery = false);
      }
    }
    if (!mounted) {
      return;
    }
    final message = switch (result) {
      GallerySaveResult.saved => 'Die video is in jou galery gestoor.',
      GallerySaveResult.permissionDenied =>
        'Toegang tot jou galery is geweier. Gee Mikpunt toegang in jou foon '
            'se instellings; die private kopie bly behoue.',
      GallerySaveResult.skippedTestingEnv =>
        'Toetsmodus: stoor in die galery is afgeskakel; die private kopie bly behoue.',
      GallerySaveResult.failed =>
        'Kon nie in die galery stoor nie. Die private kopie bly behoue.',
    };
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    final isInitialized = controller?.value.isInitialized ?? false;
    final aspectRatio = (isInitialized && controller!.value.aspectRatio > 0)
        ? controller.value.aspectRatio
        : 16 / 9;
    final position = controller?.value.position ?? Duration.zero;
    final duration = controller?.value.duration ?? Duration.zero;
    final isPlaying = controller?.value.isPlaying ?? false;

    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(title: const Text('Terugblik-video')),
      body: SafeArea(
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
            ? Center(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Text(_error!, textAlign: TextAlign.center),
                ),
              )
            : controller == null
            ? const Center(child: Text('Geen video beskikbaar nie.'))
            : Column(
                children: [
                  // The video takes all the room the controls leave.
                  Expanded(
                    child: GestureDetector(
                      onTap: () {
                        if (controller.value.isPlaying) {
                          controller.pause();
                        } else {
                          controller.play();
                        }
                      },
                      child: Center(
                        child: AspectRatio(
                          aspectRatio: aspectRatio,
                          child: Stack(
                            alignment: Alignment.center,
                            children: [
                              VideoPlayer(controller),
                              if (!isPlaying)
                                Container(
                                  decoration: BoxDecoration(
                                    color: Colors.black.withValues(alpha: 0.4),
                                    shape: BoxShape.circle,
                                  ),
                                  padding: const EdgeInsets.all(12),
                                  child: const Icon(
                                    Icons.play_arrow_rounded,
                                    size: 48,
                                    color: Colors.white,
                                  ),
                                ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 16),
                    child: Column(
                      children: [
                        VideoProgressIndicator(
                          controller,
                          allowScrubbing: true,
                          colors: const VideoProgressColors(
                            playedColor: AppTheme.gold,
                            bufferedColor: Colors.white24,
                            backgroundColor: Colors.white12,
                          ),
                          padding: const EdgeInsets.symmetric(vertical: 8),
                        ),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            Text(
                              _formatDuration(position),
                              style: const TextStyle(
                                color: Colors.white70,
                                fontSize: 12,
                              ),
                            ),
                            Text(
                              _formatDuration(duration),
                              style: const TextStyle(
                                color: Colors.white70,
                                fontSize: 12,
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                    child: Row(
                      children: [
                        IconButton(
                          iconSize: 40,
                          color: Colors.white,
                          onPressed: () {
                            if (controller.value.isPlaying) {
                              controller.pause();
                            } else {
                              controller.play();
                            }
                          },
                          icon: Icon(
                            isPlaying
                                ? Icons.pause_circle_filled_rounded
                                : Icons.play_circle_fill_rounded,
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: FilledButton.icon(
                            onPressed: _savingToGallery ? null : _saveToGallery,
                            icon: _savingToGallery
                                ? const SizedBox.square(
                                    dimension: 18,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                    ),
                                  )
                                : const Icon(Icons.save_alt_rounded),
                            label: const Text('Stoor in galery'),
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
      ),
    );
  }
}
