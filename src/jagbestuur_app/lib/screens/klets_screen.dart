import 'package:flutter/material.dart';

import '../models/chat_models.dart';
import '../services/chat_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../widgets/new_chat_dialog.dart';
import 'chat_screen.dart';

class KletsScreen extends StatefulWidget {
  const KletsScreen({
    super.key,
    required this.currentUserId,
    this.activation = 0,
  });

  final int currentUserId;

  /// Changes each time the tab is reopened; triggers a reload.
  final int activation;

  @override
  State<KletsScreen> createState() => _KletsScreenState();
}

class _KletsScreenState extends State<KletsScreen> {
  final ChatService _chatService = ChatService();

  late int _activeUserId;
  late Future<List<ChatSummary>> _chatsFuture;

  @override
  void initState() {
    super.initState();
    _activeUserId = widget.currentUserId;
    _chatsFuture = _chatService.getUserChats(_activeUserId);
  }

  @override
  void didUpdateWidget(covariant KletsScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.activation != widget.activation) {
      _reloadChats();
    }
  }

  void _reloadChats() {
    if (!mounted) return;
    setState(() {
      _chatsFuture = _chatService.getUserChats(_activeUserId);
    });
  }

  /// Pull-to-refresh. Errors are shown by the FutureBuilder, not rethrown.
  Future<void> _refreshChats() async {
    _reloadChats();
    try {
      await _chatsFuture;
    } catch (_) {}
  }

  Future<void> _startNewChat() async {
    await startNewChat(context, currentUserId: _activeUserId);
    _reloadChats();
  }

  Future<void> _openChat(ChatSummary chat) async {
    await Navigator.push(
      context,
      MaterialPageRoute(
        builder: (_) => ChatScreen(
          chatId: chat.id,
          title: chat.displayTitle,
          currentUserId: _activeUserId,
        ),
      ),
    );
    _reloadChats();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: SafeArea(
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 16, 16, 12),
                child: Row(
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Text(
                            'Klets',
                            style: TextStyle(
                              color: AppTheme.ink,
                              fontSize: 30,
                              fontWeight: FontWeight.w900,
                              letterSpacing: -0.4,
                            ),
                          ),
                          const SizedBox(height: 4),
                          const Text(
                            'Jou gesprekke',
                            style: TextStyle(
                              color: AppTheme.muted,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ],
                      ),
                    ),
                    _InboxActionButton(
                      icon: Icons.refresh_rounded,
                      tooltip: 'Herlaai',
                      onPressed: _refreshChats,
                    ),
                    const SizedBox(width: 8),
                    _InboxActionButton(
                      icon: Icons.add_comment_rounded,
                      tooltip: 'Nuwe klets',
                      onPressed: _startNewChat,
                      highlighted: true,
                    ),
                  ],
                ),
              ),
              Expanded(
                child: FutureBuilder<List<ChatSummary>>(
                  future: _chatsFuture,
                  builder: (context, snapshot) {
                    if (snapshot.connectionState == ConnectionState.waiting) {
                      return const _InboxState(
                        icon: Icons.forum_rounded,
                        title: 'Gesprekke word gelaai',
                        message:
                            'Net \'n oomblik terwyl jou boodskaplys oopmaak.',
                        showLoader: true,
                      );
                    }

                    if (snapshot.hasError) {
                      return _InboxState(
                        icon: Icons.cloud_off_rounded,
                        title: 'Kon nie kletse laai nie',
                        message: AppErrorMessage.from(
                          snapshot.error,
                          fallback: 'Kon nie kletse laai nie.',
                        ),
                        accentColor: AppTheme.dangerSoft,
                      );
                    }

                    final chats = snapshot.data ?? const <ChatSummary>[];

                    if (chats.isEmpty) {
                      return RefreshIndicator(
                        onRefresh: _refreshChats,
                        child: ListView(
                          physics: const AlwaysScrollableScrollPhysics(),
                          padding: const EdgeInsets.fromLTRB(16, 28, 16, 140),
                          children: const [
                            _InboxState(
                              icon: Icons.mark_chat_unread_outlined,
                              title: 'Geen gesprekke nie',
                              message:
                                  'Begin \'n nuwe gesprek met die plus-knoppie.',
                            ),
                          ],
                        ),
                      );
                    }

                    return RefreshIndicator(
                      onRefresh: _refreshChats,
                      child: ListView.separated(
                        padding: const EdgeInsets.fromLTRB(16, 4, 16, 140),
                        itemCount: chats.length + 1,
                        separatorBuilder: (context, index) => index == 0
                            ? const SizedBox(height: 4)
                            : const Divider(
                                height: 1,
                                indent: 66,
                                endIndent: 8,
                              ),
                        itemBuilder: (context, index) {
                          if (index == 0) {
                            return _ConversationListHeader(count: chats.length);
                          }

                          final chat = chats[index - 1];

                          return _ChatSummaryRow(
                            chat: chat,
                            onTap: () => _openChat(chat),
                          );
                        },
                      ),
                    );
                  },
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _InboxActionButton extends StatelessWidget {
  const _InboxActionButton({
    required this.icon,
    required this.tooltip,
    required this.onPressed,
    this.highlighted = false,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback onPressed;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    return Tooltip(
      message: tooltip,
      child: Material(
        color: highlighted ? AppTheme.olive : AppTheme.surface,
        shape: const CircleBorder(),
        child: InkWell(
          onTap: onPressed,
          customBorder: const CircleBorder(),
          child: Container(
            width: 44,
            height: 44,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              border: Border.all(
                color: highlighted ? AppTheme.olive : AppTheme.border,
              ),
            ),
            child: Icon(
              icon,
              color: highlighted ? AppTheme.cream : AppTheme.goldSoft,
              size: 21,
            ),
          ),
        ),
      ),
    );
  }
}

class _ConversationListHeader extends StatelessWidget {
  const _ConversationListHeader({required this.count});

  final int count;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 8, 4, 10),
      child: Row(
        children: [
          const Text(
            'Gesprekke',
            style: TextStyle(
              color: AppTheme.ink,
              fontSize: 17,
              fontWeight: FontWeight.w900,
            ),
          ),
          const Spacer(),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
            decoration: BoxDecoration(
              color: AppTheme.card,
              borderRadius: BorderRadius.circular(AppTheme.radiusSmall),
              border: Border.all(color: AppTheme.border),
            ),
            child: Text(
              '$count',
              style: const TextStyle(
                color: AppTheme.goldSoft,
                fontSize: 11,
                fontWeight: FontWeight.w900,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ChatSummaryRow extends StatelessWidget {
  const _ChatSummaryRow({required this.chat, required this.onTap});

  final ChatSummary chat;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.transparent,
      borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(AppTheme.radiusMedium),
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 13),
          child: Row(
            children: [
              Container(
                width: 48,
                height: 48,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: AppTheme.olive.withValues(alpha: 0.2),
                  shape: BoxShape.circle,
                  border: Border.all(
                    color: AppTheme.olive.withValues(alpha: 0.48),
                  ),
                ),
                child: Text(
                  _chatInitial(chat.displayTitle),
                  style: const TextStyle(
                    color: AppTheme.goldSoft,
                    fontSize: 18,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            chat.displayTitle,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              color: AppTheme.ink,
                              fontSize: 15.5,
                              fontWeight: FontWeight.w900,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Text(
                          _formatChatDate(chat.createdTimestamp),
                          style: const TextStyle(
                            color: AppTheme.muted,
                            fontSize: 11,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Row(
                      children: [
                        const Icon(
                          Icons.group_outlined,
                          color: AppTheme.muted,
                          size: 14,
                        ),
                        const SizedBox(width: 5),
                        Expanded(
                          child: Text(
                            '${chat.participantCount} deelnemers',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              color: AppTheme.muted,
                              fontSize: 12,
                              height: 1.25,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 6),
              const Icon(
                Icons.chevron_right_rounded,
                color: AppTheme.muted,
                size: 20,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _InboxState extends StatelessWidget {
  const _InboxState({
    required this.icon,
    required this.title,
    required this.message,
    this.showLoader = false,
    this.accentColor = AppTheme.goldSoft,
  });

  final IconData icon;
  final String title;
  final String message;
  final bool showLoader;
  final Color accentColor;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 62,
              height: 62,
              decoration: BoxDecoration(
                color: accentColor.withValues(alpha: 0.12),
                shape: BoxShape.circle,
                border: Border.all(color: accentColor.withValues(alpha: 0.28)),
              ),
              child: Icon(icon, color: accentColor, size: 29),
            ),
            const SizedBox(height: 16),
            Text(
              title,
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: AppTheme.ink,
                fontSize: 20,
                fontWeight: FontWeight.w900,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              message,
              textAlign: TextAlign.center,
              style: const TextStyle(color: AppTheme.muted, height: 1.45),
            ),
            if (showLoader) ...[
              const SizedBox(height: 18),
              const SizedBox(
                width: 24,
                height: 24,
                child: CircularProgressIndicator(strokeWidth: 2.4),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

String _chatInitial(String title) {
  final trimmed = title.trim();
  if (trimmed.isEmpty) return '#';
  return trimmed[0].toUpperCase();
}

String _formatChatDate(DateTime? value) {
  if (value == null) return 'Nou';

  final localValue = value.toLocal();
  final now = DateTime.now();
  final isToday =
      localValue.year == now.year &&
      localValue.month == now.month &&
      localValue.day == now.day;

  if (isToday) {
    return '${localValue.hour.toString().padLeft(2, '0')}:${localValue.minute.toString().padLeft(2, '0')}';
  }

  return '${localValue.day.toString().padLeft(2, '0')}/${localValue.month.toString().padLeft(2, '0')}';
}
