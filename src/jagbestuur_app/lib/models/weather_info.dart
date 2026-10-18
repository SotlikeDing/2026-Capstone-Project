class WeatherInfo {
  final String areaName;
  final double latitude;
  final double longitude;
  final double temperature;
  final double windSpeed;
  final String condition;
  final int rainChance;

  const WeatherInfo({
    required this.areaName,
    required this.latitude,
    required this.longitude,
    required this.temperature,
    required this.windSpeed,
    required this.condition,
    required this.rainChance,
  });
}
