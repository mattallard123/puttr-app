// Google sign-in on Android, through Credential Manager (the account sheet Android shows). The
// ID token it returns is for the Firebase project's web client, which Firebase accepts for google.com.
// The app must be signed with a key registered for co.puttr.android in Firebase (puttr-app's debug key
// for test builds, Play's for the store).
//
// Answers go to Unity as UnitySendMessage(target, "OnGoogleToken", idToken) or
// UnitySendMessage(target, "OnSignInError", "cancelled" | message).
package co.puttr.app2;

import android.app.Activity;
import android.os.CancellationSignal;

import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;

import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import java.lang.reflect.Method;
import java.util.concurrent.Executors;

public final class PuttrSignIn {
    static void send(String target, String method, String message) {
        try {
            Class<?> player = Class.forName("com.unity3d.player.UnityPlayer");
            Method m = player.getMethod("UnitySendMessage", String.class, String.class, String.class);
            m.invoke(null, target, method, message);
        } catch (Exception ignored) { }
    }

    public static void google(Activity activity, String webClientId, String target) {
        activity.runOnUiThread(() -> {
            GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(webClientId).build();
            GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
            CredentialManager.create(activity).getCredentialAsync(activity, request, new CancellationSignal(), Executors.newSingleThreadExecutor(),
                new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                    @Override public void onResult(GetCredentialResponse response) {
                        Credential c = response.getCredential();
                        if (c instanceof CustomCredential && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(c.getType())) {
                            try { send(target, "OnGoogleToken", GoogleIdTokenCredential.createFrom(c.getData()).getIdToken()); }
                            catch (Exception e) { send(target, "OnSignInError", "Google did not finish signing in."); }
                        } else send(target, "OnSignInError", "Google did not finish signing in.");
                    }

                    @Override public void onError(GetCredentialException e) {
                        if (e instanceof GetCredentialCancellationException) send(target, "OnSignInError", "cancelled");
                        else send(target, "OnSignInError", "Google sign-in is not available: " + e.getMessage());
                    }
                });
        });
    }
}
