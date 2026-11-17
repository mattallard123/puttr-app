// The screens most taps in puttr-app lead to, for navigation parity (docs/screen-parity.md):
//
//   GameViewScreen          profile/GameView.js        a played game: who, score, putts, its video
//   GameLeaderboardScreen   leaderboard/GameLeaderboard a game's top 100, each row a Player's profile
//   ChallengesScreen        ChallengesScreen.js        Active | Upcoming | Completed
//   AchievementsScreen      profile/UserAchievements   every badge by kind
//   FeedScreen              main/FeedScreen.js         Following | Community games
//   MembershipScreen        MembershipScreen.js        a member's view (non-members get the Club paywall)
//   PuttersScreen, EditPutterScreen  profile/MyPutters, EditPutter
//   EditProfileScreen       profile/EditProfile.js     (no photo upload or city search yet)
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

        public static VisualElement Note(string text) => Ui.Box("pad", Ui.Box("glass", Ui.Text(text, "sub", Ui.Regular)));
        public static VisualElement Loading() => Ui.Box("pad", Spinner.Big()).Margin(10);

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
        List<object> _putts, _chips;
        string _video;
        bool _autoReplayed;

        public GameViewScreen(App app, string gameId)
        {
            _app = app;
            _id = gameId;
            Root = Page.Make(app, "Game", out _body);
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
            App.Run(Api.Get("/video/recordings/" + gameId, data => { _video = J.Str(J.First(data), "videoUrl"); Draw(); }));
        }

        void Draw()
        {
            if (_game == null || _putts == null) return;
            _body.Clear();
            string name = J.Str(_user, "displayName", "A Player");
            VisualElement who = Ui.Box("row", Ui.Avatar(name, J.Str(_user, "photoURL")), Ui.Box("grow", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text(J.Str(_user, "homeTown"), "muted", Ui.Regular)).Margin(0));
            string userId = J.Str(_user, "userId");
            who.OnTap(() => ProfileScreen.Open(_app, userId, _user));
            int made = 0;
            foreach (object p in _putts) if (J.Int(p, "made") == 1) made++;
            bool tracks = J.Str(_game, "gameType") == "TRACKS";
            VisualElement head = Ui.Box("glass",
                Ui.Text(J.Str(_game, "title", "Game"), "h2", Ui.Bold),
                Ui.Text(Ago.Words(ProfileMath.Date(_game, "created_at"), DateTime.UtcNow), "muted", Ui.Regular),
                who.Margin(12),
                Ui.Box("row",
                    FreePuttScreen.Stat(Page.Score(_game), tracks ? "To par" : "Score"),
                    FreePuttScreen.Stat(_putts.Count.ToString(), "Putts"),
                    FreePuttScreen.Stat(_putts.Count > 0 ? Mathf.RoundToInt(made * 100f / _putts.Count) + "%" : "–", "Made")).Margin(14));
            _body.Add(Ui.Box("pad", head));

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
                    insights.Add(Ui.Box("set-row", Ui.Text("Avg pace", "sub grow", Ui.Regular), Ui.Text(AppPutt.FmtRollout(sum / pace.Count) ?? "no read", "feed-name", Ui.Bold).Margin(0), AdminLabels.Pill(a.SpeedConsistency, a.SpeedConsistency == "not consistent" ? Ui.Red : Ui.Bright)));
                }
                insights.Add(Ui.Box("set-row", Ui.Text("Tendency", "sub grow", Ui.Regular), Ui.Text(a.PushPull == "none" ? "No push/pull tendency" : a.PushPull + " (" + a.PushPullPercent + "%)", "feed-name", Ui.Bold)));
                if (a.High > 0 || a.Low > 0) insights.Add(Ui.Box("set-row", Ui.Text("Breaking putts", "sub grow", Ui.Regular), Ui.Text(a.High + " high side, " + a.Low + " low side", "feed-name", Ui.Bold)));
                _body.Add(Ui.Section("Session insights"));
                _body.Add(Ui.Box("pad", insights));
            }
            if (VertexSummary.Build(_putts) is VisualElement vertex) _body.Add(Ui.Box("pad", vertex));

            VisualElement actions = Ui.Box("row");
            actions.Add(Ui.Button("Share game", () => { GUIUtility.systemCopyBuffer = "https://app.puttr.co/game/" + J.Str(_game, "id", _id); _app.Toast("Link copied."); }, "btn btn-ghost grow"));
            if (!string.IsNullOrEmpty(_video) && _video.StartsWith("http"))
            {
                actions.Add(FreePuttScreen.Space(0).Width(10));
                string url = _video;
                actions.Add(Ui.Button("Game replay", () => SetupGuide.Play(Root, url), "btn grow"));
                // Fixture runs only: PUTTR_FIXTURE_REPLAY_AT=seconds opens the replay by itself (for photographing the player).
                if (Fixtures.On && !_autoReplayed && float.TryParse(Environment.GetEnvironmentVariable("PUTTR_FIXTURE_REPLAY_AT"), NumberStyles.Float, CultureInfo.InvariantCulture, out float at))
                { _autoReplayed = true; Root.schedule.Execute(() => SetupGuide.Play(Root, url)).ExecuteLater((long)(at * 1000)); }
            }
            _body.Add(Ui.Box("pad", actions).Margin(12));

            if (_putts.Count == 0) return;
            if (MatSummary.HasMatPutts(_putts)) { double score = J.Num(_game, "score"); _body.Add(Ui.Box("pad", Ui.Button("Putt scatter chart", () => MatSummary.Show(Root, _putts, score), "btn btn-ghost"))); }
            _body.Add(Ui.Section("All putts"));
            VisualElement list = Ui.Box("glass set-card");
            for (int i = 0; i < _putts.Count; i++)
            {
                object p = _putts[i];
                bool m = J.Int(p, "made") == 1;
                int tee = J.Int(p, "tee", 2);
                int[][] bp = PuttAssessment.Bp(p); double[] hb = PuttAssessment.Hb(p);
                bool tracked = bp.Length >= 2 && hb != null;
                // Pace as PuttCard reads it: the stored rollout, else projected from the ball's path.
                double? ro = J.Get(p, "rollout") != null ? J.Num(p, "rollout") : tracked ? AppPutt.ProjectRollout(AppPutt.AnalyzeRollout(bp, hb), m ? (bool?)null : false) : null;
                string rollout = ro.HasValue ? AppPutt.FmtRollout(ro.Value) : null;
                // Entry as PuttCard reads it: where the ball crossed the lip against the tee's line (getHoleEntry - getTeeEntry).
                double? entry = null;
                if (m && tracked)
                {
                    double at = AppPutt.HoleEntry(bp, hb, out bool found);
                    if (found) entry = at - AppPutt.TeeEntry(J.Num(p, "distance"), tee);
                }
                Label result = Ui.Text(m ? "Made" : tracked ? "Missed " + AppPutt.MissResult(bp, hb).ToLowerInvariant() : "Missed", "sub", Ui.Bold);
                result.style.color = m ? Ui.Bright : Ui.Red;
                VisualElement read = Ui.Box("gv-read");
                if (rollout != null) read.Add(Ui.Text(rollout, "gv-pace", Ui.Bold));
                if (entry.HasValue) read.Add(Ui.Text(EntryWords(entry.Value), "gv-entry", Ui.Regular));
                list.Add(Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold),
                    tracked ? new GvHole(m, entry) : Ui.Box("gv-hole"),
                    Ui.Box("grow gv-what", Ui.Text(J.Int(p, "distance") + " ft   ·   " + PracticeSession.TeeName(tee) + " tee", "feed-name", Ui.Medium), result),
                    read));
            }
            _body.Add(Ui.Box("pad", list));
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
            p.fillColor = new Color32(12, 13, 14, 255);
            p.BeginPath(); p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.lineWidth = 1.5f;
            p.strokeColor = _made ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.14f);
            p.BeginPath(); p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            if (!_entry.HasValue) return;
            float rad = (float)_entry.Value * Mathf.Deg2Rad;
            Vector2 lip = c + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * r;
            // The ball's last roll in, from just outside the lip.
            p.lineWidth = 2f;
            p.strokeColor = new Color(Ui.Bright.r, Ui.Bright.g, Ui.Bright.b, 0.45f);
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
            _body.Add(Ui.Box("pad", FreePuttScreen.Choice(Tabs, 0, i => { _tab = i; Load(); })));
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
                bool joined = J.Get(c, "userJoined") != null;
                List<object> levels = J.List(J.Get(row, "challengeLevels"));
                double score = J.Num(row, "userResults");
                DateTime? start = ProfileMath.Date(c, "startDate"), end = ProfileMath.Date(c, "endDate");
                string timing = _tab == 1 ? "Starts " + (start?.ToString("MMM d", CultureInfo.InvariantCulture) ?? "soon")
                    : _tab == 2 ? "Ended " + Ago.Words(end, now) : "Ends " + (end?.ToString("MMM d", CultureInfo.InvariantCulture) ?? "");
                int percent = ChallengeMath.Percent(score, levels, out int reached, out double goal);
                string pill = _tab == 2 ? (reached >= 0 ? J.Str(levels[reached], "title") + " earned" : joined ? "Played" : "Ended") : joined ? "Joined" : "Join";
                VisualElement card = Ui.Box(joined && _tab == 0 ? "glass challenge-on" : "glass",
                    Ui.Box("row", Ui.Box("grow", Ui.Text(J.Str(c, "title"), "feed-name", Ui.Bold), Ui.Text(J.Str(c, "subTitle"), "sub", Ui.Regular)),
                        Ui.Text(pill, joined || reached >= 0 ? "chip chip-green" : "chip", Ui.Bold)),
                    Ui.Text(J.Int(row, "participants") + " Players   ·   " + timing, "muted", Ui.Regular).Margin(6));
                if (joined && levels.Count > 0 && _tab != 1)
                {
                    card.Add(Ui.Progress(percent / 100f).Margin(10));
                    card.Add(Ui.Text(ProfileMath.Number(score) + " / " + ProfileMath.Number(goal) + " " + ChallengeMath.Unit(J.Str(c, "cType")) + "   ·   " + percent + "%", "muted", Ui.Regular).Margin(4));
                }
                object tapped = row;
                card.style.marginBottom = 10;
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
            body.Add(Page.Loading());
            App.Run(Api.Get("/profile/achievements/recent", data =>
            {
                body.Clear();
                List<object> all = J.List(data);
                foreach (var kind in Kinds)
                {
                    var mine = all.FindAll(a => J.Str(a, "aType") == kind.type || (kind.type == "QUESTS" && J.Str(a, "aType") == "QUEST_STARS"));
                    mine.Sort((a, b) => J.Num(a, "number").CompareTo(J.Num(b, "number")));
                    body.Add(Ui.Section(kind.name));
                    VisualElement grid = Ui.Box("glass medal-grid");
                    if (mine.Count == 0) grid.Add(Ui.Text("No achievements yet", "sub", Ui.Regular));
                    foreach (object a in mine)
                    {
                        ProfileScreen.Look(J.Str(a, "aType"), out string icon, out Color tint);
                        VisualElement medal = Ui.Box("medal", Ui.Icon(icon, 26, tint));
                        medal.style.backgroundColor = Color.Lerp(Ui.Ink, tint, 0.22f);
                        grid.Add(Ui.Box("medal-cell", medal, Ui.Text(J.Str(a, "title"), "medal-title", Ui.Bold)));
                    }
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
            _list = new VisualElement();
            if (Kid.Blocked(app, Kid.Social, body)) return;
            body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "Following", "Community" }, community ? 1 : 0, i => { _community = i == 1; Load(); })));
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
                _list.Add(Page.Note(_community ? "No games yet." : "No activity yet. Follow Players to see their games here."));
                if (!_community) _list.Add(Ui.Box("pad", Ui.Button("Find players", () => _app.Push(new FindPlayersScreen(_app)))));
                return;
            }
            if (!_community) _list.Add(Ui.Box("pad", Ui.Text("Find new friends  ›", "sec-link", Ui.Medium).OnTap(() => _app.Push(new FindPlayersScreen(_app)))));
            VisualElement card = Ui.Box("glass set-card");
            foreach (object row in rows)
            {
                string name = J.Str(row, "displayName", "A Player"), gameId = J.Str(row, "gameId");
                bool scored = J.Int(row, "hasLeaderboard") == 1 && J.Get(row, "score") != null;
                string what = scored ? Phrase(row) + " on " + J.Str(row, "title") : "played " + J.Str(row, "title");
                card.Add(Ui.Box("note-row", Ui.Avatar(name, J.Str(row, "photoURL")),
                    Ui.Box("grow set-words", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text(what, "sub", Ui.Regular), Ui.Text(Ago.Words(ProfileMath.Date(row, "updated_at"), DateTime.UtcNow), "muted", Ui.Regular)),
                    scored ? Ui.Text(Page.Score(row), "feed-score", Ui.Bold) : null).OnTap(() => _app.Push(new GameViewScreen(_app, gameId))));
            }
            _list.Add(Ui.Box("pad", card));
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
            body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("You're in the club!", "h2", Ui.Bold), Ui.Text(ProfileMath.Membership(app.Profile, DateTime.UtcNow), "sub", Ui.Regular).Margin(4))));
            body.Add(Ui.Section("Top members"));
            body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "Putts", "Handicap" }, 0, i => { _tab = i; Load(); })));
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
                        Ui.Box("grow", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text(J.Str(row, "homeTown"), "muted", Ui.Regular)), Ui.Text(value, "feed-score", Ui.Bold))
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
                VisualElement words = Ui.Box("grow set-words", Ui.Text(Clean(J.Str(row, "brand")), "feed-name", Ui.Bold), Ui.Text(Clean(J.Str(row, "model")), "sub", Ui.Regular));
                string detail = ProfileMath.PutterDetail(row);
                if (detail.Length > 0) words.Add(Ui.Text(detail, "muted", Ui.Regular));
                VisualElement right = primary ? Ui.Text("Your gamer", "chip chip-green", Ui.Bold)
                    : Ui.Box("btn-small", Ui.Text("Set as gamer", "btn-text", Ui.Bold)).OnTap(() => App.Run(Api.Do("PATCH", "/user-putters/" + J.Str(putter, "id") + "/primary", null, _ => Shown(), _ => Shown())));
                card.Add(Ui.Box("set-row", Ui.Icon("Target", 20, Ui.Bright), words, right).OnTap(() => _app.Push(new EditPutterScreen(_app, putter))));
            }
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
            return Ui.Box("edit-group", Ui.Text(title.ToUpperInvariant(), "kick", Ui.Bold), row.Margin(6));
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
            form.Add(Ui.Box("set-row", Ui.Box("grow set-words", Ui.Text("Set as your gamer", "feed-name", Ui.Bold)), lockedPrimary ? Ui.Text("Your gamer", "chip chip-green", Ui.Bold) : Rows.Switch(_primary, v => _primary = v)));
            form.Add(Ui.Button(_putter == null ? "Add putter" : "Save changes", Save).Margin(14));
            if (_putter != null) form.Add(Ui.Button("Remove putter", Remove, "btn btn-ghost"));
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
        readonly App _app;
        readonly VisualElement _body;
        readonly TextField _display, _first, _last, _town, _bio, _web;
        readonly Label _error;
        string _hand, _handicap;
        bool _busy;
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
            _error = Ui.Text("", "sub", Ui.Medium);
            _error.style.color = Ui.Red;
            if (Kid.IsKid(p)) return;
            Draw();
        }

        static TextField Field(string label, string value)
        {
            var f = new TextField { value = value == "null" ? "" : value };
            f.textEdition.placeholder = label;
            f.textEdition.hidePlaceholderOnFocus = false;
            f.AddToClassList("field");
            return f.Face(Ui.Medium);
        }

        static VisualElement Named(string label, TextField field) => Ui.Box("", Ui.Text(label, "muted", Ui.Medium).Margin(8), field.Margin(4));

        static VisualElement Group(string title, params VisualElement[] parts)
        {
            VisualElement g = Ui.Box("edit-group", Ui.Text(title.ToUpperInvariant(), "kick", Ui.Bold));
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
            VisualElement face = Ui.Avatar(name, J.Str(_app.Profile, "photoURL"), true);
            VisualElement badge = Ui.Box("photo-badge", Ui.Icon("VideoCamera", 14, Color.white));
            VisualElement photo = Ui.Box("photo-pick", face, badge);
            if (PhotoPicker.Supported) photo.OnTap(() => { badge.AddToClassList("photo-busy"); PhotoPicker.ChangeProfilePhoto(_app, ok => { if (ok) Draw(); else badge.RemoveFromClassList("photo-busy"); }); });
            // Home town: Google's city search (SetUserLocation); typed here only off the phone.
            VisualElement town = Rows.Row("Target", _town.value.Length > 0 ? _town.value : "Set your home town", () => _app.Push(new CityScreen(_app, _town.value, (place, lat, lng) => { _town.value = place; _lat = lat; _lng = lng; Draw(); })));
            _body.Add(Ui.Box("pad center-box", photo, Ui.Text(PhotoPicker.Supported ? "Tap to change your photo" : "Change your photo in the phone app", "muted", Ui.Regular).Margin(6)));
            _body.Add(Ui.Box("pad",
                Group("Personal info", Named("Display name", _display), Named("First name", _first), Named("Last name", _last), Ui.Text("Home town", "muted", Ui.Medium).Margin(8), town),
                Group("Golf", FreePuttScreen.Choice(new[] { "Righty", "Lefty" }, _hand == "LEFTY" ? 1 : 0, i => _hand = i == 1 ? "LEFTY" : "RIGHTY").Margin(6),
                    Ui.Text("Handicap", "muted", Ui.Medium).Margin(10), handicaps.Margin(6),
                    Rows.Row("Target", "My putters", () => _app.Push(new PuttersScreen(_app)))),
                Group("More", Named("Bio", _bio), Named("Website", _web)),
                _error.Margin(8),
                Ui.Button("Save", Save).Margin(8)));
        }

        void Save()
        {
            string name = _display.value.Trim();
            if (!Regex.IsMatch(name, "^[a-zA-Z0-9]+$")) { _error.text = "Display name can only have letters and numbers."; return; }
            if (_busy) return;
            _busy = true;
            _error.text = "";
            string me = J.Str(_app.Profile, "userId");
            App.Run(Api.Open("/profile/displayName/" + UnityEngine.Networking.UnityWebRequest.EscapeURL(name), data =>
            {
                object taken = J.First(data);
                if (taken != null && J.Str(taken, "userId") != me) { _busy = false; _error.text = "Display name already taken."; return; }
                object p = _app.Profile;
                var body = new Dictionary<string, object>
                {
                    { "displayName", name }, { "homeTown", _town.value.Trim() }, { "lat", _lat }, { "lng", _lng },
                    { "firstName", _first.value.Trim() }, { "lastName", _last.value.Trim() }, { "birthDate", false }, { "gender", false },
                    { "realHandicap", _handicap }, { "handedness", _hand }, { "persona", J.Get(p, "persona") },
                    { "ownsPuttr", J.Int(p, "ownsPuttr") == 1 ? "YES" : "NO" }, { "bio", _bio.value }, { "webLink", _web.value.Trim() },
                };
                App.Run(Api.Post("/profile/save", body, _ =>
                    App.Run(Api.Get("/profile/", fresh => { _busy = false; _app.Profile = J.First(fresh); _app.Toast("Profile saved."); _app.Pop(); }, __ => { _busy = false; _app.Pop(); })),
                    _ => { _busy = false; _error.text = "Could not save. Check your connection and try again."; }));
            }, _ => { _busy = false; _error.text = "Could not check that name. Try again."; }));
        }
    }
}
