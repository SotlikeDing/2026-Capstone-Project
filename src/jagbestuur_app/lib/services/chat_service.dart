import 'package:dio/dio.dart';

import '../models/chat_models.dart';
import '../utils/json_parsing.dart';
import 'api_client.dart';
import 'api_error.dart';

class ChatService {
  ChatService({Dio? client}) : _client = client ?? ApiClient.instance.dio;

  final Dio _client;

  /// Messages fetched per page. The API returns newest first.
  static const int messagePageSize = 50;

  Future<List<ChatSummary>> getUserChats(int userId) {
    return guardApi(() async {
      final response = await _client.get('/api/chats/user/$userId/chats');
      return parseEach(
        requireList(response.data),
        ChatSummary.fromJson,
        label: 'chat',
      );
    }, fallback: 'Kon nie kletse laai nie.');
  }

  /// Returns the newest active one-to-one chat between the two users, if it
  /// exists. [getUserChats] is ordered newest first by the API.
  Future<ChatSummary?> findDirectChat({
    required int currentUserId,
    required int otherUserId,
  }) async {
    final chats = await getUserChats(currentUserId);
    for (final chat in chats) {
      if (chat.isDirectChatBetween(currentUserId, otherUserId)) {
        return chat;
      }
    }
    return null;
  }

  /// One page of messages, newest first. Page 1 is the most recent.
  Future<List<ChatMessage>> getChatMessages(int chatId, {int page = 1}) {
    return guardApi(() async {
      final response = await _client.get(
        '/api/chats/$chatId/messages',
        queryParameters: {'page': page, 'pageSize': messagePageSize},
      );
      return parseEach(
        requireList(response.data),
        ChatMessage.fromJson,
        label: 'message',
      );
    }, fallback: 'Kon nie boodskappe laai nie.');
  }

  /// Looks up another user by e-mail so a chat can be started with them.
  Future<ChatContact> findUserByEmail(String email) {
    return guardApi(() async {
      final Response<dynamic> response;
      try {
        response = await _client.get(
          '/api/users/by-email',
          queryParameters: {'email': email.trim()},
        );
      } on DioException catch (e) {
        if (e.response?.statusCode == 404) {
          throw Exception('Geen gebruiker met daardie e-posadres nie.');
        }
        rethrow;
      }

      final json = requireMap(response.data);
      final id = tryParseInt(json['id']);
      if (id == null || id <= 0) {
        throw const FormatException('id ontbreek.');
      }
      final name = '${json['name'] ?? ''} ${json['surname'] ?? ''}'.trim();
      return ChatContact(id: id, displayName: name.isEmpty ? email : name);
    }, fallback: 'Kon nie die gebruiker vind nie.');
  }

  Future<int> createChat({
    required int senderId,
    required List<int> participantIds,
    required String title,
    required String initialMessage,
  }) {
    final now = DateTime.now().toUtc().toIso8601String();

    final participants = {
      ...participantIds,
      senderId,
    }.map((userId) => {'userId': userId, 'joinedTimestamp': now}).toList();

    return guardApi(() async {
      final response = await _client.post(
        '/api/chats/createChat',
        data: {
          'createdAt': now,
          'title': title,
          'chatParticipants': participants,
          'senderId': senderId,
          'initialMessage': initialMessage,
        },
      );

      final data = requireMap(response.data);
      return tryParseInt(data['chatId'] ?? data['id']) ??
          (throw const FormatException('chatId ontbreek.'));
    }, fallback: 'Kon nie klets skep nie.');
  }

  Future<void> sendMessage({
    required int chatId,
    required int senderId,
    required String content,
  }) {
    return guardApi(() async {
      await _client.post(
        '/api/chats/$chatId/messages',
        data: {'senderId': senderId, 'content': content},
      );
    }, fallback: 'Kon nie boodskap stuur nie.');
  }
}
