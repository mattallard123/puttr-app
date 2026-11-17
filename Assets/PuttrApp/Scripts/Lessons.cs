// Lessons with a coach, the Player's side (puttr-app components/lessons, improve/CoachVideosScreen):
//
//   FindCoachScreen      GET /lesson/coaches: live coaches first, member and non-member prices
//   LessonCoachScreen    GET /lesson/coach/:id: intro video, about, what you get, reviews, "Book a Lesson"
//   BookLessonScreen     goals + a Stroke Lab session, then Stripe's PaymentSheet (Pay below):
//                        POST /lesson/booking/create -> present -> POST /lesson/booking/confirm
//   MyLessonsScreen      GET /lesson/bookings: Active | Past
//   BookedLessonScreen   GET /lesson/booking/:id: the timeline, submit a session, cancel and refund, the video, rate it
//   PlanDetailScreen     GET /lesson/plan/:id: this week's target, the assigned games, before and after
//   CoachVideosScreen    GET /lab/coaching/videos: "From your coach"
//
// Status: 0 draft, 1 paid, 2 submitted, 3 in review, 4 delivered, 5 completed, 8 canceled, 9 refunded.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>Stripe's PaymentSheet: Plugins/iOS/PuttrPay.swift, Android PuttrPayActivity.java.</summary>
    public sealed class Pay : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PuttrPay_Present(string key, string secret, string customer, string ephemeral, string target, string request);
        public static bool Supported => true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        static void PuttrPay_Present(string key, string secret, string customer, string ephemeral, string target, string request)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var pay = new AndroidJavaClass("co.puttr.app2.PuttrPayActivity"))
                pay.CallStatic("present", activity, key, secret, customer, ephemeral, target, request);
        }
        public static bool Supported => true;
#else
        static void PuttrPay_Present(string key, string secret, string customer, string ephemeral, string target, string request) { }
        public static bool Supported => false;
#endif
        const string ObjectName = "PuttrPay";
        static Pay _one;
        static int _next;
        static readonly Dictionary<string, Action<string, string>> _waiting = new Dictionary<string, Action<string, string>>();

        /// <summary>done("ok" | "cancelled" | "error", message).</summary>
        public static void Present(string publishableKey, string clientSecret, string customer, string ephemeralKey, Action<string, string> done)
        {
            if (!Supported) { done("error", "Payments need the phone app."); return; }
            if (_one == null) { var go = new GameObject(ObjectName); DontDestroyOnLoad(go); _one = go.AddComponent<Pay>(); }
            string id = (++_next).ToString();
            _waiting[id] = done;
            PuttrPay_Present(publishableKey, clientSecret, customer ?? "", ephemeralKey ?? "", ObjectName, id);
        }

        void OnPay(string message)
        {
            string[] parts = message.Split(new[] { '|' }, 3);
            if (parts.Length < 2 || !_waiting.TryGetValue(parts[0], out Action<string, string> done)) return;
            _waiting.Remove(parts[0]);
            done(parts[1], parts.Length > 2 ? parts[2] : null);
        }
    }

    public static class LessonInfo
    {
        public static (string text, Color colour) Status(object b)
        {
            switch (J.Int(b, "status"))
            {
                case 1: return ("Submit a session", Ui.Amber);
                case 2: return (Form.When(b, "dueAt") is DateTime due ? "Due " + due.ToString("ddd", CultureInfo.InvariantCulture) : "With coach", Ui.Amber);
                case 3: return ("In review", Ui.Blue);
                case 4: return ("Ready to watch", Ui.Bright);
                case 5: return ("Completed", Ui.Faint);
                case 8: return ("Canceled", Ui.Faint);
                case 9: return ("Refunded", Ui.Red);
                default: return ("Pending", Ui.Faint);
            }
        }

        public static Label Pill(object b) { (string t, Color c) = Status(b); return AdminLabels.Pill(t, c); }

        public static string Price(double dollars) => Math.Abs(dollars - Math.Round(dollars)) < 0.005 ? "$" + dollars.ToString("0", CultureInfo.InvariantCulture) : "$" + dollars.ToString("0.00", CultureInfo.InvariantCulture);
        public static string Money(double cents) => "$" + (cents / 100.0).ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>The API's member and non-member prices; the client markup (x1.10, x1.20) if it sent only lessonPrice.</summary>
        public static (double member, double nonMember) Prices(object coach)
        {
            double m = J.Num(coach, "memberPrice"), n = J.Num(coach, "nonMemberPrice"), b = J.Num(coach, "lessonPrice");
            if (m <= 0 && b > 0) m = Math.Round(b * 1.10, 2);
            if (n <= 0 && b > 0) n = Math.Round(b * 1.20, 2);
            return (m, n);
        }

        public static string Turnaround(double h) { if (h <= 0) h = 72; return h >= 168 ? "1 week" : h >= 96 ? Math.Round(h / 24) + " days" : h + " hours"; }
        public static string TurnaroundShort(double h) { if (h <= 0) h = 72; return h >= 168 ? "1 wk" : h + " hr"; }

        public static readonly Dictionary<string, string> Specialties = new Dictionary<string, string> { { "STROKE", "Stroke" }, { "SPEED", "Speed" }, { "GREEN_READING", "Green reading" }, { "PRESSURE", "Pressure" } };

        public static VisualElement SpecialtyChips(object coach, int max = 3)
        {
            VisualElement chips = Ui.Box("chips");
            int n = 0;
            foreach (string s in J.Str(coach, "specialties").Split(','))
                if (Specialties.TryGetValue(s.Trim(), out string label) && n++ < max) chips.Add(Ui.Text(label, "chip", Ui.Bold));
            return chips;
        }

        public static string Stars(double rating) { int r = Mathf.Clamp((int)Math.Round(rating), 0, 5); return new string('★', r) + new string('☆', 5 - r); }

        public static Label StarText(double rating, string cls = "feed-name") { Label l = Ui.Text(Stars(rating), cls, Ui.Bold); l.style.color = (Color)new Color32(0xF5, 0x9E, 0x0B, 0xFF); return l; }

        /// <summary>The four steps of a lesson.</summary>
        public static VisualElement Timeline(object b, string coachName)
        {
            int st = J.Int(b, "status");
            VisualElement card = Ui.Box("glass");
            void Row(bool done, bool active, string title, string sub)
            {
                Color c = done ? Ui.Green : active ? Ui.Bright : new Color(1, 1, 1, 0.3f);
                card.Add(Ui.Box("timeline-row", Ui.Icon(done ? "CheckCircle" : "Circle", 20, c), Ui.Box("grow set-words", Ui.Text(title, "feed-name", Ui.Bold), Ui.Text(sub, "muted wrap", Ui.Regular))));
            }
            Row(st >= 1, false, "Booked and paid", Form.Date(b, "created_at", "MMM d, h:mm tt"));
            Row(st >= 2, st == 1, "Session submitted", Form.Date(b, "submittedAt", "MMM d, h:mm tt") is string s && s.Length > 0 ? s : "Attach a putting session");
            string due = Form.Date(b, "dueAt", "ddd h:mm tt");
            Row(st >= 4, st == 2 || st == 3, coachName + " reviews your putting", st >= 4 ? "Done" : due.Length > 0 ? "Due by " + due : "Starts after your session is in");
            Row(st >= 4, false, "Lesson video arrives", Form.Date(b, "deliveredAt", "MMM d, h:mm tt") is string d && d.Length > 0 ? d : "We'll notify you");
            return card;
        }

        /// <summary>Set the booking, read it again, open it (BookedLesson for the Player).</summary>
        public static void Open(App app, string bookingId) => app.Push(new BookedLessonScreen(app, bookingId));

        public static string First(string name) => string.IsNullOrEmpty(name) ? "your coach" : name.Split(' ')[0];

        /// <summary>A Stroke Lab session row: thumb, title, when.</summary>
        public static VisualElement SessionRow(object s, VisualElement right)
        {
            VisualElement thumb = Ui.Box("thumb");
            thumb.style.width = 52; thumb.style.height = 38;
            if (J.Str(s, "thumbUrl").Length > 0) Images.Load(J.Str(s, "thumbUrl"), t => { if (t != null) { thumb.style.backgroundImage = new StyleBackground(t); thumb.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop; } });
            else thumb.Add(Ui.Icon("VideoCamera", 18, Ui.Faint));
            return Ui.Box("set-row", thumb, Ui.Box("grow set-words", Ui.Text("Stroke Lab session", "feed-name", Ui.Bold), Ui.Text(Form.Ago(s, "created_at") + " · video + 3D pose", "muted", Ui.Regular)), right);
        }
    }

    /// <summary>Admin's pill, shared.</summary>
    static class AdminLabels
    {
        public static Label Pill(string text, Color colour)
        {
            Label pill = Ui.Text(text, "chip", Ui.Bold);
            pill.style.color = colour;
            pill.style.backgroundColor = Color.Lerp(Ui.Ink, colour, 0.18f);
            return pill;
        }
    }

    // ── Finding a coach ──────────────────────────────────────────────────────

    public sealed class FindCoachScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body, _list;

        public FindCoachScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Find a Coach", out _body);
            if (Kid.Blocked(app, Kid.Owner, _body)) return;
            _body.Add(Ui.Box("pad", Ui.Text("Book a personal video lesson from a real coach", "sub wrap", Ui.Regular)));
            if (!J.Flag(app.Profile, "isMember"))
                _body.Add(Ui.Box("pad", Ui.Box("glass row choice-on", Ui.Box("grow set-words", Ui.Text("Members save on every lesson", "feed-name", Ui.Bold), Ui.Text("Puttr members get discounted lesson pricing from every coach.", "muted wrap", Ui.Regular)),
                    Ui.Box("btn-small", Ui.Text("Join", "btn-text", Ui.Bold))).OnTap(() => app.Push(new ClubScreen(app, "Lessons")))));
            _list = new VisualElement();
            _body.Add(_list);
            _list.Add(Page.Loading());
        }

        public override void Shown()
        {
            if (_list == null) return;
            App.Run(Api.Get("/lesson/coaches", d => Draw(J.List(d)), _ =>
            {
                _list.Clear();
                _list.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Icon("WifiSlash", 30, Ui.Faint), Ui.Text("Couldn't load coaches. Check your connection and try again.", "sub center wrap", Ui.Regular).Margin(8), Ui.Button("Retry", Shown, "btn btn-ghost").Margin(10))));
            }));
        }

        void Draw(List<object> coaches)
        {
            _list.Clear();
            if (coaches.Count == 0) { _list.Add(Page.Note("No coaches available right now. Check back soon.")); return; }
            bool member = J.Flag(_app.Profile, "isMember");
            VisualElement cols = Ui.Box("cols");
            foreach (object c in coaches)
            {
                object coach = c;
                bool preview = J.Get(c, "live") != null && J.Int(c, "live") != 1;
                bool full = !preview && (J.Int(c, "acceptingLessons") != 1 || J.Int(c, "openLessons") >= 10);
                (double m, double n) = LessonInfo.Prices(c);
                VisualElement price = member
                    ? Ui.Box("", Strike(LessonInfo.Price(n)), Green(LessonInfo.Price(m)), Ui.Text("member price", "muted", Ui.Regular))
                    : Ui.Box("", Ui.Text(LessonInfo.Price(n), "feed-name", Ui.Bold), Ui.Text(LessonInfo.Price(m) + " for members", "muted", Ui.Regular));
                price.style.alignItems = Align.FlexEnd;
                price.style.width = 104;
                price.style.flexShrink = 0;
                string rating = J.Int(c, "ratingCount") > 0 ? null : J.Int(c, "lessonCount") > 0 ? J.Int(c, "lessonCount") + " lessons given" : "New coach";
                VisualElement ratingRow = rating == null ? Ui.Box("row", LessonInfo.StarText(J.Num(c, "avgRating"), "muted"), Ui.Text(" " + J.Num(c, "avgRating").ToString("0.0", CultureInfo.InvariantCulture) + " (" + J.Int(c, "ratingCount") + ")", "muted", Ui.Regular)) : (VisualElement)Ui.Text(rating, "muted", Ui.Regular);
                Label pill = preview ? AdminLabels.Pill("Not live yet", Ui.Blue) : full ? AdminLabels.Pill("Booked out", Ui.Amber) : AdminLabels.Pill(J.Num(c, "turnaroundHours") > 0 ? LessonInfo.TurnaroundShort(J.Num(c, "turnaroundHours")) + " turnaround" : "Available", Ui.Bright);
                VisualElement card = Ui.Box("glass",
                    Ui.Box("row", Ui.Avatar(J.Str(c, "displayName"), J.Str(c, "photoURL")), Ui.Box("grow set-words", Ui.Text(J.Str(c, "displayName"), "feed-name", Ui.Bold), Ui.Text(J.Str(c, "homeTown"), "muted", Ui.Regular), Ui.Text(J.Str(c, "headline"), "muted", Ui.Regular), ratingRow), price),
                    Ui.Box("row", Ui.Box("grow", LessonInfo.SpecialtyChips(c)), pill).Margin(8));
                if (full) card.style.opacity = 0.55f;
                card.OnTap(() => _app.Push(new LessonCoachScreen(_app, J.Str(coach, "userId"))));
                cols.Add(Ui.Box("col pad", card).Margin(8));
            }
            _list.Add(cols);
        }

        internal static Label Strike(string s) { Label l = Ui.Text(s, "muted", Ui.Regular); l.text = "<s>" + s + "</s>"; l.enableRichText = true; return l; }
        internal static Label Green(string s) { Label l = Ui.Text(s, "feed-name", Ui.Bold); l.style.color = Ui.Bright; return l; }
    }

    public sealed class LessonCoachScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        readonly string _id;

        public LessonCoachScreen(App app, string coachId)
        {
            _app = app;
            _id = coachId;
            Root = Page.Make(app, "Coach", out _body);
            if (Kid.Blocked(app, Kid.Owner, _body)) return;
            _body.Add(Page.Loading());
            App.Run(Api.Get("/lesson/coach/" + coachId, Draw, _ => { _body.Clear(); _body.Add(Page.Note("This coach is not available right now.")); }));
        }

        void Draw(object d)
        {
            _body.Clear();
            object c = J.Get(d, "coach");
            string name = J.Str(c, "displayName"), first = LessonInfo.First(name);
            _body.Add(Ui.Box("pad center-box", Ui.Avatar(name, J.Str(c, "photoURL"), true), Ui.Box("row", Ui.Text(name, "h2 wrap", Ui.Bold), Ui.Icon("SealCheck", 18, Ui.Blue)).Margin(8),
                Ui.Text(J.Str(c, "headline"), "sub center wrap", Ui.Regular), LessonInfo.SpecialtyChips(c, 4)));
            _body.Add(Ui.Box("pad stat-tiles",
                Tile(J.Int(c, "lessonCount").ToString(), "LESSONS"),
                Tile(J.Int(c, "ratingCount") > 0 ? J.Num(c, "avgRating").ToString("0.0", CultureInfo.InvariantCulture) + " ★" : "New", "RATING"),
                Tile(LessonInfo.TurnaroundShort(J.Num(c, "turnaroundHours")), "DELIVERY")));
            if (J.Str(c, "introVideoUrl").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("Meet " + first, "feed-name", Ui.Bold), VideoBox.Make(J.Str(c, "introVideoUrl"), 220).Margin(10))));
            if (J.Str(c, "bio").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("About", "feed-name", Ui.Bold), Ui.Text(J.Str(c, "bio"), "sub wrap", Ui.Regular).Margin(6))));
            VisualElement get = Ui.Box("glass", Ui.Text("What you get", "feed-name", Ui.Bold));
            foreach (string s in new[] { "Personal video breakdown of your stroke", "Review of your last 30 days of data", "Clear next steps from your coach" })
                get.Add(Ui.Box("row", Ui.Icon("Check", 16, Ui.Bright), Ui.Text(s, "sub grow wrap", Ui.Regular).Margin(0)).Margin(8));
            get.Add(Ui.Text("Delivered within " + LessonInfo.Turnaround(J.Num(c, "turnaroundHours")) + " of submitting your session, or your money back.", "muted wrap", Ui.Regular).Margin(10));
            _body.Add(Ui.Box("pad", get));
            List<object> reviews = J.List(J.Get(d, "reviews"));
            if (reviews.Count > 0)
            {
                VisualElement card = Ui.Box("glass", Ui.Text("Reviews", "feed-name", Ui.Bold));
                for (int i = 0; i < Math.Min(5, reviews.Count); i++)
                {
                    object r = reviews[i];
                    card.Add(Ui.Box("row", Ui.Avatar(J.Str(r, "displayName"), J.Str(r, "photoURL")), Ui.Box("grow set-words", Ui.Box("row", Ui.Text(J.Str(r, "displayName"), "feed-name grow", Ui.Bold), Ui.Text(Form.Ago(r, "reviewedAt"), "muted", Ui.Regular)),
                        LessonInfo.StarText(J.Num(r, "rating"), "muted"), J.Str(r, "review").Length > 0 ? Ui.Text("“" + J.Str(r, "review") + "”", "sub wrap", Ui.Regular) : null)).Margin(10));
                }
                _body.Add(Ui.Box("pad", card));
            }
            bool member = J.Flag(_app.Profile, "isMember");
            (double m, double n) = LessonInfo.Prices(c);
            VisualElement price = member ? Ui.Box("grow", FindCoachScreen.Green(LessonInfo.Price(m)), FindCoachScreen.Strike(LessonInfo.Price(n)), Ui.Text("member price", "muted", Ui.Regular))
                : Ui.Box("grow", Ui.Text(LessonInfo.Price(n), "h2 wrap", Ui.Bold), Ui.Text("Members pay " + LessonInfo.Price(m) + " · Join", "sec-link", Ui.Medium).OnTap(() => _app.Push(new ClubScreen(_app, "Lessons"))));
            bool accepting = J.Int(c, "acceptingLessons") == 1;
            bool preview = J.Get(c, "live") != null && J.Int(c, "live") != 1;
            VisualElement book = Ui.Button(accepting ? "Book a Lesson" : preview ? "Not live yet" : "Not accepting lessons", () => { if (accepting) _app.Push(new BookLessonScreen(_app, c)); });
            if (!accepting) book.AddToClassList("btn-off");
            book.style.flexGrow = 2;
            _body.Add(Form.Bottom(Ui.Box("glass row", price, book)));
        }

        static VisualElement Tile(string value, string label) => Ui.Box("glass stat-tile", Ui.Text(value, "stat-value", Ui.Bold), Ui.Text(label, "stat-label", Ui.Bold));
    }

    // ── Booking ──────────────────────────────────────────────────────────────

    public sealed class BookLessonScreen : AppScreen
    {
        readonly App _app;
        readonly object _coach;
        readonly VisualElement _body, _sessions;
        string _goals = "";
        object _session;
        bool _paying;

        public BookLessonScreen(App app, object coach)
        {
            _app = app;
            _coach = coach;
            Root = Page.Make(app, "Book a Lesson", out _body);
            if (Kid.Blocked(app, Kid.Owner, _body)) return;
            string name = J.Str(coach, "displayName");
            _body.Add(Ui.Box("pad", Ui.Text("with " + name, "sub", Ui.Regular)));
            VisualElement p = Ui.Box("pad");
            p.Add(Form.Label("1 · Your goals"));
            TextField goals = Form.Field("What do you want help with? Misses, pace, pressure putts...", "", 2000, true);
            goals.RegisterValueChangedCallback(e => _goals = e.newValue);
            p.Add(goals);
            p.Add(Form.Label("2 · Your session"));
            _sessions = new VisualElement();
            _sessions.Add(Page.Loading());
            p.Add(_sessions);
            _body.Add(p);
            // A lesson already under way with this coach.
            App.Run(Api.Get("/lesson/bookings", d =>
            {
                if (J.List(d).Exists(b => J.Str(b, "coachId") == J.Str(coach, "userId") && J.Int(b, "status") >= 1 && J.Int(b, "status") <= 3))
                    p.Add(Ui.Box("glass", Ui.Text("You already have a lesson in progress with " + name, "feed-name wrap", Ui.Bold), Ui.Text("Booking again creates a second, separate lesson.", "muted wrap", Ui.Regular)).Margin(12));
            }, _ => { }));
            bool member = J.Flag(app.Profile, "isMember");
            (double m, double n) = LessonInfo.Prices(coach);
            VisualElement priceCard = Ui.Box("glass", Ui.Box("row", Ui.Text("Lesson with " + name, "feed-name grow", Ui.Bold), member ? FindCoachScreen.Strike(LessonInfo.Price(n)) : null, Ui.Text(LessonInfo.Price(member ? m : n), "h2 wrap", Ui.Bold).Margin(0)));
            if (member) priceCard.Add(Ui.Text("Member discount applied. You save " + LessonInfo.Price(n - m) + ".", "muted", Ui.Regular).Margin(6));
            priceCard.Add(Ui.Text("Delivered within " + LessonInfo.Turnaround(J.Num(coach, "turnaroundHours")) + " of your session, or your money back.", "muted wrap", Ui.Regular).Margin(6));
            _body.Add(Ui.Box("pad", priceCard));
            if (!member)
                _body.Add(Ui.Box("pad", Ui.Box("glass row choice-on", Ui.Box("grow set-words", Ui.Text("Save " + LessonInfo.Price(n - m) + " on this lesson", "feed-name", Ui.Bold), Ui.Text("Puttr members pay " + LessonInfo.Price(m) + " for this lesson, plus discounts with every coach.", "muted wrap", Ui.Regular)),
                    Ui.Box("btn-small", Ui.Text("Join", "btn-text", Ui.Bold))).OnTap(() => app.Push(new ClubScreen(app, "Lessons")))));
            _body.Add(Form.Bottom(Ui.Button("Continue to Payment", () => { if (!_paying) App.Run(Book()); })));
            App.Run(Api.Get("/coaching/user/sessions/" + J.Str(app.Profile, "userId", Auth.Uid), d => Sessions(J.List(d)), _ => Sessions(new List<object>())));
        }

        void Sessions(List<object> all)
        {
            _sessions.Clear();
            string coach = J.Str(_coach, "displayName"), turnaround = LessonInfo.TurnaroundShort(J.Num(_coach, "turnaroundHours"));
            VisualElement card = Ui.Box("glass set-card");
            for (int i = 0; i < Math.Min(3, all.Count); i++)
            {
                object s = all[i];
                bool on = _session == s;
                VisualElement row = LessonInfo.SessionRow(s, on ? Ui.Icon("CheckCircle", 20, Ui.Green) : Ui.Icon("Circle", 20, Ui.Faint));
                if (on) row.AddToClassList("choice-on");
                card.Add(row.OnTap(() => { _session = on ? null : s; Sessions(all); }));
            }
            if (all.Count > 0) _sessions.Add(card);
            bool fresh = all.Exists(s => Form.When(s, "created_at") is DateTime t && (DateTime.Now - t).TotalDays <= 7);
            if (!fresh) _sessions.Add(Ui.Text(all.Count > 0 ? "None of your sessions are from the last week. Record a fresh one so " + coach + " reviews your current form." : "You don't have a session yet. Record a quick Stroke Lab session so " + coach + " has footage to review.", "sub wrap", Ui.Regular).Margin(10));
            _sessions.Add(Ui.Box("glass row", Ui.Icon("VideoCamera", 20, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Record a session", "feed-name", Ui.Bold), Ui.Text("Stroke Lab · video + 3D pose, a few minutes", "muted", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                .Margin(10).OnTap(() => _app.Push(new LabIntroScreen(_app))));
            _sessions.Add(Ui.Text(fresh || all.Count > 0 ? "Or book now and submit a session later. The " + turnaround + " clock starts when your session is in." : "You can also book now and submit a session afterward from My Lessons.", "muted wrap", Ui.Regular).Margin(8));
        }

        IEnumerator Book()
        {
            _paying = true;
            _app.Toast("Starting your booking...");
            object created = null; string error = null;
            yield return Api.Post("/lesson/booking/create", new Dictionary<string, object> { { "coachId", J.Str(_coach, "userId") }, { "goals", _goals }, { "sessionType", _session != null ? "LAB" : null }, { "sessionId", _session != null ? J.Get(_session, "id") : null } },
                d => created = d, e => error = e ?? "failed");
            if (created == null) { _paying = false; Dialog.Ask(Root, "Booking failed", Form.Why(error, "We could not start your booking. Please try again."), "OK", () => { }); yield break; }
            string bookingId = J.Str(created, "bookingId");
            string result = null, message = null;
            Pay.Present(J.Str(created, "publishableKey"), J.Str(created, "paymentIntent"), J.Str(created, "customer"), J.Str(created, "ephemeralKey"), (r, m) => { result = r; message = m; });
            while (result == null) yield return null;
            if (result == "cancelled") { _paying = false; yield break; } // the booking stays a draft, never listed
            if (result != "ok") { _paying = false; Dialog.Ask(Root, "Payment unavailable", message ?? "The payment did not go through.", "OK", () => { }); yield break; }
            object confirmed = null;
            yield return Api.Post("/lesson/booking/confirm", new Dictionary<string, object> { { "bookingId", bookingId } }, d => confirmed = d, e => error = e ?? "failed");
            _paying = false;
            if (confirmed == null) { Dialog.Ask(Root, "Booking failed", Form.Why(error, "Your payment went through but the booking did not confirm. Pull to refresh My Lessons in a moment."), "OK", () => { }); yield break; }
            Confirmation(bookingId);
        }

        void Confirmation(string bookingId)
        {
            _body.Clear();
            string coach = J.Str(_coach, "displayName");
            DateTime now = DateTime.UtcNow;
            var local = new Dictionary<string, object> { { "status", _session != null ? 2.0 : 1.0 }, { "created_at", now.ToString("o") } };
            if (_session != null) { local["submittedAt"] = now.ToString("o"); local["dueAt"] = now.AddHours(J.Num(_coach, "turnaroundHours", 72)).ToString("o"); }
            _body.Add(Ui.Box("pad center-box", Ui.Icon("CheckCircle", 56, Ui.Green), Ui.Text("Lesson booked!", "display center wrap", Ui.Bold).Margin(10),
                Ui.Text(_session != null ? coach + " has your session and your goals." : "Submit a session to start " + coach + "'s clock.", "sub center wrap", Ui.Regular).Margin(6)));
            _body.Add(Ui.Box("pad", LessonInfo.Timeline(local, coach)));
            _body.Add(Ui.Box("pad", Ui.Button("View My Lessons", () => { _app.Pop(); _app.Push(new MyLessonsScreen(_app)); }), Ui.Button("Done", () => _app.ShowTabs("improve"), "btn btn-ghost").Margin(8)));
        }
    }

    public sealed class MyLessonsScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body, _list;
        List<object> _bookings;
        bool _past;

        public MyLessonsScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "My Lessons", out _body);
            if (Kid.Blocked(app, Kid.Owner, _body)) return;
            _body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "Active", "Past" }, 0, i => { _past = i == 1; Draw(); })));
            _list = new VisualElement();
            _body.Add(_list);
            _list.Add(Page.Loading());
        }

        public override void Shown() { if (_list != null) App.Run(Api.Get("/lesson/bookings", d => { _bookings = J.List(d); Draw(); }, _ => { _list.Clear(); _list.Add(Page.Note("Couldn't load your lessons. Check your connection.")); })); }

        void Draw()
        {
            if (_bookings == null) return;
            _list.Clear();
            List<object> shown = _bookings.FindAll(b => { int s = J.Int(b, "status"); return _past ? !(s >= 1 && s <= 4) : s >= 1 && s <= 4; });
            if (!_past)
            {
                object plan = _bookings.Find(b => J.Str(b, "planId").Length > 0 && J.Int(b, "planStatus") == 1);
                if (plan != null)
                    _list.Add(Ui.Box("pad", Ui.Box("glass row choice-on", Ui.Icon("ListChecks", 22, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Practice Plan", "feed-name", Ui.Bold), Ui.Text("from " + J.Str(plan, "coachName") + " · ends " + Form.Date(plan, "planEndDate"), "muted", Ui.Regular)), Ui.Icon("CaretRight", 16, Color.white))
                        .OnTap(() => _app.Push(new PlanDetailScreen(_app, J.Str(plan, "planId"))))));
            }
            if (shown.Count == 0)
            {
                _list.Add(Page.Note(_past ? "No past lessons yet." : "No active lessons. Find a coach to get started."));
                if (!_past) _list.Add(Ui.Box("pad", Ui.Button("Find a Coach", () => _app.Push(new FindCoachScreen(_app)))));
                return;
            }
            VisualElement cols = Ui.Box("cols");
            foreach (object b in shown)
            {
                object booking = b;
                int st = J.Int(b, "status");
                string sub = J.Int(b, "isStudio") == 1 ? "From your coach · " + Form.Date(b, "created_at") : "Booked " + Form.Date(b, "created_at") + " · $" + (J.Num(b, "price") / 100).ToString("0", CultureInfo.InvariantCulture);
                VisualElement right = Ui.Box("", LessonInfo.Pill(b), J.Get(b, "rating") != null ? LessonInfo.StarText(J.Num(b, "rating"), "muted") : null);
                right.style.alignItems = Align.FlexEnd;
                VisualElement card = Ui.Box(st == 4 ? "glass choice-on" : "glass",
                    Ui.Box("row", Ui.Avatar(J.Str(b, "coachName"), J.Str(b, "coachPhoto")), Ui.Box("grow set-words", Ui.Text("Lesson with " + J.Str(b, "coachName"), "feed-name", Ui.Bold), Ui.Text(sub, "muted", Ui.Regular),
                        st == 2 && Form.Date(b, "dueAt").Length > 0 ? Ui.Text("Due by " + Form.Date(b, "dueAt", "ddd h:mm tt"), "muted", Ui.Regular) : null), right));
                if (st == 4 && J.Str(b, "watchedAt").Length == 0) card.Add(Ui.Box("btn", Ui.Text("Watch your lesson", "btn-text", Ui.Bold)).Margin(10));
                cols.Add(Ui.Box("col pad", card.OnTap(() => LessonInfo.Open(_app, J.Str(booking, "id")))).Margin(8));
            }
            _list.Add(cols);
            _list.Add(Ui.Box("pad", Ui.Button("Book a Lesson", () => _app.Push(new FindCoachScreen(_app)), "btn btn-ghost")).Margin(12));
        }
    }

    public sealed class BookedLessonScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _body;
        int _stars;
        readonly HashSet<string> _tags = new HashSet<string>();
        string _review = "";
        bool _sending, _marked;

        public BookedLessonScreen(App app, string bookingId)
        {
            _app = app;
            _id = bookingId;
            Root = Page.Make(app, "Lesson", out _body);
            if (Kid.Blocked(app, Kid.Owner, _body)) return;
            _body.Add(Page.Loading());
        }

        public override void Shown() { if (!Kid.Managed(_app.Profile)) Load(); }

        void Load() => App.Run(Api.Get("/lesson/booking/" + _id, Draw, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load this lesson.")); }));

        void Draw(object b)
        {
            _body.Clear();
            int st = J.Int(b, "status");
            bool delivered = st == 4 || st == 5;
            string coach = J.Str(b, "coachName");
            _body.Add(Ui.Box("pad row", Ui.Avatar(coach, J.Str(b, "coachPhoto")), Ui.Box("grow set-words", Ui.Text(delivered ? "Your Lesson" : "Lesson with " + coach, "h2 wrap", Ui.Bold), Ui.Text(coach + " · " + Form.Date(b, "created_at"), "muted", Ui.Regular)), LessonInfo.Pill(b)));
            if (!delivered)
            {
                _body.Add(Ui.Box("pad", LessonInfo.Timeline(b, coach)));
                if (st == 1) Submit(b, coach);
                if (J.Str(b, "goals").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("Your goals", "feed-name", Ui.Bold), Ui.Text("“" + J.Str(b, "goals") + "”", "sub wrap", Ui.Regular).Margin(6))));
                if (st == 1) _body.Add(Ui.Text("Cancel lesson and get a refund", "sec-link center", Ui.Medium).Margin(16).OnTap(() =>
                    Dialog.Ask(Root, "Cancel this lesson?", "You will get a full refund since your coach has not started yet.", "Cancel and refund", () =>
                        App.Run(Api.Post("/lesson/booking/cancel", new Dictionary<string, object> { { "bookingId", _id } }, _ => { _app.Toast("Lesson canceled. Your refund is on its way."); _app.Pop(); }, e => _app.Toast(Form.Why(e, "Could not cancel. Try again.")))), true)));
                return;
            }
            if (J.Str(b, "watchedAt").Length == 0 && !_marked) { _marked = true; App.Run(Api.Post("/lesson/booking/watched", new Dictionary<string, object> { { "bookingId", _id } }, null, null)); }
            if (J.Str(b, "videoUrl").Length > 0)
            {
                _body.Add(Ui.Box("pad", VideoBox.Make(J.Str(b, "videoUrl"), 230, J.Str(b, "videoThumbnail"), J.Num(b, "videoLength"))));
                if (J.Str(b, "videoTranscodeStatus") == "pending") _body.Add(Ui.Box("pad", Ui.Text("Optimizing video for streaming… playback may stall until it finishes.", "muted wrap", Ui.Regular)));
            }
            if (J.Str(b, "coachSummary").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text(coach + "'s notes", "feed-name", Ui.Bold), Ui.Text(J.Str(b, "coachSummary"), "sub wrap", Ui.Regular).Margin(6))));
            List<object> findings = J.List(J.Parse(J.Str(b, "findings")));
            if (findings.Count > 0)
            {
                VisualElement card = Ui.Box("glass set-card", Ui.Text("Key findings", "feed-name", Ui.Bold));
                foreach (object f in findings) card.Add(Ui.Box("set-row", Ui.Text(J.Str(f, "label"), "sub grow", Ui.Regular), Ui.Text(J.Str(f, "value"), "feed-name", Ui.Bold)));
                _body.Add(Ui.Box("pad", card));
            }
            if (J.Str(b, "planId").Length > 0)
                _body.Add(Ui.Box("pad", Ui.Box("glass row choice-on", Ui.Icon("ListChecks", 22, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Your practice plan", "feed-name", Ui.Bold), Ui.Text(coach + " assigned games to work on. Track your progress.", "muted wrap", Ui.Regular)), Ui.Icon("CaretRight", 16, Color.white))
                    .OnTap(() => _app.Push(new PlanDetailScreen(_app, J.Str(b, "planId"))))));
            if (J.Int(b, "isStudio") != 1) Rating(b);
        }

        void Submit(object b, string coach)
        {
            VisualElement box = Ui.Box("pad", Form.Label("Submit your session"), Page.Loading());
            _body.Add(box);
            App.Run(Api.Get("/coaching/user/sessions/" + J.Str(_app.Profile, "userId", Auth.Uid), d =>
            {
                List<object> sessions = J.List(d);
                box.Clear();
                box.Add(Form.Label("Submit your session"));
                if (sessions.Count == 0) { box.Add(Ui.Text("Record a Stroke Lab session with video, then come back here to submit it to " + coach + ".", "sub wrap", Ui.Regular)); return; }
                VisualElement card = Ui.Box("glass set-card");
                for (int i = 0; i < Math.Min(5, sessions.Count); i++)
                {
                    object s = sessions[i];
                    card.Add(LessonInfo.SessionRow(s, Ui.Box("btn-small", Ui.Text("Submit", "btn-text", Ui.Bold)).OnTap(() =>
                        App.Run(Api.Post("/lesson/booking/attachSession", new Dictionary<string, object> { { "bookingId", _id }, { "sessionType", "LAB" }, { "sessionId", J.Get(s, "id") } }, _ => Load(),
                            e => Dialog.Ask(Root, "Could not submit", Form.Why(e, "Try again in a moment."), "OK", () => { }))))));
                }
                box.Add(card);
            }, _ => { }));
        }

        void Rating(object b)
        {
            if (J.Get(b, "rating") != null)
            {
                _body.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Text("Your rating", "feed-name", Ui.Bold), LessonInfo.StarText(J.Num(b, "rating"), "h2"), Ui.Text("Thanks for the feedback!", "muted", Ui.Regular),
                    J.Str(b, "review").Length > 0 ? Ui.Text("“" + J.Str(b, "review") + "”", "sub center wrap", Ui.Regular).Margin(6) : null)));
                return;
            }
            VisualElement card = Ui.Box("glass center-box", Ui.Text("How was your lesson?", "feed-name", Ui.Bold), Ui.Text("Your rating helps other players find the right coach.", "muted center wrap", Ui.Regular));
            VisualElement stars = Ui.Box("row");
            for (int i = 1; i <= 5; i++)
            {
                int n = i;
                Label star = Ui.Text(i <= _stars ? "★" : "☆", "display wrap", Ui.Bold);
                star.style.color = (Color)new Color32(0xF5, 0x9E, 0x0B, 0xFF);
                star.style.marginRight = 6;
                stars.Add(star.OnTap(() => { _stars = n; Load(); }));
            }
            card.Add(stars.Margin(8));
            if (_stars > 0)
            {
                VisualElement tags = Ui.Box("pills");
                foreach (string t in new[] { "Clear plan", "Great video", "Fast delivery", "Fixed my miss" })
                {
                    string tag = t;
                    VisualElement pill = Ui.Box(_tags.Contains(t) ? "scope scope-on" : "scope", Ui.Text(t, "scope-text", Ui.Bold));
                    tags.Add(pill.OnTap(() => { if (!_tags.Remove(tag)) _tags.Add(tag); pill.EnableInClassList("scope-on", _tags.Contains(tag)); }));
                }
                card.Add(tags.Margin(10));
                TextField review = Form.Field("Anything you'd tell other players? (optional)", _review, 2000, true);
                review.RegisterValueChangedCallback(e => _review = e.newValue);
                card.Add(review.Margin(8));
                card.Add(Ui.Button(_sending ? "Submitting..." : "Submit Review", () =>
                {
                    if (_sending) return;
                    _sending = true;
                    App.Run(Api.Post("/lesson/booking/review", new Dictionary<string, object> { { "bookingId", _id }, { "rating", _stars }, { "tags", new List<object>(_tags) }, { "review", _review.Trim().Length > 0 ? _review.Trim() : null } },
                        _ => { _sending = false; Load(); }, e => { _sending = false; Dialog.Ask(Root, "Review failed", Form.Why(e, "Try again in a moment."), "OK", () => { }); }));
                }));
            }
            _body.Add(Ui.Box("pad", card));
        }
    }

    public sealed class PlanDetailScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _body;

        public PlanDetailScreen(App app, string planId)
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
            List<object> items = J.List(J.Get(d, "items"));
            bool done = J.Int(plan, "status") == 2;
            string coach = J.Str(plan, "coachName");
            VisualElement head = Ui.Box("pad row", Ui.Box("grow", Ui.Text("Practice Plan", "h2 wrap", Ui.Bold), Ui.Text(done ? "Completed" : "Ends " + Form.Date(plan, "endDate"), "muted", Ui.Regular)));
            if (!done) head.Add(Ui.Box("ring", Ui.Text(J.Int(d, "daysLeft") + "d", "feed-name", Ui.Bold)));
            _body.Add(head);
            if (done)
            {
                _body.Add(Ui.Box("pad center-box", Ui.Icon("CheckCircle", 48, Ui.Green), Ui.Text("Plan complete!", "display center wrap", Ui.Bold).Margin(8), Ui.Text("Nice work with " + coach + ".", "sub center", Ui.Regular)));
                Progress(J.Get(d, "progress"));
                if (J.Str(plan, "bookingId").Length > 0)
                    _body.Add(Ui.Box("pad", Ui.Button("Book your next lesson with " + LessonInfo.First(coach), () => App.Run(Api.Get("/lesson/coach/" + J.Str(plan, "coachId"), c => _app.Push(new BookLessonScreen(_app, J.Get(c, "coach"))), _ => _app.Toast("This coach is not taking lessons right now.")))),
                        Ui.Text("Maybe later", "sec-link center", Ui.Medium).Margin(12).OnTap(_app.Pop)));
            }
            else
            {
                int target = J.Int(d, "weekTarget"), sessions = J.Int(d, "weekSessions");
                _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Box("row", Ui.Text("This week", "feed-name grow", Ui.Bold), Ui.Text("Target " + target + " sessions", "muted", Ui.Regular)),
                    Ui.Progress(target > 0 ? Mathf.Min(1, sessions / (float)target) : 0).Margin(8),
                    Ui.Text(sessions >= target ? "You hit this week's target. Nice work." : sessions + " of " + target + " done. " + (target - sessions) + " more keeps you on track.", "muted wrap", Ui.Regular).Margin(8))));
            }
            _body.Add(Ui.Section("Assigned practice"));
            VisualElement cols = Ui.Box("cols");
            foreach (object it in items)
            {
                object item = it;
                int goal = J.Int(it, "targetCount"), week = Mathf.Min(J.Int(it, "weekDone"), goal);
                VisualElement dots = Ui.Box("dots");
                for (int i = 0; i < goal; i++) dots.Add(Ui.Box(i < week ? "dot dot-on" : "dot"));
                VisualElement card = Ui.Box("glass",
                    Ui.Box("row", Ui.Bubble(GameIconsFor(J.Str(it, "gameType")), Ui.Bright), Ui.Box("grow set-words", Ui.Text(J.Str(it, "gameTitle"), "feed-name", Ui.Bold), J.Str(it, "note").Length > 0 ? Ui.Text("“" + J.Str(it, "note") + "”", "muted wrap", Ui.Regular) : null),
                        done ? null : Ui.Box("btn-small", Ui.Text("Play", "btn-text", Ui.Bold)).OnTap(() => Play(item))),
                    Ui.Box("row", dots, Ui.Text(week + " of " + goal + " this week", "muted", Ui.Regular).Margin(0)).Margin(8));
                cols.Add(Ui.Box("col pad", card).Margin(8));
            }
            _body.Add(cols);
            if (!done)
            {
                string until = Form.When(plan, "endDate") is DateTime end ? end.AddDays(7).ToString("MMM d", CultureInfo.InvariantCulture) : "";
                _body.Add(Ui.Box("pad", Ui.Box("glass row", Ui.Avatar(coach, J.Str(plan, "coachPhoto")), Ui.Text("Your coach can see your plan progress until " + until + ".", "muted grow wrap", Ui.Regular).Margin(0))).Margin(10));
            }
        }

        internal static string GameIconsFor(string type) => type == "STREAK" ? "Fire" : type == "CENTERCUT" ? "Target" : type == "TOURNAMENT" ? "Trophy" : "FillTarget";

        void Play(object item)
        {
            // The game, from the plan item's fields (no planItemId: plays count by gameId).
            var game = new Dictionary<string, object>(J.Obj(item) ?? new Dictionary<string, object>());
            game["id"] = J.Get(item, "gameId");
            game["title"] = J.Str(item, "gameTitle");
            game["description"] = J.Str(item, "gameDescription");
            _app.Push(new GameScreen(_app, game));
        }

        void Progress(object p)
        {
            object before = J.Get(p, "before"), after = J.Get(p, "after");
            var rows = new List<(string label, string from, string to, bool better)>();
            if (J.Get(before, "makePct") != null && J.Get(after, "makePct") != null) rows.Add(("Make %", J.Int(before, "makePct") + "%", J.Int(after, "makePct") + "%", J.Num(after, "makePct") >= J.Num(before, "makePct")));
            foreach (object band in J.List(J.Get(after, "bands")))
            {
                object was = J.List(J.Get(before, "bands")).Find(x => J.Str(x, "label") == J.Str(band, "label"));
                if (was != null && J.Get(was, "pct") != null && J.Get(band, "pct") != null) rows.Add(("Make % " + J.Str(band, "label"), J.Int(was, "pct") + "%", J.Int(band, "pct") + "%", J.Num(band, "pct") >= J.Num(was, "pct")));
            }
            if (J.Get(before, "avgRollout") != null && J.Get(after, "avgRollout") != null)
            {
                string Pace(double v) => Math.Abs(v).ToString("0", CultureInfo.InvariantCulture) + " in " + (v >= 0 ? "past" : "short");
                rows.Add(("Pace on misses", Pace(J.Num(before, "avgRollout")), Pace(J.Num(after, "avgRollout")), Math.Abs(J.Num(after, "avgRollout")) <= Math.Abs(J.Num(before, "avgRollout"))));
            }
            if (rows.Count == 0) return;
            VisualElement card = Ui.Box("glass set-card", Ui.Text("Your progress", "feed-name", Ui.Bold));
            foreach (var r in rows)
            {
                Label to = Ui.Text(r.to, "feed-name", Ui.Bold);
                to.style.color = r.better ? Ui.Bright : Ui.Amber;
                card.Add(Ui.Box("set-row", Ui.Text(r.label, "sub grow", Ui.Regular), Ui.Text(r.from + "  →  ", "muted", Ui.Regular), to));
            }
            card.Add(Ui.Text("Compared to your 30 days before the plan.", "muted", Ui.Regular).Margin(8));
            _body.Add(Ui.Box("pad", card));
        }
    }

    public sealed class CoachVideosScreen : AppScreen
    {
        public CoachVideosScreen(App app)
        {
            Root = Page.Make(app, "From your coach", out VisualElement body);
            if (Kid.Blocked(app, Kid.Lessons, body)) return;
            body.Add(Ui.Box("pad", Ui.Text("Lesson videos your coach recorded for you", "sub wrap", Ui.Regular)));
            VisualElement list = new VisualElement();
            body.Add(list);
            list.Add(Page.Loading());
            App.Run(Api.Get("/lab/coaching/videos", d =>
            {
                list.Clear();
                List<object> videos = J.List(d);
                if (videos.Count == 0) { list.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Icon("VideoCamera", 30, Ui.Faint), Ui.Text("Nothing here yet. When a coach sends you a lesson video, it lands here.", "sub center wrap", Ui.Regular).Margin(8)))); return; }
                VisualElement cols = Ui.Box("cols");
                foreach (object v in videos)
                {
                    object video = v;
                    VisualElement card = Ui.Box("glass",
                        VideoBox.Make(J.Str(v, "publicUrl"), 190, J.Str(v, "thumbnail"), J.Num(v, "videoLength")),
                        Ui.Text(J.Str(v, "videoTitle").Length > 0 ? J.Str(v, "videoTitle") : "Coaching video", "feed-name", Ui.Bold).Margin(8),
                        Ui.Text("Uploaded " + Form.Ago(v, "created_at"), "muted", Ui.Regular),
                        Ui.Box("row", Ui.Avatar(J.Str(v, "displayName"), J.Str(v, "photoURL")), Ui.Box("grow set-words", Ui.Text(J.Str(v, "displayName"), "feed-name", Ui.Bold), Ui.Text(J.Str(v, "homeTown"), "muted", Ui.Regular))).Margin(10)
                            .OnTap(() => ProfileScreen.Open(app, J.Str(video, "coachId"))));
                    if (J.Str(v, "bookingId").Length > 0) card.Add(Ui.Text("Open the lesson", "sec-link", Ui.Medium).Margin(8).OnTap(() => LessonInfo.Open(app, J.Str(video, "bookingId"))));
                    cols.Add(Ui.Box("col pad", card).Margin(8));
                }
                list.Add(cols);
            }, _ => { list.Clear(); list.Add(Page.Note("Couldn't load your videos. Check your connection.")); }));
        }
    }
}
