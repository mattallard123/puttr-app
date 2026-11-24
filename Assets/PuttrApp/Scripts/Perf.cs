// The numbers the Phase 0 gate is decided on: when the first screen was up, how long each API
// call took, the frame rate while scrolling, and how often the app redraws when nothing moves.
//
// Idle redraw: Unity draws every frame by default, which is what makes a Unity app run warm on
// a screen of text. With Low power on, the shell draws one frame in four unless something was
// touched in the last second and a half. The switch is on the Profile tab so both can be
// measured on the same device.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace PuttrApp
{
    public static class Perf
    {
        const string LowPowerPref = "app2.lowpower";
        const int IdleInterval = 4;      // 60 / 4 = 15 frames a second at rest
        const float AwakeSeconds = 1.5f;

        static readonly List<string> _marks = new List<string>();
        static readonly Queue<string> _requests = new Queue<string>();
        static float _awakeUntil, _windowStart, _worstMs;
        static int _frames, _rendered;
        static string _fpsLine = "";

        public static bool InGame;
        public static int ImagesLoaded, ImagesFromDisk;

        static int _lowPower = -1;

        public static bool LowPower
        {
            // Read once: Tick asks every frame.
            get { if (_lowPower < 0) _lowPower = PlayerPrefs.GetInt(LowPowerPref, 1); return _lowPower == 1; }
            set { _lowPower = value ? 1 : 0; PlayerPrefs.SetInt(LowPowerPref, _lowPower); PlayerPrefs.Save(); }
        }

        /// <summary>A moment worth timing, in seconds since the app started.</summary>
        public static void Mark(string what)
        {
            string line = what + " at " + Time.realtimeSinceStartupAsDouble.ToString("0.00") + " s";
            _marks.Add(line);
            Debug.Log("Puttr 2 perf: " + line);
            ToFile(line);
        }

        /// <summary>perf.log in the app's documents folder: readable from the Mac with devicectl. Lines are held
        /// and written together (every two seconds from Tick, and when the app leaves the front), not one file
        /// open per line; past 512 KB the older half is dropped.</summary>
        static readonly StringBuilder _unwritten = new StringBuilder();
        static float _writeAt;
        const long LogLimit = 512 * 1024;

        static void ToFile(string line)
        {
            _unwritten.Append(System.DateTime.Now.ToString("s")).Append(' ').Append(line).Append('\n');
            if (App.I == null) Flush();   // nothing ticking (a test, or before the app is up): write now
        }

        public static void Flush()
        {
            if (_unwritten.Length == 0) return;
            string text = _unwritten.ToString();
            _unwritten.Clear();
            try
            {
                string file = Path.Combine(Application.persistentDataPath, "perf.log");
                var info = new FileInfo(file);
                if (info.Exists && info.Length > LogLimit)
                {
                    string all = File.ReadAllText(file);
                    int cut = all.IndexOf('\n', all.Length / 2);
                    File.WriteAllText(file, cut >= 0 ? all.Substring(cut + 1) : "");
                }
                File.AppendAllText(file, text);
            }
            catch (System.Exception) { /* a log that cannot be written is not worth failing for */ }
        }

        /// <summary>Something went wrong that is worth finding in the log later.</summary>
        public static void Note(string what)
        {
            Debug.Log("Puttr 2: " + what);
            ToFile(what);
        }

        static float _lastFpsToFile;

        public static void Request(string method, string url, long status, float ms)
        {
            // Never the query: it carries the Firebase key.
            int q = url.IndexOf('?');
            if (q >= 0) url = url.Substring(0, q);
            string line = method + " " + url.Replace(Config.ApiUrl, "") + "  " + status + "  " + Mathf.RoundToInt(ms) + " ms";
            _requests.Enqueue(line);
            while (_requests.Count > 6) _requests.Dequeue();
            Debug.Log("Puttr 2 perf: " + line);
            ToFile(line);
            Wake();
        }

        /// <summary>Something changed on screen: draw every frame for a moment.</summary>
        public static void Wake(float seconds = AwakeSeconds) => _awakeUntil = Mathf.Max(_awakeUntil, Time.unscaledTime + seconds);

        static int _lastW, _lastH;

        /// <summary>Once a frame, from App.Update.</summary>
        public static void Tick()
        {
            if (Touched()) Wake();
            if (_unwritten.Length > 0 && Time.unscaledTime >= _writeAt) { _writeAt = Time.unscaledTime + 2f; Flush(); }
            // Turning the device lays everything out again: draw every frame through it, or the new layout only
            // shows on the next slow idle frame (the iPad's slow rotation on the Play tab).
            if (Screen.width != _lastW || Screen.height != _lastH) { _lastW = Screen.width; _lastH = Screen.height; Wake(2f); }
            bool idle = !InGame && LowPower && Time.unscaledTime > _awakeUntil;
            // At rest the whole loop slows to 15 a second, not only the drawing: the loop itself (scripts, input,
            // UI layout) was most of the idle CPU (2026-11-17 walk: 10 to 13% on every tab with the loop at 60).
            // The first touch wakes it back to 60 within one slow frame.
            int fps = idle ? 60 / IdleInterval : 60;
            // In a game that is only waiting for the next putt: 30 a second. A putt playing out, a touch, or anything
            // that wakes the app puts it straight back to 60 (the console's putt arrives through the socket regardless).
            if (InGame && LowPower && !PuttrArcade.Arcade.Busy && Time.unscaledTime > _awakeUntil) fps = 30;
            if (OnDemandRendering.renderFrameInterval != 1) OnDemandRendering.renderFrameInterval = 1;
            if (Application.targetFrameRate != fps) Application.targetFrameRate = fps;

            _frames++;
            if (OnDemandRendering.willCurrentFrameRender) _rendered++;
            _worstMs = Mathf.Max(_worstMs, Time.unscaledDeltaTime * 1000f);
            float span = Time.unscaledTime - _windowStart;
            if (span >= 1f)
            {
                _fpsLine = Mathf.RoundToInt(_rendered / span) + " drawn / " + Mathf.RoundToInt(_frames / span) + " ticks a second, worst frame " + Mathf.RoundToInt(_worstMs) + " ms";
                // Into the file every five seconds while something is moving (a still screen says nothing new).
                bool moving = InGame || Time.unscaledTime <= _awakeUntil;
                if (moving && Time.unscaledTime - _lastFpsToFile >= 5f)
                {
                    _lastFpsToFile = Time.unscaledTime;
                    ToFile((InGame ? "game: " : "shell: ") + _fpsLine + ", memory " + UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024) + " MB");
                }
                _frames = _rendered = 0;
                _worstMs = 0f;
                _windowStart = Time.unscaledTime;
            }
        }

        static bool Touched()
        {
            Pointer pointer = Pointer.current;
            if (pointer != null && (pointer.press.isPressed || pointer.delta.ReadValue().sqrMagnitude > 0.01f)) return true;
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.scroll.ReadValue().sqrMagnitude > 0.01f) return true;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.isPressed) return true;
            // Typing in a field: full rate while it has focus (a hardware keyboard or a slow-to-open one sends no touches).
            return TouchScreenKeyboard.visible || (App.I != null && App.I.Typing);
        }

        /// <summary>What the overlay on the Profile tab shows.</summary>
        public static string Summary()
        {
            var sb = new StringBuilder();
            sb.Append(_fpsLine).Append('\n');
            sb.Append("low power ").Append(LowPower ? "on" : "off").Append(", memory ").Append(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)).Append(" MB");
            sb.Append(", images ").Append(ImagesLoaded).Append(" (").Append(ImagesFromDisk).Append(" from disk)\n");
            foreach (string mark in _marks) sb.Append(mark).Append('\n');
            foreach (string request in _requests) sb.Append(request).Append('\n');
            return sb.ToString().TrimEnd();
        }
    }
}
