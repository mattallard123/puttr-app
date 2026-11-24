// Plays a Puttr Arcade game from the app and saves it to the Player's account (RoundSaver), as the Puttr app does.
// Under -app2HoldPlay (development) nothing is written (Api.Held), and the messages here say so.
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
        /// <summary>-app2AutoPutt: a keyboard-style test putt into the game being played (never saved).</summary>
        public void TestPutt() { if (_arcade != null && _playing) RoundSaver.AsTest(_arcade.TestPutt); }
        string _playingName;
        /// <summary>The score of the round that just finished, until the Player is back in the app.</summary>
        int? _finishedScore;

        public GameBridge(App app)
        {
            _app = app;
            Arcade.AppJudge = Judge;
            Arcade.AppGameStarted += OnStarted;
            Arcade.AppPutt += OnPutt;
            Arcade.AppGameEnded += OnEnded;
            Arcade.AppExit += OnExit;
            // The games' announcer (On Fire's calls, every game's distance and tee) speaks through the app's commentary,
            // which fetches the Puttr app's recordings and minds the Commentary switch.
            GameAudio.Speaker = GameVoice.Say;
            GameAudio.Commentary = () => GameVoice.On;
            GameAudio.SetCommentary = on => GameVoice.On = on;
            // League Night plays the week's game through these (LeagueRound.Play).
            LeagueRound.PlayRules = PlayRuleRound;
            LeagueRound.PlayList = (round, channelType) => StartRound(round, "League Night", channelType);
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

        /// <summary>A game the lists offer to play: not retired, and one this build can play. Home's "Jump back in"
        /// uses it, so a game last played in the legacy app that has not been brought over gets no tile.</summary>
        public static bool Offered(object game) => !Retired(game) && Playable(game);

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

        // A Versus turn waiting for its game to start (PlayVersus), and the one being played now.
        string _versusNext, _versusNow;
        bool _versusOpening;

        /// <summary>A Versus turn is about to be played: the game page keeps it to one Player.</summary>
        public bool VersusPending => _versusNext != null;

        /// <summary>
        /// A Versus turn: the game opens as it does from the Play tab (its own page, then the game), played alone, and
        /// when its score is saved the turn is sent (POST /versus/:id/complete). A putt-list catalogue game still goes
        /// as a round, which sends its own turn.
        /// </summary>
        public void PlayVersus(object game, string versusId)
        {
            if (RuleFor(game) != null) { _versusNext = null; PlayCatalog(game, versusId); return; }
            _versusNext = versusId;
            _versusOpening = true;
            try { if (!Open(game)) { _versusNext = null; _app.Toast("That game is not in this build yet."); } }
            finally { _versusOpening = false; }
        }

        System.Collections.IEnumerator SendVersus(string versusId, string round)
        {
            if (Api.IsLocal(round)) { _app.Toast(Api.NotSaved + ": your Versus score was not sent to your opponent."); yield break; }
            object answer = null;
            string failed = null;
            yield return Api.Post("/versus/" + versusId + "/complete", new Dictionary<string, object> { { "userGameId", int.TryParse(round, out int id) ? id : 0 } }, d => answer = d, w => failed = w);
            if (answer == null) { Perf.Note("versus: turn not sent (" + failed + ")"); _app.Toast("Your Versus score could not be sent. Check your connection."); yield break; }
            _app.Toast(J.Str(answer, "status") == "COMPLETE" ? "Versus complete. Open Versus to see who won." : "Your Versus score is in. Your opponent is up.");
        }

        /// <summary>Opens whatever plays this API game. False if nothing does yet.</summary>
        public bool Open(object game)
        {
            if (!_versusOpening) _versusNext = null;   // any other way into a game drops a Versus turn that was never started
            Analytics.Selected(game, Analytics.CurrentScreen);
            string arcade = ArcadeNameFor(game);
            // Games with a setup (who plays, options) set up on their own page, with the Start button.
            if (arcade != null && WithPlayers.ContainsKey(arcade)) { _app.Push(new GameScreen(_app, game)); return true; }
            if (arcade != null) { _title = J.Str(game, "title"); Play(arcade); return true; }
            // Practice sets up on the game page like every game (GameScreen), then plays.
            if (J.Str(game, "gameType").ToUpperInvariant() == FreePuttScreen.GameType) { _app.Push(new GameScreen(_app, game)); return true; }
            if (RuleFor(game) != null) { if (!ConnectGate.Clear(_app)) return true; _app.Pop(); PlayCatalog(game, null); return true; }
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

        /// <summary>The coach's first-run read: five putts at 6 ft, straight; done gets the round. Its putts are saved as
        /// the app's calibration saves them (CoachSession.js mode 'calibration'): to user_putts with no game (gameId and
        /// userGameId 0), and no game row is opened or closed.</summary>
        public void PlayCalibration(System.Action<CompetitionRound> done)
        {
            var putts = new List<TournamentMath.Dealt>();
            for (int i = 0; i < 5; i++) putts.Add(new TournamentMath.Dealt { Feet = 6, Tee = 2 });
            var session = new RoundSession { Title = "Five putts, 6 ft, straight", Detail = "Calibration", Rule = RoundRule.Feet, Putts = putts };
            StartRound(new CompetitionRound(CompetitionRound.Kind.Practice, session, null) { OnDone = done, Unlinked = true }, "Calibration", null);
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
            // An Android TV or stick: the light picture (no depth of field, motion blur or grain, fewer pixels).
            if (Tv.On && Application.platform == RuntimePlatform.Android) Arcade.ForceLight = true;
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
            if (_playing || (_round != null && _round.Rules != rules) || _live != null || _rule != null) return;
            if (!ConnectGate.Clear(_app)) return;   // no Puttr, no game (puttr-app BleGate)
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
            rules.Changed = () => { DealRule(); if (_rulePanel != null) _rulePanel.Show(); if (scene != null) PuttrArcade.RuleStageMode.Instance?.Changed(RuleViews.State(rules)); GameVoice.RuleChanged(rules); };
            DealRule();
            // The game's commentary (its own voice and calls, as the app's game screen): the start line, the distance and
            // tee of each putt, the reaction. A League game played as its own game speaks the same (Matt, 2026-11-24:
            // League Night's Center Cut had no calls, as the round was left to call it and never did).
            GameVoice.RuleStart(rules);
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
            if (_round == null && J.Str(game, "id").Length > 0) _saver.UseGame(J.Str(game, "id"));
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

        public void PlayPractice(PracticeSession session, string title = null)
        {
            if (_playing) return;
            if (!ConnectGate.Clear(_app)) return;   // no Puttr, no game (puttr-app BleGate)
            _title = title;
            _practice = session;
            FreePuttMode.Feet = session.Feet;
            FreePuttMode.TeeNow = session.Tee;
            _panel = new PracticePanel(_app, session, () => { if (_arcade != null) _arcade.Leave(); });
            Play(FreePuttMode.Title);
            GameVoice.PracticeStart();   // Open.js startUserGame: the caddie's opener
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
            if (!ConnectGate.Clear(_app)) return;   // no Puttr, no game (puttr-app BleGate)
            _live = LiveMatch.Start(game);
            _livePanel = new LivePanel(_live, () => { if (_arcade != null) _arcade.Leave(); });
            // The game being played, when the room names it; else "Live".
            string liveTitle = J.Str(game, "gameTitle", J.Str(game, "title"));
            FreePuttMode.Label = liveTitle.Length > 0 ? liveTitle : "Live";
            FreePuttMode.Counts = putt => putt.Made;
            Arcade.AppAcceptsPutts = () => _live != null && _live.MyTurn && !TurnsAwayTestPutt();
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
        public void PlayLeague(object season, object week, string team) => LeagueRound.Play(_app, season, week, team);

        /// <summary>A League Night game played as the week's own rule game (LeagueRound.Play): opened, filmed and entered as a
        /// round (CompetitionRound), dealt and scored by the game (round.Rules) in its own scene.</summary>
        public void PlayRuleRound(CompetitionRound round, object game)
        {
            if (_playing || _round != null || _live != null || _rule != null || round.Rules == null) return;
            if (!ConnectGate.Clear(_app)) return;   // before the round is opened on the server or the camera asked for
            string channelType = round.Rules.GameType;
            bool film = Capture.Supported;
            if (film)
            {
                Capture.CameraAccess access = Capture.Has;
                if (access == Capture.CameraAccess.Refused) { CameraNeeded("League games", "game", false); return; }
                if (access == Capture.CameraAccess.None)
                {
                    if (Green.RealPutts) { CameraNeeded("League games", "game", true); return; }
                    film = false;
                }
            }
            _round = round;
            App.Run(round.Open(problem =>
            {
                if (problem != null) { _round = null; _app.Toast(problem); return; }
                if (film)
                {
                    if (round.How == CompetitionRound.Mode.Local) Capture.One.BeginPreview(channelType);
                    else Capture.One.Begin(channelType, round.UserGameId, null, round.Live);
                }
                PlayRule(game, round.Rules);
                if (_rule != round.Rules) { _round = null; Capture.One.End(); round.Abandon(); return; }
                // As any League game: no putt counts until the recording runs. The rule panel knows nothing of the camera.
                // And a game that is being saved or entered takes real putts only: no keyboard or on-screen test putt.
                Arcade.AppAcceptsPutts = () => _rule != null && !_rule.Over && (Capture.One.Now == Capture.State.Off || Capture.One.Ready)
                    && !(round.How != CompetitionRound.Mode.Local && TurnsAwayTestPutt());
                _rulePanel.TakesTestPutts = () => round.How == CompetitionRound.Mode.Local;
                if (film) _rulePanel.Root.Add(LeagueRound.Overlay(_app));
            }));
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
            _versusNext = null;   // a round sends its own Versus turn (CompetitionRound.VersusId)
            if (!ConnectGate.Clear(_app)) return;   // no Puttr, no game (puttr-app BleGate)
            if (round.Session.Putts.Count == 0) { _app.Toast("This round has no putts set yet."); return; }
            string kicker = round.Session.Detail.Length > 0 ? round.Session.Detail : round.Session.Title;
            _app.Push(new MatchupScreen(_app, round.Session, kicker, label == "Session" ? round.Session.Title : label, () => Begin(round, label, channelType)));
        }

        /// <summary>The name a round goes by in the game (the top bar, the pause card): the tournament's own name, "Today's
        /// session" for the coach's, else its label ("League Night", "Level 3", "Drill", a catalogue game's title).</summary>
        static string RoundName(CompetitionRound round, string label)
        {
            if (round.What == CompetitionRound.Kind.Tournament && round.Session.Detail.Length > 0) return round.Session.Detail;
            if (round.What == CompetitionRound.Kind.Coach) return round.Session.Title;
            return label;
        }

        void Begin(CompetitionRound round, string label, string channelType)
        {
            if (_playing || _round != null) return;
            // A tournament round or a league game is played on camera (TournamentGame.js / LeagueGame.js): camera
            // access is settled before the round is even opened. Where Puttr cannot film at all (the Editor, a TV)
            // the round plays without it, as before.
            bool film = channelType != null && Capture.Supported;
            if (film)
            {
                Capture.CameraAccess access = Capture.Has;
                if (access == Capture.CameraAccess.Refused) { CameraNeeded(label == "League Night" ? "League games" : "Tournament rounds", label == "League Night" ? "game" : "round", false); return; }
                if (access == Capture.CameraAccess.None)
                {
                    // No camera on this device (the Simulator): real putts cannot be entered unrecorded; test putts play on.
                    if (Green.RealPutts) { CameraNeeded(label == "League Night" ? "League games" : "Tournament rounds", label == "League Night" ? "game" : "round", true); return; }
                    film = false;
                }
            }
            _round = round;
            PuttrConsole console = Green.Console;
            App.Run(round.Open(problem =>
            {
                if (problem != null) { _round = null; _app.Toast(problem); return; }
                if (round.Session.Done) { _round = null; _app.Toast("You have already played this round."); return; }
                FreePuttMode.Label = RoundName(round, label);
                _calledFirst = false;
                Deal();
                _roundPanel = new RoundPanel(_app, round, () => { if (_arcade != null) _arcade.Leave(); });
                // On camera: a tournament records in parts (a part starts at the putt the round was opened or
                // picked up at), the league in one. With test putts nothing is saved, so the camera runs unrecorded.
                // (Tournament and League Night rounds are live: their games and recordings go to production even under -app2HoldPlay.)
                if (film)
                {
                    if (round.How == CompetitionRound.Mode.Local) Capture.One.BeginPreview(channelType);
                    else Capture.One.Begin(channelType, round.UserGameId, round.What == CompetitionRound.Kind.Tournament ? round.Session.Index : (int?)null, round.Live);
                }
                // A holed putt counts only if it came in from the front; the call says so.
                FreePuttMode.Counts = putt =>
                {
                    if (_round == null || _round.Session.Done) return putt.Made;
                    TournamentMath.Dealt now = _round.Session.Now;
                    AppPutt.Verdict v = Verdicts.Of(putt, now.Feet, now.Tee);
                    return RoundSession.Score(_round.Session.Rule, now.Feet, now.Tee, v.Count, v.Entry, now.Angle) > 0;
                };
                // A round that is being saved or entered takes real putts only (TurnsAwayTestPutt); a lesson drill saves
                // nothing. A tester with the test putt buttons on may play one in their own practice (the coach session,
                // a drill, a catalog game, a quest level): OnPutt then makes it a test round. Never in a tournament
                // round or a League game.
                Arcade.AppAcceptsPutts = () => _round != null && !_round.Session.Done && (Capture.One.Now == Capture.State.Off || Capture.One.Ready)
                    && (TestPutts.RoundTakes(TestPutts.Available(_app.Profile), _round.What, _round.How) || !TurnsAwayTestPutt());
                // Each putt's distance and tee is called, in a tournament round as in a League game (Matt, 2026-11-24).
                if (round.What == CompetitionRound.Kind.League || round.What == CompetitionRound.Kind.Tournament) App.Run(CallFirst(round));
                Play(FreePuttMode.Title);
                // A catalogue game played as a round speaks as the app's game screen did (Standard.js, Streak.js, CenterCut.js).
                string voiced = VoicedType(round);
                if (voiced != null) GameVoice.RoundStart(voiced, round.Session.Now.Feet, round.Session.Now.Tee);
                if (Fixtures.On) App.Run(DemoPutts());
            }));
        }

        /// <summary>The gameType a round speaks as: a catalogue game's (a Versus turn or a ghost game too). Tournament rounds,
        /// quests, drills and the coach's sessions had no commentary in the app (League Night calls its own putts).</summary>
        static string VoicedType(CompetitionRound round) => round != null && round.What == CompetitionRound.Kind.Game ? J.Str(round.Round, "gameType") : null;

        float _testPuttToldAt = -10f;

        /// <summary>
        /// A test putt (the keyboard's, or an on-screen test button's) played while a real Puttr is connected. A round
        /// or a live game saves each putt as it is scored, so there the putt is not played at all (it would go in as a
        /// real one, into a tournament even); -app2SaveTestPutts lets it through. Games saved by RoundSaver play it
        /// and leave it out of the save instead.
        /// </summary>
        bool TurnsAwayTestPutt()
        {
            if (!Green.RealPutts || Green.SaveTestPutts || !RoundSaver.TestPuttNow) return false;
            _answered = true;   // the reason is said here
            if (Time.realtimeSinceStartup - _testPuttToldAt > 3f)
            {
                _testPuttToldAt = Time.realtimeSinceStartup;
                PuttStrip.Say(_live != null ? "Test putts are off in a live match." : _round != null && TestPutts.Available(_app.Profile) ? "Test putts are off in a tournament round or a League game." : "Test putts are off while a Puttr is connected.");
            }
            return true;
        }

        // ── A putt that did not count: say why ───────────────────────────────
        // A game panel hides the app's toast, and the arcade drops a putt it will not take without a word. The
        // console's Putt event, the arcade's checks and OnPutt all run inside one call, so a putt the arcade judged
        // that reached neither OnPutt nor a refusal with its own message by the next frame was dropped: the panel's
        // strip says why (PuttStrip.Say). A path the console itself turns away as not a ball (a hand, a club) is
        // left unsaid, as in the app: it happens every time a ball is picked up.

        int _judged;
        bool _answered, _hearsRejects;

        bool Judge(PuttResult putt, int feet, int tee)
        {
            bool ball = Verdicts.Judge(putt, feet, tee);
            // Only a game with one of the app's panels: an arcade game takes a putt as a button on its own screens too.
            if (!ball || !_playing || (_rule == null && _round == null && _live == null && _practice == null) || (_arcade != null && _arcade.demo)) return ball;
            _answered = false;
            int mine = ++_judged;
            App.Run(Unanswered(mine));
            return true;
        }

        System.Collections.IEnumerator Unanswered(int judged)
        {
            yield return null;
            if (judged != _judged || _answered || !_playing) yield break;
            PuttStrip.Say(WhyNot());
        }

        // Why the game would not take a putt just now; null when there is nothing worth saying (the game is over).
        string WhyNot()
        {
            if (_live != null) return _live.Over ? null : !_live.MyTurn ? "Not your turn: that putt did not count." : TooSoon;
            if (_rule != null)
            {
                if (_rule.Over) return null;
                if (_rule.P == _rule.Bot) return "Puttr's turn: that putt did not count.";
            }
            if (_round != null && _round.Session.Done) return null;
            if ((_round != null || _rule != null) && Capture.One.Now != Capture.State.Off && !Capture.One.Ready) return "The camera is not recording yet: that putt did not count.";
            return TooSoon;
        }

        const string TooSoon = "That putt did not count: the last one was still playing. Wait for the replay, then putt.";

        // The console's own refusals. "repeat" is the same ball seen again as it leaves the ramp, and "not-a-ball" a
        // hand or a club: neither is news. No hole is: nothing can count until the Puttr has found it.
        void OnRejected(IReadOnlyList<BallPoint> path, string reason)
        {
            if (_playing && reason == "no-hole") PuttStrip.Say("Your Puttr has not found the hole yet. Clear the ramp and restart it.");
        }

        /// <summary>CaptureGuard.js's CapturePermissionModal: a round cannot start without the camera.</summary>
        void CameraNeeded(string subject, string unit, bool noCamera)
        {
            Perf.Note("round: not started, camera " + (noCamera ? "unavailable" : "refused"));
            UnityEngine.UIElements.VisualElement dim = null;
            UnityEngine.UIElements.VisualElement card = Ui.Box("modal",
                Ui.Icon("VideoCamera", 32, Ui.Red),
                Ui.Text("Camera access required", "h2", Ui.Bold).Margin(10),
                Ui.Text(noCamera
                    ? "No camera is available on this device, so a " + unit + " cannot be recorded here."
                    : subject + " are recorded for replay and verification. Allow camera and microphone access in Settings, then start your " + unit + ".", "sub", Ui.Regular).Margin(6));
            if (!noCamera && Application.platform == RuntimePlatform.IPhonePlayer)
                card.Add(Ui.Button("Open Settings", () => { dim.RemoveFromHierarchy(); Application.OpenURL("app-settings:"); }));
            card.Add(Ui.Button(noCamera ? "OK" : "Not now", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<UnityEngine.UIElements.ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            _app.Toasts.Add(dim);
        }

        // League Night calls each putt aloud, "6 footer, Center tee" (LeagueGame.js speakText). The first call waits
        // for the camera to join its channel and 0.3 s more, so the audio session has settled and the line is not
        // clipped; if the camera never says so, it goes after 4 s anyway.
        bool _calledFirst;
        static readonly string[] TeeCall = { "Left", "Center", "Right" };
        static string Call(TournamentMath.Dealt d) => d.Feet + " footer, " + TeeCall[Mathf.Clamp(d.Tee, 1, 3) - 1] + " tee";

        System.Collections.IEnumerator CallFirst(CompetitionRound round)
        {
            var lines = new List<string>();
            foreach (TournamentMath.Dealt d in round.Session.Putts) lines.Add(Call(d));
            CoachVoice.Warm(lines);
            float until = Time.realtimeSinceStartup + 4f;
            bool filming = Capture.One.Now != Capture.State.Off;
            while (filming && !Capture.One.Joined && Time.realtimeSinceStartup < until && _round == round) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            if (_round != round || _calledFirst || round.Session.Done) yield break;
            _calledFirst = true;
            CoachVoice.Say(Call(round.Session.Now));
        }

        void Deal()
        {
            if (_round == null || _round.Session.Done) return;
            TournamentMath.Dealt next = _round.Session.Now;
            if ((_round.What == CompetitionRound.Kind.League || _round.What == CompetitionRound.Kind.Tournament) && _calledFirst) CoachVoice.Say(Call(next));
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
            // The last putt's call plays out before the results (Matt, 2026-11-21: the Lab's was cut off), up to 12 s more.
            float waited = 0f;
            while (GameVoice.Speaking && waited < 12f) { waited += Time.unscaledDeltaTime; yield return null; }
            if (waited > 0f) yield return new WaitForSecondsRealtime(0.4f);
            if (_rule == g && _arcade != null) _arcade.Leave();
        }

        System.Collections.IEnumerator LeaveAfter(CompetitionRound round, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (_round == round && _arcade != null) _arcade.Leave();
        }

        /// <summary>The API gameType an arcade game saves as.</summary>
        /// <summary>The Puttr Arcade games that play an API game, and the gameType each saves as.</summary>
        internal static readonly Dictionary<string, string> ArcadeGames = new Dictionary<string, string>
        {
            { "On Fire", "ARCADESTREAK" }, { "Archery", "TARGET" }, { "Cricket", "CRICKET" }, { "Derby", "DERBY" }, { "Major League", "MAJORLEAGUE" }, { "Lanes", "BOWLING" }, { "Balloon Pop", "BALLOONPOP" }, { "Beer Pong", "BEERPONG" }, { "Putt Star", "ROCK" },
            // Brought over from Puttr Arcade (ArcadeExtras, unpublished rows 1611-1618).
            { "Dead Weight", "DEADWEIGHT" }, { "Send It", "SENDIT" }, { "Slingshot", "SLINGSHOT" }, { "Spot Kick", "SPOTKICK" },
            { "Tug of War", "TUGOFWAR" }, { "Turf War", "TURFWAR" }, { "Under the Cup", "UNDERTHECUP" }, { "Windmill", "WINDMILL" }, { "Putt-ball", "PUTTBALL" },
        };

        static string GameTypeFor(string arcadeName) => ArcadeGames.TryGetValue(arcadeName, out string type) ? type : arcadeName == FreePuttMode.Title ? FreePuttScreen.GameType : null;

        /// <summary>
        /// Arcade games set up with Players on the game page (PlayerPicker): { min, max, puttr }. puttr 1: the game's
        /// SoloOpponent is Puttr, so Puttr can be added as the other side (and counts toward min, as in Beer Pong);
        /// alone the Player plays Puttr's house side. Every arcade game with MaxPlayers above 1 is here.
        /// </summary>
        public static readonly Dictionary<string, int[]> WithPlayers = new Dictionary<string, int[]>
        {
            { "Beer Pong", new[] { 2, 2, 1 } },
            { "Cricket", new[] { 2, 4, 1 } }, { "Major League", new[] { 2, 4, 1 } },
            { "Tug of War", new[] { 2, 4, 1 } }, { "Turf War", new[] { 2, 4, 1 } },
            { "Archery", new[] { 1, 4, 0 } }, { "Derby", new[] { 1, 4, 0 } }, { "Lanes", new[] { 1, 4, 0 } }, { "Balloon Pop", new[] { 1, 4, 0 } },
            { "Dead Weight", new[] { 1, 4, 1 } }, { "Send It", new[] { 1, 4, 0 } }, { "Slingshot", new[] { 1, 4, 0 } }, { "Spot Kick", new[] { 1, 4, 0 } },
            { "Under the Cup", new[] { 1, 4, 0 } }, { "Windmill", new[] { 1, 4, 0 } }, { "Putt-ball", new[] { 1, 4, 0 } },
        };

        /// <summary>Played in two sides: an even number of Players (2 or 4), or one against Puttr.</summary>
        public static readonly HashSet<string> TeamGames = new HashSet<string> { "Tug of War", "Turf War" };

        /// <summary>The seat the signed-in Player has in the arcade game being played.</summary>
        int _me;
        /// <summary>Who each seat of the arcade game saves as (PlayerPicker.UserIds, in seat order), or null when one Player plays.</summary>
        List<string> _userIds;

        /// <summary>An arcade game for these Players, in seat order (the first seat putts first). me is the signed-in
        /// Player's seat. Saved as the app saves a pass-and-play game: every putt under the Player who hit it (userIds:
        /// a profile's own user id, RoundSaver.Guest for a guest), and every Player's score when it ends.</summary>
        /// <param name="levels">Easy, Medium or Hard for each seat (the game page's Difficulty); null plays the level remembered for the game.</param>
        /// <param name="title">The catalogue row's title: the name the game shows while it is played.</param>
        public void PlayWith(string arcadeName, List<string> names, int me = 0, List<string> userIds = null, int[] levels = null, string title = null)
        {
            if (_playing) return;
            if (!ConnectGate.Clear(_app)) return;   // before the seats are set for a game that will not start
            Arcade.EmbeddedPlayers = names.Count;
            Arcade.EmbeddedNames = names.ToArray();
            _me = Mathf.Clamp(me, 0, Mathf.Max(0, names.Count - 1));
            _userIds = userIds;
            _levels = levels;
            _title = title;
            Play(arcadeName, true);
        }

        /// <summary>For the game about to start, then cleared: the catalogue title it was opened by, and each seat's level.</summary>
        string _title;
        int[] _levels;

        public void Play(string arcadeName) => Play(arcadeName, false);

        void Play(string arcadeName, bool withPlayers)
        {
            if (_playing) return;
            if (!ConnectGate.Clear(_app)) return;   // no Puttr, no game (puttr-app BleGate)
            if (!withPlayers) { Arcade.EmbeddedPuttr = false; Arcade.EmbeddedPlayers = 1; Arcade.EmbeddedNames = null; Arcade.EmbeddedFirst = 0; _me = 0; _userIds = null; }
            _playing = true;
            _finishedScore = null;
            _playingName = arcadeName;
            // A round saves itself (CompetitionRound); anything else is saved as the game it is.
            _saver = new RoundSaver(_round != null || _live != null ? null : _rule != null ? _rule.GameType : GameTypeFor(arcadeName));
            // Analytics knows the game by name as well as by type (a round names itself: CompetitionRound.Open).
            if (_round == null && _live == null) Analytics.Playing(_rule != null ? _rule.GameType : GameTypeFor(arcadeName), _title ?? arcadeName, "game");
            // The name shown in the game: the catalogue row's for a game opened from one; a rule game or a round names
            // itself through its mode's label. Then the difficulty (GameLevels): the arcade's level for each seat and
            // the pause card's line. A level whose scores are not comparable with the board's is saved, not posted.
            Arcade.EmbeddedTitle = _rule == null && _round == null && _live == null ? _title : null;
            bool posts = GameLevels.Launch(arcadeName, GameTypeFor(arcadeName), withPlayers ? _levels : null, Arcade.EmbeddedNames, _me);
            if (_rule != null) { posts = _rule.PostsBoard; Arcade.EmbeddedPauseNote = GameLevels.PauseNote(_rule); }
            _saver.PostsBoard = posts;
            // The distance the app's own screen posted the score with, so the app's board for it shows this round too.
            if (_rule != null) _saver.BoardDistance = RoundSaver.BoardDistanceFor(_rule.GameType, _rule.Feet, _rule.DifficultyNotes == null, GameLevels.Innings);
            else if (arcadeName == "Major League") _saver.BoardDistance = RoundSaver.BoardDistanceFor("MAJORLEAGUE", 0, true, GameLevels.Innings);
            _title = null; _levels = null;
            // On Fire from a fixed distance saves as the retired streak game for it (Six Pack for 6 ft...).
            if (_round == null && _live == null && arcadeName == "On Fire" && StreakGameByFeet.TryGetValue(PuttrArcade.OnFireMode.FixedFeet, out int streakGame)) _saver.UseGame(streakGame.ToString());
            Perf.Mark("game opened");
            if (arcadeName != FreePuttMode.Title) { _practice = null; _panel = null; }
            if (_round == null && _live == null && _rule == null) { FreePuttMode.Label = null; Arcade.AppAcceptsPutts = null; FreePuttMode.Counts = null; }
            UnityEngine.UIElements.VisualElement panel = _live != null ? _livePanel.Root : _rule != null ? _rulePanel.Root : _round != null ? _roundPanel.Root : _panel != null ? _panel.Root : null;
            // A live match's panel (Live.cs) has no strip of its own: one over it, for "not your turn" and the like.
            if (_live != null) panel.Add(new PuttStrip(_app, () => false, true) { BesideSheet = true }.Root);
            _app.EnterGame(panel);
            PuttrConsole console = Green.Console; // made here if this is the first thing to need it
            if (!_hearsRejects) { _hearsRejects = true; console.Rejected += OnRejected; }

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
            // A rule game plays on a one-Player arcade mode: its own count of Players is the round's.
            if (_playing) _saver.Begin(_rule != null ? _rule.Players : players);
            // A Versus turn rides on a game played alone: its score goes to the opponent once the round is saved.
            _versusNow = null;
            _saver.Over = null;
            if (_playing && _versusNext != null)
            {
                string versus = _versusNext;
                _versusNext = null;
                if ((_rule != null ? _rule.Players : players) == 1) { _versusNow = versus; _saver.Over = round => App.Run(SendVersus(versus, round)); }
                else _app.Toast("A Versus turn is played on your own: this game will not be sent.");
            }
            // The announcer's recordings, ahead of the first putt: On Fire calls through GameAudio.Say, Beer Pong opens here.
            if (_playing && (_playingName == "On Fire" || _playingName == "Beer Pong")) GameVoice.Warm(GameVoice.English);
            if (_playing && _playingName == "Beer Pong") GameVoice.Opener(GameVoice.English);
        }

        void OnPutt(GameMode game, PuttResult putt)
        {
            Perf.Wake(8f);   // the putt plays out (replay, scene, read) at full rate
            if (HostMode.On) { if (_playing) HostMode.Touch(); else HostMode.Putted(); }   // someone is there; with no game on, the attract screen steps aside
            if (!_playing) return;
            _answered = true;   // the game took it (Unanswered)
            GameVoice.ShotPlayed();   // a call still waiting is about the putt just played
            if (_live != null)
            {
                // A live match writes every putt it scores (/multiplayer/savePutt, the game state): a test putt (no Puttr,
                // the simulator, the demo, the keyboard or a test button) is not played into it at all. Made-up data
                // (Fixtures) sends nothing, so its sample match plays on.
                if (!Fixtures.On && RoundSaver.TestPutt(_arcade != null && _arcade.demo)) { PuttStrip.Say("Test putts are not played in a live match."); return; }
                _live.Mine(putt);
                return;
            }
            if (_rule != null)
            {
                if (_rule.Over) return;
                bool bot = _botShot && _rule.P == _rule.Bot;
                _botShot = false;
                int feet = _rule.Feet, tee = _rule.Tee, who = _rule.P;
                AppPutt.Verdict rv = Verdicts.Of(putt, feet, tee);
                // A test putt (the demo, the simulator, the keyboard even with a Puttr connected) is played, never saved.
                bool test = RoundSaver.TestPutt(_arcade != null && _arcade.demo);
                // A sensor game pairs the putt with its Vertex stroke first: with none, the putt does not count (SensorStroke.cs).
                SensorGame sensor = _rule as SensorGame;
                if (sensor != null && !sensor.Arm(test)) { PuttStrip.Say(SensorGame.NoStroke); return; }   // in the panel: the app's toast is hidden in a game
                // Saved under whoever hit it, as the app does (savePutt with playerNames[i].userId): the signed-in
                // Player, another profile on this account, or no one for a guest. Puttr's putts are never saved.
                // Compare's seats are putters, not people: the app saves those putts under no one (Fitting.js), so a
                // putter trial stays out of the Player's stats.
                bool puttr = bot || who == _rule.Bot;
                string saveAs = _rule is CompareGame ? RoundSaver.Guest : who == 0 ? null : RoundSaver.SeatUser(_rule.UserIds, who, 0, Auth.Uid);
                // Host mode: nobody at the Puttr is the host. A member's putt is theirs; anyone else's is no one's.
                bool hosted = HostMode.On && !(_rule is CompareGame);
                if (hosted) saveAs = HostMode.SaveAs(_rule.UserIds, who);
                if (_round == null && !puttr && (hosted ? saveAs != null : who == 0 || saveAs != null)) _saver.Putt(feet, tee, rv.SavedMade, putt, test, sensor?.SensorData, saveAs);
                Shot called = _rule.Putt(rv, putt);
                if (_round != null) _round.RulePutt(called, putt);   // a League game: the round saves the putt and keeps the tally
                // The commentary: the reaction, whose turn, the next putt (the buzzer for a back-door make is the arcade's).
                if (GameVoice.RuleShot(_rule, called, who) && _arcade != null) _arcade.Audio.Buzzer();
                if (game is PuttrArcade.RuleStageMode) PuttrArcade.RuleStageMode.Pending = RuleViews.Of(_rule, called, rv, putt);
                string sound = _rule.SoundFor(called);
                if (sound != null && _arcade != null) _arcade.Audio.Library(sound);
                // Each game ends on its own win sound now (SoundFor returns it when Over), so no shared rule_win on top.
                DealRule();
                _rulePanel.Show();
                if (bot) _botAnswered = true;
                if (!_rule.Over && _rule.P == _rule.Bot) App.Run(BotTurn(_rule, 3.5f));   // Puttr's turn next: after the scene plays out
                if (_rule.Over)
                {
                    if (_round != null)
                    {
                        // A League game: the recording closes and the score is entered, then a moment to see the last read.
                        Capture.One.End();
                        CompetitionRound finishing = _round;
                        RuleGame played = _rule;
                        App.Run(finishing.Finish(() => { if (_rulePanel != null) _rulePanel.Show(); App.Run(LeaveRuleAfter(played, 3f)); }));
                    }
                    else
                    {
                        EndRule(_rule);
                        App.Run(LeaveRuleAfter(_rule, 3f));
                    }
                }
                return;
            }
            if (_round != null)
            {
                RoundSession session = _round.Session;
                if (session.Done) return;
                TournamentMath.Dealt dealt = session.Now;
                // A tester's test putt in a round opened to be saved (AppAcceptsPutts let it in): the round is a test
                // round from here, so neither this putt nor the score is sent, and the panel and result card say so.
                if (RoundSaver.TestPuttNow && Green.RealPutts && !Green.SaveTestPutts) _round.MarkTest();
                session.Add(putt);
                // user_putts.made as the app's screen for this kind of round writes it (CompetitionRound.MadeFlag).
                _round.Putt(putt, dealt, CompetitionRound.MadeFlag(_round.What, _round.Unlinked, Verdicts.Of(putt, dealt.Feet, dealt.Tee), session.LastScore));
                Deal();
                string voiced = VoicedType(_round);
                if (voiced != null && GameVoice.RoundPutt(voiced, Verdicts.Of(putt, dealt.Feet, dealt.Tee), dealt.Feet, dealt.Tee, session.Done ? (TournamentMath.Dealt?)null : session.Now) && _arcade != null) _arcade.Audio.Buzzer();
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
            // Saved under whoever hit it (the app's savePutt with the putting Player's userId): the signed-in Player,
            // another profile on this account, or no one for a guest.
            bool own = game.Putter == _me && !HostMode.On;
            string hitBy = HostMode.On ? HostMode.SaveAs(_userIds, game.Putter) : own ? null : RoundSaver.SeatUser(_userIds, game.Putter, _me, Auth.Uid);
            if (own || hitBy != null) _saver.Putt(game.PuttFeet, game.PuttTee, verdict.SavedMade, putt, _arcade != null && _arcade.demo, null, hitBy);
            // Beer Pong's announcer (BeerPong.js): a make or a miss, for whoever putted.
            if (_playingName == "Beer Pong" && _practice == null) { bool cup = game is PuttrArcade.BeerPongMode bp ? bp.Cups(putt) : putt.Made; if (cup) GameVoice.Made(GameVoice.English); else GameVoice.Missed(GameVoice.English); }
            if (game.Putter != _me) return;
            if (_practice == null || !(game is FreePuttMode)) return;
            _practice.LastVerdict = verdict;
            int puttFeet = game.PuttFeet > 0 ? game.PuttFeet : _practice.Feet, puttTee = game.PuttTee > 0 ? game.PuttTee : _practice.Tee;
            bool goal = _practice.Add(PracticePutt.From(verdict, puttFeet, puttTee, putt));
            if (goal && _arcade != null) _arcade.Audio.Library("freeputt_goal", 0.8f);   // silent until recorded
            // Random tees: the session has dealt the next one.
            FreePuttMode.Feet = _practice.Feet;
            FreePuttMode.TeeNow = _practice.Tee;
            if (GameVoice.PracticePutt(verdict, puttFeet, puttTee) && _arcade != null) _arcade.Audio.Buzzer();
            _panel.Putted(goal);
        }

        void OnEnded(GameMode game, int[] scores)
        {
            if (!_playing || scores.Length == 0) return;
            int mine = scores[Mathf.Clamp(_me, 0, scores.Length - 1)];
            _finishedScore = mine;
            if (HostMode.On)
            {
                // Host mode: each seat saves as whoever sat in it (a member, or no one), and the board keeps one row
                // a game, so it carries the game's best score under that Player's name.
                var seats = new List<double>();
                var who = new List<string>();
                for (int i = 0; i < scores.Length; i++) { seats.Add(scores[i]); who.Add(HostMode.SaveAs(_userIds, i)); }
                int top = HostMode.Winner(seats, HostMode.LowWins);
                HostMode.Scored(Arcade.EmbeddedNames, top, who);
                _finishedScore = scores[top];
                _saver.EndHosted(seats, who, top);
                return;
            }
            if (scores.Length == 1) { _saver.End(mine); return; }
            // Pass and play: every Player's score, so each profile that putted gets its own round (the app's multiplayerGameOver).
            var all = new List<double>();
            foreach (int score in scores) all.Add(score);
            _saver.End(all, _userIds, _me);
        }

        // A rule game is over: one Player closes the round with their score; more close it with everyone's. Compare's
        // seats are putters, all putted by the one Player, so it closes as a game played alone.
        void EndRule(RuleGame g)
        {
            if (HostMode.On && !(g is CompareGame))
            {
                // Host mode: as OnEnded. The best score a person made (never Puttr's) goes on the host's board.
                var seats = new List<double>();
                var who = new List<string>();
                var people = new List<double>();
                for (int i = 0; i < g.Players; i++)
                {
                    double score = g.Players < 2 ? g.Saved : g.SavedFor(i);
                    seats.Add(score);
                    who.Add(i == g.Bot ? null : HostMode.SaveAs(g.UserIds, i));
                    people.Add(i == g.Bot ? (HostMode.LowWins ? double.MaxValue : double.MinValue) : score);
                }
                int top = HostMode.Winner(people, HostMode.LowWins);
                HostMode.Scored(g.Names, top, who);
                _saver.EndHosted(seats, who, top);
                return;
            }
            if (g.Players < 2 || g is CompareGame) { _saver.End(g.Saved); return; }
            var scores = new List<double>();
            var ids = new List<string>();
            for (int i = 0; i < g.Players; i++)
            {
                scores.Add(g.SavedFor(i));
                ids.Add(i == g.Bot ? null : RoundSaver.SeatUser(g.UserIds, i, 0, Auth.Uid));
            }
            _saver.End(scores, ids, 0);
        }

        /// <summary>What to tell the Player when a finished round's writes were held (-app2HoldPlay only): null in normal
        /// use, when it was test putts (the result card says so), or when it saves nothing anyway (a lesson drill).</summary>
        public static string HeldNote(CompetitionRound round) => HeldNote(round, Api.HoldsPlay);

        /// <summary>The note apart from the launch flag (for the tests).</summary>
        public static string HeldNote(CompetitionRound round, bool holdsPlay)
        {
            if (round == null || !holdsPlay || round.Live || round.How == CompetitionRound.Mode.Local || round.What == CompetitionRound.Kind.Drill) return null;
            if (!string.IsNullOrEmpty(round.VersusId)) return Api.NotSaved + ": your Versus score was not sent to your opponent.";
            if (round.What == CompetitionRound.Kind.Quest) return Api.NotSaved + ": this level's putts and stars.";
            return Api.NotSaved + ": this round.";
        }

        // After a game saved by RoundSaver. The Puttr app says nothing about saving when a game ends, and neither does
        // this; only putts held under -app2HoldPlay, and a round of test putts (which nothing saves), are said.
        static string SavedNote(RoundSaver saver)
        {
            int held = Api.HoldsPlay ? saver.Real : saver.Held;
            if (held > 0) return Api.PuttsNotSaved(held);
            if (saver.Skipped > 0 && saver.Real == 0) return "Test putts are not saved. Putt on a Puttr to save a round.";
            return null;
        }

        float _tvBackAt = -10f;

        /// <summary>A game is up.</summary>
        public bool Busy => _playing;

        /// <summary>Back on a TV remote during a game: the first press warns, a second within 3 seconds leaves it.</summary>
        public bool TvBack()
        {
            if (!_playing || _arcade == null) return false;
            if (Time.realtimeSinceStartup - _tvBackAt > 3f)
            {
                _tvBackAt = Time.realtimeSinceStartup;
                _app.Toast("Press Back again to leave the game.");
                return true;
            }
            _tvBackAt = -10f;
            _arcade.Leave();
            return true;
        }

        void OnExit()
        {
            if (!_playing) return;
            _playing = false;
            // A finished game's last word (who won) is let finish; a game left part way goes quiet at once.
            if ((_rule != null && _rule.Over) || (_round != null && _round.Session.Done)) GameVoice.Leave(); else GameVoice.Hush();
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
                if (!left.Over) _app.Toast(result);   // a finished match already showed its result screen
                return;
            }
            if (_rule != null && _round != null)
            {
                // A League game played as its rule game: closed as a round, shown as the game.
                CompetitionRound left = _round;
                _round = null; _rule = null; _ruleGame = null; _rulePanel = null;
                FreePuttMode.Silent = false;
                if (_arcade != null) _arcade.Audio.GameBed(null);
                Arcade.AppAcceptsPutts = null;
                FreePuttMode.Label = null;
                FreePuttMode.Counts = null;
                Capture.One.End();
                left.Abandon();   // does nothing once the round has finished
                _app.LeaveGame();
                Perf.Mark("league game closed, " + left.Session.Index + " putts, " + left.How);
                if (!left.Rules.Over) { _app.ExitToTab(); return; }
                _app.Push(new LeagueFinalScreen(_app, left));
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
                // A Greenlands stage has its own card: stars and what opened, or out of balls (Greenlands.cs).
                if (left is StageGame stage) { Greenlands.Finished(_app, stage, _saver.Real > 0); return; }
                // Play again: the same Players in the same seats, saved as the same people (and Puttr still Puttr).
                if (left.Over) _app.Push(new RuleFinalScreen(_app, row, left, () => { RuleGame again = RuleCatalog.Make(row); if (again == null) return; again.Names = left.Names; again.UserIds = left.UserIds; again.Bot = left.Bot; again.First = left.First; again.Difficulty = left.Difficulty; again.Levels = left.Levels; App.Run(Later(() => PlayRule(row, again))); }));
                if (left.Over) { string note = SavedNote(_saver); if (note != null) _app.Toast(note); }
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
                // Left part way: a tournament round stays open to pick up again, the coach's session row is left as the
                // app leaves it, and anything else is marked abandoned (CompetitionRound.Abandon knows which).
                left.Abandon();
                _app.LeaveGame();
                Perf.Mark("round closed, " + left.Session.Index + " of " + left.Session.Putts.Count + " putts, " + left.How);
                if (left.Session.Done && left.OnDone != null) { left.OnDone(left); return; }
                if (!left.Session.Done) { _app.ExitToTab(); return; }   // left part way: Exit
                if (left.Session.Done) _app.Push(new FinalScreen(_app, left, left.Session.Detail.Length > 0 && left.What != CompetitionRound.Kind.Coach ? left.Session.Title + "   \u00B7   " + left.Session.Detail : left.Session.Title, Again(left)));
                // The result card's own line (CompetitionRound.Outcome) does not know about the hold: say it here.
                string heldNote = HeldNote(left);
                if (heldNote != null) _app.Toast(heldNote);
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
                Perf.Mark("free putt ended, " + done.Total + " putts, " + _saver.Sent + " sent, " + _saver.Held + " held, " + _saver.Skipped + " test putts skipped");
                if (done.Total > 0)
                {
                    // The session's numbers; what became of the putts only when they were not saved (held, or test putts).
                    string went = Api.HoldsPlay && _saver.Real > 0 ? " " + Api.NotSaved + "." : _saver.Real == 0 ? " Test putts are not saved." : "";
                    _app.Toast(done.Total + (done.Total == 1 ? " putt" : " putts") + ", " + done.MakePercent + "% made." + went);
                }
                return;
            }
            _saver.Abandon(); // does nothing if the round was finished
            _app.LeaveGame();
            Perf.Mark("game closed, " + _saver.Sent + " putts sent, " + _saver.Held + " held, " + _saver.Skipped + " test putts skipped");
            // Exit, finished or not: back to the main screen of the tab the game came from (the game over card was the result).
            _app.ExitToTab();
            string saved = SavedNote(_saver);
            if (saved != null) _app.Toast(saved);
            // An arcade game's game over is its own 3D card: once back in the app from one that finished, rate it.
            if (_finishedScore.HasValue) Feedback.AskGame(_app, _title ?? _playingName);
        }
    }
}
