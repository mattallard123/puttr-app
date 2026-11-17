// The Improve tab, the Full analysis behind it, and the Puttr Club gate.
//
// Improve, as puttr-app's CoachHome orders it and the Puttr 2 sports UI stages it: today's
// session (built on your weakest spots), your last 30 days, PQS, practice, and taking it to the course.
// Full analysis is members only, as it is in the app; so are Compare and coach chat.
//
// Not in this build: coach chat and voice, 3D sessions, coach videos and lessons, Train & learn.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>Puttr Club. Members (or coaches) get everything; others meet the paywall.</summary>
    public static class Club
    {
        public static bool Member(object profile) => J.Flag(profile, "isMember") || J.Flag(profile, "isCoach");
        /// <summary>A Club blocker shows only when the profile is in hand and says isMember 0 (not before it has loaded).</summary>
        public static bool Blocked(object profile) => profile != null && J.Get(profile, "isMember") != null && !Member(profile);

        /// <summary>Runs open for a member; otherwise shows what Puttr Club gives. Reads the profile first if it is not in hand.</summary>
        public static void Require(App app, string feature, Action open)
        {
            if (app.Profile != null) { Decide(app, feature, open); return; }
            App.Run(Api.Get("/profile/", data => { app.Profile = J.First(data); Decide(app, feature, open); }, _ => app.Toast("No connection. Try again.")));
        }

        static void Decide(App app, string feature, Action open)
        {
            if (Member(app.Profile)) open();
            else app.Push(new ClubScreen(app, feature));
        }
    }

    /// <summary>The paywall (MembershipScreen for a non-member): the plans (IAPHUB), a website order, Junior, Restore, what Club gives, the trial terms, and help.</summary>
    public sealed class ClubScreen : AppScreen
    {
        static readonly string[,] Features =
        {
            { "Full analysis", "Every putt broken down: hole entry, pace, misses and your weakest spots." },
            { "Tournaments", "A new tournament every month, with prizes." },
            { "League Night", "Play for your regional team every week." },
            { "Putting coach", "A coach that reads your putts and builds your sessions." },
            { "Online play", "Head to head with Players anywhere." },
            { "Sessions", "Film your stroke in 3D." },
        };

        readonly App _app;
        readonly TextField _order;
        readonly Label _said;

        public ClubScreen(App app, string feature)
        {
            _app = app;
            VisualElement list = Ui.Box("glass");
            for (int i = 0; i < Features.GetLength(0); i++)
                list.Add(Ui.Box("board-row", Ui.Box("grow", Ui.Text(Features[i, 0], "feed-name", Ui.Bold), Ui.Text(Features[i, 1], "muted", Ui.Regular))));
            _order = new TextField();
            _order.textEdition.placeholder = "Order number";
            _order.textEdition.hidePlaceholderOnFocus = false;
            _order.AddToClassList("field");
            _order.Face(Ui.Medium);
            _said = Ui.Text("", "sub", Ui.Regular);
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad",
                Ui.Text("PUTTR CLUB", "kick", Ui.Bold),
                // The plans card below says "Unlock your putting potential": the title does not repeat it.
                Ui.Text(string.IsNullOrEmpty(feature) ? "Puttr Club" : feature + " is for Puttr Club members", "h1", Ui.Bold),
                list.Margin(14),
                new ClubPlans(app).Root.Margin(12),
                Ui.Box("glass",
                    Ui.Text("BOUGHT ON THE PUTTR WEBSITE?", "kick", Ui.Bold),
                    Ui.Text("Enter your order number to turn on your membership.", "sub", Ui.Regular).Margin(4),
                    _order.Margin(10), Ui.Button("Activate", Activate), _said.Margin(8)).Margin(12),
                Iap.Supported ? Ui.Box("glass",
                    Ui.Text("Already subscribed on your " + (Application.platform == RuntimePlatform.Android ? "Google" : "Apple") + " account?", "feed-name", Ui.Bold),
                    Ui.Button("Restore Purchase", () => Iap.Restore((_, err) =>
                    {
                        if (err != null) { app.Toast(Iap.Words(err) ?? "Nothing to restore."); return; }
                        App.Run(Api.Get("/profile/", d => { app.Profile = J.First(d); app.Toast(Club.Member(app.Profile) ? "Membership restored." : "No membership found on this account."); if (Club.Member(app.Profile)) app.Pop(); }));
                    }), "btn btn-ghost").Margin(8)).Margin(12) : null,
                Kid.Managed(app.Profile) ? null : Ui.Box("glass row", Ui.Box("grow", Ui.Text("Junior golfer?", "feed-name", Ui.Bold), Ui.Text("Get Club free by sharing your progress", "muted", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                    .Margin(12).OnTap(() => app.Push(new JuniorLandingScreen(app))),
                Ui.Box("glass",
                    Ui.Text("FREE TRIAL TERMS", "kick", Ui.Bold),
                    Ui.Text("You will not be charged during the trial period. At the end of your 1-week free trial, your subscription will automatically convert to a monthly plan at $19.99/month unless you cancel at least 24 hours before the trial ends. You can cancel your free trial at any time through your " + (Application.platform == RuntimePlatform.Android ? "Google Play Store" : "Apple App Store") + " subscription settings.", "muted", Ui.Regular).Margin(4)).Margin(12),
                Ui.Box("glass row", Ui.Box("grow", Ui.Text("Active membership?", "feed-name", Ui.Bold), Ui.Text("Contact your caddie for help", "muted", Ui.Regular)),
                    Ui.IconButton("ChatCircle", "Chat", () => app.Push(new CaddieScreen(app)))).Margin(12)));
            var foot = new VisualElement();
            foot.style.height = 28;
            scroll.Add(foot);
            Root = scroll;
        }

        bool _busy;

        void Activate()
        {
            string order = _order.value.Trim().TrimStart('#');
            if (order.Length == 0) { _said.text = "Enter the order number from your receipt."; return; }
            if (_busy) return;
            _busy = true;
            _said.text = "Checking...";
            App.Run(Api.Post("/profile/activateClub", new Dictionary<string, object> { { "orderNumber", order } }, data =>
            {
                _busy = false;
                App.Run(Api.Get("/profile/", profile =>
                {
                    _app.Profile = J.First(profile);
                    if (Club.Member(_app.Profile)) { _app.Pop(); _app.Toast("Welcome to Puttr Club."); }
                    else _said.text = J.Str(data, "message", "That order did not turn on a membership. Check the number, or contact support.");
                }));
            }, why => { _busy = false; _said.text = why == "offline" ? "No connection. Try again." : "That order number was not accepted. Check it and try again."; }));
        }
    }

    public sealed class ImproveScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _today, _month, _pqs, _practice, _course, _lab;
        VisualElement _lessons;
        List<object> _sessions;
        List<AnalysisMath.Cell> _grid;
        List<AnalysisMath.Putt> _year;
        object _coach, _stats, _streak;
        float _loadedAt = -100f;

        public ImproveScreen(App app)
        {
            _app = app;
            _today = new VisualElement(); _month = new VisualElement(); _pqs = new VisualElement(); _practice = new VisualElement(); _course = new VisualElement(); _lab = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            // CoachHome's header: chat with your coach (members; a past member reads old chats).
            scroll.Add(Ui.Box("pad row tab-top", Ui.Box("grow"),
                Kid.IsKid(app.Profile) ? null : Ui.IconButton("ChatCircle", "Chat with your coach", () => CoachChatScreen.Open(app), true)));
            VisualElement cols = Ui.Box("cols");
            foreach (VisualElement part in new[] { _today, _lab, _month, _pqs, _practice, _course }) { part.AddToClassList("col"); cols.Add(part); }
            scroll.Add(cols);
            // CoachHome's library: Train & learn.
            scroll.Add(Ui.Section("Train & learn"));
            scroll.Add(Ui.Box("pad", Ui.Text("Warm-ups, putting drills, mobility and lessons.", "muted", Ui.Regular), Library.Tiles(app).Margin(8)));
            _lessons = new VisualElement();
            scroll.Insert(1, _lessons);
            var foot = new VisualElement();
            foot.style.height = 24;
            scroll.Add(foot);
            Root = scroll;
            Practice();
            Course();
        }

        // CoachHome: the newest open video from a coach pinned at the top, and Real coach review (testers, as the app).
        void Lessons()
        {
            if (Kid.IsKid(_app.Profile)) return;
            List<object> videos = null, bookings = null;
            bool tester = J.Flag(_app.Profile, "isTester");
            void Draw()
            {
                if (videos == null || (tester && bookings == null)) return;
                _lessons.Clear();
                var open = videos.FindAll(v => J.Str(v, "bookingId").Length > 0 ? J.Int(v, "isStudio") != 1 && J.Get(v, "rating") == null || J.Str(v, "watchedAt").Length == 0 : Form.When(v, "created_at") is DateTime t && (DateTime.Now - t).TotalDays <= 14 && !SeenVideos.Contains(J.Str(v, "id")));
                if (open.Count > 0)
                {
                    object v = open[0];
                    bool rate = J.Str(v, "bookingId").Length > 0 && J.Str(v, "watchedAt").Length > 0;
                    string name = J.Str(v, "displayName");
                    _lessons.Add(Ui.Box("pad", Ui.Box("glass row choice-on", Ui.Avatar(name, J.Str(v, "photoURL")), Ui.Box("grow set-words", Ui.Text(rate ? "RATE YOUR LESSON" : "VIDEO FROM YOUR COACH", "kick", Ui.Bold),
                        Ui.Text(rate ? "How was your lesson with " + name + "?" : name + " sent you a lesson video", "feed-name wrap", Ui.Bold), Ui.Text((rate ? "Leave a rating and this card clears" : Form.Ago(v, "created_at")) + (open.Count > 1 ? " · " + (open.Count - 1) + " more" : ""), "muted", Ui.Regular)),
                        Ui.Box("ring", Ui.Icon("Play", 18, Color.white))).OnTap(() =>
                        {
                            if (J.Str(v, "bookingId").Length > 0) LessonInfo.Open(_app, J.Str(v, "bookingId"));
                            else { foreach (object x in videos) if (J.Str(x, "bookingId").Length == 0) SeenVideos.Add(J.Str(x, "id")); SaveSeen(); _app.Push(new CoachVideosScreen(_app)); }
                        })));
                }
                if (!tester) return;
                object b = bookings.Find(x => J.Int(x, "status") == 4 && J.Str(x, "watchedAt").Length == 0) ?? bookings.Find(x => J.Str(x, "planId").Length > 0 && J.Int(x, "planStatus") == 1)
                    ?? bookings.Find(x => J.Int(x, "planStatus") == 2) ?? bookings.Find(x => J.Int(x, "status") >= 1 && J.Int(x, "status") <= 3);
                string coach = J.Str(b, "coachName");
                (string title, string words, Action go) card =
                    b != null && J.Int(b, "status") == 4 && J.Str(b, "watchedAt").Length == 0 ? ("Your lesson is ready!", coach + " broke down your stroke. Watch your lesson video.", (Action)(() => LessonInfo.Open(_app, J.Str(b, "id"))))
                    : b != null && J.Str(b, "planId").Length > 0 && J.Int(b, "planStatus") == 1 ? ("Practice plan", "Work the plan " + coach + " assigned. Track your progress.", () => _app.Push(new PlanDetailScreen(_app, J.Str(b, "planId"))))
                    : b != null && J.Int(b, "planStatus") == 2 ? ("Plan complete! See your progress", "You finished " + coach + "'s plan. See how you improved.", () => _app.Push(new PlanDetailScreen(_app, J.Str(b, "planId"))))
                    : b != null ? ("Lesson in progress", J.Int(b, "status") == 1 ? "Submit a session to " + coach + " to start the clock." : coach + " is reviewing your putting. Lesson due " + Form.Date(b, "dueAt", "ddd MMM d, h:mm tt") + ".", () => LessonInfo.Open(_app, J.Str(b, "id")))
                    : bookings.Count > 0 ? ("Book another lesson", "Keep the momentum going with a fresh review from a coach.", () => _app.Push(new FindCoachScreen(_app)))
                    : ("Get a lesson", "A real coach reviews your session and sends you a personal video lesson.", () => _app.Push(new FindCoachScreen(_app)));
                _lessons.Add(Ui.Section("Real coach review"));
                _lessons.Add(Ui.Box("pad", Ui.Box("glass row", Ui.Bubble("GraduationCap", Ui.Bright), Ui.Box("grow set-words", Ui.Text(card.title, "feed-name", Ui.Bold), Ui.Text(card.words, "muted wrap", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint)).OnTap(card.go)));
                if (bookings.Count > 0)
                    _lessons.Add(Ui.Box("pad row", b != null ? Ui.Text("Book another lesson", "sec-link grow center", Ui.Medium).OnTap(() => _app.Push(new FindCoachScreen(_app))) : null, Ui.Text("View all lessons", "sec-link grow center", Ui.Medium).OnTap(() => _app.Push(new MyLessonsScreen(_app)))));
            }
            App.Run(Api.Get("/lab/coaching/videos", d => { videos = J.List(d); Draw(); }, _ => { videos = new List<object>(); Draw(); }));
            if (tester) App.Run(Api.Get("/lesson/bookings", d => { bookings = J.List(d); Draw(); }, _ => { bookings = new List<object>(); Draw(); }));
        }

        const string SeenKey = "app2.seenCoachVideos";
        static HashSet<string> _seen;
        static HashSet<string> SeenVideos => _seen ?? (_seen = new HashSet<string>(PlayerPrefs.GetString(SeenKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)));
        static void SaveSeen() { var list = new List<string>(SeenVideos); if (list.Count > 100) list = list.GetRange(list.Count - 100, 100); PlayerPrefs.SetString(SeenKey, string.Join(",", list)); }

        public override void Shown()
        {
            if (Time.realtimeSinceStartup - _loadedAt < 20f) return;
            _loadedAt = Time.realtimeSinceStartup;
            App.Run(Api.Get("/profile/analysis/30", data => { _grid = AnalysisMath.Grid(J.List(data)); Today(); Month(); }));
            App.Run(Api.Get("/lab/sessions", data => { _sessions = J.List(data); Lab(); }, _ => { _sessions = new List<object>(); Lab(); }));
            App.Run(Api.Get("/coach/settings", data =>
            {
                _coach = data; CoachVoice.Load(data); Today(); Month();
                // No coach row yet: the first run (pick a coach, five putts, the reveal, the daily goal).
                if (J.Get(data, "exists") != null && !J.Flag(data, "exists") && !Kid.IsKid(_app.Profile) && !(_app.Top is CoachFirstRunScreen))
                    _app.Push(new CoachFirstRunScreen(_app, () => { _loadedAt = -100f; Shown(); }));
            }, _ => { _coach = new Dictionary<string, object>(); Today(); }));
            Lessons();
            App.Run(Api.Get("/profile/userStats", data => { _stats = J.First(data); Month(); }));
            App.Run(Api.Get("/streaks/", data => { _streak = data; Today(); }));
            App.Run(Api.Get("/profile/analysis/putts/365", data => { _year = AnalysisMath.ReadAll(J.List(data)); Pqs(); }, _ => { _year = new List<AnalysisMath.Putt>(); Pqs(); }));
        }

        // ── Today's session ──────────────────────────────────────────────────

        void Today()
        {
            if (_grid == null || _coach == null) return;
            _today.Clear();
            int goal = Mathf.Clamp(J.Int(_coach, "dailyGoal", 10), 3, 30);
            List<TournamentMath.Dealt> putts = AnalysisMath.Session(_grid, goal, new System.Random(DateTime.Now.DayOfYear), out string title, out string why);
            bool done = J.Str(_coach, "lastPracticeDate").StartsWith(DateTime.Now.ToString("yyyy-MM-dd"));
            int streak = J.Int(_streak, "currentStreak");
            int feet = 0; foreach (TournamentMath.Dealt p in putts) feet += p.Feet;
            VisualElement card = Ui.Box("glass hero",
                Ui.Text(done ? "DONE FOR TODAY" : "TODAY'S SESSION   ·   " + putts.Count + " PUTTS   ·   ABOUT " + Mathf.Max(2, Mathf.CeilToInt(putts.Count / 3f)) + " MIN", "kick", Ui.Bold),
                Ui.Text(done ? "Nice work today." : title, "h2", Ui.Bold).Margin(4),
                Ui.Text(done ? "Come back tomorrow to keep your streak going, or practice again now." : why, "sub", Ui.Regular).Margin(4));
            if (streak > 0) card.Add(Ui.Box("chips", Ui.Text(streak + " day streak", "chip chip-green")));
            card.Add(Ui.Button(done ? "Practice again" : "Start session", () => _app.Games.PlayCoach(putts, title)));
            _today.Add(Ui.Box("pad", card));
        }

        // ── 3D stroke session: baseline, recheck after a week, or the one in the cloud ──

        const int RecheckDays = 7;

        void Lab()
        {
            _lab.Clear();
            if (_sessions == null) return;
            object latest = _sessions.Count > 0 ? _sessions[0] : null;
            int status = J.Int(latest, "status");
            int days = latest == null ? 0 : (int)(DateTime.UtcNow - (ProfileMath.Date(latest, "created_at") ?? DateTime.UtcNow)).TotalDays;
            string kick, title, words, action;
            Action go;
            if (latest == null) { kick = "3D STROKE SESSION"; title = "Film your baseline"; words = "Six putts on camera. Your coach builds your stroke in 3D and talks you through it."; action = "Start"; go = () => _app.Push(new LabIntroScreen(_app)); }
            else if (status < 4) { kick = "3D STROKE SESSION"; title = "Processing in the cloud"; words = "Your session is being built in 3D. It is usually ready in a few minutes."; action = "View"; go = () => _app.Push(new LabSessionScreen(_app, latest)); }
            else if (days >= RecheckDays) { kick = "3D STROKE SESSION"; title = "Time for a recheck"; words = "It has been " + days + " days. Film six more putts and see what changed."; action = "Start recheck"; go = () => _app.Push(new LabIntroScreen(_app)); }
            else { kick = "3D STROKE SESSION"; title = "Your latest stroke"; words = "Recheck in " + (RecheckDays - days) + (RecheckDays - days == 1 ? " day." : " days."); action = "View in 3D"; go = () => _app.Push(new LabSessionScreen(_app, latest)); }
            VisualElement card = Ui.Box("glass", Ui.Text(kick, "kick", Ui.Bold), Ui.Text(title, "h2", Ui.Bold).Margin(4), Ui.Text(words, "sub", Ui.Regular).Margin(4), Ui.Button(action, go));
            _lab.Add(Ui.Section("Your stroke in 3D", _sessions.Count > 1 ? "All sessions" : null, _sessions.Count > 1 ? () => _app.Push(new LabSessionsScreen(_app)) : (Action)null));
            _lab.Add(Ui.Box("pad", card));
        }

        // ── Your putting, last 30 days ───────────────────────────────────────

        void Month()
        {
            if (_grid == null) return;
            _month.Clear();
            int percent = AnalysisMath.MakePercent(_grid);
            // The app reads user_stats; with no row yet, the grid's own count.
            int putts = J.Get(_stats, "total_putts_last_30") != null ? J.Int(_stats, "total_putts_last_30") : AnalysisMath.Putts(_grid);
            int best = J.Int(_coach, "longestStreak");
            VisualElement card = Ui.Box("glass",
                Ui.Box("row", FreePuttScreen.Stat(percent < 0 ? "–" : percent + "%", "Make %"), FreePuttScreen.Stat(ProfileMath.Number(putts), "Putts"), FreePuttScreen.Stat(best.ToString(), "Best streak")));
            _month.Add(Ui.Section("Your putting   ·   last 30 days", "Full analysis", () => Club.Require(_app, "Full analysis", () => _app.Push(new AnalysisScreen(_app)))));
            _month.Add(Ui.Box("pad", card));
        }

        // ── PQS ──────────────────────────────────────────────────────────────

        void Pqs()
        {
            _pqs.Clear();
            if (_year == null) return;
            DateTime now = DateTime.UtcNow;
            var last30 = _year.FindAll(p => p.At > now.AddDays(-30));
            var before = _year.FindAll(p => p.At <= now.AddDays(-30) && p.At > now.AddDays(-60));
            _pqs.Add(Ui.Section("Putting Quality Score"));
            if (last30.Count < 5)
            {
                _pqs.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("Your first score arrives after 5 putts.", "sub", Ui.Regular))));
                return;
            }
            int score = AnalysisMath.Pqs(last30, out float makeIndex, out float accuracy);
            int prior = before.Count >= 5 ? AnalysisMath.Pqs(before, out _, out _) : -1;
            VisualElement words = Ui.Box("grow next-words", Ui.Text("LAST 30 DAYS", "kick", Ui.Bold));
            if (prior >= 0)
            {
                int change = score - prior;
                Label delta = Ui.Text((change > 0 ? "▲ " : change < 0 ? "▼ " : "") + Math.Abs(change) + " vs the 30 days before", "sub", Ui.Medium);
                delta.style.color = change > 0 ? Ui.Bright : change < 0 ? Ui.Red : Ui.Faint;
                words.Add(delta);
            }
            if (last30.Count < 30) { Label cal = Ui.Text("Calibrating   ·   " + last30.Count + " of 30 putts", "sub", Ui.Medium); cal.style.color = (Color)new Color32(0xFF, 0xCF, 0x66, 0xFF); words.Add(cal); }
            VisualElement card = Ui.Box("glass", Ui.Box("row", Ui.Box("next-score", Ui.Text(score.ToString(), "score-big", Ui.Bold)), words),
                Ui.Box("row", FreePuttScreen.Stat(Mathf.RoundToInt(makeIndex * 100f).ToString(), "Make index"), FreePuttScreen.Stat(Mathf.RoundToInt(accuracy * 100f).ToString(), "Accuracy")).Margin(10));
            List<(DateTime day, int pqs)> days = AnalysisMath.PqsByDay(_year);
            if (days.Count >= 2)
            {
                var points = new List<(string label, int value)>();
                for (int i = Mathf.Max(0, days.Count - 8); i < days.Count; i++) points.Add((days[i].day.ToString("MMM d", CultureInfo.InvariantCulture), days[i].pqs));
                card.Add(new LineChart(points).Margin(10));
            }
            _pqs.Add(Ui.Box("pad", card));
        }

        // ── Practice, and taking it to the course ────────────────────────────

        void Practice()
        {
            _practice.Add(Ui.Section("Practice"));
            VisualElement open = Ui.Box("glass list-row", Ui.Bubble("FillTarget", Ui.Bright), FreePuttScreen.Space(12),
                Ui.Box("grow", Ui.Text("Free putt", "feed-name", Ui.Bold), Ui.Text("Your driving range for putting. Every putt tracked.", "tile-desc", Ui.Regular))).OnTap(() => FreePuttScreen.Open(_app));
            VisualElement compare = Ui.Box("glass list-row", Ui.Bubble("Target", Ui.Amber), FreePuttScreen.Space(12),
                Ui.Box("grow", Ui.Text("Compare", "feed-name", Ui.Bold), Ui.Text("Putt the same putts with each of your putters and see which is best.", "tile-desc", Ui.Regular)), Ui.Text("Club", "chip chip-amber"))
                .OnTap(() => Club.Require(_app, "Compare", OpenCompare));
            // Custom games: the Player's own (Creator), moved here from the Play tab (Matt). Not for a Kid profile.
            VisualElement custom = Kid.IsKid(_app.Profile) ? null : Ui.Box("glass list-row", Ui.Bubble("Ruler", Ui.Bright), FreePuttScreen.Space(12),
                Ui.Box("grow", Ui.Text("Custom games", "feed-name", Ui.Bold), Ui.Text("Build your own putts and play them.", "tile-desc", Ui.Regular)))
                .OnTap(() => _app.Push(new CreatorScreen(_app)));
            _practice.Add(open);
            _practice.Add(compare);
            if (custom != null) _practice.Add(custom);
        }

        // Compare is a tool, not a game: it lives here, off the Play tab. The catalogue row (FITTING) when it has
        // loaded, else the app's own (id 325).
        void OpenCompare()
        {
            object row = J.Parse("{\"id\":325,\"title\":\"Compare\",\"gameType\":\"FITTING\",\"description\":\"Test putters side by side: the same putts with each one, scored on entry and pace.\"}");
            App.Run(Api.Get("/games/ALL", d => { object hit = J.List(d).Find(g => J.Str(g, "gameType") == "FITTING"); _app.Push(new GameScreen(_app, hit ?? row)); }, _ => _app.Push(new GameScreen(_app, row))));
        }

        void Course()
        {
            _course.Add(Ui.Section("Take it to the course"));
            _course.Add(Ui.Box("glass list-row", Ui.Bubble("FillTrophy", Ui.Bright), FreePuttScreen.Space(12),
                Ui.Box("grow", Ui.Text("Tournaments", "feed-name", Ui.Bold), Ui.Text("A new one every month.", "tile-desc", Ui.Regular))).OnTap(() => _app.Push(new TournamentsScreen(_app))));
            _course.Add(Ui.Box("glass list-row", Ui.Bubble("FillUsersThree", Ui.Amber), FreePuttScreen.Space(12),
                Ui.Box("grow", Ui.Text("League Night", "feed-name", Ui.Bold), Ui.Text("Play for your team every week.", "tile-desc", Ui.Regular))).OnTap(() => _app.Push(new LeagueScreen(_app))));
        }
    }

    /// <summary>Full analysis, members only: every chart puttr-app's analysis screen has, but the start-line scatter.</summary>
    public sealed class AnalysisScreen : AppScreen
    {
        static readonly int[] Ranges = { 1, 7, 14, 30, 60, 90, 180, 365 };
        static readonly string[] RangeNames = { "24h", "7d", "14d", "30d", "60d", "90d", "6m", "1y" };

        readonly App _app;
        readonly VisualElement _pills, _body;
        int _range = 3;
        int _asked;

        public AnalysisScreen(App app)
        {
            _app = app;
            _pills = new VisualElement();
            _body = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("Full analysis", app.Pop));
            scroll.Add(Ui.Box("pad", _pills));
            scroll.Add(_body);
            var foot = new VisualElement();
            foot.style.height = 28;
            scroll.Add(foot);
            Root = scroll;
            Load();
        }

        void Load()
        {
            _pills.Clear();
            ScrollView row = Ui.Scroll(true);
            for (int i = 0; i < Ranges.Length; i++)
            {
                int index = i;
                row.Add(Ui.Box(i == _range ? "scope scope-on" : "scope", Ui.Text(RangeNames[i], "scope-text", Ui.Bold)).OnTap(() => { _range = index; Load(); }));
            }
            _pills.Add(row);
            _body.Clear();
            _body.Add(Ui.Text("Analyzing your putts", "muted center", Ui.Regular).Margin(30));
            int asked = ++_asked, days = Ranges[_range];
            List<AnalysisMath.Cell> grid = null;
            List<AnalysisMath.Putt> putts = null;
            void Both() { if (asked == _asked && grid != null && putts != null) Draw(grid, putts, days); }
            App.Run(Api.Get("/profile/analysis/" + days, data => { grid = AnalysisMath.Grid(J.List(data)); Both(); }, _ => { grid = AnalysisMath.Grid(new List<object>()); Both(); }));
            App.Run(Api.Get("/profile/analysis/putts/" + days, data => { putts = AnalysisMath.ReadAll(J.List(data)); Both(); }, _ => { putts = new List<AnalysisMath.Putt>(); Both(); }));
        }

        void Draw(List<AnalysisMath.Cell> grid, List<AnalysisMath.Putt> putts, int days)
        {
            _body.Clear();
            int total = AnalysisMath.Putts(grid);
            _body.Add(Ui.Box("pad", Ui.Text((days == 1 ? "Last 24 hours" : "Last " + days + " days") + "   ·   " + ProfileMath.Number(total) + " putts analyzed", "sub", Ui.Regular).Margin(10)));
            if (total == 0) { _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("No putts in this range. Try a longer one.", "sub", Ui.Regular)).Margin(10))); return; }

            AnalysisMath.Extremes(grid, out AnalysisMath.Cell best, out AnalysisMath.Cell worst);
            _body.Add(Ui.Box("pad", Ui.Box("row spots", Spot("BEST SPOT", best, Ui.Bright), Spot("WORST SPOT", worst, Ui.Red)).Margin(10)));

            // Pace
            AnalysisMath.Pace(putts, out int shortOnes, out int ideal, out int firm, out float median);
            int paced = shortOnes + ideal + firm;
            if (paced >= 5)
            {
                VisualElement pace = Ui.Box("glass");
                pace.Add(Ui.Box("row", Zone("Short", shortOnes, paced, Ui.Blue), Zone("Ideal pace", ideal, paced, Ui.Green), Zone("Too firm", firm, paced, new Color32(0xFF, 0x95, 0x00, 0xFF))));
                pace.Add(Ui.Text("Typical: " + AnalysisMath.Rollout(median) + ".   " + (shortOnes >= paced * 0.3f ? "Too many finish short: a putt that does not reach cannot drop." : firm >= paced * 0.3f ? "You are often too firm: aim to finish within a foot past." : "Your pace is dialed in."), "sub", Ui.Regular).Margin(10));
                foreach ((string name, int from, int to) band in new[] { ("Short putts, 3 to 5 ft", 3, 5), ("Mid putts, 6 to 8 ft", 6, 8), ("Long putts, 9 to 11 ft", 9, 11) })
                {
                    float at = AnalysisMath.PaceAt(putts, band.from, band.to);
                    pace.Add(Ui.Box("board-row", Ui.Box("grow", Ui.Text(band.name, "feed-name", Ui.Medium)), Ui.Text(float.IsNaN(at) ? "No putts" : AnalysisMath.Rollout(at), "sub", Ui.Regular)));
                }
                Section("Pace", pace);
            }

            // Hole entry
            int[] bands = AnalysisMath.Entries(putts, out int backDoor, out int holed);
            if (holed > 0)
            {
                VisualElement entry = Ui.Box("glass", new EntryArc(bands), Ui.Text(holed > 0 ? "Back door " + Mathf.RoundToInt(100f * backDoor / holed) + "% of makes" : "", "muted center", Ui.Medium));
                AnalysisMath.Misses(putts, out int left, out int right);
                if (left + right > 0)
                {
                    int l = Mathf.RoundToInt(100f * left / (left + right));
                    entry.Add(Ui.Text("MISS TENDENCY", "kick", Ui.Bold).Margin(14));
                    entry.Add(Split(l, 100 - l, "left", "right"));
                }
                bool lefty = J.Str(_app.Profile, "handedness").ToUpperInvariant().StartsWith("L");
                AnalysisMath.PushPull(putts, lefty, out int push, out int pull);
                if (push + pull > 0)
                {
                    int p = Mathf.RoundToInt(100f * push / (push + pull));
                    entry.Add(Ui.Text("PUSH OR PULL", "kick", Ui.Bold).Margin(14));
                    entry.Add(p == 50 ? Ui.Text("Balanced, no tendency", "sub", Ui.Regular) : Split(p, 100 - p, "push", "pull"));
                }
                Section("Hole entry", entry);
            }

            // The 27 spots
            VisualElement spots = Ui.Box("glass");
            spots.Add(Ui.Box("row", Ui.Box("spot-feet"), Ui.Text("Left", "muted grow center", Ui.Medium), Ui.Text("Center", "muted grow center", Ui.Medium), Ui.Text("Right", "muted grow center", Ui.Medium)));
            for (int feet = 3; feet <= 11; feet++)
            {
                VisualElement row = Ui.Box("row spot-row", Ui.Text(feet + " ft", "spot-feet feed-name", Ui.Bold));
                for (int tee = 1; tee <= 3; tee++)
                {
                    AnalysisMath.Cell cell = grid[(feet - 3) * 3 + tee - 1];
                    Label value = Ui.Text(cell.Total > 0 ? cell.Weighted.ToString() : "–", "spot-value", Ui.Bold);
                    VisualElement dot = Ui.Box("spot-dot", value);
                    if (cell.Total > 0) dot.style.backgroundColor = Heat(cell.Weighted);
                    row.Add(Ui.Box("grow spot-cell", dot));
                }
                spots.Add(row);
            }
            spots.Add(Ui.Text("Make % against the PGA Tour's at each spot: 100 is Tour level.", "muted", Ui.Regular).Margin(8));
            Section("Distance breakdown", spots);

            // Make % by break, from the grid
            var rings = Ui.Box("row");
            for (int tee = 1; tee <= 3; tee++)
            {
                int made = 0, all = 0;
                foreach (AnalysisMath.Cell c in grid) if (c.Tee == tee) { made += c.Made; all += c.Total; }
                rings.Add(Ui.Box("grow ring-item", new Ring(all > 0 ? Mathf.RoundToInt(100f * made / all) : -1), Ui.Text(ProfileMath.BreakNames[tee - 1], "muted center", Ui.Medium)));
            }
            Section("Make % by break", Ui.Box("glass", rings));

            // Worst to first
            List<AnalysisMath.Cell> order = AnalysisMath.WorstFirst(grid);
            if (order.Count > 0)
            {
                VisualElement list = Ui.Box("glass");
                foreach (AnalysisMath.Cell c in order)
                {
                    Label value = Ui.Text(c.Weighted.ToString(), "feed-score", Ui.Bold);
                    value.style.color = c.Weighted >= 100 ? Ui.Bright : Ui.Red;
                    list.Add(Ui.Box("board-row", Ui.Box("grow", Ui.Text(c.Name, "feed-name", Ui.Medium), Ui.Text(c.Made + " of " + c.Total + " made", "muted", Ui.Regular)), value));
                }
                var drill = new List<TournamentMath.Dealt>();
                for (int i = 0; i < Math.Min(9, order.Count); i++) drill.Add(new TournamentMath.Dealt { Feet = order[i].Feet, Tee = order[i].Tee });
                list.Add(Ui.Button("Create drill", () => _app.Games.PlayDrill(drill)));
                Section("Worst to first", list);
            }
        }

        void Section(string title, VisualElement card)
        {
            _body.Add(Ui.Section(title));
            _body.Add(Ui.Box("pad", card));
        }

        static VisualElement Spot(string label, AnalysisMath.Cell cell, Color colour)
        {
            Label name = Ui.Text(cell != null ? cell.Name : "No data", "h2", Ui.Bold);
            name.style.color = colour;
            return Ui.Box("glass grow spot-card", Ui.Text(label, "kick", Ui.Bold), name, Ui.Text(cell != null ? cell.Percent + "% made   ·   " + cell.Total + " putts" : "", "muted", Ui.Regular));
        }

        static VisualElement Zone(string name, int count, int total, Color colour)
        {
            Label value = Ui.Text(Mathf.RoundToInt(100f * count / total) + "%", "stat-value", Ui.Bold);
            value.style.color = colour;
            return Ui.Box("grow stat", value, Ui.Text(name, "muted center", Ui.Medium));
        }

        static VisualElement Split(int a, int b, string aName, string bName)
        {
            VisualElement left = Ui.Box("split-a");
            left.style.width = Length.Percent(a);
            left.style.backgroundColor = a >= b ? Ui.Red : (Color)new Color32(60, 62, 64, 255);
            VisualElement right = Ui.Box("grow split-b");
            right.style.backgroundColor = b > a ? Ui.Red : (Color)new Color32(60, 62, 64, 255);
            return Ui.Box("", Ui.Box("row split", left, right), Ui.Box("row", Ui.Text(a + "% " + aName, "muted grow", Ui.Medium), Ui.Text(b + "% " + bName, "muted", Ui.Medium)).Margin(4));
        }

        // Red at 0, amber at 50, green at Tour level and above, as the app's Spot draws them.
        static Color Heat(int weighted)
        {
            float t = Mathf.Clamp01(weighted / 100f);
            return t < 0.5f ? Color.Lerp(Ui.Red, Ui.Amber, t * 2f) : Color.Lerp(Ui.Amber, Ui.Green, (t - 0.5f) * 2f);
        }
    }

    /// <summary>Holed putts by where they came in: a half circle of five arcs, left side to right side, the busiest solid green.</summary>
    public sealed class EntryArc : VisualElement
    {
        readonly int[] _bands;

        public EntryArc(int[] bands)
        {
            _bands = bands;
            AddToClassList("entry-arc");
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Labels());
        }

        int Front { get { int n = 0; foreach (int b in _bands) n += b; return n; } }

        void Labels()
        {
            Clear();
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || Front == 0) return;
            Vector2 centre = new Vector2(w * 0.5f, h - 18f);
            float radius = Mathf.Min(w * 0.5f - 34f, h - 30f);
            for (int i = 0; i < 5; i++)
            {
                float angle = Mathf.Deg2Rad * (180f - (i + 0.5f) * 36f);
                Vector2 at = centre + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * (radius + 22f);
                Label value = Ui.Text(Mathf.RoundToInt(100f * _bands[i] / Front) + "%", "chart-label entry-value", Ui.Bold);
                value.style.left = at.x - 22f; value.style.top = at.y - 8f; value.style.width = 44f;
                value.style.unityTextAlign = TextAnchor.MiddleCenter;
                Add(value);
            }
            Label ball = Ui.Text("↑ ball", "chart-label", Ui.Regular);
            ball.style.left = centre.x - 22f; ball.style.top = h - 16f; ball.style.width = 44f; ball.style.unityTextAlign = TextAnchor.MiddleCenter;
            Add(ball);
        }

        void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0) return;
            int front = Front, most = 1;
            foreach (int b in _bands) most = Mathf.Max(most, b);
            Vector2 centre = new Vector2(w * 0.5f, h - 18f);
            float radius = Mathf.Min(w * 0.5f - 34f, h - 30f), width = radius * 0.32f;
            Painter2D p = context.painter2D;
            p.lineWidth = width;
            p.lineCap = LineCap.Butt;
            for (int i = 0; i < 5; i++)
            {
                // Left side is drawn on the left: arcs run from 180 degrees (left) to 0 (right), the screen's y down.
                float from = 180f + i * 36f + 1.5f, to = 180f + (i + 1) * 36f - 1.5f;
                float share = front > 0 ? _bands[i] / (float)most : 0f;
                p.strokeColor = _bands[i] == 0 ? (Color)new Color32(32, 34, 36, 255) : _bands[i] == most ? Ui.Green : Color.Lerp(new Color32(20, 46, 26, 255), Ui.Green, 0.25f + 0.55f * share);
                p.BeginPath(); p.Arc(centre, radius - width * 0.5f, Angle.Degrees(from), Angle.Degrees(to)); p.Stroke();
            }
            p.fillColor = Ui.Ink;
            p.BeginPath(); p.Arc(centre, radius * 0.22f, Angle.Degrees(180), Angle.Degrees(360)); p.Fill();
        }
    }
}
