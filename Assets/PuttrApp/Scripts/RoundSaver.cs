// Saves a round to the Player's account with the three calls puttr-app makes: POST /game/new/
// when it starts, /game/savePutt/ for every putt, /game/over/ with the score.
//
// The round is opened on its first real putt, so a game that is opened and left writes nothing,
// and putts wait for the round's id. Test putts are counted but never sent (RoundSaver.TestPutt): the
// keyboard's, and the on-screen test buttons' (TestPutts.cs, played through RoundSaver.AsTest).
//
// With more than one Player (pass and play) it saves as the app does: the round is opened for that many
// players, every putt goes in under the Player who hit it (a profile's own user id; "0" for a guest, who has
// no account), and the round closes with POST /game/multiplayer/over, which scores the host's round and
// gives every other profile that putted its own round. Puttr-the-bot's putts are never sent.
//
// These calls go to production, as the Puttr app's do. Only under -app2HoldPlay (Api.HoldPlay, development) are they
// answered on the device (Api.Held): Held counts those putts, Sent the ones the API really took.
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Puttr;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PuttrApp
{
    public sealed class RoundSaver
    {
        // Tracks' games row (1620, puttr-app tracks/catalog.js TRACKS_GAME_ID) is unpublished by design, so the API never
        // lists it and the saver found no game: no Tracks round or putt was saved (Matt, 2026-11-26). The app has its id.
        static readonly Dictionary<string, string> GameIds = new Dictionary<string, string> { { "TRACKS", "1620" } };

        /// <summary>The user id a guest's putts are saved under: no account, so no stats (the app sends 0).</summary>
        public const string Guest = "0";

        // The keys the arcade plays a test putt on (Arcade.cs, "Putts without a console"): space, 1 to 8, Z, C, V, X.
        static readonly Key[] TestKeys =
        {
            Key.Space, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8,
            Key.Z, Key.C, Key.V, Key.X,
        };

        /// <summary>
        /// True in the frame a keyboard test putt was played. The arcade injects it into the console and the Putt
        /// event comes back before the key press's frame is over, so a putt that arrives while one of those keys
        /// has just gone down is the keyboard's, whatever Puttr is connected. (A real putt landing in that same
        /// frame is taken for a test putt too: it errs toward not saving.)
        /// </summary>
        public static bool KeyboardPuttNow
        {
            get
            {
                Keyboard kb = Keyboard.current;
                if (kb == null) return false;
                foreach (Key key in TestKeys) if (kb[key].wasPressedThisFrame) return true;
                return false;
            }
        }

        static bool _injecting;

        /// <summary>
        /// Plays an on-screen test putt (TestPutts.cs) so that everything it reaches knows it for one. The console
        /// raises its Putt event, the arcade plays the putt and the app scores it all inside the Inject call, so the
        /// mark is up for exactly as long as that putt is being handled, whatever Puttr is connected.
        /// </summary>
        public static void AsTest(System.Action inject)
        {
            _injecting = true;
            try { inject(); }
            finally { _injecting = false; }
        }

        /// <summary>True while a test putt is being handled: a keyboard one (KeyboardPuttNow) or an on-screen one (AsTest).</summary>
        public static bool TestPuttNow => _injecting || KeyboardPuttNow;

        /// <summary>
        /// Whether a putt arriving now is a test putt, which is played but never saved: the arcade's demo, made-up
        /// data, the simulator on this machine, no Puttr at all, or a keyboard or on-screen test putt played while a
        /// real Puttr is connected. Only -app2SaveTestPutts (Green.SaveTestPutts, test accounts) lets keyboard,
        /// on-screen and simulator putts through.
        /// </summary>
        public static bool TestPutt(bool demo = false) => IsTestPutt(demo, Green.RealPutts, Green.SaveTestPutts, TestPuttNow);

        /// <summary>The rule itself, apart from the device (for the tests). realPutts is already true when saveTestPutts is.</summary>
        public static bool IsTestPutt(bool demo, bool realPutts, bool saveTestPutts, bool keyboard) => demo || !realPutts || (keyboard && !saveTestPutts);

        readonly string _gameType;
        string _gameId, _roundId;
        bool _open, _opening;
        int _players = 1;
        string _puttData = "[]";
        bool _openNow;
        System.Action _pendingFinish;
        readonly List<Dictionary<string, object>> _queued = new List<Dictionary<string, object>>();

        /// <summary>A test putt was saved into this round by the tester's switch (Green.SaveTestSwitch): the round is the
        /// tester's own, and puts no row on the club board other Players see.</summary>
        public bool HadTestPutt { get; private set; }
        /// <summary>Putts the API took.</summary>
        public int Sent { get; private set; }
        /// <summary>Real putts answered on the device under -app2HoldPlay (Api.Held): not saved anywhere. 0 in normal use.</summary>
        public int Held { get; private set; }
        /// <summary>Test putts: played, never sent.</summary>
        public int Skipped { get; private set; }
        /// <summary>Real putts taken for saving, whether or not their calls have been answered yet.</summary>
        public int Real { get; private set; }
        /// <summary>The row in the API's games table, once known. Used for the leaderboard.</summary>
        public string GameId => _gameId;
        /// <summary>The open user_games row (null until the first putt opens it).</summary>
        public string RoundId => _roundId;
        /// <summary>The last round this saver finished.</summary>
        public string LastRoundId { get; private set; }
        /// <summary>Post the finished score to the PUTTR club board too (the game page's Recent and All time), as puttr-app's
        /// postLeaderboard does. Tracks posts its own (to-par, by tee set), so it is skipped, and so is every game the app
        /// never posted (NoBoard).</summary>
        public bool PostsBoard = true;

        /// <summary>The board row's distance column, as the app's game posted it: most games 0 (one board), Major League its
        /// innings, Oh-One its start, a Dead Center spot or The Lab its distance. A board read with a distance shows only
        /// rows posted with it, so this decides whether the app's own board shows the score.</summary>
        public int BoardDistance;

        /// <summary>
        /// Games whose screen in puttr-app never calls postLeaderboard (components/games: Assessment, BeerPong, CarbonCopy,
        /// Cricket, Derby, Fitting, Horse, Lockdown, Match, Open, Pendulum, Pure, Rock, SquareUp, Stretch, Survivor, TheYips,
        /// Waltz): their rounds are saved to the account but put no row on the club board. The games this app added
        /// (Puttr Arcade's own) are not listed, so they post as before.
        /// </summary>
        public static readonly HashSet<string> NoBoard = new HashSet<string>
        {
            "ASSESSMENT", "BEERPONG", "CARBONCOPY", "CRICKET", "DERBY", "FITTING", "HORSE", "LOCKDOWN", "MATCH", "OPEN",
            "PENDULUM", "PURE", "ROCK", "SQUAREUP", "STRETCH", "SURVIVOR", "THEYIPS", "WALTZ", "TRACKS",
        };

        /// <summary>
        /// The distance column the app's screen for this game posts its board row with (ClubActions.postLeaderboard's third
        /// argument): Major League its innings, Oh-One its start (only 301 is posted here), Dead Center its spot when one
        /// is picked (0 for Random putts), The Lab its distance, and the fixed putt counts of the nine- and twelve-putt
        /// games. 0 for the rest, as the app posts them.
        /// </summary>
        public static int BoardDistanceFor(string gameType, int feet, bool fixedSpot, int innings)
        {
            switch ((gameType ?? "").ToUpperInvariant())
            {
                case "MAJORLEAGUE": return innings;
                case "OHONE": return 301;
                case "DEADCENTER": return fixedSpot ? feet : 0;
                case "LAB": return feet;
                case "CENTERCUT": case "TOURJUICE": case "SPEEDDEMON": return 9;
                case "SWITCHBACK": case "DOORS": case "LEAPFROG": case "HIGHSIDE": return 12;
                case "GEARS": case "CLOCK": return 15;
                default: return 0;
            }
        }

        /// <summary>Whether a finished round of this gameType goes on the club board at all.</summary>
        public static bool Boarded(string gameType) => !string.IsNullOrEmpty(gameType) && !NoBoard.Contains(gameType.ToUpperInvariant());

        /// <param name="gameType">The API gameType this saves as: "ARCADESTREAK", "OPEN".</param>
        public RoundSaver(string gameType)
        {
            _gameType = gameType;
            if (gameType != null) Analytics.Playing(gameType, null, gameType == "TRACKS" ? "tracks" : "game");
            if (!Fixtures.On && gameType != null) App.Run(FindGame());
        }

        // Which row of the games table this is. Asked once; the API lists games by type without a token.
        IEnumerator FindGame()
        {
            if (GameIds.TryGetValue(_gameType, out string known)) { _gameId = _gameId ?? known; Flush(); yield break; }
            // The unpublished rows (the arcade extras, the sensor games) are never listed by the API: this app has their ids.
            object local = ArcadeExtras.All.Find(g => J.Str(g, "gameType") == _gameType) ?? SensorGames.All.Find(g => J.Str(g, "gameType") == _gameType);
            if (local != null) { GameIds[_gameType] = J.Str(local, "id"); _gameId = _gameId ?? GameIds[_gameType]; Flush(); yield break; }
            long status = 0;
            string text = null;
            yield return Api.Send("GET", Config.ApiUrl + "/games/" + _gameType + "/", null, null, (s, t) => { status = s; text = t; });
            foreach (object row in J.List(J.Parse(text)))
            {
                if (J.Str(row, "gameType").ToUpperInvariant() != _gameType) continue;
                GameIds[_gameType] = J.Str(row, "id");
                _gameId = _gameId ?? GameIds[_gameType]; // a fixed-distance On Fire has set its own
                break;
            }
            if (_gameId == null) Perf.Note("game: no " + _gameType + " game in the API (status " + status + "). This round will not be saved.");
            Flush();
        }

        /// <summary>Saves as this row of the games table rather than the one found by type.</summary>
        public void UseGame(string gameId) { _gameId = gameId; Flush(); }

        /// <summary>A new round begins (also "play again").</summary>
        /// <param name="players">How many play (Puttr-the-bot's seat included, as the app counts a ghost's): the round's players column.</param>
        /// <param name="puttData">The round's puttData column: "[]" for most games; Tracks sends the round's setup.</param>
        /// <param name="openNow">Open the round now rather than on the first real putt (Tracks, as the app: its round
        /// rows are tied to the game from the first tee). Nothing is opened while there is no real Puttr to putt on.</param>
        public void Begin(int players = 1, string puttData = "[]", bool openNow = false)
        {
            _roundId = null;
            HadTestPutt = false;
            _pendingFinish = null;
            _queued.Clear();
            _open = true;
            _opening = false;
            _players = Mathf.Max(1, players);
            _puttData = puttData ?? "[]";
            _openNow = openNow;
            Flush();
        }

        /// <param name="sensorData">The Vertex burst behind the putt (the sensor games, SensorSave.js), or null.</param>
        /// <param name="userId">Who hit it: null for the signed-in Player, another profile's user id, or Guest.</param>
        /// <param name="rollout">Inches past (+) or short (-) of the hole as the app measured it (Tracks sends it); null lets the API work it out from the path.</param>
        public void Putt(int feet, int tee, bool made, PuttResult putt, bool demo = false, string sensorData = null, string userId = null, double? rollout = null)
        {
            if (!_open) return;
            if (TestPutt(demo)) { Skipped++; Debug.Log("Puttr 2 game: test putt, not saved"); return; }
            var row = new Dictionary<string, object>
            {
                { "userId", string.IsNullOrEmpty(userId) ? Auth.Uid : userId },
                { "distance", feet },
                { "tee", tee },
                { "made", made ? 1 : 0 },
                { "ballPath", PathJson(putt.Path) },
                { "holeBounds", HoleJson(putt.Hole) },
            };
            if (sensorData != null) row["sensorData"] = sensorData;
            if (rollout.HasValue && !double.IsNaN(rollout.Value) && !double.IsInfinity(rollout.Value)) row["rollout"] = System.Math.Round(rollout.Value, 1);
            if (!Green.SaveTestFlag && (TestPuttNow || !Green.ConsoleReal)) HadTestPutt = true;   // saved by the tester's switch
            Real++;
            _queued.Add(row);
            Flush();
        }

        /// <summary>The round is over, with this score.</summary>
        public void End(int score)
        {
            if (!_open) return;
            _open = false;
            Close(() => Finish(score));
        }

        /// <summary>
        /// A round more than one Player played is over: scores and userIds by seat (a profile's user id, Guest, or
        /// null for Puttr). POST /game/multiplayer/over closes the host's round with the host's score and makes a
        /// round for every other profile that putted in it; a guest and Puttr are sent as 0 and get nothing.
        /// </summary>
        /// <param name="mine">The signed-in Player's seat: their score is the one that goes on the board.</param>
        public void End(IList<double> scores, IList<string> userIds, int mine)
        {
            if (!_open) return;
            _open = false;
            if (scores == null || scores.Count == 0) { _queued.Clear(); return; }
            int seat = Mathf.Clamp(mine, 0, scores.Count - 1);
            if (scores.Count == 1) { Close(() => Finish(Mathf.RoundToInt((float)scores[0]))); return; }
            Close(() => FinishMany(OverBody(null, scores, userIds, seat, Auth.Uid), Mathf.RoundToInt((float)scores[seat])));
        }

        /// <summary>
        /// Host mode (Event and Venue): the round is over. Nobody at the Puttr is the host, so no seat is the host's:
        /// a member who scanned in gets a round of their own with their putts (the API makes it, as it does for the
        /// profiles on an account), a guest gets nothing, and the host's own round is closed with the score that
        /// goes on the host's board (boardSeat's).
        /// </summary>
        public void EndHosted(IList<double> scores, IList<string> userIds, int boardSeat)
        {
            if (!_open) return;
            _open = false;
            if (scores == null || scores.Count == 0) { _queued.Clear(); return; }
            int seat = Mathf.Clamp(boardSeat, 0, scores.Count - 1);
            Close(() => FinishMany(HostedBody(null, scores, userIds, seat, Auth.Uid), Mathf.RoundToInt((float)scores[seat])));
        }

        /// <summary>
        /// The body of POST /game/multiplayer/over for a host's game: each seat with the id it saves under (a
        /// member's own, 0 for a guest or Puttr), then the host's own id with the board score, which is what makes
        /// the API close the host's round. If the host scanned in to play, their seat already does that.
        /// </summary>
        public static Dictionary<string, object> HostedBody(string roundId, IList<double> scores, IList<string> userIds, int boardSeat, string host)
        {
            var scoreArray = new List<object>();
            var playerArray = new List<object>();
            bool hostPlays = false;
            for (int i = 0; i < scores.Count; i++)
            {
                string id = userIds != null && i < userIds.Count ? userIds[i] : null;
                bool nobody = string.IsNullOrEmpty(id) || id == Guest;
                if (!nobody && id == host) { if (hostPlays) nobody = true; else hostPlays = true; }
                scoreArray.Add(scores[i]);
                playerArray.Add(nobody ? (object)0 : id);
            }
            if (!hostPlays)
            {
                scoreArray.Add(scores[Mathf.Clamp(boardSeat, 0, scores.Count - 1)]);
                playerArray.Add(host);
            }
            return new Dictionary<string, object> { { "gameId", roundId }, { "scoreArray", scoreArray }, { "playerArray", playerArray } };
        }

        /// <summary>
        /// Who a putt from this seat is saved under: the host (the signed-in Player) for the host's seat, the seat's
        /// own profile id, Guest for a guest or a seat nobody named; null for Puttr, whose putts are never saved.
        /// </summary>
        public static string SeatUser(IList<string> userIds, int seat, int mine, string host)
        {
            if (seat == mine) return host;
            if (userIds == null || seat < 0 || seat >= userIds.Count) return Guest;
            string id = userIds[seat];
            if (id == null) return null;
            // The host is in one seat only: anyone else carrying the host's id (or none) is a guest.
            return id.Length == 0 || id == host ? Guest : id;
        }

        // Finishes now if the round is open, or as soon as it is (its /game/new is on its way); a round that no
        // real putt ever opened has nothing to finish.
        void Close(System.Action finish)
        {
            if (_roundId != null) finish();
            else if (_queued.Count > 0 || _opening) _pendingFinish = finish;
        }

        /// <summary>
        /// The body of POST /game/multiplayer/over: {gameId, scoreArray, playerArray}. The host's own user id sits at
        /// the host's seat whatever was passed there (the API closes the round on finding it), another profile keeps
        /// its id, and a guest or Puttr is 0.
        /// </summary>
        public static Dictionary<string, object> OverBody(string roundId, IList<double> scores, IList<string> userIds, int mine, string host)
        {
            var scoreArray = new List<object>();
            var playerArray = new List<object>();
            for (int i = 0; i < scores.Count; i++)
            {
                scoreArray.Add(scores[i]);
                string id = i == mine ? host : userIds != null && i < userIds.Count ? userIds[i] : null;
                // The host can sit in one seat only: a second seat with the host's id would be scored over the first.
                if (i != mine && id == host) id = null;
                playerArray.Add(string.IsNullOrEmpty(id) || id == Guest ? (object)0 : id);
            }
            return new Dictionary<string, object> { { "gameId", roundId }, { "scoreArray", scoreArray }, { "playerArray", playerArray } };
        }

        /// <summary>Left part way through: mark the round abandoned, as the app does.</summary>
        public void Abandon()
        {
            if (!_open) return; // already finished (its score may still be on its way)
            if (_roundId != null) App.Run(Api.Post("/game/abandon", new Dictionary<string, object> { { "gameId", _roundId } }, null, null, auth: false));
            _roundId = null;
            _open = false;
            _openNow = false;
            _pendingFinish = null;
            _queued.Clear();
        }

        void Flush()
        {
            if (_gameId == null || string.IsNullOrEmpty(Auth.Uid)) return;
            if (_roundId == null)
            {
                if (_opening || !_open && _pendingFinish == null) return;
                // Opened by the first real putt; or at once for a round that asked (Tracks), when there is a real Puttr to putt on.
                if (_queued.Count == 0 && !(_openNow && _open && Green.RealPutts)) return;
                _opening = true;
                _openNow = false;
                var body = new Dictionary<string, object>
                {
                    // clubId: the app sends props.club.clubId, which is "PUTTR" in the Player's app (ClubReducer's default).
                    { "userId", Auth.Uid }, { "gameId", _gameId }, { "clubId", HostMode.ClubId }, { "locationId", 0 }, { "laneId", 0 },
                    { "tournamentId", 0 }, { "players", _players }, { "puttData", _puttData },
                };
                App.Run(Api.Post("/game/new/", body, made =>
                {
                    _opening = false;
                    string id = J.Str(made, "id");
                    if (id.Length == 0) { Perf.Note("game: /game/new/ gave no id. This round will not be saved."); return; }
                    // Left before the answer came (Abandon): the row was opened for nothing, so it is marked abandoned.
                    if (!_open && _pendingFinish == null)
                    {
                        App.Run(Api.Post("/game/abandon", new Dictionary<string, object> { { "gameId", id } }, null, null, auth: false));
                        return;
                    }
                    _roundId = id;
                    Perf.Note("game: round " + _roundId + " opened" + (Api.IsLocal(_roundId) ? " on this device only (play records are held)" : ""));
                    Flush();
                    if (_pendingFinish != null) { System.Action finish = _pendingFinish; _pendingFinish = null; finish(); }
                }, why => { _opening = false; Perf.Note("game: could not open the round (" + why + ")"); }, auth: false));
                return;
            }
            foreach (Dictionary<string, object> putt in _queued)
            {
                putt["gameId"] = _gameId;
                putt["userGameId"] = _roundId;
                // Asked as the call goes out: a held call (-app2HoldPlay) answers "ok" too, and that is not a save.
                bool held = Api.Holds("/game/savePutt/");
                App.Run(Api.Post("/game/savePutt/", putt, _ => { if (held) Held++; else Sent++; }, why => Perf.Note("game: putt not saved (" + why + ")"), auth: false));
            }
            _queued.Clear();
        }

        /// <summary>Called once with the round's id when its score has been saved (a Versus turn is sent from here).</summary>
        public System.Action<string> Over;

        void Finish(int score)
        {
            string round = _roundId;
            System.Action<string> over = Over;
            Over = null;
            LastRoundId = round;
            _roundId = null;
            App.Run(Api.Post("/game/over/", new Dictionary<string, object> { { "gameId", round }, { "score", score } },
                _ => { Perf.Note("game: round " + round + (Api.IsLocal(round) ? " held, not saved, score " : " saved, score ") + score); if (PostsBoard && Boarded(_gameType) && !HadTestPutt) PostBoard(round, score); over?.Invoke(round); },
                why => Perf.Note("game: score not saved (" + why + ")"), auth: false));
        }

        // More than one Player: the API scores the host's round and makes the other profiles theirs. The board row
        // is the signed-in Player's own score, as for a round played alone.
        void FinishMany(Dictionary<string, object> body, int mine)
        {
            string round = _roundId;
            LastRoundId = round;
            _roundId = null;
            body["gameId"] = round;
            App.Run(Api.Post("/game/multiplayer/over", body,
                _ => { Perf.Note("game: round " + round + (Api.IsLocal(round) ? " held, not saved" : " saved") + ", " + J.List(body["scoreArray"]).Count + " players"); if (PostsBoard && Boarded(_gameType) && !HadTestPutt) PostBoard(round, mine); },
                why => Perf.Note("game: scores not saved (" + why + ")"), auth: false));
        }

        void PostBoard(string round, int score)
        {
            App.Run(Api.Post("/club/leaderboard/add", BoardBody(_gameId, round, score, BoardDistance), null, why => Perf.Note("game: board not posted (" + why + ")")));
        }

        /// <summary>
        /// The body of POST /club/leaderboard/add, as puttr-app's postLeaderboard: the PUTTR board, the games row, the
        /// round, the signed-in Player, the public name as a JSON string (utils/family.js publicPlayer, stringified), the
        /// score and the board's distance. The API keeps one row a round (it looks the userGameId up first).
        /// </summary>
        public static Dictionary<string, object> BoardBody(string gameId, string userGameId, double score, int distance)
        {
            object me = App.I != null ? App.I.Profile : null;
            return new Dictionary<string, object>
            {
                { "clubId", HostMode.ClubId }, { "gameId", gameId }, { "userGameId", userGameId }, { "userId", Auth.Uid }, { "userName", HostMode.On ? HostMode.BoardName() : Kid.PublicPlayer(me) }, { "score", score }, { "distance", distance },
            };
        }

        /// <summary>The camera frames as the app stores them: "[[x,y,r],...]".</summary>
        public static string PathJson(IReadOnlyList<BallPoint> path)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; path != null && i < path.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(path[i].X).Append(',').Append(path[i].Y).Append(',').Append(path[i].R).Append(']');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>The hole as the console reports it, three numbers, passed through unchanged.</summary>
        public static string HoleJson(Hole hole) =>
            "[" + Mathf.RoundToInt(hole.X) + "," + Mathf.RoundToInt(hole.Y) + "," + Mathf.RoundToInt(hole.Diameter) + "]";
    }
}
