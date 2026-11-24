// The camera for a recorded round or a 3D session on Android: Plugins/iOS/PuttrCapture.m's twin,
// through the same Agora SDK the React Native app ships (agora-special-full 4.5.3.70).
//
// Told to Unity with UnitySendMessage(target, "OnCapture", ...), exactly as on iOS:
//   joined | left | published | video|<state>|<reason> | size|<w>|<h> | error|<code>
//   pose|<json> | pose|none
//
// The framing check's body pose comes from Google's ML Kit pose detector (MediaPipe's BlazePose
// underneath, the model puttr-app runs): its landmarks have MediaPipe's names and an in-frame
// likelihood, which is what the app's framing check reads.
//
// A 3D session's preview fills the screen above the app's bottom bar, and the guide the Player lines
// up with (the dashed centre line, the live skeleton, the status frame and the coach's line) is drawn
// over it here, as on iOS: Unity draws under this view. guide(...) sets it.
package co.puttr.app2;

import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageManager;
import android.graphics.Canvas;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.text.Layout;
import android.text.StaticLayout;
import android.text.TextPaint;
import android.graphics.Color;
import android.graphics.Outline;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.view.TextureView;
import android.view.View;
import android.view.ViewGroup;
import android.view.ViewOutlineProvider;
import android.widget.FrameLayout;

import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.pose.Pose;
import com.google.mlkit.vision.pose.PoseDetection;
import com.google.mlkit.vision.pose.PoseDetector;
import com.google.mlkit.vision.pose.PoseLandmark;
import com.google.mlkit.vision.pose.defaults.PoseDetectorOptions;

import org.json.JSONObject;

import java.util.HashMap;
import java.util.Map;

import java.io.File;
import java.lang.reflect.Method;

import io.agora.rtc2.ChannelMediaOptions;
import io.agora.rtc2.Constants;
import io.agora.rtc2.IRtcEngineEventHandler;
import io.agora.rtc2.RtcEngine;
import io.agora.rtc2.RtcEngineConfig;
import io.agora.rtc2.video.VideoCanvas;
import io.agora.rtc2.video.VideoEncoderConfiguration;

public final class PuttrCapture {
    static final Handler main = new Handler(Looper.getMainLooper());
    static RtcEngine engine;
    static FrameLayout frame;
    static TextureView view;
    static GradientDrawable border;
    static String target = "";
    static Activity activity;
    static PoseDetector detector;
    static float[] place = { 0, 0, 0, 0 };
    static Guide guide;
    static boolean lab, silent, front = true;
    // The other Player in a live 1v1 (GameLobby's full-screen RtcSurfaceView), under the Player's own preview.
    static FrameLayout remote;
    static TextureView remoteView;
    static float[] remotePlace = { 0, 0, 0, 0 };

    // Unity's UnitySendMessage, found at run time so this module needs nothing of Unity's to build.
    static void send(String message) {
        if (target == null || target.isEmpty()) return;
        try {
            Class<?> player = Class.forName("com.unity3d.player.UnityPlayer");
            Method m = player.getMethod("UnitySendMessage", String.class, String.class, String.class);
            m.invoke(null, target, "OnCapture", message);
        } catch (Exception ignored) { }
    }

    static final IRtcEngineEventHandler events = new IRtcEngineEventHandler() {
        @Override public void onJoinChannelSuccess(String channel, int uid, int elapsed) { send("joined"); }
        @Override public void onUserJoined(int uid, int elapsed) {
            main.post(() -> { if (engine != null && remoteView != null) engine.setupRemoteVideo(new VideoCanvas(remoteView, VideoCanvas.RENDER_MODE_HIDDEN, uid)); });
            send("remote|" + uid);
        }
        @Override public void onUserOffline(int uid, int reason) { send("remote|0"); }
        @Override public void onLeaveChannel(RtcStats stats) { send("left"); }
        @Override public void onError(int err) { send("error|" + err); }
        @Override public void onFirstLocalVideoFramePublished(Constants.VideoSourceType source, int elapsed) { send("published"); }
        @Override public void onLocalVideoStateChanged(Constants.VideoSourceType source, int state, int reason) { send("video|" + state + "|" + reason); }
        @Override public void onVideoSizeChanged(Constants.VideoSourceType source, int uid, int width, int height, int rotation) {
            if (uid != 0) return;
            main.post(() -> { if (guide != null && guide.landmarks == null && width > 0 && height > 0) { guide.aspect = (float) width / height; guide.invalidate(); } });
            send("size|" + width + "|" + height);
        }
        @Override public void onSnapshotTaken(int uid, String filePath, int width, int height, int errCode) {
            if (errCode != 0) { send("pose|none"); return; }
            detect(filePath, width, height);
        }
    };

    public static void start(Activity a, String appId, String token, String channel, int uid, String unityTarget, boolean labMode, boolean noSound) {
        main.post(() -> {
            lab = labMode;
            silent = noSound;   // a round: picture only, no microphone
            activity = a;
            target = unityTarget;
            try {
                if (engine == null) {
                    RtcEngineConfig config = new RtcEngineConfig();
                    config.mContext = a.getApplicationContext();
                    config.mAppId = appId;
                    config.mEventHandler = events;
                    engine = RtcEngine.create(config);
                }
            } catch (Exception e) { send("error|create"); return; }
            engine.enableVideo();
            if (lab) {
                // As LabVideoCapture: portrait 1080x1920, 30 fps, 4 Mbps (at least 1.5), frame rate kept over quality; no sound.
                VideoEncoderConfiguration config = new VideoEncoderConfiguration(new VideoEncoderConfiguration.VideoDimensions(1080, 1920),
                    VideoEncoderConfiguration.FRAME_RATE.FRAME_RATE_FPS_30, 4000, VideoEncoderConfiguration.ORIENTATION_MODE.ORIENTATION_MODE_FIXED_PORTRAIT);
                config.minBitrate = 1500;
                config.degradationPrefer = VideoEncoderConfiguration.DEGRADATION_PREFERENCE.MAINTAIN_FRAMERATE;
                engine.setVideoEncoderConfiguration(config);
                engine.disableAudio();
            } else if (silent) engine.disableAudio();
            else engine.enableAudio();
            engine.setChannelProfile(Constants.CHANNEL_PROFILE_COMMUNICATION);
            engine.leaveChannel(); // a round left a moment ago may not have let go yet

            if (frame == null) {
                frame = new FrameLayout(a);
                view = new TextureView(a);
                frame.addView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
                float radius = 12 * a.getResources().getDisplayMetrics().density;
                frame.setOutlineProvider(new ViewOutlineProvider() {
                    @Override public void getOutline(View v, Outline o) { o.setRoundRect(0, 0, v.getWidth(), v.getHeight(), radius); }
                });
                frame.setClipToOutline(true);
                border = new GradientDrawable();
                border.setCornerRadius(radius);
                border.setColor(Color.TRANSPARENT);
                border.setStroke(Math.round(a.getResources().getDisplayMetrics().density), Color.argb(64, 255, 255, 255));
                frame.setForeground(border);
                frame.setClickable(false);
                frame.setFocusable(false);
                frame.setVisibility(View.GONE);
                guide = new Guide(a);
                frame.addView(guide, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            }
            // A round's picture is a small rounded tile; a 3D session's fills the screen, with the guide over it.
            front = true; // Agora opens the front lens first
            frame.setClipToOutline(!lab);
            frame.setForeground(lab ? null : border);
            frame.setBackgroundColor(Color.BLACK);
            guide.setVisibility(lab ? View.VISIBLE : View.GONE);
            guide.landmarks = null;
            guide.aspect = 0;
            guide.mirror = true;
            guide.line = "";
            guide.status = 0;
            guide.progress = 0;
            guide.confirmed = false;
            guide.invalidate();
            if (frame.getParent() == null) a.addContentView(frame, new FrameLayout.LayoutParams(1, 1));
            frame.bringToFront();
            layout();

            engine.setupLocalVideo(new VideoCanvas(view, lab ? VideoCanvas.RENDER_MODE_FIT : VideoCanvas.RENDER_MODE_HIDDEN, 0));
            engine.startPreview();

            ChannelMediaOptions options = new ChannelMediaOptions();
            options.clientRoleType = Constants.CLIENT_ROLE_BROADCASTER;
            options.publishCameraTrack = true;
            options.publishMicrophoneTrack = !lab && !silent;
            int result = engine.joinChannel(token, channel, uid, options);
            if (result != 0) send("error|" + result);
        });
    }

    /** Where the Player sees themselves: a share of the screen from the top left. Zero width hides it. */
    public static void place(float x, float y, float w, float h) {
        main.post(() -> { place = new float[] { x, y, w, h }; layout(); });
    }

    static void layout() {
        if (frame == null || frame.getParent() == null) return;
        View host = (View) frame.getParent();
        int W = host.getWidth(), H = host.getHeight();
        if (W == 0 || H == 0) { host.post(PuttrCapture::layout); return; }
        boolean hidden = place[2] <= 0 || place[3] <= 0;
        frame.setVisibility(hidden ? View.GONE : View.VISIBLE);
        if (hidden) return;
        FrameLayout.LayoutParams p = new FrameLayout.LayoutParams(Math.round(place[2] * W), Math.round(place[3] * H));
        p.leftMargin = Math.round(place[0] * W);
        p.topMargin = Math.round(place[1] * H);
        frame.setLayoutParams(p);
    }

    /** The other Player's video: a share of the screen; zero width hides it. */
    public static void placeRemote(float x, float y, float w, float h) {
        main.post(() -> {
            remotePlace = new float[] { x, y, w, h };
            if (activity == null) return;
            if (remote == null) {
                remote = new FrameLayout(activity);
                remote.setBackgroundColor(Color.BLACK);
                remoteView = new TextureView(activity);
                remote.addView(remoteView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
                remote.setClickable(false);
                remote.setFocusable(false);
            }
            if (remote.getParent() == null) activity.addContentView(remote, new FrameLayout.LayoutParams(1, 1));
            if (frame != null) frame.bringToFront();
            layoutRemote();
        });
    }

    static void layoutRemote() {
        if (remote == null || remote.getParent() == null) return;
        View host = (View) remote.getParent();
        int W = host.getWidth(), H = host.getHeight();
        if (W == 0 || H == 0) { host.post(PuttrCapture::layoutRemote); return; }
        boolean hidden = remotePlace[2] <= 0 || remotePlace[3] <= 0;
        remote.setVisibility(hidden ? View.GONE : View.VISIBLE);
        if (hidden) return;
        FrameLayout.LayoutParams p = new FrameLayout.LayoutParams(Math.round(remotePlace[2] * W), Math.round(remotePlace[3] * H));
        p.leftMargin = Math.round(remotePlace[0] * W);
        p.topMargin = Math.round(remotePlace[1] * H);
        remote.setLayoutParams(p);
    }

    public static void mute(boolean muted) { main.post(() -> { if (engine != null) engine.muteLocalAudioStream(muted); }); }

    public static void switchCamera() {
        main.post(() -> {
            if (engine != null && engine.switchCamera() == 0) front = !front;
            if (guide != null) { guide.mirror = front; guide.landmarks = null; guide.invalidate(); }
        });
    }

    /** The frame around the preview: the framing check's colour, or none. */
    public static void border(float r, float g, float b, float width) {
        main.post(() -> {
            if (border == null || activity == null || lab) return; // a 3D session's frame is the guide's
            float d = activity.getResources().getDisplayMetrics().density;
            border.setStroke(Math.max(1, Math.round(width * d)), Color.rgb(Math.round(r * 255), Math.round(g * 255), Math.round(b * 255)));
        });
    }

    /** A still of what the camera sees; its body pose follows as pose|... */
    public static void snapshot() {
        main.post(() -> {
            if (engine == null || activity == null) { send("pose|none"); return; }
            File file = new File(activity.getCacheDir(), "puttr-framing.jpg");
            if (engine.takeSnapshot(0, file.getAbsolutePath()) != 0) send("pose|none");
        });
    }

    static final String[][] NAMES = {
        { "nose", "" + PoseLandmark.NOSE },
        { "left_eye", "" + PoseLandmark.LEFT_EYE }, { "right_eye", "" + PoseLandmark.RIGHT_EYE },
        { "left_ear", "" + PoseLandmark.LEFT_EAR }, { "right_ear", "" + PoseLandmark.RIGHT_EAR },
        { "left_shoulder", "" + PoseLandmark.LEFT_SHOULDER }, { "right_shoulder", "" + PoseLandmark.RIGHT_SHOULDER },
        { "left_elbow", "" + PoseLandmark.LEFT_ELBOW }, { "right_elbow", "" + PoseLandmark.RIGHT_ELBOW },
        { "left_wrist", "" + PoseLandmark.LEFT_WRIST }, { "right_wrist", "" + PoseLandmark.RIGHT_WRIST },
        { "left_hip", "" + PoseLandmark.LEFT_HIP }, { "right_hip", "" + PoseLandmark.RIGHT_HIP },
        { "left_knee", "" + PoseLandmark.LEFT_KNEE }, { "right_knee", "" + PoseLandmark.RIGHT_KNEE },
        { "left_ankle", "" + PoseLandmark.LEFT_ANKLE }, { "right_ankle", "" + PoseLandmark.RIGHT_ANKLE },
    };

    // Joint name -> {x, y, inFrame}, x and y 0 to 1 from the top left: what the app's detector reports.
    static void detect(String path, int width, int height) {
        try {
            if (detector == null) detector = PoseDetection.getClient(new PoseDetectorOptions.Builder().setDetectorMode(PoseDetectorOptions.SINGLE_IMAGE_MODE).build());
            InputImage image = InputImage.fromFilePath(activity, Uri.fromFile(new File(path)));
            float w = image.getWidth(), h = image.getHeight();
            detector.process(image).addOnSuccessListener((Pose pose) -> {
                try {
                    if (pose.getAllPoseLandmarks().isEmpty()) { main.post(() -> { if (guide != null) { guide.landmarks = null; guide.invalidate(); } }); send("pose|none"); return; }
                    JSONObject landmarks = new JSONObject();
                    Map<String, float[]> seen = new HashMap<>();
                    for (String[] n : NAMES) {
                        PoseLandmark l = pose.getPoseLandmark(Integer.parseInt(n[1]));
                        if (l == null) continue;
                        JSONObject p = new JSONObject();
                        p.put("x", l.getPosition().x / w);
                        p.put("y", l.getPosition().y / h);
                        p.put("inFrame", l.getInFrameLikelihood());
                        landmarks.put(n[0], p);
                        seen.put(n[0], new float[] { l.getPosition().x / w, l.getPosition().y / h, l.getInFrameLikelihood() });
                    }
                    main.post(() -> {
                        // The skeleton over the picture is what this still found.
                        if (guide == null) return;
                        guide.landmarks = seen;
                        if (w > 0 && h > 0) guide.aspect = w / h;
                        guide.mirror = front;
                        guide.invalidate();
                    });
                    JSONObject all = new JSONObject();
                    all.put("width", width);
                    all.put("height", height);
                    all.put("landmarks", landmarks);
                    send("pose|" + all);
                } catch (Exception e) { send("pose|none"); }
            }).addOnFailureListener(e -> send("pose|none"));
        } catch (Exception e) { send("pose|none"); }
    }

    /** 0 no camera on this device, 1 a camera. (Permission is asked by Unity before start.) */
    public static int hasCamera(Activity a) {
        try { return a.getPackageManager().hasSystemFeature(PackageManager.FEATURE_CAMERA_ANY) ? 1 : 0; }
        catch (Exception e) { return 0; }
    }

    /** The 3D session's guide: the coach's line (empty for none), the frame colour (a 0 for none), hold progress, confirmed. */
    public static void guide(String line, float r, float g, float b, float a, float progress, boolean confirmed) {
        main.post(() -> {
            if (guide == null) return;
            guide.line = line == null ? "" : line;
            guide.status = a > 0 ? Color.rgb(Math.round(r * 255), Math.round(g * 255), Math.round(b * 255)) : 0;
            guide.progress = progress;
            guide.confirmed = confirmed;
            guide.invalidate();
        });
    }

    static final String[][] BONES = {
        { "left_shoulder", "right_shoulder" }, { "left_shoulder", "left_hip" }, { "right_shoulder", "right_hip" }, { "left_hip", "right_hip" },
        { "left_shoulder", "left_elbow" }, { "left_elbow", "left_wrist" }, { "right_shoulder", "right_elbow" }, { "right_elbow", "right_wrist" },
        { "left_hip", "left_knee" }, { "left_knee", "left_ankle" }, { "right_hip", "right_knee" }, { "right_knee", "right_ankle" },
        { "nose", "left_eye" }, { "nose", "right_eye" }, { "left_eye", "left_ear" }, { "right_eye", "right_ear" },
    };

    /** The framing guide over a 3D session's picture (CoachGame.js's overlay). */
    static final class Guide extends View {
        Map<String, float[]> landmarks;   // joint -> {x, y, inFrame}, 0 to 1 of the still
        float aspect;                     // the picture's width / height, 0 until known
        boolean mirror, confirmed;
        int status;                       // the frame colour, 0 for none
        float progress;
        String line = "";
        final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        final TextPaint text = new TextPaint(Paint.ANTI_ALIAS_FLAG);
        final float d;
        static final int GREEN = Color.rgb(0x2D, 0x8C, 0x3C), RED = Color.rgb(0xFF, 0x52, 0x52);

        Guide(Context c) {
            super(c);
            d = c.getResources().getDisplayMetrics().density;
            text.setColor(Color.WHITE);
            text.setTextSize(14 * d);
            text.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
            setWillNotDraw(false);
            setClickable(false);
            setFocusable(false);
        }

        RectF picture() {
            float W = getWidth(), H = getHeight();
            float a = aspect > 0 ? aspect : (W > H ? 16f / 9f : 9f / 16f);
            float w = W, h = w / a;
            if (h > H) { h = H; w = h * a; }
            return new RectF((W - w) / 2, (H - h) / 2, (W + w) / 2, (H + h) / 2);
        }

        float[] at(RectF p, float[] l) {
            float x = mirror ? 1 - l[0] : l[0];
            return new float[] { p.left + x * p.width(), p.top + l[1] * p.height() };
        }

        @Override protected void onDraw(Canvas c) {
            if (getWidth() == 0 || getHeight() == 0) return;
            RectF p = picture();
            paint.setStyle(Paint.Style.STROKE);
            paint.setStrokeWidth(2 * d);
            paint.setColor(GREEN);
            paint.setPathEffect(new DashPathEffect(new float[] { 8 * d, 8 * d }, 0));
            c.drawLine(p.centerX(), p.top, p.centerX(), p.bottom, paint);
            paint.setPathEffect(null);
            if (status != 0 && !confirmed) {
                paint.setColor(status);
                paint.setAlpha(230);
                paint.setStrokeWidth(3 * d);
                c.drawRect(p.left + 1.5f * d, p.top + 1.5f * d, p.right - 1.5f * d, p.bottom - 1.5f * d, paint);
            }
            Map<String, float[]> lm = landmarks;
            if (lm != null) {
                paint.setStrokeWidth(2 * d);
                for (String[] bone : BONES) {
                    float[] a = lm.get(bone[0]), b = lm.get(bone[1]);
                    if (a == null || b == null || a[2] < 0.15f || b[2] < 0.15f) continue;
                    boolean strong = a[2] >= 0.5f && b[2] >= 0.5f;
                    paint.setColor(strong ? GREEN : RED);
                    paint.setAlpha(strong ? 230 : 128);
                    float[] pa = at(p, a), pb = at(p, b);
                    c.drawLine(pa[0], pa[1], pb[0], pb[1], paint);
                }
                for (float[] l : lm.values()) {
                    if (l[2] < 0.15f) continue;
                    float[] q = at(p, l);
                    paint.setStyle(Paint.Style.FILL);
                    paint.setColor(l[2] >= 0.5f ? GREEN : RED);
                    c.drawCircle(q[0], q[1], 3.5f * d, paint);
                    paint.setStyle(Paint.Style.STROKE);
                    paint.setStrokeWidth(0.75f * d);
                    paint.setColor(Color.argb(150, 255, 255, 255));
                    c.drawCircle(q[0], q[1], 3.5f * d, paint);
                    paint.setStrokeWidth(2 * d);
                }
            }
            if (line == null || line.isEmpty()) return;
            // The coach's line in a card at the top, with the hold-still bar under it.
            int maxWidth = Math.round(Math.min(300 * d, getWidth() - 48 * d));
            StaticLayout layout = StaticLayout.Builder.obtain(line, 0, line.length(), text, maxWidth).setAlignment(Layout.Alignment.ALIGN_CENTER).build();
            float textWidth = 0;
            for (int i = 0; i < layout.getLineCount(); i++) textWidth = Math.max(textWidth, layout.getLineWidth(i));
            boolean bar = progress > 0 && !confirmed;
            float w = Math.min(maxWidth, Math.max(textWidth, bar ? 140 * d : 0)) + 28 * d;
            float h = layout.getHeight() + 16 * d + (bar ? 11 * d : 0);
            float top = 14 * d + (getRootWindowInsets() != null ? getRootWindowInsets().getSystemWindowInsetTop() : 0);
            RectF card = new RectF((getWidth() - w) / 2, top, (getWidth() + w) / 2, top + h);
            paint.setStyle(Paint.Style.FILL);
            paint.setColor(Color.argb(178, 0, 0, 0));
            c.drawRoundRect(card, 12 * d, 12 * d, paint);
            paint.setStyle(Paint.Style.STROKE);
            paint.setStrokeWidth(1 * d);
            paint.setColor(confirmed ? Color.argb(128, 0x2D, 0x8C, 0x3C) : Color.argb(26, 255, 255, 255));
            c.drawRoundRect(card, 12 * d, 12 * d, paint);
            c.save();
            c.translate(card.left + 14 * d + (w - 28 * d - maxWidth) / 2, card.top + 8 * d);
            layout.draw(c);
            c.restore();
            if (bar) {
                float y = card.top + 8 * d + layout.getHeight() + 7 * d;
                paint.setStyle(Paint.Style.FILL);
                paint.setColor(Color.argb(38, 255, 255, 255));
                c.drawRect(card.left + 14 * d, y, card.right - 14 * d, y + 4 * d, paint);
                paint.setColor(GREEN);
                c.drawRect(card.left + 14 * d, y, card.left + 14 * d + (w - 28 * d) * Math.max(0, Math.min(1, progress)), y + 4 * d, paint);
            }
        }
    }

    public static void stop() {
        main.post(() -> {
            if (engine != null) { engine.leaveChannel(); engine.stopPreview(); }
            if (guide != null) { guide.landmarks = null; guide.line = ""; }
            if (frame != null) { frame.setVisibility(View.GONE); if (frame.getParent() instanceof ViewGroup) ((ViewGroup) frame.getParent()).removeView(frame); }
            if (remote != null) { remote.setVisibility(View.GONE); if (remote.getParent() instanceof ViewGroup) ((ViewGroup) remote.getParent()).removeView(remote); }
        });
    }
}
