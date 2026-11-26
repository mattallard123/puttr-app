// Settings, from the gear on Profile: puttr-app's SettingsScreen, less Live Streaming (a test feature).
//
//   The green          Bluetooth and the Puttr, in one row
//   Tools              Setup guide, Green level, Connect Puttr to Wi-Fi, Vertex sensor, Notifications, App settings
//   Developer          testers and admins only: the Phase 0 gate switches (performance numbers, low power, entering competition scores, export)
//   Account            Sign out, Delete account (not for a managed profile)
//
// Also here: the level (the phone's own tilt, as LevelScreen.js), Wi-Fi setup over Bluetooth
// (BleStatus.js: "SSID;;PASSWORD" to 538AB803, then the green is pinged on port 3000), and the
// notification preferences (NotificationSettings.js).
using System;
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace PuttrApp
{
    public static class Dialog
    {
        /// <summary>A yes-or-cancel card over the screen.</summary>
        public static void Ask(VisualElement over, string title, string text, string yes, Action onYes, bool danger = false)
        {
            VisualElement dim = null;
            // A destructive yes is the red button (the slanted shape is drawn in the style sheet's --fill, so the class
            // colours it: an inline background would only tint the box behind the shape).
            VisualElement go = Ui.Button(yes, () => { dim.RemoveFromHierarchy(); onYes(); }, danger ? "btn btn-red" : "btn");
            VisualElement card = Ui.Box("modal", Ui.Title(title, "title-20"), Ui.Text(text, "body-2", Ui.Regular).Margin(8), go,
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
        }

        /// <summary>The phone's settings page for this app.</summary>
        public static void OpenAppSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var uri = new AndroidJavaClass("android.net.Uri"))
            using (AndroidJavaObject data = uri.CallStatic<AndroidJavaObject>("fromParts", "package", activity.Call<string>("getPackageName"), null))
            using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS", data))
                activity.Call("startActivity", intent);
#elif UNITY_IOS && !UNITY_EDITOR
            Application.OpenURL("app-settings:");
#else
            Application.OpenURL("x-apple.systempreferences:com.apple.preference.security?Privacy_Bluetooth");
#endif
        }
    }

    public static class Rows
    {
        /// <summary>One settings row: icon, words, and what sits on the right (a chevron by default).</summary>
        public static VisualElement Row(string icon, string label, Action tap, VisualElement right = null, Color? colour = null)
        {
            Color tint = colour ?? Ui.Faint;
            Label words = Ui.Text(label, "set-title", Ui.Bold);
            if (colour != null) words.style.color = colour.Value;
            // A plain stroke icon (no tile behind it), the row's title, then a chevron unless something else sits there.
            VisualElement row = Ui.Box("set-row", Ui.Icon(Cards.Stroke(icon), 24, tint), Ui.Box("grow set-words", words), right ?? Ui.Icon("CaretRight", 16, Ui.Faint));
            if (tap != null) row.OnTap(tap);
            return row;
        }

        public static VisualElement Card(params VisualElement[] rows)
        {
            VisualElement card = Ui.Box("glass set-card");
            foreach (VisualElement r in rows) if (r != null) card.Add(r);
            // One bordered block, a divider between rows and none under the last.
            if (card.childCount > 0) card[card.childCount - 1].AddToClassList("set-row-last");
            return Ui.Box("pad", card);
        }

        public static VisualElement Dot(Color colour, string text)
        {
            var dot = new VisualElement();
            dot.style.width = dot.style.height = 8;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 4;
            dot.style.backgroundColor = colour;
            dot.style.marginRight = 8;
            Label words = Ui.Text(text, "set-sub", Ui.Medium);
            words.style.color = colour;
            return Ui.Box("row set-right", dot, words);
        }

        public static VisualElement Switch(bool on, Action<bool> flip)
        {
            VisualElement knob = new VisualElement();
            knob.AddToClassList("switch-knob");
            VisualElement track = Ui.Box(on ? "switch switch-on" : "switch", knob);
            bool state = on;
            track.OnTap(() => { state = !state; track.EnableInClassList("switch-on", state); flip(state); });
            return track;
        }
    }

    public sealed class SettingsScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _green, _tools, _developer;
        Label _power, _enter;
        VisualElement _programs;
        bool _lessons;

        public SettingsScreen(App app)
        {
            _app = app;
            _green = new VisualElement(); _tools = new VisualElement(); _developer = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            VisualElement header = Ui.Header("Settings", app.Pop);
            scroll.Add(_green);
            scroll.Add(_tools);
            scroll.Add(_developer);
            if (string.IsNullOrEmpty(J.Str(app.Profile, "managedBy")))
            {
                Color red = Ui.Red;
                scroll.Add(Ui.Section("Account"));
                scroll.Add(Rows.Card(
                    Rows.Row("SignOut", "Sign out", () => Dialog.Ask(Root, "Sign out?", "You can sign back in any time.", "Sign out", app.SignedOut), Ui.Box("set-right")),
                    Rows.Row("Trash", "Delete account", DeleteAccount, Ui.Box("set-right"), red)));
            }
            scroll.Add(Ui.Box("row set-links",
                Ui.Text("Terms & Conditions", "sec-link", Ui.Regular).OnTap(() => Application.OpenURL("https://puttr.co/terms")),
                FreePuttScreen.Space(0).Width(24),
                Ui.Text("Privacy Policy", "sec-link", Ui.Regular).OnTap(() => Application.OpenURL("https://puttr.co/privacy"))));
            // The bundle's own version (Player Settings), so it cannot drift from what the store shows.
            scroll.Add(Ui.Caps("Version " + Application.version, "caps set-center").Margin(12));
            scroll.Add(FreePuttScreen.Space(28));
            // The page as App.PinHeader builds one (the header fixed above the scroll), with one more fixed bar under
            // the scroll: the app's "Need help?" bar, the Caddie. The settings scroll behind it, so help is always
            // there (Matt, 2026-11-22).
            var top = new VisualElement();
            top.AddToClassList("page-viewport");
            top.AddToClassList("pinned-top");
            header.AddToClassList("page-column");
            top.Add(header);
            VisualElement help = Ui.Box("page-viewport set-help", Ui.Box("page-column pad",
                Ui.Box("glass row set-help-card", Ui.Text("Need help?", "set-title grow", Ui.Bold), Ui.IconButton("ChatCircle", "Chat", () => app.Push(new CaddieScreen(app))))));
            help.style.paddingBottom = app.SafeInsets().y + 12f;
            Root = Ui.Box("screen pinned-page", top, scroll, help);
            Tools();
            Developer();
            Root.schedule.Execute(Green).Every(1000);
        }

        public override void Shown() => Green();

        // One row says it all: is the Puttr connected, and if not, why not.
        void Green()
        {
            _green.Clear();
            PuttrConsole console = PuttrApp.Green.Peek;
            PuttrBluetooth ble = console != null ? console.Bluetooth : new PuttrBluetooth();
            string link = console != null ? console.Link : null;
            Color colour; string words; Action tap = null;
            if (link != null) { colour = Ui.Bright; words = "Connected" + (string.IsNullOrEmpty(console.Version) ? "" : " · v" + console.Version); }
            else if (ble.State == BluetoothState.Off) { colour = Ui.Red; words = "Bluetooth is off"; tap = Dialog.OpenAppSettings; }
            else if (ble.State == BluetoothState.NotAllowed) { colour = Ui.Red; words = "Bluetooth not allowed"; tap = Dialog.OpenAppSettings; }
            else if (ble.State == BluetoothState.Scanning || ble.State == BluetoothState.Connecting) { colour = Ui.Amber; words = "Looking for your Puttr"; }
            else { colour = Ui.Faint; words = "Not connected"; }
            _green.Add(Ui.Section("Your Puttr"));
            _green.Add(Rows.Card(Rows.Row("Bluetooth", link ?? "Puttr console", tap, Rows.Dot(colour, words))));
        }

        void Tools()
        {
            _tools.Add(Ui.Section("Tools"));
            VisualElement vertexRight = Vertex.Enabled ? Rows.Dot(Vertex.Connected ? Ui.Bright : Ui.Faint, Vertex.Connected ? "Connected" : "On") : null;
            _tools.Add(Rows.Card(
                Rows.Row("FilmReel", "Setup guide", () => SetupGuide.Play(Root)),
                Rows.Row("Ruler", "Console level", () => _app.Push(new LevelScreen(_app))),
                Rows.Row("WifiHigh", "Connect Puttr to Wi-Fi", () => _app.Push(new WifiScreen(_app))),
                Rows.Row("Lightning", "Vertex sensor", () => _app.Push(new VertexScreen(_app)), vertexRight),
                Rows.Row("BellSimple", "Notifications", () => _app.Push(new NotificationPrefsScreen(_app))),
                Rows.Row("VideoCamera", "Camera setup", () => _app.Push(new CameraSetupScreen(_app))),
                Rows.Row("GearSix", "App settings", Dialog.OpenAppSettings),
                Rows.Row("ChatCircle", "Send feedback", () => Feedback.Open(_app, "settings"))));
            // Hosting (Host.cs): Event Mode and Venue Mode, for Puttr Club. Never on a Kid profile.
            if (Host.Offered(_app.Profile))
            {
                _tools.Add(Ui.Section("Host"));
                _tools.Add(Rows.Card(Host.SettingsRows(_app)));
            }
            _programs = new VisualElement();
            _tools.Add(_programs);
            Programs();
            // "My lessons" only once there is one (a booking, active or past): a new Player has nothing behind the row.
            // As it was last time at once, then as it is now.
            if (!Kid.IsKid(_app.Profile) && !Kid.Managed(_app.Profile))
                App.Run(Api.Kept("/lesson/bookings", d => { bool has = J.List(d).Count > 0; if (has != _lessons) { _lessons = has; Programs(); } }));
        }

        // Coaching and Junior (SettingsScreen's rows): become a coach, Pro, the Academy; Junior for non-members.
        void Programs()
        {
            _programs.Clear();
            if (Kid.Managed(_app.Profile)) return;
            object p = _app.Profile;
            var rows = new List<VisualElement>();
            if (!J.Flag(p, "isCoach") && !J.Flag(p, "coachApproved")) rows.Add(Rows.Row("GraduationCap", "Become a Puttr Coach", () => _app.Push(new CoachLandingScreen(_app))));
            if (Coach.Is(p))
            {
                rows.Add(Rows.Row("Medal", "Puttr Pro for Coaches", () => _app.Push(new CoachLandingScreen(_app))));
                rows.Add(Rows.Row("GraduationCap", "Coach Academy", () => _app.Push(new CoachAcademyScreen(_app))));
                rows.Add(Rows.Row("Medal", "Coach application", () => _app.Push(new CoachApplicationStatusScreen(_app))));
            }
            if (!J.Flag(p, "isMember")) rows.Add(Rows.Row("Trophy", "Junior Membership", () => _app.Push(new JuniorLandingScreen(_app))));
            if (_lessons && !Kid.IsKid(p)) rows.Add(Rows.Row("FilmReel", "My lessons", () => _app.Push(new MyLessonsScreen(_app))));
            if (rows.Count == 0) return;
            _programs.Add(Ui.Section("Coaching"));
            _programs.Add(Rows.Card(rows.ToArray()));
        }

        // The Phase 0 gate switches that used to sit at the bottom of Profile. For testers and Puttr staff
        // only (and the sample data the screen checks run on): a Player has no use for any of them.
        void Developer()
        {
            if (!J.Flag(_app.Profile, "isTester") && !Adm.IsAdmin(_app.Profile) && !Fixtures.On) return;
            _power = Ui.Text("", "set-sub", Ui.Medium);
            _enter = Ui.Text("", "set-sub", Ui.Medium);
            _developer.Add(Ui.Section("Developer"));
            _developer.Add(Rows.Card(
                Rows.Row("Flask", "Performance numbers", _app.TogglePerf),
                Rows.Row("Lightning", "Low power when idle", null, Rows.Switch(Perf.LowPower, on => Perf.LowPower = on)),
                Rows.Row("Medal", "Rehearsal mode", null, Rows.Switch(CompetitionRound.Rehearse, on => CompetitionRound.Rehearse = on)),
                Rows.Row("Flask", "Export my putts for the parity check", () => App.Run(Export(_app)))));
            _developer.Add(Ui.Box("pad", Ui.Text("Rehearsal mode on: tournament and League Night rounds are played as practice and entered nowhere. Off (as for every Player): they go on the live leaderboards, and a tournament round can be played once.", "body-2", Ui.Regular).Margin(8)));
        }

        // Documents/putts-export.json, which the Mac reads off the phone (xcrun devicectl ... copy from).
        static IEnumerator Export(App app)
        {
            app.Toast("Exporting your putts...");
            string token = null, text = null;
            yield return Auth.Token(t => token = t);
            if (token == null) { app.Toast("Sign in first."); yield break; }
            long status = 0;
            yield return Api.Send("GET", Config.ApiUrl + "/profile/analysis/putts/365", null, token, (s, body) => { status = s; text = body; });
            if (status != 200) { app.Toast("Export failed (" + status + "). Check your connection."); yield break; }
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "putts-export.json"), text);
            app.Toast(ProfileMath.Number(J.List(J.Parse(text)).Count) + " putts exported. Tell Claude it is done.");
        }

        void DeleteAccount()
        {
            Dialog.Ask(Root, "Delete your account?", "All your putt history, followers and data will be permanently deleted. This cannot be undone.", "Delete forever", () =>
            {
                string userId = J.Str(_app.Profile, "userId");
                App.Run(Api.Post("/profile/remove", new Dictionary<string, object> { { "userId", userId } },
                    _ => { _app.Toast("Your account was deleted."); _app.SignedOut(); },
                    _ => _app.Toast("That did not work. Check your connection and try again.")));
            }, true);
        }
    }

    /// <summary>The unboxing video, full screen, closed by a tap or when it ends.</summary>
    public static class SetupGuide
    {
        const string Url = "https://storage.googleapis.com/images.puttr.co/howto/Unboxing_2025.mp4";

        public static void Play(VisualElement over) => Play(over, Url);

        /// <summary>Any video, full screen, closed by a tap or when it ends (a game's replay, a league reel).</summary>
        public static void Play(VisualElement over, string url)
        {
            var go = new GameObject("Setup guide");
            var player = go.AddComponent<VideoPlayer>();
            RenderTexture texture = null;
            var audio = go.AddComponent<AudioSource>();
            player.source = VideoSource.Url;
            player.url = url;
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.SetTargetAudioSource(0, audio);
            // The picture keeps the video's own shape (a 9:16 replay stands tall, pillarboxed in black, never
            // stretched): its texture is made at the video's size once prepared, then fitted, not filled.
            var picture = new Image { scaleMode = ScaleMode.ScaleToFit };
            picture.style.position = Position.Absolute;
            picture.style.left = picture.style.top = picture.style.right = picture.style.bottom = 0;
            picture.visible = false;
            picture.pickingMode = PickingMode.Ignore;
            // Solid black with the Puttr spinner until the first frame is on screen.
            VisualElement loading = Spinner.Big();
            VisualElement dim = Ui.Box("modal-dim", picture, loading);
            dim.style.backgroundColor = Color.black;
            dim.style.paddingLeft = dim.style.paddingRight = dim.style.paddingTop = dim.style.paddingBottom = 0;
            bool closed = false;
            void Close()
            {
                if (closed) return;
                closed = true;
                dim.RemoveFromHierarchy();
                UnityEngine.Object.Destroy(go);
                if (texture != null) texture.Release();
            }
            dim.OnTap(Close);
            player.prepareCompleted += p =>
            {
                if (closed) return;
                int w = (int)p.width, h = (int)p.height;
                if (w <= 0 || h <= 0) { w = 1920; h = 1080; }
                if (p.pixelAspectRatioDenominator > 0 && p.pixelAspectRatioNumerator > 0 && p.pixelAspectRatioNumerator != p.pixelAspectRatioDenominator)
                    w = Mathf.RoundToInt(w * (float)p.pixelAspectRatioNumerator / p.pixelAspectRatioDenominator);
                texture = new RenderTexture(w, h, 0);
                texture.Create();
                RenderTexture was = RenderTexture.active;
                RenderTexture.active = texture;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = was;
                p.targetTexture = texture;
                picture.image = texture;
                p.Play();
            };
            // Shown once a frame has been drawn into it, so nothing grey flashes up first.
            dim.schedule.Execute(() =>
            {
                if (closed || picture.visible || !player.isPlaying || player.frame < 1) return;
                picture.visible = true;
                loading.RemoveFromHierarchy();
            }).Every(50);
            player.loopPointReached += _ => Close();
            player.errorReceived += (_, __) => { loading.Clear(); loading.Add(Ui.Text("Could not load the video.", "body-2", Ui.Regular)); };
            player.Prepare();
            App.Layer(over).Add(dim);
            Perf.Wake(600f);
        }
    }

    // ── Green level ──────────────────────────────────────────────────────────

    /// <summary>The phone laid on the mat: a bubble level from its accelerometer (LevelScreen.js).</summary>
    public sealed class LevelScreen : AppScreen
    {
        readonly Bubble _bubble;
        readonly Label _total, _totalWords, _lr, _fb, _pill;

        public LevelScreen(App app)
        {
            _bubble = new Bubble();
            _total = Ui.Text("0.0°", "level-big ital", Ui.Bold);
            _totalWords = Ui.Caps("off level");
            _lr = Ui.Text("0.0°", "level-axis ital", Ui.Bold);
            _fb = Ui.Text("0.0°", "level-axis ital", Ui.Bold);
            _pill = Ui.Text("LEVEL", "level-pill", Ui.Bold);
            VisualElement root = Ui.Box("level-root",
                Ui.Header("Console level", app.Pop),
                Ui.Box("pad", Ui.Box("card center-box", _total, _totalWords)),
                Ui.Box("grow level-stage", _bubble, _pill),
                // Left to right and front to back: one strip, two cells.
                Ui.Box("pad", Ui.Box("strip", Ui.Box("strip-cell strip-cell-first", _lr, Ui.Caps("L / R", "strip-cap")), Ui.Box("strip-cell", _fb, Ui.Caps("F / B", "strip-cap")))),
                Ui.Text("Lay your phone flat on the mat, screen up.", "body-2 center", Ui.Regular).Margin(12),
                FreePuttScreen.Space(24));
            Root = root;
            root.schedule.Execute(Read).Every(40);
        }

        public override void Shown()
        {
            if (UnityEngine.InputSystem.Accelerometer.current != null) UnityEngine.InputSystem.InputSystem.EnableDevice(UnityEngine.InputSystem.Accelerometer.current);
        }

        static string Signed(float v) => Mathf.Abs(v) < 0.1f ? "0.0°" : (v > 0 ? "+" : "") + v.ToString("0.0") + "°";

        void Read()
        {
            Perf.Wake(0.5f);
            var meter = UnityEngine.InputSystem.Accelerometer.current;
            if (meter == null)
            {
                _total.text = "–";
                _totalWords.text = "THIS DEVICE HAS NO TILT SENSOR";
                _lr.text = _fb.text = "–";
                _pill.style.visibility = Visibility.Hidden;
                _bubble.Set(0, 0, false, false);
                return;
            }
            if (!meter.enabled) UnityEngine.InputSystem.InputSystem.EnableDevice(meter);
            Vector3 a = meter.acceleration.ReadValue();
            float x = Mathf.Atan2(a.x, Mathf.Sqrt(a.y * a.y + a.z * a.z)) * Mathf.Rad2Deg;
            float y = Mathf.Atan2(a.y, Mathf.Sqrt(a.x * a.x + a.z * a.z)) * Mathf.Rad2Deg;
            float total = Mathf.Sqrt(x * x + y * y);
            bool level = total < 1f;
            _total.text = level ? "0.0°" : total.ToString("0.0") + "°";
            _total.style.color = level ? Ui.Bright : Color.white;
            _totalWords.text = level ? "PERFECTLY LEVEL" : "OFF LEVEL";
            _lr.text = Signed(x);
            _fb.text = Signed(y);
            _pill.style.visibility = level ? Visibility.Visible : Visibility.Hidden;
            _bubble.Set(x, y, level, total < 3f);
        }

        sealed class Bubble : VisualElement
        {
            float _x, _y; bool _level, _close;

            public Bubble()
            {
                style.flexGrow = 1;
                style.alignSelf = Align.Stretch;
                style.minHeight = 260;
                generateVisualContent += Draw;
            }

            public void Set(float x, float y, bool level, bool close) { _x = x; _y = y; _level = level; _close = close; MarkDirtyRepaint(); }

            void Draw(MeshGenerationContext context)
            {
                Rect r = contentRect;
                float cx = r.width / 2, cy = r.height / 2, R = Mathf.Min(r.width, r.height) * 0.32f;
                if (R <= 0) return;
                Painter2D p = context.painter2D;
                Color line = Ui.Line, green = Ui.Green;
                p.lineWidth = 1.5f;
                p.strokeColor = line;
                p.BeginPath(); p.MoveTo(new Vector2(cx - R * 1.15f, cy)); p.LineTo(new Vector2(cx + R * 1.15f, cy)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(cx, cy - R * 1.15f)); p.LineTo(new Vector2(cx, cy + R * 1.15f)); p.Stroke();
                p.strokeColor = Ui.Faint;
                p.BeginPath(); p.Arc(new Vector2(cx, cy), R, 0, 360); p.Stroke();
                p.strokeColor = line;
                p.BeginPath(); p.Arc(new Vector2(cx, cy), R * 0.66f, 0, 360); p.Stroke();
                p.strokeColor = _level ? green : Ui.Faint;
                p.lineWidth = 2f;
                p.BeginPath(); p.Arc(new Vector2(cx, cy), R * 0.48f, 0, 360); p.Stroke();
                float bubble = R * 0.32f, move = R - bubble;
                Vector2 o = new Vector2(_x / 45f * move, -_y / 45f * move);
                if (o.magnitude > move) o = o.normalized * move;
                p.fillColor = _level ? green : _close ? Color.white : Ui.Soft;
                p.BeginPath(); p.Arc(new Vector2(cx, cy) + o, bubble, 0, 360); p.Fill();
                p.fillColor = Ui.Faint;
                p.BeginPath(); p.Arc(new Vector2(cx, cy), 3, 0, 360); p.Fill();
            }
        }
    }

    // ── Wi-Fi ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends the home network to the Puttr over Bluetooth ("SSID;;PASSWORD" to 538AB803). The Puttr reboots
    /// onto it; nothing comes back over Bluetooth, so success is the Puttr answering on port 3000.
    /// </summary>
    public sealed class WifiScreen : AppScreen
    {
        enum Step { Form, Waiting, Joined, Failed }
        const string Characteristic = "538AB803";
        const float Wait = 150f, Grace = 15f, Every = 5f;

        readonly App _app;
        readonly VisualElement _status, _body;
        readonly TextField _ssid, _password;
        Step _step;
        float _started;
        int _checks;
        string _host;
        Texture2D _snapshot;

        public WifiScreen(App app)
        {
            _app = app;
            _status = new VisualElement(); _body = new VisualElement();
            _ssid = Field("Network name", false);
            _password = Field("Password", true);
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("Connect Puttr to Wi-Fi", Leave));
            scroll.Add(_status);
            scroll.Add(Ui.Section("Local Wi-Fi"));
            scroll.Add(Ui.Box("pad", _body));
            scroll.Add(FreePuttScreen.Space(28));
            Root = scroll;
            Green.Console.Bluetooth.Wrote += Wrote;
            Root.schedule.Execute(Tick).Every(1000);
            Draw();
            App.Run(Ping(true));
        }

        void Leave()
        {
            Green.Console.Bluetooth.Wrote -= Wrote;
            _app.Pop();
        }

        static TextField Field(string placeholder, bool secret)
        {
            var field = new TextField { isPasswordField = secret };
            field.textEdition.placeholder = placeholder;
            field.textEdition.hidePlaceholderOnFocus = false;
            field.AddToClassList("field");
            return field.Face(Ui.Medium);
        }

        void Status()
        {
            _status.Clear();
            PuttrConsole console = Green.Console;
            bool ble = console.Bluetooth.Connected;
            _status.Add(Ui.Section("Your Puttr"));
            _status.Add(Rows.Card(Rows.Row("Bluetooth", ble ? console.Bluetooth.ConnectedName ?? "Puttr" : "Puttr console", ble ? null : (Action)Dialog.OpenAppSettings,
                Rows.Dot(ble ? Ui.Bright : Ui.Faint, ble ? "Connected" + (string.IsNullOrEmpty(console.Bluetooth.Version) ? "" : " · v" + console.Bluetooth.Version) : "Not connected"))));
        }

        void Draw()
        {
            Status();
            _body.Clear();
            VisualElement card = Ui.Box("card");
            switch (_step)
            {
                case Step.Form:
                    bool ble = Green.Console.Bluetooth.Connected;
                    card.Add(_ssid);
                    card.Add(_password);
                    VisualElement send = Ui.Button("Send Wi-Fi to Puttr", Send);
                    send.SetEnabled(ble);
                    card.Add(send);
                    if (!ble) { Label need = Ui.Text("Connect to your Puttr over Bluetooth first.", "body-2", Ui.Medium).Margin(8); need.style.color = Ui.Red; card.Add(need); }
                    card.Add(Ui.Text("Puttr joins 2.4 GHz networks only.", "body-2", Ui.Regular).Margin(8));
                    break;
                case Step.Waiting:
                    int left = Mathf.Max(0, Mathf.CeilToInt(Wait - (Time.realtimeSinceStartup - _started)));
                    card.Add(Ui.Num(left / 60 + ":" + (left % 60).ToString("00"), "num-44 set-center"));
                    card.Add(Ui.Text("Your Puttr is rebooting to join \"" + _ssid.value + "\"...", "body-2", Ui.Regular).Margin(8));
                    if (_checks > 0) card.Add(Ui.Caps("Checked " + _checks + (_checks == 1 ? " time" : " times")).Margin(4));
                    break;
                case Step.Joined:
                    card.Add(Ui.Title("Your Puttr is on your local Wi-Fi", "title-20"));
                    card.Add(Ui.Button("Change network", () => { _step = Step.Form; Draw(); }, "btn btn-ghost"));
                    break;
                case Step.Failed:
                    card.Add(Ui.Title("Puttr didn't reconnect", "title-20"));
                    card.Add(Ui.Text("Double-check the password and make sure your network is 2.4 GHz.", "body-2", Ui.Regular).Margin(8));
                    card.Add(Ui.Button("Try again", () => { _step = Step.Form; Draw(); }));
                    break;
            }
            _body.Add(card);
            if (_step == Step.Joined) Camera();
        }

        // A still from the Puttr's own camera, which support can be sent (BleStatus.js "Camera check").
        void Camera()
        {
            VisualElement card = Ui.Box("card");
            card.style.marginTop = 12;
            card.Add(Ui.Text("CAMERA CHECK", "caps caps-green", Ui.Bold));
            if (_snapshot != null)
            {
                // The Puttr's own picture, in a bordered window, with its buttons under it.
                var image = new Image { image = _snapshot, scaleMode = ScaleMode.ScaleToFit };
                image.style.width = 240; image.style.height = 180;
                VisualElement window = Ui.Box("window", image);
                window.style.alignSelf = Align.FlexStart;
                window.style.marginTop = 8;
                card.Add(window);
            }
            card.Add(Ui.Box("row connect-pair",
                Ui.Button("Update snapshot", () => App.Run(Snapshot()), "btn btn-ghost"),
                Ui.Box("connect-gap"),
                Ui.Button("Send to support", () => App.Run(Support()), "btn btn-ghost")).Margin(12));
            _body.Add(card);
            if (_snapshot == null) App.Run(Snapshot());
        }

        IEnumerator Snapshot()
        {
            if (_host == null) yield break;
            using (UnityWebRequest req = UnityWebRequestTexture.GetTexture("http://" + _host + ":3000/img/cam.jpg?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            {
                req.timeout = 6;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { _app.Toast("Could not reach your Puttr's camera."); yield break; }
                _snapshot = DownloadHandlerTexture.GetContent(req);
            }
            Draw();
        }

        IEnumerator Support()
        {
            if (_host == null) yield break;
            byte[] jpg = null;
            using (UnityWebRequest req = UnityWebRequest.Get("http://" + _host + ":3000/img/cam.jpg?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            {
                req.timeout = 6;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) jpg = req.downloadHandler.data;
            }
            if (jpg == null) { _app.Toast("Could not reach your Puttr's camera."); yield break; }
            string token = null;
            yield return Auth.Token(t => token = t);
            var form = new List<IMultipartFormSection> { new MultipartFormFileSection("file", jpg, "cam.jpg", "image/jpeg") };
            string version = Green.Console.Bluetooth.Version ?? Green.Console.Version;
            if (!string.IsNullOrEmpty(version)) form.Add(new MultipartFormDataSection("greenVersion", version));
            if (!string.IsNullOrEmpty(_ssid.value)) form.Add(new MultipartFormDataSection("ssid", _ssid.value));
            using (UnityWebRequest post = UnityWebRequest.Post(Config.ApiUrl + "/green/supportTicket", form))
            {
                if (token != null) post.SetRequestHeader("Authorization", "Bearer " + token);
                post.timeout = 30;
                yield return post.SendWebRequest();
                _app.Toast(post.result == UnityWebRequest.Result.Success ? "Support ticket created. We'll be in touch by email." : "Could not send it. Try again.");
            }
        }

        void Send()
        {
            string ssid = _ssid.value.Trim(), password = _password.value;
            if (!Green.Console.Bluetooth.Connected) { _app.Toast("Connect to your Puttr over Bluetooth first."); return; }
            if (ssid.Length == 0) { _app.Toast("Enter your network's name."); return; }
            if (ssid.Contains(";;") || password.Contains(";;")) { _app.Toast("The name or password cannot contain ;;"); return; }
            Dialog.Ask(Root, "Send \"" + ssid + "\" to your Puttr?", "It will reboot to connect.", "Send", () => Green.Console.Bluetooth.Write(Characteristic, ssid + ";;" + password));
        }

        void Wrote(string head, bool ok)
        {
            if (head != Characteristic || _step != Step.Form) return;
            if (!ok) { _app.Toast("Send failed. Stay close to your Puttr and try again."); return; }
            _step = Step.Waiting;
            _started = Time.realtimeSinceStartup;
            _checks = 0;
            Draw();
        }

        float _lastCheck;
        void Tick()
        {
            if (_step == Step.Form) { Status(); return; }
            if (_step != Step.Waiting) return;
            float now = Time.realtimeSinceStartup;
            if (now - _started >= Wait) { _step = Step.Failed; Draw(); return; }
            if (now - _started >= Grace && now - _lastCheck >= Every) { _lastCheck = now; _checks++; App.Run(Ping(false)); }
            Draw();
        }

        // The Puttr answers on port 3000 by its name ("puttr") or by Bonjour ("puttr.local"): any answer counts.
        IEnumerator Ping(bool first)
        {
            foreach (string host in new[] { "puttr", "puttr.local" })
            {
                using (UnityWebRequest req = UnityWebRequest.Get("http://" + host + ":3000"))
                {
                    req.timeout = 3;
                    yield return req.SendWebRequest();
                    if (req.responseCode > 0)
                    {
                        _host = host;
                        if (_step == Step.Waiting || (first && _step == Step.Form)) { _step = Step.Joined; Draw(); }
                        yield break;
                    }
                }
            }
        }
    }

    // ── Notification preferences ─────────────────────────────────────────────

    public sealed class NotificationPrefsScreen : AppScreen
    {
        static readonly (string key, string title, string words)[] Kinds =
        {
            ("practice", "Practice reminders", "Daily goal, streak alerts, and milestones"),
            ("competition", "Competition", "League Night, tournaments, and challenges"),
            ("offers", "News and offers", "Puttr Club, new features, and comeback nudges"),
        };

        readonly App _app;
        readonly VisualElement _list;
        object _prefs;

        public NotificationPrefsScreen(App app)
        {
            // A Player who said "Not now" in the onboarding guide is asked here, where they came to turn pushes on.
            if (Push.Later) Push.Register(app, true);
            _app = app;
            _list = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("Notifications", app.Pop));
            scroll.Add(_list);
            scroll.Add(Ui.Box("pad", Ui.Text("Messages about your account, lessons and orders always send.", "body-2", Ui.Regular).Margin(12)));
            Root = scroll;
            _list.Add(Ui.Box("pad", Spinner.Small()));
        }

        public override void Shown()
        {
            App.Run(Api.Get("/notifications/prefs/", data => { _prefs = data; Draw(); }, _ =>
            {
                _list.Clear();
                _list.Add(Ui.Box("pad", Ui.Text("Could not load your notification settings. Check your connection.", "body-2", Ui.Regular)));
            }));
        }

        void Draw()
        {
            _list.Clear();
            var rows = new List<VisualElement>();
            foreach (var kind in Kinds)
            {
                string key = kind.key;
                bool on = J.Get(_prefs, key) == null || J.Flag(_prefs, key);
                VisualElement row = Ui.Box("set-row",
                    Ui.Box("grow set-words", Ui.Text(kind.title, "set-title", Ui.Bold), Ui.Text(kind.words, "set-sub", Ui.Regular)),
                    Rows.Switch(on, value => App.Run(Api.Post("/notifications/prefs/", new Dictionary<string, object> { { key, value } },
                        data => _prefs = data,
                        _ => { _app.Toast("Could not save that. Try again."); Draw(); }))));
                rows.Add(row);
            }
            _list.Add(Rows.Card(rows.ToArray()));
        }
    }
}
