import 'dart:convert';

import 'package:flutter/foundation.dart';

import '../models/weather_info.dart';
import '../safety/payload_guard.dart';

class WeatherService {
  Future<WeatherInfo> getWeatherForLocation({
    required double latitude,
    required double longitude,
  }) async {
    final uri = Uri.parse(
      'https://api.open-meteo.com/v1/forecast'
      '?latitude=$latitude'
      '&longitude=$longitude'
      '&current=temperature_2m,wind_speed_10m,weather_code'
      '&daily=precipitation_probability_max'
      '&timezone=auto',
    );

    final GuardedHttpResponse response;
    try {
      response = await PayloadGuard.httpGet(uri);
    } catch (e) {
      throw Exception('Kon nie weerdata kry nie. Kontroleer jou verbinding.');
    }

    if (response.statusCode != 200) {
      throw Exception('Kon nie weerdata kry nie.');
    }

    try {
      final data = jsonDecode(response.body) as Map<String, dynamic>;
      final current = data['current'] as Map<String, dynamic>;
      final daily = data['daily'];

      final temperature = (current['temperature_2m'] as num).toDouble();
      final windSpeed = (current['wind_speed_10m'] as num).toDouble();
      final weatherCode = (current['weather_code'] as num).toInt();

      var rainChance = 0;
      if (daily is Map) {
        final probabilities = daily['precipitation_probability_max'];
        if (probabilities is List &&
            probabilities.isNotEmpty &&
            probabilities.first is num) {
          rainChance = (probabilities.first as num).round();
        }
      }

      return WeatherInfo(
        areaName: 'Jou huidige ligging',
        latitude: latitude,
        longitude: longitude,
        temperature: temperature,
        windSpeed: windSpeed,
        condition: _weatherCodeToText(weatherCode),
        rainChance: rainChance,
      );
    } on Object catch (e) {
      // FormatException from jsonDecode, TypeError from a missing field.
      debugPrint('[WeatherService] Unexpected response: $e');
      throw Exception('Die weerdiens het ’n onverwagte antwoord gestuur.');
    }
  }

  String _weatherCodeToText(int code) {
    switch (code) {
      case 0:
        return 'Helder lug';
      case 1:
      case 2:
      case 3:
        return 'Gedeeltelik bewolk';
      case 45:
      case 48:
        return 'Mis';
      case 51:
      case 53:
      case 55:
        return 'Ligte motreën';
      case 56:
      case 57:
        return 'Ysige motreën';
      case 61:
      case 63:
      case 65:
        return 'Reën';
      case 66:
      case 67:
        return 'Ysige reën';
      case 71:
      case 73:
      case 75:
      case 77:
        return 'Sneeu';
      case 80:
      case 81:
      case 82:
        return 'Reënbuie';
      case 85:
      case 86:
        return 'Sneeubuie';
      case 95:
        return 'Donderstorm';
      case 96:
      case 99:
        return 'Donderstorm met hael';
      default:
        return 'Onbekende weerstoestand';
    }
  }
}
