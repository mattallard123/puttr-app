// The coach's first run on Improve (puttr-app coach/mvp CoachOnboarding, CoachReveal) and the drill tools:
//
//   CoachFirstRunScreen    welcome, pick a coach (their bundled intro mp3, Coaches/{id}_intro), five putts at 6 ft straight,
//                          the reveal (computeReveal here, then ai.puttr.co /coach/diagnose over it), the daily
//                          goal and reminder: POST /coach/settings and /streaks/goal/
//   Metronome              TEMPO drills: effects_metronome every 60000/bpm ms, 50 to 120 bpm (85 to start)
//   SpeedDrillResultScreen SPEED drills: the spread of the rollouts (max minus min), lower is better
//   CameraSetupScreen      VideoConfig: the camera full screen with a centre line, front or rear
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class FirstRunMath
    {
        public static readonly Dictionary<string, string> Intro = new Dictionary<string, string>
        {
            { "maya", "Hey, I'm Maya. We'll keep it positive and build that stroke together, you've got this." },
            { "theo", "I'm Theo. We'll read the numbers, find what's off, and fix it precisely." },
            { "riley", "I'm Riley. I'll push you hard and I won't sugar-coat it. Ready to work?" },
            { "jax", "Hey, I'm Jax! We're gonna get your stats up and have a blast doing it, let's go!" },
        };
        public static readonly Dictionary<string, string> Personality = new Dictionary<string, string> { { "maya", "Encouraging" }, { "theo", "Analytical" }, { "riley", "Tough love" }, { "jax", "Hype" } };

        /// <summary>The make % expected at a handicap (coachLogic.js targetFor).</summary>
        public static int Target(string hcp)
        {
            hcp = (hcp ?? "").Trim();
            if (hcp.Length == 0 || hcp == "NONE") return 70;
            if (hcp == "scratch" || hcp == "PRO" || hcp.StartsWith("+")) return 85;
            if (hcp == "1-9") return 78;
            if (hcp == "10-19") return 68;
            if (hcp == "20+") return 58;
            if (hcp == "new") return 50;
            if (double.TryParse(hcp, NumberStyles.Float, CultureInfo.InvariantCulture, out double h)) return h <= 0 ? 85 : h <= 9 ? 78 : h <= 19 ? 68 : 58;
            return 70;
        }

        /// <summary>
        /// computeReveal over getPuttAssessment: misses tally by side; a make counts by its entry against the
        /// tee's line. Lean needs a gap of 2 under 8 putts (1 otherwise); pace from the average rollout (+-3 in).
        /// </summary>
        public static Dictionary<string, object> Reveal(List<AppPutt.Verdict> putts, string hcp)
        {
            int n = putts.Count, made = 0, left = 0, right = 0;
            double sum = 0; int rolls = 0;
            foreach (AppPutt.Verdict v in putts)
            {
                if (v.Made) { made++; if (v.OffLine > 0) right++; else if (v.OffLine < 0) left++; }
                else if (v.MissSide == "RIGHT") right++; else left++;
                if (v.Rollout.HasValue) { sum += v.Rollout.Value; rolls++; }
            }
            bool small = n < 8;
            string side = Math.Abs(left - right) >= (small ? 2 : 1) ? (left > right ? "left" : "right") : null;
            int leanPct = (int)Math.Round(Math.Max(left, right) / (double)Math.Max(1, left + right) * 100);
            double avg = rolls > 0 ? sum / rolls : 0;
            string bias = rolls == 0 ? "on pace" : avg > 3 ? "runs hot" : avg < -3 ? "runs soft" : "on pace";
            int pct = n > 0 ? (int)Math.Round(made * 100.0 / n) : 0, target = Target(hcp);
            string lead = small ? "Early read: " : "";
            string title = side != null ? (small ? "You're leaning " + side + "." : "Your tendency is " + side + ".") : bias != "on pace" ? "Your pace is the thing to watch." : "Solid and balanced so far.";
            string body = side != null ? lead + leanPct + "% of your putts drifted " + side + " of center" + (bias != "on pace" ? ", and your speed " + bias : "") + "." + (small ? " Let's confirm it over a few sessions." : "")
                : bias != "on pace" ? lead + "your direction looks good; it's the speed that " + bias + " to dial in." : lead + "no clear directional lean yet. A few more sessions and I'll spot your tendencies.";
            if (lead.Length == 0) body = char.ToUpperInvariant(body[0]) + body.Substring(1); // the app leaves "your direction..." lower case without the lead
            string strength = pct >= target ? "You made " + pct + "% cold, ahead of the " + target + "% I'd expect at your level. Strong base." : "Your stroke is consistent enough to fix fast.";
            string plan = side != null ? "2 minutes a day. I'll rotate you through your spots and chip away at that " + side + " drift." : "2 minutes a day, rotating through your spots to sharpen pace and accuracy everywhere.";
            return new Dictionary<string, object>
            {
                { "percentMade", pct }, { "sampleSize", n }, { "confidence", small ? "low" : "medium" }, { "leanSide", side }, { "leanPct", leanPct }, { "speedBias", bias },
                { "targetMakePct", target }, { "leakTitle", title }, { "leakBody", body }, { "strength", strength }, { "plan", plan }, { "focusKey", side != null ? "pull-" + side : "speed" },
            };
        }

        /// <summary>The reminder time as minutes after UTC midnight (today's offset), as the app.</summary>
        public static int UtcMinutes(int hour, int minute)
        {
            DateTime local = DateTime.Today.AddHours(hour).AddMinutes(minute);
            DateTime utc = local.ToUniversalTime();
            return utc.Hour * 60 + utc.Minute;
        }
    }

    public sealed class CoachFirstRunScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        readonly Action _done;
        string _step = "welcome", _persona = "maya";
        bool _introPlayed;
        Dictionary<string, object> _reveal;
        int _goal = 10, _hour = 18, _minute = 30;
        bool _deeper;

        public CoachFirstRunScreen(App app, Action done)
        {
            _app = app;
            _done = done;
            if (ProfileSetup.Arg("-app2CoachPick")) _step = "pick";   // for checking the picker from a script
            _body = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(_body);
            scroll.Add(FreePuttScreen.Space(32));
            Root = scroll;
            Draw();
        }

        static VisualElement Face(string id, float size)
        {
            Coaches.Persona p = Coaches.Of(id);
            VisualElement face = Ui.Box("avatar");
            face.style.width = face.style.height = size;
            face.AddToClassList("coach-face");   // a rounded square, as every avatar
            Texture2D t = Resources.Load<Texture2D>("PuttrApp/Coaches/" + p.Name);
            if (t != null) face.style.backgroundImage = new StyleBackground(t);
            return face;
        }

        void Draw()
        {
            _body.Clear();
            _body.Add(Ui.Box("top", Ui.Text("‹", "back back-chevron", Ui.Medium).OnTap(Back), Ui.Text("", "top-title", Ui.Bold), Ui.Box("back-balance")));
            VisualElement p = Ui.Box("pad");
            _body.Add(p);
            switch (_step)
            {
                case "welcome":
                    VisualElement row = Ui.Box("row");
                    foreach (Coaches.Persona c in Coaches.All) row.Add(Face(c.Id, 56));
                    p.Add(row.Margin(24));
                    p.Add(Ui.Title("Meet your\nputting coaches.", "title").Margin(16));
                    p.Add(Ui.Text("Pick the one whose style fits you. Give them 5 putts and 5 minutes, they'll show you exactly what to work on.", "sub wrap", Ui.Regular).Margin(12));
                    p.Add(Ui.Button("Let's go →", () => { _step = "pick"; Draw(); }).Margin(24));
                    p.Add(Ui.Text("Takes about 5 minutes", "body-2 center", Ui.Regular).Margin(8));
                    break;
                case "pick":
                    p.Add(Ui.Title("Pick your coach", "title"));
                    p.Add(Ui.Text("Four personalities. Same data, very different vibe. Swap anytime.", "sub wrap", Ui.Regular).Margin(8));
                    VisualElement grid = Ui.Box("team-grid");
                    foreach (Coaches.Persona c in Coaches.All)
                    {
                        string id = c.Id;
                        VisualElement card = Ui.Box(id == _persona ? "card team-cell coach-on" : "card team-cell", Face(id, 96), Ui.Title(c.Name, "title-20").Margin(8), Ui.Text(FirstRunMath.Personality[id].ToUpperInvariant(), "caps", Ui.Bold));
                        grid.Add(Ui.Box("team-slot", card.OnTap(() => { _persona = id; Speak(); Draw(); })));
                    }
                    p.Add(grid);
                    p.Add(Form.Label(Coaches.Of(_persona).Name + " sounds like"));
                    p.Add(Ui.Box("card", Ui.Text(FirstRunMath.Intro[_persona], "sub wrap", Ui.Regular)));
                    p.Add(Ui.Button("Choose " + Coaches.Of(_persona).Name + " →", () =>
                    {
                        CoachVoice.Hush();
                        App.Run(Api.Post("/coach/settings", new Dictionary<string, object> { { "personaId", _persona } }, null, null));
                        _step = "calibrate"; Draw();
                    }).Margin(16));
                    if (!_introPlayed) { _introPlayed = true; Speak(); }
                    break;
                case "calibrate":
                    p.Add(Ui.Box("row", Face(_persona, 48), Ui.Title("Let's see your stroke", "title-20 grow").Margin(0)));
                    p.Add(Ui.Box("card", Ui.Text("Five putts, 6 ft, straight", "body", Ui.Bold), Ui.Text("Putt like you normally would, I'm watching your speed and where the ball finishes.", "sub wrap", Ui.Regular).Margin(8)).Margin(12));
                    p.Add(Ui.StatStrip(("6 ft", "Distance"), ("Straight", "Break"), ("5", "Putts")).Margin(12));
                    p.Add(Ui.Box("card", Ui.Text("I'm watching: speed and where it finishes", "sub wrap", Ui.Regular)).Margin(12));
                    if (Green.Peek == null || !Green.Peek.Online) p.Add(Ui.Text("Connect your Puttr to begin.", "body-2 center", Ui.Regular).Margin(12));
                    p.Add(Ui.Button("I'm ready", () => _app.Games.PlayCalibration(round =>
                    {
                        var seen = new List<AppPutt.Verdict>(round.Session.Seen);
                        _reveal = FirstRunMath.Reveal(seen, J.Str(_app.Profile, "realHandicap"));
                        _step = "reveal";
                        Draw();
                        App.Run(Diagnose());
                    })).Margin(12));
                    break;
                case "reveal":
                    string first = HomeScreen.FirstName(_app.Profile);
                    p.Add(Ui.Box("row", Face(_persona, 48), Ui.Title("Here's what I saw, " + (first.Length > 0 ? first : "there") + ".", "title-20 grow").Margin(0)));
                    Label leakTag = Ui.Caps("Biggest leak");
                    leakTag.style.color = Ui.Red;   // the colour is on the words; the card keeps its plain border
                    VisualElement leak = Ui.Box("card", leakTag, Ui.Title(J.Str(_reveal, "leakTitle"), "title").Margin(8), Ui.Text(J.Str(_reveal, "leakBody"), "sub wrap", Ui.Regular).Margin(8));
                    p.Add(leak.Margin(16));
                    if (J.Str(_reveal, "strength").Length > 0) p.Add(Ui.Box("card", GreenText("✓ Your strength"), Ui.Text(J.Str(_reveal, "strength"), "sub wrap", Ui.Regular).Margin(8)).Margin(12));
                    if (J.Str(_reveal, "plan").Length > 0) p.Add(Ui.Box("card", Ui.Text("The plan", "body", Ui.Bold), Ui.Text(J.Str(_reveal, "plan"), "sub wrap", Ui.Regular).Margin(8)).Margin(12));
                    p.Add(Ui.Box("card row", Ui.Icon("VideoCamera", 24, Color.white), Ui.Text("This is the what. Soon I'll have you film your setup once so I can show you the why behind it.", "sub grow wrap", Ui.Regular).Margin(0)).Margin(12));
                    p.Add(Ui.Button("Start my plan →", () => { _step = "commit"; Draw(); }).Margin(16));
                    Label deeper = Ui.Text("Map your whole green: 12 putts, all distances →", "sec-link center", Ui.Medium);
                    p.Add(deeper.Margin(12).OnTap(() => { _deeper = true; _step = "commit"; Draw(); }));
                    break;
                default:
                    p.Add(Ui.Box("center-box", Ui.Icon("FillFire", 44, Ui.Amber)).Margin(24));
                    p.Add(Ui.Title("Build the habit", "title center").Margin(8));
                    p.Add(Ui.Text("Players who putt daily improve 2× faster. Pick your goal.", "sub center wrap", Ui.Regular).Margin(8));
                    p.Add(Form.Label("Daily goal (putts)"));
                    int[] goals = { 10, 20, 30, 50 };
                    p.Add(Form.Pills(new[] { "10", "20", "30", "50" }, Array.IndexOf(goals, _goal), i => _goal = goals[i]));
                    p.Add(Form.Label("Remind me at"));
                    p.Add(FreePuttScreen.Stepper(() => DateTime.Today.AddHours(_hour).AddMinutes(_minute).ToString("h:mm tt", CultureInfo.InvariantCulture), d =>
                    {
                        int total = (_hour * 60 + _minute + d * 30 + 1440) % 1440;
                        _hour = total / 60; _minute = total % 60;
                    }));
                    p.Add(Ui.Button("Start day 1", () => App.Run(Finish())).Margin(16));
                    break;
            }
        }

        static Label GreenText(string s) { Label l = Ui.Text(s, "body", Ui.Bold); l.style.color = Ui.Bright; return l; }

        // Each coach introduces themselves in their own voice: the bundled intro, as the app's speakCoachIntro.
        void Speak() => CoachVoice.Intro(Coaches.Of(_persona), FirstRunMath.Intro[_persona]);

        void Back()
        {
            CoachVoice.Hush();
            switch (_step)
            {
                case "pick": _step = "welcome"; break;
                case "calibrate": _step = "pick"; break;
                case "commit": _step = "reveal"; break;
                default: _app.Pop(); return;
            }
            Draw();
        }

        // The model's words over the local reveal, when it answers in time.
        IEnumerator Diagnose()
        {
            var body = new Dictionary<string, object> { { "personaId", _persona }, { "profile", new Dictionary<string, object> { { "firstName", HomeScreen.FirstName(_app.Profile) }, { "handedness", J.Str(_app.Profile, "handedness", "RIGHTY") }, { "realHandicap", J.Str(_app.Profile, "realHandicap") } } }, { "assessment", _reveal } };
            object said = null;
            yield return Api.Send("POST", "https://ai.puttr.co/coach/diagnose", J.Write(body), null, (s, text) => { if (s == 200) said = J.Parse(text); });
            if (said == null || J.Str(said, "leakTitle").Length == 0 || _step != "reveal") yield break;
            foreach (string k in new[] { "leakTitle", "leakBody", "strength", "plan" }) if (J.Str(said, k).Length > 0) _reveal[k] = J.Str(said, k);
            Draw();
        }

        IEnumerator Finish()
        {
            string time = DateTime.Today.AddHours(_hour).AddMinutes(_minute).ToString("h:mm tt", CultureInfo.InvariantCulture);
            var settings = new Dictionary<string, object>
            {
                { "personaId", _persona }, { "voiceOn", CoachVoice.On }, { "dailyGoal", _goal }, { "reminderTime", time }, { "reminderUtcMinutes", FirstRunMath.UtcMinutes(_hour, _minute) }, { "onboarded", true },
                { "baseline", _reveal != null ? new Dictionary<string, object> { { "leanSide", _reveal["leanSide"] }, { "focusKey", _reveal["focusKey"] }, { "percentMade", _reveal["percentMade"] } } : null },
            };
            yield return Api.Post("/coach/settings", settings, d => CoachVoice.Load(d), null);
            yield return Api.Post("/streaks/goal/", new Dictionary<string, object> { { "dailyGoal", _goal } }, null, null);
            CoachVoice.Persona = Coaches.Of(_persona);
            _app.Pop();
            _done?.Invoke();
            // "Map your whole green": the 12-putt Assessment (game 853).
            if (_deeper) App.Run(Api.Get("/games/ALL", d => { object a = J.List(d).Find(g => J.Int(g, "id") == 853 || J.Str(g, "gameType") == "ASSESSMENT"); if (a != null) _app.Push(new GameScreen(_app, a)); }, _ => { }));
        }
    }

    // ── Drills ───────────────────────────────────────────────────────────────

    public sealed class Metronome : MonoBehaviour
    {
        public static int Bpm = 80;   // the app's metronome starts at 80
        static Metronome _one;
        AudioSource _source;
        float _next;

        public static bool Running => _one != null && _one.enabled;

        public static void Start(int bpm)
        {
            Bpm = Mathf.Clamp(bpm, 50, 120);
            if (_one == null)
            {
                var go = new GameObject("Metronome");
                DontDestroyOnLoad(go);
                _one = go.AddComponent<Metronome>();
                _one._source = go.AddComponent<AudioSource>();
                _one._source.clip = Resources.Load<AudioClip>("PuttrApp/Sounds/effects_metronome");
            }
            _one._next = Time.realtimeSinceStartup;
            _one.enabled = true;
        }

        public static void Stop() { if (_one != null) _one.enabled = false; }

        void Update()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next += 60f / Bpm;
            if (_next < Time.realtimeSinceStartup) _next = Time.realtimeSinceStartup + 60f / Bpm;
            if (_source.clip != null) _source.PlayOneShot(_source.clip);
        }

        /// <summary>The pill: bpm with - and +, and play or stop.</summary>
        public static VisualElement Pill()
        {
            Label play = Ui.Text(Running ? "Stop" : "Play", "sec-link", Ui.Bold);
            VisualElement stepper = FreePuttScreen.Stepper(() => Bpm + " bpm", d => { Bpm = Mathf.Clamp(Bpm + d, 50, 120); if (Running) Start(Bpm); });
            return Ui.Box("row", stepper, play.Margin(0).OnTap(() => { if (Running) Stop(); else Start(Bpm); play.text = Running ? "Stop" : "Play"; }));
        }
    }

    public static class DrillMath
    {
        /// <summary>LessonDrill SPEED: max minus min of the rollouts read (inches), once there are two; else null.</summary>
        public static double? SpeedSpread(IEnumerable<AppPutt.Verdict> putts)
        {
            double lo = double.MaxValue, hi = double.MinValue; int n = 0;
            foreach (AppPutt.Verdict v in putts) if (v.Rollout.HasValue) { lo = Math.Min(lo, v.Rollout.Value); hi = Math.Max(hi, v.Rollout.Value); n++; }
            return n >= 2 ? Math.Round(hi - lo, 1) : (double?)null;
        }
    }

    public sealed class SpeedDrillResultScreen : AppScreen
    {
        public SpeedDrillResultScreen(App app, CompetitionRound round)
        {
            Root = Page.Make(app, round.Session.Detail.Length > 0 ? round.Session.Detail : "Speed drill", out VisualElement body);
            double? spread = DrillMath.SpeedSpread(round.Session.Seen);
            body.Add(Ui.Box("pad center-box", Ui.Text(spread.HasValue ? spread.Value.ToString("0.0", CultureInfo.InvariantCulture) : "–", "code", Ui.Bold), Ui.Text("Score", "body-2 center", Ui.Regular),
                Ui.Text(spread.HasValue ? "Inches between your longest and shortest roll. Lower is better: the same speed every time." : "Not enough rolls were read to score this drill.", "sub center wrap", Ui.Regular).Margin(12)));
            object lesson = round.Round;
            body.Add(Ui.Box("pad", Ui.Button("Again", () => { app.Pop(); app.Games.PlayLessonDrill(lesson); }), Ui.Button("Done", app.Pop, "btn btn-ghost").Margin(8)));
        }
    }

    /// <summary>VideoConfig: the camera full screen with a centre line, front or rear. The same Agora preview the app
    /// uses (channel CAMERA_SETUP, no sound), so the camera is asked for only here.</summary>
    public sealed class CameraSetupScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _picture;
        readonly Label _status;
        bool _on;

        public CameraSetupScreen(App app)
        {
            _app = app;
            _status = Ui.Text("CONNECTING TO VIDEO SERVER", "body-2 center", Ui.Bold);
            // On a dark backing so the centre line does not run through the words.
            _status.style.backgroundColor = Color.black;
            _status.style.paddingLeft = _status.style.paddingRight = 12;
            _status.style.paddingTop = _status.style.paddingBottom = 8;
            _status.style.alignSelf = Align.Center;
            _status.style.maxWidth = Length.Percent(86);
            _status.AddToClassList("wrap");
            _picture = Ui.Box("grow", _status);
            _picture.style.justifyContent = Justify.Center;
            VisualElement line = new VisualElement();
            line.style.position = Position.Absolute; line.style.left = Length.Percent(50); line.style.top = 0; line.style.bottom = 0; line.style.width = 4; line.style.marginLeft = -2;
            line.style.backgroundColor = Ui.Bright;
            VisualElement flip = Ui.Secondary("Flip", () => Capture.One.SwitchCamera());
            flip.style.position = Position.Absolute; flip.style.right = 16; flip.style.bottom = 32;
            // The centre line and the flip button sit on the picture, below the header.
            _picture.Insert(0, line);
            _picture.Add(flip);
            Root = Ui.Box("grow", Ui.Header("Camera Setup", Leave), _picture);
            Root.style.backgroundColor = Color.black;
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Stop());
            if (!Capture.Supported) { _status.text = "Camera setup needs the phone app."; return; }
            _on = true;
            Capture.One.BeginCall("CAMERA_SETUP", 1, false);
            Capture.One.Changed += Place;
            _picture.RegisterCallback<GeometryChangedEvent>(_ => Place());
        }

        void Place()
        {
            if (!_on || _picture.panel == null) return;
            if (Capture.One.Now == Capture.State.Failed) { _status.text = "The camera could not start. Allow camera access for Puttr in Settings."; return; }
            Rect all = _picture.panel.visualTree.layout, r = _picture.worldBound;
            if (all.width <= 0) return;
            Capture.One.Border(Color.black, 0);
            Capture.One.Place(new Rect(r.x / all.width, r.y / all.height, r.width / all.width, r.height / all.height));
        }

        void Stop() { if (!_on) return; _on = false; Capture.One.Changed -= Place; Capture.One.End(); }

        void Leave() { Stop(); _app.Pop(); }
    }
}
