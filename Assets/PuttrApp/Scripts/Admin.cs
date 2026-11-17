// Admin, for supporting Players (puttr-app components/admin). Shown only when profile.isAdmin is 1
// (the API checks it again: requireAdmin). Rebuilt from the app:
//
//   AdminScreen            search by email or name; Games today, 3D Sessions, Coaches (applications and
//                          certifications to grade), Juniors
//   AdminUserScreen        a Player: account, membership (activate a year or to a date), coach switch,
//                          devices, the full overview, 3D sessions, last 20 putts
//   AdminGameScreen        a game's putts
//   AdminApplicationScreen a coach application: approve (tier, verified credentials), waitlist, reject
//   AdminCertGradeScreen   a certification lesson: the video, the five-part rubric, pass or fail
//   AdminJuniorScreen      a junior application: details, status log, email delivery, approve, decline, resend
//
// /club/analytics/ search, games, user and userDevices are admin only in the API (requireAdmin, 2026-11-16)
// and are called signed in. A game's putts (/club/analytics/putts/) stays open: the web share page uses it.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    static class Adm
    {
        public static bool IsAdmin(object profile) => J.Int(profile, "isAdmin") == 1;

        public static string Ago(object row, string key) => PuttrApp.Ago.Words(ProfileMath.Date(row, key), DateTime.UtcNow);
        public static string Day(object row, string key) { DateTime? d = ProfileMath.Date(row, key); return d == null ? "–" : d.Value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture); }
        public static string When(object row, string key) { DateTime? d = ProfileMath.Date(row, key); return d == null ? "" : d.Value.ToLocalTime().ToString("HH:mm MMM d", CultureInfo.InvariantCulture); }
        public static string N(double v) => ProfileMath.Number(v);
        /// <summary>"a / b", leaving out whichever is empty.</summary>
        public static string Pair(string a, string b) => a.Length > 0 && b.Length > 0 ? a + " / " + b : a + b;

        public static Label Pill(string text, Color colour)
        {
            Label pill = Ui.Text(text, "chip", Ui.Bold);
            pill.style.color = colour;
            pill.style.backgroundColor = Color.Lerp(Ui.Ink, colour, 0.18f);
            return pill;
        }

        public static VisualElement Row(string label, string value, Color? colour = null)
        {
            Label v = Ui.Text(string.IsNullOrEmpty(value) ? "–" : value, "feed-name adm-value", Ui.Bold);
            if (colour != null) v.style.color = colour.Value;
            return Ui.Box("board-row", Ui.Text(label, "muted adm-key", Ui.Medium), v);
        }

        public static VisualElement Tiles(params (string value, string label, string sub)[] tiles)
        {
            VisualElement grid = Ui.Box("adm-tiles");
            foreach (var t in tiles)
            {
                VisualElement tile = Ui.Box("adm-tile", Ui.Text(t.value, "h2", Ui.Bold), Ui.Text(t.label.ToUpperInvariant(), "kick", Ui.Bold));
                if (!string.IsNullOrEmpty(t.sub)) tile.Add(Ui.Text(t.sub, "muted", Ui.Regular));
                grid.Add(tile);
            }
            return grid;
        }

        public static VisualElement Card(string title, string note, params VisualElement[] parts)
        {
            VisualElement card = Ui.Box("glass adm-card");
            foreach (VisualElement p in parts) if (p != null) card.Add(p);
            VisualElement box = Ui.Box("");
            box.Add(Ui.Section(title, note));
            box.Add(Ui.Box("pad", card));
            return box;
        }

        public static VisualElement UserRow(App app, object row)
        {
            string name = J.Str(row, "displayName", J.Str(row, "email", "Unknown"));
            object who = row;
            return Ui.Box("glass list-row", Ui.Avatar(name, J.Str(row, "photoURL")), Ui.Box("grow set-words", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text(J.Str(row, "homeTown"), "muted", Ui.Regular), Ui.Text(J.Str(row, "email"), "muted", Ui.Regular)))
                .OnTap(() => app.Push(new AdminUserScreen(app, J.Str(who, "userId"))));
        }

        public static Color Amber => new Color32(0xE9, 0xB9, 0x49, 0xFF);

        public static VisualElement Info(VisualElement over, string title, string text) { Dialog.Ask(over, title, text, "OK", () => { }); return null; }
    }

    // ── Home ─────────────────────────────────────────────────────────────────

    public sealed class AdminScreen : AppScreen
    {
        static readonly (string key, string name)[] Tabs = { ("games", "Games"), ("sessions", "Sessions"), ("applications", "Coaches"), ("juniors", "Juniors") };
        readonly App _app;
        readonly VisualElement _tabs, _list;
        readonly TextField _search;
        string _tab = "games";
        bool _juniorsAll;
        List<object> _users;

        public AdminScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Admin", out VisualElement body);
            if (!Adm.IsAdmin(app.Profile)) { body.Add(Page.Note("Admin is for Puttr staff.")); return; }
            _search = new TextField();
            _search.textEdition.placeholder = "Search by email or name...";
            _search.textEdition.hidePlaceholderOnFocus = false;
            _search.AddToClassList("field");
            _search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Search(); });
            _tabs = new VisualElement();
            _list = new VisualElement();
            body.Add(Ui.Box("pad", _search.Face(Ui.Medium), Ui.Box("row", Ui.Button("Search", Search, "btn grow"), FreePuttScreen.Space(0).Width(8),
                Ui.Box("btn-small", Ui.Text("Clear", "btn-text", Ui.Bold)).OnTap(() => { _users = null; _search.value = ""; Draw(); }), FreePuttScreen.Space(0).Width(8),
                Ui.Box("btn-small", Ui.Text("Refresh", "btn-text", Ui.Bold)).OnTap(Load))));
            body.Add(_tabs);
            body.Add(_list);
            Load();
        }

        void Search()
        {
            string text = _search.value.Trim();
            if (text.Length == 0) return;
            _list.Clear();
            _list.Add(Page.Loading());
            App.Run(Api.Get("/club/analytics/users/" + UnityEngine.Networking.UnityWebRequest.EscapeURL(text), data => { _users = J.List(data); Draw(); }, _ => { _users = new List<object>(); Draw(); }));
        }

        void Draw()
        {
            _tabs.Clear();
            _list.Clear();
            if (_users != null)
            {
                _list.Add(Ui.Section(_users.Count + " USERS"));
                VisualElement col = Ui.Box("pad");
                foreach (object u in _users) col.Add(Adm.UserRow(_app, u));
                _list.Add(col);
                return;
            }
            int index = Array.FindIndex(Tabs, t => t.key == _tab);
            _tabs.Add(Ui.Box("pad", FreePuttScreen.Choice(Array.ConvertAll(Tabs, t => t.name), index, i => { _tab = Tabs[i].key; Load(); })).Margin(8));
        }

        void Load()
        {
            _users = null;
            Draw();
            _list.Add(Page.Loading());
            string asked = _tab;
            switch (_tab)
            {
                case "games": App.Run(Api.Get("/club/analytics/games/", d => { if (asked == _tab) Games(J.List(d)); }, _ => Failed())); break;
                case "sessions": App.Run(Api.Get("/admin/lab/sessions", d => { if (asked == _tab) Sessions(J.List(d)); }, _ => Failed())); break;
                case "applications":
                    List<object> apps = null, certs = null;
                    void Then() { if (apps != null && certs != null && asked == _tab) Coaches(apps, certs); }
                    App.Run(Api.Get("/admin/coach/applications", d => { apps = J.List(J.Get(d, "applications")); Then(); }, _ => { apps = new List<object>(); Then(); }));
                    App.Run(Api.Get("/admin/coach/certifications", d => { certs = J.List(J.Get(d, "certifications")); Then(); }, _ => { certs = new List<object>(); Then(); }));
                    break;
                case "juniors": App.Run(Api.Get("/admin/junior/applications", d => { if (asked == _tab) Juniors(J.List(J.Get(d, "applications"))); }, _ => Failed())); break;
            }
        }

        void Failed() { _list.Clear(); _list.Add(Page.Note("Could not load this list. Check your connection and that this account is an admin.")); }

        void Games(List<object> rows)
        {
            _list.Clear();
            _list.Add(Ui.Section(rows.Count + " GAMES TODAY"));
            VisualElement col = Ui.Box("pad");
            foreach (object r in rows)
            {
                object row = r;
                VisualElement user = Ui.Box("btn-small", Ui.Text("User", "btn-text", Ui.Bold)).OnTap(() => _app.Push(new AdminUserScreen(_app, J.Str(row, "userId"))));
                col.Add(Ui.Box("glass list-row", Ui.Box("grow set-words", Ui.Text(J.Str(r, "title", "Game"), "feed-name", Ui.Bold), Ui.Text(Adm.When(r, "created_at"), "muted", Ui.Regular),
                    Ui.Text(J.Str(r, "displayName", J.Str(r, "email")) + "   ·   " + J.Int(r, "players") + "p   ·   status " + J.Int(r, "status") + "   ·   score " + RoundSession.Points(J.Num(r, "score")), "sub", Ui.Regular)), user)
                    .OnTap(() => _app.Push(new AdminGameScreen(_app, row))));
            }
            _list.Add(col);
        }

        public static string Capture(object session)
        {
            object check = J.Parse(J.Str(session, "captureCheck", "null"));
            if (check == null) return null;
            if (J.Str(check, "verdict") == "ok") return "Capture OK";
            var words = new List<string>();
            foreach (object reason in J.List(J.Get(check, "reasons")))
            {
                string r = reason as string;
                if (r == "poorVisibility") words.Add("Not visible");
                else if (r == "poorFraming") words.Add("Bad framing");
                else if (r == "wrongPerspective")
                {
                    object halves = J.Get(check, "halves");
                    string a = J.Str(halves, "first"), b = J.Str(halves, "second");
                    if (a == "downTheLine" && b == "downTheLine") words.Add("All down the line");
                    else if (a == "faceOn" && b == "faceOn") words.Add("All face on");
                    else if (a == "faceOn" && b == "downTheLine") words.Add("Views swapped");
                    else
                    {
                        if (a != "downTheLine") words.Add(a == "faceOn" ? "1st half face on" : "1st half unclear");
                        if (b != "faceOn") words.Add(b == "downTheLine" ? "2nd half down the line" : "2nd half unclear");
                    }
                }
            }
            return words.Count == 0 ? "Flagged" : string.Join(", ", words);
        }

        void Sessions(List<object> rows)
        {
            _list.Clear();
            _list.Add(Ui.Section(rows.Count + " SESSIONS"));
            VisualElement col = Ui.Box("pad");
            foreach (object r in rows)
            {
                object row = r;
                int status = J.Int(r, "status");
                VisualElement thumb = Ui.Box("adm-thumb");
                Images.Into(thumb, J.Str(r, "thumbUrl"));
                VisualElement words = Ui.Box("grow set-words", Ui.Text(J.Str(r, "displayName", J.Str(r, "email", "Unknown")), "feed-name", Ui.Bold),
                    Ui.Text(Adm.When(r, "created_at") + "   ·   #" + J.Str(r, "id") + (J.Get(r, "score") != null ? "   ·   score " + J.Num(r, "score").ToString("0.#", CultureInfo.InvariantCulture) : "") + "   ·   " + J.Str(r, "handedness"), "muted", Ui.Regular),
                    Ui.Box("chips", Adm.Pill(status >= 4 ? "Analyzed" : status == 3 ? "Annotated" : "Recorded", Ui.Faint)));
                string capture = Capture(r);
                if (capture != null) words[2].Add(Adm.Pill(capture, capture == "Capture OK" ? Ui.Bright : Ui.Red));
                VisualElement user = Ui.Box("btn-small", Ui.Text("User", "btn-text", Ui.Bold)).OnTap(() => _app.Push(new AdminUserScreen(_app, J.Str(row, "userId"))));
                col.Add(Ui.Box("glass list-row", thumb, words, user).OnTap(() => _app.Push(new LabSessionScreen(_app, row))));
            }
            _list.Add(col);
        }

        static string AppStatus(object a, out Color colour)
        {
            bool draft = J.Get(a, "submittedAt") == null && J.Int(a, "status") == 0;
            int s = J.Int(a, "status");
            object live = J.Get(a, "goLive");
            colour = Ui.Blue;
            if (draft) { colour = new Color(1, 1, 1, 0.4f); return "DRAFT"; }
            if (s == 2 && live != null) { colour = J.Flag(live, "live") ? Ui.Bright : Ui.Amber; return J.Flag(live, "live") ? "LIVE" : "ONBOARDING"; }
            switch (s) { case 1: colour = Ui.Amber; return "IN REVIEW"; case 2: colour = Ui.Bright; return "APPROVED"; case 3: colour = Ui.Amber; return "WAITLIST"; case 4: colour = Ui.Red; return "REJECTED"; default: return "NEW"; }
        }

        void Coaches(List<object> apps, List<object> certs)
        {
            _list.Clear();
            int open = apps.FindAll(a => J.Get(a, "submittedAt") != null && J.Int(a, "status") <= 1).Count;
            _list.Add(Ui.Section(open + " AWAITING REVIEW" + (certs.Count > 0 ? "   ·   " + certs.Count + " TO GRADE" : "")));
            VisualElement col = Ui.Box("pad");
            foreach (object c in certs)
            {
                object cert = c;
                string name = J.Str(c, "coachName", J.Str(c, "coachEmail"));
                col.Add(Ui.Box("glass list-row adm-cert", Ui.Avatar(name, J.Str(c, "coachPhoto")), Ui.Box("grow set-words", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text("Certification lesson delivered " + Adm.Ago(c, "deliveredAt"), "muted", Ui.Regular)),
                    Adm.Pill("GRADE", Ui.Blue)).OnTap(() => _app.Push(new AdminCertGradeScreen(_app, cert))));
            }
            foreach (object a in apps)
            {
                object app = a;
                string name = J.Str(a, "displayName", J.Str(a, "email"));
                int creds = J.List(J.Parse(J.Str(a, "credentials", "[]")) ?? J.Get(a, "credentials")).Count;
                bool draft = J.Get(a, "submittedAt") == null && J.Int(a, "status") == 0;
                string status = AppStatus(a, out Color colour);
                string where = J.Str(a, "location");
                col.Add(Ui.Box("glass list-row", Ui.Avatar(name, J.Str(a, "photoURL")), Ui.Box("grow set-words", Ui.Text(name, "feed-name", Ui.Bold),
                    Ui.Text((where.Length > 0 ? where + "   ·   " : "") + J.Int(a, "yearsCoaching") + " yrs   ·   " + creds + (creds == 1 ? " credential" : " credentials"), "muted", Ui.Regular),
                    Ui.Text(draft ? "Step " + Math.Max(1, J.Int(a, "step")) + " of 6   ·   started " + Adm.Ago(a, "created_at") : Adm.Ago(a, "submittedAt"), "muted", Ui.Regular)),
                    Adm.Pill(status, colour)).OnTap(() => _app.Push(new AdminApplicationScreen(_app, J.Str(app, "id")))));
            }
            if (certs.Count + apps.Count == 0) col.Add(Ui.Text("No coach applications.", "sub", Ui.Regular));
            _list.Add(col);
        }

        void Juniors(List<object> rows)
        {
            _list.Clear();
            int pending = rows.FindAll(r => J.Int(r, "status") == 0).Count;
            _list.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "Pending   ·   " + pending, "All   ·   " + rows.Count }, _juniorsAll ? 1 : 0, i => { _juniorsAll = i == 1; Juniors(rows); })).Margin(4));
            int[] order = { 0, 2, 3, 1 }; // status 0, 3, 1, 2
            List<object> shown = _juniorsAll ? new List<object>(rows) : rows.FindAll(r => J.Int(r, "status") == 0);
            shown.Sort((a, b) =>
            {
                int oa = Array.IndexOf(new[] { 0, 3, 1, 2 }, J.Int(a, "status")), ob = Array.IndexOf(new[] { 0, 3, 1, 2 }, J.Int(b, "status"));
                return oa != ob ? oa.CompareTo(ob) : string.CompareOrdinal(J.Str(a, "submittedAt"), J.Str(b, "submittedAt"));
            });
            VisualElement col = Ui.Box("pad");
            if (shown.Count == 0) col.Add(Ui.Text(_juniorsAll ? "No junior applications yet." : "No applications waiting for review.", "sub", Ui.Regular));
            foreach (object r in shown)
            {
                string name = J.Str(r, "displayName", J.Str(r, "email"));
                int age = AdminJuniorScreen.Age(r);
                var line = new List<string>();
                if (age >= 0) line.Add(age + "");
                if (J.Str(r, "hometown").Length > 0) line.Add(J.Str(r, "hometown"));
                line.Add(Adm.Ago(r, "submittedAt"));
                VisualElement sub = Ui.Box("chips");
                if (!J.Flag(r, "postingRequired")) sub.Add(Adm.Pill("No-post track", Ui.Red));
                if (J.Int(r, "status") == 3) sub.Add(Ui.Text("Parent: " + J.Str(r, "parentEmail", "no email"), "chip", Ui.Medium));
                else if (age >= 0 && age < 18) sub.Add(J.Get(r, "parentConsentAt") != null ? Adm.Pill("Consent ✓", Ui.Bright) : Adm.Pill("Consent sent, waiting", Ui.Blue));
                if (J.Get(r, "handicap") != null) sub.Add(Ui.Text("HCP " + J.Str(r, "handicap"), "chip", Ui.Medium));
                int s = J.Int(r, "status");
                string id = J.Str(r, "id");
                col.Add(Ui.Box("glass list-row", Ui.Avatar(name, J.Str(r, "photoURL")), Ui.Box("grow set-words", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text(string.Join("   ·   ", line), "muted", Ui.Regular), sub),
                    Adm.Pill(s == 1 ? "APPROVED" : s == 2 ? "REJECTED" : s == 3 ? "UNDER 13" : "PENDING", s == 1 ? Ui.Bright : s == 2 ? Ui.Red : s == 3 ? Ui.Blue : Ui.Amber))
                    .OnTap(() => _app.Push(new AdminJuniorScreen(_app, id))));
            }
            _list.Add(col);
        }
    }

    // ── A Player ─────────────────────────────────────────────────────────────

    public sealed class AdminUserScreen : AppScreen
    {
        readonly App _app;
        readonly string _userId;
        readonly VisualElement _who, _devices, _overview, _lab, _putts;
        object _user;

        public AdminUserScreen(App app, string userId)
        {
            _app = app;
            _userId = userId;
            Root = Page.Make(app, "User", out VisualElement body);
            _who = new VisualElement(); _devices = new VisualElement(); _overview = new VisualElement(); _lab = new VisualElement(); _putts = new VisualElement();
            foreach (VisualElement part in new[] { _who, _devices, _overview, _lab, _putts }) body.Add(part);
            _who.Add(Page.Loading());
            LoadUser();
            App.Run(Api.Get("/club/analytics/userDevices/" + userId, d => Devices(J.List(d)), _ => { }));
            App.Run(Api.Get("/admin/lab/sessions/" + userId, d => Lab(J.List(d)), _ => Lab(new List<object>())));
            LoadOverview();
        }

        void LoadUser() => App.Run(Api.Get("/club/analytics/user/" + _userId, d =>
        {
            _user = J.First(J.Get(d, "user"));
            Who();
            _putts.Clear();
            _putts.Add(AdminPutts.Build(J.List(J.Get(d, "userPutts"))));
        }, _ => { _who.Clear(); _who.Add(Page.Note("Could not load this user.")); }));

        void LoadOverview()
        {
            _overview.Clear();
            _overview.Add(Page.Note("Loading overview..."));
            App.Run(Api.Get("/admin/user/overview/" + _userId, d => { _overview.Clear(); AdminOverview.Build(_app, _overview, d); }, why =>
            {
                _overview.Clear();
                Label retry = Ui.Text("Overview failed (" + why + "). Tap to retry.", "sub", Ui.Bold);
                retry.style.color = Ui.Red;
                _overview.Add(Ui.Box("pad", retry).OnTap(LoadOverview));
            }));
        }

        void Who()
        {
            _who.Clear();
            if (_user == null) { _who.Add(Page.Note("No profile for this user.")); return; }
            object u = _user;
            string name = J.Str(u, "displayName", "Unknown");
            VisualElement card = Ui.Box("glass");
            card.Add(Ui.Box("row", Ui.Avatar(name, J.Str(u, "photoURL"), true), Ui.Box("grow set-words", Ui.Text(name, "h2", Ui.Bold), Ui.Text((J.Str(u, "firstName") + " " + J.Str(u, "lastName")).Trim(), "sub", Ui.Regular),
                Ui.Text(J.Str(u, "email") + "  (copy)", "sec-link", Ui.Medium).OnTap(() => { GUIUtility.systemCopyBuffer = J.Str(u, "email"); _app.Toast("Email copied."); }))).OnTap(() => ProfileScreen.Open(_app, _userId, u)));
            card.Add(Ui.Box("chips", J.Str(u, "persona").Length > 0 ? Ui.Text(J.Str(u, "persona"), "chip", Ui.Medium) : null, Ui.Text("Joined " + Adm.Day(u, "created_at"), "chip", Ui.Medium)));
            card.Add(Membership(u).Margin(10));
            card.Add(Rows.Row("GraduationCap", "Coach (video library)", null, Rows.Switch(J.Int(u, "isCoach") == 1, on =>
                App.Run(Api.Post("/admin/members/setCoach", new Dictionary<string, object> { { "userId", _userId }, { "isCoach", on ? 1 : 0 } }, _ => _app.Toast(on ? "Coach on." : "Coach off."), _ => { _app.Toast("Couldn't change it."); LoadUser(); })))));
            _who.Add(Ui.Box("pad", card));
        }

        VisualElement Membership(object u)
        {
            if (J.Flag(u, "isMember"))
            {
                DateTime? sub = ProfileMath.Date(u, "subscriptionExpiration"), trial = ProfileMath.Date(u, "trialExpiration");
                string label, detail = "";
                DateTime? until;
                if (sub != null && (trial == null || sub >= trial))
                {
                    string type = J.Str(u, "subscriptionType");
                    label = type == "membership_1year" ? "Annual" : type == "membership_1month" ? "Monthly" : type.Length > 0 ? type : "Subscription";
                    until = sub;
                    detail = J.Str(u, "subscriptionPlatform");
                }
                else if (trial != null) { label = "Trial"; until = trial; }
                else { label = "Active Member"; until = null; }
                bool expired = until != null && until.Value.Date < DateTime.UtcNow.Date;
                Label date = Ui.Text(until == null ? "" : (expired ? "expired " : "until ") + until.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture), "sub", Ui.Bold);
                if (expired) date.style.color = Ui.Red;
                return Ui.Box("glass row adm-member", Ui.Icon("FillTrophy", 18, Ui.Bright), Ui.Text("  " + label + "  ", "feed-name", Ui.Bold), date, Ui.Text(detail.Length > 0 ? "   ·   " + detail : "", "muted", Ui.Regular));
            }
            return Ui.Box("row", Ui.Button("Activate year", ActivateYear, "btn grow"), FreePuttScreen.Space(0).Width(8), Ui.Button("Activate to date", ActivateDate, "btn btn-ghost grow"));
        }

        void Activate(Dictionary<string, object> body, string done)
        {
            body["userId"] = _userId;
            App.Run(Api.Post("/admin/members/activate", body, _ => { _app.Toast(done); LoadUser(); }, why => _app.Toast("Couldn't activate (" + why + ").")));
        }

        // "What is this membership?": a website order or a comp. A year from today.
        void ActivateYear()
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", Ui.Text("Activate a year", "h2", Ui.Bold), Ui.Text("What is this membership?", "sub", Ui.Regular).Margin(6),
                Ui.Button("Website order", () => { dim.RemoveFromHierarchy(); Activate(new Dictionary<string, object> { { "source", "shopify" } }, "Activated for a year (website order)."); }),
                Ui.Button("Comp (free)", () => { dim.RemoveFromHierarchy(); Activate(new Dictionary<string, object> { { "source", "comp" } }, "Activated for a year (comp)."); }, "btn btn-ghost"),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        // Through a date (YYYY-MM-DD), confirmed before it is sent.
        void ActivateDate()
        {
            VisualElement dim = null;
            var field = new TextField { value = DateTime.Now.AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            field.AddToClassList("field");
            Label error = Ui.Text("", "sub", Ui.Medium);
            error.style.color = Ui.Red;
            VisualElement card = Ui.Box("modal", Ui.Text("Membership end date", "h2", Ui.Bold), Ui.Text("YYYY-MM-DD, today or later.", "muted", Ui.Regular), field.Face(Ui.Medium).Margin(8), error,
                Ui.Button("Activate", () =>
                {
                    if (!DateTime.TryParseExact(field.value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end) || end.Date < DateTime.Now.Date) { error.text = "Enter a date like 2027-11-16, today or later."; return; }
                    dim.RemoveFromHierarchy();
                    Dialog.Ask(Root, "Activate membership?", "Activate through " + end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) + "?", "Activate",
                        () => Activate(new Dictionary<string, object> { { "expiration", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } }, "Activated through " + end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) + "."));
                }),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(Root).Add(dim);
        }

        void Devices(List<object> rows)
        {
            _devices.Clear();
            _devices.Add(Ui.Section("Devices", rows.Count + (rows.Count == 1 ? " device" : " devices")));
            ScrollView strip = Ui.Scroll(true);
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                object info = J.Parse(J.Str(rows[i], "deviceInfo", "{}")) ?? J.Get(rows[i], "deviceInfo");
                VisualElement card = Ui.Box("glass adm-device", Ui.Text(J.Str(info, "model", "Unknown"), "feed-name", Ui.Bold), Ui.Text(J.Str(info, "brand") + "   ·   " + J.Str(info, "platVer"), "muted", Ui.Regular), Ui.Text("v" + J.Str(info, "version"), "muted", Ui.Regular));
                if (strip.contentContainer.childCount == 0) card.style.marginLeft = 16;
                strip.Add(card);
            }
            if (rows.Count == 0) strip.Add(Ui.Box("pad", Ui.Text("No devices recorded.", "muted", Ui.Regular)));
            _devices.Add(strip);
        }

        void Lab(List<object> rows)
        {
            _lab.Clear();
            _lab.Add(Ui.Section("3D sessions"));
            ScrollView strip = Ui.Scroll(true);
            foreach (object r in rows)
            {
                object row = r;
                VisualElement tile = Ui.Box("glass adm-labtile", Ui.Text(J.Get(r, "score") != null ? J.Num(r, "score").ToString("0", CultureInfo.InvariantCulture) : "–", "h2 center", Ui.Bold), Ui.Text(Adm.Day(r, "created_at"), "muted center", Ui.Regular)).OnTap(() => _app.Push(new LabSessionScreen(_app, row)));
                if (strip.contentContainer.childCount == 0) tile.style.marginLeft = 16;
                strip.Add(tile);
            }
            if (rows.Count == 0) strip.Add(Ui.Box("pad", Ui.Text("No lab sessions", "muted", Ui.Regular)));
            _lab.Add(strip);
        }
    }

    /// <summary>The Player overview (AdminUserOverview): everything /admin/user/overview/:userId says, read only.</summary>
    public static class AdminOverview
    {
        static string Pct(double made, double total) => total > 0 ? Mathf.RoundToInt((float)(made / total * 100)) + "%" : "–";
        static string R1(object row, string key) => J.Get(row, key) == null ? "–" : J.Num(row, key).ToString("0.0", CultureInfo.InvariantCulture);

        public static void Build(App app, VisualElement into, object d)
        {
            object profile = J.Get(d, "profile"), auth = J.Get(d, "auth"), activity = J.Get(d, "activity"), games = J.Get(d, "games");
            object putts = J.Get(activity, "putts"), stats = J.Get(activity, "stats"), streak = J.Get(activity, "streak");

            // Snapshot
            int days30 = J.Int(stats, "active_days_last_30"), prev = J.Int(stats, "active_days_prev_30");
            into.Add(Adm.Card("Snapshot", null, Adm.Tiles(
                (Adm.N(J.Num(putts, "total")), "Putts", J.Int(putts, "last30") + " last 30d   ·   " + J.Int(putts, "last7") + " last 7d"),
                (Adm.N(J.Num(J.Get(games, "totals"), "completed")), "Games done", J.Int(J.Get(games, "totals"), "abandoned") + " abandoned"),
                (Adm.N(J.Num(putts, "activeDays")), "Active days", days30 + " / 30d (" + (days30 - prev >= 0 ? "+" : "") + (days30 - prev) + ")"),
                (J.Int(streak, "current_streak", J.Int(stats, "current_streak")) + "d", "Streak", "best " + J.Int(streak, "longest_streak", J.Int(stats, "longest_streak")) + "d"),
                (Adm.Ago(putts, "lastPutt"), "Last putt", "first " + Adm.Day(putts, "firstPutt")),
                (Adm.Ago(auth, "lastSignInTime"), "Last sign-in", string.Join(", ", J.List(J.Get(auth, "providers")).ConvertAll(p => p as string ?? J.Str(p, "providerId")))))));

            // Account
            VisualElement flags = Ui.Box("chips");
            if (J.Int(profile, "isAdmin") == 1) flags.Add(Adm.Pill("Admin", Ui.Red));
            if (J.Int(profile, "isTester") == 1) flags.Add(Adm.Pill("Tester", Adm.Amber));
            if (J.Int(profile, "isCoach") == 1) flags.Add(Adm.Pill("Coach", Ui.Bright));
            if (J.Flag(profile, "isMember")) flags.Add(Adm.Pill("Member", Ui.Bright));
            if (J.Int(profile, "ownsPuttr") == 1) flags.Add(Adm.Pill("Owns Puttr", Ui.Bright));
            if (J.Flag(auth, "disabled")) flags.Add(Adm.Pill("AUTH DISABLED", Ui.Red));
            if (J.Get(auth, "emailVerified") != null && !J.Flag(auth, "emailVerified")) flags.Add(Adm.Pill("Email unverified", Adm.Amber));
            object social = J.Get(d, "social");
            var greens = J.List(J.Get(social, "greens")).ConvertAll(g => J.Str(g, "greenMac") + " (" + J.Int(g, "connects") + " connects, " + Adm.Ago(g, "lastConnect") + ")");
            List<object> tokens = J.List(J.Get(social, "pushTokens"));
            object prefs = J.Get(social, "notifPrefs");
            object follow = J.Get(social, "follow");
            List<object> putters = J.List(J.Get(social, "putters"));
            object putter = putters.Find(p => J.Int(p, "isPrimary") == 1) ?? (putters.Count > 0 ? putters[0] : null);
            into.Add(Adm.Card("Account", null, flags,
                Adm.Row("User ID", J.Str(profile, "userId")),
                Adm.Row("Last sign-in", J.Get(auth, "lastSignInTime") != null ? Adm.Ago(auth, "lastSignInTime") + "   ·   " + Adm.Day(auth, "lastSignInTime") : "–"),
                Adm.Row("Account created", Adm.Day(profile, "created_at")),
                Adm.Row("Source", J.Str(profile, "membershipSource", J.Str(profile, "source"))),
                Adm.Row("Persona / hand", Adm.Pair(J.Str(profile, "persona"), J.Str(profile, "handedness"))),
                Adm.Row("Hometown / timezone", Adm.Pair(J.Str(profile, "homeTown"), J.Str(profile, "timezone"))),
                Adm.Row("Handicap", J.Str(profile, "realHandicap", J.Str(profile, "handicap"))),
                Adm.Row("Greens paired", greens.Count > 0 ? string.Join("\n", greens) : "none", greens.Count > 0 ? (Color?)null : Adm.Amber),
                Adm.Row("Push tokens", tokens.Count > 0 ? tokens.Count + "   ·   last " + Adm.Ago(tokens[0], "updated_at") : "none", tokens.Count > 0 ? (Color?)null : Adm.Amber),
                Adm.Row("Notifications", prefs == null ? "defaults" : "practice " + (J.Flag(prefs, "practice") ? "on" : "off") + "   ·   competition " + (J.Flag(prefs, "competition") ? "on" : "off") + "   ·   offers " + (J.Flag(prefs, "offers") ? "on" : "off")),
                Adm.Row("Primary putter", putter == null ? "none" : (J.Str(putter, "brand") + " " + PuttersScreen.Clean(J.Str(putter, "model"))).Trim()),
                Adm.Row("Followers / following", J.Int(follow, "followers", J.Int(profile, "followers")) + " / " + J.Int(follow, "following", J.Int(profile, "following")))));

            // Activity: 12 months of putts as bars, and make % by distance.
            var months = new List<(string label, int value)>();
            var byMonth = new Dictionary<string, object>();
            foreach (object m in J.List(J.Get(activity, "byMonth"))) byMonth[J.Str(m, "month")] = m;
            DateTime first = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-11);
            for (int i = 0; i < 12; i++) { DateTime m = first.AddMonths(i); months.Add((m.ToString("MMM", CultureInfo.InvariantCulture), byMonth.TryGetValue(m.ToString("yyyy-MM"), out object row) ? J.Int(row, "putts") : 0)); }
            VisualElement bars = Ui.Box("adm-bars");
            int max = 1; foreach (var m in months) max = Math.Max(max, m.value);
            foreach (var m in months)
            {
                VisualElement bar = Ui.Box("adm-bar");
                bar.style.height = Mathf.Max(2, m.value * 60f / max);
                bar.style.backgroundColor = m.value > 0 ? Ui.Green : (Color)new Color32(50, 52, 54, 255);
                bars.Add(Ui.Box("adm-barcol", Ui.Text(m.value > 0 ? Adm.N(m.value) : "", "muted center", Ui.Regular), bar, Ui.Text(m.label, "muted center", Ui.Regular)));
            }
            VisualElement dist = Ui.Box("chips");
            foreach (object r in J.List(J.Get(activity, "byDistance"))) if (J.Int(r, "putts") >= 5) dist.Add(Ui.Text(Pct(J.Num(r, "made"), J.Num(r, "putts")) + "   " + J.Int(r, "distance") + "ft · " + J.Int(r, "putts"), "chip", Ui.Medium));
            object coach = J.Get(activity, "coach");
            into.Add(Adm.Card("Activity", null, Ui.Text("PUTTS PER MONTH   ·   LAST 12", "kick", Ui.Bold), bars,
                Adm.Row("Average rollout", R1(putts, "avgRollout") + " in"),
                Adm.Row("Caddie", coach == null ? "never onboarded" : J.Str(coach, "personaId") + "   ·   goal " + J.Int(coach, "dailyGoal") + "/day   ·   streak " + J.Int(coach, "currentStreak") + " (best " + J.Int(coach, "longestStreak") + ")"),
                Ui.Text("MAKE % BY DISTANCE (5+ PUTTS)", "kick", Ui.Bold).Margin(10), dist));

            // Games and replays
            object totals = J.Get(games, "totals"), video = J.Get(games, "video");
            double completed = J.Num(totals, "completed"), withVideo = J.Num(video, "gamesWithVideo");
            VisualElement recent = Ui.Box("");
            List<object> recentGames = J.List(J.Get(games, "recent"));
            for (int i = 0; i < Math.Min(10, recentGames.Count); i++)
            {
                object g = recentGames[i];
                int s = J.Int(g, "status");
                string title = J.Str(g, "title", "Game") + (J.Int(g, "tournamentId") > 0 ? " · tournament" : "") + (J.Int(g, "players") > 1 ? " · " + J.Int(g, "players") + "p" : "");
                Label score = Ui.Text(RoundSession.Points(J.Num(g, "score")), "feed-score", Ui.Bold);
                score.style.color = s == 0 ? Adm.Amber : s == 1 ? Ui.Red : Ui.Bright;
                Label rec = Ui.Text(J.Int(g, "videoSegments") > 0 ? "replay ✓" : J.Int(g, "videoRows") > 0 ? "NO VIDEO FILE" : "no recording", "muted", Ui.Medium);
                if (J.Int(g, "videoSegments") > 0) rec.style.color = Ui.Bright; else if (J.Int(g, "videoRows") > 0) rec.style.color = Ui.Red;
                string id = J.Str(g, "id");
                recent.Add(Ui.Box("board-row", Ui.Box("grow", Ui.Text(title, "feed-name", Ui.Bold), Ui.Text(Adm.Day(g, "created_at") + "   ·   " + (s == 0 ? "Open" : s == 1 ? "Abandoned" : "Done") + "   ·   putt " + J.Int(g, "puttCounter"), "muted", Ui.Regular), rec), score)
                    .OnTap(() => app.Push(new GameViewScreen(app, id))));
            }
            VisualElement top = Ui.Box("");
            foreach (object t in J.List(J.Get(games, "top")))
                top.Add(Ui.Box("board-row", Ui.Box("grow", Ui.Text(J.Str(t, "title"), "feed-name", Ui.Bold), Ui.Text(J.Str(t, "gameType") + "   ·   avg " + R1(t, "avg") + "   ·   best " + R1(t, "best") + "   ·   last " + Adm.Ago(t, "last"), "muted", Ui.Regular)), Ui.Text(J.Int(t, "plays") + "×", "feed-score", Ui.Bold)));
            into.Add(Adm.Card("Games & replays", J.Get(video, "lastVideo") != null ? "last video " + Adm.Ago(video, "lastVideo") : null,
                Adm.Tiles((Adm.N(completed), "Completed", null), (Adm.N(withVideo), "Replays", Pct(withVideo, completed) + " of completed"), (Adm.N(J.Num(video, "emptyRecordings")), "Empty recordings", "recorder made no file"),
                    (Adm.N(J.Num(totals, "tournament")), "Tournament", null), (Adm.N(J.Num(totals, "lab")), "Lab", null), (Adm.N(J.Num(totals, "multi")), "Local multi", null)),
                Ui.Text("MOST PLAYED", "kick", Ui.Bold).Margin(10), top, Ui.Text("RECENT   ·   TAP TO OPEN", "kick", Ui.Bold).Margin(10), recent));

            // Quests
            object quests = J.Get(d, "quests");
            List<object> questList = J.List(J.Get(quests, "list"));
            if (questList.Count > 0)
            {
                VisualElement q = Ui.Box("");
                foreach (object r in questList)
                    q.Add(Ui.Box("board-row", Ui.Box("grow", Ui.Text(J.Str(r, "title"), "feed-name", Ui.Bold), Ui.Text(J.Str(r, "questType") + "   ·   difficulty " + J.Int(r, "difficulty") + "   ·   " + J.Int(r, "played") + "/" + J.Int(r, "levels") + " levels   ·   " + Adm.Ago(r, "lastPlayed"), "muted", Ui.Regular)), Ui.Text(J.Int(r, "stars") + " stars", "feed-score", Ui.Bold)));
                into.Add(Adm.Card("Quests", null, q));
            }

            // League
            object league = J.Get(d, "league");
            List<object> seasons = J.List(J.Get(league, "seasons"));
            if (seasons.Count > 0)
            {
                VisualElement l = Ui.Box("");
                foreach (object s in seasons)
                {
                    League.Team team = League.TeamOf(J.Str(s, "team"));
                    object bySeason = J.Get(J.Get(league, "bySeason"), J.Str(s, "seasonId"));
                    l.Add(Ui.Box("board-row", Ui.Box("grow", Ui.Text("Season " + J.Int(s, "seasonNumber") + "   ·   " + (team != null ? team.Name : J.Str(s, "team")), "feed-name", Ui.Bold),
                        Ui.Text("weeks " + J.Int(bySeason, "weeks") + "   ·   best " + R1(bySeason, "best") + "   ·   joined " + Adm.Day(s, "joinedAt"), "muted", Ui.Regular))));
                }
                into.Add(Adm.Card("League", null, l));
            }

            // Tournaments
            List<object> tourneys = J.List(J.Get(d, "tournaments"));
            if (tourneys.Count > 0)
            {
                VisualElement t = Ui.Box("");
                foreach (object r in tourneys)
                    t.Add(Ui.Box("board-row", Ui.Box("grow", Ui.Text(J.Str(r, "title"), "feed-name", Ui.Bold), Ui.Text(Adm.Day(r, "startDate") + " to " + Adm.Day(r, "endDate") + "   ·   " + J.Int(r, "roundsPlayed") + " rounds   ·   " + J.Int(r, "participants") + " players", "muted", Ui.Regular)),
                        Ui.Text(J.Get(r, "total") != null ? R1(r, "total") : "–", "feed-score", Ui.Bold)));
                into.Add(Adm.Card(tourneys.Count + " tournaments joined", null, t));
            }

            // 3D lab
            object lab = J.Get(d, "lab");
            if (J.Num(lab, "sessions") > 0)
                into.Add(Adm.Card("3D lab", null, Adm.Tiles((Adm.N(J.Num(lab, "sessions")), "Sessions", J.Int(lab, "ready") + " ready"), (Adm.N(J.Num(lab, "withVideo")), "With video", null), (Adm.N(J.Num(lab, "withPose")), "With pose", null),
                    (Adm.N(J.Num(lab, "dualCamera")), "Dual camera", null), (Adm.N(J.Num(lab, "captureChecked")), "Capture checked", null), (R1(lab, "avgScore"), "Avg score", null))));

            // Versus
            object versus = J.Get(d, "versus");
            object vt = J.Get(versus, "totals");
            if (J.Num(vt, "sent") + J.Num(vt, "received") > 0 || J.Get(versus, "multiplayer") != null)
                into.Add(Adm.Card("Versus & online", null, Adm.Tiles((J.Int(vt, "sent") + " / " + J.Int(vt, "received"), "Friend versus", "sent / received"), (J.Int(vt, "won") + "–" + J.Int(vt, "lost"), "Record", J.Int(vt, "open") + " open"),
                    (Adm.N(J.Num(J.Get(versus, "multiplayer"), "total")), "Online 1v1", null))));

            // Coaching
            object coaching = J.Get(d, "coaching");
            object coachProfile = J.Get(coaching, "profile"), application = J.Get(coaching, "application"), junior = J.Get(coaching, "junior");
            if (coachProfile != null || application != null || junior != null || J.List(J.Get(coaching, "lessons")).Count > 0)
            {
                string[] appStatus = { "Submitted", "In review", "Approved", "Waitlisted", "Rejected" }, junStatus = { "Submitted", "Approved", "Rejected", "Parent waitlist" };
                into.Add(Adm.Card("Coaching & applications", null,
                    coachProfile != null ? Adm.Row("Coach tier", J.Str(coachProfile, "tier") + (J.Get(coachProfile, "certifiedAt") != null ? "   ·   certified" : "")) : null,
                    coachProfile != null ? Adm.Row("Accepting lessons", J.Flag(coachProfile, "acceptingLessons") ? "yes" : "no") : null,
                    application != null ? Adm.Row("Coach application", appStatus[Mathf.Clamp(J.Int(application, "status"), 0, 4)] + "   ·   " + Adm.Day(application, "submittedAt")) : null,
                    junior != null ? Adm.Row("Junior application", junStatus[Mathf.Clamp(J.Int(junior, "status"), 0, 3)] + "   ·   " + Adm.Day(junior, "submittedAt")) : null,
                    Adm.Row("Lessons as player", J.List(J.Get(coaching, "lessons")).Count.ToString())));
            }

            // Billing
            object billing = J.Get(d, "billing");
            List<object> activations = J.List(J.Get(billing, "activations")), iap = J.List(J.Get(billing, "iapEvents"));
            DateTime? expires = ProfileMath.Date(profile, "subscriptionExpiration");
            VisualElement act = Ui.Box("");
            foreach (object a in activations) act.Add(Adm.Row("Order " + J.Str(a, "orderNumber"), J.Str(a, "result")));
            foreach (object e in iap) act.Add(Adm.Row(J.Str(e, "type", "IAP"), Adm.Day(e, "created_at")));
            into.Add(Adm.Card("Billing", null,
                Adm.Row("Subscription", J.Str(profile, "subscriptionType", "none")),
                Adm.Row("Expires", expires == null ? "–" : Adm.Day(profile, "subscriptionExpiration"), expires != null && expires < DateTime.UtcNow ? Ui.Red : (Color?)null),
                Adm.Row("Trial expiration", Adm.Day(profile, "trialExpiration")),
                Adm.Row("Membership source", J.Str(profile, "membershipSource")),
                Adm.Row("Stripe customer", J.Str(profile, "stripeCustomerId")),
                act));

            // Achievements and challenges
            List<object> earned = J.List(J.Get(d, "achievements")), challenges = J.List(J.Get(d, "challenges"));
            if (earned.Count + challenges.Count > 0)
            {
                VisualElement pills = Ui.Box("chips");
                foreach (object a in earned) { DateTime? at = ProfileMath.Date(a, "created_at"); pills.Add(Adm.Pill(J.Str(a, "title") + (at != null ? " · " + at.Value.ToString("MMM yy", CultureInfo.InvariantCulture) : ""), Ui.Bright)); }
                VisualElement ch = Ui.Box("");
                foreach (object c in challenges) ch.Add(Adm.Row(J.Str(c, "title"), Adm.N(J.Num(c, "currentScore"))));
                into.Add(Adm.Card("Achievements & challenges", null, pills, ch));
            }
        }
    }

    /// <summary>The putt grid (Putts.js): each putt's path on the mat, made or missed, distance, tee, time.</summary>
    public static class AdminPutts
    {
        public static VisualElement Build(List<object> putts)
        {
            VisualElement box = Ui.Box("");
            box.Add(Ui.Section("Putts", putts.Count.ToString()));
            if (putts.Count == 0) { box.Add(Page.Note("No putts recorded")); return box; }
            VisualElement grid = Ui.Box("cols pad");
            foreach (object p in putts)
            {
                bool made = J.Int(p, "made") == 1;
                int tee = J.Int(p, "tee", 2);
                Label result = Ui.Text(made ? "Made" : "Missed", "chip", Ui.Bold);
                result.style.color = made ? Ui.Bright : Ui.Red;
                grid.Add(Ui.Box("col", Ui.Box("glass adm-putt", new PathView(J.Get(p, "ballPath"), J.Get(p, "holeBounds"), made),
                    Ui.Box("row", Ui.Text(J.Int(p, "distance") + "ft   ·   " + (tee == 1 ? "Left" : tee == 3 ? "Right" : "Straight"), "feed-name grow", Ui.Bold), result),
                    Ui.Text(ProfileMath.Date(p, "created_at")?.ToLocalTime().ToString("h:mm tt   ·   MMM d, yyyy", CultureInfo.InvariantCulture) ?? "", "muted", Ui.Regular))));
            }
            box.Add(grid);
            return box;
        }

        /// <summary>A ball path on the 475 x 350 camera frame, with the hole glowing green or red.</summary>
        sealed class PathView : VisualElement
        {
            readonly List<Vector3> _path = new List<Vector3>();
            readonly Vector3 _hole;
            readonly bool _made;

            public PathView(object path, object hole, bool made)
            {
                _made = made;
                if (path is string ps) path = J.Parse(ps);
                if (hole is string hs) hole = J.Parse(hs);
                foreach (object pt in J.List(path)) { List<object> a = J.List(pt); if (a.Count >= 2) _path.Add(new Vector3((float)Puttr.PuttrJson.Number(a[0], 0), (float)Puttr.PuttrJson.Number(a[1], 0), a.Count > 2 ? (float)Puttr.PuttrJson.Number(a[2], 8) : 8)); }
                List<object> h = J.List(hole);
                _hole = h.Count >= 3 ? new Vector3((float)Puttr.PuttrJson.Number(h[0], 0), (float)Puttr.PuttrJson.Number(h[1], 0), (float)Puttr.PuttrJson.Number(h[2], 30)) : new Vector3(320, 172, 30);
                AddToClassList("adm-path");
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                Rect r = contentRect;
                if (r.width <= 0) return;
                float k = Mathf.Min(r.width / 475f, r.height / 350f);
                Vector2 P(Vector3 v) => new Vector2(v.x * k, v.y * k);
                Painter2D p = ctx.painter2D;
                p.fillColor = new Color32(0x14, 0x30, 0x1A, 0xFF);
                p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(475 * k, 0)); p.LineTo(new Vector2(475 * k, 350 * k)); p.LineTo(new Vector2(0, 350 * k)); p.ClosePath(); p.Fill();
                p.fillColor = _made ? new Color(0.48f, 0.83f, 0.27f, 0.35f) : new Color(1f, 0.32f, 0.32f, 0.3f);
                p.BeginPath(); p.Arc(P(_hole), _hole.z * k * 0.9f, 0, 360); p.Fill();
                p.fillColor = new Color32(0x05, 0x07, 0x08, 0xFF);
                p.BeginPath(); p.Arc(P(_hole), _hole.z * k * 0.5f, 0, 360); p.Fill();
                if (_path.Count < 2) return;
                p.strokeColor = Color.white;
                p.lineWidth = 2;
                p.BeginPath(); p.MoveTo(P(_path[0])); for (int i = 1; i < _path.Count; i++) p.LineTo(P(_path[i])); p.Stroke();
                p.fillColor = _made ? (Color)Ui.Bright : Ui.Red;
                p.BeginPath(); p.Arc(P(_path[_path.Count - 1]), 4, 0, 360); p.Fill();
            }
        }
    }

    // ── A game ───────────────────────────────────────────────────────────────

    public sealed class AdminGameScreen : AppScreen
    {
        public AdminGameScreen(App app, object game)
        {
            Root = Page.Make(app, "Game", out VisualElement body);
            VisualElement putts = new VisualElement();
            void Load() { putts.Clear(); putts.Add(Page.Loading()); App.Run(Api.Open("/club/analytics/putts/" + J.Str(game, "id"), d => { putts.Clear(); putts.Add(AdminPutts.Build(J.List(d))); }, _ => { putts.Clear(); putts.Add(Page.Note("Could not load the putts.")); })); }
            body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Box("row", Ui.Box("grow", Ui.Text(J.Str(game, "title", "Game"), "h2", Ui.Bold), Ui.Text(J.Str(game, "displayName"), "sub", Ui.Regular)),
                    Ui.Box("btn-small", Ui.Text("Refresh", "btn-text", Ui.Bold)).OnTap(Load)),
                Ui.Box("chips", Ui.Text(J.Int(game, "players") + " players", "chip", Ui.Medium), Ui.Text("status " + J.Int(game, "status"), "chip", Ui.Medium),
                    Ui.Text(ProfileMath.Date(game, "created_at")?.ToLocalTime().ToString("h:mm tt   ·   MMM d, yyyy", CultureInfo.InvariantCulture) ?? "", "chip", Ui.Medium)),
                Ui.Box("btn-small", Ui.Text("User", "btn-text", Ui.Bold)).Margin(8).OnTap(() => app.Push(new AdminUserScreen(app, J.Str(game, "userId")))))));
            body.Add(putts);
            Load();
        }
    }

    // ── A coach application ──────────────────────────────────────────────────

    public sealed class AdminApplicationScreen : AppScreen
    {
        static readonly Dictionary<string, string> CredentialNames = new Dictionary<string, string> { { "PGA_AM", "PGA of America" }, { "LPGA", "LPGA" }, { "PGA_INTL", "PGA (International)" }, { "TPI", "TPI Certified" }, { "AIMPOINT", "AimPoint" }, { "OTHER", "Other" } };
        static readonly (string key, string words)[] Reasons = { ("EXPERIENCE", "Not enough coaching experience"), ("RESPONSE_QUALITY", "Weak free response"), ("VIDEO_QUALITY", "Intro video quality"), ("DEMAND", "No demand in their market"), ("OTHER", "Other") };

        readonly App _app;
        readonly string _id;
        readonly VisualElement _body;
        TextField _notes;
        object _a;

        public AdminApplicationScreen(App app, string id)
        {
            _app = app;
            _id = id;
            Root = Page.Make(app, "Coach application", out _body);
            _body.Add(Page.Loading());
            Load();
        }

        void Load() => App.Run(Api.Get("/admin/coach/application/" + _id, d => { _a = J.Get(d, "application") ?? d; Draw(); }, _ => { _body.Clear(); _body.Add(Page.Note("Could not load the application.")); }));

        List<object> Credentials() { object c = J.Get(_a, "credentials"); if (c is string s) c = J.Parse(s); return J.List(c); }

        void Draw()
        {
            _body.Clear();
            object a = _a;
            int status = J.Int(a, "status");
            bool draft = J.Get(a, "submittedAt") == null && status == 0, decided = status == 2 || status == 4;
            string name = J.Str(a, "displayName", "Unknown");
            _body.Add(Ui.Box("pad", Ui.Box("glass row", Ui.Avatar(name, J.Str(a, "photoURL"), true), Ui.Box("grow set-words", Ui.Text(name, "h2", Ui.Bold), Ui.Text(J.Str(a, "email"), "sub", Ui.Regular),
                Ui.Text(J.Str(a, "location", "No location") + (J.Str(a, "country").Length > 0 ? "   ·   " + J.Str(a, "country") : "") + (draft ? "   ·   started " + Adm.Day(a, "created_at") : "   ·   applied " + Adm.Day(a, "submittedAt")), "muted", Ui.Regular)))));
            if (draft) _body.Add(Page.Note("DRAFT: not submitted yet. Step " + Math.Max(1, J.Int(a, "step")) + " of 6."));
            object ps = J.Get(a, "playerStats");
            _body.Add(Adm.Card("Their Puttr usage", null, Adm.Tiles((Adm.N(J.Num(ps, "putts")), "Putts", null), (Adm.N(J.Num(ps, "games")), "Games", null), (Adm.N(J.Num(ps, "labSessions")), "Lab sessions", null), (J.Get(ps, "lastPlayed") != null ? Adm.Ago(ps, "lastPlayed") : "never", "Last played", null))));
            string ctx = J.Str(a, "teachingContext");
            _body.Add(Adm.Card("Coaching background", null,
                Ui.Text(J.Int(a, "yearsCoaching") + " years coaching   ·   " + J.Int(a, "playerCount") + " players today   ·   teaches " + (ctx == "BOTH" ? "at a facility and online" : ctx == "ONLINE" ? "online" : "at a facility"), "sub", Ui.Regular),
                Ui.Text("Intended price $" + J.Str(a, "intendedPrice") + "   ·   turnaround " + J.Str(a, "intendedTurnaround") + "h   ·   " + J.Str(a, "weeklyCapacity") + " lessons/week", "sub", Ui.Regular).Margin(6)));
            VisualElement creds = Ui.Box("");
            foreach (object c in Credentials())
            {
                string type = J.Str(c, "type"), proof = J.Str(c, "proofUrl");
                VisualElement thumb = Ui.Box("adm-proof");
                if (proof.StartsWith("http")) { Images.Into(thumb, proof); thumb.OnTap(() => Application.OpenURL(proof)); }
                creds.Add(Ui.Box("board-row", Ui.Icon("Medal", 18, J.Flag(c, "verified") ? Ui.Bright : Ui.Faint), Ui.Text("  " + (CredentialNames.TryGetValue(type, out string n) ? n : type), "feed-name grow", Ui.Bold), proof.StartsWith("http") ? thumb : Ui.Text("no proof", "muted", Ui.Regular)));
            }
            if (creds.childCount == 0) creds.Add(Ui.Text("None submitted. Community tier only.", "sub", Ui.Regular));
            _body.Add(Adm.Card("Credentials", null, creds));
            _body.Add(Adm.Card("Putting philosophy", null, Ui.Text(J.Str(a, "freeResponse", "None"), "sub", Ui.Regular)));
            string intro = J.Str(a, "introVideoUrl");
            if (intro.StartsWith("http")) _body.Add(Adm.Card("Intro video", null, Ui.Button("Play intro video", () => SetupGuide.Play(Root, intro), "btn btn-ghost")));
            if (status == 2)
            {
                object live = J.Get(a, "goLive");
                if (live != null)
                {
                    object progress = J.Get(live, "academyProgress");
                    _body.Add(Adm.Card("Go-live checklist", J.Flag(live, "live") ? "Live on Find a Coach" : null,
                        Adm.Row("Academy", J.Int(progress, "passed") + " of " + J.Int(progress, "total") + " online modules passed", J.Flag(live, "academyComplete") ? Ui.Bright : (Color?)null),
                        Adm.Row("Certification", J.Flag(live, "certPassed") ? "Certification lesson passed" : J.Flag(live, "certPending") ? "Lesson delivered, waiting on your grade" : J.Flag(live, "academyComplete") ? "Unlocked, not passed yet" : "Unlocks after the Academy", J.Flag(live, "certPassed") ? Ui.Bright : (Color?)null),
                        Adm.Row("Coaching profile", J.Flag(live, "profileComplete") ? "Headline, bio, price and intro video set" : "Not complete", J.Flag(live, "profileComplete") ? Ui.Bright : (Color?)null),
                        Adm.Row("Stripe payouts", J.Flag(live, "payoutsEnabled") ? "Payouts enabled" : "Not set up", J.Flag(live, "payoutsEnabled") ? Ui.Bright : (Color?)null)));
                }
                object cert = J.Get(a, "certification");
                object rubric = J.Get(cert, "certRubric");
                if (rubric is string rs) rubric = J.Parse(rs);
                string title, words;
                VisualElement grade = null;
                if (J.Get(a, "certifiedAt") != null) { title = "Certified"; words = "Passed " + Adm.Day(a, "certifiedAt") + (rubric != null ? " with " + J.Int(rubric, "total") + " of 25." : "."); }
                else if (cert != null && J.Int(cert, "status") == 4 && rubric == null) { title = "Certification lesson delivered"; words = "Delivered " + Adm.Ago(cert, "deliveredAt") + ". Grade it to certify this coach."; object c = cert; grade = Ui.Button("Grade certification lesson", () => _app.Push(new AdminCertGradeScreen(_app, c))); }
                else if (cert != null && (J.Int(cert, "status") == 2 || J.Int(cert, "status") == 3)) { title = "Certification lesson in progress"; words = "Started " + Adm.Ago(cert, "submittedAt") + ". Waiting on the coach to deliver."; }
                else if (rubric != null && !J.Flag(rubric, "pass")) { title = "Last attempt did not pass"; words = "Scored " + J.Int(rubric, "total") + " of 25. A fresh attempt is seeded when they retry."; }
                else { title = "Certification not started"; words = "Unlocks for the coach once the online Academy modules are done."; }
                _body.Add(Adm.Card("Certification", null, Ui.Text(title, "feed-name", Ui.Bold), Ui.Text(words, "sub", Ui.Regular), grade));
            }
            if (!decided)
            {
                _notes = new TextField { multiline = true };
                _notes.textEdition.placeholder = "Optional notes for the record...";
                _notes.textEdition.hidePlaceholderOnFocus = false;
                _notes.AddToClassList("field");
                _body.Add(Adm.Card("Review notes (internal)", null, _notes.Face(Ui.Medium)));
                VisualElement reject = Ui.Button("Reject", Reject, "btn btn-ghost grow"), wait = Ui.Button("Waitlist", Waitlist, "btn btn-ghost grow"), approve = Ui.Button("Approve", Approve, "btn grow");
                reject.style.borderTopColor = reject.style.borderBottomColor = reject.style.borderLeftColor = reject.style.borderRightColor = Ui.Red;
                _body.Add(Ui.Box("pad", Ui.Box("row", reject, FreePuttScreen.Space(0).Width(8), wait, FreePuttScreen.Space(0).Width(8), approve)).Margin(10));
            }
            else _body.Add(Adm.Card("Decision", null, Ui.Text((status == 2 ? "Approved " : "Rejected ") + Adm.Day(a, "decisionAt"), "feed-name", Ui.Bold), Ui.Text(J.Str(a, "reviewNotes"), "sub", Ui.Regular)));
        }

        void Decide(Dictionary<string, object> body)
        {
            body["reviewNotes"] = _notes != null ? _notes.value : "";
            App.Run(Api.Post("/admin/coach/application/" + _id + "/decide", body, _ => { _app.Pop(); _app.Toast("Decision saved and the applicant has been notified."); },
                why => _app.Toast(why == "http_400" ? "This application has already been decided." : "Could not save the decision.")));
        }

        void Waitlist() => Dialog.Ask(Root, "Waitlist this applicant?", "They get a friendly email saying we are matching coaches to demand.", "Waitlist", () => Decide(new Dictionary<string, object> { { "action", "waitlist" } }));

        // Approve: which credentials were checked, and the tier. Certified needs at least one verified.
        void Approve()
        {
            var verified = new HashSet<string>();
            string tier = "COMMUNITY";
            VisualElement dim = null, checks = Ui.Box(""), tiers = Ui.Box("");
            void DrawTiers()
            {
                tiers.Clear();
                VisualElement cert = Ui.Box(tier == "CERTIFIED" ? "scope scope-on" : "scope", Ui.Text("Certified Coach", "scope-text", Ui.Bold));
                if (verified.Count == 0) cert.style.opacity = 0.35f; else cert.OnTap(() => { tier = "CERTIFIED"; DrawTiers(); });
                tiers.Add(Ui.Box("row", cert, Ui.Box(tier == "COMMUNITY" ? "scope scope-on" : "scope", Ui.Text("Puttr Coach", "scope-text", Ui.Bold)).OnTap(() => { tier = "COMMUNITY"; DrawTiers(); })));
            }
            foreach (object c in Credentials())
            {
                string type = J.Str(c, "type");
                checks.Add(Rows.Row("Medal", CredentialNames.TryGetValue(type, out string n) ? n : type, null, Rows.Switch(false, on => { if (on) verified.Add(type); else verified.Remove(type); if (verified.Count == 0) tier = "COMMUNITY"; DrawTiers(); })));
            }
            if (checks.childCount == 0) checks.Add(Ui.Text("No credentials submitted.", "muted", Ui.Regular));
            DrawTiers();
            VisualElement card = Ui.Box("modal", Ui.Text("Approve applicant", "h2", Ui.Bold), Ui.Text("VERIFIED CREDENTIALS", "kick", Ui.Bold).Margin(8), checks, Ui.Text("TIER", "kick", Ui.Bold).Margin(8), tiers,
                Ui.Button("Approve", () => { dim.RemoveFromHierarchy(); Decide(new Dictionary<string, object> { { "action", "approve" }, { "tier", verified.Count > 0 ? tier : "COMMUNITY" }, { "verifiedTypes", new List<object>(verified) } }); }).Margin(10),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(Root).Add(dim);
        }

        void Reject()
        {
            string reason = null;
            VisualElement dim = null, list = Ui.Box("");
            VisualElement go = Ui.Button("Reject", () => { if (reason == null) return; dim.RemoveFromHierarchy(); Decide(new Dictionary<string, object> { { "action", "reject" }, { "rejectReason", reason } }); });
            go.SetEnabled(false);
            void DrawReasons()
            {
                list.Clear();
                foreach (var r in Reasons)
                {
                    string key = r.key;
                    list.Add(Ui.Box(reason == key ? "scope scope-on adm-reason" : "scope adm-reason", Ui.Text(r.words, "scope-text", Ui.Bold)).OnTap(() => { reason = key; go.SetEnabled(true); DrawReasons(); }));
                }
            }
            DrawReasons();
            VisualElement card = Ui.Box("modal", Ui.Text("Reject applicant", "h2", Ui.Bold), Ui.Text("REASON", "kick", Ui.Bold).Margin(8), list, go.Margin(10), Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(Root).Add(dim);
        }
    }

    // ── Grading a certification lesson ───────────────────────────────────────

    public sealed class AdminCertGradeScreen : AppScreen
    {
        static readonly (string key, string title, string words)[] Criteria =
        {
            ("accuracy", "Data read accuracy", "Numbers read correctly: bands, rollout sign, entry"),
            ("data", "Use of Puttr data", "Snapshot, rollout pace, entry, trends used in the lesson"),
            ("comm", "Tool fluency", "Replay, overlays, wrist and head tracking, telestrator used correctly"),
            ("plan", "Practice plan setup", "Games and drills assigned with targets, cadence, and window"),
            ("production", "Production quality", "Audio and video are clear and watchable"),
        };
        readonly App _app;
        readonly object _cert;
        readonly Dictionary<string, int> _scores = new Dictionary<string, int>();
        readonly VisualElement _rubric, _bar;
        readonly TextField _notes;

        /// <summary>The app's pass rule (the API checks it again): 18 or more of 25, no 1s, at most one 2.</summary>
        public static bool Passes(IEnumerable<int> scores)
        {
            int total = 0, ones = 0, twos = 0;
            foreach (int s in scores) { total += s; if (s == 1) ones++; if (s == 2) twos++; }
            return total >= 18 && ones == 0 && twos <= 1;
        }

        public AdminCertGradeScreen(App app, object cert)
        {
            _app = app;
            _cert = cert;
            Root = Page.Make(app, "Grade certification", out VisualElement body);
            string name = J.Str(cert, "coachName", J.Str(cert, "coachEmail"));
            int length = J.Int(cert, "lessonVideoLength");
            body.Add(Ui.Box("pad", Ui.Box("glass row", Ui.Avatar(name, J.Str(cert, "coachPhoto"), true), Ui.Box("grow set-words", Ui.Text(name, "h2", Ui.Bold),
                Ui.Text("Delivered " + Adm.Ago(cert, "deliveredAt") + (length > 0 ? "   ·   " + length / 60 + ":" + (length % 60).ToString("00") : ""), "muted", Ui.Regular)))));
            string video = J.Str(cert, "lessonVideoUrl");
            if (video.StartsWith("http"))
            {
                string status = J.Str(cert, "lessonVideoTranscodeStatus");
                body.Add(Adm.Card("Lesson video", null, Ui.Button("Watch the lesson", () => SetupGuide.Play(Root, video), "btn btn-ghost"),
                    status == "pending" ? Ui.Text("Optimizing video for streaming... playback may stall until it finishes.", "muted", Ui.Regular)
                        : Ui.Text(status == "done" ? "Re-optimize video" : "Optimize video for streaming", "sec-link", Ui.Medium).Margin(8).OnTap(Transcode)));
            }
            if (J.Str(cert, "coachSummary").Length > 0) body.Add(Adm.Card("Their summary notes", null, Ui.Text(J.Str(cert, "coachSummary"), "sub", Ui.Regular)));
            _rubric = new VisualElement();
            _notes = new TextField { multiline = true };
            _notes.textEdition.placeholder = "Feedback for the coach (sent by email on a fail)...";
            _notes.textEdition.hidePlaceholderOnFocus = false;
            _notes.AddToClassList("field");
            body.Add(Adm.Card("Rubric", "pass at 18 of 25, no 1s, at most one 2", _rubric, _notes.Face(Ui.Medium).Margin(10)));
            _bar = Ui.Box("pad");
            body.Add(_bar);
            Draw();
        }

        void Draw()
        {
            _rubric.Clear();
            foreach (var c in Criteria)
            {
                string key = c.key;
                VisualElement row = Ui.Box("row");
                for (int i = 1; i <= 5; i++)
                {
                    int v = i;
                    bool on = _scores.TryGetValue(key, out int s) && s == v;
                    VisualElement b = Ui.Box("rate-star", Ui.Text(v.ToString(), "btn-text", Ui.Bold)).OnTap(() => { _scores[key] = v; Draw(); });
                    if (on) b.style.backgroundColor = v <= 2 ? Ui.Red : Ui.Green;
                    row.Add(b);
                }
                _rubric.Add(Ui.Box("adm-criterion", Ui.Text(c.title, "feed-name", Ui.Bold), Ui.Text(c.words, "muted", Ui.Regular), row.Margin(6)));
            }
            _bar.Clear();
            int total = 0; foreach (int s in _scores.Values) total += s;
            bool all = _scores.Count == Criteria.Length, pass = all && Passes(_scores.Values);
            _bar.Add(Ui.Text(total + " / 25   ·   " + (all ? (pass ? "Pass" : "Fail") : Criteria.Length - _scores.Count + " left to score"), "h2 center", Ui.Bold));
            VisualElement go = Ui.Button(!all ? "Score all five to grade" : pass ? "Pass and Certify" : "Fail with Feedback", () => Grade(total, pass), all && !pass ? "btn btn-red" : "btn");
            go.SetEnabled(all);
            _bar.Add(go.Margin(8));
        }

        void Grade(int total, bool pass)
        {
            Dialog.Ask(Root, pass ? "Pass this coach?" : "Fail this attempt?",
                "They score " + total + " of 25. " + (pass ? "They become certified and can go live once their profile and payouts are done." : "They get your feedback by email and one redo on a fresh session."),
                pass ? "Pass" : "Fail", () =>
                {
                    var scores = new Dictionary<string, object>();
                    foreach (var kv in _scores) scores[kv.Key] = kv.Value;
                    App.Run(Api.Post("/admin/coach/certification/" + J.Str(_cert, "id") + "/grade", new Dictionary<string, object> { { "scores", scores }, { "notes", _notes.value } },
                        _ => { _app.Pop(); _app.Toast("Graded. The coach has been notified."); }, why => _app.Toast(why == "http_400" ? "This lesson has already been graded or is not delivered." : "Could not save the grade.")));
                }, !pass);
        }

        void Transcode() => App.Run(Api.Post("/admin/lesson/booking/" + J.Str(_cert, "id") + "/transcode", new Dictionary<string, object>(), d =>
        {
            string status = J.Str(d, "status");
            Adm.Info(Root, status == "pending" ? "Optimizing video" : "Transcode " + status, status == "pending" ? "The transcoder is working on it. Reopen this certification in a few minutes to get the streamable video." : J.Str(d, "reason", J.Str(d, "error", J.Write(d))));
        }, _ => _app.Toast("Could not start the transcode.")));
    }

    // ── A junior application ─────────────────────────────────────────────────

    public sealed class AdminJuniorScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _body, _emails;
        object _a, _timeline;

        public static int Age(object row)
        {
            DateTime? born = ProfileMath.Date(row, "birthDate");
            if (born == null) return -1;
            DateTime now = DateTime.UtcNow;
            int age = now.Year - born.Value.Year;
            if (now < born.Value.AddYears(age)) age--;
            return age;
        }

        public AdminJuniorScreen(App app, string id)
        {
            _app = app;
            _id = id;
            Root = Page.Make(app, "Junior application", out _body);
            _emails = new VisualElement();
            _body.Add(Page.Loading());
            Load();
            LoadEmails();
        }

        void Load() => App.Run(Api.Get("/admin/junior/application/" + _id, d => { _a = J.Get(d, "application"); _timeline = J.Get(d, "timeline"); Draw(); }, _ => { _body.Clear(); _body.Add(Page.Note("Could not load the application.")); }));

        void LoadEmails()
        {
            _emails.Clear();
            _emails.Add(Page.Note("Looking up email delivery..."));
            App.Run(Api.Get("/admin/junior/application/" + _id + "/emails", Emails, why => { _emails.Clear(); _emails.Add(Page.Note("Lookup failed: " + why)); }));
        }

        void Emails(object d)
        {
            _emails.Clear();
            VisualElement card = Ui.Box("");
            string unavailable = J.Str(d, "unavailable");
            List<object> messages = J.List(J.Get(d, "messages"));
            if (unavailable.Length > 0) card.Add(Ui.Text(unavailable, "sub", Ui.Regular));
            else if (messages.Count == 0) card.Add(Ui.Text("SendGrid has no mail on record for " + string.Join(" or ", J.List(J.Get(d, "addresses")).ConvertAll(x => x as string)) + " in the last 30 days.", "sub", Ui.Regular));
            foreach (object m in messages)
            {
                string status = J.Str(m, "status");
                Label s = Ui.Text(status.Replace("_", " ").ToUpperInvariant(), "kick", Ui.Bold);
                s.style.color = status == "delivered" ? Ui.Bright : status == "not_delivered" ? Ui.Red : Ui.Amber;
                card.Add(Ui.Box("board-row", Ui.Box("grow", Ui.Text(J.Str(m, "subject", "(no subject)"), "feed-name", Ui.Bold),
                    Ui.Text("to " + J.Str(m, "to") + "   ·   " + Adm.Ago(m, "lastEvent") + "   ·   opened " + J.Int(m, "opens") + "x   ·   clicked " + J.Int(m, "clicks") + "x", "muted", Ui.Regular)), s));
            }
            _emails.Add(Adm.Card("Email delivery (SendGrid)", "refresh", card));
            _emails.Q<Label>(className: "sec-link")?.OnTap(LoadEmails);
        }

        void Draw()
        {
            _body.Clear();
            object a = _a;
            int status = J.Int(a, "status"), age = Age(a);
            bool minor = age >= 0 && age < 18;
            string name = J.Str(a, "displayName", J.Str(a, "email"));
            Label pill = Adm.Pill(status == 1 ? "APPROVED" : status == 2 ? "REJECTED" : status == 3 ? "UNDER 13 WAITLIST" : "PENDING", status == 1 ? Ui.Bright : status == 2 ? Ui.Red : status == 3 ? Ui.Blue : Ui.Amber);
            _body.Add(Ui.Box("pad", Ui.Box("glass row", Ui.Avatar(name, J.Str(a, "photoURL"), true), Ui.Box("grow set-words", Ui.Text(name, "h2", Ui.Bold),
                Ui.Text(J.Str(a, "email") + (J.Get(a, "memberSince") != null ? "   ·   joined " + ProfileMath.Date(a, "memberSince")?.ToString("MMM yyyy", CultureInfo.InvariantCulture) : ""), "muted", Ui.Regular)), pill)));
            string country = J.Str(a, "country"), region = J.Str(a, "region");
            bool nilRisk = minor && country == "US" && (region == "AL" || region == "HI");
            string nil = !minor ? "Not a minor, n/a" : country != "US" ? "Outside US" : nilRisk ? region + " bans HS deals" : region + " allows HS deals";
            object ps = J.Get(a, "playerStats");
            string followers = J.Str(a, "followersBand");
            followers = followers == "LT500" ? "<500" : followers == "500" ? "500+" : followers == "5K" ? "5k+" : followers == "25K" ? "25k+" : followers;
            string level = J.Str(a, "level");
            var rows = new List<VisualElement>
            {
                Adm.Row("Date of birth", Adm.Day(a, "birthDate") + (age >= 0 ? " (" + age + ")" : "")),
                Adm.Row("Hometown", J.Str(a, "hometown") + (country.Length > 0 ? ", " + country : "")),
                Adm.Row("NIL status", nil, minor && country == "US" ? (nilRisk ? Ui.Red : Ui.Bright) : (Color?)null),
                !J.Flag(a, "postingRequired") ? Adm.Row("Posting track", "NO-POST (free access only)", Ui.Amber) : null,
                Adm.Row("Handicap", J.Str(a, "handicap")),
                Adm.Row("Level", level == "CASUAL" ? "Casual" : level == "SCHOOL_TEAM" ? "School team" : level == "TOURNAMENTS" ? "Tournaments" : level),
                Adm.Row("School / team", J.Str(a, "school")),
                Adm.Row("Followers", followers + (J.Str(a, "primaryPlatform").Length > 0 ? " on " + J.Str(a, "primaryPlatform").ToLowerInvariant() : "")),
                minor ? Adm.Row("Parent consent", J.Get(a, "parentConsentAt") != null ? "Approved " + Adm.Ago(a, "parentConsentAt") : "Sent to " + J.Str(a, "parentEmail"), J.Get(a, "parentConsentAt") != null ? Ui.Bright : Ui.Amber) : null,
                Adm.Row("Puttr activity", Adm.N(J.Num(ps, "putts")) + " putts   ·   " + Adm.N(J.Num(ps, "games")) + " games   ·   last " + Adm.Ago(ps, "lastPlayed")),
            };
            foreach (string social in new[] { "instagram", "tiktok", "youtube" })
            {
                string handle = J.Str(a, social);
                if (handle.Length == 0) continue;
                string url = handle.StartsWith("http") ? handle : social == "instagram" ? "https://instagram.com/" + handle.TrimStart('@') : social == "tiktok" ? "https://tiktok.com/@" + handle.TrimStart('@') : handle;
                rows.Add(Ui.Box("board-row", Ui.Text(char.ToUpper(social[0]) + social.Substring(1), "muted adm-key", Ui.Medium), Ui.Text((social == "youtube" ? "" : "@") + handle.TrimStart('@'), "sec-link", Ui.Medium).OnTap(() => Application.OpenURL(url))));
            }
            _body.Add(Adm.Card("Details", null, rows.ToArray()));
            if (J.Str(a, "extraInfo").Length > 0) _body.Add(Adm.Card("From the applicant", null, Ui.Text(J.Str(a, "extraInfo"), "sub", Ui.Regular)));
            if (status != 0 && J.Str(a, "reviewNotes").Length > 0) _body.Add(Adm.Card(status == 2 ? "Decline reason (sent to the player)" : "Review notes", null, Ui.Text(J.Str(a, "reviewNotes"), "sub", Ui.Regular)));
            if (minor && J.Get(a, "parentConsentAt") == null && status == 0)
                _body.Add(Page.Note("Parent consent has not landed yet (" + Adm.Ago(a, "submittedAt") + " since submit). Approving now still grants the membership; consider waiting for the sign-off, or resend the consent email below."));

            // Status log
            VisualElement log = Ui.Box("");
            foreach (object item in J.List(J.Get(_timeline, "items")))
            {
                string tone = J.Str(item, "tone");
                VisualElement dot = Ui.Box("adm-dot");
                dot.style.backgroundColor = tone == "good" ? Ui.Bright : tone == "bad" ? Ui.Red : tone == "warn" ? Ui.Amber : new Color(1, 1, 1, 0.35f);
                Label title = Ui.Text(J.Str(item, "title"), "feed-name", Ui.Bold);
                if (tone == "bad") title.style.color = Ui.Red;
                log.Add(Ui.Box("adm-log", dot, Ui.Box("grow set-words", title, Ui.Text(J.Str(item, "detail"), "sub", Ui.Regular), Ui.Text(ProfileMath.Date(item, "at")?.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) + "   ·   " + Adm.Ago(item, "at"), "muted", Ui.Regular))));
            }
            if (!J.Flag(_timeline, "eventsLogged")) log.Add(Ui.Text("No email records for this application. Sends before Nov 2026 were not logged, so an email may have gone out without appearing here. Use Resend below to send a tracked copy.", "muted", Ui.Regular).Margin(6));
            VisualElement resends = Ui.Box("chips");
            var stages = new List<(string key, string label, bool parent)>();
            if (status == 3) stages.Add(("parentWaitlist", "Parent waitlist email", true));
            else
            {
                if (minor) stages.Add(("parentConsent", "Parent consent email", true));
                stages.Add(("received", "Confirmation to player", false));
                if (status == 1) stages.Add(("approved", "Approval email", false));
                if (status == 2) stages.Add(("rejected", "Decline email", false));
            }
            foreach (var st in stages) { var s = st; resends.Add(Adm.Pill("Resend " + s.label.ToLowerInvariant(), Ui.Blue).OnTap(() => Resend(s.key, s.label, s.parent))); }
            _body.Add(Adm.Card("Status log", null, log, resends.Margin(8)));
            _body.Add(_emails);

            if (status == 0)
                _body.Add(Ui.Box("pad", Ui.Box("row", Ui.Button("Decline", Decline, "btn btn-ghost grow"), FreePuttScreen.Space(0).Width(8), Ui.Button("Approve", Approve, "btn grow"))).Margin(12));
        }

        void Merge(object d)
        {
            object a = J.Get(d, "application");
            if (a is Dictionary<string, object> fresh && _a is Dictionary<string, object> mine) foreach (var kv in fresh) mine[kv.Key] = kv.Value;
            if (J.Get(d, "timeline") != null) _timeline = J.Get(d, "timeline");
            Draw();
        }

        void Approve() => Dialog.Ask(Root, "Approve application?", "This grants free Club membership for one year (isMember + expiration). The player gets a push and an email.", "Approve",
            () => App.Run(Api.Post("/admin/junior/application/" + _id + "/decide", new Dictionary<string, object> { { "action", "approve" } }, d => { Merge(d); _app.Toast("Approved."); }, why => _app.Toast(why == "http_400" ? "Already decided." : "Could not save the decision."))));

        void Decline()
        {
            VisualElement dim = null;
            var reason = new TextField { multiline = true, maxLength = 1000 };
            reason.textEdition.placeholder = "Optional. e.g. We need an active public golf account to verify posting.";
            reason.textEdition.hidePlaceholderOnFocus = false;
            reason.AddToClassList("field");
            VisualElement card = Ui.Box("modal", Ui.Text("Decline application", "h2", Ui.Bold), Ui.Text("The player gets a soft decline email. Anything you type below is included in that email, word for word.", "sub", Ui.Regular).Margin(6),
                Ui.Text("REASON (SENT TO THE PLAYER)", "kick", Ui.Bold).Margin(8), reason.Face(Ui.Medium),
                Ui.Button("Decline", () =>
                {
                    dim.RemoveFromHierarchy();
                    var body = new Dictionary<string, object> { { "action", "reject" } };
                    if (reason.value.Trim().Length > 0) body["declineReason"] = reason.value.Trim();
                    App.Run(Api.Post("/admin/junior/application/" + _id + "/decide", body, d => { Merge(d); _app.Toast("Declined."); }, why => _app.Toast(why == "http_400" ? "Already decided." : "Could not save the decision.")));
                }, "btn btn-red").Margin(10),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(Root).Add(dim);
        }

        void Resend(string stage, string label, bool parent)
        {
            VisualElement dim = null;
            var to = new TextField { value = parent ? J.Str(_a, "parentEmail") : J.Str(_a, "email") };
            to.AddToClassList("field");
            VisualElement send = Ui.Button("Send", () =>
            {
                if (to.value.Trim().Length == 0) return;
                dim.RemoveFromHierarchy();
                App.Run(Api.Post("/admin/junior/application/" + _id + "/resend", new Dictionary<string, object> { { "stage", stage }, { "to", to.value.Trim() } }, d =>
                {
                    Merge(d);
                    object sent = J.Get(d, "sent");
                    if (J.Str(sent, "result") == "sent") Adm.Info(Root, "Sent", label + " went to " + J.Str(sent, "to") + ".");
                    else Adm.Info(Root, "SendGrid rejected it", J.Str(sent, "error", "No reason given."));
                    Root.schedule.Execute(LoadEmails).StartingIn(4000);
                }, why => _app.Toast("Not sent (" + why + ").")));
            });
            VisualElement card = Ui.Box("modal", Ui.Text("Resend " + label.ToLowerInvariant(), "h2", Ui.Bold),
                Ui.Text(parent ? "Check the address with the parent first. Changing it here updates the application, so future emails go to the new address too."
                    : "Sends the same email again. A different address here is a one-off; the player's profile email is not changed.", "sub", Ui.Regular).Margin(6),
                Ui.Text("SEND TO", "kick", Ui.Bold).Margin(8), to.Face(Ui.Medium), send.Margin(10), Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(Root).Add(dim);
        }
    }
}
