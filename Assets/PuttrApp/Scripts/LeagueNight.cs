// League Night, from the live API as puttr-app shows it: the card on Home, the leaderboard
// (teams or Players, for the season or any week), picking a team, and playing the week's game.
//
// Six regional teams. Each week every Player may post one score a day; a Player's best of the
// week counts, a team's week is its five best Players, and the season is the weeks added up.
//
// Not brought over yet: a team's own page, the weekly recap and season finale videos, past
// seasons, fist bumps and opening another Player's round.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class League
    {
        public sealed class Team
        {
            public string Id, Name, Region, Motto;
            public Color Colour;
        }

        // puttr-api/src/leagueTeams.js
        public static readonly Team[] Teams =
        {
            new Team { Id = "lions", Name = "The Lions", Region = "United Kingdom", Motto = "For Crown & Country", Colour = new Color32(0xC8, 0x10, 0x2E, 0xFF) },
            new Team { Id = "mounties", Name = "The Mounties", Region = "Canada", Motto = "True North, Strong & Free", Colour = new Color32(0xFF, 0x2A, 0x2A, 0xFF) },
            new Team { Id = "noreasters", Name = "The Nor'easters", Region = "US Northeast", Motto = "Built To Weather Anything", Colour = new Color32(0x1E, 0x5A, 0xA8, 0xFF) },
            new Team { Id = "renegades", Name = "The Renegades", Region = "US Southeast", Motto = "Run It Hot", Colour = new Color32(0xFF, 0x6B, 0x1A, 0xFF) },
            new Team { Id = "blizzards", Name = "The Blizzards", Region = "US Midwest", Motto = "Cold-Blooded", Colour = new Color32(0x4F, 0xC3, 0xF7, 0xFF) },
            new Team { Id = "mavericks", Name = "The Mavericks", Region = "US West", Motto = "Rules Optional", Colour = new Color32(0xB2, 0x47, 0xE8, 0xFF) },
        };

        public static Team TeamOf(string id)
        {
            foreach (Team team in Teams) if (team.Id == id) return team;
            return null;
        }

        /// <summary>A week's times are wall-clock strings with no zone; the app reads them as the device's own, and so does this.</summary>
        public static DateTime? Time(object week, string key)
        {
            string text = J.Str(week, key);
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime at) ? at : (DateTime?)null;
        }

        /// <summary>The week being played: the first that has not ended, else the last.</summary>
        public static object CurrentWeek(object season, DateTime now)
        {
            List<object> weeks = J.List(J.Get(season, "weeks"));
            foreach (object week in weeks)
            {
                DateTime? ends = Time(week, "endsAt");
                if (ends != null && ends.Value > now) return week;
            }
            return weeks.Count > 0 ? weeks[weeks.Count - 1] : null;
        }

        public static bool Live(object week, DateTime now)
        {
            DateTime? starts = Time(week, "startsAt"), ends = Time(week, "endsAt");
            return starts != null && ends != null && now >= starts.Value && now <= ends.Value;
        }

        /// <summary>"3d left" while a day or more remains, then "HH:MM:SS left"; "Ended" after.</summary>
        public static string Countdown(DateTime? ends, DateTime now)
        {
            if (ends == null) return "";
            TimeSpan left = ends.Value - now;
            if (left.TotalSeconds <= 0) return "Ended";
            if (left.TotalDays >= 1) return (int)left.TotalDays + "d left";
            return ((int)left.TotalHours).ToString("00") + ":" + left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00") + " left";
        }

        public static string Ordinal(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");
        public static string Points(double n) => n.ToString(n % 1 == 0 ? "#,0" : "#,0.##", CultureInfo.InvariantCulture);

        /// <summary>The week's standings name only teams with a score: the rest are added, unranked, so all six show.</summary>
        public static List<object> AllSix(List<object> standings)
        {
            var all = new List<object>(standings);
            foreach (Team team in Teams)
            {
                bool there = false;
                foreach (object row in standings) if (J.Str(row, "team") == team.Id) there = true;
                if (!there) all.Add(new Dictionary<string, object> { { "team", team.Id }, { "total", 0.0 } });
            }
            return all;
        }

        /// <summary>The Player's team (or null), the season (or null), and this week's contribution, read together.</summary>
        public static void Load(Action<string, object, object> done)
        {
            string team = null;
            object season = null;
            int waiting = 2;
            void Then()
            {
                if (--waiting > 0) return;
                object week = season != null ? CurrentWeek(season, DateTime.Now) : null;
                if (team == null || week == null) { done(team, season, null); return; }
                App.Run(Api.Get("/league/me/contribution?seasonId=" + J.Str(season, "id") + "&weekNumber=" + J.Int(week, "weekNumber"),
                    mine => done(team, season, mine), _ => done(team, season, null)));
            }
            App.Run(Api.Get("/league/me", data => { team = J.Str(data, "team", null); Then(); }, _ => Then())); // 404: no team yet
            App.Run(Api.Get("/league/season/current", data => { season = data; Then(); }, _ => Then()));        // 404: between seasons
        }

        /// <summary>The team's crest (puttr-app assets/images/league/*.svg, drawn to Resources/PuttrApp/League), or its letter.</summary>
        public static VisualElement Crest(Team team, float size)
        {
            Texture2D art = team != null ? Resources.Load<Texture2D>("PuttrApp/League/" + team.Id) : null;
            if (art != null)
            {
                VisualElement image = Ui.Box("crest-image");
                image.style.width = image.style.height = size;
                image.style.flexShrink = 0;
                image.style.backgroundImage = new StyleBackground(art);
                return image;
            }
            string letter = team == null ? "?" : team.Name.Replace("The ", "").Substring(0, 1);
            Label mark = Ui.Text(letter, "crest-letter", Ui.Bold);
            mark.style.fontSize = size * 0.46f;
            VisualElement crest = Ui.Box("crest", mark);
            crest.style.width = crest.style.height = size;
            crest.style.borderTopLeftRadius = crest.style.borderTopRightRadius = crest.style.borderBottomLeftRadius = crest.style.borderBottomRightRadius = size * 0.28f;
            crest.style.backgroundColor = team != null ? Color.Lerp(Ui.Ink, team.Colour, 0.85f) : (Color)new Color32(38, 40, 42, 255);
            return crest;
        }
    }

    public static partial class Compete
    {
        static partial void LeagueCard(App app, VisualElement slot)
        {
            League.Load((teamId, season, mine) =>
            {
                slot.Clear();
                if (season == null) return; // between seasons: Home says nothing
                object week = League.CurrentWeek(season, DateTime.Now);
                League.Team team = League.TeamOf(teamId);
                int weeks = J.List(J.Get(season, "weeks")).Count, number = J.Int(week, "weekNumber");
                bool live = League.Live(week, DateTime.Now);
                slot.Add(Ui.Section("League Night"));

                // The whole card opens the scoreboards; its button plays.
                VisualElement card = Ui.Box("glass").OnTap(() => app.Push(new LeagueScreen(app)));
                Label clock = Ui.Text("", "chip chip-amber", Ui.Bold);
                void Tick() => clock.text = live ? League.Countdown(League.Time(week, "endsAt"), DateTime.Now) : "Week " + number + " locked";
                Tick();
                clock.schedule.Execute(Tick).Every(1000);

                if (team == null)
                {
                    card.Add(Ui.Box("row", Ui.Box("grow", Ui.Text("Six teams. One trophy.", "h2", Ui.Bold), Ui.Text("Week " + number + " of " + weeks + ". Pick your regional team and your scores start counting.", "sub", Ui.Regular).Margin(4))));
                    card.Add(Ui.Box("chips", clock));
                    card.Add(Ui.Button("Pick your team", () => app.Push(new LeagueJoinScreen(app))));
                }
                else
                {
                    double best = J.Num(mine, "bestScore");
                    string standing = best > 0 ? "Your best this week: " + League.Points(best) + (J.Get(mine, "teamRank") != null ? "   ·   #" + J.Int(mine, "teamRank") + " on the team" : "") : "No score yet this week";
                    card.Add(Ui.Box("row", League.Crest(team, 48), FreePuttScreen.Space(0), Ui.Box("grow crest-words", Ui.Text(team.Name, "h2", Ui.Bold), Ui.Text(standing, "sub", Ui.Regular))));
                    VisualElement chips = Ui.Box("chips", Ui.Text("Week " + number + " of " + weeks, "chip"), clock);
                    if (J.Flag(mine, "inTopFive")) chips.Add(Ui.Text("In the top 5", "chip chip-green"));
                    card.Add(chips);
                    if (!live) card.Add(Ui.Text("Results drop Monday at 8pm.", "muted", Ui.Regular).Margin(10));
                    else if (J.Flag(mine, "playedToday")) card.Add(Ui.Box("btn btn-ghost btn-off", Ui.Text("Played today. Come back tomorrow", "btn-text", Ui.Bold)));
                    else card.Add(Ui.Button("Play this week", () => app.Games.PlayLeague(season, week, team.Id)));
                }
                slot.Add(Ui.Box("pad", card));
            });
        }
    }

    public sealed class LeagueScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _head, _pickers, _body, _foot;
        string _team;
        object _season, _mine;
        int _scope = -1;        // 0 season, n week n; -1 until the season is known
        bool _players;
        bool _wide;             // landscape: Teams and Players side by side, no toggle
        int _draws;             // which Draw a board's answer belongs to

        public LeagueScreen(App app)
        {
            _app = app;
            _head = new VisualElement(); _pickers = new VisualElement(); _body = new VisualElement(); _foot = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("League Night", app.Pop));
            scroll.Add(Ui.Box("pad", _head, _pickers));
            scroll.Add(_body);
            scroll.Add(Ui.Box("pad", _foot));
            var end = new VisualElement();
            end.style.height = 28;
            scroll.Add(end);
            Root = scroll;
            _wide = Screen.width > Screen.height;
            // A turn of the device: both boards side by side in landscape, the toggle back in portrait.
            scroll.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                bool wide = Screen.width > Screen.height;
                if (wide == _wide) return;
                _wide = wide;
                if (_season != null && _scope >= 0) Draw();
            });
        }

        // Read again whenever the page comes to the front: a game just played moves the board.
        public override void Shown()
        {
            if (Kid.Blocked(_app, Kid.League, _body)) { _head.Clear(); _pickers.Clear(); _foot.Clear(); return; }
            League.Load((team, season, mine) =>
            {
                _team = team; _season = season; _mine = mine;
                if (season == null) { _head.Clear(); _head.Add(Ui.Box("glass", Ui.Text("Between seasons. The next League Night season has not started yet.", "sub", Ui.Regular))); return; }
                if (_scope < 0)
                {
                    object week = League.CurrentWeek(season, DateTime.Now);
                    _scope = League.Live(week, DateTime.Now) ? J.Int(week, "weekNumber") : 0;
                }
                Draw();
            });
        }

        object Week(int number)
        {
            foreach (object week in J.List(J.Get(_season, "weeks"))) if (J.Int(week, "weekNumber") == number) return week;
            return null;
        }

        void Draw()
        {
            List<object> weeks = J.List(J.Get(_season, "weeks"));
            object current = League.CurrentWeek(_season, DateTime.Now), shown = _scope > 0 ? Week(_scope) : null;
            DateTime now = DateTime.Now;

            _head.Clear();
            string when = shown == null
                ? "Season " + J.Int(_season, "number") + "   ·   Week " + J.Int(current, "weekNumber") + " of " + weeks.Count
                : J.Str(shown, "gameTitle", "Week " + _scope) + (League.Live(shown, now) ? "   ·   " + League.Countdown(League.Time(shown, "endsAt"), now) : "");
            _head.Add(Ui.Text(when, "sub", Ui.Regular));

            _pickers.Clear();
            ScrollView scopes = Ui.Scroll(true);
            scopes.style.marginTop = 10;
            scopes.Add(Pill("Season", _scope == 0, () => { _scope = 0; Draw(); }));
            foreach (object week in weeks)
            {
                int number = J.Int(week, "weekNumber");
                DateTime? starts = League.Time(week, "startsAt");
                VisualElement pill = Pill("Week " + number + (League.Live(week, now) ? "  ●" : ""), _scope == number, () => { _scope = number; Draw(); });
                if (starts != null && starts.Value > now) pill.style.opacity = 0.45f;
                scopes.Add(pill);
            }
            _pickers.Add(scopes);
            _wide = Screen.width > Screen.height;
            if (!_wide) _pickers.Add(FreePuttScreen.Choice(new[] { "Teams", "Players" }, _players ? 1 : 0, i => { _players = i == 1; Draw(); }));

            _body.Clear();
            DateTime? begins = shown != null ? League.Time(shown, "startsAt") : null;
            if (begins != null && begins.Value > now)
                _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("Week " + _scope + " has not started. It opens " + begins.Value.ToString("MMM d", CultureInfo.InvariantCulture) + ".", "sub", Ui.Regular)).Margin(12)));
            else if (_wide)
            {
                // Landscape: the two boards at half the width each.
                int draw = ++_draws;
                _body.Add(Ui.Box("pad league-split",
                    Ui.Box("league-half league-half-l", Ui.Text("Teams", "sec-title league-half-title", Ui.Bold), Board(false, draw)),
                    Ui.Box("league-half league-half-r", Ui.Text("Players", "sec-title league-half-title", Ui.Bold), Board(true, draw))));
            }
            else _body.Add(Ui.Box("pad", Board(_players, ++_draws)));

            // What the Player can do.
            _foot.Clear();
            League.Team mine = League.TeamOf(_team);
            if (mine == null) _foot.Add(Ui.Button("Pick your team", () => _app.Push(new LeagueJoinScreen(_app))));
            else if (League.Live(current, now))
            {
                double best = J.Num(_mine, "bestScore");
                _foot.Add(Ui.Text(best > 0 ? "Your best this week: " + League.Points(best) + (J.Get(_mine, "teamRank") != null ? "   ·   #" + J.Int(_mine, "teamRank") + " on " + mine.Name : "") : "No score yet this week", "sub center", Ui.Regular).Margin(14));
                if (J.Flag(_mine, "playedToday")) _foot.Add(Ui.Box("btn btn-ghost btn-off", Ui.Text("Played today. Come back tomorrow", "btn-text", Ui.Bold)));
                else _foot.Add(Ui.Button("Play this week", () => _app.Games.PlayLeague(_season, current, mine.Id)));
            }
            else _foot.Add(Ui.Text("This week is locked. Results drop Monday at 8pm.", "sub center", Ui.Regular).Margin(14));

            // A finished week has its recap; every season before this one is in the archive.
            string recapSeason = J.Str(_season, "id");
            int recapWeek = _scope;
            if (shown != null && League.Time(shown, "endsAt") is DateTime ended && ended < now)
                _foot.Add(Ui.Button("Week " + recapWeek + " recap", () => _app.Push(new LeagueRecapScreen(_app, recapSeason, recapWeek)), "btn btn-ghost").Margin(10));
            _foot.Add(Ui.Text("Past seasons  ›", "sec-link center", Ui.Medium).Margin(14).OnTap(() => _app.Push(new LeagueSeasonsScreen(_app))));
        }

        /// <summary>One board, Teams or Players, for the scope shown; filled when the API answers.</summary>
        VisualElement Board(bool players, int draw)
        {
            string path = "/league/season/" + J.Str(_season, "id") + (_scope > 0 ? "/week/" + _scope : "") + (players ? "/leaderboard?scope=global&limit=100" : "/standings");
            VisualElement card = Ui.Box("glass", Spinner.Small());
            card.style.marginTop = 12;
            App.Run(Api.Get(path, data => { if (draw == _draws) Rows(card, J.List(data), players); },
                _ => { card.Clear(); card.Add(Ui.Text("Could not load the board. Pull back and try again.", "sub", Ui.Regular)); }));
            return card;
        }

        static VisualElement Pill(string text, bool on, Action tap) =>
            Ui.Box(on ? "scope scope-on" : "scope", Ui.Text(text, "scope-text", Ui.Bold)).OnTap(tap);

        void Rows(VisualElement card, List<object> rows, bool players)
        {
            card.Clear();
            if (players)
            {
                if (rows.Count == 0) { card.Add(Ui.Text(_scope > 0 ? "No Player scores posted yet this week." : "No Player scores posted yet this season.", "sub", Ui.Regular)); return; }
                foreach (object row in rows)
                {
                    object player = J.Get(row, "player");
                    League.Team team = League.TeamOf(J.Str(row, "team"));
                    string name = J.Str(player, "displayName", J.Str(player, "username", "A Player"));
                    bool me = J.Str(player, "userId") == Auth.Uid;
                    int games = J.Int(row, "attempts");
                    Label place = Ui.Text(J.Get(row, "rank") != null ? J.Int(row, "rank").ToString() : "–", "board-place", Ui.Bold);
                    if (team != null) place.style.color = team.Colour;
                    string under = J.Str(player, "homeTown");
                    if (games > 0) under += (under.Length > 0 ? "   ·   " : "") + games + (games == 1 ? " game" : " games");
                    VisualElement line = Ui.Box("board-row", place, Ui.Avatar(name, J.Str(player, "photoURL")),
                        Ui.Box("grow", Ui.Text(name + (me ? "   ·   you" : ""), "feed-name", Ui.Bold), Ui.Text(under, "muted", Ui.Regular)),
                        PutterBrands.RowBadge(row), // their putter's brand: a tap opens the putter sheet
                        Ui.Text(League.Points(J.Num(row, "score")), "feed-score", Ui.Bold));
                    if (me) line.AddToClassList("board-me");
                    // A week's row is that Player's game; the season's, the Player.
                    string game = J.Str(row, "userGameId"), userId = J.Str(player, "userId");
                    object who = player;
                    if (_scope > 0 && game.Length > 0) line.OnTap(() => _app.Push(new GameViewScreen(_app, game)));
                    else line.OnTap(() => ProfileScreen.Open(_app, userId, who));
                    card.Add(line);
                }
                return;
            }
            rows = League.AllSix(rows);
            foreach (object row in rows)
            {
                League.Team team = League.TeamOf(J.Str(row, "team"));
                if (team == null) continue;
                object rank = J.Get(row, _scope > 0 ? "weekRank" : "seasonRank") ?? J.Get(row, "weekRank");
                double total = J.Num(row, _scope > 0 ? "total" : "seasonTotal", J.Num(row, "total"));
                int change = J.Int(row, "weekRankChange");
                string under = team.Region + (J.Get(row, "weekRankChange") != null && change != 0 ? "   ·   " + (change > 0 ? "up " : "down ") + Math.Abs(change) : "");
                VisualElement line = Ui.Box("board-row", Ui.Text(rank != null ? PuttrMath(rank) : "–", "board-place", Ui.Bold), League.Crest(team, 40), FreePuttScreen.Space(0),
                    Ui.Box("grow crest-words", Ui.Text(team.Name, "feed-name", Ui.Bold), Ui.Text(under, "muted", Ui.Regular)),
                    Ui.Text(total > 0 ? League.Points(total) : "–", "feed-score", Ui.Bold));
                if (team.Id == _team) line.AddToClassList("board-me");
                League.Team tapped = team;
                string sid = J.Str(_season, "id");
                int week = _scope;
                line.OnTap(() => _app.Push(new LeagueTeamScreen(_app, tapped, sid, week)));
                card.Add(line);
            }
        }

        static string PuttrMath(object rank) => ((int)Puttr.PuttrJson.Number(rank, 0)).ToString();
    }

    /// <summary>Choosing a team. It is for the season: the API will not move a Player once they have joined.</summary>
    public sealed class LeagueJoinScreen : AppScreen
    {
        readonly App _app;
        readonly ScrollView _scroll;
        string _chosen;
        bool _busy;

        public LeagueJoinScreen(App app)
        {
            _app = app;
            _scroll = Ui.Scroll();
            Root = _scroll;
            Draw();
        }

        void Draw()
        {
            _scroll.Clear();
            _scroll.Add(Ui.Header("", _app.Pop));
            VisualElement grid = Ui.Box("team-grid");
            foreach (League.Team team in League.Teams)
            {
                League.Team pick = team;
                VisualElement cell = Ui.Box(team.Id == _chosen ? "glass team-cell team-on" : "glass team-cell", League.Crest(team, 56),
                    Ui.Text(team.Name, "feed-name center", Ui.Bold).Margin(8), Ui.Text(team.Region, "muted center", Ui.Regular), Ui.Text(team.Motto, "muted center", Ui.Regular));
                if (team.Id == _chosen) cell.style.borderTopColor = cell.style.borderBottomColor = cell.style.borderLeftColor = cell.style.borderRightColor = team.Colour;
                grid.Add(Ui.Box("team-slot", cell.OnTap(() => { _chosen = pick.Id; Draw(); })));
            }
            League.Team chosen = League.TeamOf(_chosen);
            _scroll.Add(Ui.Box("pad",
                Ui.Text("Pick your team", "h1", Ui.Bold),
                Ui.Text("Six regions. One season. Your team competes every week, and your best score counts.", "sub", Ui.Regular),
                grid,
                Ui.Text("Locked for the season. You can switch teams next season.", "muted center", Ui.Regular).Margin(12),
                chosen == null ? Ui.Box("btn btn-off", Ui.Text("Select a team", "btn-text", Ui.Bold)) : Ui.Button("Join " + chosen.Name, () => Club.Require(_app, "League Night", Confirm))));
        }

        // One more look before it is final.
        void Confirm()
        {
            League.Team team = League.TeamOf(_chosen);
            if (team == null || _busy) return;
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal",
                Ui.Text("Lock in " + team.Name + "?", "h2", Ui.Bold),
                Ui.Text("You play for them for the whole season. This cannot be changed until the next one.", "sub", Ui.Regular).Margin(6),
                Ui.Button("Lock it in", () => { dim.RemoveFromHierarchy(); Join(team); }),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            Root.parent.Add(dim);
        }

        void Join(League.Team team)
        {
            _busy = true;
            App.Run(Api.Post("/league/me/join", new Dictionary<string, object> { { "team", team.Id } }, _ =>
            {
                _busy = false;
                _app.Pop();
                _app.Toast("You are one of " + team.Name + ". Play this week to put a score on the board.");
            }, why =>
            {
                _busy = false;
                _app.Toast(why == "http_409" ? "You are already on a team this season." : why == "offline" ? "No connection. Try again." : "Could not join. Try again in a moment.");
            }));
        }
    }
}
