// The pages behind the Profile tab's cards, as puttr-app has them: who you follow and who
// follows you (with finding and following Players), your past games, and a challenge's page.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Ago
    {
        /// <summary>"just now", "5 minutes ago", "2 hours ago", "3 days ago", then the date.</summary>
        public static string Words(DateTime? thenUtc, DateTime nowUtc)
        {
            if (thenUtc == null) return "";
            TimeSpan gone = nowUtc - thenUtc.Value;
            if (gone.TotalMinutes < 1) return "just now";
            if (gone.TotalHours < 1) return Count((int)gone.TotalMinutes, "minute");
            if (gone.TotalDays < 1) return Count((int)gone.TotalHours, "hour");
            if (gone.TotalDays < 30) return Count((int)gone.TotalDays, "day");
            return thenUtc.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        }

        static string Count(int n, string unit) => n + " " + unit + (n == 1 ? "" : "s") + " ago";
    }

    /// <summary>Following and Followers, with the way to find more Players.</summary>
    public sealed class SocialScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _tabs, _list;
        List<object> _following, _followers;
        bool _showFollowers;

        public SocialScreen(App app, bool followers)
        {
            _app = app;
            _showFollowers = followers;
            _tabs = new VisualElement();
            _list = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            // Find is the header's one action: an icon square where the header's balancing space was.
            VisualElement header = Ui.Header("Players", app.Pop);
            header.RemoveAt(header.childCount - 1);
            header.Add(Ui.Box("back-balance social-find", Ui.IconSquare("MagnifyingGlass", () => _app.Push(new FindPlayersScreen(_app)))));
            scroll.Add(header);
            scroll.Add(Ui.Box("pad", _tabs, _list));
            Root = scroll;
        }

        public override void Shown()
        {
            if (Kid.Blocked(_app, Kid.Social, _list)) return;
            App.Run(Api.Get("/profile/following/", data => { _following = J.List(data); Draw(); }));
            App.Run(Api.Get("/profile/followers/", data => { _followers = J.List(data); Draw(); }));
            Draw();
        }

        void Draw()
        {
            _tabs.Clear();
            // The two counts as one stat strip; the list under it is the lit one's.
            VisualElement strip = Ui.StatStrip(
                (_following != null ? _following.Count.ToString() : "–", "Following"),
                (_followers != null ? _followers.Count.ToString() : "–", "Followers"));
            strip.AddToClassList("social-strip");
            strip[_showFollowers ? 1 : 0].AddToClassList("social-on");
            strip[0].OnTap(() => { _showFollowers = false; Draw(); });
            strip[1].OnTap(() => { _showFollowers = true; Draw(); });
            _tabs.Add(strip);

            _list.Clear();
            List<object> rows = _showFollowers ? _followers : _following;
            if (rows == null) { _list.Add(Spinner.Small()); return; }
            if (rows.Count == 0) { _list.Add(Ui.Text(_showFollowers ? "No followers yet" : "Not following anyone yet. Tap the search button to look for Players.", "body-2 center social-note", Ui.Regular)); return; }
            rows = new List<object>(rows);
            rows.Sort((a, b) => string.Compare(J.Str(a, "displayName"), J.Str(b, "displayName"), StringComparison.OrdinalIgnoreCase));
            _list.Add(MemberList(rows, _following, () => Shown()));
        }

        /// <summary>Players as rows of one bordered block, a divider between them.</summary>
        public static VisualElement MemberList(List<object> members, List<object> following, Action changed)
        {
            VisualElement block = Ui.Box("glass social-list");
            foreach (object member in members) block.Add(MemberRow(member, following, changed));
            if (block.childCount > 0) block[block.childCount - 1].AddToClassList("set-row-last");
            return block;
        }

        /// <summary>A Player: photo, name, home town, and Follow or Following.</summary>
        public static VisualElement MemberRow(object member, List<object> following, Action changed)
        {
            string id = J.Str(member, "userId"), name = J.Str(member, "displayName", "A Player");
            bool followed = false;
            foreach (object mine in following ?? new List<object>()) if (J.Str(mine, "userId") == id) followed = true;
            VisualElement avatar = Ui.Avatar(name, J.Str(member, "photoURL"));
            if (J.Flag(member, "isMember")) avatar.AddToClassList("avatar-member");
            Label label = Ui.Text(followed ? "FOLLOWING" : "FOLLOW", "follow-text", Ui.Bold);
            VisualElement button = Ui.Box(followed ? "follow follow-on" : "follow", label);
            bool busy = false;
            button.OnTap(() =>
            {
                if (busy || id == Auth.Uid) return;
                busy = true;
                button.AddToClassList("btn-off");
                // Both are GETs in the API. Unfollowing is immediate, as in the app.
                App.Run(Api.Get("/members/" + (followed ? "unfollow/" : "follow/") + id, _ => changed(), _ => { busy = false; button.RemoveFromClassList("btn-off"); }));
            });
            VisualElement who = Ui.Box("grow", Ui.Text(name, "set-title", Ui.Bold), Ui.Text(J.Str(member, "homeTown"), "set-sub", Ui.Regular));
            if (App.I != null) { avatar.OnTap(() => ProfileScreen.Open(App.I, id, member)); who.OnTap(() => ProfileScreen.Open(App.I, id, member)); }
            return Ui.Box("member-row", avatar, who, id == Auth.Uid ? null : button);
        }
    }

    /// <summary>Find members: by name once two letters are typed.</summary>
    public sealed class FindPlayersScreen : AppScreen
    {
        readonly VisualElement _list;
        readonly Label _context;
        List<object> _following = new List<object>();
        string _asked = "";

        public FindPlayersScreen(App app)
        {
            var field = new TextField();
            field.textEdition.placeholder = "Search by name...";
            field.textEdition.hidePlaceholderOnFocus = false;
            field.AddToClassList("field");
            field.Face(Ui.Medium);
            field.RegisterValueChangedCallback(change => Search(change.newValue.Trim()));
            _context = Ui.Text("Type two or more letters of a Player's name.", "body-2", Ui.Medium);
            _list = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("Find Players", app.Pop));
            scroll.Add(Ui.Box("pad", field, _context, _list));
            Root = scroll;
            if (Kid.IsKid(app.Profile)) { scroll.Clear(); var body = new VisualElement(); scroll.Add(Ui.Header("Find Players", app.Pop)); scroll.Add(body); Kid.Blocked(app, Kid.Social, body); return; }
            App.Run(Api.Get("/profile/following/", data => _following = J.List(data)));
            field.schedule.Execute(() => field.Focus()).StartingIn(250);
        }

        void Search(string name)
        {
            _asked = name;
            if (name.Length < 2) { _list.Clear(); _context.text = "Type two or more letters of a Player's name."; return; }
            App.Run(Api.Get("/members/search/" + UnityEngine.Networking.UnityWebRequest.EscapeURL(name).Replace("+", "%20") + "/", data =>
            {
                if (_asked != name) return; // an answer to letters since typed over
                Show(name, J.List(data));
            }));
        }

        void Show(string name, List<object> members)
        {
            _context.text = members.Count == 0 ? "No Players found" : "Results for \"" + name + "\"";
            _list.Clear();
            if (members.Count == 0) return;
            _list.Add(SocialScreen.MemberList(members, _following, () => App.Run(Api.Get("/profile/following/", data => { _following = J.List(data); if (_asked == name) Show(name, members); }))).Margin(12));
        }
    }

    /// <summary>Every finished game, newest first, with how many of each have been played.</summary>
    public sealed class GamesScreen : AppScreen
    {
        /// <summary>Your past games, or another Player's (memberId): each opens that game's page.</summary>
        public GamesScreen(App app, string memberId = null)
        {
            var counts = new VisualElement();
            var list = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header(memberId == null ? "Recent games" : "Games", app.Pop));
            scroll.Add(counts);
            scroll.Add(Ui.Box("pad", list));
            // A tablet on its side (or anything 900 wide): the games three across, else one to a row.
            list.style.flexDirection = FlexDirection.Row;
            list.style.flexWrap = Wrap.Wrap;
            int cols = 0;
            void Lay()
            {
                float w = list.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 0) return;
                int want = w >= 900 ? 3 : w >= 600 ? 2 : 1;
                const float gap = 12f;
                float each = (w - gap * (want - 1)) / want;
                int i = 0;
                foreach (VisualElement row in list.Children())
                {
                    if (!row.ClassListContains("game-row")) { row.style.width = Length.Percent(100); continue; }
                    row.style.width = want == 1 ? Length.Percent(100) : new StyleLength(each);
                    row.style.marginRight = want > 1 && i % want != want - 1 ? gap : 0f;
                    i++;
                }
                cols = want;
            }
            list.RegisterCallback<GeometryChangedEvent>(_ => Lay());
            var foot = new VisualElement();
            foot.style.height = 28;
            scroll.Add(foot);
            Root = scroll;

            string counted = memberId == null ? "/profile/gameCounts/" : "/members/gameCounts/" + memberId;
            string played = memberId == null ? "/profile/games/" : "/members/games/" + memberId;
            App.Run(Api.Get(counted, data =>
            {
                List<object> rows = J.List(data);
                if (rows.Count == 0) return;
                ScrollView strip = Ui.Scroll(true);
                double total = 0;
                foreach (object row in rows) total += J.Num(row, "count");
                // One stat strip that scrolls sideways: the total, then a cell a game.
                VisualElement cells = Ui.Box("strip count-strip");
                cells.Add(Tile(ProfileMath.Number(total), "Total", true));
                foreach (object row in rows) cells.Add(Tile(ProfileMath.Number(J.Num(row, "count")), J.Str(row, "title"), false));
                strip.Add(cells);
                var end = new VisualElement();
                end.style.width = 16;
                strip.Add(end);
                counts.Add(Ui.Section("Games played"));
                strip.style.marginBottom = 24;   // room between the counts and the games (Matt)
                counts.Add(strip);
            }));
            App.Run(Api.Get(played, data =>
            {
                List<object> rows = J.List(data);
                if (rows.Count == 0) { list.Add(Ui.Text("No finished games yet.", "body-2 center", Ui.Regular).Margin(32)); return; }
                foreach (object game in rows)
                {
                    double score = J.Num(game, "score");
                    VisualElement words = Ui.Box("grow", Ui.Text(J.Str(game, "title", "Game"), "set-title", Ui.Bold), Ui.Text(Ago.Words(ProfileMath.Date(game, "updated_at"), DateTime.UtcNow), "set-sub", Ui.Regular));
                    if (J.Int(game, "tournamentId") != 0) words.Add(Ui.Text("Tournament", "chip chip-green game-tag", Ui.Bold));
                    Label value = Ui.Text(score > 0 ? score.ToString("0.##", CultureInfo.InvariantCulture) : "–", "game-score ital", Ui.Bold);
                    if (score <= 0) value.style.color = Ui.Faint;
                    string gameId = J.Str(game, "id");
                    list.Add(Ui.Box("glass game-row", words, value).OnTap(() => app.Push(new GameViewScreen(app, gameId))));
                }
                list.schedule.Execute(Lay);
            }, _ => list.Add(Ui.Text("Could not load your games.", "body-2 center", Ui.Regular).Margin(32))));
        }

        static VisualElement Tile(string count, string title, bool first)
        {
            return Ui.Box(first ? "strip-cell strip-cell-first count-cell" : "strip-cell count-cell", Ui.Num(count, "strip-num"), Ui.Caps(title, "strip-cap"));
        }
    }

    public static class ChallengeMath
    {
        /// <summary>The tiers a score has passed (strictly more than a tier's number), and how far it is to the next: 0 to 100.</summary>
        public static int Percent(double score, List<object> levels, out int reached, out double goal)
        {
            reached = -1;
            for (int i = 0; i < levels.Count; i++) if (score > J.Num(levels[i], "number")) reached = i;
            if (levels.Count == 0) { goal = 0; return 0; }
            if (reached >= levels.Count - 1) { goal = J.Num(levels[levels.Count - 1], "number"); return 100; }
            goal = J.Num(levels[reached + 1], "number");
            return goal > 0 ? (int)Math.Round(score / goal * 100.0) : 0;
        }

        public static string Unit(string type) => type == "DISTANCE" ? "feet" : type == "GAMES" ? "games" : "putts";

        public static Color Tier(string title) =>
            title == "GOLD" ? (Color)new Color32(255, 215, 0, 255) : title == "SILVER" ? (Color)new Color32(192, 192, 192, 255) : (Color)new Color32(205, 127, 48, 255);
    }

    /// <summary>One challenge: what it is, joining it, how far along the Player is, and its tiers.</summary>
    public sealed class ChallengeScreen : AppScreen
    {
        readonly App _app;
        readonly ScrollView _scroll;
        object _row;
        bool _busy;

        public ChallengeScreen(App app, object row)
        {
            _app = app;
            _row = row;
            _scroll = Ui.Scroll();
            Root = _scroll;
            Draw(false);
        }

        void Draw(bool justJoined)
        {
            object challenge = J.Get(_row, "challenge");
            List<object> levels = J.List(J.Get(_row, "challengeLevels"));
            string id = J.Str(challenge, "id"), type = J.Str(challenge, "cType"), unit = ChallengeMath.Unit(type);
            bool joined = J.Get(challenge, "userJoined") != null;
            double score = J.Get(_row, "userResults") != null ? J.Num(_row, "userResults") : J.Num(challenge, "currentScore");
            int percent = ChallengeMath.Percent(score, levels, out int reached, out double goal);
            int people = J.Int(challenge, "participants");
            ProfileScreen.Look(type, out string icon, out _);

            _scroll.Clear();
            _scroll.Add(Ui.Header("Challenge", _app.Pop));
            // A flat crest: the glyph on a surface square, in the tier's colour once one is earned (white until then).
            VisualElement medal = Ui.Box("medal medal-big", Ui.Icon(Cards.Stroke(icon), 36, reached >= 0 ? ChallengeMath.Tier(J.Str(levels[reached], "title")) : Color.white));
            DateTime? start = ProfileMath.Date(challenge, "startDate"), end = ProfileMath.Date(challenge, "endDate");
            VisualElement hero = Ui.Box("glass challenge-hero", medal,
                Ui.Title(J.Str(challenge, "title", "Challenge"), "title challenge-name"),
                Ui.Text(J.Str(challenge, "subTitle"), "body-2 center", Ui.Regular),
                Ui.Caps(ProfileMath.TimeLeft(end, DateTime.UtcNow) + (people > 0 ? "   ·   " + ProfileMath.Number(people) + " joined" : ""), "caps challenge-when"));
            if (start != null && end != null) hero.Add(Ui.Text(ProfileMath.Day(start.Value) + " – " + ProfileMath.Day(end.Value), "body-2 center", Ui.Regular).Margin(4));
            _scroll.Add(Ui.Box("pad", hero));

            if (joined)
            {
                VisualElement card = Ui.Box("glass");
                card.Add(Ui.Text(justJoined ? "You are in. Putt well." : "YOUR PROGRESS", justJoined ? "body" : "caps caps-green", justJoined ? Ui.Regular : Ui.Bold));
                card.Add(Ui.Box("row", Ui.Num(ProfileMath.Number(score), "num-44"), Ui.Text(unit, "body-2 challenge-unit", Ui.Regular)));
                if (reached >= 0) card.Add(Ui.Text(J.Str(levels[reached], "title") + " earned", "chip chip-green challenge-earned", Ui.Bold));
                VisualElement bar = Ui.Progress(percent / 100f);
                bar.AddToClassList("challenge-bar");
                card.Add(bar);
                string next = reached < levels.Count - 1 && levels.Count > 0 ? ProfileMath.Number(Math.Max(0, goal - score)) + " more to " + J.Str(levels[reached + 1], "title") : "Every tier earned";
                card.Add(Ui.Box("row challenge-line", Ui.Text(next, "set-sub grow", Ui.Medium), Ui.Text(percent + "%", "set-sub", Ui.Bold)));
                _scroll.Add(Ui.Box("pad", card).Margin(12));
            }
            else _scroll.Add(Ui.Box("pad", Ui.Button("Join challenge", () => Change("join", id))));

            if (levels.Count > 0)
            {
                _scroll.Add(Ui.Section("Challenge tiers"));
                VisualElement tiers = Ui.Box("glass set-card");
                int tierAt = 0;
                foreach (object level in levels)
                {
                    string title = J.Str(level, "title");
                    VisualElement dot = Ui.Box("tier-dot");
                    dot.style.backgroundColor = ChallengeMath.Tier(title);
                    tiers.Add(Ui.Box(++tierAt == levels.Count ? "set-row set-row-last" : "set-row", dot, Ui.Box("grow", Ui.Text(title, "set-title", Ui.Bold), Ui.Text(ProfileMath.Number(J.Num(level, "number")) + " " + unit, "set-sub", Ui.Regular)),
                        joined && score > J.Num(level, "number") ? Ui.Text("Earned", "chip chip-green", Ui.Bold) : null));
                }
                _scroll.Add(Ui.Box("pad", tiers));
            }
            if (joined && !justJoined) _scroll.Add(Ui.Box("pad", Ui.Button("Leave challenge", () => Change("leave", id), "btn btn-ghost")));
            var foot = new VisualElement();
            foot.style.height = 28;
            _scroll.Add(foot);
        }

        void Change(string what, string id)
        {
            if (_busy) return;
            _busy = true;
            App.Run(Api.Get("/challenges/" + what + "/" + id, _ =>
            {
                _busy = false;
                if (what == "leave") { _app.Pop(); _app.Toast("You have left the challenge."); return; }
                // The Player's score so far is worked out by the API: ask it to, then read the challenge again.
                App.Run(Api.Get("/challenges/update", __ => Reload(id), __ => Reload(id)));
            }, why => { _busy = false; _app.Toast(why == "offline" ? "No connection. Try again." : "That did not work. Try again in a moment."); }));
        }

        void Reload(string id)
        {
            App.Run(Api.Get("/challenges/userChallenges", data =>
            {
                foreach (object row in J.List(data)) if (J.Str(J.Get(row, "challenge"), "id") == id) _row = row;
                // If the list has not caught up, show it as joined anyway: the join itself succeeded.
                if (J.Get(J.Get(_row, "challenge"), "userJoined") == null && J.Get(_row, "challenge") is Dictionary<string, object> challenge) challenge["userJoined"] = "now";
                Draw(true);
            }, _ => Draw(false)));
        }
    }
}
