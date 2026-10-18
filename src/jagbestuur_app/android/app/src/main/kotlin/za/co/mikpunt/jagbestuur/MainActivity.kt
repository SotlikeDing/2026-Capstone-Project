package za.co.mikpunt.jagbestuur

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)

        MethodChannel(
            flutterEngine.dartExecutor.binaryMessenger,
            "jagbestuur/permissions",
        ).setMethodCallHandler { call, result ->
            when (call.method) {
                "requestNotifications" -> result.success(requestNotifications())
                else -> result.notImplemented()
            }
        }
    }

    /**
     * Android 13+ needs POST_NOTIFICATIONS at runtime for the hunt's
     * foreground-service notification to be shown. Returns true if already
     * granted; otherwise shows the system prompt and returns false.
     */
    private fun requestNotifications(): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            return true
        }
        val permission = Manifest.permission.POST_NOTIFICATIONS
        if (checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED) {
            return true
        }
        requestPermissions(arrayOf(permission), NOTIFICATION_REQUEST_CODE)
        return false
    }

    private companion object {
        const val NOTIFICATION_REQUEST_CODE = 4101
    }
}
