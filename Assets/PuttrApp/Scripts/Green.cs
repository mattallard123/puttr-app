// The app's one connection to the Puttr (the SDK's PuttrConsole: Bluetooth and WiFi). The
// arcade games and the app's own practice screens all listen to this one, because a Puttr
// talks to one Bluetooth device at a time.
using Puttr;
using UnityEngine;

namespace PuttrApp
{
    public static class Green
    {
        static PuttrConsole _console;

        /// <summary>Made the first time something needs a putt, so the app's start does not wait on a radio.</summary>
        public static PuttrConsole Console
        {
            get
            {
                if (_console != null) return _console;
                // Made inactive so a tester's Wi-Fi choice is in place before the radio starts.
                var go = new GameObject("Puttr Console");
                go.SetActive(false);
                Object.DontDestroyOnLoad(go);
                _console = go.AddComponent<PuttrConsole>();
                if (WifiOnly)
                {
                    _console.useBluetooth = false;
                    _console.host = WifiHost;
                }
                go.SetActive(true);
                PuttrArcade.Arcade.SharedConsole = _console;
                return _console;
            }
        }

        /// <summary>The console if something has started it, else null (for showing status without starting a radio).</summary>
        public static PuttrConsole Peek => _console;

        /// <summary>-app2SaveTestPutts: send keyboard and simulator putts too. Only for a test account.</summary>
        public static bool SaveTestPutts = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-app2SaveTestPutts") >= 0;

        /// <summary>
        /// Whether a putt arriving now came from a real Puttr. Keyboard test putts and the starter
        /// kit's simulator on this machine are played but never saved: the API is the live one,
        /// and they would land in a real Player's stats and streak.
        /// </summary>
        public static bool RealPutts
        {
            get
            {
                if (Fixtures.On) return false;
                if (SaveTestPutts) return true;
                if (_console == null || !_console.Online) return false;
                string host = _console.ActiveHost ?? "";
                bool simulator = !_console.Bluetooth.Connected && (host.StartsWith("localhost") || host.StartsWith("127."));
                return !simulator;
            }
        }

        const string PrefWifiOnly = "app2.wifiOnly", PrefWifiHost = "app2.wifiHost";

        /// <summary>
        /// Testers only: talk to the Puttr over its Wi-Fi socket (port 3000) and leave Bluetooth off,
        /// so a phone can test a green that another device holds over Bluetooth.
        /// </summary>
        public static bool WifiOnly
        {
            get => PlayerPrefs.GetInt(PrefWifiOnly, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(PrefWifiOnly, value ? 1 : 0);
                PuttrConsole c = Console;
                c.useBluetooth = !value;
                if (value) { c.Bluetooth.Stop(); c.SetHost(WifiHost); }
                else { c.SetHost(""); c.Bluetooth.Start(); }
            }
        }

        /// <summary>The Wi-Fi address a tester chose ("puttr.local", "192.168.1.40", "host:port"); empty searches by name.</summary>
        public static string WifiHost
        {
            get => PlayerPrefs.GetString(PrefWifiHost, "");
            set
            {
                PlayerPrefs.SetString(PrefWifiHost, (value ?? "").Trim());
                if (WifiOnly) Console.SetHost(WifiHost);
            }
        }

        /// <summary>"Puttr over Bluetooth", or null while it is still being looked for.</summary>
        public static string Link => _console != null ? _console.Link : null;
    }
}
