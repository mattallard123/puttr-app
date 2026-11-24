// A round: a fixed list of putts, each from a dealt distance and tee, scored as it goes. It is
// what a tournament round and a League Night game both are; only the scoring differs.
//
//   Tournament: a holed putt scores its distance in feet.
//   League Night (Center Cut): a holed putt scores its distance, less the share by which it
//   came in off the line from its tee: d * (1 - |off line| / 90).
//   Either way a putt that drops in the back of the hole scores nothing.
//
// A League Night game is the week's own game (LeagueRound in LeagueNight.cs): when this build has it as a
// rule game, that game deals and scores the putts (CompetitionRound.Rules) and the round here only keeps the
// tally and makes the API calls.
//
// The numbers are apart from the screen and the API so they can be tested (RoundSession); the
// calls that enter a round in a tournament or the league are in CompetitionRound.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Puttr;
using UnityEngine;

namespace PuttrApp
{
    /// <summary>
    /// How a putt scores. Feet: its distance when holed from the front (tournaments, Standard games, lesson drills).
    /// CenterCut: the distance less the share off line (League Night, Center Cut). Make: one a make (quests).
    /// Precision: one a make that comes in within the putt's angle window (Precision quest levels, REDUCER).
    /// </summary>
    public enum RoundRule { Feet, CenterCut, Make, Precision }

    /// <summary>The app's verdict on each putt, worked out once (by the arcade's AppJudge) and found again by whoever needs it.</summary>
    public static class Verdicts
    {
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PuttResult, AppPutt.Verdict> Known = new System.Runtime.CompilerServices.ConditionalWeakTable<PuttResult, AppPutt.Verdict>();

        public static AppPutt.Verdict Of(PuttResult putt, int feet, int tee)
        {
            if (Known.TryGetValue(putt, out AppPutt.Verdict v)) return v;
            v = AppPutt.Judge(AppPutt.Path(putt.Path ?? new List<BallPoint>()), AppPutt.Hole(putt.Hole), feet, tee);
            Known.Add(putt, v);
            return v;
        }

        /// <summary>The arcade's judge: puts the app's verdict on the putt, and turns away what the app's games ignore.</summary>
        public static bool Judge(PuttResult putt, int feet, int tee)
        {
            if (putt.Path == null || putt.Path.Count == 0) return false;
            AppPutt.Verdict v = Of(putt, feet, tee);
            if (!v.Ball || v.RollUnder) return false;
            putt.Made = v.Made;
            putt.HasEntry = v.Made && v.HasEntry;
            putt.EntryAngle = (int)v.Entry;
            // How far off its own line the ball went in (- left, + right), for games where the window narrows: a ball
            // rolling straight at the middle enters at minus its heading, so a center cut reads 0 from any tee, the
            // dealt one or wherever a putt simulator rolls from.
            putt.ExpectedEntryAngle = -putt.Launch.HeadingDeg;
            putt.OffCenter = v.HasEntry ? (float)v.Entry - putt.ExpectedEntryAngle : 0f;
            putt.EntryDescription = v.Description;
            putt.MissSide = v.MissSide == "LEFT" ? MissSide.Left : v.MissSide == "RIGHT" ? MissSide.Right : MissSide.None;
            return true;
        }
    }

    public sealed class RoundSession
    {
        public string Title = "Round", Detail = "";
        public RoundRule Rule;
        public List<TournamentMath.Dealt> Putts = new List<TournamentMath.Dealt>();
        public readonly List<double> Scores = new List<double>();
        /// <summary>The app's verdict on each putt played, in order: what the Final screen's stat lines are made of.</summary>
        public readonly List<AppPutt.Verdict> Seen = new List<AppPutt.Verdict>();
        public PracticePutt Last;
        /// <summary>A streak: the round ends at the first miss (Streak quest levels).</summary>
        public bool StopOnMiss;
        public bool Stopped { get; private set; }
        public double LastScore;

        public int Index => Scores.Count;
        public bool Done => Stopped || Index >= Putts.Count;
        public TournamentMath.Dealt Now => Putts[Mathf.Min(Index, Putts.Count - 1)];
        public double Total { get { double sum = 0; foreach (double score in Scores) sum += score; return Math.Round(sum, 2); } }
        public int Possible { get { if (Rule == RoundRule.Make || Rule == RoundRule.Precision) return Putts.Count; int feet = 0; foreach (TournamentMath.Dealt putt in Putts) feet += putt.Feet; return feet; } }
        public int Made { get { int n = 0; foreach (double score in Scores) if (score > 0) n++; return n; } }

        /// <summary>
        /// What a putt scores, as the app's games score it (TournamentGame / LeagueGame processBallPath):
        /// made when getMakeProbability is over 1; entry is getHoleEntry's angle, which is 0 when the
        /// camera saw no entry (so such a make counts as straight in); outside the tee's line by 90
        /// degrees or more is the back door and scores nothing.
        /// </summary>
        public static double Score(RoundRule rule, int feet, int tee, int count, double entry, double window = 0)
        {
            if (count <= 1) return 0;
            if (rule == RoundRule.Make) return 1;
            // QuestGame REDUCER: inside the tee's line plus or minus half the window, both ends strict.
            if (rule == RoundRule.Precision) { double te = AppPutt.TeeEntry(feet, tee); return entry > te - window / 2 && entry < te + window / 2 ? 1 : 0; }
            double line = AppPutt.TeeEntry(feet, tee);
            if (!(entry > line - 90 && entry < line + 90)) return 0;
            if (rule == RoundRule.Feet) return feet;
            return feet * (1.0 - Math.Abs(line - entry) / 90.0);
        }

        /// <summary>Scores the putt against the one dealt, and moves on to the next.</summary>
        public double Add(PuttResult putt)
        {
            if (Done) return 0;
            TournamentMath.Dealt dealt = Now;
            AppPutt.Verdict v = Verdicts.Of(putt, dealt.Feet, dealt.Tee);
            double score = Score(Rule, dealt.Feet, dealt.Tee, v.Count, v.Entry, dealt.Angle);
            Last = PracticePutt.From(v, dealt.Feet, dealt.Tee);
            // Holed, but through the back door: say so, since "Made" with no points would read as a mistake.
            if (v.Made && score <= 0) { Last.Made = false; Last.Entry = null; BackDoor = true; } else BackDoor = false;
            LastScore = score;
            Scores.Add(score);
            Seen.Add(v);
            if (StopOnMiss && score <= 0) Stopped = true;
            return score;
        }

        public bool BackDoor { get; private set; }

        /// <summary>
        /// A putt a rule game dealt and scored itself (a League Night game played as the week's own game): kept here
        /// so the round's total, its saved state and its putt count follow the game. A game that deals as it goes has
        /// no list up front, so the putt is added to the list as it is played.
        /// </summary>
        public double Record(AppPutt.Verdict v, double score, int feet, int tee)
        {
            if (Index >= Putts.Count) Putts.Add(new TournamentMath.Dealt { Feet = feet, Tee = tee });
            if (v != null) { Last = PracticePutt.From(v, feet, tee); Seen.Add(v); }
            BackDoor = false;
            LastScore = score;
            Scores.Add(score);
            return score;
        }

        public static string Points(double score) => score.ToString(score % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture);

        /// <summary>One putt at each distance from 3 to 11 feet, shuffled, from a random tee: what a League Night game
        /// plays when the week's game row sets no putts of its own (LeagueGame.js createRandomPutts).</summary>
        public static List<TournamentMath.Dealt> Shuffled(System.Random dice)
        {
            var putts = new List<TournamentMath.Dealt>();
            for (int feet = 3; feet <= 11; feet++) putts.Add(new TournamentMath.Dealt { Feet = feet, Tee = dice.Next(1, 4) });
            for (int i = putts.Count - 1; i > 0; i--)
            {
                int j = dice.Next(i + 1);
                TournamentMath.Dealt swap = putts[i]; putts[i] = putts[j]; putts[j] = swap;
            }
            return putts;
        }

        public string DealtJson()
        {
            var rows = new List<object>();
            for (int i = 0; i < Putts.Count; i++) rows.Add(new Dictionary<string, object> { { "id", i }, { "distance", Putts[i].Feet }, { "tee", Putts[i].Tee } });
            return J.Write(rows);
        }
    }

    /// <summary>
    /// Entering a round with the API. Three ways a round is played:
    ///   Entered: opened as the tournament round or league game it is; the score goes on the board. This is what a
    ///   round with a real Puttr is, as in the Puttr app (Matt, 2026-11-18).
    ///   Rehearsal: the same putts, saved and recorded as an ordinary practice game, entered nowhere. Only for testers
    ///   and staff who turn on "Rehearsal mode" in Settings, Developer (CompetitionRound.Rehearse, off by default).
    ///   Local: no real Puttr (test putts): nothing is sent at all.
    /// </summary>
    public sealed class CompetitionRound
    {
        /// <summary>
        /// Tournament, League, Coach, Practice: as before. Quest: a quest level (POST /quests/savePutt and
        /// /quests/level/finish, stars by putts made). Drill: a lesson's drill, saved nowhere (as the app).
        /// Game: a catalog game played as itself (Standard and Center Cut games), and as a Versus turn when VersusId is set.
        /// </summary>
        public enum Kind { Tournament, League, Coach, Practice, Quest, Drill, Game }
        public enum Mode { Entered, Rehearsal, Local }
        /// <summary>Instead of the final screen: who started the round reads the putts (the coach's calibration, a speed drill).</summary>
        public Action<CompetitionRound> OnDone;

        // A new key: the old "app2.enter" defaulted to off, and a phone that never touched it must not stay a rehearsal.
        const string RehearsePref = "app2.rehearse";
        /// <summary>Settings, Developer, "Rehearsal mode" (testers and staff): tournament and League Night rounds are played
        /// as practice and entered nowhere. Off by default, so a round is entered, as in the Puttr app.</summary>
        public static bool Rehearse
        {
            get => PlayerPrefs.GetInt(RehearsePref, 0) == 1;
            set { PlayerPrefs.SetInt(RehearsePref, value ? 1 : 0); PlayerPrefs.Save(); }
        }
        /// <summary>Competition rounds are entered: true unless Rehearsal mode is on.</summary>
        public static bool Enter => !Rehearse;

        /// <summary>The way a competition round is played with a real Puttr (the rest, apart from the device, for the tests).</summary>
        public static Mode ModeFor(Kind what, bool realPutts, bool rehearse)
        {
            if (!realPutts) return Mode.Local;
            if (what == Kind.Tournament || what == Kind.League) return rehearse ? Mode.Rehearsal : Mode.Entered;
            return Mode.Entered;
        }

        /// <summary>
        /// The made flag a round's putt is saved with (user_putts.made), as the app's screen for that round writes it:
        /// a quest level the level's own verdict (QuestGame.js gameLogic(makeMiss), so a Precision make outside its window
        /// is a miss), the coach's session and calibration a make the camera is sure of (coachLogic.js buildPutt:
        /// getMakeProbability > 1), and every other round the camera's count above 0 (GameActions.savePutt's made > 0).
        /// </summary>
        public static bool MadeFlag(Kind what, bool unlinked, AppPutt.Verdict v, double score)
        {
            if (v == null) return score > 0;
            if (what == Kind.Quest) return score > 0;
            if (what == Kind.Coach || unlinked) return v.Made;
            return v.SavedMade;
        }

        /// <summary>The coach's calibration: putts saved to user_putts with no game (gameId and userGameId 0), no game row
        /// opened or closed (CoachSession.js, mode 'calibration').</summary>
        public bool Unlinked;

        public readonly Kind What;
        public readonly RoundSession Session;
        public Mode How { get; private set; } = Mode.Local;
        public string UserGameId { get; private set; }
        public string Problem { get; private set; }
        public object Submitted { get; private set; }  // the league's answer: {isNewBest, teamRank, inTopFive, ...}
        public bool Finished { get; private set; }

        // Tournament
        public object Tournament, Round;
        public int RoundNumber;
        // League
        public string SeasonId, Team;
        public int WeekNumber;
        // Quest
        public string QuestId, LevelId;
        public List<object> LevelStars;   // [{star, puttsMade}]
        public int Stars { get; private set; }
        // Versus
        public string VersusId;
        /// <summary>A random opponent's ghost (ONLINE): their saved game's result for each putt, played back beside yours.</summary>
        public List<double> Ghost;
        public string GhostName, GhostPhoto;
        public double GhostTotal(int upTo) { double t = 0; if (Ghost != null) for (int i = 0; i < Math.Min(upTo, Ghost.Count); i++) t += Ghost[i]; return t; }
        public object VersusResult;         // {status, winnerId, challengerScore, challengedScore}

        // A round played by a rule game
        /// <summary>
        /// The rule game that plays this round, or null for a putt list on the ramp. A League Night week whose game
        /// this build has as a rule game (Center Cut) is played by that game, in its own scene with its own scoring;
        /// the round is still opened, saved, filmed and entered here, so the once-a-day rule, rehearsal and the
        /// Rehearsal mode switch are the same either way.
        /// </summary>
        public RuleGame Rules;
        /// <summary>The catalog row of the game being played (its title, and what GameBridge.PlayRule is given).</summary>
        public object Game;

        /// <summary>The round's score: the rule game's own total for the Player when one plays it, else the putts added up.
        /// To two places, as the app rounds a league score (league/utils/format.js roundScore) and the API stores it.</summary>
        public double Score => Rules != null && Rules.Score != null && Rules.Score.Length > 0 ? Math.Round(Rules.Score[0], 2) : Session.Total;

        string _gameId;

        public CompetitionRound(Kind what, RoundSession session, string gameId)
        {
            What = what;
            Session = session;
            _gameId = gameId;
        }

        /// <summary>Opens the round. done(null) to play; otherwise a sentence saying why not.</summary>
        public IEnumerator Open(Action<string> done)
        {
            if (!Green.RealPutts) { How = Mode.Local; done(null); yield break; }
            // A quest level and a lesson drill open nothing: their putts go to their own routes, or nowhere.
            if (What == Kind.Quest || What == Kind.Drill) { How = Mode.Entered; done(null); yield break; }
            // Only competition rounds can be rehearsed (a tester's switch): a coach session or a drill is the Player's own practice.
            How = ModeFor(What, true, Rehearse);
            Analytics.Playing(What == Kind.Game ? J.Str(Round, "gameType").ToUpperInvariant() : null, Session != null ? Session.Title : null, What.ToString().ToLowerInvariant());
            if (Unlinked) { _gameId = "0"; UserGameId = "0"; done(null); yield break; }
            object row = null;
            string failed = null;
            if (What == Kind.Practice && string.IsNullOrEmpty(_gameId))
            {
                // A drill is saved as Free putt is: find that game's row.
                yield return Api.Get("/games/" + FreePuttScreen.GameType + "/", data => { foreach (object g in J.List(data)) if (J.Str(g, "gameType").ToUpperInvariant() == FreePuttScreen.GameType) _gameId = J.Str(g, "id"); }, null);
            }
            if (What == Kind.Coach)
            {
                // The coach's own session row: {userGameId, gameId}.
                // When it cannot be had (offline), the app plays on with 0 and 0: the putts are saved unlinked and
                // /coach/session-complete makes the row then and claims them (coach.js sessionComplete's fallback).
                object started = null;
                yield return Api.Post("/coach/session-start", new Dictionary<string, object>(), data => started = data, null);
                _gameId = J.Str(started, "gameId", "0");
                string id = J.Str(started, "userGameId");
                UserGameId = id.Length > 0 ? id : "0";
                if (_gameId.Length == 0) _gameId = "0";
                Perf.Note("round: coach session opened, game " + UserGameId);
                done(null);
                yield break;
            }
            else if (How == Mode.Entered && What == Kind.Tournament)
            {
                // puttData: the round's own, as TournamentGame.js sends round.puttData (stored on the row as it came).
                var body = new Dictionary<string, object> { { "gameId", _gameId }, { "tournamentId", J.Str(Tournament, "id") }, { "tournamentGameId", J.Str(Round, "id") }, { "players", 1 }, { "puttData", RoundPuttData(Round, Session) } };
                yield return Api.Post("/tournaments/game/new/", body, data => row = data, why => failed = why, live: Live);
            }
            else
            {
                var body = new Dictionary<string, object>
                {
                    { "userId", Auth.Uid }, { "gameId", _gameId }, { "clubId", HostMode.ClubId }, { "locationId", 0 }, { "laneId", 0 },
                    // A ghost game is a game for two (the app's totalPlayers with the ghost seated).
                    { "tournamentId", 0 }, { "players", Ghost != null ? 2 : 1 }, { "puttData", Session.DealtJson() },
                };
                yield return Api.Post("/game/new/", body, data => row = data, why => failed = why, auth: false, live: Live);
            }
            UserGameId = J.Str(row, "id");
            if (failed != null || UserGameId.Length == 0)
            {
                Problem = failed == "offline" ? "No connection. Check your WiFi and try again." : "Could not start the round. Try again in a moment.";
                done(Problem);
                yield break;
            }
            // A tournament round is one row for good: finished cannot be replayed, and one in progress is picked up where it stopped.
            if (How == Mode.Entered && What == Kind.Tournament)
            {
                object putts = J.Get(row, "playerPutts");
                if (putts is string text) putts = J.Parse(text);
                List<object> mine = J.List(putts).Count > 0 ? J.List(J.List(putts)[0]) : new List<object>();
                // The API hands back the round's finished row when it was played already. The app then re-sends its
                // score (TournamentGame.js finishRestoredGame): the round's score can have been lost on the way, which
                // would leave the Player stuck on this round. Then it shows the round as played.
                if (J.Int(row, "status") == 2 || (Session.Putts.Count > 0 && J.Int(row, "puttCounter") >= Session.Putts.Count))
                {
                    double was = 0;
                    foreach (object v in mine) was += PuttrJson.Number(v, 0);
                    yield return Api.Post("/tournaments/game/end/", new Dictionary<string, object> { { "tournamentId", J.Str(Tournament, "id") }, { "round", RoundNumber }, { "score", Math.Round(was, 2) } },
                        null, why => Perf.Note("round: restored tournament score not re-sent (" + why + ")"), live: Live);
                    Finished = true;
                    Problem = "You have already played this round.";
                    done(Problem);
                    yield break;
                }
                int played = Mathf.Min(J.Int(row, "puttCounter"), Mathf.Min(mine.Count, Session.Putts.Count));
                for (int i = 0; i < played; i++) Session.Scores.Add(PuttrJson.Number(mine[i], 0));
            }
            Perf.Note("round: " + What + " opened as " + How + ", game " + UserGameId + (Session.Index > 0 ? ", resumed at putt " + (Session.Index + 1) : ""));
            done(null);
        }

        /// <summary>A tournament round or League Night game the Player entered: written to production even under
        /// -app2HoldPlay, which holds other play records (Api.Held).</summary>
        public bool Live => How == Mode.Entered && (What == Kind.Tournament || What == Kind.League);

        /// <summary>What a tournament round's game row is opened with: the round's own puttData as it came (a string, or
        /// the parsed list written back), else the putts dealt.</summary>
        public static string RoundPuttData(object round, RoundSession session)
        {
            object raw = J.Get(round, "puttData");
            if (raw is string text && text.Length > 0) return text;
            if (raw is List<object> list && list.Count > 0) return J.Write(list);
            return session.DealtJson();
        }

        /// <summary>Saves a putt just scored, and where the round stands (so it can be picked up again).</summary>
        public void Putt(PuttResult putt, TournamentMath.Dealt dealt, bool made)
        {
            if (How == Mode.Local || What == Kind.Drill) return;
            if (What == Kind.Quest)
            {
                App.Run(Api.Post("/quests/savePutt/", new Dictionary<string, object>
                {
                    { "questId", QuestId }, { "levelId", LevelId }, { "distance", dealt.Feet }, { "tee", dealt.Tee }, { "made", made ? 1 : 0 },
                    { "ballPath", RoundSaver.PathJson(putt.Path) }, { "holeBounds", RoundSaver.HoleJson(putt.Hole) },
                }, null, why => Perf.Note("quest: putt not saved (" + why + ")")));
                return;
            }
            if (UserGameId == null) return;
            // The coach's session and calibration save the putt and nothing else (CoachSession.js handlePutt): the
            // session's row is closed by /coach/session-complete, and calibration has none.
            bool own = What == Kind.Coach || Unlinked;
            App.Run(Api.Post("/game/savePutt/", new Dictionary<string, object>
            {
                { "userId", Auth.Uid }, { "gameId", _gameId }, { "userGameId", UserGameId }, { "distance", dealt.Feet }, { "tee", dealt.Tee },
                { "made", made ? 1 : 0 }, { "ballPath", RoundSaver.PathJson(putt.Path) }, { "holeBounds", RoundSaver.HoleJson(putt.Hole) },
            }, null, why => Perf.Note("round: putt not saved (" + why + ")"), auth: false, live: Live));
            if (own) return;
            App.Run(Api.Post("/game/save/", new Dictionary<string, object> { { "gameState", StateJson() } }, null, why => Perf.Note("round: state not saved (" + why + ")"), auth: false, live: Live));
        }

        /// <summary>A putt the round's rule game just called (GameBridge.OnPutt): kept on the tally and saved as any round's putt is.</summary>
        public void RulePutt(Shot called, PuttResult putt)
        {
            if (called == null || called.Player != 0) return;   // only the signed-in Player's putts are the round's
            Session.Record(called.V, called.Points, called.Feet, called.Tee);
            Putt(putt, new TournamentMath.Dealt { Feet = called.Feet, Tee = called.Tee }, called.V != null && called.V.SavedMade);
        }

        /// <summary>Left before it finished: the game is marked abandoned, as the app does. A tournament round is not: it
        /// stays open to be picked up again.</summary>
        public void Abandon()
        {
            // The coach's row is left as the app leaves it (CoachSession.js sends nothing on leaving; the row opens as
            // status 1 anyway), and calibration has no row.
            if (Finished || UserGameId == null || (How == Mode.Entered && What == Kind.Tournament) || What == Kind.Coach || Unlinked || How == Mode.Local) return;
            App.Run(Api.Post("/game/abandon", new Dictionary<string, object> { { "gameId", UserGameId } }, null, null, auth: false, live: Live));
        }

        /// <summary>
        /// A tester played a test putt (TestPutts.cs) in a round that was opened to be saved: from here it is a test
        /// round. The game row it opened is marked abandoned, and nothing more is sent: no putt, no score, no session.
        /// </summary>
        public void MarkTest()
        {
            if (How == Mode.Local || Finished) return;
            Abandon();
            How = Mode.Local;
        }

        /// <summary>The user_games columns the API rewrites after every putt.</summary>
        public string StateJson() => J.Write(new Dictionary<string, object>
        {
            { "id", UserGameId }, { "score", Score }, { "currentPlayer", 0 }, { "puttCounter", Session.Index },
            { "playerPutts", new List<object> { new List<double>(Session.Scores), new List<object>(), new List<object>(), new List<object>() } },
        });

        /// <summary>The round is over: closes the game, and if it was entered, puts the score on the board.</summary>
        public IEnumerator Finish(Action done)
        {
            if (Finished) { done?.Invoke(); yield break; }
            Finished = true;
            double score = Score;
            // QuestGame.endGame: the highest star whose putts-made threshold was met.
            if (What == Kind.Quest)
            {
                Stars = 0;
                foreach (object s in LevelStars ?? new List<object>()) if (Session.Made >= J.Int(s, "puttsMade")) Stars = Math.Max(Stars, J.Int(s, "star"));
                if (How != Mode.Local)
                    yield return Api.Post("/quests/level/finish", new Dictionary<string, object> { { "questId", QuestId }, { "levelId", LevelId }, { "result", Stars } }, null, why => Problem = why);
                done?.Invoke();
                yield break;
            }
            if (What == Kind.Drill || Unlinked) { done?.Invoke(); yield break; }
            if (How == Mode.Local || UserGameId == null) { done?.Invoke(); yield break; }
            if (How == Mode.Entered && What == Kind.League)
            {
                var body = new Dictionary<string, object>
                {
                    { "userId", Auth.Uid }, { "team", Team }, { "score", score }, { "gameId", int.TryParse(_gameId, out int id) ? id : 0 },
                    { "seasonId", SeasonId }, { "weekNumber", WeekNumber }, { "userGameId", int.TryParse(UserGameId, out int ug) ? ug : 0 },
                };
                // The API answers a refusal with 400 and {error}: "already_played_today" is the once-a-day rule.
                yield return Api.Post("/league/score/submit", body, data => Submitted = data, why => Problem = FailureCode(why), live: Live);
                Perf.Note("round: league score " + score + (Problem == null ? " submitted" : " NOT submitted (" + Problem + ")"));
            }
            if (How == Mode.Entered && What == Kind.Tournament)
            {
                var body = new Dictionary<string, object> { { "tournamentId", J.Str(Tournament, "id") }, { "round", RoundNumber }, { "score", score } };
                yield return Api.Post("/tournaments/game/end/", body, data => Submitted = data, why => Problem = why, live: Live);
                Perf.Note("round: tournament score " + score + (Problem == null ? " submitted" : " NOT submitted (" + Problem + ")"));
            }
            if (What == Kind.Coach)
            {
                // The session's own row is closed here, with the makes as its score (coach.js sessionComplete): no
                // /game/over after it, which would overwrite that score and run the game-over updates a second time.
                var body = CoachBody(Session, UserGameId);
                yield return Api.Post("/coach/session-complete", body, data => Submitted = data, why => Problem = why);
                done?.Invoke();
                yield break;
            }
            bool over = false;
            // Against a ghost the game closes as a two-player game: the ghost is player 0 (no account).
            if (Ghost != null)
                App.Run(Api.Post("/game/multiplayer/over", new Dictionary<string, object> { { "gameId", UserGameId }, { "scoreArray", new List<object> { score, GhostTotal(Ghost.Count) } }, { "playerArray", new List<object> { Auth.Uid, 0 } } }, _ => over = true, _ => over = true, auth: false));
            else
            App.Run(Api.Post("/game/over/", new Dictionary<string, object> { { "gameId", UserGameId }, { "score", score } }, _ => over = true, _ => over = true, auth: false, live: Live));
            // A Versus turn: the API scores it from the game just closed, so it goes once that game is over.
            if (!string.IsNullOrEmpty(VersusId))
            {
                float until = Time.realtimeSinceStartup + 8f;
                while (!over && Time.realtimeSinceStartup < until) yield return null;
                yield return Api.Post("/versus/" + VersusId + "/complete", new Dictionary<string, object> { { "userGameId", int.TryParse(UserGameId, out int ug) ? ug : 0 } }, data => VersusResult = data, why => Problem = why);
            }
            // A catalog game played as itself (Standard, Center Cut, Streak; alone, against a ghost or as a Versus turn)
            // goes on the game's board, as Standard.js, CenterCut.js and Streak.js post it after the game is over.
            if (What == Kind.Game && RoundSaver.Boarded(J.Str(Round, "gameType")))
                App.Run(Api.Post("/club/leaderboard/add", RoundSaver.BoardBody(_gameId, UserGameId, score, RoundSaver.BoardDistanceFor(J.Str(Round, "gameType"), 0, false, 0)),
                    null, why => Perf.Note("round: board not posted (" + why + ")")));
            done?.Invoke();
        }

        /// <summary>The body of /coach/session-complete: {makePct, made, userGameId}. made counts the putts the camera is sure
        /// went in (coachLogic.js buildPutt, getMakeProbability > 1), out of the putts played.</summary>
        public static Dictionary<string, object> CoachBody(RoundSession session, string userGameId)
        {
            int made = 0;
            foreach (AppPutt.Verdict v in session.Seen) if (v != null && v.Made) made++;
            int putts = session.Seen.Count;
            return new Dictionary<string, object> { { "makePct", putts > 0 ? Math.Round(100.0 * made / putts, MidpointRounding.AwayFromZero) : 0 }, { "made", made }, { "userGameId", int.TryParse(userGameId, out int ug) ? ug : 0 } };
        }

        /// <summary>The API's own error code for the last refusal ({error: "already_played_today"}), else the transport's ("http_400").</summary>
        static string FailureCode(string why)
        {
            string code = J.Str(Api.LastFailure, "error");
            return code.Length > 0 ? code : why;
        }

        int NextStar()
        {
            int need = int.MaxValue;
            foreach (object s in LevelStars ?? new List<object>()) need = Math.Min(need, J.Int(s, "puttsMade"));
            return need == int.MaxValue ? 1 : need;
        }

        /// <summary>What the result card says about where the score went.</summary>
        public string Outcome()
        {
            if (How == Mode.Local) return "Test putts: nothing was saved or entered.";
            // Under -app2HoldPlay the card says the round was held instead of claiming a save.
            string held = GameBridge.HeldNote(this); if (held != null) return held;
            if (What == Kind.Quest) return Stars == 0 ? "No stars this time. Make " + NextStar() + " to earn one." : Stars + (Stars == 1 ? " star" : " stars") + " earned.";
            if (What == Kind.Drill) return "Drill complete.";
            if (What == Kind.Game && !string.IsNullOrEmpty(VersusId))
            {
                if (VersusResult == null) return Problem != null ? "Your score could not be sent to your opponent. Check your connection." : "Versus sent.";
                if (J.Str(VersusResult, "status") != "COMPLETE") return "Versus sent. Waiting on your opponent to play.";
                string winner = J.Str(VersusResult, "winnerId");
                return winner.Length == 0 ? "It's a tie." : winner == Auth.Uid ? "You won!" : "You lost.";
            }
            if (What == Kind.Game) return "Saved to your stats.";
            if (What == Kind.Coach) return Problem == null && J.Get(Submitted, "currentStreak") != null ? "Session saved. Day " + J.Int(Submitted, "currentStreak") + " of your practice streak." : "Session saved to your stats.";
            if (What == Kind.Practice) return "Saved to your stats.";
            if (How == Mode.Rehearsal) return "Rehearsal: saved as practice, not entered. Turn off Rehearsal mode in Settings to play for real.";
            if (Problem == "already_played_today") return "You have already played today. Come back tomorrow.";
            if (Problem != null) return "Your score could not be entered. Check your connection: it is saved on your round.";
            if (What == Kind.League)
            {
                string rank = J.Get(Submitted, "teamRank") != null ? "   ·   #" + J.Int(Submitted, "teamRank") + " on your team" : "";
                return (J.Flag(Submitted, "isNewBest") ? "New personal best" : "Score posted") + rank + (J.Flag(Submitted, "inTopFive") ? "   ·   in the top 5" : "");
            }
            return "Round " + RoundNumber + " entered.";
        }
    }

    // Center Cut played as a League Night game. (The game itself is in RuleGamesD.cs.)
    public sealed partial class CenterCutGame
    {
        /// <summary>
        /// The putts to play in place of nine dealt at random: a League Night game plays the putts its round was
        /// opened with (the week's game row's own puttData when it has any), so what is saved with the game and what
        /// is played are the same list.
        /// </summary>
        public List<TournamentMath.Dealt> Fixed;

        protected override void Begin()
        {
            base.Begin();
            if (Fixed != null && Fixed.Count > 0) List = new List<TournamentMath.Dealt>(Fixed);
        }
    }
}
