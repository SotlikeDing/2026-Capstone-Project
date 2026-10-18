import 'package:flutter/material.dart';

import '../models/chat_models.dart';
import '../screens/chat_screen.dart';
import '../services/chat_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';

/// What the user filled in on [NewChatDialog].
class NewChatDraft {
  const NewChatDraft({
    required this.title,
    required this.message,
    this.recipientEmail,
  });

  final String title;
  final String message;
  final String? recipientEmail;
}

/// Asks for a chat title, first message and (unless [recipient] is fixed) the
/// other person's e-mail address.
///
/// The dialog owns its text controllers and disposes them in its own
/// [State.dispose], after the closing animation has finished.
class NewChatDialog extends StatefulWidget {
  const NewChatDialog({super.key, this.recipient, this.initialTitle = ''});

  /// Pre-selected recipient (e.g. a booking's farm owner). When null the user
  /// types an e-mail address.
  final ChatContact? recipient;
  final String initialTitle;

  static Future<NewChatDraft?> show(
    BuildContext context, {
    ChatContact? recipient,
    String initialTitle = '',
  }) {
    return showDialog<NewChatDraft>(
      context: context,
      builder: (_) =>
          NewChatDialog(recipient: recipient, initialTitle: initialTitle),
    );
  }

  @override
  State<NewChatDialog> createState() => _NewChatDialogState();
}

class _NewChatDialogState extends State<NewChatDialog> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _emailController = TextEditingController();
  late final TextEditingController _titleController = TextEditingController(
    text: widget.initialTitle,
  );
  late final TextEditingController _messageController = TextEditingController();

  @override
  void dispose() {
    _emailController.dispose();
    _titleController.dispose();
    _messageController.dispose();
    super.dispose();
  }

  void _submit() {
    if (!_formKey.currentState!.validate()) return;

    final title = _titleController.text.trim();
    Navigator.of(context).pop(
      NewChatDraft(
        title: title.isEmpty ? 'Nuwe klets' : title,
        message: _messageController.text.trim(),
        recipientEmail: widget.recipient == null
            ? _emailController.text.trim()
            : null,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final recipient = widget.recipient;

    return AlertDialog(
      backgroundColor: AppTheme.card,
      insetPadding: const EdgeInsets.symmetric(horizontal: 18, vertical: 24),
      title: Text(
        recipient == null ? 'Nuwe gesprek' : 'Kontak ${recipient.displayName}',
        style: Theme.of(context).textTheme.titleLarge?.copyWith(
          color: AppTheme.ink,
          fontWeight: FontWeight.w900,
        ),
      ),
      content: Form(
        key: _formKey,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (recipient == null) ...[
                TextFormField(
                  controller: _emailController,
                  keyboardType: TextInputType.emailAddress,
                  autofillHints: const [AutofillHints.email],
                  decoration: const InputDecoration(
                    labelText: 'Ontvanger se e-posadres',
                    prefixIcon: Icon(Icons.alternate_email_rounded),
                  ),
                  validator: (value) {
                    final email = value?.trim() ?? '';
                    if (email.isEmpty) return 'Voer ’n e-posadres in.';
                    if (!email.contains('@') || !email.contains('.')) {
                      return 'Voer ’n geldige e-posadres in.';
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
              ],
              TextFormField(
                controller: _titleController,
                decoration: const InputDecoration(
                  labelText: 'Gesprekstitel',
                  prefixIcon: Icon(Icons.title_rounded),
                ),
              ),
              const SizedBox(height: 12),
              TextFormField(
                controller: _messageController,
                minLines: 2,
                maxLines: 4,
                decoration: const InputDecoration(
                  labelText: 'Eerste boodskap',
                  prefixIcon: Icon(Icons.chat_bubble_outline_rounded),
                ),
                validator: (value) => (value?.trim() ?? '').isEmpty
                    ? 'Skryf ’n eerste boodskap.'
                    : null,
              ),
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Kanselleer'),
        ),
        ElevatedButton(onPressed: _submit, child: const Text('Skep')),
      ],
    );
  }
}

/// Shows [NewChatDialog], resolves the recipient, creates the chat and opens
/// it. Returns true when a chat was created. Errors are shown as SnackBars.
Future<bool> startNewChat(
  BuildContext context, {
  required int currentUserId,
  ChatContact? recipient,
  String initialTitle = '',
  ChatService? chatService,
}) async {
  final draft = await NewChatDialog.show(
    context,
    recipient: recipient,
    initialTitle: initialTitle,
  );
  if (draft == null || !context.mounted) return false;

  final service = chatService ?? ChatService();
  final messenger = ScaffoldMessenger.of(context);

  try {
    final contact =
        recipient ?? await service.findUserByEmail(draft.recipientEmail!);

    if (contact.id == currentUserId) {
      messenger.showSnackBar(
        const SnackBar(
          content: Text('Jy kan nie ’n klets met jouself begin nie.'),
        ),
      );
      return false;
    }

    final chatId = await service.createChat(
      senderId: currentUserId,
      participantIds: [contact.id],
      title: draft.title,
      initialMessage: draft.message,
    );

    if (!context.mounted) return true;

    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => ChatScreen(
          chatId: chatId,
          title: draft.title,
          currentUserId: currentUserId,
        ),
      ),
    );
    return true;
  } catch (e) {
    messenger.showSnackBar(
      SnackBar(
        content: Text(
          AppErrorMessage.from(e, fallback: 'Kon nie nuwe klets skep nie.'),
        ),
      ),
    );
    return false;
  }
}
