// A round: a fixed list of putts, each from a dealt distance and tee, scored as it goes. It is
// what a tournament round and a League Night game both are; only the scoring differs.
//
//   Tournament: a holed putt scores its distance in feet.
//   League Night (Center Cut): a holed putt scores its distance, less the share by which it
//   came in off the line from its tee: d * (1 - |off line| / 90).
//   Either way a putt that drops in the back of the hole scores nothing.
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

        public static string Points(double score) => score.ToString(score % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture);

        /// <summary>One putt at each distance from 3 to 11 feet, shuffled, from a random tee: a League Night game when the week sets none.</summary>
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
    ///   Entered: opened as the tournament round or league game it is; the score goes on the board.
    ///   Rehearsal: the same putts, saved and recorded as an ordinary practice game, entered nowhere.
    ///   Local: no real Puttr (test putts): nothing is sent at all.
    /// Entered needs the switch on the Profile tab (CompetitionRound.Enter), which is off until
    /// the scoring here has been checked putt for putt against the current app: a tournament
    /// round can be played once, and both boards are live.
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

        const string EnterPref = "app2.enter";
        public static bool Enter
        {
            get => PlayerPrefs.GetInt(EnterPref, 0) == 1;
            set { PlayerPrefs.SetInt(EnterPref, value ? 1 : 0); PlayerPrefs.Save(); }
        }

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
            // Only competition scores wait for the switch: a coach session or a drill is the Player's own practice.
            How = What == Kind.Coach || What == Kind.Practice || What == Kind.Game ? Mode.Entered : Enter ? Mode.Entered : Mode.Rehearsal;
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
                object started = null;
                yield return Api.Post("/coach/session-start", new Dictionary<string, object>(), data => started = data, why => failed = why);
                _gameId = J.Str(started, "gameId");
                row = new Dictionary<string, object> { { "id", J.Str(started, "userGameId") } };
            }
            else if (How == Mode.Entered && What == Kind.Tournament)
            {
                var body = new Dictionary<string, object> { { "gameId", _gameId }, { "tournamentId", J.Str(Tournament, "id") }, { "tournamentGameId", J.Str(Round, "id") }, { "players", 1 }, { "puttData", Session.DealtJson() } };
                yield return Api.Post("/tournaments/game/new/", body, data => row = data, why => failed = why);
            }
            else
            {
                var body = new Dictionary<string, object>
                {
                    { "userId", Auth.Uid }, { "gameId", _gameId }, { "clubId", "PUTTR" }, { "locationId", 0 }, { "laneId", 0 },
                    { "tournamentId", 0 }, { "players", 1 }, { "puttData", Session.DealtJson() },
                };
                yield return Api.Post("/game/new/", body, data => row = data, why => failed = why, auth: false);
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
                if (J.Int(row, "status") == 2) { Problem = "You have already played this round."; done(Problem); yield break; }
                object putts = J.Get(row, "playerPutts");
                if (putts is string text) putts = J.Parse(text);
                List<object> mine = J.List(putts).Count > 0 ? J.List(J.List(putts)[0]) : new List<object>();
                int played = Mathf.Min(J.Int(row, "puttCounter"), Mathf.Min(mine.Count, Session.Putts.Count));
                for (int i = 0; i < played; i++) Session.Scores.Add(PuttrJson.Number(mine[i], 0));
            }
            Perf.Note("round: " + What + " opened as " + How + ", game " + UserGameId + (Session.Index > 0 ? ", resumed at putt " + (Session.Index + 1) : ""));
            done(null);
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
            App.Run(Api.Post("/game/savePutt/", new Dictionary<string, object>
            {
                { "userId", Auth.Uid }, { "gameId", _gameId }, { "userGameId", UserGameId }, { "distance", dealt.Feet }, { "tee", dealt.Tee },
                { "made", made ? 1 : 0 }, { "ballPath", RoundSaver.PathJson(putt.Path) }, { "holeBounds", RoundSaver.HoleJson(putt.Hole) },
            }, null, why => Perf.Note("round: putt not saved (" + why + ")"), auth: false));
            App.Run(Api.Post("/game/save/", new Dictionary<string, object> { { "gameState", StateJson() } }, null, why => Perf.Note("round: state not saved (" + why + ")"), auth: false));
        }

        /// <summary>The user_games columns the API rewrites after every putt.</summary>
        public string StateJson() => J.Write(new Dictionary<string, object>
        {
            { "id", UserGameId }, { "score", Session.Total }, { "currentPlayer", 0 }, { "puttCounter", Session.Index },
            { "playerPutts", new List<object> { new List<double>(Session.Scores), new List<object>(), new List<object>(), new List<object>() } },
        });

        /// <summary>The round is over: closes the game, and if it was entered, puts the score on the board.</summary>
        public IEnumerator Finish(Action done)
        {
            if (Finished) { done?.Invoke(); yield break; }
            Finished = true;
            double score = Session.Total;
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
            if (What == Kind.Drill) { done?.Invoke(); yield break; }
            if (How == Mode.Local || UserGameId == null) { done?.Invoke(); yield break; }
            if (How == Mode.Entered && What == Kind.League)
            {
                var body = new Dictionary<string, object>
                {
                    { "userId", Auth.Uid }, { "team", Team }, { "score", score }, { "gameId", int.TryParse(_gameId, out int id) ? id : 0 },
                    { "seasonId", SeasonId }, { "weekNumber", WeekNumber }, { "userGameId", int.TryParse(UserGameId, out int ug) ? ug : 0 },
                };
                yield return Api.Post("/league/score/submit", body, data => Submitted = data, why => Problem = why);
                Perf.Note("round: league score " + score + (Problem == null ? " submitted" : " NOT submitted (" + Problem + ")"));
            }
            if (How == Mode.Entered && What == Kind.Tournament)
            {
                var body = new Dictionary<string, object> { { "tournamentId", J.Str(Tournament, "id") }, { "round", RoundNumber }, { "score", score } };
                yield return Api.Post("/tournaments/game/end/", body, data => Submitted = data, why => Problem = why);
                Perf.Note("round: tournament score " + score + (Problem == null ? " submitted" : " NOT submitted (" + Problem + ")"));
            }
            if (What == Kind.Coach)
            {
                int made = Session.Made, putts = Session.Putts.Count;
                var body = new Dictionary<string, object> { { "makePct", putts > 0 ? Math.Round(100.0 * made / putts) : 0 }, { "made", made }, { "userGameId", int.TryParse(UserGameId, out int ug) ? ug : 0 } };
                yield return Api.Post("/coach/session-complete", body, data => Submitted = data, why => Problem = why);
            }
            bool over = false;
            // Against a ghost the game closes as a two-player game: the ghost is player 0 (no account).
            if (Ghost != null)
                App.Run(Api.Post("/game/multiplayer/over", new Dictionary<string, object> { { "gameId", UserGameId }, { "scoreArray", new List<object> { score, GhostTotal(Ghost.Count) } }, { "playerArray", new List<object> { Auth.Uid, 0 } } }, _ => over = true, _ => over = true, auth: false));
            else
            App.Run(Api.Post("/game/over/", new Dictionary<string, object> { { "gameId", UserGameId }, { "score", score } }, _ => over = true, _ => over = true, auth: false));
            // A Versus turn: the API scores it from the game just closed, so it goes once that game is over.
            if (!string.IsNullOrEmpty(VersusId))
            {
                float until = Time.realtimeSinceStartup + 8f;
                while (!over && Time.realtimeSinceStartup < until) yield return null;
                yield return Api.Post("/versus/" + VersusId + "/complete", new Dictionary<string, object> { { "userGameId", int.TryParse(UserGameId, out int ug) ? ug : 0 } }, data => VersusResult = data, why => Problem = why);
            }
            done?.Invoke();
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
            if (How == Mode.Rehearsal) return "Rehearsal: saved as practice, not entered. Turn on \"Enter competition scores\" on your Profile to play for real.";
            if (Problem == "http_409" || Problem == "already_played_today") return "You have already played today. Come back tomorrow.";
            if (Problem != null) return "Your score could not be entered. Check your connection: it is saved on your round.";
            if (What == Kind.League)
            {
                string rank = J.Get(Submitted, "teamRank") != null ? "   ·   #" + J.Int(Submitted, "teamRank") + " on your team" : "";
                return (J.Flag(Submitted, "isNewBest") ? "New personal best" : "Score posted") + rank + (J.Flag(Submitted, "inTopFive") ? "   ·   in the top 5" : "");
            }
            return "Round " + RoundNumber + " entered.";
        }
    }
}
