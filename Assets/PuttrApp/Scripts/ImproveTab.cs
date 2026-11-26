// The Improve tab, the Full analysis behind it, and the Puttr Club gate.
//
// Improve, to docs/design-guidelines.md ("Broadcast"): the header, the 3D stroke session (the tab's one main button),
// the coach review, PQS, today's session as a list row (Home owns it), your last 30 days, practice and the video
// library. The 3D scenes are in ImproveArena.cs (Live3D.cs draws them into their windows), the styles in
// Resources/PuttrApp/Improve.uss. Full analysis is members only, as it is in the app; so are Compare and coach chat.
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
            else { Analytics.Gate(feature ?? "club"); app.Push(new ClubScreen(app, feature)); }
        }
    }

    /// <summary>
    /// The paywall, as puttr-app's MembershipScreen shows it to a non-member: the plans (IAPHUB), a website order, Junior,
    /// Restore Purchase, what Club gives, the free trial terms and help, then the terms and privacy links the store asks
    /// for. A managed profile gets the owner notice instead (billing is the owner's). Styles in Resources/PuttrApp/Club.uss.
    /// </summary>
    public sealed class ClubScreen : AppScreen
    {
        // MembershipScreen appFeatures, the nearest icons the app ships.
        static readonly string[,] Features =
        {
            { "Trophy", "Tournaments", "Compete in tournaments against the Puttr community for real prizes" },
            { "Lightning", "Battle", "Play live matches against friends over streaming video" },
            { "Planet", "Online Play", "Play against others in the community any time with online games" },
            { "Target", "Analysis", "Detailed putt data and auto-generated drills for your trouble areas" },
            { "ChatCircle", "Putting Coach", "Trained on millions of putts, your putting coach helps you improve" },
            { "Flask", "Sessions", "Record sessions to analyze a 3D model of your stroke" },
            { "GraduationCap", "Learn", "Training, stretches and workout videos focused on putting" },
            { "Medal", "Quest Levels", "Access to all beginner, advanced and difficult quest levels" },
        };

        readonly App _app;
        readonly VisualElement _orderCard = Ui.Box("glass club-card club-center");
        TextField _order;
        Label _said;
        bool _busy;

        public ClubScreen(App app, string feature)
        {
            _app = app;
            bool android = Application.platform == RuntimePlatform.Android;
            ScrollView scroll = Ui.Scroll();
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Club");
            if (sheet != null) scroll.styleSheets.Add(sheet);
            scroll.Add(Ui.Header("Puttr Club", app.Pop));
            VisualElement body = Ui.Box("club");
            scroll.Add(body);
            Root = scroll;
            if (Kid.Blocked(app, Kid.Owner, body)) return;

            if (!string.IsNullOrEmpty(feature)) body.Add(Ui.Text(feature + " is part of Puttr Club.", "club-why", Ui.Medium));
            body.Add(Ui.Box("glass club-card", new ClubPlans(app).Root));

            DrawOrder();
            body.Add(_orderCard);

            body.Add(Ui.Box("glass club-card club-row",
                Ui.Box("club-tile", Ui.Icon("GraduationCap", 20, Ui.Green)),
                Ui.Box("club-row-words", Ui.Text("Junior golfer?", "club-row-title", Ui.Medium), Ui.Text("Get Club free by sharing your progress", "club-row-sub", Ui.Regular)),
                Ui.Button("Apply", () => app.Push(new JuniorLandingScreen(app)), "btn club-small")));

            body.Add(Ui.Box("glass club-card club-center",
                Ui.Text("Already subscribed on your " + (android ? "Google" : "Apple") + " account?", "club-row-sub club-center-text", Ui.Regular),
                Ui.Button("Restore Purchase", Restore, "btn club-outline")));

            VisualElement list = Ui.Box("glass club-card", Ui.Text("Everything you need to putt better", "club-list-title", Ui.Bold));
            for (int i = 0; i < Features.GetLength(0); i++)
                list.Add(Ui.Box("club-feature",
                    Ui.Box("club-tile club-tile-sm", Ui.Icon(Features[i, 0], 18, Ui.Green)),
                    Ui.Box("club-row-words", Ui.Text(Features[i, 1], "club-feature-title", Ui.Bold), Ui.Text(Features[i, 2], "club-row-sub", Ui.Regular))));
            body.Add(list);

            body.Add(Ui.Box("glass club-card",
                Ui.Text("Free Trial Terms", "club-terms-title", Ui.Bold),
                Ui.Text("You will not be charged during the trial period. At the end of your 1-week free trial, your subscription will automatically convert to a monthly plan at $19.99/month unless you cancel at least 24 hours before the trial ends. You can cancel your free trial at any time through your " + (android ? "Google Play Store" : "Apple App Store") + " subscription settings.", "club-terms", Ui.Regular)));

            body.Add(Ui.Box("glass club-card club-row",
                Ui.Box("club-tile club-tile-grey", Ui.Icon("Question", 20, new Color(1f, 1f, 1f, 0.5f))),
                Ui.Box("club-row-words", Ui.Text("Active membership?", "club-row-title", Ui.Medium), Ui.Text("Contact your caddie for help", "club-row-sub", Ui.Regular)),
                Ui.Button("Chat", () => app.Push(new CaddieScreen(app)), "btn club-small")));

            body.Add(Ui.Box("club-links",
                Ui.Text("Terms of Use", "club-link", Ui.Medium).OnTap(() => Application.OpenURL("https://puttr.co/terms")),
                Ui.Text("Privacy Policy", "club-link", Ui.Medium).OnTap(() => Application.OpenURL("https://puttr.co/privacy"))));
        }

        void Restore()
        {
            Iap.Restore((_, err) =>
            {
                if (err != null) { _app.Toast(Iap.Words(err) ?? "Nothing to restore."); return; }
                App.Run(Api.Get("/profile/", d => { _app.Profile = J.First(d); _app.Toast(Club.Member(_app.Profile) ? "Membership restored." : "No membership found on this account."); if (Club.Member(_app.Profile)) _app.Pop(); }));
            });
        }

        // OnlineOrder: "Purchased on the Puttr website? Enter your order number:" and Activate, or the outcome.
        void DrawOrder(string said = null, string done = null)
        {
            _orderCard.Clear();
            if (done != null) { _orderCard.Add(Ui.Text(done, "club-row-sub club-center-text", Ui.Medium)); return; }
            string typed = _order != null ? _order.value : "";
            _order = new TextField { maxLength = 6, value = typed, keyboardType = TouchScreenKeyboardType.NumberPad };
            _order.textEdition.placeholder = "0000";
            _order.textEdition.hidePlaceholderOnFocus = false;
            _order.AddToClassList("club-order");
            if (said != null) _order.AddToClassList("club-order-bad");
            _order.Face(Ui.Bold);
            _order.RegisterValueChangedCallback(e => { string digits = new string(Array.FindAll(e.newValue.ToCharArray(), char.IsDigit)); if (digits != e.newValue) _order.SetValueWithoutNotify(digits); });
            _said = Ui.Text(said ?? "Purchased on the Puttr website? Enter your order number:", "club-row-sub club-center-text", Ui.Medium);
            _orderCard.Add(_said);
            _orderCard.Add(Ui.Box("club-order-row", _order, _busy ? (VisualElement)Spinner.Small() : Ui.Button("Activate", Activate, "btn club-small")));
        }

        void Activate()
        {
            string order = new string(Array.FindAll(_order.value.ToCharArray(), char.IsDigit));
            if (order.Length < 4 || order.Length > 6) { DrawOrder("Please enter your order number (4-6 digits)"); return; }
            if (_busy) return;
            _busy = true;
            DrawOrder();
            App.Run(Api.Post("/profile/activateClub", new Dictionary<string, object> { { "orderNumber", order } }, data =>
            {
                _busy = false;
                string result = J.Str(data, "result", "ERROR");
                bool pro = J.Str(data, "plan") == "pro";
                if (result == "ACTIVATED" || result == "ALREADY_ACTIVATED")
                {
                    DrawOrder(null, pro
                        ? "Puttr Pro activated. Your account now reflects your Puttr Pro coach subscription from order #" + order + ". Coach tools and membership features are unlocked."
                        : "Puttr Club activated. Your account now reflects your Puttr Club membership from order #" + order + ".");
                    App.Run(Api.Get("/profile/", profile => { _app.Profile = J.First(profile); if (Club.Member(_app.Profile)) { _app.Pop(); _app.Toast("Welcome to Puttr Club."); } }));
                }
                else if (result == "NO_MEMBERSHIP") DrawOrder("We don't see a membership on order #" + order + ". You can subscribe above.");
                else if (result == "ALREADY_FULFILLED" || result == "EMAIL_MISMATCH" || result == "ERROR")
                    DrawOrder(null, "Order #" + order + " needs a quick check. We've opened a support ticket and will email you once it's sorted.");
                else DrawOrder("Something went wrong. Please try again.");
            }, why =>
            {
                _busy = false;
                DrawOrder(why == "offline" ? "No connection. Try again." : why != null && why.Contains("429") ? "Too many attempts. Please try again in an hour." : "Something went wrong. Please try again.");
            }));
        }
    }

    /// <summary>
    /// Improve (docs/design-guidelines.md): the header, then the 3D stroke session with the tab's one main button, the
    /// coach review, PQS, today's session as a list row, the last 30 days as a stat strip, Practice tiles and the video
    /// library. Each piece of information once. On a tablet or a phone on its side the cards pair up in two columns.
    /// The mat scene (Arena.Today) is no longer shown here: Home owns Today's session.
    /// </summary>
    public sealed class ImproveScreen : AppScreen
    {
        readonly App _app;
        readonly ScrollView _scroll;
        readonly VisualElement _top, _coachFace, _foot;
        readonly VisualElement _lessons, _lab, _pqs, _today, _month, _practice, _train, _course;
        bool? _wide;
        List<object> _sessions;
        List<AnalysisMath.Cell> _grid;
        List<AnalysisMath.Putt> _year;
        object _coach, _stats, _streak;
        float _loadedAt = -100f;

        public ImproveScreen(App app)
        {
            _app = app;
            Root = Ui.Box("imp");
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Improve");
            if (sheet != null) Root.styleSheets.Add(sheet);

            // The header: the tab's name, chat with your coach (members; a past member reads old chats) and the coach
            // settings gear (coach, voice, daily goal, reminder). Neither for a Kid profile.
            _coachFace = Ui.Box("imp-coach-face", Ui.Icon("ChatCircle", 16, Color.white));
            bool kid = Kid.IsKid(app.Profile);
            _top = Ui.TabHeader("Improve",
                kid ? null : Ui.Box("imp-coach", _coachFace, Ui.Caps("Chat", "caps imp-coach-t")).OnTap(() => CoachChatScreen.Open(app)),
                kid ? null : Ui.IconSquare("GearSix", OpenSettings));

            _lessons = Ui.Box("imp-part"); _lab = Ui.Box("imp-part"); _pqs = Ui.Box("imp-part"); _today = Ui.Box("imp-part"); _month = Ui.Box("imp-part");
            _practice = Ui.Box("imp-part"); _train = Ui.Box("imp-part"); _course = Ui.Box("imp-part");
            Skeleton.Hold(_lab, Skeleton.Section(280));
            Skeleton.Hold(_pqs, Skeleton.Section(230));
            Skeleton.Hold(_month, Skeleton.Section(96));

            _scroll = Ui.Scroll();
            _scroll.AddToClassList("imp-scroll");
            _foot = new VisualElement();
            _foot.style.height = 32;
            Root.Add(_scroll);
            Practice();
            Train();
            // Tournaments and League Night live on the Play tab only (Matt, 2026-11-17): no "Take it to the course" here.
            // A Player who has not picked a coach on this phone yet: the coach picker leads, so Improve waits behind a
            // spinner until /coach/settings says whether there is a coach (Matt, 2026-11-17).
            if (!Kid.IsKid(app.Profile) && !CoachKnown(app)) Hold(true);
            Arrange();
            Root.RegisterCallback<GeometryChangedEvent>(_ => Root.schedule.Execute(Arrange));
        }

        // ── Layout: one column on a phone, cards paired in two columns where there is room ──

        void Arrange()
        {
            bool wide = false;
            for (VisualElement e = Root.parent; e != null; e = e.parent) if (e.ClassListContains("wide")) { wide = true; break; }
            if (_wide == wide) return;
            _wide = wide;
            Root.EnableInClassList("imp-wide", wide);
            foreach (VisualElement part in new[] { _top, _lessons, _lab, _pqs, _today, _month, _practice, _train, _course, _foot }) part.RemoveFromHierarchy();
            _scroll.Clear();
            // The header, then the 3D stroke session: it leads the tab and holds its one main button. The analysis
            // card comes straight after it (Matt, 2026-11-25: Full analysis front and centre).
            _scroll.Add(_top);
            _scroll.Add(_lab);
            if (!wide)
                foreach (VisualElement part in new[] { _month, _lessons, _pqs, _today, _practice, _train, _course }) _scroll.Add(part);
            else
            {
                _scroll.Add(Ui.Box("imp-pair", Ui.Box("imp-pair-col imp-pair-a", _month, _lessons, _today), Ui.Box("imp-pair-col imp-pair-b", _pqs, _course)));
                foreach (VisualElement part in new[] { _practice, _train }) _scroll.Add(part);
            }
            _scroll.Add(_foot);
        }

        // ── The coach picker first ───────────────────────────────────────────

        VisualElement _hold;
        bool _needsPick;
        static bool _fixturePicked;   // -app2NewPlayer: picked once this run

        static string CoachKey(App app) => "app2.coachPicked." + J.Str(app.Profile, "userId");
        static bool CoachKnown(App app) => !(ProfileSetup.NewPlayer && Fixtures.On) && PlayerPrefs.GetInt(CoachKey(app), 0) == 1;

        void Hold(bool on)
        {
            _scroll.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            if (on && _hold == null)
            {
                _hold = Ui.Box("", Spinner.Big());
                _hold.style.position = Position.Absolute;
                _hold.style.left = _hold.style.right = _hold.style.top = _hold.style.bottom = 0;
                _hold.style.justifyContent = Justify.Center;
                Root.Add(_hold);
            }
            if (!on && _hold != null) { _hold.RemoveFromHierarchy(); _hold = null; }
        }

        /// <summary>The coach first run over Improve, while Improve is the screen in front.</summary>
        void LeadWithPicker()
        {
            if (!_needsPick || _app.Top != null || Root.panel == null) return;
            _needsPick = false;   // once per answer: backing out of it shows Improve (the next read asks again)
            PickCoach(_app, () => { _loadedAt = -100f; Shown(); });
            Hold(false);   // under the picker: Improve is there if the Player backs out of it
        }

        /// <summary>
        /// The coach first run, from wherever it is asked for (Improve's lead, Home's Choose your coach): every press
        /// opens it until a coach is chosen. after runs once one is.
        /// </summary>
        public static void PickCoach(App app, Action after)
        {
            if (app.Top is CoachFirstRunScreen) return;   // a press on the card's button reaches the card too
            app.Push(new CoachFirstRunScreen(app, () =>
            {
                _fixturePicked = true;
                PlayerPrefs.SetInt(CoachKey(app), 1);
                CoachVoice.Changed();   // Home's card and anything else showing the coach reads it again
                after?.Invoke();
            }));
        }

        void CoachAnswered(object data)
        {
            bool none = J.Get(data, "exists") != null && !J.Flag(data, "exists") || ProfileSetup.NewPlayer && Fixtures.On && !_fixturePicked;
            if (none && !Kid.IsKid(_app.Profile)) { _needsPick = true; LeadWithPicker(); return; }
            _needsPick = false;
            if (J.Get(data, "exists") != null || !Fixtures.On) PlayerPrefs.SetInt(CoachKey(_app), 1);
            Hold(false);
        }

        public override void Shown()
        {
            if (_labBody != null) _labBody.gameObject.SetActive(true);
            Arrange();
            LeadWithPicker();
            // A session filmed a moment ago: said at once, and its list read whatever was read in the last 20 seconds.
            bool fresh = Time.realtimeSinceStartup - _loadedAt < 20f;
            if (fresh && LabPending.Id != null) { Lab(); ReadLab(); }
            if (fresh) return;
            _loadedAt = Time.realtimeSinceStartup;
            App.Run(Api.Get("/profile/analysis/30", data => { _grid = AnalysisMath.Grid(J.List(data)); Today(); Month(); }));
            ReadLab();
            App.Run(Api.Get("/coach/settings", data =>
            {
                _coach = data; CoachVoice.Load(data); Face(); Today(); Month();
                // No coach row yet: the first run (pick a coach, five putts, the reveal, the daily goal).
                CoachAnswered(data);
            }, _ => { _coach = new Dictionary<string, object>(); Today(); Hold(false); }));
            Lessons();
            App.Run(Api.Get("/profile/userStats", data => { _stats = J.First(data); Month(); }));
            App.Run(Api.Get("/streaks/", data => { _streak = data; Today(); }));
            App.Run(Api.Get("/profile/analysis/putts/365", data => { _year = AnalysisMath.ReadAll(J.List(data)); Pqs(); }, _ => { _year = new List<AnalysisMath.Putt>(); Pqs(); }));
        }

        /// <summary>The Coach settings sheet over Improve; what it saves shows at once (the coach's face, today's session length).</summary>
        void OpenSettings()
        {
            void Open() => CoachSettingsSheet.Open(_app, Root, _coach, saved =>
            {
                var merged = new Dictionary<string, object>();
                if (_coach is Dictionary<string, object> was) foreach (KeyValuePair<string, object> kv in was) merged[kv.Key] = kv.Value;
                // As doubles and strings, the way J reads what the parser makes.
                foreach (KeyValuePair<string, object> kv in saved) merged[kv.Key] = kv.Value is int n ? (object)(double)n : kv.Value;
                _coach = merged;
                Face();
                Today();
            });
            if (_coach != null) { Open(); return; }
            App.Run(Api.Get("/coach/settings", d => { _coach = d; CoachVoice.Load(d); Face(); Open(); }, _ => Open()));
        }

        /// <summary>The coach this Player picked, on the chat button.</summary>
        void Face()
        {
            Texture2D face = Resources.Load<Texture2D>("PuttrApp/Coaches/" + CoachVoice.Persona.Name);
            if (face == null) return;
            _coachFace.Clear();
            _coachFace.style.backgroundImage = face;
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
                    VisualElement video = Ui.Box("imp-card imp-lesson", Ui.Avatar(name, J.Str(v, "photoURL")), Ui.Box("grow imp-lesson-words", Ui.Caps(rate ? "Rate your lesson" : "Video from your coach", "caps caps-green"),
                        Ui.Text(rate ? "How was your lesson with " + name + "?" : name + " sent you a lesson video", "body imp-gap-4", Ui.Bold), Ui.Text((rate ? "Leave a rating and this card clears" : Form.Ago(v, "created_at")) + (open.Count > 1 ? " · " + (open.Count - 1) + " more" : ""), "body-2 imp-gap-4", Ui.Medium)),
                        Ui.Icon("Play", 20, Color.white)).OnTap(() =>
                        {
                            if (J.Str(v, "bookingId").Length > 0) LessonInfo.Open(_app, J.Str(v, "bookingId"));
                            else { foreach (object x in videos) if (J.Str(x, "bookingId").Length == 0) SeenVideos.Add(J.Str(x, "id")); SaveSeen(); _app.Push(new CoachVideosScreen(_app)); }
                        });
                    _lessons.Add(Ui.Box("pad imp-m24", video));
                }
                if (!tester) return;
                object b = bookings.Find(x => J.Int(x, "status") == 4 && J.Str(x, "watchedAt").Length == 0) ?? bookings.Find(x => J.Str(x, "planId").Length > 0 && J.Int(x, "planStatus") == 1)
                    ?? bookings.Find(x => J.Int(x, "planStatus") == 2) ?? bookings.Find(x => J.Int(x, "status") >= 1 && J.Int(x, "status") <= 3);
                string coach = J.Str(b, "coachName");
                // What the card says: its kind (the kick), the line, and a small line under it.
                (string title, string words, string note, Action go) card =
                    b != null && J.Int(b, "status") == 4 && J.Str(b, "watchedAt").Length == 0 ? ("Your lesson is ready!", coach + " broke down your stroke. Watch your lesson video.", null, (Action)(() => LessonInfo.Open(_app, J.Str(b, "id"))))
                    : b != null && J.Str(b, "planId").Length > 0 && J.Int(b, "planStatus") == 1 ? ("Practice plan", "Work the plan " + coach + " assigned. Track your progress.", null, () => _app.Push(new PlanDetailScreen(_app, J.Str(b, "planId"))))
                    : b != null && J.Int(b, "planStatus") == 2 ? ("Plan complete! See your progress", "You finished " + coach + "'s plan. See how you improved.", null, () => _app.Push(new PlanDetailScreen(_app, J.Str(b, "planId"))))
                    : b != null ? ("Lesson in progress", J.Int(b, "status") == 1 ? "Submit a session to " + coach + " to start the clock." : coach + " is reviewing your putting.", J.Int(b, "status") == 1 ? null : "Lesson due " + Form.Date(b, "dueAt", "ddd MMM d, h:mm tt") + ".", () => LessonInfo.Open(_app, J.Str(b, "id")))
                    : bookings.Count > 0 ? ("Book another lesson", "Keep the momentum going with a fresh review from a coach.", null, () => _app.Push(new FindCoachScreen(_app)))
                    : ("Get a lesson", "A real coach reviews your session and sends you a personal video lesson.", null, () => _app.Push(new FindCoachScreen(_app)));
                // The ring counts down to the due time while a coach reviews; full otherwise.
                bool reviewing = b != null && J.Int(b, "status") >= 1 && J.Int(b, "status") <= 3 && card.note != null;
                float left = 1f;
                if (reviewing && Form.When(b, "dueAt") is DateTime due && Form.When(b, "created_at") is DateTime made && due > made)
                    left = (float)((due - DateTime.Now).TotalSeconds / (due - made).TotalSeconds);
                Color accent = reviewing ? Ui.Amber : Ui.Bright;
                VisualElement ring = coach.Length > 0 ? new DueRing(Initials(coach), left, accent) : (VisualElement)Ui.Box("imp-due", Ui.Icon("GraduationCap", 22, Ui.Bright));
                Label kick = Ui.Caps(card.title);
                kick.style.color = accent;
                VisualElement words = Ui.Box("grow imp-lesson-words", kick, Ui.Text(card.words, "body imp-gap-4", Ui.Bold));
                if (card.note != null) words.Add(Ui.Text(card.note, "body-2 imp-gap-4", Ui.Medium));
                VisualElement box = Ui.Box("imp-card", Ui.Box("imp-lesson", ring, words, Ui.Icon("CaretRight", 16, Ui.Faint)).OnTap(card.go));
                if (bookings.Count > 0)
                {
                    VisualElement links = Ui.Box("imp-links");
                    if (b != null) links.Add(Ui.Text("Book another lesson", "imp-link", Ui.Bold).OnTap(() => _app.Push(new FindCoachScreen(_app))));
                    links.Add(Ui.Text("View all lessons", b != null ? "imp-link imp-link-div" : "imp-link", Ui.Bold).OnTap(() => _app.Push(new MyLessonsScreen(_app))));
                    box.Add(links);
                }
                _lessons.Add(Ui.Section("Real coach review"));
                _lessons.Add(Ui.Box("pad", box));
            }
            App.Run(Api.Get("/lab/coaching/videos", d => { videos = J.List(d); Draw(); }, _ => { videos = new List<object>(); Draw(); }));
            if (tester) App.Run(Api.Get("/lesson/bookings", d => { bookings = J.List(d); Draw(); }, _ => { bookings = new List<object>(); Draw(); }));
        }

        static string Initials(string name)
        {
            string[] parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "";
            string first = parts[0].Substring(0, 1), last = parts.Length > 1 ? parts[parts.Length - 1].Substring(0, 1) : "";
            return (first + last).ToUpperInvariant();
        }

        const string SeenKey = "app2.seenCoachVideos";
        static HashSet<string> _seen;
        static HashSet<string> SeenVideos => _seen ?? (_seen = new HashSet<string>(PlayerPrefs.GetString(SeenKey, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)));
        static void SaveSeen() { var list = new List<string>(SeenVideos); if (list.Count > 100) list = list.GetRange(list.Count - 100, 100); PlayerPrefs.SetString(SeenKey, string.Join(",", list)); }

        // ── Today's session ──────────────────────────────────────────────────

        // Home owns Today's session as the session card; here it is one list row that opens the same session brief.
        void Today()
        {
            if (_grid == null || _coach == null) return;
            _today.Clear();
            int goal = Mathf.Clamp(J.Int(_coach, "dailyGoal", 20), 3, 30);
            List<TournamentMath.Dealt> putts = AnalysisMath.Session(_grid, goal, new System.Random(DateTime.Now.DayOfYear), out string title, out string why);
            bool done = J.Str(_coach, "lastPracticeDate").StartsWith(DateTime.Now.ToString("yyyy-MM-dd"));
            int streak = J.Int(_streak, "currentStreak");
            string length = putts.Count + " putts · about " + Mathf.Max(2, Mathf.CeilToInt(putts.Count / 3f)) + " min";
            VisualElement row = Ui.Box("imp-card imp-row", Ui.Box("grow imp-row-words",
                Ui.Text(done ? "Practice again" : title, "body", Ui.Bold),
                Ui.Text(done ? "Done for today · " + length : length, "body-2 imp-gap-4", Ui.Medium)));
            // A streak is amber text with the flame, no chip.
            if (streak > 0) row.Add(Ui.Box("imp-streak", Ui.Icon("FillFire", 16, Ui.Amber), Ui.Text(streak + (streak == 1 ? " day" : " days"), "imp-streak-t", Ui.Bold)));
            row.Add(Ui.Icon("CaretRight", 16, Ui.Faint));
            // As on Home: the session brief (the focus, the putts to come) comes before the putting, the way the app does it.
            row.OnTap(() => _app.Push(new SessionBriefScreen(_app, CoachVoice.Persona, putts, title, why)));
            _today.Add(Ui.Section("Today's session"));
            _today.Add(Ui.Box("pad", row));
        }

        // ── 3D stroke session: record one any day, review the latest ──

        // Recording is always on offer (Matt, 2026-11-25): a Player can film a session every day, so the card never
        // waits for a recheck date. Record session is the tab's main button; Review session sits under it once there
        // is a session to open.
        void ReadLab() => App.Run(Api.Get("/lab/sessions", data => { _sessions = J.List(data); Lab(); }, _ => { if (_sessions == null) _sessions = new List<object>(); Lab(); }));

        UnityEngine.UIElements.IVisualElementScheduledItem _labPoll;

        void Lab()
        {
            _lab.Clear();
            _labPoll?.Pause(); _labPoll = null;
            if (_sessions == null) return;
            // Just filmed and not in the list yet (LabPending): the card says so, and the list is asked for again
            // every few seconds while this tab is up, until the new session is in it.
            bool waiting = LabPending.Waiting(_sessions);
            if (waiting) _labPoll = _lab.schedule.Execute(ReadLab).StartingIn(5000);
            object latest = _sessions.Count > 0 ? _sessions[0] : null;
            int status = J.Int(latest, "status");
            int days = latest == null ? 0 : (int)(DateTime.Now.Date - (ProfileMath.Date(latest, "created_at") ?? DateTime.UtcNow).ToLocalTime().Date).TotalDays;
            const string kick = "3D STROKE SESSION";
            string title, words, review = null;
            if (waiting) { title = "Processing in the cloud"; words = "Your session is being built in 3D. It is usually ready in a few minutes."; if (latest != null) review = "Review last session"; }
            else if (latest == null) { title = "Film your baseline"; words = "Six putts on camera. Your coach builds your stroke in 3D and talks you through it."; }
            else if (status < 4) { title = "Processing in the cloud"; words = "Your session is being built in 3D. It is usually ready in a few minutes."; review = "View session"; }
            else
            {
                title = "Your latest stroke"; review = "Review session";
                words = days <= 0 ? "Recorded today. Film another any time: a session a day shows what is changing."
                    : (days == 1 ? "Recorded yesterday." : "Recorded " + days + " days ago.") + " Film six more putts and see what changed.";
            }
            // The Player's own latest stroke once its pose file is in (Follow loads it, and watches a session the cloud
            // is still building); until then, and with no session, a representative stroke on a loop.
            Follow(latest);
            // The picture is the putter meeting the ball, on a loop (Matt, 2026-11-24: the body model looked off here;
            // the Player's own stroke in 3D is on the session's page).
            var loop = new Live3D(Arena.Stroke()) { Name = "stroke", Draggable = true };
            loop.AddToClassList("imp-fill");
            VisualElement view = loop;
            // The session card: the picture in its window, then the words and the tab's one main button on a solid bar.
            VisualElement eyebrow = Ui.Box("row", Ui.Caps(kick, "caps caps-green grow"));
            if (_sessions.Count > 1) eyebrow.Add(Ui.Text("All sessions", "sec-link", Ui.Medium).OnTap(() => _app.Push(new LabSessionsScreen(_app))));
            VisualElement bar = Ui.Box("imp-bar imp-bar-16", eyebrow, Ui.Title(title, "title imp-gap-8"), Ui.Text(words, "body-2 imp-gap-8", Ui.Medium), Ui.Button("Record session", () => _app.Push(new LabIntroScreen(_app))));
            if (review != null) bar.Add(Ui.Button(review, () => _app.Push(new LabSessionScreen(_app, latest)), "btn btn-ghost imp-lab-second"));
            _lab.Add(Ui.Box("pad imp-m16", Ui.Box("window imp-win-top imp-lab-win", view), bar));
        }

        LabSessionData _labData;
        BodyViewer _labBody;

        /// <summary>
        /// Keeps the card on the latest session: while the cloud builds it, its status is read every two seconds and the
        /// card turns to "Your latest stroke" the moment it is ready; once ready, its pose file is loaded and the
        /// Player's own stroke plays in the card. A session still not built two hours on is left alone (it is stuck).
        /// </summary>
        void Follow(object latest)
        {
            string id = latest == null ? "" : J.Str(latest, "id");
            if (_labData != null && _labData.Id == id) return;
            _labData?.Close();
            _labData = null;
            if (_labBody != null) { UnityEngine.Object.Destroy(_labBody.gameObject); _labBody = null; }
            if (id.Length == 0) return;
            var data = new LabSessionData(latest, false);
            bool ready = data.Ready;
            double hours = (DateTime.UtcNow - (ProfileMath.Date(latest, "created_at") ?? DateTime.UtcNow)).TotalHours;
            if (!ready && hours > 2) return;
            _labData = data;
            data.Changed += () =>
            {
                if (_labData != data || _sessions == null || _sessions.Count == 0) return;
                bool draw = false;
                if (!ready && data.Ready) { ready = true; _sessions[0] = data.Row; draw = true; }
                if (draw) Lab();
            };
            data.Start();
        }

        /// <summary>The first putt of the session with enough of the Player in frame, on a loop from the angle it was filmed.</summary>
        static BodyViewer Body(LabSessionData data)
        {
            try
            {
                List<Pose3D.Frame> frames = Pose3D.Read(data.Blaze, out _);
                for (int j = 0; j < data.PuttCount && frames.Count > 0; j++)
                {
                    (double s, double e) = BodyFaults.Window(j);
                    List<Pose3D.Frame> putt = Pose3D.Window(frames, (float)s, (float)e);
                    int seen = 0;
                    foreach (Pose3D.Frame f in putt) if (f.J.Count > 0) seen++;
                    if (seen < 8) continue;
                    BodyViewer viewer = BodyViewer.Make();
                    viewer.Show(putt);
                    BodyFaults.Stroke stroke = BodyFaults.Analyze(data.Pose, s, e, data.Lefty);
                    if (stroke != null) viewer.Yaw = stroke.DownTheLine ? Mathf.PI / 2 : 0f;
                    viewer.Playing = true;
                    return viewer;
                }
            }
            catch (Exception e) { Perf.Note("improve: stroke not shown (" + e.Message + ")"); }
            return null;
        }

        // The mannequin's camera rests while another screen is in front.
        public override void Hidden() { if (_labBody != null) _labBody.gameObject.SetActive(false); }

        // ── Your putting, last 30 days ───────────────────────────────────────

        void Month()
        {
            if (_grid == null) return;
            _month.Clear();
            int percent = AnalysisMath.MakePercent(_grid);
            // The app reads user_stats; with no row yet, the grid's own count.
            int putts = J.Get(_stats, "total_putts_last_30") != null ? J.Int(_stats, "total_putts_last_30") : AnalysisMath.Putts(_grid);
            int best = J.Int(_coach, "longestStreak");
            // The analysis card (Matt, 2026-11-25: Full analysis front and centre): the 30 days in numbers, every spot
            // of the mat with its score, one sentence on the best and worst, and the way in to Full analysis. Every
            // Player sees the card; the Club gate is on the page behind it.
            Action open = () => AnalysisScreen.Open(_app);
            _month.Add(Ui.Section("Your putting · last 30 days"));
            _month.Add(Ui.Box("pad", Ui.StatStrip((percent < 0 ? "–" : percent + "%", "Make %"), (ProfileMath.Number(putts), "Putts"), (best.ToString(), "Best streak"))));
            // The mat lying on its side: a row per tee, a column per distance, the hole off to the left.
            VisualElement map = Ui.Box("imp-map");
            for (int tee = 1; tee <= 3; tee++)
            {
                VisualElement row = Ui.Box("row", Ui.Caps(AnalysisMath.TeeNames[tee - 1].Substring(0, 1), "caps imp-map-side"));
                for (int feet = 3; feet <= 11; feet++)
                {
                    AnalysisMath.Cell cell = _grid[(feet - 3) * 3 + tee - 1];
                    Label value = Ui.Text(cell.Total > 0 ? cell.Weighted.ToString() : "–", "imp-map-value", Ui.Bold);
                    value.style.color = cell.Total > 0 ? AnalysisPage.Heat(cell.Weighted) : Ui.Faint;
                    row.Add(Ui.Box("imp-map-cell", value));
                }
                map.Add(row);
            }
            VisualElement feetRow = Ui.Box("row", Ui.Box("imp-map-side"));
            for (int feet = 3; feet <= 11; feet++) feetRow.Add(Ui.Caps(feet + (feet == 11 ? " ft" : ""), "caps imp-map-feet"));
            map.Add(feetRow);
            AnalysisMath.Extremes(_grid, out AnalysisMath.Cell top, out AnalysisMath.Cell low);
            string words = top == null ? "Putt a few and every spot on the mat gets its score here."
                : top == low ? "Putt from more spots to see where you are strongest and weakest."
                : "Strongest from " + top.Name + " (" + top.Weighted + "). Weakest from " + low.Name + " (" + low.Weighted + ").";  // the grid's own scores, not make %
            _month.Add(Ui.Box("pad imp-m12", Ui.Box("imp-card imp-pad",
                map,
                Ui.Text(words, "body imp-m12", Ui.Bold),
                Ui.Text("Each spot against the PGA Tour's make % from there: 100 is Tour level.", "body-2 imp-gap-4", Ui.Medium),
                Ui.Secondary("Full analysis", open))));
        }

        // ── PQS ──────────────────────────────────────────────────────────────

        void Pqs()
        {
            _pqs.Clear();
            if (_year == null) return;
            DateTime now = DateTime.UtcNow;
            var last30 = _year.FindAll(p => p.At > now.AddDays(-30));
            var before = _year.FindAll(p => p.At <= now.AddDays(-30) && p.At > now.AddDays(-60));
            _pqs.Add(Ui.Section("Putting Quality Score", "?", () => PqsHelp.Explain(_app)));
            if (last30.Count < 5)
            {
                _pqs.Add(Ui.Box("pad", Ui.Box("imp-card imp-pad", Ui.Text("Your first score arrives after 5 putts.", "body-2", Ui.Medium))));
                return;
            }
            int score = AnalysisMath.Pqs(last30, out float makeIndex, out float accuracy);
            int prior = before.Count >= 5 ? AnalysisMath.Pqs(before, out _, out _) : -1;
            // The score block: one numeral with its caption, the two parts of the score beside it.
            VisualElement Line(string label, float value, bool first) =>
                Ui.Box(first ? "imp-pqs-line" : "imp-pqs-line imp-pqs-line-div", Ui.Text(label, "body-2 grow", Ui.Medium), Ui.Num(Mathf.RoundToInt(value * 100f).ToString(), "num-20"));
            VisualElement card = Ui.Box("imp-card imp-pad", Ui.Box("imp-pqs-row",
                Ui.Box("imp-pqs-lead", Ui.Num(score.ToString(), "num-44"), Ui.Caps("Out of 100", "caps imp-gap-4")),
                Ui.Box("grow imp-pqs-side", Line("Make index", makeIndex, true), Line("Accuracy", accuracy, false))));
            if (prior >= 0)
            {
                int change = score - prior;
                string colour = change > 0 ? "#7AD446" : change < 0 ? "#FF5252" : "#8B9399";
                card.Add(Ui.Text("<color=" + colour + ">" + (change > 0 ? "▲ " : change < 0 ? "▼ " : "") + Math.Abs(change) + "</color> vs the 30 days before", "body-2 imp-gap-12", Ui.Medium));
            }
            if (last30.Count < 30) { Label cal = Ui.Text("Calibrating · " + last30.Count + " of 30 putts", prior >= 0 ? "body-2 imp-gap-4" : "body-2 imp-gap-12", Ui.Medium); cal.style.color = Ui.Amber; card.Add(cal); }
            List<(DateTime day, int pqs)> days = AnalysisMath.PqsByDay(_year);
            if (days.Count >= 2)
            {
                var values = new List<int>();
                int from = Mathf.Max(0, days.Count - 8);
                for (int i = from; i < days.Count; i++) values.Add(days[i].pqs);
                card.Add(new TrendRibbon(values));
                card.Add(Ui.Box("imp-ribx", Ui.Text(days[from].day.ToString("MMM d", CultureInfo.InvariantCulture), "imp-ribx-t", Ui.Medium), Ui.Text(days[days.Count - 1].day.ToString("MMM d", CultureInfo.InvariantCulture), "imp-ribx-t", Ui.Medium)));
            }
            _pqs.Add(Ui.Box("pad", card));
        }

        // ── Practice: a row of tiles ─────────────────────────────────────────

        ScrollView _flow;

        void Practice()
        {
            _practice.Add(Ui.Section("Practice"));
            _flow = Ui.Scroll(true);
            _flow.AddToClassList("imp-flow");
            _flow.Add(Ui.Box("imp-flow-gap"));
            FlowCard("FREEPUTT", "Free putt", "Your driving range for putting. Every putt tracked.", false, () => FreePuttScreen.Open(_app));
            FlowCard("FITTING", "Compare", "Putt the same putts with each of your putters and see which is best.", true, () => Club.Require(_app, "Compare", OpenCompare));
            // Custom games: the Player's own (Creator), moved here from the Play tab (Matt). Not for a Kid profile.
            if (!Kid.IsKid(_app.Profile)) FlowCard("ASSESSMENT", "Custom games", "Build your own putts and play them.", false, () => _app.Push(new CreatorScreen(_app)));
            _flow.Add(Ui.Box("imp-flow-tail"));
            _practice.Add(_flow);
        }

        // A tile: the card art in its window, then the name and one line on a solid bar under it.
        void FlowCard(string art, string title, string words, bool club, Action go)
        {
            VisualElement picture = Ui.Box("window imp-win-top imp-cf-art");
            picture.style.backgroundImage = Resources.Load<Texture2D>("PuttrApp/Cards/" + art);
            VisualElement card = Ui.Box("imp-cf", picture,
                Ui.Box("imp-bar imp-bar-12 grow",
                    Ui.Box("row", Ui.Title(title, "title-20 grow"), club ? Ui.Caps("Club", "caps imp-cf-club") : null),
                    Ui.Text(words, "body-2 imp-gap-4", Ui.Medium))).OnTap(go);
            _flow.Add(card);
        }

        // Compare is a tool, not a game: it lives here, off the Play tab. The catalogue row (FITTING) when it has
        // loaded, else the app's own (id 325).
        void OpenCompare()
        {
            object row = J.Parse("{\"id\":325,\"title\":\"Compare\",\"gameType\":\"FITTING\",\"description\":\"Test putters side by side: the same putts with each one, scored on entry and pace.\"}");
            App.Run(Api.Get("/games/ALL", d => { object hit = J.List(d).Find(g => J.Str(g, "gameType") == "FITTING"); _app.Push(new GameScreen(_app, hit ?? row)); }, _ => _app.Push(new GameScreen(_app, row))));
        }

        // ── Train & learn (CoachHome's library) ──────────────────────────────

        // Each library section as a row of its videos (Matt: videos, not game cards), from /improve/lessons and workouts.
        void Train()
        {
            _train.Clear();
            if (Library.Lessons == null) { Skeleton.Hold(_train, Skeleton.Strip(150)); Library.Load(Train); return; }
            foreach (var s in Library.Sections)
            {
                List<object> videos = Library.In(s.key);
                if (videos.Count == 0) continue;
                string key = s.key;
                _train.Add(Ui.Section(s.title, "See all", () => _app.Push(new LearnScreen(_app, key))));
                ScrollView row = Ui.Scroll(true);
                row.AddToClassList("imp-vid-row");
                row.Add(FreePuttScreen.Space(0).Width(16));
                for (int i = 0; i < videos.Count; i++)
                {
                    object video = videos[i];
                    int index = i;
                    bool locked = Library.Locked(_app, i);
                    VisualElement thumb = Ui.Box("imp-vid-thumb", Ui.Box("imp-vid-play", Ui.Icon("Play", 20, Color.white)));
                    Images.Into(thumb, J.Str(video, "thumbnail"));
                    VisualElement card = Ui.Box("imp-vid", thumb, Ui.Text(J.Str(video, "title"), "imp-vid-name", Ui.Bold),
                        locked ? Ui.Caps("Puttr Club", "caps imp-gap-4") : null)
                        .OnTap(() => { if (Library.Locked(_app, index)) Club.Require(_app, "Lessons", () => { }); else _app.Push(new LessonScreen(_app, video)); });
                    row.Add(card);
                }
                row.Add(FreePuttScreen.Space(0).Width(4));
                _train.Add(row);
            }
        }
    }


    /// <summary>
    /// Full analysis, members only: the page body is AnalysisPage (AnalysisPage.cs), shared with the coach's lesson
    /// workspace through BuildInto; under it, pinned above the tab bar, the coach's plan (CoachPlan.AnalysisBar).
    /// </summary>
    public sealed class AnalysisScreen : AppScreen
    {
        readonly AnalysisPage _page;
        readonly VisualElement _bar;
        Action _refreshBar;

        /// <summary>
        /// The whole analysis page body for any Player, added to host (which should scroll): the range chips and every
        /// card. userId null or the signed-in Player's id reads their own analysis (with Create drill); anyone else's is
        /// read through /coaching/analysis/putts, which the server allows for that Player's coach.
        /// </summary>
        public static void BuildInto(App app, VisualElement host, string userId) => new AnalysisPage(app, host, userId);

        /// <summary>BuildInto with the Player's handedness (RIGHTY or LEFTY), which orients pushes and pulls for another Player.</summary>
        public static void BuildInto(App app, VisualElement host, string userId, string handedness) => new AnalysisPage(app, host, userId, handedness);

        /// <summary>The way in from anywhere (Improve's card, a game over page, Home's finished session): members only.</summary>
        public static void Open(App app) => Club.Require(app, "Full analysis", () => app.Push(new AnalysisScreen(app)));

        public AnalysisScreen(App app)
        {
            ScrollView scroll = Ui.Scroll();
            scroll.AddToClassList("grow");
            scroll.Add(Ui.Header("Full analysis", app.Pop));
            VisualElement content = new VisualElement();
            scroll.Add(content);
            var foot = new VisualElement();
            foot.style.height = 28;
            scroll.Add(foot);
            _page = new AnalysisPage(app, content, J.Str(app.Profile, "userId"));
            // The coach's plan, once there is something for the coach to read (the app shows it once the putts arrive).
            _bar = CoachPlan.AnalysisBar(app, () => _page.Days, out _refreshBar);
            _bar.style.display = DisplayStyle.None;
            bool kid = Kid.IsKid(app.Profile);
            _page.Loaded += () => { if (!kid) _bar.style.display = DisplayStyle.Flex; };
            Root = Ui.Box("grow", scroll, _bar);
            Root.style.flexGrow = 1;
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Improve");
            if (sheet != null) Root.styleSheets.Add(sheet);
            // The Player's coach, by name and face on the bar, and the persona the analysis is written in.
            App.Run(Api.Get("/coach/settings", d => { CoachVoice.Load(d); _refreshBar?.Invoke(); }, _ => { }));
        }

        public override void Shown() => _refreshBar?.Invoke();
    }

    /// <summary>Holed putts by where they came in: a half circle of five arcs, left side to right side, the busiest solid
    /// green. It is the front of the hole, the half nearest the ball, so it is the lower half with the ball under it
    /// (Matt, 2026-11-26: it was drawn as the upper half, the back of the hole, over a ball at the bottom).</summary>
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

        // The hole's centre is at the top of the box; under the arc go its numbers, then the ball's label.
        const float Top = 8f;
        static float Radius(float w, float h) => Mathf.Min(w * 0.5f - 34f, h - Top - 50f);

        void Labels()
        {
            Clear();
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || Front == 0) return;
            Vector2 centre = new Vector2(w * 0.5f, Top);
            float radius = Radius(w, h);
            for (int i = 0; i < 5; i++)
            {
                float angle = Mathf.Deg2Rad * (180f - (i + 0.5f) * 36f);
                Vector2 at = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius + 18f);
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
            Vector2 centre = new Vector2(w * 0.5f, Top);
            float radius = Radius(w, h), width = radius * 0.32f;
            Painter2D p = context.painter2D;
            p.lineWidth = width;
            p.lineCap = LineCap.Butt;
            for (int i = 0; i < 5; i++)
            {
                // Left side is drawn on the left, round the bottom of the hole: from 180 degrees (left) through 90
                // (straight down, the middle of the front) to 0 (right), the screen's y down.
                float from = 180f - (i + 1) * 36f + 1.5f, to = 180f - i * 36f - 1.5f;
                float share = front > 0 ? _bands[i] / (float)most : 0f;
                p.strokeColor = _bands[i] == 0 ? Ui.Surface2 : _bands[i] == most ? Ui.Green : Color.Lerp(Ui.Surface2, Ui.Green, 0.25f + 0.55f * share);
                p.BeginPath(); p.Arc(centre, radius - width * 0.5f, Angle.Degrees(from), Angle.Degrees(to)); p.Stroke();
            }
            p.fillColor = Ui.Base;
            p.BeginPath(); p.Arc(centre, radius * 0.22f, Angle.Degrees(0), Angle.Degrees(180)); p.Fill();
        }
    }
}
