// Tracks, broadcast golf (puttr-app components/games/Tracks.js, played on a 3D hole: TracksWorld):
//
//   TracksCoursesScreen  GET /tracks/courses: the Collection by rank, your best on each, Club lock on the rest
//   TracksSetupScreen    the course, Front 9 / Back 9 / 18, Amateur / Pro / Major
//   TracksPlayScreen     the round: the club and the mat putt are called and the mat putt, the putt plays the shot (line sets how
//                        straight, pace how far), lies cost a foot or two on the mat, water is a penalty and a drop,
//                        on the green the putt is its real length to 11 ft and a quarter-length lag beyond, the cup has
//                        a front window by level, gimmes tap in, 12 picks up. 1 to 4 Players take turns as golf does
//                        (TracksRound): everyone tees off in order, then whoever is farthest from the hole plays
//
// Saving as the app: POST /game/new (gameType TRACKS) with each mat putt (RoundSaver), POST /tracks/round at tee
// off, PUT /tracks/round/:id after every hole and at the end, /game/over and the leaderboard with strokes to par.
// With more Players, as the app: one game for the round with every mat putt under the Player who hit it (a guest's
// under 0), a /tracks/round row per Player, /game/multiplayer/over with everyone's strokes to par, and the
// signed-in Player's score on the board.
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
            Ui.Box("tracks-title-box", Ui.Text("Inspired by", "body-2 tracks-inspired", Ui.Medium), Ui.Title(name, titleClass));

        public static VisualElement Where(string location, string textClass)
        {
            Label words = Ui.Text(location, textClass, Ui.Regular);
            return Ui.Box("row tracks-where", Ui.Icon("MapPin", 13, Ui.Faint), words);
        }
    }

    public sealed class TracksCoursesScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body, _tabs, _list;
        readonly TextField _search;
        List<object> _rows;
        int _tab;   // 0 Collection (Puttr's picks), 1 Community (added by Players), 2 Leaderboard across every track
        static bool _devSetupOpened;

        public TracksCoursesScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Tracks", out _body);
            _tabs = Ui.Box("pad");
            _body.Add(_tabs);
            // Search the tracks (both tabs), with Add a track beside it.
            _search = Form.Field("Search tracks", "");
            _search.AddToClassList("grow");
            _search.RegisterValueChangedCallback(_ => DrawList());
            VisualElement add = Ui.Button("Add a track", AddTrack, "btn btn-ghost tracks-add-btn");
            _body.Add(Ui.Box("pad row tracks-search", _search, add));
            _list = new VisualElement();
            _body.Add(_list);
            _list.Add(Page.Loading());
            // -app2TracksTab 1|2: open on Community or Leaderboard; -app2TracksAdd: the Add a track sheet (for screenshots from a script).
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-app2TracksTab");
            if (at >= 0 && at + 1 < args.Length) int.TryParse(args[at + 1], out _tab);
            if (Array.IndexOf(args, "-app2TracksAdd") >= 0) Root.schedule.Execute(() => { AddTrack(); }).StartingIn(1500);
            DrawTabs();
        }

        public override void Shown()
        {
            App.Run(Api.Get("/tracks/courses", d => { _rows = J.List(d); DrawList(); }, _ => { if (_rows != null) return; _list.Clear(); _list.Add(Page.Note("Couldn't load the courses. Check your connection.")); }));
        }

        void DrawTabs()
        {
            _tabs.Clear();
            // The row bakes its highlight in when it is built, so a pick draws the row again as well as the list.
            _tabs.Add(FreePuttScreen.Choice(new[] { "Collection", "Community", "Leaderboard" }, _tab, i => { _tab = i; DrawTabs(); DrawList(); }));
            if (_search.parent != null) _search.parent.style.display = _tab == 2 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        bool Mine(object c) => J.Str(c, "userId").Length > 0 && J.Str(c, "userId") == J.Str(_app.Profile, "userId", Auth.Uid);
        static bool Community(object c) => J.Str(c, "userId").Length > 0 && J.Get(c, "rank") == null;

        void DrawList()
        {
            if (_search.parent != null) _search.parent.style.display = _tab == 2 ? DisplayStyle.None : DisplayStyle.Flex;
            _list.Clear();
            if (_tab == 2)
            {
                // The board draws its own sections (a header and one bordered block of rows per level).
                VisualElement board = new VisualElement();
                _list.Add(Ui.Box("pad", Ui.Text("Last 30 days. Each Player's best round at each level, on any track.", "body-2", Ui.Regular)));
                _list.Add(board);
                TracksBoard.Fill(_app, board, null);
                return;
            }
            if (_rows == null) { _list.Add(Page.Loading()); return; }
            string f = _search.value.Trim().ToLowerInvariant();
            List<object> shown;
            if (f.Length > 0)
            {
                // A search looks across both tabs, your own tracks first.
                shown = _rows.FindAll(c => new[] { J.Str(c, "name"), J.Str(c, "location"), J.Str(c, "short"), J.Str(c, "creatorName"), J.Str(c, "creatorHomeTown") }.Any(v => v.ToLowerInvariant().Contains(f)));
                shown = shown.OrderBy(c => Mine(c) ? 0 : 1).ThenBy(c => Community(c) ? 1 : 0).ToList();
            }
            else
            {
                shown = _rows.FindAll(c => Community(c) == (_tab == 1));
                if (_tab == 1) shown = shown.OrderBy(c => Mine(c) ? 0 : 1).ToList();
            }
            bool member = !Club.Blocked(_app.Profile);
            VisualElement cols = Ui.Box("cols");
            foreach (object c in shown)
            {
                object course = c;
                bool mine = Mine(c), community = Community(c);
                // As Quests: the free tracks are for everyone; the rest, and every community track but your own, are Puttr Club.
                bool locked = !member && !J.Flag(c, "isFree") && !mine;
                string best = J.Get(c, "myBest") != null ? "Your best " + TracksEngine.ToPar(J.Int(c, "myBest")) : J.Int(c, "rounds") > 0 ? J.Int(c, "rounds") + (J.Int(c, "rounds") == 1 ? " round played" : " rounds played") : "Not played yet";
                // The course from overhead, every hole on it (painted as the card scrolls into view, then kept).
                // Nothing is written over the picture: the rank and Club / Free are chips on the bar under it.
                VisualElement art = CourseStill(c);
                VisualElement body = Ui.Box("tracks-ccard-body");
                if (community)
                {
                    // Who added it: you, or the Player and their home town.
                    string by = mine ? "YOUR TRACK" : "COMMUNITY TRACK";
                    VisualElement who = Ui.Box("row tracks-by", Ui.Avatar(J.Str(c, "creatorName", "Player"), J.Str(c, "creatorPhoto")),
                        Ui.Box("grow", Ui.Caps(by, mine ? "caps caps-green" : "caps"),
                            mine ? null : Ui.Text(J.Str(c, "creatorName", "A Puttr player") + (J.Str(c, "creatorHomeTown").Length > 0 ? " · " + J.Str(c, "creatorHomeTown") : ""), "body-2", Ui.Regular)));
                    body.Add(who);
                    body.Add(Ui.Title(J.Str(c, "name"), "title-20"));
                }
                else body.Add(TrackLabels.Inspired(J.Str(c, "name"), "title-20"));
                body.Add(TrackLabels.Where(J.Str(c, "location"), "body-2"));
                body.Add(Ui.Box("chips", J.Get(c, "rank") != null ? Ui.Text("No. " + J.Int(c, "rank"), "chip") : null,
                    locked ? Ui.Text("Club", "chip chip-amber") : J.Flag(c, "isFree") ? Ui.Text("Free", "chip chip-green") : null,
                    Ui.Text("Par " + J.Int(c, "par"), "chip"), Ui.Text(J.Int(c, "holes") + " holes", "chip"), Ui.Text(J.Int(c, "yds").ToString("#,0") + " yds", "chip"), Ui.Text(best, "chip")));
                VisualElement card = Ui.Box("tracks-ccard", art, body);
                card.OnTap(() => { if (locked) _app.Push(new ClubScreen(_app, "Every Tracks course")); else _app.Push(new TracksSetupScreen(_app, course)); });
                cols.Add(Ui.Box("col pad", card).Margin(12));
            }
            if (shown.Count == 0)
                _list.Add(Page.Note(f.Length > 0 ? "No tracks match \"" + _search.value.Trim() + "\". Add it from the map with Add a track."
                    : _tab == 1 ? "No community tracks yet. Add your home course from the map and it shows up here for everyone." : "No courses yet."));
            _list.Add(cols);
            // -app2TracksSetup: open the first course's setup (for screenshots from a script).
            if (!_devSetupOpened && _rows.Count > 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TracksSetup") >= 0) { _devSetupOpened = true; _app.Push(new TracksSetupScreen(_app, _rows[0])); }
        }

        // Adding a track is for members, as in the app.
        void AddTrack() => Club.Require(_app, "Add your own tracks", () => AddTrackSheet.Open(_app, Root,
            added =>
            {
                // Your new track, first on Community; the catalog is fetched again so its card is complete.
                _tab = 1; _search.SetValueWithoutNotify(""); DrawTabs();
                App.Run(Api.Get("/tracks/courses", d => { _rows = J.List(d); DrawList(); }, null));
                _app.Toast("Added " + J.Str(added, "name", "your track") + ".");
            },
            name => { _search.value = name ?? ""; }));

        /// <summary>A box the shape of the still (2:1), the still faded in when it is painted, the hole numbers over it.</summary>
        /// <summary>The course from overhead, painted w by h (the card's 2:1 unless asked: Home's tablet card is a square).</summary>
        public static VisualElement CourseStill(object meta, int w = TracksArt.CardW, int h = TracksArt.CardH)
        {
            VisualElement art = Ui.Box("tracks-art");
            var pic = new Image { scaleMode = ScaleMode.StretchToFill };
            pic.AddToClassList("tracks-art-pic");
            pic.pickingMode = PickingMode.Ignore;
            art.Add(pic);
            art.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float tall = Mathf.Round(e.newRect.width * h / w);
                if (tall > 0 && Mathf.Abs(e.newRect.height - tall) > 1f) art.style.height = tall;
            });
            TracksArt.Show(art, meta, w, h, still =>
            {
                pic.image = still.Tex;
                TracksArt.Numbers(pic, still);
                pic.AddToClassList("tracks-art-on");
            });
            return art;
        }
    }

    /// <summary>The round's setup under a live blimp shot of the course: the camera drifts high over the holes in order,
    /// banking gently through the turns, in a bordered window at the top of the page (docs/design-guidelines.md: 3D sits
    /// in a window, its words on a solid bar under it). The world is built when the screen comes to the front and put
    /// away when anything else does.</summary>
    public sealed class TracksSetupScreen : AppScreen
    {
        const string LevelKey = "app2.tracks.level", SetKey = "app2.tracks.set";
        readonly App _app;
        readonly object _meta;
        readonly Image _fly;
        readonly VisualElement _flyBox;   // the window the fly-over shows in
        VisualElement _loading;   // Tee off: the flyover frozen under the Puttr spinner while the round loads
        string _level, _set;
        TracksWorld _world;
        int _visit;
        /// <summary>Who plays (Tracks.js renderPlayersCard: 1 to 4 local Players, taking turns on the one mat). The app's
        /// own picker: you, then anyone added: a profile on this account or a guest. A kid's profile plays alone.</summary>
        readonly PlayerPicker _picker;
        static readonly Dictionary<string, float> Share = new Dictionary<string, float>();   // where the blimp was on each course, to pick the flight up there

        public TracksSetupScreen(App app, object meta)
        {
            _app = app;
            _meta = meta;
            _level = Pref(LevelKey, "pro");
            _set = Pref(SetKey, "all");
            if (J.Int(meta, "holes") < 18) _set = "all";
            if (!Kid.IsKid(app.Profile)) _picker = new PlayerPicker(app, 1, 4, false);
            // The window: black while the course builds, then the fly-over faded in.
            _fly = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            _fly.AddToClassList("tracks-fly");
            _fly.pickingMode = PickingMode.Ignore;
            _flyBox = Ui.Box("tracks-fly-box", _fly);
            Draw();
            // Tee off stays at the foot of the screen, under the page: the setup scrolls above it, so it can always be pressed.
            VisualElement bar = Ui.Box("tracks-teebar", Ui.Button("Tee off", Start));
            _pageSlot = Ui.Box("tracks-setup-page");
            Root = Ui.Box("tracks-setup", Ui.Header("Tracks", _app.Pop), _pageSlot, bar);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Arrange());
            Root.RegisterCallback<AttachToPanelEvent>(_ => Root.schedule.Execute(Arrange));
            _flyBox.RegisterCallback<GeometryChangedEvent>(_ => Size());
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Land());
        }

        static string Pref(string key, string fallback) { try { return PlayerPrefs.GetString(key, fallback); } catch (Exception) { return fallback; } }

        VisualElement _pageSlot, _courseCard, _courseBar, _form, _left;
        bool? _two;

        /// <summary>
        /// One column, or on a wide screen on its side two (Matt, 2026-11-26): the fly-over in a square window with the
        /// course under it on the left, and the round's settings on the right, where they fit without scrolling.
        /// </summary>
        void Arrange()
        {
            float w = Root.resolvedStyle.width, h = Root.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0f) return;
            bool two = w >= 900f && w > h;
            if (_two != two)
            {
                _two = two;
                _pageSlot.Clear();
                _flyBox.style.height = StyleKeyword.Null;
                if (two)
                {
                    _left = Ui.Box("tracks-setup-left", _courseCard);
                    _left.RegisterCallback<GeometryChangedEvent>(_ => Square());
                    ScrollView right = Ui.Scroll();
                    right.AddToClassList("tracks-setup-right");
                    right.Add(Ui.Box("pad", _form));
                    _pageSlot.Add(Ui.Box("tracks-setup-cols", _left, right));
                }
                else
                {
                    _left = null;
                    ScrollView page = Ui.Scroll();
                    page.AddToClassList("tracks-setup-one");
                    page.Add(Ui.Box("pad", _courseCard));
                    page.Add(Ui.Box("pad", _form));
                    page.Add(FreePuttScreen.Space(24));
                    _pageSlot.Add(page);
                }
            }
            if (two) Square();
        }

        // The fly-over's window is as tall as it is wide, or as tall as the column leaves it above the course's bar.
        void Square()
        {
            if (_left == null) return;
            float w = _courseCard.resolvedStyle.width, room = _left.resolvedStyle.height - _courseBar.resolvedStyle.height - 34f;
            if (float.IsNaN(w) || float.IsNaN(room) || w < 10f || room < 10f) return;
            float side = Mathf.Floor(Mathf.Min(w, room));
            if (Mathf.Abs(_flyBox.resolvedStyle.height - side) > 1f) _flyBox.style.height = side;
        }

        void Draw()
        {
            // The header is the page's own (Ui.Header). The fly-over's window, then the course on a solid bar under it:
            // "Inspired by" and its name, the place, par and yards, with the "?" (how it plays) at the right.
            VisualElement bar = Ui.Box("tracks-ccard-body",
                Ui.Box("row tracks-setup-name", Ui.Box("grow tracks-setup-words", TrackLabels.Inspired(J.Str(_meta, "name"), "title"), TrackLabels.Where(J.Str(_meta, "location"), "body-2")),
                    Ui.IconSquare("Question", HowItPlays)),
                Ui.Box("chips", Ui.Text("Par " + J.Int(_meta, "par"), "chip"), Ui.Text(J.Int(_meta, "yds").ToString("#,0") + " yds", "chip")));
            _courseBar = bar;
            _courseCard = Ui.Box("tracks-ccard", _flyBox, bar);
            VisualElement p = Ui.Box("tracks-setup-form");
            // Players first, as the app's setup has its Players card before the round's settings.
            if (_picker != null) p.Add(Ui.Box("tracks-players", _picker.Root));
            if (J.Int(_meta, "holes") >= 18)
            {
                p.Add(Form.Label("Holes"));
                string[] sets = { "front", "back", "all" };
                p.Add(Form.Pills(new[] { "Front 9", "Back 9", "18 holes" }, Array.IndexOf(sets, _set), i => { _set = sets[i]; PlayerPrefs.SetString(SetKey, _set); }));
            }
            p.Add(Form.Label("Level"));
            Label blurb = Ui.Text(TracksEngine.LevelOf(_level).Blurb, "body-2", Ui.Regular);
            p.Add(Form.Pills(TracksEngine.Levels.Select(l => l.Name).ToArray(), Array.FindIndex(TracksEngine.Levels, l => l.Id == _level), i => { _level = TracksEngine.Levels[i].Id; PlayerPrefs.SetString(LevelKey, _level); blurb.text = TracksEngine.Levels[i].Blurb; }));
            p.Add(blurb);
            // Commentary is the one voice switch: the booth's lines, the gallery and the putt call. (Caddie mode, the spoken
            // advice before every shot, was taken out 2026-11-19: Matt, too much; the commentary is enough.)
            // Under it in the same block, the track's own board (TracksLeaderboardScreen), from its setup as in the app.
            string slug = J.Str(_meta, "slug"), name = J.Str(_meta, "name");
            VisualElement rows = Ui.Box("card tb-card tracks-switches",
                Rows.Row("Megaphone", "Commentary", null, Rows.Switch(TracksVoice.Commentary, on => TracksVoice.Commentary = on)),
                Rows.Row("ListNumbers", "Leaderboard", () => _app.Push(new TracksLeaderboardScreen(_app, slug, name))));
            TracksResultScreen.LastRow(rows);
            p.Add(rows.Margin(16));
            _form = p;
        }

        /// <summary>"How it plays", from the "?" at the top of the panel.</summary>
        void HowItPlays()
        {
            VisualElement dim = null;
            void Close() => dim?.RemoveFromHierarchy();
            VisualElement card = Ui.Box("modal",
                Ui.Box("row", Ui.Title("How it plays", "title-20 grow"), Ui.Box("tracks-add-x", Ui.Icon("X", 18, Ui.Faint)).OnTap(Close)),
                Ui.Text("Puttr picks the club and calls the putt: a driver is an 11 ft putt, a wedge 4 ft. Your line sets how straight it flies and your pace how far. From the rough or a bunker the putt gets longer. On the green it's a real putt.", "body-2", Ui.Regular).Margin(12));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(Close);
            dim.Face(Ui.Medium);
            App.Layer(Root).Add(dim);
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

        /// <summary>The fly-over draws at its window's own shape, at up to twice its points (1600 on the long side).</summary>
        void Size()
        {
            if (_world == null) return;
            Rect r = _flyBox.layout;
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
            _loading?.RemoveFromHierarchy(); _loading = null;
            _fly.RemoveFromClassList("tracks-fly-on");
            if (_world == null) return;
            Share[J.Str(_meta, "slug")] = _world.BlimpShare;
            _world.Close();
            _world = null;
            _fly.image = null;
        }

        /// <summary>
        /// A still of the flyover as it stands (a GPU copy of the world's picture), or null before it has faded in. The
        /// loading cover shows it here and on the round's screen, so the course stays in view, paused, while it loads.
        /// </summary>
        RenderTexture Freeze()
        {
            if (_world == null || _world.Texture == null || !_fly.ClassListContains("tracks-fly-on")) return null;
            var still = new RenderTexture(_world.Texture.width, _world.Texture.height, 0) { name = "tracks-loading-still" };
            Graphics.Blit(_world.Texture, still);
            return still;
        }

        void Start()
        {
            if (_loading != null) return;   // already teeing off
            if (!ConnectGate.Clear(_app)) return;   // no Puttr, no round: the card comes up before anything loads
            RenderTexture still = Freeze();
            if (still != null) { _fly.image = still; _world.Pause(true); }
            _loading = TracksLoading.Cover(still, J.Str(_meta, "name"));
            Root.Add(_loading);
            void Unfreeze()
            {
                _loading?.RemoveFromHierarchy(); _loading = null;
                if (_world != null) { _world.Pause(false); _fly.image = _world.Texture; }
                if (still != null) { still.Release(); UnityEngine.Object.Destroy(still); }
            }
            App.Run(TracksArt.Course(J.Str(_meta, "slug"), (course, why) =>
            {
                if (course == null)
                {
                    Unfreeze();
                    if ((why ?? "").Contains("members_only")) _app.Push(new ClubScreen(_app, "Every Tracks course"));
                    else if (why == "no_holes") _app.Toast("This course has no holes to play.");
                    else _app.Toast("Couldn't load the course. Check your connection.");
                    return;
                }
                if (string.IsNullOrEmpty(course.Name)) course.Name = J.Str(_meta, "name");
                if (string.IsNullOrEmpty(course.Slug)) course.Slug = J.Str(_meta, "slug");
                List<string> names = _picker != null ? _picker.Names : new List<string> { PlayerPicker.Short(_app.Profile) };
                List<string> ids = _picker != null ? _picker.UserIds : new List<string> { Auth.Uid };
                // The round's screen takes the frozen still (and releases it) for its own cover; this screen's goes when it is hidden.
                if (!ConnectGate.Clear(_app)) { Unfreeze(); return; }   // the Puttr dropped while the course loaded
                _app.Push(new TracksPlayScreen(_app, course, _set, _level, names, ids, _picker != null ? _picker.First : 0, still));
                _loading?.RemoveFromHierarchy(); _loading = null;
            }));
        }
    }

    public sealed class TracksPlayScreen : AppScreen
    {
        // The app's own pauses (Tracks.js SHOT_REST_MS, PUTT_REST_MS, TAP_IN_MS, HOLE_DONE_MS): the shot card is up for
        // RestShot and the hole-done card for HoleDone, and no putt is taken during either.
        const float RestShot = 3.0f, RestPutt = 2.0f, TapIn = 2.2f, HoleDone = 2.2f;
        /// <summary>Where the hole-done card sits. The app centres it over the hole; here the view behind it is the ball
        /// lying in the cup, so it sits over the scorecard where the shot bar was. True puts it mid-screen as the app.</summary>
        static readonly bool HoleCardCentred = false;
        readonly App _app;
        readonly TracksCourse _course;
        readonly List<TracksCourse.Hole> _holes;
        readonly string _set, _level;
        readonly TracksWorld _world;
        readonly Image _view;
        VisualElement _loadingCover;
        RenderTexture _loadingStill;
        readonly VisualElement _hud, _shotBar, _card, _camBar, _simRow, _entryBox, _clubs, _center, _doneSlot;
        readonly Label _caption, _captionSub, _holeNum, _holeName, _holeMeta, _entryTop, _entryBottom;
        // The entry to make it: the games' own ideal putt picture (PuttrArcade.AimView, on a ramp of its own: AimStage),
        // the rim lit where a make counts and turned with the tee, then the putt rolled again and where it went in
        // (Matt, 2026-11-26: it was a flat drawing of the mat).
        sealed class EntryPutt { public Vector2[] Path; public bool Made, Lip, Green; public double? EntryDeg; }
        readonly PuttrArcade.AimStage _aim;
        EntryPutt _entryLast, _entryShown;
        double _entryWindow = 180, _entryTee;
        int _entryFt, _entryTeeNo;
        float _aimAt;
        bool _camsOpen, _wide;
        float _w, _h;   // the screen in panel units, for the type that is sized to it
        /// <summary>The suggested club for this shot, so a club the Player changed to can say what the pick was.</summary>
        string _suggestedId;
        /// <summary>A shot in the air: what the shot card says of it once it lands.</summary>
        sealed class ShotPlayed { public TracksEngine.Club Club; public double Lateral, Depth, LineIn, RollIn, ToPin; public bool AimedGreen; }
        readonly RoundSaver _saver = new RoundSaver("TRACKS");
        /// <summary>Everyone playing and whose turn it is. Each Player's ball, lie and card are their own.</summary>
        readonly TracksRound _round;
        readonly List<string> _userIds;
        readonly VisualElement _turn;
        int _idx, _tee = 2;
        bool _busy, _done, _ended, _roundsAsked;
        /// <summary>A keyboard test putt was played: the round is a test round from then on. Its holes are made up,
        /// so its scorecard, score and board row are not written (only -app2SaveTestPutts sends them).</summary>
        bool _testRound;
        TracksEngine.Club _club;
        float _started;

        // The Player whose turn it is, and their ball, lie and card. The shot code reads and writes these as it did
        // when one Player played, so every shot lands on the Player who hit it.
        TracksPlayer P => _round.Now;
        int[] _strokes => P.Strokes;
        int[] _putts => P.Putts;
        bool?[] _fairways => P.Fairways;
        bool[] _gir => P.Gir;
        List<double> _drives => P.Drives;
        List<double> _approachFt => P.ApproachFt;
        Vector2 _ball { get => P.Ball; set => P.Ball = value; }
        string _lie { get => P.Lie; set => P.Lie = value; }
        bool _onGreen { get => P.OnGreen; set => P.OnGreen = value; }
        bool _holeSand { get => P.HoleSand; set => P.HoleSand = value; }
        int _shotNo { get => P.ShotNo; set => P.ShotNo = value; }
        int _penalties { get => P.Penalties; set => P.Penalties = value; }
        int _sandHoles { get => P.SandHoles; set => P.SandHoles = value; }
        int _sandSaves { get => P.SandSaves; set => P.SandSaves = value; }
        int _scrambles { get => P.Scrambles; set => P.Scrambles = value; }
        double _longestPuttFt { get => P.LongestPuttFt; set => P.LongestPuttFt = value; }

        TracksCourse.Hole Hole => _holes[_idx];
        /// <summary>-app2TracksAuto: test putts play themselves (for screenshots from a script).</summary>
        public static bool AutoPlay;
        /// <summary>-app2TracksGreen 8:made (or firm, lip, miss, short): the first hole starts on the green that far out, and the first test putt does that.</summary>
        static readonly string DevGreen = ArgAfter("-app2TracksGreen");
        string _devPutt;
        // -app2TracksHole 7: start on that hole of the set; -app2TracksCam plan|follow|green: that camera from the start;
        // -app2TracksNoHud: the picture alone (all for photographing the course from a script).
        static readonly string DevHole = ArgAfter("-app2TracksHole"), DevCam = ArgAfter("-app2TracksCam");
        static readonly bool DevNoHud = Array.IndexOf(DevArgs(), "-app2TracksNoHud") >= 0;
        // The launch arguments and PUTTR_ARGS: on a simulator the arguments only arrive in the environment (App.ReadArguments).
        static string[] DevArgs()
        {
            string extra = Environment.GetEnvironmentVariable("PUTTR_ARGS");
            string[] a = Environment.GetCommandLineArgs();
            return string.IsNullOrEmpty(extra) ? a : a.Concat(extra.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
        }
        static string ArgAfter(string flag) { string[] a = DevArgs(); int i = Array.IndexOf(a, flag); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        /// <param name="names">Who plays, in the order picked (the signed-in Player first).</param>
        /// <param name="userIds">Who each saves as (PlayerPicker.UserIds): the signed-in Player, a profile's id, or RoundSaver.Guest.</param>
        /// <param name="first">Who tees off first on every hole.</param>
        public TracksPlayScreen(App app, TracksCourse course, string set, string level, List<string> names = null, List<string> userIds = null, int first = 0, RenderTexture loadingStill = null)
        {
            _app = app; _course = course; _set = set; _level = level;
            _holes = set == "front" ? course.Holes.Take(9).ToList() : set == "back" ? course.Holes.Skip(9).Take(9).ToList() : course.Holes.ToList();
            if (_holes.Count == 0) _holes = course.Holes.ToList();
            if (int.TryParse(DevHole, out int devHole) && devHole > 1 && devHole <= _holes.Count) _holes = _holes.Skip(devHole - 1).Concat(_holes.Take(devHole - 1)).ToList();
            if (names == null || names.Count == 0) names = new List<string> { PlayerPicker.Short(app.Profile) };
            _userIds = userIds ?? new List<string> { Auth.Uid };
            var colours = Enumerable.Range(0, names.Count).Select(i => RulePanel.PlayerColour(i)).ToList();
            _round = new TracksRound(names, _userIds, colours, _holes.Count, first);
            _world = TracksWorld.Create();
            _view = new Image { image = _world.Texture, scaleMode = ScaleMode.ScaleAndCrop };
            _view.style.position = Position.Absolute; _view.style.left = _view.style.right = _view.style.top = _view.style.bottom = 0;
            _hud = new VisualElement(); _hud.style.position = Position.Absolute; _hud.style.left = _hud.style.right = _hud.style.top = _hud.style.bottom = 0;
            _hud.pickingMode = PickingMode.Ignore;
            if (DevNoHud) _hud.style.opacity = 0f;

            // Hole card, top left; the camera, top right (just the one in use, tap for the others).
            _holeNum = Ui.Text("", "tracks-num", Ui.Bold); _holeName = Ui.Text("", "tracks-name", Ui.Bold); _holeMeta = Ui.Text("", "tracks-meta", Ui.Medium);
            _hud.Add(Ui.Box("tracks-top", Ui.Text("\u2039", "tracks-back", Ui.Medium).OnTap(Leave), Ui.Box("tracks-holecard", Ui.Box("tracks-numbox", _holeNum), Ui.Box("tracks-metabox", _holeName, _holeMeta)), Ui.Box("grow")));
            // The entry to make it (puttr-app tracks/PuttReadout.js): the mat around the cup, the window to come in
            // through drawn on the cup and turned with the tee, and after the putt its path and where it went in.
            _aim = new PuttrArcade.AimStage();
            // The course's sun stays off the ramp: its picture is lit by the console's hood alone, as in the games.
            foreach (Light sun in _world.GetComponentsInChildren<Light>(true)) sun.cullingMask &= ~(1 << PuttrArcade.AimStage.Layer);
            var entryPic = new Image { image = _aim.View.Texture, scaleMode = ScaleMode.ScaleAndCrop };
            entryPic.AddToClassList("tracks-entry-view");
            entryPic.pickingMode = PickingMode.Ignore;
            _entryTop = Ui.Text("", "tracks-entry-k", Ui.Bold); _entryBottom = Ui.Text("", "tracks-entry-v", Ui.Bold);
            _entryBox = Ui.Box("tracks-entry hidden", Ui.Text("ENTRY", "tracks-k", Ui.Bold), Ui.Box("tracks-entry-mat", entryPic), _entryTop, _entryBottom);   // the words under the picture, not over the cup
            // The putt rolled again in the picture: drawn each frame while it runs.
            _aimAt = Time.realtimeSinceStartup;
            _hud.schedule.Execute(() => { float now = Time.realtimeSinceStartup; _aim.View.Tick(Mathf.Min(0.1f, now - _aimAt)); _aimAt = now; }).Every(16);
            _entryBox.pickingMode = PickingMode.Ignore;
            _hud.Add(_entryBox);
            // The clubs, always there on a screen on its side. Added before the camera so the camera's list opens over it.
            _clubs = Ui.Box("tracks-clubs hidden");
            _hud.Add(_clubs);
            _camBar = Ui.Box("tracks-camdock");
            _hud.Add(_camBar);
            // The clubs sit under the camera picker, however tall it is at this size (Matt, 2026-11-23: they overlapped on an
            // iPad held sideways). A short phone keeps its own place: there is no height to spare.
            _camBar.RegisterCallback<GeometryChangedEvent>(e =>
            {
                for (VisualElement up = _hud; up != null; up = up.parent) if (up.ClassListContains("tracks-short")) { _clubs.style.top = StyleKeyword.Null; return; }
                _clubs.style.top = e.newRect.yMax + 10f;
            });
            _caption = Ui.Text("", "tracks-cap-t", Ui.Bold); _captionSub = Ui.Text("", "tracks-cap-s", Ui.Regular);
            VisualElement lower = Ui.Box("tracks-lower hidden", _caption, _captionSub);
            lower.name = "lower";
            _simRow = Ui.Box("tracks-simrow");
            // Test putts come from a keyboard (the simulator's, a Mac's), never an on-screen button: Space a putt a
            // little off line and pace, M made, F firm, L lip out, N miss, S short. Testers and made-up data only.
            _hud.schedule.Execute(KeyPutts).Every(30);
            _shotBar = Ui.Box("tracks-shotbar");
            // The scorecard across the bottom: every hole's par and strokes, the total and where it stands to par.
            _card = Ui.Box("tracks-sc");
            _doneSlot = Ui.Box("tracks-doneslot");
            _doneSlot.pickingMode = PickingMode.Ignore;
            // With more than one Player: whose shot it is, in their colour, over the shot bar.
            _turn = Ui.Box("tracks-turn hidden");
            _turn.pickingMode = PickingMode.Ignore;
            _hud.Add(Ui.Box("tracks-bottom", lower, _simRow, _doneSlot, _turn, _shotBar, _card));
            // The shot card, mid-screen. It takes no taps, and it is gone before the next putt is taken.
            _center = Ui.Box("tracks-center");
            _center.pickingMode = PickingMode.Ignore;
            _hud.Add(_center);

            // Until the first hole is built: the setup's flyover, paused where it was, under the Puttr spinner.
            _loadingStill = loadingStill;
            _loadingCover = TracksLoading.Cover(loadingStill, course.Name);
            Root = Ui.Box("tracks-root", _view, _hud, _loadingCover);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Stop());
            // On its side (a tablet, a phone turned) the scorecard shows the whole round and the entry gets bigger.
            Root.RegisterCallback<GeometryChangedEvent>(e =>
            {
                // The world draws at the view's own shape (up to 1600 on the long side), not a square cropped to fit.
                Rect r = e.newRect;
                if (!_ended && !float.IsNaN(r.width) && r.width >= 10 && r.height >= 10)
                {
                    float k = Mathf.Min(2f, 1600f / Mathf.Max(r.width, r.height));
                    _world.Resize(Mathf.RoundToInt(r.width * k), Mathf.RoundToInt(r.height * k));
                    _view.image = _world.Texture;
                }
                if (float.IsNaN(r.width) || float.IsNaN(r.height)) return;
                bool wide = r.width > r.height, resized = Mathf.Abs(r.width - _w) > 1f || Mathf.Abs(r.height - _h) > 1f;
                _w = r.width; _h = r.height;
                // A phone on its side is wide but short: the club list and the entry tighten up to fit.
                Root.EnableInClassList("tracks-short", wide && r.height < 500f);
                if (wide != _wide)
                {
                    _wide = wide;
                    Root.EnableInClassList("tracks-wide", wide);
                    DrawCard();
                }
                // The shot interface is laid out and sized for the screen's shape, so it is drawn again when that changes
                // (hidden still, if a shot is in the air).
                if (resized)
                {
                    bool hid = _shotBar.ClassListContains("hidden");
                    DrawShot();
                    if (hid) { _shotBar.AddToClassList("hidden"); _simRow.AddToClassList("hidden"); }
                }
            });
            // -app2TracksCams: open the camera list (for screenshots from a script).
            _camsOpen = Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TracksCams") >= 0;
            DrawCams();
            Green.Console.Putt += OnPutt;
            _started = Time.realtimeSinceStartup;
            // The game opens at the first tee, as in the app (Tracks.js startGame: POST /game/new with the round's
            // setup as puttData), so every mat putt and the round rows are tied to it. Without this the saver was
            // never begun, and it dropped every putt and never opened or closed the game.
            int n = _round.Players.Count;
            _saver.Begin(n, J.Write(new Dictionary<string, object>
            {
                { "courseId", _course.Slug }, { "holeSet", _set }, { "level", _level }, { "levels", Enumerable.Repeat((object)_level, n).ToList() }, { "players", n },
            }), true);
            _app.EnterPractice();
            TracksVoice.Preload(null);   // the calls and the booth's lines, fetched ahead so they play at once
            App.Run(StartHole(0));
        }

        // ── Holes ──

        IEnumerator StartHole(int i)
        {
            _idx = i; _busy = true;
            _round.StartHole(i, Hole.Tee);   // every ball on the tee, the first Player to play
            _holeNum.text = Hole.N.ToString(); _holeName.text = Hole.Name.Length > 0 ? Hole.Name : "Hole " + Hole.N; _holeMeta.text = "Par " + Hole.Par + " · " + Hole.Yds + " yds";
            DrawCard();
            _center.Clear(); _doneSlot.Clear();   // the last hole's cards
            _entryLast = null; DrawEntry();
            TracksVoice.NewHole();   // what the booth still owed about the last hole is late now (audio.js dropPendingLines)
            if (i == 0) Booth("round_start");   // Tracks.js startGame
            Say("Hole " + Hole.N, Hole.Name + (Hole.Name.Length > 0 ? " · " : "") + "Par " + Hole.Par + ", " + Hole.Yds + " yards");
            yield return _world.BuildHole(_course, Hole);
            if (i == 0 && DevGreen != null && _strokes[0] == 0)
            {
                string[] dev = DevGreen.Split(':');
                double devFt = double.TryParse(dev[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double f) ? f : 8;
                _ball = Hole.Pin + (Hole.Tee - Hole.Pin).normalized * (float)(devFt / 3); _lie = "green"; _onGreen = true;
                _devPutt = dev.Length > 1 ? dev[1] : "made";
            }
            PlaceBalls();
            _world.SetCam(_world.Mode == TracksWorld.Cam.Green ? TracksWorld.Cam.Broadcast : _world.Mode, _ball, true);
            if (i == 0) LiftLoading();
            if (i == 0 && DevCam != null && Enum.TryParse(DevCam, true, out TracksWorld.Cam devCam)) _world.SetCam(devCam, _ball, true);
            DrawCams();
            _world.Bed("course_loop", 0.28f);
            if (!_roundsAsked && i == 0) { _roundsAsked = true; App.Run(StartRound()); }
            _busy = false;
            SetUpShot();
        }

        double DistYds => Vector2.Distance(_ball, Hole.Pin);
        double LeaveFt => DistYds * 3;

        /// <summary>The ball of the Player whose turn it is, and everyone else's who has teed off and not holed out.</summary>
        void PlaceBalls()
        {
            _world.PlaceBall(_ball);
            if (!_round.Multi) return;
            var others = new List<(Vector2, Color)>();
            foreach (TracksPlayer q in _round.Players)
                if (q != P && !q.HoledOut && _round.Teed(q)) others.Add((q.Ball, q.Colour));
            _world.ShowOthers(others);
        }

        /// <summary>The next Player's turn (Tracks.js nextTurn after a shot's rest): their ball, the camera on it, the shot set up.</summary>
        void NextTurn()
        {
            int was = _round.Cur;
            _round.NextTurn(Hole.Pin);
            PlaceBalls();
            _world.SetCam(_onGreen ? TracksWorld.Cam.Green : _world.Mode == TracksWorld.Cam.Green ? TracksWorld.Cam.Broadcast : _world.Mode, _ball);
            DrawCams();
            // A new Player on the tee: the lower third says whose shot it is and what is left.
            if (_round.Multi && _round.Cur != was) Say(TracksEngine.TurnWords(P.Name), TracksEngine.DistanceLeft(_onGreen, DistYds));
            SetUpShot();
        }

        void SetUpShot()
        {
            _center.Clear();   // the shot card rests with the ball and leaves when the next shot is set up
            _club = _onGreen ? TracksEngine.GreenPutt(LeaveFt) : TracksEngine.Suggest(DistYds, _lie);
            _suggestedId = _club.Id;
            _tee = TeeFor();
            _entryLast = null;
            DrawShot();
            DrawEntry();
            DrawCard();   // the scorecard marks whose turn it is
            Announce();
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
            // Saved as the app saves it: made is the camera's own count (savePutt's c > 0), not the game's kinder
            // "ended in the cup", with the rollout the app measured. Nothing more is saved once the round is a test round.
            // Each Player's putts go in under their own user id (a guest's under 0), as the app's savePutt(p.userId || 0).
            // A test putt (no Puttr, the simulator, the keyboard or a test button) is played but makes the round a test
            // round: the saver leaves the putt out, and the scorecard, score and board row of a round with a made-up
            // shot in it are not written either (only -app2SaveTestPutts sends them).
            if (RoundSaver.TestPutt()) _testRound = true;
            _saver.Putt(_club.Ft, _tee, v.SavedMade, putt, _testRound, null, RoundSaver.SeatUser(_userIds, P.Seat, 0, Auth.Uid), v.Rollout);
            // The path in inches with the cup at the origin, +x right and +y past it (the camera's y runs the other way).
            var inches = new List<Vector2>();
            foreach (int[] q in bp) inches.Add(new Vector2((float)((q[0] - hb[0]) / TracksEngine.MatPxPerIn), (float)((hb[1] - q[1]) / TracksEngine.MatPxPerIn)));
            Apply(TracksEngine.ReadLineIn(bp, hb), v.Rollout, made, made && v.HasEntry ? v.Entry : (double?)null, inches);
        }

        // The keys down at the last look: a key counts once, when it goes down (the look runs every 30 ms, not every frame).
        readonly HashSet<UnityEngine.InputSystem.Key> _keysDown = new HashSet<UnityEngine.InputSystem.Key>();

        void KeyPutts()
        {
            UnityEngine.InputSystem.Keyboard kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || _club == null || _club.Tap) return;
            if (!(Fixtures.On || J.Flag(_app.Profile, "isTester") || Application.isEditor)) return;
            string dev = null; bool any = false;
            foreach (var (key, kind) in new[] { (UnityEngine.InputSystem.Key.Space, (string)null), (UnityEngine.InputSystem.Key.M, "made"),
                (UnityEngine.InputSystem.Key.F, "firm"), (UnityEngine.InputSystem.Key.L, "lip"), (UnityEngine.InputSystem.Key.N, "miss"), (UnityEngine.InputSystem.Key.S, "short") })
            {
                bool down = kb[key].isPressed;
                if (down && _keysDown.Add(key)) { any = true; dev ??= kind; }
                else if (!down) _keysDown.Remove(key);
            }
            if (!any) return;
            if (dev != null) _devPutt = dev;
            Simulate();
        }

        /// <summary>Testers and sample data: a putt without the mat, a little off line and pace.</summary>
        void Simulate()
        {
            if (_busy || _done || _club == null || _club.Tap) return;
            if (!Green.SaveTestPutts) _testRound = true;
            string dev = _devPutt; _devPutt = null;
            switch (dev)
            {
                case "made": Apply(0.2, 8, true, 6, null); return;
                case "firm": Apply(-0.2, 34, true, -4, null); return;
                case "lip": Apply(1.6, 16, true, 62, null); return;
                case "miss": Apply(3.4, 14, false, null, null); return;
                case "short": Apply(0.4, -1.5, false, null, null); return;
            }
            double line = (UnityEngine.Random.value - 0.5) * 3, roll = (UnityEngine.Random.value - 0.4) * 14;
            bool made = _onGreen && _club.Ft <= 6 && UnityEngine.Random.value < 0.5;
            Apply(line, roll, made, made ? (UnityEngine.Random.value - 0.5) * 30 : (double?)null, null);
        }

        void Apply(double lineIn, double? rollout, bool matMade, double? entryDeg, List<Vector2> path)
        {
            _busy = true;
            // The call for this shot is moot once the ball is away (Tracks.js dropQueuedCall).
            TracksVoice.ShotPlayed();
            // A putt with no path (the test putt) rolls in straight to where it finished, as the app draws it.
            Vector2 end = matMade ? Vector2.zero : new Vector2((float)lineIn, (float)(rollout ?? TracksEngine.PaceTargetIn));
            if (path == null || path.Count < 2) path = new List<Vector2> { new Vector2(end.x * 0.25f, -19f), end };
            _entryLast = new EntryPutt { Path = path.ToArray(), Made = matMade, EntryDeg = entryDeg, Green = _onGreen };
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
            _shotNo++;
            Vector2 from = _ball, land = _ball + dir * (float)total + perp * (float)lateral;
            // In the air the lower third names the club and reads the putt; the shot card takes over when it lands.
            Say(club.Name, TracksEngine.DescribePutt(lineIn, rollout));
            _world.Sfx(club.Big || club.Id == "3W" ? "golf_drive" : "golf_iron", 1f, club.Pitch ? 1.15f : 1f);
            // What the card shows (Tracks.js r.lastShot): the yards off line and pace, and the mat's inches behind them.
            var played = new ShotPlayed { Club = club, Lateral = lateral, Depth = err.depth, LineIn = lineIn, RollIn = rollout ?? TracksEngine.PaceTargetIn, ToPin = toPin, AimedGreen = atPin || Hole.Par == 3 };
            _world.Fly(from, land, false, () => Land(from, land, played));
        }

        void Land(Vector2 from, Vector2 land, ShotPlayed played)
        {
            string lie = _course.LieAt(land);
            bool teeShot = _shotNo == 1 && Hole.Par > 3;
            double yds = Vector2.Distance(from, land);   // the card's big number: how far it really went
            // The app has a chip from beside the green as its own state; here that is a pitch from inside 30 yds.
            bool chip = played.Club.Pitch && played.ToPin <= 30;
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
                Booth(TracksEngine.CourseCall("water", teeShot, played.AimedGreen, chip, 0));
                ShowShotCard(played, yds, "water", "Water, +" + TracksEngine.WaterPenalty + ". Drop where it crossed.", true);
                After(null, null, RestShot);
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
            Booth(TracksEngine.CourseCall(lie, teeShot, played.AimedGreen, chip, LeaveFt));
            // The card has the app's words; its last line is the one thing the app showed elsewhere (the distance chip): what is left.
            ShowShotCard(played, yds, lie, lie == "green" ? Math.Round(LeaveFt) + " ft to the hole" : Math.Round(DistYds) + " yds to go", false);
            After(null, null, RestShot);
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
            if (_entryLast != null) { _entryLast.Made = made; _entryLast.Lip = lip; DrawEntry(); }
            Vector2 toHole = (Hole.Pin - _ball).normalized, side = new Vector2(toHole.y, -toHole.x);
            Vector2 end = made ? Hole.Pin : Hole.Pin + toHole * (float)(yin / 36) + side * (float)(xin / 36);
            Say(club.Name, TracksEngine.DescribePutt(lineIn, rollout));
            // 11 ft and in plays up close at true scale (the ball rolls the putt's line and pace and drops, rattles or lips
            // out as the engine said); a lag plays on the course.
            bool close = !club.Lag;
            Action finish = () =>
            {
                if (made)
                {
                    _longestPuttFt = Math.Max(_longestPuttFt, ft);
                    // A lag that drops is shown going into the cup too, and the world plays the cup's note as it lands.
                    if (!close) _world.SinkBall(true);
                    Booth(TracksEngine.PuttCall(true, ft, false, club.Lag, 0, yin));   // only a long one: a short make lets the score call speak
                    HoleOut(false, true);
                    return;
                }
                _ball = end; _lie = "green";
                double left = LeaveFt;
                Booth(TracksEngine.PuttCall(false, ft, lip, club.Lag, left, yin));
                After(lip ? "Lipped out" : club.Lag ? "Lag, scaled from " + club.Ft + " ft" : matMade ? "Holed on the mat" : "Missed", Math.Round(left) + " ft left.", RestPutt);
            };
            if (close) _world.PuttUpClose(new TracksWorld.GreenPutt { Ft = ft, Xin = xin, Yin = yin, RolloutIn = rollout, EntryDeg = matMade && made ? entryDeg ?? teeDeg : (double?)null, Made = made, Lip = lip }, finish);
            else _world.Fly(_ball, end, true, finish);
        }

        void After(string title, string sub, float rest)
        {
            // A shot from the course rests under its card (no title); a putt on the green keeps the lower third.
            if (title != null) Say(title, sub); else HideLower();
            if (_strokes[_idx] >= TracksEngine.MaxHoleScore) { _strokes[_idx] = TracksEngine.MaxHoleScore; HoleOut(true); return; }
            DrawCard();
            _hud.schedule.Execute(() =>
            {
                _busy = false;
                NextTurn();   // one Player: it is theirs again
            }).StartingIn((long)(rest * 1000));
        }

        void TapInNow()
        {
            _busy = true;
            _strokes[_idx]++; _putts[_idx]++;
            Booth("gimme");
            _world.PuttUpClose(new TracksWorld.GreenPutt { Ft = Math.Max(0.5, LeaveFt), Made = true, RolloutIn = 5, EntryDeg = 0 }, () => HoleOut(false, true));
        }

        void HoleOut(bool pickedUp = false, bool cupHeard = false)
        {
            TracksPlayer who = P;
            if (!pickedUp && _strokes[_idx] <= Hole.Par) { if (!_gir[_idx]) _scrambles++; if (_holeSand) _sandSaves++; }
            who.Holed[_idx] = true;
            who.HoledOut = true;
            who.Picked = pickedUp;
            string label = pickedUp ? "Picked up" : TracksEngine.ScoreName(_strokes[_idx], Hole.Par);
            if (!pickedUp) { if (!cupHeard) _world.Sfx("golf_cup"); _world.Gallery(_strokes[_idx], Hole.Par); }
            // The booth reacts, it never names the score: the card on screen does that.
            Booth(TracksEngine.ScoreCall(pickedUp, _strokes[_idx], Hole.Par));
            DrawCard();
            _busy = true; _club = null; DrawShot(); DrawEntry();
            if (!_round.AllHoledOut)
            {
                // Someone is still playing the hole (Tracks.js holeOut: "Ava: Birdie. 3 on the par 4."), then the next
                // shot after the putt's rest, the farthest from the hole first.
                Say(who.Name + ": " + label, who.Strokes[_idx] + " on the par " + Hole.Par + ".");
                _hud.schedule.Execute(() => { _busy = false; NextTurn(); }).StartingIn((long)(RestPutt * 1000));
                return;
            }
            ShowHoleDone(label);
            Flush(false);
            _hud.schedule.Execute(() => { if (_idx + 1 < _holes.Count) App.Run(StartHole(_idx + 1)); else EndRound(); }).StartingIn((long)(HoleDone * 1000));
        }

        int Par => _holes.Sum(h => h.Par);
        List<int> Pars => _holes.Select(h => h.Par).ToList();

        // ── Saving ──

        IEnumerator StartRound()
        {
            // The round rows are tied to the game (the app's startRoundRows(userGameId)), so they wait for it: it opens
            // at the first tee with a Puttr connected, else on the first real putt. A round of test putts has no game
            // and no rows. (This used to give up after 25 seconds and write the row with no userGameId.)
            while (_saver.RoundId == null && !_ended) yield return new WaitForSecondsRealtime(0.5f);
            string userGameId = _saver.RoundId ?? _saver.LastRoundId;   // LastRoundId: opened and finished in the same half second
            if (userGameId == null || _testRound || Fixtures.On) yield break;
            // A row per Player, as the app's startRoundRows: a profile's under its user id, a guest's under no one
            // (userId null: the host keeps the round, it is on no account and no board).
            var players = new List<object>();
            foreach (TracksPlayer q in _round.Players)
            {
                string id = RoundSaver.SeatUser(_userIds, q.Seat, 0, Auth.Uid);
                players.Add(new Dictionary<string, object>
                {
                    { "i", q.Seat }, { "userId", string.IsNullOrEmpty(id) || id == RoundSaver.Guest ? null : id },
                    { "name", q.Seat == 0 ? J.Str(_app.Profile, "displayName", q.Name) : q.Name }, { "level", _level },
                });
            }
            var body = new Dictionary<string, object>
            {
                { "userGameId", userGameId }, { "trackSlug", _course.Slug }, { "trackVersion", Math.Max(1, _course.Version) }, { "holeSet", _set }, { "level", _level }, { "look", "broadcast" }, { "par", Par },
                { "players", players },
            };
            bool any = false;
            yield return Api.Post("/tracks/round", body, d =>
            {
                // [{ i, id }] (tracks.js startRound): each row back to the Player in that seat. Under -app2HoldPlay the
                // call is answered on the device with one local { id }, which is no row: nothing is updated then.
                if (!(d is List<object>)) return;
                List<object> rows = J.List(d);
                foreach (object r in rows)
                {
                    if (J.Get(r, "i") == null) continue;
                    int seat = J.Int(r, "i");
                    string id = J.Str(r, "id");
                    if (seat < 0 || seat >= _round.Players.Count || id.Length == 0 || Api.IsLocal(id)) continue;
                    _round.Players[seat].RoundId = id;
                    any = true;
                }
            }, null);
            if (!any) yield break;
            // Holes finished (or the whole round) while the rows were being made: send where the round stands now.
            if (_done) Flush(true);
            else if (_round.Players.Any(q => q.Holed.Any(h => h))) Flush(false);
        }

        Dictionary<string, object> Payload(TracksPlayer q, bool final)
        {
            int played = final ? _holes.Count : Math.Min(_holes.Count, _idx + 1);
            var holes = new List<object>();
            for (int i = 0; i < played; i++) holes.Add(new Dictionary<string, object> { { "n", _holes[i].N }, { "par", _holes[i].Par }, { "s", q.Strokes[i] }, { "p", q.Putts[i] }, { "fw", q.Fairways[i] }, { "gir", q.Gir[i] } });
            var hs = holes.Cast<Dictionary<string, object>>().ToList();
            var body = new Dictionary<string, object>
            {
                { "strokes", hs.Sum(h => (int)h["s"]) }, { "holesPlayed", played }, { "holes", holes }, { "putts", hs.Sum(h => (int)h["p"]) },
                { "onePutts", hs.Count(h => (int)h["p"] == 1) }, { "threePutts", hs.Count(h => (int)h["p"] >= 3) },
                { "fairwaysHit", hs.Count(h => (bool?)h["fw"] == true) }, { "fairwaysTried", hs.Count(h => h["fw"] != null) }, { "gir", hs.Count(h => (bool)h["gir"]) },
                { "scrambles", q.Scrambles }, { "sandSaves", q.SandSaves }, { "sandTries", q.SandHoles }, { "penalties", q.Penalties },
                { "longestDriveYds", q.Drives.Count > 0 ? (object)Math.Round(q.Drives.Max()) : null }, { "avgDriveYds", q.Drives.Count > 0 ? (object)Math.Round(q.Drives.Average()) : null },
                { "proximityFt", q.ApproachFt.Count > 0 ? (object)Math.Round(q.ApproachFt.Average()) : null }, { "longestPuttFt", q.LongestPuttFt > 0 ? (object)Math.Round(q.LongestPuttFt) : null },
            };
            if (final) { body["completed"] = 1; body["durationSec"] = Mathf.RoundToInt(Time.realtimeSinceStartup - _started); }
            return body;
        }

        /// <summary>Every Player's row, where the round stands (the app's flushRound).</summary>
        void Flush(bool final)
        {
            if (_testRound) return;
            foreach (TracksPlayer q in _round.Players)
                if (q.RoundId != null) App.Run(Api.Do("PUT", "/tracks/round/" + q.RoundId, Payload(q, final), null, null));
        }

        /// <summary>The round is being written for real: it has a game, play records are not held (-app2HoldPlay), and no test shot was played.</summary>
        bool Saving => !_testRound && !Api.Holds("/tracks/round") && (_saver.RoundId != null || _saver.LastRoundId != null);

        void EndRound()
        {
            _done = true;
            // The signed-in Player's score is the round's own (seat 0, the app's toPars[0]): the booth's call and the board.
            int par = Par;
            int toPar = _round.Players[0].Total - par;
            Booth(TracksEngine.RoundCall(toPar));   // heard over the result page, as the app's round summary
            Flush(true);
            string userGameId = _saver.RoundId;
            // A round with made-up shots in it has no score to keep: its game is marked abandoned, as a round left part way.
            // With more Players the game closes as the app's multiplayerGameOver: everyone's strokes to par by seat, the
            // host's round scored and every other profile given its own; a guest is sent as 0 and gets nothing.
            if (_testRound) _saver.Abandon();
            else if (_round.Multi) _saver.End(_round.Players.Select(q => (double)(q.Total - par)).ToList(), _userIds, 0);
            else _saver.End(toPar);
            string gameId = _saver.GameId;
            userGameId = userGameId ?? _saver.LastRoundId;
            // The board row goes with a game that real putts opened (the app's postLeaderboard after gameOver).
            if (gameId != null && userGameId != null && !_testRound && !_saver.HadTestPutt)
                App.Run(Api.Post("/club/leaderboard/add", new Dictionary<string, object> { { "clubId", "PUTTR" }, { "gameId", gameId }, { "userGameId", userGameId }, { "userId", Auth.Uid },
                    { "userName", Kid.PublicPlayer(_app.Profile) },
                    { "score", toPar }, { "distance", TracksEngine.LeaderboardCode(_set, _level) } }, null, null));
            // The result screen says nothing of saving, as the app's; only a round held under -app2HoldPlay says so here.
            bool held = !_testRound && _saver.Real > 0 && Api.Holds("/tracks/round");
            Stop();
            _app.Pop();
            _app.Push(new TracksResultScreen(_app, _course, _holes, _round, _level));
            if (held) _app.Toast(Api.NotSaved + ": this round.");
        }

        void Leave()
        {
            if (_done) { _app.Pop(); return; }
            // What leaving costs, from what is really being written: the finished holes are kept only when the round is.
            string kept = Saving ? "Holes you finished are saved. " : Api.Holds("/tracks/round") ? Api.NotSaved + ": this round. " : _testRound || !Green.RealPutts ? "Test putts: this round is not saved. " : "";
            Dialog.Ask(Root, "Leave the round?", kept + "You can't pick this round up again.", "Leave", () => { Flush(false); _saver.Abandon(); Stop(); _app.Pop(); });
        }

        void Stop()
        {
            if (_ended) return;
            _ended = true;
            // Left part way: the booth stops. A finished round's last line plays on over the result.
            if (!_done) TracksVoice.Stop();
            Green.Console.Putt -= OnPutt;
            DropLoadingStill();
            _world.Close();
            _aim.Close();
            _app.LeavePractice();
        }

        /// <summary>The first hole is built and framed: the loading cover fades away, a frame later so the course is drawn under it.</summary>
        void LiftLoading()
        {
            VisualElement cover = _loadingCover;
            if (cover == null) return;
            _loadingCover = null;
            cover.schedule.Execute(() => cover.AddToClassList("tracks-loading-off")).StartingIn(60);
            cover.schedule.Execute(() => { cover.RemoveFromHierarchy(); DropLoadingStill(); }).StartingIn(560);
            Perf.Wake(0.8f);
        }

        void DropLoadingStill()
        {
            if (_loadingStill == null) return;
            _loadingStill.Release(); UnityEngine.Object.Destroy(_loadingStill);
            _loadingStill = null;
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

        void HideLower() => _hud.Q("lower").AddToClassList("hidden");

        /// <summary>One line from the booth (TracksVoice.Event, the app's commentate), when Commentary is on.</summary>
        /// <summary>A moment in the round: the gallery reacts to it (always: it is the course's sound, not the booth), and the
        /// booth calls it when the commentary is on, its line over the crowd as on a broadcast.</summary>
        void Booth(string category)
        {
            if (category == null) return;
            _world?.Crowd(category);
            if (TracksVoice.Commentary) TracksVoice.Event(category);
        }

        static readonly (TracksWorld.Cam mode, string label)[] Cams = { (TracksWorld.Cam.Broadcast, "Broadcast"), (TracksWorld.Cam.Follow, "Ball follow"), (TracksWorld.Cam.Plan, "Plan"), (TracksWorld.Cam.Green, "Green") };

        // The camera: the one in use as a pill; a tap opens all four, a pick closes them again.
        void DrawCams()
        {
            _camBar.Clear();
            DrawClubs();   // the open camera list covers the top of the club list, so the clubs step aside for it
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
            bool all = _wide || _holes.Count <= 9, multi = _round.Multi;
            _card.EnableInClassList("tracks-sc-multi", multi);
            int from = all ? 0 : _idx / 9 * 9, to = all ? _holes.Count : Math.Min(_holes.Count, from + 9);
            List<TracksPlayer> ps = _round.Players;
            VisualElement cols = Ui.Box("tracks-sc-row");
            // The label column: HOLE, PAR, then a row per Player (their name in their colour), or SCORE for one.
            VisualElement lab = Col("tracks-sc-col tracks-sc-lab", "HOLE", "PAR", multi ? ps.Select(q => TracksEngine.CardName(q.Name)).ToList() : new List<string> { "SCORE" }, null);
            if (multi)
            {
                var names = lab.Query<Label>(className: "tracks-sc-s").ToList();
                for (int k = 0; k < names.Count && k < ps.Count; k++)
                {
                    names[k].style.color = ps[k].Colour;
                    // The Player whose turn it is.
                    names[k].EnableInClassList("tracks-sc-turn", !_done && _club != null && k == _round.Cur);
                }
            }
            cols.Add(lab);
            for (int i = from; i < to; i++)
            {
                var scores = new List<string>(); var tones = new List<string>();
                foreach (TracksPlayer q in ps)
                {
                    string sc = q.Strokes[i] > 0 ? q.Strokes[i].ToString() : "";
                    string tone = null;
                    if (sc.Length > 0 && q.Holed[i]) { int d = q.Strokes[i] - _holes[i].Par; tone = d < 0 ? "tracks-under" : d > 0 ? "tracks-over" : null; }
                    scores.Add(sc); tones.Add(tone);
                }
                cols.Add(Col("tracks-sc-col" + (i == _idx && !_done ? " tracks-sc-now" : ""), _holes[i].N.ToString(), _holes[i].Par.ToString(), scores, tones));
                // On its side an 18: the front nine's total after hole 9.
                if (all && _holes.Count > 9 && i == 8) cols.Add(Sum("OUT", 0, 9));
            }
            if (all && _holes.Count > 9) cols.Add(Sum("IN", 9, _holes.Count));
            cols.Add(Sum("TOT", 0, _holes.Count));
            // Where each Player stands to par over the holes they have finished.
            List<int> pars = Pars;
            VisualElement box = Ui.Box("tracks-sc-parbox", Ui.Text("TO PAR", "tracks-k", Ui.Bold));
            foreach (TracksPlayer q in ps)
            {
                int toPar = q.ToParSoFar(pars);
                Label tp = Ui.Text(TracksEngine.ToPar(toPar), multi ? "tracks-sc-topar tracks-sc-topar-small" : "tracks-sc-topar", Ui.Bold);
                if (toPar < 0) tp.AddToClassList("tracks-under");
                else if (multi) tp.style.color = q.Colour;
                box.Add(tp);
            }
            cols.Add(box);
            _card.Add(cols);
        }

        VisualElement Sum(string label, int from, int to)
        {
            int par = 0;
            for (int i = from; i < Math.Min(to, _holes.Count); i++) par += _holes[i].Par;
            var scores = new List<string>();
            foreach (TracksPlayer q in _round.Players)
            {
                int strokes = 0; bool any = false;
                for (int i = from; i < Math.Min(to, _holes.Count); i++) { strokes += q.Strokes[i]; any |= q.Strokes[i] > 0; }
                scores.Add(any ? strokes.ToString() : "");
            }
            return Col("tracks-sc-col tracks-sc-sum", label, par.ToString(), scores, null);
        }

        /// <summary>One column of the scorecard: the hole, its par, and a score for each Player (tones colour under and over par).</summary>
        static VisualElement Col(string cls, string hole, string par, IList<string> scores, IList<string> tones)
        {
            VisualElement col = Ui.Box(cls, Ui.Text(hole, "tracks-sc-n", Ui.Bold), Ui.Text(par, "tracks-sc-p", Ui.Medium));
            for (int k = 0; k < scores.Count; k++)
            {
                Label s = Ui.Text(scores[k], "tracks-sc-s", Ui.Bold);
                if (tones != null && k < tones.Count && tones[k] != null) s.AddToClassList(tones[k]);
                col.Add(s);
            }
            return col;
        }

        // The entry panel: the window to make it from this tee, and the last putt's path and entry after it.
        void DrawEntry()
        {
            bool show = _club != null && !_club.Tap || _entryLast != null;
            _entryBox.EnableInClassList("hidden", !show);
            if (!show) return;
            if (_club != null)
            {
                _entryWindow = _club.Lag ? 2 * TracksEngine.LagTolerance(_level, LeaveFt) : TracksEngine.LevelOf(_level).Entry;
                _entryTee = AppPutt.TeeEntry(_club.Ft, _tee);
                _entryFt = (int)Math.Round((double)_club.Ft); _entryTeeNo = _tee;
            }
            _entryTop.text = _entryWindow >= 180 ? "FRONT DOOR" : "WINDOW " + Math.Round(_entryWindow) + "°";
            EntryPutt last = _entryLast;
            string verdict = "";
            if (last != null && last.Green && (last.Made || last.Lip))
            {
                verdict = last.Made ? "IN" : "LIP OUT";
                if (last.EntryDeg != null) { double off = last.EntryDeg.Value - _entryTee; verdict += " · " + Math.Abs(Math.Round(off)) + "° " + (off < 0 ? "L" : "R"); }
            }
            else if (last == null && _club != null) verdict = _tee == 1 ? "BREAKS L TO R" : _tee == 3 ? "BREAKS R TO L" : "STRAIGHT";
            _entryBottom.text = verdict;
            _entryBottom.EnableInClassList("tracks-entry-miss", last != null && last.Lip);
            // The picture. A new putt (or none): the window laid out afresh, then the putt rolled again. The same putt
            // with its verdict in: only the dot where it went in, so the roll is not started over.
            double half = Math.Min(90, _entryWindow / 2);
            bool entered = last != null && last.Green && last.EntryDeg != null && (last.Made || last.Lip);
            bool ok = entered && Math.Abs(last.EntryDeg.Value) <= 90 && Math.Abs(last.EntryDeg.Value - _entryTee) <= half;
            float? dot = entered ? (float?)last.EntryDeg.Value : null;
            if (last != null && last == _entryShown) { _aim.View.Entry(dot, ok); return; }
            _entryShown = last;
            _aim.View.ShowWindow(Mathf.Max(3, _entryFt), _entryTeeNo < 1 ? 2 : _entryTeeNo, (float)half, dot, ok, (float)_entryTee);
            if (last != null && last.Path != null && last.Path.Length > 1)
            {
                // AimView's path runs toward the Player as y grows; this one runs past the hole.
                var path = new Vector2[last.Path.Length];
                for (int i = 0; i < path.Length; i++) path[i] = new Vector2(last.Path[i].x, -last.Path[i].y);
                _aim.View.Replay(path, last.Made);
            }
        }

        // ── The shot interface (Tracks.js renderCaddie, renderDistanceBig, renderClubList), on solid plates over the course ──

        /// <summary>The call before a shot, as the app's announceShot: "Seven feet, center tee" (TracksVoice says it when the
        /// Commentary is on: it is where to putt from).</summary>
        void Announce()
        {
            if (_club == null || _club.Tap || _done) return;
            TracksVoice.Call(_club.Ft, _tee);
        }

        /// <summary>The clubs to pick from: a Pitch first when the pin is inside 80 yds, then the bag, longest first.</summary>
        List<TracksEngine.Club> ClubRows()
        {
            var rows = new List<TracksEngine.Club>();
            if (DistYds < TracksEngine.PitchMaxYds) rows.Add(TracksEngine.Suggest(DistYds, _lie));
            rows.AddRange(TracksEngine.Clubs);
            return rows;
        }

        /// <summary>The Player takes a club of their own (Tracks.js chooseClub): its putt from this lie, the tee again, and the call again.</summary>
        void ChooseClub(TracksEngine.Club k)
        {
            if (_busy || _done || _onGreen || _club == null || _club.Tap) return;
            int? ft = k.Pitch ? k.Ft : TracksEngine.PuttFor(k, _lie);
            if (ft == null) return;
            _club = k.Pitch ? k : k.With(ft.Value);
            _tee = TeeFor();
            DrawShot();
            DrawEntry();
            Announce();
        }

        /// <summary>A big number with its unit small beside it: "11 FT", "285 YDS".</summary>
        static VisualElement BigNumber(string number, string unit, float font)
        {
            Label n = Ui.Text(number, "tracks-ft-n", Ui.Bold), u = Ui.Text(unit, "tracks-ft-u", Ui.Bold);
            n.style.fontSize = font;
            u.style.fontSize = Mathf.Round(font * 0.3f);
            u.style.marginBottom = Mathf.Round(font * 0.17f);   // the unit sits on the number's baseline, not under its box
            return Ui.Box("tracks-ft", n, u);
        }

        static Label Words(string text, string tone)
        {
            Label words = Ui.Text(text, "tracks-shot-say", Ui.Medium);
            if (tone != null) words.AddToClassList(tone);
            return words;
        }

        // The shot bar: the club large, the putt to hit as a big number of feet with the three tees, the lie, and the
        // short facts: what is left, the amber reasons, the hint.
        void DrawShot()
        {
            _shotBar.Clear();
            _simRow.Clear();
            _shotBar.RemoveFromClassList("hidden"); _simRow.RemoveFromClassList("hidden");
            DrawClubs();
            DrawTurn();
            if (_club == null) { _shotBar.AddToClassList("hidden"); return; }
            bool canPick = !_onGreen && !_club.Tap;
            // The club. Upright a tap on it opens the clubs; on its side they are always listed, so it is only the name.
            VisualElement head = Ui.Box("tracks-shot-head", Ui.Text(_club.Name, "tracks-shot-club", Ui.Bold));
            if (canPick && !_wide) head.Add(new TracksChevron(false));
            if (canPick && _club.Id != _suggestedId) head.Add(Ui.Text("pick: " + TracksEngine.Suggest(DistYds, _lie).Short, "tracks-shot-was", Ui.Medium));
            head.Add(Ui.Box("grow"));
            if (canPick && !_club.Pitch) head.Add(Ui.Text(_club.Yds + " yds", "tracks-shot-yds", Ui.Bold));
            if (canPick && !_wide) head.OnTap(PickClub);
            VisualElement plate = Ui.Box("tracks-shot", head);
            // With more than one Player the shot is edged in the colour of the Player playing it.
            if (_round.Multi) { plate.AddToClassList("tracks-shot-turn"); plate.style.borderLeftColor = P.Colour; }
            if (_club.Tap) plate.Add(Words(TracksEngine.Hint(_club, _tee), "tracks-shot-say-soft"));   // a gimme has no putt to place
            else
            {
                string lie = _onGreen ? "green" : _lie;
                VisualElement facts = Ui.Box("tracks-shot-facts", Ui.Text(TracksEngine.LieOf(lie).Name.ToUpperInvariant(), "tracks-lie tracks-lie-" + TracksEngine.LieTone(lie), Ui.Bold));
                facts.Add(Ui.Text(TracksEngine.DistanceLeft(_onGreen, DistYds), "tracks-shot-left", Ui.Bold));
                foreach (string why in TracksEngine.Adjustments(_club, _lie, _tee)) facts.Add(Ui.Text(why, "tracks-shot-amber", Ui.Bold));
                facts.Add(Ui.Text(TracksEngine.Hint(_club, _tee), "tracks-shot-hint", Ui.Medium));
                // The putt to hit is the games' one tee marker (the feet large, the three tees, the ball on the one to putt
                // from), at the device's size: a phone 46, a tablet 72, a TV 100.
                plate.Add(Ui.Box("tracks-shot-main", Frame.TeeMarker(_club.Ft, _tee), facts));
            }
            _shotBar.Add(plate);
            // On its side the bar keeps clear of the club list down the right.
            if (_wide && _w > 0f) _shotBar.style.maxWidth = Mathf.Max(420f, _w - 264f); else _shotBar.style.maxWidth = StyleKeyword.None;
        }

        /// <summary>
        /// Whose shot it is, over the shot bar, with more than one Player (Tracks.js TurnPill and the outlined tile of the
        /// Player who is away): their colour, "Ava's shot", and the stroke they are playing on this hole.
        /// </summary>
        void DrawTurn()
        {
            _turn.Clear();
            bool show = _round.Multi && _club != null && !_done;
            _turn.EnableInClassList("hidden", !show);
            if (!show) return;
            VisualElement swatch = Ui.Box("tracks-turn-swatch");
            swatch.style.backgroundColor = P.Colour;
            int stroke = _strokes[_idx] + 1;
            _turn.Add(swatch);
            _turn.Add(Ui.Text(TracksEngine.TurnWords(P.Name), "tracks-turn-t", Ui.Bold));
            _turn.Add(Ui.Text(stroke == 1 ? "Tee shot" : "Stroke " + stroke, "tracks-turn-s", Ui.Medium));
            _turn.style.borderLeftColor = P.Colour;
            _turn.style.borderRightColor = P.Colour;
            _turn.style.borderTopColor = P.Colour;
            _turn.style.borderBottomColor = P.Colour;
        }

        // The clubs, always listed on a screen on its side (Tracks.js renderClubList): each club's putt and yards from
        // this lie, the suggested club tagged PICK, the club in hand lit, a tap to take another. On the green the putt leads
        // the list and the bag is dimmed; between holes all of it is.
        void DrawClubs()
        {
            _clubs.Clear();
            bool show = _wide && !_camsOpen && !_done;
            _clubs.EnableInClassList("hidden", !show);
            if (!show) return;
            bool canPick = _club != null && !_onGreen && !_club.Tap;
            _clubs.Add(Ui.Text("CLUBS", "tracks-k", Ui.Bold));
            var rows = new List<TracksEngine.Club>();
            if (_club != null && _club.Putt) rows.Add(_club);
            if (canPick) rows.AddRange(ClubRows()); else rows.AddRange(TracksEngine.Clubs);
            foreach (TracksEngine.Club k in rows)
            {
                TracksEngine.Club club = k;
                int? ft = k.Tap ? (int?)null : k.Pitch || k.Putt || !canPick ? k.Ft : TracksEngine.PuttFor(k, _lie);
                bool playable = ft != null && (canPick || k.Putt), on = _club != null && k.Id == _club.Id, sug = canPick && k.Id == _suggestedId;
                VisualElement row = Ui.Box("tracks-clubrow" + (on ? " tracks-clubrow-on" : sug ? " tracks-clubrow-sug" : "") + (playable || on ? "" : " tracks-clubrow-out"),
                    Ui.Text(k.Short, "tracks-clubrow-n", Ui.Bold), k.Putt ? null : Ui.Text(k.Yds + " yds", "tracks-clubrow-y", Ui.Medium), sug ? Ui.Text("PICK", "tracks-clubrow-tag", Ui.Bold) : null,
                    Ui.Box("grow"), Ui.Text(ft != null ? ft.ToString() : "–", "tracks-clubrow-ft", Ui.Bold), Ui.Text("FT", "tracks-clubrow-u", Ui.Bold));
                if (canPick && playable) row.OnTap(() => ChooseClub(club));
                _clubs.Add(row);
            }
        }

        // Upright the same clubs are a sheet (Tracks.js renderClubModal): what is left and from where, each club's
        // yards and putt, the suggested club and the clubs that reach tagged, and why a club cannot be played.
        void PickClub()
        {
            if (_busy || _club == null || _onGreen || _club.Tap) return;
            VisualElement dim = null;
            TracksEngine.Lie lie = TracksEngine.LieOf(_lie);
            double d = DistYds;
            VisualElement list = Ui.Box("modal", Ui.Title("Pick a club", "title-20"),
                Ui.Text(Math.Round(d) + " yds to the pin from the " + lie.Name.ToLowerInvariant() + (lie.PenaltyFt > 0 ? ". Every putt is " + lie.PenaltyFt + " ft longer from here." : "."), "muted wrap", Ui.Regular));
            foreach (TracksEngine.Club k in ClubRows())
            {
                TracksEngine.Club club = k;
                int? ft = k.Pitch ? k.Ft : TracksEngine.PuttFor(k, _lie);
                bool playable = ft != null, on = k.Id == _club.Id, sug = k.Id == _suggestedId, reach = k.Pitch || k.Yds >= d - 12;
                // Why not: the lie holds the long clubs back, or its extra feet take the putt past the end of the mat.
                string why = playable ? "" : k.Yds > lie.MaxYds ? " · too much club from the " + lie.Name.ToLowerInvariant() : " · needs a " + (k.Ft + lie.PenaltyFt) + " ft putt, the mat stops at " + TracksEngine.MaxPuttFt;
                VisualElement names = Ui.Box("row", Ui.Text(k.Name, "body tracks-pick-name", Ui.Bold), sug ? Ui.Text("PICK", "tracks-clubrow-tag", Ui.Bold) : null,
                    reach && playable && !k.Pitch ? Ui.Text("REACHES", "tracks-clubrow-tag tracks-clubrow-reach", Ui.Medium) : null);
                VisualElement row = Ui.Box("tracks-pickrow" + (on ? " tracks-clubrow-on" : sug ? " tracks-clubrow-sug" : "") + (playable ? "" : " tracks-clubrow-out"),
                    Ui.Box("grow", names, Ui.Text((k.Pitch ? k.Yds + " yds, pace sets the distance" : k.Yds + " yds") + why, "muted wrap", Ui.Regular)),
                    Ui.Text(playable ? ft.ToString() : "–", "tracks-clubrow-ft tracks-pick-ft", Ui.Bold), Ui.Text("FT", "tracks-clubrow-u", Ui.Bold));
                if (playable) row.OnTap(() => { dim.RemoveFromHierarchy(); ChooseClub(club); });
                list.Add(row);
            }
            list.Add(Ui.Button("Close", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            list.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", list).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        // ── The cards after a shot (tracks/PuttReadout.js ShotResultCard, Tracks.js renderHoleDone) ──

        /// <summary>
        /// The shot just played, mid-screen while the ball rests where it landed: how far it went in big type, the
        /// club and the lie it found, and where the line and the speed put it (yards off, inches on the mat). Shots
        /// from the course only, as the app. It is up for the shot's rest (RestShot, 3000 ms); the next shot's setup
        /// takes it down at the same moment putts are taken again, so it never stands over a putt being read.
        /// next: what is left, or the penalty.
        /// </summary>
        void ShowShotCard(ShotPlayed p, double yds, string lie, string next, bool nextBad)
        {
            HideLower();   // the card says all the lower third did, so the two are never up together
            _center.Clear();
            // The app's two sizes: compact over the hole on a phone, full size on a tablet.
            bool compact = !_wide || _h < 500f;
            float width = compact ? Mathf.Min(300f, _w > 148f ? _w - 48f : 300f) : 340f;
            float big = compact ? 40f : Mathf.Clamp(Mathf.Round(width * 0.3f), 44f, 96f);
            string lieName = TracksEngine.CardLie(lie);
            VisualElement where = Ui.Box("tracks-result-where", Ui.Text(p.Club.Name, "tracks-result-sub", Ui.Medium),
                lieName != null ? Ui.Text("  ·  " + lieName, "tracks-result-sub tracks-tone-" + TracksEngine.CardLieTone(lie), Ui.Bold) : null);
            string line = TracksEngine.CardLine(p.Lateral, out bool lineOk), speed = TracksEngine.CardSpeed(p.Depth, out bool speedOk);
            VisualElement Side(string k, string v, bool ok, string sub) => Ui.Box("tracks-result-col", Ui.Text(k, "tracks-result-k", Ui.Bold),
                Ui.Text(v, "tracks-result-v " + (ok ? "tracks-tone-good" : "tracks-tone-amber"), Ui.Bold), Ui.Text(sub, "tracks-result-sub", Ui.Medium));
            // With more than one Player the card says whose shot it was, in their colour.
            Label whose = null;
            if (_round.Multi) { whose = Ui.Text(P.Name.ToUpperInvariant(), "tracks-result-who", Ui.Bold); whose.style.color = P.Colour; }
            VisualElement card = Ui.Box(compact ? "tracks-result" : "tracks-result tracks-result-big", whose,
                BigNumber(Math.Floor(yds + 0.5).ToString("0"), "YDS", big), where,
                Ui.Box("tracks-result-cols", Side("LINE", line, lineOk, TracksEngine.CardLineMat(p.LineIn)), Ui.Box("tracks-result-rule"), Side("SPEED", speed, speedOk, TracksEngine.CardSpeedMat(p.RollIn))),
                Ui.Text(next, nextBad ? "tracks-result-next tracks-tone-bad" : "tracks-result-next", Ui.Medium));
            card.style.width = width;
            card.pickingMode = PickingMode.Ignore;
            _center.Add(card);
        }

        /// <summary>The hole is done: its name, the score in words (green under par, red over), the strokes, and what is
        /// next, for the HoleDone pause (2200 ms) before the next tee. It takes the lower third's place.</summary>
        void ShowHoleDone(string label)
        {
            HideLower();
            _center.Clear(); _doneSlot.Clear();
            TracksCourse.Hole next = _idx + 1 < _holes.Count ? _holes[_idx + 1] : null;
            string head = ("Hole " + Hole.N + (Hole.Name.Length > 0 ? " · " + Hole.Name : "")).ToUpperInvariant();
            string nextUp = next == null ? "Adding up the round" : "Next up: hole " + next.N + (next.Name.Length > 0 ? ", " + next.Name : "") + ". Par " + next.Par + ", " + next.Yds + " yds";
            VisualElement card = Ui.Box("tracks-holedone", Ui.Text(head, "tracks-holedone-k", Ui.Medium));
            // Everyone's score on the hole, one row each (Tracks.js renderHoleDone); one Player has it large.
            foreach (TracksPlayer q in _round.Players)
            {
                int d = q.Strokes[_idx] - Hole.Par;
                string words = _round.Multi ? (q.Picked ? "Picked up" : TracksEngine.ScoreName(q.Strokes[_idx], Hole.Par)) : label;
                Label score = Ui.Text(words, _round.Multi ? "tracks-holedone-v tracks-holedone-v-small" : "tracks-holedone-v", Ui.Bold);
                if (d != 0) score.AddToClassList(d < 0 ? "tracks-tone-good" : "tracks-tone-bad");
                VisualElement row = Ui.Box("tracks-holedone-score");
                if (_round.Multi)
                {
                    Label who = Ui.Text(q.Name, "tracks-holedone-who", Ui.Bold);
                    who.style.color = q.Colour;
                    row.Add(who);
                }
                row.Add(score);
                row.Add(Ui.Text(q.Strokes[_idx] + " on a par " + Hole.Par, "tracks-holedone-s", Ui.Medium));
                card.Add(row);
            }
            card.Add(Ui.Text(nextUp, "tracks-holedone-next", Ui.Medium));
            card.pickingMode = PickingMode.Ignore;
            (HoleCardCentred ? _center : _doneSlot).Add(card);
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
            // The title is the section header above the card, as Jump back in and League Night have theirs (Matt,
            // 2026-11-25); a track not yet played says New as the first chip.
            if (fresh) chips.Insert(0, Ui.Text("New", "chip chip-green"));
            void Play() { if (free) app.Push(new TracksSetupScreen(app, t)); else Club.Require(app, "Every Tracks course", () => app.Push(new TracksSetupScreen(app, t))); }
            // A flat card, the game tile's shape (GameCards.Make): the course from overhead in a window on top (painted
            // once and kept: TracksArt), then a solid bar with everything said about it. Nothing is written over the picture.
            VisualElement bar = Ui.Box("tracks-ccard-body",
                TrackLabels.Inspired(J.Str(t, "name"), "title-20"), TrackLabels.Where(J.Str(t, "location"), "body-2"),
                chips,
                Ui.Box("row totw-actions", Page.Swallow(Ui.Button("All courses", () => app.Push(new TracksCoursesScreen(app)), "btn btn-ghost grow totw-btn")),
                    Page.Swallow(Ui.Button(locked ? "Puttr Club" : "Play", Play, "btn btn-ghost grow totw-btn tracks-btn-2"))));
            // A tablet has the width to spare and not the height (Matt, 2026-11-24): the course is a square on the
            // left and everything said about it is a column beside it. A phone keeps the picture on top.
            bool side = App.Tablet;
            VisualElement card = Ui.Box(side ? "tracks-ccard totw totw-side" : "tracks-ccard totw", side ? TracksCoursesScreen.CourseStill(t, 768, 768) : TracksCoursesScreen.CourseStill(t), bar);
            // Anywhere on the card plays the track; the buttons keep their own taps.
            card.OnTap(Play);
            into.Add(Ui.Section("Track of the week"));
            into.Add(Ui.Box("pad", card));
        }
    }

    /// <summary>
    /// The round's result (Tracks.js renderGameOver). With more than one Player: who won (or "Tied"), everyone ranked by
    /// strokes with their score to par, and a chip per Player that shows that Player's stats and scorecard. One Player
    /// sees their own straight away, as before.
    /// </summary>
    public sealed class TracksResultScreen : AppScreen
    {
        readonly List<TracksCourse.Hole> _holes;
        readonly TracksRound _round;
        readonly VisualElement _chips, _detail;
        int _show;

        public TracksResultScreen(App app, TracksCourse course, List<TracksCourse.Hole> holes, TracksRound round, string level)
        {
            _holes = holes; _round = round;
            int par = holes.Sum(h => h.Par);
            // The shared game over page: the score to par (or who won), then the board and Exit and the rating, with
            // the stats and the scorecard as the second column.
            var spec = new GameOverSpec
            {
                Eyebrow = course.Name + "   ·   " + TracksEngine.LevelOf(level).Name + "   ·   " + holes.Count + " holes",
                RateAs = "Tracks: " + course.Name,
                XpUnpaid = true,   // the round's own game was just saved: the XP panel asks what it paid (nothing for test putts)
            };
            if (!round.Multi)
            {
                TracksPlayer me = round.Players[0];
                spec.Score = TracksEngine.ToPar(me.Total - par);
                spec.ScoreCaption = me.Total + " strokes on a par " + par;
            }
            else
            {
                spec.Outcome = round.Outcome();
                // Everyone, lowest first: their colour, name and score to par.
                spec.Standings = new List<(string, string, Color)>();
                foreach (TracksPlayer q in round.Ranking()) spec.Standings.Add((q.Name + (q.Guest && q.Seat != 0 ? " · guest" : ""), TracksEngine.ToPar(q.Total - par) + "  (" + q.Total + ")", q.Colour));
            }
            _chips = Ui.Box("row tracks-result-chips");
            _detail = Ui.Box("");
            VisualElement second = Ui.Box("");
            if (round.Multi) second.Add(_chips);
            second.Add(_detail);
            Show(0);
            spec.Detail = second;
            // Where the round stands: the track's board (the round is on it once saved).
            string slug = course.Slug, name = course.Name;
            spec.Actions = new List<(string, Action, bool)>
            {
                ("Exit", app.ExitToTab, true),
                ("Leaderboard", () => app.Push(new TracksLeaderboardScreen(app, slug, name)), false),
            };
            Root = GameOver.Build(app, spec);
        }

        /// <summary>One Player's stats and scorecard (Tracks.js setStatsFor); the chips pick whose.</summary>
        void Show(int seat)
        {
            _show = Mathf.Clamp(seat, 0, _round.Players.Count - 1);
            int par = _holes.Sum(h => h.Par);
            _chips.Clear();
            foreach (TracksPlayer q in _round.Players)
            {
                int at = q.Seat;
                VisualElement swatch = Ui.Box("tracks-turn-swatch");
                swatch.style.backgroundColor = q.Colour;
                // One chip style, two states (the picked one white); the swatch alone carries the Player's colour.
                VisualElement chip = Ui.Box(at == _show ? "scope scope-on tracks-pchip" : "scope tracks-pchip", swatch, Ui.Text(q.Name.ToUpperInvariant(), "scope-text", Ui.Bold),
                    Ui.Text(TracksEngine.ToPar(q.Total - par), "scope-text tracks-pchip-s", Ui.Bold));
                _chips.Add(chip.OnTap(() => Show(at)));
            }
            _detail.Clear();
            TracksPlayer p = _round.Players[_show];
            int fwTried = p.Fairways.Count(f => f != null), fwHit = p.Fairways.Count(f => f == true);
            _detail.Add(Ui.StatStrip((fwTried > 0 ? fwHit + "/" + fwTried : "–", "Fairways"), (p.Gir.Count(g => g) + "/" + _holes.Count, "Greens"), (p.Putts.Sum().ToString(), "Putts")).Margin(16));
            VisualElement card = Ui.Box("card tb-card");
            for (int i = 0; i < _holes.Count; i++)
                card.Add(Ui.Box("set-row", Ui.Text(_holes[i].N.ToString(), "board-place", Ui.Bold), Ui.Box("grow", Ui.Text(_holes[i].Name.Length > 0 ? _holes[i].Name : "Hole " + _holes[i].N, "body", Ui.Bold), Ui.Text("Par " + _holes[i].Par + " · " + p.Putts[i] + (p.Putts[i] == 1 ? " putt" : " putts"), "body-2", Ui.Regular)),
                    Ui.Text(p.Strokes[i] + "  " + TracksEngine.ScoreName(p.Strokes[i], _holes[i].Par), "body tracks-row-v", Ui.Bold)));
            LastRow(card);
            _detail.Add(Ui.Section(_round.Multi ? p.Name + "'s scorecard" : "Scorecard"));
            _detail.Add(card);
        }

        /// <summary>The last row of a bordered block has no divider under it.</summary>
        internal static void LastRow(VisualElement block) { if (block.childCount > 0) block[block.childCount - 1].style.borderBottomWidth = 0; }
    }
}

namespace PuttrApp
{
    /// <summary>
    /// "Loading course": the Puttr spinner and the course's name over the flyover, paused where it was when Tee off was
    /// pressed (dimmed), or over the dark when the flyover had not come in yet.
    /// </summary>
    public static class TracksLoading
    {
        public static VisualElement Cover(Texture still, string courseName)
        {
            VisualElement cover = Ui.Box("tracks-loading");
            cover.pickingMode = PickingMode.Position;   // nothing under it takes a tap while the round loads
            if (still != null)
            {
                var picture = new Image { image = still, scaleMode = ScaleMode.ScaleAndCrop };
                picture.AddToClassList("tracks-loading-still");
                picture.pickingMode = PickingMode.Ignore;
                cover.Add(picture);
            }
            // Its own mark, turning where it sits: the shared spinner turns at the middle of the screen, which is where
            // these words are.
            Image mark = Spinner.Mark(46f, Ui.Bright);
            float began = Time.realtimeSinceStartup;
            mark.schedule.Execute(() => { mark.style.rotate = new Rotate((Time.realtimeSinceStartup - began) * 300f % 360f); Perf.Wake(0.2f); }).Every(16);
            VisualElement words = Ui.Box("tracks-loading-box", mark, Ui.Text("Loading course", "tracks-loading-t", Ui.Bold));
            if (!string.IsNullOrEmpty(courseName)) words.Add(Ui.Text(courseName, "tracks-loading-s", Ui.Medium));
            words.pickingMode = PickingMode.Ignore;
            cover.Add(words);
            return cover;
        }
    }
}
