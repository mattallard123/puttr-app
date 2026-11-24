// The shared screens around a game: its page before play (what it is, your best, the top
// scores) and the results after. In puttr-app these are GameIntro, GameOver and the leaderboard.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>
    /// A game's board on its page, as puttr-app's Leaderboard: the PUTTR club board, Recent (the last 24 hours, first)
    /// or All time, with each Player's photo or brand-coloured letter. Compact (Matt, 2026-11-17): narrow rows, one line
    /// a Player, and while it loads, placeholder rows in the board itself (never the screen-centred spinner). The last
    /// answer for each board is kept, so coming back shows it at once and the fresh one replaces it in place.
    /// </summary>
    public static class Board
    {
        static readonly Dictionary<string, List<object>> Cache = new Dictionary<string, List<object>>();
        /// <summary>The boards read so far are dropped (the host cleared the leaderboard).</summary>
        public static void Forget() => Cache.Clear();
        static StyleSheet _sheet;
        /// <summary>GameBoard.uss: the compact board and the game page's side panel.</summary>
        public static StyleSheet Sheet => _sheet != null ? _sheet : _sheet = Resources.Load<StyleSheet>("PuttrApp/GameBoard");

        public static string PathFor(string gameId, object game, bool recent, long since = 0)
        {
            var query = new List<string>();
            if (recent) query.Add("hours=24");
            if (since > 0) query.Add("since=" + since.ToString(CultureInfo.InvariantCulture));   // a venue's Tonight and This week (HostMode.Since)
            if (J.Str(game, "leaderboardOrder") == "asc") query.Add("order=asc");
            return "/club/leaderboard/" + System.Uri.EscapeDataString(HostMode.ClubId) + "/" + gameId + "/" + J.Int(game, "leaderboardDistance") + (query.Count > 0 ? "/?" + string.Join("&", query) : "");
        }

        /// <summary>A script flag's value, or null.</summary>
        public static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        /// <summary>Rows-shaped placeholders: a dot, a name bar and a score bar per row.</summary>
        public static VisualElement Placeholder(int count)
        {
            VisualElement box = Ui.Box("gb-sk");
            for (int i = 0; i < count; i++)
                box.Add(Ui.Box("gb-row", Ui.Box("gb-place"), Skeleton.Circle(26), Ui.Box("grow gb-sk-name", Skeleton.Bar(0.35f + 0.08f * ((i * 3) % 5), 10f)), Ui.Box("gb-sk-score", Skeleton.Bar(1f, 10f))));
            return box;
        }

        public static VisualElement Build(string gameId, int rows = 10, string title = null, object game = null)
        {
            VisualElement list = Ui.Box("gb-list");
            // A host's board has no "last 24 hours": an event's is the whole event, and a venue's is Tonight, This
            // week or All time (from 6 in the morning, in the venue's own time).
            bool host = HostMode.On, venue = HostMode.Venue;
            bool recent = !host;
            int span = 0;
            int asks = 0;
            void Load()
            {
                if (string.IsNullOrEmpty(gameId)) { GameLeaderboardScreen.Rows(list, new List<object>(), rows, recent, true); return; }
                long since = venue && span < 2 ? HostMode.Since(System.DateTime.Now, span == 1) : 0;
                string path = PathFor(gameId, game, recent, since);
                int ask = ++asks;
                // -app2BoardFile rows.json: the board from a saved API answer (to check a live board's rows from a script).
                string file = Arg("-app2BoardFile");
                if (file != null && System.IO.File.Exists(file)) { GameLeaderboardScreen.Rows(list, J.List(J.Parse(System.IO.File.ReadAllText(file))), rows, recent, true); return; }
                bool had = Cache.TryGetValue(path, out List<object> cached);
                if (had) GameLeaderboardScreen.Rows(list, cached, rows, recent, true);
                else { list.Clear(); Skeleton.Hold(list, Placeholder(6)); }
                App.Run(Api.Open(path, data =>
                {
                    if (ask != asks) return;
                    List<object> fresh = J.List(data);
                    Cache[path] = fresh;
                    GameLeaderboardScreen.Rows(list, fresh, rows, recent, true);
                }, _ =>
                {
                    if (ask != asks || had) return;
                    list.Clear();
                    list.Add(Ui.Text("Could not load the leaderboard.", "sub gb-empty", Ui.Regular));
                }));
            }
            VisualElement choice = venue ? FreePuttScreen.Choice(new[] { "Tonight", "This week", "All time" }, 0, i => { span = i; Load(); })
                : host ? new VisualElement()
                : FreePuttScreen.Choice(new[] { "Recent", "All time" }, 0, i => { recent = i == 0; Load(); });
            choice.AddToClassList("gb-choice");
            string boardName = host ? BrandKit.Str("leaderboardTitle", "Leaderboard") : "Leaderboard";
            VisualElement head = title != null && App.I != null
                ? Ui.Section(boardName, "Top 100", () => App.I.Push(new GameLeaderboardScreen(App.I, gameId, title, game)))
                : Ui.Section(boardName);
            head.AddToClassList("gb-head");
            VisualElement card = Ui.Box("glass gb-card", head, choice, list);
            if (Sheet != null) card.styleSheets.Add(Sheet);
            Load();
            return card;
        }
    }

    /// <summary>
    /// Difficulty, in its three places:
    ///   the game page's Easy / Medium / Hard (an arcade game's own level for each Player; a rule game's distances),
    ///   what goes on the leaderboard (a board has one key a game, so only the level every score there was played at),
    ///   and the catalogue's 0 to 10 rating behind the card meter and the Play tab's filter.
    /// The choice is remembered for each game on this device ("app2.level.TYPE").
    /// </summary>
    public static class GameLevels
    {
        public const int Easy = 0, Medium = 1, Hard = 2;
        public static readonly string[] Names = { "Easy", "Medium", "Hard" };
        public static int Clamp(int level) => level < Easy ? Easy : level > Hard ? Hard : level;

        // ── The distances a level deals ──

        /// <summary>The feet a rule game deals from at a level: 3 to 6 on Easy and 8 to 11 on Hard (the arcade's own short
        /// and long ranges, GameMode.DealtFeet); Medium is the game's own range, so Medium is the game as it always was.</summary>
        public static (int lo, int hi) Band(int level, int from = 3, int to = 11) => Clamp(level) == Easy ? (3, 6) : Clamp(level) == Hard ? (8, 11) : (from, to);

        /// <summary>Under the pills, for the nine-putt games (one from each distance on Medium).</summary>
        public static readonly string[] NineNotes = { "Nine putts from 3 to 6 ft.", "Nine putts, one from every distance from 3 to 11 ft.", "Nine putts from 8 to 11 ft." };
        /// <summary>For the games that deal a random distance each putt.</summary>
        public static readonly string[] RandomNotes = { "Every putt from 3 to 6 ft.", "Putts from 3 to 11 ft.", "Every putt from 8 to 11 ft." };

        // ── Remembered for each game ──

        static string Key(string gameType) => "app2.level." + (gameType ?? "").ToUpperInvariant();
        public static int Saved(string gameType) => Clamp(PlayerPrefs.GetInt(Key(gameType), Medium));
        public static void Save(string gameType, int level) => PlayerPrefs.SetInt(Key(gameType), Clamp(level));

        /// <summary>Major League's innings (MajorLeague.js: 3, 6 or 9). Three is what Puttr Arcade plays and what this
        /// build has always played, so it is where it starts, and the only length on the board (Posts).</summary>
        public static readonly int[] InningsChoices = { 3, 6, 9 };
        public const int BoardInnings = 3;
        public static int Innings
        {
            get { int n = PlayerPrefs.GetInt("app2.majorleague.innings", BoardInnings); return System.Array.IndexOf(InningsChoices, n) >= 0 ? n : BoardInnings; }
            set => PlayerPrefs.SetInt("app2.majorleague.innings", System.Array.IndexOf(InningsChoices, value) >= 0 ? value : BoardInnings);
        }

        /// <summary>Putt Star's music, remembered on this device. It changes the song only, so every genre posts alike.</summary>
        public static int PuttStarGenre
        {
            get { int n = PlayerPrefs.GetInt("app2.puttstar.genre", 0); return n >= 0 && n < PuttrArcade.PuttStarSong.GenreNames.Length ? n : 0; }
            set => PlayerPrefs.SetInt("app2.puttstar.genre", value);
        }

        // ── The leaderboard ──
        // The PUTTR board is keyed by the game alone (RoundSaver posts distance 0 for every game), so one level posts:
        // Medium, the level a game starts on. Easy and Hard rounds are saved to the account like any other, and the
        // game page says they are not posted.

        public static bool Posts(int level) => Clamp(level) == Medium;
        public static string NotPosted(string[] words) => words[Easy] + " and " + words[Hard] + " scores are not posted to the leaderboard.";

        // ── The arcade games ──

        public enum Kind
        {
            /// <summary>No Easy / Medium / Hard on the page (On Fire: its Distance is the choice, with a board for each).</summary>
            None,
            /// <summary>The arcade's own level, one for each Player (Arcade.EmbeddedLevels).</summary>
            Levels,
            /// <summary>A game with no level in the arcade that deals its own distance: the level is the range, one for the game.</summary>
            Distance,
        }

        static readonly HashSet<string> LevelGames = new HashSet<string>
        {
            "Archery", "Cricket", "Derby", "Major League", "Lanes", "Putt Star", "Dead Weight", "Send It", "Slingshot", "Spot Kick",
            "Tug of War", "Turf War", "Under the Cup", "Windmill", "Putt-ball",
        };
        static readonly HashSet<string> DistanceGames = new HashSet<string> { "Balloon Pop", "Beer Pong" };

        /// <summary>What the game page offers for an arcade game (by the arcade's own name for it, as GameBridge.ArcadeGames).</summary>
        public static Kind ArcadeKind(string arcadeName) => arcadeName == null ? Kind.None : LevelGames.Contains(arcadeName) ? Kind.Levels : DistanceGames.Contains(arcadeName) ? Kind.Distance : Kind.None;

        /// <summary>The three words for a game's levels: the app's own where it had them (MajorLeague.js, Rock.js).</summary>
        public static string[] Words(string arcadeName) =>
            arcadeName == "Major League" ? new[] { "Rookie", "Pro", "All-Star" } : arcadeName == "Putt Star" ? new[] { "Easy", "Standard", "Hard" } : Names;

        /// <summary>What each level changes in an arcade game, a line a level (the numbers are the engine's: Arcade.GoalInches,
        /// GameMode.DealtFeet, and the modes' own).</summary>
        public static string[] ArcadeNotes(string arcadeName)
        {
            switch (arcadeName)
            {
                case "Major League": return new[]
                {
                    "At-bats from 3 to 6 ft. Wide bands, and any make is at least a single.",
                    "At-bats from 5 to 9 ft. A home run is within 9° of dead center; a make outside the bands is an out.",
                    "At-bats from 8 to 11 ft. A home run is within 5°, and anything beyond 48° is an out.",
                };
                case "Putt Star": return new[]
                {
                    "A make counts double and a miss half.",
                    "A make lifts the crowd by its distance and a miss drops it by the same.",
                    "A make counts half and a miss double.",
                };
                case "Archery": return new[]
                {
                    "Putts from 3 to 6 ft. Big rings: the closer to the hole the better, and holing it is gold.",
                    "Putts from 5 to 9 ft. A centered make is gold, any other make the next ring, and a near miss scores less.",
                    "Putts from 8 to 11 ft. Only holed putts score, by how centered they go in.",
                };
                case "Windmill": return new[]
                {
                    "The whole mat counts as the hole, and a make can come in anywhere across the front of the cup.",
                    "A putt within 6 in of the hole counts, and a make must come in within 45° of straight.",
                    "Only the hole counts, and a make must come in within 30° of straight.",
                };
                case "Putt-ball": return new[] { "Putts from 3 to 5 ft.", "Putts from 6 to 8 ft.", "Putts from 9 to 11 ft." };
                case "Cricket": return new[] { "The whole mat counts as the hole.", "A putt within 6 in of the hole counts.", "Only the hole counts." };
                case "Balloon Pop": case "Beer Pong": return RandomNotes;
                default: return new[]
                {
                    "Putts from 3 to 6 ft, and the whole mat counts as the hole.",
                    "Putts from 5 to 9 ft, and a putt within 6 in of the hole counts.",
                    "Putts from 8 to 11 ft, and only the hole counts.",
                };
            }
        }

        /// <summary>Each seat's level once whoever goes first sits first (GameScreen rotates the names the same way).</summary>
        public static int[] Seated(IList<int> levels, int first)
        {
            var seats = new int[levels.Count];
            for (int i = 0; i < seats.Length; i++) seats[i] = Clamp(levels[(first + i) % levels.Count]);
            return seats;
        }

        /// <summary>The pause card's line: the one level, or each Player's when they differ.</summary>
        public static string PauseNote(string[] words, IList<int> levels, IList<string> names)
        {
            if (levels == null || levels.Count == 0) return null;
            bool same = true;
            for (int i = 1; i < levels.Count; i++) if (levels[i] != levels[0]) same = false;
            if (same || names == null || names.Count < levels.Count) return "Difficulty: " + words[Clamp(levels[0])];
            var each = new List<string>();
            for (int i = 0; i < levels.Count; i++) each.Add(names[i] + ": " + words[Clamp(levels[i])]);
            return string.Join("   ·   ", each);
        }

        /// <summary>
        /// Sets an arcade game up at its level as it starts (GameBridge.Play): the engine's level for each seat, or the
        /// distance range for a game without one, the pause card's line, and Major League's innings. seatLevels is the
        /// game page's choice in seat order; null (a game started without the page) plays the level remembered for it.
        /// Says whether the signed-in Player's score goes on the board.
        /// </summary>
        public static bool Launch(string arcadeName, string gameType, IList<int> seatLevels, IList<string> names, int me)
        {
            PuttrArcade.Arcade.EmbeddedLevels = null;
            PuttrArcade.Arcade.EmbeddedFeet = null;
            PuttrArcade.Arcade.EmbeddedPauseNote = null;
            Kind kind = ArcadeKind(arcadeName);
            if (kind == Kind.None) return true;
            string[] words = Words(arcadeName);
            int saved = Saved(gameType);
            if (kind == Kind.Distance)
            {
                (int lo, int hi) = Band(saved);
                if (saved != Medium) PuttrArcade.Arcade.EmbeddedFeet = new[] { lo, hi };
                PuttrArcade.Arcade.EmbeddedPauseNote = "Difficulty: " + words[saved];
                return Posts(saved);
            }
            var levels = new List<int>();
            if (seatLevels != null) foreach (int level in seatLevels) levels.Add(Clamp(level));
            if (levels.Count == 0) levels.Add(saved);
            PuttrArcade.Arcade.EmbeddedLevels = levels.ToArray();
            PuttrArcade.Arcade.EmbeddedPauseNote = PauseNote(words, levels, names);
            bool posts = Posts(levels[Mathf.Clamp(me, 0, levels.Count - 1)]);
            if (arcadeName == "Putt Star") PuttrArcade.PuttStarMode.EmbeddedGenre = (PuttrArcade.PuttStarGenre)PuttStarGenre;
            if (arcadeName == "Major League")
            {
                PuttrArcade.MajorLeagueMode.EmbeddedInnings = Innings;
                PuttrArcade.Arcade.EmbeddedPauseNote += "   ·   " + Innings + " innings";
                posts = posts && Innings == BoardInnings;
            }
            return posts;
        }

        /// <summary>The pause card's line for a rule game ("Difficulty: Hard", "Difficulty: Diabolical (7 doors)"), or null.</summary>
        public static string PauseNote(RuleGame rules) => rules == null || string.IsNullOrEmpty(rules.DifficultyWord) ? null : "Difficulty: " + rules.DifficultyWord;

        // ── The catalogue's rating: the card meter and the Play tab's filter ──

        /// <summary>True for a row that carries a difficulty rating at all.</summary>
        public static bool Rated(object game) => J.Get(game, "difficulty") != null;
        /// <summary>The row's rating, 0 to 10 (the API sends it as a number or as text, "8.7").</summary>
        public static double Rating(object game) => J.Num(game, "difficulty");
        /// <summary>The meter's lit bars (common/GameTile.js): one up to 3, two up to 6, three above.</summary>
        public static int Bars(double rating) => rating <= 3 ? 1 : rating <= 6 ? 2 : 3;

        public static readonly string[] Filters = { "All levels", "Beginner", "Intermediate", "Advanced" };
        /// <summary>
        /// PlayScreen.js's bands: Beginner 0 to 3, Intermediate 4 to 7, Advanced 8 to 10. The app compared whole numbers,
        /// so a custom game rated 3.5 or 7.5 matched no filter at all: here each band runs up to the next one's start.
        /// </summary>
        public static bool InFilter(double rating, int filter)
        {
            switch (filter)
            {
                case 1: return rating < 4;
                case 2: return rating >= 4 && rating < 8;
                case 3: return rating >= 8;
                default: return true;
            }
        }
    }

    /// <summary>
    /// A game's one page before play, as puttr-app's GameIntro (CenterCut is the reference): a column up to 500 wide
    /// with the title, what it is, your best, a "How to play" link, then the options and who plays, so all of it sits
    /// above the fold, with Play (or Start) pinned under it. A game with a leaderboard (hasLeaderboard) shows it as a
    /// side panel in landscape and as a trophy at the top right in portrait.
    /// </summary>
    public sealed class GameScreen : AppScreen
    {
        static readonly string[] BeerPongHowTo =
        {
            "Ten cups each, against a friend or against Puttr. Every cup is its own putt.",
            "Hole it through the front and the ball drops in the cup. Two putts a turn.",
            "Clear the rack first. Clear it shooting first and they get a rebuttal.",
        };

        readonly App _app;
        readonly object _game;
        readonly string _arcade;
        readonly VisualElement _setup;
        readonly VisualElement _levelBox = new VisualElement();   // Difficulty (and Major League's Innings), under the setup
        readonly string _title, _type;
        readonly bool _hasBoard;
        int _soloLevel = GameLevels.Medium;   // an arcade game's level when no Players picker holds it
        RuleGame _rules;
        PlayerPicker _picker;
        PracticeSession _session;          // Practice: its tees and goal

        public GameScreen(App app, object game)
        {
            _app = app; _game = game;
            string id = J.Str(game, "id"), title = J.Str(game, "title", "Game");
            int putts = J.Int(game, "putts"), feet = J.Int(game, "distance");
            _arcade = GameBridge.ArcadeNameFor(game);
            bool fire = _arcade == "On Fire";
            bool hasBoard = J.Int(game, "hasLeaderboard") == 1 && id.Length > 0;
            _title = title; _type = J.Str(game, "gameType").ToUpperInvariant(); _hasBoard = hasBoard;
            _soloLevel = GameLevels.Saved(_type);

            // Who plays and the options live here for an arcade game with Players (every arcade game for more than one) and the rule games.
            int[] range = null;
            bool arcadeSetup = _arcade != null && GameBridge.WithPlayers.TryGetValue(_arcade, out range);
            if (_arcade == null && J.Str(game, "gameType").ToUpperInvariant() != FreePuttScreen.GameType && GameBridge.RuleFor(game) == null)
                _rules = RuleCatalog.Make(game);
            if (J.Str(game, "gameType").ToUpperInvariant() == FreePuttScreen.GameType) _session = new PracticeSession();
            bool setup = arcadeSetup || _rules != null || _session != null;

            // Header: back, the title, and (portrait, with a leaderboard) the trophy.
            Label back = Ui.Text("\u2039", "back back-chevron", Ui.Medium).OnTap(app.Pop);
            VisualElement trophy = Ui.IconSquare("ListNumbers", () => app.Push(new GameLeaderboardScreen(app, id, title, game)));
            VisualElement head = Ui.Box("game-head", back, Ui.Title(title, "title-20 game-title grow"), trophy);

            VisualElement chips = Ui.Box("chips");
            chips.Add(Ui.Text(putts > 0 ? putts + " putts" : "Infinite", "chip"));
            if (feet >= 3 && feet <= 11) chips.Add(Ui.Text(feet + " ft", "chip"));
            // With a Players picker below, the count is chosen there, not repeated here.
            if (!setup) chips.Add(Ui.Text(Players(J.Int(game, "multiPlayer")), "chip"));
            // The Puttr app's game page says nothing about saving: rounds are saved. Only a development build launched with
            // -app2HoldPlay (Api.Held) says its rounds are not.
            if (GameBridge.Playable(game) && Api.HoldsPlay) chips.Add(Ui.Text(Api.NotSaved, "chip chip-amber"));

            Label best = Ui.Text("", "muted", Ui.Regular);
            best.style.marginTop = 8;
            if (id.Length > 0) App.Run(Api.Get("/game/best/" + id, data => best.text = Best(data)));

            string about = J.Str(game, "description");
            VisualElement column = Ui.Box("game-column", head);
            if (about.Length > 0) column.Add(Ui.Text(about, "sub game-about", Ui.Regular));
            column.Add(chips);
            if (!HostMode.On) column.Add(best);   // the host's own best is not the room's business: the board beside the game is

            // How to play: a link to the steps (GameIntro's "Show Rules"), so the setup stays on screen.
            string[] steps = arcadeSetup && _arcade == "Beer Pong" ? BeerPongHowTo : _rules != null ? _rules.HowTo : null;
            string rulesText = J.Str(game, "rules");
            if ((steps != null && steps.Length > 0) || (rulesText.Length > 0 && rulesText != about))
                column.Add(Ui.Button("How to play", () => Rules(title, steps, rulesText), "btn btn-ghost game-rules"));

            _setup = new VisualElement();
            if (arcadeSetup) _picker = new PlayerPicker(app, range[0], range[1], range.Length > 2 && range[2] == 1);
            else if (_rules is CompareGame) _picker = PlayerPicker.Putters(app, _rules.MinPlayers, _rules.MaxPlayers);
            else if (_rules != null && _rules.MaxPlayers > 1 && !Kid.IsKid(app.Profile)) _picker = new PlayerPicker(app, _rules.MinPlayers, _rules.MaxPlayers);
            // Host mode: every game asks who is playing, a game for one Player too (the name goes on the host's board).
            if (_picker == null && HostMode.On) _picker = new PlayerPicker(app, 1, 1, false);
            // An arcade game with its own level for each Player: with two or more playing, every row carries its chip.
            if (_picker != null && GameLevels.ArcadeKind(_arcade) == GameLevels.Kind.Levels) _picker.ShowLevels(GameLevels.Words(_arcade), _soloLevel);
            if (_picker != null) _picker.Changed = DrawLevels;
            if (setup) { column.Add(_setup); DrawSetup(); }
            // Difficulty sits under the setup for every game that has one, with or without Players (Putt Star has none).
            column.Add(_levelBox);
            if (GameVoice.Offered(game)) column.Add(GameVoice.SwitchRow());   // the legacy Commentary switch
            DrawLevels();

            // On Fire's Distance: Mixed (3 to 11 ft) or one distance for every putt. The board and best follow it.
            VisualElement board = Ui.Box("game-board");
            string boardTitle = title, boardId = id;
            void ShowBoard() { board.Clear(); board.Add(PuttrApp.Board.Build(boardId, 25, boardTitle, game)); }
            if (fire)
            {
                Label words = Ui.Text("", "muted", Ui.Regular);
                void Pick(int f)
                {
                    PuttrArcade.OnFireMode.FixedFeet = f;
                    PlayerPrefs.SetInt("app2.onfire.feet", f);
                    boardId = GameBridge.StreakGameByFeet.TryGetValue(f, out int g) ? g.ToString() : id;
                    boardTitle = f == 0 ? title : title + " · " + f + " ft";
                    App.Run(Api.Get("/game/best/" + boardId, data => best.text = Best(data)));
                    words.text = f == 0 ? "A new distance and tee every putt, 3 to 11 ft." : "Every putt from " + f + " ft; the tee still changes.";
                    if (hasBoard) ShowBoard();
                }
                int saved = Mathf.Clamp(PlayerPrefs.GetInt("app2.onfire.feet", 0), 0, 11);
                if (saved > 0 && saved < 3) saved = 0;
                var labels = new List<string> { "Mixed" };
                for (int f = 3; f <= 11; f++) labels.Add(f + " ft");
                column.Add(Ui.Box("", Form.Label("Distance"), Form.Pills(labels.ToArray(), saved == 0 ? 0 : saved - 2, i => Pick(i == 0 ? 0 : i + 2)), words));
                Pick(saved);
            }
            else if (hasBoard) ShowBoard();

            // Versus: challenge a friend to the same putts (members send; GameIntro pressVersus). Random player (ONLINE).
            if (!HostMode.On && VersusMath.Eligible(game) && GameBridge.RuleFor(game) != null && !Kid.IsKid(app.Profile) && !J.Flag(app.Profile, "isCoach"))
                column.Add(Ui.Button("Challenge a friend", () => Club.Require(app, "Versus", () => app.Push(new VersusSetupScreen(app, game))), "btn btn-ghost"));
            // Play a random player opens in place: the shuffle and Accept sit where the button was.
            if (!HostMode.On && J.Int(game, "isOnline") == 1 && GameBridge.RuleFor(game) != null && !Kid.IsKid(app.Profile))
            {
                VisualElement ghost = Ui.Box("");
                VisualElement open = null;
                open = Ui.Button("Play a random player", () =>
                {
                    open.style.display = DisplayStyle.None;
                    ghost.Clear();
                    ghost.Add(new GhostPicker(app, game, () => { ghost.Clear(); open.style.display = DisplayStyle.Flex; }).Root);
                    ghost.schedule.Execute(() => (ghost.GetFirstAncestorOfType<ScrollView>())?.ScrollTo(ghost)).StartingIn(60);
                }, "btn btn-ghost");
                column.Add(open);
                column.Add(ghost);
            }

            ScrollView scroll = Ui.Scroll();
            scroll.AddToClassList("game-scroll");
            scroll.Add(column);
            scroll.Add(FreePuttScreen.Space(16));

            // The Play/Start button stays put under the setup column while it scrolls.
            VisualElement go = GameBridge.Playable(game)
                ? Ui.Button(setup ? "Start" : "Play", Start, "btn game-go")
                : Ui.Box("btn btn-ghost btn-off game-go", Ui.Text("Not in this build yet", "btn-text", Ui.Bold));
            VisualElement foot = Ui.Box("game-foot", go);
            VisualElement main = Ui.Box("game-main", scroll, foot);

            // Landscape: the leaderboard is a narrow panel down the right (30% of the width, 250 to 340).
            ScrollView side = Ui.Scroll();
            side.AddToClassList("game-side");
            side.AddToClassList("gb-side");
            side.Add(board);
            Root = Ui.Box("game-page", main, side);
            if (PuttrApp.Board.Sheet != null) Root.styleSheets.Add(PuttrApp.Board.Sheet);
            // Host mode: the QR to the host's web leaderboard, in the bottom right corner over the leaderboard.
            VisualElement qr = null;
            if (HostMode.On)
            {
                qr = HostMode.Qr(92f);
                qr.style.position = Position.Absolute;
                qr.style.right = 16;
                qr.pickingMode = PickingMode.Ignore;
                Root.Add(qr);
            }

            bool? landscape = null;
            void Lay()
            {
                Vector4 safe = app.SafeInsets();
                foot.style.paddingBottom = 10f + safe.y;
                bool wide = Screen.width > Screen.height;
                // Over the leaderboard on its side; upright (no leaderboard panel), above the Start button.
                if (qr != null) qr.style.bottom = (wide ? 16f : 92f) + safe.y;
                if (landscape == wide) return;
                landscape = wide;
                side.style.display = wide && hasBoard ? DisplayStyle.Flex : DisplayStyle.None;
                side.style.width = Length.Percent(30);   // GameBoard.uss holds it between 250 and 340
                trophy.style.visibility = !wide && hasBoard ? Visibility.Visible : Visibility.Hidden;
            }
            Root.RegisterCallback<GeometryChangedEvent>(_ => Lay());
            Root.schedule.Execute(Lay).Every(400);
            Lay();
        }

        // GameIntro's rules modal: the game's own steps, else the catalog's rules text.
        void Rules(string title, string[] steps, string text)
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", Ui.Title(title, "title-20"));
            if (steps != null && steps.Length > 0)
                foreach (string line in steps) card.Add(Ui.Box("row rule-how", Ui.Box("rule-dot"), Ui.Text(line, "sub wrap grow", Ui.Regular)));
            else card.Add(Ui.Text(text, "sub wrap", Ui.Regular).Margin(8));
            card.Add(Ui.Button("Got it", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        // Behind the page: the game's own set, the camera swinging slowly round it (GameBridge.ShowBackdrop).
        public override void Shown() => _app.Games.ShowBackdrop(_game, _rules, () => { _app.SetBackdrop(true); Root.AddToClassList("game-see"); });

        public override void Hidden()
        {
            PuttrArcade.PuttStarMode.StopPreview();
            _app.Games.HideBackdrop();
            _app.SetBackdrop(false);
            Root.RemoveFromClassList("game-see");
        }

        static string Best(object data)
        {
            object row = J.First(data);
            int games = J.Int(row, "games");
            return games > 0 ? "Your best: " + J.Num(row, "best").ToString("0.#") + "   ·   " + games + (games == 1 ? " round played" : " rounds played") : "You have not played this one yet.";
        }

        // Players, then the game's own options (each rule game makes its options on its own instance).
        void DrawSetup()
        {
            _setup.Clear();
            if (_picker != null) _setup.Add(_picker.Root);
            VisualElement options = _rules != null ? _rules.Options() : null;
            if (options != null) _setup.Add(options);
            if (_session != null) { var practice = new VisualElement(); FreePuttScreen.Setup(practice, _session); _setup.Add(practice); }
            // A rule game's option can change whether it has a Difficulty at all (Dead Center: only for Random putts).
            if (_rules != null) _rules.OptionsChanged = DrawLevels;
            DrawLevels();
        }

        // ── Difficulty ──

        // The pills, what the chosen level does, and (for a game with a leaderboard) whether it posts there.
        VisualElement LevelPills(string[] words, string[] notes, int on, System.Action<int> pick, System.Func<int, string> board)
        {
            Label note = Ui.Text("", "muted wrap", Ui.Regular);
            Label unposted = Ui.Text("", "muted wrap", Ui.Regular);
            unposted.style.color = Ui.Amber;
            void Say(int level)
            {
                note.text = notes[level];
                string why = _hasBoard ? board(level) : null;
                unposted.text = why ?? "";
                unposted.style.display = string.IsNullOrEmpty(why) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            Say(on);
            return Ui.Box("", Form.Label("Difficulty"), Form.Pills(words, on, i => { pick(i); Say(i); }), note, unposted);
        }

        void DrawLevels()
        {
            _levelBox.Clear();
            if (_arcade != null) { ArcadeLevels(); return; }
            if (_rules == null) return;
            string[] notes = _rules.DifficultyNotes;
            // No Easy / Medium / Hard for this game (fixed putts, a spot the Player picks, or its own difficulty option):
            // it plays as it always has.
            if (notes == null) { _rules.Difficulty = GameLevels.Medium; _picker?.HideLevels(); return; }
            _rules.Difficulty = GameLevels.Saved(_type);
            RuleGame rules = _rules;
            // Two or more people (Puttr does not count): each has their own level, on their row (Matt, 2026-11-21).
            // (Turning them on redraws the picker, whose Changed draws this box again: that pass did the rest.)
            if (_picker != null && !_picker.HasLevels) { _picker.ShowLevels(GameLevels.Names, rules.Difficulty); if (_picker.Changed != null) return; }
            if (_picker != null && _picker.LevelChips)
            {
                GameLevels.Save(_type, _picker.Level);
                rules.Difficulty = _picker.Level;
                string why = _hasBoard && !GameLevels.Posts(_picker.Level) ? " " + GameLevels.NotPosted(GameLevels.Names) : "";
                _levelBox.Add(Ui.Box("", Form.Label("Difficulty"), Ui.Text("Each Player has their own level: tap it on their row to change it." + why, "muted wrap", Ui.Regular)));
                return;
            }
            _levelBox.Add(LevelPills(GameLevels.Names, notes, rules.Difficulty, i => { rules.Difficulty = i; if (_picker != null) _picker.Level = i; GameLevels.Save(_type, i); },
                level => GameLevels.Posts(level) ? null : GameLevels.NotPosted(GameLevels.Names)));
        }

        void ArcadeLevels()
        {
            GameLevels.Kind kind = GameLevels.ArcadeKind(_arcade);
            if (kind == GameLevels.Kind.None) return;
            string[] words = GameLevels.Words(_arcade), notes = GameLevels.ArcadeNotes(_arcade);
            bool league = _arcade == "Major League";
            // Major League's board is three innings on Pro: say which part of the choice keeps a score off it.
            string Unposted(int level)
            {
                if (league && GameLevels.Innings != GameLevels.BoardInnings) return "Only " + GameLevels.BoardInnings + "-inning games on " + words[GameLevels.Medium] + " are posted to the leaderboard.";
                return GameLevels.Posts(level) ? null : GameLevels.NotPosted(words);
            }
            if (league)
            {
                // MajorLeague.js's Innings card: 3, 6 or 9.
                int at = Mathf.Max(0, System.Array.IndexOf(GameLevels.InningsChoices, GameLevels.Innings));
                _levelBox.Add(Ui.Box("", Form.Label("Innings"), Form.Pills(new[] { "3", "6", "9" }, at, i => { GameLevels.Innings = GameLevels.InningsChoices[i]; DrawLevels(); })));
            }
            if (_arcade == "Putt Star")
                _levelBox.Add(Ui.Box("", Form.Label("Music"), Form.Pills(PuttrArcade.PuttStarSong.GenreNames, GameLevels.PuttStarGenre, i => { GameLevels.PuttStarGenre = i; PuttrArcade.PuttStarMode.PreviewGenre((PuttrArcade.PuttStarGenre)i); }),
                    Ui.Text("Makes speed the band up. Misses slow it down.", "muted wrap", Ui.Regular)));
            if (_picker != null && _picker.LevelChips)
            {
                // Two or more Players: the level is on each Player's row, so it is not repeated here as pills.
                GameLevels.Save(_type, _picker.Level);
                _soloLevel = _picker.Level;
                string why = _hasBoard ? Unposted(_picker.Level) : null;
                Label line = Ui.Text("Each Player has their own level: tap it on their row to change it." + (string.IsNullOrEmpty(why) ? "" : " " + why), "muted wrap", Ui.Regular);
                _levelBox.Add(Ui.Box("", Form.Label("Difficulty"), line));
                return;
            }
            int now = _picker != null && kind == GameLevels.Kind.Levels ? _picker.Level : _soloLevel;
            _levelBox.Add(LevelPills(words, notes, now, i =>
            {
                _soloLevel = i;
                if (_picker != null) _picker.Level = i;
                GameLevels.Save(_type, i);
            }, Unposted));
        }

        void Start()
        {
            if (_picker != null && !_picker.Ready) { _app.Toast(_picker.Need); return; }
            List<string> names = _picker != null ? _picker.Names : new List<string> { PlayerPicker.Short(_app.Profile) };
            if (_app.Games.VersusPending && names.Count > 1) { _app.Toast("A Versus turn is played on your own. Remove the other Players, then start."); return; }
            int first = _picker != null ? _picker.First : 0, puttr = _picker != null ? _picker.PuttrSeat : -1;
            // Who each seat saves as (a profile's user id, a guest, Puttr never): in step with names.
            List<string> ids = _picker != null ? _picker.UserIds : null;
            if (HostMode.On)
            {
                HostMode.Players = new List<string>(names);
                HostMode.SeatIds = ids != null ? new List<string>(ids) : new List<string>();
                HostMode.LowWins = J.Str(_game, "leaderboardOrder") == "asc";
                if (_picker != null) HostSession.Guests = _picker.TypedNames;   // seated again for the next game
            }
            if (_arcade != null && GameBridge.WithPlayers.ContainsKey(_arcade))
            {
                bool teams = GameBridge.TeamGames.Contains(_arcade);
                // Against Puttr: the arcade's own house side plays Puttr, so only the one Player is passed.
                // Each seat's level, where the arcade has one for every Player; else the game's one level, which
                // GameBridge reads from where the pills saved it.
                List<int> levels = GameLevels.ArcadeKind(_arcade) == GameLevels.Kind.Levels ? _picker.Levels : null;
                PuttrArcade.Arcade.EmbeddedPuttr = puttr >= 0;
                if (puttr >= 0)
                {
                    if (names.Count > 2) { _app.Toast("Puttr plays one on one. Remove Puttr to play with more Players."); return; }
                    PuttrArcade.Arcade.EmbeddedFirst = first == puttr ? 1 : 0;
                    int human = puttr == 0 ? 1 : 0;
                    _app.Games.PlayWith(_arcade, new List<string> { names[human] }, levels: levels != null ? new[] { levels[human] } : null, title: _title);
                    return;
                }
                if (teams && names.Count == 3) { _app.Toast(_title + " is played in two teams: 2 or 4 Players, or you against Puttr."); return; }
                if (_arcade == "Beer Pong") { PuttrArcade.Arcade.EmbeddedFirst = first; _app.Games.PlayWith(_arcade, names, 0, ids, title: _title); return; }
                // The other games take turns in seat order: whoever goes first sits first, and the rest follow round.
                var seats = new List<string>();
                var seatIds = new List<string>();
                for (int i = 0; i < names.Count; i++)
                {
                    seats.Add(names[(first + i) % names.Count]);
                    seatIds.Add(ids != null && ids.Count == names.Count ? ids[(first + i) % names.Count] : RoundSaver.Guest);
                }
                PuttrArcade.Arcade.EmbeddedFirst = 0;
                _app.Games.PlayWith(_arcade, seats, (names.Count - first) % names.Count, seatIds, levels != null ? GameLevels.Seated(levels, first) : null, _title);
                return;
            }
            if (_session != null)
            {
                PracticeSession playing = _session;
                if (playing.RandomTees) playing.Deal();
                // The same choices next time, in a fresh session.
                _session = new PracticeSession { RandomTees = playing.RandomTees, RangeMin = playing.RangeMin, RangeMax = playing.RangeMax, Feet = playing.Feet, Tee = playing.Tee, Goal = playing.Goal, Target = playing.Target };
                _app.Games.PlayPractice(playing, _title);
                DrawSetup();
                return;
            }
            if (_rules != null)
            {
                RuleGame playing = _rules;
                playing.Names = names;
                playing.UserIds = ids;
                playing.First = first;
                playing.Bot = puttr;
                playing.Levels = _picker != null && _picker.LevelChips && playing.DifficultyNotes != null ? _picker.Levels.ToArray() : null;
                // A fresh game for next time this page is used; its options start from the same choices.
                _rules = RuleCatalog.Make(_game);
                _app.Games.PlayRule(_game, playing);
                DrawSetup();
                return;
            }
            _app.Games.Open(_game);
        }

        /// <summary>The app's multiPlayer column: 0 one Player, 1 two, 2 two or more, 3 one or more.</summary>
        public static string Players(int multiPlayer)
        {
            switch (multiPlayer)
            {
                case 1: return "2 Players";
                case 2: return "2 or more Players";
                case 3: return "1 to 4 Players";
                default: return "1 Player";
            }
        }
    }

    // ── The top 100 ──────────────────────────────────────────────────────────

    public sealed class GameLeaderboardScreen : AppScreen
    {
        readonly App _app;
        readonly string _gameId;
        readonly object _game;
        readonly VisualElement _card;
        bool _recent = true;
        int _asks;

        public GameLeaderboardScreen(App app, string gameId, string title, object game = null)
        {
            _app = app;
            _gameId = gameId;
            _game = game;
            Root = Page.Make(app, title, out VisualElement body);
            if (Board.Sheet != null) Root.styleSheets.Add(Board.Sheet);
            string label = J.Str(game, "leaderboardLabel");
            int distance = J.Int(game, "leaderboardDistance");
            body.Add(Ui.Box("pad", Ui.Caps("Top 100", "caps caps-green"), Ui.Title("Leaderboard" + (label.Length > 0 ? " (" + label + ")" : distance > 0 ? " (" + distance + " ft)" : ""), "title-20 wrap")));
            body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "Recent", "All time" }, 0, i => { _recent = i == 0; Load(); })));
            _card = Ui.Box("glass set-card");
            body.Add(Ui.Box("pad", _card));
            Load();
        }

        void Load()
        {
            _card.Clear();
            Skeleton.Hold(_card, Board.Placeholder(10));
            string path = Board.PathFor(_gameId, _game, _recent);
            int ask = ++_asks;
            App.Run(Api.Open(path, data => { if (ask == _asks) Rows(_card, J.List(data), 100, _recent); }, _ => { if (ask != _asks) return; _card.Clear(); _card.Add(Ui.Text("Could not load the leaderboard.", "sub", Ui.Regular)); }));
        }

        // A value stored as an object inside userName without its quotes escaped (the API's publicPlayer copies
        // profile.putterData in raw: "putterData":"{"brand":"PXG",...}"), so the whole string is not JSON.
        static readonly System.Text.RegularExpressions.Regex Unescaped = new System.Text.RegularExpressions.Regex("\"([A-Za-z_]+)\":\"([\\{\\[][^\\\\]*?[\\}\\]])\"(?=\\s*[,}])");
        static readonly System.Text.RegularExpressions.Regex Hidden = new System.Text.RegularExpressions.Regex("[\u0000-\u001F\u007F-\u009F\u200B-\u200F\u202A-\u202E\u2066-\u2069\uFEFF]");

        /// <summary>The row's userName (publicPlayer as JSON) as an object. Control and bidi characters are stripped; a
        /// nested object left unescaped is re-quoted; failing that, the fields are read one by one. Never the raw text.</summary>
        public static object Player(object row)
        {
            object given = J.Get(row, "userName");
            if (given is Dictionary<string, object>) return given;
            string raw = J.Str(row, "userName");
            if (raw.Length == 0 || raw == "undefined" || raw == "null") return null;
            raw = Hidden.Replace(raw.Replace('\u00A0', ' '), "").Trim();
            if (!raw.StartsWith("{")) return new Dictionary<string, object> { { "displayName", raw } };
            if (J.Parse(raw) is Dictionary<string, object> parsed) return parsed;
            string fixedUp = Unescaped.Replace(raw, m => "\"" + m.Groups[1].Value + "\":\"" + m.Groups[2].Value.Replace("\"", "\\\"") + "\"");
            if (J.Parse(fixedUp) is Dictionary<string, object> repaired) return repaired;
            var fields = new Dictionary<string, object>();
            foreach (string key in new[] { "displayName", "name", "username", "firstName", "photoURL", "homeTown", "userId" })
            {
                var m = System.Text.RegularExpressions.Regex.Match(raw, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (m.Success) fields[key] = m.Groups[1].Value;
            }
            if (!fields.ContainsKey("displayName")) fields["displayName"] = fields.TryGetValue("name", out object n) ? n : "Player";
            return fields;
        }

        /// <summary>The board's rows: place (gold, silver, bronze), photo or brand-coloured letter, name, score; you
        /// highlighted. compact: the game page's narrow board (smaller, home town under the name).</summary>
        public static void Rows(VisualElement into, List<object> rows, int max, bool recent = false, bool compact = false)
        {
            into.Clear();
            App app = App.I;
            string me = J.Str(app != null ? app.Profile : null, "userId", Auth.Uid);
            int place = 0;
            foreach (object row in rows)
            {
                object p = Player(row);
                if (p == null) continue;
                if (++place > max) break;
                PutterBrands.Player who = PutterBrands.Resolve(new Dictionary<string, object> { { "player", p } });
                string name = string.IsNullOrWhiteSpace(who.Name) || who.Name == "Player" ? J.Str(p, "name", "Guest") : who.Name.Trim();
                string userId = J.Str(row, "userId", J.Str(p, "userId"));
                Label at = Ui.Text(place.ToString(), compact ? "board-place gb-place" : "board-place", Ui.Bold);
                if (place <= 3) at.style.color = place == 1 ? (Color)Ui.Gold : Color.white;   // the podium: gold for first, white for second and third
                double score = J.Num(row, "score");
                Label value = Ui.Num(score % 1 == 0 ? score.ToString("0", CultureInfo.InvariantCulture) : score.ToString("0.0", CultureInfo.InvariantCulture), compact ? "g-score gb-score" : "g-score");
                VisualElement avatar = Ui.Avatar(name, who.Photo);
                // LeaderRow's fist bump, left of the score: not for yourself or a guest, and it does not open the profile.
                VisualElement bump = app != null && FistBumps.CanBump(app, userId) ? FistBumps.Button(app, userId) : null;
                if (bump != null && compact) bump.style.width = bump.style.height = 28;
                VisualElement r;
                if (compact)
                {
                    avatar.AddToClassList("gb-av");
                    VisualElement words = Ui.Box("grow gb-words", Ui.Text(name, "gb-name", Ui.Bold));
                    if (who.HomeTown.Length > 0) words.Add(Ui.Text(who.HomeTown, "gb-town", Ui.Regular));
                    r = Ui.Box("gb-row", at, avatar, words, bump, value);
                }
                else
                {
                    VisualElement words = Ui.Box("grow", Ui.Text(name, "g-name", Ui.Bold));
                    if (who.HomeTown.Length > 0) words.Add(Ui.Text(who.HomeTown, "muted", Ui.Regular));
                    r = Ui.Box("board-row", at, avatar, words, bump, value);
                }
                if (userId == me && me.Length > 0) r.AddToClassList("gb-me");
                bool guest = string.IsNullOrEmpty(userId) || userId == "0";
                VisualElement over = into;
                r.OnTap(() => { if (app == null) return; if (guest) Dialog.Ask(over, "Guest Player", "This player was a guest and doesn't have a Puttr profile.", "OK", () => { }); else ProfileScreen.Open(app, userId, p); });
                into.Add(r);
            }
            if (place == 0) into.Add(Ui.Text(recent ? "No scores in the last 24 hours. Try All time." : "No scores yet. Yours could be the first.", compact ? "sub gb-empty" : "sub", Ui.Regular).Margin(compact ? 8 : 12));
        }
    }
}
