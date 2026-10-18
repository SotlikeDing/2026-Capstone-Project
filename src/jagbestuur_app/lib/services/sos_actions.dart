import 'package:flutter/foundation.dart';
import 'package:geolocator/geolocator.dart';
import 'package:url_launcher/url_launcher.dart';

import 'location_service.dart';

/// Emergency actions behind the SOS button.
///
/// Everything goes through the phone's own dialer and SMS app: both work on a
/// plain cellular signal in the veld, where mobile data often doesn't.
class SosActions {
  const SosActions._();

  /// South Africa's emergency number from any cellphone (routes to police,
  /// ambulance or fire). Works without airtime and on most networks without a
  /// SIM.
  static const String emergencyNumber = '112';

  /// Opens the dialer with [emergencyNumber] filled in. Android does not let
  /// ordinary apps place the call without the user pressing call.
  static Future<void> callEmergencyServices() async {
    final opened = await launchUrl(Uri(scheme: 'tel', path: emergencyNumber));
    if (!opened) {
      throw Exception(
        'Kon nie die foon-app oopmaak nie. Bel $emergencyNumber self.',
      );
    }
  }

  /// Opens an SMS containing the current GPS position and a map link.
  static Future<void> shareLocation() async {
    final position = await _bestEffortPosition();
    await _openSms('My ligging: ${_describe(position)}');
  }

  /// Opens an SMS asking for help, with the current GPS position.
  static Future<void> sendEmergencyMessage() async {
    final position = await _bestEffortPosition();
    await _openSms(
      'NOOD: Ek het dringend hulp nodig tydens ’n jag. '
      'My ligging: ${_describe(position)}',
    );
  }

  static Future<Position> _bestEffortPosition() {
    return LocationService.currentPosition(
      timeout: const Duration(seconds: 15),
      allowLastKnown: true,
    );
  }

  static String _describe(Position position) {
    final lat = position.latitude.toStringAsFixed(6);
    final lng = position.longitude.toStringAsFixed(6);
    final accuracy = position.accuracy.round();
    return '$lat, $lng (±$accuracy m) '
        'https://maps.google.com/?q=$lat,$lng';
  }

  static Future<void> _openSms(String body) async {
    // Built by hand: Uri(queryParameters:) encodes spaces as '+', which many
    // SMS apps show literally.
    final uri = Uri.parse('sms:?body=${Uri.encodeComponent(body)}');
    final opened = await launchUrl(uri);
    if (!opened) {
      debugPrint('[SosActions] No SMS app handled $uri');
      throw Exception('Kon nie die SMS-app oopmaak nie.');
    }
  }
}
