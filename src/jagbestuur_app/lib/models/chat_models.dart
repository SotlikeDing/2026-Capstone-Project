class ChatSummary {
  final int id;
  final String? title;
  final DateTime? createdTimestamp;
  final List<int> participantUserIds;

  const ChatSummary({
    required this.id,
    required this.title,
    required this.createdTimestamp,
    required this.participantUserIds,
  });

  int get participantCount => participantUserIds.length;

  String get displayTitle => (title != null && title!.trim().isNotEmpty)
      ? title!.trim()
      : 'Klets #$id';

  factory ChatSummary.fromJson(Map<String, dynamic> json) {
    final participants = json['participants'];
    final participantUserIds = participants is List
        ? participants
              .whereType<Map>()
              .where((participant) {
                return participant['leftAtTimestamp'] == null &&
                    participant['LeftAtTimestamp'] == null;
              })
              .map(
                (participant) =>
                    _toInt(participant['userId'] ?? participant['UserId']),
              )
              .where((userId) => userId > 0)
              .toSet()
              .toList(growable: false)
        : const <int>[];

    return ChatSummary(
      id: _toInt(json['id']),
      title: json['title'] as String?,
      createdTimestamp: _toDateTime(json['createdTimestamp']),
      participantUserIds: participantUserIds,
    );
  }

  bool isDirectChatBetween(int firstUserId, int secondUserId) {
    final participants = participantUserIds.toSet();
    return participants.length == 2 &&
        participants.contains(firstUserId) &&
        participants.contains(secondUserId);
  }
}

/// Another user a chat can be started with.
class ChatContact {
  const ChatContact({required this.id, required this.displayName});

  final int id;
  final String displayName;
}

class ChatMessage {
  final int id;
  final int chatId;
  final int senderId;
  final String senderName;
  final String content;
  final DateTime? sentAt;

  const ChatMessage({
    required this.id,
    required this.chatId,
    required this.senderId,
    required this.senderName,
    required this.content,
    required this.sentAt,
  });

  factory ChatMessage.fromJson(Map<String, dynamic> json) {
    return ChatMessage(
      id: _toInt(json['id']),
      chatId: _toInt(json['chatId']),
      senderId: _toInt(json['senderId']),
      senderName: (json['senderName'] as String?) ?? 'Onbekend',
      content: (json['content'] as String?) ?? '',
      sentAt: _toDateTime(json['sentAt']),
    );
  }
}

int _toInt(dynamic value) {
  if (value is int) return value;
  return int.tryParse(value.toString()) ?? 0;
}

DateTime? _toDateTime(dynamic value) {
  if (value == null) return null;

  final parsed = DateTime.tryParse(value.toString());
  if (parsed == null || parsed.isUtc) return parsed;

  return DateTime.utc(
    parsed.year,
    parsed.month,
    parsed.day,
    parsed.hour,
    parsed.minute,
    parsed.second,
    parsed.millisecond,
    parsed.microsecond,
  );
}
