// The coach's side (puttr-app components/coaching and lessons). A coach (isCoach or coachApproved) gets a
// Coach tab where Improve was:
//
//   CoachScreen               the dashboard: lessons to review, the go-live checklist, Lessons, My Players,
//                             Player Review, Video Library, Academy, Coaching Profile, games, affiliate, apply
//   LessonInboxScreen         GET /lesson/coaching/inbox: Needs review, Waiting on session, Delivered, Practice space
//   LessonWorkspaceScreen     the booking, the Player's goals and session; Start Review; Deliver Lesson
//   LessonDeliverScreen       the lesson video (camera roll, resumable upload), summary, practice plan, send
//   LessonDeliveredScreen     what was sent, watched or not, the rating, the plan's progress
//   CoachLessonSetupScreen    headline, bio, price, turnaround, specialties, accepting, Stripe payouts
//   AssignPlanScreen          games and drills, times a week, a cue; 1, 2 or 4 weeks
//   PlayerRosterScreen        active plans and the recently ended
//   PlayerMonitorScreen       a plan's progress, before and after, edit, one nudge a day
//   CoachingDashboardScreen   Player Review: followers and following, a Player's goals and notes, their sessions
//   CoachingVideosScreen      the coach's library and Player feedback videos; upload, preview, delete
//
// Recording the review: on iPhone, ReplayKit records the app with the microphone (the app's RecordPill); the
// coach saves it from Apple's preview and picks it in Deliver. On Android the system screen recorder, as the app.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Coach
    {
        public static bool Is(object profile) => J.Flag(profile, "isCoach") || J.Flag(profile, "coachApproved");

        /// <summary>The lessons waiting on the coach (the Coach tab's badge).</summary>
        public static int Pending;

        public static void RefreshPending(Action done = null) => App.Run(Api.Get("/lesson/coaching/pending", d => { Pending = J.Int(d, "needsReview"); done?.Invoke(); }, _ => done?.Invoke()));

        /// <summary>A card on the dashboard: icon, title, words, badge.</summary>
        public static VisualElement Card(string icon, string title, string words, int badge, Color badgeColour, Action tap)
        {
            VisualElement right = Ui.Box("row");
            if (badge > 0) right.Add(AdminLabels.Pill(badge > 9 ? "9+" : badge.ToString(), badgeColour));
            right.Add(Ui.Icon("CaretRight", 16, Ui.Faint));
            return Ui.Box("glass dash-card", Ui.Bubble(icon, Ui.Bright), Ui.Box("grow set-words", Ui.Text(title, "feed-name", Ui.Bold), Ui.Text(words, "muted wrap", Ui.Regular)), right).OnTap(tap);
        }

        /// <summary>Opens a booking where the coach works on it: delivered ones show what was sent.</summary>
        public static void OpenBooking(App app, object b)
        {
            int st = J.Int(b, "status");
            if (st == 4 || st == 5) app.Push(new LessonDeliveredScreen(app, J.Str(b, "id")));
            else app.Push(new LessonWorkspaceScreen(app, J.Str(b, "id")));
        }

        /// <summary>Grade from a session score (A 18+, B 15+, C 12+, else D).</summary>
        public static string Grade(double score) => score >= 18 ? "A" : score >= 15 ? "B" : score >= 12 ? "C" : "D";
    }

    /// <summary>The iPhone's screen recorder (ReplayKit, in-app plus microphone), as the app's RecordPill.</summary>
    public static class Recorder
    {
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int PuttrRecord_Available();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int PuttrRecord_Recording();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrRecord_Start();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrRecord_Stop();
        public static bool Supported => PuttrRecord_Available() != 0;
        public static bool Recording => PuttrRecord_Recording() != 0;
        public static void Start() => PuttrRecord_Start();
        /// <summary>Stops; Apple's preview opens on its own (Plugins/iOS/PuttrRecord.m) to save or share it.</summary>
        public static void Stop() => PuttrRecord_Stop();
        public static bool Preview() => true;
#else
        public static bool Supported => false;
        public static bool Recording => false;
        public static void Start() { }
        public static void Stop() { }
        public static bool Preview() => false;
#endif
        static float _since;

        /// <summary>REC, then a red Stop with the time; stopping opens Apple's preview to save it.</summary>
        public static VisualElement Pill(VisualElement over)
        {
            Label words = Ui.Text("REC", "scope-text", Ui.Bold);
            VisualElement pill = Ui.Box("scope", words);
            if (!Supported)
            {
                pill.OnTap(() => Dialog.Ask(over, "Record your lesson", "Use your phone's screen recorder (Quick Settings on Android), narrate your review, then upload the recording in Deliver.", "Got it", () => { }));
                return pill;
            }
            void Tick()
            {
                bool on = Recording;
                pill.style.backgroundColor = on ? (StyleColor)(Color)Ui.Red : new StyleColor(StyleKeyword.Null);
                words.text = on ? "■  " + Form.Duration(Time.realtimeSinceStartup - _since) : "REC";
            }
            pill.schedule.Execute(Tick).Every(500);
            pill.OnTap(() =>
            {
                if (Recording)
                {
                    Stop();
                    pill.schedule.Execute(() => { if (!Preview()) App.I.Toast("Recording saved."); }).StartingIn(600);
                }
                else { _since = Time.realtimeSinceStartup; Start(); }
                Tick();
            });
            return pill;
        }
    }

    // ── The dashboard ────────────────────────────────────────────────────────

    public sealed class CoachScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        object _application, _academy, _affiliate, _pending, _games;
        int _waiting;
        bool _showEarnings;

        public CoachScreen(App app)
        {
            _app = app;
            _body = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Box("pad", Ui.Text("COACH", "display wrap", Ui.Bold), Ui.Text("Manage your players and content", "muted", Ui.Regular)));
            scroll.Add(_body);
            scroll.Add(FreePuttScreen.Space(28));
            Root = scroll;
        }

        public override void Shown()
        {
            _waiting = 5;
            void Done() { if (--_waiting <= 0) Draw(); }
            App.Run(Api.Get("/coach/application", d => { _application = J.Get(d, "application"); Done(); }, _ => Done()));
            App.Run(Api.Get("/coach/academy", d => { _academy = d; Done(); }, _ => Done()));
            App.Run(Api.Get("/coach/affiliate", d => { _affiliate = d; Done(); }, _ => { _affiliate = null; Done(); }));
            App.Run(Api.Get("/lesson/coaching/pending", d => { _pending = d; Coach.Pending = J.Int(d, "needsReview"); Done(); }, _ => Done()));
            App.Run(Api.Get("/creator/games/", d => { _games = d; Done(); }, _ => Done()));
        }

        void Draw()
        {
            _body.Clear();
            object goLive = J.Get(_academy, "goLive");
            bool live = J.Flag(_app.Profile, "isTester") || J.Flag(goLive, "live");
            int review = J.Int(_pending, "needsReview"), overdue = J.Int(_pending, "overdue");
            if (review > 0)
            {
                Color c = overdue > 0 ? Ui.Red : Ui.Green;
                VisualElement banner = Ui.Box("glass row", Ui.Icon("Warning", 22, c), Ui.Box("grow set-words", Ui.Text(review + " lesson" + (review == 1 ? "" : "s") + " to review", "feed-name", Ui.Bold),
                    Ui.Text(overdue > 0 ? overdue + " overdue. Deliver soon to avoid a refund." : live ? "Record and deliver to get paid." : "Record and deliver to your player.", "muted wrap", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint));
                banner.style.borderTopColor = banner.style.borderBottomColor = banner.style.borderLeftColor = banner.style.borderRightColor = c;
                _body.Add(Ui.Box("pad", banner.OnTap(() => _app.Push(new LessonInboxScreen(_app)))));
            }
            if (goLive != null && !J.Flag(goLive, "live")) GoLive(goLive);
            VisualElement cols = Ui.Box("cols");
            void Add(VisualElement card) => cols.Add(Ui.Box("col pad", card));
            // The banner above carries the count; the card does not repeat it.
            Add(Coach.Card("Tray", "Lessons", live ? "Paid lesson bookings from players" : "Lessons you deliver to your players", 0, Ui.Bright, () => _app.Push(new LessonInboxScreen(_app))));
            Add(Coach.Card("UsersThree", "My Players", "Active practice plans and progress", 0, Ui.Bright, () => _app.Push(new PlayerRosterScreen(_app))));
            Add(Coach.Card("FilmReel", "Player Review", "Review player putting sessions", 0, Ui.Bright, () => _app.Push(new CoachingDashboardScreen(_app))));
            Add(Coach.Card("VideoCamera", "Video Library", "Drills, tips and player videos", 0, Ui.Bright, () => _app.Push(new CoachingVideosScreen(_app))));
            Add(Coach.Card("GraduationCap", "Coach Academy", goLive != null ? "Online coaching modules and certification" : "Training on the studio coaching toolset", 0, Ui.Bright, () => _app.Push(new CoachAcademyScreen(_app))));
            if (goLive != null || live)
                Add(Coach.Card("GearSix", "Coaching Profile & Payouts", J.Flag(goLive, "payoutsEnabled") ? "Headline, price, availability and Stripe payouts" : "Set your price and connect Stripe to get paid", 0, Ui.Bright, () => _app.Push(new CoachLessonSetupScreen(_app))));
            _body.Add(cols);

            List<object> games = J.List(_games);
            _body.Add(Ui.Section("Games & Drills", "Build", () => _app.Push(new CreatorScreen(_app))));
            if (games.Count == 0) _body.Add(Page.Note("No games yet. Build a custom drill for your players."));
            else
            {
                VisualElement grid = Ui.Box("cols");
                foreach (object g in games) { object game = g; grid.Add(Ui.Box("col pad", Ui.Box("glass row", Ui.Bubble("FillTarget", Ui.Bright), Ui.Box("grow set-words", Ui.Text(J.Str(g, "title"), "feed-name", Ui.Bold), Ui.Text(J.Int(g, "putts") + " putts", "muted", Ui.Regular))).OnTap(() => _app.Push(new EditGameScreen(_app, game)))).Margin(6)); }
                _body.Add(grid);
            }
            Affiliate();
            OnlineCoach(live);
        }

        void GoLive(object gl)
        {
            bool academy = J.Flag(gl, "academyComplete"), cert = J.Flag(gl, "certPassed"), pending = J.Flag(gl, "certPending"), profile = J.Flag(gl, "profileComplete"), payouts = J.Flag(gl, "payoutsEnabled");
            int done = (academy ? 1 : 0) + (cert ? 1 : 0) + (profile ? 1 : 0) + (payouts ? 1 : 0);
            object p = J.Get(gl, "academyProgress");
            VisualElement card = Ui.Box("glass", Ui.Text("You are approved. " + done + " of 4 steps done. Tap a step to complete it.", "feed-name wrap", Ui.Bold));
            bool nextShown = false;
            void Row(bool ok, bool waiting, string title, string sub, Action tap)
            {
                bool next = !ok && !waiting && !nextShown;
                if (next) nextShown = true;
                VisualElement row = Ui.Box(next ? "set-row choice-on" : "set-row", Ui.Icon(ok ? "CheckCircle" : waiting ? "Clock" : "Circle", 20, ok ? Ui.Green : waiting ? Ui.Amber : Ui.Faint), Ui.Box("grow set-words", Ui.Text(title, "feed-name", Ui.Bold), Ui.Text(sub, "muted wrap", Ui.Regular)));
                if (tap != null && !ok) row.OnTap(tap);
                card.Add(row);
            }
            Row(academy, false, "Finish the Coach Academy", J.Int(p, "passed") + " of " + J.Int(p, "total") + " online modules done", () => _app.Push(new CoachAcademyScreen(_app)));
            Row(cert, pending, "Coach your certification lesson", cert ? "Passed" : pending ? "Delivered. Puttr is grading it." : academy ? "A practice lesson, graded by Puttr" : "Unlocks after the Academy", academy && !pending ? () => CoachAcademyScreen.BeginCertification(_app, Root) : (Action)null);
            Row(profile, false, "Complete your coaching profile", "Headline, bio, price and intro video", () => _app.Push(new CoachLessonSetupScreen(_app)));
            Row(payouts, false, "Set up payouts", payouts ? "Stripe connected" : "Connect Stripe to get paid for lessons", () => _app.Push(new CoachLessonSetupScreen(_app)));
            _body.Add(Ui.Box("pad", card));
        }

        void Affiliate()
        {
            if (_affiliate == null || (J.Get(_affiliate, "configured") != null && !J.Flag(_affiliate, "configured"))) return;
            VisualElement card = Ui.Box("glass", Ui.Text("Affiliate Program", "feed-name", Ui.Bold));
            if (!J.Flag(_affiliate, "enrolled"))
            {
                card.Add(Ui.Text("Earn commission when players you refer buy Puttr gear and memberships", "sub wrap", Ui.Regular).Margin(6));
                card.Add(Ui.Button("Enroll Now", () => App.Run(Api.Post("/coach/affiliate/enroll", new Dictionary<string, object>(), d =>
                {
                    _affiliate = d;
                    if (J.Flag(d, "created")) Dialog.Ask(Root, "Check your email", "Your affiliate account is set up. We sent you the details.", "OK", () => { });
                    Draw();
                }, e => _app.Toast(Form.Why(e, "Could not enroll. Try again."))))).Margin(10));
            }
            else
            {
                string link = J.Str(_affiliate, "link");
                card.Add(Form.Label("Your referral link"));
                card.Add(Ui.Box("glass row", Ui.Text(link, "sub grow wrap", Ui.Regular).Margin(0), Ui.Text("Copy", "sec-link", Ui.Bold).OnTap(() => { GUIUtility.systemCopyBuffer = link; _app.Toast("Copied to clipboard"); })));
                card.Add(Ui.Text(_showEarnings ? "Hide earnings" : "Show earnings", "sec-link", Ui.Medium).Margin(10).OnTap(() => { _showEarnings = !_showEarnings; Draw(); }));
                if (_showEarnings)
                {
                    object s = J.Get(_affiliate, "stats");
                    card.Add(Ui.Box("stat-tiles", Tile(J.Num(s, "totalEarnings"), "EARNED"), Tile(J.Num(s, "pendingAmount"), "PENDING"), Tile(J.Num(s, "paidAmount"), "PAID OUT")));
                    if (J.Str(_affiliate, "portalUrl").Length > 0) card.Add(Ui.Button("Manage Account", () => Application.OpenURL(J.Str(_affiliate, "portalUrl")), "btn btn-ghost").Margin(10));
                }
            }
            _body.Add(Ui.Box("pad", card).Margin(12));
        }

        static VisualElement Tile(double dollars, string label) => Ui.Box("glass stat-tile", Ui.Text("$" + dollars.ToString("0.00", CultureInfo.InvariantCulture), "stat-value", Ui.Bold), Ui.Text(label, "stat-label", Ui.Bold));

        void OnlineCoach(bool live)
        {
            int st = _application != null ? J.Int(_application, "status") : -1;
            if (live || (st == 2 && J.Str(_application, "submittedAt").Length > 0)) return;
            string[] chips = { "SUBMITTED", "UNDER REVIEW", "APPROVED", "WAITLISTED", "NOT APPROVED" };
            VisualElement card = _application == null || J.Str(_application, "submittedAt").Length == 0
                ? Ui.Box("glass", Ui.Text("Apply to Become a Puttr Online Coach", "feed-name wrap", Ui.Bold), Ui.Text("Give paid video lessons to players anywhere. Apply, get approved, and open your lesson inbox.", "muted wrap", Ui.Regular).Margin(6))
                : Ui.Box("glass", Ui.Box("row", Ui.Text("Puttr Online Coach", "feed-name grow", Ui.Bold), AdminLabels.Pill(chips[Mathf.Clamp(st, 0, 4)], st == 2 ? Ui.Bright : st == 4 ? Ui.Red : Ui.Amber)));
            _body.Add(Ui.Box("pad", card.OnTap(() => CoachApplyScreen.Open(_app))).Margin(12));
        }
    }

    // ── Lessons ──────────────────────────────────────────────────────────────

    public sealed class LessonInboxScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        List<object> _rows;
        object _earnings, _stripe, _academy;

        public LessonInboxScreen(App app)
        {
            _app = app;
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Box("top", Ui.Text("‹", "back back-chevron", Ui.Medium).OnTap(app.Pop), Ui.Text("Lessons", "sec-title", Ui.Bold),
                Ui.Box("row", Ui.Icon("Barbell", 22, Ui.Faint).OnTap(() => CoachAcademyScreen.OpenPractice(app, Root)), FreePuttScreen.Space(0).Width(16), Ui.Icon("GearSix", 22, Ui.Faint).OnTap(() => app.Push(new CoachLessonSetupScreen(app))))));
            _body = new VisualElement();
            scroll.Add(_body);
            scroll.Add(FreePuttScreen.Space(28));
            Root = scroll;
            _body.Add(Page.Loading());
        }

        public override void Shown()
        {
            int waiting = 4;
            void Done() { if (--waiting == 0) Draw(); }
            App.Run(Api.Get("/lesson/coaching/inbox", d => { _rows = J.List(d); Done(); }, _ => { _rows = new List<object>(); Done(); }));
            App.Run(Api.Get("/lesson/coaching/earnings", d => { _earnings = d; Done(); }, _ => Done()));
            App.Run(Api.Get("/lesson/coaching/stripe/status", d => { _stripe = d; Done(); }, _ => Done()));
            App.Run(Api.Get("/coach/golive", d => { _academy = J.Get(d, "goLive"); Done(); }, _ => Done()));
            Coach.RefreshPending();
        }

        void Draw()
        {
            _body.Clear();
            bool live = J.Flag(_app.Profile, "isTester") || J.Flag(_academy, "live");
            int delivered = J.Int(_earnings, "lessons");
            if (live || delivered > 0) _body.Add(Ui.Box("pad", Ui.Text("$" + (J.Num(_earnings, "earned") / 100).ToString("0", CultureInfo.InvariantCulture) + " earned · " + delivered + " delivered", "muted", Ui.Medium)));
            double pendingCents = J.Num(_stripe, "pendingCents");
            if (!J.Flag(_stripe, "payoutsEnabled") && (live || pendingCents > 0))
            {
                int n = J.Int(_stripe, "pendingCount");
                _body.Add(Ui.Box("pad", Ui.Box("glass row", Ui.Icon("Warning", 22, Ui.Amber), Ui.Box("grow set-words", Ui.Text(pendingCents > 0 ? "Set up payouts to get paid" : "Set up payouts", "feed-name", Ui.Bold),
                    Ui.Text(pendingCents > 0 ? LessonInfo.Money(pendingCents) + " from " + n + " delivered lesson" + (n == 1 ? "" : "s") + " is waiting. Connect your bank to receive it." : "You can accept lessons now. Connect your bank to receive your earnings.", "muted wrap", Ui.Regular))).OnTap(() => _app.Push(new CoachLessonSetupScreen(_app)))));
            }
            List<object> normal = _rows.FindAll(b => !J.Flag(b, "isTutorial"));
            Section("Needs review", normal.FindAll(b => J.Int(b, "status") == 2 || J.Int(b, "status") == 3));
            Section("Waiting on session", normal.FindAll(b => J.Int(b, "status") == 1));
            Section("Delivered", normal.FindAll(b => J.Int(b, "status") >= 4));
            Section("Practice space", _rows.FindAll(b => J.Flag(b, "isTutorial")));
            if (_rows.Count == 0)
            {
                _body.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Icon("Tray", 30, Ui.Faint),
                    Ui.Text(live ? "No lessons yet. Once your profile is live, new bookings land here." : "No lessons yet. Record a review of a player's session in Player Review and choose Deliver as lesson.", "sub center wrap", Ui.Regular).Margin(8),
                    Ui.Button(live ? "Set Up Your Profile" : "Open Player Review", () => _app.Push(live ? (AppScreen)new CoachLessonSetupScreen(_app) : new CoachingDashboardScreen(_app))).Margin(10))));
            }
        }

        void Section(string title, List<object> rows)
        {
            if (rows.Count == 0) return;
            _body.Add(Ui.Section(title.ToUpperInvariant() + " · " + rows.Count));
            VisualElement cols = Ui.Box("cols");
            foreach (object b in rows) cols.Add(Ui.Box("col pad", Row(b)).Margin(6));
            _body.Add(cols);
        }

        VisualElement Row(object b)
        {
            int st = J.Int(b, "status");
            string kind = J.Flag(b, "isCertification") ? "CERTIFICATION · GRADED BY PUTTR" : J.Int(b, "isStudio") == 1 ? "STUDIO · YOUR PLAYER" : J.Flag(b, "isTutorial") ? "TUTORIAL · PRACTICE SPACE" : null;
            string line = st == 1 ? "Waiting on their session" : st == 2 ? "Session submitted " + Form.Ago(b, "submittedAt") : st == 3 ? "You started this review" : st == 9 ? "Expired and refunded" : "Delivered " + Form.Ago(b, "deliveredAt");
            VisualElement right = null;
            if (st == 2 || st == 3)
            {
                if (Form.When(b, "dueAt") is DateTime due)
                {
                    double hours = (due - DateTime.Now).TotalHours;
                    right = hours < 0 ? AdminLabels.Pill("Overdue", Ui.Red) : hours < 24 ? AdminLabels.Pill("Due in " + Math.Max(1, (int)hours) + "h", Ui.Red) : AdminLabels.Pill("Due " + due.ToString("ddd h tt", CultureInfo.InvariantCulture), Ui.Amber);
                }
            }
            else if (st >= 4) right = J.Get(b, "rating") != null ? LessonInfo.StarText(J.Num(b, "rating"), "muted") : J.Num(b, "price") > 0 ? (VisualElement)AdminLabels.Pill("+" + LessonInfo.Money(J.Num(b, "price") - J.Num(b, "platformFee")), Ui.Bright) : null;
            string hcp = J.Str(b, "playerHandicap");
            VisualElement card = Ui.Box("glass",
                kind != null ? AdminLabels.Pill(kind, kind.StartsWith("CERT") ? Ui.Blue : Ui.Bright) : null,
                Ui.Box("row", Ui.Avatar(J.Str(b, "playerName"), J.Str(b, "playerPhoto")), Ui.Box("grow set-words", Ui.Text(J.Str(b, "playerName") + (hcp.Length > 0 ? "  ·  hcp " + hcp : ""), "feed-name", Ui.Bold),
                    J.Str(b, "goals").Length > 0 ? Ui.Text(J.Str(b, "goals"), "muted", Ui.Regular) : null, Ui.Text(line, "muted", Ui.Regular)), right).Margin(kind != null ? 8 : 0));
            return card.OnTap(() => Coach.OpenBooking(_app, b));
        }
    }

    public sealed class LessonWorkspaceScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _body;
        object _booking, _session;
        bool _analysis;

        public LessonWorkspaceScreen(App app, string bookingId)
        {
            _app = app;
            _id = bookingId;
            Root = Page.Make(app, "Lesson", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/lesson/booking/" + _id, b =>
        {
            _booking = b;
            Draw();
            App.Run(Api.Get("/coaching/user/sessions/" + J.Str(b, "userId"), d => { _session = J.List(d).Find(s => J.Str(s, "id") == J.Str(b, "sessionId")); Draw(); }, _ => { }));
        }, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load this lesson.")); }));

        void Draw()
        {
            object b = _booking;
            _body.Clear();
            int st = J.Int(b, "status");
            string first = LessonInfo.First(J.Str(b, "playerName"));
            if (J.Flag(b, "isCertification")) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("Certification lesson", "feed-name", Ui.Bold), Ui.Text("Coach this exactly like a real lesson. Your delivery goes to Puttr for grading, not to a player.", "muted wrap", Ui.Regular))));
            if (J.Flag(b, "isTutorial")) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("Tutorial mode", "feed-name", Ui.Bold), Ui.Text("A practice space with real tools and a sample session. Nothing you do here reaches a real player.", "muted wrap", Ui.Regular))));
            string sub = (J.Str(b, "playerHandicap").Length > 0 ? "hcp " + J.Str(b, "playerHandicap") + " · " : "") + (Form.Date(b, "dueAt").Length > 0 ? "due " + Form.Date(b, "dueAt", "ddd MMM d, h:mm tt") : J.Int(b, "isStudio") == 1 ? "studio lesson" : "$" + (J.Num(b, "price") / 100).ToString("0", CultureInfo.InvariantCulture) + " lesson");
            _body.Add(Ui.Box("pad row", Ui.Avatar(J.Str(b, "playerName"), J.Str(b, "playerPhoto")), Ui.Box("grow set-words", Ui.Text(J.Str(b, "playerName"), "h2 wrap", Ui.Bold), Ui.Text(sub, "muted", Ui.Regular)), Recorder.Pill(Root)));
            _body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "Session", "Analysis" }, _analysis ? 1 : 0, i => { _analysis = i == 1; Draw(); })));
            if (_analysis)
            {
                // PlayerStatsPanel's numbers: the Player's profile (stats, PQS, recent games).
                _body.Add(Ui.Box("pad", Ui.Button("Open " + first + "'s profile and stats", () => ProfileScreen.Open(_app, J.Str(b, "userId")), "btn btn-ghost")));
            }
            else
            {
                if (J.Str(b, "goals").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text(first + "'s goals", "feed-name", Ui.Bold), Ui.Text("“" + J.Str(b, "goals") + "”", "sub wrap", Ui.Regular).Margin(6))));
                _body.Add(Ui.Section("Submitted session"));
                if (st == 1 || J.Str(b, "sessionId").Length == 0) _body.Add(Page.Note("Waiting on " + first + " to submit a session. Their turnaround clock has not started."));
                else if (_session == null) _body.Add(Page.Loading());
                else
                {
                    object s = _session;
                    _body.Add(Ui.Box("pad", Ui.Box("glass", LessonInfo.SessionRow(s, AdminLabels.Pill(Coach.Grade(J.Num(s, "score")), Ui.Bright)),
                        Ui.Box("glass row", Ui.Icon("Play", 20, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Watch the session", "feed-name", Ui.Bold), Ui.Text("Replay, 3D pose and the coach review", "muted", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                            .Margin(10).OnTap(() => _app.Push(new LabSessionScreen(_app, s))))));
                }
            }
            if (st == 2 || st == 3)
            {
                VisualElement bar = Ui.Box("pad");
                if (st == 2) bar.Add(Ui.Button("Start Review", () => App.Run(Api.Post("/lesson/coaching/start", new Dictionary<string, object> { { "bookingId", _id } }, _ =>
                {
                    Dialog.Ask(Root, "Record your lesson", Recorder.Supported ? "Tap REC, narrate your review of their session, then tap the red timer and save it. Then choose Deliver Lesson and pick the recording." : "Use your phone's screen recorder from Quick Settings, narrate your review, then upload the recording in Deliver.", "Got it", () => { });
                    Shown();
                }, e => _app.Toast(Form.Why(e, "Could not start the review."))))));
                bar.Add(Ui.Button("Deliver Lesson", () => _app.Push(new LessonDeliverScreen(_app, _booking)), st == 2 ? "btn btn-ghost" : "btn").Margin(8));
                _body.Add(bar);
            }
        }
    }

    public sealed class LessonDeliverScreen : AppScreen
    {
        readonly App _app;
        object _booking;
        readonly VisualElement _body;
        string _summary = "", _videoId;
        double _length;
        bool _uploading, _sending;
        float _progress;

        public LessonDeliverScreen(App app, object booking)
        {
            _app = app;
            _booking = booking;
            Root = Page.Make(app, "Deliver Lesson", out _body);
            Draw();
        }

        public override void Shown() => App.Run(Api.Get("/lesson/booking/" + J.Str(_booking, "id"), b => { _booking = b; Draw(); }, _ => { }));

        void Draw()
        {
            object b = _booking;
            _body.Clear();
            string name = J.Str(b, "playerName"), first = LessonInfo.First(name);
            bool cert = J.Flag(b, "isCertification"), tutorial = J.Flag(b, "isTutorial"), studio = J.Int(b, "isStudio") == 1;
            VisualElement p = Ui.Box("pad", Ui.Text("to " + name, "sub", Ui.Regular));
            p.Add(Form.Label("1 · Lesson video"));
            if (_uploading) p.Add(Ui.Box("glass", Ui.Text("Uploading... " + Mathf.RoundToInt(_progress * 100) + "%", "feed-name", Ui.Bold), Ui.Progress(_progress).Margin(8), Ui.Text("Keep the app open until the upload finishes.", "muted", Ui.Regular).Margin(6)));
            else if (_videoId != null) p.Add(Ui.Box("glass row", Ui.Icon("CheckCircle", 24, Ui.Green), Ui.Box("grow set-words", Ui.Text("Video uploaded", "feed-name", Ui.Bold), Ui.Text(Form.Duration(_length) + " · ready to send", "muted", Ui.Regular)), Ui.Text("Replace", "sec-link", Ui.Medium).OnTap(Pick)));
            else p.Add(Ui.Box("dashed", Ui.Icon("VideoCamera", 36, Ui.Faint), Ui.Text("Upload your lesson video", "feed-name", Ui.Bold).Margin(10), Ui.Text("Tap REC while reviewing their session to record your breakdown in the app, or pick a video from your camera roll.", "muted center wrap", Ui.Regular).Margin(4)).OnTap(Pick));
            p.Add(Form.Label("2 · Summary notes"));
            TextField notes = Form.Field("What you saw, what to work on, and what success looks like for " + first + ".", _summary, 4000, true);
            notes.RegisterValueChangedCallback(e => _summary = e.newValue);
            p.Add(notes);
            p.Add(Form.Label("3 · Practice plan"));
            bool hasPlan = J.Str(b, "planId").Length > 0;
            p.Add(Ui.Box(hasPlan ? "glass row choice-on" : "glass row", Ui.Icon("ListChecks", 22, Ui.Bright), Ui.Box("grow set-words", Ui.Text(hasPlan ? "Practice plan attached" : "Add a practice plan", "feed-name", Ui.Bold), Ui.Text(hasPlan ? "Tap to edit games, targets, and duration" : "Assign games and drills. Strongly recommended.", "muted wrap", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                .OnTap(() => _app.Push(hasPlan ? new AssignPlanScreen(_app, null, null, null, J.Str(b, "planId")) : new AssignPlanScreen(_app, J.Str(b, "id"), null, name, null))));
            string info = cert ? "Graded by Puttr" : tutorial ? "Practice run" : studio ? "Studio lesson" : "You earn";
            string infoSub = cert ? "Your delivery goes to Puttr for grading, not to a player." : tutorial ? "Nothing here reaches a real player." : studio ? "Sent to your player, no payment." : "Paid through Stripe after delivery";
            p.Add(Ui.Box("glass row", Ui.Box("grow set-words", Ui.Text(info, "feed-name", Ui.Bold), Ui.Text(infoSub, "muted wrap", Ui.Regular)), !cert && !tutorial && !studio ? Ui.Text(LessonInfo.Money(J.Num(b, "price") - J.Num(b, "platformFee")), "h2 wrap", Ui.Bold) : null).Margin(16));
            _body.Add(p);
            _body.Add(Form.Bottom(Ui.Button(_sending ? "Sending..." : cert ? "Submit for Grading" : tutorial ? "Finish Practice Delivery" : "Send Lesson to " + first, () => App.Run(Send(cert, tutorial, first)))));
        }

        void Pick()
        {
            PhotoPicker.PickVideo(path =>
            {
                if (path == null) return;
                App.Run(Uploads.Length(path, seconds =>
                {
                    _length = Math.Max(0, seconds);
                    _uploading = true; _progress = 0; Draw();
                    string file = "lesson_vid_" + J.Str(_booking, "id") + "_" + UnityEngine.Random.Range(0, 100000000) + ".mp4";
                    App.Run(Uploads.Video(path, file, f => { _progress = f; Draw(); }, url =>
                    {
                        if (url == null) { _uploading = false; Draw(); Dialog.Ask(Root, "Upload failed", "Could not upload your video. Try again.", "OK", () => { }); return; }
                        var save = new Dictionary<string, object> { { "fileName", file }, { "userId", J.Str(_booking, "userId") }, { "coachId", J.Str(_app.Profile, "userId", Auth.Uid) }, { "videoTitle", "Lesson for " + J.Str(_booking, "playerName") },
                            { "videoLength", Math.Round(_length) }, { "videoWidth", 1080 }, { "videoHeight", 1920 }, { "quiet", true } };
                        App.Run(Api.Post("/coaching/saveVideo", save, r => { _uploading = false; _videoId = J.Str(r, "insertId"); Draw(); }, e => { _uploading = false; Draw(); _app.Toast(Form.Why(e, "Could not save the video.")); }));
                    }));
                }));
            });
        }

        IEnumerator Send(bool cert, bool tutorial, string first)
        {
            if (_sending) yield break;
            if (_videoId == null) { Dialog.Ask(Root, "Add your lesson video", "Record or pick the video of your review first.", "OK", () => { }); yield break; }
            if (_summary.Trim().Length == 0) { Dialog.Ask(Root, "Add a summary", "Tell " + first + " what you saw and what to work on.", "OK", () => { }); yield break; }
            _sending = true; Draw();
            object done = null; string error = null;
            yield return Api.Post("/lesson/coaching/deliver", new Dictionary<string, object> { { "bookingId", J.Str(_booking, "id") }, { "videoId", _videoId }, { "coachSummary", _summary.Trim() }, { "findings", null } }, d => done = d, e => error = e ?? "failed");
            _sending = false;
            if (done == null) { Draw(); Dialog.Ask(Root, "Could not send", Form.Why(error, "Try again in a moment."), "OK", () => { }); yield break; }
            Coach.RefreshPending();
            Dialog.Ask(_app.Toasts, cert ? "Submitted for grading" : tutorial ? "Practice delivery complete" : "Lesson sent!", cert ? "Puttr will grade your lesson and let you know." : tutorial ? "That is the whole flow. Real lessons work the same way." : first + " got a notification and can watch it now.", "Done", () => { });
            while (_app.Top != null && !(_app.Top is LessonInboxScreen)) _app.Pop();
        }
    }

    public sealed class LessonDeliveredScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _body;

        public LessonDeliveredScreen(App app, string bookingId)
        {
            _app = app;
            _id = bookingId;
            Root = Page.Make(app, "Lesson", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/lesson/booking/" + _id, Draw, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load this lesson.")); }));

        void Draw(object b)
        {
            _body.Clear();
            string first = LessonInfo.First(J.Str(b, "playerName"));
            bool studio = J.Int(b, "isStudio") == 1, special = J.Flag(b, "isCertification") || J.Flag(b, "isTutorial");
            string sub = "Delivered " + Form.Ago(b, "deliveredAt") + (studio ? " · studio lesson" : J.Num(b, "price") > 0 ? " · you earned " + LessonInfo.Money(J.Num(b, "price") - J.Num(b, "platformFee")) : "");
            _body.Add(Ui.Box("pad row", Ui.Avatar(J.Str(b, "playerName"), J.Str(b, "playerPhoto")), Ui.Box("grow set-words", Ui.Text(J.Str(b, "playerName"), "h2 wrap", Ui.Bold), Ui.Text(sub, "muted wrap", Ui.Regular)), J.Int(b, "status") == 5 ? AdminLabels.Pill("Completed", Ui.Faint) : null));
            if (special) _body.Add(Page.Note(J.Flag(b, "isCertification") ? "Your certification lesson is with Puttr for grading." : "This was a practice delivery. Nothing reached a real player."));
            else
            {
                VisualElement card = Ui.Box("glass set-card",
                    Ui.Box("set-row", Ui.Text("Watched", "sub grow", Ui.Regular), Ui.Text(J.Str(b, "watchedAt").Length > 0 ? "Watched" : "Not watched yet", "feed-name", Ui.Bold)));
                if (!studio) card.Add(Ui.Box("set-row", Ui.Text("Rating", "sub grow", Ui.Regular), J.Get(b, "rating") != null ? (VisualElement)LessonInfo.StarText(J.Num(b, "rating")) : Ui.Text("No rating yet", "muted", Ui.Regular)));
                if (J.Str(b, "review").Length > 0) card.Add(Ui.Text(first + "'s review: “" + J.Str(b, "review") + "”", "sub wrap", Ui.Regular).Margin(8));
                _body.Add(Ui.Box("pad", card));
            }
            if (J.Str(b, "planId").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("glass row", Ui.Icon("ListChecks", 22, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Practice plan", "feed-name", Ui.Bold), Ui.Text("Tap for the full monitor", "muted", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                .OnTap(() => _app.Push(new PlayerMonitorScreen(_app, J.Str(b, "planId"))))));
            else if (!special) _body.Add(Page.Note("No practice plan was attached to this lesson."));
            _body.Add(Ui.Section("Your lesson video"));
            if (J.Str(b, "videoUrl").Length > 0) _body.Add(Ui.Box("pad", VideoBox.Make(J.Str(b, "videoUrl"), 220, J.Str(b, "videoThumbnail"), J.Num(b, "videoLength")),
                J.Str(b, "videoTranscodeStatus") == "pending" ? Ui.Text("Optimizing for streaming…", "muted", Ui.Regular).Margin(6) : null));
            else _body.Add(Page.Note("The lesson video is still processing. Check back in a moment."));
            if (J.Str(b, "coachSummary").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("Your notes", "feed-name", Ui.Bold), Ui.Text(J.Str(b, "coachSummary"), "sub wrap", Ui.Regular).Margin(6))));
            if (J.Str(b, "goals").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text(first + "'s goals", "feed-name", Ui.Bold), Ui.Text(J.Str(b, "goals"), "sub wrap", Ui.Regular).Margin(6))));
            _body.Add(Ui.Box("pad", Ui.Box("glass row", Ui.Box("grow set-words", Ui.Text("Revisit the session", "feed-name", Ui.Bold), Ui.Text(first + "'s submitted session and putting analysis", "muted wrap", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                .OnTap(() => _app.Push(new LessonWorkspaceScreen(_app, _id)))));
        }
    }

    // ── The coaching profile and payouts ─────────────────────────────────────

    public sealed class CoachLessonSetupScreen : AppScreen
    {
        static readonly int[] Turnarounds = { 24, 48, 72, 168 };
        static readonly string[] Keys = { "STROKE", "SPEED", "GREEN_READING", "PRESSURE" };
        readonly App _app;
        readonly VisualElement _body, _payouts;
        string _headline = "", _bio = "", _price = "";
        int _turnaround = 72;
        bool _accepting;
        readonly HashSet<string> _specialties = new HashSet<string>();
        bool _saving;

        public CoachLessonSetupScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Coaching Profile", out _body);
            _payouts = new VisualElement();
            _body.Add(Page.Loading());
            App.Run(Api.Get("/lesson/coaching/profile", d =>
            {
                _headline = J.Str(d, "headline"); _bio = J.Str(d, "bio");
                _price = J.Num(d, "lessonPrice") > 0 ? J.Num(d, "lessonPrice").ToString("0", CultureInfo.InvariantCulture) : "";
                _turnaround = J.Int(d, "turnaroundHours", 72);
                _accepting = J.Int(d, "acceptingLessons") == 1;
                foreach (string s in J.Str(d, "specialties").Split(',')) if (s.Trim().Length > 0) _specialties.Add(s.Trim());
                Draw();
            }, _ => Draw()));
        }

        public override void Shown() => App.Run(Api.Get("/lesson/coaching/stripe/status", Payouts, _ => { }));

        void Draw()
        {
            _body.Clear();
            VisualElement p = Ui.Box("pad", Ui.Text("How players find and book you for online lessons", "sub wrap", Ui.Regular));
            p.Add(Form.Label("Photo"));
            VisualElement photo = Ui.Avatar(J.Str(_app.Profile, "displayName"), J.Str(_app.Profile, "photoURL"), true);
            p.Add(Ui.Box("row", photo, Ui.Text("Change photo", "sec-link", Ui.Medium).OnTap(() => PhotoPicker.ChangeProfilePhoto(_app, ok => { if (ok) Draw(); }))));
            p.Add(Form.Label("Headline"));
            TextField headline = Form.Field("Short game specialist, 20 yrs teaching", _headline, 120);
            headline.RegisterValueChangedCallback(e => _headline = e.newValue);
            p.Add(headline);
            p.Add(Form.Label("About you"));
            TextField bio = Form.Field("Who you coach and how", _bio, 2000, true);
            bio.RegisterValueChangedCallback(e => _bio = e.newValue);
            p.Add(bio);
            p.Add(Form.Label("Lesson price"));
            TextField price = Form.Field("50", _price, 4);
            price.keyboardType = TouchScreenKeyboardType.NumberPad;
            Label priceNote = Ui.Text("", "muted wrap", Ui.Regular);
            void Note() { double x = double.TryParse(_price, out double v) ? v : 50; priceNote.text = "You receive the full $" + x.ToString("0") + " per lesson. Players pay " + LessonInfo.Price(Math.Round(x * 1.10, 2)) + " (members) or " + LessonInfo.Price(Math.Round(x * 1.20, 2)) + " (non-members). Minimum price $50."; }
            price.RegisterValueChangedCallback(e => { _price = System.Text.RegularExpressions.Regex.Replace(e.newValue, "[^0-9]", ""); Note(); });
            Note();
            p.Add(price); p.Add(priceNote);
            p.Add(Form.Label("Turnaround promise"));
            p.Add(Form.Pills(new[] { "24 hrs", "48 hrs", "72 hrs", "1 week" }, Array.IndexOf(Turnarounds, _turnaround), i => _turnaround = Turnarounds[i]));
            p.Add(Ui.Text("The clock starts when a player submits their session. Miss it by 4 days and they get an automatic refund.", "muted wrap", Ui.Regular));
            p.Add(Form.Label("Specialties"));
            VisualElement pills = Ui.Box("pills");
            foreach (string k in Keys)
            {
                string key = k;
                VisualElement pill = Ui.Box(_specialties.Contains(k) ? "scope scope-on" : "scope", Ui.Text(LessonInfo.Specialties[k], "scope-text", Ui.Bold));
                pills.Add(pill.OnTap(() => { if (!_specialties.Remove(key)) _specialties.Add(key); pill.EnableInClassList("scope-on", _specialties.Contains(key)); }));
            }
            p.Add(pills);
            p.Add(_payouts.Margin(12));
            p.Add(Ui.Box("glass row", Ui.Box("grow set-words", Ui.Text("Accepting new lessons", "feed-name", Ui.Bold), Ui.Text("Pause anytime. Lessons already booked are unaffected.", "muted wrap", Ui.Regular)), Rows.Switch(_accepting, on => _accepting = on)).Margin(12));
            _body.Add(p);
            _body.Add(Form.Bottom(Ui.Button(_saving ? "Saving..." : "Save Profile", Save)));
        }

        void Payouts(object s)
        {
            _payouts.Clear();
            bool enabled = J.Flag(s, "payoutsEnabled"), onboarded = J.Flag(s, "onboarded");
            VisualElement card = Ui.Box("glass", Ui.Box("row", Ui.Icon(enabled ? "CheckCircle" : "Warning", 22, enabled ? Ui.Green : Ui.Amber), Ui.Text(enabled ? "Payouts enabled" : onboarded ? "Finish payout setup" : "Set up payouts", "feed-name grow", Ui.Bold).Margin(0)),
                Ui.Text(enabled ? "You get paid through Stripe when each lesson is delivered." : "You can accept lessons now. Connect a bank account with Stripe to receive your earnings, we hold them safely until then.", "muted wrap", Ui.Regular).Margin(6));
            object bal = J.Get(s, "balance");
            if (enabled && bal != null)
            {
                string next = Form.When(J.Get(s, "nextPayout"), "arrivalDate") is DateTime t ? "Next payout " + t.ToString("MMM d", CultureInfo.InvariantCulture) : "Paid to your bank automatically";
                card.Add(Ui.Box("set-row", Ui.Box("grow set-words", Ui.Text("ON THE WAY", "stat-label", Ui.Bold), Ui.Text(LessonInfo.Money(J.Num(bal, "availableCents") + J.Num(bal, "pendingCents")) + " " + J.Str(bal, "currency").ToUpperInvariant(), "stat-value", Ui.Bold), Ui.Text(next, "muted", Ui.Regular))));
            }
            if (!enabled && J.Num(s, "pendingCents") > 0) card.Add(AdminLabels.Pill(LessonInfo.Money(J.Num(s, "pendingCents")) + " waiting for payout", Ui.Amber).Margin(8));
            card.Add(Ui.Button(enabled ? "Open Stripe dashboard" : "Set up with Stripe", () => App.Run(Api.Post(enabled ? "/lesson/coaching/stripe/dashboard" : "/lesson/coaching/stripe/onboard", new Dictionary<string, object>(),
                d => { if (J.Str(d, "url").Length > 0) Application.OpenURL(J.Str(d, "url")); }, e => Dialog.Ask(Root, "Stripe", Form.Why(e, "Could not open Stripe. Try again."), "OK", () => { })))).Margin(10));
            _payouts.Add(card);
        }

        void Save()
        {
            if (_saving) return;
            int.TryParse(_price, out int price);
            if (_accepting && price <= 0) { Dialog.Ask(Root, "Set a price", "Add your lesson price to accept lessons.", "OK", () => { }); return; }
            if (price > 0 && price < 50) { Dialog.Ask(Root, "Price too low", "The minimum lesson price is $50.", "OK", () => { }); return; }
            _saving = true; Draw();
            App.Run(Api.Post("/lesson/coaching/profile", new Dictionary<string, object> { { "headline", _headline }, { "bio", _bio }, { "lessonPrice", price }, { "turnaroundHours", _turnaround }, { "specialties", string.Join(",", _specialties) }, { "acceptingLessons", _accepting ? 1 : 0 } },
                _ => { _saving = false; _app.Toast("Profile saved."); Draw(); }, e => { _saving = false; Draw(); Dialog.Ask(Root, "Could not save", Form.Why(e, "Try again in a moment."), "OK", () => { }); }));
        }
    }

    // ── Practice plans ───────────────────────────────────────────────────────

    public sealed class AssignPlanScreen : AppScreen
    {
        static readonly string[] Skills = { "All", "Distance", "Speed", "Entry angle", "Pressure", "Custom" };
        readonly App _app;
        readonly string _bookingId, _userId, _planId, _player;
        readonly VisualElement _body, _list;
        readonly List<Dictionary<string, object>> _items = new List<Dictionary<string, object>>();
        List<object> _catalog = new List<object>(), _custom = new List<object>();
        int _weeks = 2, _skill;
        string _search = "", _start;
        bool _saving;

        public AssignPlanScreen(App app, string bookingId, string userId, string playerName, string planId)
        {
            _app = app;
            _bookingId = bookingId; _userId = userId; _planId = planId; _player = playerName;
            Root = Page.Make(app, planId != null ? "Edit Plan" : "Practice Plan", out _body);
            _list = new VisualElement();
            _body.Add(Page.Loading());
            int waiting = planId != null ? 3 : 2;
            void Done() { if (--waiting == 0) Draw(); }
            App.Run(Api.Get("/games/ALL", d => { _catalog = J.List(d).FindAll(g => { string t = J.Str(g, "gameType"); return t != "ASSESSMENT" && t != "OPEN" && !GameBridge.Retired(g); }); Done(); }, _ => Done()));
            App.Run(Api.Get("/creator/games/", d => { _custom = J.List(d); Done(); }, _ => Done()));
            if (planId != null) App.Run(Api.Get("/lesson/plan/" + planId, d =>
            {
                object plan = J.Get(d, "plan");
                _start = J.Str(plan, "startDate");
                if (Form.When(plan, "startDate") is DateTime s && Form.When(plan, "endDate") is DateTime e) { double w = Math.Round((e - s).TotalDays / 7); _weeks = w <= 1 ? 1 : w <= 3 ? 2 : 4; }
                foreach (object it in J.List(J.Get(d, "items"))) _items.Add(new Dictionary<string, object> { { "gameId", J.Get(it, "gameId") }, { "title", J.Str(it, "gameTitle") }, { "gameType", J.Str(it, "gameType") }, { "targetCount", J.Int(it, "targetCount", 3) }, { "note", J.Str(it, "note") } });
                if (_player == null) _app.Toast("Editing " + J.Str(plan, "playerName") + "'s plan");
                Done();
            }, _ => Done()));
        }

        void Draw()
        {
            _body.Clear();
            string first = LessonInfo.First(_player);
            VisualElement p = Ui.Box("pad", Ui.Text(_planId != null ? "for " + first + " · their progress so far is kept" : _userId != null ? "for " + first + " · they get notified right away" : "for " + first + " · attaches to this lesson", "sub wrap", Ui.Regular));
            p.Add(Form.Label("Duration"));
            if (_planId != null && Form.When(new Dictionary<string, object> { { "s", _start } }, "s") is DateTime st) p.Add(Ui.Text("Counted from the plan start, " + st.ToString("MMM d", CultureInfo.InvariantCulture) + ".", "muted", Ui.Regular));
            p.Add(FreePuttScreen.Choice(new[] { "1 week", "2 weeks", "4 weeks" }, _weeks == 1 ? 0 : _weeks == 2 ? 1 : 2, i => _weeks = i == 0 ? 1 : i == 1 ? 2 : 4));
            if (_items.Count > 0)
            {
                p.Add(Form.Label("Selected · " + _items.Count));
                foreach (Dictionary<string, object> it in _items)
                {
                    Dictionary<string, object> item = it;
                    p.Add(Ui.Box("glass", Ui.Box("row", Ui.Bubble(PlanDetailScreen.GameIconsFor(J.Str(it, "gameType")), Ui.Bright), Ui.Text(J.Str(it, "title"), "feed-name grow", Ui.Bold).Margin(0).OnTap(() => Configure(item, null)),
                        AdminLabels.Pill(J.Int(it, "targetCount") + "x", Ui.Bright), Ui.Icon("Trash", 18, Ui.Faint).OnTap(() => { _items.Remove(item); Draw(); })),
                        J.Str(it, "note").Length > 0 ? Ui.Text("“" + J.Str(it, "note") + "”", "muted wrap", Ui.Regular).Margin(6) : null).Margin(8));
                }
            }
            p.Add(Form.Label("Games & drills"));
            TextField search = Form.Field("Search all games and drills...", _search);
            search.RegisterValueChangedCallback(e => { _search = e.newValue; List(); });
            p.Add(search);
            p.Add(Form.Pills(Skills, _skill, i => { _skill = i; List(); }));
            _body.Add(p);
            _body.Add(_list);
            List();
            string verb = _planId != null ? "Save Changes" : _userId != null ? "Assign Plan" : "Attach Plan";
            _body.Add(Form.Bottom(Ui.Button(_saving ? "Saving..." : verb + (_items.Count > 0 ? " · " + _items.Count + " item" + (_items.Count == 1 ? "" : "s") : ""), () => App.Run(Save()))));
        }

        void List()
        {
            _list.Clear();
            bool custom = _skill == Skills.Length - 1;
            string[] skillKeys = { null, "distance", "speed", "angle", "pressure" };
            List<object> games = (custom ? _custom : _catalog).FindAll(g =>
                (_search.Length == 0 || J.Str(g, "title").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (custom || _skill == 0 || J.Str(g, "skills").ToLowerInvariant().Contains(skillKeys[_skill])));
            if (games.Count == 0) { _list.Add(Page.Note(custom ? "You have no custom games yet." : "No games match your search.")); return; }
            VisualElement cols = Ui.Box("cols");
            foreach (object g in games)
            {
                object game = g;
                bool on = _items.Exists(i => J.Str(i, "gameId") == J.Str(g, "id"));
                string kind = J.Str(g, "gameType") == "PRACTICE" || J.Num(g, "multiPlayer") == 0 ? "Practice" : "Game";
                VisualElement tile = Ui.Box(on ? "glass row choice-on" : "glass row", Ui.Bubble(PlanDetailScreen.GameIconsFor(J.Str(g, "gameType")), Ui.Bright), Ui.Box("grow set-words", Ui.Text(J.Str(g, "title"), "feed-name", Ui.Bold), Ui.Text(kind + (J.Str(g, "skills").Length > 0 ? " · " + J.Str(g, "skills").Replace(",", ", ") : ""), "muted", Ui.Regular)), on ? Ui.Icon("CheckCircle", 20, Ui.Green) : null);
                cols.Add(Ui.Box("col pad", tile.OnTap(() => Configure(_items.Find(i => J.Str(i, "gameId") == J.Str(game, "id")), game))).Margin(6));
            }
            _list.Add(cols);
        }

        void Configure(Dictionary<string, object> item, object game)
        {
            bool existing = item != null;
            int count = existing ? J.Int(item, "targetCount", 3) : 3;
            string note = existing ? J.Str(item, "note") : "";
            VisualElement dim = null;
            TextField cue = Form.Field("Cue for the player (optional), e.g. eyes over the ball", note, 255, true);
            VisualElement card = Ui.Box("modal", Ui.Text(existing ? J.Str(item, "title") : J.Str(game, "title"), "h2 wrap", Ui.Bold),
                Ui.Text("Times per week", "feed-name", Ui.Bold).Margin(12), Ui.Text("How often to play it each week", "muted", Ui.Regular),
                FreePuttScreen.Stepper(() => count.ToString(), d => count = Mathf.Clamp(count + d, 1, 20)).Margin(8), cue.Margin(10),
                Ui.Button(existing ? "Update" : "Add", () =>
                {
                    dim.RemoveFromHierarchy();
                    if (!existing) { item = new Dictionary<string, object> { { "gameId", J.Get(game, "id") }, { "title", J.Str(game, "title") }, { "gameType", J.Str(game, "gameType") } }; _items.Add(item); }
                    item["targetCount"] = count;
                    item["note"] = cue.value.Trim();
                    Draw();
                }),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        IEnumerator Save()
        {
            if (_saving) yield break;
            if (_items.Count == 0) { Dialog.Ask(Root, "Add a game", "Pick at least one game or drill for the plan.", "OK", () => { }); yield break; }
            _saving = true; Draw();
            var items = new List<object>();
            foreach (Dictionary<string, object> i in _items) items.Add(new Dictionary<string, object> { { "gameId", J.Get(i, "gameId") }, { "targetCount", J.Int(i, "targetCount", 3) }, { "note", J.Str(i, "note").Length > 0 ? J.Str(i, "note") : null } });
            var body = new Dictionary<string, object> { { "durationWeeks", _weeks }, { "items", items } };
            if (_planId == null) { if (_bookingId != null) body["bookingId"] = _bookingId; else body["userId"] = _userId; }
            string error = null; bool ok = false;
            yield return Api.Post(_planId != null ? "/lesson/coaching/plan/" + _planId : "/lesson/coaching/plan", body, _ => ok = true, e => error = e ?? "failed");
            _saving = false;
            if (!ok) { Draw(); Dialog.Ask(Root, "Could not save plan", Form.Why(error, "Try again in a moment."), "OK", () => { }); yield break; }
            _app.Pop();
        }
    }

    public sealed class PlayerRosterScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;

        public PlayerRosterScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "My Players", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/lesson/coaching/players", Draw, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load your players.")); }));

        void Draw(object d)
        {
            _body.Clear();
            List<object> players = J.List(J.Get(d, "players")), ended = J.List(J.Get(d, "recentlyEnded"));
            _body.Add(Ui.Box("pad", Ui.Text("Active plans · " + players.Count, "muted", Ui.Medium)));
            if (players.Count == 0) _body.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Icon("UsersThree", 30, Ui.Faint), Ui.Text("No active practice plans. Assign a plan when you deliver a lesson.", "sub center wrap", Ui.Regular).Margin(8))));
            VisualElement cols = Ui.Box("cols");
            foreach (object p in players)
            {
                object row = p;
                int done = J.Int(p, "weekDone"), target = J.Int(p, "weekTarget");
                bool idle = J.Get(p, "idleDays") != null && J.Int(p, "idleDays") >= 3;
                Label pill = idle ? AdminLabels.Pill("Idle " + J.Int(p, "idleDays") + "d", Ui.Amber) : done >= target ? AdminLabels.Pill("On track", Ui.Bright) : AdminLabels.Pill("In progress", Ui.Faint);
                VisualElement bar = Ui.Progress(target > 0 ? done / (float)target : 0);
                if (idle) bar.Q(className: "bar-fill").style.backgroundColor = (Color)Ui.Amber;
                VisualElement card = Ui.Box("glass", Ui.Box("row", Ui.Avatar(J.Str(p, "playerName"), J.Str(p, "playerPhoto")), Ui.Box("grow set-words", Ui.Text(J.Str(p, "playerName"), "feed-name", Ui.Bold),
                    Ui.Text("Week " + J.Int(p, "weekNo") + " of " + J.Int(p, "totalWeeks") + " · " + done + " of " + target + " this week", "muted", Ui.Regular)), pill), bar.Margin(10));
                if (idle) card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = (Color)Ui.Amber;
                cols.Add(Ui.Box("col pad", card.OnTap(() => _app.Push(new PlayerMonitorScreen(_app, J.Str(row, "id"))))).Margin(6));
            }
            _body.Add(cols);
            if (ended.Count > 0)
            {
                _body.Add(Ui.Section("Recently ended"));
                VisualElement card = Ui.Box("glass set-card");
                foreach (object e in ended)
                {
                    object row = e;
                    VisualElement r = Ui.Box("set-row", Ui.Avatar(J.Str(e, "playerName"), J.Str(e, "playerPhoto")), Ui.Box("grow set-words", Ui.Text(J.Str(e, "playerName"), "feed-name", Ui.Bold), Ui.Text("Finished " + Form.Date(e, "completedAt"), "muted", Ui.Regular)),
                        J.Get(e, "rating") != null ? (VisualElement)LessonInfo.StarText(J.Num(e, "rating"), "muted") : Ui.Icon("CaretRight", 16, Ui.Faint));
                    r.style.opacity = 0.75f;
                    card.Add(r.OnTap(() => _app.Push(new PlayerMonitorScreen(_app, J.Str(row, "id")))));
                }
                _body.Add(Ui.Box("pad", card));
            }
        }
    }

    public sealed class PlayerMonitorScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _body;

        public PlayerMonitorScreen(App app, string planId)
        {
            _app = app;
            _id = planId;
            Root = Page.Make(app, "Practice Plan", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/lesson/plan/" + _id, Draw, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load this plan.")); }));

        void Draw(object d)
        {
            _body.Clear();
            object plan = J.Get(d, "plan");
            int status = J.Int(plan, "status");
            string name = J.Str(plan, "playerName"), first = LessonInfo.First(name);
            _body.Add(Ui.Box("pad row", Ui.Avatar(name, J.Str(plan, "playerPhoto")), Ui.Box("grow set-words", Ui.Text(name, "h2 wrap", Ui.Bold), Ui.Text(status == 2 ? "Plan finished " + Form.Date(plan, "completedAt") : "Plan ends " + Form.Date(plan, "endDate"), "muted", Ui.Regular)),
                AdminLabels.Pill(status == 2 ? "Ended" : "Active", status == 2 ? Ui.Faint : Ui.Bright)));
            object before = J.Get(J.Get(d, "progress"), "before"), after = J.Get(J.Get(d, "progress"), "after");
            VisualElement tiles = Ui.Box("pad stat-tiles");
            void Tile(string label, double? was, double? now, string value, bool lowerBetter = false)
            {
                if (was == null || now == null) return;
                double change = lowerBetter ? Math.Abs(was.Value) - Math.Abs(now.Value) : now.Value - was.Value;
                Label delta = Ui.Text(Math.Abs(change) < 0.5 ? "" : (change > 0 ? "▲ " : "▼ ") + Math.Abs(change).ToString("0", CultureInfo.InvariantCulture), "muted", Ui.Bold);
                delta.style.color = change > 0 ? Ui.Bright : Ui.Red;
                tiles.Add(Ui.Box("glass stat-tile", Ui.Text(label, "stat-label", Ui.Bold), Ui.Text(value, "stat-value", Ui.Bold), delta));
            }
            double? N(object o, string k) => J.Get(o, k) != null ? J.Num(o, k) : (double?)null;
            object Band(object o) => J.List(J.Get(o, "bands")).Find(b => J.Str(b, "label") == "6-8 ft");
            Tile("MAKE %", N(before, "makePct"), N(after, "makePct"), J.Int(after, "makePct") + "%");
            Tile("MAKE 6-8", N(Band(before), "pct"), N(Band(after), "pct"), J.Int(Band(after), "pct") + "%");
            double ro = J.Num(after, "avgRollout");
            Tile("PACE", N(before, "avgRollout"), N(after, "avgRollout"), (ro > 0 ? "+" : "") + ro.ToString("0", CultureInfo.InvariantCulture) + "\"", true);
            if (tiles.childCount > 0) _body.Add(tiles);
            VisualElement card = Ui.Box("glass", Ui.Text("PLAN PROGRESS", "stat-label", Ui.Bold));
            foreach (object it in J.List(J.Get(d, "items")))
            {
                int target = J.Int(it, "targetCount"), week = Mathf.Min(J.Int(it, "weekDone"), target);
                Label line = Ui.Text(week + " of " + target + " this week" + (week >= target ? " ✓" : ""), "muted", Ui.Regular);
                if (week >= target) line.style.color = Ui.Bright;
                card.Add(Ui.Box("", Ui.Box("row", Ui.Bubble(PlanDetailScreen.GameIconsFor(J.Str(it, "gameType")), Ui.Bright), Ui.Box("grow set-words", Ui.Text(J.Str(it, "gameTitle"), "feed-name", Ui.Bold), line)), Ui.Progress(target > 0 ? week / (float)target : 0).Margin(6)).Margin(10));
            }
            _body.Add(Ui.Box("pad", card));
            if (status != 2)
            {
                VisualElement buttons = Ui.Box("pad");
                if (status == 1) buttons.Add(Ui.Button("Edit plan", () => _app.Push(new AssignPlanScreen(_app, null, null, name, _id)), "btn btn-ghost"));
                buttons.Add(Ui.Button("Send a nudge", () => App.Run(Api.Post("/lesson/coaching/nudge", new Dictionary<string, object> { { "planId", _id } }, r =>
                {
                    if (J.Flag(r, "sent")) Dialog.Ask(Root, "Nudge sent", name + " will get a note from you.", "OK", () => { });
                    else Dialog.Ask(Root, "Already nudged today", "You can send one nudge per player per day.", "OK", () => { });
                }, e => _app.Toast(Form.Why(e, "Could not send the nudge."))))).Margin(8));
                _body.Add(buttons);
            }
            string until = Form.When(plan, "endDate") is DateTime end ? end.AddDays(7).ToString("MMM d", CultureInfo.InvariantCulture) : "";
            _body.Add(Ui.Box("pad", Ui.Text("You can see " + first + "'s plan progress until " + until + ".", "muted center wrap", Ui.Regular)));
        }
    }

    // ── Player Review ────────────────────────────────────────────────────────

    public sealed class CoachingDashboardScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body, _list;
        readonly List<object> _players = new List<object>();
        string _search = "";

        public CoachingDashboardScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Player Review", out _body);
            _list = new VisualElement();
            _body.Add(Page.Loading());
            int waiting = 2;
            var seen = new HashSet<string>();
            void Add(List<object> rows) { foreach (object r in rows) if (seen.Add(J.Str(r, "userId"))) _players.Add(r); }
            void Done()
            {
                if (--waiting > 0) return;
                _players.Sort((a, b) => string.Compare(J.Str(a, "displayName"), J.Str(b, "displayName"), StringComparison.OrdinalIgnoreCase));
                if (app.Profile != null && seen.Add(J.Str(app.Profile, "userId"))) _players.Insert(0, app.Profile);
                Draw();
            }
            App.Run(Api.Get("/profile/followers/", d => { Add(J.List(d)); Done(); }, _ => Done()));
            App.Run(Api.Get("/profile/following/", d => { Add(J.List(d)); Done(); }, _ => Done()));
        }

        void Draw()
        {
            _body.Clear();
            _body.Add(Ui.Box("pad row", Ui.Box("grow", Ui.Text("Select Player", "h2 wrap", Ui.Bold), Ui.Text(_players.Count + " player" + (_players.Count == 1 ? "" : "s"), "muted", Ui.Regular)),
                Ui.Box("btn-small row", Ui.Icon("UserPlus", 16, Color.white), Ui.Text(" Find player", "btn-text", Ui.Bold)).OnTap(() => _app.Push(new FindPlayersScreen(_app)))));
            if (_players.Count > 5)
            {
                TextField search = Form.Field("Search players...", _search);
                search.RegisterValueChangedCallback(e => { _search = e.newValue; List(); });
                _body.Add(Ui.Box("pad", search));
            }
            _body.Add(_list);
            List();
            VisualElement invite = Ui.Box("glass", Ui.Text("Player not on Puttr?", "feed-name", Ui.Bold), Ui.Text("Have them scan this to get the app and connect with you. They will show up in this list.", "muted wrap", Ui.Regular).Margin(6));
            _body.Add(Ui.Box("pad", invite).Margin(12));
            App.Run(Api.Get("/coach/invite", d =>
            {
                string link = J.Str(d, "link");
                if (link.Length == 0) return;
                VisualElement qr = Ui.Box("thumb");
                qr.style.width = qr.style.height = 140;
                Images.Load("https://api.qrserver.com/v1/create-qr-code/?size=280x280&data=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(link), t => { if (t != null) qr.style.backgroundImage = new StyleBackground(t); });
                invite.Add(qr.Margin(10));
                invite.Add(Ui.Button("Copy invite link", () => { GUIUtility.systemCopyBuffer = link; _app.Toast("Copied to clipboard"); }, "btn btn-ghost").Margin(10));
            }, _ => { }));
        }

        void List()
        {
            _list.Clear();
            string me = J.Str(_app.Profile, "userId");
            VisualElement card = Ui.Box("glass set-card");
            int n = 0;
            foreach (object p in _players)
            {
                if (_search.Length > 0 && J.Str(p, "displayName").IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0 && J.Str(p, "homeTown").IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                object player = p;
                n++;
                string hcp = J.Str(p, "realHandicap", J.Str(p, "handicap"));
                if (hcp.StartsWith("-")) hcp = "+" + hcp.Substring(1);
                card.Add(Ui.Box("set-row", Ui.Avatar(J.Str(p, "displayName"), J.Str(p, "photoURL")), Ui.Box("grow set-words", Ui.Box("row", Ui.Text(J.Str(p, "displayName"), "feed-name", Ui.Bold), J.Str(p, "userId") == me ? AdminLabels.Pill("YOU", Ui.Bright) : null), Ui.Text(J.Str(p, "homeTown"), "muted", Ui.Regular)),
                    hcp.Length > 0 ? Ui.Box("", Ui.Text(hcp, "feed-name center", Ui.Bold), Ui.Text("HCP", "stat-label center", Ui.Bold)) : null).OnTap(() => _app.Push(new CoachPlayerScreen(_app, player))));
            }
            _list.Add(n == 0 ? Page.Note(_search.Length > 0 ? "No players match your search" : "No players found") : Ui.Box("pad", card));
        }
    }

    /// <summary>UserView: a Player's goals and notes, their plan, their sessions.</summary>
    public sealed class CoachPlayerScreen : AppScreen
    {
        readonly App _app;
        readonly object _player;
        readonly VisualElement _body;
        object _meta, _plan;
        List<object> _sessions;

        public CoachPlayerScreen(App app, object player)
        {
            _app = app;
            _player = player;
            Root = Page.Make(app, J.Str(player, "displayName"), out _body);
        }

        bool Self => J.Str(_player, "userId") == J.Str(_app.Profile, "userId");

        public override void Shown()
        {
            string id = J.Str(_player, "userId");
            int waiting = Self ? 1 : 3;
            void Done() { if (--waiting == 0) Draw(); }
            if (!Self)
            {
                App.Run(Api.Get("/coaching/playerMeta/" + id, d => { _meta = d; Done(); }, _ => Done()));
                App.Run(Api.Get("/lesson/coaching/players", d => { _plan = J.List(J.Get(d, "players")).Find(p => J.Str(p, "userId") == id); Done(); }, _ => Done()));
            }
            App.Run(Api.Get("/coaching/user/sessions/" + id, d => { _sessions = J.List(d); Done(); }, _ => { _sessions = new List<object>(); Done(); }));
        }

        void Draw()
        {
            _body.Clear();
            object p = _player;
            string first = LessonInfo.First(J.Str(p, "displayName"));
            _body.Add(Ui.Box("pad row", Ui.Avatar(J.Str(p, "displayName"), J.Str(p, "photoURL"), true), Ui.Box("grow set-words", Ui.Text(J.Str(p, "displayName"), "h2 wrap", Ui.Bold), Ui.Text(J.Str(p, "homeTown"), "muted", Ui.Regular)), Recorder.Pill(Root)));
            if (!Self)
            {
                VisualElement tools = Ui.Box("pad row");
                if (_plan != null) tools.Add(Ui.Button("Practice Plan", () => _app.Push(new PlayerMonitorScreen(_app, J.Str(_plan, "id"))), "btn btn-ghost grow"));
                else tools.Add(Ui.Button("Assign Plan", () => _app.Push(new AssignPlanScreen(_app, null, J.Str(p, "userId"), J.Str(p, "displayName"), null)), "btn btn-ghost grow"));
                bool empty = J.Str(_meta, "goals").Length == 0 && J.Str(_meta, "notes").Length == 0;
                VisualElement notes = Ui.Button(empty ? "Add Notes" : "Goals & Notes", Meta, "btn btn-ghost grow");
                notes.style.marginLeft = 8;
                tools.Add(notes);
                _body.Add(tools);
                if (_plan != null) _body.Add(Ui.Box("pad", Ui.Text("Week " + J.Int(_plan, "weekNo") + " of " + J.Int(_plan, "totalWeeks") + " · " + J.Int(_plan, "weekDone") + " of " + J.Int(_plan, "weekTarget") + " sessions this week", "muted", Ui.Regular)));
            }
            _body.Add(Ui.Section("Sessions"));
            if (_sessions.Count == 0) _body.Add(Page.Note("No recorded sessions yet"));
            VisualElement cols = Ui.Box("cols");
            foreach (object s in _sessions)
            {
                object session = s;
                object check = J.Parse(J.Str(s, "captureCheck"));
                object halves = J.Get(check, "halves");
                VisualElement views = halves != null ? Ui.Box("row", AdminLabels.Pill("DTL", J.Str(halves, "first") == "downTheLine" ? Ui.Bright : Ui.Faint), AdminLabels.Pill("FACE ON", J.Str(halves, "second") == "faceOn" ? Ui.Bright : Ui.Faint)) : null;
                cols.Add(Ui.Box("col pad", Ui.Box("glass", LessonInfo.SessionRow(s, AdminLabels.Pill(Coach.Grade(J.Num(s, "score")) + " · " + J.Int(s, "score"), Ui.Bright)), views)
                    .OnTap(() => _app.Push(new LabSessionScreen(_app, session)))).Margin(6));
            }
            _body.Add(cols);
            if (!Self) _body.Add(Ui.Box("pad", Ui.Button("Deliver a session review as a lesson", () => StudioLesson(), "btn btn-ghost")).Margin(12));
        }

        // A studio lesson: POST /lesson/coaching/studio/start with the newest session, then Deliver.
        void StudioLesson()
        {
            if (_sessions.Count == 0) { _app.Toast("They need a recorded session first."); return; }
            App.Run(Api.Post("/lesson/coaching/studio/start", new Dictionary<string, object> { { "userId", J.Str(_player, "userId") }, { "sessionType", "LAB" }, { "sessionId", J.Get(_sessions[0], "id") } },
                d => { object b = J.Get(d, "booking"); App.Run(Api.Get("/lesson/booking/" + J.Str(b, "id"), full => _app.Push(new LessonDeliverScreen(_app, full)), _ => _app.Push(new LessonDeliverScreen(_app, b)))); },
                e => _app.Toast(Form.Why(e, "Could not start the lesson."))));
        }

        void Meta()
        {
            VisualElement dim = null;
            TextField goals = Form.Field("What they're working toward, e.g. stop leaving putts short", J.Str(_meta, "goals"), 2000, true);
            TextField notes = Form.Field("Lesson history, tendencies, what to check next time...", J.Str(_meta, "notes"), 4000, true);
            VisualElement card = Ui.Box("modal", Ui.Text(LessonInfo.First(J.Str(_player, "displayName")), "h2 wrap", Ui.Bold),
                Form.Label("Goals · shown on their review"), goals, Form.Label("Private notes · only you see these"), notes,
                Ui.Button("Save", () =>
                {
                    dim.RemoveFromHierarchy();
                    string g = goals.value.Trim(), n = notes.value.Trim();
                    App.Run(Api.Post("/coaching/playerMeta", new Dictionary<string, object> { { "userId", J.Str(_player, "userId") }, { "goals", g.Length > 0 ? g : null }, { "notes", n.Length > 0 ? n : null } }, d => { _meta = d; Draw(); }, e => _app.Toast(Form.Why(e, "Could not save."))));
                }),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(Root).Add(dim);
        }
    }

    // ── The coach's videos ───────────────────────────────────────────────────

    public sealed class CoachingVideosScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        List<object> _videos = new List<object>();
        string _search = "";
        float _progress = -1;

        public CoachingVideosScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Videos", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/coaching/videos/", d => { _videos = J.List(d); Draw(); }, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load your videos.")); }));

        void Draw()
        {
            _body.Clear();
            _body.Add(Ui.Box("pad row", Ui.Box("grow"), Ui.Box("btn-small", Ui.Text(_progress >= 0 ? Mathf.RoundToInt(_progress * 100) + "%" : "+ Upload", "btn-text", Ui.Bold)).OnTap(Upload)));
            if (_videos.Count >= 5)
            {
                TextField search = Form.Field("Search videos...", _search);
                search.RegisterValueChangedCallback(e => { _search = e.newValue; Draw(); search.Focus(); });
                _body.Add(Ui.Box("pad", search));
            }
            List<object> shown = _videos.FindAll(v => _search.Length == 0 || J.Str(v, "videoTitle").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 || J.Str(v, "displayName").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
            List<object> library = shown.FindAll(v => J.Int(v, "library") == 1), players = shown.FindAll(v => J.Int(v, "library") != 1);
            _body.Add(Ui.Section("Video Library · " + library.Count));
            if (library.Count == 0) _body.Add(Page.Note("No library videos yet"));
            else _body.Add(List(library));
            _body.Add(Ui.Section("Player Reviews · " + players.Count));
            if (players.Count == 0) { _body.Add(Page.Note("No player review videos yet")); return; }
            var groups = new Dictionary<string, List<object>>();
            var order = new List<string>();
            foreach (object v in players) { string k = J.Str(v, "userId"); if (!groups.ContainsKey(k)) { groups[k] = new List<object>(); order.Add(k); } groups[k].Add(v); }
            foreach (string k in order)
            {
                object first = groups[k][0];
                _body.Add(Ui.Box("pad row", Ui.Avatar(J.Str(first, "displayName"), J.Str(first, "photoURL")), Ui.Text(J.Str(first, "displayName"), "feed-name grow", Ui.Bold).Margin(0), AdminLabels.Pill(groups[k].Count.ToString(), Ui.Faint)));
                _body.Add(List(groups[k]));
            }
        }

        VisualElement List(List<object> videos)
        {
            VisualElement cols = Ui.Box("cols");
            foreach (object v in videos)
            {
                object video = v;
                VisualElement thumb = Ui.Box("thumb");
                if (J.Str(v, "thumbnail").Length > 0) Images.Load(J.Str(v, "thumbnail"), t => { if (t != null) { thumb.style.backgroundImage = new StyleBackground(t); thumb.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop; } });
                cols.Add(Ui.Box("col pad", Ui.Box("glass row", thumb, Ui.Box("grow set-words", Ui.Text(J.Str(v, "videoTitle").Length > 0 ? J.Str(v, "videoTitle") : "Untitled", "feed-name", Ui.Bold), Ui.Text(Form.Duration(J.Num(v, "videoLength")) + " · " + Form.Ago(v, "created_at"), "muted", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                    .OnTap(() => Preview(video))).Margin(6));
            }
            return cols;
        }

        void Preview(object v)
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", VideoBox.Make(J.Str(v, "publicUrl"), 260), Ui.Text(J.Str(v, "videoTitle").Length > 0 ? J.Str(v, "videoTitle") : "Untitled", "h2 wrap", Ui.Bold).Margin(10),
                Ui.Text(Form.Duration(J.Num(v, "videoLength")) + " · " + Form.Ago(v, "created_at"), "muted", Ui.Regular),
                Ui.Button("Delete", () => Dialog.Ask(Root, "Delete Video", "Are you sure you want to delete this video?", "Delete", () =>
                {
                    dim.RemoveFromHierarchy();
                    App.Run(Api.Post("/coaching/deleteVideo", new Dictionary<string, object> { { "videoId", J.Get(v, "id") } }, _ => Shown(), e => _app.Toast(Form.Why(e, "Could not delete."))));
                }, true), "btn btn-ghost").Margin(10),
                Ui.Button("Close", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        // The upload wizard: Player Feedback (pick a Player) or Video Library, then a video from the camera roll, then a title.
        void Upload()
        {
            if (_progress >= 0) return;
            VisualElement dim = null;
            void Close() => dim?.RemoveFromHierarchy();
            VisualElement card = Ui.Box("modal", Ui.Text("Upload Video", "h2 wrap", Ui.Bold), Ui.Text("What type of video are you uploading?", "sub", Ui.Regular).Margin(6));
            void Pick(object player)
            {
                Close();
                string date = DateTime.Now.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture);
                string title = player != null ? "Player feedback - " + date : "Coach video - " + date;
                PhotoPicker.PickVideo(path =>
                {
                    if (path == null) return;
                    App.Run(Uploads.Length(path, seconds =>
                    {
                        _progress = 0; Draw();
                        string file = "coaching_vid_" + UnityEngine.Random.Range(0, 100000000) + ".mp4";
                        App.Run(Uploads.Video(path, file, f => { _progress = f; Draw(); }, url =>
                        {
                            if (url == null) { _progress = -1; Draw(); Dialog.Ask(Root, "Upload Failed", "Could not upload your video. Try again.", "OK", () => { }); return; }
                            var save = new Dictionary<string, object> { { "fileName", file }, { "coachId", J.Str(_app.Profile, "userId", Auth.Uid) }, { "videoLength", Math.Round(Math.Max(0, seconds)) }, { "videoTitle", title }, { "videoWidth", 1080 }, { "videoHeight", 1920 } };
                            if (player != null) save["userId"] = J.Str(player, "userId"); else save["isLibrary"] = 1;
                            App.Run(Api.Post("/coaching/saveVideo", save, _ => { _progress = -1; _app.Toast(player != null ? "Sent to " + LessonInfo.First(J.Str(player, "displayName")) + "." : "Saved to your library."); Shown(); }, e => { _progress = -1; Draw(); _app.Toast(Form.Why(e, "Could not save the video.")); }));
                        }));
                    }));
                });
            }
            card.Add(Ui.Box("glass row", Ui.Icon("User", 22, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Player Feedback", "feed-name", Ui.Bold), Ui.Text("Review video for a specific player", "muted", Ui.Regular))).Margin(12).OnTap(() =>
            {
                Close();
                App.Run(Api.Get("/profile/followers/", d =>
                {
                    VisualElement pick = null;
                    VisualElement list = Ui.Box("modal", Ui.Text("Which player?", "h2 wrap", Ui.Bold));
                    ScrollView scroll = Ui.Scroll();
                    scroll.style.maxHeight = 420;
                    foreach (object p in J.List(d)) { object player = p; scroll.Add(Ui.Box("set-row", Ui.Avatar(J.Str(p, "displayName"), J.Str(p, "photoURL")), Ui.Text(J.Str(p, "displayName"), "feed-name grow", Ui.Bold)).OnTap(() => { pick.RemoveFromHierarchy(); Pick(player); })); }
                    list.Add(scroll);
                    list.Add(Ui.Button("Cancel", () => pick.RemoveFromHierarchy(), "btn btn-ghost"));
                    list.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                    pick = Ui.Box("modal-dim", list);
                    App.Layer(Root).Add(pick);
                }, _ => _app.Toast("Couldn't load your players.")));
            }));
            card.Add(Ui.Box("glass row", Ui.Icon("FilmReel", 22, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Video Library", "feed-name", Ui.Bold), Ui.Text("Drills, techniques, or instructional content", "muted", Ui.Regular))).Margin(10).OnTap(() => Pick(null)));
            card.Add(Ui.Button("Cancel", Close, "btn btn-ghost").Margin(10));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(Close);
            App.Layer(Root).Add(dim);
        }
    }
}
