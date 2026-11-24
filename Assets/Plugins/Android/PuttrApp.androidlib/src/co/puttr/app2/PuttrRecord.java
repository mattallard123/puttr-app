// Recording the coach's lesson on Android: the screen (MediaProjection) and the microphone into an MP4
// (MediaRecorder), written to the path Unity gives, so the file goes straight into Deliver's lesson-video slot.
// The same messages as the iPhone plugin (Plugins/iOS/PuttrRecord.m), UnitySendMessage(target, "OnRecord", ...):
//   started | stopped|<path> | failed|<reason> | cancelled
//
// Android asks the coach first (PuttrRecordActivity shows the system's screen-capture consent), and the capture
// runs in a foreground service (PuttrRecordService), which Android 10 and later require for a MediaProjection.
// Both must be declared in the manifest; until they are, available() says no and the app keeps the system
// screen recorder guide.
package co.puttr.app2;

import android.app.Activity;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.PackageManager;

import java.lang.reflect.Method;

public final class PuttrRecord {
    static String target = "PuttrRecord";
    static String path = "";
    static volatile boolean recording;

    /** True when this build declares the consent activity and the capture service (see the manifest note above). */
    public static boolean available(Activity activity) {
        try {
            PackageManager pm = activity.getPackageManager();
            pm.getActivityInfo(new ComponentName(activity, PuttrRecordActivity.class), 0);
            pm.getServiceInfo(new ComponentName(activity, PuttrRecordService.class), 0);
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    public static boolean isRecording() { return recording; }

    /** Asks for the capture (the system's consent sheet), then records to outPath. */
    public static void start(Activity activity, String unityTarget, String outPath) {
        target = unityTarget == null || unityTarget.isEmpty() ? "PuttrRecord" : unityTarget;
        path = outPath;
        if (recording) { send("started"); return; }
        activity.runOnUiThread(() -> {
            try { activity.startActivity(new Intent(activity, PuttrRecordActivity.class)); }
            catch (Exception e) { send("failed|Screen recording is not available on this phone."); }
        });
    }

    /** Stops and keeps the file. */
    public static void stop(Activity activity) {
        activity.runOnUiThread(() -> {
            if (!PuttrRecordService.finish(false)) send("failed|Nothing was recording.");
        });
    }

    /** Stops and throws the file away. */
    public static void cancel(Activity activity) {
        activity.runOnUiThread(() -> PuttrRecordService.finish(true));
    }

    static void send(String message) {
        try {
            Class<?> player = Class.forName("com.unity3d.player.UnityPlayer");
            Method m = player.getMethod("UnitySendMessage", String.class, String.class, String.class);
            m.invoke(null, target, "OnRecord", message == null ? "" : message);
        } catch (Exception ignored) { }
    }
}
