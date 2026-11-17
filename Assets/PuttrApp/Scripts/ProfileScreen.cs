// The Profile tab, from the live API, card for card as puttr-app's profile/UserOverview.js lays
// it out: who you are, your membership and putter, all-time numbers, achievements, challenges,
// the last 30 days, putts by month, make % by distance and by break, competition, personal bests
// and quest stars.
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
    /// Your profile (the Profile tab), or another Player's (puttr-app MemberOverview: same cards, read from
    /// /members/*/:userId, with Follow and Fist bump in place of your own membership and putter rows).
    /// </summary>
    public sealed class ProfileScreen : AppScreen
    {
        readonly App _app;
        readonly string _member; // null: the signed-in Player
        List<object> _following;
        bool _bumped, _busy;
        readonly VisualElement _who, _all, _achievements, _challenges, _activity, _months, _distance, _breaks, _compete, _bests, _quests;
        object _profile, _putter;
        List<object> _mine, _puttr;
        float _loadedAt = -100f;

        public ProfileScreen(App app) : this(app, null, null) { }

        /// <summary>Another Player's profile. seed: whatever row was tapped (a name and photo to show at once).</summary>
        public static void Open(App app, string userId, object seed = null)
        {
            if (string.IsNullOrEmpty(userId)) { app.Toast("That Player played as a guest."); return; }
            if (userId == J.Str(app.Profile, "userId")) { app.ShowTabs("profile"); return; }
            app.Push(new ProfileScreen(app, userId, seed));
        }

        ProfileScreen(App app, string member, object seed)
        {
            _app = app;
            _member = member;
            _profile = seed;
            _who = Ui.Box("glass");
            _all = Slot(); _achievements = Slot(); _challenges = Slot(); _activity = Slot(); _months = Slot();
            _distance = Slot(); _breaks = Slot(); _compete = Slot(); _bests = Slot(); _quests = Slot();
            ScrollView scroll = Ui.Scroll();
            // The bell and the gear: notifications, and Settings (which also holds the developer switches and Sign out).
            if (member != null)
            {
                scroll.Add(Ui.Header("Profile", app.Pop));
                scroll.Add(Ui.Box("pad", _who));
            }
            else
                scroll.Add(Ui.Box("pad", Ui.Box("profile-top", Ui.Box("grow"), Inbox.Bell(app),
                    Ui.Box("head-icon", Ui.Icon("GearSix", 24, Color.white)).OnTap(() => app.Push(new SettingsScreen(app)))), _who));
            if (seed != null) { ShowWho(); }
            // Two columns on a tablet (App.Layout .wide); the strips that scroll sideways stay full width.
            VisualElement cols = Ui.Box("cols");
            foreach (VisualElement slot in new[] { _activity, _months, _distance, _breaks, _compete, _bests, _quests }) { slot.AddToClassList("col"); cols.Add(slot); }
            scroll.Add(_all);
            scroll.Add(_achievements);
            scroll.Add(_challenges);
            scroll.Add(cols);
            var foot = new VisualElement();
            foot.style.height = 24;
            scroll.Add(foot);
            Root = scroll;
        }

        static VisualElement Slot() => new VisualElement();
        // As the app does, the numbers are read again whenever the tab comes to the front (but not twice in a few seconds).
        public override void Shown()
        {
            if (_member != null) { ShownMember(); return; }
            if (_app.Profile != null && _profile == null) { _profile = _app.Profile; ShowWho(); ShowAllTime(); }
            if (Time.realtimeSinceStartup - _loadedAt < 20f) return;
            _loadedAt = Time.realtimeSinceStartup;
            App.Run(Api.Get("/profile/", data => { _profile = _app.Profile = J.First(data); ShowWho(); ShowAllTime(); }, Failed));
            App.Run(Api.Get("/user-putters", data => { List<object> putters = J.List(data); _putter = putters.Count > 0 ? putters[0] : null; ShowWho(); }));
            App.Run(Api.Get("/profile/userStats", data => ShowActivity(J.First(data))));
            App.Run(Api.Get("/profile/achievements/recent", data => ShowAchievements(J.List(data))));
            App.Run(Api.Get("/profile/putts/90", data => { _mine = J.List(data); ShowDistance(); }));
            App.Run(Api.Get("/members/analytics/puttsByDistance", data => { _puttr = J.List(data); ShowDistance(); }));
            App.Run(Api.Get("/profile/breaks/90", data => ShowBreaks(J.List(data))));
            App.Run(Api.Get("/challenges/userChallenges", data => ShowChallenges(J.List(data))));
            App.Run(Api.Get("/profile/summary/", ShowSummary));
            Inbox.Load();
        }

        void ShownMember()
        {
            if (Time.realtimeSinceStartup - _loadedAt < 20f) return;
            _loadedAt = Time.realtimeSinceStartup;
            string id = "/" + _member;
            App.Run(Api.Get("/profile/following/", data => { _following = J.List(data); ShowWho(); }));
            App.Run(Api.Get("/members/profile" + id, data => { object p = J.First(data); if (p != null) _profile = p; ShowWho(); ShowAllTime(); }));
            App.Run(Api.Get("/members/userStats" + id, data => ShowActivity(J.First(data))));
            App.Run(Api.Get("/members/achievements/recent" + id, data => ShowAchievements(J.List(data))));
            App.Run(Api.Get("/members/putts" + id, data => { _mine = J.List(data); ShowDistance(); }));
            App.Run(Api.Get("/members/analytics/puttsByDistance", data => { _puttr = J.List(data); ShowDistance(); }));
            App.Run(Api.Get("/members/breaks" + id, data => ShowBreaks(J.List(data))));
            App.Run(Api.Get("/members/summary" + id, ShowSummary));
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

        void Bump()
        {
            if (_bumped) return;
            _bumped = true;
            ShowWho();
            App.Run(Api.Get("/members/fistbump/" + _member, _ => { }, _ => { _bumped = false; ShowWho(); _app.Toast("Could not send fist bump."); }));
        }

        void Failed(string why)
        {
            if (why == "signed_out") _app.SignedOut();
        }

        static void Fill(VisualElement slot, string title, VisualElement card, string link = null)
        {
            slot.Clear();
            if (card == null) return;
            slot.Add(Ui.Section(title, link));
            slot.Add(Ui.Box("pad", card));
        }

        static VisualElement Row(string icon, Color tint, string title, string detail, VisualElement trailing = null)
        {
            VisualElement words = Ui.Box("grow", Ui.Text(title, "feed-name", Ui.Bold));
            if (!string.IsNullOrEmpty(detail)) words.Add(Ui.Text(detail, "muted", Ui.Regular));
            VisualElement bubble = Ui.Bubble(icon, tint);
            bubble.style.marginRight = 12;
            return Ui.Box("profile-row", bubble, words, trailing);
        }

        // ── Who ──────────────────────────────────────────────────────────────

        void ShowWho()
        {
            if (_profile == null) return;
            string name = J.Str(_profile, "displayName", "Player"), town = J.Str(_profile, "homeTown"), bio = J.Str(_profile, "bio");
            bool member = J.Flag(_profile, "isMember");
            _who.Clear();

            VisualElement avatar = Ui.Avatar(name, J.Str(_profile, "photoURL"), true);
            if (member) avatar.AddToClassList("avatar-member");
            VisualElement words = Ui.Box("grow", Ui.Text(name, "h2", Ui.Bold));
            if (town.Length > 0) words.Add(Ui.Text(town, "sub", Ui.Regular));
            DateTime? joined = ProfileMath.Date(_profile, "created_at");
            if (joined != null) words.Add(Ui.Text("Member since " + joined.Value.ToString("MMM yyyy", CultureInfo.InvariantCulture), "muted", Ui.Regular));
            _who.Add(Ui.Box("row", avatar, words));
            if (bio.Length > 0) _who.Add(Ui.Text(bio, "sub", Ui.Regular).Margin(10));

            if (_member != null)
            {
                // Follow, and once you follow them a fist bump; then their counts and handicap.
                if (_following != null)
                {
                    VisualElement buttons = Ui.Box("row", Ui.Button(Following ? "Following" : "Follow", Follow, Following ? "btn btn-ghost grow" : "btn grow"));
                    if (Following) { buttons.Add(FreePuttScreen.Space(0).Width(10)); buttons.Add(Ui.Button(_bumped ? "Bumped" : "Fist bump", Bump, "btn btn-ghost grow")); }
                    _who.Add(buttons.Margin(12));
                }
                VisualElement theirs = Ui.Box("row",
                    FreePuttScreen.Stat(ProfileMath.Number(J.Num(_profile, "following")), "Following"),
                    FreePuttScreen.Stat(ProfileMath.Number(J.Num(_profile, "followers")), "Followers"),
                    FreePuttScreen.Stat(ProfileMath.Handicap(_profile), "Handicap"));
                theirs.AddToClassList("profile-counts");
                _who.Add(theirs);
                return;
            }

            // UserOverview: the actions sit in a row under the name. A Kid changes their character
            // instead of editing a profile; Switch profile shows for a managed player, or an owner
            // with players (the sheet it opens also holds Manage profiles); Admin only for isAdmin
            // (the API checks it again on every admin call).
            VisualElement actions = Ui.Box("profile-actions");
            if (Kid.IsKid(_profile))
                actions.Add(ActionLink("Change character", "profile-action-green", () => _app.Push(new CharacterScreen(_app,
                    new Dictionary<string, object> { { "userId", J.Str(_profile, "userId") }, { "name", J.Str(_profile, "firstName", name) } }, false))));
            else
                actions.Add(ActionLink("Edit profile", "profile-action-green", () => _app.Push(new EditProfileScreen(_app))));
            if (Profiles.HasSwitch(_profile)) actions.Add(ActionLink("Switch profile", "profile-action-blue", () => Profiles.Sheet(_app, Root)));
            if (Adm.IsAdmin(_profile)) actions.Add(ActionLink("Admin", "profile-action-grey", () => _app.Push(new AdminScreen(_app))));
            words.Add(actions);

            // Membership, putter and counts: compact rows under the name on a phone, a column of
            // their own beside it on a tablet (.wide .profile-head).
            VisualElement info = Ui.Box("profile-info");
            bool isMember = J.Flag(_profile, "isMember");
            VisualElement membership = Mini("Trophy", Ui.Bright, "Membership", ProfileMath.Membership(_profile, DateTime.UtcNow),
                isMember ? null : Ui.Text("Join now", "chip-join profile-join", Ui.Bold)).OnTap(() => _app.Push(new MembershipScreen(_app)));
            membership.AddToClassList("profile-mini-first"); // no rule above it when it heads its own column on a tablet
            info.Add(membership);
            string brand = J.Str(_putter, "brand").Trim();
            if (brand == "null") brand = "";
            info.Add(Mini("Target", Ui.Bright, brand.Length > 0 ? brand : "Add your putter", brand.Length > 0 ? ProfileMath.PutterDetail(_putter) : "Tell Puttr what you putt with")
                .OnTap(() => _app.Push(new PuttersScreen(_app))));
            // An owner with no other players yet: the way in to adding them (once there are, Switch profile above leads there).
            if (!Profiles.HasSwitch(_profile) && !Kid.Managed(_profile))
                info.Add(Mini("UsersThree", Ui.Blue, "Profiles", "Add profiles to share this device").OnTap(() => _app.Push(new ProfilesScreen(_app))));

            VisualElement counts = Ui.Box("row",
                FreePuttScreen.Stat(ProfileMath.Number(J.Num(_profile, "following")), "Following"),
                FreePuttScreen.Stat(ProfileMath.Number(J.Num(_profile, "followers")), "Followers"),
                FreePuttScreen.Stat(ProfileMath.Handicap(_profile), "Handicap"));
            counts.AddToClassList("profile-counts");
            counts[0].OnTap(() => _app.Push(new SocialScreen(_app, false)));
            counts[1].OnTap(() => _app.Push(new SocialScreen(_app, true)));
            info.Add(counts);

            // Re-seat the identity (avatar, name, bio) beside the info column.
            var identity = new List<VisualElement>();
            foreach (VisualElement child in _who.Children()) identity.Add(child);
            _who.Clear();
            VisualElement id = Ui.Box("profile-id");
            foreach (VisualElement child in identity) id.Add(child);
            _who.Add(Ui.Box("profile-head", id, info));
        }

        static VisualElement ActionLink(string label, string tone, Action onTap) =>
            Ui.Text(label, "profile-action " + tone, Ui.Medium).OnTap(onTap);

        /// <summary>A tight tappable row: small icon tile, title, one line under it, and a chevron or a call to action.</summary>
        static VisualElement Mini(string icon, Color tint, string title, string detail, VisualElement trailing = null)
        {
            VisualElement tile = Ui.Box("profile-mini-icon", Ui.Icon(icon, 16, tint));
            tile.style.backgroundColor = Color.Lerp(Ui.Ink, tint, 0.18f);
            VisualElement words = Ui.Box("grow profile-mini-words", Ui.Text(title, "profile-mini-title", Ui.Bold));
            if (!string.IsNullOrEmpty(detail)) words.Add(Ui.Text(detail, "profile-mini-detail", Ui.Regular));
            return Ui.Box("profile-mini", tile, words, trailing ?? Ui.Icon("CaretRight", 14, new Color(1f, 1f, 1f, 0.35f)));
        }

        void ShowAllTime()
        {
            if (_profile == null) return;
            // Two by two on a phone, one row of four on a tablet (.wide .profile-all).
            VisualElement grid = Ui.Box("glass");
            grid.Add(Ui.Box("profile-all",
                Ui.Box("row profile-all-pair",
                    FreePuttScreen.Stat(ProfileMath.Number(J.Num(_profile, "totalMade")), "Made (" + ProfileMath.MadePercent(_profile) + "%)"),
                    FreePuttScreen.Stat(ProfileMath.Number(J.Num(_profile, "totalPutts")), "Total putts")),
                Ui.Box("row profile-all-pair profile-all-second",
                    FreePuttScreen.Stat(ProfileMath.Number(J.Num(_profile, "totalFeet")), "Total feet"),
                    FreePuttScreen.Stat(ProfileMath.Number(J.Num(_profile, "totalGames")), "Games"))));
            grid.Add(Ui.Text("View recent games  ›", "sec-link center", Ui.Medium).Margin(14).OnTap(() => _app.Push(new GamesScreen(_app, _member))));
            Fill(_all, "All time", grid);
        }

        // ── Achievements and challenges ──────────────────────────────────────

        internal static void Look(string type, out string icon, out Color tint)
        {
            switch (type)
            {
                case "PUTTS": icon = "FillTarget"; tint = Ui.Green; break;
                case "DISTANCE": icon = "FillTrophy"; tint = Ui.Red; break;
                case "STREAK": icon = "FillFire"; tint = new Color32(0xF5, 0x9E, 0x0B, 0xFF); break;
                case "GAMES": icon = "FillJoystick"; tint = new Color32(0x9F, 0xB3, 0xBD, 0xFF); break;
                case "DAYS": icon = "FillTarget"; tint = new Color32(0xFF, 0x70, 0x43, 0xFF); break;
                case "QUESTS": icon = "FillPlanet"; tint = new Color32(0x4F, 0xC3, 0xF7, 0xFF); break;
                default: icon = "FillTrophy"; tint = new Color32(0xFF, 0xD7, 0x00, 0xFF); break;
            }
        }

        void ShowAchievements(List<object> earned)
        {
            if (earned.Count == 0) { Fill(_achievements, "Achievements", Ui.Box("glass", Ui.Text("No achievements yet. Your first few putts will earn one.", "sub", Ui.Regular))); return; }
            _achievements.Clear();
            _achievements.Add(_member == null ? Ui.Section("Achievements", "See all", () => _app.Push(new AchievementsScreen(_app))) : Ui.Section("Achievements"));
            ScrollView row = Ui.Scroll(true);
            for (int i = 0; i < Mathf.Min(earned.Count, 20); i++)
            {
                Look(J.Str(earned[i], "aType"), out string icon, out Color tint);
                VisualElement medal = Ui.Box("medal", Ui.Icon(icon, 30, tint));
                medal.style.backgroundColor = Color.Lerp(Ui.Ink, tint, 0.22f);
                VisualElement item = Ui.Box("medal-item", medal, Ui.Text(J.Str(earned[i], "title"), "medal-title", Ui.Bold), Ui.Text(J.Str(earned[i], "description"), "medal-desc", Ui.Regular));
                if (i == 0) item.style.marginLeft = 16;
                row.Add(item);
            }
            _achievements.Add(row);
        }

        void ShowChallenges(List<object> rows) => Cards.Challenges(_app, _challenges, rows, joinedFirst: false);
        // (Another Player's challenges are not shown: the app's MemberOverview has none.)

        // ── Activity ─────────────────────────────────────────────────────────

        void ShowActivity(object stats)
        {
            Dictionary<string, int> calendar = ProfileMath.Calendar(stats);
            int days = J.Int(stats, "active_days_last_30"), before = J.Int(stats, "active_days_prev_30"), change = days - before;
            DateTime today = DateTime.Now.Date;

            VisualElement card = Ui.Box("glass");
            Label trend = Ui.Text((change > 0 ? "+" : "") + change + " vs the 30 days before", "sub", Ui.Regular);
            if (calendar.Count == 0) { Fill(_activity, "Last 30 days", Ui.Box("glass", Ui.Text("No putts in the last two months.", "sub", Ui.Regular))); }
            trend.style.color = change > 0 ? Ui.Bright : change < 0 ? Ui.Red : Ui.Faint;
            card.Add(Ui.Box("row", Ui.Text(days.ToString(), "score-mid", Ui.Bold), Ui.Box("grow activity-words", Ui.Text("ACTIVE DAYS", "kick", Ui.Bold), trend)));
            card.Add(new CalendarView(calendar, today).Margin(12));
            Fill(_activity, "Last 30 days", card);

            Fill(_months, "Putts by month", Ui.Box("glass", new LineChart(ProfileMath.ByMonth(calendar, today))));
        }

        // ── Make % ───────────────────────────────────────────────────────────

        // As the app draws it: a row per distance putted, the distance large on the left, then three
        // thin bars each with its percentage in front: You (green), PGA Tour (faint), all Puttr Players (dark green).
        internal static readonly Color YouColour = new Color32(0x2D, 0x8C, 0x3C, 0xFF), PgaColour = new Color32(0x3F, 0x41, 0x42, 0xFF), PuttrColour = new Color32(0x03, 0x6B, 0x36, 0xFF);

        void ShowDistance()
        {
            if (_mine == null) return;
            if (_mine.Count == 0) { Fill(_distance, "Make % by distance", Ui.Box("glass", Ui.Text("No putts yet!", "sub", Ui.Regular))); return; }
            VisualElement card = Ui.Box("glass");
            card.Add(Ui.Box("row legend", Key("You", YouColour), Key("PGA", PgaColour), Key("Puttr", PuttrColour)));
            foreach (object row in _mine)
            {
                int feet = J.Int(row, "distance");
                if (feet < 3 || feet > 11 || J.Num(row, "total") <= 0) continue;
                int you = (int)Math.Round(J.Num(row, "made") / J.Num(row, "total") * 100.0);
                int puttr = 0;
                foreach (object all in _puttr ?? new List<object>()) if (J.Int(all, "distance") == feet && J.Num(all, "total") > 0) puttr = (int)Math.Round(J.Num(all, "made") / J.Num(all, "total") * 100.0);
                VisualElement bars = Ui.Box("grow distance-bars", Line(you, YouColour, true), Line(ProfileMath.Pga[feet - 3], PgaColour, false));
                if (puttr > 0) bars.Add(Line(puttr, PuttrColour, false));
                card.Add(Ui.Box("distance-row", Ui.Box("distance-feet", Ui.Text(feet.ToString(), "distance-number", Ui.Bold), Ui.Text("ft", "muted center", Ui.Regular)), bars));
            }
            Fill(_distance, "Make % by distance", card, "Last 90 days");
        }

        internal static VisualElement Key(string name, Color colour)
        {
            VisualElement dot = Ui.Box("legend-dot");
            dot.style.backgroundColor = colour;
            return Ui.Box("row legend-item", dot, Ui.Text(name, "muted", Ui.Regular));
        }

        internal static VisualElement Line(int percent, Color colour, bool you)
        {
            VisualElement fill = Ui.Box("line-fill");
            fill.style.width = Length.Percent(Mathf.Clamp(percent, 0, 100));
            fill.style.backgroundColor = colour;
            Label value = Ui.Text(percent + "%", "line-value", you ? Ui.Medium : Ui.Regular);
            value.style.color = you ? Color.white : (Color)new Color32(107, 108, 109, 255);
            return Ui.Box("row line", value.Width(38), Ui.Box("grow line-track", fill));
        }

        void ShowBreaks(List<object> rows)
        {
            int[] percent = ProfileMath.Breaks(rows);
            if (percent[0] < 0 && percent[1] < 0 && percent[2] < 0) { _breaks.Clear(); return; }
            // Breaks.js: three 76pt rings spread across the card, each with its tee under it.
            VisualElement rings = Ui.Box("profile-breaks");
            for (int i = 0; i < 3; i++)
                rings.Add(Ui.Box("profile-break", new Ring(percent[i]), Ui.Text(ProfileMath.BreakNames[i], "profile-break-name", Ui.Medium)));
            Fill(_breaks, "Make % by break", Ui.Box("glass", rings), "Last 90 days");
        }

        // ── Competition, bests, quests (GET /profile/summary/) ───────────────

        void ShowSummary(object summary)
        {
            object league = J.Get(summary, "league"), tournaments = J.Get(summary, "tournaments"), versus = J.Get(summary, "versus"), quests = J.Get(summary, "quests");
            VisualElement compete = Ui.Box("glass");
            compete.Add(Row("UsersThree", Ui.Amber, "League Night", league != null
                ? J.Str(league, "team", "Your team") + "   ·   " + J.Int(league, "weeksPlayed") + " weeks played"
                : "Six teams. One trophy. Join a team to play."));
            int played = J.Int(tournaments, "played");
            compete.Add(Row("Trophy", Ui.Bright, "Tournaments", played > 0
                ? played + (played == 1 ? " played" : " played") + (J.Get(tournaments, "bestFinish") != null ? "   ·   best finish " + Place(J.Int(tournaments, "bestFinish")) : "")
                : "None played yet. Find one to enter."));
            int total = J.Int(versus, "total");
            compete.Add(Row("User", Ui.Blue, "Versus", total > 0
                ? J.Int(versus, "won") + " won   ·   " + J.Int(versus, "lost") + " lost" + (J.Int(versus, "open") > 0 ? "   ·   " + J.Int(versus, "open") + " your turn" : "")
                : "Challenge a friend to a head to head."));
            FirstRow(compete);
            if (_member == null && compete.childCount >= 3)
            {
                compete[0].OnTap(() => _app.Push(new LeagueScreen(_app)));
                compete[1].OnTap(() => _app.Push(new TournamentsScreen(_app)));
                compete[2].OnTap(() => _app.Push(new VersusInboxScreen(_app)));
            }
            Fill(_compete, "Competition", compete);

            List<object> bests = J.List(J.Get(summary, "bests"));
            if (bests.Count == 0) _bests.Clear();
            else
            {
                VisualElement card = Ui.Box("glass rows");
                foreach (object best in bests)
                {
                    DateTime? at = ProfileMath.Date(best, "bestAt");
                    int plays = J.Int(best, "plays");
                    string detail = (at != null ? at.Value.ToString("MMM d", CultureInfo.InvariantCulture) + "   ·   " : "") + plays + (plays == 1 ? " play" : " plays");
                    card.Add(Row("FillTrophy", new Color32(0xFF, 0xD7, 0x00, 0xFF), J.Str(J.Get(best, "game"), "title", "Game"), detail,
                        Ui.Text(J.Get(best, "best") != null ? J.Num(best, "best").ToString("0.#", CultureInfo.InvariantCulture) : "–", "feed-score", Ui.Bold)));
                }
                FirstRow(card);
                Fill(_bests, "Personal bests", card);
            }

            int stars = J.Int(quests, "stars"), max = Mathf.Max(1, J.Int(quests, "maxStars"));
            VisualElement questCard = Ui.Box("glass");
            if (stars == 0) questCard.Add(Ui.Text("Earn up to three stars a level. Play your first quest to start.", "sub", Ui.Regular));
            else
            {
                questCard.Add(Ui.Text(ProfileMath.Number(stars) + " of " + ProfileMath.Number(max) + " stars", "feed-name", Ui.Bold));
                questCard.Add(Ui.Progress(stars / (float)max));
                string next = J.Str(J.Get(quests, "nextQuest"), "title");
                questCard.Add(Ui.Text(J.Int(quests, "questsComplete") + " of " + J.Int(quests, "questsTotal") + " quests complete" + (next.Length > 0 ? "   ·   next up: " + next : ""), "muted", Ui.Regular).Margin(8));
            }
            Fill(_quests, "Quest stars", questCard.OnTap(() => _app.Push(new QuestsScreen(_app))));
        }

        // Rows are ruled off from the one above; the first in a card has nothing above it.
        static void FirstRow(VisualElement card)
        {
            if (card.childCount == 0) return;
            card[0].style.marginTop = 0;
            card[0].style.paddingTop = 0;
            card[0].style.borderTopWidth = 0;
        }

        static string Place(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");
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
            p.strokeColor = new Color32(44, 46, 48, 255);
            p.BeginPath(); p.Arc(centre, radius, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            if (_percent <= 0) return;
            p.strokeColor = _percent >= 60 ? Ui.Bright : _percent >= 40 ? Ui.Amber : Ui.Red;
            p.BeginPath(); p.Arc(centre, radius, Angle.Degrees(-90), Angle.Degrees(-90 + 360f * Mathf.Clamp01(_percent / 100f))); p.Stroke();
        }
    }
}
