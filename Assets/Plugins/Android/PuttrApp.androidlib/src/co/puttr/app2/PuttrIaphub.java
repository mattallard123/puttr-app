// IAPHUB on Android (com.iaphub:iaphub-android-sdk, as react-native-iaphub uses): the Puttr Club
// subscriptions. Same app id and key as puttr-app; the IAPHUB user is the Firebase uid, or the
// account owner's for a managed profile, so the API's webhook finds the right profile.
//
// Answers go to Unity: UnitySendMessage(target, "OnIap", "<id>|ok|<json>") or "<id>|error|<code>|<message>",
// and "update" when IAPHUB says the user changed (a purchase landed, a renewal, a restore).
package co.puttr.app2;

import android.app.Activity;

import com.iaphub.ActiveProduct;
import com.iaphub.Iaphub;
import com.iaphub.IaphubError;
import com.iaphub.Product;
import com.iaphub.Products;

import org.json.JSONArray;
import org.json.JSONObject;

import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Map;

import kotlin.Unit;

public final class PuttrIaphub {
    static String target = "";
    static boolean started;

    static void send(String message) {
        try {
            Class<?> player = Class.forName("com.unity3d.player.UnityPlayer");
            Method m = player.getMethod("UnitySendMessage", String.class, String.class, String.class);
            m.invoke(null, target, "OnIap", message);
        } catch (Exception ignored) { }
    }

    static void answer(String request, IaphubError err, Object value) {
        if (err != null) { send(request + "|error|" + err.getCode() + "|" + String.valueOf(err.getMessage()).replace('|', ' ')); return; }
        send(request + "|ok|" + (value == null ? "" : value.toString()));
    }

    static Object json(Object v) {
        try {
            if (v == null) return JSONObject.NULL;
            if (v instanceof Map) {
                JSONObject o = new JSONObject();
                for (Object e : ((Map<?, ?>) v).entrySet()) { Map.Entry<?, ?> kv = (Map.Entry<?, ?>) e; o.put(String.valueOf(kv.getKey()), json(kv.getValue())); }
                return o;
            }
            if (v instanceof List) { JSONArray a = new JSONArray(); for (Object x : (List<?>) v) a.put(json(x)); return a; }
            if (v instanceof Date) return ((Date) v).getTime();
            return v;
        } catch (Exception e) { return JSONObject.NULL; }
    }

    public static void start(Activity activity, String appId, String apiKey, String unityTarget) {
        target = unityTarget;
        if (started) return;
        started = true;
        Iaphub.INSTANCE.start(activity.getApplicationContext(), appId, apiKey, null, false, true, "production", "", "", ""); // no sdk name: IAPHUB rejects "unity" (library_name_invalid)
        Iaphub.INSTANCE.setOnUserUpdateListener(() -> { send("update"); return Unit.INSTANCE; });
    }

    public static void login(String userId, String request) {
        Iaphub.INSTANCE.login(userId, err -> { answer(request, err, null); return Unit.INSTANCE; });
    }

    public static void logout() { Iaphub.INSTANCE.logout(); }

    public static void products(String request) {
        Iaphub.INSTANCE.getProducts(new ArrayList<>(), (err, products) -> {
            if (err != null || products == null) { answer(request, err, null); return Unit.INSTANCE; }
            try {
                JSONObject out = new JSONObject();
                JSONArray sale = new JSONArray(), active = new JSONArray();
                for (Product p : products.getProductsForSale()) sale.put(json(p.getData()));
                for (ActiveProduct p : products.getActiveProducts()) active.put(json(p.getData()));
                out.put("productsForSale", sale);
                out.put("activeProducts", active);
                answer(request, null, out);
            } catch (Exception e) { send(request + "|error|unexpected|" + e.getMessage()); }
            return Unit.INSTANCE;
        });
    }

    public static void buy(Activity activity, String sku, String request) {
        activity.runOnUiThread(() -> Iaphub.INSTANCE.buy(activity, sku, null, true, (err, transaction) -> {
            answer(request, err, transaction == null ? null : json(transaction.getData()));
            return Unit.INSTANCE;
        }));
    }

    public static void restore(String request) {
        Iaphub.INSTANCE.restore((err, response) -> { answer(request, err, null); return Unit.INSTANCE; });
    }
}
