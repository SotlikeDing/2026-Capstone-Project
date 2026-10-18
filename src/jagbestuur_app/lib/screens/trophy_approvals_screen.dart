import 'dart:async';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../models/shared_booking.dart';
import '../services/auth_service.dart';
import '../services/booking_feature_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../widgets/app_feedback_panel.dart';
import '../widgets/app_logo.dart';

class TrophyApprovalsScreen extends StatefulWidget {
  const TrophyApprovalsScreen({super.key, this.activation = 0});
  final int activation;
  @override
  State<TrophyApprovalsScreen> createState() => _TrophyApprovalsScreenState();
}

class _TrophyApprovalsScreenState extends State<TrophyApprovalsScreen> {
  final BookingFeatureService _service = BookingFeatureService();
  List<TrophySubmission>? _items;
  String? _error;
  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(covariant TrophyApprovalsScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.activation != widget.activation) unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final rows = await _service.listTrophySubmissions();
      if (mounted) {
        setState(() {
          _items = rows;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => _error = AppErrorMessage.from(
            e,
            fallback: 'Kon nie trofee-indienings laai nie.',
          ),
        );
      }
    }
  }

  Future<void> _open(TrophySubmission item) async {
    final reviewed = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) =>
            TrophyApprovalDetailScreen(submission: item, service: _service),
      ),
    );
    if (reviewed == true) await _load();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    backgroundColor: Colors.transparent,
    body: Container(
      decoration: AppTheme.pageDecoration,
      child: SafeArea(
        child: RefreshIndicator(
          onRefresh: _load,
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 120),
            children: [
              const Center(child: AppLogo(compact: true, showTagline: false)),
              const SizedBox(height: 20),
              Text(
                'Trofee-goedkeurings',
                style: Theme.of(context).textTheme.headlineSmall,
              ),
              const Text(
                'Slegs indienings van besprekings waaraan jy as PH toegewys is.',
                style: TextStyle(color: AppTheme.muted),
              ),
              const SizedBox(height: 16),
              if (_items == null && _error == null)
                const Center(child: CircularProgressIndicator()),
              if (_error != null)
                AppFeedbackPanel(
                  icon: Icons.error_outline,
                  title: 'Indienings kon nie laai nie',
                  message: _error,
                  action: TextButton(
                    onPressed: _load,
                    child: const Text('Probeer weer'),
                  ),
                ),
              if (_items?.isEmpty == true)
                const AppFeedbackPanel(
                  icon: Icons.fact_check_outlined,
                  title: 'Geen hangende indienings nie',
                  message:
                      'Nuwe indienings deur jagters van jou toegewysde jagte sal hier verskyn.',
                ),
              for (final item in _items ?? const <TrophySubmission>[])
                Card(
                  child: ListTile(
                    onTap: () => _open(item),
                    leading: CircleAvatar(
                      backgroundImage: NetworkImage(
                        item.photoUrl,
                        headers: {
                          if (AuthService.instance.accessToken
                              case final token?)
                            'Authorization': 'Bearer $token',
                        },
                      ),
                    ),
                    title: Text(item.species),
                    subtitle: Text(
                      '${item.bookingTitle}\n${item.hunterName} • ${DateFormat('d MMM yyyy').format(item.createdAt)} • ${item.supportingPhotoCount} foto’s',
                    ),
                    isThreeLine: true,
                    trailing: _StatusChip(item.status),
                  ),
                ),
            ],
          ),
        ),
      ),
    ),
  );
}

class TrophyApprovalDetailScreen extends StatefulWidget {
  const TrophyApprovalDetailScreen({
    super.key,
    required this.submission,
    required this.service,
  });
  final TrophySubmission submission;
  final BookingFeatureService service;
  @override
  State<TrophyApprovalDetailScreen> createState() =>
      _TrophyApprovalDetailScreenState();
}

class _TrophyApprovalDetailScreenState
    extends State<TrophyApprovalDetailScreen> {
  bool _busy = false;
  bool _loadingContext = true;
  String? _contextError;
  List<SharedBookingMedia> _supportingMedia = const [];
  BookingRouteSummary? _selectedRoute;

  @override
  void initState() {
    super.initState();
    unawaited(_loadContext());
  }

  Future<void> _loadContext() async {
    try {
      final results = await Future.wait<Object>([
        widget.service.listMedia(widget.submission.bookingId),
        widget.service.listRoutes(widget.submission.bookingId),
      ]);
      final media = results[0] as List<SharedBookingMedia>;
      final routes = results[1] as List<BookingRouteSummary>;
      BookingRouteSummary? selectedRoute;
      for (final route in routes) {
        if (route.selectedForRecap) {
          selectedRoute = route;
          break;
        }
      }
      if (!mounted) return;
      setState(() {
        _supportingMedia = media
            .where(
              (item) => widget.submission.supportingMediaIds.contains(item.id),
            )
            .toList(growable: false);
        _selectedRoute = selectedRoute;
        _loadingContext = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _contextError = AppErrorMessage.from(
          error,
          fallback: 'Kon nie ondersteunende bewys laai nie.',
        );
        _loadingContext = false;
      });
    }
  }

  Future<void> _review(String action) async {
    String? reason;
    if (action != 'Keur goed') {
      final controller = TextEditingController();
      reason = await showDialog<String>(
        context: context,
        builder: (c) => AlertDialog(
          title: Text(
            action == 'Keur af'
                ? 'Rede vir afkeuring'
                : 'Verduidelik die regstelling',
          ),
          content: TextField(
            controller: controller,
            maxLines: 4,
            decoration: const InputDecoration(
              hintText: 'Verpligte verduideliking',
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c),
              child: const Text('Kanselleer'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(c, controller.text.trim()),
              child: const Text('Gaan voort'),
            ),
          ],
        ),
      );
      controller.dispose();
      if (reason == null || reason.trim().isEmpty) return;
    } else {
      final confirm = await showDialog<bool>(
        context: context,
        builder: (c) => AlertDialog(
          title: const Text('Keur trofee goed?'),
          content: const Text(
            'Hierdie aksie plaas die trofee in die jagter se Trofeekamer en maak dit vir Rowland Ward en die ranglys geskik.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c, false),
              child: const Text('Kanselleer'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(c, true),
              child: const Text('Keur goed'),
            ),
          ],
        ),
      );
      if (confirm != true) return;
    }
    setState(() => _busy = true);
    try {
      await widget.service.reviewTrophy(widget.submission.id, action, reason);
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              AppErrorMessage.from(
                e,
                fallback: 'Kon nie die hersiening voltooi nie.',
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
  Widget build(BuildContext context) {
    final s = widget.submission;
    final headers = {
      if (AuthService.instance.accessToken case final token?)
        'Authorization': 'Bearer $token',
    };
    return Scaffold(
      appBar: AppBar(title: Text(s.species)),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          AspectRatio(
            aspectRatio: 4 / 3,
            child: Image.network(
              s.photoUrl,
              headers: headers,
              fit: BoxFit.cover,
              errorBuilder: (_, _, _) =>
                  const Center(child: Icon(Icons.broken_image, size: 48)),
            ),
          ),
          const SizedBox(height: 16),
          _line('Status', s.status),
          _line('Jagter', s.hunterName),
          _line('Bespreking', s.bookingTitle),
          _line('Oesdatum', DateFormat('d MMM yyyy').format(s.harvestDate)),
          _line('Ligging', s.locationText),
          _line('Vuurwapen', s.firearmType),
          if (s.antlerSizeCm != null)
            _line('Horingmaat', '${s.antlerSizeCm!.toStringAsFixed(1)} cm'),
          if (s.carcassWeightKg != null)
            _line('Karkasgewig', '${s.carcassWeightKg!.toStringAsFixed(1)} kg'),
          if (s.shotDistanceMeters != null)
            _line('Skootafstand', '${s.shotDistanceMeters} m'),
          if (s.notes?.isNotEmpty == true) _line('Notas', s.notes!),
          _line('Ondersteunende foto’s', '${s.supportingPhotoCount}'),
          if (_loadingContext)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 12),
              child: LinearProgressIndicator(),
            ),
          if (_contextError != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: Text(
                _contextError!,
                style: const TextStyle(color: Colors.orangeAccent),
              ),
            ),
          if (_selectedRoute case final route?)
            Card(
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const ListTile(
                      contentPadding: EdgeInsets.zero,
                      leading: Icon(Icons.route),
                      title: Text('Geselekteerde roete'),
                    ),
                    Text(
                      '${(route.distanceMeters / 1000).toStringAsFixed(2)} km • '
                      '${route.durationSeconds ~/ 60} min • ${route.pointCount} punte',
                    ),
                    if (route.pins.isNotEmpty) ...[
                      const SizedBox(height: 8),
                      const Text(
                        'Relevante jagmerkers',
                        style: TextStyle(fontWeight: FontWeight.w700),
                      ),
                      for (final pin in route.pins)
                        Text(
                          '• ${pin.type} • ${DateFormat('d MMM HH:mm').format(pin.timestamp.toLocal())}'
                          '${pin.note?.isNotEmpty == true ? ' • ${pin.note}' : ''}',
                        ),
                    ],
                  ],
                ),
              ),
            ),
          if (_supportingMedia.isNotEmpty) ...[
            const Padding(
              padding: EdgeInsets.only(top: 12, bottom: 8),
              child: Text(
                'Ondersteunende bewys',
                style: TextStyle(fontWeight: FontWeight.w700),
              ),
            ),
            SizedBox(
              height: 110,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: _supportingMedia.length,
                separatorBuilder: (_, _) => const SizedBox(width: 8),
                itemBuilder: (context, index) {
                  final item = _supportingMedia[index];
                  return ClipRRect(
                    borderRadius: BorderRadius.circular(8),
                    child: AspectRatio(
                      aspectRatio: 4 / 3,
                      child: Image.network(
                        item.imageUrl,
                        headers: headers,
                        fit: BoxFit.cover,
                        errorBuilder: (_, _, _) => const ColoredBox(
                          color: Colors.black12,
                          child: Center(child: Icon(Icons.broken_image)),
                        ),
                      ),
                    ),
                  );
                },
              ),
            ),
          ],
          const SizedBox(height: 16),
          if (s.status == 'Pending' || s.status == 'Resubmitted') ...[
            FilledButton.icon(
              onPressed: _busy ? null : () => _review('Keur goed'),
              icon: const Icon(Icons.check),
              label: const Text('Keur goed'),
            ),
            OutlinedButton.icon(
              onPressed: _busy ? null : () => _review('RequestCorrection'),
              icon: const Icon(Icons.edit_note),
              label: const Text('Versoek regstelling'),
            ),
            OutlinedButton.icon(
              onPressed: _busy ? null : () => _review('Keur af'),
              icon: const Icon(Icons.close),
              label: const Text('Keur af'),
            ),
          ],
        ],
      ),
    );
  }

  Widget _line(String label, String value) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 6),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 120,
          child: Text(
            label,
            style: const TextStyle(
              color: AppTheme.muted,
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
        Expanded(child: Text(value)),
      ],
    ),
  );
}

class _StatusChip extends StatelessWidget {
  const _StatusChip(this.status);
  final String status;
  @override
  Widget build(BuildContext context) => Chip(
    label: Text(status),
    avatar: Icon(
      status == 'Approved'
          ? Icons.check_circle
          : status == 'Rejected'
          ? Icons.cancel
          : Icons.hourglass_top,
      size: 17,
    ),
  );
}
