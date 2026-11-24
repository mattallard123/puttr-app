// A 3D session, as puttr-app's coach/mvp/CoachGame.js runs it: six putts filmed, three from down
// the line and three face on, the coach checking the framing and calling each putt, then the
// cloud turns the video into a 3D model of the stroke (LabViewer.cs shows it).
//
//   POST /lab/session/start {handedness}                  -> {insertId}
//   (film on Agora channel LAB_{id}; POST /lab/recording/start once joined: Capture.BeginLab)
//   POST /lab/session/putt {sessionId, ballPath, sensorData, holeBounds, distance, tee, speed, rollout, entry, strokeOffsetMs}
//   POST /lab/session/end {sessionId, score}
//   (2.5 s later the camera stops and POST /lab/recording/stop)
//   GET /lab/session/status/:id until the video is processed (status 4: the 3D annotation is ready)
using System;
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class LabMath
    {
        public const int Putts = 6;

        // coach/mvp/coachLogic.js
        public static readonly string[] MakeCenter = { "Center cut.", "Dead center.", "Pure.", "Right in the heart.", "Painted the middle.", "Flush.", "Perfect line.", "Middle of the cup.", "Bullseye.", "Can't roll it better." };
        public static readonly string[] Make = { "Nice putt.", "Solid.", "Great roll.", "That's the stroke.", "Drained it.", "Money.", "Good speed, good line.", "Keep that feeling.", "In it goes.", "Good putt." };
        public static readonly string[] MakeEdge = { "They all count.", "Snuck it in.", "In the side door.", "Caught the edge, it counts.", "Used the whole cup.", "Leaked in, good pace.", "Edge of the cup, still yours.", "In the door. Keep rolling.", "In. Tighten the line a touch.", "That one fell. Find the middle next." };
        public static readonly string[] Miss = { "Good stroke, stay with it.", "Close. Same line again.", "Almost. Reset and roll it.", "No worries, you're right there.", "Good pace. Tighten the line.", "Shake it off, the next one drops.", "Just a hair off. Keep going.", "Stay smooth, it will fall.", "Right idea. One more.", "Trust it. Next putt." };

        static readonly System.Random Dice = new System.Random();
        static readonly Dictionary<string[], int> Last = new Dictionary<string[], int>();

        static string NoRepeat(string[] pool)
        {
            int i = Dice.Next(pool.Length);
            if (Last.TryGetValue(pool, out int was) && was == i) i = (i + 1) % pool.Length;
            Last[pool] = i;
            return pool[i];
        }

        /// <summary>pickFeedback: a line that matches where the ball went.</summary>
        public static string Feedback(AppPutt.Verdict v)
        {
            if (!v.Made) return NoRepeat(Miss);
            if (v.Description == "CENTER CUT") return NoRepeat(MakeCenter);
            if (v.Description == "LEFT SIDE" || v.Description == "RIGHT SIDE" || v.Description == "BACK DOOR") return NoRepeat(MakeEdge);
            return NoRepeat(Make);
        }

        /// <summary>
        /// The session's own per-putt score (CoachGame getPuttScore): speed 0 to 10 from the rollout,
        /// entry 10 less a point every 10 degrees off the tee's line. APP QUIRK: this line is the
        /// rounded angle without getTeeEntry's 1.1, and a miss scores no entry.
        /// </summary>
        public static (double speed, double entry) Score(int feet, int tee, AppPutt.Verdict v)
        {
            double a = tee == 1 ? -8 : tee == 3 ? 8 : 0;
            double perfect = AppPutt.JsRound(Math.Atan(a / (feet * 12.0)) * (180 / Math.PI));
            double entry = v.Made ? 10 - AppPutt.JsRound(Math.Abs(perfect - v.Entry) / 10) : 0;
            double speed = v.Rollout != null ? AppPutt.SpeedScore(v.Rollout, 0, v.Made) / 5 : 0;
            return (Math.Round(speed, 2), Math.Round(entry, 2));
        }

        /// <summary>The legacy speed the API still keeps in a column (getBallSpeed): pixels climbed a frame before the hole.</summary>
        public static double LegacySpeed(int[][] bp, double[] hb)
        {
            double startY = 500, ct = 0, holeBottom = hb[1] + hb[2] / 2;
            int total = bp.Length;
            for (int i = 0; i < total; i++)
            {
                double y = bp[i][1];
                if (y <= startY && y >= holeBottom)
                {
                    if (y != startY) { if (i > 0) ct += startY - y; startY = y; }
                }
                else { total = i; break; }
            }
            double speed = total > 0 ? ct / total : 0;
            return double.IsNaN(speed) ? 0 : Math.Round(speed, 2);
        }

        /// <summary>The entry as the app sends it: getHoleEntry's [x, y, degrees, quadrant] for a make, 0 for a miss.</summary>
        public static object EntryJson(int[][] bp, double[] hb, bool made)
        {
            if (!made) return 0;
            foreach (int[] p in bp)
            {
                double a = hb[0] - p[0], b = hb[1] - p[1];
                if (Math.Sqrt(a * a + b * b) < hb[2] / 2)
                {
                    double deg = AppPutt.HoleEntry(bp, hb, out _);
                    string quad = (p[1] < hb[1] ? "BACK" : "FRONT") + " " + (p[0] < hb[0] ? "LEFT" : "RIGHT");
                    return new List<object> { p[0], p[1], deg, quad };
                }
            }
            return new List<object> { 0, 0, 0, 0 };
        }
    }

    /// <summary>Before filming: what a 3D session is, the distance, and Start.</summary>
    public sealed class LabIntroScreen : AppScreen
    {
        int _feet = 5;
        readonly VisualElement _chips;
        readonly App _app;

        public LabIntroScreen(App app)
        {
            _app = app;
            _chips = Ui.Box("lab-dist");
            DrawChips();
            Image howIcon = Ui.Icon("FillTarget", 24, Color.white), howGo = Ui.Icon("CaretRight", 16, Ui.Faint);
            howIcon.AddToClassList("lab-how-icon");
            howGo.AddToClassList("lab-how-end");
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad",
                Ui.Caps("3D session · about 90 seconds", "caps caps-green"),
                Ui.Title("Let's see why, not just what.", "title lab-intro-title"),
                Ui.Text("Filming your stroke from down the line and face on lets me see the cause: your eyes, stance, and posture.", "body-2 lab-intro-body", Ui.Medium),
                Ui.Box("card lab-how", howIcon,
                    Ui.Box("lab-how-words", Ui.Text("How to set up", "body", Ui.Bold), Ui.Text("Watch the 30-second guide to position your phone for both angles.", "body-2 lab-line", Ui.Medium)), howGo)
                    .OnTap(() => Application.OpenURL("https://storage.googleapis.com/images.puttr.co/howto/Session_Setup_Final.mp4"))));
            scroll.Add(Ui.Section("Distance"));
            scroll.Add(Ui.Box("pad",
                Ui.Text("Pick your distance. Re-checks compare best at the same distance. Straight, center tee.", "body-2", Ui.Medium),
                _chips,
                Ui.Button("Start putt capture", Start),
                Ui.Text("One time. Re-check monthly or when your numbers drift.", "body-2 center lab-note", Ui.Medium)));
            Root = LabUi.Style(scroll);
        }

        void DrawChips()
        {
            _chips.Clear();
            for (int feet = 3; feet <= 11; feet++)
            {
                int pick = feet;
                _chips.Add(Ui.Box(feet == _feet ? "scope scope-on" : "scope", Ui.Text(feet + " FT", "scope-text", Ui.Bold)).OnTap(() => { _feet = pick; DrawChips(); }));
            }
        }

        void Start()
        {
            if (J.Flag(_app.Profile, "isKid")) { _app.Toast("Video is off for Kid profiles."); return; }
            // CoachGame startGame: no Puttr, no session (the connect sheet instead). Test putts count as a Puttr.
            if (PuttStrip.SaysOffline(_app.Profile)) { ConnectSheet.Open(_app, Root); return; }
            _app.Pop();
            _app.Push(new LabCaptureScreen(_app, _feet));
        }
    }

    /// <summary>
    /// Filming, as CoachGame.js's capture: the live camera fills the screen above a bottom bar, with the
    /// guide drawn over the picture by the native plugin (dashed centre line, skeleton, red/amber/green
    /// frame, the coach's line at the top) while the coach talks the Player into frame. The bar has the
    /// angle, the putt count and progress, Flip and End. With no camera (a Mac, the Simulator) or no
    /// access, the screen says so instead.
    /// </summary>
    public sealed class LabCaptureScreen : AppScreen
    {
        readonly App _app;
        readonly int _feet;
        readonly FramingCheck _framing = new FramingCheck();
        readonly List<AppPutt.Verdict> _putts = new List<AppPutt.Verdict>();
        readonly List<(double speed, double entry)> _scores = new List<(double, double)>();
        readonly VisualElement _picture, _cover, _bar, _fill;
        readonly Label _angle, _count, _last;
        Label _endText;
        string _session, _guideKey;
        bool _ended, _listening, _shown = true, _moving, _leaveAsked, _turnSaid, _camera;
        float _fullAt = -1f;
        Rect _placed = new Rect(-1, -1, -1, -1);
        Action<object> _onPose;
        IVisualElementScheduledItem _ticker;

        const string TurnUpright = "Turn your phone upright. Portrait films your whole setup.";

        public LabCaptureScreen(App app, int feet)
        {
            _app = app;
            _feet = feet;
            _cover = Ui.Box("lab-cover");
            _picture = Ui.Box("lab-picture", _cover);
            _angle = Ui.Caps("");
            _count = Ui.Title("", "title-20 lab-count");
            _last = Ui.Text("", "lab-last", Ui.Medium);
            _fill = Ui.Box("lab-progress-fill");
            // Two secondary buttons: this screen has no main one (the putts are the action).
            VisualElement end = Ui.Button("End", Leave, "btn btn-ghost lab-cap-btn");
            _endText = end.Q<Label>(className: "btn-text");
            _bar = Ui.Box("lab-capbar",
                Ui.Box("lab-caprow",
                    Ui.Box("lab-capwords", _angle, _count),
                    Ui.Button("Flip", () => { if (_camera) Capture.One.SwitchCamera(); }, "btn btn-ghost lab-cap-btn"),
                    end),
                Ui.Box("lab-progress", _fill),
                _last);
            Root = LabUi.Style(Ui.Box("lab-cap", _picture, _bar));
            Root.RegisterCallback<GeometryChangedEvent>(_ => { Pad(); Place(); });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => { if (!_ended) Stop(); });
            _framing.Changed += Draw;
            Draw();

            // -app2LabBar: the filming layout without a camera (a Mac screenshot of the bar and the picture area).
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-app2LabBar") >= 0)
            {
                Cover("VideoCamera", "Starting camera", "Hold on while the camera and the recorder start.");
                return;
            }
            switch (Capture.Has)
            {
                case Capture.CameraAccess.None:
                    _bar.style.display = DisplayStyle.None;
                    Cover("Warning", "Camera not available on this device",
                        "A 3D session films your stroke with your phone's camera. Open Puttr on your iPhone or Android phone to film your baseline.",
                        ("Back", _app.Pop));
                    return;
                case Capture.CameraAccess.Refused:
                    _bar.style.display = DisplayStyle.None;
                    NoAccess();
                    return;
            }
            Cover("VideoCamera", "Starting camera", "Hold on while the camera and the recorder start.");
            App.Run(Open());
        }

        IEnumerator Open()
        {
            string handedness = J.Str(_app.Profile, "handedness").ToUpperInvariant().StartsWith("L") ? "LEFTY" : "RIGHTY";
            object started = null;
            string failed = null;
            // The real session row: 3D sessions are not held (Api.PlayRecords), so this and every write below reach production.
            yield return Api.Post("/lab/session/start", new Dictionary<string, object> { { "handedness", handedness } }, d => started = d, w => failed = w);
            _session = J.Str(started, "insertId");
            if (failed != null || _session.Length == 0)
            {
                _app.Toast(failed == "http_403" ? "Video is off for this profile." : "Could not start the session. Check your connection.");
                _app.Pop();
                yield break;
            }
            if (Root.panel == null) yield break; // left while it started
            _app.EnterPractice();
            Green.Console.distanceFt = _feet;
            Green.Console.tee = 2;
            _camera = true;
            _onPose = pose => _framing.Seen(pose);
            Capture.One.Pose += _onPose;
            Capture.One.Changed += OnCapture;
            Green.Console.Putt += OnPutt;
            _listening = true;
            Capture.One.BeginLab(_session);
            Place();
            CoachVoice.Warm(LabMath.MakeCenter); CoachVoice.Warm(LabMath.Make); CoachVoice.Warm(LabMath.MakeEdge); CoachVoice.Warm(LabMath.Miss);
            CoachVoice.Say("Set your phone behind the ball, facing the hole. Then step back until I can see your whole body.");
            _ticker = Root.schedule.Execute(Tick).Every(100);
            Perf.Mark("3d session " + _session + " started");
        }

        // ── The picture ─────────────────────────────────────────────────────

        /// <summary>The native preview over the picture area, or hidden when something of the app's must show there.</summary>
        void Place()
        {
            bool show = _camera && _shown && !_ended && !_moving && Root.panel != null
                && Capture.One.Now != Capture.State.Off && Capture.One.Now != Capture.State.Failed;
            Rect share = new Rect(0, 0, 0, 0);
            if (show)
            {
                Rect all = Root.panel.visualTree.layout, r = _picture.worldBound;
                if (all.width <= 0 || all.height <= 0 || r.width <= 0 || r.height <= 0) return;
                share = new Rect(r.x / all.width, r.y / all.height, r.width / all.width, r.height / all.height);
            }
            if (share == _placed) return;
            _placed = share;
            Capture.One.Place(share);
        }

        /// <summary>The bar clears the home indicator.</summary>
        void Pad()
        {
            Vector4 safe = _app.SafeInsets();
            _bar.style.paddingBottom = 12 + safe.y;
            _bar.style.paddingLeft = 16 + safe.x;
            _bar.style.paddingRight = 16 + safe.z;
            _cover.style.marginTop = safe.w;
        }

        public override void Shown() { _shown = true; Place(); }
        public override void Hidden() { _shown = false; Place(); }

        void OnCapture()
        {
            if (_ended) return;
            if (Capture.One.Now == Capture.State.Failed)
            {
                if (Capture.One.Refused) NoAccess();
                else Cover("Warning", "The camera did not start", Capture.One.Problem ?? "Something stopped the camera.",
                    ("Try again", Retry), ("Leave", Leave));
            }
            Place();
            Draw();
        }

        void Retry()
        {
            if (_session == null || _ended) return;
            Cover("VideoCamera", "Starting camera", "Hold on while the camera and the recorder start.");
            _guideKey = null;
            Capture.One.BeginLab(_session);
            Place();
        }

        void NoAccess()
        {
            var actions = new List<(string, Action)>();
            if (Application.platform == RuntimePlatform.IPhonePlayer) actions.Add(("Open Settings", () => Application.OpenURL("app-settings:")));
            actions.Add(("Back", Leave));
            Cover("VideoCamera", "Camera access is off", "Puttr needs the camera to film your stroke. Turn on Camera for Puttr in Settings, then start again.", actions.ToArray());
        }

        /// <summary>What the picture area shows when the camera is not over it.</summary>
        void Cover(string icon, string title, string body, params (string label, Action tap)[] actions)
        {
            _cover.Clear();
            _cover.Add(Ui.Icon(icon, 32, Ui.Bright));
            _cover.Add(Ui.Title(title, "title-20 lab-cover-title"));
            if (!string.IsNullOrEmpty(body)) _cover.Add(Ui.Text(body, "body-2 lab-cover-body", Ui.Medium));
            VisualElement buttons = Ui.Box("lab-cover-actions");
            for (int i = 0; i < actions.Length; i++) buttons.Add(Ui.Button(actions[i].label, actions[i].tap, i == 0 ? "btn" : "btn btn-ghost"));
            if (actions.Length > 0) _cover.Add(buttons);
        }

        // ── The guide over the picture, ten times a second ──────────────────

        void Tick()
        {
            if (_ended) return;
            Place();
            float now = Time.realtimeSinceStartup;
            string line;
            Color frame = Color.clear;
            float progress = 0f;
            bool confirmed = false;
            string waiting = Capture.One.Waiting();
            if (waiting != null) line = waiting;
            else if (Capture.NoteInGuide && now < _noteUntil) { line = _noteTitle + "\n" + _noteBody; frame = _noteGood ? Ui.Green : Ui.Red; }
            else if (Screen.width > Screen.height)
            {
                // CoachGame: the recording is a portrait strip, so the coach asks for the phone upright, once a turn.
                line = TurnUpright;
                if (!_turnSaid) { _turnSaid = true; CoachVoice.Say("Turn your device upright. Portrait captures your whole setup."); }
            }
            else
            {
                _turnSaid = false;
                _framing.Tick();
                confirmed = _framing.Confirmed;
                if (confirmed) { line = "Framing good"; _fullAt = -1f; }
                else if (_framing.Status == FramingStatus.Full)
                {
                    if (_fullAt < 0f) _fullAt = now;
                    line = "Hold still...";
                    progress = Mathf.Clamp01((now - _fullAt) / 2f);
                }
                else { _fullAt = -1f; line = _framing.Hint; }
                if (!confirmed) frame = _framing.Colour;
            }
            string key = line + "|" + frame + "|" + Mathf.RoundToInt(progress * 20) + "|" + confirmed;
            if (key == _guideKey) return;
            _guideKey = key;
            Capture.One.Guide(line, frame, progress, confirmed);
        }

        // ── Putts ───────────────────────────────────────────────────────────

        string _noteTitle, _noteBody;
        bool _noteGood;
        float _noteUntil;

        void OnPutt(PuttResult putt)
        {
            if (_ended || _session == null) return;
            if (!Capture.One.Ready) { _app.Toast("Recording is not running yet: that putt was not counted."); return; }
            int[][] bp = AppPutt.Path(putt.Path);
            double[] hb = AppPutt.Hole(putt.Hole);
            AppPutt.Verdict v = AppPutt.Judge(bp, hb, _feet, 2);
            if (!v.Ball || v.RollUnder) return;
            _framing.PuttSeen();
            _putts.Add(v);
            _scores.Add(LabMath.Score(_feet, 2, v));
            float offset = (Time.realtimeSinceStartup - Capture.One.RecordingStartedAt) * 1000f;
            App.Run(Api.Post("/lab/session/putt", new Dictionary<string, object>
            {
                { "sessionId", _session }, { "ballPath", RoundSaver.PathJson(putt.Path) }, { "sensorData", null }, { "holeBounds", RoundSaver.HoleJson(putt.Hole) },
                { "distance", _feet }, { "tee", 2 }, { "speed", LabMath.LegacySpeed(bp, hb) }, { "rollout", v.Rollout },
                { "entry", J.Write(LabMath.EntryJson(bp, hb, v.Made)) }, { "strokeOffsetMs", null },
            }, null, w => Perf.Note("lab: putt not saved (" + w + ")")));
            Perf.Note("lab: putt " + _putts.Count + " at " + Mathf.RoundToInt(offset) + " ms");

            // Seen and saved: a card over the picture says which putt it was and what became of it (Matt, 2026-11-24).
            string what = string.IsNullOrEmpty(v.Description) ? "" : " · " + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(v.Description.ToLowerInvariant());
            _noteTitle = "Putt " + _putts.Count + " of " + LabMath.Putts + " recorded";
            _noteBody = (v.Made ? "Made" : "Missed") + what;
            _noteGood = v.Made;
            _noteUntil = Time.realtimeSinceStartup + 2.2f;
            Capture.One.Note(_noteTitle, _noteBody, v.Made);
            _guideKey = null;

            if (_putts.Count == 3)
            {
                CoachVoice.Say("Nice. Now move your phone in front of you, facing you, for three more.");
                _framing.Reset();
                Move();
            }
            else CoachVoice.Say(LabMath.Feedback(v));
            if (_putts.Count >= LabMath.Putts) App.Run(End());
            Draw();
        }

        /// <summary>After putt 3 (CoachGame's camera modal): face on, for three seconds or until tapped; the camera keeps recording.</summary>
        void Move()
        {
            if (_putts.Count >= LabMath.Putts) return;
            _moving = true;
            Cover("VideoCamera", "Face on", "Move your camera to face you from the front.", ("Got it", EndMove));
            Place();
            Root.schedule.Execute(EndMove).StartingIn(3000);
        }

        void EndMove()
        {
            if (!_moving) return;
            _moving = false;
            _guideKey = null;
            Place();
        }

        IEnumerator End()
        {
            _ended = true;
            Stop();
            double total = 0;
            foreach ((double speed, double entry) s in _scores) total += s.speed + s.entry;
            double score = _scores.Count > 0 ? total / _scores.Count : 0;
            yield return Api.Post("/lab/session/end", new Dictionary<string, object> { { "sessionId", _session }, { "score", score } }, null, null);
            Perf.Mark("3d session " + _session + " ended, score " + score.ToString("0.00"));
            yield return new WaitForSecondsRealtime(1.2f);
            _app.LeavePractice();
            _app.Pop();
            _app.Push(new LabProcessingScreen(_app, _session, _putts, score));
        }

        /// <summary>Stops listening and filming: the recorder runs 2.5 s more, then POST /lab/recording/stop (Capture.CloseLab).</summary>
        void Stop()
        {
            _ticker?.Pause();
            Capture.One.Place(new Rect(0, 0, 0, 0));
            _placed = new Rect(0, 0, 0, 0);
            if (!_listening) return;
            _listening = false;
            Green.Console.Putt -= OnPutt;
            if (_onPose != null) Capture.One.Pose -= _onPose;
            Capture.One.Changed -= OnCapture;
            Capture.One.End();
        }

        void Leave()
        {
            // Mid-session, End asks once (CoachGame's BackButton confirm): a second tap within three seconds leaves.
            if (_putts.Count > 0 && !_ended && !_leaveAsked)
            {
                _leaveAsked = true;
                _endText.text = "LEAVE?";
                Root.schedule.Execute(() => { _leaveAsked = false; _endText.text = "END"; }).StartingIn(3000);
                return;
            }
            _ended = true;
            Stop();
            CoachVoice.Hush();
            if (_camera) _app.LeavePractice();
            _app.Pop();
        }

        void Draw()
        {
            bool faceOn = _putts.Count >= 3;
            _angle.text = (faceOn ? "FACE ON" : "DOWN THE LINE") + "   ·   " + _feet + " FT STRAIGHT";
            _count.text = "PUTT " + Mathf.Min(_putts.Count + 1, LabMath.Putts) + " OF " + LabMath.Putts;
            _fill.style.width = Length.Percent(100f * _putts.Count / LabMath.Putts);
            if (_putts.Count == 0) { _last.text = "Putts count once the camera is recording."; _last.style.color = Ui.Faint; return; }
            AppPutt.Verdict last = _putts[_putts.Count - 1];
            string what = string.IsNullOrEmpty(last.Description) ? "" : "   ·   " + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(last.Description.ToLowerInvariant());
            _last.text = "Putt " + _putts.Count + ": " + (last.Made ? "Made" : "Missed") + what;
            _last.style.color = last.Made ? Ui.Bright : Ui.Red;
        }
    }

    /// <summary>After filming: the cloud's progress, until the 3D session can be opened.</summary>
    public sealed class LabProcessingScreen : AppScreen
    {
        readonly App _app;
        readonly string _session;
        readonly Label _status, _note;
        readonly VisualElement _action;

        public LabProcessingScreen(App app, string session, List<AppPutt.Verdict> putts, double score)
        {
            _app = app;
            _session = session;
            int made = 0;
            foreach (AppPutt.Verdict v in putts) if (v.Made) made++;
            _status = Ui.Title("Uploading video...", "title-20 lab-done-status");
            _note = Ui.Text("You can leave this screen. The 3D session appears in Improve when it is ready.", "body-2 center lab-intro-body", Ui.Medium);
            _action = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad",
                Ui.Box("lab-done",
                    Ui.Caps("Session complete", "caps caps-green"),
                    Ui.Num(made + " of " + putts.Count, "num-44 lab-done-num"),
                    Ui.Caps("made · session score " + score.ToString("0.0"))),
                Ui.Box("card lab-done-card", _status, _note),
                _action));
            Root = LabUi.Style(scroll);
            if (Api.IsLocal(session))
            {
                // A local id would only come from a development build that held /lab/session (it is never in
                // Api.PlayRecords): nothing was uploaded for the cloud to process. A real id is polled below.
                _status.text = Api.NotSaved.ToUpperInvariant();
                _note.text = "The camera filmed your putts, but this session was held on this device, so there is nothing to process.";
                return;
            }
            Root.schedule.Execute(Poll).Every(2000);
            Poll();
        }

        bool _ready;

        void Poll()
        {
            if (_ready) return;
            App.Run(Api.Get("/lab/session/status/" + _session, data =>
            {
                object row = J.First(data);
                if (row == null) { _status.text = "UPLOADING VIDEO..."; return; }
                int status = J.Int(row, "status");
                _status.text = status <= 1 ? "PROCESSING VIDEO..." : status == 2 ? "ANALYZING VIDEO..." : "YOUR 3D SESSION IS READY";
                if (status >= 3 && !_ready)
                {
                    _ready = true;
                    _action.Add(Ui.Button("View session", () => { _app.Pop(); _app.Push(new LabSessionScreen(_app, row)); }));
                }
            }));
        }
    }

    /// <summary>
    /// Every 3D session filmed, newest first (puttr-app LabScreen with SessionRowCard): Record a 3D session, then one block
    /// with a row per session: its thumbnail, date, LATEST on the newest, how it was filmed ("Good capture"), how long ago, the
    /// score and side (or Processing...), Club data when a Vertex rode along, and a bin that deletes it after a yes.
    /// GET /lab/sessions; GET /lab/session/delete/:id (the app's delete is a GET that sets status 5).
    /// </summary>
    public sealed class LabSessionsScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _list;
        int _asked;

        public LabSessionsScreen(App app)
        {
            _app = app;
            _list = new VisualElement();
            VisualElement record = Ui.Button("Record a 3D session", () => app.Push(new LabIntroScreen(app)), "btn lab-record");
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("3D sessions", app.Pop));
            scroll.Add(Ui.Box("pad", record));
            scroll.Add(Ui.Section("Previous sessions"));
            scroll.Add(Ui.Box("pad", _list));
            scroll.Add(FreePuttScreen.Space(32));
            Root = LabUi.Style(scroll);
            _list.Add(Page.Loading());
        }

        // The app reloads the list every time the screen comes back (a session may have finished processing).
        public override void Shown() => Load();

        void Load()
        {
            int my = ++_asked;
            App.Run(Api.Get("/lab/sessions", data => { if (my == _asked) Draw(J.List(data)); }, _ =>
            {
                if (my != _asked) return;
                _list.Clear();
                _list.Add(Ui.Text("Could not load your sessions.", "lab-empty-text", Ui.Medium));
            }));
        }

        void Draw(List<object> rows)
        {
            _list.Clear();
            if (rows.Count == 0)
            {
                _list.Add(Ui.Box("lab-empty", Ui.Icon("VideoCamera", 24, Ui.Faint),
                    Ui.Text("No sessions yet", "body lab-empty-title", Ui.Bold), Ui.Text("Start a session to record your stroke", "body-2 center", Ui.Medium)));
                return;
            }
            rows.Sort((a, b) => Nullable.Compare(ProfileMath.Date(b, "created_at"), ProfileMath.Date(a, "created_at")));
            // One bordered block, a row a session.
            VisualElement block = Ui.Box("lab-block");
            for (int i = 0; i < rows.Count; i++) block.Add(Row(rows[i], i == 0));
            _list.Add(block);
        }

        /// <summary>SessionRowCard.</summary>
        VisualElement Row(object s, bool latest)
        {
            int status = J.Int(s, "status", 4);
            bool processing = J.Get(s, "status") != null && status < 4;
            LabCapture.Info capture = LabCapture.Describe(LabCapture.Parse(s));

            VisualElement thumb = Images.Cover(Ui.Box("lab-thumb", Ui.Icon("VideoCamera", 20, Ui.Faint)));
            string thumbUrl = J.Str(s, "thumbUrl");
            if (thumbUrl.StartsWith("http")) { thumb.Clear(); Images.Into(thumb, thumbUrl); }

            DateTime? at = ProfileMath.Date(s, "created_at");
            string day = at?.ToLocalTime().ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
            string name = J.Str(s, "playerName");
            string title = name.Length > 0 ? name + (day != null ? " \u00B7 " + day : "") : day ?? "Session";
            VisualElement top = Ui.Box("row lab-row-top", Ui.Text(title, "lab-row-title", Ui.Bold));
            if (latest) top.Add(Ui.Caps("Latest", "caps caps-green lab-latest"));
            if (!processing && capture != null)
            {
                // How it was filmed: the words in green or amber, no ground behind them.
                Label how = Ui.Caps(capture.Short, "caps lab-capture-text");
                how.style.color = capture.Ok ? Ui.Bright : Ui.Amber;
                top.Add(Ui.Box("lab-capture", Ui.Icon(capture.Ok ? "CheckCircle" : "Warning", 12, capture.Ok ? Ui.Bright : Ui.Amber), how));
            }

            // One quiet line: the score, the side, club data, or that the cloud is still working.
            var meta = new List<string>();
            if (J.Get(s, "score") != null) meta.Add("Score " + J.Num(s, "score").ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            if (J.Str(s, "handedness").Length > 0) meta.Add(J.Str(s, "handedness") == "LEFTY" ? "Lefty" : "Righty");
            bool club = J.Int(s, "hasClubData") == 1 || J.List(J.Get(s, "putts")).Exists(p => J.Get(p, "sensorData") != null);
            if (club) meta.Add("Club data");
            if (processing) meta.Add("Processing...");

            VisualElement words = Ui.Box("lab-srow-words", top);
            if (at != null) words.Add(Ui.Text(LabUi.Ago(s), "body-2 lab-row-ago", Ui.Medium));
            if (meta.Count > 0) words.Add(Ui.Text(string.Join(" · ", meta), "body-2 lab-row-ago", Ui.Medium));

            object session = s;
            VisualElement bin = Ui.Box("lab-bin", Ui.Icon("Trash", 20, Ui.Faint)).OnTap(() => Delete(session));
            bin.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            return Ui.Box(latest ? "lab-srow lab-srow-first" : "lab-srow", thumb, words, bin).OnTap(() => _app.Push(new LabSessionScreen(_app, session)));
        }

        void Delete(object s)
        {
            Dialog.Ask(Root, "Delete session", "Are you sure you want to delete this session?", "Delete", () =>
                App.Run(Api.Get("/lab/session/delete/" + J.Str(s, "id"), _ => Load(), _ => _app.Toast("Could not delete the session. Check your connection."))), true);
        }
    }
}
