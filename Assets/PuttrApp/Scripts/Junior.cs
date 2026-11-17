// Puttr Junior (puttr-app components/junior) and making a private tournament (tournaments/CreateTournament.js).
//
//   JuniorLandingScreen        GET /junior/application: none -> the pitch; status 3 -> the parent waitlist; else status
//   JuniorApplyScreen          three steps -> POST /junior/apply (under 13 goes to the parent waitlist, no age copy)
//   JuniorStatusScreen         submitted (with the parent's sign-off for a minor), approved, not this time
//   JuniorParentWaitlistScreen POST /junior/parentWaitlist {parentEmail}
//   CreateTournamentScreen     details (a GIF cover from Giphy), then invites -> POST /tournaments/create (4 rounds of 9)
//
// Status: 0 submitted, 1 approved, 2 rejected, 3 parent waitlist.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class JuniorLandingScreen : AppScreen
    {
        public JuniorLandingScreen(App app)
        {
            Root = Page.Make(app, "Puttr Junior", out VisualElement body);
            if (Kid.Blocked(app, Kid.Owner, body)) return;
            body.Add(Page.Loading());
            App.Run(Api.Get("/junior/application", d =>
            {
                object a = J.Get(d, "application");
                if (a != null) { app.Pop(); app.Push(J.Int(a, "status") == 3 ? (AppScreen)new JuniorParentWaitlistScreen(app, J.Str(a, "parentEmail")) : new JuniorStatusScreen(app)); return; }
                body.Clear();
                VisualElement p = Ui.Box("pad", AdminLabels.Pill("★ PUTTR JUNIOR", Ui.Bright));
                Label head = Ui.Text("Free Puttr Club for junior golfers who <color=#7AD446>share the journey</color>", "display wrap", Ui.Bold);
                head.enableRichText = true;
                p.Add(head.Margin(10));
                p.Add(Ui.Text("For high school and college golfers ages 13 and up. Post your putting progress, get full Club access on us.", "sub wrap", Ui.Regular).Margin(8));
                p.Add(Form.Label("What you get"));
                p.Add(Item("Medal", "Puttr Club membership, free", "Every game, quest, league and tournament unlocked"));
                p.Add(Item("Trophy", "Junior tournaments", "Compete in your age division with verified stats"));
                p.Add(Item("FilmReel", "A putting resume", "PQS trend and tournament results you can share with coaches"));
                p.Add(Form.Label("What we ask"));
                p.Add(Item("Megaphone", "1 post a month", "Share a reel or result on Instagram, TikTok or YouTube with #PuttrJunior"));
                p.Add(Item("CalendarBlank", "Stay active", "Membership renews each year while you keep posting"));
                p.Add(Ui.Button("Start Application", () => app.Push(new JuniorApplyScreen(app))).Margin(18));
                p.Add(Ui.Text("Takes about 2 minutes. Reviewed by the Puttr team.", "muted center", Ui.Regular).Margin(8));
                body.Add(p);
            }, _ => { body.Clear(); body.Add(Page.Note("Couldn't load. Check your connection.")); }));
        }

        static VisualElement Item(string icon, string title, string words) => Ui.Box("glass row", Ui.Bubble(icon, Ui.Bright), Ui.Box("grow set-words", Ui.Text(title, "feed-name", Ui.Bold), Ui.Text(words, "muted wrap", Ui.Regular))).Margin(8);
    }

    public static class JuniorMath
    {
        public static int Age(DateTime birth, DateTime today) { int a = today.Year - birth.Year; if (birth.Date > today.AddYears(-a)) a--; return a; }
        /// <summary>A minor in a state that bars posting benefits for high school athletes (US AL, HI): no posting requirement.</summary>
        public static bool NoPostTrack(int age, string country, string region) => age < 18 && country == "US" && (region == "AL" || region == "HI");
    }

    public sealed class JuniorApplyScreen : AppScreen
    {
        static readonly string[] Levels = { "CASUAL", "SCHOOL_TEAM", "TOURNAMENTS" }, Platforms = { "INSTAGRAM", "TIKTOK", "YOUTUBE" }, Bands = { "LT500", "500", "5K", "25K" };
        readonly App _app;
        readonly VisualElement _top, _body, _foot;
        int _step = 1;
        int _day = 1, _month = 1, _year = 2008;
        bool _dateSet;
        string _hometown = "", _region, _country, _handicap, _level, _school = "", _ig = "", _tt = "", _yt = "", _platform, _band, _extra = "", _parentName = "", _parentEmail = "";
        bool _agreePost, _agreeSchool, _agreeShare, _sending;

        public JuniorApplyScreen(App app)
        {
            _app = app;
            _top = new VisualElement(); _body = new VisualElement(); _foot = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(_top); scroll.Add(_body); scroll.Add(_foot); scroll.Add(FreePuttScreen.Space(28));
            Root = scroll;
            _hometown = J.Str(app.Profile, "homeTown");
            _handicap = J.Str(app.Profile, "realHandicap");
            if (Form.When(app.Profile, "birthDate") is DateTime b) { _day = b.Day; _month = b.Month; _year = b.Year; _dateSet = true; }
            Draw();
        }

        DateTime Birth => new DateTime(_year, _month, Mathf.Min(_day, DateTime.DaysInMonth(_year, _month)));
        int Age => JuniorMath.Age(Birth, DateTime.Today);
        bool Minor => Age < 18;
        bool NoPost => JuniorMath.NoPostTrack(Age, _country, _region);

        bool Valid()
        {
            if (_step == 1) return _dateSet && _hometown.Trim().Length > 0;
            if (_step == 2) return (_ig + _tt + _yt).Trim().Length > 0;
            bool parent = !Minor || (_parentName.Trim().Length > 0 && _parentEmail.Contains("@"));
            return parent && _agreePost && (NoPost || (_agreeSchool && _agreeShare)) && (!NoPost || _agreeShare);
        }

        void Draw()
        {
            _top.Clear(); _body.Clear();
            _top.Add(Form.Steps(_step, 3, () => { if (_step > 1) { _step--; Draw(); } else _app.Pop(); }));
            VisualElement p = Ui.Box("pad");
            _body.Add(p);
            if (_step == 1) About(p); else if (_step == 2) Socials(p); else Agreement(p);
            Foot();
        }

        void Foot()
        {
            _foot.Clear();
            VisualElement go = Ui.Button(_sending ? "Submitting..." : _step == 3 ? "Submit Application" : "Continue", Next);
            if (!Valid() || _sending) go.AddToClassList("btn-off");
            _foot.Add(Form.Bottom(go));
        }

        TextField Text(VisualElement p, string label, string placeholder, string value, Action<string> set, int max = 120, bool multi = false)
        {
            p.Add(Form.Label(label));
            TextField f = Form.Field(placeholder, value, max, multi);
            f.RegisterValueChangedCallback(e => { set(e.newValue); Foot(); });
            p.Add(f);
            return f;
        }

        void About(VisualElement p)
        {
            p.Add(Ui.Text("About you", "h2 wrap", Ui.Bold));
            p.Add(Form.Label("Date of birth"));
            string[] months = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
            p.Add(Ui.Box("row",
                FreePuttScreen.Stepper(() => months[_month - 1].Substring(0, 3), d => { _month = (_month - 1 + d + 12) % 12 + 1; _dateSet = true; Foot(); }),
                FreePuttScreen.Stepper(() => _day.ToString(), d => { _day = (_day - 1 + d + 31) % 31 + 1; _dateSet = true; Foot(); }).Margin(0),
                FreePuttScreen.Stepper(() => _year.ToString(), d => { _year = Mathf.Clamp(_year + d, 1950, DateTime.Today.Year); _dateSet = true; Foot(); })));
            if (!_dateSet) p.Add(Ui.Text("Select your birthday", "muted", Ui.Regular).Margin(4));
            p.Add(Form.Label("Hometown"));
            p.Add(Ui.Box("glass row", Ui.Icon("Target", 18, _hometown.Length > 0 ? Ui.Green : Ui.Faint), Ui.Text(_hometown.Length > 0 ? _hometown : "Search your city", "feed-name grow", Ui.Medium).Margin(0))
                .OnTap(() => _app.Push(new CityScreen(_app, _hometown, (name, lat, lng) => { _hometown = name; _region = CityScreen.LastRegion; _country = CityScreen.LastCountry; Draw(); }))));
            Text(p, "Handicap, or your best guess", "e.g. 12.4", _handicap, v => _handicap = v, 6);
            p.Add(Form.Label("How do you compete?"));
            p.Add(Form.Pills(new[] { "Casual", "School team", "Tournaments" }, Array.IndexOf(Levels, _level), i => _level = Levels[i]));
            Text(p, "School or team (optional)", "e.g. Franklin High School", _school, v => _school = v);
        }

        void Socials(VisualElement p)
        {
            p.Add(Ui.Text("Your socials", "h2 wrap", Ui.Bold));
            p.Add(Ui.Box("glass", Ui.Text("Add at least one account you actively post on. This is where your Puttr content will live.", "sub wrap", Ui.Regular)).Margin(10));
            Text(p, "Instagram", "@handle", _ig, v => _ig = v, 40);
            Text(p, "TikTok", "@handle", _tt, v => _tt = v, 40);
            Text(p, "YouTube (optional)", "Channel link", _yt, v => _yt = v, 200);
            p.Add(Form.Label("Where do you post most?"));
            p.Add(Form.Pills(new[] { "Instagram", "TikTok", "YouTube" }, Array.IndexOf(Platforms, _platform), i => _platform = Platforms[i]));
            p.Add(Form.Label("Roughly how many followers there?"));
            p.Add(Form.Pills(new[] { "<500", "500+", "5k+", "25k+" }, Array.IndexOf(Bands, _band), i => _band = Bands[i]));
            p.Add(Ui.Text("Follower count does not decide approval. We care that you actually post.", "muted wrap", Ui.Regular));
            Text(p, "Anything else we should see? (optional)", "Golf resume, swing page, best posts...", _extra, v => _extra = v, 2000, true);
        }

        void Agreement(VisualElement p)
        {
            p.Add(Ui.Text("The agreement", "h2 wrap", Ui.Bold));
            if (NoPost) p.Add(Ui.Box("glass", Ui.Icon("Question", 20, Ui.Amber), Ui.Text("Your state doesn't allow high school athletes to receive benefits in exchange for posting. Good news: you can still get free Puttr Club with no posting requirement. Share whenever you want to, totally optional.", "sub wrap", Ui.Regular)).Margin(10));
            else if (Minor) p.Add(Ui.Box("glass", Ui.Icon("UsersThree", 20, Ui.Blue), Ui.Text("You're under 18, so a parent or guardian signs off. We'll email them a consent link, your membership activates after they approve and you're accepted.", "sub wrap", Ui.Regular)).Margin(10));
            if (Minor)
            {
                Text(p, "Parent or guardian name", "Full name", _parentName, v => _parentName = v, 120);
                TextField email = Text(p, "Parent or guardian email", "parent@email.com", _parentEmail, v => _parentEmail = v.Trim(), 200);
                email.keyboardType = TouchScreenKeyboardType.EmailAddress;
            }
            p.Add(Form.Label("You agree to"));
            if (!NoPost)
            {
                p.Add(Form.Check(_agreePost, "Post at least once a month tagged #PuttrJunior and say \"Puttr gave me this free because I'm a junior golfer\" (your own words are fine)", on => { _agreePost = on; Foot(); }));
                p.Add(Form.Check(_agreeSchool, "No school names, logos, uniforms or mascots in any Puttr content", on => { _agreeSchool = on; Foot(); }));
                p.Add(Form.Check(_agreeShare, "Puttr may reshare my posts and show my results in the app (first name + last initial only)", on => { _agreeShare = on; Foot(); }));
            }
            else
            {
                p.Add(Form.Check(_agreePost, "Keep my account active and re-verify each year", on => { _agreePost = on; Foot(); }));
                p.Add(Form.Check(_agreeShare, "Puttr may show my results in the app (first name + last initial only)", on => { _agreeShare = on; Foot(); }));
                p.Add(Form.Check(_agreeSchool, "Puttr may reshare posts I choose to make", on => _agreeSchool = on));
            }
        }

        void Next()
        {
            if (!Valid() || _sending) return;
            if (_step == 1 && Age < 13) { _app.Pop(); _app.Push(new JuniorParentWaitlistScreen(_app, null)); return; }
            if (_step < 3) { _step++; Draw(); return; }
            _sending = true; Foot();
            string Null(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            var body = new Dictionary<string, object>
            {
                { "birthDate", Birth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, { "hometown", _hometown }, { "region", _region }, { "country", _country },
                { "handicap", Null(_handicap) }, { "competitiveLevel", _level }, { "schoolTeam", Null(_school) },
                { "instagram", Null(_ig?.TrimStart('@')) }, { "tiktok", Null(_tt?.TrimStart('@')) }, { "youtube", Null(_yt) },
                { "primaryPlatform", _platform }, { "followerBand", _band }, { "extraInfo", Null(_extra) }, { "agreed", true },
            };
            if (Minor) { body["parentName"] = _parentName.Trim(); body["parentEmail"] = _parentEmail.Trim(); }
            App.Run(Api.Post("/junior/apply", body, _ => { _app.Pop(); _app.Push(new JuniorStatusScreen(_app)); },
                e => { _sending = false; Foot(); Dialog.Ask(Root, "Submit failed", Form.Why(e, "Could not submit. Check your connection and try again."), "OK", () => { }); }));
        }
    }

    public sealed class JuniorStatusScreen : AppScreen
    {
        public JuniorStatusScreen(App app)
        {
            Root = Page.Make(app, "Junior Membership", out VisualElement body);
            body.Add(Page.Loading());
            App.Run(Api.Get("/junior/application", d =>
            {
                object a = J.Get(d, "application");
                if (a == null || J.Int(a, "status") == 3) { app.Pop(); app.Push(new JuniorLandingScreen(app)); return; }
                body.Clear();
                string first = HomeScreen.FirstName(app.Profile);
                int st = J.Int(a, "status");
                if (st == 1)
                {
                    App.Run(Api.Get("/profile/", p => app.Profile = J.First(p), _ => { }));
                    VisualElement card = Ui.Box("glass center-box", Ui.Icon("CheckCircle", 48, Ui.Green), AdminLabels.Pill("PUTTR JUNIOR", Ui.Bright).Margin(8), Ui.Text(first.Length > 0 ? "You're in, " + first : "You're in", "display center wrap", Ui.Bold).Margin(8),
                        Ui.Text("Puttr Club is unlocked. Every game, league and tournament is yours.", "sub center wrap", Ui.Regular).Margin(6),
                        Ui.Text(J.Int(a, "postingRequired", 1) == 1 ? "Your side of the deal starts now: 1 post a month with #PuttrJunior. First idea: your next personal best reel." : "No posting requirement applies to you. Share whenever you want to, tag #PuttrJunior so we can find it.", "sub center wrap", Ui.Regular).Margin(6),
                        Ui.Button("Start Putting", () => app.ShowTabs("play")).Margin(14));
                    if (Form.When(a, "expiresAt") is DateTime ex) card.Add(Ui.Text("Membership renews " + ex.ToString("MMMM yyyy", CultureInfo.InvariantCulture) + " while you stay active", "muted center", Ui.Regular).Margin(8));
                    body.Add(Ui.Box("pad", card));
                    return;
                }
                if (st == 2)
                {
                    body.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Icon("XCircle", 44, Ui.Red), Ui.Text("Not this time", "display center wrap", Ui.Bold).Margin(8),
                        Ui.Text("After review, we are not able to bring you into the program right now. Keep putting and posting, you are welcome to apply again in a few months.", "sub center wrap", Ui.Regular).Margin(6))));
                    return;
                }
                bool minor = J.Str(a, "parentEmail").Length > 0, consent = J.Str(a, "parentConsentAt").Length > 0;
                VisualElement steps = Ui.Box("glass");
                void Row(string state, string icon, string title, string words)
                {
                    Color c = state == "done" ? Ui.Green : state == "current" ? Color.white : new Color(1, 1, 1, 0.3f);
                    steps.Add(Ui.Box("timeline-row", Ui.Icon(icon, 22, c), Ui.Box("grow set-words", Ui.Text(title, "feed-name", Ui.Bold), Ui.Text(words, "muted wrap", Ui.Regular))));
                }
                Row("done", "CheckCircle", "Application in, " + Form.Date(a, "submittedAt"), first.Length > 0 ? "Nice work, " + first + ". Sit tight." : "Nice work. Sit tight.");
                if (minor) Row(consent ? "done" : "current", consent ? "CheckCircle" : "Bell", consent ? "Parent approved" : "Waiting on your parent", consent ? "Your parent signed off. One step left." : "We emailed " + J.Str(a, "parentEmail") + " for sign-off. Give them a nudge.");
                Row(minor && !consent ? "todo" : "current", "Target", "Puttr team review", "We review every application by hand, usually within 3 days. You will hear back by push and email.");
                Row("todo", "Medal", "Membership activates", "Free Puttr Club, every game and tournament unlocked.");
                body.Add(Ui.Box("pad", steps));
            }, _ => { body.Clear(); body.Add(Page.Note("Couldn't load. Check your connection.")); }));
        }
    }

    public sealed class JuniorParentWaitlistScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        string _sent, _email = "";
        bool _sending;

        public JuniorParentWaitlistScreen(App app, string sentTo)
        {
            _app = app;
            _sent = string.IsNullOrEmpty(sentTo) ? null : sentTo;
            Root = Page.Make(app, "Puttr Junior", out _body);
            Draw();
        }

        void Draw()
        {
            _body.Clear();
            VisualElement p = Ui.Box("pad center-box", Ui.Icon(_sent != null ? "CheckCircle" : "Bell", 48, _sent != null ? Ui.Green : Ui.Blue),
                Ui.Text(_sent != null ? "Your parent's in the loop" : "Let's loop in a parent", "display center wrap", Ui.Bold).Margin(10),
                Ui.Text(_sent != null ? "We emailed " + _sent + " everything about Puttr Junior. They are first in line when our family program opens, and we will let them know the moment it does." : "Puttr Junior works through a parent or guardian for younger players. Drop their email and we'll send them everything, including first access when our family program opens.", "sub center wrap", Ui.Regular).Margin(8));
            _body.Add(p);
            if (_sent != null)
            {
                _body.Add(Ui.Box("pad", Ui.Button("Done", _app.Pop), Ui.Text("Use a different email", "sec-link center", Ui.Medium).Margin(12).OnTap(() => { _sent = null; Draw(); })));
                return;
            }
            TextField email = Form.Field("Parent email", _email, 200);
            email.keyboardType = TouchScreenKeyboardType.EmailAddress;
            VisualElement go = Ui.Button(_sending ? "Sending..." : "Send to My Parent", () =>
            {
                if (!_email.Contains("@") || _sending) return;
                _sending = true; Draw();
                App.Run(Api.Post("/junior/parentWaitlist", new Dictionary<string, object> { { "parentEmail", _email.Trim() } }, _ => { _sending = false; _sent = _email.Trim(); Draw(); },
                    _ => { _sending = false; Draw(); Dialog.Ask(Root, "Could not send", "Check the email address and your connection, then try again.", "OK", () => { }); }));
            });
            if (!_email.Contains("@")) go.AddToClassList("btn-off");
            email.RegisterValueChangedCallback(e => { _email = e.newValue; go.EnableInClassList("btn-off", !_email.Contains("@")); });
            _body.Add(Ui.Box("pad", email, go));
        }
    }

    // ── A private tournament ─────────────────────────────────────────────────

    public sealed class CreateTournamentScreen : AppScreen
    {
        // puttr-app's Giphy key (a client key).
        const string GiphyKey = "y71kSsUmJvYyO9YjnAGxeDmRes1F5zrm";
        readonly App _app;
        readonly VisualElement _body;
        int _step = 1, _min = 3, _max = 11;
        string _title = "", _description = "", _hero;
        DateTime _start = DateTime.Today, _end = DateTime.Today;
        readonly List<object> _friends = new List<object>();
        readonly HashSet<string> _invites = new HashSet<string>();
        bool _sending;

        public CreateTournamentScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Create tournament", out _body);
            if (Kid.Blocked(app, Kid.Social, _body)) return;
            var seen = new HashSet<string>();
            int waiting = 2;
            void Add(object d) { foreach (object r in J.List(d)) if (seen.Add(J.Str(r, "userId"))) _friends.Add(r); if (--waiting == 0) { _friends.Sort((a, b) => string.Compare(J.Str(a, "displayName"), J.Str(b, "displayName"), StringComparison.OrdinalIgnoreCase)); if (_step == 2) Draw(); } }
            App.Run(Api.Get("/profile/followers/", Add, _ => Add(null)));
            App.Run(Api.Get("/profile/following/", Add, _ => Add(null)));
            Draw();
        }

        void Draw()
        {
            _body.Clear();
            // The two steps, side by side: a done one ticked.
            VisualElement steps = Ui.Box("row", AdminLabels.Pill(_step > 1 ? "\u2713  Details" : "1  Details", Ui.Bright), FreePuttScreen.Space(0).Width(10), AdminLabels.Pill("2  Invite players", _step == 2 ? Ui.Bright : Ui.Faint));
            steps.style.justifyContent = Justify.Center;
            _body.Add(Ui.Box("pad", steps));
            VisualElement p = Ui.Box("pad");
            _body.Add(p);
            if (_step == 1) Details(p); else Invites(p);
        }

        void Details(VisualElement p)
        {
            p.Add(Ui.Text("Tournament details", "h2 wrap", Ui.Bold));
            VisualElement cover = Ui.Box("dashed");
            if (_hero != null) Images.Load(_hero, t => { if (t != null) { cover.Clear(); cover.style.backgroundImage = new StyleBackground(t); cover.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop; } });
            else cover.Add(Ui.Text("Tap to add cover image", "muted", Ui.Bold));
            p.Add(cover.Margin(12).OnTap(Giphy));
            p.Add(Form.Label("Title"));
            TextField title = Form.Field("Tournament name", _title, 80);
            title.RegisterValueChangedCallback(e => _title = e.newValue);
            p.Add(title);
            p.Add(Form.Label("Description"));
            TextField words = Form.Field("What's this tournament about?", _description, 500, true);
            words.RegisterValueChangedCallback(e => _description = e.newValue);
            p.Add(words);
            p.Add(Form.Label("Schedule"));
            p.Add(Ui.Box("row", Ui.Box("grow", Ui.Text("Start", "muted", Ui.Regular), FreePuttScreen.Stepper(() => _start.ToString("MMM d, yyyy", CultureInfo.InvariantCulture), d => _start = _start.AddDays(d) < DateTime.Today ? DateTime.Today : _start.AddDays(d))),
                Ui.Box("grow", Ui.Text("End", "muted", Ui.Regular), FreePuttScreen.Stepper(() => _end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture), d => _end = _end.AddDays(d) < DateTime.Today ? DateTime.Today : _end.AddDays(d))).Margin(0)));
            p.Add(Form.Label("Distance range · " + _min + "ft – " + _max + "ft"));
            VisualElement dots = Ui.Box("pills");
            for (int ft = 3; ft <= 11; ft++)
            {
                int f = ft;
                dots.Add(Ui.Box(ft >= _min && ft <= _max ? "scope scope-on" : "scope", Ui.Text(ft.ToString(), "scope-text", Ui.Bold)).OnTap(() => { Range(f); Draw(); }));
            }
            p.Add(dots);
            p.Add(Ui.Button("Continue →", Continue).Margin(16));
        }

        // DistanceRange: outside extends, an end shrinks by one, inside moves the nearer end.
        void Range(int ft)
        {
            if (ft < _min) _min = ft;
            else if (ft > _max) _max = ft;
            else if (ft == _min && _max > _min) _min++;
            else if (ft == _max && _max > _min) _max--;
            else if (ft - _min <= _max - ft) _min = ft; else _max = ft;
        }

        void Continue()
        {
            string problem = _title.Trim().Length == 0 ? "You must enter a title" : _description.Trim().Length == 0 ? "You must enter a description" : _end < _start ? "End date must be the same or after the start date" : (_end - _start).TotalDays > 30 ? "Tournaments cannot be over 30 days" : _hero == null ? "You must select a cover image" : null;
            string head = _title.Trim().Length == 0 ? "Title" : _description.Trim().Length == 0 ? "Description" : _hero == null && _end >= _start && (_end - _start).TotalDays <= 30 ? "Image" : "Dates";
            if (problem != null) { Dialog.Ask(Root, head, problem, "OK", () => { }); return; }
            _step = 2;
            Draw();
        }

        void Invites(VisualElement p)
        {
            p.Add(Ui.Text("Invite players", "h2 wrap", Ui.Bold));
            p.Add(Ui.Text("Tap to select from your friends", "sub", Ui.Regular));
            p.Add(AdminLabels.Pill(_invites.Count + " player" + (_invites.Count == 1 ? "" : "s") + " selected", Ui.Bright).Margin(8));
            if (_friends.Count == 0) p.Add(Page.Loading());
            VisualElement card = Ui.Box("glass set-card");
            foreach (object f in _friends)
            {
                string id = J.Str(f, "userId");
                bool on = _invites.Contains(id);
                card.Add(Ui.Box("set-row", Ui.Avatar(J.Str(f, "displayName"), J.Str(f, "photoURL")), Ui.Text(J.Str(f, "displayName"), "feed-name grow", Ui.Bold), Ui.Text(on ? "✓" : "+", "h2 wrap", Ui.Bold))
                    .OnTap(() => { if (!_invites.Remove(id)) _invites.Add(id); Draw(); }));
            }
            p.Add(card.Margin(10));
            VisualElement create = Ui.Button(_sending ? "Creating..." : "Create tournament", Create);
            if (_invites.Count == 0) create.AddToClassList("btn-off");
            p.Add(Ui.Box("row", Ui.Button("Back", () => { _step = 1; Draw(); }, "btn btn-ghost grow"), create.Margin(0)).Margin(14));
            create.style.flexGrow = 1; create.style.marginLeft = 8;
        }

        void Create()
        {
            if (_sending) return;
            if (_invites.Count == 0) { Dialog.Ask(Root, "Invites", "You must invite at least one player", "OK", () => { }); return; }
            _sending = true; Draw();
            var body = new Dictionary<string, object>
            {
                { "title", _title.Trim() }, { "description", _description.Trim() }, { "startDate", _start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, { "endDate", _end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "heroImage", _hero }, { "randomRounds", 9 }, { "shortPutt", _min }, { "longPutt", _max }, { "invites", new List<object>(_invites) }, { "scope", "PRIVATE" },
            };
            // The API answers in text ("Tournament Added"), not JSON.
            App.Run(Create(body));
        }

        IEnumerator Create(Dictionary<string, object> body)
        {
            string token = null;
            yield return Auth.Token(t => token = t);
            long status = 0;
            yield return Api.Send("POST", Config.ApiUrl + "/tournaments/create", J.Write(body), token, (s, _) => status = s);
            _sending = false;
            if (status >= 200 && status < 300) { _app.Pop(); Dialog.Ask(_app.Toasts, "Success", "Tournament created successfully!", "OK", () => { }); }
            else { Draw(); _app.Toast("Could not create the tournament. Try again."); }
        }

        // GiphyDialog: search (trending when empty); a pick sets downsized_medium as the cover.
        void Giphy()
        {
            VisualElement dim = null;
            ScrollView results = Ui.Scroll();
            results.style.maxHeight = 420;
            VisualElement grid = Ui.Box("team-grid");
            results.Add(grid);
            TextField search = Form.Field("Search GIFs", "");
            int asked = 0;
            void Load(string q)
            {
                int me = ++asked;
                string url = q.Trim().Length == 0 ? "https://api.giphy.com/v1/gifs/trending?api_key=" + GiphyKey + "&limit=24&rating=g" : "https://api.giphy.com/v1/gifs/search?api_key=" + GiphyKey + "&limit=24&rating=g&q=" + UnityWebRequest.EscapeURL(q);
                App.Run(Fetch(url, d =>
                {
                    if (me != asked) return;
                    grid.Clear();
                    foreach (object g in J.List(J.Get(d, "data")))
                    {
                        string still = J.Str(J.Get(J.Get(g, "images"), "downsized_still"), "url"), pick = J.Str(J.Get(J.Get(g, "images"), "downsized_medium"), "url");
                        VisualElement tile = Ui.Box("thumb");
                        tile.style.width = Length.Percent(100); tile.style.height = 90;
                        Images.Load(still, t => { if (t != null) { tile.style.backgroundImage = new StyleBackground(t); tile.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop; } });
                        grid.Add(Ui.Box("team-slot", tile).OnTap(() => { dim.RemoveFromHierarchy(); _hero = pick; Draw(); }));
                    }
                }));
            }
            search.RegisterValueChangedCallback(e => search.schedule.Execute(() => Load(e.newValue)).StartingIn(300));
            VisualElement card = Ui.Box("modal", Ui.Text("Cover image", "h2 wrap", Ui.Bold), search.Margin(8), results, Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
            Load("golf");
        }

        static IEnumerator Fetch(string url, Action<object> done)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) done(J.Parse(req.downloadHandler.text));
            }
        }
    }
}
