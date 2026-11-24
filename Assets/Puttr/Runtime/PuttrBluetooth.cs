// The console over Bluetooth.
//
// One service, no pairing. Putts are notifications of JSON text, up to 12
// points each, with nothing to mark the end of a path: the path is over when
// 100 ms pass with no more of it. The hole and the firmware version are read
// once on connecting. See the Bluetooth page of the developer docs.
//
// The radio work is in a small native plugin (Assets/Puttr/Native~): a bundle
// on macOS, and the same source compiled into the app on iOS and tvOS. Everywhere else Supported is false and nothing here does
// anything, so callers need no #if of their own.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Puttr
{
    public enum BluetoothState { Unavailable, NotAllowed, Off, Scanning, Connecting, Connected }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>Android asks for Bluetooth at run time: scan and connect (12 and later) or location (before).</summary>
    public static class AndroidPermissions
    {
        public static void Bluetooth(Action granted)
        {
            string[] wanted = AndroidVersion() >= 31
                ? new[] { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" }
                : new[] { "android.permission.ACCESS_FINE_LOCATION" };
            Ask(wanted, granted);
        }

        public static void Ask(string[] wanted, Action granted)
        {
            bool all = true;
            foreach (string p in wanted) if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(p)) all = false;
            if (all) { granted(); return; }
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            int answered = 0;
            void One(string _) { if (++answered >= wanted.Length) granted(); }
            callbacks.PermissionGranted += One;
            callbacks.PermissionDenied += One; // the plugin reports "not allowed" itself
            UnityEngine.Android.Permission.RequestUserPermissions(wanted, callbacks);
        }

        public static int AndroidVersion()
        {
            using (var version = new UnityEngine.AndroidJavaClass("android.os.Build$VERSION")) return version.GetStatic<int>("SDK_INT");
        }
    }
#endif

    public sealed class PuttrBluetooth
    {
        public struct Device
        {
            public string Id, Name;
            /// <summary>Signal strength in dBm: about -50 beside the console, -90 at the edge of range. 0 if unknown.</summary>
            public int Rssi;
            /// <summary>When it was last heard from (Time.realtimeSinceStartup).</summary>
            public float Seen;
        }

        /// <summary>Is there a Bluetooth radio this build can use?</summary>
        public static bool Supported =>
        // puttr-app-2: every build with a radio, Apple TV and Android TV included. The web (Samsung TV) build has none.
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_IOS || UNITY_TVOS || (UNITY_ANDROID && !UNITY_EDITOR)
            !_missing;
#else
            false;
#endif

        public BluetoothState State { get; private set; } = BluetoothState.Unavailable;
        /// <summary>Every Puttr heard advertising, nearest first.</summary>
        public IReadOnlyList<Device> Devices => _devices;
        public bool Connected => State == BluetoothState.Connected;
        public string ConnectedId { get; private set; }
        public string ConnectedName { get; private set; }
        public string Version { get; private set; }
        public string LastError { get; private set; }

        /// <summary>A whole ball path has arrived.</summary>
        public event Action<IReadOnlyList<BallPoint>> Path;
        /// <summary>The console's hole, read as it connected.</summary>
        public event Action<Hole> HoleRead;
        /// <summary>Something above changed: a console came or went, or the radio did.</summary>
        public event Action Changed;

        const float QuietSeconds = 0.1f;
        const float ForgetSeconds = 12f;

        readonly List<Device> _devices = new List<Device>();
        readonly List<BallPoint> _path = new List<BallPoint>();
        float _lastChunk;
        bool _running;
#pragma warning disable CS0414
        static bool _missing;
#pragma warning restore CS0414

        public void Start()
        {
            if (!Supported || _running) return;
            try { PuttrBle_Start(); _running = true; }
            catch (DllNotFoundException) { _missing = true; }
            catch (EntryPointNotFoundException) { _missing = true; }
        }

        /// <summary>
        /// Look again while nothing is in range. On Apple this restarts the scan and asks for a console the device
        /// already holds (it does not advertise); Android's scan runs on, so this does nothing there.
        /// </summary>
        public void Rescan()
        {
            if (!_running || State != BluetoothState.Scanning) return;
            try { PuttrBle_Start(); } catch (Exception) { }
        }

        /// <summary>True between Start and Stop.</summary>
        public bool Running => _running;

        /// <summary>Stops looking and drops the console (the native side cancels the connection).</summary>
        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try { PuttrBle_Stop(); } catch (Exception) { }
            State = Supported ? BluetoothState.Off : BluetoothState.Unavailable;
            ConnectedId = null; ConnectedName = null;
        }

        /// <summary>Connect to one of Devices by its Id.</summary>
        public void Connect(string id)
        {
            if (!_running || string.IsNullOrEmpty(id)) return;
            LastError = null;
            PuttrBle_Connect(id);
        }

        public void Disconnect()
        {
            if (_running) PuttrBle_Disconnect();
        }

        /// <summary>Writes text to one of the console's characteristics (by its first 8 hex digits, as "538AB803").</summary>
        public void Write(string head, string text)
        {
            if (!_running || !Connected) return;
            try { PuttrBle_Write(head, text ?? ""); }
            catch (Exception) { Wrote?.Invoke(head, false); } // the Mac bundle built before writing existed
        }

        /// <summary>A "value" line: any other characteristic read or notified.</summary>
        public event Action<string, string> Value;
        /// <summary>A "wrote" line: head, and whether the console took it.</summary>
        public event Action<string, bool> Wrote;

        /// <summary>Call every frame: takes in what the radio has heard since the last call.</summary>
        public void Pump()
        {
            if (!_running) return;
            float now = Time.realtimeSinceStartup;
            bool changed = false;
            for (int guard = 0; guard < 200; guard++)
            {
                int length = PuttrBle_Poll(Buffer, Buffer.Length);
                if (length <= 0) break;
                string[] part = Encoding.UTF8.GetString(Buffer, 0, length).Split('\t');
                switch (part[0])
                {
                    case "value":
                        if (part.Length > 2) Value?.Invoke(part[1], part[2]);
                        break;
                    case "wrote":
                        if (part.Length > 2) Wrote?.Invoke(part[1], part[2] == "ok");
                        break;
                    case "state":
                        if (part.Length > 1 && int.TryParse(part[1], out int s))
                        {
                            State = s == 2 ? BluetoothState.NotAllowed : s == 3 ? BluetoothState.Off : s == 4 ? BluetoothState.Scanning : s == 5 ? BluetoothState.Connecting : s == 6 ? BluetoothState.Connected : BluetoothState.Unavailable;
                            changed = true;
                        }
                        break;
                    case "found":
                        if (part.Length > 3)
                        {
                            int.TryParse(part[3], out int rssi);
                            var device = new Device { Id = part[1], Name = part[2], Rssi = rssi, Seen = now };
                            int at = _devices.FindIndex(d => d.Id == device.Id);
                            if (at < 0) { _devices.Add(device); changed = true; }
                            else
                            {
                                if (rssi == 0) device.Rssi = _devices[at].Rssi;
                                changed |= _devices[at].Name != device.Name || Mathf.Abs(_devices[at].Rssi - device.Rssi) > 4;
                                _devices[at] = device;
                            }
                        }
                        break;
                    case "connected":
                        ConnectedId = part.Length > 1 ? part[1] : null;
                        ConnectedName = part.Length > 2 ? part[2] : "Puttr";
                        changed = true;
                        break;
                    case "disconnected":
                        if (part.Length > 1 && part[1] == ConnectedId) { ConnectedId = null; ConnectedName = null; Version = null; }
                        _path.Clear();
                        changed = true;
                        break;
                    case "hole":
                        if (part.Length > 1 && PuttrAnalysis.TryParseHole(part[1], out Hole hole)) HoleRead?.Invoke(hole);
                        break;
                    case "version":
                        Version = part.Length > 1 ? part[1].Trim() : null;
                        changed = true;
                        break;
                    case "ball":
                        // One notification is one complete JSON array. A cut-off one will not parse, and is dropped.
                        List<BallPoint> chunk = part.Length > 1 ? PuttrAnalysis.ParsePath(part[1]) : null;
                        if (chunk != null && chunk.Count > 0) { _path.AddRange(chunk); _lastChunk = now; }
                        break;
                    case "error":
                        LastError = part.Length > 1 ? part[1] : "Bluetooth error";
                        changed = true;
                        break;
                }
            }

            // No more of this path for a tenth of a second: that was all of it.
            if (_path.Count > 0 && now - _lastChunk > QuietSeconds)
            {
                var whole = new List<BallPoint>(_path);
                _path.Clear();
                Path?.Invoke(whole);
            }

            // A console that has stopped advertising has gone (unless it is the one in use).
            for (int i = _devices.Count - 1; i >= 0; i--)
                if (now - _devices[i].Seen > ForgetSeconds && _devices[i].Id != ConnectedId) { _devices.RemoveAt(i); changed = true; }
            if (changed)
            {
                _devices.Sort((a, b) => (b.Rssi == 0 ? -200 : b.Rssi).CompareTo(a.Rssi == 0 ? -200 : a.Rssi));
                Changed?.Invoke();
            }
        }

        static readonly byte[] Buffer = new byte[8192];

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android: Plugins/Android/PuttrApp.androidlib PuttrBle.java, the same lines as the Apple plugin.
        static UnityEngine.AndroidJavaClass _java;
        static UnityEngine.AndroidJavaClass Java => _java ?? (_java = new UnityEngine.AndroidJavaClass("co.puttr.app2.PuttrBle"));
        static void PuttrBle_Start()
        {
            if (Application.platform == RuntimePlatform.Android) AndroidPermissions.Bluetooth(() =>
            {
                using (var player = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (UnityEngine.AndroidJavaObject activity = player.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity"))
                    Java.CallStatic("start", activity);
            });
        }
        static void PuttrBle_Stop() => Java.CallStatic("stop");
        static void PuttrBle_Connect(string identifier) => Java.CallStatic("connect", identifier);
        static void PuttrBle_Disconnect() => Java.CallStatic("disconnect");
        static void PuttrBle_Write(string head, string text) => Java.CallStatic("write", head, text);
        static int PuttrBle_Poll(byte[] buffer, int capacity)
        {
            string line = Java.CallStatic<string>("poll");
            if (line == null) return 0;
            byte[] bytes = Encoding.UTF8.GetBytes(line);
            int n = Math.Min(bytes.Length, capacity);
            Array.Copy(bytes, buffer, n);
            return n;
        }
#elif ((UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR) || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // On iOS and tvOS the plugin is compiled into the app itself; on a Mac it is PuttrBle.bundle.
#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
        const string Library = "__Internal";
#else
        const string Library = "PuttrBle";
#endif
        [DllImport(Library)] static extern void PuttrBle_Start();
        [DllImport(Library)] static extern void PuttrBle_Stop();
        [DllImport(Library)] static extern void PuttrBle_Connect(string identifier);
        [DllImport(Library)] static extern void PuttrBle_Disconnect();
        [DllImport(Library)] static extern int PuttrBle_Poll(byte[] buffer, int capacity);
        [DllImport(Library)] static extern void PuttrBle_Write(string head, string text);
#else
        // puttr-app-2: the web build (Samsung TV) and anything else: no Bluetooth (Supported is false).
        static void PuttrBle_Start() { }
        static void PuttrBle_Stop() { }
        static void PuttrBle_Connect(string identifier) { }
        static void PuttrBle_Disconnect() { }
        static int PuttrBle_Poll(byte[] buffer, int capacity) => 0;
        static void PuttrBle_Write(string head, string text) { }
#endif
    }
}
