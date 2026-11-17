// Recording a round on camera, as puttr-app's VideoCapture does it: the phone joins an Agora
// channel with its camera on, and the API's cloud recorder records that channel.
//
//   POST /video/agora/token {channelName, uid}                       -> {vidToken}
//   (join the channel: Assets/Plugins/iOS/PuttrCapture.m)
//   POST /video/saveGame/ {roomName, userGameId[, segment]}          ties the recording to the game
//   POST /video/agora/startRecording {channelName, uid, width, height}   (tried twice)
//   ... the round ...
//   POST /video/agora/stopRecording {channelName, uid}               three seconds after it ends
//
// A putt counts only while Ready: on camera, published, recorder running, app in front.
// iPhone and Android: elsewhere Supported is false and a round cannot be entered.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PuttrApp
{
    public sealed class Capture : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PuttrCapture_Start(string appId, string token, string channel, int uid, string target, int lab);
        [DllImport("__Internal")] static extern void PuttrCapture_SwitchCamera();
        [DllImport("__Internal")] static extern void PuttrCapture_Snapshot();
        [DllImport("__Internal")] static extern void PuttrCapture_Border(float r, float g, float b, float w);
        [DllImport("__Internal")] static extern void PuttrCapture_Place(float x, float y, float w, float h);
        [DllImport("__Internal")] static extern void PuttrCapture_Stop();
        [DllImport("__Internal")] static extern void PuttrCapture_PlaceRemote(float x, float y, float w, float h);
        [DllImport("__Internal")] static extern void PuttrCapture_Mute(int muted);
        public static bool Supported => true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        // Android: Plugins/Android/PuttrApp.androidlib PuttrCapture.java, the same messages as the iPhone plugin.
        static AndroidJavaClass _java;
        static AndroidJavaClass Java => _java ?? (_java = new AndroidJavaClass("co.puttr.app2.PuttrCapture"));
        static void PuttrCapture_Start(string appId, string token, string channel, int uid, string target, int lab)
        {
            string[] wanted = lab != 0 ? new[] { "android.permission.CAMERA" } : new[] { "android.permission.CAMERA", "android.permission.RECORD_AUDIO" };
            Puttr.AndroidPermissions.Ask(wanted, () =>
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    Java.CallStatic("start", activity, appId, token, channel, uid, target, lab != 0);
            });
        }
        static void PuttrCapture_SwitchCamera() => Java.CallStatic("switchCamera");
        static void PuttrCapture_Snapshot() => Java.CallStatic("snapshot");
        static void PuttrCapture_Border(float r, float g, float b, float w) => Java.CallStatic("border", r, g, b, w);
        static void PuttrCapture_Place(float x, float y, float w, float h) => Java.CallStatic("place", x, y, w, h);
        static void PuttrCapture_Stop() => Java.CallStatic("stop");
        static void PuttrCapture_PlaceRemote(float x, float y, float w, float h) => Java.CallStatic("placeRemote", x, y, w, h);
        static void PuttrCapture_Mute(int muted) => Java.CallStatic("mute", muted != 0);
        public static bool Supported => true;
#else
        static void PuttrCapture_Start(string appId, string token, string channel, int uid, string target, int lab) { }
        static void PuttrCapture_SwitchCamera() { }
        static void PuttrCapture_Snapshot() { }
        static void PuttrCapture_Border(float r, float g, float b, float w) { }
        static void PuttrCapture_Place(float x, float y, float w, float h) { }
        static void PuttrCapture_Stop() { }
        static void PuttrCapture_PlaceRemote(float x, float y, float w, float h) { }
        static void PuttrCapture_Mute(int muted) { }
        public static bool Supported => false;
#endif

        public enum State { Off, Starting, Recording, Failed }

        const string ObjectName = "PuttrCapture";
        static Capture _one;

        public static Capture One
        {
            get
            {
                if (_one != null) return _one;
                var go = new GameObject(ObjectName);
                DontDestroyOnLoad(go);
                return _one = go.AddComponent<Capture>();
            }
        }

        public State Now { get; private set; } = State.Off;
        /// <summary>Why it is not recording, in words for the Player. Null when all is well.</summary>
        public string Problem { get; private set; }
        public bool Ready => Now == State.Recording && _joined && _published && !_background;
        public event Action Changed;

        string _channel;
        int _uid, _width = 540, _height = 960, _run;
        bool _joined, _published, _background;

        /// <summary>The Agora channel a game records on: "TOURNAMENT_123_0", "CENTERCUT_456".</summary>
        public static string Channel(string gameType, string userGameId, int? segment) =>
            gameType + "_" + userGameId + (segment.HasValue ? "_" + segment.Value : "");

        string _labSession;
        /// <summary>When a 3D session's recording started (realtime seconds): each putt is placed on the video from it.</summary>
        public float RecordingStartedAt { get; private set; }
        /// <summary>A 3D session's body pose for the latest still: the app's detector shape {width, height, landmarks}, or null for none.</summary>
        public event Action<object> Pose;

        /// <summary>Films a 3D session on channel LAB_{sessionId}, as puttr-app's LabVideoCapture.</summary>
        public void BeginLab(string sessionId)
        {
            if (!Supported) { Set(State.Failed, "3D sessions need the phone app."); return; }
            _labSession = sessionId;
            _call = false;
            _run++;
            _channel = "LAB_" + sessionId;
            _uid = UnityEngine.Random.Range(1, 10000000);
            _joined = _published = false;
            Set(State.Starting, null);
            StartCoroutine(Run(_run, sessionId, null));
        }

        bool _call, _silent;
        /// <summary>The other Player's Agora uid in a live 1v1 call, or 0 until they join.</summary>
        public int RemoteUid { get; private set; }

        /// <summary>A live 1v1 video call (GameLobby): channel MP_{gameId}, as the numeric profile id; nothing is recorded.
        /// Also a camera-only preview (channel CAMERA_SETUP) for the video drills.</summary>
        public void BeginCall(string channel, int uid, bool sound = true)
        {
            _silent = !sound;
            _labSession = null;
            if (!Supported) { Set(State.Failed, "Video needs the phone app."); return; }
            _call = true;
            _run++;
            _channel = channel;
            _uid = uid;
            RemoteUid = 0;
            _joined = _published = false;
            Set(State.Starting, null);
            StartCoroutine(Run(_run, null, null));
        }

        public void PlaceRemote(Rect share) => PuttrCapture_PlaceRemote(share.x, share.y, share.width, share.height);
        public void Mute(bool muted) => PuttrCapture_Mute(muted ? 1 : 0);

        public void SwitchCamera() => PuttrCapture_SwitchCamera();
        public void Snapshot() => PuttrCapture_Snapshot();
        public void Border(Color colour, float width) => PuttrCapture_Border(colour.r, colour.g, colour.b, width);

        public void Begin(string gameType, string userGameId, int? segment)
        {
            _labSession = null;
            _call = false;
            if (!Supported) { Set(State.Failed, "Recording needs the phone app."); return; }
            _run++;
            _channel = Channel(gameType, userGameId, segment);
            _uid = UnityEngine.Random.Range(1, 10000000);
            _joined = _published = false;
            Set(State.Starting, null);
            StartCoroutine(Run(_run, userGameId, segment));
        }

        IEnumerator Run(int run, string userGameId, int? segment)
        {
            string token = null, failed = null;
            yield return Api.Post("/video/agora/token", new Dictionary<string, object> { { "channelName", _channel }, { "uid", _uid } },
                data => token = J.Str(data, "vidToken"), why => failed = why, auth: false);
            if (run != _run) yield break;
            if (string.IsNullOrEmpty(token)) { Set(State.Failed, failed == "offline" ? "No connection: recording could not start." : "Recording could not start."); yield break; }

            // A 3D session, or the camera-only preview: picture, no sound (so no microphone prompt).
            PuttrCapture_Start(Config.AgoraAppId, token, _channel, _uid, ObjectName, _labSession != null || (_call && _silent) ? 1 : 0);
            float until = Time.realtimeSinceStartup + 20f;
            while (!_joined && run == _run && Now == State.Starting && Time.realtimeSinceStartup < until) yield return null;
            if (run != _run || Now != State.Starting) yield break;
            if (!_joined) { Set(State.Failed, "The camera could not start. Allow camera access for Puttr in Settings."); yield break; }
            if (_call) { Set(State.Recording, null); yield break; }

            if (_labSession != null)
            {
                // A 3D session is recorded by the lab's own recorder (POST /lab/recording/start), from this moment on.
                RecordingStartedAt = Time.realtimeSinceStartup;
                bool labStarted = false;
                yield return Api.Post("/lab/recording/start", new Dictionary<string, object> { { "sessionId", _labSession }, { "channelName", _channel }, { "uid", _uid }, { "width", 1080 }, { "height", 1920 } },
                    _ => labStarted = true, null);
                if (run != _run) yield break;
                if (labStarted) { Perf.Note("capture: lab recording " + _channel); Set(State.Recording, null); }
                else Set(State.Failed, "Recording could not start. Check your connection, then leave and start the session again.");
                yield break;
            }
            var game = new Dictionary<string, object> { { "roomName", _channel }, { "userGameId", userGameId } };
            if (segment.HasValue) game["segment"] = segment.Value;
            bool saved = false;
            yield return Api.Post("/video/saveGame/", game, _ => saved = true, null, auth: false);
            if (run != _run) yield break;
            if (!saved) { Set(State.Failed, "Recording could not start. Check your connection."); yield break; }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool started = false;
                yield return Api.Post("/video/agora/startRecording", new Dictionary<string, object> { { "channelName", _channel }, { "uid", _uid }, { "width", _width }, { "height", _height } },
                    _ => started = true, null, auth: false);
                if (run != _run) yield break;
                if (started) { Perf.Note("capture: recording " + _channel); Set(State.Recording, null); yield break; }
                if (attempt == 0) yield return new WaitForSecondsRealtime(2f);
            }
            Set(State.Failed, "Recording could not start. Check your connection, then leave and re-open this round.");
        }

        /// <summary>The round is over, or was left: the camera stays on for three seconds more, then the recording is closed.</summary>
        public void End()
        {
            if (Now == State.Off) return;
            bool wasRecording = Now != State.Failed || _joined;
            string channel = _channel, lab = _labSession;
            int uid = _uid;
            _run++;
            Set(State.Off, null);
            PuttrCapture_Place(0, 0, 0, 0);
            if (_call) { _call = false; RemoteUid = 0; PuttrCapture_PlaceRemote(0, 0, 0, 0); PuttrCapture_Stop(); return; }
            if (lab != null) { App.Run(CloseLab(_run, channel, uid, lab)); return; }
            App.Run(Close(_run, channel, uid, wasRecording));
        }

        IEnumerator Close(int run, string channel, int uid, bool recorded)
        {
            yield return new WaitForSecondsRealtime(3f);
            if (run == _run) PuttrCapture_Stop(); // unless another round has taken the camera since
            if (recorded) yield return Api.Post("/video/agora/stopRecording", new Dictionary<string, object> { { "channelName", channel }, { "uid", uid } }, null, null, auth: false);
        }

        // A 3D session keeps filming 2.5 s after the last putt (LabVideoCapture's tail), then the lab's recorder is stopped.
        IEnumerator CloseLab(int run, string channel, int uid, string session)
        {
            yield return new WaitForSecondsRealtime(2.5f);
            if (run == _run) PuttrCapture_Stop();
            yield return Api.Post("/lab/recording/stop", new Dictionary<string, object> { { "sessionId", session }, { "channelName", channel }, { "uid", uid } }, null, null);
            _labSession = null;
        }

        /// <summary>Where the Player sees themselves, as shares of the screen from the top left.</summary>
        public void Place(Rect share) => PuttrCapture_Place(share.x, share.y, share.width, share.height);

        void Set(State state, string problem)
        {
            Now = state;
            Problem = problem;
            if (problem != null) Perf.Note("capture: " + problem);
            Changed?.Invoke();
        }

        void OnApplicationPause(bool paused) { _background = paused; Changed?.Invoke(); }

        // From the plugin.
        void OnCapture(string message)
        {
            string[] parts = message.Split('|');
            switch (parts[0])
            {
                case "joined": _joined = true; break;
                case "left": _joined = false; break;
                case "published": _published = true; break;
                case "size":
                    if (parts.Length >= 3 && int.TryParse(parts[1], out int w) && int.TryParse(parts[2], out int h) && w > 0 && h > 0) { _width = w; _height = h; }
                    break;
                case "video":
                    // 1 capturing, 2 encoding: frames are flowing even if the first-frame call was missed. 3 failed.
                    if (parts.Length >= 2 && (parts[1] == "1" || parts[1] == "2")) _published = true;
                    if (parts.Length >= 2 && parts[1] == "3" && Now != State.Off) Set(State.Failed, "Camera unavailable. Close any other app using the camera, then leave and re-open this round.");
                    break;
                case "remote": RemoteUid = parts.Length > 1 && int.TryParse(parts[1], out int ru) ? ru : 0; break;
                case "error": Perf.Note("capture: agora error " + (parts.Length > 1 ? parts[1] : "?")); break;
                case "pose":
                    string json = message.Length > 5 ? message.Substring(5) : "none";
                    Pose?.Invoke(json == "none" ? null : J.Parse(json));
                    return;
            }
            Changed?.Invoke();
        }

        /// <summary>What to tell the Player while a round waits for the camera. Null when putts count.</summary>
        public string Waiting()
        {
            if (Now == State.Off) return null;
            if (Now == State.Failed) return Problem;
            if (_background) return "Camera paused. Keep Puttr open for the whole round.";
            if (!Ready) return "Starting camera. Your putts will count as soon as recording is running.";
            return null;
        }
    }
}
