// League Night, from the live API as puttr-app shows it: the card on Home, the leaderboard
// (teams or Players, for the season or any week), picking a team, and playing the week's game.
//
// Six regional teams. Each week every Player may post one score a day; a Player's best of the
// week counts, a team's week is its five best Players, and the season is the weeks added up.
//
// Each week has its own game (the season's weeks carry gameId, gameType and gameTitle). LeagueRound
// plays it: as the game itself where this build has its rules (Center Cut), as a putt list on the ramp
// where the game is one, and otherwise it tells the Player the game cannot be played here.
//
// The teams are whatever the API sends (League.Learn); the six this build was made with, and their
// crests, are what it falls back on.
//
// A team's own page, the recaps, past seasons and the finale are in LeagueMore.cs. Not brought over
// yet: fist bumps.
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
            /// <summary>The crest's picture on the web, when the API sends one. A team this build has art for uses that art.</summary>
            public string CrestUrl;
            public Color Colour;
        }

        // puttr-api/src/leagueTeams.js, as this build was made: what is shown until the API says otherwise, and
        // for any team the API names without describing.
        static readonly Team[] Bundled =
        {
            new Team { Id = "lions", Name = "The Lions", Region = "United Kingdom", Motto = "For Crown & Country", Colour = new Color32(0xC8, 0x10, 0x2E, 0xFF) },
            new Team { Id = "mounties", Name = "The Mounties", Region = "Canada", Motto = "True North, Strong & Free", Colour = new Color32(0xFF, 0x2A, 0x2A, 0xFF) },
            new Team { Id = "noreasters", Name = "The Nor'easters", Region = "US Northeast", Motto = "Built To Weather Anything", Colour = new Color32(0x1E, 0x5A, 0xA8, 0xFF) },
            new Team { Id = "renegades", Name = "The Renegades", Region = "US Southeast", Motto = "Run It Hot", Colour = new Color32(0xFF, 0x6B, 0x1A, 0xFF) },
            new Team { Id = "blizzards", Name = "The Blizzards", Region = "US Midwest", Motto = "Cold-Blooded", Colour = new Color32(0x4F, 0xC3, 0xF7, 0xFF) },
            new Team { Id = "mavericks", Name = "The Mavericks", Region = "US West", Motto = "Rules Optional", Colour = new Color32(0xB2, 0x47, 0xE8, 0xFF) },
        };

        static List<Team> _teams = new List<Team>(Bundled);
        // Teams the API named in a row (a standing, a score, a champion) but that are in no list this build has.
        static readonly Dictionary<string, Team> Strangers = new Dictionary<string, Team>();

        /// <summary>The season's teams: the API's list once it has sent one (Learn), until then the six this build was made with.</summary>
        public static IReadOnlyList<Team> Teams => _teams;

        /// <summary>
        /// The team with this id, or null when there is no id. An id that is in no list still gets a team (named
        /// after the id, no region, a letter for a crest), so a team the API adds shows on the boards instead of
        /// being left off them.
        /// </summary>
        public static Team TeamOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (Team team in _teams) if (team.Id == id) return team;
            foreach (Team team in Bundled) if (team.Id == id) return team;
            if (!Strangers.TryGetValue(id, out Team made))
                Strangers[id] = made = new Team { Id = id, Name = "The " + char.ToUpperInvariant(id[0]) + id.Substring(1), Region = "", Motto = "", Colour = Ui.Bright };
            return made;
        }

        /// <summary>
        /// Takes the teams from an API answer: a `teams` list of {id, name, region, motto, color, crest} on the
        /// season (or on any league answer). Whatever a team leaves out is filled from the team of the same id this
        /// build was made with. An answer with no teams changes nothing.
        /// </summary>
        public static void Learn(object answer)
        {
            var read = new List<Team>();
            foreach (object row in J.List(J.Get(answer, "teams")))
            {
                string id = J.Str(row, "id", J.Str(row, "team"));
                if (id.Length == 0 || read.Exists(t => t.Id == id)) continue;
                Team had = null;
                foreach (Team team in Bundled) if (team.Id == id) had = team;
                var made = new Team
                {
                    Id = id,
                    Name = J.Str(row, "name", had != null ? had.Name : "The " + char.ToUpperInvariant(id[0]) + id.Substring(1)),
                    Region = J.Str(row, "region", had != null ? had.Region : ""),
                    Motto = J.Str(row, "motto", had != null ? had.Motto : ""),
                    CrestUrl = J.Str(row, "crest", J.Str(row, "crestUrl", J.Str(row, "logo"))),
                    Colour = had != null ? had.Colour : (Color)Ui.Bright,
                };
                string hex = J.Str(row, "color", J.Str(row, "colour"));
                if (hex.Length > 0 && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out Color colour)) made.Colour = colour;
                read.Add(made);
            }
            if (read.Count > 0) _teams = read;
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

        // ── Which team a Player is likely a fan of (LeagueOnboarding's region detect, constants/regionMapping.js) ──

        static readonly string[] Northeast = { "CT", "ME", "MA", "NH", "NJ", "NY", "PA", "RI", "VT", "DE", "MD", "DC" };
        static readonly string[] Southeast = { "AL", "AR", "FL", "GA", "KY", "LA", "MS", "NC", "SC", "TN", "VA", "WV", "PR", "VI" };
        static readonly string[] Midwest = { "IL", "IN", "IA", "KS", "MI", "MN", "MO", "NE", "ND", "OH", "SD", "WI" };
        static readonly string[] West = { "AK", "AZ", "CA", "CO", "HI", "ID", "MT", "NV", "NM", "OK", "OR", "TX", "UT", "WA", "WY" };
        static readonly string[] Provinces = { "AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT" };

        /// <summary>
        /// The team for a region (regionToTeam): GB or UK the Lions, CA the Mounties, and in the US the team of the state's
        /// Census region. The app reads the phone's country and leaves a US Player without a guess until it knows the
        /// state; here the state comes from the profile's home town ("Austin, TX"), so a US Player gets one too. With no
        /// country, a home town ending in a US state, a Canadian province or a UK nation still names a team. Null when
        /// nothing is confident.
        /// </summary>
        public static string Suggest(string country, string homeTown)
        {
            string cc = (country ?? "").Trim().ToUpperInvariant();
            string town = homeTown ?? "";
            int comma = town.LastIndexOf(',');
            string last = comma >= 0 ? town.Substring(comma + 1).Trim() : "";
            string code = last.ToUpperInvariant();
            if (cc == "GB" || cc == "UK") return "lions";
            if (cc == "CA") return "mounties";
            if (cc == "US") return StateTeam(code);
            if (cc.Length == 2 && cc != "IV") return null;   // another country: no team is theirs (IV: no region known)
            // No country from the phone: what the home town says.
            string state = StateTeam(code);
            if (state != null) return state;
            if (Array.IndexOf(Provinces, code) >= 0 || code == "CANADA") return "mounties";
            string lower = last.ToLowerInvariant();
            if (lower == "uk" || lower == "united kingdom" || lower == "england" || lower == "scotland" || lower == "wales" || lower == "northern ireland") return "lions";
            return null;
        }

        static string StateTeam(string state)
        {
            if (Array.IndexOf(Northeast, state) >= 0) return "noreasters";
            if (Array.IndexOf(Southeast, state) >= 0) return "renegades";
            if (Array.IndexOf(Midwest, state) >= 0) return "blizzards";
            if (Array.IndexOf(West, state) >= 0) return "mavericks";
            return null;
        }

        /// <summary>The phone's country (ISO two letters), or "" when it cannot be read.</summary>
        public static string DeviceCountry()
        {
            try { return RegionInfo.CurrentRegion.TwoLetterISORegionName ?? ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>
        /// LeagueRulesModal, the "?" sheet: League Night and its four rules. A Player with no team gets Join a Team under
        /// them (the Home card's empty state and the leaderboard's "?" before joining).
        /// </summary>
        public static void Help(App app, VisualElement over, bool join)
        {
            VisualElement dim = null;
            // Plain stroke icons, no tinted disc behind them (docs/design-guidelines.md).
            VisualElement Rule(string icon, string title, string words) =>
                Ui.Box("lg-help-row", Ui.Box("lg-help-disc", Ui.Icon(icon, 24, Color.white)), Ui.Box("grow lg-help-words", Ui.Text(title, "lg-help-t", Ui.Bold), Ui.Text(words, "lg-help-d", Ui.Medium)));
            VisualElement card = Ui.Box("modal lg-help",
                Ui.Box("lg-help-bar", Ui.Title("League Night", "title-20 grow"), Ui.Box("lg-help-x", Ui.Icon("X", 20, Ui.Faint)).OnTap(() => dim.RemoveFromHierarchy())),
                Ui.Text("Join a regional team and compete in weekly matches. Only your best score counts.", "lg-help-d lg-help-lead", Ui.Medium),
                Ui.Box("lg-help-line"),
                Rule("Target", "Weekly matches", "A new game drops every week. You can play it once per day, and your best score for the week counts."),
                Rule("UsersThree", "Team of 5", "Your team's top 5 scores combine for a weekly total. Every putt helps your region climb the standings."),
                Rule("CalendarBlank", "4-week season", "Seasons run 4 weeks with standings, recaps, and a finale. Jump in any week. It's never too late."),
                Rule("ListNumbers", "Standings & recaps", "See how your team stacks up each week. Results drop every Monday with full recaps."));
            if (join && !Tv.On)
            {
                card.Add(Ui.Button("Join a Team", () => { dim.RemoveFromHierarchy(); app.Push(new LeagueJoinScreen(app)); }));
            }
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            // The layer is the app's stage, outside any page: the sheet brings its own styles.
            if (Sheet != null) dim.styleSheets.Add(Sheet);
            App.Layer(over).Add(dim);
        }

        public static string Ordinal(int n) => n + (n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th");
        public static string Points(double n) => n.ToString(n % 1 == 0 ? "#,0" : "#,0.##", CultureInfo.InvariantCulture);

        /// <summary>The week's standings name only teams with a score: the rest are added, unranked, so every team shows.</summary>
        public static List<object> AllSix(List<object> standings)
        {
            var all = new List<object>(standings);
            foreach (Team team in _teams)
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
            Action<string, object, object> told = done;
            done = (t, s, m) => { LastTeam = t; LastSeason = s; LastMine = m; Loaded = true; told(t, s, m); };
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
            App.Run(Api.Get("/league/season/current", data => { season = data; Learn(data); Then(); }, _ => Then()));        // 404: between seasons
        }

        // ── What was read last, so a League page comes back drawn and is refreshed in place (Matt, 2026-11-17) ──

        /// <summary>The last Load's answer: the Player's team, the season and this week's contribution.</summary>
        public static string LastTeam;
        public static object LastSeason, LastMine;
        public static bool Loaded;
        /// <summary>The last answer for each board path (standings, leaderboards, a team's scores).</summary>
        public static readonly Dictionary<string, List<object>> Cache = new Dictionary<string, List<object>>();

        static StyleSheet _sheet;
        /// <summary>League.uss: the board rows and the team page, after puttr-app's LeagueLeaderboard and LeagueTeamDetail.</summary>
        public static StyleSheet Sheet => _sheet != null ? _sheet : _sheet = Resources.Load<StyleSheet>("PuttrApp/League");

        /// <summary>
        /// Reads a board: the kept rows at once (or placeholder rows in the slot, never the screen spinner), then the
        /// fresh answer in place. fill gets the rows each time; failed only runs when there was nothing to show.
        /// </summary>
        public static void Board(string path, VisualElement slot, Action<List<object>> fill, Action failed, Func<bool> current = null)
        {
            bool had = Cache.TryGetValue(path, out List<object> kept);
            if (had) fill(kept);
            else { slot.Clear(); Skeleton.Hold(slot, Placeholder(6)); }
            App.Run(Api.Get(path, data =>
            {
                List<object> rows = J.List(data);
                Cache[path] = rows;
                if (current == null || current()) fill(rows);
            }, _ => { if (!had && (current == null || current())) failed(); }));
        }

        /// <summary>Row-shaped placeholders: a rank, a disc, two bars and a score.</summary>
        public static VisualElement Placeholder(int count)
        {
            VisualElement box = Ui.Box("lg-sk");
            for (int i = 0; i < count; i++)
                box.Add(Ui.Box(i == 0 ? "lg-row lg-first" : "lg-row", Ui.Box("lg-rank"), Skeleton.Circle(40), Ui.Box("grow lg-words", Skeleton.Bar(0.5f + 0.07f * (i % 4), 12f), Skeleton.Bar(0.3f, 9f).Margin(6)), Ui.Box("lg-sk-score", Skeleton.Bar(1f, 16f))));
            return box;
        }

        /// <summary>Who a board row is: `player`, else the userName JSON (read as the club board reads it).</summary>
        public static object PlayerOf(object row)
        {
            object p = J.Get(row, "player");
            if (p is Dictionary<string, object>) return p;
            return GameLeaderboardScreen.Player(row) ?? row;
        }

        /// <summary>A team's tint: its colour at this share over the page, worked out solid (the app renders in linear colour).</summary>
        public static Color Tint(Team team, float alpha) => Color.Lerp(Ui.Ink, team != null ? team.Colour : Ui.Bright, alpha);   // solid: see the note at the top of App.uss

        /// <summary>LeagueLeaderboard's TeamRow: a big rank, the crest, name and region, the total with its rank change, a caret.
        /// The Player's own team's row is filled a step lighter (surface-2). Rows share one bordered block (.lg-board).</summary>
        public static VisualElement TeamRow(Team team, object rank, double total, object change, bool season, bool mine, Action tap)
        {
            Label place = Ui.Num(rank != null ? ((int)Puttr.PuttrJson.Number(rank, 0)).ToString() : "\u2013", "lg-team-rank");
            if (rank == null) place.style.color = Ui.Faint;
            VisualElement right = Ui.Box("lg-right", Ui.Num(total > 0 ? Points(total) : "\u2013", "lg-team-total"));
            if (change != null)
            {
                int n = (int)Puttr.PuttrJson.Number(change, 0);
                Label delta = Ui.Text(n == 0 ? "no change" : (n > 0 ? "+" : "\u2212") + Math.Abs(n) + (season ? " this wk" : " vs prev"), "lg-delta", Ui.Medium);
                delta.style.color = n == 0 ? Ui.Faint : n > 0 ? Ui.Bright : Ui.Red;
                right.Add(delta);
            }
            VisualElement row = Ui.Box("lg-row lg-team-row", place, Crest(team, 42), Ui.Box("grow lg-words", Ui.Text(team.Name, "lg-name", Ui.Bold), Ui.Text(team.Region, "lg-sub", Ui.Medium)),
                right, Ui.Icon("CaretRight", 14, Ui.Faint));
            if (mine) Mark(row);
            return row.OnTap(tap);
        }

        /// <summary>LeagueLeaderboard's PlayerRow and LeagueTeamDetail's PlayerScoreRow: the rank, the photo (with the
        /// team's crest on it where the board mixes teams), name, home town and games, the putter's brand, the score.
        /// top5: the week's five that count (a TOP 5 label in bright green).</summary>
        public static VisualElement PlayerRow(object row, Team team, string rank, bool me, bool crestOnPhoto, bool top5, string games, bool replay)
        {
            object p = PlayerOf(row);
            string name = J.Str(p, "displayName", J.Str(p, "username", J.Str(p, "name", "A Player")));
            Label place = Ui.Num(rank, "lg-player-rank");
            VisualElement photo = Ui.Box("lg-photo", Ui.Avatar(name, J.Str(p, "photoURL")));
            if (crestOnPhoto && team != null) photo.Add(Ui.Box("lg-photo-crest", Crest(team, 18)));
            VisualElement title = Ui.Box("lg-title-row", Ui.Text(name + (me ? " \u00B7 you" : ""), "lg-name lg-name-player", Ui.Bold));
            if (top5) title.Add(Ui.Caps("Top 5", "caps caps-green lg-top5"));
            VisualElement words = Ui.Box("grow lg-words", title);
            string under = J.Str(p, "homeTown");
            if (!string.IsNullOrEmpty(games)) under += (under.Length > 0 ? " \u00B7 " : "") + games;
            if (under.Length > 0) words.Add(Ui.Text(under, "lg-sub", Ui.Medium));
            VisualElement score = Ui.Box(replay ? "lg-right lg-right-replay" : "lg-right", Ui.Num(Points(J.Num(row, "score", J.Num(row, "total"))), "lg-player-score"));
            if (replay) score.Add(Ui.Icon("CaretRight", 14, Ui.Faint));
            var badgeRow = new Dictionary<string, object> { { "player", p }, { "putter", J.Get(row, "putter") }, { "putterBrand", J.Get(row, "putterBrand") } };
            VisualElement line = Ui.Box("lg-row lg-player-row", place, photo, words, PutterBrands.RowBadge(badgeRow), score);
            if (me) Mark(line);
            return line;
        }

        /// <summary>The Player's own row (or their team's): a step lighter than the block it is in. No coloured outline.</summary>
        static void Mark(VisualElement row) => row.AddToClassList("lg-mine");

        /// <summary>
        /// One chip style for every picker on the League pages (Season and weeks, Teams and Players): white when on.
        /// live: a small red dot (the week being played). later: the words in grey (a week that has not started).
        /// </summary>
        public static VisualElement Pill(string text, bool on, bool live, bool later, Action tap)
        {
            VisualElement pill = Ui.Box(on ? "scope scope-on lg-pill" : later ? "scope lg-pill lg-pill-later" : "scope lg-pill", Ui.Text((text ?? "").ToUpperInvariant(), "scope-text", Ui.Bold));
            if (live) pill.Add(Ui.Box("lg-live-dot"));
            return pill.OnTap(tap);
        }

        /// <summary>Rows share one bordered block with a divider above each but the first: call once a block is filled.</summary>
        public static void FirstRow(VisualElement block)
        {
            if (block.childCount > 0) block[0].AddToClassList("lg-first");
        }

        /// <summary>A line inside a board block when it has no rows.</summary>
        public static VisualElement Empty(string text) => Ui.Text(text, "body-2 lg-empty", Ui.Medium);

        /// <summary>A button that cannot be pressed yet: the main button's shape, dimmed.</summary>
        public static VisualElement OffButton(string text)
        {
            var button = new Slant();
            button.AddToClassList("btn");
            button.AddToClassList("slant");
            button.AddToClassList("btn-off");
            button.Add(Ui.Text((text ?? "").ToUpperInvariant(), "btn-text", Ui.Bold));
            return button;
        }

        /// <summary>The team's crest: the art this build has for it (puttr-app assets/images/league/*.svg, drawn to
        /// Resources/PuttrApp/League), else the picture the API named, else its letter.</summary>
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
            string bare = team == null ? "" : team.Name.Replace("The ", "").Trim();
            string letter = bare.Length == 0 ? "?" : bare.Substring(0, 1);
            Label mark = Ui.Text(letter, "crest-letter", Ui.Bold);
            mark.style.fontSize = size * 0.46f;
            VisualElement crest = Ui.Box("crest", mark);
            crest.style.width = crest.style.height = size;
            crest.style.borderTopLeftRadius = crest.style.borderTopRightRadius = crest.style.borderBottomLeftRadius = crest.style.borderBottomRightRadius = 4;
            crest.style.backgroundColor = team != null ? Color.Lerp(Ui.Ink, team.Colour, 0.85f) : Ui.Surface2;   // a team's own colour: data, not interface
            // No art in this build: the API's picture over the letter once it is in (the letter stays if it never comes).
            if (team != null && !string.IsNullOrEmpty(team.CrestUrl) && team.CrestUrl.StartsWith("http"))
                Images.Load(team.CrestUrl, texture =>
                {
                    if (texture == null) return;
                    crest.style.backgroundImage = new StyleBackground(texture);
                    crest.style.backgroundColor = Color.clear;
                    mark.style.display = DisplayStyle.None;
                });
            return crest;
        }
    }

    public static partial class Compete
    {
        static partial void LeagueCard(App app, VisualElement slot)
        {
            // Leagues are off for a Kid profile, and Home does not invite them in (MainScreen renderCompeteTiles).
            if (Kid.IsKid(app.Profile)) { slot.Clear(); return; }
            League.Load((teamId, season, mine) =>
            {
                slot.Clear();
                if (Kid.IsKid(app.Profile)) return;   // the profile may have arrived while the league was read
                if (season == null) return; // between seasons: Home says nothing
                object week = League.CurrentWeek(season, DateTime.Now);
                League.Team team = League.TeamOf(teamId);
                int weeks = J.List(J.Get(season, "weeks")).Count, number = J.Int(week, "weekNumber");
                bool live = League.Live(week, DateTime.Now);
                slot.Add(Ui.Section("League Night"));

                // The whole card opens the scoreboards; its button plays.
                // Home's one main button is Today's session: the buttons here are secondary.
                VisualElement card = Ui.Box("card").OnTap(() => app.Push(new LeagueScreen(app)));
                Label clock = Ui.Text("", "chip chip-amber", Ui.Bold);
                void Tick() => clock.text = live ? League.Countdown(League.Time(week, "endsAt"), DateTime.Now) : "Week " + number + " locked";
                Tick();
                clock.schedule.Execute(Tick).Every(1000);

                if (team == null)
                {
                    card.Add(Ui.Title("Six teams. One trophy.", "title-20"));
                    card.Add(Ui.Text("Week " + number + " of " + weeks + ". Pick your regional team and your scores start counting.", "body-2", Ui.Medium).Margin(4));
                    card.Add(Ui.Box("chips", clock));
                    card.Add(Ui.Secondary("Pick your team", () => app.Push(new LeagueJoinScreen(app))));
                }
                else
                {
                    double best = J.Num(mine, "bestScore");
                    string standing = best > 0 ? "Your best this week: " + League.Points(best) + (J.Get(mine, "teamRank") != null ? "   ·   #" + J.Int(mine, "teamRank") + " on the team" : "") : "No score yet this week";
                    card.Add(Ui.Box("row", League.Crest(team, 48), FreePuttScreen.Space(0), Ui.Box("grow crest-words", Ui.Title(team.Name, "title-20"), Ui.Text(standing, "body-2", Ui.Medium).Margin(4))));
                    VisualElement chips = Ui.Box("chips", Ui.Text("Week " + number + " of " + weeks, "chip"), clock);
                    string playing = J.Str(week, "gameTitle");
                    if (playing.Length > 0) chips.Insert(1, Ui.Text(playing, "chip"));   // the week's own game
                    if (J.Flag(mine, "inTopFive")) chips.Add(Ui.Text("In the top 5", "chip chip-green"));
                    card.Add(chips);
                    if (!live) card.Add(Ui.Text("Results drop Monday at 8pm.", "body-2", Ui.Medium).Margin(12));
                    else if (J.Flag(mine, "playedToday")) card.Add(Ui.Text("Played today. Come back tomorrow.", "body-2", Ui.Medium).Margin(12));
                    else if (!Tv.On) card.Add(Ui.Secondary("Play this week", () => LeagueRound.Play(app, season, week, team.Id)));   // a TV shows the scores only
                }
                slot.Add(Ui.Box("pad", card));
            });
        }
    }

    public sealed class LeagueScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _head, _pickers, _body, _foot, _action;
        string _team;
        object _season, _mine;
        int _scope = -1;        // 0 season, n week n; -1 until the season is known
        bool _players;
        bool _wide;             // landscape: Teams and Players side by side, no toggle
        int _draws;             // which Draw a board's answer belongs to
        readonly object _past;  // a past season's leaderboard (Past seasons > Leaderboard): its boards only, nothing to play

        public LeagueScreen(App app) : this(app, null) { }

        /// <summary>A past season's boards (LeagueSeasons onLeaderboard: LeagueLeaderboard with that seasonId), opened
        /// on the season's totals. pastSeason is the row from GET /league/seasons/past.</summary>
        public LeagueScreen(App app, object pastSeason)
        {
            _app = app;
            _past = pastSeason;
            _head = new VisualElement(); _pickers = new VisualElement(); _body = new VisualElement(); _foot = new VisualElement();
            // The week's main button (Pick your team / Play this week) stays at the bottom, always in view; the boards
            // scroll behind it (Matt).
            _action = Ui.Box("league-action");
            ScrollView scroll = Ui.Scroll();
            scroll.AddToClassList("grow");
            scroll.Add(Ui.Header(pastSeason != null ? "Season " + J.Int(pastSeason, "number") : "League Night", app.Pop));
            scroll.Add(Ui.Box("pad", _head, _pickers));
            scroll.Add(_body);
            scroll.Add(Ui.Box("pad", _foot));
            var end = new VisualElement();
            end.style.height = 110;   // the last row can scroll up clear of the button
            scroll.Add(end);
            Root = Ui.Box("league-page", scroll, _action);
            if (League.Sheet != null) Root.styleSheets.Add(League.Sheet);
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

        // Read again whenever the page comes to the front: a game just played moves the board. What is on the page
        // stays while it reads (the last answers are kept), and is redrawn in place when the new one is in.
        public override void Shown()
        {
            if (Kid.Blocked(_app, Kid.League, _body)) { _head.Clear(); _pickers.Clear(); _foot.Clear(); _action.Clear(); return; }
            if (_past != null)
            {
                // A finished season: its own rows, the season's totals first, nothing to read about this week.
                _season = _past;
                if (_scope < 0) _scope = 0;
                Draw();
                return;
            }
            if (_season == null && League.Loaded && League.LastSeason != null)
            {
                _team = League.LastTeam; _season = League.LastSeason; _mine = League.LastMine;
                if (_scope < 0)
                {
                    object week = League.CurrentWeek(_season, DateTime.Now);
                    _scope = League.Live(week, DateTime.Now) ? J.Int(week, "weekNumber") : 0;
                }
                Draw();
            }
            League.Load((team, season, mine) =>
            {
                _team = team; _season = season; _mine = mine;
                if (season == null) { _head.Clear(); _head.Add(Ui.Box("card", Ui.Text("Between seasons. The next League Night season has not started yet.", "body-2", Ui.Medium))); return; }
                if (_scope < 0)
                {
                    object week = League.CurrentWeek(season, DateTime.Now);
                    _scope = League.Live(week, DateTime.Now) ? J.Int(week, "weekNumber") : 0;
                }
                Draw();
                // -app2LeagueTeam lions: that team's page straight away (to check it from a script).
                string open = PuttrApp.Board.Arg("-app2LeagueTeam");
                if (open != null && !_openedTeam && League.TeamOf(open) != null) { _openedTeam = true; _app.Push(new LeagueTeamScreen(_app, League.TeamOf(open), J.Str(_season, "id"), _scope)); }
            });
        }

        static bool _openedTeam;

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
            string when = _past != null && shown == null ? PastDates(_past)
                : shown == null
                ? "Season " + J.Int(_season, "number") + "   ·   Week " + J.Int(current, "weekNumber") + " of " + weeks.Count
                : J.Str(shown, "gameTitle", "Week " + _scope) + (League.Live(shown, now) ? "   ·   " + League.Countdown(League.Time(shown, "endsAt"), now) : "");
            // The "?" (LeagueLeaderboard's header help button) beside the line, so it is there in the Play tab too.
            VisualElement help = Ui.IconSquare("Question", () => League.Help(_app, Root, _past == null && League.TeamOf(_team) == null));
            _head.Add(Ui.Box("row", Ui.Text(when, "body-2 lg-when", Ui.Medium), help));

            _pickers.Clear();
            ScrollView scopes = Ui.Scroll(true);
            scopes.style.marginTop = 12;
            scopes.Add(Pill("Season", _scope == 0, false, false, () => { _scope = 0; Draw(); }));
            foreach (object week in weeks)
            {
                int number = J.Int(week, "weekNumber");
                DateTime? starts = League.Time(week, "startsAt");
                scopes.Add(Pill("Week " + number, _scope == number, League.Live(week, now), starts != null && starts.Value > now, () => { _scope = number; Draw(); }));
            }
            _pickers.Add(scopes);
            _wide = Screen.width > Screen.height;
            // Teams or Players: the same chip pair as every other filter (white when on), not a green switch.
            if (!_wide) _pickers.Add(Ui.Box("row lg-switch",
                Pill("Teams", !_players, false, false, () => { _players = false; Draw(); }),
                Pill("Players", _players, false, false, () => { _players = true; Draw(); })));

            _body.Clear();
            DateTime? begins = shown != null ? League.Time(shown, "startsAt") : null;
            if (begins != null && begins.Value > now)
                _body.Add(Ui.Box("pad", Ui.Box("card", Ui.Text("Week " + _scope + " has not started. It opens " + begins.Value.ToString("MMM d", CultureInfo.InvariantCulture) + ".", "body-2", Ui.Medium)).Margin(12)));
            else if (_wide)
            {
                // Landscape: the two boards at half the width each.
                int draw = ++_draws;
                _body.Add(Ui.Box("pad league-split",
                    Ui.Box("league-half league-half-l", Ui.Caps("Teams", "caps league-half-title"), Board(false, draw)),
                    Ui.Box("league-half league-half-r", Ui.Caps("Players", "caps league-half-title"), Board(true, draw))));
            }
            else _body.Add(Ui.Box("pad", Board(_players, ++_draws)));

            // What the Player can do.
            _foot.Clear();
            _action.Clear();
            if (_past != null)
            {
                // A past season: its weeks' recaps where they were made, nothing to join or play.
                string pastId = J.Str(_past, "id");
                int pastWeek = _scope;
                if (shown != null && J.Flag(shown, "hasRecap"))
                    _foot.Add(Ui.Button("Week " + pastWeek + " recap", () => _app.Push(new LeagueRecapScreen(_app, pastId, pastWeek)), "btn btn-ghost"));
                _action.style.display = DisplayStyle.None;
                return;
            }
            League.Team mine = League.TeamOf(_team);
            if (mine == null && !Tv.On) _action.Add(Ui.Button("Pick your team", () => _app.Push(new LeagueJoinScreen(_app))));
            else if (League.Live(current, now))
            {
                double best = J.Num(_mine, "bestScore");
                _foot.Add(Ui.Text(best > 0 ? "Your best this week: " + League.Points(best) + (J.Get(_mine, "teamRank") != null ? "   ·   #" + J.Int(_mine, "teamRank") + " on " + mine.Name : "") : "No score yet this week", "body-2 center", Ui.Medium).Margin(16));
                if (J.Flag(_mine, "playedToday")) _action.Add(Ui.Text("Played today. Come back tomorrow.", "body-2 center league-action-note", Ui.Medium));
                else if (!Tv.On) _action.Add(Ui.Button("Play this week", () => LeagueRound.Play(_app, _season, current, mine.Id)));
            }
            else _foot.Add(Ui.Text("This week is locked. Results drop Monday at 8pm.", "body-2 center", Ui.Medium).Margin(16));

            // A finished week has its recap; every season before this one is in the archive.
            string recapSeason = J.Str(_season, "id");
            int recapWeek = _scope;
            if (shown != null && League.Time(shown, "endsAt") is DateTime ended && ended < now)
                _foot.Add(Ui.Button("Week " + recapWeek + " recap", () => _app.Push(new LeagueRecapScreen(_app, recapSeason, recapWeek)), "btn btn-ghost"));
            _foot.Add(Ui.Text("Past seasons  ›", "sec-link center lg-link", Ui.Medium).OnTap(() => _app.Push(new LeagueSeasonsScreen(_app))));
            _action.style.display = _action.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>One board, Teams or Players, for the scope shown: the kept rows at once, the fresh ones in place.</summary>
        VisualElement Board(bool players, int draw)
        {
            string path = "/league/season/" + J.Str(_season, "id") + (_scope > 0 ? "/week/" + _scope : "") + (players ? "/leaderboard?scope=global&limit=100" : "/standings");
            // One bordered block; every team or Player is a row in it.
            VisualElement card = Ui.Box("lg-board");
            card.style.marginTop = 12;
            League.Board(path, card, rows => Rows(card, rows, players),
                () => { card.Clear(); card.Add(League.Empty("Could not load the board. Pull back and try again.")); }, () => draw == _draws);
            return card;
        }

        static VisualElement Pill(string text, bool on, bool live, bool later, Action tap) => League.Pill(text, on, live, later, tap);

        static string PastDates(object season)
        {
            DateTime? start = ProfileMath.Date(season, "startDate"), end = ProfileMath.Date(season, "endDate");
            return start != null && end != null ? "Final   ·   " + start.Value.ToString("MMM d", CultureInfo.InvariantCulture) + " \u2013 " + end.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : "Final standings";
        }

        void Rows(VisualElement card, List<object> rows, bool players)
        {
            card.Clear();
            bool season = _scope == 0;
            if (players)
            {
                if (rows.Count == 0) { card.Add(League.Empty(_scope > 0 ? "No Player scores posted yet this week." : "No Player scores posted yet this season.")); return; }
                foreach (object row in rows)
                {
                    object player = League.PlayerOf(row);
                    League.Team team = League.TeamOf(J.Str(row, "team"));
                    bool me = J.Str(player, "userId") == Auth.Uid;
                    object tries = J.Get(row, "attempts") ?? J.Get(row, "gamesPlayed") ?? J.Get(row, "totalSubmissions");
                    int games = tries != null ? (int)Puttr.PuttrJson.Number(tries, 0) : 0;
                    string game = J.Str(row, "userGameId"), userId = J.Str(player, "userId");
                    VisualElement line = League.PlayerRow(row, team, J.Get(row, "rank") != null ? J.Int(row, "rank").ToString() : "\u2013", me, true, false,
                        tries != null ? games + (games == 1 ? " game" : " games") : null, false);
                    // A week's row is that Player's game; the season's, the Player.
                    object who = player;
                    if (_scope > 0 && game.Length > 0) line.OnTap(() => _app.Push(new GameViewScreen(_app, game)));
                    else line.OnTap(() => ProfileScreen.Open(_app, userId, who));
                    card.Add(line);
                }
                League.FirstRow(card);
                return;
            }
            rows = League.AllSix(rows);
            string sid = J.Str(_season, "id");
            int week = _scope;
            foreach (object row in rows)
            {
                League.Team team = League.TeamOf(J.Str(row, "team"));
                if (team == null) continue;
                object rank = season ? (J.Get(row, "seasonRank") ?? J.Get(row, "weekRank")) : J.Get(row, "weekRank");
                double total = season ? J.Num(row, "seasonTotal", J.Num(row, "total")) : J.Num(row, "total");
                League.Team tapped = team;
                card.Add(League.TeamRow(team, rank, total, J.Get(row, "weekRankChange"), season, team.Id == _team, () => _app.Push(new LeagueTeamScreen(_app, tapped, sid, week))));
            }
            League.FirstRow(card);
        }

        static string PuttrMath(object rank) => ((int)Puttr.PuttrJson.Number(rank, 0)).ToString();
    }

    /// <summary>Choosing a team. It is for the season: the API will not move a Player once they have joined.</summary>
    public sealed class LeagueJoinScreen : AppScreen
    {
        readonly App _app;
        readonly ScrollView _scroll;
        string _chosen;
        readonly string _suggested;   // the team for the Player's region, or null (League.Suggest)
        bool _busy;

        public LeagueJoinScreen(App app)
        {
            _app = app;
            _scroll = Ui.Scroll();
            Root = _scroll;
            if (League.Sheet != null) Root.styleSheets.Add(League.Sheet);
            string id = League.Suggest(League.DeviceCountry(), J.Str(app.Profile, "homeTown"));
            // Only a team that is in this season's list.
            foreach (League.Team team in League.Teams) if (team.Id == id) _suggested = id;
            Draw();
        }

        // LeagueOnboarding's RegionBanner: "We think you're a Lions fan", and a tap picks them.
        VisualElement Suggestion()
        {
            League.Team team = _suggested != null ? League.TeamOf(_suggested) : null;
            if (team == null) return null;
            return Ui.Box("card lg-suggest", Ui.Icon("MapPin", 24, Color.white), Ui.Box("grow lg-suggest-words",
                Ui.Text("We think you're a " + team.Name.Replace("The ", "") + " fan", "lg-suggest-t", Ui.Bold),
                Ui.Text("Based on your region. Tap to select, or pick another below.", "lg-suggest-d", Ui.Medium))).OnTap(() => { _chosen = team.Id; Draw(); });
        }

        void Draw()
        {
            _scroll.Clear();
            _scroll.Add(Ui.Header("", _app.Pop));
            VisualElement grid = Ui.Box("team-grid");
            foreach (League.Team team in League.Teams)
            {
                League.Team pick = team;
                // The picked team: a lighter fill and a 1 px bright green edge (never the team's own colour as an outline).
                VisualElement cell = Ui.Box(team.Id == _chosen ? "glass team-cell team-on" : "glass team-cell", League.Crest(team, 56),
                    Ui.Text(team.Name, "body center", Ui.Bold).Margin(8), Ui.Caps(team.Region, "caps center lg-team-region"), Ui.Text(team.Motto, "body-2 center", Ui.Medium).Margin(4));
                if (team.Id == _suggested) cell.Add(Ui.Caps("For you", "caps lg-foryou"));
                grid.Add(Ui.Box("team-slot", cell.OnTap(() => { _chosen = pick.Id; Draw(); })));
            }
            League.Team chosen = League.TeamOf(_chosen);
            _scroll.Add(Ui.Box("pad lg-join",
                Ui.Title("Pick your team"),
                Ui.Text("Six regions. One season. Your team competes every week, and your best score counts.", "body-2", Ui.Medium).Margin(4),
                Suggestion(),
                grid,
                Ui.Text("Locked for the season. You can switch teams next season.", "body-2 center", Ui.Medium).Margin(12),
                chosen == null ? League.OffButton("Select a team") : Ui.Button("Join " + chosen.Name, () => Club.Require(_app, "League Night", Confirm))));
        }

        // One more look before it is final.
        void Confirm()
        {
            League.Team team = League.TeamOf(_chosen);
            if (team == null || _busy) return;
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal",
                Ui.Title("Lock in " + team.Name + "?", "title-20"),
                Ui.Text("You play for them for the whole season. This cannot be changed until the next one.", "body-2", Ui.Medium).Margin(8),
                Ui.Button("Lock it in", () => { dim.RemoveFromHierarchy(); Join(team); }),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
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

    /// <summary>
    /// Playing a week's League Night game. The week names its game (gameId, gameType, gameTitle from
    /// GET /league/season/current), and the score is entered against that game: POST /league/score/submit
    /// refuses any other gameId ("wrong_game"). So the game played is the week's, one of three ways:
    ///
    ///   Rules  this build has the game as a putt-list rule game for one Player, most points wins (Center Cut,
    ///          Tour Juice...): it is played as that game, in its own scene with its own scoring and putt read,
    ///          and the game's score is the league score.
    ///   List   the game is a list of putts with no rule game here (a Standard or Streak game with its own
    ///          puttData): its putts on the ramp, scored by its rule.
    ///   None   anything else (the arcade games such as On Fire, games for two or more, games where fewer is
    ///          better): the Player is told it cannot be played here. Nothing is scored as some other game.
    ///
    /// Either way it is a League round (CompetitionRound, Kind.League): once a day, best of the week, entered only
    /// as in the Puttr app (a rehearsal only for a tester with Rehearsal mode on).
    /// </summary>
    public static class LeagueRound
    {
        public enum Way { Rules, List, None }

        /// <summary>Set by GameBridge: plays a round whose Rules are set as that rule game (round, the game's catalog row).</summary>
        public static Action<CompetitionRound, object> PlayRules;
        /// <summary>Set by GameBridge: plays a round's putt list on the ramp (round, the gameType its recording is named for).</summary>
        public static Action<CompetitionRound, string> PlayList;

        static bool _loading, _old;

        /// <summary>The week's gameType. The API always sends one; without it the app takes the week for Center Cut, and so does this.</summary>
        public static string TypeOf(object week)
        {
            string type = J.Str(week, "gameType").ToUpperInvariant();
            return type.Length > 0 ? type : "CENTERCUT";
        }

        /// <summary>How this build plays a game row, and the rule game that plays it (a new one each call) when there is one.</summary>
        public static Way WayFor(object game, out RuleGame rules)
        {
            rules = null;
            RuleGame made = RuleCatalog.Make(game);
            // A league score is one Player's points, and the best of the week is the highest (MAX(score) in the API).
            if (made is ListGame && made.MinPlayers <= 1 && !made.LowerIsBetter) { rules = made; return Way.Rules; }
            return made == null && GameBridge.RuleFor(game) != null ? Way.List : Way.None;
        }

        /// <summary>The putts of a week's game: the game row's own when it sets any, else one from each distance from 3 to 11
        /// feet, shuffled, a random tee each (LeagueGame.js: currentGame.puttData, else createRandomPutts).</summary>
        public static List<TournamentMath.Dealt> Putts(object game, System.Random dice)
        {
            List<TournamentMath.Dealt> own = TournamentMath.Putts(game);
            return own.Count > 0 ? own : RoundSession.Shuffled(dice);
        }

        /// <summary>The week's game in the catalog (for its putts and title); when the catalog does not list it, a row made
        /// from what the week says, as the app does.</summary>
        public static object Row(List<object> catalog, object week)
        {
            string id = J.Str(week, "gameId"), type = TypeOf(week);
            foreach (object game in catalog)
                if (id.Length > 0 && J.Str(game, "id") == id && J.Str(game, "gameType").ToUpperInvariant() == type) return game;
            return new Dictionary<string, object> { { "id", J.Get(week, "gameId") }, { "gameType", type }, { "title", J.Str(week, "gameTitle", "League Night") } };
        }

        /// <summary>Play this week: reads the week's game, then starts it the way this build can play it.</summary>
        public static void Play(App app, object season, object week, string team)
        {
            if (_loading || week == null || app.Games.Busy) return;
            // GameBridge.PlayLeague comes back here once it hands league games over; a second time round means nothing took it.
            if (_old) { _old = false; NotPlayable(app, J.Str(week, "gameTitle", "this week's game")); return; }
            _loading = true;
            App.Run(Api.Get("/games/ALL", data => { _loading = false; Start(app, season, week, team, Row(J.List(data), week)); },
                why => { _loading = false; app.Toast(why == "offline" ? "No connection. Check your WiFi and try again." : "Could not load this week's game. Try again in a moment."); }));
        }

        static void Start(App app, object season, object week, string team, object game)
        {
            string type = TypeOf(week), title = J.Str(week, "gameTitle", J.Str(game, "title", "this week's game"));
            int number = J.Int(week, "weekNumber");
            Way way = WayFor(game, out RuleGame rules);
            var session = new RoundSession { Title = "League Night", Detail = "Week " + number };
            // The gameId entered is the week's own: the API checks it against the schedule.
            var round = new CompetitionRound(CompetitionRound.Kind.League, session, J.Str(week, "gameId")) { SeasonId = J.Str(season, "id"), WeekNumber = number, Team = team, Game = game };
            Perf.Note("league: week " + number + " is " + type + " (game " + J.Str(week, "gameId") + "), played as " + way);

            if (way == Way.Rules && PlayRules != null)
            {
                rules.Names = new List<string> { PlayerPicker.Short(app.Profile) };
                // Center Cut plays the list the round is opened with, so the putts saved with the game are the putts played.
                if (rules is CenterCutGame centerCut)
                {
                    session.Rule = RoundRule.CenterCut;
                    session.Putts = Putts(game, new System.Random());
                    centerCut.Fixed = session.Putts;
                }
                round.Rules = rules;
                app.Push(new LeagueStartScreen(app, "League Night   ·   Week " + number, rules, () => PlayRules(round, game)));
                return;
            }
            if (way == Way.List && PlayList != null)
            {
                session.Rule = GameBridge.RuleFor(game) ?? RoundRule.Feet;
                session.Putts = TournamentMath.Putts(game);
                session.StopOnMiss = type == "STREAK";
                PlayList(round, type);
                return;
            }
            // GameBridge has not taken league games over yet: a Center Cut week still plays as it did before, Center
            // Cut scoring over nine putts on the ramp. Any other game is not played as something it is not.
            if (way == Way.Rules && type == "CENTERCUT" && PlayRules == null)
            {
                _old = true;
                app.Games.PlayLeague(season, week, team);
                _old = false;
                return;
            }
            NotPlayable(app, title);
        }

        /// <summary>The week's game is one this build cannot play: say so, and where it can be played.</summary>
        static void NotPlayable(App app, string title)
        {
            Perf.Note("league: " + title + " cannot be played in this build");
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal",
                Ui.Title("Not playable here yet", "title-20"),
                Ui.Text("This week's League Night game is " + title + ", and this app cannot play it yet. Play it in the Puttr app and your score goes on the same board.", "body-2", Ui.Medium).Margin(8));
            card.Add(Ui.Button("OK", () => dim.RemoveFromHierarchy()));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            app.Toasts.Add(dim);
        }

        /// <summary>
        /// What a filmed League game needs over a rule game's own panel (which knows nothing of the camera): the Player's
        /// own picture in the corner of the scene, where a round's panel puts it, and a line while putts wait for the
        /// recording (no putt counts until it runs). GameBridge adds this to the rule panel's root.
        /// </summary>
        public static VisualElement Overlay(App app)
        {
            Label note = Ui.Text("", "sheet-note", Ui.Bold);
            VisualElement box = new VisualElement();
            box.pickingMode = PickingMode.Ignore;
            note.pickingMode = PickingMode.Ignore;
            box.style.position = Position.Absolute;
            box.Add(note);
            string said = "";
            bool placed = false, wasWide = false;
            void Tick()
            {
                bool wide = Screen.width > Screen.height;
                float picture = wide ? PuttrArcade.FreePuttMode.WideShare : PuttrArcade.FreePuttMode.UprightShare;
                if (!placed || wide != wasWide)
                {
                    placed = true; wasWide = wide;
                    Vector4 safe = app.SafeInsets();   // left, bottom, right, top
                    // The same corner tile as RoundPanel.Place: bottom right of the scene, 9 by 16.
                    float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 0.46f;
                    if (wide)
                    {
                        float h = 0.10f * aspect * 16f / 9f;
                        float bottom = Screen.height > 0 ? Screen.safeArea.y / Screen.height : 0f;
                        Capture.One.Place(new Rect(picture - 0.11f, 1f - h - bottom - 0.03f, 0.10f, h));
                    }
                    else Capture.One.Place(new Rect(0.74f, picture - 0.23f * aspect * 16f / 9f - 0.01f, 0.23f, 0.23f * aspect * 16f / 9f));
                    // The line sits along the bottom of the scene, left of the picture.
                    box.style.left = (wide ? safe.x : 0f) + 12f;
                    box.style.width = Length.Percent(wide ? picture * 100f - 16f : 66f);
                    box.style.bottom = wide ? (StyleLength)(safe.y + 12f) : (StyleLength)Length.Percent((1f - picture) * 100f + 1f);
                }
                string waiting = Capture.One.Waiting() ?? "";
                if (waiting == said) return;
                said = waiting;
                note.text = waiting;
                note.style.display = waiting.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            note.style.display = DisplayStyle.None;
            box.schedule.Execute(Tick).Every(300);
            return box;
        }
    }

    /// <summary>Before a League game played as the week's own game: what it is and how it scores, then Start.</summary>
    public sealed class LeagueStartScreen : AppScreen
    {
        public LeagueStartScreen(App app, string kicker, RuleGame rules, Action start)
        {
            VisualElement how = Ui.Box("card");
            foreach (string line in rules.HowTo) how.Add(Ui.Text(line, "body", Ui.Medium).Margin(how.childCount == 0 ? 0 : 8));
            how.Add(Ui.Text("One League game a day. Your best of the week counts for your team.", "body-2", Ui.Medium).Margin(12));
            // A tester with Rehearsal mode on plays it as practice: better known before it is played than after.
            if (Green.RealFor(true) && CompetitionRound.Rehearse)
                how.Add(Ui.Text("Rehearsal mode is on in Settings, so this game is saved as practice and not entered.", "body-2", Ui.Medium).Margin(8));
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad",
                Ui.Caps(kicker, "caps caps-green center"),
                Ui.Title(rules.Title, "title center").Margin(4),
                how.Margin(16),
                Ui.Button("Start", () => { app.Pop(); start(); })));
            Root = scroll;
        }
    }

    /// <summary>After a League game played as the week's own game: the game's score, what became of it in the league,
    /// and the putts as the game called them (RuleFinalScreen's list). A League game is not played again the same day.</summary>
    public sealed class LeagueFinalScreen : AppScreen
    {
        public LeagueFinalScreen(App app, CompetitionRound round)
        {
            RuleGame g = round.Rules;
            int made = 0;
            foreach (Shot s in g.Shots) if (s.V != null && s.V.Made) made++;
            bool saved = round.How != CompetitionRound.Mode.Local;
            var spec = new GameOverSpec
            {
                Eyebrow = "Final   ·   " + round.Session.Title + "   ·   " + round.Session.Detail,
                Score = g.ScoreText(0),
                ScoreCaption = g.Title + "   ·   " + made + " of " + g.Shots.Count + " holed",
                Outcome = round.Outcome(),
                ReviewId = saved ? round.UserGameId : null,
                RateAs = "League Night",
                Putts = new List<GameOverPutt>(),
                PqsPutts = saved ? new List<AnalysisMath.Putt>() : null,
            };
            int n = 0;
            foreach (Shot s in g.Shots)
            {
                n++;
                spec.Putts.Add(new GameOverPutt { Number = n.ToString(), From = GameOver.From(s.Feet, s.Tee), Call = g.FinalCall(s), Points = g.FinalPoints(s), Good = s.Good });
                if (saved && s.V != null) spec.PqsPutts.Add(GameOver.PqsPutt(s.Feet, s.Tee, s.V));
            }
            Root = GameOver.Build(app, spec);
            if (League.Sheet != null) Root.styleSheets.Add(League.Sheet);
        }
    }
}
