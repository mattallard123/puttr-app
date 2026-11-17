// Paying for a lesson on Android: Stripe's PaymentSheet (stripe-android 23.12.0, as puttr-app's
// stripe-react-native), from a see-through activity of our own (PaymentSheet needs a ComponentActivity,
// and Unity's own activity is not one). Answers Unity with UnitySendMessage(target, "OnPay",
// "<id>|ok" | "<id>|cancelled" | "<id>|error|<message>").
package co.puttr.app2;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

import androidx.activity.ComponentActivity;

import com.stripe.android.PaymentConfiguration;
import com.stripe.android.paymentsheet.PaymentSheet;
import com.stripe.android.paymentsheet.PaymentSheetResult;

import java.lang.reflect.Method;

public final class PuttrPayActivity extends ComponentActivity {
    static String target = "", request = "", key = "", secret = "", customer = "", ephemeral = "";
    PaymentSheet sheet;
    boolean shown;

    public static void present(Activity from, String publishableKey, String clientSecret, String customerId, String ephemeralKey, String unityTarget, String requestId) {
        key = publishableKey; secret = clientSecret; customer = customerId; ephemeral = ephemeralKey;
        target = unityTarget; request = requestId;
        from.runOnUiThread(() -> from.startActivity(new Intent(from, PuttrPayActivity.class)));
    }

    static void send(String message) {
        try {
            Class<?> player = Class.forName("com.unity3d.player.UnityPlayer");
            Method m = player.getMethod("UnitySendMessage", String.class, String.class, String.class);
            m.invoke(null, target, "OnPay", request + "|" + message);
        } catch (Exception ignored) { }
    }

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        PaymentConfiguration.init(getApplicationContext(), key);
        sheet = new PaymentSheet(this, this::done);
    }

    @Override protected void onResume() {
        super.onResume();
        if (shown) return;
        shown = true;
        try {
            PaymentSheet.Configuration.Builder config = new PaymentSheet.Configuration.Builder("Puttr, Inc.");
            if (!customer.isEmpty() && !ephemeral.isEmpty()) config.customer(new PaymentSheet.CustomerConfiguration(customer, ephemeral));
            sheet.presentWithPaymentIntent(secret, config.build());
        } catch (Exception e) {
            send("error|" + String.valueOf(e.getMessage()).replace('|', ' '));
            finish();
        }
    }

    void done(PaymentSheetResult result) {
        if (result instanceof PaymentSheetResult.Completed) send("ok");
        else if (result instanceof PaymentSheetResult.Canceled) send("cancelled");
        else if (result instanceof PaymentSheetResult.Failed) send("error|" + String.valueOf(((PaymentSheetResult.Failed) result).getError().getMessage()).replace('|', ' '));
        finish();
    }
}
