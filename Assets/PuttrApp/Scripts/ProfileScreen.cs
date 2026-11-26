// The Profile tab, from the live API, carrying everything puttr-app's profile/UserOverview.js shows:
// who you are, your membership and putter, all-time numbers, achievements, challenges, the last 30
// days, putts by month, make % by distance and by break, competition, personal bests and quest stars.
// One scrolling page as UserOverview has it, flat as docs/design-guidelines.md ("Broadcast") asks:
// the drawn pieces are ProfileVisuals.cs, the styles Resources/PuttrApp/Profile.uss.
//
// Not brought over yet: the Vertex stroke card and chipping.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>The Profile tab's sums and sentences, apart from the screen so they can be tested.</summary>
    public static class ProfileMath
    {
        /// <summary>PGA Tour make % from 3 to 11 feet (the app's proStats).</summary>
        public static readonly int[] Pga = { 99, 95, 90, 85, 80, 75, 70, 65, 61 };
        public static readonly string[] BreakNames = { "Left edge", "Straight", "Right edge" };

        public static DateTime? Date(object row, string key)
        {
            string text = J.Str(row, key);
            if (text.Length == 0) return null;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime at) ? at : (DateTime?)null;
        }

        public static string Day(DateTime at) => at.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        public static string Number(double n) => n.ToString("#,0", CultureInfo.InvariantCulture);

        /// <summary>The line under "Membership", as the app words it.</summary>
        public static string Membership(object profile, DateTime now)
        {
            if (!J.Flag(profile, "isMember")) return "Unlock all games and features";
            DateTime? trial = Date(profile, "trialExpiration"), paid = Date(profile, "subscriptionExpiration");
            DateTime? until = trial ?? paid;
            if (until == null || until.Value <= now) return "Active";
            string source = J.Str(profile, "membershipSource");
            if (source.Length == 0) source = trial != null ? "admin" : J.Str(profile, "subscriptionType").Length > 0 ? "store" : "";
            string day = Day(until.Value);
            switch (source)
            {
                case "store": return (J.Flag(profile, "subscriptionCancelled") ? "Ends " : "Renews ") + day;
                case "junior": return "Junior membership through " + day;
                case "shopify_pro": return "Pro year ends " + day;
                default: return "Club year ends " + day;
            }
        }

        /// <summary>A negative handicap is a plus handicap; none is a dash.</summary>
        public static string Handicap(object profile)
        {
            object raw = J.Get(profile, "handicap");
            if (raw == null) return "–";
            double h = J.Num(profile, "handicap");
            return h < 0 ? "+" + Math.Abs(h).ToString("0.#", CultureInfo.InvariantCulture) : h.ToString("0.#", CultureInfo.InvariantCulture);
        }

        public static int MadePercent(object profile)
        {
            double made = J.Num(profile, "totalMade"), putts = J.Num(profile, "totalPutts");
            return made > 1 && putts > 0 ? (int)Math.Round(made / putts * 100.0) : 0;
        }

        /// <summary>What a putter is made of, joined with dots; the API's "null" strings count as nothing.</summary>
        public static string PutterDetail(object putter)
        {
            var parts = new List<string>();
            foreach (string key in new[] { "model", "length", "head", "shaft" })
            {
                string value = J.Str(putter, key).Trim();
                if (value.Length == 0 || value == "null" || value == "undefined") continue;
                parts.Add(key == "length" ? value + "\"" : value);
            }
            return string.Join("   ·   ", parts);
        }

        /// <summary>Putts on each day, by "yyyy-MM-dd", from user_stats.calendar_year (a JSON string of {count, date}).</summary>
        public static Dictionary<string, int> Calendar(object stats)
        {
            var days = new Dictionary<string, int>();
            object raw = J.Get(stats, "calendar_year");
            if (raw is string text) raw = J.Parse(text);
            foreach (object entry in J.List(raw))
            {
                string date = J.Str(entry, "date");
                if (date.Length >= 10) days[date.Substring(0, 10)] = J.Int(entry, "count");
            }
            return days;
        }

        /// <summary>Putts in each of the last seven months, oldest first, with the month's short name.</summary>
        public static List<(string month, int putts)> ByMonth(Dictionary<string, int> calendar, DateTime today)
        {
            var months = new List<(string, int)>();
            for (int back = 6; back >= 0; back--)
            {
                DateTime month = new DateTime(today.Year, today.Month, 1).AddMonths(-back);
                string prefix = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                int sum = 0;
                foreach (KeyValuePair<string, int> day in calendar) if (day.Key.StartsWith(prefix)) sum += day.Value;
                months.Add((month.ToString("MMM", CultureInfo.InvariantCulture), sum));
            }
            return months;
        }

        /// <summary>Make % from each tee (1 left edge, 2 straight, 3 right edge) from /profile/breaks rows {ct, tee, made}; -1 where nothing was putted.</summary>
        public static int[] Breaks(List<object> rows)
        {
            var made = new double[4];
            var all = new double[4];
            foreach (object row in rows)
            {
                int tee = J.Int(row, "tee");
                if (tee < 1 || tee > 3) continue;
                all[tee] += J.Num(row, "ct");
                if (J.Int(row, "made") == 1) made[tee] += J.Num(row, "ct");
            }
            var percent = new int[3];
            for (int tee = 1; tee <= 3; tee++) percent[tee - 1] = all[tee] > 0 ? (int)Math.Round(made[tee] / all[tee] * 100.0) : -1;
            return percent;
        }

        /// <summary>"3 days left", "5 hours left", or "Ended".</summary>
        public static string TimeLeft(DateTime? end, DateTime now)
        {
            if (end == null) return "";
            TimeSpan left = end.Value - now;
            if (left.TotalSeconds <= 0) return "Ended";
            if (left.TotalDays >= 2) return (int)left.TotalDays + " days left";
            if (left.TotalHours >= 2) return (int)left.TotalHours + " hours left";
            return "Ends soon";
        }
    }


    /// <summary>
    /// Your profile (the Profile tab), or another Player's (puttr-app MemberOverview: same data, read from
    /// /members/*/:userId, with Follow and Fist bump in place of your own membership and actions).
    ///
    /// One scrolling page, as UserOverview has it, built to docs/design-guidelines.md: the header, the Player
    /// card (photo with the edit pencil on its corner, name, hometown), putter, Switch profile, the stat strip
    /// (following, followers, handicap), the membership row, then all time as one stat strip, achievement
    /// crests, challenge cards, the calendar, the putts by month line, make % by distance and by break,
    /// competition, personal bests and quest stars. Each piece of information shows once. No main button. On a tablet or a phone on its side (.wide) the hero takes a column
    /// of its own on the left; with room enough the sections then sit in two columns (.pf-two).
    /// The drawn pieces are in ProfileVisuals.cs, the styles in Resources/PuttrApp/Profile.uss.
    /// </summary>
    public sealed class ProfileScreen : AppScreen
    {
        readonly App _app;
        readonly string _member; // null: the signed-in Player
        List<object> _following;
        bool _busy, _puttersRead;
        readonly VisualElement _h2h, _pqs;
        readonly VisualElement _who, _all, _achievements, _challenges, _activity, _months, _distance, _breaks, _compete, _bests, _quests;
        readonly VisualElement _head, _top, _list, _two, _colA, _colB, _sideCol;
        readonly ScrollView _main, _side;
        readonly VisualElement _face;
        bool _allFacts;
        static readonly Color Surface3 = new Color32(48, 53, 58, 255);   // surface-3 in docs/design-guidelines.md
        string _faceKey;
        bool? _wideNow, _twoNow;
        object _profile, _putter;
        List<object> _mine, _puttr;
        float _loadedAt = -100f;

        /// <summary>Something the tab shows has changed (a challenge joined or left): read again the next time it is shown.</summary>
        public void Stale() => _loadedAt = -100f;

        public ProfileScreen(App app) : this(app, null, null) { }

        /// <summary>Another Player's profile. seed: whatever row was tapped (a name and photo to show at once).</summary>
        public static void Open(App app, string userId, object seed = null)
        {
            if (string.IsNullOrEmpty(userId)) { app.Toast("That Player played as a guest."); return; }
            if (userId == J.Str(app.Profile, "userId")) { app.ShowTabs("profile"); return; }
            if (Kid.Gate(app, Kid.Social)) return;
            app.Push(new ProfileScreen(app, userId, seed));
        }

        ProfileScreen(App app, string member, object seed)
        {
            _app = app;
            _member = member;
            _profile = seed;
            _who = Slot(); _all = Slot(); _achievements = Slot(); _challenges = Slot(); _activity = Slot(); _months = Slot();
            _distance = Slot(); _breaks = Slot(); _compete = Slot(); _bests = Slot(); _quests = Slot();
            _h2h = Slot(); _pqs = Slot(); // another Player's only: Head to head, and the Putt Quality Score
            _face = Ui.Box("pf-face-wrap");

            Root = Ui.Box("pf");
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Profile");
            if (sheet != null) Root.styleSheets.Add(sheet);

            // The head: the tab's header (or room for Back on another Player's), then who, the strip and membership.
            _top = Ui.Box(member != null ? "pf-top pf-top-member" : "pf-top");
            FillTop();
            _head = Ui.Box("pf-head", _top, _who);

            // The sections, in the page's order on a phone; Layout() deals them into two columns when there is room.
            _list = Ui.Box("pf-list");
            _colA = Ui.Box("pf-col");
            _colB = Ui.Box("pf-col");
            _two = Ui.Box("pf-cols", _colA, _colB);
            _main = Ui.Scroll();
            _main.AddToClassList("pf-main");
            _main.Add(_head);
            _main.Add(_list);
            _main.Add(_two);
            var foot = new VisualElement();
            foot.style.height = 32;
            _main.Add(foot);
            _side = Ui.Scroll();
            _side.AddToClassList("pf-side");
            _sideCol = _side.contentContainer;
            // The main scroll comes first, so -app2Scroll and anything else looking for "the" scroll finds it;
            // .pf-wide lays the row out reversed to put the side column on the left.
            Root.Add(_main);
            Root.Add(_side);
            if (member != null)
            {
                VisualElement bar = Ui.Header("Profile", _app.Pop);
                bar.style.position = Position.Absolute;
                bar.style.left = bar.style.right = bar.style.top = 0;
                bar.style.backgroundColor = Ui.Base;
                Root.Add(bar);
                // The head keeps exactly the bar's room (plus a little air), whatever the bar measures:
                // a fixed 44 was shorter than the bar, which then cut the top off the photo (Matt, 2026-11-23).
                bar.RegisterCallback<GeometryChangedEvent>(e => { if (e.newRect.height > 1f) _top.style.height = e.newRect.height + 12f; });
            }
            Deal(false);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            // App.Layout sets .wide on the app without resizing this screen, so look again now and then too.
            Root.schedule.Execute(Layout).Every(400);

            if (seed != null) ShowWho();
            // Placeholders where each section will be, until its data draws it (the page has its shape at once).
            else Skeleton.Hold(_who, HeroSkeleton());
            Skeleton.Hold(_all, Skeleton.Section(200));
            Skeleton.Hold(_achievements, Skeleton.Strip(110));
            Skeleton.Hold(_challenges, Skeleton.Section(130));
            Skeleton.Hold(_activity, Skeleton.Section(300));
            Skeleton.Hold(_months, Skeleton.Section(170));
            Skeleton.Hold(_distance, Skeleton.Section(220));
            Skeleton.Hold(_breaks, Skeleton.Section(150));
            if (member != null) { Skeleton.Hold(_h2h, Skeleton.Section(170)); Skeleton.Hold(_pqs, Skeleton.Section(320)); }
        }

        static VisualElement Slot() => new VisualElement();

        static VisualElement HeroSkeleton()
        {
            VisualElement face = Skeleton.Circle(80);
            face.style.borderTopLeftRadius = face.style.borderTopRightRadius = face.style.borderBottomLeftRadius = face.style.borderBottomRightRadius = 4;
            return Ui.Box("pf-sk-hero", Ui.Box("row", face, Ui.Box("grow pf-sk-words", Skeleton.Bar(0.7f, 28f), Skeleton.Bar(0.5f).Margin(8))), Skeleton.Bar(1f, 64f).Margin(16), Skeleton.Bar(1f, 64f).Margin(16));
        }

        void FillTop()
        {
            _top.Clear();
            // The tab's header: its name, then the bell (notifications, with its unread count) and the gear (Settings,
            // which also holds the developer switches and Sign out) as icon squares.
            // Made again whenever the head moves, since the bell stops listening once it leaves the panel.
            // Another Player's profile: the Back bar is pinned over the page (below), so it stays while the page
            // scrolls; this keeps its room at the top.
            if (_member != null) return;
            VisualElement bell = Inbox.Bell(_app);
            bell.AddToClassList("icon-sq");
            _top.Add(Ui.TabHeader("Profile", bell, Ui.IconSquare("GearSix", () => _app.Push(new SettingsScreen(_app)))));
        }

        // ── Layout: one column on a phone; the head beside the sections when there is room ──

        void Layout()
        {
            bool wide = false;
            for (VisualElement e = Root.parent; e != null; e = e.parent) if (e.ClassListContains("wide")) { wide = true; break; }
            if (_wideNow != wide)
            {
                _wideNow = wide;
                Root.EnableInClassList("pf-wide", wide);
                _side.style.display = wide ? DisplayStyle.Flex : DisplayStyle.None;
                if (wide) _sideCol.Add(_head); else _main.contentContainer.Insert(0, _head);
                FillTop();
            }
            float width = Root.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f) return;
            if (wide) _side.style.width = Mathf.Clamp(width * 0.34f, 320f, 420f);
            float rest = wide ? width - Mathf.Clamp(width * 0.34f, 320f, 420f) : width;
            bool two = wide && rest >= 700f;
            if (_twoNow != two) { _twoNow = two; Root.EnableInClassList("pf-two", two); Deal(two); }
        }

        /// <summary>Puts the sections in one list, or (two) all time across the top, two columns, and make % by distance across the foot.</summary>
        void Deal(bool two)
        {
            if (!two)
            {
                foreach (VisualElement slot in new[] { _h2h, _all, _achievements, _challenges, _activity, _pqs, _months, _distance, _breaks, _compete, _bests, _quests }) _list.Add(slot);
                _two.style.display = DisplayStyle.None;
                return;
            }
            _list.Add(_h2h);
            _list.Add(_all);
            foreach (VisualElement slot in new[] { _achievements, _challenges, _activity, _compete }) _colA.Add(slot);
            foreach (VisualElement slot in new[] { _pqs, _months, _breaks, _bests, _quests }) _colB.Add(slot);
            _two.style.display = DisplayStyle.Flex;
            _main.contentContainer.Insert(_main.contentContainer.IndexOf(_two) + 1, _distance);
        }

        // As the app does, the numbers are read again whenever the tab comes to the front (but not twice in a few seconds).
        public override void Shown()
        {
            if (_member != null) { ShownMember(); return; }
            if (_app.Profile != null && _profile == null) { _profile = _app.Profile; ShowWho(); ShowAllTime(); }
            if (Time.realtimeSinceStartup - _loadedAt < 20f) return;
            _loadedAt = Time.realtimeSinceStartup;
            App.Run(Api.Get("/profile/", data => { _profile = _app.Profile = J.First(data); ShowWho(); ShowAllTime(); }, Failed));
            App.Run(Api.Get("/user-putters", data => { List<object> putters = J.List(data); _putter = putters.Count > 0 ? putters[0] : null; _puttersRead = true; ShowWho(); }));
            App.Run(Api.Get("/profile/userStats", data => ShowActivity(J.First(data))));
            App.Run(Api.Get("/profile/achievements/recent", data => ShowAchievements(J.List(data))));
            App.Run(Api.Get("/profile/putts/90", data => { _mine = J.List(data); ShowDistance(); }));
            App.Run(Api.Get("/members/analytics/puttsByDistance", data => { _puttr = J.List(data); ShowDistance(); }));
            App.Run(Api.Get("/profile/breaks/90", data => ShowBreaks(J.List(data))));
            App.Run(Api.Get("/challenges/userChallenges", data => ShowChallenges(J.List(data))));
            App.Run(Api.Get("/profile/summary/", ShowSummary));
            App.Run(Xp.Load(ShowWho));
            Inbox.Load();
        }

        void ShownMember()
        {
            if (Time.realtimeSinceStartup - _loadedAt < 20f) return;
            _loadedAt = Time.realtimeSinceStartup;
            string id = "/" + _member;
            Xp.Need(new[] { _member }, ShowWho);
            App.Run(Api.Get("/profile/following/", data => { _following = J.List(data); ShowWho(); }));
            App.Run(Api.Get("/members/profile" + id, data => { object p = J.First(data); if (p != null) _profile = p; ShowWho(); ShowAllTime(); }));
            App.Run(Api.Get("/members/userStats" + id, data => ShowActivity(J.First(data))));
            App.Run(Api.Get("/members/achievements/recent" + id, data => ShowAchievements(J.List(data))));
            App.Run(Api.Get("/members/putts" + id, data => { _mine = J.List(data); ShowDistance(); }));
            App.Run(Api.Get("/members/analytics/puttsByDistance", data => { _puttr = J.List(data); ShowDistance(); }));
            App.Run(Api.Get("/members/breaks" + id, data => ShowBreaks(J.List(data))));
            App.Run(Api.Get("/members/summary" + id, ShowSummary, _ => _h2h.Clear()));
            // PuttScore.js: every putt of theirs, scored here into the Putt Quality Score and its trend.
            App.Run(Api.Get("/members/allPutts" + id, data => ShowPqs(J.List(data)), _ => _pqs.Clear()));
            // (Another Player's challenges are not shown: the app's MemberOverview has none.)
            _challenges.Clear();
        }

        bool Following => _following != null && _following.Exists(u => J.Str(u, "userId") == _member);

        void Follow()
        {
            if (_busy || _following == null) return;
            _busy = true;
            bool now = Following;
            App.Run(Api.Get("/members/" + (now ? "unfollow/" : "follow/") + _member, _ =>
                App.Run(Api.Get("/profile/following/", data => { _following = J.List(data); _busy = false; ShowWho(); }, __ => _busy = false)),
                _ => { _busy = false; _app.Toast("That did not work. Try again."); }));
        }

        void Bump() => FistBumps.Send(_app, _member, ShowWho);

        void Failed(string why)
        {
            if (why == "signed_out") _app.SignedOut();
        }

        /// <summary>A section: its title (and link) over the card.</summary>
        static void Put(VisualElement slot, string title, VisualElement card, string link = null, Action onLink = null)
        {
            slot.Clear();
            if (card == null) return;
            slot.Add(Ui.Section(title, link, onLink));
            slot.Add(Ui.Box("pad", card));
        }

        static VisualElement Card(params VisualElement[] children) => Ui.Box("pf-card", children);

        static VisualElement Empty(string words) => Card(Ui.Text(words, "sub", Ui.Regular)).Pad();

        /// <summary>A list row: a plain icon (or none), a title over one quiet line, then a value or a chevron.</summary>
        static VisualElement Row(string icon, string title, string detail, VisualElement trailing = null, Color? tint = null)
        {
            VisualElement words = Ui.Box("grow", Ui.Text(title, "pf-row-title", Ui.Bold));
            if (!string.IsNullOrEmpty(detail)) words.Add(Ui.Text(detail, "pf-row-detail", Ui.Regular));
            VisualElement row = Ui.Box("pf-row");
            if (icon != null)
            {
                Image glyph = Ui.Icon(icon, 24, tint ?? Color.white);
                glyph.AddToClassList("pf-row-icon");
                row.Add(glyph);
            }
            row.Add(words);
            if (trailing != null) row.Add(trailing);
            return row;
        }

        /// <summary>One cell of a stat strip (Ui.StatStrip's, built by hand where a cell is tapped).</summary>
        static VisualElement StripCell(string number, string caption, bool first, Action tap = null)
        {
            VisualElement cell = Ui.Box(first ? "strip-cell strip-cell-first" : "strip-cell", Ui.Num(number, "strip-num"), Ui.Caps(caption, "strip-cap"));
            return tap != null ? cell.OnTap(tap) : cell;
        }

        /// <summary>Progress: a 4 point bar, brand green on its track.</summary>
        static VisualElement Meter(float share)
        {
            VisualElement fill = Ui.Box("pf-meter-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(share) * 100f);
            return Ui.Box("pf-meter", fill);
        }

        /// <summary>Marks the first of a run (USS here has no :first-child): no rule or gap before it.</summary>
        static T First<T>(T element) where T : VisualElement { element.AddToClassList("pf-first"); return element; }

        static VisualElement Chevron()
        {
            Image chevron = Ui.Icon("CaretRight", 16, Ui.Faint);
            chevron.AddToClassList("pf-row-end");
            return chevron;
        }

        // ── Who: the Player card, the strip and membership ──────────────────

        static string Clean(string value)
        {
            value = (value ?? "").Trim();
            return value == "null" || value == "undefined" ? "" : value;
        }

        void ShowWho()
        {
            if (_profile == null) return;
            string name = J.Str(_profile, "displayName", "Player"), town = Clean(J.Str(_profile, "homeTown")), bio = Clean(J.Str(_profile, "bio"));
            bool mine = _member == null, isMember = J.Flag(_profile, "isMember");
            _who.Clear();

            // The photo: only made again when the photo changes, so a second ShowWho does not flash the letter.
            string key = name + "|" + J.Str(_profile, "photoURL") + "|" + J.Str(_profile, "userId");
            if (key != _faceKey)
            {
                _faceKey = key;
                _face.Clear();
                var face = new Dictionary<string, object> { { "name", name }, { "photoURL", J.Str(_profile, "photoURL") }, { "userId", J.Str(_profile, "userId") }, { "isKid", Kid.IsKid(_profile) ? 1 : 0 } };
                VisualElement avatar = Profiles.Face(face, 80);
                avatar.AddToClassList("pf-face");
                // A rounded square, as every avatar (Profiles.Face rounds it to a circle).
                avatar.style.borderTopLeftRadius = avatar.style.borderTopRightRadius = avatar.style.borderBottomLeftRadius = avatar.style.borderBottomRightRadius = 4;
                _face.Add(avatar);
            }
            // A member's photo keeps a plain bright green border.
            if (_face.childCount > 0) _face[0].EnableInClassList("pf-face-member", isMember);
            // Your own: the pencil on the photo's corner edits the profile (a Kid changes their character instead).
            _face.Q(className: "pf-edit-hit")?.RemoveFromHierarchy();
            if (mine)
            {
                Action edit = Kid.IsKid(_profile)
                    ? (Action)(() => _app.Push(new CharacterScreen(_app,
                        new Dictionary<string, object> { { "userId", J.Str(_profile, "userId") }, { "name", J.Str(_profile, "firstName", name) } }, false)))
                    : (Action)(() => _app.Push(new EditProfileScreen(_app)));
                _face.Add(Ui.Box("pf-edit-hit", Ui.Box("pf-edit", new PencilGlyph())).OnTap(edit));
            }

            // The name, then the rank badge (Xp.cs) once the Player's level is known.
            int? level = mine ? (Xp.Mine != null ? Xp.Level(Xp.Mine.Value) : (int?)null) : Xp.LevelOf(_member);
            VisualElement title = Ui.Title(name, name.Length > 18 ? "title-20" : "title");
            VisualElement words = Ui.Box("grow pf-id-words", level != null ? Ui.Box("row xp-named", title, Xp.Badge(level.Value, true)) : title);
            var line = new List<string>();
            if (town.Length > 0) line.Add(town);
            DateTime? joined = ProfileMath.Date(_profile, "created_at");
            if (joined != null) line.Add("Since " + joined.Value.ToString("MMM yyyy", CultureInfo.InvariantCulture));
            if (line.Count > 0) words.Add(Ui.Caps(string.Join("  ·  ", line), "caps caps-green pf-id-line"));
            // Another Player's membership (your own is the row below).
            if (!mine && isMember) words.Add(Ui.Caps("Puttr Club", "caps pf-id-line"));
            VisualElement hero = Ui.Box("pf-hero", Ui.Box("pf-id", _face, words));
            if (bio.Length > 0) hero.Add(Ui.Text(bio, "pf-bio", Ui.Regular));

            // The putter: its brand's mark, the brand and model, and what it is made of.
            object putter;
            string brand;
            if (mine) { putter = _putter; brand = Clean(J.Str(_putter, "brand")); }
            else { PutterBrands.Player p = PutterBrands.Resolve(_profile); putter = p.Putter; brand = Clean(p.Brand); }
            string model = Clean(J.Str(putter, "model"));
            var spec = new List<string>();
            string length = Clean(J.Str(putter, "length"));
            if (length.Length > 0) spec.Add(length + "\"");
            foreach (string part in new[] { "head", "shaft" }) { string v = Clean(J.Str(putter, part)); if (v.Length > 0) spec.Add(v.ToLowerInvariant()); }
            string gear = brand.Length > 0 ? (model.Length > 0 ? brand + " " + model : brand) : "";
            if (gear.Length > 0 || (mine && _puttersRead))
            {
                VisualElement badge = brand.Length > 0 ? PutterBrands.Badge(brand, 26f, 1.4f, true) : null;
                VisualElement gearWords = Ui.Box("pf-putter-words", Ui.Text(gear.Length > 0 ? gear : "Add your putter", "pf-row-title", Ui.Bold));
                string detail = gear.Length > 0 ? string.Join("  ·  ", spec) : "Tell Puttr what you putt with";
                if (detail.Length > 0) gearWords.Add(Ui.Text(detail, "pf-row-detail", Ui.Regular));
                VisualElement row = Ui.Box("pf-card pf-putter", Ui.Box("pf-putter-mark", badge ?? (VisualElement)Ui.Icon("Target", 24, Color.white)), gearWords);
                if (mine) { row.Add(Chevron()); row.OnTap(() => _app.Push(new PuttersScreen(_app))); }
                hero.Add(row);
            }

            if (mine)
            {
                // UserOverview's actions. Editing is the pencil on the photo (above); Switch profile shows for a
                // managed player or an owner with players (its sheet also holds Manage profiles); Admin only for
                // isAdmin (the API checks it again on every admin call). Profile has no main button.
                if (Profiles.HasSwitch(_profile))
                {
                    VisualElement switcher = Ui.Secondary("Switch profile", () => Profiles.Sheet(_app, Root));
                    switcher.AddToClassList("pf-switch");
                    hero.Add(switcher);
                }
                if (Adm.IsAdmin(_profile)) hero.Add(Ui.Text("Admin", "pf-link", Ui.Medium).OnTap(() => _app.Push(new AdminScreen(_app))));
            }
            else if (_following != null)
            {
                // Follow (this page's one main button), and once you follow them a fist bump.
                VisualElement buttons = Ui.Box("pf-actions", Following ? Ui.Secondary("Following", Follow) : Ui.Button("Follow", Follow));
                if (Following && FistBumps.CanBump(_app, _member))
                {
                    VisualElement bump = Ui.Secondary(FistBumps.Bumped(_member) ? "Bumped" : "Fist bump", Bump);
                    bump.AddToClassList("pf-action-next");
                    buttons.Add(bump);
                }
                hero.Add(buttons);
            }
            _who.Add(hero);

            // Your level (Xp.cs): the card, under a header whose "?" opens the Levels sheet. Another Player shows the badge only.
            if (mine && Xp.Mine != null)
            {
                Action levels = () => _app.Push(new LevelsScreen(_app));
                _who.Add(Ui.Section("Level", "?", levels));
                _who.Add(Ui.Box("pad", Xp.LevelCard(Xp.Mine.Value, false, levels)));
            }

            // The stat strip: following, followers and the handicap.
            _who.Add(Ui.Box("pad pf-gap", Ui.Box("strip",
                StripCell(ProfileMath.Number(J.Num(_profile, "following")), "Following", true, mine ? () => _app.Push(new SocialScreen(_app, false)) : (Action)null),
                StripCell(ProfileMath.Number(J.Num(_profile, "followers")), "Followers", false, mine ? () => _app.Push(new SocialScreen(_app, true)) : (Action)null),
                StripCell(ProfileMath.Handicap(_profile), "Handicap", false))));

            if (!mine) return;
            // Membership as a row, and Profiles for an owner with none yet (once there are, Switch profile leads there).
            VisualElement end = isMember ? Chevron() : Ui.Box("row", Ui.Caps("Join now", "caps caps-green pf-row-tag"), Chevron());
            VisualElement club = Card(First(Row(null, "Puttr Club", ProfileMath.Membership(_profile, DateTime.UtcNow), end))).Pad();
            _who.Add(Ui.Box("pad pf-gap-12", club.OnTap(() => MembershipScreen.Open(_app))));
            if (!Profiles.HasSwitch(_profile) && !Kid.Managed(_profile))
                _who.Add(Ui.Box("pad pf-gap-12", Card(First(Row("UsersThree", "Profiles", "Add profiles to share this device", Chevron()))).Pad().OnTap(() => _app.Push(new ProfilesScreen(_app)))));
        }

        // ── All time: one stat strip ─────────────────────────────────────────

        void ShowAllTime()
        {
            if (_profile == null) return;
            double made = J.Num(_profile, "totalMade"), putts = J.Num(_profile, "totalPutts"), feet = J.Num(_profile, "totalFeet"), games = J.Num(_profile, "totalGames");
            (string value, string caption)[] cells;
            if (!_allFacts)
                cells = new[] { (ProfileMath.Number(made), "Made"), (ProfileMath.Number(putts), "Putts"), (ProfileMath.Number(feet), "Feet"), (ProfileMath.Number(games), "Games") };
            else
            {
                // What each total means (the back of the old cards), never the total again: tap the strip to turn it.
                DateTime? joined = ProfileMath.Date(_profile, "created_at");
                string perDay = joined != null && putts > 0
                    ? ProfileMath.Number(Math.Max(1.0, Math.Round(putts / Math.Max(1.0, (DateTime.UtcNow - joined.Value).TotalDays)))) : "–";
                double miles = feet / 5280.0;
                string perGame = games > 0 && putts > 0 ? ProfileMath.Number(Math.Round(putts / games)) : "–";
                cells = new[]
                {
                    (ProfileMath.MadePercent(_profile) + "%", "Make rate"), (perDay, "Per day"),
                    (miles.ToString(miles >= 100 ? "#,0" : "0.0", CultureInfo.InvariantCulture), "Miles"), (perGame, "Per game"),
                };
            }
            VisualElement strip = Ui.StatStrip(cells).OnTap(() => { _allFacts = !_allFacts; ShowAllTime(); });
            Put(_all, "All time", strip, "Recent games", () => _app.Push(new GamesScreen(_app, _member)));
        }

        // ── Achievements and challenges ──────────────────────────────────────

        internal static void Look(string type, out string icon, out Color tint)
        {
            switch (type)
            {
                case "PUTTS": icon = "FillTarget"; tint = Ui.Green; break;
                case "DISTANCE": icon = "FillTrophy"; tint = Ui.Red; break;
                case "STREAK": icon = "FillFire"; tint = Ui.Amber; break;
                case "GAMES": icon = "FillJoystick"; tint = Ui.Faint; break;
                case "DAYS": icon = "FillTarget"; tint = Ui.Amber; break;
                case "QUESTS": icon = "FillPlanet"; tint = Ui.Soft; break;
                default: icon = "FillTrophy"; tint = Ui.Gold; break;
            }
        }

        void ShowAchievements(List<object> earned)
        {
            if (earned.Count == 0) { Put(_achievements, "Achievements", Empty("No achievements yet. Your first few putts will earn one.")); return; }
            _achievements.Clear();
            _achievements.Add(_member == null ? Ui.Section("Achievements", "See all", () => _app.Push(new AchievementsScreen(_app))) : Ui.Section("Achievements"));
            // Flat crests in a row that scrolls sideways.
            ScrollView crests = Ui.Scroll(true);
            crests.AddToClassList("pf-crests");
            for (int i = 0; i < Mathf.Min(earned.Count, 20); i++)
            {
                Look(J.Str(earned[i], "aType"), out string icon, out Color tint);
                crests.Add(Ui.Box(i == 0 ? "pf-coin-item pf-first" : "pf-coin-item", new Coin(icon, tint, 0f),
                    Ui.Text(J.Str(earned[i], "title"), "pf-coin-title", Ui.Bold), Ui.Text(J.Str(earned[i], "description"), "pf-coin-desc", Ui.Regular)));
            }
            _achievements.Add(crests);
        }

        // A card a challenge: its title with the time left, then how far along (or Join).
        void ShowChallenges(List<object> rows)
        {
            if (_member != null) { _challenges.Clear(); return; }
            if (rows.Count == 0) { Put(_challenges, "Challenges", Empty("No active challenges right now.")); return; }
            VisualElement cards = Ui.Box("pf-challenges");
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < Mathf.Min(rows.Count, 3); i++)
            {
                object row = rows[i], challenge = J.Get(row, "challenge");
                List<object> levels = J.List(J.Get(row, "challengeLevels"));
                bool joined = Cards.IsJoined(challenge);
                double score = J.Get(row, "userResults") != null ? J.Num(row, "userResults") : J.Num(challenge, "currentScore");
                int percent = ChallengeMath.Percent(score, levels, out _, out double goal);

                DateTime? end = ProfileMath.Date(challenge, "endDate");
                bool ended = end != null && (end.Value - now).TotalSeconds <= 0;
                string left = end == null ? "" : (end.Value - now).TotalDays >= 365 ? "365+ days left" : ProfileMath.TimeLeft(end, now);

                VisualElement top = Ui.Box("row", Ui.Text(J.Str(challenge, "title", "Challenge"), "pf-row-title grow pf-wrap", Ui.Bold));
                if (left.Length > 0)
                {
                    Label when = Ui.Caps(left, "caps pf-row-tag");
                    when.style.color = ended ? Ui.Faint : Ui.Amber;
                    top.Add(when);
                }
                VisualElement card = Ui.Box(i == 0 ? "pf-card pf-card-pad pf-challenge pf-first" : "pf-card pf-card-pad pf-challenge", top);
                if (joined && levels.Count > 0)
                {
                    card.Add(Meter(percent / 100f));
                    card.Add(Ui.Text(ProfileMath.Number(score) + " of " + ProfileMath.Number(goal), "pf-row-detail pf-meter-line", Ui.Regular));
                }
                else card.Add(Ui.Caps("Join", "caps caps-green pf-join"));
                cards.Add(card.OnTap(() => _app.Push(new ChallengeScreen(_app, row))));
            }
            _challenges.Clear();
            _challenges.Add(Ui.Section("Challenges", "See all", () => _app.Push(new ChallengesScreen(_app))));
            _challenges.Add(Ui.Box("pad", cards));
        }

        // ── Last 30 days and putts by month ──────────────────────────────────

        void ShowActivity(object stats)
        {
            Dictionary<string, int> calendar = ProfileMath.Calendar(stats);
            int days = J.Int(stats, "active_days_last_30"), before = J.Int(stats, "active_days_prev_30"), change = days - before;
            DateTime today = DateTime.Now.Date;

            if (calendar.Count == 0) { Put(_activity, "Last 30 days", Empty("No putts in the last two months.")); _months.Clear(); return; }
            Label trend = Ui.Text((change > 0 ? "+" : "") + change + " vs the 30 days before", "pf-trend", Ui.Medium);
            trend.style.color = change > 0 ? Ui.Bright : change < 0 ? Ui.Red : Ui.Faint;
            VisualElement head = Ui.Box("pf-act-head", Ui.Num(days.ToString(), "num-44 pf-act-number"), Ui.Box("grow", Ui.Caps("Active days"), trend));
            Put(_activity, "Last 30 days", Card(head, new DayCalendar(calendar, today)).Pad());

            Put(_months, "Putts by month", Card(new Ribbon(ProfileMath.ByMonth(calendar, today))));
        }

        // ── Make % ───────────────────────────────────────────────────────────

        // A lane a distance putted: the distance large on the left, then three bars each with its percentage in
        // front: You (brand green), PGA Tour (text-2) and all Puttr Players (surface-3). The bars grow in.
        static readonly Color PgaColour = Ui.Faint, PuttrColour = Surface3;

        void ShowDistance()
        {
            if (_mine == null) return;
            if (_mine.Count == 0) { Put(_distance, "Make % by distance", Empty("No putts yet!")); return; }
            VisualElement card = Card(Ui.Box("row pf-legend", Key("You", Ui.Green), Key("PGA Tour", PgaColour), Key("Puttr Players", PuttrColour))).Pad();
            VisualElement lanes = Ui.Box("pf-lanes");
            int n = 0;
            foreach (object row in _mine)
            {
                int feet = J.Int(row, "distance");
                if (feet < 3 || feet > 11 || J.Num(row, "total") <= 0) continue;
                int you = (int)Math.Round(J.Num(row, "made") / J.Num(row, "total") * 100.0);
                int puttr = 0;
                foreach (object all in _puttr ?? new List<object>()) if (J.Int(all, "distance") == feet && J.Num(all, "total") > 0) puttr = (int)Math.Round(J.Num(all, "made") / J.Num(all, "total") * 100.0);
                float delay = n * 0.06f;
                VisualElement bars = Ui.Box("grow pf-bars", Bar(you, null, true, delay), Bar(ProfileMath.Pga[feet - 3], PgaColour, false, delay + 0.08f));
                if (puttr > 0) bars.Add(Bar(puttr, PuttrColour, false, delay + 0.16f));
                lanes.Add(Ui.Box(n == 0 ? "pf-lane pf-lane-first" : "pf-lane", Ui.Box("pf-feet", Ui.Num(feet.ToString(), "num-20"), Ui.Caps("ft")), bars));
                n++;
            }
            card.Add(lanes);
            Put(_distance, "Make % by distance", card, "Last 90 days");
        }

        static VisualElement Key(string name, Color colour)
        {
            VisualElement dot = Ui.Box("pf-key-dot");
            dot.style.backgroundColor = colour;
            return Ui.Box("row pf-key", dot, Ui.Caps(name));
        }

        static VisualElement Bar(int percent, Color? colour, bool you, float delay)
        {
            VisualElement fill = Ui.Box("pf-bar-fill");
            fill.style.backgroundColor = you ? Ui.Green : colour ?? PgaColour;
            fill.style.width = Length.Percent(0);
            fill.schedule.Execute(() => fill.style.width = Length.Percent(Mathf.Clamp(percent, 0, 100))).StartingIn((long)(150 + delay * 1000));
            return Ui.Box(you ? "row pf-bar pf-bar-you" : "row pf-bar", Ui.Text(percent + "%", "pf-bar-value", Ui.Bold), Ui.Box("grow pf-bar-track", fill));
        }

        void ShowBreaks(List<object> rows)
        {
            int[] percent = ProfileMath.Breaks(rows);
            if (percent[0] < 0 && percent[1] < 0 && percent[2] < 0) { _breaks.Clear(); return; }
            // Breaks.js's three tees, side by side: the make %, the tee, and a bar. Under 40% reads red.
            VisualElement tees = Ui.Box("pf-breaks");
            for (int i = 0; i < 3; i++)
            {
                Label value = Ui.Num(percent[i] < 0 ? "–" : percent[i] + "%");
                if (percent[i] >= 0 && percent[i] < 40) value.style.color = Ui.Red;
                tees.Add(Ui.Box(i == 0 ? "pf-break pf-first" : "pf-break", value, Ui.Caps(ProfileMath.BreakNames[i], "caps pf-break-name"), Meter(Mathf.Max(0, percent[i]) / 100f)));
            }
            Put(_breaks, "Make % by break", Card(tees).Pad(), "Last 90 days");
        }

        // ── Competition, bests, quests (GET /profile/summary/) ───────────────

        void ShowSummary(object summary)
        {
            if (_member != null) { ShowMemberSummary(summary); return; }
            object league = J.Get(summary, "league"), tournaments = J.Get(summary, "tournaments"), versus = J.Get(summary, "versus"), quests = J.Get(summary, "quests");
            bool mine = _member == null;
            VisualElement compete = Card().Pad();
            compete.Add(Row("UsersThree", "League Night", league != null
                ? J.Str(league, "team", "Your team") + "  ·  " + J.Int(league, "weeksPlayed") + " weeks played"
                : "Six teams. One trophy. Join a team to play.", mine ? Chevron() : null));
            int played = J.Int(tournaments, "played");
            compete.Add(Row("Trophy", "Tournaments", played > 0
                ? played + " played" + (J.Get(tournaments, "bestFinish") != null ? "  ·  best finish " + Place(J.Int(tournaments, "bestFinish")) : "")
                : "None played yet. Find one to enter.", mine ? Chevron() : null));
            int total = J.Int(versus, "total");
            compete.Add(Row("User", "Versus", total > 0
                ? J.Int(versus, "won") + " won  ·  " + J.Int(versus, "lost") + " lost" + (J.Int(versus, "open") > 0 ? "  ·  " + J.Int(versus, "open") + " your turn" : "")
                : "Challenge a friend to a head to head.", mine ? Chevron() : null));
            First(compete[0]);
            if (mine)
            {
                compete[0].OnTap(() => _app.Push(new LeagueScreen(_app)));
                compete[1].OnTap(() => _app.Push(new TournamentsScreen(_app)));
                compete[2].OnTap(() => _app.Push(new VersusInboxScreen(_app)));
            }
            Put(_compete, "Competition", compete);

            List<object> bests = J.List(J.Get(summary, "bests"));
            if (bests.Count == 0) _bests.Clear();
            else
            {
                VisualElement card = Card().Pad();
                foreach (object best in bests)
                {
                    DateTime? at = ProfileMath.Date(best, "bestAt");
                    int plays = J.Int(best, "plays");
                    string detail = (at != null ? at.Value.ToString("MMM d", CultureInfo.InvariantCulture) + "  ·  " : "") + plays + (plays == 1 ? " play" : " plays");
                    card.Add(Row("Trophy", J.Str(J.Get(best, "game"), "title", "Game"), detail,
                        Ui.Num(J.Get(best, "best") != null ? J.Num(best, "best").ToString("0.#", CultureInfo.InvariantCulture) : "–", "num-20 pf-gold"), Ui.Gold));
                }
                First(card[0]);
                Put(_bests, "Personal bests", card);
            }

            int stars = J.Int(quests, "stars"), max = Mathf.Max(1, J.Int(quests, "maxStars"));
            VisualElement words = Ui.Box("grow");
            if (stars == 0) words.Add(Ui.Text("Earn up to three stars a level. Play your first quest to start.", "sub", Ui.Regular));
            else
            {
                words.Add(Ui.Text(ProfileMath.Number(stars) + " of " + ProfileMath.Number(max) + " stars", "pf-row-title", Ui.Bold));
                words.Add(Meter(stars / (float)max));
                string next = J.Str(J.Get(quests, "nextQuest"), "title");
                words.Add(Ui.Text(J.Int(quests, "questsComplete") + " of " + J.Int(quests, "questsTotal") + " quests complete" + (next.Length > 0 ? "  ·  next up: " + next : ""), "pf-row-detail pf-meter-line", Ui.Regular));
            }
            VisualElement questCard = Card(Ui.Box("row", words, mine ? Chevron() : null)).Pad();
            Put(_quests, "Quest stars", mine ? questCard.OnTap(() => _app.Push(new GreenlandsScreen(_app))) : questCard);
        }

        static string Place(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");

        // ── Another Player: GET /members/summary/:userId (ProfileSummaryCards, the member rules) ──

        /// <summary>ProfileSummaryCards' score: whole numbers as they are, anything else to one place; none is a dash.</summary>
        internal static string Score(object row, string key)
        {
            if (J.Get(row, key) == null) return "–";
            double v = J.Num(row, key);
            return v % 1 == 0 ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>MemberOverview's firstName: their first name, else the first word of their display name.</summary>
        string FirstName()
        {
            string first = Clean(J.Str(_profile, "firstName"));
            if (first.Length == 0) { string shown = J.Str(_profile, "displayName").Trim(); int space = shown.IndexOf(' '); first = space > 0 ? shown.Substring(0, space) : shown; }
            return first;
        }

        static Label Lit(string text, string classes, Font font, Color colour) { Label l = Ui.Text(text, classes, font); l.style.color = colour; return l; }

        /// <summary>
        /// Another Player's summary, by the member rule: never show zeros (an empty row hides, an all-empty card hides).
        /// Head to head with Challenge to Versus, Competition (each row opens its place), Quest stars and Personal bests
        /// with Beat it or Play.
        /// </summary>
        void ShowMemberSummary(object summary)
        {
            string first = FirstName();
            object tournaments = J.Get(summary, "tournaments"), versus = J.Get(summary, "versus"), quests = J.Get(summary, "quests"), league = J.Get(summary, "league");
            List<object> bests = J.List(J.Get(summary, "bests"));

            // Head to head: make % over 90 days, the first best you share, and your Versus record with them.
            _h2h.Clear();
            object h = J.Get(summary, "headToHead");
            if (h != null)
            {
                object you = J.Get(h, "viewer"), them = J.Get(h, "target");
                object shared = bests.Find(b => J.Get(b, "viewerBest") != null);
                object vs = J.Get(versus, "vsViewer");
                bool hasVersus = vs != null && J.Int(vs, "targetWon") + J.Int(vs, "viewerWon") > 0;
                bool hasMake = J.Get(you, "makePct") != null || J.Get(them, "makePct") != null;
                if (hasMake || shared != null || hasVersus)
                {
                    VisualElement card = Card(Ui.Text("You vs " + (first.Length > 0 ? first : "them") + " · last 90 days", "pf-row-detail", Ui.Regular),
                        Ui.Box("h2h-row h2h-names", Ui.Text("YOU", "h2h-label", Ui.Medium), Ui.Box("h2h-mid"), Ui.Text((first.Length > 0 ? first : "Them").ToUpperInvariant(), "h2h-label", Ui.Medium))).Pad();
                    void Line(string label, string a, string b, bool aWins, bool bWins) =>
                        card.Add(Ui.Box("h2h-row h2h-line", Lit(a, "h2h-cell", Ui.Bold, aWins ? Ui.Bright : Color.white), Ui.Text(label.ToUpperInvariant(), "h2h-label h2h-mid", Ui.Medium), Lit(b, "h2h-cell", Ui.Bold, bWins ? Ui.Bright : Color.white)));
                    if (hasMake)
                    {
                        bool ha = J.Get(you, "makePct") != null, hb = J.Get(them, "makePct") != null;
                        double a = J.Num(you, "makePct"), b = J.Num(them, "makePct");
                        Line("Make %", ha ? a.ToString("0", CultureInfo.InvariantCulture) + "%" : "–", hb ? b.ToString("0", CultureInfo.InvariantCulture) + "%" : "–", ha && (!hb || a > b), hb && (!ha || b > a));
                    }
                    if (shared != null)
                    {
                        double a = J.Num(shared, "viewerBest"), b = J.Num(shared, "best");
                        Line("Best " + J.Str(J.Get(shared, "game"), "title", "game"), Score(shared, "viewerBest"), Score(shared, "best"), a > b, b > a);
                    }
                    if (hasVersus)
                    {
                        int a = J.Int(vs, "viewerWon"), b = J.Int(vs, "targetWon");
                        Line("Versus wins", a.ToString(), b.ToString(), a > b, b > a);
                    }
                    // Secondary: Follow is this page's one main button.
                    if (!Kid.IsKid(_app.Profile))
                    {
                        VisualElement versusButton = Ui.Secondary("Challenge to Versus", ChallengeToVersus);
                        versusButton.AddToClassList("pf-h2h-btn");
                        card.Add(versusButton);
                    }
                    Put(_h2h, "Head to head", card);
                }
            }

            // Competition: only what they have played, each row opening that competition.
            VisualElement compete = Card().Pad();
            if (league != null)
            {
                League.Team team = League.TeamOf(J.Str(league, "team"));
                int played = J.Int(league, "weeksPlayed"), total = J.Int(league, "weeksTotal");
                string weeks = total > 0 ? played + " of " + total + " weeks" : played + (played == 1 ? " week" : " weeks");
                compete.Add(Row("UsersThree", "League Night · " + (team != null ? team.Name : J.Str(league, "team")),
                    "Season " + J.Int(league, "seasonNumber") + " · " + weeks + " played" + (J.Get(league, "best") != null ? " · best " + Score(league, "best") : ""), Chevron())
                    .OnTap(() => _app.Push(new LeagueScreen(_app))));
            }
            if (J.Int(tournaments, "played") > 0)
            {
                var bits = new List<string> { J.Int(tournaments, "played") + " played" };
                if (J.Int(tournaments, "bestFinish") > 0) bits.Add("best finish " + Place(J.Int(tournaments, "bestFinish")));
                if (J.Int(tournaments, "bestRound") > 0) bits.Add("best round " + J.Int(tournaments, "bestRound"));
                compete.Add(Row("Trophy", "Tournaments", string.Join(" · ", bits), Chevron()).OnTap(() => _app.Push(new TournamentsScreen(_app))));
            }
            if (J.Int(versus, "total") > 0)
            {
                var bits = new List<string> { J.Int(versus, "won") + "–" + J.Int(versus, "lost") };
                if (J.Int(versus, "open") > 0) bits.Add(J.Int(versus, "open") + " open");
                object vs = J.Get(versus, "vsViewer");
                if (vs != null && J.Int(vs, "targetWon") + J.Int(vs, "viewerWon") > 0) bits.Add(J.Int(vs, "targetWon") + "–" + J.Int(vs, "viewerWon") + " against you");
                compete.Add(Row("User", "Versus record", string.Join(" · ", bits), Chevron()).OnTap(() => _app.Push(new VersusInboxScreen(_app))));
            }
            if (compete.childCount == 0) _compete.Clear();
            else { First(compete[0]); Put(_compete, "Competition", compete); }

            // Quest stars: hidden at none; a tap opens Quests.
            int stars = J.Int(quests, "stars"), max = Mathf.Max(1, J.Int(quests, "maxStars"));
            if (stars == 0) _quests.Clear();
            else
            {
                VisualElement words = Ui.Box("grow",
                    Ui.Text(ProfileMath.Number(stars) + " of " + ProfileMath.Number(max) + " stars", "pf-row-title", Ui.Bold),
                    Meter(stars / (float)max),
                    Ui.Text(J.Int(quests, "questsComplete") + " of " + J.Int(quests, "questsTotal") + " quests complete", "pf-row-detail pf-meter-line", Ui.Regular));
                Put(_quests, "Quest stars", Card(Ui.Box("row", words, Chevron())).Pad().OnTap(() => _app.Push(new GreenlandsScreen(_app))));
            }

            // Personal bests: their top score on the games they play most, with yours beside it; Beat it plays it with theirs as the target.
            if (bests.Count == 0) { _bests.Clear(); return; }
            VisualElement list = Card(Ui.Text("Tap Beat it to play with " + (first.Length > 0 ? first : "their") + " score as the target", "pf-row-detail", Ui.Regular)).Pad();
            foreach (object best in bests)
            {
                object b = best;
                bool youLead = J.Get(best, "viewerBest") != null && J.Num(best, "viewerBest") >= J.Num(best, "best");
                VisualElement detail = Ui.Box("pf-best-line", Ui.Text(Score(best, "best") + (J.Get(best, "viewerBest") != null ? " · yours " + Score(best, "viewerBest") : ""), "pf-row-detail", Ui.Regular));
                if (youLead) detail.Add(Lit("you lead", "pf-row-detail pf-best-lead", Ui.Bold, Ui.Bright));
                VisualElement go = Ui.Box("pf-beat", Ui.Text(youLead ? "PLAY" : "BEAT IT", "pf-beat-t", Ui.Bold)).OnTap(() => BeatIt(b, first));
                list.Add(Ui.Box("pf-row pf-best", Ui.Box("grow", Ui.Text(J.Str(J.Get(best, "game"), "title", "Game"), "pf-row-title", Ui.Bold), detail), go));
            }
            Put(_bests, "Personal bests", list);
        }

        /// <summary>Head to head's Challenge to Versus: the Versus flow with this Player already picked.</summary>
        void ChallengeToVersus() => Club.Require(_app, "Versus", () => _app.Push(new VersusPickerScreen(_app, _member, _profile)));

        /// <summary>
        /// Beat it (MemberOverview beatBest): that game's page with their best as the target, as GameIntro shows the
        /// targetScore it is given ("Target: beat Sam 31.1"). The target is shown, not scored against, as in the app.
        /// </summary>
        void BeatIt(object best, string first)
        {
            object game = J.Get(best, "game");
            if (J.Str(game, "gameType").Length == 0) return;
            if (J.Str(game, "gameType") == "TRACKS") { _app.Push(new TracksCoursesScreen(_app)); return; }
            var screen = new GameScreen(_app, game);
            _app.Push(screen);
            if (J.Get(best, "best") != null) GameTarget.Show(screen.Root, (first.Length > 0 ? first : "their") + " " + Score(best, "best"));
        }

        // ── Another Player: the Putt Quality Score (members/PuttScore.js) ─────

        void ShowPqs(List<object> putts)
        {
            PqsModel model = PqsModel.Of(putts, DateTime.Now);
            Put(_pqs, "Putt Quality Score", new PqsCard(model, Root).Root, "?", () => PqsCard.Help(Root));
        }
    }

    /// <summary>
    /// Fist bumps (GET /members/fistbump/:userId: a push and an inbox message to that Player). The app locks a bumped
    /// Player's button until the screen reloads; the API keeps no record the sender can read back, so the Players
    /// bumped are remembered here for the rest of the day (on this device, for this account), and every leaderboard,
    /// tournament and profile shows the same state.
    /// </summary>
    public static class FistBumps
    {
        static HashSet<string> _sent;
        static string _loadedFor;

        static string Key => "app2.fistbumps." + Auth.Uid;
        static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        static HashSet<string> Sent
        {
            get
            {
                string who = Auth.Uid + "|" + Today;
                if (_sent != null && _loadedFor == who) return _sent;
                _loadedFor = who;
                _sent = new HashSet<string>();
                // Stored as "day|id,id,..."; another day's list starts empty.
                string saved = PlayerPrefs.GetString(Key, "");
                int bar = saved.IndexOf('|');
                if (bar > 0 && saved.Substring(0, bar) == Today)
                    foreach (string id in saved.Substring(bar + 1).Split(',')) if (id.Length > 0) _sent.Add(id);
                return _sent;
            }
        }

        static void Save() { PlayerPrefs.SetString(Key, Today + "|" + string.Join(",", Sent)); PlayerPrefs.Save(); }

        public static bool Bumped(string userId) => !string.IsNullOrEmpty(userId) && Sent.Contains(userId);

        /// <summary>Whether this signed-in profile may bump that Player: never yourself, a guest, or from a Kid profile (the API refuses it).</summary>
        public static bool CanBump(App app, string userId) =>
            !string.IsNullOrEmpty(userId) && userId != "0" && userId != J.Str(app?.Profile, "userId", Auth.Uid) && !Kid.IsKid(app?.Profile) && !Tv.On;

        /// <summary>Sends one, at once marked as sent; put back (and said) if the call fails.</summary>
        public static void Send(App app, string userId, Action changed)
        {
            if (string.IsNullOrEmpty(userId) || Bumped(userId)) return;
            Sent.Add(userId);
            Save();
            Haptics.Medium();
            changed?.Invoke();
            app?.Toast("Fist bump sent");
            App.Run(Api.Get("/members/fistbump/" + userId, _ => { }, _ =>
            {
                Sent.Remove(userId);
                Save();
                changed?.Invoke();
                app?.Toast("Could not send fist bump. Please try again.");
            }));
        }

        /// <summary>LeaderRow's small square fist-bump button, left of the score; it locks to the green "sent" look once tapped.</summary>
        public static VisualElement Button(App app, string userId)
        {
            VisualElement button = Ui.Box("");
            button.style.width = button.style.height = 32;
            button.style.flexShrink = 0;
            button.style.marginRight = 8;
            button.style.marginLeft = 8;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius = button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 4;
            button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 1;
            void Look()
            {
                bool on = Bumped(userId);
                Color bg = on ? Ui.Surface : Ui.Surface2;
                Color edge = on ? Ui.Green : Ui.Line;
                button.style.backgroundColor = bg;
                button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = edge;
                button.Clear();
                button.Add(new FistGlyph(on ? Ui.Bright : Color.white, bg));
            }
            Look();
            button.OnTap(() => { if (!Bumped(userId)) Send(app, userId, Look); });
            return Page.Swallow(button);
        }
    }

    /// <summary>
    /// The target a game's page was opened with (GameIntro's targetScore from a profile's Beat it): a green pill under
    /// the game's description, "Target: beat Sam 31.1". Placed into GameScreen's column from outside it, so the page
    /// itself needs no change; styled inline since the game page does not load Profile.uss.
    /// </summary>
    public static class GameTarget
    {
        public static void Show(VisualElement root, string words)
        {
            VisualElement column = root?.Q(className: "game-column");
            if (column == null) return;
            VisualElement pill = Ui.Box("", Ui.Icon("Trophy", 16, Ui.Bright), Ui.Text("Target: beat " + words, "", Ui.Bold));
            pill.style.flexDirection = FlexDirection.Row;
            pill.style.alignItems = Align.Center;
            pill.style.alignSelf = Align.FlexStart;
            pill.style.marginTop = 12;
            pill.style.paddingLeft = pill.style.paddingRight = 12;
            pill.style.paddingTop = pill.style.paddingBottom = 8;
            pill.style.backgroundColor = Ui.Surface;
            pill.style.borderTopLeftRadius = pill.style.borderTopRightRadius = pill.style.borderBottomLeftRadius = pill.style.borderBottomRightRadius = 4;
            Label text = (Label)pill[1];
            text.style.fontSize = 13;
            text.style.color = Ui.Bright;
            text.style.marginLeft = 8;
            VisualElement about = column.Q(className: "game-about");
            int at = about != null ? column.IndexOf(about) + 1 : Mathf.Min(1, column.childCount);
            column.Insert(at, pill);
        }
    }

    /// <summary>A fist from the front (the app's 👊), drawn: four knuckles over the palm, the thumb folded across.</summary>
    public sealed class FistGlyph : VisualElement
    {
        readonly Color _colour, _gap;

        public FistGlyph(Color colour, Color gap)
        {
            _colour = colour;
            _gap = gap;
            style.width = style.height = 16;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            float s = Mathf.Min(contentRect.width, contentRect.height) / 16f;
            if (s <= 0) return;
            Painter2D p = context.painter2D;
            p.fillColor = _colour;
            for (int i = 0; i < 4; i++) { ProfileTex.RoundRect(p, new Rect((2f + i * 3f) * s, 2.5f * s, 3f * s, 6f * s), 1.5f * s); p.Fill(); }
            ProfileTex.RoundRect(p, new Rect(2f * s, 6f * s, 12f * s, 8.5f * s), 2.2f * s);
            p.Fill();
            // The gaps between the fingers and the line of the folded thumb, in the button's own colour.
            p.strokeColor = _gap;
            p.lineWidth = 0.9f * s;
            p.lineCap = LineCap.Round;
            for (int i = 1; i < 4; i++) { p.BeginPath(); p.MoveTo(new Vector2((2f + i * 3f) * s, 3f * s)); p.LineTo(new Vector2((2f + i * 3f) * s, 7.5f * s)); p.Stroke(); }
            p.BeginPath(); p.MoveTo(new Vector2(3f * s, 9.8f * s)); p.LineTo(new Vector2(9.5f * s, 9.8f * s)); p.LineTo(new Vector2(11f * s, 8.4f * s)); p.Stroke();
        }
    }

    static class ProfileCards
    {
        /// <summary>A card with padding inside (the calendar and the ribbon draw to the card's edge instead).</summary>
        public static VisualElement Pad(this VisualElement card) { card.AddToClassList("pf-card-pad"); return card; }
    }



    /// <summary>A ring that fills to a percentage, with the number in the middle. Below 0 means nothing to show.</summary>
    public sealed class Ring : VisualElement
    {
        readonly int _percent;

        public Ring(int percent)
        {
            _percent = percent;
            AddToClassList("profile-ring"); // not "ring": a later App.uss rule makes .ring a 54px bordered play button
            Add(Ui.Text(percent < 0 ? "–" : percent + "%", "ring-value", Ui.Bold));
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            float size = Mathf.Min(contentRect.width, contentRect.height);
            if (size <= 0f) return;
            Vector2 centre = contentRect.center;
            float width = Mathf.Max(5f, size * 0.08f), radius = size * 0.5f - width * 0.5f;
            Painter2D p = context.painter2D;
            p.lineWidth = width;
            p.lineCap = LineCap.Round;
            p.strokeColor = Ui.Surface2;
            p.BeginPath(); p.Arc(centre, radius, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            if (_percent <= 0) return;
            p.strokeColor = _percent >= 60 ? Ui.Bright : _percent >= 40 ? Ui.Amber : Ui.Red;
            p.BeginPath(); p.Arc(centre, radius, Angle.Degrees(-90), Angle.Degrees(-90 + 360f * Mathf.Clamp01(_percent / 100f))); p.Stroke();
        }
    }
}
