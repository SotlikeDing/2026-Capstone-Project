import 'dart:io';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:intl/intl.dart';

import '../models/hunt_photo.dart';
import '../models/hunt_session.dart';
import '../safety/app_error_barrier.dart';
import '../services/hunt_media_service.dart';
import '../theme/app_theme.dart';
import '../widgets/hunt_photo_privacy_notice.dart';

class HuntPhotoManagerScreen extends StatefulWidget {
  const HuntPhotoManagerScreen({
    super.key,
    required this.session,
    this.trophyId,
  });

  final HuntSession session;
  final int? trophyId;

  @override
  State<HuntPhotoManagerScreen> createState() => _HuntPhotoManagerScreenState();
}

class _HuntPhotoManagerScreenState extends State<HuntPhotoManagerScreen> {
  final HuntMediaService _media = HuntMediaService();
  final ImagePicker _picker = ImagePicker();
  List<HuntPhoto> _photos = const <HuntPhoto>[];
  bool _loading = true;
  bool _importing = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
    HuntMediaService.mediaVersion.addListener(_onMediaChanged);
  }

  @override
  void dispose() {
    HuntMediaService.mediaVersion.removeListener(_onMediaChanged);
    super.dispose();
  }

  void _onMediaChanged() {
    if (mounted) {
      _load();
    }
  }

  Future<void> _load() async {
    try {
      final photos = await _media.listPhotosForSession(widget.session.id);
      if (!mounted) {
        return;
      }
      setState(() {
        _photos = photos;
        _loading = false;
        _error = null;
      });
    } catch (error, stack) {
      AppErrorBarrier.report('Load hunt photos', error, stack);
      if (mounted) {
        setState(() {
          _loading = false;
          _error = 'Kon nie die private jagfoto’s laai nie.';
        });
      }
    }
  }

  Future<void> _importPhotos() async {
    if (_importing) {
      return;
    }
    final accepted = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        icon: const Icon(Icons.privacy_tip_outlined, color: AppTheme.gold),
        title: const Text('Foto-metadata en privaatheid'),
        content: const HuntPhotoPrivacyNotice(),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Kanselleer'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Kies foto’s'),
          ),
        ],
      ),
    );
    if (accepted != true || !mounted) {
      return;
    }

    try {
      final selected = await _picker.pickMultiImage();
      if (selected.isEmpty || !mounted) {
        return;
      }
      setState(() => _importing = true);
      final imported = await _media.importPhotos(
        session: widget.session,
        sourcePaths: selected.map((photo) => photo.path).toList(),
        trophyId: widget.trophyId,
      );
      await _load();
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('${imported.length} jagfoto’s privaat gestoor.'),
        ),
      );
      for (final photo in imported.where((photo) => photo.needsConfirmation)) {
        if (!mounted) {
          break;
        }
        await _editPhoto(photo, confirmationRequired: true);
      }
    } catch (error, stack) {
      AppErrorBarrier.report('Import hunt photos', error, stack);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(error.toString().replaceFirst('Exception: ', '')),
          ),
        );
      }
    } finally {
      if (mounted) {
        setState(() => _importing = false);
      }
    }
  }

  Future<void> _editPhoto(
    HuntPhoto photo, {
    bool confirmationRequired = false,
  }) async {
    final caption = TextEditingController(text: photo.caption);
    var waypointIndex = photo.matchedWaypointIndex;
    if (waypointIndex != null &&
        (waypointIndex < 0 ||
            waypointIndex >= widget.session.waypoints.length)) {
      waypointIndex = null;
    }
    final result = await showDialog<({String caption, int? waypointIndex})>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text(
            confirmationRequired ? 'Bevestig foto-plasing' : 'Wysig jagfoto',
          ),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (confirmationRequired)
                  const Padding(
                    padding: EdgeInsets.only(bottom: 12),
                    child: Text(
                      'Die metadata-passing is onseker. Kies die korrekte punt '
                      'of bevestig die voorgestelde punt.',
                    ),
                  ),
                TextField(
                  controller: caption,
                  maxLength: 240,
                  maxLines: 3,
                  decoration: const InputDecoration(
                    labelText: 'Byskrif of nota',
                  ),
                ),
                const SizedBox(height: 8),
                DropdownButtonFormField<int?>(
                  initialValue: waypointIndex,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Roetepunt'),
                  items: <DropdownMenuItem<int?>>[
                    const DropdownMenuItem<int?>(
                      value: null,
                      child: Text('Nie geplaas nie'),
                    ),
                    for (
                      var index = 0;
                      index < widget.session.waypoints.length;
                      index++
                    )
                      DropdownMenuItem<int?>(
                        value: index,
                        child: Text(
                          'Punt ${index + 1} • '
                          '${DateFormat('HH:mm:ss').format(widget.session.waypoints[index].timestamp)}',
                        ),
                      ),
                  ],
                  onChanged: (value) =>
                      setDialogState(() => waypointIndex = value),
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
              onPressed: () => Navigator.pop(dialogContext, (
                caption: caption.text.trim(),
                waypointIndex: waypointIndex,
              )),
              child: const Text('Stoor'),
            ),
          ],
        ),
      ),
    );
    caption.dispose();
    if (result == null) {
      return;
    }

    final index = result.waypointIndex;
    final updated = photo.copyWith(
      caption: result.caption,
      matchedWaypointIndex: index,
      clearMatchedWaypoint: index == null,
      adjustedTimestamp: index == null
          ? null
          : widget.session.waypoints[index].timestamp,
      clearAdjustedTimestamp: index == null,
      matchMethod: index == null
          ? HuntPhotoMatchMethod.unmatched
          : HuntPhotoMatchMethod.manual,
      matchConfidence: index == null
          ? HuntPhotoMatchConfidence.none
          : HuntPhotoMatchConfidence.high,
      needsConfirmation: index == null,
      clearMatchedPin: index != photo.matchedWaypointIndex,
    );
    try {
      await _media.updatePhoto(updated);
      await _load();
    } catch (error, stack) {
      AppErrorBarrier.report('Update hunt photo', error, stack);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Kon nie die foto bywerk nie.')),
        );
      }
    }
  }

  Future<void> _deletePhoto(HuntPhoto photo) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Verwyder jagfoto?'),
        content: const Text(
          'Die private foto word verwyder. Bestaande video’s en die jag bly behoue.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Kanselleer'),
          ),
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Verwyder'),
          ),
        ],
      ),
    );
    if (confirmed != true) {
      return;
    }
    try {
      await _media.deletePhoto(photo.id);
      await _load();
    } catch (error, stack) {
      AppErrorBarrier.report('Delete hunt photo', error, stack);
    }
  }

  String _matchLabel(HuntPhoto photo) {
    final method = switch (photo.matchMethod) {
      HuntPhotoMatchMethod.gps => 'GPS',
      HuntPhotoMatchMethod.timestamp => 'Tyd',
      HuntPhotoMatchMethod.pin => 'Jagpen',
      HuntPhotoMatchMethod.manual => 'Handmatig',
      HuntPhotoMatchMethod.unmatched => 'Nie geplaas nie',
    };
    return photo.needsConfirmation ? '$method • bevestiging nodig' : method;
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Jagfoto’s')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _importing ? null : _importPhotos,
        icon: _importing
            ? const SizedBox.square(
                dimension: 18,
                child: CircularProgressIndicator(strokeWidth: 2),
              )
            : const Icon(Icons.add_photo_alternate_outlined),
        label: const Text('Laai jagfoto’s op'),
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
          ? Center(child: Text(_error!))
          : _photos.isEmpty
          ? const Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: Text(
                  'Nog geen jagfoto’s nie. Foto’s bly privaat in die app en '
                  'kan sonder ’n video bekyk word.',
                  textAlign: TextAlign.center,
                ),
              ),
            )
          : ListView.separated(
              padding: const EdgeInsets.fromLTRB(16, 16, 16, 104),
              itemCount: _photos.length,
              separatorBuilder: (_, _) => const SizedBox(height: 12),
              itemBuilder: (context, index) {
                final photo = _photos[index];
                final file = File(photo.localPath);
                return Card(
                  clipBehavior: Clip.antiAlias,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      AspectRatio(
                        aspectRatio: photo.width > 0 && photo.height > 0
                            ? photo.width / photo.height
                            : 4 / 3,
                        child: FutureBuilder<bool>(
                          future: file.exists(),
                          builder: (context, snapshot) => snapshot.data == true
                              ? Image.file(
                                  file,
                                  fit: BoxFit.cover,
                                  cacheWidth: 900,
                                  errorBuilder: (_, _, _) =>
                                      const _MissingPhoto(),
                                )
                              : const _MissingPhoto(),
                        ),
                      ),
                      Padding(
                        padding: const EdgeInsets.all(12),
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    photo.caption.isEmpty
                                        ? 'Geen byskrif'
                                        : photo.caption,
                                    style: const TextStyle(
                                      fontWeight: FontWeight.w800,
                                    ),
                                  ),
                                  const SizedBox(height: 4),
                                  Text(
                                    '${_matchLabel(photo)} • ${photo.width}×${photo.height}',
                                    style: TextStyle(
                                      color: photo.needsConfirmation
                                          ? AppTheme.dangerSoft
                                          : AppTheme.muted,
                                      fontSize: 12,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                            IconButton(
                              onPressed: () => _editPhoto(photo),
                              tooltip: 'Wysig plasing en byskrif',
                              icon: const Icon(
                                Icons.edit_location_alt_outlined,
                              ),
                            ),
                            IconButton(
                              onPressed: () => _deletePhoto(photo),
                              tooltip: 'Verwyder foto',
                              icon: const Icon(Icons.delete_outline),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                );
              },
            ),
    );
  }
}

class _MissingPhoto extends StatelessWidget {
  const _MissingPhoto();

  @override
  Widget build(BuildContext context) {
    return const ColoredBox(
      color: AppTheme.card,
      child: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.broken_image_outlined, color: AppTheme.muted),
            SizedBox(height: 8),
            Text('Foto ontbreek of is ongeldig'),
          ],
        ),
      ),
    );
  }
}
