import 'dart:io';

import 'package:path_provider/path_provider.dart';

import 'auth_service.dart';

/// Resolves on-device folders that belong to the signed-in user.
///
/// Hunt routes and recap videos contain precise GPS data, so they are kept
/// under `users/<userId>/` and are never visible to another account that
/// signs in on the same phone.
class UserStorage {
  const UserStorage._();

  /// The signed-in user's id. Throws if nobody is signed in, so data can never
  /// be written to (or read from) a shared location by accident.
  static int requireUserId() {
    final session = AuthService.instance.currentSession;
    if (session == null || session.userId <= 0) {
      throw StateError('Geen gebruiker is aangemeld nie.');
    }
    return session.userId;
  }

  /// `<documents>/users/<userId>/<name>`, created if needed.
  static Future<Directory> documentsDir(String name, {int? userId}) async {
    final base = await getApplicationDocumentsDirectory();
    return _ensure(base, userId ?? requireUserId(), name);
  }

  /// `<temp>/users/<userId>/<name>`, created if needed.
  static Future<Directory> temporaryDir(String name, {int? userId}) async {
    final base = await getTemporaryDirectory();
    return _ensure(base, userId ?? requireUserId(), name);
  }

  static Future<Directory> _ensure(
    Directory base,
    int userId,
    String name,
  ) async {
    final sep = Platform.pathSeparator;
    final dir = Directory('${base.path}${sep}users$sep$userId$sep$name');
    if (!await dir.exists()) {
      await dir.create(recursive: true);
    }
    return dir;
  }
}
