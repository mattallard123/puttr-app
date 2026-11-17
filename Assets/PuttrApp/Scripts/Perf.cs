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

        public static bool LowPower
        {
            get => PlayerPrefs.GetInt(LowPowerPref, 1) == 1;
            set { PlayerPrefs.SetInt(LowPowerPref, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>A moment worth timing, in seconds since the app started.</summary>
        public static void Mark(string what)
        {
            string line = what + " at " + Time.realtimeSinceStartupAsDouble.ToString("0.00") + " s";
            _marks.Add(line);
            Debug.Log("Puttr 2 perf: " + line);
            ToFile(line);
        }

        /// <summary>perf.log in the app's documents folder: readable from the Mac with devicectl.</summary>
        static void ToFile(string line)
        {
            try { File.AppendAllText(Path.Combine(Application.persistentDataPath, "perf.log"), System.DateTime.Now.ToString("s") + " " + line + "\n"); }
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

        /// <summary>Once a frame, from App.Update.</summary>
        public static void Tick()
        {
            if (Touched()) Wake();
            bool idle = !InGame && LowPower && Time.unscaledTime > _awakeUntil;
            int want = idle ? IdleInterval : 1;
            if (OnDemandRendering.renderFrameInterval != want) OnDemandRendering.renderFrameInterval = want;

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
            return TouchScreenKeyboard.visible;
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
