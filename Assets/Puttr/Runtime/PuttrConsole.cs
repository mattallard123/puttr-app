// The WiFi connection to a Puttr console, as one component.
//
// Add it to a GameObject and subscribe to Putt. It does what the WiFi page of
// the docs asks for:
//
//   - finds the console (puttr.local, puttr, puttr.lan, puttr.home, or a host
//     you give it), probing GET /version
//   - opens the putt socket and lets it reconnect
//   - reads GET /hole on connect and every 30 seconds, and keeps the last
//     hole on disk
//   - heartbeats GET /version every 5 seconds and reconnects after 2 misses
//   - filters paths that are not putts and applies the 2.5 second pause
//
// Project setting to check: Player > Other Settings > Allow downloads over
// HTTP must be "Always allowed". The console speaks plain http.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Puttr
{
    public enum ConsoleState { Searching, Connecting, Connected }

    public sealed class PuttrConsole : MonoBehaviour
    {
        [Tooltip("Host name or IP address, optionally with :port. Leave empty to search for the console by name.")]
        public string host = "";
        [Tooltip("The console's name: its Bluetooth name, puttr unless it was renamed.")]
        public string consoleName = "puttr";
        public int port = 3000;
        [Tooltip("Also look for the simulator on this computer.")]
        public bool tryLocalhost = true;
        [Tooltip("Camera frames per second, if you have measured this console. Most are 36 to 41.")]
        public float framesPerSecond = PuttrAnalysis.DefaultFps;
        [Tooltip("An original Puttr does not serve /hole. When true, score with the typical hole location until a real one is known.")]
        public bool assumeTypicalHole = true;

        [Tooltip("Also look for the console over Bluetooth, and take putts from it. macOS builds and the macOS editor only, so far.")]
        public bool useBluetooth = true;
        [Tooltip("Look for the console on WiFi.")]
        public bool useWifi = true;

        [Tooltip("Write connection changes to the log.")]
        public bool logConnection = true;

        [Tooltip("Where you told the Player to putt from, 3 to 11 feet. 0 if unknown.")]
        public float distanceFt = 0;
        [Tooltip("1 left, 2 center, 3 right.")]
        public int tee = 2;

        /// <summary>A real putt, once each.</summary>
        public event Action<PuttResult> Putt;
        /// <summary>A path that was not counted, and why: "not-a-ball", "repeat", "no-hole".</summary>
        public event Action<IReadOnlyList<BallPoint>, string> Rejected;
        public event Action<ConsoleState> StateChanged;

        public ConsoleState State { get; private set; } = ConsoleState.Searching;
        /// <summary>host:port of the console in use, or null.</summary>
        public string ActiveHost { get; private set; }
        public string Version { get; private set; }
        public bool HasHole { get; private set; }
        public Hole Hole { get; private set; }
        /// <summary>True while the hole is the typical location, not one read from this console.</summary>
        public bool HoleIsAssumed { get; private set; }
        public bool HoleCalibrated => HasHole && PuttrAnalysis.IsHoleCalibrated(Hole);
        /// <summary>The Bluetooth side: what is in range, and which console is connected.</summary>
        public PuttrBluetooth Bluetooth { get; } = new PuttrBluetooth();
        /// <summary>Is a console connected, by either route?</summary>
        public bool Online => State == ConsoleState.Connected || Bluetooth.Connected;
        /// <summary>"puttr on WiFi", "puttr over Bluetooth", or null.</summary>
        public string Link => Bluetooth.Connected ? Bluetooth.ConnectedName + " over Bluetooth" : State == ConsoleState.Connected ? ActiveHost + " on WiFi" : null;
        /// <summary>Every WiFi address tried by Probe, with the version it answered with (null for no answer).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Probed => _probed;
        readonly List<KeyValuePair<string, string>> _probed = new List<KeyValuePair<string, string>>();
        const string PrefBluetooth = "puttr.bluetooth";

        /// <summary>The names being tried while searching.</summary>
        public IReadOnlyList<string> Candidates => _candidates;

        const string PrefHost = "puttr.host";
        const string PrefHole = "puttr.hole";
        const float PulseSeconds = 5f;
        const float HoleSeconds = 30f;
        const int PulseMissesToDrop = 2;

        readonly SocketIOClient _socket = new SocketIOClient();
        readonly List<string> _candidates = new List<string>();
        float _gateUntil;
        bool _restart;

        void Awake()
        {
            _socket.Event += OnSocketEvent;
            Bluetooth.Path += HandlePath;
            Bluetooth.HoleRead += SetHole;
            Bluetooth.Changed += OnBluetoothChanged;

            // -puttrHost localhost:3100 on the command line overrides the Inspector.
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-puttrHost") host = args[i + 1];
            // -puttrNoBluetooth: for automated runs. macOS ends a process that
            // touches Bluetooth when the program that launched it has no
            // Bluetooth permission text of its own (a script, a test runner).
            if (Array.IndexOf(args, "-puttrNoBluetooth") >= 0) useBluetooth = false;

            // Keep the last hole on disk so scoring can start at launch.
            if (PuttrAnalysis.TryParseHole(PlayerPrefs.GetString(PrefHole, ""), out Hole saved))
            {
                Hole = saved;
                HasHole = true;
            }
        }

        void OnEnable()
        {
            StartCoroutine(Run());
            if (useBluetooth) Bluetooth.Start();
        }

        void OnDisable()
        {
            StopAllCoroutines();
            _socket.Close();
            Bluetooth.Stop();
        }

        void OnDestroy() => _socket.Dispose();

        void Update()
        {
            _socket.Pump();
            Bluetooth.Pump();
        }

        // A console in range and nothing connected: take the one used last
        // time, else the one with this console's name, else the nearest.
        void OnBluetoothChanged()
        {
            if (logConnection && Bluetooth.Connected && _bluetoothLogged != Bluetooth.ConnectedId)
                Debug.Log("Puttr: connected to " + Bluetooth.ConnectedName + " over Bluetooth");
            _bluetoothLogged = Bluetooth.Connected ? Bluetooth.ConnectedId : null;
            if (!useBluetooth || Bluetooth.State != BluetoothState.Scanning || Bluetooth.Devices.Count == 0) return;
            if (Time.realtimeSinceStartup < _bluetoothRetry) return;
            _bluetoothRetry = Time.realtimeSinceStartup + 4f;
            string last = PlayerPrefs.GetString(PrefBluetooth, "");
            string pick = null;
            foreach (PuttrBluetooth.Device d in Bluetooth.Devices) if (d.Id == last) pick = d.Id;
            if (pick == null)
                foreach (PuttrBluetooth.Device d in Bluetooth.Devices)
                    if (string.Equals(d.Name.Trim(), consoleName, StringComparison.OrdinalIgnoreCase)) { pick = d.Id; break; }
            Bluetooth.Connect(pick ?? Bluetooth.Devices[0].Id);
        }
        string _bluetoothLogged;
        float _bluetoothRetry;

        /// <summary>Connect to this Bluetooth console, and prefer it from now on.</summary>
        public void UseBluetooth(string id)
        {
            PlayerPrefs.SetString(PrefBluetooth, id ?? "");
            _bluetoothRetry = Time.realtimeSinceStartup + 4f;
            Bluetooth.Connect(id);
        }

        /// <summary>Ask every likely WiFi address for its version, and list the answers in Probed.</summary>
        public IEnumerator Probe()
        {
            BuildCandidates();
            var hosts = new List<string>(_candidates);
            if (!string.IsNullOrEmpty(ActiveHost) && !hosts.Contains(ActiveHost)) hosts.Insert(0, ActiveHost);
            var requests = new List<UnityWebRequest>();
            foreach (string h in hosts)
            {
                UnityWebRequest req = UnityWebRequest.Get("http://" + h + "/version");
                req.timeout = 3;
                req.SendWebRequest();
                requests.Add(req);
            }
            bool pending = true;
            while (pending)
            {
                pending = false;
                foreach (UnityWebRequest req in requests) if (!req.isDone) pending = true;
                yield return null;
            }
            _probed.Clear();
            for (int i = 0; i < hosts.Count; i++)
            {
                _probed.Add(new KeyValuePair<string, string>(hosts[i], ReadVersion(requests[i])));
                requests[i].Dispose();
            }
        }

        /// <summary>Use this host from now on (a name or IP address, optionally with :port). Empty searches by name.</summary>
        public void SetHost(string newHost)
        {
            host = (newHost ?? "").Trim();
            _restart = true;
        }

        /// <summary>For an original Puttr: a hole you read over Bluetooth.</summary>
        public void SetHole(Hole hole)
        {
            Hole = hole;
            HasHole = true;
            HoleIsAssumed = false;
            PlayerPrefs.SetString(PrefHole, "[" + hole.X + "," + hole.Y + "," + hole.Diameter + "]");
        }

        /// <summary>
        /// Feed a path in as if the console had sent it. For tests and for
        /// playing recorded putts with no console.
        /// </summary>
        public void Inject(IReadOnlyList<BallPoint> path, bool skipPause = false)
        {
            if (skipPause) _gateUntil = 0;
            if (!HasHole) { Hole = Hole.Typical; HasHole = true; HoleIsAssumed = true; }
            HandlePath(path);
        }

        // ── Connection ──────────────────────────────────────────────────────

        IEnumerator Run()
        {
            int attempts = 0;
            while (true)
            {
                _restart = false;
                SetState(ConsoleState.Searching);
                ActiveHost = null;
                while (!useWifi) yield return null;

                BuildCandidates();
                string found = null, version = null;
                yield return Discover(_candidates, (h, v) => { found = h; version = v; });
                if (found == null)
                {
                    // Look often at first, then every 15 seconds: the Player
                    // may be in the middle of setting the console up.
                    float wait = ++attempts < 4 ? 3f : 15f;
                    float until = Time.realtimeSinceStartup + wait;
                    while (Time.realtimeSinceStartup < until && !_restart) yield return null;
                    continue;
                }

                attempts = 0;
                ActiveHost = found;
                Version = version;
                PlayerPrefs.SetString(PrefHost, found);
                SetState(ConsoleState.Connecting);
                yield return RefreshHole();

                SplitHost(found, out string name, out int p);
                _socket.Connect(name, p);

                int misses = 0;
                float now = Time.realtimeSinceStartup;
                float nextPulse = now + PulseSeconds, nextHole = now + HoleSeconds, nextReconnect = now + 2f;
                while (!_restart)
                {
                    yield return null;
                    now = Time.realtimeSinceStartup;

                    if (_socket.State == SocketIOClient.SocketState.Open) SetState(ConsoleState.Connected);
                    else
                    {
                        SetState(ConsoleState.Connecting);
                        // Let the socket put itself back, a couple of seconds apart.
                        if (_socket.State == SocketIOClient.SocketState.Closed && now >= nextReconnect)
                        {
                            nextReconnect = now + 2f;
                            _socket.Connect(name, p);
                        }
                    }

                    if (now >= nextPulse)
                    {
                        nextPulse = now + PulseSeconds;
                        string v = null;
                        yield return GetVersion(found, 3, r => v = r);
                        misses = v == null ? misses + 1 : 0;
                        if (v != null) Version = v;
                        if (misses >= PulseMissesToDrop) break;
                    }
                    if (now >= nextHole)
                    {
                        nextHole = now + HoleSeconds;
                        yield return RefreshHole();
                    }
                }
                _socket.Close();
            }
        }

        void BuildCandidates()
        {
            _candidates.Clear();
            void Add(string h)
            {
                if (string.IsNullOrEmpty(h)) return;
                if (!h.Contains(":")) h += ":" + port;
                if (!_candidates.Contains(h)) _candidates.Add(h);
            }
            if (!string.IsNullOrEmpty(host)) { Add(host); return; }
            Add(PlayerPrefs.GetString(PrefHost, ""));
            Add(consoleName + ".local");
            Add(consoleName);
            Add(consoleName + ".lan");
            Add(consoleName + ".home");
            if (tryLocalhost) Add("localhost");
        }

        // Probe every candidate at once and take the first that answers.
        IEnumerator Discover(List<string> hosts, Action<string, string> done)
        {
            var requests = new List<UnityWebRequest>();
            foreach (string h in hosts)
            {
                UnityWebRequest req = UnityWebRequest.Get("http://" + h + "/version");
                req.timeout = 3;
                req.SendWebRequest();
                requests.Add(req);
            }
            string found = null, version = null;
            while (found == null)
            {
                bool pending = false;
                for (int i = 0; i < requests.Count; i++)
                {
                    UnityWebRequest req = requests[i];
                    if (!req.isDone) { pending = true; continue; }
                    string v = ReadVersion(req);
                    if (v != null) { found = hosts[i]; version = v; break; }
                }
                if (!pending) break;
                yield return null;
            }
            foreach (UnityWebRequest req in requests)
            {
                if (!req.isDone) req.Abort();
                req.Dispose();
            }
            done(found, version);
        }

        IEnumerator GetVersion(string hostPort, int timeout, Action<string> done)
        {
            using (UnityWebRequest req = UnityWebRequest.Get("http://" + hostPort + "/version"))
            {
                req.timeout = timeout;
                yield return req.SendWebRequest();
                done(ReadVersion(req));
            }
        }

        static string ReadVersion(UnityWebRequest req)
        {
            if (req.result != UnityWebRequest.Result.Success) return null;
            if (!PuttrJson.TryParse(req.downloadHandler.text, out object value)) return null;
            if (value is Dictionary<string, object> map && map.TryGetValue("version", out object v) && v != null) return v.ToString();
            return null;
        }

        // GET /hole is served by Puttr 2 only. On an original Puttr it is a
        // 404, and the hole has to come from Bluetooth (SetHole) or, for a
        // demo, from the typical location.
        IEnumerator RefreshHole()
        {
            if (ActiveHost == null) yield break;
            using (UnityWebRequest req = UnityWebRequest.Get("http://" + ActiveHost + "/hole"))
            {
                req.timeout = 3;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success && PuttrAnalysis.TryParseHole(req.downloadHandler.text, out Hole hole))
                {
                    SetHole(hole);
                    yield break;
                }
            }
            if (!HasHole && assumeTypicalHole)
            {
                Hole = Hole.Typical;
                HasHole = true;
                HoleIsAssumed = true;
            }
        }

        static void SplitHost(string hostPort, out string name, out int port)
        {
            int colon = hostPort.LastIndexOf(':');
            name = colon < 0 ? hostPort : hostPort.Substring(0, colon);
            port = 3000;
            if (colon >= 0) int.TryParse(hostPort.Substring(colon + 1), out port);
        }

        void SetState(ConsoleState state)
        {
            if (State == state) return;
            State = state;
            if (logConnection) Debug.Log("Puttr: " + (state == ConsoleState.Searching ? "searching for a console" : state == ConsoleState.Connecting ? "connecting to " + ActiveHost : "connected to " + ActiveHost + ", hole " + (HasHole ? Hole.ToString() : "unknown")));
            StateChanged?.Invoke(state);
        }

        // ── Putts ───────────────────────────────────────────────────────────

        void OnSocketEvent(string name, object arg)
        {
            if (name != "BALL_PATH") return;
            // The payload is one string holding the whole path as JSON.
            List<BallPoint> path = PuttrAnalysis.ParsePath(arg);
            if (path != null && path.Count > 0) HandlePath(path);
        }

        void HandlePath(IReadOnlyList<BallPoint> path)
        {
            // A path is "something moved in front of the camera", not "a putt
            // happened". After any path, ignore the next 2.5 seconds: the same
            // ball can be seen again as it leaves the ramp.
            float now = Time.realtimeSinceStartup;
            if (now < _gateUntil) { Rejected?.Invoke(path, "repeat"); return; }
            _gateUntil = now + PuttrAnalysis.RepeatGateSeconds;

            if (!HasHole) { Rejected?.Invoke(path, "no-hole"); return; }

            PuttResult result = PuttrAnalysis.Analyze(path, Hole, distanceFt, tee, framesPerSecond);
            if (!result.Valid) { Rejected?.Invoke(path, result.Reason); return; }
            Putt?.Invoke(result);
        }
    }
}
