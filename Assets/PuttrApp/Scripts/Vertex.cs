// The Vertex putter sensor (puttr-app components/vertex): find it, put it in live mode, read each
// putt's six packets, and the screens to pair it (VertexSetup.js) and see it working (VertexConfig.js).
//
// Live mode: write [2, 1, 0, 0, 0, leftHand, 0, 0, 0, 0] to fff1, leftHand from byte 5 of fff1 as read.
// If the sensor says [2, x] with x not 1, it has dropped out of live mode: write again (at most every 3 s).
// A putt is six fff2 packets, byte 0 its number 0 to 5; anything out of order or older than 3 s starts again.
// Enabled is remembered on the phone (the app's AsyncStorage VERTEX_ENABLED); nothing goes to the API.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class VertexPutt
    {
        public float BackTime, ForwardTime, Rhythm, Tempo, BackRotation, ForwardRotation, FaceChange, Speed, BackLength, Lie;
        /// <summary>The six packets as the app saves them (sensorData), for the sensor games' saved putts.</summary>
        public string Raw;

        // A signed 16-bit value as the sensor sends it: sign in the high bit, then magnitude.
        static float P(byte hi, byte lo, float res)
        {
            float v = ((hi & 0x7F) * 256 + lo) * res;
            return hi >= 128 ? -v : v;
        }

        /// <summary>parseVertexMetrics. Face change is backswing plus forward rotation (2.8.4 shipped the wrong sign).</summary>
        public static VertexPutt Read(byte[][] packets)
        {
            byte[] p1 = packets[1], p2 = packets[2], p3 = packets[3];
            var v = new VertexPutt
            {
                BackTime = p1[3] * 0.01f,
                ForwardTime = p2[3] * 0.01f,
                BackRotation = P(p1[4], p1[5], 0.1f),
                ForwardRotation = P(p1[14], p1[15], 0.1f),
                Speed = P(p1[6], p1[7], 0.01f),
                BackLength = P(p1[8], p1[9], 1f),
                Lie = P(p3[17], p3[18], 0.1f),
            };
            v.Rhythm = v.ForwardTime > 0 ? v.BackTime / v.ForwardTime : 0;
            v.Tempo = v.BackTime + v.ForwardTime > 0 ? 60f / (v.BackTime + v.ForwardTime) : 0;
            v.FaceChange = v.BackRotation + v.ForwardRotation;
            return v;
        }

        public static string FaceWords(float f) => Mathf.Abs(f) < 0.5f ? "Square" : f > 0 ? "Open" : "Closed";
        public static Color FaceColour(float f) => Mathf.Abs(f) <= 1 ? Ui.Bright : Mathf.Abs(f) <= 2 ? Ui.Amber : Ui.Red;
    }

    public sealed class Vertex : MonoBehaviour
    {
        public sealed class Sensor { public string Id, Name; public int Battery = -1, Rssi; public string Firmware; }

        const string EnabledKey = "VERTEX_ENABLED";
        static Vertex _one;

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 0) == 1;
            set { PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0); PlayerPrefs.Save(); if (value) One.Keep(); else One.Drop(); }
        }

        public static bool Supported =>
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public static bool Connected { get; private set; }
        public static bool Live { get; private set; }
        public static string Stage { get; private set; } = "idle";
        public static string Error { get; private set; }
        public static int Calibration { get; private set; } = -1;
        public static int Packets { get; private set; }
        public static int Reasserts { get; private set; }
        public static Sensor Current { get; private set; }
        public static readonly List<Sensor> Found = new List<Sensor>();
        public static readonly List<VertexPutt> Putts = new List<VertexPutt>();
        public static event Action Changed;
        public static event Action<VertexPutt> Putt;

        bool _keep, _leftHand, _acked;
        float _lastLive = -10f, _firstPacket, _reconnectAt = -1f, _scanUntil;
        readonly byte[][] _packets = new byte[6][];
        int _next;

        public static Vertex One
        {
            get
            {
                if (_one != null) return _one;
                var go = new GameObject("Vertex");
                DontDestroyOnLoad(go);
                return _one = go.AddComponent<Vertex>();
            }
        }

        /// <summary>At start: an enabled sensor is looked for and kept connected, as Vertex.js does.</summary>
        public static void Boot() { if (Enabled && Supported) One.Keep(); }

        void Keep() { _keep = true; Scan(30f); }
        void Drop() { _keep = false; Native.Scan(false); Native.Disconnect(); }

        public static void Scan(float seconds)
        {
            if (!Supported) { Error = "The Vertex sensor needs the iPhone or Android app."; Changed?.Invoke(); return; }
            Found.Clear();
            Error = null;
            Stage = "searching";
            One._scanUntil = Time.realtimeSinceStartup + seconds;
            Native.Scan(true);
            Changed?.Invoke();
        }

        public static void StopScan() { Native.Scan(false); if (Stage == "searching") Stage = "idle"; Changed?.Invoke(); }

        public static void Connect(Sensor s)
        {
            Current = s;
            Stage = "connecting";
            Error = null;
            Native.Connect(s.Id);
            Changed?.Invoke();
        }

        public static void Disconnect() { One._keep = false; Native.Disconnect(); }

        void Update()
        {
            for (int guard = 0; guard < 100; guard++)
            {
                string line = Native.Poll();
                if (line == null) break;
                Handle(line.Split('\t'));
            }
            float now = Time.realtimeSinceStartup;
            if (Stage == "searching" && now > _scanUntil)
            {
                Native.Scan(false);
                Stage = "idle";
                if (!Connected) Error = "No sensor found. Wake it with a waggle and keep it close.";
                Changed?.Invoke();
            }
            if (_reconnectAt > 0 && now > _reconnectAt) { _reconnectAt = -1f; if (_keep && !Connected) Scan(30f); }
        }

        void Handle(string[] p)
        {
            switch (p[0])
            {
                case "vstate":
                    if (p[1] == "2") Error = "Allow Bluetooth for Puttr in Settings.";
                    else if (p[1] == "3") Error = "Bluetooth is off.";
                    break;
                case "vfound":
                {
                    string id = p[1];
                    Sensor s = Found.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (s == null) Found.Add(s = new Sensor { Id = id });
                    s.Name = p[2];
                    int.TryParse(p[3], out s.Rssi);
                    byte[] maker = Hex(p.Length > 4 ? p[4] : "");
                    if (maker.Length > 2) s.Battery = Mathf.Clamp(maker[2], 0, 100);
                    if (maker.Length >= 12) s.Firmware = maker[maker.Length - 3] + "." + maker[maker.Length - 2] + "." + maker[maker.Length - 1];
                    // Keeping an enabled sensor: the first one heard is it.
                    if (_keep && !Connected && Stage == "searching") { Native.Scan(false); Connect(s); }
                    break;
                }
                case "vconnected":
                    Connected = true;
                    _acked = p.Length > 3 && p[3] == "1";
                    Stage = "subscribing";
                    if (Current == null) Current = new Sensor { Id = p[1], Name = p[2] };
                    // The status read arrives first; if it has not, live mode goes out anyway after a moment.
                    Invoke(nameof(GoLive), 0.6f);
                    break;
                case "vdisconnected":
                    Connected = Live = false;
                    Stage = "disconnected";
                    if (_keep) _reconnectAt = Time.realtimeSinceStartup + 3f;
                    break;
                case "vstatus":
                {
                    byte[] b = Hex(p[1]);
                    if (b.Length > 5) { _leftHand = b[5] == 1; Calibration = b[4]; }
                    if (b.Length > 1 && b[0] == 2 && b[1] == 1) { Live = true; Stage = "live"; }
                    else if (b.Length > 1 && b[0] == 2 && b[1] != 1 && Time.realtimeSinceStartup - _lastLive > 3f) { Reasserts++; GoLive(); }
                    break;
                }
                case "vdata":
                    Packet(Hex(p[1]));
                    break;
                case "verror":
                    Error = p.Length > 1 ? p[1] : "Something went wrong";
                    break;
            }
            Changed?.Invoke();
        }

        void GoLive()
        {
            if (!Connected) return;
            _lastLive = Time.realtimeSinceStartup;
            Native.Write(new byte[] { 2, 1, 0, 0, 0, (byte)(_leftHand ? 1 : 0), 0, 0, 0, 0 });
            if (Stage == "subscribing") Stage = "ready";
            Changed?.Invoke();
        }

        void Packet(byte[] b)
        {
            if (b.Length < 20) return;
            Packets++;
            float now = Time.realtimeSinceStartup;
            int id = b[0];
            if (_next > 0 && now - _firstPacket > 3f) _next = 0;
            if (_next > 0 && id == _next - 1 && Same(_packets[id], b)) return; // the same packet twice
            if (id != _next) { _next = 0; if (id != 0) return; }
            if (id == 0) _firstPacket = now;
            _packets[id] = b;
            _next = id + 1;
            if (_next < 6) return;
            _next = 0;
            VertexPutt putt = VertexPutt.Read(_packets);
            putt.Raw = SensorMath.RawJson(_packets);
            Putts.Add(putt);
            Live = true;
            Stage = "live";
            Putt?.Invoke(putt);
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static byte[] Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new byte[0];
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        static class Native
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            static AndroidJavaClass _java;
            static AndroidJavaClass Java => _java ?? (_java = new AndroidJavaClass("co.puttr.app2.PuttrVertex"));
            static AndroidJavaObject Activity()
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) return player.GetStatic<AndroidJavaObject>("currentActivity");
            }
            public static void Scan(bool on) => Puttr.AndroidPermissions.Bluetooth(() => Java.CallStatic("scan", Activity(), on));
            public static void Connect(string id) => Puttr.AndroidPermissions.Bluetooth(() => Java.CallStatic("connect", Activity(), id));
            public static void Disconnect() => Java.CallStatic("disconnect");
            public static void Write(byte[] b) => Java.CallStatic("write", (sbyte[])(Array)b);
            public static string Poll() => Java.CallStatic<string>("poll");
#elif UNITY_IOS && !UNITY_EDITOR
            [DllImport("__Internal")] static extern void PuttrVertex_Scan(int on);
            [DllImport("__Internal")] static extern void PuttrVertex_Connect(string id);
            [DllImport("__Internal")] static extern void PuttrVertex_Disconnect();
            [DllImport("__Internal")] static extern void PuttrVertex_Write(byte[] bytes, int length);
            [DllImport("__Internal")] static extern int PuttrVertex_Poll(byte[] buffer, int capacity);
            static readonly byte[] Buffer = new byte[4096];
            public static void Scan(bool on) => PuttrVertex_Scan(on ? 1 : 0);
            public static void Connect(string id) => PuttrVertex_Connect(id);
            public static void Disconnect() => PuttrVertex_Disconnect();
            public static void Write(byte[] b) => PuttrVertex_Write(b, b.Length);
            public static string Poll() { int n = PuttrVertex_Poll(Buffer, Buffer.Length); return n > 0 ? Encoding.UTF8.GetString(Buffer, 0, n) : null; }
#else
            public static void Scan(bool on) { }
            public static void Connect(string id) { }
            public static void Disconnect() { }
            public static void Write(byte[] b) { }
            public static string Poll() => null;
#endif
        }
    }

    /// <summary>Pairing (when off) and the live view (when on), one screen.</summary>
    public sealed class VertexScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        bool _pairing;

        public VertexScreen(App app)
        {
            _app = app;
            _body = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("Vertex sensor", Leave));
            scroll.Add(_body);
            scroll.Add(FreePuttScreen.Space(28));
            Root = scroll;
            Vertex.Changed += Draw;
            Vertex.Putt += Putted;
            if (Vertex.Enabled && !Vertex.Connected) Vertex.Scan(30f);
            Draw();
        }

        void Leave()
        {
            Vertex.Changed -= Draw;
            Vertex.Putt -= Putted;
            if (!Vertex.Enabled) { Vertex.StopScan(); Vertex.Disconnect(); }
            _app.Pop();
        }

        void Putted(VertexPutt _) => Draw();

        void Draw()
        {
            _body.Clear();
            if (!Vertex.Enabled && !_pairing) { Intro(); return; }
            Connection();
            LastPutt();
            if (Vertex.Enabled)
            {
                Diagnostics();
                _body.Add(Ui.Box("pad", Ui.Button("Disable sensor", () => Dialog.Ask(Root, "Disable the Vertex sensor?", "Putts will no longer carry stroke data.", "Disable", () => { Vertex.Enabled = false; Leave(); }, true), "btn btn-ghost").Margin(12)));
            }
            else if (Vertex.Connected)
            {
                VisualElement enable = Ui.Button("Enable sensor", Enable);
                enable.SetEnabled(Vertex.Putts.Count > 0);
                _body.Add(Ui.Box("pad", enable.Margin(12), Ui.Button("Skip test & enable", Enable, "btn btn-ghost")));
            }
            else _body.Add(Ui.Box("pad", Ui.Button("Cancel", () => { _pairing = false; Vertex.StopScan(); Draw(); }, "btn btn-ghost").Margin(12)));
        }

        void Enable()
        {
            Vertex.Enabled = true;
            _pairing = false;
            _app.Toast("Vertex sensor enabled.");
            Draw();
        }

        void Intro()
        {
            if (Vertex.Error != null) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text(Vertex.Error, "sub", Ui.Medium))).Margin(4));
            _body.Add(Ui.Box("pad", Ui.Box("glass",
                Ui.Text("CALIBRATE FIRST", "kick", Ui.Bold),
                Ui.Text("Calibrate your sensor in the Vertex app before pairing it with Puttr.", "sub", Ui.Regular).Margin(4),
                Ui.Button("Pair sensor", () => { _pairing = true; Vertex.Scan(30f); Draw(); }))));
            _body.Add(Ui.Section("What the sensor measures"));
            _body.Add(Ui.Box("pad", Ui.Box("glass",
                Ui.Text("Face change at impact, tempo, rhythm, putter speed, rotation and stroke length, on every putt.", "sub", Ui.Regular))));
            _body.Add(Ui.Box("pad", Ui.Box("glass list-row-flat",
                Ui.Text("Don't have one? Use code PUTTR15.", "sub", Ui.Medium),
                Ui.Button("Buy now", () => Application.OpenURL("https://vertex-golf.com/?wt_coupon=puttr15"), "btn btn-ghost"))).Margin(10));
        }

        void Connection()
        {
            _body.Add(Ui.Section("Connection"));
            VisualElement card = Ui.Box("glass");
            Vertex.Sensor s = Vertex.Current;
            if (Vertex.Connected && s != null)
            {
                card.Add(Ui.Box("row", Ui.Box("grow", Ui.Text(s.Name ?? "Vertex", "feed-name", Ui.Bold)), Rows.Dot(Ui.Bright, Vertex.Live ? "Live" : "Connected")));
                if (s.Battery >= 0)
                {
                    Label battery = Ui.Text("Battery " + s.Battery + "%", "sub", Ui.Medium);
                    battery.style.color = s.Battery <= 15 ? Ui.Red : s.Battery <= 30 ? Ui.Amber : Ui.Bright;
                    card.Add(battery);
                }
                if (Vertex.Putts.Count == 0) card.Add(Ui.Text("Hit a test putt to see your numbers.", "sub", Ui.Regular).Margin(6));
            }
            else
            {
                string words = Vertex.Stage == "searching" ? (Vertex.Found.Count > 0 ? "Found " + Vertex.Found[0].Name : "Searching...") : Vertex.Stage == "connecting" ? "Connecting..." : "Not connected";
                card.Add(Ui.Box("row", Ui.Box("grow", Ui.Text("Vertex", "feed-name", Ui.Bold)), Rows.Dot(Ui.Amber, words)));
                if (Vertex.Error != null) card.Add(Ui.Text(Vertex.Error, "sub", Ui.Regular).Margin(6));
                // While pairing, every sensor heard can be chosen.
                if (_pairing) foreach (Vertex.Sensor f in Vertex.Found)
                {
                    Vertex.Sensor pick = f;
                    card.Add(Rows.Row("Lightning", f.Name + (f.Battery >= 0 ? "  ·  " + f.Battery + "%" : ""), () => Vertex.Connect(pick)));
                }
                if (!_pairing && Vertex.Stage != "searching") card.Add(Ui.Button("Search again", () => Vertex.Scan(30f), "btn btn-ghost"));
                if (_pairing) card.Add(Ui.Text("Wake the sensor with a waggle and keep it within a few feet.", "muted", Ui.Regular).Margin(8));
            }
            _body.Add(Ui.Box("pad", card));
        }

        void LastPutt()
        {
            if (Vertex.Putts.Count == 0) return;
            VertexPutt v = Vertex.Putts[Vertex.Putts.Count - 1];
            _body.Add(Ui.Section("Last putt   ·   " + Vertex.Putts.Count + (Vertex.Putts.Count == 1 ? " putt" : " putts")));
            Label face = Ui.Text((v.FaceChange > 0 ? "+" : "") + v.FaceChange.ToString("0.0") + "°", "score-big", Ui.Bold);
            face.style.color = VertexPutt.FaceColour(v.FaceChange);
            VisualElement card = Ui.Box("glass",
                Ui.Box("row", Ui.Box("grow", Ui.Text("FACE CHANGE", "kick", Ui.Bold), face), Ui.Text(VertexPutt.FaceWords(v.FaceChange), "h2", Ui.Bold)),
                Ui.Box("row", FreePuttScreen.Stat(v.Tempo.ToString("0") + " bpm", "Tempo"), FreePuttScreen.Stat(v.Rhythm.ToString("0.0") + " : 1", "Rhythm"), FreePuttScreen.Stat(v.Speed.ToString("0.00") + " m/s", "Speed")).Margin(10),
                Ui.Box("row", FreePuttScreen.Stat(v.BackRotation.ToString("0.0") + "°", "Back rotation"), FreePuttScreen.Stat(v.ForwardRotation.ToString("0.0") + "°", "Forward rotation"), FreePuttScreen.Stat(v.BackLength.ToString("0") + " mm", "Backstroke")).Margin(10),
                Ui.Box("row", FreePuttScreen.Stat(v.BackTime.ToString("0.00") + " s", "Back time"), FreePuttScreen.Stat(v.ForwardTime.ToString("0.00") + " s", "Forward time"), FreePuttScreen.Stat(v.Lie.ToString("0.0") + "°", "Lie angle")).Margin(10));
            _body.Add(Ui.Box("pad", card));
        }

        void Diagnostics()
        {
            _body.Add(Ui.Section("Sensor link"));
            string calibration = Vertex.Calibration == 3 ? "Calibrated" : Vertex.Calibration < 0 ? "Unknown" : "Status " + Vertex.Calibration;
            _body.Add(Ui.Box("pad", Ui.Box("glass",
                Ui.Box("row", FreePuttScreen.Stat(Vertex.Stage, "Stage"), FreePuttScreen.Stat(Vertex.Live ? "Confirmed" : "Sent", "Live mode"), FreePuttScreen.Stat(calibration, "Calibration")),
                Ui.Box("row", FreePuttScreen.Stat(Vertex.Packets.ToString(), "Packets"), FreePuttScreen.Stat(Vertex.Reasserts.ToString(), "Reasserts"), FreePuttScreen.Stat(Vertex.Current?.Firmware ?? "–", "Firmware")).Margin(10))));
        }
    }
}
