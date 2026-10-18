import 'package:dio/dio.dart';

import '../models/booking.dart';
import '../models/booking_hunter.dart';
import 'api_client.dart';
import 'api_error.dart';

class BookingService {
  BookingService({Dio? client}) : _client = client ?? ApiClient.instance.dio;

  final Dio _client;

  /// Bookings assigned to the explicitly authenticated Hunter, PH or VG role.
  Future<List<Booking>> getBookings({int? userRoleId}) {
    return guardApi(() async {
      final response = await _client.get('/api/bookings');
      return parseEach(
        requireList(response.data),
        Booking.fromJson,
        label: 'booking',
      );
    }, fallback: 'Kon nie besprekings laai nie.');
  }

  Future<BookingHunters> getHunters(int bookingId) {
    return guardApi(() async {
      final response = await _client.get('/api/bookings/$bookingId/hunters');
      return BookingHunters.fromJson(requireMap(response.data));
    }, fallback: 'Kon nie die jagters laai nie.');
  }

  /// Adds a hunter by name + email (main hunter, confirmed booking). Returns the
  /// server's message ("added" or "invite sent"); throws with its reason on refusal.
  Future<String> addHunter(
    int bookingId, {
    required String name,
    required String email,
  }) {
    return guardApi(() async {
      final response = await _client.post(
        '/api/bookings/$bookingId/hunters',
        data: {'name': name, 'email': email},
      );
      return readApiMessage(response.data) ?? 'Jagter bygevoeg.';
    }, fallback: 'Kon nie die jagter byvoeg nie.');
  }

  /// Removes an added hunter (main hunter) or leaves the trip (the hunter themselves).
  Future<void> removeHunter(int bookingId, int companionId) {
    return guardApi(() async {
      await _client.delete('/api/bookings/$bookingId/hunters/$companionId');
    }, fallback: 'Kon nie die jagter verwyder nie.');
  }
}
