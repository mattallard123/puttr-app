// Plays a Puttr Arcade game from the app and saves it to the Player's account (RoundSaver).
//
// The arcade is built the first time a game is played and kept, switched off, between games.
// It listens to the app's one Puttr connection (Green.Console).
using System.Collections.Generic;
using Puttr;
using PuttrArcade;
using UnityEngine;

namespace PuttrApp
{
    public sealed class GameBridge
    {
        readonly App _app;
        GameObject _arcadeObject;
        Arcade _arcade;
        RoundSaver _saver;
        bool _playing;
        string _playingName;
        /// <summary>The score of the round that just finished, until the Player is back in the app.</summary>
        int? _finishedScore;

        public GameBridge(App app)
        {
            _app = app;
            Arcade.AppJudge = Verdicts.Judge;
            Arcade.AppGameStarted += OnStarted;
            Arcade.AppPutt += OnPutt;
            Arcade.AppGameEnded += OnEnded;
            Arcade.AppExit += OnExit;
        }

        /// <summary>
        /// The streak games, retired from this build (Matt, 2026-11-16): On Fire's Distance option plays them.
        /// A fixed distance saves as the old game for that distance, so its board and history carry on.
        /// Ladder and Up & Back (a distance that climbs) are retired with them.
        /// </summary>
        public static readonly Dictionary<int, int> StreakGameByFeet = new Dictionary<int, int>
        {
            { 3, 73 }, { 4, 74 }, { 5, 75 }, { 6, 76 }, { 7, 78 }, { 8, 79 }, { 9, 80 }, { 10, 81 }, { 11, 82 },
        };

        /// <summary>A catalog game this build no longer offers: the Puttr streak and standard games (Straight Up, Three to
        /// Five...), which stay in the legacy app (Matt, 2026-11-17). Custom streak and standard games still play.</summary>
        public static bool Retired(object game)
        {
            string type = J.Str(game, "gameType").ToUpperInvariant();
            return (type == "STREAK" || type == "STANDARD") && J.Int(game, "creatorId") == 0 && J.Str(game, "userId").Length == 0;
        }

        /// <summary>The arcade game that plays an API game, or null if it has not been brought over.</summary>
        public static string ArcadeNameFor(object game)
        {
            string type = J.Str(game, "gameType").ToUpperInvariant();
            foreach (KeyValuePair<string, string> a in ArcadeGames) if (a.Value == type) return a.Key;
            return type == "ONFIRE" ? "On Fire" : null;
        }

        /// <summary>True for a game this build can play: an arcade game, one of the app's own screens, or a putt-list game on the 3D ramp.</summary>
        public static bool Playable(object game) => ArcadeNameFor(game) != null || J.Str(game, "gameType").ToUpperInvariant() == FreePuttScreen.GameType || RuleFor(game) != null || RuleCatalog.Has(game);

        /// <summary>
        /// A catalog game that is a fixed list of putts scored putt by putt plays on the 3D ramp as a round:
        /// STANDARD scores a make's distance (games/Standard.js), CENTERCUT its distance less the share off line.
        /// </summary>
        public static RoundRule? RuleFor(object game)
        {
            string type = J.Str(game, "gameType").ToUpperInvariant();
            if (TournamentMath.Putts(game).Count == 0) return null;
            if (type == "STANDARD") return RoundRule.Feet;
            if (type == "CENTERCUT") return RoundRule.CenterCut;
            if (type == "STREAK") return RoundRule.Make; // until the first miss (PlayCatalog sets StopOnMiss)
            return null;
        }

        /// <summary>Opens whatever plays this API game. False if nothing does yet.</summary>
        public bool Open(object game)
        {
            string arcade = ArcadeNameFor(game);
            // Games with a setup (who plays, options) set up on their own page, with the Start button.
            if (arcade != null && WithPlayers.ContainsKey(arcade)) { _app.Push(new GameScreen(_app, game)); return true; }
            if (arcade != null) { Play(arcade); return true; }
            // Practice sets up on the game page like every game (GameScreen), then plays.
            if (J.Str(game, "gameType").ToUpperInvariant() == FreePuttScreen.GameType) { _app.Push(new GameScreen(_app, game)); return true; }
            if (RuleFor(game) != null) { _app.Pop(); PlayCatalog(game, null); return true; }
            RuleGame rules = RuleCatalog.Make(game);
            if (rules != null) { _app.Push(new GameScreen(_app, game)); return true; }
            return false;
        }

        /// <summary>A catalog putt-list game as itself, or as a Versus turn (versusId).</summary>
        public void PlayCatalog(object game, string versusId)
        {
            RoundRule rule = RuleFor(game) ?? RoundRule.Feet;
            string title = J.Str(game, "title", "Game");
            var session = new RoundSession { Title = title, Detail = versusId != null ? "Versus" : "", Rule = rule, Putts = TournamentMath.Putts(game), StopOnMiss = J.Str(game, "gameType").ToUpperInvariant() == "STREAK" };
            StartRound(new CompetitionRound(CompetitionRound.Kind.Game, session, J.Str(game, "id")) { VersusId = versusId, Round = game }, title, null);
        }

        /// <summary>A quest level: its putts, one point a make (a Precision level only inside the angle window), a Streak level over at the first miss.</summary>
        public void PlayQuest(object quest, object level)
        {
            string type = J.Str(level, "levelType").ToUpperInvariant();
            object stars = J.Get(level, "levelStars");
            if (stars is string text) stars = J.Parse(text);
            var session = new RoundSession
            {
                Title = "Level " + J.Int(level, "levelNum"), Detail = J.Str(quest, "title"),
                Rule = type == "REDUCER" ? RoundRule.Precision : RoundRule.Make, StopOnMiss = type == "STREAK", Putts = TournamentMath.Putts(level),
            };
            StartRound(new CompetitionRound(CompetitionRound.Kind.Quest, session, null) { QuestId = J.Str(quest, "id"), LevelId = J.Str(level, "id"), LevelStars = J.List(stars), Tournament = quest, Round = level }, "Level " + J.Int(level, "levelNum"), null);
        }

        /// <summary>A lesson's drill: its putts, a make from the front scoring its distance. Saved nowhere, as in the app.</summary>
        public void PlayLessonDrill(object lesson)
        {
            var session = new RoundSession { Title = "Drill", Detail = J.Str(lesson, "title"), Rule = RoundRule.Feet, Putts = TournamentMath.Putts(new Dictionary<string, object> { { "puttData", J.Get(lesson, "drillPutts") } }) };
            var round = new CompetitionRound(CompetitionRound.Kind.Drill, session, null) { Round = lesson };
            string type = J.Str(lesson, "drillType").ToUpperInvariant();
            // LessonDrill SPEED: the score is the spread of the rollouts (max minus min, lower is better), not the makes.
            if (type == "SPEED") round.OnDone = left => _app.Push(new SpeedDrillResultScreen(_app, left));
            // TEMPO: the metronome ticks through the drill (85 bpm unless the Player set another).
            if (type == "TEMPO") Metronome.Start(Metronome.Bpm);
            StartRound(round, "Drill", null);
        }

        /// <summary>ONLINE against a random opponent's ghost: their saved putts (GET /games/challenge/getGame) scored beside yours.</summary>
        public void PlayGhost(object game, object opponent, object data)
        {
            RoundRule rule = RuleFor(game) ?? RoundRule.Feet;
            string title = J.Str(game, "title", "Game");
            var session = new RoundSession { Title = title, Detail = "vs " + J.Str(opponent, "displayName"), Rule = rule, Putts = TournamentMath.Putts(game) };
            var ghost = new List<double>();
            object row = J.First(J.Get(data, "game"));
            object putts = J.Get(row, "playerPutts");
            if (putts is string text) putts = J.Parse(text);
            foreach (object v in J.List(J.First(putts))) ghost.Add(v is bool b ? (b ? 1 : 0) : Puttr.PuttrJson.Number(v, 0));
            StartRound(new CompetitionRound(CompetitionRound.Kind.Game, session, J.Str(game, "id")) { Round = game, Ghost = ghost, GhostName = J.Str(opponent, "displayName", "Opponent"), GhostPhoto = J.Str(opponent, "photoURL") }, title, null);
        }

        /// <summary>The coach's first-run read: five putts at 6 ft, straight, saved as practice; done gets the round.</summary>
        public void PlayCalibration(System.Action<CompetitionRound> done)
        {
            var putts = new List<TournamentMath.Dealt>();
            for (int i = 0; i < 5; i++) putts.Add(new TournamentMath.Dealt { Feet = 6, Tee = 2 });
            var session = new RoundSession { Title = "Five putts, 6 ft, straight", Detail = "Calibration", Rule = RoundRule.Feet, Putts = putts };
            StartRound(new CompetitionRound(CompetitionRound.Kind.Practice, session, null) { OnDone = done }, "Calibration", null);
        }

        // ── A rule game (RuleGames.cs): its rules over the ramp, its panel under it ──────

        RuleGame _rule;
        object _ruleGame;
        RulePanel _rulePanel;

        // ── The game page's backdrop: the game's own set, the camera swinging slowly round it ──

        string _backdropWanted;

        /// <summary>Which arcade set stands behind a game's page: its own game, its rule game's 3D world, else the ramp.</summary>
        public static string BackdropFor(object game, RuleGame rules)
        {
            string arcade = ArcadeNameFor(game);
            if (arcade != null) return arcade;
            if (rules != null && RuleViews.For(rules.GameType) != null) return PuttrArcade.RuleStageMode.Title;
            return FreePuttMode.Title;
        }

        /// <summary>Puts the set up behind the game page; ready() once it shows (the first time, the arcade builds first).</summary>
        public void ShowBackdrop(object game, RuleGame rules, System.Action ready)
        {
            if (_playing) return;
            string mode = BackdropFor(game, rules);
            if (mode == PuttrArcade.RuleStageMode.Title)
            {
                PuttrArcade.RuleStageMode.NextScene = RuleViews.For(rules.GameType);
                try { PuttrArcade.RuleStageMode.StartView = RuleViews.State(rules); } catch (System.Exception) { PuttrArcade.RuleStageMode.StartView = null; }
            }
            _backdropWanted = mode;
            App.Run(Backdrop(mode, ready));
        }

        System.Collections.IEnumerator Backdrop(string mode, System.Action ready)
        {
            if (_arcadeObject == null)
            {
                PuttrArcade.Loader.Quiet = true;   // builds behind the page, out of sight
                MakeArcade(null);
            }
            else _arcadeObject.SetActive(true);
            while (_arcade == null || !_arcade.IsReady) yield return null;
            if (_playing || _backdropWanted != mode) yield break;
            _arcade.Backdrop(_arcade.IndexOf(mode));
            ready?.Invoke();
        }

        public void HideBackdrop()
        {
            _backdropWanted = null;
            if (_playing || _arcade == null) return;
            _arcade.EndBackdrop();
            _arcadeObject.SetActive(false);
        }

        void MakeArcade(string gameName)
        {
            PuttrConsole console = Green.Console;
            Arcade.Embedded = true;
            Arcade.EmbeddedGameName = gameName;
            // Built switched off, so the console the arcade asks for on its own object never starts:
            // the arcade uses the app's (Arcade.SharedConsole).
            _arcadeObject = new GameObject("Puttr Arcade");
            _arcadeObject.SetActive(false);
            Object.DontDestroyOnLoad(_arcadeObject);
            _arcade = _arcadeObject.AddComponent<Arcade>();
            foreach (PuttrConsole own in _arcadeObject.GetComponents<PuttrConsole>()) if (own != console) own.enabled = false;
            _arcadeObject.SetActive(true); // builds itself behind its own loader, then starts the game (if one is named)
        }

        // Started before a backdrop's build finished: the game starts once the arcade is ready.
        System.Collections.IEnumerator LaunchWhenReady(string arcadeName)
        {
            PuttrArcade.Loader.Quiet = false;
            while (_arcade == null || !_arcade.IsReady) yield return null;
            if (_playing && _playingName == arcadeName) _arcade.Launch(_arcade.IndexOf(arcadeName));
        }

        public void PlayRule(object game, RuleGame rules)
        {
            if (_playing || _round != null || _live != null || _rule != null) return;
            _rule = rules;
            _ruleGame = game;
            rules.Start();
            FreePuttMode.Label = rules.Title;
            FreePuttMode.Silent = true;
            FreePuttMode.Counts = putt => Verdicts.Of(putt, _rule != null ? _rule.Feet : 6, _rule != null ? _rule.Tee : 2).Made;
            // On Puttr's turn only Puttr's own (simulated) putt counts.
            Arcade.AppAcceptsPutts = () => _rule != null && !_rule.Over && (_rule.P != _rule.Bot || _botShot);
            _rulePanel = new RulePanel(_app, rules, () => { if (_arcade != null) _arcade.Leave(); });
            System.Func<PuttrArcade.RuleScene> scene = RuleViews.For(rules.GameType);
            rules.Changed = () => { DealRule(); if (_rulePanel != null) _rulePanel.Show(); if (scene != null) PuttrArcade.RuleStageMode.Instance?.Changed(RuleViews.State(rules)); };
            DealRule();
            if (scene != null)
            {
                // Its own 3D world over the panel (RuleScene), in place of the plain mat.
                PuttrArcade.RuleStageMode.NextScene = scene;
                PuttrArcade.RuleStageMode.StartView = RuleViews.State(rules);
                PuttrArcade.RuleStageMode.Label = rules.Title;
                PuttrArcade.RuleStageMode.Bed = rules.Bed;
                Play(PuttrArcade.RuleStageMode.Title);
            }
            else Play(FreePuttMode.Title);
            // The game's own row, so its board and history are the game's (Play looks the row up by type otherwise).
            if (J.Str(game, "id").Length > 0) _saver.UseGame(J.Str(game, "id"));
            if (scene == null) App.Run(StartBed(rules));
            if (Fixtures.On) App.Run(DemoPutts());
            if (rules.P == rules.Bot) App.Run(BotTurn(rules, 3f));   // Puttr goes first
        }

        // ── Puttr as a Player: a simulated putt on its turn, played like any other, never saved ──

        bool _botShot, _botAnswered;
        readonly System.Random _botDice = new System.Random();

        System.Collections.IEnumerator BotTurn(RuleGame g, float wait)
        {
            while (_arcade == null || !_arcade.IsReady) yield return null;
            yield return new WaitForSecondsRealtime(wait);
            if (_rule != g || g.Over || g.P != g.Bot || !_playing) yield break;
            g.BotPrepare(_botDice);
            DealRule();
            if (_rulePanel != null) _rulePanel.Show();
            yield return new WaitForSecondsRealtime(1.2f);
            if (_rule != g || g.Over || g.P != g.Bot || !_playing) yield break;
            // A decent putter: more short ones holed than long ones, a centred line when it goes in.
            double make = Mathf.Lerp(0.75f, 0.4f, Mathf.InverseLerp(3f, 11f, g.Feet));
            bool good = _botDice.NextDouble() < make;
            float line = good ? (float)(_botDice.NextDouble() * 1.4 - 0.7) : (float)((_botDice.NextDouble() < 0.5 ? -1 : 1) * (1.4 + _botDice.NextDouble() * 2.6));
            float speed = good ? 34f + (float)_botDice.NextDouble() * 12f : 28f + (float)_botDice.NextDouble() * 40f;
            float heading = g.Tee == 1 ? 3f : g.Tee == 3 ? -3f : 0f;
            PuttrConsole console = Green.Console;
            Hole hole = console.HasHole ? console.Hole : PuttrSamples.SampleHole;
            _botShot = true;
            _botAnswered = false;
            console.Inject(PuttrSamples.Synthesize(line, speed, heading + (float)(_botDice.NextDouble() * 2 - 1), hole), true);
            yield return new WaitForSecondsRealtime(2f);
            if (_botAnswered) yield break;
            _botShot = false;   // turned away (no hole yet): try again
            if (_rule == g && !g.Over && g.P == g.Bot) App.Run(BotTurn(g, 1f));
        }

        // The arcade is built on the first game: its sound is there once it is ready.
        System.Collections.IEnumerator StartBed(RuleGame rules)
        {
            while (_arcade == null || !_arcade.IsReady) yield return null;
            if (_rule == rules && rules.Bed != null) _arcade.Audio.GameBed(rules.Bed, 0.3f);
        }

        void DealRule()
        {
            if (_rule == null) return;
            FreePuttMode.Feet = _rule.Feet;
            FreePuttMode.TeeNow = _rule.Tee;
            Green.Console.distanceFt = _rule.Feet;
            Green.Console.tee = _rule.Tee;
        }

        // ── Free putt: the 3D game with the session's numbers beside it ──────

        PracticeSession _practice;
        PracticePanel _panel;

        static System.Collections.IEnumerator Later(System.Action act) { yield return null; act(); }

        public void PlayPractice(PracticeSession session)
        {
            if (_playing) return;
            _practice = session;
            FreePuttMode.Feet = session.Feet;
            FreePuttMode.TeeNow = session.Tee;
            _panel = new PracticePanel(_app, session, () => { if (_arcade != null) _arcade.Leave(); });
            Play(FreePuttMode.Title);
            if (Fixtures.On) App.Run(DemoPutts());
        }

        // With made-up data there is no Puttr: a few made-up putts, so the screen has something to show. Never saved.
        System.Collections.IEnumerator DemoPutts()
        {
            while (_arcade == null || !_arcade.IsReady) yield return null;
            var dice = new System.Random(11);
            for (int i = 0; i < 12 && _playing; i++)   // enough for a nine-putt game to reach its final screen
            {
                yield return new WaitForSecondsRealtime(i == 0 ? 1.2f : 4.5f);
                bool good = i % 3 != 1;
                float line = good ? (float)(dice.NextDouble() * 1.4 - 0.7) : (float)(dice.NextDouble() * 7.0 - 3.5);
                float speed = good ? 34f + (float)dice.NextDouble() * 14f : 30f + (float)dice.NextDouble() * 40f;
                if (_playing) Green.Console.Inject(PuttrSamples.Synthesize(line, speed, (float)(dice.NextDouble() * 4.0 - 2.0), PuttrSamples.SampleHole), true);
            }
        }

        // ── A round of a tournament, or a League Night game ──────────────────

        CompetitionRound _round;
        RoundPanel _roundPanel;
        LiveMatch _live;
        LivePanel _livePanel;

        /// <summary>A live 1v1 (Live.cs): this Player's putts go to the room; the panel shows both scores.</summary>
        public void PlayLive(object game)
        {
            if (_playing || _round != null || _live != null) return;
            if (Kid.IsKid(_app.Profile)) { _app.Toast("Online play is off for Kid profiles."); return; }
            _live = LiveMatch.Start(game);
            _livePanel = new LivePanel(_live, () => { if (_arcade != null) _arcade.Leave(); });
            FreePuttMode.Label = "Live";
            FreePuttMode.Counts = putt => putt.Made;
            Arcade.AppAcceptsPutts = () => _live != null && _live.MyTurn;
            _live.Changed += DealLive;
            DealLive();
            Play(FreePuttMode.Title);
        }

        void DealLive()
        {
            if (_live == null) return;
            TournamentMath.Dealt next = _live.Now;
            FreePuttMode.Feet = next.Feet;
            FreePuttMode.TeeNow = next.Tee;
            Green.Console.distanceFt = next.Feet;
            Green.Console.tee = next.Tee;
        }

        /// <summary>Round n of a tournament: its putts as dealt, a make scoring its distance in feet.</summary>
        public void PlayRound(object tournament, object round, int number)
        {
            var session = new RoundSession { Title = "Round " + number, Detail = J.Str(tournament, "title"), Rule = RoundRule.Feet, Putts = TournamentMath.Putts(round) };
            StartRound(new CompetitionRound(CompetitionRound.Kind.Tournament, session, J.Str(round, "gameId")) { Tournament = tournament, Round = round, RoundNumber = number }, "Round " + number, "TOURNAMENT");
        }

        /// <summary>This week's League Night game: nine putts, one from each distance, scored by how true they roll in.</summary>
        public void PlayLeague(object season, object week, string team)
        {
            var session = new RoundSession { Title = "League Night", Detail = "Week " + J.Int(week, "weekNumber"), Rule = RoundRule.CenterCut, Putts = RoundSession.Shuffled(new System.Random()) };
            StartRound(new CompetitionRound(CompetitionRound.Kind.League, session, J.Str(week, "gameId")) { SeasonId = J.Str(season, "id"), WeekNumber = J.Int(week, "weekNumber"), Team = team }, "League Night", "CENTERCUT");
        }

        /// <summary>Today's coach session: the putts the analysis dealt, saved as the coach's session.</summary>
        public void PlayCoach(List<TournamentMath.Dealt> putts, string title)
        {
            var session = new RoundSession { Title = "Today's session", Detail = title, Rule = RoundRule.Feet, Putts = putts };
            StartRound(new CompetitionRound(CompetitionRound.Kind.Coach, session, null), "Session", null);
        }

        /// <summary>A drill on the weakest spots, from Full analysis: saved as practice.</summary>
        public void PlayDrill(List<TournamentMath.Dealt> putts)
        {
            var session = new RoundSession { Title = "Drill", Detail = "Worst to first", Rule = RoundRule.Feet, Putts = putts };
            StartRound(new CompetitionRound(CompetitionRound.Kind.Practice, session, null), "Drill", null);
        }

        void StartRound(CompetitionRound round, string label, string channelType)
        {
            if (_playing || _round != null) return;
            if (round.Session.Putts.Count == 0) { _app.Toast("This round has no putts set yet."); return; }
            string kicker = round.Session.Detail.Length > 0 ? round.Session.Detail : round.Session.Title;
            _app.Push(new MatchupScreen(_app, round.Session, kicker, label == "Session" ? round.Session.Title : label, () => Begin(round, label, channelType)));
        }

        void Begin(CompetitionRound round, string label, string channelType)
        {
            if (_playing || _round != null) return;
            _round = round;
            PuttrConsole console = Green.Console;
            App.Run(round.Open(problem =>
            {
                if (problem != null) { _round = null; _app.Toast(problem); return; }
                if (round.Session.Done) { _round = null; _app.Toast("You have already played this round."); return; }
                FreePuttMode.Label = label;
                Deal();
                _roundPanel = new RoundPanel(_app, round, () => { if (_arcade != null) _arcade.Leave(); });
                // On camera, when there is one and the round is being saved: a tournament records in
                // parts (a part starts at the putt the round was opened or picked up at), the league in one.
                if (channelType != null && round.How != CompetitionRound.Mode.Local && Capture.Supported)
                    Capture.One.Begin(channelType, round.UserGameId, round.What == CompetitionRound.Kind.Tournament ? round.Session.Index : (int?)null);
                // A holed putt counts only if it came in from the front; the call says so.
                FreePuttMode.Counts = putt =>
                {
                    if (_round == null || _round.Session.Done) return putt.Made;
                    TournamentMath.Dealt now = _round.Session.Now;
                    AppPutt.Verdict v = Verdicts.Of(putt, now.Feet, now.Tee);
                    return RoundSession.Score(_round.Session.Rule, now.Feet, now.Tee, v.Count, v.Entry, now.Angle) > 0;
                };
                Arcade.AppAcceptsPutts = () => _round != null && !_round.Session.Done && (Capture.One.Now == Capture.State.Off || Capture.One.Ready);
                Play(FreePuttMode.Title);
                if (Fixtures.On) App.Run(DemoPutts());
            }));
        }

        void Deal()
        {
            if (_round == null || _round.Session.Done) return;
            TournamentMath.Dealt next = _round.Session.Now;
            FreePuttMode.Feet = next.Feet;
            FreePuttMode.TeeNow = next.Tee;
            Green.Console.distanceFt = next.Feet;
            Green.Console.tee = next.Tee;
        }

        // Play again: a coach session or a drill can be; a tournament round or a league game cannot (once each).
        System.Action Again(CompetitionRound left)
        {
            List<TournamentMath.Dealt> putts = left.Session.Putts;
            if (left.What == CompetitionRound.Kind.Coach) return () => PlayCoach(putts, left.Session.Detail);
            if (left.What == CompetitionRound.Kind.Practice) return () => PlayDrill(putts);
            if (left.What == CompetitionRound.Kind.Quest) { object q = left.Tournament, l = left.Round; return () => PlayQuest(q, l); }
            if (left.What == CompetitionRound.Kind.Drill) { object lesson = left.Round; return () => PlayLessonDrill(lesson); }
            if (left.What == CompetitionRound.Kind.Game && string.IsNullOrEmpty(left.VersusId)) { object g = left.Round; return () => PlayCatalog(g, null); }
            return null;
        }

        System.Collections.IEnumerator LeaveRuleAfter(RuleGame g, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (_rule == g && _arcade != null) _arcade.Leave();
        }

        System.Collections.IEnumerator LeaveAfter(CompetitionRound round, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (_round == round && _arcade != null) _arcade.Leave();
        }

        /// <summary>The API gameType an arcade game saves as.</summary>
        /// <summary>The Puttr Arcade games that play an API game, and the gameType each saves as.</summary>
        static readonly Dictionary<string, string> ArcadeGames = new Dictionary<string, string>
        {
            { "On Fire", "ARCADESTREAK" }, { "Archery", "TARGET" }, { "Cricket", "CRICKET" }, { "Derby", "DERBY" }, { "Major League", "MAJORLEAGUE" }, { "Lanes", "BOWLING" }, { "Balloon Pop", "BALLOONPOP" }, { "Beer Pong", "BEERPONG" }, { "Putt Star", "ROCK" },
            // Brought over from Puttr Arcade (ArcadeExtras, unpublished rows 1611-1618).
            { "Dead Weight", "DEADWEIGHT" }, { "Send It", "SENDIT" }, { "Slingshot", "SLINGSHOT" }, { "Spot Kick", "SPOTKICK" },
            { "Tug of War", "TUGOFWAR" }, { "Turf War", "TURFWAR" }, { "Under the Cup", "UNDERTHECUP" }, { "Windmill", "WINDMILL" },
        };

        static string GameTypeFor(string arcadeName) => ArcadeGames.TryGetValue(arcadeName, out string type) ? type : arcadeName == FreePuttMode.Title ? FreePuttScreen.GameType : null;

        /// <summary>Arcade games for more than one Player: who plays them is picked first (min, max).</summary>
        // Arcade games set up with Players on the game page: { min, max }. Puttr counts as a Player (Beer Pong's house side).
        public static readonly Dictionary<string, int[]> WithPlayers = new Dictionary<string, int[]> { { "Beer Pong", new[] { 2, 2 } } };

        /// <summary>An arcade game for these Players. The first is the signed-in profile; only their putts are saved.</summary>
        public void PlayWith(string arcadeName, List<string> names)
        {
            if (_playing) return;
            Arcade.EmbeddedPlayers = names.Count;
            Arcade.EmbeddedNames = names.ToArray();
            Play(arcadeName, true);
        }

        public void Play(string arcadeName) => Play(arcadeName, false);

        void Play(string arcadeName, bool withPlayers)
        {
            if (_playing) return;
            if (!withPlayers) { Arcade.EmbeddedPlayers = 1; Arcade.EmbeddedNames = null; Arcade.EmbeddedFirst = 0; }
            _playing = true;
            _finishedScore = null;
            _playingName = arcadeName;
            // A round saves itself (CompetitionRound); anything else is saved as the game it is.
            _saver = new RoundSaver(_round != null || _live != null ? null : _rule != null ? _rule.GameType : GameTypeFor(arcadeName));
            // On Fire from a fixed distance saves as the retired streak game for it (Six Pack for 6 ft...).
            if (_round == null && _live == null && arcadeName == "On Fire" && StreakGameByFeet.TryGetValue(PuttrArcade.OnFireMode.FixedFeet, out int streakGame)) _saver.UseGame(streakGame.ToString());
            Perf.Mark("game opened");
            if (arcadeName != FreePuttMode.Title) { _practice = null; _panel = null; }
            if (_round == null && _live == null && _rule == null) { FreePuttMode.Label = null; Arcade.AppAcceptsPutts = null; FreePuttMode.Counts = null; }
            _app.EnterGame(_live != null ? _livePanel.Root : _round != null ? _roundPanel.Root : _rule != null ? _rulePanel.Root : _panel != null ? _panel.Root : null);
            PuttrConsole console = Green.Console; // made here if this is the first thing to need it

            _backdropWanted = null;
            PuttrArcade.Loader.Quiet = false;
            if (_arcadeObject == null) MakeArcade(arcadeName);
            else
            {
                _arcadeObject.SetActive(true);
                if (_arcade.IsReady) _arcade.Launch(_arcade.IndexOf(arcadeName));
                else App.Run(LaunchWhenReady(arcadeName));
            }
        }

        void OnStarted(GameMode game, int players)
        {
            if (_playing) _saver.Begin();
        }

        void OnPutt(GameMode game, PuttResult putt)
        {
            if (!_playing) return;
            if (_live != null) { _live.Mine(putt); return; }
            if (_rule != null)
            {
                if (_rule.Over) return;
                bool bot = _botShot && _rule.P == _rule.Bot;
                _botShot = false;
                int feet = _rule.Feet, tee = _rule.Tee, who = _rule.P;
                AppPutt.Verdict rv = Verdicts.Of(putt, feet, tee);
                // A sensor game pairs the putt with its Vertex stroke first: with none, the putt does not count (SensorStroke.cs).
                SensorGame sensor = _rule as SensorGame;
                if (sensor != null && !sensor.Arm(!Green.RealPutts || (_arcade != null && _arcade.demo))) { _app.Toast(SensorGame.NoStroke); return; }
                // Only the first Player is on this account; a guest's putts are played, not saved.
                if (who == 0 && !bot) _saver.Putt(feet, tee, rv.SavedMade, putt, _arcade != null && _arcade.demo, sensor?.SensorData);
                Shot called = _rule.Putt(rv, putt);
                if (game is PuttrArcade.RuleStageMode) PuttrArcade.RuleStageMode.Pending = RuleViews.Of(_rule, called, rv, putt);
                string sound = _rule.SoundFor(called);
                if (sound != null && _arcade != null) _arcade.Audio.Library(sound);
                if (_rule.Over && sound != "rule_win" && _arcade != null) _arcade.Audio.Library("rule_win", 0.7f);
                DealRule();
                _rulePanel.Show();
                if (bot) _botAnswered = true;
                if (!_rule.Over && _rule.P == _rule.Bot) App.Run(BotTurn(_rule, 3.5f));   // Puttr's turn next: after the scene plays out
                if (_rule.Over)
                {
                    _saver.End(_rule.Saved);
                    App.Run(LeaveRuleAfter(_rule, 3f));
                }
                return;
            }
            if (_round != null)
            {
                RoundSession session = _round.Session;
                if (session.Done) return;
                TournamentMath.Dealt dealt = session.Now;
                session.Add(putt);
                _round.Putt(putt, dealt, Verdicts.Of(putt, dealt.Feet, dealt.Tee).SavedMade);
                Deal();
                _roundPanel.Show();
                if (session.Done)
                {
                    Capture.One.End();
                    CompetitionRound finishing = _round;
                    // Entered, then a moment to see the last read, then the Final screen.
                    App.Run(finishing.Finish(() =>
                    {
                        if (_roundPanel != null) _roundPanel.Show();
                        App.Run(LeaveAfter(finishing, 3f));
                    }));
                }
                return;
            }
            AppPutt.Verdict verdict = Verdicts.Of(putt, game.PuttFeet > 0 ? game.PuttFeet : 6, game.PuttTee > 0 ? game.PuttTee : 2);
            if (game.Putter != 0) return; // a guest's putt: played, not saved
            _saver.Putt(game.PuttFeet, game.PuttTee, verdict.SavedMade, putt, _arcade != null && _arcade.demo);
            if (_practice == null || !(game is FreePuttMode)) return;
            _practice.LastVerdict = verdict;
            bool goal = _practice.Add(PracticePutt.From(verdict, game.PuttFeet, game.PuttTee));
            // Random tees: the session has dealt the next one.
            FreePuttMode.Feet = _practice.Feet;
            FreePuttMode.TeeNow = _practice.Tee;
            _panel.Show(goal);
        }

        void OnEnded(GameMode game, int[] scores)
        {
            if (!_playing || scores.Length == 0) return;
            _finishedScore = scores[0];
            _saver.End(scores[0]);
        }

        void OnExit()
        {
            if (!_playing) return;
            _playing = false;
            _arcadeObject.SetActive(false);
            if (_live != null)
            {
                LiveMatch left = _live;
                _live = null;
                _livePanel = null;
                Arcade.AppAcceptsPutts = null;
                FreePuttMode.Label = null;
                FreePuttMode.Counts = null;
                left.Changed -= DealLive;
                string result = left.Over ? (left.WinnerId == Auth.Uid ? "You won " : "You lost ") + RoundSession.Points(left.Score(left.Me)) + " to " + RoundSession.Points(left.Score(left.Me == 1 ? 2 : 1)) + "." : "You left the game. Open it from Live games to carry on.";
                left.Close();
                _app.LeaveGame();
                _app.ExitToTab();
                _app.Toast(result);
                return;
            }
            if (_rule != null)
            {
                RuleGame left = _rule;
                object row = _ruleGame;
                _rule = null; _ruleGame = null; _rulePanel = null;
                FreePuttMode.Silent = false;
                if (_arcade != null) _arcade.Audio.GameBed(null);
                Arcade.AppAcceptsPutts = null;
                FreePuttMode.Label = null;
                FreePuttMode.Counts = null;
                if (!left.Over) _saver.Abandon();
                _app.LeaveGame();
                if (!left.Over) { _app.ExitToTab(); return; }   // left part way: Exit
                if (left.Over) _app.Push(new RuleFinalScreen(_app, row, left, () => { RuleGame again = RuleCatalog.Make(row); if (again == null) return; again.Names = left.Names; App.Run(Later(() => PlayRule(row, again))); }));
                return;
            }
            if (_round != null)
            {
                CompetitionRound left = _round;
                _round = null;
                _roundPanel = null;
                Arcade.AppAcceptsPutts = null;
                FreePuttMode.Label = null;
                Capture.One.End();
                Metronome.Stop();
                // Left part way: a tournament round stays open to pick up again; anything else is marked abandoned.
                if (!left.Finished && left.UserGameId != null && !(left.How == CompetitionRound.Mode.Entered && left.What == CompetitionRound.Kind.Tournament))
                    App.Run(Api.Post("/game/abandon", new Dictionary<string, object> { { "gameId", left.UserGameId } }, null, null, auth: false));
                _app.LeaveGame();
                Perf.Mark("round closed, " + left.Session.Index + " of " + left.Session.Putts.Count + " putts, " + left.How);
                if (left.Session.Done && left.OnDone != null) { left.OnDone(left); return; }
                if (!left.Session.Done) { _app.ExitToTab(); return; }   // left part way: Exit
                if (left.Session.Done) _app.Push(new FinalScreen(_app, left, left.Session.Detail.Length > 0 && left.What != CompetitionRound.Kind.Coach ? left.Session.Title + "   \u00B7   " + left.Session.Detail : left.Session.Title, Again(left)));
                return;
            }
            if (_practice != null)
            {
                // Free putt has no way to lose: leaving it is finishing it. The app saves a score of 0.
                PracticeSession done = _practice;
                _practice = null;
                _panel = null;
                _saver.End(0);
                _app.LeaveGame();
                _app.ExitToTab();   // Exit: back to the main screen of the tab it came from
                Perf.Mark("free putt ended, " + done.Total + " putts, " + _saver.Sent + " sent, " + _saver.Skipped + " test putts skipped");
                if (done.Total > 0)
                    _app.Toast(done.Total + (done.Total == 1 ? " putt" : " putts") + ", " + done.MakePercent + "% made. " + (_saver.Sent > 0 || _saver.Skipped == 0 ? "Saved to your account." : "Test putts are not saved."));
                return;
            }
            _saver.Abandon(); // does nothing if the round was finished
            _app.LeaveGame();
            int sent = _saver.Sent, skipped = _saver.Skipped;
            Perf.Mark("game closed, " + sent + " putts sent, " + skipped + " test putts skipped");
            // Exit, finished or not: back to the main screen of the tab the game came from (the game over card was the result).
            _app.ExitToTab();
            if (sent > 0) _app.Toast(sent + (sent == 1 ? " putt" : " putts") + " saved to your account.");
            else if (skipped > 0) _app.Toast("Test putts are not saved. Putt on a Puttr to save a round.");
        }
    }
}
