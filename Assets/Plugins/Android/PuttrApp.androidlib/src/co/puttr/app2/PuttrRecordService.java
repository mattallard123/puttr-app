// The lesson recording itself: a foreground service (Android requires one for a MediaProjection) that mirrors the
// screen into a MediaRecorder with the microphone, H.264 and AAC in an MP4. It goes to the foreground before it
// takes the projection (Android 14 refuses the other order), and it finishes the file whether the coach taps
// Stop in the app or stops it from the system's own recording chip.
package co.puttr.app2;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.MediaRecorder;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;

import java.io.File;

public final class PuttrRecordService extends Service {
    static final String CHANNEL = "puttr_lesson_recording";
    static final int NOTIFICATION = 4032;
    static PuttrRecordService running;

    MediaProjection projection;
    VirtualDisplay display;
    MediaRecorder recorder;
    String path;
    boolean started, ended;

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        foreground();
        try {
            begin(intent);
        } catch (Exception e) {
            ended = true;
            release();
            if (path != null) new File(path).delete();
            PuttrRecord.recording = false;
            running = null;
            PuttrRecord.send("failed|" + (e.getMessage() != null ? e.getMessage() : "The recording could not start."));
            stopForeground(true);
            stopSelf();
        }
        return START_NOT_STICKY;
    }

    void foreground() {
        NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (manager != null) manager.createNotificationChannel(new NotificationChannel(CHANNEL, "Lesson recording", NotificationManager.IMPORTANCE_LOW));
        Notification notification = new Notification.Builder(this, CHANNEL)
            .setContentTitle("Recording your lesson")
            .setContentText("Tap Stop in Puttr when you are done.")
            .setSmallIcon(getApplicationInfo().icon)
            .setOngoing(true)
            .build();
        if (Build.VERSION.SDK_INT >= 29) startForeground(NOTIFICATION, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
        else startForeground(NOTIFICATION, notification);
    }

    @SuppressWarnings("deprecation")
    void begin(Intent intent) throws Exception {
        if (intent == null) throw new IllegalStateException("The recording could not start.");
        int code = intent.getIntExtra("code", 0);
        Intent grant = intent.getParcelableExtra("data");
        path = intent.getStringExtra("path");
        int width = intent.getIntExtra("width", 1080), height = intent.getIntExtra("height", 1920), dpi = intent.getIntExtra("dpi", 420);
        if (grant == null || path == null || path.isEmpty()) throw new IllegalStateException("The recording could not start.");
        new File(path).delete();

        // At most 1280 on the long side, in steps of 16 the encoders all take: plenty for a narrated review, and a
        // file small enough to upload from a phone.
        float scale = Math.min(1f, 1280f / Math.max(width, height));
        int w = Math.max(16, Math.round(width * scale / 16f) * 16), h = Math.max(16, Math.round(height * scale / 16f) * 16);

        recorder = Build.VERSION.SDK_INT >= 31 ? new MediaRecorder(this) : new MediaRecorder();
        recorder.setAudioSource(MediaRecorder.AudioSource.MIC);
        recorder.setVideoSource(MediaRecorder.VideoSource.SURFACE);
        recorder.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4);
        recorder.setOutputFile(path);
        recorder.setVideoEncoder(MediaRecorder.VideoEncoder.H264);
        recorder.setAudioEncoder(MediaRecorder.AudioEncoder.AAC);
        recorder.setAudioEncodingBitRate(128000);
        recorder.setAudioSamplingRate(44100);
        recorder.setVideoEncodingBitRate(5_000_000);
        recorder.setVideoFrameRate(30);
        recorder.setVideoSize(w, h);
        recorder.prepare();

        MediaProjectionManager manager = (MediaProjectionManager) getSystemService(MEDIA_PROJECTION_SERVICE);
        projection = manager.getMediaProjection(code, grant);
        if (projection == null) throw new IllegalStateException("Android did not allow the recording.");
        // Android 14 wants the callback before the display; it also fires when the coach stops from the system chip.
        projection.registerCallback(new MediaProjection.Callback() {
            @Override public void onStop() { end(false); }
        }, new Handler(Looper.getMainLooper()));
        display = projection.createVirtualDisplay("PuttrLesson", w, h, dpi, DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR, recorder.getSurface(), null, null);
        recorder.start();
        started = true;
        running = this;
        PuttrRecord.recording = true;
        PuttrRecord.send("started");
    }

    /** Ends the running take; false when nothing was recording. */
    static boolean finish(boolean discard) {
        PuttrRecordService service = running;
        if (service == null) return false;
        service.end(discard);
        return true;
    }

    void end(boolean discard) {
        if (ended) return;
        ended = true;
        boolean ok = false;
        try {
            if (started && recorder != null) { recorder.stop(); ok = true; }
        } catch (RuntimeException e) {
            ok = false; // stop() throws when no frame was ever written
        }
        release();
        running = null;
        PuttrRecord.recording = false;
        File file = path != null ? new File(path) : null;
        if (discard) {
            if (file != null) file.delete();
            PuttrRecord.send("cancelled");
        } else if (ok && file != null && file.length() > 0) {
            PuttrRecord.send("stopped|" + path);
        } else {
            if (file != null) file.delete();
            PuttrRecord.send("failed|The recording could not be saved.");
        }
        stopForeground(true);
        stopSelf();
    }

    void release() {
        try { if (recorder != null) recorder.release(); } catch (Exception ignored) { }
        recorder = null;
        try { if (display != null) display.release(); } catch (Exception ignored) { }
        display = null;
        MediaProjection p = projection;
        projection = null;
        try { if (p != null) p.stop(); } catch (Exception ignored) { }
    }

    @Override public void onDestroy() {
        // The system ended the service mid-take: keep what was written.
        if (!ended && started) end(false);
        super.onDestroy();
    }
}
