// The system's "start recording?" consent for a lesson recording, from a see-through activity of our own (as
// PuttrPhotoActivity), so Unity's activity needs nothing added. A yes starts PuttrRecordService with the grant and
// the screen's size; a no tells Unity "cancelled".
package co.puttr.app2;

import android.app.Activity;
import android.content.Intent;
import android.media.projection.MediaProjectionManager;
import android.os.Bundle;
import android.util.DisplayMetrics;

public final class PuttrRecordActivity extends Activity {
    static final int CONSENT = 4031;

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        MediaProjectionManager manager = (MediaProjectionManager) getSystemService(MEDIA_PROJECTION_SERVICE);
        if (manager == null) { PuttrRecord.send("failed|Screen recording is not available on this phone."); finish(); return; }
        try { startActivityForResult(manager.createScreenCaptureIntent(), CONSENT); }
        catch (Exception e) { PuttrRecord.send("failed|Screen recording is not available on this phone."); finish(); }
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != CONSENT) return;
        if (result != RESULT_OK || data == null) { PuttrRecord.send("cancelled"); finish(); return; }
        // The whole screen in real pixels (the status and navigation bars too), as the capture sees it.
        DisplayMetrics metrics = new DisplayMetrics();
        getWindowManager().getDefaultDisplay().getRealMetrics(metrics);
        Intent service = new Intent(this, PuttrRecordService.class);
        service.putExtra("code", result);
        service.putExtra("data", data);
        service.putExtra("path", PuttrRecord.path);
        service.putExtra("width", metrics.widthPixels);
        service.putExtra("height", metrics.heightPixels);
        service.putExtra("dpi", metrics.densityDpi);
        try { startForegroundService(service); }
        catch (Exception e) { PuttrRecord.send("failed|" + (e.getMessage() != null ? e.getMessage() : "The recording could not start.")); }
        finish();
    }
}
