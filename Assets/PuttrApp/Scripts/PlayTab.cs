// The Play tab, as the Puttr 2 sports UI lays it out (puttr-app/docs/puttr2-sports-ui, Phone 2):
// filter chips, then one wide tile per game with three plain tags (who, what skill, how long)
// and your best. Nothing else on a tile.
//
// The games are the live catalogue (GET /games/ALL). Your best comes from your last hundred
// finished games (GET /profile/games/, one call) rather than a call per game. No game is locked:
// in the current app membership gates features (tournaments, Versus, video), not games.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class GameTags
    {
        /// <summary>Who it is for, from the multiPlayer column: 0 one Player, 1 two, 2 two or more, 3 one to four.</summary>
        public static string Who(object game)
        {
            switch (J.Int(game, "multiPlayer"))
            {
                case 1: return "2 Players";
                case 2: return "2 to 4";
                case 3: return "1 to 4";
                default: return "Solo";
            }
        }

        public static bool Together(object game) => J.Int(game, "multiPlayer") != 0;

        /// <summary>The skill it trains, from the skills column (angle, speed, distance, pressure, equipment), first one named.</summary>
        public static string Skill(object game)
        {
            string skills = J.Str(game, "skills").ToLowerInvariant();
            foreach (string skill in skills.Split(','))
            {
                switch (skill.Trim())
                {
                    case "angle": return "Line";
                    case "speed": return "Pace";
                    case "distance": return "Distance";
                    case "pressure": return "Pressure";
                    case "equipment": return "Gear";
                }
            }
            string type = J.Str(game, "gameType").ToUpperInvariant();
            if (type == "OPEN") return "Practice";
            if (type == "ARCADESTREAK" || type == "STREAK" || type == "ONFIRE") return "Streak";
            return "Putting";
        }

        public static bool Trains(object game, string skill) => Skill(game) == skill;

        /// <summary>
        /// About how long it takes: there is no column for it, so it is worked out from the putts
        /// (about 25 seconds a putt, a Player at a time). Open-ended games say so.
        /// </summary>
        public static int Minutes(object game)
        {
            int putts = J.Int(game, "putts");
            if (putts <= 0) return 0;
            int players = J.Int(game, "multiPlayer") == 0 ? 1 : 2;
            return Mathf.Max(1, Mathf.RoundToInt(putts * players * 25f / 60f));
        }

        public static string Length(object game)
        {
            int minutes = Minutes(game);
            if (minutes > 0) return minutes + " min";
            string type = J.Str(game, "gameType").ToUpperInvariant();
            return type == "ARCADESTREAK" || type == "STREAK" || type == "ONFIRE" ? "Until you miss" : "Open";
        }

        /// <summary>Your best in each game (by games.id) from finished games {gameId, score}. Zero is not a best.</summary>
        public static Dictionary<string, double> Bests(List<object> games)
        {
            var best = new Dictionary<string, double>();
            foreach (object game in games)
            {
                string id = J.Str(game, "gameId");
                double score = J.Num(game, "score");
                if (id.Length == 0 || score <= 0) continue;
                if (!best.TryGetValue(id, out double was) || score > was) best[id] = score;
            }
            return best;
        }
    }

    /// <summary>The Play tab's search (PlayScreen.js): a game matches when its title or its description holds what was
    /// typed, ignoring case and the spaces around it. Nothing typed matches every game.</summary>
    public static class GameSearch
    {
        public static bool Matches(object game, string query)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0) return true;
            return J.Str(game, "title").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || J.Str(game, "description").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public sealed class PlayScreen : AppScreen
    {
        // The ways to play as top tabs, white titles like a sports game's modes. Local is the games; the others are the
        // section's own screen, shown in place (its back arrow hidden: a tab has nowhere to go back to).
        public static string OpenAt;
        static readonly string[] Tabs = { "Local", "Online", "Tracks", "Quests", "Tournaments", "League" };   // Custom games live in Improve, Practice
        static readonly string[] Chips = { "All", "Solo", "Together", "Line", "Pace", "Sensor" };
        /// <summary>One sentence on what each way to play is, under its tab (Matt, 2026-11-22: as Tracks has). Tracks
        /// says its own on its page (TracksCoursesScreen.Intro).</summary>
        public static readonly Dictionary<string, string> Intros = new Dictionary<string, string>
        {
            { "Local", "Every game for the Players at your Puttr, from a quick round on your own to a four Player match." },
            { "Online", "Play another Player wherever they are: take turns in Versus, or putt at the same time on a Live call." },
            { "Quests", "Greenlands: eight worlds of stages to beat. Balls are lives, and a boss guards the way to each new world." },
            { "Tournaments", "Play the same round as every other Player this month and see where your score puts you." },
            { "League", "League Night: play your team's game each week and move it up the table." },
        };
        static VisualElement Intro(string tab) => Intros.TryGetValue(tab, out string words) ? Ui.Box("pad play-intro", Ui.Text(words, "sub wrap", Ui.Regular)) : null;
        static int _tab;   // kept while the app runs

        readonly App _app;
        readonly VisualElement _chips, _list, _tabRow, _body, _local;
        readonly Dictionary<int, AppScreen> _screens = new Dictionary<int, AppScreen>();
        AppScreen _online;
        int _onlineMode;
        List<object> _games;
        Dictionary<string, double> _bests = new Dictionary<string, double>();
        int _chip;
        // Search games (GameCategories' search): the field takes the chips' place while it is open, and the chips
        // still apply under it. Not kept between visits, as in the app.
        string _query = "";
        bool _searching;
        TextField _search;

        public PlayScreen(App app)
        {
            _app = app;
            _chips = new VisualElement();
            _list = Ui.Box("gcard-list");
            _list.RegisterCallback<GeometryChangedEvent>(_ => Tiles(false));
            ScrollView local = Ui.Scroll();
            local.AddToClassList("grow");
            local.Add(Intro("Local"));
            local.Add(Ui.Box("pad", _chips));
            local.Add(Ui.Box("pad", _list));
            var foot = new VisualElement();
            foot.style.height = 24;
            local.Add(foot);
            _local = local;
            _tabRow = new VisualElement();
            _body = Ui.Box("play-body");
            // The one tab header (docs/design-guidelines.md): the name, and search on the right. A TV draws its own bar.
            VisualElement head = Tv.On ? null : Ui.TabHeader("Play", Ui.IconSquare("MagnifyingGlass", () => { if (_tab != 0) ShowTab(0); OpenSearch(); }));
            Root = Ui.Box("play-page", head, _tabRow, _body);
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Home");
            if (sheet != null) Root.styleSheets.Add(sheet);
            DrawChips();
            _list.Add(Spinner.Small());
            // The catalogue and the bests as they were last time, at once, then as they are now.
            App.Run(Api.Kept("/games/ALL", data => { _games = J.List(data); Draw(); }, _ => { _list.Clear(); _list.Add(Ui.Text("Could not load the games.", "sub center", Ui.Regular).Margin(30)); }));
            App.Run(Api.Kept("/profile/games/", data => { _bests = GameTags.Bests(J.List(data)); Draw(); }));
            // -app2Open playquests: straight to a top tab by name, for checking it from a script.
            if (OpenAt != null) { _tab = System.Math.Max(0, System.Array.IndexOf(Tabs, OpenAt)); OpenAt = null; }
            if (!Shows(_tab)) _tab = 0;
            ShowTab(_tab);
        }

        // Kids play the games, Tracks, Quests and Tournaments; Online and League are not for a Kid profile.
        bool Shows(int tab) => !Kid.IsKid(_app.Profile) || (Tabs[tab] != "Online" && Tabs[tab] != "League");

        /// <summary>Back on a TV remote with nothing on top: from another tab back to Local.</summary>
        public void TvBack() { if (_tab != 0) ShowTab(0); }

        /// <summary>On the Local tab (the TV's first screen).</summary>
        public bool AtLocal => _tab == 0;

        public override void Shown()
        {
            if (_tab == 0) return;
            if (_tab == 1) _online?.Shown();
            else if (_screens.TryGetValue(_tab, out AppScreen s)) s.Shown();
        }

        // The phone and tablet tab row is kept between taps: building a new one put its scroll back to the first tab,
        // which pushed a tab picked further along (Tournaments) off the screen.
        ScrollView _tabScroll;
        string _tabsShown;
        readonly Dictionary<int, VisualElement> _tabCells = new Dictionary<int, VisualElement>();

        void RevealTab()
        {
            ScrollView row = _tabScroll;
            if (row == null || !_tabCells.TryGetValue(_tab, out VisualElement cell)) return;
            row.schedule.Execute(() => { if (cell.parent != null && row.panel != null) row.ScrollTo(cell); });
        }

        void DrawTabs()
        {
            string shown = "";
            for (int i = 0; i < Tabs.Length; i++) if (Shows(i)) shown += i;
            if (!Tv.On && _tabScroll != null && shown == _tabsShown)
            {
                foreach (KeyValuePair<int, VisualElement> cell in _tabCells) cell.Value.EnableInClassList("play-tab-on", cell.Key == _tab);
                RevealTab();
                return;
            }
            // On a TV, a tab that had the remote's focus keeps it through the redraw.
            bool focused = Tv.On && _tabRow.panel != null && _tabRow.focusController?.focusedElement is VisualElement f && _tabRow.Contains(f);
            _tabRow.Clear();
            _tabCells.Clear();
            ScrollView row = Ui.Scroll(true);
            row.AddToClassList("play-tabs");
            for (int i = 0; i < Tabs.Length; i++)
            {
                if (!Shows(i)) continue;
                int index = i;
                VisualElement tab = Ui.Box(i == _tab ? "play-tab play-tab-on" : "play-tab", Ui.Text(Tabs[i].ToUpperInvariant(), "play-tab-t ital", Ui.Bold)).OnTap(() => ShowTab(index));
                row.Add(tab);
                _tabCells[i] = tab;
                if (focused && i == _tab) tab.schedule.Execute(() => Tv.FocusOn(tab));
            }
            if (!Tv.On)
            {
                _tabScroll = row;
                _tabsShown = shown;
                _tabRow.Add(row);
                RevealTab();
                return;
            }
            // A TV: the Puttr logo, the tabs, then the green's status and who is signed in (the TV has no other tabs).
            row.AddToClassList("grow");
            var logo = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoHeader"), scaleMode = ScaleMode.ScaleToFit };
            logo.AddToClassList("tv-logo");
            _tabRow.Add(Ui.Box("row tv-bar", logo, row, TvAccount.Green(_app), TvAccount.Button(_app)));
        }

        void ShowTab(int tab)
        {
            _tab = tab;
            DrawTabs();
            _body.Clear();
            if (tab == 0) { _body.Add(_local); return; }
            if (tab == 1) { ShowOnline(); return; }
            if (!_screens.TryGetValue(tab, out AppScreen screen))
            {
                switch (Tabs[tab])
                {
                    case "Tracks": screen = new TracksCoursesScreen(_app); break;
                    case "Quests": screen = new GreenlandsScreen(_app); break;   // Quests are Greenlands now (the old list: Quests.cs)
                    case "Tournaments": screen = new TournamentsScreen(_app); break;
                    default: screen = new LeagueScreen(_app); break;
                }
                Embed(screen);
                _screens[tab] = screen;
            }
            VisualElement intro = Intro(Tabs[tab]);
            if (intro != null) { intro.AddToClassList("play-intro-in"); _body.Add(intro); }   // in line with the section's own column
            _body.Add(screen.Root);
            screen.Shown();
        }

        // Online: Versus (your turn, then theirs) and Live (real-time 1v1), one under the other's switch.
        void ShowOnline()
        {
            if (Tv.On) _onlineMode = 0;   // Live is a video call: a TV has no camera
            VisualElement modes = Tv.On ? null : FreePuttScreen.Choice(new[] { "Versus", "Live" }, _onlineMode, i => { _onlineMode = i; _online = null; ShowOnline(); });
            if (_online == null)
            {
                _online = _onlineMode == 0 ? (AppScreen)new VersusInboxScreen(_app) : new LiveLobbyScreen(_app);
                Embed(_online);
            }
            _body.Clear();
            VisualElement about = Intro("Online");
            about.AddToClassList("play-intro-in");
            _body.Add(about);
            if (modes != null) _body.Add(Ui.Box("pad play-online", modes));
            _body.Add(_online.Root);
            _online.Shown();
        }

        /// <summary>A screen shown inside a tab: no back arrow, and it fills the space under the tabs.</summary>
        static void Embed(AppScreen screen)
        {
            VisualElement root = screen.Root;
            root.AddToClassList("grow");
            // The back header, wherever the screen keeps it (straight in its scroll, or in a scroll inside a page).
            VisualElement chevron = root.Q(className: "back-chevron");
            for (VisualElement e = chevron; e != null && e != root.parent; e = e.parent)
                if (e.ClassListContains("top")) { e.style.display = DisplayStyle.None; break; }
        }

        int _cols;
        float _w;

        // The cards tile across a wide screen (an iPad on its side: three; upright: two), one to a row on a phone.
        void Tiles(bool force = true)
        {
            float w = _list.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0) return;
            int cols = w >= 840 ? 3 : w >= 560 ? 2 : 1;
            if (cols == _cols && Mathf.Approximately(w, _w) && !force) return;
            _cols = cols; _w = w;
            float gap = 12f, each = (w - gap * (cols - 1)) / cols;
            int at = 0;
            foreach (VisualElement card in _list.Children())
            {
                if (!card.ClassListContains("gcard")) continue;
                // One column: every card the full width (auto let two short cards share a row on a phone).
                card.style.width = cols == 1 ? Length.Percent(100) : new StyleLength(each);
                card.style.flexGrow = cols == 1 ? 1 : 0;
                // Left to right with an even gap, so a short last row starts at the left.
                card.style.marginRight = cols > 1 && at % cols != cols - 1 ? gap : 0f;
                at++;
            }
        }

        void DrawChips()
        {
            _chips.Clear();
            if (_searching) { _chips.Add(SearchBar()); return; }
            ScrollView row = Ui.Scroll(true);
            // On a TV a rail is padded for the focus ring and pulled out by as much (App.uss, .tv .hscroll): 10 each way.
            row.style.marginTop = Tv.On ? 0 : 10;
            row.style.marginBottom = Tv.On ? 2 : 12;
            for (int i = 0; i < Chips.Length; i++)
            {
                if (Tv.On && Chips[i] == "Sensor") continue;   // the Vertex sensor pairs over Bluetooth: not on a TV
                int index = i;
                row.Add(Ui.Box(i == _chip ? "scope scope-on" : "scope", Ui.Text(Chips[i].ToUpperInvariant(), "scope-text", Ui.Bold)).OnTap(() => { _chip = index; DrawChips(); Draw(); }));
            }
            _chips.Add(row);
        }

        // "Search games...": a magnifier, the field, and an X that clears the search and brings the chips back.
        VisualElement SearchBar()
        {
            if (_search == null)
            {
                _search = new TextField { maxLength = 40 };
                _search.textEdition.placeholder = "Search games...";
                _search.textEdition.hidePlaceholderOnFocus = false;
                // The phone's keyboard types straight into the field, so the list filters on every letter (CityScreen does the same).
                _search.textEdition.hideMobileInput = true;
                _search.AddToClassList("play-search-field");
                _search.Face(Ui.Medium);
                // A moment after the last letter, not on each one: every search builds the list again.
                _search.RegisterValueChangedCallback(e =>
                {
                    _query = e.newValue ?? "";
                    string typed = _query;
                    _search.schedule.Execute(() => { if (_query == typed) Draw(); }).StartingIn(180);
                });
            }
            _search.SetValueWithoutNotify(_query);
            return Ui.Box("play-search", Ui.Icon("MagnifyingGlass", 16, Ui.Faint), _search, Ui.Icon("X", 16, Ui.Faint).OnTap(CloseSearch));
        }

        void OpenSearch()
        {
            _searching = true;
            DrawChips();
            _search.schedule.Execute(() => _search.Focus()).StartingIn(100);
        }

        void CloseSearch()
        {
            _searching = false;
            _query = "";
            _search?.Blur();
            DrawChips();
            Draw();
        }

        bool Shown(object game)
        {
            if (!GameSearch.Matches(game, _query)) return false;
            switch (Chips[_chip])
            {
                case "Solo": return !GameTags.Together(game) || J.Int(game, "multiPlayer") == 3;
                case "Together": return GameTags.Together(game);
                case "Line": return GameTags.Trains(game, "Line");
                case "Pace": return GameTags.Trains(game, "Pace");
                case "Sensor": return J.Str(game, "gameCat") == "SENSOR";
                default: return J.Str(game, "gameCat") != "SENSOR";   // the Vertex games live under Sensor
            }
        }

        // The list is built once a frame at most: the catalogue and the bests arrive separately (each twice when a
        // kept answer is shown first), and each used to build all the cards again.
        bool _drawQueued;
        void Draw()
        {
            if (_drawQueued) return;
            _drawQueued = true;
            _list.schedule.Execute(() => { _drawQueued = false; DrawNow(); });
        }

        void DrawNow()
        {
            if (_games == null) return;
            _list.Clear();
            // The games this build can play come first; then the most played this month.
            var games = new List<object>();
            var all = new List<object>(_games);
            all.AddRange(SensorGames.All);   // not in the API's catalogue on purpose (published = 0): the app lists them
            foreach (object extra in ArcadeExtras.All)   // the arcade games, unpublished for the same reason
                if (!all.Exists(g => J.Str(g, "gameType") == J.Str(extra, "gameType"))) all.Add(extra);
            foreach (object game in all) if (Shown(game) && !GameBridge.Retired(game) && J.Str(game, "gameType") != "FITTING") games.Add(game);   // Compare lives in Improve
            games.Sort((a, b) =>
            {
                int playable = GameBridge.Playable(b).CompareTo(GameBridge.Playable(a));
                return playable != 0 ? playable : J.Num(b, "last30").CompareTo(J.Num(a, "last30"));
            });
            if (Chips[_chip] == "Sensor") _list.Add(Ui.Box("glass play-sensor", Ui.Icon("Lightning", 20, Ui.Bright), Ui.Text(Vertex.Connected ? "Vertex sensor connected." : "These games read your stroke from a Vertex sensor. Pair one in Settings, Vertex sensor.", "sub wrap grow", Ui.Regular)).OnTap(() => _app.Push(new VertexScreen(_app))));
            if (games.Count == 0) { _list.Add(Ui.Text(_query.Trim().Length > 0 ? "No games match \"" + _query.Trim() + "\"." : "No games match. Try another filter.", "sub center", Ui.Regular).Margin(30)); return; }
            foreach (object game in games) _list.Add(Tile(_app, game, _bests));
            Tiles();
        }

        /// <summary>One wide card, as the arcade draws its games: the game's picture, three tags, the name, your best.</summary>
        public static VisualElement Tile(App app, object game, Dictionary<string, double> bests)
        {
            bool playable = GameBridge.Playable(game);
            bool mine = bests != null && bests.TryGetValue(J.Str(game, "id"), out double score);
            string best = mine ? "Best " + bests[J.Str(game, "id")].ToString("0.##", CultureInfo.InvariantCulture) : "Not played";
            VisualElement card = GameCards.Make(game, best, mine, true, GameTags.Who(game), GameTags.Skill(game), GameTags.Length(game));
            if (!playable) card.style.opacity = 0.5f; // in the catalogue, not in this build yet
            return card.OnTap(() => app.Push(new GameScreen(app, game)));
        }
    }
}
