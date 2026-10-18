import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:intl/intl.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:video_player/video_player.dart';

import '../models/app_session.dart';
import '../models/booking.dart';
import '../models/hunt_session.dart';
import '../models/shared_booking.dart';
import '../services/auth_service.dart';
import '../services/booking_feature_service.dart';
import '../services/hunt_photo_metadata_service.dart';
import '../services/hunt_storage_service.dart';
import '../services/trophy_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../widgets/app_feedback_panel.dart';
import '../widgets/hunt_photo_privacy_notice.dart';

class BookingWorkspaceScreen extends StatefulWidget {
  const BookingWorkspaceScreen({
    super.key,
    required this.booking,
    required this.session,
  });
  final Booking booking;
  final AppSession session;

  @override
  State<BookingWorkspaceScreen> createState() => _BookingWorkspaceScreenState();
}

class _BookingWorkspaceScreenState extends State<BookingWorkspaceScreen>
    with SingleTickerProviderStateMixin {
  final BookingFeatureService _service = BookingFeatureService();
  late final TabController _tabs;
  late final List<_WorkspaceTab> _availableTabs;

  @override
  void initState() {
    super.initState();
    _availableTabs = [
      if (widget.session.can(MobileCapabilities.viewBookingMedia))
        _WorkspaceTab.media,
      if (widget.session.can(MobileCapabilities.submitRoute) ||
          widget.session.can(MobileCapabilities.selectRoute))
        _WorkspaceTab.route,
      if (widget.session.can(MobileCapabilities.viewRecap)) _WorkspaceTab.recap,
      if (widget.session.can(MobileCapabilities.submitTrophy))
        _WorkspaceTab.trophy,
    ];
    _tabs = TabController(length: _availableTabs.length, vsync: this);
  }

  @override
  void dispose() {
    _tabs.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.booking.title),
        bottom: TabBar(
          controller: _tabs,
          isScrollable: true,
          tabs: [
            for (final tab in _availableTabs)
              Tab(icon: Icon(tab.icon), text: tab.label),
          ],
        ),
      ),
      body: TabBarView(
        controller: _tabs,
        children: [
          for (final tab in _availableTabs)
            switch (tab) {
              _WorkspaceTab.media => _BookingMediaTab(
                booking: widget.booking,
                session: widget.session,
                service: _service,
              ),
              _WorkspaceTab.route => _BookingRouteTab(
                booking: widget.booking,
                session: widget.session,
                service: _service,
              ),
              _WorkspaceTab.recap => _BookingRecapTab(
                booking: widget.booking,
                session: widget.session,
                service: _service,
              ),
              _WorkspaceTab.trophy => _TrophySubmissionTab(
                booking: widget.booking,
                service: _service,
              ),
            },
        ],
      ),
    );
  }
}

enum _WorkspaceTab {
  media(Icons.photo_library_outlined, 'Media'),
  route(Icons.route_outlined, 'Roete'),
  recap(Icons.movie_creation_outlined, 'Terugblik'),
  trophy(Icons.workspace_premium_outlined, 'Trofee');

  const _WorkspaceTab(this.icon, this.label);
  final IconData icon;
  final String label;
}

class _BookingMediaTab extends StatefulWidget {
  const _BookingMediaTab({
    required this.booking,
    required this.session,
    required this.service,
  });
  final Booking booking;
  final AppSession session;
  final BookingFeatureService service;
  @override
  State<_BookingMediaTab> createState() => _BookingMediaTabState();
}

class _BookingMediaTabState extends State<_BookingMediaTab> {
  final ImagePicker _picker = ImagePicker();
  final HuntPhotoMetadataService _metadata = const HuntPhotoMetadataService();
  List<SharedBookingMedia>? _items;
  String? _error;
  bool _uploading = false;
  double _progress = 0;
  final List<String> _failed = [];

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final items = await widget.service.listMedia(widget.booking.id);
      if (mounted) {
        setState(() {
          _items = items;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = AppErrorMessage.from(e, fallback: 'Kon nie media laai nie.');
        });
      }
    }
  }

  Future<void> _pick() async {
    final accepted = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Foto-metadata'),
        content: const HuntPhotoPrivacyNotice(),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('Kanselleer'),
          ),
          const _AcceptMetadataButton(),
        ],
      ),
    );
    if (accepted != true || !mounted) return;
    final files = await _picker.pickMultiImage();
    if (files.isEmpty) return;
    await _upload(files.map((x) => x.path).toList());
  }

  /// VG only: one short clip for the recap. Phones don't expose a clip's recording
  /// time reliably after picking, so the VG confirms when it was filmed - that
  /// moment decides where the clip plays in the story.
  Future<void> _pickVideo() async {
    final file = await _picker.pickVideo(
      source: ImageSource.gallery,
      maxDuration: const Duration(seconds: 30),
    );
    if (file == null || !mounted) return;
    final capturedAt = await _askCaptureTime();
    if (!mounted) return;
    setState(() {
      _uploading = true;
      _progress = 0;
    });
    String? error;
    try {
      await widget.service.uploadVideo(
        widget.booking.id,
        file.path,
        capturedAt: capturedAt,
        onProgress: (sent, total) {
          if (mounted && total > 0) setState(() => _progress = sent / total);
        },
      );
    } catch (e) {
      error = AppErrorMessage.from(
        e,
        fallback: 'Kon nie die videogreep oplaai nie.',
      );
    }
    if (mounted) setState(() => _uploading = false);
    await _load();
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          error ??
              'Videogreep opgelaai. Dit speel sonder klank in die terugblik.',
        ),
      ),
    );
  }

  Future<DateTime?> _askCaptureTime() async {
    final start = widget.booking.startDate;
    final end = widget.booking.endDate;
    final today = DateTime.now();
    final date = await showDatePicker(
      context: context,
      helpText: 'Wanneer is die greep verfilm?',
      initialDate: today.isBefore(end)
          ? (today.isBefore(start) ? start : today)
          : end,
      firstDate: start.subtract(const Duration(days: 1)),
      lastDate: end.add(const Duration(days: 1)).isAfter(today)
          ? today
          : end.add(const Duration(days: 1)),
    );
    if (date == null || !mounted) return null;
    final time = await showTimePicker(
      context: context,
      helpText: 'Tyd van verfilming',
      initialTime: const TimeOfDay(hour: 7, minute: 0),
    );
    if (time == null) return null;
    return DateTime(date.year, date.month, date.day, time.hour, time.minute);
  }

  Future<void> _upload(List<String> paths) async {
    setState(() {
      _uploading = true;
      _progress = 0;
      _failed.clear();
    });
    for (var index = 0; index < paths.length; index++) {
      final path = paths[index];
      try {
        final metadata = await _metadata.extract(File(path));
        await widget.service.uploadPhoto(
          widget.booking.id,
          path,
          metadata,
          onProgress: (sent, total) {
            if (mounted && total > 0) {
              setState(() => _progress = (index + sent / total) / paths.length);
            }
          },
        );
      } catch (_) {
        _failed.add(path);
      }
    }
    if (mounted) setState(() => _uploading = false);
    await _load();
    if (mounted && _failed.isNotEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            '${_failed.length} foto’s het misluk. Gebruik “Probeer weer”.',
          ),
        ),
      );
    }
  }

  Future<void> _edit(SharedBookingMedia item) async {
    final caption = TextEditingController(text: item.caption ?? '');
    final point = TextEditingController(
      text: item.matchedRoutePointIndex?.toString() ?? '',
    );
    final pin = TextEditingController(text: item.matchedPinId ?? '');
    final timestamp = TextEditingController(
      text:
          (item.adjustedCaptureTimestamp ?? item.originalCaptureTimestamp)
              ?.toUtc()
              .toIso8601String() ??
          '',
    );
    final save = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Bevestig of verander plasing'),
        content: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: caption,
                decoration: const InputDecoration(labelText: 'Byskrif'),
              ),
              TextField(
                controller: timestamp,
                decoration: const InputDecoration(
                  labelText: 'Tydstempel (ISO 8601)',
                ),
              ),
              TextField(
                controller: point,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Roetepunt-indeks',
                ),
              ),
              TextField(
                controller: pin,
                decoration: const InputDecoration(labelText: 'Jagpen-ID'),
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('Kanselleer'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const Text('Stoor'),
          ),
        ],
      ),
    );
    if (save != true) return;
    try {
      await widget.service.updateMedia(widget.booking.id, item.id, {
        'caption': caption.text.trim(),
        'adjustedCaptureTimestamp': DateTime.tryParse(
          timestamp.text.trim(),
        )?.toUtc().toIso8601String(),
        'matchedRoutePointIndex': int.tryParse(point.text.trim()),
        'matchedPinId': pin.text.trim().isEmpty ? null : pin.text.trim(),
        'matchMethod': 'Manual',
      });
      await _load();
    } catch (e) {
      _show(e);
    }
  }

  Future<void> _delete(SharedBookingMedia item) async {
    final yes = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Verwyder foto?'),
        content: const Text(
          'Die gedeelde privaat lêer word verwyder en ’n klein ouditrekord bly behoue.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('Kanselleer'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const Text('Verwyder'),
          ),
        ],
      ),
    );
    if (yes != true) return;
    try {
      await widget.service.deleteMedia(widget.booking.id, item.id);
      await _load();
    } catch (e) {
      _show(e);
    }
  }

  Future<void> _moderate(SharedBookingMedia item, bool include) async {
    String? reason;
    if (!include) {
      final controller = TextEditingController();
      reason = await showDialog<String>(
        context: context,
        builder: (c) => AlertDialog(
          title: const Text('Sluit foto uit'),
          content: TextField(
            controller: controller,
            maxLines: 3,
            decoration: const InputDecoration(
              labelText: 'Rede',
              hintText:
                  'Verduidelik waarom dit nie in die terugblik hoort nie.',
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c),
              child: const Text('Kanselleer'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(c, controller.text.trim()),
              child: const Text('Sluit uit'),
            ),
          ],
        ),
      );
      controller.dispose();
      if (reason == null || reason.isEmpty) return;
    }
    try {
      await widget.service.moderateMedia(
        widget.booking.id,
        item.id,
        include ? 'Included' : 'Excluded',
        reason,
      );
      await _load();
    } catch (error) {
      _show(error);
    }
  }

  void _show(Object error) {
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            AppErrorMessage.from(error, fallback: 'Die aksie het misluk.'),
          ),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_items == null && _error == null) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_error != null) {
      return Center(
        child: AppFeedbackPanel(
          icon: Icons.cloud_off,
          title: 'Media kon nie laai nie',
          message: _error,
          action: TextButton(
            onPressed: _load,
            child: const Text('Probeer weer'),
          ),
        ),
      );
    }
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 120),
        children: [
          const HuntPhotoPrivacyNotice(),
          const SizedBox(height: 12),
          if (widget.session.can(MobileCapabilities.uploadBookingMedia))
            FilledButton.icon(
              onPressed: _uploading ? null : _pick,
              icon: const Icon(Icons.cloud_upload_outlined),
              label: Text(
                _uploading
                    ? 'Laai op ${(_progress * 100).round()}%'
                    : 'Laai jagfoto’s op',
              ),
            ),
          if (widget.session.can(MobileCapabilities.uploadBookingVideo)) ...[
            const SizedBox(height: 8),
            OutlinedButton.icon(
              onPressed: _uploading ? null : _pickVideo,
              icon: const Icon(Icons.videocam_outlined),
              label: const Text('Voeg ’n kort videogreep by (maks 30 s)'),
            ),
          ],
          if (_failed.isNotEmpty)
            TextButton.icon(
              onPressed: _uploading ? null : () => _upload(List.of(_failed)),
              icon: const Icon(Icons.refresh),
              label: const Text('Probeer mislukte oplaaie weer'),
            ),
          const SizedBox(height: 12),
          if (_items!.isEmpty)
            const AppFeedbackPanel(
              icon: Icons.photo_library_outlined,
              title: 'Nog geen gedeelde foto’s nie',
              message: 'Foto’s van alle gemagtigde deelnemers verskyn hier.',
            ),
          for (final item in _items!)
            Card(
              child: ListTile(
                leading: SizedBox(
                  width: 68,
                  height: 68,
                  child: item.isVideo
                      ? Container(
                          color: Colors.black26,
                          alignment: Alignment.center,
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              const Icon(Icons.play_circle_outline),
                              if (item.durationSeconds case final secs?)
                                Text(
                                  '${secs.round()} s',
                                  style: Theme.of(context).textTheme.labelSmall,
                                ),
                            ],
                          ),
                        )
                      : Image.network(
                          item.imageUrl,
                          headers: {
                            if (AuthService.instance.accessToken
                                case final token?)
                              'Authorization': 'Bearer $token',
                          },
                          fit: BoxFit.cover,
                          errorBuilder: (_, _, _) =>
                              const Icon(Icons.broken_image_outlined),
                        ),
                ),
                title: Text(
                  item.caption?.isNotEmpty == true
                      ? item.caption!
                      : item.originalFileName,
                ),
                subtitle: Text(
                  '${item.isVideo ? 'Video • ' : ''}${item.uploaderRole} • ${item.matchMethod} • ${item.matchConfidence}${item.inclusionStatus == 'Excluded' ? ' • Uitgesluit' : ''}',
                ),
                trailing: PopupMenuButton<String>(
                  onSelected: (value) async {
                    if (value == 'edit') await _edit(item);
                    if (value == 'delete') await _delete(item);
                    if (value == 'include') await _moderate(item, true);
                    if (value == 'exclude') await _moderate(item, false);
                    if (value == 'confirm') {
                      await widget.service.updateMedia(
                        widget.booking.id,
                        item.id,
                        {
                          'caption': item.caption,
                          'adjustedCaptureTimestamp': item
                              .adjustedCaptureTimestamp
                              ?.toIso8601String(),
                          'matchedRoutePointIndex': item.matchedRoutePointIndex,
                          'matchedPinId': item.matchedPinId,
                          'matchMethod': 'Manual',
                        },
                      );
                    }
                    await _load();
                  },
                  itemBuilder: (_) => [
                    if (item.needsConfirmation &&
                        item.uploaderUserId == widget.session.userId)
                      const PopupMenuItem(
                        value: 'confirm',
                        child: Text('Bevestig passing'),
                      ),
                    if (item.uploaderUserId == widget.session.userId) ...[
                      const PopupMenuItem(
                        value: 'edit',
                        child: Text('Verander plasing'),
                      ),
                      const PopupMenuItem(
                        value: 'delete',
                        child: Text('Verwyder'),
                      ),
                    ],
                    if (widget.session.can(
                      MobileCapabilities.moderateBookingMedia,
                    ))
                      PopupMenuItem(
                        value: item.inclusionStatus == 'Excluded'
                            ? 'include'
                            : 'exclude',
                        child: Text(
                          item.inclusionStatus == 'Excluded'
                              ? 'Sluit weer in'
                              : 'Hou uit die terugblik',
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

class _AcceptMetadataButton extends StatelessWidget {
  const _AcceptMetadataButton();
  @override
  Widget build(BuildContext context) => FilledButton(
    onPressed: () => Navigator.pop(context, true),
    child: const Text('Ek verstaan'),
  );
}

class _BookingRouteTab extends StatefulWidget {
  const _BookingRouteTab({
    required this.booking,
    required this.session,
    required this.service,
  });
  final Booking booking;
  final AppSession session;
  final BookingFeatureService service;
  @override
  State<_BookingRouteTab> createState() => _BookingRouteTabState();
}

class _BookingRouteTabState extends State<_BookingRouteTab> {
  List<BookingRouteSummary>? _routes;
  String? _error;
  bool _busy = false;
  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final r = await widget.service.listRoutes(widget.booking.id);
      if (mounted) {
        setState(() {
          _routes = r;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => _error = AppErrorMessage.from(
            e,
            fallback: 'Kon nie roetes laai nie.',
          ),
        );
      }
    }
  }

  Future<void> _submit() async {
    final sessions = await const HuntStorageService().listSessions();
    if (!mounted) return;
    if (sessions.isEmpty) {
      _show('Geen plaaslike voltooide jagroete is beskikbaar nie.');
      return;
    }
    final selected = await showDialog<HuntSession>(
      context: context,
      builder: (c) => SimpleDialog(
        title: const Text('Kies ’n jagroete'),
        children: [
          for (final s in sessions)
            SimpleDialogOption(
              onPressed: () => Navigator.pop(c, s),
              child: Text(
                '${DateFormat('d MMM yyyy').format(s.startedAt)} • ${(s.totalDistanceMeters / 1000).toStringAsFixed(1)} km',
              ),
            ),
        ],
      ),
    );
    if (selected == null) return;
    setState(() => _busy = true);
    try {
      await widget.service.submitRoute(widget.booking.id, selected);
      await _load();
    } catch (e) {
      _show(AppErrorMessage.from(e, fallback: 'Kon nie roete indien nie.'));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _select(BookingRouteSummary route) async {
    setState(() => _busy = true);
    try {
      await widget.service.selectRoute(widget.booking.id, route.id);
      await _load();
    } catch (e) {
      _show(AppErrorMessage.from(e, fallback: 'Kon nie roete kies nie.'));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _show(String message) {
    if (mounted) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(message)));
    }
  }

  @override
  Widget build(BuildContext context) {
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 120),
        children: [
          if (widget.session.can(MobileCapabilities.submitRoute))
            FilledButton.icon(
              onPressed: _busy ? null : _submit,
              icon: const Icon(Icons.upload_file),
              label: const Text('Dien jou jagroete in'),
            ),
          if (_error != null)
            AppFeedbackPanel(
              icon: Icons.error_outline,
              title: 'Roetes kon nie laai nie',
              message: _error,
            ),
          if (_routes == null && _error == null)
            const Center(child: CircularProgressIndicator()),
          if (_routes?.isEmpty == true)
            const AppFeedbackPanel(
              icon: Icons.route_outlined,
              title: 'Geen roetes ingedien nie',
              message:
                  '’n Jagter dien ’n roete in en die toegewysde PH kies dit vir die terugblik.',
            ),
          for (final route in _routes ?? const <BookingRouteSummary>[])
            Card(
              child: ListTile(
                leading: Icon(
                  route.selectedForRecap ? Icons.check_circle : Icons.route,
                  color: route.selectedForRecap ? AppTheme.goldSoft : null,
                ),
                title: Text(
                  '${(route.distanceMeters / 1000).toStringAsFixed(1)} km • ${route.pointCount} punte',
                ),
                subtitle: Text(
                  '${route.status} • weergawe ${route.routeVersion}',
                ),
                trailing:
                    widget.session.can(MobileCapabilities.selectRoute) &&
                        !route.selectedForRecap
                    ? TextButton(
                        onPressed: _busy ? null : () => _select(route),
                        child: const Text('Kies'),
                      )
                    : null,
              ),
            ),
        ],
      ),
    );
  }
}

class _BookingRecapTab extends StatefulWidget {
  const _BookingRecapTab({
    required this.booking,
    required this.session,
    required this.service,
  });
  final Booking booking;
  final AppSession session;
  final BookingFeatureService service;
  @override
  State<_BookingRecapTab> createState() => _BookingRecapTabState();
}

class _BookingRecapTabState extends State<_BookingRecapTab> {
  List<BookingRecap>? _recaps;
  final Map<int, RecapCleanupStatus> _cleanupStatuses = {};
  String? _error;
  bool _busy = false;
  Timer? _timer;
  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final r = await widget.service.listRecaps(widget.booking.id);
      if (!mounted) return;
      setState(() {
        _recaps = r;
        _error = null;
      });
      await _loadCleanupStatuses(r);
      _timer?.cancel();
      if (r.any((x) => x.isBusy)) {
        _timer = Timer(const Duration(seconds: 8), () => unawaited(_load()));
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => _error = AppErrorMessage.from(
            e,
            fallback: 'Kon nie recapstatus laai nie.',
          ),
        );
      }
    }
  }

  Future<void> _loadCleanupStatuses(List<BookingRecap> recaps) async {
    final trackedRecapIds = recaps
        .where(
          (recap) => recap.isPublished && recap.deletionStatus != 'Completed',
        )
        .map((recap) => recap.id)
        .toSet();
    final entries = await Future.wait(
      recaps.where((recap) => trackedRecapIds.contains(recap.id)).map((
        recap,
      ) async {
        try {
          final status = await widget.service.getCleanupStatus(
            widget.booking.id,
            recap.id,
          );
          return MapEntry(recap.id, status);
        } catch (_) {
          return null;
        }
      }),
    );
    if (!mounted) return;
    setState(() {
      _cleanupStatuses.removeWhere(
        (recapId, _) => !trackedRecapIds.contains(recapId),
      );
      for (final entry
          in entries.whereType<MapEntry<int, RecapCleanupStatus>>()) {
        _cleanupStatuses[entry.key] = entry.value;
      }
    });
  }

  Future<void> _act(Future<void> Function() call) async {
    setState(() => _busy = true);
    try {
      await call();
      await _load();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              AppErrorMessage.from(e, fallback: 'Die aksie het misluk.'),
            ),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _confirmPublish(BookingRecap recap) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Publiseer privaat op YouTube?'),
        content: const SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Bevestig die volgende voordat die oplaai begin:'),
              SizedBox(height: 12),
              _PublishFact(
                icon: Icons.lock_outline,
                text:
                    'Die video bly privaat. Net die kanaaleienaar en Google-rekeninge wat op YouTube genooi is, kan dit kyk.',
              ),
              _PublishFact(
                icon: Icons.delete_sweep_outlined,
                text:
                    'Nadat YouTube die video bevestig het, word die bronfoto’s en bediener-terugblik ná die ingestelde retensietyd verwyder. Trofeefoto’s en ouditrekords bly behoue.',
              ),
              _PublishFact(
                icon: Icons.restore_page_outlined,
                text:
                    'YouTube herkomprimeer die video en die API kan dit nie as ’n oorspronklike rugsteun teruglaai nie.',
              ),
              _PublishFact(
                icon: Icons.speed_outlined,
                text:
                    'Die kanaal se daaglikse API-kwota kan bepaal hoeveel recaps op een dag gepubliseer kan word.',
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Kanselleer'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Publiseer privaat'),
          ),
        ],
      ),
    );
    if (confirmed == true) {
      await _act(
        () => widget.service.publishRecap(widget.booking.id, recap.id),
      );
    }
  }

  Future<void> _openYoutube(String url) async {
    final opened = await launchUrl(
      Uri.parse(url),
      mode: LaunchMode.externalApplication,
    );
    if (!opened && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('YouTube kon nie oopmaak nie.')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 120),
        children: [
          const _RecapLifecycleNotice(),
          const SizedBox(height: 12),
          if (widget.session.can(MobileCapabilities.generateRecap))
            FilledButton.icon(
              onPressed:
                  _busy ||
                      (_recaps?.any(
                            (r) =>
                                r.status != 'Failed' && r.status != 'Archived',
                          ) ??
                          false)
                  ? null
                  : () => _act(
                      () => widget.service.requestRecap(
                        widget.booking.id,
                        '${widget.booking.title} • ${widget.booking.date}',
                      ),
                    ),
              icon: const Icon(Icons.movie_creation_outlined),
              label: const Text('Maak gedeelde terugblik'),
            ),
          if (_error != null)
            AppFeedbackPanel(
              icon: Icons.error_outline,
              title: 'Recaps kon nie laai nie',
              message: _error,
            ),
          if (_recaps == null && _error == null)
            const Center(child: CircularProgressIndicator()),
          if (_recaps?.isEmpty == true)
            const AppFeedbackPanel(
              icon: Icons.movie_outlined,
              title: 'Nog geen terugblik nie',
              message:
                  'Die gekose jagroete en alle ingeslote jagter-, PH- en VG-foto’s word in een bedienerbeheerde video gekombineer.',
            ),
          for (final recap in _recaps ?? const <BookingRecap>[])
            _buildRecapCard(context, recap),
        ],
      ),
    );
  }

  Widget _buildRecapCard(BuildContext context, BookingRecap recap) {
    final cleanup = _cleanupStatuses[recap.id];
    final progress = (recap.progress.clamp(0, 100) / 100).toDouble();
    final youtubeDetails = [
      _youtubeStatusLabel(recap.uploadStatus),
      if (recap.uploadStatus == 'Uploading') '${recap.uploadProgress}%',
      if (recap.youtubeProcessingStatus case final status?)
        _youtubeProcessingLabel(status),
    ].join(' • ');

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(recap.title, style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            Text(
              'Vordering: ${_recapStatusLabel(recap.status)} '
              '(${recap.progress.clamp(0, 100)}%)',
            ),
            LinearProgressIndicator(value: progress),
            const SizedBox(height: 8),
            Text('YouTube: $youtubeDetails'),
            Text('Skoonmaak: ${_cleanupStatusLabel(recap.deletionStatus)}'),
            if (cleanup != null) ...[
              const SizedBox(height: 4),
              _CleanupSchedule(status: cleanup),
            ],
            if (recap.youtubeVerifiedAt case final verifiedAt?)
              Text(
                'Deur YouTube bevestig: '
                '${DateFormat('d MMM yyyy HH:mm').format(verifiedAt.toLocal())}',
              ),
            if (recap.failureReason case final reason?) ...[
              const SizedBox(height: 6),
              Text(reason, style: const TextStyle(color: AppTheme.dangerSoft)),
            ],
            if (recap.canPublish && !widget.session.canPublishPrivateRecap) ...[
              const SizedBox(height: 8),
              const Text(
                'Hierdie terugblik bly op die bediener en tel teen die bespreking se '
                'berginglimiet totdat die toegewysde PH dit privaat publiseer.',
                style: TextStyle(color: AppTheme.goldSoft),
              ),
            ],
            if (recap.isPublished && !widget.session.isPh) ...[
              const SizedBox(height: 8),
              const Text(
                'Die YouTube-argief is privaat. Die skakel is net vir die '
                'kanaaleienaar en individueel-genooide Google-rekeninge beskikbaar.',
                style: TextStyle(color: AppTheme.muted),
              ),
            ],
            const SizedBox(height: 4),
            Wrap(
              spacing: 8,
              runSpacing: 4,
              children: [
                if (recap.playbackUrl case final url?)
                  TextButton.icon(
                    onPressed: () => Navigator.push(
                      context,
                      MaterialPageRoute(
                        builder: (_) => SharedRecapPlayerScreen(
                          url: url,
                          title: recap.title,
                        ),
                      ),
                    ),
                    icon: const Icon(Icons.play_arrow),
                    label: const Text('Kyk bediener-terugblik'),
                  ),
                if (recap.canPublish && widget.session.canPublishPrivateRecap)
                  FilledButton.icon(
                    onPressed: _busy ? null : () => _confirmPublish(recap),
                    icon: const Icon(Icons.cloud_upload),
                    label: const Text('Publiseer privaat'),
                  ),
                if (recap.youtubeUrl case final url?)
                  if (widget.session.isPh)
                    TextButton.icon(
                      onPressed: () => _openYoutube(url),
                      icon: const Icon(Icons.open_in_new),
                      label: const Text('Open private YouTube-video'),
                    ),
                if (recap.status == 'Failed')
                  TextButton(
                    onPressed: _busy
                        ? null
                        : () => _act(
                            () => widget.service.retryRecap(
                              widget.booking.id,
                              recap.id,
                            ),
                          ),
                    child: const Text('Probeer terugblik weer'),
                  ),
                if (widget.session.canPublishPrivateRecap &&
                    const {
                      'Failed',
                      'RetryableFailure',
                      'VerificationFailed',
                    }.contains(recap.uploadStatus))
                  TextButton(
                    onPressed: _busy
                        ? null
                        : () => _act(
                            () => widget.service.retryPublish(
                              widget.booking.id,
                              recap.id,
                            ),
                          ),
                    child: const Text('Probeer publikasie weer'),
                  ),
                if (recap.isPublished &&
                    recap.deletionStatus != 'Completed' &&
                    widget.session.canPublishPrivateRecap)
                  TextButton(
                    onPressed: _busy ? null : () => _confirmCleanup(recap),
                    child: const Text('Versoek onmiddellike veilige skoonmaak'),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _confirmCleanup(BookingRecap recap) async {
    final yes = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Maak bronmedia nou skoon?'),
        content: const Text(
          'Slegs groot bronfoto’s en die private terugblik word verwyder nadat die YouTube-video en permanente argief reeds onafhanklik bevestig is. Trofeefoto’s en ouditmetadata bly behoue.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('Wag vir retensie'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const Text('Bevestig'),
          ),
        ],
      ),
    );
    if (yes == true) {
      await _act(
        () =>
            widget.service.requestImmediateCleanup(widget.booking.id, recap.id),
      );
    }
  }
}

class _RecapLifecycleNotice extends StatelessWidget {
  const _RecapLifecycleNotice();

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(14),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.shield_outlined, color: AppTheme.goldSoft),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              'Foto’s en die gegenereerde terugblik bly privaat op die Mikpunt-bediener. '
              'Slegs ’n PH kan die finale video privaat na die besigheid se '
              'YouTube-kanaal publiseer. Ná YouTube-verifikasie word bronmedia '
              'volgens die bediener se retensietyd verwyder; trofeefoto’s en '
              'permanente ouditrekords bly behoue.',
              style: Theme.of(
                context,
              ).textTheme.bodySmall?.copyWith(color: AppTheme.muted),
            ),
          ),
        ],
      ),
    ),
  );
}

class _PublishFact extends StatelessWidget {
  const _PublishFact({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 20, color: AppTheme.goldSoft),
        const SizedBox(width: 10),
        Expanded(child: Text(text)),
      ],
    ),
  );
}

class _CleanupSchedule extends StatelessWidget {
  const _CleanupSchedule({required this.status});

  final RecapCleanupStatus status;

  @override
  Widget build(BuildContext context) {
    final localFormat = DateFormat('d MMM yyyy HH:mm');
    final (icon, message, color) = status.isComplete
        ? (
            Icons.check_circle_outline,
            status.sourceFilesDeletedAt == null
                ? 'Bronmedia is veilig verwyder.'
                : 'Bronmedia verwyder: '
                      '${localFormat.format(status.sourceFilesDeletedAt!.toLocal())}',
            AppTheme.muted,
          )
        : (
            Icons.schedule_outlined,
            'Outomatiese skoonmaak vanaf: '
                '${localFormat.format(status.notBefore.toLocal())}',
            AppTheme.goldSoft,
          );

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 18, color: color),
        const SizedBox(width: 6),
        Expanded(
          child: Text(
            status.failureReason == null
                ? message
                : '$message • ${status.failureReason}',
            style: TextStyle(color: color),
          ),
        ),
      ],
    );
  }
}

String _recapStatusLabel(String status) => switch (status) {
  'Queued' => 'In die ry',
  'Processing' => 'Word saamgestel',
  'Completed' => 'Gereed',
  'Uploading' => 'Word gepubliseer',
  'Published' => 'Privaat gepubliseer',
  'Archived' => 'Geargiveer',
  'Failed' => 'Misluk',
  _ => status,
};

String _youtubeStatusLabel(String status) => switch (status) {
  'NotRequested' => 'Nog nie versoek nie',
  'Queued' => 'In die ry',
  'Uploading' => 'Laai op',
  'Uploaded' => 'Opgelaai; wag vir verwerking',
  'Processing' => 'YouTube verwerk die video',
  'Published' => 'Privaat gepubliseer en bevestig',
  'Failed' => 'Misluk',
  'RetryableFailure' => 'Tydelik misluk',
  'VerificationFailed' => 'Verifikasie het misluk',
  'PermanentlyFailed' => 'Permanent misluk',
  _ => status,
};

String _youtubeProcessingLabel(String status) => switch (status.toLowerCase()) {
  'uploaded' => 'Opgelaai',
  'processing' => 'Verwerk',
  'processed' => 'Verwerk en bevestig',
  'failed' => 'Verwerking het misluk',
  'rejected' => 'Deur YouTube geweier',
  _ => status,
};

String _cleanupStatusLabel(String status) => switch (status) {
  'NotRequested' => 'Nie geskeduleer nie',
  'CleanupPending' => 'Veilig geskeduleer',
  'Processing' => 'Word skoongemaak',
  'Completed' => 'Bronmedia verwyder',
  'PartialFailure' => 'Onvolledig; probeer weer',
  _ => status,
};

class SharedRecapPlayerScreen extends StatefulWidget {
  const SharedRecapPlayerScreen({
    super.key,
    required this.url,
    required this.title,
  });
  final String url, title;
  @override
  State<SharedRecapPlayerScreen> createState() =>
      _SharedRecapPlayerScreenState();
}

class _SharedRecapPlayerScreenState extends State<SharedRecapPlayerScreen> {
  VideoPlayerController? _controller;
  String? _error;
  @override
  void initState() {
    super.initState();
    unawaited(_init());
  }

  Future<void> _init() async {
    try {
      final c = VideoPlayerController.networkUrl(
        Uri.parse(widget.url),
        httpHeaders: {
          if (AuthService.instance.accessToken case final token?)
            'Authorization': 'Bearer $token',
        },
      );
      await c.initialize();
      if (!mounted) {
        await c.dispose();
        return;
      }
      setState(() => _controller = c);
      await c.play();
    } catch (e) {
      if (mounted) {
        setState(() => _error = 'Die private video kon nie speel nie.');
      }
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(widget.title)),
    body: Center(
      child: _error != null
          ? Text(_error!)
          : _controller == null
          ? const CircularProgressIndicator()
          : AspectRatio(
              aspectRatio: _controller!.value.aspectRatio,
              child: VideoPlayer(_controller!),
            ),
    ),
  );
}

class _TrophySubmissionTab extends StatefulWidget {
  const _TrophySubmissionTab({required this.booking, required this.service});
  final Booking booking;
  final BookingFeatureService service;
  @override
  State<_TrophySubmissionTab> createState() => _TrophySubmissionTabState();
}

class _TrophySubmissionTabState extends State<_TrophySubmissionTab> {
  final _species = TextEditingController();
  final _location = TextEditingController();
  final _distance = TextEditingController();
  final _antlerSize = TextEditingController();
  final _carcassWeight = TextEditingController();
  final _firearm = TextEditingController();
  final _notes = TextEditingController();
  final _picker = ImagePicker();
  final _trophies = TrophyService();
  final Set<int> _supportingMediaIds = {};
  List<SharedBookingMedia>? _bookingMedia;
  List<TrophySubmission>? _submissions;
  String? _path;
  late DateTime _harvestDate;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _harvestDate = widget.booking.endDate;
    unawaited(_loadContext());
  }

  Future<void> _loadContext() async {
    try {
      final values = await Future.wait<Object>([
        widget.service.listMedia(widget.booking.id),
        widget.service.listTrophySubmissions(),
      ]);
      if (!mounted) return;
      setState(() {
        _bookingMedia = values[0] as List<SharedBookingMedia>;
        _submissions = (values[1] as List<TrophySubmission>)
            .where((item) => item.bookingId == widget.booking.id)
            .toList(growable: false);
      });
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              AppErrorMessage.from(
                error,
                fallback:
                    'Kon nie trofeestatus en ondersteunende media laai nie.',
              ),
            ),
          ),
        );
      }
    }
  }

  @override
  void dispose() {
    for (final c in [
      _species,
      _location,
      _distance,
      _antlerSize,
      _carcassWeight,
      _firearm,
      _notes,
    ]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _pick() async {
    final f = await _picker.pickImage(
      source: ImageSource.gallery,
      imageQuality: 92,
    );
    if (f != null && mounted) setState(() => _path = f.path);
  }

  Future<void> _submit() async {
    if (_species.text.trim().isEmpty ||
        _location.text.trim().isEmpty ||
        _firearm.text.trim().isEmpty ||
        _path == null) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Voltooi spesie, ligging, vuurwapen en trofeefoto.'),
        ),
      );
      return;
    }
    setState(() => _busy = true);
    String? uploaded;
    try {
      uploaded = await _trophies.uploadPhoto(_path!);
      await widget.service.submitTrophy({
        'bookingId': widget.booking.id,
        'species': _species.text.trim(),
        'trophyPhotoPath': uploaded,
        'harvestDate': _harvestDate.toUtc().toIso8601String(),
        'locationText': _location.text.trim(),
        'shotDistanceMeters': int.tryParse(_distance.text.trim()),
        'antlerSizeCm': double.tryParse(
          _antlerSize.text.trim().replaceAll(',', '.'),
        ),
        'carcassWeightKg': double.tryParse(
          _carcassWeight.text.trim().replaceAll(',', '.'),
        ),
        'firearmType': _firearm.text.trim(),
        'notes': _notes.text.trim(),
        'supportingMediaIds': _supportingMediaIds.toList(growable: false),
      });
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Trofee ingedien. Dit wag nou op die PH se goedkeuring.',
            ),
          ),
        );
        _species.clear();
        _location.clear();
        _distance.clear();
        _antlerSize.clear();
        _carcassWeight.clear();
        _firearm.clear();
        _notes.clear();
        setState(() {
          _path = null;
          _supportingMediaIds.clear();
        });
        await _loadContext();
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              AppErrorMessage.from(e, fallback: 'Kon nie trofee indien nie.'),
            ),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _chooseHarvestDate() async {
    final selected = await showDatePicker(
      context: context,
      initialDate: _harvestDate,
      firstDate: widget.booking.startDate,
      lastDate: widget.booking.endDate,
    );
    if (selected != null && mounted) setState(() => _harvestDate = selected);
  }

  Future<void> _resubmit(TrophySubmission submission) async {
    final notes = TextEditingController(text: submission.notes ?? '');
    final correctedNotes = await showDialog<String>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Dien regstelling weer in'),
        content: TextField(
          controller: notes,
          maxLines: 4,
          decoration: InputDecoration(
            labelText: 'Bygewerkte notas',
            helperText: submission.reviewNotes,
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c),
            child: const Text('Kanselleer'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, notes.text.trim()),
            child: const Text('Gaan voort'),
          ),
        ],
      ),
    );
    notes.dispose();
    if (correctedNotes == null || !mounted) return;
    final replacePhoto = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Vervang trofeefoto?'),
        content: const Text(
          'Kies “Vervang” indien die PH ’n duideliker trofeefoto versoek het.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('Hou huidige foto'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const Text('Vervang'),
          ),
        ],
      ),
    );
    String? replacement;
    setState(() => _busy = true);
    try {
      if (replacePhoto == true) {
        final selected = await _picker.pickImage(
          source: ImageSource.gallery,
          imageQuality: 92,
        );
        if (selected == null) return;
        replacement = await _trophies.uploadPhoto(selected.path);
      }
      await widget.service.resubmitTrophy(
        submission.id,
        correctedNotes,
        replacement,
      );
      await _loadContext();
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              AppErrorMessage.from(
                error,
                fallback: 'Kon nie die regstelling weer indien nie.',
              ),
            ),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.fromLTRB(16, 16, 16, 120),
    children: [
      const AppFeedbackPanel(
        icon: Icons.verified_user_outlined,
        title: 'PH-goedkeuring is verpligtend',
        message:
            'Die trofee wag op goedkeuring totdat die toegewysde PH dit goedkeur. Eers dan verskyn dit in die Trofeekamer, Rowland Ward en ranglys.',
      ),
      if (_submissions == null)
        const LinearProgressIndicator()
      else
        for (final submission in _submissions!)
          Card(
            child: ListTile(
              leading: const Icon(Icons.workspace_premium_outlined),
              title: Text('${submission.species} • ${submission.status}'),
              subtitle: Text(
                '${DateFormat('d MMM yyyy').format(submission.harvestDate)}'
                '${submission.reviewNotes?.isNotEmpty == true ? '\nPH: ${submission.reviewNotes}' : ''}',
              ),
              isThreeLine: submission.reviewNotes?.isNotEmpty == true,
              trailing: submission.status == 'CorrectionRequested'
                  ? TextButton(
                      onPressed: _busy ? null : () => _resubmit(submission),
                      child: const Text('Maak reg'),
                    )
                  : null,
            ),
          ),
      const SizedBox(height: 12),
      TextField(
        controller: _species,
        decoration: const InputDecoration(labelText: 'Spesie'),
      ),
      ListTile(
        contentPadding: EdgeInsets.zero,
        title: const Text('Oesdatum'),
        subtitle: Text(DateFormat('d MMMM yyyy').format(_harvestDate)),
        trailing: const Icon(Icons.calendar_month_outlined),
        onTap: _busy ? null : _chooseHarvestDate,
      ),
      TextField(
        controller: _location,
        decoration: const InputDecoration(
          labelText: 'Ligging (geen presiese GPS nodig nie)',
        ),
      ),
      TextField(
        controller: _distance,
        keyboardType: TextInputType.number,
        decoration: const InputDecoration(labelText: 'Skootafstand (m)'),
      ),
      TextField(
        controller: _antlerSize,
        keyboardType: const TextInputType.numberWithOptions(decimal: true),
        decoration: const InputDecoration(
          labelText: 'Opsionele horingmaat (cm)',
        ),
      ),
      TextField(
        controller: _carcassWeight,
        keyboardType: const TextInputType.numberWithOptions(decimal: true),
        decoration: const InputDecoration(
          labelText: 'Opsionele karkasgewig (kg)',
        ),
      ),
      TextField(
        controller: _firearm,
        decoration: const InputDecoration(labelText: 'Vuurwapentipe'),
      ),
      TextField(
        controller: _notes,
        maxLines: 3,
        decoration: const InputDecoration(labelText: 'Notas'),
      ),
      const SizedBox(height: 12),
      OutlinedButton.icon(
        onPressed: _busy ? null : _pick,
        icon: const Icon(Icons.photo_camera_back_outlined),
        label: Text(_path == null ? 'Kies trofeefoto' : 'Foto gekies'),
      ),
      const SizedBox(height: 8),
      Text(
        'Ondersteunende bookingfoto’s (${_supportingMediaIds.length}/50)',
        style: Theme.of(context).textTheme.titleSmall,
      ),
      if (_bookingMedia == null)
        const Padding(
          padding: EdgeInsets.symmetric(vertical: 8),
          child: LinearProgressIndicator(),
        )
      else if (_bookingMedia!
          .where((item) => item.inclusionStatus != 'Excluded')
          .isEmpty)
        const Padding(
          padding: EdgeInsets.symmetric(vertical: 8),
          child: Text('Geen geskikte gedeelde foto’s is beskikbaar nie.'),
        )
      else
        for (final item in _bookingMedia!.where(
          (candidate) => candidate.inclusionStatus != 'Excluded',
        ))
          CheckboxListTile(
            value: _supportingMediaIds.contains(item.id),
            onChanged: _busy
                ? null
                : (selected) {
                    setState(() {
                      if (selected == true && _supportingMediaIds.length < 50) {
                        _supportingMediaIds.add(item.id);
                      } else if (selected != true) {
                        _supportingMediaIds.remove(item.id);
                      }
                    });
                  },
            title: Text(
              item.caption?.isNotEmpty == true
                  ? item.caption!
                  : item.originalFileName,
            ),
            subtitle: Text('${item.uploaderRole} • ${item.matchConfidence}'),
            secondary: SizedBox(
              width: 52,
              height: 52,
              child: Image.network(
                item.imageUrl,
                headers: {
                  if (AuthService.instance.accessToken case final token?)
                    'Authorization': 'Bearer $token',
                },
                fit: BoxFit.cover,
                errorBuilder: (_, _, _) => const Icon(Icons.broken_image),
              ),
            ),
          ),
      FilledButton.icon(
        onPressed: _busy ? null : _submit,
        icon: const Icon(Icons.send),
        label: Text(_busy ? 'Dien in…' : 'Dien in vir goedkeuring'),
      ),
    ],
  );
}
