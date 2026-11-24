// Sharing a file (a Player's data export) through the system share sheet, via this module's FileProvider.
package co.puttr.app2;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;

import androidx.core.content.FileProvider;

import java.io.File;

public final class PuttrShare {
    public static void file(Activity activity, String path, String type) {
        activity.runOnUiThread(() -> {
            try {
                Uri uri = FileProvider.getUriForFile(activity, activity.getPackageName() + ".puttrfiles", new File(path));
                Intent send = new Intent(Intent.ACTION_SEND);
                send.setType(type);
                send.putExtra(Intent.EXTRA_STREAM, uri);
                send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                activity.startActivity(Intent.createChooser(send, null));
            } catch (Exception ignored) { }
        });
    }

    // A message and a link (a played game's "Share game"): Android sends one text, so the link goes after the words.
    public static void text(Activity activity, String text, String url, String subject) {
        activity.runOnUiThread(() -> {
            try {
                String body = text == null ? "" : text;
                if (url != null && url.length() > 0) body = body.length() > 0 ? body + " " + url : url;
                Intent send = new Intent(Intent.ACTION_SEND);
                send.setType("text/plain");
                send.putExtra(Intent.EXTRA_TEXT, body);
                if (subject != null && subject.length() > 0) send.putExtra(Intent.EXTRA_SUBJECT, subject);
                activity.startActivity(Intent.createChooser(send, null));
            } catch (Exception ignored) { }
        });
    }
}
