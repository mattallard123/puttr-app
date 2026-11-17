// A push that arrives while the app is open: Android shows nothing for it, so Unity shows its own
// toast (as the app's react-native-notifier) and refreshes the notifications bell.
package co.puttr.app2;

import com.google.firebase.messaging.FirebaseMessagingService;
import com.google.firebase.messaging.RemoteMessage;

import org.json.JSONObject;

import java.util.Map;

public final class PuttrMessagingService extends FirebaseMessagingService {
    @Override public void onMessageReceived(RemoteMessage message) {
        try {
            JSONObject o = new JSONObject();
            RemoteMessage.Notification n = message.getNotification();
            o.put("title", n != null && n.getTitle() != null ? n.getTitle() : "");
            o.put("body", n != null && n.getBody() != null ? n.getBody() : "");
            JSONObject data = new JSONObject();
            for (Map.Entry<String, String> kv : message.getData().entrySet()) data.put(kv.getKey(), kv.getValue());
            o.put("data", data);
            PuttrPush.send("OnPushForeground", o.toString());
        } catch (Exception ignored) { }
    }

    @Override public void onNewToken(String token) {
        PuttrPush.send("OnPushToken", token);
    }
}
