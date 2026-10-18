# Handoff: Outo-skoot (gunshot detection) - work in progress

> For the next Claude Code session: read this, then `Plans/gunshot_detection_implementation_plan.md`, then continue at
> **"Next steps"**. Written 2026-10-08 before a laptop restart.

## Context
- Branch: **`main_dev_helgard`**, recreated from `origin/main_dev` (commit `3145eeb`). Exists locally only, not pushed.
- **The user said: implement fully, DO NOT COMMIT anything.** Everything below is uncommitted working-tree changes.
- User decisions on the plan (section 7):
  1. screen-off listening: **yes**
  2. pin only near shots by default: **yes**
  3. on-device personal learning only: **yes**
  4. team sharing (phase 8): **later** (not in this round)
  5. platforms: **Android only**
- Flutter SDK: **`C:\src\flutter\bin`** (not on PATH; use `export PATH="/c/src/flutter/bin:$PATH"` in Git Bash).
  Android SDK: `C:\Users\helga\OneDrive\Desktop\Android\sdk`. No phone/emulator was connected (`adb devices` was empty).
- Python venv with `ai-edge-litert`, numpy, scipy: `C:\Users\helga\.venvs\mikpunt-ai\Scripts\python.exe`.

## Done (all in `src/jagbestuur_app/`, uncommitted)
| File | What |
|---|---|
| `pubspec.yaml` | added `record ^7.1.1`, `tflite_flutter ^0.12.1`, `flutter_foreground_task ^11.0.3`; assets `lib/assets/models/yamnet.tflite` + `yamnet_class_map.csv` |
| `lib/assets/models/yamnet.tflite` (16 MB) | TF Hub `google/lite-model/yamnet/tflite/1` - waveform in (resized to 15600), outputs: scores [1,521], embedding [1,1024], spectrogram [96,64]. "Gunshot, gunfire" is class index **421** (looked up by name anyway) |
| `lib/assets/models/yamnet_class_map.csv` | from tensorflow/models research/audioset/yamnet |
| `lib/services/gunshot/onset_gate.dart` | PCM16->float, `OnsetGate` (peak dBFS + 18 dB energy jump over 200 ms, 300 ms refractory), `AudioRingBuffer` (2 s) |
| `lib/services/gunshot/gunshot_settings.dart` | `GunshotSensitivity` (gate dBFS -20/-30/-40; model thresholds), `GunshotCalibration` (**placeholder thresholds 0.40/0.20/0.10 - must be replaced by calibration**, nearPeakDbfs -6), `GunshotSettings` (enabled/sensitivity/pinDistant, SharedPreferences per user) |
| `lib/services/gunshot/shot_classifier.dart` | `ShotClassifier` interface, `kGunshotClassNames`, `kYamnetWindowSamples` |
| `lib/services/gunshot/yamnet_classifier.dart` | tflite_flutter `IsolateInterpreter`, raw byte buffers for all 3 outputs, class map parsing |
| `lib/services/gunshot/shot_event_merger.dart` | merge shots within 4 s into one event ("2 skote", "(ver)") |
| `lib/services/gunshot/personal_head.dart` | logistic regression on embeddings, active from 20 labels (>=5 each), share n/(n+50) cap 0.8, `scoreToConfidence` (threshold -> 0.5) |
| `lib/services/gunshot/gunshot_pipeline.dart` | PCM -> frames -> gate -> classifier -> head -> near/far filter -> merger -> events; stats |
| `lib/services/gunshot/mic_foreground_service.dart` | flutter_foreground_task, type microphone, allowAutoRestart false, stopWithTask true |
| `lib/services/gunshot/gunshot_detector_service.dart` | recorder (unprocessed -> voiceRecognition -> mic fallback, AGC/NS/AEC off), model load, learning data in `gunshot_learning/labels.json` (embeddings only, no audio), retrain via compute() |
| `lib/models/hunt_pin.dart` | new optional fields `autoDetected`, `confidence`, `confirmed`, getter `needsReview`; old JSON still loads |
| `lib/services/hunt_tracking_service.dart` | `dropPin(...)` takes `position`, `autoDetected`, `confidence` |
| `lib/widgets/gunshot_settings_sheet.dart` | Outo-skoot sheet (toggle, sensitivity, distant shots, learning status + "Vergeet", live stats) |
| `lib/widgets/live_pin_list.dart` | confirm button + "Outo NN%" for auto pins |
| `lib/screens/map_screen.dart` | microphone toolbar button, start listening at hunt start (while visible), stop at hunt end/dispose, auto pin + fresh GPS fix + haptic + undo snackbar, follow-up shots update the same pin, confirm/remove = training labels, review-pin styling, "LUISTER VIR SKOTE" badge |
| `android/app/src/main/AndroidManifest.xml` | `RECORD_AUDIO`, `FOREGROUND_SERVICE_MICROPHONE`, `<service ...flutter_foreground_task.service.ForegroundService foregroundServiceType="microphone">` |
| `test/gunshot_detection_test.dart` | 19 tests (gate, ring buffer, merger, head, pipeline with fake classifier, class map, pin JSON compat) |
| `tools/audio/calibrate_yamnet.py` (repo root `tools/`) | calibration script (written, **not run yet**) |
| `Plans/gunshot_detection_implementation_plan.md` | the reviewed plan (rewritten this session) |

Verified: `flutter analyze` = no issues; `flutter test` = **46/46 pass** (27 old + 19 new).

## Update 2026-10-08 (second session) - steps 1-4 below are DONE
- Build: the Kotlin jvmTarget fix is in `android/build.gradle.kts`; debug and release APKs build (no R8 rules needed). If Gradle
  fails with `PKIX path building failed` while downloading, run with `JAVA_TOOL_OPTIONS=-Djavax.net.ssl.trustStoreType=Windows-ROOT`
  (Java doesn't trust this machine's HTTPS interception; curl does).
- Calibration ran: high 0.01 / medium 0.05 / low 0.35, now in `GunshotCalibration`. Two bugs fixed in `calibrate_yamnet.py`
  first (stereo integer clips were not scaled -> full-scale garbage; clips starting at the bang got no lead-in so the gate never
  fired). UrbanSound8K came from the Hugging Face parquet mirror (Zenodo kept failing), unpacked in that session's scratchpad.
- Plan doc status updated. Remaining: on-phone checks (step 5). Still nothing committed.
- Code review fixes (same day): start/stop/dispose serialised with cancellation; pipeline close waits for a running
  classification; atomic `HuntTrackingService.updatePinWith` for confirm + follow-up shots; unreviewed auto pins excluded from
  shot statistics and marked in the recap; one training pass at a time with version check (forget can't be undone); learning
  loaded once, saved one write at a time, and loaded before the settings sheet opens; removed/failed pins end their shot event;
  foreground-service failure reported ("LUISTER (SKERM AAN)"); early-onset window fix; pins carry the shot time; iOS mic text;
  `.gitignore` for `tools/audio`. analyze clean, 50/50 tests pass, debug APK builds.

## Where it stopped
`flutter build apk --debug` **FAILED** in the plugin:
```
Execution failed for task ':tflite_flutter:compileDebugKotlin'.
Inconsistent JVM-target compatibility detected for tasks 'compileDebugJavaWithJavac' (11) and 'compileDebugKotlin' (17).
```
tflite_flutter 0.12.1 sets Java 11 but no Kotlin jvmTarget. The proposed fix (NOT applied - the edit was interrupted) was to add
to `android/build.gradle.kts` after the `evaluationDependsOn(":app")` block:
```kotlin
// tflite_flutter compiles Java for JVM 11 but leaves Kotlin on JVM 17; align them.
subprojects {
    if (name == "tflite_flutter") {
        tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile>().configureEach {
            compilerOptions.jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_11)
        }
    }
}
```
Check first that the Kotlin Gradle plugin classes are visible to the root build script (`android/settings.gradle.kts` plugins
block). Alternative if not: `kotlin.jvm.target.validation.mode=warning` in `android/gradle.properties` (less clean). Ask the user
before choosing if unsure - they interrupted the last attempt.

## Next steps
1. Fix the build (above), then `flutter build apk --debug` (takes ~6 min) and `flutter build apk --release` (R8 may need
   `-dontwarn org.tensorflow.lite.gpu.**` keep rules for tflite_flutter).
2. **Calibration** (phase 3): run `tools/audio/calibrate_yamnet.py --us8k <UrbanSound8K root> --esc50 <ESC-50 root>` with the venv
   above, then put the printed thresholds into `GunshotCalibration` in `gunshot_settings.dart` (and the comment there).
   Datasets were being downloaded into the OLD session's scratchpad
   (`C:\Users\helga\AppData\Local\Temp\claude\C--Users-helga-OneDrive-Desktop-Akademia-Capstone-Projek\c56040c5-f232-48c7-92db-c2a9dcaaa55e\scratchpad\`):
   - `esc50/ESC-50-master` - complete (2000 clips).
   - `us8k/` - UrbanSound8K download was **in progress** (partial; the restart kills it). Re-download:
     `curl -sSL "https://zenodo.org/records/1203745/files/UrbanSound8K.tar.gz?download=1" | tar -xz --wildcards '*/UrbanSound8K.csv' '*-6-*.wav' '*-3-*.wav' '*-2-*.wav' '*-5-*.wav' '*-1-*.wav'`
     (Zenodo sometimes answers 504 - retry). Backup mirror: Hugging Face `danavery/urbansound8K` (16 parquet files, ~400 MB each).
     The script expects UrbanSound8K's layout `metadata/UrbanSound8K.csv` + `audio/foldN/*.wav` (the tar extracts to that).
   - Keep datasets out of the repo (non-commercial licences; used only to measure).
3. Re-run `flutter analyze` and `flutter test` after any change.
4. Update `Plans/gunshot_detection_implementation_plan.md` status: implemented; settings live in the map's microphone sheet (not
   the profile screen); iOS out of scope (Android only); two foreground notifications while listening (geolocator's location one +
   Outo-skoot's microphone one) - known, acceptable.
5. Not possible without a phone: on-device check of model load/latency, background listening with the screen off, battery over
   4 h, real gunshots (field test, phase 7). Tell the user these remain.
6. Do NOT commit (user's instruction). Summarise changes for the user to review.
