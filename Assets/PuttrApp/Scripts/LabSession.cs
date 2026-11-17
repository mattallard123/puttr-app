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
            _chips = Ui.Box("tee-grid");
            DrawChips();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad",
                Ui.Text("3D SESSION   ·   ABOUT 90 SECONDS", "kick", Ui.Bold),
                Ui.Text("Let's see why, not just what.", "h1", Ui.Bold),
                Ui.Text("Filming your stroke from down the line and face on lets me see the cause: your eyes, stance, and posture.", "sub", Ui.Regular),
                Ui.Box("glass list-row", Ui.Bubble("FillTarget", Ui.Bright), FreePuttScreen.Space(12),
                    Ui.Box("grow", Ui.Text("How to set up", "feed-name", Ui.Bold), Ui.Text("Watch the 30-second guide to position your phone for both angles.", "tile-desc", Ui.Regular)))
                    .OnTap(() => Application.OpenURL("https://storage.googleapis.com/images.puttr.co/howto/Session_Setup_Final.mp4")).Margin(14),
                Ui.Text("DISTANCE", "kick", Ui.Bold).Margin(16),
                Ui.Text("Pick your distance. Re-checks compare best at the same distance. Straight, center tee.", "muted", Ui.Regular),
                _chips,
                Ui.Button("Start putt capture", Start),
                Ui.Text("One time. Re-check monthly or when your numbers drift.", "muted center", Ui.Regular).Margin(10)));
            Root = scroll;
        }

        void DrawChips()
        {
            _chips.Clear();
            for (int feet = 3; feet <= 11; feet++)
            {
                int pick = feet;
                _chips.Add(Ui.Box(feet == _feet ? "tee-cell tee-on" : "tee-cell", Ui.Text(feet + " ft", "tee-number", Ui.Bold)).OnTap(() => { _feet = pick; DrawChips(); }));
            }
        }

        void Start()
        {
            if (J.Flag(_app.Profile, "isKid")) { _app.Toast("Video is off for Kid profiles."); return; }
            if (!Capture.Supported) { _app.Toast("3D sessions need the phone app: the camera is not available here."); return; }
            _app.Pop();
            _app.Push(new LabCaptureScreen(_app, _feet));
        }
    }

    /// <summary>Filming: the camera on top, and below it the framing check, the putt count and the last putt.</summary>
    public sealed class LabCaptureScreen : AppScreen
    {
        readonly App _app;
        readonly int _feet;
        readonly FramingCheck _framing = new FramingCheck();
        readonly ScrollView _scroll;
        readonly List<AppPutt.Verdict> _putts = new List<AppPutt.Verdict>();
        readonly List<(double speed, double entry)> _scores = new List<(double, double)>();
        string _session;
        bool _ended, _listening;
        Action<object> _onPose;

        const float CameraShare = 0.6f; // the top of an upright phone shows the camera

        public LabCaptureScreen(App app, int feet)
        {
            _app = app;
            _feet = feet;
            _scroll = Ui.Scroll();
            VisualElement space = new VisualElement();
            space.style.height = Length.Percent(CameraShare * 100f);
            space.style.flexShrink = 0;
            Root = Ui.Box("lab", space, _scroll);
            _framing.Changed += Draw;
            Draw();
            App.Run(Open());
        }

        IEnumerator Open()
        {
            string handedness = J.Str(_app.Profile, "handedness").ToUpperInvariant().StartsWith("L") ? "LEFTY" : "RIGHTY";
            object started = null;
            string failed = null;
            yield return Api.Post("/lab/session/start", new Dictionary<string, object> { { "handedness", handedness } }, d => started = d, w => failed = w);
            _session = J.Str(started, "insertId");
            if (failed != null || _session.Length == 0)
            {
                _app.Toast(failed == "http_403" ? "Video is off for this profile." : "Could not start the session. Check your connection.");
                _app.Pop();
                yield break;
            }
            _app.EnterPractice();
            Green.Console.distanceFt = _feet;
            Green.Console.tee = 2;
            Capture.One.Place(new Rect(0.04f, 0.04f, 0.92f, CameraShare - 0.06f));
            Capture.One.BeginLab(_session);
            _onPose = pose => _framing.Seen(pose);
            Capture.One.Pose += _onPose;
            Capture.One.Changed += Draw;
            Green.Console.Putt += OnPutt;
            _listening = true;
            CoachVoice.Warm(LabMath.MakeCenter); CoachVoice.Warm(LabMath.Make); CoachVoice.Warm(LabMath.MakeEdge); CoachVoice.Warm(LabMath.Miss);
            CoachVoice.Say("Set your phone behind the ball, facing the hole. Then step back until I can see your whole body.");
            Root.schedule.Execute(Tick).Every(100);
            Perf.Mark("3d session " + _session + " started");
        }

        void Tick()
        {
            if (_ended || Capture.One.Now != Capture.State.Recording) return;
            _framing.Tick();
            Capture.One.Border(_framing.Colour, _framing.Confirmed ? 3f : 6f);
        }

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

            if (_putts.Count == 3)
            {
                CoachVoice.Say("Nice. Now move your phone in front of you, facing you, for three more.");
                _framing.Reset();
            }
            else CoachVoice.Say(LabMath.Feedback(v));
            if (_putts.Count >= LabMath.Putts) App.Run(End());
            Draw();
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

        void Stop()
        {
            if (!_listening) return;
            _listening = false;
            Green.Console.Putt -= OnPutt;
            if (_onPose != null) Capture.One.Pose -= _onPose;
            Capture.One.Changed -= Draw;
            Capture.One.End();
        }

        void Leave()
        {
            Stop();
            CoachVoice.Hush();
            _app.LeavePractice();
            _app.Pop();
        }

        void Draw()
        {
            _scroll.Clear();
            bool faceOn = _putts.Count >= 3;
            _scroll.Add(Ui.Box("sheet-card row",
                Ui.Box("grow", Ui.Text((faceOn ? "FACE ON" : "DOWN THE LINE") + "   ·   " + _feet + " FT STRAIGHT", "kick", Ui.Bold),
                    Ui.Text("Putt " + Mathf.Min(_putts.Count + 1, LabMath.Putts) + " of " + LabMath.Putts, "h2", Ui.Bold).Margin(2)),
                Ui.Box("btn-small", Ui.Text("Flip", "btn-text", Ui.Bold)).OnTap(() => Capture.One.SwitchCamera()),
                FreePuttScreen.Space(8),
                Ui.Box("btn-small", Ui.Text(_ended ? "Done" : "End", "btn-text", Ui.Bold)).OnTap(Leave)));

            string waiting = Capture.One.Waiting();
            if (waiting != null) _scroll.Add(Ui.Text(waiting, "sheet-note sheet-warn", Ui.Bold));
            else
            {
                Label hint = Ui.Text(_framing.Confirmed ? "Framing good" : _framing.Status == FramingStatus.Full ? "Hold still..." : _framing.Hint, "sheet-note", Ui.Bold);
                hint.style.backgroundColor = Color.Lerp(Ui.Ink, _framing.Colour, 0.35f);
                _scroll.Add(hint);
            }
            if (_putts.Count > 0)
            {
                AppPutt.Verdict last = _putts[_putts.Count - 1];
                _scroll.Add(Frame.PuttRead(last, 0, false));
            }
        }
    }

    /// <summary>After filming: the cloud's progress, until the 3D session can be opened.</summary>
    public sealed class LabProcessingScreen : AppScreen
    {
        readonly App _app;
        readonly string _session;
        readonly Label _status;
        readonly VisualElement _action;

        public LabProcessingScreen(App app, string session, List<AppPutt.Verdict> putts, double score)
        {
            _app = app;
            _session = session;
            int made = 0;
            foreach (AppPutt.Verdict v in putts) if (v.Made) made++;
            _status = Ui.Text("Uploading video...", "h2 center", Ui.Bold);
            _action = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad",
                Ui.Text("SESSION COMPLETE", "kick center", Ui.Bold),
                Ui.Text(made + " of " + putts.Count, "score-big", Ui.Bold),
                Ui.Text("made   ·   session score " + score.ToString("0.0"), "sub center", Ui.Regular),
                Ui.Box("glass", _status, Ui.Text("You can leave this screen. The 3D session appears in Improve when it is ready.", "muted center", Ui.Regular).Margin(8)).Margin(18),
                _action));
            Root = scroll;
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
                if (row == null) { _status.text = "Uploading video..."; return; }
                int status = J.Int(row, "status");
                _status.text = status <= 1 ? "Processing video..." : status == 2 ? "Analyzing video..." : "Your 3D session is ready";
                if (status >= 3 && !_ready)
                {
                    _ready = true;
                    _action.Add(Ui.Button("View session", () => { _app.Pop(); _app.Push(new LabSessionScreen(_app, row)); }));
                }
            }));
        }
    }

    /// <summary>Every 3D session filmed, newest first (GET /lab/sessions).</summary>
    public sealed class LabSessionsScreen : AppScreen
    {
        public LabSessionsScreen(App app)
        {
            var list = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("3D sessions", app.Pop));
            scroll.Add(Ui.Box("pad", Ui.Button("Record a new session", () => app.Push(new LabIntroScreen(app))), list.Margin(14)));
            Root = scroll;
            App.Run(Api.Get("/lab/sessions", data =>
            {
                List<object> rows = J.List(data);
                if (rows.Count == 0) { list.Add(Ui.Text("No 3D sessions yet.", "sub center", Ui.Regular)); return; }
                foreach (object row in rows)
                {
                    object session = row;
                    int status = J.Int(row, "status");
                    list.Add(Ui.Box("glass game-row",
                        Ui.Box("grow", Ui.Text("Session " + J.Str(row, "id"), "feed-name", Ui.Bold), Ui.Text(Ago.Words(ProfileMath.Date(row, "created_at"), DateTime.UtcNow), "muted", Ui.Regular)),
                        Ui.Text(status >= 4 ? "3D ready" : "Processing", status >= 4 ? "chip chip-green" : "chip chip-amber", Ui.Bold))
                        .OnTap(() => app.Push(new LabSessionScreen(app, session))));
                }
            }, _ => list.Add(Ui.Text("Could not load your sessions.", "sub center", Ui.Regular))));
        }
    }
}
