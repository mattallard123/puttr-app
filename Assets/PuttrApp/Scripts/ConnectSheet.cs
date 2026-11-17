// The Puttr connection card, from the status pill on Home: puttr-app's BleModal.js.
//
//   Bluetooth chip     enabled / disabled (tap: the phone's settings)
//   The Puttr          connected (and a hole calibration warning), scanning, or not connected (tap to retry)
//   Testers only       talk to the Puttr over its Wi-Fi socket instead of Bluetooth: an address, or find it on
//                      the network; and the link to send the Puttr Wi-Fi details over Bluetooth
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
            card.Add(Ui.Button("Done", () => dim.RemoveFromHierarchy(), "btn btn-ghost connect-done").Margin(14));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
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
            Label words = Ui.Text(wifi ? "WI-FI SOCKET" : on ? "BLUETOOTH ENABLED" : c.Bluetooth.State == BluetoothState.NotAllowed ? "BLUETOOTH NOT ALLOWED" : "BLUETOOTH DISABLED", "connect-chip-text", Ui.Medium);
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
                into.Add(Ui.Text("PUTTR CONNECTED", "connect-title", Ui.Bold));
                string version = c.Bluetooth.Connected ? c.Bluetooth.Version : c.Version;
                into.Add(Ui.Text(c.Link + (string.IsNullOrEmpty(version) ? "" : " · v" + version), "connect-sub", Ui.Regular));
                if (holeError) into.Add(HoleError());
                return null;
            }
            if (Searching(c))
            {
                into.Add(Ui.Text("SCANNING...", "connect-title", Ui.Bold));
                into.Add(Ui.Text(c.useBluetooth ? "Searching for Puttr" : "Looking for Puttr on " + (string.IsNullOrEmpty(c.host) ? "your Wi-Fi" : c.host), "connect-sub", Ui.Regular));
                return logo;
            }
            into.Add(Ui.Text("NOT CONNECTED", "connect-title", Ui.Bold));
            into.Add(Ui.Box("connect-retry", Ui.Text("Tap to retry", "connect-chip-text", Ui.Medium)).OnTap(() => Retry(c)));
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
            Label head = Ui.Text("Hole Calibration Error", "feed-name", Ui.Bold);
            head.style.color = Ui.Red;
            box.Add(Ui.Box("row", Ui.Icon("Warning", 18, Ui.Red), head));
            box.Add(Ui.Text("The hole location detected by your Puttr is outside the expected range. This can affect game accuracy.", "muted", Ui.Regular).Margin(6));
            box.Add(Ui.Text("Every time the Puttr box is plugged in it finds the hole. To fix this:", "sub", Ui.Bold).Margin(10));
            foreach ((string title, string text) in new[]
            {
                ("Clear the putting surface", "Roll the Puttr carpet out, with nothing on the ramp and no sunlight on it"),
                ("Vacuum the carpet on the ramp", "Dust, hair and marks on the ramp can look like the hole to the camera"),
                ("Restart the Puttr box", "Unplug the box and plug it back in, then wait 2 minutes"),
            })
                box.Add(Ui.Box("connect-step", Ui.Text(title, "feed-name", Ui.Medium), Ui.Text(text, "muted", Ui.Regular)));
            return box;
        }

        // Testers: the Wi-Fi socket instead of Bluetooth.
        static void Wifi(App app, VisualElement into, PuttrConsole c, Action refresh)
        {
            into.AddToClassList("connect-tester");
            into.Add(Ui.Text("TESTER", "kick", Ui.Bold));
            VisualElement body = new VisualElement();
            into.Add(Rows.Row("WifiHigh", "Connect over Wi-Fi", null, Rows.Switch(Green.WifiOnly, on => { Green.WifiOnly = on; Draw(); refresh(); })));
            into.Add(body);
            Draw();

            void Draw()
            {
                body.Clear();
                if (!Green.WifiOnly)
                {
                    body.Add(Ui.Text("Uses Bluetooth. Switch on to reach the Puttr over its Wi-Fi socket (port 3000) instead.", "muted", Ui.Regular).Margin(4));
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
                body.Add(Ui.Box("row",
                    Ui.Button("Connect", () => { Green.WifiHost = field.value; refresh(); }, "btn grow"),
                    FreePuttScreen.Space(0).Width(8),
                    Ui.Button("Find on network", () => App.Run(Find(c, found, field, refresh)), "btn btn-ghost grow")).Margin(8));
                body.Add(found);
                body.Add(Ui.Text("Leave the address empty to look for \"puttr\" by name.", "muted", Ui.Regular).Margin(6));
            }
        }

        // Asks every likely address for its version; tap one that answered to use it.
        static System.Collections.IEnumerator Find(PuttrConsole c, VisualElement into, TextField field, Action refresh)
        {
            into.Clear();
            into.Add(Ui.Text("Looking...", "muted", Ui.Regular).Margin(6));
            yield return c.Probe();
            into.Clear();
            var answered = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> p in c.Probed) if (p.Value != null) answered.Add(p);
            if (answered.Count == 0) { into.Add(Ui.Text("No Puttr answered on this network.", "sub", Ui.Medium).Margin(6)); yield break; }
            foreach (KeyValuePair<string, string> p in answered)
            {
                string host = p.Key;
                into.Add(Rows.Row("WifiHigh", host, () => { field.value = host; Green.WifiHost = host; refresh(); }, Rows.Dot(Ui.Bright, "v" + p.Value)));
            }
        }
    }
}
