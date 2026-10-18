import 'package:flutter/material.dart';

import '../models/hunt_pin.dart';

IconData iconForHuntPin(HuntPinType type) {
  switch (type) {
    case HuntPinType.shotFired:
      return Icons.gps_fixed_rounded;
    case HuntPinType.animalFound:
      return Icons.pets_rounded;
    case HuntPinType.campsite:
      return Icons.cabin_rounded;
    case HuntPinType.waterSource:
      return Icons.water_drop_rounded;
    case HuntPinType.hazard:
      return Icons.warning_amber_rounded;
    case HuntPinType.custom:
      return Icons.push_pin_rounded;
  }
}
