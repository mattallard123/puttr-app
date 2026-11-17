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
package co.puttr.app2;

import android.app.Activity;
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
        @Override public void onVideoSizeChanged(Constants.VideoSourceType source, int uid, int width, int height, int rotation) { if (uid == 0) send("size|" + width + "|" + height); }
        @Override public void onSnapshotTaken(int uid, String filePath, int width, int height, int errCode) {
            if (errCode != 0) { send("pose|none"); return; }
            detect(filePath, width, height);
        }
    };

    public static void start(Activity a, String appId, String token, String channel, int uid, String unityTarget, boolean lab) {
        main.post(() -> {
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
            } else engine.enableAudio();
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
            }
            if (frame.getParent() == null) a.addContentView(frame, new FrameLayout.LayoutParams(1, 1));
            frame.bringToFront();
            layout();

            engine.setupLocalVideo(new VideoCanvas(view, lab ? VideoCanvas.RENDER_MODE_FIT : VideoCanvas.RENDER_MODE_HIDDEN, 0));
            engine.startPreview();

            ChannelMediaOptions options = new ChannelMediaOptions();
            options.clientRoleType = Constants.CLIENT_ROLE_BROADCASTER;
            options.publishCameraTrack = true;
            options.publishMicrophoneTrack = !lab;
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

    public static void switchCamera() { main.post(() -> { if (engine != null) engine.switchCamera(); }); }

    /** The frame around the preview: the framing check's colour, or none. */
    public static void border(float r, float g, float b, float width) {
        main.post(() -> {
            if (border == null || activity == null) return;
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
                    if (pose.getAllPoseLandmarks().isEmpty()) { send("pose|none"); return; }
                    JSONObject landmarks = new JSONObject();
                    for (String[] n : NAMES) {
                        PoseLandmark l = pose.getPoseLandmark(Integer.parseInt(n[1]));
                        if (l == null) continue;
                        JSONObject p = new JSONObject();
                        p.put("x", l.getPosition().x / w);
                        p.put("y", l.getPosition().y / h);
                        p.put("inFrame", l.getInFrameLikelihood());
                        landmarks.put(n[0], p);
                    }
                    JSONObject all = new JSONObject();
                    all.put("width", width);
                    all.put("height", height);
                    all.put("landmarks", landmarks);
                    send("pose|" + all);
                } catch (Exception e) { send("pose|none"); }
            }).addOnFailureListener(e -> send("pose|none"));
        } catch (Exception e) { send("pose|none"); }
    }

    public static void stop() {
        main.post(() -> {
            if (engine != null) { engine.leaveChannel(); engine.stopPreview(); }
            if (frame != null) { frame.setVisibility(View.GONE); if (frame.getParent() instanceof ViewGroup) ((ViewGroup) frame.getParent()).removeView(frame); }
            if (remote != null) { remote.setVisibility(View.GONE); if (remote.getParent() instanceof ViewGroup) ((ViewGroup) remote.getParent()).removeView(remote); }
        });
    }
}
