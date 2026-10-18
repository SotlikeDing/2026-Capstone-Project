class AppErrorMessage {
  const AppErrorMessage._();

  /// Returns a user-facing Afrikaans message while retaining detailed errors
  /// that the mobile app already creates in Afrikaans.
  static String from(Object? error, {required String fallback}) {
    final rawMessage = error?.toString().trim() ?? '';
    if (rawMessage.isEmpty) {
      return fallback;
    }

    final message = rawMessage
        .replaceFirst(
          RegExp(
            r'^(?:Exception|FormatException|HttpException|DioException)\s*:\s*',
            caseSensitive: false,
          ),
          '',
        )
        .trim();
    final normalized = message.toLowerCase();

    if (_isAfrikaansMessage(normalized)) {
      return message;
    }

    if (_containsAny(normalized, const [
      'invalid email or password',
      'invalid username or password',
      'invalid credentials',
      'invalid login',
      'authentication failed',
      'login failed',
      'invalid password',
    ])) {
      return 'Ongeldige e-posadres of wagwoord.';
    }

    if (_containsAny(normalized, const [
      'network error',
      'failed host lookup',
      'socketexception',
      'connection refused',
      'connection reset',
      'connection timed out',
      'timed out',
      'timeout',
    ])) {
      return 'Kon nie met die bediener verbind nie. Probeer weer.';
    }

    if (_containsAny(normalized, const ['unauthorized', 'not authorized'])) {
      return 'Jou aanmeldbesonderhede is ongeldig.';
    }

    if (_containsAny(normalized, const ['forbidden', 'access denied'])) {
      return 'Jy het nie toestemming om hierdie aksie uit te voer nie.';
    }

    if (_containsAny(normalized, const ['not found', '404'])) {
      return 'Die versoekte inligting kon nie gevind word nie.';
    }

    if (_containsAny(normalized, const ['already exists', 'duplicate'])) {
      return 'Hierdie inligting bestaan reeds.';
    }

    if (_containsAny(normalized, const [
      'validation failed',
      'validation error',
      'is required',
      'required field',
    ])) {
      return 'Kontroleer die ingevoerde inligting en probeer weer.';
    }

    if (_containsAny(normalized, const [
      'internal server error',
      'server error',
      'status code 5',
    ])) {
      return 'Die bediener kon nie jou versoek verwerk nie. Probeer weer.';
    }

    return fallback;
  }

  static bool _containsAny(String value, List<String> phrases) {
    return phrases.any(value.contains);
  }

  static bool _isAfrikaansMessage(String value) {
    return _containsAny(value, const [
      'kon nie',
      'ongeldig',
      'ontbreek',
      'toestemming',
      'ligging',
      'weerdata',
      'bespreking',
      'permit',
      'trofee',
      'ranglys',
      'klets',
      'boodskap',
      'rekening',
      'wagwoord',
      'e-pos',
      'verwyder',
      'bywerk',
      'bygevoeg',
      'versoek',
      'bediener',
      'probeer weer',
    ]);
  }
}
