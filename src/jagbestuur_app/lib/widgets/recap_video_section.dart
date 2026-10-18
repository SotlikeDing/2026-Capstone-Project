import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../safety/app_error_barrier.dart';
import '../screens/hike_recap_screen.dart';
import '../services/video_storage_service.dart';
import '../theme/app_theme.dart';

class RecapVideoSection extends StatefulWidget {
  const RecapVideoSection({super.key});

  @override
  State<RecapVideoSection> createState() => _RecapVideoSectionState();
}

class _RecapVideoSectionState extends State<RecapVideoSection> {
  final VideoStorageService _storage = const VideoStorageService();
  late Future<List<SavedRecapVideo>> _videosFuture;

  @override
  void initState() {
    super.initState();
    _reload();
    VideoStorageService.libraryVersion.addListener(_onVideosChanged);
  }

  @override
  void dispose() {
    VideoStorageService.libraryVersion.removeListener(_onVideosChanged);
    super.dispose();
  }

  void _onVideosChanged() {
    if (!mounted) {
      return;
    }

    setState(_reload);
  }

  void _reload() {
    _videosFuture = _storage.listVideos();
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
                  'Terugblik-video’s',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800),
                ),
              ),
              IconButton(
                onPressed: () => setState(_reload),
                icon: const Icon(Icons.refresh_rounded),
                tooltip: 'Laai video’s weer',
              ),
            ],
          ),
        ),
        FutureBuilder<List<SavedRecapVideo>>(
          future: _videosFuture,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Padding(
                padding: EdgeInsets.all(16),
                child: Center(child: CircularProgressIndicator()),
              );
            }

            final videos = snapshot.data ?? const <SavedRecapVideo>[];

            if (videos.isEmpty) {
              return const Padding(
                padding: EdgeInsets.symmetric(vertical: 16),
                child: Text(
                  'Nog geen terugblik-video’s gestoor nie.',
                  style: TextStyle(color: AppTheme.muted),
                ),
              );
            }

            final dateFormat = DateFormat('d MMM yyyy, HH:mm');

            return ListView.separated(
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              itemCount: videos.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final video = videos[index];

                return Container(
                  decoration: BoxDecoration(
                    color: AppTheme.card,
                    borderRadius: BorderRadius.circular(AppTheme.radiusLarge),
                    border: Border.all(color: AppTheme.border),
                  ),
                  child: ListTile(
                    leading: const CircleAvatar(
                      backgroundColor: AppTheme.gold,
                      child: Icon(Icons.movie_rounded, color: Colors.black),
                    ),
                    title: Text(video.title),
                    subtitle: Text(dateFormat.format(video.createdAt)),
                    trailing: PopupMenuButton<String>(
                      onSelected: (value) async {
                        if (value != 'delete') {
                          return;
                        }

                        try {
                          await _storage.deleteVideo(video.id);
                        } catch (e, stack) {
                          AppErrorBarrier.report(
                            'Delete recap video',
                            e,
                            stack,
                          );
                          if (!context.mounted) {
                            return;
                          }
                          ScaffoldMessenger.of(context).showSnackBar(
                            const SnackBar(
                              content: Text('Kon nie video verwyder nie.'),
                            ),
                          );
                        }

                        if (!mounted) {
                          return;
                        }

                        setState(_reload);
                      },
                      itemBuilder: (_) => const [
                        PopupMenuItem(value: 'delete', child: Text('Verwyder')),
                      ],
                    ),
                    onTap: () {
                      Navigator.push(
                        context,
                        MaterialPageRoute(
                          builder: (_) => RecapVideoPlayerScreen(video: video),
                        ),
                      );
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
