# Implementation Plan: Gunshot Detection & Auto-Pin (Jagbestuur mobile app)

> **Status (2026-10-08): implemented (phases 1-6), Android only - field test (phase 7) still open.** Decisions taken in section 7:
> screen-off listening yes, near shots only by default, on-device learning only, team sharing later, Android only (iOS out of
> scope, so the `Info.plist` items in section 4 are not done). Differences from the plan below:
> - Settings live in the map screen's microphone sheet (`widgets/gunshot_settings_sheet.dart`), not on the profile screen.
> - While listening there are two foreground notifications (geolocator's location one + Outo-skoot's microphone one) - known,
>   accepted.
> - Calibrated (phase 3) on 374 UrbanSound8K gunshots vs. 5410 other clips (UrbanSound8K + ESC-50): Hoog 0.01 (94% caught,
>   4.8% false pins per clip), Medium 0.05 (85%, 1.8%), Laag 0.35 (44%, 0.44%). Worst false triggers: can opening, fireworks,
>   glass breaking, clapping, footsteps. Details in `GunshotCalibration` (`gunshot_settings.dart`).
> - Debug and release APKs build. Not yet checked on a phone: model load/latency, screen-off listening, battery over 4 h, real
>   gunshots (phase 7).
>
> The earlier version of this plan was generic; the corrections are listed in section 9.

## 1. Goal

While a hunter has an **active hunt** running on the map screen (`lib/screens/map_screen.dart`), the phone listens for gunshots.
When one is heard, the app drops a **"Skoot geskiet" pin at the hunter's current GPS position** - the same pin type the hunter can
already drop by hand - so every shot is on the hunt's map and recap without touching the phone.

Requirements:
- **On-device and offline.** Hunting areas have no signal; no audio ever leaves the phone.
- **Only during an active hunt**, and only when the hunter switched it on (opt-in toggle).
- **Real AI, then learns from use** (same approach as the WebAPI AI Helper): a pre-trained sound-classification network decides,
  and the hunter's confirm / remove taps train a small personal correction model on top of it.
- **Battery-friendly** for a 4-6 hour hunt: the neural network only runs when a loud, sharp sound happens.
- **Honest pins**: an automatic pin is marked as automatic, shows its confidence, and is easy to confirm or remove.

## 2. What already exists (and is reused)

| Piece | Where | Reuse |
|---|---|---|
| Active-hunt state | `HuntTrackingService.isTracking`, `HuntTrackingService.activeHunt` (ValueNotifier) | Start/stop listening with the hunt |
| Pins incl. **`HuntPinType.shotFired`** ("Skoot geskiet", `gps_fixed` icon) | `lib/models/hunt_pin.dart` | Auto-pins are normal `shotFired` pins + 3 new optional fields |
| Crash-safe pin journal (`*.pins.jsonl`), recovery, recap | `HuntTrackingService.dropPin/removePin/updatePin` | Auto-pins survive a killed app like manual pins |
| Latest GPS fix, background location (geolocator foreground notification) | `HuntTrackingService.latestPosition`, `_trackingLocationSettings()` | Pin position |
| Pin markers, live pin list with remove | `map_screen.dart`, `widgets/live_pin_list.dart` | Show / confirm / remove auto-pins |

Hunts are stored **on the device only** (`hunt_sessions/`), there is no MobileAPI endpoint for hunts. This plan keeps that; server sync
is an optional later phase (section 7).

## 3. How detection works

```
 microphone (16 kHz mono PCM, phone's audio processing OFF)
        │  20 ms frames
        ▼
 ┌───────────────────────────┐  cheap, runs on every frame
 │ OnsetGate                 │  peak > threshold dBFS  AND  a jump of > 20 dB over the last ~200 ms
 └────────────┬──────────────┘  (a shot is a sudden impulse; wind, talking and engines are not)
              │ trigger (with 2 s ring buffer, so the start of the bang is kept)
              ▼
 ┌───────────────────────────┐  only on triggers - typically a few times per hour
 │ YAMNet (TFLite, ~4 MB)    │  0.975 s window from 0.2 s before the onset; runs in a background isolate
 │ + personal correction head│  score = max(gun classes); + 1024-d embedding for learning
 └────────────┬──────────────┘
              │ score ≥ sensitivity threshold
              ▼
 ┌───────────────────────────┐
 │ ShotEventMerger           │  echoes and quick follow-up shots within 4 s = ONE event ("2 skote")
 └────────────┬──────────────┘
              ▼
 HuntTrackingService.dropPin(shotFired, auto: true, confidence, near/far) → map marker + snackbar "Ongedaan maak" + haptic
```

**Model.** Google's **YAMNet** (pre-trained on AudioSet, 521 sound classes) as a TFLite file in the app. The gun classes are looked
up **by name** from the bundled class map (`Gunshot, gunfire`, `Machine gun`, `Fusillade`, `Cap gun`), not by hard-coded index.
YAMNet is multi-label, so its gunshot scores are often modest: the thresholds are **calibrated**, not a fixed 85% (section 6).

**Audio settings matter.** Phones apply automatic gain, noise suppression and echo cancellation that flatten a bang. Recording uses
the unprocessed audio source on Android and turns those off (`record` package: `autoGain/echoCancel/noiseSuppress: false`, Android
`audioSource: unprocessed` - names to be checked against the installed package version).

**Near vs far.** A shot from the hunter's own rifle (or someone next to them) nearly clips the microphone; a shot from the next
farm does not. Each event gets a `near` flag from its peak level. **Default: only near shots get a pin** (the pin is the phone's
position, which is only meaningful for shots fired close by). A setting can also pin distant shots, labelled "Skoot gehoor (ver)".

**Learning from the hunter.** Every auto-pin has *Bevestig* / *Verwyder*. The tap stores the event's YAMNet embedding (1024 numbers -
**not audio**) with the label in local storage. From 20 labels (at least 5 of each) a small logistic-regression head is trained on
the phone and blended in, `share = n / (n + 50)` capped at 0.8 - the same rule the AI Helper's teaming model uses. So a hunter whose
`.243` keeps being missed, or whose truck door keeps firing a pin, gets fewer mistakes over time.

## 4. Changes, file by file

**New (`src/jagbestuur_app/lib/`)**
- `services/gunshot/audio_stream_service.dart` - mic stream (`record`), 2 s ring buffer, starts/stops with the hunt.
- `services/gunshot/onset_gate.dart` - pure Dart frame analysis (peak dBFS, short-term energy jump). Unit-tested.
- `services/gunshot/yamnet_classifier.dart` - loads `assets/models/yamnet.tflite` + class map, runs in an isolate
  (`tflite_flutter` `IsolateInterpreter`), returns gun score + embedding.
- `services/gunshot/shot_event_merger.dart` - debounce / merge within 4 s, near/far from peak level. Unit-tested.
- `services/gunshot/personal_head.dart` - stored labelled embeddings, logistic regression, blend share. Unit-tested.
- `services/gunshot/gunshot_detector_service.dart` - wires the above, exposes `Stream<GunshotEvent>`, sensitivity setting.
- `assets/models/yamnet.tflite`, `assets/models/yamnet_class_map.csv` (Apache-2.0, from TensorFlow Hub / Kaggle Models).

**Changed**
- `models/hunt_pin.dart` - optional `autoDetected`, `confidence`, `confirmed` fields; `fromJson` defaults keep old journals and
  sessions readable.
- `services/hunt_tracking_service.dart` - `dropPin(...)` accepts the new fields; if the latest GPS fix is older than 10 s, ask for a
  fresh fix first (fallback: the latest one, with a note).
- `screens/map_screen.dart` - toolbar toggle "Outo-skoot" (only during a hunt), mic-on indicator, start/stop detector with the hunt,
  snackbar with *Ongedaan maak*, haptic feedback, auto-pins drawn outlined until confirmed.
- `widgets/live_pin_list.dart` - *Bevestig* / *Verwyder* on auto-pins, confidence shown.
- Settings (profile screen): sensitivity *Laag / Medium / Hoog*, "pin also distant shots", "forget what you learned".
- `pubspec.yaml` - `record`, `tflite_flutter`, `flutter_foreground_task` (phase 5), asset entries.
- `android/app/src/main/AndroidManifest.xml` - `RECORD_AUDIO`, `FOREGROUND_SERVICE_MICROPHONE`, the foreground-task service.
  `tflite_flutter` may require `minSdk` 26 (currently 24) - check when adding it.
- `ios/Runner/Info.plist` - `NSMicrophoneUsageDescription`, `UIBackgroundModes` (audio, location). **Existing gap:** the plist has
  no location usage descriptions at all, so iOS location prompts would fail today - add `NSLocationWhenInUseUsageDescription` /
  `NSLocationAlwaysAndWhenInUseUsageDescription` regardless of this feature.
- `tools/audio/calibrate_yamnet.py` (new, Python) - runs the same TFLite model over labelled clips and prints precision/recall per
  threshold, so the *Laag/Medium/Hoog* values are measured, not guessed.

## 5. Phases

| # | Phase | Delivers | Est. |
|---|---|---|---|
| 1 | Audio + onset gate | Mic stream with processing off, ring buffer, onset gate; debug screen showing levels and triggers | 1-2 d |
| 2 | YAMNet classifier | Model + class map in assets, isolate inference, latency check on a real phone (target < 100 ms) | 1-2 d |
| 3 | Calibration | `calibrate_yamnet.py` on public clips (UrbanSound8K `gun_shot` vs. dog bark, door, children, engine, drilling; FSD50K gunshot clips) → thresholds per sensitivity | 1 d |
| 4 | Pins on the map | Merger, near/far, auto-pins via `dropPin`, toggle, snackbar/undo, confirm/remove, settings | 2 d |
| 5 | Screen off / phone in pocket | Android: `flutter_foreground_task` service with `location` + `microphone` types, started at hunt start (Android 14 only allows a microphone service to start while the app is visible). iOS: audio background mode | 2-3 d |
| 6 | Learning | Store embeddings on confirm/remove, on-device logistic head, blend, "forget" setting | 1-2 d |
| 7 | Field test | Range day / real hunt: own shots, distant shots, suppressed, wind, truck doors; tune; 4 h battery measurement | 1 d |
| 8 | *(optional)* Share with the team | Upload shot pins (not audio) to MobileAPI so the PJ sees where shots were fired | 2-3 d |

Phases 1-4 give a demo-able feature with the app open; phase 5 is what makes it useful in the veld.

## 6. Accuracy, battery, privacy

- **False positives** (branch snap, door slam, clap): impulse gate + YAMNet + merger + the personal head; every auto-pin is reversible.
- **Missed shots** (suppressor, wind, phone deep in a pocket): the *Hoog* sensitivity; misses the hunter marks by hand are not
  training data (no embedding), so the plan does not claim to learn from those.
- **Thresholds** come from phase 3 measurements; the old plan's "> 85% confidence" is not realistic for YAMNet's multi-label scores.
- **Battery**: streaming the mic is cheap; YAMNet runs only on triggers (a few times per hour, ~20-60 ms each). Phase 7 measures a
  4 h hunt with and without detection; if the drop is more than ~5 percentage points, the gate thresholds are tightened.
- **Privacy (POPIA)**: audio lives only in a 2 s RAM buffer and is never saved or uploaded. Only the 1024-number embedding is stored
  for confirmed/removed events, on the device. Listening is opt-in per hunt with a visible mic indicator, and the permission text says
  exactly this.

## 7. Decisions for you

> Answered 2026-10-08: 1 yes, 2 near only, 3 on-device only, 4 later, 5 Android only.

1. **Screen-off listening (phase 5) in this round?** Recommended: yes - a hunter's phone is in a pocket. It is also the riskiest part
   (Android 14+ background-microphone rules; two foreground notifications unless location moves into the same service).
2. **Distant shots**: pin only near shots by default (recommended), or pin every shot heard?
3. **Learning**: on-device personal head only (recommended), or also upload embeddings (no audio) to train a shared model?
4. **Team sharing (phase 8)**: now, later, or not at all? Hunts are device-only today.
5. **Targets**: Android only for the capstone, or iOS too? (iOS needs the plist fixes above and a Mac to build.)

## 8. Testing

- **Unit (Dart, `flutter test`)**: onset gate on synthetic PCM (impulse vs. noise vs. speech-like envelope), merger timing and
  near/far, personal head training/blend, `HuntPin` JSON backward compatibility (old journals without the new fields).
- **Offline model evaluation**: `tools/audio/calibrate_yamnet.py` - precision/recall per threshold on public labelled clips.
- **On device**: debug screen (phase 1) shows levels, triggers and scores live; phase 7 field test with a written result sheet.
- Unit tests live in `test/gunshot_detection_test.dart`. The Flutter SDK is at `C:\src\flutter\bin` (not on PATH).

## 9. Corrections to the previous version of this plan

- `shotFired` pins, crash-safe pin storage and active-hunt state **already exist** - reuse them instead of a new marker manager / storage.
- `flutter_local_notifications` is not needed (geolocator already shows the tracking notification; feedback is a snackbar + haptic).
- FFT/spectrogram packages are not needed: YAMNet takes the raw 16 kHz waveform.
- "Confidence > 85%" replaced by measured thresholds; detection limited to near shots by default because the pin is the phone's
  position, not the shooter's.
- Android 14+ only lets a microphone foreground service start while the app is visible - start it with the hunt, not later.
- iOS location usage descriptions are missing today (separate small fix).
