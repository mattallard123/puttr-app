// A short tap (Haptics.cs), where puttr-app used react-native-haptic-feedback. Asked of the window's own view,
// so it needs no VIBRATE permission and follows the Player's touch feedback setting, as the React Native app
// did (ignoreAndroidSystemSettings: false).
// kind: 0 light, 1 medium, 2 heavy (impacts); 3 success, 4 warning, 5 error (notifications).
package co.puttr.app2;

import android.app.Activity;
import android.os.Build;
import android.view.HapticFeedbackConstants;
import android.view.View;

public final class PuttrHaptics {
    public static void play(Activity activity, int kind) {
        if (activity == null) return;
        activity.runOnUiThread(() -> {
            try {
                View view = activity.getWindow().getDecorView();
                view.performHapticFeedback(constant(kind));
            } catch (Exception ignored) { }
        });
    }

    private static int constant(int kind) {
        // CONFIRM and REJECT came with Android 11; before it the nearest plain tap stands in.
        boolean r = Build.VERSION.SDK_INT >= Build.VERSION_CODES.R;
        switch (kind) {
            case 0: return HapticFeedbackConstants.KEYBOARD_TAP;
            case 2: return HapticFeedbackConstants.LONG_PRESS;
            case 3: return r ? HapticFeedbackConstants.CONFIRM : HapticFeedbackConstants.VIRTUAL_KEY;
            case 4: return HapticFeedbackConstants.LONG_PRESS;
            case 5: return r ? HapticFeedbackConstants.REJECT : HapticFeedbackConstants.LONG_PRESS;
            default: return HapticFeedbackConstants.VIRTUAL_KEY;
        }
    }
}
