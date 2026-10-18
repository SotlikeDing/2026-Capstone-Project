import 'dart:async';

import 'package:flutter/material.dart';

import '../models/chat_models.dart';
import '../services/chat_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';

class ChatScreen extends StatefulWidget {
  const ChatScreen({
    super.key,
    required this.chatId,
    required this.currentUserId,
    this.title = 'Klets',
  });

  final int? chatId;
  final String title;
  final int currentUserId;

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends State<ChatScreen> {
  final ChatService _chatService = ChatService();
  final TextEditingController _messageController = TextEditingController();

  /// Newest first, as the API returns them; the list is drawn reversed so
  /// the newest message sits at the bottom next to the input.
  List<ChatMessage> _messages = <ChatMessage>[];
  bool _isLoading = true;
  bool _isSending = false;
  bool _isLoadingOlder = false;
  bool _hasOlder = false;
  int _loadedPages = 0;
  Timer? _pollTimer;

  static const Duration _pollInterval = Duration(seconds: 10);

  @override
  void initState() {
    super.initState();
    _loadMessages();
    if (widget.chatId != null) {
      _pollTimer = Timer.periodic(_pollInterval, (_) => _pollNewMessages());
    }
  }

  @override
  void dispose() {
    _pollTimer?.cancel();
    _messageController.dispose();
    super.dispose();
  }

  /// Merges [incoming] into the loaded messages, newest first, without
  /// duplicates.
  List<ChatMessage> _merge(List<ChatMessage> incoming) {
    final byId = <int, ChatMessage>{
      for (final message in _messages) message.id: message,
      for (final message in incoming) message.id: message,
    };
    final merged = byId.values.toList()
      ..sort((a, b) {
        final aTime = a.sentAt;
        final bTime = b.sentAt;
        if (aTime != null && bTime != null && aTime != bTime) {
          return bTime.compareTo(aTime);
        }
        return b.id.compareTo(a.id);
      });
    return merged;
  }

  Future<void> _loadMessages() async {
    final chatId = widget.chatId;
    if (chatId == null) {
      setState(() {
        _isLoading = false;
      });
      return;
    }

    setState(() {
      _isLoading = _messages.isEmpty;
    });

    try {
      final messages = await _chatService.getChatMessages(chatId);
      if (!mounted) return;

      setState(() {
        _messages = _merge(messages);
        _loadedPages = _loadedPages == 0 ? 1 : _loadedPages;
        _hasOlder = messages.length >= ChatService.messagePageSize;
        _isLoading = false;
      });
    } catch (e) {
      if (!mounted) return;

      setState(() {
        _isLoading = false;
      });

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            AppErrorMessage.from(e, fallback: 'Kon nie boodskappe laai nie.'),
          ),
        ),
      );
    }
  }

  /// Background refresh; failures stay silent so a weak signal doesn't spam
  /// the user with SnackBars every few seconds.
  Future<void> _pollNewMessages() async {
    final chatId = widget.chatId;
    if (chatId == null || _isLoading || _isSending) return;

    try {
      final latest = await _chatService.getChatMessages(chatId);
      if (!mounted) return;
      final merged = _merge(latest);
      if (merged.length != _messages.length ||
          (merged.isNotEmpty && merged.first.id != _messages.first.id)) {
        setState(() => _messages = merged);
      }
    } catch (_) {}
  }

  Future<void> _loadOlderMessages() async {
    final chatId = widget.chatId;
    if (chatId == null || _isLoadingOlder || !_hasOlder) return;

    setState(() => _isLoadingOlder = true);
    try {
      final nextPage = _loadedPages + 1;
      final older = await _chatService.getChatMessages(chatId, page: nextPage);
      if (!mounted) return;
      setState(() {
        _messages = _merge(older);
        _loadedPages = nextPage;
        _hasOlder = older.length >= ChatService.messagePageSize;
      });
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            AppErrorMessage.from(
              e,
              fallback: 'Kon nie ouer boodskappe laai nie.',
            ),
          ),
        ),
      );
    } finally {
      if (mounted) setState(() => _isLoadingOlder = false);
    }
  }

  Future<void> _sendMessage() async {
    final chatId = widget.chatId;
    final text = _messageController.text.trim();

    if (chatId == null || text.isEmpty || _isSending) {
      return;
    }

    setState(() {
      _isSending = true;
    });

    try {
      await _chatService.sendMessage(
        chatId: chatId,
        senderId: widget.currentUserId,
        content: text,
      );

      _messageController.clear();
      await _loadMessages();
    } catch (e) {
      if (!mounted) return;

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            AppErrorMessage.from(e, fallback: 'Kon nie boodskap stuur nie.'),
          ),
        ),
      );
    } finally {
      if (mounted) {
        setState(() {
          _isSending = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    if (widget.chatId == null) {
      return Scaffold(
        backgroundColor: Colors.transparent,
        appBar: AppBar(title: Text(widget.title)),
        body: Container(
          decoration: AppTheme.pageDecoration,
          child: const _ConversationState(
            icon: Icons.chat_bubble_outline_rounded,
            title: 'Geen gesprek beskikbaar nie',
            message: 'Hierdie gesprek kan nie nou oopgemaak word nie.',
          ),
        ),
      );
    }

    return Scaffold(
      backgroundColor: Colors.transparent,
      appBar: AppBar(
        toolbarHeight: 66,
        titleSpacing: 0,
        title: Row(
          children: [
            Container(
              width: 40,
              height: 40,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: AppTheme.olive.withValues(alpha: 0.22),
                shape: BoxShape.circle,
                border: Border.all(
                  color: AppTheme.olive.withValues(alpha: 0.5),
                ),
              ),
              child: Text(
                _conversationInitial(widget.title),
                style: const TextStyle(
                  color: AppTheme.goldSoft,
                  fontWeight: FontWeight.w900,
                ),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    widget.title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const Text(
                    'Werk elke 10 sekondes op',
                    style: TextStyle(
                      color: AppTheme.muted,
                      fontSize: 12,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
        actions: [
          IconButton(
            onPressed: _loadMessages,
            icon: const Icon(Icons.refresh_rounded),
            tooltip: 'Herlaai',
          ),
          const SizedBox(width: 6),
        ],
        bottom: const PreferredSize(
          preferredSize: Size.fromHeight(1),
          child: Divider(height: 1),
        ),
      ),
      body: Container(
        decoration: AppTheme.pageDecoration,
        child: Column(
          children: [
            Expanded(
              child: _isLoading
                  ? const _ConversationState(
                      icon: Icons.forum_rounded,
                      title: 'Boodskappe word gelaai',
                      message: 'Net \'n oomblik.',
                      showLoader: true,
                    )
                  : _messages.isEmpty
                  ? const _ConversationState(
                      icon: Icons.chat_bubble_outline_rounded,
                      title: 'Nog geen boodskappe nie',
                      message: 'Stuur die eerste boodskap om te begin.',
                    )
                  : RefreshIndicator(
                      onRefresh: _loadMessages,
                      child: ListView.builder(
                        // Reversed: index 0 (newest) is drawn at the bottom.
                        reverse: true,
                        padding: const EdgeInsets.fromLTRB(16, 14, 16, 18),
                        itemCount: _messages.length + (_hasOlder ? 1 : 0),
                        itemBuilder: (context, index) {
                          if (index == _messages.length) {
                            return Center(
                              child: TextButton.icon(
                                onPressed: _isLoadingOlder
                                    ? null
                                    : _loadOlderMessages,
                                icon: _isLoadingOlder
                                    ? const SizedBox(
                                        width: 16,
                                        height: 16,
                                        child: CircularProgressIndicator(
                                          strokeWidth: 2,
                                        ),
                                      )
                                    : const Icon(Icons.history_rounded),
                                label: const Text('Laai ouer boodskappe'),
                              ),
                            );
                          }

                          final message = _messages[index];
                          final isMine =
                              message.senderId == widget.currentUserId;

                          return _MessageBubble(
                            message: message,
                            isMine: isMine,
                          );
                        },
                      ),
                    ),
            ),
            SafeArea(
              top: false,
              child: Container(
                padding: const EdgeInsets.fromLTRB(12, 10, 12, 12),
                decoration: const BoxDecoration(
                  color: AppTheme.surface,
                  border: Border(top: BorderSide(color: AppTheme.border)),
                ),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Expanded(
                      child: TextField(
                        controller: _messageController,
                        minLines: 1,
                        maxLines: 4,
                        textInputAction: TextInputAction.send,
                        onSubmitted: (_) => _sendMessage(),
                        decoration: InputDecoration(
                          hintText: 'Tik jou boodskap...',
                          filled: true,
                          fillColor: AppTheme.card,
                          contentPadding: const EdgeInsets.symmetric(
                            horizontal: 16,
                            vertical: 13,
                          ),
                          border: OutlineInputBorder(
                            borderRadius: BorderRadius.circular(
                              AppTheme.radiusLarge,
                            ),
                            borderSide: const BorderSide(
                              color: AppTheme.border,
                            ),
                          ),
                          enabledBorder: OutlineInputBorder(
                            borderRadius: BorderRadius.circular(
                              AppTheme.radiusLarge,
                            ),
                            borderSide: const BorderSide(
                              color: AppTheme.border,
                            ),
                          ),
                          focusedBorder: OutlineInputBorder(
                            borderRadius: BorderRadius.circular(
                              AppTheme.radiusLarge,
                            ),
                            borderSide: const BorderSide(
                              color: AppTheme.gold,
                              width: 1.4,
                            ),
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Material(
                      color: _isSending
                          ? AppTheme.disabledSurface
                          : AppTheme.olive,
                      shape: const CircleBorder(),
                      child: InkWell(
                        onTap: _isSending ? null : _sendMessage,
                        customBorder: const CircleBorder(),
                        child: SizedBox(
                          width: 48,
                          height: 48,
                          child: Center(
                            child: _isSending
                                ? const SizedBox(
                                    width: 20,
                                    height: 20,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                      color: AppTheme.goldSoft,
                                    ),
                                  )
                                : const Icon(
                                    Icons.send_rounded,
                                    color: AppTheme.cream,
                                  ),
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _MessageBubble extends StatelessWidget {
  const _MessageBubble({required this.message, required this.isMine});

  final ChatMessage message;
  final bool isMine;

  @override
  Widget build(BuildContext context) {
    final bubbleColor = isMine ? AppTheme.olive : AppTheme.card;
    final textColor = isMine ? AppTheme.cream : AppTheme.ink;
    final metaColor = isMine
        ? AppTheme.cream.withValues(alpha: 0.68)
        : AppTheme.muted;

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.end,
        mainAxisAlignment: isMine
            ? MainAxisAlignment.end
            : MainAxisAlignment.start,
        children: [
          if (!isMine) ...[
            _SenderAvatar(name: message.senderName),
            const SizedBox(width: 8),
          ],
          Flexible(
            child: Container(
              constraints: BoxConstraints(
                maxWidth: MediaQuery.sizeOf(context).width * 0.78,
              ),
              padding: const EdgeInsets.fromLTRB(14, 10, 14, 7),
              decoration: BoxDecoration(
                color: bubbleColor,
                borderRadius: BorderRadius.only(
                  topLeft: const Radius.circular(18),
                  topRight: const Radius.circular(18),
                  bottomLeft: Radius.circular(isMine ? 18 : 5),
                  bottomRight: Radius.circular(isMine ? 5 : 18),
                ),
                border: Border.all(
                  color: isMine
                      ? AppTheme.olive
                      : AppTheme.border.withValues(alpha: 0.9),
                ),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (!isMine) ...[
                    Text(
                      message.senderName,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        color: AppTheme.goldSoft,
                        fontSize: 12,
                        fontWeight: FontWeight.w900,
                      ),
                    ),
                    const SizedBox(height: 4),
                  ],
                  Text(
                    message.content,
                    style: TextStyle(
                      color: textColor,
                      fontSize: 14.5,
                      height: 1.4,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Align(
                    alignment: Alignment.bottomRight,
                    child: Text(
                      _formatMessageTime(message.sentAt),
                      style: TextStyle(
                        color: metaColor,
                        fontSize: 10.5,
                        fontWeight: FontWeight.w700,
                      ),
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

class _SenderAvatar extends StatelessWidget {
  const _SenderAvatar({required this.name});

  final String name;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 30,
      height: 30,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: AppTheme.gold.withValues(alpha: 0.16),
        shape: BoxShape.circle,
        border: Border.all(color: AppTheme.gold.withValues(alpha: 0.36)),
      ),
      child: Text(
        _conversationInitial(name),
        style: const TextStyle(
          color: AppTheme.gold,
          fontSize: 12,
          fontWeight: FontWeight.w900,
        ),
      ),
    );
  }
}

class _ConversationState extends StatelessWidget {
  const _ConversationState({
    required this.icon,
    required this.title,
    required this.message,
    this.showLoader = false,
  });

  final IconData icon;
  final String title;
  final String message;
  final bool showLoader;

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
                color: AppTheme.goldSoft.withValues(alpha: 0.12),
                shape: BoxShape.circle,
                border: Border.all(
                  color: AppTheme.goldSoft.withValues(alpha: 0.28),
                ),
              ),
              child: Icon(icon, color: AppTheme.goldSoft, size: 29),
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

String _conversationInitial(String value) {
  final trimmed = value.trim();
  if (trimmed.isEmpty) return '#';
  return trimmed[0].toUpperCase();
}

String _formatMessageTime(DateTime? value) {
  if (value == null) return 'Nou';

  final local = value.toLocal();
  final now = DateTime.now();
  String two(int n) => n.toString().padLeft(2, '0');
  final time = '${two(local.hour)}:${two(local.minute)}';

  final isToday =
      local.year == now.year &&
      local.month == now.month &&
      local.day == now.day;
  if (isToday) return time;

  final date = local.year == now.year
      ? '${two(local.day)}/${two(local.month)}'
      : '${two(local.day)}/${two(local.month)}/${local.year}';
  return '$date $time';
}
