import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';

import '../models/weather_info.dart';
import '../services/location_service.dart';
import '../services/weather_service.dart';
import '../theme/app_theme.dart';
import '../utils/app_error_message.dart';
import '../widgets/app_feedback_panel.dart';
import '../widgets/hunting_card.dart';

class WeatherScreen extends StatefulWidget {
  const WeatherScreen({super.key});

  @override
  State<WeatherScreen> createState() => _WeatherScreenState();
}

class _WeatherScreenState extends State<WeatherScreen> {
  final WeatherService _weatherService = WeatherService();

  Future<WeatherInfo>? _weatherFuture;

  String _statusText = 'Weer word vir jou huidige ligging gelaai...';

  @override
  void initState() {
    super.initState();
    _weatherFuture = _loadWeatherForCurrentLocation();
  }

  void _setStatus(String text) {
    if (!mounted) return;
    setState(() => _statusText = text);
  }

  Future<WeatherInfo> _loadWeatherForCurrentLocation() async {
    // Deferred so no setState runs while initState is still building.
    await Future<void>.delayed(Duration.zero);
    _setStatus('GPS-ligging word gekry...');

    final Position position;
    try {
      position = await LocationService.currentPosition(
        settings: const LocationSettings(accuracy: LocationAccuracy.high),
        allowLastKnown: true,
      );
    } on LocationUnavailableException catch (e) {
      _setStatus(e.message);
      throw Exception(e.message);
    }

    _setStatus('Weer word gelaai vir jou huidige ligging...');

    final weather = await _weatherService.getWeatherForLocation(
      latitude: position.latitude,
      longitude: position.longitude,
    );

    _setStatus(
      'Weer gelaai vir: ${position.latitude.toStringAsFixed(5)}, '
      '${position.longitude.toStringAsFixed(5)}',
    );

    return weather;
  }

  /// Errors are shown by the FutureBuilder, not rethrown to the
  /// RefreshIndicator.
  Future<void> _refreshWeather() async {
    setState(() {
      _weatherFuture = _loadWeatherForCurrentLocation();
    });

    try {
      await _weatherFuture;
    } catch (_) {}
  }

  void _showWeatherApiMessage() {
    showDialog(
      context: context,
      builder: (_) {
        return AlertDialog(
          title: const Text('Weer'),
          content: const Text(
            'Hierdie skerm gebruik die gebruiker se foon GPS-ligging en kry dan weerdata vir daardie koördinate. In die finale stelsel kan dieselfde beginsel deur jou eie weerdiens bestuur word.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Reg'),
            ),
          ],
        );
      },
    );
  }

  void _retryWeather() {
    setState(() {
      _weatherFuture = _loadWeatherForCurrentLocation();
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppTheme.background,
      appBar: AppBar(
        title: const Text('Weer'),
        actions: [
          IconButton(
            onPressed: _showWeatherApiMessage,
            icon: const Icon(Icons.info_outline),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: _refreshWeather,
        child: FutureBuilder<WeatherInfo>(
          future: _weatherFuture,
          builder: (context, snapshot) {
            if (snapshot.connectionState == ConnectionState.waiting) {
              return ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  AppFeedbackPanel(
                    icon: Icons.cloud_sync_outlined,
                    title: 'Weer word gelaai',
                    message: _statusText,
                    isLoading: true,
                  ),
                ],
              );
            }

            if (snapshot.hasError || snapshot.data == null) {
              return ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  AppFeedbackPanel(
                    icon: Icons.cloud_off_rounded,
                    title: 'Kon nie weerdata laai nie',
                    message: AppErrorMessage.from(
                      snapshot.error,
                      fallback: 'Kon nie weerdata laai nie.',
                    ),
                    action: SizedBox(
                      width: double.infinity,
                      child: ElevatedButton.icon(
                        onPressed: _retryWeather,
                        icon: const Icon(Icons.refresh),
                        label: const Text('Probeer weer'),
                      ),
                    ),
                  ),
                ],
              );
            }

            final weather = snapshot.data!;

            return ListView(
              padding: const EdgeInsets.all(16),
              children: [
                HuntingCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Icon(
                        Icons.wb_sunny,
                        color: AppTheme.gold,
                        size: 54,
                      ),
                      const SizedBox(height: 12),
                      Text(
                        weather.areaName,
                        style: const TextStyle(
                          color: AppTheme.muted,
                          fontSize: 14,
                        ),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        '${weather.temperature.toStringAsFixed(1)}°C',
                        style: const TextStyle(
                          color: Colors.white,
                          fontSize: 46,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      Text(
                        weather.condition,
                        style: const TextStyle(
                          color: AppTheme.cream,
                          fontSize: 16,
                        ),
                      ),
                      const SizedBox(height: 14),
                      Text(
                        'GPS: ${weather.latitude.toStringAsFixed(5)}, ${weather.longitude.toStringAsFixed(5)}',
                        style: const TextStyle(
                          color: AppTheme.muted,
                          fontSize: 13,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 14),
                Row(
                  children: [
                    Expanded(
                      child: _WeatherSmallCard(
                        icon: Icons.air,
                        label: 'Wind',
                        value: '${weather.windSpeed.toStringAsFixed(0)} km/h',
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: _WeatherSmallCard(
                        icon: Icons.water_drop,
                        label: 'Reënkans',
                        value: '${weather.rainChance}%',
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                HuntingCard(
                  child: Text(
                    _statusText,
                    style: const TextStyle(color: AppTheme.cream, height: 1.45),
                  ),
                ),
                const SizedBox(height: 14),
                SizedBox(
                  width: double.infinity,
                  child: ElevatedButton.icon(
                    onPressed: _retryWeather,
                    icon: const Icon(Icons.my_location),
                    label: const Text('Herlaai weer vir my huidige ligging'),
                    style: ElevatedButton.styleFrom(
                      backgroundColor: AppTheme.gold,
                      foregroundColor: AppTheme.background,
                      padding: const EdgeInsets.symmetric(vertical: 14),
                    ),
                  ),
                ),
                const SizedBox(height: 14),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _WeatherSmallCard extends StatelessWidget {
  final IconData icon;
  final String label;
  final String value;

  const _WeatherSmallCard({
    required this.icon,
    required this.label,
    required this.value,
  });

  @override
  Widget build(BuildContext context) {
    return HuntingCard(
      child: Column(
        children: [
          Icon(icon, color: AppTheme.gold, size: 30),
          const SizedBox(height: 10),
          Text(
            label,
            style: const TextStyle(color: AppTheme.muted, fontSize: 13),
          ),
          const SizedBox(height: 4),
          Text(
            value,
            style: const TextStyle(
              color: Colors.white,
              fontWeight: FontWeight.bold,
            ),
          ),
        ],
      ),
    );
  }
}
