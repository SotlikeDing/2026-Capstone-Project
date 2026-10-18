import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../models/hunt_session.dart';
import '../safety/app_error_barrier.dart';
import '../screens/hike_recap_screen.dart';
import '../screens/hunt_photo_manager_screen.dart';
import '../services/hunt_storage_service.dart';
import '../theme/app_theme.dart';

class RecapSection extends StatefulWidget {
  const RecapSection({super.key});

  @override
  State<RecapSection> createState() => _RecapSectionState();
}

class _RecapSectionState extends State<RecapSection> {
  final HuntStorageService _storage = const HuntStorageService();
  late Future<List<HuntSession>> _sessionsFuture;

  @override
  void initState() {
    super.initState();
    _reload();
    HuntStorageService.sessionsVersion.addListener(_onSessionsChanged);
  }

  @override
  void dispose() {
    HuntStorageService.sessionsVersion.removeListener(_onSessionsChanged);
    super.dispose();
  }

  void _onSessionsChanged() {
    if (!mounted) {
      return;
    }
    setState(_reload);
  }

  void _reload() {
    _sessionsFuture = _storage.listSessions();
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
          child: Row(
            children: [
              const Expanded(
                child: Text(
                  'Jag-terugblikke',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800),
                ),
              ),
              IconButton(
                onPressed: () => setState(_reload),
                icon: const Icon(Icons.refresh_rounded),
                tooltip: 'Laai terugblikke weer',
              ),
            ],
          ),
        ),
        FutureBuilder<List<HuntSession>>(
          future: _sessionsFuture,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Padding(
                padding: EdgeInsets.all(16),
                child: Center(child: CircularProgressIndicator()),
              );
            }

            final sessions = snapshot.data ?? const <HuntSession>[];

            if (sessions.isEmpty) {
              return const Padding(
                padding: EdgeInsets.symmetric(vertical: 16),
                child: Text(
                  'Nog geen jag-terugblikke gestoor nie.',
                  style: TextStyle(color: AppTheme.muted),
                ),
              );
            }

            final dateFormat = DateFormat('d MMM yyyy, HH:mm');

            return ListView.separated(
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              itemCount: sessions.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final session = sessions[index];
                final km = (session.totalDistanceMeters / 1000).toStringAsFixed(
                  2,
                );

                return Container(
                  decoration: BoxDecoration(
                    color: AppTheme.card,
                    borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
                    border: Border.all(color: AppTheme.border),
                  ),
                  child: ListTile(
                    leading: const CircleAvatar(
                      backgroundColor: AppTheme.gold,
                      child: Icon(Icons.route_rounded, color: Colors.black),
                    ),
                    title: Text(dateFormat.format(session.startedAt)),
                    subtitle: Text(
                      '$km km • ${session.duration.inMinutes} min • ${session.pins.length} merkers',
                    ),
                    trailing: PopupMenuButton<String>(
                      onSelected: (value) async {
                        if (value == 'photos') {
                          await Navigator.push(
                            context,
                            MaterialPageRoute(
                              builder: (_) =>
                                  HuntPhotoManagerScreen(session: session),
                            ),
                          );
                          if (mounted) {
                            setState(_reload);
                          }
                          return;
                        }
                        if (value != 'delete') {
                          return;
                        }

                        final confirmed = await showDialog<bool>(
                          context: context,
                          builder: (ctx) => AlertDialog(
                            title: const Text('Verwyder terugblik?'),
                            content: Text(
                              'Die terugblik van ${dateFormat.format(session.startedAt)}, '
                              'sy private jagfoto’s en gegenereerde video’s sal '
                              'permanent verwyder word. Trofeë bly behoue.',
                            ),
                            actions: [
                              TextButton(
                                onPressed: () => Navigator.pop(ctx, false),
                                child: const Text('Kanselleer'),
                              ),
                              TextButton(
                                onPressed: () => Navigator.pop(ctx, true),
                                child: const Text(
                                  'Verwyder',
                                  style: TextStyle(color: AppTheme.dangerSoft),
                                ),
                              ),
                            ],
                          ),
                        );

                        if (confirmed == true) {
                          try {
                            await _storage.deleteSession(session.id);
                          } catch (e, stack) {
                            AppErrorBarrier.report(
                              'Delete hunt session',
                              e,
                              stack,
                            );
                            if (context.mounted) {
                              ScaffoldMessenger.of(context).showSnackBar(
                                const SnackBar(
                                  content: Text(
                                    'Kon nie terugblik verwyder nie.',
                                  ),
                                ),
                              );
                            }
                          }
                          if (!mounted) {
                            return;
                          }
                          setState(_reload);
                        }
                      },
                      itemBuilder: (_) => const [
                        PopupMenuItem(
                          value: 'photos',
                          child: Text('Laai jagfoto’s op'),
                        ),
                        PopupMenuItem(value: 'delete', child: Text('Verwyder')),
                      ],
                    ),
                    onTap: () async {
                      await Navigator.push(
                        context,
                        MaterialPageRoute(
                          builder: (_) => HikeRecapScreen(
                            keyframes: session.waypoints,
                            pins: session.pins,
                            sessionId: session.id,
                            sessionStartedAt: session.startedAt,
                          ),
                        ),
                      );

                      if (!mounted) {
                        return;
                      }

                      setState(_reload);
                    },
                  ),
                );
              },
            );
          },
        ),
      ],
    );
  }
}
