import 'dart:async';

import 'package:flutter/material.dart';

import '../models/booking.dart';
import '../models/shared_booking.dart';
import '../services/booking_feature_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../widgets/app_feedback_panel.dart';

/// Post-trip ratings between the Hunter, PH and VG of one finished booking.
/// Who may rate whom (and when) is decided by the server; this screen shows
/// the other participants with the user's current rating and saves changes.
class TeamRatingScreen extends StatefulWidget {
  const TeamRatingScreen({super.key, required this.booking, this.service});

  final Booking booking;
  final BookingFeatureService? service;

  @override
  State<TeamRatingScreen> createState() => _TeamRatingScreenState();
}

class _TeamRatingScreenState extends State<TeamRatingScreen> {
  late final BookingFeatureService _service =
      widget.service ?? BookingFeatureService();
  BookingTeamRating? _team;
  String? _error;
  final Map<int, int> _stars = {};
  final Map<int, TextEditingController> _comments = {};
  final Set<int> _saving = {};

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    for (final controller in _comments.values) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final team = await _service.getTeamRatings(widget.booking.id);
      if (!mounted) return;
      setState(() {
        _team = team;
        _error = null;
        for (final member in team.members) {
          _stars[member.userId] = member.myStars ?? 0;
          _comments.putIfAbsent(member.userId, TextEditingController.new).text =
              member.myComment ?? '';
        }
      });
    } catch (e) {
      if (mounted) {
        setState(
          () => _error = AppErrorMessage.from(
            e,
            fallback: 'Kon nie die span laai nie.',
          ),
        );
      }
    }
  }

  Future<void> _save(BookingTeamMember member) async {
    final stars = _stars[member.userId] ?? 0;
    if (stars == 0) return;
    setState(() => _saving.add(member.userId));
    String message;
    try {
      await _service.rateTeamMember(
        widget.booking.id,
        rateeUserId: member.userId,
        rateeRoleCode: member.roleCode,
        stars: stars,
        comment: _comments[member.userId]?.text,
      );
      message = 'Gradering vir ${member.name} gestoor.';
    } catch (e) {
      message = AppErrorMessage.from(
        e,
        fallback: 'Kon nie die gradering stoor nie.',
      );
    }
    if (!mounted) return;
    setState(() => _saving.remove(member.userId));
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Gradeer die span')),
      body: _buildBody(context),
    );
  }

  Widget _buildBody(BuildContext context) {
    if (_error != null) {
      return Center(
        child: AppFeedbackPanel(
          icon: Icons.cloud_off,
          title: 'Kon nie laai nie',
          message: _error,
          action: TextButton(
            onPressed: _load,
            child: const Text('Probeer weer'),
          ),
        ),
      );
    }
    final team = _team;
    if (team == null) return const Center(child: CircularProgressIndicator());
    if (!team.canRate || team.members.isEmpty) {
      return Center(
        child: AppFeedbackPanel(
          icon: Icons.star_outline_rounded,
          title: 'Niks om te gradeer nie',
          message:
              team.reason ??
              'Daar is niemand anders op hierdie bespreking om te gradeer nie.',
        ),
      );
    }
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Text(
          'U gradering verskyn op hul profiel. U kan dit later aanpas.',
          style: Theme.of(context).textTheme.bodySmall,
        ),
        const SizedBox(height: 12),
        for (final member in team.members)
          Card(
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    member.name,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  Text(member.roleLabel),
                  const SizedBox(height: 6),
                  Row(
                    children: [
                      for (var star = 1; star <= 5; star++)
                        IconButton(
                          tooltip: '$star ster${star == 1 ? '' : 're'}',
                          onPressed: _saving.contains(member.userId)
                              ? null
                              : () => setState(
                                  () => _stars[member.userId] = star,
                                ),
                          icon: Icon(
                            star <= (_stars[member.userId] ?? 0)
                                ? Icons.star_rounded
                                : Icons.star_outline_rounded,
                            color: AppTheme.gold,
                          ),
                        ),
                    ],
                  ),
                  TextField(
                    controller: _comments[member.userId],
                    maxLength: 1000,
                    decoration: const InputDecoration(
                      labelText: 'Opsionele kommentaar',
                    ),
                  ),
                  Align(
                    alignment: Alignment.centerRight,
                    child: FilledButton(
                      onPressed:
                          (_stars[member.userId] ?? 0) == 0 ||
                              _saving.contains(member.userId)
                          ? null
                          : () => _save(member),
                      child: Text(
                        member.myStars == null ? 'Gradeer' : 'Werk by',
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}
