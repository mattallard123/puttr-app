// Saves a round to the Player's account with the three calls puttr-app makes: POST /game/new/
// when it starts, /game/savePutt/ for every putt, /game/over/ with the score.
//
// The round is opened on its first real putt, so a game that is opened and left writes nothing,
// and putts wait for the round's id. Test putts are counted but never sent (Green.RealPutts).
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Puttr;
using UnityEngine;

namespace PuttrApp
{
    public sealed class RoundSaver
    {
        static readonly Dictionary<string, string> GameIds = new Dictionary<string, string>();

        readonly string _gameType;
        string _gameId, _roundId;
        bool _open, _opening;
        int? _pendingScore;
        readonly List<Dictionary<string, object>> _queued = new List<Dictionary<string, object>>();

        public int Sent { get; private set; }
        public int Skipped { get; private set; }
        /// <summary>The row in the API's games table, once known. Used for the leaderboard.</summary>
        public string GameId => _gameId;
        /// <summary>The open user_games row (null until the first putt opens it).</summary>
        public string RoundId => _roundId;
        /// <summary>The last round this saver finished.</summary>
        public string LastRoundId { get; private set; }
        /// <summary>Post the finished score to the PUTTR club board too (the game page's Recent and All time), as puttr-app's
        /// postLeaderboard does. Tracks posts its own (to-par, by tee set), so it is skipped.</summary>
        public bool PostsBoard = true;

        /// <param name="gameType">The API gameType this saves as: "ARCADESTREAK", "OPEN".</param>
        public RoundSaver(string gameType)
        {
            _gameType = gameType;
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
        public void Begin()
        {
            _roundId = null;
            _pendingScore = null;
            _queued.Clear();
            _open = true;
            _opening = false;
        }

        /// <param name="sensorData">The Vertex burst behind the putt (the sensor games, SensorSave.js), or null.</param>
        public void Putt(int feet, int tee, bool made, PuttResult putt, bool demo = false, string sensorData = null)
        {
            if (!_open) return;
            if (demo || !Green.RealPutts) { Skipped++; Debug.Log("Puttr 2 game: test putt, not saved"); return; }
            var row = new Dictionary<string, object>
            {
                { "userId", Auth.Uid },
                { "distance", feet },
                { "tee", tee },
                { "made", made ? 1 : 0 },
                { "ballPath", PathJson(putt.Path) },
                { "holeBounds", HoleJson(putt.Hole) },
            };
            if (sensorData != null) row["sensorData"] = sensorData;
            _queued.Add(row);
            Flush();
        }

        /// <summary>The round is over, with this score.</summary>
        public void End(int score)
        {
            if (!_open) return;
            _open = false;
            if (_roundId != null) Finish(score);
            else if (_queued.Count > 0 || _opening) _pendingScore = score; // still being opened
        }

        /// <summary>Left part way through: mark the round abandoned, as the app does.</summary>
        public void Abandon()
        {
            if (!_open) return; // already finished (its score may still be on its way)
            if (_roundId != null) App.Run(Api.Post("/game/abandon", new Dictionary<string, object> { { "gameId", _roundId } }, null, null, auth: false));
            _roundId = null;
            _open = false;
            _queued.Clear();
        }

        void Flush()
        {
            if (_gameId == null || string.IsNullOrEmpty(Auth.Uid)) return;
            if (_roundId == null)
            {
                if (_opening || _queued.Count == 0) return;
                _opening = true;
                var body = new Dictionary<string, object>
                {
                    { "userId", Auth.Uid }, { "gameId", _gameId }, { "clubId", 0 }, { "locationId", 0 }, { "laneId", 0 },
                    { "tournamentId", 0 }, { "players", 1 }, { "puttData", "[]" },
                };
                App.Run(Api.Post("/game/new/", body, made =>
                {
                    _opening = false;
                    _roundId = J.Str(made, "id");
                    if (_roundId.Length == 0) { _roundId = null; Perf.Note("game: /game/new/ gave no id. This round will not be saved."); return; }
                    Perf.Note("game: round " + _roundId + " opened");
                    Flush();
                    if (_pendingScore.HasValue) { Finish(_pendingScore.Value); _pendingScore = null; }
                }, why => { _opening = false; Perf.Note("game: could not open the round (" + why + ")"); }, auth: false));
                return;
            }
            foreach (Dictionary<string, object> putt in _queued)
            {
                putt["gameId"] = _gameId;
                putt["userGameId"] = _roundId;
                App.Run(Api.Post("/game/savePutt/", putt, _ => Sent++, why => Perf.Note("game: putt not saved (" + why + ")"), auth: false));
            }
            _queued.Clear();
        }

        void Finish(int score)
        {
            string round = _roundId;
            LastRoundId = round;
            _roundId = null;
            App.Run(Api.Post("/game/over/", new Dictionary<string, object> { { "gameId", round }, { "score", score } },
                _ => { Perf.Note("game: round " + round + " saved, score " + score); if (PostsBoard && _gameType != "TRACKS") PostBoard(round, score); },
                why => Perf.Note("game: score not saved (" + why + ")"), auth: false));
        }

        void PostBoard(string round, int score)
        {
            object me = App.I != null ? App.I.Profile : null;
            // The public board: the player's public name and photo and nothing else (puttr-app utils/family.js publicPlayer).
            string who = J.Write(new Dictionary<string, object> { { "displayName", J.Str(me, "displayName", "Player") }, { "userId", Auth.Uid }, { "photoURL", J.Str(me, "photoURL") } });
            App.Run(Api.Post("/club/leaderboard/add", new Dictionary<string, object>
            {
                { "clubId", "PUTTR" }, { "gameId", _gameId }, { "userGameId", round }, { "userId", Auth.Uid }, { "userName", who }, { "score", score }, { "distance", 0 },
            }, null, why => Perf.Note("game: board not posted (" + why + ")")));
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
