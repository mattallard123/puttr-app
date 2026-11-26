// The screens most taps in puttr-app lead to, for navigation parity (docs/screen-parity.md):
//
//   GameViewScreen          profile/GameView.js        a played game: who, score, putts, its video
//   GameLeaderboardScreen   leaderboard/GameLeaderboard a game's top 100, each row a Player's profile
//   ChallengesScreen        ChallengesScreen.js        Active | Upcoming | Completed
//   AchievementsScreen      profile/UserAchievements   every badge by kind
//   FeedScreen              main/FeedScreen.js         Following | Community games
//   MembershipScreen        MembershipScreen.js        a member's view (non-members get the Club paywall)
//   PuttersScreen, EditPutterScreen  profile/MyPutters, EditPutter
//   EditProfileScreen       profile/EditProfile.js     photo, names, home town, persona, side, handicap, gender, birthday, bio
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    static class Page
    {
        public static ScrollView Make(App app, string title, out VisualElement body)
        {
            body = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header(title, app.Pop));
            scroll.Add(body);
            scroll.Add(FreePuttScreen.Space(28));
            return scroll;
        }

        public static VisualElement Note(string text) => Ui.Box("pad", Ui.Box("card", Ui.Text(text, "body-2", Ui.Regular)));
        public static VisualElement Loading() => Ui.Box("pad", Spinner.Big()).Margin(12);

        /// <summary>FreePuttScreen.Choice (its lit option follows the tap).</summary>
        public static VisualElement Switch(string[] names, int chosen, Action<int> pick) => FreePuttScreen.Choice(names, chosen, pick);

        /// <summary>A tap on this child (a button on a tappable card) does not also tap the card under it.</summary>
        public static T Swallow<T>(T element) where T : VisualElement
        {
            element.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            element.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            element.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            return element;
        }

        /// <summary>The app's score badge: Tracks is to par (E, +2, -1); anything else its score, or a dash.</summary>
        public static string Score(object game)
        {
            double score = J.Num(game, "score");
            if (J.Str(game, "gameType") == "TRACKS") return score == 0 ? "E" : score > 0 ? "+" + score.ToString("0", CultureInfo.InvariantCulture) : score.ToString("0", CultureInfo.InvariantCulture);
            return score > 0 ? score.ToString("0.##", CultureInfo.InvariantCulture) : "–";
        }
    }

    // ── A played game ────────────────────────────────────────────────────────

    public sealed class GameViewScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _body;
        object _game, _user;
        List<object> _putts, _chips, _recordings;
        bool _autoReplayed;
        VisualElement _coach;

        public GameViewScreen(App app, string gameId)
        {
            _app = app;
            _id = gameId;
            Root = Page.Make(app, "Game", out _body);
            if (PuttCards.Sheet != null) Root.styleSheets.Add(PuttCards.Sheet);
            _body.Add(Page.Loading());
            App.Run(Api.Open("/club/analytics/game/" + gameId, data =>
            {
                List<object> rows = J.List(data);
                _game = rows.Count > 0 ? J.Get(rows[0], "game") : null;
                _user = rows.Count > 1 ? J.Get(rows[1], "user") : null;
                if (J.Str(_game, "gameCat") == "CHIPPING") App.Run(Api.Get("/profile/game/chips/" + gameId, d => { _chips = J.List(d); Draw(); }, _ => { _chips = new List<object>(); Draw(); }));
                Draw();
            }, _ => { _body.Clear(); _body.Add(Page.Note("Could not load this game.")); }));
            App.Run(Api.Get("/profile/game/putts/" + gameId, data => { _putts = J.List(data); Draw(); }, _ => { _putts = new List<object>(); Draw(); }));
            App.Run(Api.Get("/video/recordings/" + gameId, data => { _recordings = GameReplay.Segments(J.List(data)); Draw(); }));
        }

        void Draw()
        {
            if (_game == null || _putts == null) return;
            _body.Clear();
            string name = J.Str(_user, "displayName", "A Player");
            VisualElement who = Ui.Box("row gv-who", Ui.Avatar(name, J.Str(_user, "photoURL")), Ui.Box("grow", Ui.Text(name, "set-title", Ui.Bold), Ui.Text(J.Str(_user, "homeTown"), "set-sub", Ui.Regular)));
            string userId = J.Str(_user, "userId");
            who.OnTap(() => ProfileScreen.Open(_app, userId, _user));
            int made = 0;
            foreach (object p in _putts) if (J.Int(p, "made") == 1) made++;
            bool tracks = J.Str(_game, "gameType") == "TRACKS";
            // The game and who played it on one card; its three numbers as a stat strip under it.
            VisualElement head = Ui.Box("card gv-head",
                Ui.Title(J.Str(_game, "title", "Game"), "title-20 gv-title"),
                Ui.Text(Ago.Words(ProfileMath.Date(_game, "created_at"), DateTime.UtcNow), "body-2", Ui.Regular),
                who);
            VisualElement numbers = Ui.StatStrip(
                (Page.Score(_game), tracks ? "To par" : "Score"),
                (_putts.Count.ToString(), "Putts"),
                (_putts.Count > 0 ? Mathf.RoundToInt(made * 100f / _putts.Count) + "%" : "–", "Made"));
            numbers.AddToClassList("set-strip");
            _body.Add(Ui.Box("pad", head, numbers));

            if (J.Str(_game, "gameCat") == "CHIPPING") { if (_chips != null) Chipping.Build(_body, _chips); return; }

            // Session insights: average pace, push or pull, and the breaking putts (getPuttAssessment).
            var pace = new List<double>();
            foreach (object p in _putts)
            {
                if (J.Get(p, "rollout") != null) { pace.Add(J.Num(p, "rollout")); continue; }
                int[][] bp = PuttAssessment.Bp(p); double[] hb = PuttAssessment.Hb(p);
                if (bp.Length >= 2 && hb != null && AppPutt.ProjectRollout(AppPutt.AnalyzeRollout(bp, hb), J.Int(p, "made") == 0 ? false : (bool?)null) is double ro) pace.Add(ro);
            }
            if (_putts.Count > 0)
            {
                PuttAssessment a = PuttAssessment.Of(_putts, J.Str(_user, "handedness", J.Str(_app.Profile, "handedness")));
                VisualElement insights = Ui.Box("glass set-card");
                if (pace.Count > 0)
                {
                    double sum = 0; foreach (double v in pace) sum += v;
                    insights.Add(Ui.Box("set-row", Ui.Text("Avg pace", "gv-key", Ui.Regular), Ui.Text(AppPutt.FmtRollout(sum / pace.Count) ?? "no read", "gv-value", Ui.Bold), AdminLabels.Pill(a.SpeedConsistency, a.SpeedConsistency == "not consistent" ? Ui.Red : Ui.Bright)));
                }
                insights.Add(Ui.Box("set-row", Ui.Text("Tendency", "gv-key", Ui.Regular), Ui.Text(a.PushPull == "none" ? "No push/pull tendency" : a.PushPull + " (" + a.PushPullPercent + "%)", "gv-value", Ui.Bold)));
                if (a.High > 0 || a.Low > 0) insights.Add(Ui.Box("set-row", Ui.Text("Breaking putts", "gv-key", Ui.Regular), Ui.Text(a.High + " high side, " + a.Low + " low side", "gv-value", Ui.Bold)));
                insights[insights.childCount - 1].AddToClassList("set-row-last");
                _body.Add(Ui.Section("Session insights"));
                _body.Add(Ui.Box("pad", insights));
            }
            if (VertexSummary.Build(_putts) is VisualElement vertex) _body.Add(Ui.Box("pad", vertex));
            // The coach analysis row ("<coach> · Ready to analyze", GameView renderCoach): your own game only, never a Kid profile.
            // Made once (it reads the coach's settings), then moved into each redraw of the page.
            if (!Kid.IsKid(_app.Profile) && J.Str(_game, "userId", userId) == J.Str(_app.Profile, "userId"))
            {
                if (_coach == null) _coach = CoachPlan.GameRow(_app, J.Str(_game, "id", _id));
                if (_coach != null) _body.Add(Ui.Box("pad", _coach).Margin(12));
            }

            // Game replay is the page's one main button when there is a recording; Share is the secondary.
            VisualElement actions = Ui.Box("row gv-actions");
            // GameView shareGame: the system share sheet with the line and the game's link (off the phone, the clipboard).
            actions.Add(Ui.Button("Share game", () =>
            {
                if (!Share.Text("Check out the game I just played on Puttr!", "https://app.puttr.co/game/" + J.Str(_game, "id", _id), "Puttr Game")) _app.Toast("Link copied.");
            }, "btn btn-ghost"));
            bool replay = _recordings != null && _recordings.Count > 0;
            if (replay)
            {
                actions.Add(Ui.Box("gv-gap"));
                actions.Add(Ui.Button("Game replay", () => GameReplay.Open(Root, _recordings, _putts), "btn"));
                // Fixture runs only: PUTTR_FIXTURE_REPLAY_AT=seconds opens the replay by itself (for photographing the player),
                // on putt PUTTR_FIXTURE_REPLAY_PUTT (1 = the first) when that is set.
                if (Fixtures.On && !_autoReplayed && float.TryParse(Environment.GetEnvironmentVariable("PUTTR_FIXTURE_REPLAY_AT"), NumberStyles.Float, CultureInfo.InvariantCulture, out float at))
                {
                    _autoReplayed = true;
                    int.TryParse(Environment.GetEnvironmentVariable("PUTTR_FIXTURE_REPLAY_PUTT"), out int first);
                    Root.schedule.Execute(() => GameReplay.Open(Root, _recordings, _putts, Mathf.Max(0, first - 1))).ExecuteLater((long)(at * 1000));
                }
            }
            _body.Add(Ui.Box("pad", actions));

            if (_putts.Count == 0) return;
            if (MatSummary.HasMatPutts(_putts)) { double score = J.Num(_game, "score"); _body.Add(Ui.Box("pad", Ui.Button("Putt scatter chart", () => MatSummary.Show(Root, _putts, score), "btn btn-ghost"))); }
            _body.Add(Ui.Section("All putts"));
            // PuttCard for each putt: grade and score, distance and break, the pace bar, the entry. A tap opens its
            // ball path replayed on the green; with a recording, the detail also offers that putt's clip.
            VisualElement list = Ui.Box("pc-list");
            for (int i = 0; i < _putts.Count; i++)
            {
                int index = i;
                list.Add(PuttCards.Card(_putts[i], i, Root, null, false, replay ? () => GameReplay.Open(Root, _recordings, _putts, index) : (Action)null));
            }
            _body.Add(Ui.Box("pad", list));
        }

        /// <summary>A putt as PuttCard reads it, for its row here and its card in the replay's strip.</summary>
        internal struct PuttRead
        {
            public bool Made, Tracked;
            public string Result, Rollout;
            public double? Entry;
        }

        internal static PuttRead Read(object p)
        {
            bool m = J.Int(p, "made") == 1;
            int tee = J.Int(p, "tee", 2);
            int[][] bp = PuttAssessment.Bp(p); double[] hb = PuttAssessment.Hb(p);
            bool tracked = bp.Length >= 2 && hb != null;
            // Pace: the stored rollout, else projected from the ball's path.
            double? ro = J.Get(p, "rollout") != null ? J.Num(p, "rollout") : tracked ? AppPutt.ProjectRollout(AppPutt.AnalyzeRollout(bp, hb), m ? (bool?)null : false) : null;
            // Entry: where the ball crossed the lip against the tee's line (getHoleEntry - getTeeEntry).
            double? entry = null;
            if (m && tracked)
            {
                double at = AppPutt.HoleEntry(bp, hb, out bool found);
                if (found) entry = at - AppPutt.TeeEntry(J.Num(p, "distance"), tee);
            }
            return new PuttRead
            {
                Made = m,
                Tracked = tracked,
                Result = m ? "Made" : tracked ? "Missed " + AppPutt.MissResult(bp, hb).ToLowerInvariant() : "Missed",
                Rollout = ro.HasValue ? AppPutt.FmtRollout(ro.Value) : null,
                Entry = entry,
            };
        }

        /// <summary>"8° right · Right center", "Dead center · Center cut": the entry off the tee's line and its zone (getEntryDescription).</summary>
        internal static string EntryWords(double diff)
        {
            string zone = AppPutt.EntryDescription(diff);
            zone = zone.Substring(0, 1) + zone.Substring(1).ToLowerInvariant();
            int deg = (int)AppPutt.JsRound(Math.Abs(diff));
            return (deg == 0 ? "Dead center" : deg + "° " + (diff > 0 ? "right" : "left")) + " · " + zone;
        }
    }

    /// <summary>
    /// The cup from above, the ball coming up from the bottom: a green dot on the lip where a holed putt went in
    /// (0° the front, + to the right, 180° the back door), as PuttCard's entry glow. A miss is the empty cup, dimmed.
    /// </summary>
    sealed class GvHole : VisualElement
    {
        readonly bool _made;
        readonly double? _entry;

        public GvHole(bool made, double? entry)
        {
            _made = made;
            _entry = entry;
            AddToClassList("gv-hole");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0) return;
            Vector2 c = new Vector2(w * 0.5f, h * 0.5f);
            float r = Mathf.Min(w, h) * 0.33f;
            Painter2D p = context.painter2D;
            p.fillColor = Ui.Base;
            p.BeginPath(); p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.lineWidth = 1.5f;
            p.strokeColor = _made ? Ui.Faint : Ui.Line;
            p.BeginPath(); p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            if (!_entry.HasValue) return;
            float rad = (float)_entry.Value * Mathf.Deg2Rad;
            Vector2 lip = c + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * r;
            // The ball's last roll in, from just outside the lip.
            p.lineWidth = 2f;
            p.strokeColor = Ui.Green;
            p.BeginPath(); p.MoveTo(c + (lip - c) * 1.45f); p.LineTo(lip); p.Stroke();
            p.fillColor = Ui.Bright;
            p.BeginPath(); p.Arc(lip, 3.5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
        }
    }

    // ── Challenges ───────────────────────────────────────────────────────────

    public sealed class ChallengesScreen : AppScreen
    {
        static readonly string[] Tabs = { "Active", "Upcoming", "Completed" };
        readonly App _app;
        readonly VisualElement _body, _list;
        int _tab;

        public ChallengesScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Challenges", out _body);
            _list = new VisualElement();
            _body.Add(Ui.Box("pad", Page.Switch(Tabs, 0, i => { _tab = i; Load(); })));
            _body.Add(_list);
        }

        public override void Shown() => Load();

        void Load()
        {
            int asked = _tab;
            _list.Clear();
            _list.Add(Page.Loading());
            App.Run(Api.Get("/challenges/" + Tabs[_tab].ToUpperInvariant(), data => { if (asked == _tab) Draw(J.List(data)); }, _ => { _list.Clear(); _list.Add(Page.Note("Could not load challenges.")); }));
        }

        void Draw(List<object> rows)
        {
            _list.Clear();
            if (rows.Count == 0) { _list.Add(Page.Note(_tab == 0 ? "No active challenges right now." : _tab == 1 ? "Nothing coming up yet. Check back soon." : "No finished challenges yet.")); return; }
            DateTime now = DateTime.UtcNow;
            foreach (object row in rows)
            {
                object c = J.Get(row, "challenge");
                bool joined = Cards.IsJoined(c);
                List<object> levels = J.List(J.Get(row, "challengeLevels"));
                double score = J.Num(row, "userResults");
                DateTime? start = ProfileMath.Date(c, "startDate"), end = ProfileMath.Date(c, "endDate");
                string timing = _tab == 1 ? "Starts " + (start?.ToString("MMM d", CultureInfo.InvariantCulture) ?? "soon")
                    : _tab == 2 ? "Ended " + Ago.Words(end, now) : "Ends " + (end?.ToString("MMM d", CultureInfo.InvariantCulture) ?? "");
                int percent = ChallengeMath.Percent(score, levels, out int reached, out double goal);
                string pill = _tab == 2 ? (reached >= 0 ? J.Str(levels[reached], "title") + " earned" : joined ? "Played" : "Ended") : joined ? "Joined" : "Join";
                VisualElement card = Ui.Box("glass challenge-card",
                    Ui.Box("row", Ui.Box("grow", Ui.Text(J.Str(c, "title"), "set-title", Ui.Bold), Ui.Text(J.Str(c, "subTitle"), "set-sub", Ui.Regular)),
                        Ui.Text(pill, joined || reached >= 0 ? "chip chip-green" : "chip", Ui.Bold)),
                    Ui.Caps(J.Int(row, "participants") + " Players   ·   " + timing, "caps challenge-meta"));
                if (joined && levels.Count > 0 && _tab != 1)
                {
                    VisualElement bar = Ui.Progress(percent / 100f);
                    bar.AddToClassList("challenge-bar");
                    card.Add(bar);
                    card.Add(Ui.Caps(ProfileMath.Number(score) + " / " + ProfileMath.Number(goal) + " " + ChallengeMath.Unit(J.Str(c, "cType")) + "   ·   " + percent + "%", "caps challenge-meta"));
                }
                object tapped = row;
                _list.Add(Ui.Box("pad", card.OnTap(() => _app.Push(new ChallengeScreen(_app, tapped)))));
            }
        }
    }

    // ── Achievements ─────────────────────────────────────────────────────────

    public sealed class AchievementsScreen : AppScreen
    {
        static readonly (string type, string name)[] Kinds = { ("PUTTS", "Putts"), ("GAMES", "Games"), ("DISTANCE", "Distance"), ("DAYS", "Daily streak"), ("STREAK", "Hot streak"), ("QUESTS", "Quests") };

        public AchievementsScreen(App app)
        {
            Root = Page.Make(app, "Achievements", out VisualElement body);
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Profile");
            if (sheet != null) Root.styleSheets.Add(sheet);
            body.Add(Page.Loading());
            // Every achievement there is, by kind (GET /profile/achievements/: each with created_at once it is earned):
            // the ones earned in their kind's colour, the rest muted, so the page shows what is still to be had
            // (Matt, 2026-11-26: it listed only the ones earned).
            App.Run(Api.Get("/profile/achievements/", data =>
            {
                body.Clear();
                var all = new List<object>();
                foreach (object row in J.List(data))
                {
                    // By kind: {category, achievements}. A plain list of achievements is taken as it is, all earned.
                    if (J.Get(row, "achievements") != null) all.AddRange(J.List(J.Get(row, "achievements")));
                    else { if (J.Obj(row) != null && J.Get(row, "created_at") == null) J.Obj(row)["created_at"] = "earned"; all.Add(row); }
                }
                foreach (var kind in Kinds)
                {
                    var mine = all.FindAll(a => J.Str(a, "aType") == kind.type || (kind.type == "QUESTS" && J.Str(a, "aType") == "QUEST_STARS"));
                    mine.Sort((a, b) => J.Num(a, "number").CompareTo(J.Num(b, "number")));
                    // One of each (an achievement saved twice comes back twice), earned if any copy of it is.
                    var seen = new Dictionary<string, object>();
                    var each = new List<object>();
                    foreach (object a in mine)
                    {
                        string key = J.Str(a, "aType") + "|" + J.Str(a, "title") + "|" + J.Str(a, "number");
                        if (!seen.ContainsKey(key)) { seen[key] = a; each.Add(a); }
                        else if (J.Str(a, "created_at").Length > 0 && J.Str(seen[key], "created_at").Length == 0) { each[each.IndexOf(seen[key])] = a; seen[key] = a; }
                    }
                    mine = each;
                    if (mine.Count == 0) continue;
                    int got = mine.FindAll(a => J.Str(a, "created_at").Length > 0).Count;
                    // The Profile's coin shelf, one per kind, the coins wrapping onto as many rows as they need.
                    body.Add(Ui.Section(kind.name, got + " of " + mine.Count));
                    VisualElement grid = Ui.Box("pf-coin-grid");
                    foreach (object a in mine)
                    {
                        bool earned = J.Str(a, "created_at").Length > 0;
                        ProfileScreen.Look(J.Str(a, "aType"), out string icon, out Color tint);
                        var coin = new Coin(icon, tint, 0f, earned ? tint : new Color(0.36f, 0.39f, 0.42f));
                        if (earned) { coin.style.borderTopColor = coin.style.borderBottomColor = coin.style.borderLeftColor = coin.style.borderRightColor = tint; }
                        grid.Add(Ui.Box(earned ? "pf-coin-item" : "pf-coin-item pf-coin-off", coin, Ui.Text(J.Str(a, "title"), "pf-coin-title", Ui.Bold), Ui.Text(J.Str(a, "description"), "pf-coin-desc", Ui.Regular)));
                    }
                    // Flat crests on the page itself, as the Profile tab draws them: no shelf, no gradient.
                    body.Add(Ui.Box("pad", grid));
                }
            }));
        }
    }

    // ── What Players have been playing ───────────────────────────────────────

    public sealed class FeedScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _list;
        bool _community;

        public FeedScreen(App app, bool community)
        {
            _app = app;
            _community = community;
            Root = Page.Make(app, "Activity", out VisualElement body);
            if (PuttCards.Sheet != null) Root.styleSheets.Add(PuttCards.Sheet);
            _list = new VisualElement();
            if (Kid.Blocked(app, Kid.Social, body)) return;
            body.Add(Ui.Box("pad", Page.Switch(new[] { "Following", "Community" }, community ? 1 : 0, i => { _community = i == 1; Load(); })));
            body.Add(_list);
            Load();
        }

        void Load()
        {
            bool asked = _community;
            _list.Clear();
            _list.Add(Page.Loading());
            App.Run(Api.Get(_community ? "/profile/communityGames/" : "/profile/memberGames/", data => { if (asked == _community) Draw(J.List(data)); }));
        }

        void Draw(List<object> rows)
        {
            _list.Clear();
            rows = rows.FindAll(r => J.Str(r, "gameType") != "COACHSESSION");
            if (rows.Count == 0)
            {
                // FeedScreen's empty states: a bubble, a title and a line, and for Following the way to find Players.
                VisualElement empty = Ui.Box("fd-empty", Ui.Box("fd-empty-icon", Ui.Icon(_community ? "Planet" : "UsersThree", 28, Ui.Faint)),
                    Ui.Text(_community ? "No games yet" : "No activity yet", "fd-empty-title", Ui.Bold),
                    Ui.Text(_community ? "Community games will appear here as Players complete sessions." : "Follow other Players to see their games in your feed.", "fd-empty-text", Ui.Regular));
                if (!_community) empty.Add(Ui.Button("Find Players", () => _app.Push(new FindPlayersScreen(_app)), "btn fd-empty-btn"));
                _list.Add(empty);
                return;
            }
            // The slim find-friends row over the friends feed.
            if (!_community)
                _list.Add(Ui.Box("pad", Ui.Box("fd-find", Ui.Icon("UserPlus", 18, Ui.Bright), Ui.Text("Find new friends", "fd-find-t grow", Ui.Medium), Ui.Icon("CaretRight", 14, Ui.Faint))
                    .OnTap(() => _app.Push(new FindPlayersScreen(_app)))));
            VisualElement grid = Ui.Box("fd-grid");
            foreach (object row in rows) grid.Add(Card(row));
            _list.Add(Ui.Box("pad", grid));
        }

        /// <summary>
        /// One activity, as FeedScreen's card: who (avatar, name, how long ago) with the score in its green box, then the
        /// game as GameCard has it (title, the three-bar difficulty, the description, Solo or Players, putts, feet and the
        /// round Play button). The card opens the played game; Play opens that game's page.
        /// </summary>
        VisualElement Card(object row)
        {
            string name = J.Str(row, "displayName", "A Player"), gameId = J.Str(row, "gameId");
            VisualElement head = Ui.Box("fd-head", Ui.Avatar(name, J.Str(row, "photoURL")),
                Ui.Box("grow fd-who", Ui.Text(name, "fd-name", Ui.Bold), Ui.Text(Ago.Words(ProfileMath.Date(row, "updated_at"), DateTime.UtcNow), "fd-ago", Ui.Regular)),
                Ui.Box("fd-score", Ui.Text(Badge(row), "fd-score-t", Ui.Bold)));

            VisualElement title = Ui.Box("fd-title-row", Ui.Text(J.Str(row, "title", "Game"), "fd-title grow", Ui.Bold), Meter(row));
            VisualElement game = Ui.Box("fd-game", title);
            string about = J.Str(row, "description");
            if (about.Length > 0) game.Add(Ui.Text(about, "fd-desc", Ui.Regular));

            int m = J.Int(row, "multiPlayer");
            VisualElement stats = Ui.Box("fd-stats",
                Ui.Box("fd-players grow", Ui.Icon(m >= 1 ? "UsersThree" : "User", 14, Ui.Faint), Ui.Text(m == 1 ? "2 players" : m == 2 ? "2+ players" : m == 3 ? "1+ players" : "Solo", "fd-players-t", Ui.Medium)),
                Stat(J.Int(row, "putts"), "putts"), Stat(J.Int(row, "distance"), "feet"));
            if (J.Str(row, "gameType").Length > 0)
            {
                VisualElement play = Ui.Box("fd-play", Ui.Icon("Play", 14, Color.white)).OnTap(() => Play(row));
                Page.Swallow(play);
                stats.Add(play);
            }
            return Ui.Box("glass fd-card", head, game, stats).OnTap(() => _app.Push(new GameViewScreen(_app, gameId)));
        }

        // The row carries the game's own columns (games.*), so it opens the game's page as a catalog card would.
        void Play(object row)
        {
            if (J.Str(row, "gameType") == "TRACKS") _app.Push(new TracksCoursesScreen(_app));
            else _app.Push(new GameScreen(_app, row));
        }

        static VisualElement Stat(int value, string label) =>
            Ui.Box("fd-stat", Ui.Text(value == 0 ? "∞" : value.ToString(), "fd-stat-v", Ui.Bold), Ui.Text(label, "fd-stat-l", Ui.Regular));

        /// <summary>GameCard's difficulty: one bar lit up to 3, two up to 6, three above, in green, amber or red.</summary>
        static VisualElement Meter(object game)
        {
            int lit = GameLevels.Bars(J.Num(game, "difficulty"));
            Color colour = lit == 1 ? Ui.Green : lit == 2 ? Ui.Amber : Ui.Red;
            VisualElement meter = Ui.Box("fd-meter");
            for (int i = 1; i <= 3; i++)
            {
                VisualElement bar = Ui.Box("fd-meter-bar");
                bar.style.backgroundColor = i <= lit ? colour : Ui.Surface2;
                meter.Add(bar);
            }
            return meter;
        }

        /// <summary>activityScore.js scoreBadge: the score as stored ("12", "17.58", "0"); Tracks to par ("E", "+3", "-2").</summary>
        public static string Badge(object row)
        {
            double s = J.Num(row, "score");
            if (J.Str(row, "gameType") == "TRACKS") return s == 0 ? "E" : s > 0 ? "+" + s.ToString("0", CultureInfo.InvariantCulture) : s.ToString("0", CultureInfo.InvariantCulture);
            return J.Get(row, "score") == null ? "" : s.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // utils/activityScore.js scorePhrase.
        public static string Phrase(object row)
        {
            if (J.Str(row, "gameType") != "TRACKS") return "scored " + Page.Score(row);
            double s = J.Num(row, "score");
            return s == 0 ? "shot even" : "shot " + Math.Abs(s).ToString("0", CultureInfo.InvariantCulture) + (s < 0 ? " under" : " over");
        }
    }

    // ── Membership ───────────────────────────────────────────────────────────

    /// <summary>A member's membership page: in the club, and the top members. Anyone else gets the Club paywall.</summary>
    public sealed class MembershipScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _list;
        int _tab;

        /// <summary>Opens the membership page, or for a non-member who can join, the Club plans directly (no page with just a button).</summary>
        public static void Open(App app)
        {
            if (!Club.Member(app.Profile) && string.IsNullOrEmpty(J.Str(app.Profile, "managedBy"))) app.Push(new ClubScreen(app, null));
            else app.Push(new MembershipScreen(app));
        }

        public MembershipScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Puttr Club", out VisualElement body);
            _list = new VisualElement();
            string owner = J.Str(app.Profile, "familyOwnerName");
            if (!string.IsNullOrEmpty(J.Str(app.Profile, "managedBy")))
            {
                body.Add(Page.Note(Club.Member(app.Profile)
                    ? "You're in the Puttr Club. Included with " + (owner.Length > 0 ? owner + "'s" : "your family's") + " membership."
                    : "Puttr Club is not active. Ask " + (owner.Length > 0 ? owner : "the account owner") + " about joining."));
                return;
            }
            if (!Club.Member(app.Profile)) { body.Add(Ui.Box("pad", Ui.Button("See Puttr Club", () => { app.Pop(); app.Push(new ClubScreen(app, null)); }))); return; }
            body.Add(Ui.Box("pad", Ui.Box("card club-in", Ui.Title("You're in the club!", "title-20 club-title"), Ui.Text(ProfileMath.Membership(app.Profile, DateTime.UtcNow), "body-2", Ui.Regular))));
            body.Add(Ui.Section("Top members"));
            body.Add(Ui.Box("pad", Page.Switch(new[] { "Putts", "Handicap" }, 0, i => { _tab = i; Load(); })));
            body.Add(_list);
            Load();
        }

        void Load()
        {
            int asked = _tab;
            _list.Clear();
            _list.Add(Page.Loading());
            App.Run(Api.Get(_tab == 0 ? "/members/best/putts/" : "/members/best/handicap/", data =>
            {
                if (asked != _tab) return;
                _list.Clear();
                VisualElement card = Ui.Box("glass set-card");
                List<object> rows = J.List(data);
                for (int i = 0; i < rows.Count; i++)
                {
                    object row = rows[i];
                    string name = J.Str(row, "displayName", "A Player"), userId = J.Str(row, "userId");
                    string value = _tab == 0 ? ProfileMath.Number(J.Num(row, "totalPutts", J.Num(row, "putts"))) : ProfileMath.Handicap(row);
                    card.Add(Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold), Ui.Avatar(name, J.Str(row, "photoURL")),
                        Ui.Box("grow", Ui.Text(name, "set-title", Ui.Bold), Ui.Text(J.Str(row, "homeTown"), "set-sub", Ui.Regular)), Ui.Num(value, "num-20 set-num"))
                        .OnTap(() => ProfileScreen.Open(_app, userId, row)));
                }
                _list.Add(Ui.Box("pad", card));
            }));
        }
    }

    // ── Putters ──────────────────────────────────────────────────────────────

    public sealed class PuttersScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _list;

        public PuttersScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "My putters", out VisualElement body);
            _list = new VisualElement();
            body.Add(_list);
            body.Add(Ui.Box("pad", Ui.Button("Add a putter", () => app.Push(new EditPutterScreen(app, null)))));
        }

        public override void Shown() => App.Run(Api.Get("/user-putters", data => Draw(J.List(data))));

        void Draw(List<object> rows)
        {
            _list.Clear();
            if (rows.Count == 0) { _list.Add(Page.Note("No putters yet. Add the one you putt with.")); return; }
            VisualElement card = Ui.Box("glass set-card");
            foreach (object row in rows)
            {
                object putter = row;
                bool primary = J.Int(row, "isPrimary") == 1;
                VisualElement words = Ui.Box("grow set-words", Ui.Text(Clean(J.Str(row, "brand")), "set-title", Ui.Bold), Ui.Text(Clean(J.Str(row, "model")), "set-sub", Ui.Regular));
                string detail = ProfileMath.PutterDetail(row);
                if (detail.Length > 0) words.Add(Ui.Text(detail, "set-sub", Ui.Regular));
                VisualElement right = primary ? Ui.Text("Your gamer", "chip chip-green", Ui.Bold)
                    : Ui.Box("follow", Ui.Text("SET AS GAMER", "follow-text", Ui.Bold)).OnTap(() => { Haptics.Medium(); App.Run(Api.Do("PATCH", "/user-putters/" + J.Str(putter, "id") + "/primary", null, _ => Shown(), _ => Shown())); });
                card.Add(Ui.Box("set-row", Ui.Icon("Target", 20, Ui.Faint), words, right).OnTap(() => _app.Push(new EditPutterScreen(_app, putter))));
            }
            card[card.childCount - 1].AddToClassList("set-row-last");
            _list.Add(Ui.Box("pad", card));
        }

        internal static string Clean(string s) => s == null || s == "null" ? "" : s;
    }

    public sealed class EditPutterScreen : AppScreen
    {
        // components/common/putterBrands.js
        static readonly string[] Brands = { "Scotty Cameron", "L.A.B. Golf", "TaylorMade", "Odyssey", "PING", "Bettinardi", "PXG", "EVNROLL", "Cleveland", "Cobra", "Edel", "LA GOLF", "Mizuno", "Performance Golf", "SeeMore", "Sik Golf", "Sub 70", "Swag", "Top Flite", "US Kids Golf", "Vice", "Wilson", "Other" };
        static readonly string[] Lengths = { "30", "31", "32", "33", "34", "35", "36", "37", "38", "39", "40" };
        static readonly string[] Heads = { "Blade", "Mallet", "Mid-Mallet" };
        static readonly string[] Shafts = { "Center Shaft", "Double Bend", "Flow Neck", "Plumbers Neck", "Slant Neck" };
        static readonly string[] Grips = { "Arm Lock", "Broomstick", "Claw", "Cross Handed", "Palm to Palm", "Pencil / Saw", "Reverse Overlap", "Split Grip", "Standard" };

        readonly App _app;
        readonly object _putter;
        readonly VisualElement _body;
        readonly TextField _brandText, _model;
        string _brand, _length, _head, _shaft, _grip;
        bool _primary, _busy;

        public EditPutterScreen(App app, object putter)
        {
            _app = app;
            _putter = putter;
            Root = Page.Make(app, putter == null ? "Add a putter" : "Edit putter", out _body);
            _brand = PuttersScreen.Clean(J.Str(putter, "brand"));
            _length = PuttersScreen.Clean(J.Str(putter, "length"));
            _head = PuttersScreen.Clean(J.Str(putter, "head"));
            _shaft = PuttersScreen.Clean(J.Str(putter, "shaft"));
            _grip = PuttersScreen.Clean(J.Str(putter, "grip"));
            _primary = putter == null || J.Int(putter, "isPrimary") == 1;
            _brandText = Field("Brand", _brand);
            _model = Field("Model", PuttersScreen.Clean(J.Str(putter, "model")));
            Draw();
        }

        static TextField Field(string placeholder, string value)
        {
            var f = new TextField { value = value };
            f.textEdition.placeholder = placeholder;
            f.textEdition.hidePlaceholderOnFocus = false;
            f.AddToClassList("field");
            return f.Face(Ui.Medium);
        }

        VisualElement Chips(string title, string[] options, string chosen, Action<string> pick, string suffix = "")
        {
            ScrollView row = Ui.Scroll(true);
            foreach (string o in options)
            {
                string option = o;
                row.Add(Ui.Box(o == chosen ? "scope scope-on" : "scope", Ui.Text(o + suffix, "scope-text", Ui.Bold)).OnTap(() => { pick(chosen == option ? null : option); Draw(); }));
            }
            return Ui.Box("edit-group", Ui.Text(title.ToUpperInvariant(), "caps caps-green", Ui.Bold), row.Margin(8));
        }

        void Draw()
        {
            _body.Clear();
            VisualElement form = Ui.Box("pad");
            bool listed = Array.IndexOf(Brands, _brand) >= 0 && _brand != "Other";
            form.Add(Chips("Brand", Brands, listed ? _brand : string.IsNullOrEmpty(_brand) ? null : "Other", b => { _brand = b; if (b != "Other") _brandText.value = b ?? ""; else _brandText.value = ""; }));
            if (_brand == "Other" || (!listed && !string.IsNullOrEmpty(_brand))) form.Add(_brandText.Margin(8));
            form.Add(_model.Margin(12));
            form.Add(Chips("Length", Lengths, _length, v => _length = v, "\""));
            form.Add(Chips("Head", Heads, _head, v => _head = v));
            form.Add(Chips("Shaft", Shafts, _shaft, v => _shaft = v));
            form.Add(Chips("Grip", Grips, _grip, v => _grip = v));
            bool lockedPrimary = _putter != null && J.Int(_putter, "isPrimary") == 1;
            form.Add(Ui.Box("set-row", Ui.Box("grow set-words", Ui.Text("Set as your gamer", "set-title", Ui.Bold)), lockedPrimary ? Ui.Text("Your gamer", "chip chip-green", Ui.Bold) : Rows.Switch(_primary, v => _primary = v)));
            form.Add(Ui.Button(_putter == null ? "Add putter" : "Save changes", Save).Margin(16));
            if (_putter != null) form.Add(Ui.Button("Remove putter", Remove, "btn btn-red").Margin(12));
            _body.Add(form);
        }

        void Save()
        {
            string brand = _brand == "Other" || Array.IndexOf(Brands, _brand) < 0 ? _brandText.value.Trim() : _brand;
            if (string.IsNullOrEmpty(brand)) { _app.Toast("Pick a brand."); return; }
            if (_busy) return;
            _busy = true;
            object N(string v) => string.IsNullOrEmpty(v) ? null : v;
            var body = new Dictionary<string, object> { { "brand", brand }, { "model", N(_model.value.Trim()) }, { "length", N(_length) }, { "head", N(_head) }, { "shaft", N(_shaft) }, { "grip", N(_grip) }, { "isPrimary", _primary } };
            App.Run(Api.Do(_putter == null ? "POST" : "PUT", _putter == null ? "/user-putters" : "/user-putters/" + J.Str(_putter, "id"), body,
                _ => { _busy = false; _app.Pop(); }, _ => { _busy = false; _app.Toast("Could not save. Try again."); }));
        }

        void Remove() => Dialog.Ask(Root, "Remove this putter?", "It comes off your profile.", "Remove", () =>
            App.Run(Api.Do("DELETE", "/user-putters/" + J.Str(_putter, "id"), null, _ => _app.Pop(), _ => _app.Toast("Could not remove it. Try again."))), true);
    }

    // ── Edit profile ─────────────────────────────────────────────────────────

    public sealed class EditProfileScreen : AppScreen
    {
        static readonly string[] Handicaps = BuildHandicaps();
        // UserPersona.js: what the Player wants from Puttr.
        static readonly (string title, string desc, string icon)[] Personas =
        {
            ("GRINDER", "Improve my putting and compete", "Target"),
            ("GAME NIGHTER", "Play with my friends and family", "UsersThree"),
            ("E-SPORTER", "Play online against the Puttr community", "Trophy"),
        };
        // EditProfile.js GENDER_OPTIONS: the label shown and the value stored.
        static readonly (string label, string value)[] Genders = { ("Male", "M"), ("Female", "F"), ("Non-Binary", "APP") };
        readonly App _app;
        readonly VisualElement _body;
        readonly TextField _display, _first, _last, _town, _bio, _web;
        readonly Label _error;
        string _hand, _handicap, _persona, _gender;
        // The birthday as month, day and year steppers (no native date picker here); sent only once it is changed,
        // as EditProfile sends birthDate only when the picker was used.
        int _bMonth = 1, _bDay = 1, _bYear = 1990;
        bool _birthSet, _birthChanged, _genderChanged;
        bool _busy, _photoBusy;
        static bool _cityOpened;
        object _lat, _lng;

        static string[] BuildHandicaps()
        {
            var list = new List<string> { "NONE", "PRO", "+5", "+4", "+3", "+2", "+1" };
            for (int i = 0; i <= 36; i++) list.Add(i.ToString());
            return list.ToArray();
        }

        public EditProfileScreen(App app)
        {
            _app = app;
            object p = app.Profile;
            Root = Page.Make(app, "Edit profile", out _body);
            if (PuttCards.Sheet != null) Root.styleSheets.Add(PuttCards.Sheet);
            if (Kid.Blocked(app, Kid.Social, _body)) return;
            _display = Field("Display name", J.Str(p, "displayName"));
            _first = Field("First name", J.Str(p, "firstName"));
            _last = Field("Last name", J.Str(p, "lastName"));
            _town = Field("Home town", J.Str(p, "homeTown"));
            _lat = J.Get(p, "lat");
            _lng = J.Get(p, "lng");
            _bio = Field("Bio", J.Str(p, "bio"));
            _bio.multiline = true;
            _bio.maxLength = 150;
            _web = Field("Website", J.Str(p, "webLink"));
            _hand = J.Str(p, "handedness", "RIGHTY");
            _handicap = J.Str(p, "realHandicap");
            _persona = J.Str(p, "persona");
            _gender = J.Str(p, "gender");
            // The stored birthDate is a calendar day (the app shows it as UTC), so its date part is the day.
            string born = J.Str(p, "birthDate");
            if (born.Length >= 10 && DateTime.TryParseExact(born.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime b))
            {
                _bMonth = b.Month; _bDay = b.Day; _bYear = b.Year; _birthSet = true;
            }
            _error = Ui.Text("", "body-2", Ui.Medium);
            _error.style.color = Ui.Red;
            if (Kid.IsKid(p)) return;
            Draw();
            // -app2CityQuery "Austin": straight on to the home town search (for checking it from a script).
            if (!_cityOpened && Array.IndexOf(Environment.GetCommandLineArgs(), "-app2CityQuery") >= 0)
                _body.schedule.Execute(() => { if (_cityOpened) return; _cityOpened = true; _app.Push(new CityScreen(_app, _town.value, (place, lat, lng) => { _town.value = place; _lat = lat; _lng = lng; Draw(); })); }).StartingIn(400);
        }

        static TextField Field(string label, string value)
        {
            var f = new TextField { value = value == "null" ? "" : value };
            f.textEdition.placeholder = label;
            f.textEdition.hidePlaceholderOnFocus = false;
            f.AddToClassList("field");
            return f.Face(Ui.Medium);
        }

        static VisualElement Named(string label, TextField field) => Ui.Box("", Sub(label), field);

        /// <summary>A field's name over it: a grey caps label (the group's own name is the green one).</summary>
        static Label Sub(string label) => Ui.Caps(label, "caps edit-label");

        static VisualElement Group(string title, params VisualElement[] parts)
        {
            VisualElement g = Ui.Box("edit-group", Ui.Text(title.ToUpperInvariant(), "caps caps-green", Ui.Bold));
            foreach (VisualElement part in parts) g.Add(part);
            return g;
        }

        void Draw()
        {
            _body.Clear();
            ScrollView handicaps = Ui.Scroll(true);
            foreach (string h in Handicaps)
            {
                string value = h;
                handicaps.Add(Ui.Box(h == _handicap ? "scope scope-on" : "scope", Ui.Text(h == "NONE" ? "None" : h == "PRO" ? "Pro" : h, "scope-text", Ui.Bold)).OnTap(() => { _handicap = value; Draw(); }));
            }
            // The photo (ProfilePhotoPicker): the library, then saved at once.
            string name = J.Str(_app.Profile, "displayName", "?");
            // Aspect-filled (scale-and-crop), never stretched to the circle.
            VisualElement face = Images.Cover(Ui.Avatar(name, J.Str(_app.Profile, "photoURL"), true));
            VisualElement badge = Ui.Box("photo-badge", Ui.Icon("VideoCamera", 14, Color.white));
            VisualElement photo = Ui.Box("photo-pick", face, badge);
            if (PhotoPicker.Supported) photo.OnTap(() =>
            {
                if (_photoBusy) return;
                // Once a photo is chosen: the Puttr loader over the dimmed avatar until the new one is here to show.
                VisualElement spin = null;
                void Busy(bool on)
                {
                    _photoBusy = on;
                    if (on) { face.AddToClassList("photo-busy"); badge.AddToClassList("photo-busy"); spin = Spinner.Small(); spin.style.position = Position.Absolute; photo.Add(spin); }
                    else { face.RemoveFromClassList("photo-busy"); badge.RemoveFromClassList("photo-busy"); spin?.RemoveFromHierarchy(); }
                }
                PhotoPicker.ChangeProfilePhoto(_app, ok =>
                {
                    if (!ok) { Busy(false); return; }
                    string fresh = J.Str(_app.Profile, "photoURL");
                    // The new picture downloaded first, so the redrawn avatar shows it at once (no letter flash).
                    if (fresh.StartsWith("http")) Images.Load(fresh, _ => { _photoBusy = false; Draw(); });
                    else { _photoBusy = false; Draw(); }
                }, () => Busy(true));
            });
            // Home town: Google's city search (SetUserLocation); typed here only off the phone.
            VisualElement town = Rows.Row("Target", _town.value.Length > 0 ? _town.value : "Set your home town", () => _app.Push(new CityScreen(_app, _town.value, (place, lat, lng) => { _town.value = place; _lat = lat; _lng = lng; Draw(); })));
            _body.Add(Ui.Box("pad center-box", photo, Ui.Text(PhotoPicker.Supported ? "Tap to change your photo" : "Change your photo in the phone app", "body-2 edit-photo-note", Ui.Regular)));
            // Persona (UserPersona.js): the three kinds of Player, the chosen one lit.
            VisualElement personas = Ui.Box("ep-personas");
            foreach (var option in Personas)
            {
                string value = option.title;
                bool on = value == _persona;
                VisualElement bubble = Ui.Box("edit-persona-icon", Ui.Icon(option.icon, 24, on ? Ui.Bright : Color.white));
                personas.Add(Ui.Box(on ? "ep-persona ep-persona-on" : "ep-persona", bubble,
                    Ui.Box("grow", Ui.Text(value, "ep-persona-title", Ui.Bold), Ui.Text(option.desc, "ep-persona-desc", Ui.Regular)),
                    on ? Ui.Icon("CheckCircle", 18, Ui.Bright) : null).OnTap(() => { _persona = value; Draw(); }));
            }
            // Gender: Male, Female or Non-Binary (nothing lit until one is chosen or stored).
            int genderAt = Array.FindIndex(Genders, g => g.value == _gender);
            VisualElement gender = FreePuttScreen.Choice(Array.ConvertAll(Genders, g => g.label), genderAt, i => { _gender = Genders[i].value; _genderChanged = true; Draw(); });
            // Birthday: month, day and year steppers (as the Junior application's), with the date it makes under them.
            string[] months = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;
            Label birthday = Ui.Text(BirthWords(), "body-2", Ui.Regular);
            void Touched() { _birthSet = true; _birthChanged = true; birthday.text = BirthWords(); }
            VisualElement birth = Ui.Box("row ep-birth",
                FreePuttScreen.Stepper(() => months[_bMonth - 1], d => { _bMonth = (_bMonth - 1 + d + 12) % 12 + 1; Touched(); }),
                FreePuttScreen.Stepper(() => _bDay.ToString(), d => { _bDay = (_bDay - 1 + d + 31) % 31 + 1; Touched(); }),
                FreePuttScreen.Stepper(() => _bYear.ToString(), d => { _bYear = Mathf.Clamp(_bYear + d, 1920, DateTime.Today.Year); Touched(); }));
            _body.Add(Ui.Box("pad",
                Group("Personal info", Named("Display name", _display), Named("First name", _first), Named("Last name", _last), Sub("Home town"), town),
                Group("Golf", Sub("Persona"), personas,
                    Sub("Your side"), Page.Switch(new[] { "Righty", "Lefty" }, _hand == "LEFTY" ? 1 : 0, i => _hand = i == 1 ? "LEFTY" : "RIGHTY"),
                    Sub("Handicap"), handicaps,
                    Rows.Row("Target", "My putters", () => _app.Push(new PuttersScreen(_app)))),
                Group("More", Sub("Gender"), gender,
                    Sub("Birthday"), birth, birthday.Margin(4),
                    Named("Bio", _bio), Named("Website", _web)),
                _error.Margin(8),
                Ui.Button("Save", Save).Margin(16)));
        }

        DateTime Birth => new DateTime(_bYear, _bMonth, Mathf.Min(_bDay, DateTime.DaysInMonth(_bYear, _bMonth)));

        string BirthWords() => _birthSet ? Birth.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : "Not set";

        void Save()
        {
            string name = _display.value.Trim();
            // The rule onboarding applies (OnboardingGuide: letters, numbers and spaces; the API checks nothing).
            // A name left as it was is not held to it: names from Google or Apple sign-in were never checked,
            // and their owners must still be able to save the rest of the page.
            if (name.Length == 0) { _error.text = "Enter a display name."; return; }
            if (name != J.Str(_app.Profile, "displayName").Trim() && !Regex.IsMatch(name, "^[a-zA-Z0-9 ]+$")) { _error.text = "Display name can only have letters, numbers and spaces."; return; }
            if (_busy) return;
            _busy = true;
            _error.text = "";
            string me = J.Str(_app.Profile, "userId");
            App.Run(Api.Open("/profile/displayName/" + UnityEngine.Networking.UnityWebRequest.EscapeURL(name), data =>
            {
                object taken = J.First(data);
                if (taken != null && J.Str(taken, "userId") != me) { _busy = false; _error.text = "Display name already taken."; return; }
                object p = _app.Profile;
                // birthDate and gender go only once changed here ("yyyy-MM-dd" and M, F or APP), as EditProfile sends them;
                // otherwise false, and /profile/save only writes a field whose value is truthy, so false leaves it as it is.
                var body = new Dictionary<string, object>
                {
                    { "displayName", name }, { "homeTown", _town.value.Trim() }, { "lat", _lat }, { "lng", _lng },
                    { "firstName", _first.value.Trim() }, { "lastName", _last.value.Trim() },
                    { "birthDate", _birthChanged ? (object)Birth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : false },
                    { "gender", _genderChanged && !string.IsNullOrEmpty(_gender) ? (object)_gender : false },
                    { "realHandicap", _handicap }, { "handedness", _hand }, { "persona", string.IsNullOrEmpty(_persona) ? J.Get(p, "persona") : _persona },
                    { "ownsPuttr", J.Int(p, "ownsPuttr") == 1 ? "YES" : "NO" }, { "bio", _bio.value }, { "webLink", _web.value.Trim() },
                };
                App.Run(Api.Post("/profile/save", body, _ =>
                    App.Run(Api.Get("/profile/", fresh => { _busy = false; _app.Profile = J.First(fresh); _app.Toast("Profile saved."); _app.Pop(); }, __ => { _busy = false; _app.Pop(); })),
                    _ => { _busy = false; _error.text = "Could not save. Check your connection and try again."; }));
            }, _ => { _busy = false; _error.text = "Could not check that name. Try again."; }));
        }
    }
}
