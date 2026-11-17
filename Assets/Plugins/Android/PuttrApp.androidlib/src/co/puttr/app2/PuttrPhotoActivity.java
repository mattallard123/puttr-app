// Picking a photo on Android: a see-through activity that opens the system photo picker and copies
// the chosen image to the app's cache, then tells Unity: UnitySendMessage(target, "OnPhoto", path),
// or "" if cancelled. (An activity of our own, so Unity's own activity needs nothing added.)
package co.puttr.app2;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.MediaStore;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.lang.reflect.Method;

public final class PuttrPhotoActivity extends Activity {
    static String target = "";
    static boolean video, camera;
    static final int PICK = 4021;

    public static void pick(Activity from, String unityTarget) { pickKind(from, unityTarget, false); }

    /** The system camera (front if it will), the photo written to the cache through this module's FileProvider. */
    public static void takePhoto(Activity from, String unityTarget) {
        target = unityTarget;
        video = false;
        camera = true;
        from.runOnUiThread(() -> from.startActivity(new Intent(from, PuttrPhotoActivity.class)));
    }

    public static void pickKind(Activity from, String unityTarget, boolean wantVideo) {
        target = unityTarget;
        video = wantVideo;
        camera = false;
        from.runOnUiThread(() -> from.startActivity(new Intent(from, PuttrPhotoActivity.class)));
    }

    static void send(String path) {
        try {
            Class<?> player = Class.forName("com.unity3d.player.UnityPlayer");
            Method m = player.getMethod("UnitySendMessage", String.class, String.class, String.class);
            m.invoke(null, target, "OnPhoto", path == null ? "" : path);
        } catch (Exception ignored) { }
    }

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        Intent intent;
        if (camera) {
            File out = new File(getCacheDir(), "puttr-camera.jpg");
            intent = new Intent(MediaStore.ACTION_IMAGE_CAPTURE);
            intent.putExtra(MediaStore.EXTRA_OUTPUT, androidx.core.content.FileProvider.getUriForFile(this, getPackageName() + ".puttrfiles", out));
            intent.putExtra("android.intent.extras.CAMERA_FACING", 1);
            intent.putExtra("android.intent.extras.LENS_FACING_FRONT", 1);
            intent.putExtra("android.intent.extra.USE_FRONT_CAMERA", true);
            intent.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            try { startActivityForResult(intent, PICK); }
            catch (Exception e) { send("nocamera"); finish(); }
            return;
        }
        if (Build.VERSION.SDK_INT >= 33) { intent = new Intent(MediaStore.ACTION_PICK_IMAGES); if (video) intent.setType("video/*"); }
        else { intent = new Intent(Intent.ACTION_GET_CONTENT); intent.setType(video ? "video/*" : "image/*"); }
        try { startActivityForResult(intent, PICK); }
        catch (Exception e) { send(""); finish(); }
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        String path = "";
        if (camera) {
            File out = new File(getCacheDir(), "puttr-camera.jpg");
            send(result == RESULT_OK && out.length() > 0 ? out.getAbsolutePath() : "");
            finish();
            return;
        }
        Uri uri = data != null ? data.getData() : null;
        if (result == RESULT_OK && uri != null) {
            File out = new File(getCacheDir(), video ? "puttr-video.mp4" : "puttr-photo");
            try (InputStream in = getContentResolver().openInputStream(uri); OutputStream os = new FileOutputStream(out)) {
                byte[] buf = new byte[16384];
                int n;
                while ((n = in.read(buf)) > 0) os.write(buf, 0, n);
                path = out.getAbsolutePath();
            } catch (Exception ignored) { path = ""; }
        }
        send(path);
        finish();
    }
}
