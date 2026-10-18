# Device safety: rules and pre-install checklist

How the Jagbestuur app keeps test builds on physical phones contained, and what
to check before every install.

## What is already in the code

| Safeguard | Where | What it does |
|---|---|---|
| `isTestingEnv` | `lib/config/safety_config.dart` | Defaults to `true`. Recap videos remain in permanent per-user private storage, but are not exported to the public gallery. Release builds opt out with `--dart-define=IS_TESTING_ENV=false`. |
| Legacy data cleanup | `lib/services/legacy_storage_cleanup.dart` | Deletes the pre-account shared `hunt_sessions` and `recap_videos` folders at startup. Current data below `users/<userId>/` is never touched. |
| Trophy photo upload | `api/trophies/photos`, `TrophyPhotoStorage` | Authenticated uploads only; validates a 10 MB limit and JPEG/PNG/WebP magic bytes, then stores an unguessable per-user file name. |
| Global error barrier | `lib/safety/app_error_barrier.dart`, installed in `main()` | `FlutterError.onError`, `PlatformDispatcher.instance.onError` and `ErrorWidget.builder` log every uncaught error and show a fallback UI instead of a red screen or a dead app. |
| Image cache cap | same file | Decoded image cache limited to 60 MB (Flutter's default is 100 MB). |
| Payload guard (20 MB) | `lib/safety/payload_guard.dart` | `PayloadGuard.readString/readLines` check file size before reading. `PayloadGuard.httpGet` and `PayloadGuardInterceptor` (on both Dio clients) abort downloads mid-stream past 20 MB. |
| Staged video export | `lib/screens/hike_recap_screen.dart` | Capture → encode → persist → gallery. Each stage finishes and closes its files before the next one starts. The whole export is inside one try/catch/finally, so temp frames and partial `.mp4` files are always removed. |
| Path-only gallery save | `VideoStorageService.saveVideoToGallery` | Passes the file path to `gal`. The video is never read into a `Uint8List`. Returns a `GallerySaveResult` and never throws. |
| Lifecycle | `HikeRecapScreen.dispose`, `MapScreen.dispose` | Cancels timers, cancels a running FFmpeg session, and disposes `MapController`. |

## Rules for new code

1. **Never load an unbounded payload.** For files, use `PayloadGuard.readString` / `readLines`,
   or call `PayloadGuard.ensureFileFits` before `readAsBytes`. For HTTP, use
   `PayloadGuard.httpGet` or the shared Dio clients, not raw `http.get`. For
   large media, pass file paths, not bytes.
2. **Give every `Image.network` / `Image.file` a `cacheWidth`** (or wrap the provider in
   `ResizeImage`) sized to the widget. A 4000 px photo decodes to about 64 MB of RAM
   regardless of how small it is on screen.
3. **Everything you create in `initState`, you release in `dispose`:**
   `AnimationController`, `TextEditingController`, `ScrollController`,
   `FocusNode`, `MapController`, `VideoPlayerController`, `Timer`,
   `StreamSubscription`, `IOSink`, `ValueNotifier` listeners, `FFmpegKit` sessions.
4. **After every `await` in a `State`, check `mounted`** before `setState` or using `context`.
5. **Fire-and-forget futures are wrapped in `unawaited(...)`** and must handle their
   own errors. Otherwise the error only reaches the global barrier.
6. **Catch at the feature boundary.** User-triggered async work (button handlers)
   gets its own try/catch with a SnackBar. The global barrier is the last line
   of defence, not the plan.
7. **Log with `debugPrint` or `AppErrorBarrier.report`,** never `print`.

## What the analyzer catches (`flutter analyze`)

`analysis_options.yaml` escalates these rules:

| Rule | Severity | Catches |
|---|---|---|
| `cancel_subscriptions` | error | `StreamSubscription` fields never cancelled |
| `close_sinks` | error | `StreamController` / `IOSink` fields never closed |
| `use_build_context_synchronously` | error | `context` used after an `await` without a `mounted` check |
| `control_flow_in_finally` | error | `return`/`throw` in `finally` hiding the real error |
| `unawaited_futures` | warning | futures whose errors nobody sees |
| `avoid_void_async`, `only_throw_errors`, `empty_catches`, `avoid_print` | info | sloppy async / error handling |

**What it cannot catch:** no built-in Dart lint detects a missing `dispose()`
for `AnimationController`, `ScrollController`, `TextEditingController` and
similar, and none checks platform channels for leaks. Cover those with:

- Code review against rule 3 above. Grep for `Controller(` and `FocusNode(` and
  check that each has a matching `.dispose()`.
- Leak tracking in widget tests (built into `flutter_test` since Flutter 3.18):

  ```dart
  testWidgets('recap screen does not leak', experimentalLeakTesting:
      LeakTesting.settings.withTrackedAll(), (tester) async { ... });
  ```

- DevTools → Memory → "Diff snapshots" before and after opening and closing a screen
  (see the checklist below).

## Pre-install checklist (run before EVERY build goes on a phone)

### 1. Code
- [ ] `flutter analyze`: no new errors or warnings.
- [ ] `flutter test`: all green.
- [ ] `git diff main_dev`: every new controller, timer, subscription and sink has a matching dispose/cancel/close.
- [ ] New file or network reads go through `PayloadGuard`. New images have `cacheWidth`.
- [ ] `isTestingEnv` is `true` for test builds (the default; confirm nobody passed `--dart-define=IS_TESTING_ENV=false`).

### 2. Android manifest and Gradle
- [ ] `android/app/build.gradle.kts` is the only app build script. Don't add a Groovy
      `build.gradle` next to it; Gradle would silently prefer one over the other.
- [ ] Only the permissions you use are declared: location (fine, coarse,
      foreground-service location), `WAKE_LOCK`, `POST_NOTIFICATIONS`, and media for
      gallery export. Background location is intentionally *not* requested; tracking
      runs as a foreground service.
- [ ] Cleartext HTTP is allowed only in the debug and profile manifests. Release builds
      default to `https://` URLs; the API server must have a valid certificate.
- [ ] Release signing reads `android/key.properties` (git-ignored). Without it the
      build is signed with the debug key and Gradle warns: never distribute that APK.
- [ ] `applicationId` and `AppConfig.tileUserAgentPackageName` both remain
      `za.co.mikpunt.jagbestuur`.
- [ ] `targetSdk` comes from Flutter's default. After a Flutter upgrade, re-check its
      behaviour changes: API 34+ requires a foreground-service type for location
      tracking; API 33+ requires runtime `POST_NOTIFICATIONS` (requested when a hunt starts).

### 3. Build profile
- [ ] **Debug** (`flutter run`) for logic and breakpoints only. It's JIT-compiled, much slower and
      uses more memory, so don't judge performance or memory on it.
- [ ] **Profile** (`flutter run --profile`) for any performance or memory test.
      This is the build to open DevTools against.
- [ ] **Release** (`flutter run --release`) for a final smoke test before handing the APK to anyone.

### 4. On the device (profile build, DevTools open)
- [ ] Open and close the map and recap screens 10 times. In DevTools Memory the Dart
      heap should return to its baseline, not climb steadily.
- [ ] Generate a recap video. Watch memory during capture and encode, then confirm
      afterwards (`adb shell run-as za.co.mikpunt.jagbestuur ls -la cache files`)
      that no `hike_recap_frames_*` folders or stray `trek_herblik_*.mp4` files are left behind.
- [ ] Press back in the middle of a video export: no crash, and the temp files are gone.
- [ ] Deny gallery and location permissions: the app shows a message and keeps working.
- [ ] Airplane mode: network calls fail with a message, not a crash.
- [ ] `adb logcat | grep -E "AppErrorBarrier|FATAL|ANR"`: nothing unexpected.

### 5. If the phone misbehaves
- [ ] `adb logcat -b crash` and `adb bugreport` straight away, before rebooting, so the evidence survives.
- [ ] Uninstall the app (`adb uninstall za.co.mikpunt.jagbestuur`) to rule it in or out.
- [ ] Check free storage. A full disk (for example from accumulated video files)
      destabilises Android much more than an app crash does.
