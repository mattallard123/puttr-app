// Tracks, broadcast golf (puttr-app components/games/Tracks.js, played on a 3D hole: TracksWorld):
//
//   TracksCoursesScreen  GET /tracks/courses: the Collection by rank, your best on each, Club lock on the rest
//   TracksSetupScreen    the course, Front 9 / Back 9 / 18, Amateur / Pro / Major
//   TracksPlayScreen     the round: the caddie calls the club and the mat putt, the putt plays the shot (line sets how
//                        straight, pace how far), lies cost a foot or two on the mat, water is a penalty and a drop,
//                        on the green the putt is its real length to 11 ft and a quarter-length lag beyond, the cup has
//                        a front window by level, gimmes tap in, 12 picks up
//
// Saving as the app: POST /game/new (gameType TRACKS) with each mat putt (RoundSaver), POST /tracks/round at tee
// off, PUT /tracks/round/:id after every hole and at the end, /game/over and the leaderboard with strokes to par.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Puttr;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>How a course is named: the courses are inspired by the real ones, and say so; the place gets a pin.</summary>
    static class TrackLabels
    {
        public static VisualElement Inspired(string name, string titleClass) =>
            Ui.Box("tracks-title-box", Ui.Text("Inspired by", "tracks-inspired", Ui.Medium), Ui.Text(name, titleClass, Ui.Bold));

        public static VisualElement Where(string location, string textClass)
        {
            Label words = Ui.Text(location, textClass, Ui.Regular);
            return Ui.Box("row tracks-where", Ui.Icon("MapPin", 13, new Color(1f, 1f, 1f, 0.55f)), words);
        }
    }

    public sealed class TracksCoursesScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        static bool _devSetupOpened;

        public TracksCoursesScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Tracks", out _body);
            _body.Add(Ui.Box("pad", Ui.Text("Play the world's great courses on your mat. Every putt is a shot: the putt's length picks the club, line and pace send it.", "sub wrap", Ui.Regular)));
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/tracks/courses", Draw, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load the courses. Check your connection.")); }));

        void Draw(object data)
        {
            _body.Clear();
            _body.Add(Ui.Box("pad", Ui.Text("Play the world's great courses on your mat. Every putt is a shot: the putt's length picks the club, line and pace send it.", "sub wrap", Ui.Regular)));
            List<object> rows = J.List(data);
            bool member = !Club.Blocked(_app.Profile);
            VisualElement cols = Ui.Box("cols");
            foreach (object c in rows)
            {
                object course = c;
                bool locked = !member && !J.Flag(c, "isFree");
                string best = J.Get(c, "myBest") != null ? "Your best " + TracksEngine.ToPar(J.Int(c, "myBest")) : J.Int(c, "rounds") > 0 ? J.Int(c, "rounds") + (J.Int(c, "rounds") == 1 ? " round played" : " rounds played") : "Not played yet";
                // The course from overhead, every hole on it (painted as the card scrolls into view, then kept).
                VisualElement art = CourseStill(c);
                art.Add(Ui.Box("tracks-art-top", J.Get(c, "rank") != null ? Ui.Text("NO. " + J.Int(c, "rank"), "tracks-art-chip", Ui.Bold) : null, Ui.Box("grow"),
                    locked ? Ui.Text("Club", "chip chip-amber") : J.Flag(c, "isFree") ? Ui.Text("Free", "chip chip-green") : null));
                VisualElement card = Ui.Box("glass tracks-ccard", art,
                    Ui.Box("tracks-ccard-body", TrackLabels.Inspired(J.Str(c, "name"), "feed-name wrap"), TrackLabels.Where(J.Str(c, "location"), "muted"),
                        Ui.Box("chips", Ui.Text("Par " + J.Int(c, "par"), "chip"), Ui.Text(J.Int(c, "holes") + " holes", "chip"), Ui.Text(J.Int(c, "yds").ToString("#,0") + " yds", "chip"), Ui.Text(best, "chip"))));
                card.OnTap(() => { if (locked) _app.Push(new ClubScreen(_app, "Every Tracks course")); else _app.Push(new TracksSetupScreen(_app, course)); });
                cols.Add(Ui.Box("col pad", card).Margin(8));
            }
            if (rows.Count == 0) _body.Add(Page.Note("No courses yet."));
            _body.Add(cols);
            // -app2TracksSetup: open the first course's setup (for screenshots from a script).
            if (!_devSetupOpened && rows.Count > 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TracksSetup") >= 0) { _devSetupOpened = true; _app.Push(new TracksSetupScreen(_app, rows[0])); }
        }

        /// <summary>A box the shape of the still (2:1), the still faded in when it is painted, the hole numbers over it.</summary>
        public static VisualElement CourseStill(object meta)
        {
            VisualElement art = Ui.Box("tracks-art");
            var pic = new Image { scaleMode = ScaleMode.StretchToFill };
            pic.AddToClassList("tracks-art-pic");
            pic.pickingMode = PickingMode.Ignore;
            art.Add(pic);
            art.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float h = Mathf.Round(e.newRect.width * TracksArt.CardH / TracksArt.CardW);
                if (h > 0 && Mathf.Abs(e.newRect.height - h) > 1f) art.style.height = h;
            });
            TracksArt.Show(art, meta, TracksArt.CardW, TracksArt.CardH, still =>
            {
                pic.image = still.Tex;
                TracksArt.Numbers(pic, still);
                pic.AddToClassList("tracks-art-on");
            });
            return art;
        }
    }

    /// <summary>The round's setup over a live blimp shot of the course: the camera drifts high over the holes in order,
    /// banking gently through the turns, and the setup sits on a dark glass panel over it. The world is built when the
    /// screen comes to the front and put away when anything else does.</summary>
    public sealed class TracksSetupScreen : AppScreen
    {
        const string LevelKey = "app2.tracks.level", SetKey = "app2.tracks.set";
        readonly App _app;
        readonly object _meta;
        readonly Image _fly, _still;
        string _level, _set;
        TracksWorld _world;
        int _visit;
        static readonly Dictionary<string, float> Share = new Dictionary<string, float>();   // where the blimp was on each course, to pick the flight up there

        public TracksSetupScreen(App app, object meta)
        {
            _app = app;
            _meta = meta;
            _level = Pref(LevelKey, "pro");
            _set = Pref(SetKey, "all");
            if (J.Int(meta, "holes") < 18) _set = "all";
            // Behind the page: the overhead still while the course builds, then the fly-over faded in over it.
            _still = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            _still.AddToClassList("tracks-fly-still");
            _fly = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            _fly.AddToClassList("tracks-fly");
            _still.pickingMode = _fly.pickingMode = PickingMode.Ignore;
            ScrollView page = Draw();
            page.AddToClassList("tracks-setup-page");
            VisualElement dim = Ui.Box("tracks-fly-dim");
            dim.pickingMode = PickingMode.Ignore;
            var shade = new Image { image = TracksArt.TopShade(), scaleMode = ScaleMode.StretchToFill };
            shade.AddToClassList("tracks-fly-top");
            shade.pickingMode = PickingMode.Ignore;
            Root = Ui.Box("tracks-setup", _still, _fly, dim, shade, page);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Size());
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Land());
            TracksArt.Show(Root, meta, TracksArt.CardW, TracksArt.CardH, s => _still.image = s.Tex);
        }

        static string Pref(string key, string fallback) { try { return PlayerPrefs.GetString(key, fallback); } catch (Exception) { return fallback; } }

        ScrollView Draw()
        {
            // The name lives in the panel, under "Inspired by", not in the header too.
            ScrollView page = Page.Make(_app, "", out VisualElement body);
            VisualElement p = Ui.Box("tracks-panel", TrackLabels.Inspired(J.Str(_meta, "name"), "h2 wrap"), TrackLabels.Where(J.Str(_meta, "location"), "sub"), Ui.Box("chips", Ui.Text("Par " + J.Int(_meta, "par"), "chip"), Ui.Text(J.Int(_meta, "yds").ToString("#,0") + " yds", "chip")));
            if (J.Int(_meta, "holes") >= 18)
            {
                p.Add(Form.Label("Holes"));
                string[] sets = { "front", "back", "all" };
                p.Add(Form.Pills(new[] { "Front 9", "Back 9", "18 holes" }, Array.IndexOf(sets, _set), i => { _set = sets[i]; PlayerPrefs.SetString(SetKey, _set); }));
            }
            p.Add(Form.Label("Level"));
            Label blurb = Ui.Text(TracksEngine.LevelOf(_level).Blurb, "muted wrap", Ui.Regular);
            p.Add(Form.Pills(TracksEngine.Levels.Select(l => l.Name).ToArray(), Array.FindIndex(TracksEngine.Levels, l => l.Id == _level), i => { _level = TracksEngine.Levels[i].Id; PlayerPrefs.SetString(LevelKey, _level); blurb.text = TracksEngine.Levels[i].Blurb; }));
            p.Add(blurb);
            p.Add(Ui.Box("tracks-how", Ui.Text("How it plays", "feed-name", Ui.Bold),
                Ui.Text("The caddie picks the club and calls the putt: a driver is an 11 ft putt, a wedge 4 ft. Your line sets how straight it flies and your pace how far. From the rough or a bunker the putt gets longer. On the green it's a real putt.", "sub wrap", Ui.Regular).Margin(6)).Margin(14));
            body.Add(Ui.Box("pad", p));
            body.Add(Form.Bottom(Ui.Button("Tee off", Start)));
            return page;
        }

        public override void Shown()
        {
            if (_world != null) return;
            _world = TracksWorld.Create();
            Size();
            _fly.image = _world.Texture;
            int visit = ++_visit;
            TracksWorld world = _world;
            App.Run(TracksArt.Course(J.Str(_meta, "slug"), (course, _) =>
            {
                if (course == null || world != _world || visit != _visit || world == null) return;
                world.StartCoroutine(Fly(world, course));
            }));
        }

        IEnumerator Fly(TracksWorld world, TracksCourse course)
        {
            float t0 = Time.realtimeSinceStartup;
            yield return world.BuildCourse(course);
            Perf.Note("Tracks setup course built in " + (Time.realtimeSinceStartup - t0).ToString("0.00") + " s");
            if (world != _world) yield break;
            world.Blimp(Share.TryGetValue(J.Str(_meta, "slug"), out float at) ? at : 0f);
            yield return null; yield return null;   // a frame drawn before it shows
            if (world == _world) { _fly.image = world.Texture; _fly.AddToClassList("tracks-fly-on"); Perf.Wake(); }
        }

        /// <summary>The fly-over draws at the page's own shape, at up to twice its points (1600 on the long side).</summary>
        void Size()
        {
            if (_world == null) return;
            Rect r = Root.layout;
            if (float.IsNaN(r.width) || r.width < 10 || r.height < 10) return;
            float k = Mathf.Min(2f, 1600f / Mathf.Max(r.width, r.height));
            int w = Mathf.RoundToInt(r.width * k), h = Mathf.RoundToInt(r.height * k);
            if (_world.Texture != null && Mathf.Abs(_world.Texture.width - w) < 8 && Mathf.Abs(_world.Texture.height - h) < 8) return;
            _world.Resize(w, h);
            _fly.image = _world.Texture;
        }

        public override void Hidden() => Land();

        void Land()
        {
            _visit++;
            _fly.RemoveFromClassList("tracks-fly-on");
            if (_world == null) return;
            Share[J.Str(_meta, "slug")] = _world.BlimpShare;
            _world.Close();
            _world = null;
            _fly.image = null;
        }

        void Start()
        {
            _app.Toast("Loading the course...");
            App.Run(TracksArt.Course(J.Str(_meta, "slug"), (course, why) =>
            {
                if (course == null)
                {
                    if ((why ?? "").Contains("members_only")) _app.Push(new ClubScreen(_app, "Every Tracks course"));
                    else if (why == "no_holes") _app.Toast("This course has no holes to play.");
                    else _app.Toast("Couldn't load the course. Check your connection.");
                    return;
                }
                if (string.IsNullOrEmpty(course.Name)) course.Name = J.Str(_meta, "name");
                if (string.IsNullOrEmpty(course.Slug)) course.Slug = J.Str(_meta, "slug");
                _app.Push(new TracksPlayScreen(_app, course, _set, _level));
            }));
        }
    }

    public sealed class TracksPlayScreen : AppScreen
    {
        const float RestShot = 3.0f, RestPutt = 2.0f, TapIn = 2.2f, HoleDone = 2.6f;
        readonly App _app;
        readonly TracksCourse _course;
        readonly List<TracksCourse.Hole> _holes;
        readonly string _set, _level;
        readonly TracksWorld _world;
        readonly Image _view;
        readonly VisualElement _hud, _shotBar, _card, _camBar, _simRow, _entryBox;
        readonly Label _caption, _captionSub, _holeNum, _holeName, _holeMeta, _entryTop, _entryBottom;
        readonly TracksEntryView _entry;
        bool _camsOpen, _wide;
        readonly RoundSaver _saver = new RoundSaver("TRACKS");
        readonly int[] _strokes, _putts;
        readonly bool?[] _fairways;
        readonly bool[] _gir, _holed;
        readonly List<double> _drives = new List<double>(), _approachFt = new List<double>();
        int _idx, _shotNo, _tee = 2, _penalties, _sandHoles, _sandSaves, _scrambles;
        double _longestPuttFt;
        bool _holeSand, _busy, _done, _ended;
        string _lie = "tee", _roundId;
        bool _onGreen;
        Vector2 _ball;
        TracksEngine.Club _club;
        float _started;

        TracksCourse.Hole Hole => _holes[_idx];
        /// <summary>-app2TracksAuto: test putts play themselves (for screenshots from a script).</summary>
        public static bool AutoPlay;

        public TracksPlayScreen(App app, TracksCourse course, string set, string level)
        {
            _app = app; _course = course; _set = set; _level = level;
            _holes = set == "front" ? course.Holes.Take(9).ToList() : set == "back" ? course.Holes.Skip(9).Take(9).ToList() : course.Holes.ToList();
            if (_holes.Count == 0) _holes = course.Holes.ToList();
            _strokes = new int[_holes.Count]; _putts = new int[_holes.Count]; _fairways = new bool?[_holes.Count]; _gir = new bool[_holes.Count]; _holed = new bool[_holes.Count];
            _world = TracksWorld.Create();
            _view = new Image { image = _world.Texture, scaleMode = ScaleMode.ScaleAndCrop };
            _view.style.position = Position.Absolute; _view.style.left = _view.style.right = _view.style.top = _view.style.bottom = 0;
            _hud = new VisualElement(); _hud.style.position = Position.Absolute; _hud.style.left = _hud.style.right = _hud.style.top = _hud.style.bottom = 0;
            _hud.pickingMode = PickingMode.Ignore;

            // Hole card, top left; the camera, top right (just the one in use, tap for the others).
            _holeNum = Ui.Text("", "tracks-num", Ui.Bold); _holeName = Ui.Text("", "tracks-name", Ui.Bold); _holeMeta = Ui.Text("", "tracks-meta", Ui.Medium);
            _hud.Add(Ui.Box("tracks-top", Ui.Text("\u2039", "tracks-back", Ui.Medium).OnTap(Leave), Ui.Box("tracks-holecard", Ui.Box("tracks-numbox", _holeNum), Ui.Box("tracks-metabox", _holeName, _holeMeta)), Ui.Box("grow")));
            // The entry to make it (puttr-app tracks/PuttReadout.js): the mat around the cup, the window to come in
            // through drawn on the cup and turned with the tee, and after the putt its path and where it went in.
            _entry = new TracksEntryView();
            _entryTop = Ui.Text("", "tracks-entry-k", Ui.Bold); _entryBottom = Ui.Text("", "tracks-entry-v", Ui.Bold);
            _entryBox = Ui.Box("tracks-entry hidden", Ui.Text("ENTRY", "tracks-k", Ui.Bold), Ui.Box("tracks-entry-mat", _entry, _entryTop, _entryBottom));
            _entryBox.pickingMode = PickingMode.Ignore;
            _hud.Add(_entryBox);
            _camBar = Ui.Box("tracks-camdock");
            _hud.Add(_camBar);
            _caption = Ui.Text("", "tracks-cap-t", Ui.Bold); _captionSub = Ui.Text("", "tracks-cap-s", Ui.Regular);
            VisualElement lower = Ui.Box("tracks-lower hidden", _caption, _captionSub);
            lower.name = "lower";
            _simRow = Ui.Box("tracks-simrow");
            _shotBar = Ui.Box("tracks-shotbar");
            // The scorecard across the bottom: every hole's par and strokes, the total and where it stands to par.
            _card = Ui.Box("tracks-sc");
            _hud.Add(Ui.Box("tracks-bottom", lower, _simRow, _shotBar, _card));

            Root = Ui.Box("tracks-root", _view, _hud);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Stop());
            // On its side (a tablet, a phone turned) the scorecard shows the whole round and the entry gets bigger.
            Root.RegisterCallback<GeometryChangedEvent>(e =>
            {
                bool wide = e.newRect.width > e.newRect.height;
                if (wide == _wide) return;
                _wide = wide;
                Root.EnableInClassList("tracks-wide", wide);
                DrawCard();
            });
            // -app2TracksCams: open the camera list (for screenshots from a script).
            _camsOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TracksCams") >= 0;
            DrawCams();
            Green.Console.Putt += OnPutt;
            _started = Time.realtimeSinceStartup;
            _app.EnterPractice();
            App.Run(StartHole(0));
        }

        // ── Holes ──

        IEnumerator StartHole(int i)
        {
            _idx = i; _shotNo = 0; _lie = "tee"; _onGreen = false; _ball = Hole.Tee; _holeSand = false; _busy = true;
            _holeNum.text = Hole.N.ToString(); _holeName.text = Hole.Name.Length > 0 ? Hole.Name : "Hole " + Hole.N; _holeMeta.text = "Par " + Hole.Par + " · " + Hole.Yds + " yds";
            DrawCard();
            _entry.Last = null; DrawEntry();
            Say("Hole " + Hole.N, Hole.Name + (Hole.Name.Length > 0 ? " · " : "") + "Par " + Hole.Par + ", " + Hole.Yds + " yards");
            yield return _world.BuildHole(_course, Hole);
            _world.PlaceBall(_ball);
            _world.SetCam(_world.Mode == TracksWorld.Cam.Green ? TracksWorld.Cam.Broadcast : _world.Mode, _ball, true);
            DrawCams();
            _world.Bed("course_loop", 0.28f);
            if (_roundId == null && i == 0) App.Run(StartRound());
            _busy = false;
            CallCaddie();
        }

        double DistYds => Vector2.Distance(_ball, Hole.Pin);
        double LeaveFt => DistYds * 3;

        void CallCaddie()
        {
            _club = _onGreen ? TracksEngine.GreenPutt(LeaveFt) : TracksEngine.Suggest(DistYds, _lie);
            _tee = TeeFor();
            _entry.Last = null;
            DrawShot();
            DrawEntry();
            if (AutoPlay && !_club.Tap) _hud.schedule.Execute(Simulate).StartingIn(1500);
            if (_club.Tap) _hud.schedule.Execute(() => { if (!_busy && _club != null && _club.Tap) TapInNow(); }).StartingIn((long)(TapIn * 1000));
        }

        // The mat tee: off the course the side the ball lies on; on the green a random break.
        int TeeFor()
        {
            if (_onGreen) return _club == null || _club.Tap ? 2 : UnityEngine.Random.Range(1, 4);
            if (_lie == "fairway" || _lie == "tee" || _lie == "green" || _lie == "fringe") return 2;
            var (d, s) = TracksCourse.Closest(Hole, _ball);
            Vector2 a = TracksCourse.PointAt(Hole, s - 2), b = TracksCourse.PointAt(Hole, s + 2), q = TracksCourse.PointAt(Hole, s);
            float cross = (b.x - a.x) * (_ball.y - q.y) - (b.y - a.y) * (_ball.x - q.x);
            return Mathf.Abs(cross) < 1 ? 2 : cross > 0 ? 1 : 3;
        }

        bool CanReach(TracksEngine.Club c) => c.Pitch || c.Yds >= DistYds - 12;

        Vector2 AimTarget()
        {
            if (CanReach(_club)) return Hole.Pin;
            var (_, s0) = TracksCourse.Closest(Hole, _ball);
            for (float s = s0; s <= Hole.Len; s += 2) { Vector2 pt = TracksCourse.PointAt(Hole, s); if (Vector2.Distance(pt, _ball) >= _club.Yds) return pt; }
            return TracksCourse.PointAt(Hole, Mathf.Min(Hole.Len, s0 + (float)_club.Yds));
        }

        // ── Putts ──

        void OnPutt(PuttResult putt)
        {
            if (_busy || _done || _club == null || _club.Tap || !putt.Valid) return;
            int[][] bp = AppPutt.Path(putt.Path); double[] hb = AppPutt.Hole(putt.Hole);
            if (bp.Length < 2 || hb == null) return;
            AppPutt.Verdict v = AppPutt.Judge(bp, hb, _club.Ft, _tee);
            if (!v.Ball || v.RollUnder) return;
            int[] last = bp[bp.Length - 1];
            bool made = v.Count >= 1 || Mathf.Sqrt((float)((last[0] - hb[0]) * (last[0] - hb[0]) + (last[1] - hb[1]) * (last[1] - hb[1]))) <= hb[2] / 2 + 10;
            _saver.Putt(_club.Ft, _tee, made, putt);
            // The path in inches with the cup at the origin, +x right and +y past it (the camera's y runs the other way).
            var inches = new List<Vector2>();
            foreach (int[] q in bp) inches.Add(new Vector2((float)((q[0] - hb[0]) / TracksEngine.MatPxPerIn), (float)((hb[1] - q[1]) / TracksEngine.MatPxPerIn)));
            Apply(TracksEngine.ReadLineIn(bp, hb), v.Rollout, made, made && v.HasEntry ? v.Entry : (double?)null, inches);
        }

        /// <summary>Testers and sample data: a putt without the mat, a little off line and pace.</summary>
        void Simulate()
        {
            if (_busy || _done || _club == null || _club.Tap) return;
            double line = (UnityEngine.Random.value - 0.5) * 3, roll = (UnityEngine.Random.value - 0.4) * 14;
            bool made = _onGreen && _club.Ft <= 6 && UnityEngine.Random.value < 0.5;
            Apply(line, roll, made, made ? (UnityEngine.Random.value - 0.5) * 30 : (double?)null, null);
        }

        void Apply(double lineIn, double? rollout, bool matMade, double? entryDeg, List<Vector2> path)
        {
            _busy = true;
            // A putt with no path (the test putt) rolls in straight to where it finished, as the app draws it.
            Vector2 end = matMade ? Vector2.zero : new Vector2((float)lineIn, (float)(rollout ?? TracksEngine.PaceTargetIn));
            if (path == null || path.Count < 2) path = new List<Vector2> { new Vector2(end.x * 0.25f, -19f), end };
            _entry.Last = new TracksEntryView.Putt { Path = path.ToArray(), Made = matMade, EntryDeg = entryDeg, Green = _onGreen };
            DrawEntry();
            _shotBar.AddToClassList("hidden"); _simRow.AddToClassList("hidden");
            _strokes[_idx]++;
            if (_onGreen) { _putts[_idx]++; PuttOnGreen(lineIn, rollout, matMade, entryDeg); }
            else Shot(lineIn, rollout);
            DrawCard();
        }

        void Shot(double lineIn, double? rollout)
        {
            TracksEngine.Club club = _club;
            double toPin = DistYds;
            bool atPin = CanReach(club);
            double carry = atPin ? Math.Min(club.Yds, toPin) : club.Yds;
            var err = TracksEngine.ShotError(club, _lie, _level, lineIn, rollout, carry);
            double total = err.carry + err.depth, lateral = err.lateral;
            Vector2 target = AimTarget();
            Vector2 dir = (target - _ball).normalized, perp = new Vector2(dir.y, -dir.x);
            // Major: the cup is the fairway. A putt outside the cup misses the fairway by the same share.
            if (_level == "major" && !atPin)
            {
                Vector2 meant = _ball + dir * (float)carry;
                if (_course.LieAt(meant) == "fairway")
                {
                    float half = 0;
                    for (int sgn = -1; sgn <= 1; sgn += 2) { float w = 0; while (w < 45 && _course.LieAt(meant + perp * sgn * (w + 1)) == "fairway") w++; half += w / 2; }
                    lateral = Math.Sign(lineIn) * Math.Min(Math.Abs(lineIn) / TracksEngine.HoleRadiusIn, 2.5) * (half + 0.5);
                }
            }
            string putt = TracksEngine.DescribePutt(lineIn, rollout), shot = TracksEngine.DescribeShot(lateral, err.depth);
            _shotNo++;
            Vector2 from = _ball, land = _ball + dir * (float)total + perp * (float)lateral;
            Say(club.Name, putt);
            _world.Sfx(club.Big || club.Id == "3W" ? "golf_drive" : "golf_iron", 1f, club.Pitch ? 1.15f : 1f);
            _world.Fly(from, land, false, () => Land(from, land, club, putt, shot));
        }

        void Land(Vector2 from, Vector2 land, TracksEngine.Club club, string putt, string shot)
        {
            string lie = _course.LieAt(land);
            bool teeShot = _shotNo == 1 && Hole.Par > 3;
            _world.Sfx(lie == "water" ? "splash" : lie == "sand" ? "bunker" : "ball_land", 0.8f);
            if (lie == "water")
            {
                _strokes[_idx] += TracksEngine.WaterPenalty; _penalties++;
                Vector2 prev = from;
                for (int i = 1; i <= 80; i++) { Vector2 q = Vector2.Lerp(from, land, i / 80f); if (_course.LieAt(q) == "water") break; prev = q; }
                _ball = prev;
                string l2 = _course.LieAt(_ball);
                _lie = l2 == "water" || l2 == "tee" ? "rough" : l2;
                if (teeShot) _fairways[_idx] = false;
                After(putt, shot + ". Water, +1. Drop where it crossed.", RestShot);
                return;
            }
            _ball = land; _lie = lie;
            if (teeShot) { _fairways[_idx] = lie == "fairway"; _drives.Add(Vector2.Distance(from, land)); }
            if (lie == "sand" && !_holeSand) { _holeSand = true; _sandHoles++; }
            if (lie == "green")
            {
                _onGreen = true;
                _approachFt.Add(LeaveFt);
                if (_strokes[_idx] <= Hole.Par - 2) _gir[_idx] = true;
            }
            string where = lie == "green" ? (LeaveFt <= 8 ? "Tight. " : "On the green, ") + Math.Round(LeaveFt) + " ft" : TracksEngine.LieOf(lie).Name + ", " + Math.Round(DistYds) + " yds to go";
            After(putt, shot + ". " + where + ".", RestShot);
        }

        void PuttOnGreen(double lineIn, double? rollout, bool matMade, double? entryDeg)
        {
            double ft = Math.Max(0.1, LeaveFt);
            TracksEngine.Club club = _club;
            var res = TracksEngine.GreenResult(ft, lineIn, rollout, _level, club.Ratio);
            bool made = res.made, lip = false;
            double xin = res.xin, yin = res.yin;
            double teeDeg = AppPutt.TeeEntry(club.Ft, _tee);
            if (matMade)
            {
                double deg = entryDeg ?? teeDeg;
                double half = club.Lag ? TracksEngine.LagTolerance(_level, ft) : TracksEngine.LevelOf(_level).Entry / 2;
                if (Math.Abs(deg) <= 90 && Math.Abs(deg - teeDeg) <= half) made = true;
                else { made = false; lip = true; double k = club.Lag ? Math.Max(1, club.Ratio / 2) : 1; xin = Math.Sign(deg - teeDeg) * 6 * k; yin = 18 * k; }
            }
            if (_entry.Last != null) { _entry.Last.Made = made; _entry.Last.Lip = lip; DrawEntry(); }
            Vector2 toHole = (Hole.Pin - _ball).normalized, side = new Vector2(toHole.y, -toHole.x);
            Vector2 end = made ? Hole.Pin : Hole.Pin + toHole * (float)(yin / 36) + side * (float)(xin / 36);
            Say(club.Name, TracksEngine.DescribePutt(lineIn, rollout));
            _world.Fly(_ball, end, true, () =>
            {
                if (made) { _longestPuttFt = Math.Max(_longestPuttFt, ft); HoleOut(); return; }
                _ball = end; _lie = "green";
                double left = LeaveFt;
                After(lip ? "Lipped out" : club.Lag ? "Lag, scaled from " + club.Ft + " ft" : matMade ? "Holed on the mat" : "Missed", Math.Round(left) + " ft left.", RestPutt);
            });
        }

        void After(string title, string sub, float rest)
        {
            Say(title, sub);
            if (_strokes[_idx] >= TracksEngine.MaxHoleScore) { _strokes[_idx] = TracksEngine.MaxHoleScore; HoleOut(true); return; }
            DrawCard();
            _hud.schedule.Execute(() =>
            {
                _busy = false;
                _world.PlaceBall(_ball);
                _world.SetCam(_onGreen ? TracksWorld.Cam.Green : _world.Mode == TracksWorld.Cam.Green ? TracksWorld.Cam.Broadcast : _world.Mode, _ball);
                DrawCams();
                CallCaddie();
            }).StartingIn((long)(rest * 1000));
        }

        void TapInNow()
        {
            _busy = true;
            _strokes[_idx]++; _putts[_idx]++;
            _world.Fly(_ball, Hole.Pin, true, () => HoleOut());
        }

        void HoleOut(bool pickedUp = false)
        {
            if (!pickedUp && _strokes[_idx] <= Hole.Par) { if (!_gir[_idx]) _scrambles++; if (_holeSand) _sandSaves++; }
            _holed[_idx] = true;
            string label = pickedUp ? "Picked up" : TracksEngine.ScoreName(_strokes[_idx], Hole.Par);
            if (!pickedUp) { _world.Sfx("golf_cup"); if (_strokes[_idx] <= Hole.Par) _world.Sfx("golf_clap", _strokes[_idx] < Hole.Par ? 0.9f : 0.5f); }
            Say(label, _strokes[_idx] + " on the par " + Hole.Par);
            DrawCard();
            Flush(false);
            _busy = true; _club = null; DrawShot(); DrawEntry();
            _hud.schedule.Execute(() => { if (_idx + 1 < _holes.Count) App.Run(StartHole(_idx + 1)); else EndRound(); }).StartingIn((long)(HoleDone * 1000));
        }

        int Par => _holes.Sum(h => h.Par);
        int ToParNow { get { int s = 0, p = 0; for (int i = 0; i < _holes.Count; i++) if (_holed[i]) { s += _strokes[i]; p += _holes[i].Par; } return s - p; } }

        // ── Saving ──

        IEnumerator StartRound()
        {
            // The user_games row opens on the first mat putt; wait a little for it so the round is tied to the game.
            float until = Time.realtimeSinceStartup + 25f;
            while (_saver.RoundId == null && Time.realtimeSinceStartup < until && !_ended) yield return new WaitForSecondsRealtime(1f);
            var body = new Dictionary<string, object>
            {
                { "userGameId", _saver.RoundId }, { "trackSlug", _course.Slug }, { "trackVersion", _course.Version }, { "holeSet", _set }, { "level", _level }, { "look", "broadcast" }, { "par", Par },
                { "players", new List<object> { new Dictionary<string, object> { { "i", 0 }, { "userId", Auth.Uid }, { "name", J.Str(_app.Profile, "displayName", "You") }, { "level", _level } } } },
            };
            if (Fixtures.On || !Green.RealPutts) yield break;
            yield return Api.Post("/tracks/round", body, d => { object r = J.First(d); _roundId = J.Str(r, "id"); }, null);
        }

        Dictionary<string, object> Payload(bool final)
        {
            int played = final ? _holes.Count : Math.Min(_holes.Count, _idx + 1);
            var holes = new List<object>();
            for (int i = 0; i < played; i++) holes.Add(new Dictionary<string, object> { { "n", _holes[i].N }, { "par", _holes[i].Par }, { "s", _strokes[i] }, { "p", _putts[i] }, { "fw", _fairways[i] }, { "gir", _gir[i] } });
            var hs = holes.Cast<Dictionary<string, object>>().ToList();
            var body = new Dictionary<string, object>
            {
                { "strokes", hs.Sum(h => (int)h["s"]) }, { "holesPlayed", played }, { "holes", holes }, { "putts", hs.Sum(h => (int)h["p"]) },
                { "onePutts", hs.Count(h => (int)h["p"] == 1) }, { "threePutts", hs.Count(h => (int)h["p"] >= 3) },
                { "fairwaysHit", hs.Count(h => (bool?)h["fw"] == true) }, { "fairwaysTried", hs.Count(h => h["fw"] != null) }, { "gir", hs.Count(h => (bool)h["gir"]) },
                { "scrambles", _scrambles }, { "sandSaves", _sandSaves }, { "sandTries", _sandHoles }, { "penalties", _penalties },
                { "longestDriveYds", _drives.Count > 0 ? (object)Math.Round(_drives.Max()) : null }, { "avgDriveYds", _drives.Count > 0 ? (object)Math.Round(_drives.Average()) : null },
                { "proximityFt", _approachFt.Count > 0 ? (object)Math.Round(_approachFt.Average()) : null }, { "longestPuttFt", _longestPuttFt > 0 ? (object)Math.Round(_longestPuttFt) : null },
            };
            if (final) { body["completed"] = 1; body["durationSec"] = Mathf.RoundToInt(Time.realtimeSinceStartup - _started); }
            return body;
        }

        void Flush(bool final) { if (_roundId != null) App.Run(Api.Do("PUT", "/tracks/round/" + _roundId, Payload(final), null, null)); }

        void EndRound()
        {
            _done = true;
            int toPar = _strokes.Sum() - Par;
            Flush(true);
            string userGameId = _saver.RoundId;
            _saver.End(toPar);
            string gameId = _saver.GameId;
            userGameId = userGameId ?? _saver.LastRoundId;
            if (gameId != null && userGameId != null && Green.RealPutts)
                App.Run(Api.Post("/club/leaderboard/add", new Dictionary<string, object> { { "clubId", "PUTTR" }, { "gameId", gameId }, { "userGameId", userGameId }, { "userId", Auth.Uid },
                    { "userName", J.Write(new Dictionary<string, object> { { "displayName", J.Str(_app.Profile, "displayName", "Player") }, { "userId", Auth.Uid }, { "photoURL", J.Str(_app.Profile, "photoURL") } }) },
                    { "score", toPar }, { "distance", TracksEngine.LeaderboardCode(_set, _level) } }, null, null));
            Stop();
            _app.Pop();
            _app.Push(new TracksResultScreen(_app, _course, _holes, _strokes, _putts, _fairways, _gir, _level));
        }

        void Leave()
        {
            if (_done) { _app.Pop(); return; }
            Dialog.Ask(Root, "Leave the round?", "Holes you finished are saved. You can't pick this round up again.", "Leave", () => { Flush(false); _saver.Abandon(); Stop(); _app.Pop(); });
        }

        void Stop()
        {
            if (_ended) return;
            _ended = true;
            Green.Console.Putt -= OnPutt;
            _world.Close();
            _app.LeavePractice();
        }

        // ── The broadcast graphics ──

        void Say(string title, string sub)
        {
            _caption.text = title; _captionSub.text = sub;
            VisualElement lower = _hud.Q("lower");
            lower.RemoveFromClassList("hidden");
            string mine = title + sub;
            _hud.schedule.Execute(() => { if (_caption.text + _captionSub.text == mine) lower.AddToClassList("hidden"); }).StartingIn(3400);
        }

        static readonly (TracksWorld.Cam mode, string label)[] Cams = { (TracksWorld.Cam.Broadcast, "Broadcast"), (TracksWorld.Cam.Follow, "Ball follow"), (TracksWorld.Cam.Plan, "Plan"), (TracksWorld.Cam.Green, "Green") };

        // The camera: the one in use as a pill; a tap opens all four, a pick closes them again.
        void DrawCams()
        {
            _camBar.Clear();
            if (!_camsOpen)
            {
                string now = Cams.First(c => c.mode == _world.Mode).label;
                _camBar.Add(Ui.Box("tracks-campill", Ui.Box("tracks-campill-text", Ui.Text("CAMERA", "tracks-k", Ui.Bold), Ui.Text(now, "tracks-cam-t", Ui.Bold)), new TracksChevron(false))
                    .OnTap(() => { _camsOpen = true; DrawCams(); }));
                return;
            }
            VisualElement list = Ui.Box("tracks-camlist", Ui.Box("tracks-camlist-head", Ui.Text("CAMERA", "tracks-k grow", Ui.Bold), new TracksChevron(true)).OnTap(() => { _camsOpen = false; DrawCams(); }));
            foreach (var (mode, label) in Cams)
            {
                var m = mode;
                list.Add(Ui.Box(_world.Mode == m ? "tracks-camopt tracks-camopt-on" : "tracks-camopt", Ui.Text(label, "tracks-cam-t", Ui.Bold))
                    .OnTap(() => { _world.SetCam(m, _ball); _camsOpen = false; DrawCams(); }));
            }
            _camBar.Add(list);
        }

        // The scorecard: hole, par and strokes per hole, the total, and to par. Upright the nine being played,
        // on its side the whole round with the nines added up.
        void DrawCard()
        {
            _card.Clear();
            bool all = _wide || _holes.Count <= 9;
            int from = all ? 0 : _idx / 9 * 9, to = all ? _holes.Count : Math.Min(_holes.Count, from + 9);
            VisualElement cols = Ui.Box("tracks-sc-row");
            cols.Add(Col("tracks-sc-col tracks-sc-lab", "HOLE", "PAR", "SCORE", null));
            string Played(int i) => _strokes[i] > 0 ? _strokes[i].ToString() : "";
            for (int i = from; i < to; i++)
            {
                string s = Played(i);
                string tone = null;
                if (s.Length > 0 && _holed[i]) { int d = _strokes[i] - _holes[i].Par; tone = d < 0 ? "tracks-under" : d > 0 ? "tracks-over" : null; }
                cols.Add(Col("tracks-sc-col" + (i == _idx && !_done ? " tracks-sc-now" : ""), _holes[i].N.ToString(), _holes[i].Par.ToString(), s, tone));
                // On its side an 18: the front nine's total after hole 9.
                if (all && _holes.Count > 9 && i == 8) cols.Add(Sum("OUT", 0, 9));
            }
            if (all && _holes.Count > 9) cols.Add(Sum("IN", 9, _holes.Count));
            cols.Add(Sum("TOT", 0, _holes.Count));
            int toPar = ToParNow;
            Label tp = Ui.Text(TracksEngine.ToPar(toPar), "tracks-sc-topar", Ui.Bold);
            if (toPar < 0) tp.AddToClassList("tracks-under");
            cols.Add(Ui.Box("tracks-sc-parbox", Ui.Text("TO PAR", "tracks-k", Ui.Bold), tp));
            _card.Add(cols);
        }

        VisualElement Sum(string label, int from, int to)
        {
            int par = 0, strokes = 0; bool any = false;
            for (int i = from; i < Math.Min(to, _holes.Count); i++) { par += _holes[i].Par; strokes += _strokes[i]; any |= _strokes[i] > 0; }
            return Col("tracks-sc-col tracks-sc-sum", label, par.ToString(), any ? strokes.ToString() : "", null);
        }

        static VisualElement Col(string cls, string hole, string par, string score, string tone)
        {
            Label s = Ui.Text(score, "tracks-sc-s", Ui.Bold);
            if (tone != null) s.AddToClassList(tone);
            return Ui.Box(cls, Ui.Text(hole, "tracks-sc-n", Ui.Bold), Ui.Text(par, "tracks-sc-p", Ui.Medium), s);
        }

        // The entry panel: the window to make it from this tee, and the last putt's path and entry after it.
        void DrawEntry()
        {
            bool show = _club != null && !_club.Tap || _entry.Last != null;
            _entryBox.EnableInClassList("hidden", !show);
            if (!show) return;
            if (_club != null)
            {
                _entry.WindowDeg = _club.Lag ? 2 * TracksEngine.LagTolerance(_level, LeaveFt) : TracksEngine.LevelOf(_level).Entry;
                _entry.TeeDeg = AppPutt.TeeEntry(_club.Ft, _tee);
            }
            _entryTop.text = _entry.WindowDeg >= 180 ? "FRONT DOOR" : "WINDOW " + Math.Round(_entry.WindowDeg) + "°";
            TracksEntryView.Putt last = _entry.Last;
            string verdict = "";
            if (last != null && last.Green && (last.Made || last.Lip))
            {
                verdict = last.Made ? "IN" : "LIP OUT";
                if (last.EntryDeg != null) { double off = last.EntryDeg.Value - _entry.TeeDeg; verdict += " · " + Math.Abs(Math.Round(off)) + "° " + (off < 0 ? "L" : "R"); }
            }
            else if (last == null && _club != null) verdict = _tee == 1 ? "BREAKS L TO R" : _tee == 3 ? "BREAKS R TO L" : "STRAIGHT";
            _entryBottom.text = verdict;
            _entryBottom.EnableInClassList("tracks-entry-miss", last != null && last.Lip);
            _entry.MarkDirtyRepaint();
        }

        // The shot bar: the caddie's club, and the mat putt as other games give it: distance and tee.
        void DrawShot()
        {
            _shotBar.Clear();
            _simRow.Clear();
            _shotBar.RemoveFromClassList("hidden"); _simRow.RemoveFromClassList("hidden");
            if (_club == null) { _shotBar.AddToClassList("hidden"); return; }
            string clubLine = _club.Tap ? "Tap in" : _club.Putt ? Math.Round(LeaveFt) + " ft to the hole" : _club.Pitch ? Math.Round(DistYds) + " yds pitch" : _club.Yds + " yds · " + Math.Round(DistYds) + " to the pin";
            VisualElement clubCard = Ui.Box("tracks-club", Ui.Text(_onGreen ? "ON THE GREEN" : "FROM THE " + TracksEngine.LieOf(_lie).Name.ToUpperInvariant(), "tracks-k", Ui.Bold), Ui.Text(_club.Name, "tracks-club-v", Ui.Bold), Ui.Text(clubLine, "tracks-club-s", Ui.Regular));
            if (!_onGreen && !_club.Tap) { clubCard.Add(Ui.Text("Change club", "tracks-change", Ui.Bold)); clubCard.OnTap(PickClub); }
            string head = _club.Tap ? "Gimme" : _club.Ft + " ft   ·   " + PracticeSession.TeeName(_tee) + " tee";
            string hint = _club.Tap ? "Inside 2.5 ft. It taps itself in."
                : _club.Lag ? "Lag it close. Your miss scales up " + _club.Ratio.ToString("0.#") + " times."
                : _club.Putt ? "Make it." : "Dead straight, perfect speed.";
            VisualElement puttCard = Ui.Box("tracks-puttcard grow", Ui.Text("YOUR MAT PUTT", "tracks-k", Ui.Bold), Ui.Text(head, "tracks-putt-v", Ui.Bold), Ui.Text(hint, "tracks-club-s", Ui.Regular));
            if (!_club.Tap) _shotBar.Add(clubCard);   // a gimme has no club to call: the putt card says it all
            _shotBar.Add(puttCard);
            bool canSim = Fixtures.On || J.Flag(_app.Profile, "isTester") || Application.isEditor;
            if (canSim && !_club.Tap) _simRow.Add(Ui.Button("Test putt", Simulate, "btn tracks-sim"));
        }

        void PickClub()
        {
            if (_busy) return;
            VisualElement dim = null;
            VisualElement list = Ui.Box("modal", Ui.Text("Change club", "h2", Ui.Bold), Ui.Text("The caddie's pick is highlighted. A shorter club lays up along the hole.", "muted wrap", Ui.Regular));
            foreach (TracksEngine.Club c in TracksEngine.Playable(_lie))
            {
                TracksEngine.Club club = c.With(TracksEngine.PuttFor(c, _lie) ?? c.Ft);
                bool on = _club != null && club.Id == _club.Id;
                list.Add(Ui.Box(on ? "set-row choice-on" : "set-row", Ui.Text(club.Name, "feed-name grow", Ui.Bold), Ui.Text(club.Yds + " yds · " + club.Ft + " ft putt", "muted", Ui.Regular))
                    .OnTap(() => { dim.RemoveFromHierarchy(); _club = club; DrawShot(); }));
            }
            list.Add(Ui.Button("Keep the caddie's club", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            list.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", list).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }
    }

    /// <summary>
    /// The entry to make it (puttr-app tracks/PuttReadout.js): the mat around the cup, 8 in either side, 11 in
    /// short of it to 5 in past, drawn to scale. The window to come in through is a pie on the cup, centred on the
    /// tee's entry so it turns with the break, with the line in from the tee. After a putt: its path, and on the
    /// green a dashed line through the cup where it went in, green inside the window and red outside it.
    /// </summary>
    public sealed class TracksEntryView : VisualElement
    {
        public sealed class Putt { public Vector2[] Path; public bool Made, Lip, Green; public double? EntryDeg; }
        const float XIn = 8f, YMin = -11f, YMax = 5f, CupR = 2.125f;   // zoomed in from the app's 24 in square so the window reads on a phone
        public double WindowDeg = 180, TeeDeg;
        public Putt Last;

        public TracksEntryView()
        {
            AddToClassList("tracks-entry-view");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            float size = contentRect.width;
            if (size <= 0f) return;
            float s = size / (XIn * 2);
            Vector2 Px(Vector2 p) => new Vector2((Mathf.Clamp(p.x, -XIn, XIn) + XIn) * s, (YMax - Mathf.Clamp(p.y, YMin, YMax)) * size / (YMax - YMin));
            Vector2 hole = Px(Vector2.zero);
            float cupR = CupR * s;
            // degrees on the rim: 0 is the front door (toward the bottom), left negative
            Vector2 Rim(double deg, float r) { float a = (float)(deg * Math.PI / 180); return hole + new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r); }
            Painter2D p = ctx.painter2D;
            p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;

            // faint grid and the centre line
            p.strokeColor = new Color(1, 1, 1, 0.05f); p.lineWidth = 1;
            for (int i = 1; i < 4; i++) { Seg(p, new Vector2(0, size * i / 4f), new Vector2(size, size * i / 4f)); Seg(p, new Vector2(size * i / 4f, 0), new Vector2(size * i / 4f, size)); }

            Color green = Ui.Bright, red = Ui.Red;
            double centre = TeeDeg, half = Math.Min(90, WindowDeg / 2);
            bool entered = Last != null && Last.Green && Last.EntryDeg != null && (Last.Made || Last.Lip);
            bool entryOk = entered && Math.Abs(Last.EntryDeg.Value) <= 90 && Math.Abs(Last.EntryDeg.Value - centre) <= half;

            // the line in from the tee: through the middle of the window and out the bottom of the mat
            if (Last == null)
            {
                p.strokeColor = new Color(green.r, green.g, green.b, 0.55f); p.lineWidth = 1.5f;
                Vector2 from = Rim(centre, cupR + 3), dir = (from - hole).normalized;
                float len = (size - from.y) / Mathf.Max(0.2f, dir.y);
                Dashed(p, from, from + dir * len, 4, 3);
            }

            // the cup, its ring, and the window as a pie on it
            p.strokeColor = new Color(1, 1, 1, 0.12f); p.lineWidth = 1;
            p.BeginPath(); p.Arc(hole, cupR + 3, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            p.fillColor = new Color(0, 0, 0, 0.75f); p.strokeColor = new Color(1, 1, 1, 0.35f); p.lineWidth = 1.5f;
            p.BeginPath(); p.Arc(hole, cupR, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill(); p.Stroke();
            Color pie = entered ? (entryOk ? new Color(green.r, green.g, green.b, 0.45f) : new Color(red.r, red.g, red.b, 0.35f)) : new Color(green.r, green.g, green.b, 0.4f);
            p.fillColor = pie; p.strokeColor = new Color(green.r, green.g, green.b, 0.95f); p.lineWidth = 1.2f;
            p.BeginPath(); p.MoveTo(hole);
            int steps = Mathf.Max(6, (int)(half / 4));
            for (int i = 0; i <= steps; i++) p.LineTo(Rim(centre - half + 2 * half * i / steps, cupR));
            p.ClosePath(); p.Fill(); p.Stroke();

            if (Last == null || Last.Path == null || Last.Path.Length < 2) return;
            // the putt's path
            p.strokeColor = new Color(1, 1, 1, 0.85f); p.lineWidth = 2;
            p.BeginPath(); p.MoveTo(Px(Last.Path[0]));
            for (int i = 1; i < Last.Path.Length; i++) p.LineTo(Px(Last.Path[i]));
            p.Stroke();
            if (entered)
            {
                Color tone = entryOk ? green : red;
                Vector2 e = Rim(Last.EntryDeg.Value, cupR);
                p.strokeColor = tone; p.lineWidth = 1.5f;
                Dashed(p, hole - (e - hole) * 1.6f, e + (e - hole) * 1.6f, 3, 2);
                p.fillColor = tone; p.strokeColor = Color.black; p.lineWidth = 1;
                p.BeginPath(); p.Arc(e, 4.5f, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill(); p.Stroke();
            }
            else if (!Last.Made)
            {
                p.fillColor = Color.white; p.strokeColor = Last.Green ? red : Color.black; p.lineWidth = 1.5f;
                p.BeginPath(); p.Arc(Px(Last.Path[Last.Path.Length - 1]), 5, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill(); p.Stroke();
            }
        }

        static void Seg(Painter2D p, Vector2 a, Vector2 b) { p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke(); }

        static void Dashed(Painter2D p, Vector2 a, Vector2 b, float on, float off)
        {
            float len = Vector2.Distance(a, b);
            if (len < 0.5f) return;
            Vector2 d = (b - a) / len;
            for (float t = 0; t < len; t += on + off) Seg(p, a + d * t, a + d * Mathf.Min(len, t + on));
        }
    }

    /// <summary>A small chevron, down (closed) or up (open), for the camera pill.</summary>
    public sealed class TracksChevron : VisualElement
    {
        readonly bool _up;

        public TracksChevron(bool up)
        {
            _up = up;
            AddToClassList("tracks-chev");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx =>
            {
                float w = contentRect.width, h = contentRect.height;
                if (w <= 0) return;
                Painter2D p = ctx.painter2D;
                p.strokeColor = Color.white; p.lineWidth = 2; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                p.BeginPath();
                if (_up) { p.MoveTo(new Vector2(w * 0.2f, h * 0.65f)); p.LineTo(new Vector2(w * 0.5f, h * 0.35f)); p.LineTo(new Vector2(w * 0.8f, h * 0.65f)); }
                else { p.MoveTo(new Vector2(w * 0.2f, h * 0.35f)); p.LineTo(new Vector2(w * 0.5f, h * 0.65f)); p.LineTo(new Vector2(w * 0.8f, h * 0.35f)); }
                p.Stroke();
            };
        }
    }

    /// <summary>Home's Track of the week (puttr-app main/TrackOfTheWeek.js, GET /tracks/featured): a world-ranked course a week, its record, and Play.</summary>
    public static class TrackOfTheWeek
    {
        public static void Fill(App app, VisualElement into, object data)
        {
            into.Clear();
            object t = J.Get(data, "track");
            if (t == null) return;
            bool free = J.Flag(t, "isFree");
            bool locked = !free && Club.Blocked(app.Profile);
            bool fresh = J.Int(data, "myRoundsTotal") == 0;
            object low = J.Get(data, "lowest");
            object mine = J.Get(data, "myBest");
            // The record to chase: the lowest round on it, or yours when you hold it.
            string record = null;
            if (low != null && J.Get(low, "toPar") != null)
            {
                int lp = J.Int(low, "toPar");
                record = mine != null && J.Int(data, "myBest") <= lp ? "Your record " + TracksEngine.ToPar(lp) : "Record " + TracksEngine.ToPar(lp) + " · " + J.Str(low, "displayName", "Player");
            }
            else if (mine != null) record = "Your best " + TracksEngine.ToPar(J.Int(data, "myBest"));
            int week = J.Int(data, "roundsThisWeek");
            VisualElement chips = Ui.Box("chips", Ui.Text("Par " + J.Int(t, "par"), "chip"), Ui.Text(J.Int(t, "yds").ToString("#,0") + " yds", "chip"));
            if (record != null) chips.Add(Ui.Text(record, "chip chip-green"));
            if (week > 0) chips.Add(Ui.Text(week + (week == 1 ? " round" : " rounds") + " this week", "chip"));
            VisualElement kick = Ui.Box("row", Ui.Text("TRACK OF THE WEEK", "kick", Ui.Bold), fresh ? Ui.Text("NEW", "chip chip-green").Margin(0) : null);
            VisualElement card = Ui.Box("gcard gcard-big totw");
            card.style.backgroundImage = new StyleBackground(GameCards.Picture(new Dictionary<string, object> { { "gameType", "TRACKS" } }));
            var shade = new Image { image = GameCards.Fade, scaleMode = ScaleMode.StretchToFill };
            shade.AddToClassList("gcard-shade");
            shade.pickingMode = PickingMode.Ignore;
            card.Add(shade);
            card.Add(Ui.Box("totw-top", Ui.Box("totw-kick", kick)));
            card.Add(Ui.Box("totw-bottom",
                TrackLabels.Inspired(J.Str(t, "name"), "gcard-name gcard-name-big"), TrackLabels.Where(J.Str(t, "location"), "gcard-line"),
                chips,
                Ui.Box("row", Ui.Button("All courses", () => app.Push(new TracksCoursesScreen(app)), "btn btn-ghost grow totw-btn"), FreePuttScreen.Space(10),
                    Ui.Button(locked ? "Puttr Club" : "Play", () => { if (free) app.Push(new TracksSetupScreen(app, t)); else Club.Require(app, "Every Tracks course", () => app.Push(new TracksSetupScreen(app, t))); }, "btn grow totw-btn"))));
            into.Add(Ui.Box("pad", card).Margin(18));
            // The card is the course itself: all of it from overhead, painted once and kept (TracksArt).
            // Its own shape (the card is nearly square), the course in the top half above the words.
            TracksArt.Show(card, t, TracksArt.CardW, 860, still => card.style.backgroundImage = new StyleBackground(still.Tex), 0.1f, 0.52f);
        }
    }

    public sealed class TracksResultScreen : AppScreen
    {
        public TracksResultScreen(App app, TracksCourse course, List<TracksCourse.Hole> holes, int[] strokes, int[] putts, bool?[] fairways, bool[] gir, string level)
        {
            Root = Page.Make(app, course.Name, out VisualElement body);
            int total = strokes.Sum(), par = holes.Sum(h => h.Par), toPar = total - par;
            body.Add(Ui.Box("pad center-box", Ui.Text(TracksEngine.LevelOf(level).Name.ToUpperInvariant() + " · " + holes.Count + " HOLES", "kick", Ui.Bold),
                Ui.Text(TracksEngine.ToPar(toPar), "code", Ui.Bold), Ui.Text(total + " strokes on a par " + par, "sub", Ui.Regular)));
            int fwTried = fairways.Count(f => f != null), fwHit = fairways.Count(f => f == true);
            body.Add(Ui.Box("pad stat-tiles", Tile(fwTried > 0 ? fwHit + "/" + fwTried : "–", "FAIRWAYS"), Tile(gir.Count(g => g) + "/" + holes.Count, "GREENS"), Tile(putts.Sum().ToString(), "PUTTS")));
            VisualElement card = Ui.Box("glass set-card");
            for (int i = 0; i < holes.Count; i++)
                card.Add(Ui.Box("set-row", Ui.Text(holes[i].N.ToString(), "board-place", Ui.Bold), Ui.Box("grow", Ui.Text(holes[i].Name.Length > 0 ? holes[i].Name : "Hole " + holes[i].N, "feed-name", Ui.Bold), Ui.Text("Par " + holes[i].Par + " · " + putts[i] + (putts[i] == 1 ? " putt" : " putts"), "muted", Ui.Regular)),
                    Ui.Text(strokes[i] + "  " + TracksEngine.ScoreName(strokes[i], holes[i].Par), "feed-name", Ui.Bold)));
            body.Add(Ui.Section("Scorecard"));
            body.Add(Ui.Box("pad", card));
            body.Add(Form.Bottom(Ui.Button("Exit", app.ExitToTab)));
        }

        static VisualElement Tile(string v, string k) => Ui.Box("glass stat-tile", Ui.Text(v, "stat-value", Ui.Bold), Ui.Text(k, "stat-label", Ui.Bold));
    }
}
