// Tournaments, from the live API as puttr-app shows them: the row on Home, the list, and one
// tournament's page (art, prize, rounds, leaderboard, and joining or playing the next round).
//
// A tournament is up to four rounds. A round is a fixed list of putts, each from a set distance
// and tee; a holed putt scores its distance in feet, one attempt a round, highest total wins.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>The sums and sentences, apart from the screens so they can be tested.</summary>
    public static class TournamentMath
    {
        /// <summary>A putt to play. Angle: a Precision quest putt's entry window in degrees (0: none).</summary>
        public struct Dealt { public int Feet, Tee; public double Angle; }

        /// <summary>The putts of a round, from its puttData: a JSON string "[{id, distance, tee}]".</summary>
        public static List<Dealt> Putts(object round)
        {
            var putts = new List<Dealt>();
            object raw = J.Get(round, "puttData");
            if (raw is string text) raw = J.Parse(text);
            foreach (object putt in J.List(raw))
            {
                // The mat is 3 to 11 feet and has three tees: anything else in the data is not putted.
                int feet = J.Int(putt, "distance"), tee = J.Int(putt, "tee", 2);
                if (feet < 3 || feet > 11) continue;
                putts.Add(new Dealt { Feet = feet, Tee = Mathf.Clamp(tee, 1, 3), Angle = J.Num(putt, "angle") });
            }
            return putts;
        }

        /// <summary>The most a round, or every round together, can score: the feet of every putt.</summary>
        public static int Possible(IEnumerable<object> rounds)
        {
            int feet = 0;
            foreach (object round in rounds) foreach (Dealt putt in Putts(round)) feet += putt.Feet;
            return feet;
        }

        /// <summary>The round to play next, 1 to 4, from the Player's results (r1_score .. r4_score); 5 when all are played.</summary>
        public static int CurrentRound(object results, int rounds = 4)
        {
            for (int round = 1; round <= rounds; round++)
                if (J.Get(results, "r" + round + "_score") == null) return round;
            return rounds + 1;
        }

        public enum State { Upcoming, Active, Ended }

        /// <summary>By the day, in UTC, as the app compares them.</summary>
        public static State StateOf(object tournament, DateTime nowUtc)
        {
            DateTime? start = ProfileMath.Date(tournament, "startDate"), end = ProfileMath.Date(tournament, "endDate");
            if (start != null && nowUtc.Date < start.Value.Date) return State.Upcoming;
            if (end != null && nowUtc.Date > end.Value.Date) return State.Ended;
            return State.Active;
        }

        public static string Dates(object tournament)
        {
            DateTime? start = ProfileMath.Date(tournament, "startDate"), end = ProfileMath.Date(tournament, "endDate");
            if (start == null || end == null) return "";
            return start.Value.ToString("MMM d", CultureInfo.InvariantCulture) + " – " + end.Value.ToString("MMM d", CultureInfo.InvariantCulture);
        }

        /// <summary>Easy, Medium or Hard from the game's difficulty, which arrives as a string like "7.0".</summary>
        public static string Level(object round)
        {
            double difficulty = J.Num(round, "difficulty");
            return difficulty > 6.5 ? "Hard" : difficulty > 3.2 ? "Medium" : "Easy";
        }

        /// <summary>The tournament's picture, sized by the image service the app uses for images.puttr.co.</summary>
        public static string Art(object tournament, int size)
        {
            string url = J.Str(tournament, "prizeImage");
            if (url.Length == 0 || url == "null") url = J.Str(tournament, "heroImage");
            if (url.Contains("giphy.com/")) return GiphyStill(url); // a private tournament's GIF cover
            return Sized(url, size);
        }

        /// <summary>Giphy's JPG still of a GIF (480w_s.jpg beside it). Unity decodes PNG and JPG only, and every other
        /// still Giphy makes is itself a GIF. Anything not a Giphy GIF comes back as it was.</summary>
        public static string GiphyStill(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.Contains("giphy.com/")) return url ?? "";
            int query = url.IndexOf('?');
            string path = query >= 0 ? url.Substring(0, query) : url;
            if (!path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return url;
            return path.Substring(0, path.LastIndexOf('/') + 1) + "480w_s.jpg";
        }

        public static string Sized(string url, int size)
        {
            const string bucket = "images.puttr.co";
            int at = url.IndexOf(bucket, StringComparison.Ordinal);
            if (at < 0) return url;
            return "https://puttr.imgix.net" + url.Substring(at + bucket.Length) + "?w=" + size + "&h=" + size;
        }
    }

    public static partial class Compete
    {
        // On Home: League Night first (LeagueNight.cs), then the tournaments.
        static partial void FillCards(App app, VisualElement slot)
        {
            slot.Clear();
            var league = new VisualElement();
            var tournaments = new VisualElement();
            slot.Add(league);
            slot.Add(tournaments);
            LeagueCard(app, league);
            Load(list => TournamentRow(app, tournaments, list));
        }

        static partial void LeagueCard(App app, VisualElement slot);

        /// <summary>Public tournaments that are running or coming, with the private ones the Player is invited to first.</summary>
        public static void Load(Action<List<object>> done)
        {
            List<object> open = null, invited = null;
            void Both()
            {
                if (open == null || invited == null) return;
                var all = new List<object>();
                var seen = new HashSet<string>();
                foreach (object row in invited) if (seen.Add(J.Str(row, "id"))) { ((Dictionary<string, object>)row)["private"] = true; all.Add(row); }
                foreach (object row in open) if (seen.Add(J.Str(row, "id"))) all.Add(row);
                done(all);
            }
            App.Run(Api.Get("/tournaments/upcoming", data => { open = J.List(data); Both(); }, _ => { open = new List<object>(); Both(); }));
            App.Run(Api.Get("/tournaments/PRIVATE", data => { invited = J.List(data); Both(); }, _ => { invited = new List<object>(); Both(); }));
        }

        static void TournamentRow(App app, VisualElement slot, List<object> list)
        {
            slot.Clear();
            if (list.Count == 0) return;
            slot.Add(Ui.Section("Tournaments", "See all", () => app.Push(new TournamentsScreen(app))));
            ScrollView row = Ui.Scroll(true);
            for (int i = 0; i < Mathf.Min(6, list.Count); i++)
            {
                VisualElement poster = Poster(app, list[i], 200);
                poster.style.marginLeft = i == 0 ? 16 : 12;
                row.Add(poster);
            }
            var end = new VisualElement();
            end.style.width = 16;
            row.Add(end);
            slot.Add(row);
        }

        /// <summary>
        /// A tournament as a poster (docs/design-guidelines.md, the game tile's shape): its art in a window, 5 wide to 3
        /// high, and under it a solid bar with the name, the dates and the Players, and whether the Player is in.
        /// Nothing is written over the picture. size is the poster's width.
        /// </summary>
        public static VisualElement Poster(App app, object tournament, float size)
        {
            bool joined = J.Get(tournament, "userJoined") != null;
            TournamentMath.State state = TournamentMath.StateOf(tournament, DateTime.UtcNow);
            VisualElement art = Ui.Box("poster-art");
            art.style.height = Mathf.Round(size * 0.6f);
            art.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            art.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            art.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            Images.Into(art, TournamentMath.Art(tournament, 400));
            int players = J.Int(tournament, "participants");
            string when = TournamentMath.Dates(tournament);
            if (players > 0) when += (when.Length > 0 ? "   \u00B7   " : "") + players + (players == 1 ? " Player" : " Players");
            VisualElement tags = Ui.Box("row poster-tags");
            if (J.Flag(tournament, "private")) tags.Add(Ui.Text("Private", "chip chip-amber", Ui.Bold));
            tags.Add(Ui.Text(joined ? "Joined" : state == TournamentMath.State.Ended ? "Ended" : "Join", joined ? "chip chip-green" : "chip", Ui.Bold));
            VisualElement poster = Ui.Box("poster", art,
                Ui.Box("poster-bar", Ui.Title(J.Str(tournament, "title", "Tournament"), "poster-title"), Ui.Caps(when, "caps poster-when"), tags));
            poster.style.width = size;
            poster.style.minWidth = poster.style.maxWidth = size; // fixed: a row or a column never stretches it
            poster.style.flexShrink = 0;
            return poster.OnTap(() => app.Push(new TournamentScreen(app, tournament)));
        }
    }

    public sealed class TournamentsScreen : AppScreen
    {
        /// <summary>Set by CreateTournamentScreen once the API has taken a new tournament: the list reads again (twice, a
        /// moment apart, as the read replica can lag the write) when it comes back to the front.</summary>
        public static bool Created;

        readonly App _app;
        readonly ScrollView _scroll;
        readonly VisualElement _open, _past, _pull, _pullSpin;
        readonly Image _pullMark;
        float _fetchedAt = -100f, _pulled, _wheel;
        int _asked;
        bool _refreshing, _dragging, _scripted;
        Vector2 _dragFrom;

        /// <summary>How far down the list is pulled before letting go reads it again, and the most it shows.</summary>
        const float PullAt = 70f, PullMost = 110f, Throttle = 8f;

        public TournamentsScreen(App app)
        {
            _app = app;
            _scroll = Ui.Scroll();
            _scroll.Add(Ui.Header("Tournaments", app.Pop));
            if (!Kid.IsKid(app.Profile) && !Tv.On) _scroll.Add(Ui.Box("pad", Ui.Button("Create tournament", () => Club.Require(app, "Private tournaments", () => app.Push(new CreateTournamentScreen(app))), "btn btn-ghost")));
            _open = new VisualElement();
            _past = new VisualElement();
            _scroll.Add(_open);
            _scroll.Add(_past);
            var foot = new VisualElement();
            foot.style.height = 28;
            _scroll.Add(foot);
            // Until the list is here: its heading and a row of poster-sized placeholders.
            VisualElement posters = Ui.Box("row sk-strip");
            for (int i = 0; i < 3; i++)
            {
                VisualElement card = Skeleton.Card(CardSize);
                card.style.width = card.style.height = CardSize; card.style.marginRight = 12; card.style.flexShrink = 0;
                posters.Add(card);
            }
            Skeleton.Hold(_open, Ui.Box("pad", Skeleton.Bar(0.45f, 16f).Margin(14), posters.Margin(12)));

            // Pull to refresh: UI Toolkit has none, so the page watches for a pull past the top of the list (a finger's
            // elastic overscroll, a mouse drag, or the wheel rolled up at the top) and shows the Puttr mark turning with it.
            _pullMark = Spinner.Mark(30f, new Color(1f, 1f, 1f, 0.55f));
            _pullMark.AddToClassList("tourn-pull-mark");
            _pullSpin = new VisualElement();
            _pull = Ui.Box("tourn-pull", _pullMark, _pullSpin);
            _pull.pickingMode = PickingMode.Ignore;
            VisualElement page = Ui.Box("tourn-page", _scroll, _pull);
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Tournaments");
            if (sheet != null) page.styleSheets.Add(sheet);
            Root = page;
            WatchPull();
            // -app2TournPull: a pull to refresh three seconds in (for checking from a script).
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TournPull") >= 0)
                _scroll.schedule.Execute(() => { _dragging = true; Pulled(PullAt * 0.8f); _scroll.schedule.Execute(Refresh).ExecuteLater(1500); }).ExecuteLater(3000);
        }

        // Read again whenever the page comes to the front (a new tournament, a round played), at most every few seconds.
        public override void Shown()
        {
            if (Created)
            {
                Created = false;
                Fetch(null);
                _scroll.schedule.Execute(() => { if (_scroll.panel != null) Fetch(null); }).ExecuteLater(2500);
                return;
            }
            if (Time.realtimeSinceStartup - _fetchedAt > Throttle) Fetch(null);
        }

        /// <summary>The current and upcoming tournaments (the Player's private ones first) and the past ones. What is on
        /// screen stays until the new answer is here, so a refresh never blanks the page.</summary>
        void Fetch(Action done)
        {
            int me = ++_asked;
            _fetchedAt = Time.realtimeSinceStartup;
            int waiting = 2;
            void One() { if (--waiting == 0) done?.Invoke(); }
            // As puttr-app's TournamentsScreen: "Current and upcoming" as a row of square posters to scroll sideways,
            // then "Past" the same way under it.
            Compete.Load(list =>
            {
                if (me == _asked)
                {
                    _open.Clear();
                    _open.Add(Ui.Section("Current and upcoming"));
                    if (list.Count == 0) _open.Add(Ui.Box("pad", Ui.Box("card", Ui.Text("No tournaments right now. A new one opens on the first of every month.", "body-2", Ui.Medium))));
                    else _open.Add(Row(_app, list));
                    Perf.Wake(0.3f);
                }
                One();
            });
            App.Run(Api.Get("/tournaments/past", data =>
            {
                if (me == _asked)
                {
                    List<object> list = J.List(data);
                    _past.Clear();
                    if (list.Count > 0) { _past.Add(Ui.Section("Past")); _past.Add(Row(_app, list)); }
                    // -app2TournPast: open the latest past tournament (-app2TournReel: and play its highlights), for checking from a script.
                    string[] args = Environment.GetCommandLineArgs();
                    if (list.Count > 0 && !_scripted && (Array.IndexOf(args, "-app2TournPast") >= 0 || Array.IndexOf(args, "-app2TournReel") >= 0))
                    { _scripted = true; _app.Push(new TournamentScreen(_app, list[0])); }
                }
                One();
            }, _ => One()));
        }

        void WatchPull()
        {
            // A finger: the ScrollView's elastic overscroll takes the content past the top (a negative offset).
            // A mouse: a drag down that starts with the list at the top. TrickleDown so a poster under the pointer
            // does not keep it; a drag of a few units or less stays a tap.
            _scroll.RegisterCallback<PointerDownEvent>(e =>
            {
                _dragging = !_refreshing && _scroll.scrollOffset.y <= 1f;
                _dragFrom = e.position;
            }, TrickleDown.TrickleDown);
            _scroll.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging) return;
                float down = e.position.y - _dragFrom.y;
                if (down < -6f || _scroll.scrollOffset.y > 1f) { _dragging = false; Pulled(0f); return; }
                if (e.pointerType == "mouse") Pulled(down * 0.6f);
            }, TrickleDown.TrickleDown);
            _scroll.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragging && _pulled > 8f) e.StopPropagation(); // a pull is not also a tap on the poster under it
                Release();
            }, TrickleDown.TrickleDown);
            _scroll.RegisterCallback<PointerCancelEvent>(_ => { _dragging = false; Pulled(0f); }, TrickleDown.TrickleDown);
            _scroll.RegisterCallback<PointerCaptureOutEvent>(_ => { if (_dragging) Release(); });
            // The wheel (or a trackpad) rolled up while already at the top.
            _scroll.RegisterCallback<WheelEvent>(e =>
            {
                if (_refreshing || _scroll.scrollOffset.y > 1f || e.delta.y >= 0f) { _wheel = 0f; return; }
                _wheel += -e.delta.y * 12f;
                Pulled(_wheel);
                if (_wheel >= PullAt) { _wheel = 0f; Refresh(); }
                else _scroll.schedule.Execute(() => { if (!_refreshing && _wheel > 0f) { _wheel = 0f; Pulled(0f); } }).ExecuteLater(400);
            }, TrickleDown.TrickleDown);
            // The elastic overscroll is read every frame it moves.
            _scroll.schedule.Execute(() =>
            {
                if (_refreshing) return;
                float over = -_scroll.scrollOffset.y;
                if (over > 0.5f) { Pulled(over, true); if (over >= PullAt) _armed = true; }
                else if (_armed) { _armed = false; Refresh(); }
                else if (!_dragging && _wheel <= 0f && _pulled > 0f) Pulled(0f);
            }).Every(16);
        }

        bool _armed; // a finger pulled far enough: the refresh starts as the list springs back

        void Release()
        {
            bool go = _dragging && _pulled >= PullAt;
            _dragging = false;
            if (go) Refresh(); else if (!_armed) Pulled(0f);
        }

        /// <summary>Shows the Puttr mark coming in and turning in the gap over the list as it is pulled. A finger's pull
        /// moves the list itself (elastic); a mouse or wheel pull moves it here, the same way.</summary>
        void Pulled(float distance, bool elastic = false)
        {
            _pulled = Mathf.Clamp(distance, 0f, PullMost);
            float share = Mathf.Clamp01(_pulled / PullAt);
            _scroll.contentContainer.style.translate = new Translate(0, elastic ? 0f : _pulled);
            _pullMark.style.opacity = share;
            _pullMark.style.translate = new Translate(0, Mathf.Max(0f, _pulled - 30f) * 0.5f);
            _pullMark.style.rotate = new Rotate(new Angle(share * 300f, AngleUnit.Degree));
            _pullMark.style.display = _refreshing ? DisplayStyle.None : DisplayStyle.Flex;
            Perf.Wake(0.2f);
        }

        /// <summary>Reads the lists again with the Puttr loader turning until they are back.</summary>
        public void Refresh()
        {
            if (_refreshing) return;
            _refreshing = true;
            _armed = _dragging = false;
            Pulled(0f);
            _pullSpin.Add(Spinner.Small()); // the one Puttr loader, in the middle of the screen
            float since = Time.realtimeSinceStartup;
            Fetch(() =>
            {
                // Long enough to be seen, so a quick answer still reads as a refresh.
                long wait = (long)Mathf.Max(0f, (0.6f - (Time.realtimeSinceStartup - since)) * 1000f);
                _scroll.schedule.Execute(() => { _pullSpin.Clear(); _refreshing = false; Perf.Wake(0.3f); }).ExecuteLater(wait);
            });
        }

        /// <summary>A poster's width: the art is cover-cropped into its window, never stretched.</summary>
        const float CardSize = 220;

        static VisualElement Row(App app, List<object> list)
        {
            ScrollView row = Ui.Scroll(true);
            row.AddToClassList("tourn-row");
            for (int i = 0; i < list.Count; i++)
            {
                VisualElement poster = Compete.Poster(app, list[i], CardSize);
                poster.style.marginLeft = i == 0 ? 16 : 12;
                row.Add(poster);
            }
            var end = new VisualElement();
            end.style.width = 16;
            end.style.flexShrink = 0;
            row.Add(end);
            return row;
        }
    }

    public sealed class TournamentScreen : AppScreen
    {
        readonly App _app;
        readonly object _tournament;
        readonly string _id;
        readonly VisualElement _hero, _prize, _champion, _rounds, _board, _action;
        List<object> _roundRows = new List<object>(), _leaders;
        object _results;
        bool _joined, _busy, _scripted, _haveRounds, _reeled;
        int _heroMode = -1; // 0: phone (art over the title), 1: wide (art boxed beside the info)
        readonly HashSet<string> _open = new HashSet<string>(); // leaderboard rows the Player has opened
        readonly Dictionary<string, List<object>> _userGames = new Dictionary<string, List<object>>();

        /// <summary>TournamentView's landscape art: a square of at most 240, never the page's width.</summary>
        const float WideArt = 240, NarrowArtMax = 520, WideAt = 600;

        public TournamentScreen(App app, object tournament)
        {
            _app = app;
            _tournament = tournament;
            _id = J.Str(tournament, "id");
            _joined = J.Get(tournament, "userJoined") != null;

            _hero = new VisualElement();
            _hero.RegisterCallback<GeometryChangedEvent>(_ => Hero());
            _prize = new VisualElement(); _champion = new VisualElement(); _rounds = new VisualElement(); _board = new VisualElement(); _action = new VisualElement();

            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad", _hero, _prize, _action, _champion));
            scroll.Add(_rounds);
            scroll.Add(_board);
            var foot = new VisualElement();
            foot.style.height = 28;
            scroll.Add(foot);
            Root = scroll;
            // The rounds and the leaderboard come from their own calls: their shapes hold the page until they do.
            Skeleton.Hold(_rounds, Skeleton.Strip(92, 4));
            Skeleton.Hold(_board, Skeleton.Section(260));
        }

        // TournamentView's hero card. Phone: the square art in a window across the card, and under it, on the
        // card's solid ground, the status, title, dates, Players, description and host: nothing is written over
        // the picture. Wide (tablet, landscape): the art boxed at 240 on the left, and the same column beside it,
        // with the prize at its foot.
        void Hero()
        {
            float width = _hero.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0) return;
            int mode = width >= WideAt ? 1 : 0;
            if (mode == _heroMode) return;
            _heroMode = mode;
            _hero.Clear();
            _prize.Clear();

            object t = _tournament;
            TournamentMath.State state = TournamentMath.StateOf(t, DateTime.UtcNow);
            bool wide = mode == 1;
            float side = wide ? Mathf.Min(WideArt, Mathf.Round(width * 0.4f)) : Mathf.Min(width, NarrowArtMax);

            VisualElement art = Ui.Box("tourn-art");
            art.style.width = art.style.height = side;
            art.style.minWidth = art.style.maxWidth = side;
            art.style.minHeight = art.style.maxHeight = side;
            Images.Into(art, TournamentMath.Art(t, 800));
            if (!TournamentMath.Art(t, 800).StartsWith("http")) art.Add(Ui.Box("tourn-art-icon", Ui.Icon("Trophy", wide ? 56 : 80, Ui.Faint)));

            Label status = Ui.Text(state == TournamentMath.State.Active ? "ACTIVE" : state == TournamentMath.State.Upcoming ? "UPCOMING" : "ENDED",
                state == TournamentMath.State.Active ? "chip chip-green tourn-status" : state == TournamentMath.State.Upcoming ? "chip chip-amber tourn-status" : "chip tourn-status", Ui.Bold);
            string title = J.Str(t, "title", "Tournament");
            int players = J.Int(t, "participants");
            string dates = TournamentMath.Dates(t);
            Label meta = Ui.Caps((dates.Length > 0 ? dates + "   \u00B7   " : "") + players + (players == 1 ? " Player" : " Players"), "caps tourn-meta");
            string host = J.Str(t, "displayName");
            VisualElement hostChip = host.Length == 0 ? null : Ui.Box("tourn-host",
                Ui.Avatar(host, J.Str(t, "photoURL")),
                Ui.Box("tourn-host-words", Ui.Caps("Hosted by", "caps"), Ui.Text(host, "tourn-host-name", Ui.Bold)));
            string about = J.Str(t, "description");
            Label description = about.Length > 0 && about != "null" ? Ui.Text(about, "body-2 tourn-desc", Ui.Medium) : null;

            string prize = J.Str(t, "prizeDescription"), link = J.Str(t, "prizeLink");
            VisualElement prizeRow = null;
            if (prize.Length > 0 && prize != "null")
            {
                // A plain trophy, gold (a prize), with no tinted square behind it.
                prizeRow = Ui.Box("row tourn-prize",
                    Ui.Icon("Trophy", 24, Ui.Gold),
                    Ui.Box("grow tourn-prize-words", Ui.Text("Prize details", "body", Ui.Bold), Ui.Text(prize, "body-2 tourn-prize-text", Ui.Medium)));
                if (link.StartsWith("http")) { prizeRow.Add(Ui.Icon("CaretRight", 14, Ui.Faint)); prizeRow.OnTap(() => Application.OpenURL(link)); }
            }

            // The same words in both shapes: the status, the name, the dates and Players, what it is, who hosts it.
            VisualElement column = Ui.Box("tourn-col", status, Ui.Title(title, "title tourn-hero-title"), meta, description, hostChip);
            if (wide)
            {
                if (prizeRow != null) { column.Add(Ui.Box("grow")); prizeRow.AddToClassList("tourn-prize-in"); column.Add(prizeRow); }
                _hero.Add(Ui.Box("glass tourn-hero tourn-hero-wide", art, column));
            }
            else
            {
                _hero.Add(Ui.Box("glass tourn-hero tourn-hero-narrow", art, column));
                if (prizeRow != null) { prizeRow.AddToClassList("card"); prizeRow.AddToClassList("tourn-prize-card"); _prize.Add(prizeRow); }
            }
            Perf.Wake(0.3f);
        }

        // Read again every time the page comes to the front: a round just played changes all of it.
        public override void Shown()
        {
            _userGames.Clear(); // a round just played adds a game
            App.Run(Api.Get("/tournaments/details/" + _id, data => { _roundRows = J.List(J.Get(J.First(data), "rounds")); _haveRounds = true; Show(); }));
            App.Run(Api.Get("/tournaments/userResults/" + _id, data => { _results = J.First(data); if (_results != null) _joined = true; Show(); }));
            App.Run(Api.Get("/tournaments/leaderboard/" + _id, data => { _leaders = J.List(data); Show(); }));
        }

        void Show()
        {
            TournamentMath.State state = TournamentMath.StateOf(_tournament, DateTime.UtcNow);
            int count = Mathf.Max(1, _roundRows.Count), current = TournamentMath.CurrentRound(_results, count), possible = TournamentMath.Possible(_roundRows);

            // What the Player can do now.
            _action.Clear();
            // A TV shows the standings only: a round is recorded with the phone's camera.
            if (Tv.On) { }
            else if (state == TournamentMath.State.Upcoming)
            {
                DateTime? start = ProfileMath.Date(_tournament, "startDate");
                _action.Add(Ui.Text("Starts " + (start != null ? start.Value.ToString("dddd, MMMM d", CultureInfo.InvariantCulture) : "soon") + ".", "body-2 center", Ui.Medium).Margin(16));
                if (!_joined) _action.Add(Ui.Button("Join tournament", () => Club.Require(_app, "Tournaments", Join)));
            }
            else if (state == TournamentMath.State.Active)
            {
                if (!_joined) _action.Add(Ui.Button("Join tournament", () => Club.Require(_app, "Tournaments", Join)));
                else if (_roundRows.Count > 0 && current <= _roundRows.Count) _action.Add(Ui.Button("Play round " + current, () => _app.Games.PlayRound(_tournament, _roundRows[current - 1], current)));
                else if (_roundRows.Count > 0) _action.Add(Ui.Text("All rounds played. Check back for the final standings.", "body-2 center", Ui.Medium).Margin(16));
            }

            // The champion, once it is over.
            _champion.Clear();
            if (state == TournamentMath.State.Ended && _leaders != null && _leaders.Count > 0)
            {
                object first = _leaders[0];
                Label crown = Ui.Caps("Champion", "caps");
                crown.style.color = Ui.Gold;   // gold: a trophy
                VisualElement card = Ui.Box("card champion",
                    crown,
                    Ui.Box("row champion-row", Ui.Avatar(J.Str(first, "displayName"), J.Str(first, "photoUrl"), true),
                        Ui.Box("grow", Ui.Title(J.Str(first, "displayName", "A Player"), "title-20"), Ui.Text(J.Num(first, "totalScore").ToString("0") + (possible > 0 ? " of " + possible + " feet" : " feet"), "body-2", Ui.Medium).Margin(4)),
                        PutterBrands.RowBadge(first, 28f)));
                string reel = J.Str(_tournament, "reelVideoUrl");
                if (reel.StartsWith("http"))
            {
                card.Add(Ui.Button("Watch the highlights", () => Highlights(reel), "btn btn-ghost"));
                if (!_reeled && Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TournReel") >= 0) { _reeled = true; Highlights(reel); }
            }
                card.style.marginTop = 12;
                _champion.Add(card);
            }

            // The rounds, once they are here (the placeholder holds the place until then).
            if (_haveRounds) _rounds.Clear();
            if (_haveRounds && _roundRows.Count > 0)
            {
                _rounds.Add(Ui.Section(_roundRows.Count + (_roundRows.Count == 1 ? " round" : " rounds")));
                // One bordered strip, a cell a round: the round, its putts, and the Player's score or how hard it is.
                VisualElement tiles = Ui.Box("strip round-tiles");
                for (int i = 0; i < _roundRows.Count; i++)
                {
                    object round = _roundRows[i];
                    object score = J.Get(_results, "r" + (i + 1) + "_score");
                    List<TournamentMath.Dealt> putts = TournamentMath.Putts(round);
                    bool now = i + 1 == current && state == TournamentMath.State.Active && _joined;
                    VisualElement tile = Ui.Box((i == 0 ? "strip-cell strip-cell-first" : "strip-cell") + (now ? " round-now" : ""),
                        Ui.Num("R" + (i + 1), "strip-num"),
                        Ui.Caps(putts.Count + (putts.Count == 1 ? " putt" : " putts"), "strip-cap"),
                        Ui.Caps(score != null ? J.Num(_results, "r" + (i + 1) + "_score").ToString("0") + " ft" : TournamentMath.Level(round), score != null ? "strip-cap round-done" : "strip-cap"));
                    tiles.Add(tile);
                }
                _rounds.Add(Ui.Box("pad", tiles));
            }

            // The leaderboard.
            _board.Clear();
            if (_leaders != null)
            {
                _board.Add(Ui.Section("Leaderboard", possible > 0 ? possible + " feet possible" : null));
                // The Players are rows in one bordered block.
                VisualElement card = Ui.Box("tourn-board");
                if (_leaders.Count == 0) card.Add(Ui.Text("No rounds played yet. Yours could be the first.", "body-2 tourn-board-empty", Ui.Medium));
                int roundCount = _roundRows.Count > 0 ? _roundRows.Count : 4;
                for (int i = 0; i < Mathf.Min(_leaders.Count, 50); i++) card.Add(LeaderRow(_leaders[i], i + 1, roundCount));
                _board.Add(Ui.Box("pad", card));
            }
        }

        // LeaderRow: a tap on the row opens it in place to that Player's round scores (R1 to R4); a tap
        // on a played round opens the game view for that round, with its video replay. The avatar opens
        // their profile and the brand mark their putter.
        VisualElement LeaderRow(object row, int place, int roundCount)
        {
            string name = J.Str(row, "displayName", "A Player");
            string userId = J.Str(row, "userId");
            double total = J.Num(row, "totalScore");
            Label rank = Ui.Num(place.ToString(), "tourn-place");
            if (place == 1) rank.style.color = Ui.Gold;   // gold: the leader (the champion once it has ended)
            VisualElement avatar = Ui.Avatar(name, J.Str(row, "photoUrl"));
            Swallow(avatar);
            avatar.OnTap(() => ProfileScreen.Open(_app, userId, row));
            VisualElement head = Ui.Box("tourn-leader-head", rank, avatar,
                Ui.Box("grow tourn-leader-words", Ui.Text(name, "tourn-leader-name", Ui.Bold), Ui.Text(J.Str(row, "homeTown"), "tourn-leader-town", Ui.Medium)),
                PutterBrands.RowBadge(row, 24f)); // their putter's brand (LeaderRow's 24): a tap opens the putter sheet
            // The fist bump, left of the total (tournaments/LeaderRow): never on your own row; it does not open the row.
            if (FistBumps.CanBump(_app, userId)) head.Add(FistBumps.Button(_app, userId));
            head.Add(Ui.Num(total > 0 ? total.ToString("0") : "-", "tourn-total"));

            VisualElement rounds = Ui.Box("tourn-rounds");
            for (int r = 1; r <= Mathf.Min(roundCount, 4); r++)
            {
                bool played = J.Num(row, "r" + r + "_score") > 0; // as LeaderRow: a 0 or missing score is not tappable
                VisualElement pill = Ui.Box(played ? "tourn-round tourn-round-played" : "tourn-round",
                    Ui.Caps("R" + r, "caps tourn-round-label"),
                    Ui.Num(played ? J.Num(row, "r" + r + "_score").ToString("0") : "-", "tourn-round-score"));
                int num = r;
                if (played) pill.OnTap(() => OpenRound(num, userId));
                rounds.Add(pill);
            }

            string key = userId.Length > 0 ? userId : name + place;
            // -app2TournExpand: the leader's row starts open; -app2TournRound: and its round 1 opens (for checking from a script).
            if (place == 1 && !_scripted && Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TournExpand") >= 0)
            {
                _scripted = true;
                _open.Add(key);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TournRound") >= 0) OpenRound(1, userId);
            }
            VisualElement line = Ui.Box(_open.Contains(key) ? "tourn-leader tourn-leader-open" : "tourn-leader", head, rounds);
            if (place == 1) line.AddToClassList("tourn-leader-first");
            if (userId == Auth.Uid) line.AddToClassList("board-me");
            head.OnTap(() =>
            {
                bool open = !_open.Remove(key);
                if (open) _open.Add(key);
                line.EnableInClassList("tourn-leader-open", open);
                Perf.Wake(0.5f); // keep drawing through the height transition
            });
            return line;
        }

        /// <summary>pressLeaderboardRound: that Player's games in this tournament, then the one for the round.</summary>
        void OpenRound(int round, string userId)
        {
            if (userId.Length == 0) return;
            void Go(List<object> games)
            {
                foreach (object game in games)
                    if (J.Int(game, "roundNum") == round && J.Str(game, "id").Length > 0) { _app.Push(new GameViewScreen(_app, J.Str(game, "id"))); return; }
                _app.Toast("That round's game is not available.");
            }
            if (_userGames.TryGetValue(userId, out List<object> have)) { Go(have); return; }
            App.Run(Api.Get("/tournaments/userGames/" + _id + "/" + userId, data => { List<object> games = J.List(data); _userGames[userId] = games; Go(games); },
                why => _app.Toast(why == "offline" ? "No connection. Try again." : "Could not open that round.")));
        }

        /// <summary>The tournament's highlights reel, in the app: the app's full-screen player (black, the video fitted to
        /// its own shape, the Puttr loader until the first frame), with a close button over it.</summary>
        void Highlights(string url)
        {
            SetupGuide.Play(Root, url);
            VisualElement layer = App.Layer(Root);
            VisualElement player = null; // the player Play just put on top (the loader's own layer can come after it)
            for (int i = layer.childCount - 1; i >= 0 && player == null; i--) if (layer[i].ClassListContains("modal-dim")) player = layer[i];
            if (player == null) return;
            // A tap anywhere closes the player; the button is there so that is plain. It sits inside the player, so its
            // tap reaches the player's own close.
            float k = Screen.width > 0 && layer.resolvedStyle.width > 0 ? layer.resolvedStyle.width / Screen.width : 1f;
            VisualElement close = Ui.Box("", Ui.Icon("XCircle", 34, Color.white));
            close.style.position = Position.Absolute;
            close.style.right = 12;
            close.style.top = 10 + (Screen.height - Screen.safeArea.yMax) * k;
            close.style.width = close.style.height = 48;
            close.style.alignItems = Align.Center;
            close.style.justifyContent = Justify.Center;
            player.Add(close);
        }

        /// <summary>A tap on this child does not also open or close the row under it.</summary>
        static void Swallow(VisualElement element)
        {
            element.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            element.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            element.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
        }

        void Join()
        {
            if (_busy || _joined) return; // the API never answers a second join
            _busy = true;
            App.Run(Api.Get("/tournaments/join/" + _id, _ => { _busy = false; _joined = true; _app.Toast("You are in. Good luck."); Shown(); },
                why => { _busy = false; _app.Toast(why == "offline" ? "No connection. Try again." : "Could not join. Try again in a moment."); }));
        }
    }
}
