// Push on Android: the FCM token (as @react-native-firebase/messaging getToken), the device id the
// API keys tokens by (ANDROID_ID, as DeviceInfo.getUniqueIdSync), and what a tapped notification carried.
//
// A notification tapped while the app was closed or in the background opens Unity's activity with the
// push's data as extras: pendingTap() hands them over once. A push that arrives while the app is open
// comes through PuttrMessagingService and goes to Unity at once ("OnPushForeground").
package co.puttr.app2;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.provider.Settings;

import com.google.firebase.messaging.FirebaseMessaging;

import org.json.JSONObject;

import java.lang.reflect.Method;

public final class PuttrPush {
    static String target = "";
    static String lastIntentId = "";

    static void send(String method, String message) {
        if (target.isEmpty()) return;
        try {
            Class<?> player = Class.forName("com.unity3d.player.UnityPlayer");
            Method m = player.getMethod("UnitySendMessage", String.class, String.class, String.class);
            m.invoke(null, target, method, message == null ? "" : message);
        } catch (Exception ignored) { }
    }

    public static void token(String unityTarget) {
        target = unityTarget;
        FirebaseMessaging.getInstance().getToken().addOnCompleteListener(t -> send("OnPushToken", t.isSuccessful() ? t.getResult() : ""));
    }

    public static String deviceId(Activity a) {
        String id = Settings.Secure.getString(a.getContentResolver(), Settings.Secure.ANDROID_ID);
        return id == null ? "" : (id.length() > 64 ? id.substring(0, 64) : id);
    }

    /** The data of the notification that opened the app (FCM puts it in the launch intent's extras), once. */
    public static String pendingTap(Activity a) {
        Intent intent = a.getIntent();
        Bundle extras = intent == null ? null : intent.getExtras();
        if (extras == null || !extras.containsKey("google.message_id")) return "";
        String id = extras.getString("google.message_id", "");
        if (id.equals(lastIntentId)) return "";
        lastIntentId = id;
        try {
            JSONObject data = new JSONObject();
            for (String key : extras.keySet()) if (!key.startsWith("google.") && !key.startsWith("gcm.") && !key.equals("from") && !key.equals("collapse_key")) data.put(key, String.valueOf(extras.get(key)));
            return data.toString();
        } catch (Exception e) { return ""; }
    }
}
