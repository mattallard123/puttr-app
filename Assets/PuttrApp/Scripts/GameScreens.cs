// The shared screens around a game: its page before play (what it is, your best, the top
// scores) and the results after. In puttr-app these are GameIntro, GameOver and the leaderboard.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>
    /// A game's board on its page, as puttr-app's Leaderboard: the PUTTR club board, Recent (the last 24 hours, first)
    /// or All time, with each Player's photo or brand-coloured letter.
    /// </summary>
    public static class Board
    {
        public static VisualElement Build(string gameId, int rows = 10, string title = null, object game = null)
        {
            VisualElement list = new VisualElement();
            bool recent = true;
            void Load()
            {
                list.Clear();
                list.Add(Spinner.Small().Margin(12));
                if (string.IsNullOrEmpty(gameId)) { GameLeaderboardScreen.Rows(list, new List<object>(), rows); return; }
                var query = new List<string>();
                if (recent) query.Add("hours=24");
                if (J.Str(game, "leaderboardOrder") == "asc") query.Add("order=asc");
                string path = "/club/leaderboard/PUTTR/" + gameId + "/" + J.Int(game, "leaderboardDistance") + (query.Count > 0 ? "/?" + string.Join("&", query) : "");
                bool asked = recent;
                App.Run(Api.Open(path, data => { if (asked == recent) GameLeaderboardScreen.Rows(list, J.List(data), rows, recent); },
                    _ => { list.Clear(); list.Add(Ui.Text("Could not load the leaderboard.", "sub", Ui.Regular)); }));
            }
            VisualElement card = Ui.Box("glass",
                FreePuttScreen.Choice(new[] { "Recent", "All time" }, 0, i => { recent = i == 0; Load(); }),
                list);
            if (title != null && App.I != null) card.Add(Ui.Text("See the top 100  ›", "sec-link center", Ui.Medium).Margin(10).OnTap(() => App.I.Push(new GameLeaderboardScreen(App.I, gameId, title, game))));
            Load();
            return card;
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

            // Who plays and the options live here for an arcade game with Players (Beer Pong) and the rule games.
            int[] range = null;
            bool arcadeSetup = _arcade != null && GameBridge.WithPlayers.TryGetValue(_arcade, out range);
            if (_arcade == null && J.Str(game, "gameType").ToUpperInvariant() != FreePuttScreen.GameType && GameBridge.RuleFor(game) == null)
                _rules = RuleCatalog.Make(game);
            if (J.Str(game, "gameType").ToUpperInvariant() == FreePuttScreen.GameType) _session = new PracticeSession();
            bool setup = arcadeSetup || _rules != null || _session != null;

            // Header: back, the title, and (portrait, with a leaderboard) the trophy.
            Label back = Ui.Text("\u2039", "back back-chevron", Ui.Medium).OnTap(app.Pop);
            VisualElement trophy = Ui.Box("game-trophy", Ui.Icon("Trophy", 20, Color.white)).OnTap(() => app.Push(new GameLeaderboardScreen(app, id, title, game)));
            VisualElement head = Ui.Box("game-head", back, Ui.Text(title, "game-title grow", Ui.Bold), trophy);

            VisualElement chips = Ui.Box("chips");
            chips.Add(Ui.Text(putts > 0 ? putts + " putts" : "Infinite", "chip"));
            if (feet >= 3 && feet <= 11) chips.Add(Ui.Text(feet + " ft", "chip"));
            // With a Players picker below, the count is chosen there, not repeated here.
            if (!setup) chips.Add(Ui.Text(Players(J.Int(game, "multiPlayer")), "chip"));
            if (GameBridge.Playable(game)) chips.Add(Ui.Text("Saves to your account", "chip chip-green"));

            Label best = Ui.Text("", "muted", Ui.Regular);
            best.style.marginTop = 8;
            if (id.Length > 0) App.Run(Api.Get("/game/best/" + id, data => best.text = Best(data)));

            string about = J.Str(game, "description");
            VisualElement column = Ui.Box("game-column", head);
            if (about.Length > 0) column.Add(Ui.Text(about, "sub game-about", Ui.Regular));
            column.Add(chips);
            column.Add(best);

            // How to play: a link to the steps (GameIntro's "Show Rules"), so the setup stays on screen.
            string[] steps = arcadeSetup && _arcade == "Beer Pong" ? BeerPongHowTo : _rules != null ? _rules.HowTo : null;
            string rulesText = J.Str(game, "rules");
            if ((steps != null && steps.Length > 0) || (rulesText.Length > 0 && rulesText != about))
                column.Add(Ui.Box("game-rules", Ui.Icon("Question", 14, new Color(1, 1, 1, 0.6f)), Ui.Text("How to play", "game-rules-t", Ui.Medium)).OnTap(() => Rules(title, steps, rulesText)));

            _setup = new VisualElement();
            if (arcadeSetup) _picker = new PlayerPicker(app, range[0], range[1]);
            else if (_rules is CompareGame) _picker = PlayerPicker.Putters(app, _rules.MinPlayers, _rules.MaxPlayers);
            else if (_rules != null && _rules.MaxPlayers > 1 && !Kid.IsKid(app.Profile)) _picker = new PlayerPicker(app, _rules.MinPlayers, _rules.MaxPlayers);
            if (setup) { column.Add(_setup); DrawSetup(); }

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
            if (VersusMath.Eligible(game) && GameBridge.RuleFor(game) != null && !Kid.IsKid(app.Profile) && !J.Flag(app.Profile, "isCoach"))
                column.Add(Ui.Button("Challenge a friend", () => Club.Require(app, "Versus", () => app.Push(new VersusSetupScreen(app, game))), "btn btn-ghost"));
            // Play a random player opens in place: the shuffle and Accept sit where the button was.
            if (J.Int(game, "isOnline") == 1 && GameBridge.RuleFor(game) != null && !Kid.IsKid(app.Profile))
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
            if (fire) column.AddToClassList("fire");

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

            // Landscape: the leaderboard is a panel down the right (38% of the width, at most 500).
            ScrollView side = Ui.Scroll();
            side.AddToClassList("game-side");
            side.Add(board);
            Root = Ui.Box("game-page", main, side);

            bool? landscape = null;
            void Lay()
            {
                Vector4 safe = app.SafeInsets();
                foot.style.paddingBottom = 10f + safe.y;
                bool wide = Screen.width > Screen.height;
                if (landscape == wide) return;
                landscape = wide;
                side.style.display = wide && hasBoard ? DisplayStyle.Flex : DisplayStyle.None;
                side.style.width = Length.Percent(38);
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
            VisualElement card = Ui.Box("modal", Ui.Text(title, "h2", Ui.Bold));
            if (steps != null && steps.Length > 0)
                foreach (string line in steps) card.Add(Ui.Box("row rule-how", Ui.Box("rule-dot"), Ui.Text(line, "sub wrap grow", Ui.Regular)));
            else card.Add(Ui.Text(text, "sub wrap", Ui.Regular).Margin(6));
            card.Add(Ui.Button("Got it", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        // Behind the page: the game's own set, the camera swinging slowly round it (GameBridge.ShowBackdrop).
        public override void Shown() => _app.Games.ShowBackdrop(_game, _rules, () => { _app.SetBackdrop(true); Root.AddToClassList("game-see"); });

        public override void Hidden()
        {
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
        }

        void Start()
        {
            if (_picker != null && !_picker.Ready) { _app.Toast(_picker.Need); return; }
            List<string> names = _picker != null ? _picker.Names : new List<string> { PlayerPicker.Short(_app.Profile) };
            int first = _picker != null ? _picker.First : 0, puttr = _picker != null ? _picker.PuttrSeat : -1;
            if (_arcade != null && GameBridge.WithPlayers.ContainsKey(_arcade))
            {
                // Beer Pong against Puttr is the arcade's own house side: you, with Puttr at the other end.
                if (puttr >= 0) { PuttrArcade.Arcade.EmbeddedFirst = first == puttr ? 1 : 0; _app.Games.PlayWith(_arcade, new List<string> { names[0] }); }
                else { PuttrArcade.Arcade.EmbeddedFirst = first; _app.Games.PlayWith(_arcade, names); }
                return;
            }
            if (_session != null)
            {
                PracticeSession playing = _session;
                if (playing.RandomTees) playing.Deal();
                // The same choices next time, in a fresh session.
                _session = new PracticeSession { RandomTees = playing.RandomTees, RangeMin = playing.RangeMin, RangeMax = playing.RangeMax, Feet = playing.Feet, Tee = playing.Tee, Goal = playing.Goal, Target = playing.Target };
                _app.Games.PlayPractice(playing);
                DrawSetup();
                return;
            }
            if (_rules != null)
            {
                RuleGame playing = _rules;
                playing.Names = names;
                playing.First = first;
                playing.Bot = puttr;
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
}
