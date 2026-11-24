// The Puttr connection card, from the status pill on Home: puttr-app's BleModal.js.
//
//   Bluetooth chip     enabled / disabled (tap: the phone's settings)
//   The Puttr          connected (and a hole calibration warning), scanning, or not connected (tap to retry)
//   Testers only       a note on keyboard test putts (TestPutts.cs); talk to the Puttr over its Wi-Fi socket instead of
//                      Bluetooth: an address, or find it on the network; and the link to send the Puttr Wi-Fi
//                      details over Bluetooth
using System;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class ConnectSheet
    {
        public static void Open(App app, VisualElement over)
        {
            PuttrConsole console = Green.Console;   // opening the card starts looking, as the app's scan button
            if (console.useBluetooth) console.Bluetooth.Start();
            bool tester = J.Flag(app.Profile, "isTester") || Fixtures.On;

            VisualElement dim = null;
            VisualElement chip = new VisualElement(), state = new VisualElement(), wifi = new VisualElement();
            VisualElement card = Ui.Box("modal connect-card", chip, state);
            if (tester) card.Add(wifi);
            card.Add(Ui.Button("Done", () => dim.RemoveFromHierarchy(), "btn btn-ghost connect-done").Margin(16));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);

            string shown = null;
            Image spinner = null;
            void Refresh()
            {
                string now = Key(console);
                if (now == shown) return;
                shown = now;
                Chip(chip, console);
                spinner = State(state, console);
            }
            Refresh();
            card.schedule.Execute(Refresh).Every(500);
            card.schedule.Execute(() => { if (spinner != null) spinner.style.rotate = new Rotate(Time.realtimeSinceStartup * 240f % 360f); }).Every(30);
            if (tester) Wifi(app, wifi, console, Refresh);
        }

        // What the card shows depends only on this; redraw when it changes.
        static string Key(PuttrConsole c) => c.Link + "|" + c.Bluetooth.State + "|" + c.State + "|" + c.useBluetooth + "|" + c.Version + "|" + c.HasHole + c.HoleIsAssumed + c.HoleCalibrated;

        static bool Searching(PuttrConsole c) => c.useBluetooth
            ? c.Bluetooth.State == BluetoothState.Scanning || c.Bluetooth.State == BluetoothState.Connecting
            : c.State != ConsoleState.Connected;

        static void Chip(VisualElement chip, PuttrConsole c)
        {
            chip.Clear();
            bool wifi = !c.useBluetooth;
            bool on = wifi || c.Bluetooth.Connected || c.Bluetooth.State == BluetoothState.Scanning || c.Bluetooth.State == BluetoothState.Connecting;
            Color tint = on ? Ui.Bright : Ui.Red;
            Label words = Ui.Text(wifi ? "WI-FI SOCKET" : on ? "BLUETOOTH ENABLED" : c.Bluetooth.State == BluetoothState.NotAllowed ? "BLUETOOTH NOT ALLOWED" : "BLUETOOTH DISABLED", "connect-chip-text", Ui.Bold);
            words.style.color = tint;
            VisualElement pill = Ui.Box(on ? "connect-chip" : "connect-chip connect-chip-off", Ui.Icon(wifi ? "WifiHigh" : "Bluetooth", 16, tint), words);
            if (!on) pill.OnTap(Dialog.OpenAppSettings);
            chip.Add(pill);
        }

        static Image State(VisualElement into, PuttrConsole c)
        {
            into.Clear();
            bool connected = c.Link != null;
            bool holeError = connected && c.HasHole && !c.HoleIsAssumed && !c.HoleCalibrated;
            Color tint = connected ? (holeError ? Ui.Amber : Ui.Bright) : Searching(c) ? Color.white : Ui.Red;
            var logo = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite"), tintColor = tint, scaleMode = ScaleMode.ScaleToFit };
            logo.AddToClassList("connect-logo");
            logo.style.transformOrigin = new TransformOrigin(Length.Percent(49.9f), Length.Percent(54.9f));   // turns on its middle disc
            logo.scaleMode = ScaleMode.StretchToFill;
            VisualElement ring = Ui.Box(connected ? (holeError ? "connect-ring connect-amber" : "connect-ring connect-on") : Searching(c) ? "connect-ring" : "connect-ring connect-off", logo);
            into.Add(ring);
            if (connected)
            {
                into.Add(Ui.Text("PUTTR CONNECTED", "connect-title ital", Ui.Bold));
                string version = c.Bluetooth.Connected ? c.Bluetooth.Version : c.Version;
                into.Add(Ui.Text(c.Link + (string.IsNullOrEmpty(version) ? "" : " · v" + version), "connect-sub", Ui.Regular));
                if (holeError) into.Add(HoleError());
                return null;
            }
            if (Searching(c))
            {
                into.Add(Ui.Text("SCANNING...", "connect-title ital", Ui.Bold));
                into.Add(Ui.Text(c.useBluetooth ? "Searching for Puttr" : "Looking for Puttr on " + (string.IsNullOrEmpty(c.host) ? "your Wi-Fi" : c.host), "connect-sub", Ui.Regular));
                return logo;
            }
            into.Add(Ui.Text("NOT CONNECTED", "connect-title ital", Ui.Bold));
            into.Add(Ui.Button("Retry", () => Retry(c), "btn btn-ghost connect-retry"));
            ring.OnTap(() => Retry(c));
            return null;
        }

        static void Retry(PuttrConsole c)
        {
            if (c.useBluetooth) { c.Bluetooth.Stop(); c.Bluetooth.Start(); }
            else c.SetHost(c.host);
        }

        // BleModal's "Hole Calibration Error" with its three fixes.
        static VisualElement HoleError()
        {
            VisualElement box = Ui.Box("connect-warn");
            Label head = Ui.Text("Hole Calibration Error", "connect-warn-title", Ui.Bold);
            box.Add(Ui.Box("row", Ui.Icon("Warning", 20, Ui.Red), head));
            box.Add(Ui.Text("The hole location detected by your Puttr is outside the expected range. This can affect game accuracy.", "body-2", Ui.Regular).Margin(8));
            box.Add(Ui.Text("Every time the Puttr box is plugged in it finds the hole. To fix this:", "body-2", Ui.Bold).Margin(12));
            foreach ((string title, string text) in new[]
            {
                ("Clear the putting surface", "Roll the Puttr carpet out, with nothing on the ramp and no sunlight on it"),
                ("Vacuum the carpet on the ramp", "Dust, hair and marks on the ramp can look like the hole to the camera"),
                ("Restart the Puttr box", "Unplug the box and plug it back in, then wait 2 minutes"),
            })
                box.Add(Ui.Box("connect-step", Ui.Text(title, "body", Ui.Bold), Ui.Text(text, "body-2", Ui.Regular)));
            return box;
        }

        // Testers: the Wi-Fi socket instead of Bluetooth.
        static void Wifi(App app, VisualElement into, PuttrConsole c, Action refresh)
        {
            into.AddToClassList("connect-tester");
            into.Add(Ui.Text("TESTER", "caps caps-green", Ui.Bold));
            into.Add(Ui.Text("Test putts: with no Puttr, putt from a keyboard (Space, or 1 to 6). They are never saved.", "body-2", Ui.Regular).Margin(4));
            VisualElement body = new VisualElement();
            into.Add(Rows.Row("WifiHigh", "Connect over Wi-Fi", null, Rows.Switch(Green.WifiOnly, on => { Green.WifiOnly = on; Draw(); refresh(); })));
            into.Add(body);
            Draw();

            void Draw()
            {
                body.Clear();
                if (!Green.WifiOnly)
                {
                    body.Add(Ui.Text("Uses Bluetooth. Switch on to reach the Puttr over its Wi-Fi socket (port 3000) instead.", "body-2", Ui.Regular).Margin(4));
                    body.Add(Rows.Row("WifiHigh", "Send Wi-Fi details to Puttr", () => app.Push(new WifiScreen(app))));
                    return;
                }
                var field = new TextField { value = Green.WifiHost };
                field.textEdition.placeholder = "puttr.local or 192.168.1.40";
                field.textEdition.hidePlaceholderOnFocus = true;
                field.AddToClassList("field");
                field.Face(Ui.Medium);
                body.Add(field);
                VisualElement found = new VisualElement();
                // Both secondary: this is a tester's panel inside the card, and the card's own buttons are secondary too.
                body.Add(Ui.Box("row connect-pair",
                    Ui.Button("Connect", () => { Green.WifiHost = field.value; refresh(); }, "btn btn-ghost"),
                    Ui.Box("connect-gap"),
                    Ui.Button("Find on network", () => App.Run(Find(c, found, field, refresh)), "btn btn-ghost")));
                body.Add(found);
                body.Add(Ui.Text("Leave the address empty to look for \"puttr\" by name.", "body-2", Ui.Regular).Margin(8));
            }
        }

        // Asks every likely address for its version; tap one that answered to use it.
        static System.Collections.IEnumerator Find(PuttrConsole c, VisualElement into, TextField field, Action refresh)
        {
            into.Clear();
            into.Add(Ui.Text("Looking...", "body-2", Ui.Regular).Margin(8));
            yield return c.Probe();
            into.Clear();
            var answered = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> p in c.Probed) if (p.Value != null) answered.Add(p);
            if (answered.Count == 0) { into.Add(Ui.Text("No Puttr answered on this network.", "body-2", Ui.Medium).Margin(8)); yield break; }
            foreach (KeyValuePair<string, string> p in answered)
            {
                string host = p.Key;
                into.Add(Rows.Row("WifiHigh", host, () => { field.value = host; Green.WifiHost = host; refresh(); }, Rows.Dot(Ui.Bright, "v" + p.Value)));
            }
        }
    }

    /// <summary>
    /// "Puttr not connected" before a game (puttr-app common/BleGate.js): no game starts without a Puttr, unless the
    /// Player has test putts to use instead (a tester, made-up data). And the hole warning (BleInit.js): when a
    /// Puttr connects and the hole it found is outside the normal range, the connect card comes up with the fixes.
    /// </summary>
    public static class ConnectGate
    {
        static bool _open, _warned, _usingTestPutts;

        /// <summary>The rule itself, apart from the device (for the tests). A tester sees the card too, and passes once
        /// they have picked test putts on it.</summary>
        public static bool Blocks(bool disconnected, bool choseTestPutts) => disconnected && !choseTestPutts;

        /// <summary>True when a game may start now. False, and the card is up, when there is no Puttr to putt on.</summary>
        public static bool Clear(App app)
        {
            if (!Blocks(PuttStrip.Disconnected, _usingTestPutts && TestPutts.Available(app.Profile))) return true;
            Show(app);
            return false;
        }

        /// <summary>The card itself, for checking its layout from a script (-app2Open gate).</summary>
        public static void Preview(App app) => Show(app);

        static void Show(App app)
        {
            if (_open) return;
            _open = true;
            Analytics.Track("connect_gate_shown");
            VisualElement dim = null;
            void Close() { _open = false; dim.RemoveFromHierarchy(); }
            string words = !Tv.On ? "Connect to your Puttr console before starting a game. Make sure it is powered on and within range."
                : PuttrBluetooth.Supported
                ? "Turn your Puttr on near this TV. If it is too far for Bluetooth, put it on the same Wi-Fi as this TV: Puttr app, Settings, Connect Puttr to Wi-Fi."
                : "Your Puttr must be on the same Wi-Fi as this TV. Set that up in the Puttr app under Settings, Connect Puttr to Wi-Fi.";
            VisualElement card = Ui.Box("modal gate-card",
                Ui.Box("gate-icon", Ui.Icon("WifiSlash", 32, Ui.Red)),
                Ui.Title("Puttr not connected", "title-20 fb-title"),
                Ui.Text(words, "body-2 center", Ui.Regular).Margin(8),
                Ui.Button("Connect", () => { Close(); ConnectSheet.Open(app, app.Toasts); }).Margin(16));
            // Testers only, until the app is closed: the game is started again from its own button.
            if (TestPutts.Available(app.Profile))
                card.Add(Ui.Button("Use test putts", () => { _usingTestPutts = true; Close(); app.Toast("Test putts on. Tap start again."); }, "btn btn-ghost").Margin(4));
            card.Add(Ui.Button("Dismiss", Close, "btn btn-ghost").Margin(4));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(Close);
            App.Layer(app.Toasts).Add(dim);
            Perf.Wake(2f);
        }

        /// <summary>True when a connected Puttr's hole is outside the normal range (not one the app assumed).</summary>
        public static bool BadHole(bool online, bool hasHole, bool assumed, bool calibrated) => online && hasHole && !assumed && !calibrated;

        /// <summary>Called once the app is up: watches for a Puttr that connects with a bad hole read.</summary>
        public static void Begin(App app)
        {
            app.Toasts.schedule.Execute(() =>
            {
                PuttrConsole c = Green.Peek;
                bool bad = c != null && BadHole(c.Online, c.HasHole, c.HoleIsAssumed, c.HoleCalibrated);
                if (!bad) { _warned = false; return; }   // gone, or read again and good: warn afresh next time
                if (_warned || Fixtures.On) return;
                if (app.Games != null && app.Games.Busy) return;   // not over a game: as soon as the Player is back
                _warned = true;
                Analytics.Track("hole_warning_shown");
                ConnectSheet.Open(app, app.Toasts);
            }).Every(1000);
        }
    }
}
