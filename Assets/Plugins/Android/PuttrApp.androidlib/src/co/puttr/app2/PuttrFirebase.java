// The native Firebase Auth SDK on Android: Plugins/iOS/PuttrFirebase.m's twin. Same package
// (co.puttr.android) and same Firebase app as the React Native app, so a Player signed in there is
// still signed in here after the update.
//
// Answers go to Unity as UnitySendMessage(target, "OnFirebase", "<id>|ok|<value>") or
// "<id>|error|<code>|<message>", the code Firebase's own ("ERROR_WRONG_PASSWORD", ...).
package co.puttr.app2;

import android.content.Context;

import com.google.android.gms.tasks.Task;
import com.google.firebase.FirebaseApp;
import com.google.firebase.FirebaseOptions;
import com.google.firebase.auth.AuthCredential;
import com.google.firebase.auth.AuthResult;
import com.google.firebase.auth.FirebaseAuth;
import com.google.firebase.auth.FirebaseAuthException;
import com.google.firebase.auth.FirebaseUser;
import com.google.firebase.auth.GoogleAuthProvider;
import com.google.firebase.auth.OAuthProvider;

import java.lang.reflect.Method;

public final class PuttrFirebase {
    // puttr-app/android/app/google-services.json (client ids; the key is read from firebase.xml).
    static final String APP_ID = "1:636312820806:android:23f732b1f9478b19a0fe47";
    static final String PROJECT = "puttr-287402";

    static FirebaseAuth auth;

    // The API key comes from res/values/firebase.xml (local only, never committed: Tools/keys/firebase.example.xml).
    static String apiKey(Context context) {
        int id = context.getResources().getIdentifier("google_api_key", "string", context.getPackageName());
        return id != 0 ? context.getString(id) : "";
    }

    static void send(String target, String message) {
        try {
            Class<?> player = Class.forName("com.unity3d.player.UnityPlayer");
            Method m = player.getMethod("UnitySendMessage", String.class, String.class, String.class);
            m.invoke(null, target, "OnFirebase", message);
        } catch (Exception ignored) { }
    }

    static void answer(String target, String request, String value, Exception error) {
        if (error == null) { send(target, request + "|ok|" + (value == null ? "" : value)); return; }
        String code = error instanceof FirebaseAuthException ? ((FirebaseAuthException) error).getErrorCode() : "ERROR_" + error.getClass().getSimpleName();
        String text = error.getMessage() == null ? "" : error.getMessage().replace('|', ' ');
        send(target, request + "|error|" + code + "|" + text);
    }

    public static int configure(Context context) {
        try {
            if (FirebaseApp.getApps(context).isEmpty()) {
                FirebaseOptions options = new FirebaseOptions.Builder()
                    .setApplicationId(APP_ID).setApiKey(apiKey(context)).setProjectId(PROJECT)
                    .setGcmSenderId("636312820806").setStorageBucket("puttr-287402.appspot.com")
                    .setDatabaseUrl("https://puttr-287402.firebaseio.com").build();
                FirebaseApp.initializeApp(context.getApplicationContext(), options);
            }
            auth = FirebaseAuth.getInstance();
            return 1;
        } catch (Exception e) {
            return 0;
        }
    }

    public static String currentUid() {
        FirebaseUser user = auth == null ? null : auth.getCurrentUser();
        return user == null ? null : user.getUid();
    }

    public static void getIdToken(boolean force, String target, String request) {
        FirebaseUser user = auth == null ? null : auth.getCurrentUser();
        if (user == null) { send(target, request + "|error|ERROR_NO_USER|Not signed in"); return; }
        user.getIdToken(force).addOnCompleteListener(t -> answer(target, request, t.isSuccessful() ? t.getResult().getToken() : null, t.isSuccessful() ? null : t.getException()));
    }

    static void finish(Task<AuthResult> task, String target, String request) {
        task.addOnCompleteListener(t -> {
            if (!t.isSuccessful()) { answer(target, request, null, t.getException()); return; }
            FirebaseUser user = t.getResult().getUser();
            answer(target, request, user == null ? "" : user.getUid(), null);
        });
    }

    public static void signInEmail(String email, String password, String target, String request) {
        finish(auth.signInWithEmailAndPassword(email, password), target, request);
    }

    /** A new email and password account (EmailAuth "Create account"). */
    public static void createUser(String email, String password, String target, String request) {
        finish(auth.createUserWithEmailAndPassword(email, password), target, request);
    }

    /** provider "apple.com": secret is the raw nonce. provider "google.com": secret is the access token. */
    public static void signInCredential(String provider, String idToken, String secret, String target, String request) {
        AuthCredential credential;
        if ("google.com".equals(provider)) credential = GoogleAuthProvider.getCredential(idToken, secret == null || secret.isEmpty() ? null : secret);
        else credential = OAuthProvider.newCredentialBuilder(provider).setIdTokenWithRawNonce(idToken, secret).build();
        finish(auth.signInWithCredential(credential), target, request);
    }

    public static void signInCustomToken(String token, String target, String request) {
        finish(auth.signInWithCustomToken(token), target, request);
    }

    public static int signOut() {
        if (auth != null) auth.signOut();
        return 1;
    }
}
