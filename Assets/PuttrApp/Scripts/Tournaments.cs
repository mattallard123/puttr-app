// Tournaments, from the live API as puttr-app shows them: the row on Home, the list, and one
// tournament's page (art, prize, rounds, leaderboard, and joining or playing the next round).
//
// A tournament is up to four rounds. A round is a fixed list of putts, each from a set distance
// and tee; a holed putt scores its distance in feet, one attempt a round, highest total wins.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>The sums and sentences, apart from the screens so they can be tested.</summary>
    public static class TournamentMath
    {
        /// <summary>A putt to play. Angle: a Precision quest putt's entry window in degrees (0: none).</summary>
        public struct Dealt { public int Feet, Tee; public double Angle; }

        /// <summary>The putts of a round, from its puttData: a JSON string "[{id, distance, tee}]".</summary>
        public static List<Dealt> Putts(object round)
        {
            var putts = new List<Dealt>();
            object raw = J.Get(round, "puttData");
            if (raw is string text) raw = J.Parse(text);
            foreach (object putt in J.List(raw))
            {
                // The mat is 3 to 11 feet and has three tees: anything else in the data is not putted.
                int feet = J.Int(putt, "distance"), tee = J.Int(putt, "tee", 2);
                if (feet < 3 || feet > 11) continue;
                putts.Add(new Dealt { Feet = feet, Tee = Mathf.Clamp(tee, 1, 3), Angle = J.Num(putt, "angle") });
            }
            return putts;
        }

        /// <summary>The most a round, or every round together, can score: the feet of every putt.</summary>
        public static int Possible(IEnumerable<object> rounds)
        {
            int feet = 0;
            foreach (object round in rounds) foreach (Dealt putt in Putts(round)) feet += putt.Feet;
            return feet;
        }

        /// <summary>The round to play next, 1 to 4, from the Player's results (r1_score .. r4_score); 5 when all are played.</summary>
        public static int CurrentRound(object results, int rounds = 4)
        {
            for (int round = 1; round <= rounds; round++)
                if (J.Get(results, "r" + round + "_score") == null) return round;
            return rounds + 1;
        }

        public enum State { Upcoming, Active, Ended }

        /// <summary>By the day, in UTC, as the app compares them.</summary>
        public static State StateOf(object tournament, DateTime nowUtc)
        {
            DateTime? start = ProfileMath.Date(tournament, "startDate"), end = ProfileMath.Date(tournament, "endDate");
            if (start != null && nowUtc.Date < start.Value.Date) return State.Upcoming;
            if (end != null && nowUtc.Date > end.Value.Date) return State.Ended;
            return State.Active;
        }

        public static string Dates(object tournament)
        {
            DateTime? start = ProfileMath.Date(tournament, "startDate"), end = ProfileMath.Date(tournament, "endDate");
            if (start == null || end == null) return "";
            return start.Value.ToString("MMM d", CultureInfo.InvariantCulture) + " – " + end.Value.ToString("MMM d", CultureInfo.InvariantCulture);
        }

        /// <summary>Easy, Medium or Hard from the game's difficulty, which arrives as a string like "7.0".</summary>
        public static string Level(object round)
        {
            double difficulty = J.Num(round, "difficulty");
            return difficulty > 6.5 ? "Hard" : difficulty > 3.2 ? "Medium" : "Easy";
        }

        /// <summary>The tournament's picture, sized by the image service the app uses for images.puttr.co.</summary>
        public static string Art(object tournament, int size)
        {
            string url = J.Str(tournament, "prizeImage");
            if (url.Length == 0 || url == "null") url = J.Str(tournament, "heroImage");
            return Sized(url, size);
        }

        public static string Sized(string url, int size)
        {
            const string bucket = "images.puttr.co";
            int at = url.IndexOf(bucket, StringComparison.Ordinal);
            if (at < 0) return url;
            return "https://puttr.imgix.net" + url.Substring(at + bucket.Length) + "?w=" + size + "&h=" + size;
        }
    }

    public static partial class Compete
    {
        // On Home: League Night first (LeagueNight.cs), then the tournaments.
        static partial void FillCards(App app, VisualElement slot)
        {
            slot.Clear();
            var league = new VisualElement();
            var tournaments = new VisualElement();
            slot.Add(league);
            slot.Add(tournaments);
            LeagueCard(app, league);
            Load(list => TournamentRow(app, tournaments, list));
        }

        static partial void LeagueCard(App app, VisualElement slot);

        /// <summary>Public tournaments that are running or coming, with the private ones the Player is invited to first.</summary>
        public static void Load(Action<List<object>> done)
        {
            List<object> open = null, invited = null;
            void Both()
            {
                if (open == null || invited == null) return;
                var all = new List<object>();
                var seen = new HashSet<string>();
                foreach (object row in invited) if (seen.Add(J.Str(row, "id"))) { ((Dictionary<string, object>)row)["private"] = true; all.Add(row); }
                foreach (object row in open) if (seen.Add(J.Str(row, "id"))) all.Add(row);
                done(all);
            }
            App.Run(Api.Get("/tournaments/upcoming", data => { open = J.List(data); Both(); }, _ => { open = new List<object>(); Both(); }));
            App.Run(Api.Get("/tournaments/PRIVATE", data => { invited = J.List(data); Both(); }, _ => { invited = new List<object>(); Both(); }));
        }

        static void TournamentRow(App app, VisualElement slot, List<object> list)
        {
            slot.Clear();
            if (list.Count == 0) return;
            slot.Add(Ui.Section("Tournaments", "See all", () => app.Push(new TournamentsScreen(app))));
            ScrollView row = Ui.Scroll(true);
            for (int i = 0; i < Mathf.Min(6, list.Count); i++)
            {
                VisualElement poster = Poster(app, list[i], 200);
                poster.style.marginLeft = i == 0 ? 16 : 10;
                row.Add(poster);
            }
            var end = new VisualElement();
            end.style.width = 16;
            row.Add(end);
            slot.Add(row);
        }

        /// <summary>A tournament as a square poster: its art, name, dates, players, and whether the Player is in.</summary>
        public static VisualElement Poster(App app, object tournament, float size)
        {
            bool joined = J.Get(tournament, "userJoined") != null;
            TournamentMath.State state = TournamentMath.StateOf(tournament, DateTime.UtcNow);
            VisualElement art = Ui.Box("poster");
            art.style.width = size;
            art.style.height = size;
            art.style.minWidth = art.style.maxWidth = size; // fixed: a row or a column never stretches it
            art.style.flexShrink = 0;
            art.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            art.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            art.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            Images.Into(art, TournamentMath.Art(tournament, 400));
            if (J.Flag(tournament, "private")) art.Add(Ui.Text("PRIVATE", "chip chip-amber poster-chip", Ui.Bold));
            int players = J.Int(tournament, "participants");
            // TournamentCard's panel: the title with its Join/Joined/Ended pill, then the dates and the Players.
            VisualElement line = Ui.Box("row poster-line", Ui.Text(TournamentMath.Dates(tournament), "muted grow", Ui.Regular));
            if (players > 0) line.Add(Ui.Text(players + (players == 1 ? " Player" : " Players"), "muted", Ui.Regular));
            art.Add(Ui.Box("poster-panel",
                Ui.Box("row tourn-title-row",
                    Ui.Text(J.Str(tournament, "title", "Tournament"), "tile-title grow tourn-title", Ui.Bold),
                    Ui.Text(joined ? "Joined" : state == TournamentMath.State.Ended ? "Ended" : "Join", joined ? "chip chip-green" : "chip", Ui.Bold)),
                line));
            return art.OnTap(() => app.Push(new TournamentScreen(app, tournament)));
        }
    }

    public sealed class TournamentsScreen : AppScreen
    {
        public TournamentsScreen(App app)
        {
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("Tournaments", app.Pop));
            if (!Kid.IsKid(app.Profile)) scroll.Add(Ui.Box("pad", Ui.Button("Create tournament", () => Club.Require(app, "Private tournaments", () => app.Push(new CreateTournamentScreen(app))), "btn btn-ghost")));
            var open = new VisualElement();
            var past = new VisualElement();
            scroll.Add(open);
            scroll.Add(past);
            var foot = new VisualElement();
            foot.style.height = 28;
            scroll.Add(foot);
            Root = scroll;

            // As puttr-app's TournamentsScreen: "Current and upcoming" as a row of square posters to
            // scroll sideways, then "Past" the same way under it.
            Compete.Load(list =>
            {
                open.Add(Ui.Section("Current and upcoming"));
                if (list.Count == 0) open.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("No tournaments right now. A new one opens on the first of every month.", "sub", Ui.Regular))));
                else open.Add(Row(app, list));
            });
            App.Run(Api.Get("/tournaments/past", data =>
            {
                List<object> list = J.List(data);
                if (list.Count == 0) return;
                past.Add(Ui.Section("Past"));
                past.Add(Row(app, list));
            }));
        }

        /// <summary>TournamentCard's size: 220 square, the art cover-cropped, never stretched.</summary>
        const float CardSize = 220;

        static VisualElement Row(App app, List<object> list)
        {
            ScrollView row = Ui.Scroll(true);
            row.AddToClassList("tourn-row");
            for (int i = 0; i < list.Count; i++)
            {
                VisualElement poster = Compete.Poster(app, list[i], CardSize);
                poster.style.marginLeft = i == 0 ? 16 : 12;
                row.Add(poster);
            }
            var end = new VisualElement();
            end.style.width = 16;
            end.style.flexShrink = 0;
            row.Add(end);
            return row;
        }
    }

    public sealed class TournamentScreen : AppScreen
    {
        readonly App _app;
        readonly object _tournament;
        readonly string _id;
        readonly VisualElement _hero, _prize, _champion, _rounds, _board, _action;
        List<object> _roundRows = new List<object>(), _leaders;
        object _results;
        bool _joined, _busy, _scripted;
        int _heroMode = -1; // 0: phone (art over the title), 1: wide (art boxed beside the info)
        readonly HashSet<string> _open = new HashSet<string>(); // leaderboard rows the Player has opened
        readonly Dictionary<string, List<object>> _userGames = new Dictionary<string, List<object>>();

        /// <summary>TournamentView's landscape art: a square of at most 240, never the page's width.</summary>
        const float WideArt = 240, NarrowArtMax = 520, WideAt = 600;

        public TournamentScreen(App app, object tournament)
        {
            _app = app;
            _tournament = tournament;
            _id = J.Str(tournament, "id");
            _joined = J.Get(tournament, "userJoined") != null;

            _hero = new VisualElement();
            _hero.RegisterCallback<GeometryChangedEvent>(_ => Hero());
            _prize = new VisualElement(); _champion = new VisualElement(); _rounds = new VisualElement(); _board = new VisualElement(); _action = new VisualElement();

            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad", _hero, _prize, _action, _champion));
            scroll.Add(_rounds);
            scroll.Add(_board);
            var foot = new VisualElement();
            foot.style.height = 28;
            scroll.Add(foot);
            Root = scroll;
        }

        // TournamentView's hero card. Phone: the square art across the card with the host and status
        // over it, the title and dates on a scrim at its foot, the description under it. Wide (tablet,
        // landscape): the art boxed at 240 on the left, and the status, title, dates, Players,
        // description, host and prize in the column beside it.
        void Hero()
        {
            float width = _hero.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0) return;
            int mode = width >= WideAt ? 1 : 0;
            if (mode == _heroMode) return;
            _heroMode = mode;
            _hero.Clear();
            _prize.Clear();

            object t = _tournament;
            TournamentMath.State state = TournamentMath.StateOf(t, DateTime.UtcNow);
            bool wide = mode == 1;
            float side = wide ? Mathf.Min(WideArt, Mathf.Round(width * 0.4f)) : Mathf.Min(width, NarrowArtMax);

            VisualElement art = Ui.Box("tourn-art");
            art.style.width = art.style.height = side;
            art.style.minWidth = art.style.maxWidth = side;
            art.style.minHeight = art.style.maxHeight = side;
            Images.Into(art, TournamentMath.Art(t, 800));
            if (!TournamentMath.Art(t, 800).StartsWith("http")) art.Add(Ui.Box("tourn-art-icon", Ui.Icon("FillTrophy", wide ? 56 : 80, Ui.Bright)));

            Label status = Ui.Text(state == TournamentMath.State.Active ? "ACTIVE" : state == TournamentMath.State.Upcoming ? "UPCOMING" : "ENDED",
                state == TournamentMath.State.Active ? "chip chip-green tourn-status" : state == TournamentMath.State.Upcoming ? "chip chip-amber tourn-status" : "chip tourn-status", Ui.Bold);
            string title = J.Str(t, "title", "Tournament");
            int players = J.Int(t, "participants");
            VisualElement meta = Ui.Box("tourn-meta");
            string dates = TournamentMath.Dates(t);
            if (dates.Length > 0) meta.Add(Ui.Text(dates, "tourn-pill", Ui.Medium));
            meta.Add(Ui.Text(players + (players == 1 ? " Player" : " Players"), "tourn-pill", Ui.Medium));
            string host = J.Str(t, "displayName");
            VisualElement hostChip = host.Length == 0 ? null : Ui.Box("tourn-host",
                Ui.Avatar(host, J.Str(t, "photoURL")),
                Ui.Box("tourn-host-words", Ui.Text("HOSTED BY", "tourn-kick", Ui.Bold), Ui.Text(host, "tourn-host-name", Ui.Bold)));
            string about = J.Str(t, "description");
            Label description = about.Length > 0 && about != "null" ? Ui.Text(about, "tourn-desc", Ui.Regular) : null;

            string prize = J.Str(t, "prizeDescription"), link = J.Str(t, "prizeLink");
            VisualElement prizeRow = null;
            if (prize.Length > 0 && prize != "null")
            {
                prizeRow = Ui.Box("row tourn-prize",
                    Ui.Bubble("FillTrophy", new Color32(0xFF, 0xB3, 0x00, 0xFF)),
                    Ui.Box("grow tourn-prize-words", Ui.Text("Prize details", "feed-name", Ui.Bold), Ui.Text(prize, "tourn-prize-text", Ui.Regular)));
                if (link.StartsWith("http")) { prizeRow.Add(Ui.Text("\u203A", "h2", Ui.Medium)); prizeRow.OnTap(() => Application.OpenURL(link)); }
            }

            if (wide)
            {
                VisualElement column = Ui.Box("tourn-col", status, Ui.Text(title, "tourn-hero-title", Ui.Bold), meta, description, hostChip);
                if (hostChip != null) hostChip.AddToClassList("tourn-host-flat");
                if (prizeRow != null) { column.Add(Ui.Box("grow")); prizeRow.AddToClassList("tourn-prize-in"); column.Add(prizeRow); }
                _hero.Add(Ui.Box("glass tourn-hero tourn-hero-wide", art, column));
            }
            else
            {
                if (hostChip != null) { hostChip.AddToClassList("tourn-host-pill"); art.Add(hostChip); }
                art.Add(status);
                art.Add(Ui.Box("tourn-scrim", Ui.Text(title, "tourn-hero-title", Ui.Bold), meta));
                VisualElement card = Ui.Box("glass tourn-hero tourn-hero-narrow", art, description);
                _hero.Add(card);
                if (prizeRow != null) { prizeRow.AddToClassList("glass"); prizeRow.AddToClassList("tourn-prize-card"); _prize.Add(prizeRow); }
            }
            Perf.Wake(0.3f);
        }

        // Read again every time the page comes to the front: a round just played changes all of it.
        public override void Shown()
        {
            _userGames.Clear(); // a round just played adds a game
            App.Run(Api.Get("/tournaments/details/" + _id, data => { _roundRows = J.List(J.Get(J.First(data), "rounds")); Show(); }));
            App.Run(Api.Get("/tournaments/userResults/" + _id, data => { _results = J.First(data); if (_results != null) _joined = true; Show(); }));
            App.Run(Api.Get("/tournaments/leaderboard/" + _id, data => { _leaders = J.List(data); Show(); }));
        }

        void Show()
        {
            TournamentMath.State state = TournamentMath.StateOf(_tournament, DateTime.UtcNow);
            int count = Mathf.Max(1, _roundRows.Count), current = TournamentMath.CurrentRound(_results, count), possible = TournamentMath.Possible(_roundRows);

            // What the Player can do now.
            _action.Clear();
            if (state == TournamentMath.State.Upcoming)
            {
                DateTime? start = ProfileMath.Date(_tournament, "startDate");
                _action.Add(Ui.Text("Starts " + (start != null ? start.Value.ToString("dddd, MMMM d", CultureInfo.InvariantCulture) : "soon") + ".", "sub center", Ui.Regular).Margin(14));
                if (!_joined) _action.Add(Ui.Button("Join tournament", () => Club.Require(_app, "Tournaments", Join)));
            }
            else if (state == TournamentMath.State.Active)
            {
                if (!_joined) _action.Add(Ui.Button("Join tournament", () => Club.Require(_app, "Tournaments", Join)));
                else if (_roundRows.Count > 0 && current <= _roundRows.Count) _action.Add(Ui.Button("Play round " + current, () => _app.Games.PlayRound(_tournament, _roundRows[current - 1], current)));
                else if (_roundRows.Count > 0) _action.Add(Ui.Text("All rounds played. Check back for the final standings.", "sub center", Ui.Regular).Margin(14));
            }

            // The champion, once it is over.
            _champion.Clear();
            if (state == TournamentMath.State.Ended && _leaders != null && _leaders.Count > 0)
            {
                object first = _leaders[0];
                VisualElement card = Ui.Box("glass champion",
                    Ui.Text("CHAMPION", "kick center", Ui.Bold),
                    Ui.Box("row champion-row", Ui.Avatar(J.Str(first, "displayName"), J.Str(first, "photoUrl"), true),
                        Ui.Box("grow", Ui.Text(J.Str(first, "displayName", "A Player"), "h2", Ui.Bold), Ui.Text(J.Num(first, "totalScore").ToString("0") + (possible > 0 ? " of " + possible + " feet" : " feet"), "sub", Ui.Regular)),
                        PutterBrands.RowBadge(first, 28f)));
                string reel = J.Str(_tournament, "reelVideoUrl");
                if (reel.StartsWith("http")) card.Add(Ui.Button("Watch the highlights", () => Application.OpenURL(reel), "btn btn-ghost"));
                card.style.marginTop = 12;
                _champion.Add(card);
            }

            // The rounds.
            _rounds.Clear();
            if (_roundRows.Count > 0)
            {
                _rounds.Add(Ui.Section(_roundRows.Count + (_roundRows.Count == 1 ? " round" : " rounds")));
                VisualElement tiles = Ui.Box("row round-tiles");
                for (int i = 0; i < _roundRows.Count; i++)
                {
                    object round = _roundRows[i];
                    object score = J.Get(_results, "r" + (i + 1) + "_score");
                    List<TournamentMath.Dealt> putts = TournamentMath.Putts(round);
                    VisualElement tile = Ui.Box(i + 1 == current && state == TournamentMath.State.Active && _joined ? "glass round-tile round-now" : "glass round-tile",
                        Ui.Text("R" + (i + 1), "h2 center", Ui.Bold),
                        Ui.Text(putts.Count + (putts.Count == 1 ? " putt" : " putts"), "muted center", Ui.Regular),
                        Ui.Text(score != null ? J.Num(_results, "r" + (i + 1) + "_score").ToString("0") + " ft" : TournamentMath.Level(round), score != null ? "chip chip-green round-chip" : "chip round-chip", Ui.Bold));
                    tiles.Add(tile);
                }
                _rounds.Add(Ui.Box("pad", tiles));
            }

            // The leaderboard.
            _board.Clear();
            if (_leaders != null)
            {
                _board.Add(Ui.Section("Leaderboard", possible > 0 ? possible + " feet possible" : null));
                VisualElement card = Ui.Box("glass");
                if (_leaders.Count == 0) card.Add(Ui.Text("No rounds played yet. Yours could be the first.", "sub", Ui.Regular));
                int roundCount = _roundRows.Count > 0 ? _roundRows.Count : 4;
                for (int i = 0; i < Mathf.Min(_leaders.Count, 50); i++) card.Add(LeaderRow(_leaders[i], i + 1, roundCount));
                _board.Add(Ui.Box("pad", card));
            }
        }

        // LeaderRow: a tap on the row opens it in place to that Player's round scores (R1 to R4); a tap
        // on a played round opens the game view for that round, with its video replay. The avatar opens
        // their profile and the brand mark their putter.
        VisualElement LeaderRow(object row, int place, int roundCount)
        {
            string name = J.Str(row, "displayName", "A Player");
            string userId = J.Str(row, "userId");
            double total = J.Num(row, "totalScore");
            Label rank = Ui.Text(place.ToString(), "board-place", Ui.Bold);
            if (place <= 3) rank.style.color = place == 1 ? (Color)new Color32(0xFF, 0xD7, 0x00, 0xFF) : place == 2 ? (Color)new Color32(0xC0, 0xC0, 0xC0, 0xFF) : (Color)new Color32(0xCD, 0x7F, 0x32, 0xFF);
            VisualElement avatar = Ui.Avatar(name, J.Str(row, "photoUrl"));
            Swallow(avatar);
            avatar.OnTap(() => ProfileScreen.Open(_app, userId, row));
            VisualElement head = Ui.Box("tourn-leader-head", rank, avatar,
                Ui.Box("grow tourn-leader-words", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text(J.Str(row, "homeTown"), "muted", Ui.Regular)),
                PutterBrands.RowBadge(row, 24f), // their putter's brand (LeaderRow's 24): a tap opens the putter sheet
                Ui.Text(total > 0 ? total.ToString("0") : "-", "tourn-total", Ui.Bold));

            VisualElement rounds = Ui.Box("tourn-rounds");
            for (int r = 1; r <= Mathf.Min(roundCount, 4); r++)
            {
                bool played = J.Num(row, "r" + r + "_score") > 0; // as LeaderRow: a 0 or missing score is not tappable
                VisualElement pill = Ui.Box(played ? "tourn-round tourn-round-played" : "tourn-round",
                    Ui.Text("R" + r, "tourn-round-label", Ui.Medium),
                    Ui.Text(played ? J.Num(row, "r" + r + "_score").ToString("0") : "-", "tourn-round-score", Ui.Bold));
                int num = r;
                if (played) pill.OnTap(() => OpenRound(num, userId));
                rounds.Add(pill);
            }

            string key = userId.Length > 0 ? userId : name + place;
            // -app2TournExpand: the leader's row starts open; -app2TournRound: and its round 1 opens (for checking from a script).
            if (place == 1 && !_scripted && Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TournExpand") >= 0)
            {
                _scripted = true;
                _open.Add(key);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TournRound") >= 0) OpenRound(1, userId);
            }
            VisualElement line = Ui.Box(_open.Contains(key) ? "tourn-leader tourn-leader-open" : "tourn-leader", head, rounds);
            if (userId == Auth.Uid) line.AddToClassList("board-me");
            head.OnTap(() =>
            {
                bool open = !_open.Remove(key);
                if (open) _open.Add(key);
                line.EnableInClassList("tourn-leader-open", open);
                Perf.Wake(0.5f); // keep drawing through the height transition
            });
            return line;
        }

        /// <summary>pressLeaderboardRound: that Player's games in this tournament, then the one for the round.</summary>
        void OpenRound(int round, string userId)
        {
            if (userId.Length == 0) return;
            void Go(List<object> games)
            {
                foreach (object game in games)
                    if (J.Int(game, "roundNum") == round && J.Str(game, "id").Length > 0) { _app.Push(new GameViewScreen(_app, J.Str(game, "id"))); return; }
                _app.Toast("That round's game is not available.");
            }
            if (_userGames.TryGetValue(userId, out List<object> have)) { Go(have); return; }
            App.Run(Api.Get("/tournaments/userGames/" + _id + "/" + userId, data => { List<object> games = J.List(data); _userGames[userId] = games; Go(games); },
                why => _app.Toast(why == "offline" ? "No connection. Try again." : "Could not open that round.")));
        }

        /// <summary>A tap on this child does not also open or close the row under it.</summary>
        static void Swallow(VisualElement element)
        {
            element.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            element.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            element.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
        }

        void Join()
        {
            if (_busy || _joined) return; // the API never answers a second join
            _busy = true;
            App.Run(Api.Get("/tournaments/join/" + _id, _ => { _busy = false; _joined = true; _app.Toast("You are in. Good luck."); Shown(); },
                why => { _busy = false; _app.Toast(why == "offline" ? "No connection. Try again." : "Could not join. Try again in a moment."); }));
        }
    }
}
