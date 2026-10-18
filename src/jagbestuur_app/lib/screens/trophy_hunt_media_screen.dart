import 'dart:io';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../models/hunt_photo.dart';
import '../models/hunt_session.dart';
import '../models/trophy.dart';
import '../safety/app_error_barrier.dart';
import '../services/hunt_media_service.dart';
import '../services/hunt_storage_service.dart';
import '../services/video_storage_service.dart';
import '../theme/app_theme.dart';
import 'hike_recap_screen.dart';
import 'hunt_photo_manager_screen.dart';

class TrophyHuntMediaScreen extends StatefulWidget {
  const TrophyHuntMediaScreen({super.key, required this.trophy});

  final Trophy trophy;

  @override
  State<TrophyHuntMediaScreen> createState() => _TrophyHuntMediaScreenState();
}

class _TrophyHuntMediaScreenState extends State<TrophyHuntMediaScreen> {
  final HuntMediaService _media = HuntMediaService();
  final HuntStorageService _hunts = const HuntStorageService();
  final VideoStorageService _videos = const VideoStorageService();

  List<HuntSession> _allSessions = const <HuntSession>[];
  List<HuntSession> _linkedSessions = const <HuntSession>[];
  List<HuntPhoto> _linkedPhotos = const <HuntPhoto>[];
  List<SavedRecapVideo> _linkedVideos = const <SavedRecapVideo>[];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
    HuntMediaService.mediaVersion.addListener(_onChanged);
    VideoStorageService.libraryVersion.addListener(_onChanged);
  }

  @override
  void dispose() {
    HuntMediaService.mediaVersion.removeListener(_onChanged);
    VideoStorageService.libraryVersion.removeListener(_onChanged);
    super.dispose();
  }

  void _onChanged() {
    if (mounted) {
      _load();
    }
  }

  Future<void> _load() async {
    try {
      final sessions = await _hunts.listSessions();
      final ids = (await _media.linkedSessionIds(widget.trophy.id)).toSet();
      final photos = await _media.listPhotosForTrophy(widget.trophy.id);
      final videos = await _videos.listVideos();
      if (!mounted) {
        return;
      }
      setState(() {
        _allSessions = sessions;
        _linkedSessions = sessions
            .where((session) => ids.contains(session.id))
            .toList();
        _linkedPhotos = photos;
        _linkedVideos = videos
            .where(
              (video) =>
                  video.trophyId == widget.trophy.id ||
                  ids.contains(video.sessionId),
            )
            .toList();
        _loading = false;
        _error = null;
      });
    } catch (error, stack) {
      AppErrorBarrier.report('Load trophy hunt media', error, stack);
      if (mounted) {
        setState(() {
          _loading = false;
          _error = 'Kon nie die gekoppelde jagmedia laai nie.';
        });
      }
    }
  }

  Future<void> _manageLinks() async {
    final selected = _linkedSessions.map((session) => session.id).toSet();
    final result = await showDialog<Set<String>>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: const Text('Koppel ’n bestaande jag'),
          content: SizedBox(
            width: 420,
            child: _allSessions.isEmpty
                ? const Text('Geen voltooide jagte is beskikbaar nie.')
                : ListView(
                    shrinkWrap: true,
                    children: [
                      for (final session in _allSessions)
                        CheckboxListTile(
                          value: selected.contains(session.id),
                          title: Text(
                            DateFormat(
                              'd MMM yyyy, HH:mm',
                            ).format(session.startedAt),
                          ),
                          subtitle: Text(
                            '${(session.totalDistanceMeters / 1000).toStringAsFixed(2)} km',
                          ),
                          onChanged: (checked) => setDialogState(() {
                            if (checked == true) {
                              selected.add(session.id);
                            } else {
                              selected.remove(session.id);
                            }
                          }),
                        ),
                    ],
                  ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text('Kanselleer'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(dialogContext, selected),
              child: const Text('Stoor skakels'),
            ),
          ],
        ),
      ),
    );
    if (result == null) {
      return;
    }
    try {
      final current = _linkedSessions.map((session) => session.id).toSet();
      for (final id in result.difference(current)) {
        await _media.linkTrophyToSession(widget.trophy.id, id);
      }
      for (final id in current.difference(result)) {
        await _media.unlinkTrophyFromSession(widget.trophy.id, id);
      }
      await _load();
    } catch (error, stack) {
      AppErrorBarrier.report('Link trophy hunt', error, stack);
    }
  }

  Future<HuntSession?> _chooseSession() async {
    if (_linkedSessions.isEmpty) {
      await _manageLinks();
    }
    if (_linkedSessions.isEmpty || !mounted) {
      return null;
    }
    if (_linkedSessions.length == 1) {
      return _linkedSessions.first;
    }
    return showDialog<HuntSession>(
      context: context,
      builder: (dialogContext) => SimpleDialog(
        title: const Text('Kies ’n gekoppelde jag'),
        children: [
          for (final session in _linkedSessions)
            SimpleDialogOption(
              onPressed: () => Navigator.pop(dialogContext, session),
              child: Text(
                DateFormat('d MMM yyyy, HH:mm').format(session.startedAt),
              ),
            ),
        ],
      ),
    );
  }

  Future<void> _openPhotos() async {
    final session = await _chooseSession();
    if (session == null || !mounted) {
      return;
    }
    await Navigator.push(
      context,
      MaterialPageRoute(
        builder: (_) => HuntPhotoManagerScreen(
          session: session,
          trophyId: widget.trophy.id,
        ),
      ),
    );
    await _load();
  }

  Future<void> _openRecap({bool generateVideo = false}) async {
    final session = await _chooseSession();
    if (session == null || !mounted) {
      return;
    }
    await Navigator.push(
      context,
      MaterialPageRoute(
        builder: (_) => HikeRecapScreen(
          keyframes: session.waypoints,
          pins: session.pins,
          sessionId: session.id,
          sessionStartedAt: session.startedAt,
          trophyId: widget.trophy.id,
          trophyName: widget.trophy.displaySpecies,
          trophyPhotoPath: widget.trophy.photoPath,
          autoStartVideoExport: generateVideo,
        ),
      ),
    );
    await _load();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text('${widget.trophy.displaySpecies} • Jagmedia')),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
          ? Center(child: Text(_error!))
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Text(
                  'Gekoppelde jagte: ${_linkedSessions.length}',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 12),
                OutlinedButton.icon(
                  onPressed: _manageLinks,
                  icon: const Icon(Icons.link_rounded),
                  label: const Text('Koppel ’n bestaande jag'),
                ),
                const SizedBox(height: 8),
                FilledButton.icon(
                  onPressed: _openPhotos,
                  icon: const Icon(Icons.add_photo_alternate_outlined),
                  label: const Text('Laai jagfoto’s op'),
                ),
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  onPressed: _openPhotos,
                  icon: const Icon(Icons.photo_library_outlined),
                  label: const Text('Bekyk jagfoto’s'),
                ),
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  onPressed: () => _openRecap(),
                  icon: const Icon(Icons.route_outlined),
                  label: const Text('Bekyk interaktiewe terugblik'),
                ),
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  onPressed: () => _openRecap(generateVideo: true),
                  icon: const Icon(Icons.movie_creation_outlined),
                  label: const Text('Maak terugblik-video'),
                ),
                const SizedBox(height: 24),
                const Text(
                  'Gekoppelde foto’s',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800),
                ),
                const SizedBox(height: 8),
                if (_linkedPhotos.isEmpty)
                  const Text(
                    'Nog geen jagfoto’s vir hierdie trofee nie.',
                    style: TextStyle(color: AppTheme.muted),
                  )
                else
                  SizedBox(
                    height: 112,
                    child: ListView.separated(
                      scrollDirection: Axis.horizontal,
                      itemCount: _linkedPhotos.length,
                      separatorBuilder: (_, _) => const SizedBox(width: 8),
                      itemBuilder: (context, index) {
                        final photo = _linkedPhotos[index];
                        return ClipRRect(
                          borderRadius: BorderRadius.circular(12),
                          child: Image.file(
                            File(photo.localPath),
                            width: 112,
                            height: 112,
                            cacheWidth: 336,
                            fit: BoxFit.cover,
                            errorBuilder: (_, _, _) => const SizedBox(
                              width: 112,
                              child: Icon(Icons.broken_image_outlined),
                            ),
                          ),
                        );
                      },
                    ),
                  ),
                const SizedBox(height: 24),
                const Text(
                  'Gekoppelde video’s',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800),
                ),
                const SizedBox(height: 8),
                if (_linkedVideos.isEmpty)
                  const Text(
                    'Nog geen video’s vir hierdie trofee nie.',
                    style: TextStyle(color: AppTheme.muted),
                  )
                else
                  for (final video in _linkedVideos)
                    ListTile(
                      leading: const Icon(Icons.movie_outlined),
                      title: Text(video.title),
                      subtitle: Text(
                        '${video.durationSeconds}s • ${video.resolution}',
                      ),
                      onTap: () => Navigator.push(
                        context,
                        MaterialPageRoute(
                          builder: (_) => RecapVideoPlayerScreen(video: video),
                        ),
                      ),
                    ),
              ],
            ),
    );
  }
}
