// Live 1v1 (puttr-app MultiplayerScreen, multiplayer/MultiplayerGame): two Players on their own Puttrs,
// in turns, in real time. The host (the Player who made the game) is the referee, exactly as the app:
// both send each ball path to the room, the host scores it and sends the new state. So a Player here
// and a Player on the current app can play each other. Puttr Club only; off for Kid profiles.
//
//   socket.io  wss://puttr-multiplayer.wn.r.appspot.com   JOIN_ROOM "MP_<id>", MESSAGE {room, event}
//              events: GAME_JOINED, BALL_PATH {player 1|2, ballPath, holeBounds, puttCounter (this app only)}, GAME_STATE {gameState}, GAME_OVER {winnerId}
//   POST /multiplayer/game/create {participantId, numPutts, distanceRange, entryAngle, puttData, gameState}
//   GET  /multiplayer/game/:id, /multiplayer/active, /multiplayer/completed, /multiplayer/stats/
//   POST /multiplayer/savePutt/, /multiplayer/game/save, /multiplayer/game/over {gameId, winnerId}, /multiplayer/game/delete
//
// The socket server only relays: it keeps no game state and a room is forgotten when a socket drops. So after a
// drop the client connects again, joins the room again and says GAME_JOINED (socket.io-client does the same for
// the app). The state comes back from the host (it answers GAME_JOINED with GAME_STATE, a message the app already
// reads at any time) and from the game's row (GET /multiplayer/game/:id, which the app's host reads on GAME_JOINED).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
using System.Net.WebSockets;
using System.Threading.Tasks;
#endif
using Puttr;
using PuttrArcade;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>A Socket.IO client that sends as well as listens, over wss (the SDK's only listens, over ws).</summary>
    public sealed class LiveSocket : IDisposable
    {
        public const string Url = "wss://puttr-multiplayer.wn.r.appspot.com/socket.io/?EIO=4&transport=websocket";
        readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
        readonly ConcurrentQueue<string> _out = new ConcurrentQueue<string>();
        CancellationTokenSource _cts;
        public bool Open { get; private set; }
        // The server pings every 25 seconds. A line that has heard nothing for much longer is dead, even if the
        // phone has not noticed (walking out of wifi leaves the socket "open" with nobody on the other end).
        long _heard = System.Diagnostics.Stopwatch.GetTimestamp();
        public double Silent => (System.Diagnostics.Stopwatch.GetTimestamp() - Interlocked.Read(ref _heard)) / (double)System.Diagnostics.Stopwatch.Frequency;
        void Heard() => Interlocked.Exchange(ref _heard, System.Diagnostics.Stopwatch.GetTimestamp());
        public event Action Connected;
        public event Action<string, object> Event;
        public event Action<string> Closed;

        public void Connect()
        {
            _cts = new CancellationTokenSource();
            Heard();
#if UNITY_WEBGL && !UNITY_EDITOR
            _js?.Dispose();
            _js = new JsWebSocket(Url);
#else
            CancellationToken ct = _cts.Token;
            Task.Run(() => Run(ct));
#endif
        }

        public void Emit(string name, object arg) => _out.Enqueue("42" + J.Write(new List<object> { name, arg }));

        public void Pump()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            PumpJs();
#endif
            while (_main.TryDequeue(out Action a)) a();
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            Open = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            _js?.Dispose();
            _js = null;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL (the Samsung TV app): the browser's WebSocket, read here a frame at a time (no threads there).
        JsWebSocket _js;

        void PumpJs()
        {
            if (_js == null) return;
            while (Open && _out.TryDequeue(out string msg)) _js.Send(msg);
            string packet;
            while (_js != null && (packet = _js.Receive()) != null)
            {
                Heard();
                if (packet.StartsWith("0")) _js.Send("40");
                else if (packet == "2") _js.Send("3");
                else if (packet.StartsWith("40")) { Open = true; _main.Enqueue(() => Connected?.Invoke()); }
                else if (packet.StartsWith("42"))
                {
                    List<object> args = J.List(J.Parse(packet.Substring(2)));
                    if (args.Count > 0) { string name = args[0] as string; object arg = args.Count > 1 ? args[1] : null; _main.Enqueue(() => Event?.Invoke(name, arg)); }
                }
            }
            if (_js != null && _js.State == JsWebSocket.SocketState.Closed)
            {
                string why = _js.Reason;
                _js.Dispose();
                _js = null;
                Open = false;
                _main.Enqueue(() => Closed?.Invoke(why));
            }
        }
#else

        async Task Run(CancellationToken ct)
        {
            string why = "closed";
            try
            {
                using (var ws = new ClientWebSocket())
                {
                    await ws.ConnectAsync(new Uri(Url), ct).ConfigureAwait(false);
                    var buffer = new ArraySegment<byte>(new byte[16384]);
                    var text = new StringBuilder();
                    Task<WebSocketReceiveResult> receive = null;
                    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        while (Open && _out.TryDequeue(out string msg))
                            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                        if (receive == null) receive = ws.ReceiveAsync(buffer, ct);
                        if (await Task.WhenAny(receive, Task.Delay(50, ct)).ConfigureAwait(false) != receive) continue;
                        WebSocketReceiveResult part = await receive.ConfigureAwait(false);
                        receive = null;
                        if (part.MessageType == WebSocketMessageType.Close) break;
                        text.Append(Encoding.UTF8.GetString(buffer.Array, 0, part.Count));
                        if (!part.EndOfMessage) continue;
                        string packet = text.ToString();
                        text.Clear();
                        Heard();
                        if (packet.StartsWith("0")) { await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("40")), WebSocketMessageType.Text, true, ct).ConfigureAwait(false); }
                        else if (packet == "2") { await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("3")), WebSocketMessageType.Text, true, ct).ConfigureAwait(false); }
                        else if (packet.StartsWith("40")) { Open = true; _main.Enqueue(() => Connected?.Invoke()); }
                        else if (packet.StartsWith("42"))
                        {
                            object parsed = J.Parse(packet.Substring(2));
                            List<object> args = J.List(parsed);
                            if (args.Count > 0) { string name = args[0] as string; object arg = args.Count > 1 ? args[1] : null; _main.Enqueue(() => Event?.Invoke(name, arg)); }
                        }
                    }
                }
            }
            catch (Exception e) { why = e.Message; }
            Open = false;
            _main.Enqueue(() => Closed?.Invoke(why));
        }
#endif
    }

    /// <summary>The sums of a live game, apart from the socket and the screens so they can be tested.</summary>
    public static class LiveMath
    {
        /// <summary>Tries to get back to the room before the Player is asked (1 + 2 + 4 + 8 + 8 + 8 seconds of waiting).</summary>
        public const int MaxAttempts = 6;

        /// <summary>Seconds to wait before try number <paramref name="attempt"/> (from 1): 1, 2, 4, then 8 each time.</summary>
        public static float Backoff(int attempt) => Mathf.Min(8f, Mathf.Pow(2f, Mathf.Max(0, attempt - 1)));

        /// <summary>How far a game has got: it only ever goes up (host, participant, next putt), so an older state is told from a newer one.</summary>
        public static int Progress(int puttCounter, int currentPlayer) => puttCounter * 2 + (currentPlayer == 2 ? 1 : 0);

        public static int Progress(object state) => Progress(J.Int(state, "puttCounter"), J.Int(state, "currentPlayer", 1));

        /// <summary>gameState.playerPutts: the points of each putt, host first. A missed or missing putt is 0.</summary>
        public static List<double>[] PlayerPutts(object state)
        {
            var both = new[] { new List<double>(), new List<double>() };
            List<object> pp = J.List(J.Get(state, "playerPutts"));
            for (int p = 0; p < 2 && p < pp.Count; p++)
                foreach (object v in J.List(pp[p])) both[p].Add(v == null ? 0 : PuttrJson.Number(v, 0));
            return both;
        }

        public static double Score(List<double> putts) { double s = 0; foreach (double v in putts) s += v; return Math.Round(s, 1); }

        /// <summary>GameSummary renderPlayerPercent: putts that scored, of putts hit. -1 before the first putt.</summary>
        public static int MakePercent(List<double> putts)
        {
            if (putts == null || putts.Count == 0) return -1;
            int made = 0;
            foreach (double v in putts) if (v != 0) made++;
            return (int)Math.Round(made * 100.0 / putts.Count);
        }

        /// <summary>
        /// Extra putts each Player hit after a tie, once the game is over (0: it ended in regulation). The app's
        /// summary prints puttCounter - total + 1, which is one too many after the last putt has moved the counter on.
        /// </summary>
        public static int SuddenDeathPutts(int puttCounter, int total, bool overtime) => overtime && total > 0 ? Math.Max(1, puttCounter - total) : 0;

        /// <summary>multiplayer_games.status: 0 active, 1 completed, 2 deleted, 3 expired.</summary>
        public static bool Finished(object row) => J.Str(row, "status") == "1";

        public static object State(object row)
        {
            object state = J.Get(row, "gameState");
            return state is string text ? J.Parse(text) : state;
        }
    }

    /// <summary>One live game: its row, its state, the socket, and the referee (when this Player is host).</summary>
    public sealed class LiveMatch : MonoBehaviour
    {
        /// <summary>The line to the room. Lost: the tries ran out, and the Player is asked to retry or leave.</summary>
        public enum Link { Connecting, Live, Reconnecting, Lost }

        const float ConnectSeconds = 10f;   // a try that has not opened by then has failed (a dead network can hang for a minute)
        const float SilentSeconds = 50f;    // the server's ping every 25 s plus its 20 s grace
        const float ResendSeconds = 5f;

        public object Game;                 // the multiplayer_games row
        public List<TournamentMath.Dealt> Putts = new List<TournamentMath.Dealt>();
        public int CurrentPlayer = 1, PuttCounter;
        public List<double>[] PlayerPutts = { new List<double>(), new List<double>() };
        public bool Overtime, Over;
        public string Rules = "entry", WinnerId;
        public double EntryAngle = 180;
        public string Said;                 // the last thing that happened, for the panel
        public Link Net = Link.Connecting;
        public event Action Changed;
        LiveSocket _socket;
        int _attempt;
        bool _everOpen;
        float _retryAt, _connectBy, _pausedAt;
        // This Player's putt, sent but not yet scored by the host (the participant's side only).
        object _pending;
        int _pendingSlot;
        float _pendingAt;

        public string Id => J.Str(Game, "id");
        public bool Host => J.Str(Game, "userId") == Auth.Uid;
        public int Me => Host ? 1 : 2;
        /// <summary>A game with no room: made-up data, or a game made under -app2HoldPlay without writing it (Api.Held gives it a local id).</summary>
        public bool Local => Fixtures.On || Id.StartsWith("local-", StringComparison.Ordinal);
        /// <summary>This Player has putted and the host has not scored it yet.</summary>
        public bool Waiting => _pending != null;
        public bool MyTurn => !Over && CurrentPlayer == Me && Net == Link.Live && _pending == null;
        public TournamentMath.Dealt Now => Putts.Count == 0 ? new TournamentMath.Dealt { Feet = 6, Tee = 2 } : Putts[(Overtime ? PuttCounter % Putts.Count : Math.Min(PuttCounter, Putts.Count - 1))];
        public double Score(int player) => LiveMath.Score(PlayerPutts[player - 1]);
        public string Name(int player) => J.Str(Game, player == 1 ? "hostName" : "participantName", player == 1 ? "Host" : "Opponent");
        public string Photo(int player) => J.Str(Game, player == 1 ? "hostPhoto" : "participantPhoto");

        public static LiveMatch Start(object game)
        {
            var go = new GameObject("Live match");
            DontDestroyOnLoad(go);
            LiveMatch m = go.AddComponent<LiveMatch>();
            m.Load(game);
            if (m.Local || m.Over) m.Net = Link.Live;
            else m.Open();
            return m;
        }

        public void Load(object game)
        {
            Game = game;
            Putts = TournamentMath.Putts(game);
            EntryAngle = J.Num(game, "entryAngle", 180);
            ReadState(LiveMath.State(game));
            if (LiveMath.Finished(game)) { Over = true; WinnerId = J.Str(game, "winnerId"); }
        }

        /// <summary>What the final screen shows, from the game as it stands here.</summary>
        public LiveResult Result()
        {
            LiveResult r = LiveResult.From(Game, Auth.Uid);
            r.Dealt = Putts;
            r.Putts = PlayerPutts;
            r.Overtime = Overtime;
            r.PuttCounter = PuttCounter;
            r.CenterCut = Rules == "centercut";
            r.Winner = string.IsNullOrEmpty(WinnerId) ? 0 : WinnerId == J.Str(Game, "userId") ? 1 : WinnerId == J.Str(Game, "participantId") ? 2 : 0;
            return r;
        }

        void ReadState(object state)
        {
            if (state == null) return;
            CurrentPlayer = J.Int(state, "currentPlayer", 1);
            PuttCounter = J.Int(state, "puttCounter");
            Overtime = J.Flag(state, "isOvertime");
            Rules = J.Str(state, "rules", Rules);
            PlayerPutts = LiveMath.PlayerPutts(state);
            // The turn has moved on, so the host scored the putt this Player was waiting on.
            if (_pending != null && (PuttCounter != _pendingSlot || CurrentPlayer != Me)) _pending = null;
            Changed?.Invoke();
        }

        // A state from the room or the row is only taken if it is not behind this one: an echo or a slow read
        // must never undo a putt. (The row is written after every putt, but a read can still arrive behind the room.)
        bool Newer(object state, bool orSame)
        {
            if (state == null) return false;
            int theirs = LiveMath.Progress(state), mine = LiveMath.Progress(PuttCounter, CurrentPlayer);
            return orSame ? theirs >= mine : theirs > mine;
        }

        object StateObject() => new Dictionary<string, object>
        {
            { "currentPlayer", CurrentPlayer }, { "puttCounter", PuttCounter }, { "isOvertime", Overtime }, { "rules", Rules },
            { "playerPutts", new List<object> { new List<double>(PlayerPutts[0]), new List<double>(PlayerPutts[1]) } },
        };

        // ── The line to the room ─────────────────────────────────────────────

        // One try at the room. A socket is used once: after a drop a new one is made, and whatever an old one
        // still has to say is ignored (s != _socket).
        void Open()
        {
            _socket?.Dispose();
            LiveSocket s = _socket = new LiveSocket();
            _connectBy = Time.realtimeSinceStartup + ConnectSeconds;
            s.Connected += () =>
            {
                if (s != _socket) return;
                _attempt = 0;
                _everOpen = true;
                Net = Link.Live;
                // The server forgets a dropped socket's rooms, so every connection joins again. GAME_JOINED asks
                // the host for the state (see Received) exactly as the app's own reconnect does.
                s.Emit("JOIN_ROOM", "MP_" + Id);
                Send(new Dictionary<string, object> { { "messageType", "GAME_JOINED" } });
                Resync();
                Changed?.Invoke();
            };
            s.Event += (name, arg) => { if (s == _socket && name == "message" && arg is Dictionary<string, object>) Received(arg); };
            s.Closed += why => { if (s == _socket) Dropped(); };
            s.Connect();
        }

        // The socket closed, never opened, or went silent: wait a little longer each time, then ask the Player.
        void Dropped()
        {
            _socket?.Dispose();
            _socket = null;
            if (Over) return;   // the final screen needs nothing more from the room
            _attempt++;
            if (_attempt > LiveMath.MaxAttempts) Net = Link.Lost;
            else
            {
                Net = _everOpen ? Link.Reconnecting : Link.Connecting;
                _retryAt = Time.realtimeSinceStartup + LiveMath.Backoff(_attempt);
            }
            Changed?.Invoke();
        }

        /// <summary>The Player's Retry, after the tries ran out: start again from the first.</summary>
        public void Retry()
        {
            if (Local || Over) return;
            _attempt = 0;
            Net = _everOpen ? Link.Reconnecting : Link.Connecting;
            Open();
            Changed?.Invoke();
        }

        // The participant's catch-up after a (re)connect: the row is the only record the server side keeps. The
        // host needs none: it is the referee, its own state is the game, and it reads the row for the names on
        // GAME_JOINED as before.
        void Resync()
        {
            if (Local || Host) return;
            App.Run(Api.Get("/multiplayer/game/" + Id, data =>
            {
                object g = J.First(data);
                if (this == null || g == null) return;
                Game = g;
                if (Putts.Count == 0) Putts = TournamentMath.Putts(g);
                object state = LiveMath.State(g);
                if (Newer(state, false)) ReadState(state);
                if (LiveMath.Finished(g) && !Over) { Over = true; WinnerId = J.Str(g, "winnerId"); _pending = null; }
                Resend();
                Changed?.Invoke();
            }));
        }

        void Update()
        {
            _socket?.Pump();
            if (Local || Over) return;
            float now = Time.realtimeSinceStartup;
            if (_socket == null) { if (Net != Link.Lost && now >= _retryAt) Open(); return; }
            if (!_socket.Open) { if (now > _connectBy) Dropped(); return; }
            if (_socket.Silent > SilentSeconds) { Dropped(); return; }
            if (_pending != null && now - _pendingAt > ResendSeconds) Resend();
        }

        // Phones cut sockets in the background without saying so. Back after more than a moment: a new line now,
        // not when the silence is noticed. Back quickly: just read what was missed.
        void OnApplicationPause(bool paused)
        {
            if (Local || Over) return;
            if (paused) { _pausedAt = Time.realtimeSinceStartup; return; }
            if (Net == Link.Lost) return;
            if (_socket != null && _socket.Open && Time.realtimeSinceStartup - _pausedAt < 15f) { Resync(); return; }
            _attempt = 0;
            Net = _everOpen ? Link.Reconnecting : Link.Connecting;
            Open();
            Changed?.Invoke();
        }

        void Send(object evt) => _socket?.Emit("MESSAGE", new Dictionary<string, object> { { "room", "MP_" + Id }, { "event", evt } });

        void Received(object evt)
        {
            switch (J.Str(evt, "messageType"))
            {
                case "GAME_JOINED":
                    // Someone (this phone included: the room echoes) has just joined or come back. The server
                    // keeps nothing to replay, so the host tells the room where the game stands. The app reads
                    // GAME_STATE and GAME_OVER whenever they come, so this is safe against it too.
                    if (!Host) break;
                    App.Run(Api.Get("/multiplayer/game/" + Id, data => { object g = J.First(data); if (this != null && g != null) { Game = g; Changed?.Invoke(); } }));
                    Send(new Dictionary<string, object> { { "messageType", "GAME_STATE" }, { "gameState", StateObject() } });
                    if (Over) Send(new Dictionary<string, object> { { "messageType", "GAME_OVER" }, { "winnerId", WinnerId } });
                    break;
                case "BALL_PATH":
                    // The host's own putt was scored when it was hit (Mine), so its echo is not scored again.
                    if (!Host || J.Int(evt, "player") == Me) break;
                    // A putt from this app says which putt of the game it is. One sent again across a reconnect
                    // after it was already scored is for a putt gone by, and is dropped, so it can never count
                    // twice. (The app sends no number: for it the turn check in Referee is the only guard, as there.)
                    if (J.Get(evt, "puttCounter") != null && J.Int(evt, "puttCounter") != PuttCounter) break;
                    Referee(J.Int(evt, "player"), J.Get(evt, "ballPath"), J.Get(evt, "holeBounds"));
                    break;
                case "GAME_STATE":
                    object state = J.Get(evt, "gameState");
                    if (state is string text) state = J.Parse(text);
                    // The host is where the state comes from: its own echo, arriving late, must not undo a putt.
                    if (Host || !Newer(state, true)) break;
                    ReadState(state);
                    TournamentMath.Dealt n = Now;
                    Said = (CurrentPlayer == Me ? "Your turn: " : Name(CurrentPlayer) + "'s turn: ") + n.Feet + " ft, " + PracticeSession.TeeName(n.Tee) + " tee";
                    // Still this Player's turn on the putt they already hit: the host never got it (it was away).
                    Resend();
                    Changed?.Invoke();
                    break;
                case "GAME_OVER":
                    Over = true;
                    WinnerId = J.Str(evt, "winnerId");
                    _pending = null;
                    Changed?.Invoke();
                    break;
            }
        }

        /// <summary>This Player's putt: to the room (the host judges it, here or on the other phone).</summary>
        public void Mine(PuttResult putt)
        {
            if (!MyTurn) return;
            object path = J.Parse(RoundSaver.PathJson(putt.Path)), hole = J.Parse(RoundSaver.HoleJson(putt.Hole));
            // puttCounter is extra to the app's message (which ignores it): it lets this app's host tell a repeat.
            var ball = new Dictionary<string, object> { { "messageType", "BALL_PATH" }, { "player", Me }, { "ballPath", path }, { "holeBounds", hole }, { "puttCounter", PuttCounter } };
            if (Host || Local)
            {
                // The referee's own putt is scored here, at once. (The app's host waits for the room to echo it
                // back, and loses the putt if the socket drops in between.) The room still gets the path: the
                // app flashes the make or miss from it.
                Send(ball);
                Referee(Me, path, hole);
                return;
            }
            // The participant's putt is kept until the host's next state shows it was scored. If the line drops
            // first it is sent again after the rejoin, as socket.io-client's send buffer does for the app.
            _pending = ball;
            _pendingSlot = PuttCounter;
            _pendingAt = Time.realtimeSinceStartup;
            Send(ball);
            Said = "";
            Changed?.Invoke();
        }

        // Sends the waiting putt again, but only while the game still stands on that same putt and turn, and not
        // within two seconds of the last send (the host may simply not have answered yet).
        void Resend()
        {
            if (_pending == null) return;
            if (Over || CurrentPlayer != Me || PuttCounter != _pendingSlot) { _pending = null; return; }
            float now = Time.realtimeSinceStartup;
            if (_socket == null || !_socket.Open || now - _pendingAt < 2f) return;
            _pendingAt = now;
            Send(_pending);
        }

        // MultiplayerGame receivedSocketMessage, the host's half: score it, save it, move the turn on, tell the room.
        void Referee(int player, object pathValue, object holeValue)
        {
            if (player < 1 || player > 2) return;
            string pathText = pathValue is string ps ? ps : J.Write(pathValue);
            if (Over || player != CurrentPlayer) { Said = "Putt hit out of turn"; Changed?.Invoke(); return; }
            TournamentMath.Dealt putt = Now;
            List<BallPoint> path = PuttrAnalysis.ParsePath(pathText);
            PuttrAnalysis.TryParseHole(holeValue is string hs ? hs : J.Write(holeValue), out Hole hole);
            AppPutt.Verdict v = AppPutt.Judge(AppPutt.Path(path), AppPutt.Hole(hole), putt.Feet, putt.Tee);
            double points = 0;
            if (v.Count > 1)
            {
                double te = AppPutt.TeeEntry(putt.Feet, putt.Tee);
                if (Rules == "centercut") points = Math.Abs(te - v.Entry) >= 90 ? 0 : Math.Round(putt.Feet * (1 - Math.Abs(te - v.Entry) / 90.0), 1);
                else if (v.Entry > te - EntryAngle / 2 && v.Entry < te + EntryAngle / 2) points = putt.Feet;
            }
            string userId = player == 1 ? J.Str(Game, "userId") : J.Str(Game, "participantId");
            // As MultiplayerActions.savePutt: made is the camera's count above 0 (the app passes c and saves made > 0),
            // not the game's verdict (which scores the putt below).
            if (!Fixtures.On)
                App.Run(Api.Post("/multiplayer/savePutt/", new Dictionary<string, object>
                {
                    { "userId", userId }, { "mpGameId", Id }, { "distance", putt.Feet }, { "tee", putt.Tee }, { "made", v.SavedMade ? 1 : 0 },
                    { "ballPath", pathText }, { "holeBounds", holeValue is string h1 ? h1 : J.Write(holeValue) },
                }, null, null));
            // gameLogic
            List<double> mine = PlayerPutts[CurrentPlayer - 1];
            while (mine.Count <= PuttCounter) mine.Add(0);
            mine[PuttCounter] = points;
            if (CurrentPlayer == 1) CurrentPlayer = 2;
            else
            {
                PuttCounter++;
                CurrentPlayer = 1;
                if (PuttCounter >= Putts.Count)
                {
                    double a = Score(1), b = Score(2);
                    if (a != b) { End(a > b ? 1 : 2); }
                    else Overtime = true;
                }
            }
            Said = (points > 0 ? Name(player) + " made it: " + RoundSession.Points(points) : Name(player) + " missed");
            if (!Fixtures.On) App.Run(Api.Post("/multiplayer/game/save", new Dictionary<string, object> { { "gameId", Id }, { "gameState", J.Write(StateObject()) } }, null, null));
            Send(new Dictionary<string, object> { { "messageType", "GAME_STATE" }, { "gameState", StateObject() } });
            Changed?.Invoke();
        }

        void End(int winner)
        {
            Over = true;
            WinnerId = winner == 1 ? J.Str(Game, "userId") : J.Str(Game, "participantId");
            if (!Fixtures.On) App.Run(Api.Post("/multiplayer/game/over", new Dictionary<string, object> { { "gameId", Id }, { "winnerId", WinnerId } }, null, null));
            Send(new Dictionary<string, object> { { "messageType", "GAME_OVER" }, { "winnerId", WinnerId } });
        }

        public void Close()
        {
            _socket?.Dispose();
            _socket = null;
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// A finished live game, as the final screen shows it (GameSummary): who won, both Players' score and make
    /// rate, the sudden-death note, and every putt. Read from a multiplayer_games row, or from a match just played.
    /// </summary>
    public sealed class LiveResult
    {
        public readonly string[] Names = new string[2], Photos = new string[2];
        public List<double>[] Putts = { new List<double>(), new List<double>() };
        public List<TournamentMath.Dealt> Dealt = new List<TournamentMath.Dealt>();
        public bool Overtime, CenterCut;
        public int PuttCounter;
        /// <summary>1 the host, 2 the participant, 0 neither (no winner on the row; the reader was not in the game).</summary>
        public int Winner, Me;

        public static LiveResult From(object game, string uid)
        {
            var r = new LiveResult();
            r.Names[0] = J.Str(game, "hostName", "Host");
            r.Names[1] = J.Str(game, "participantName", "Opponent");
            r.Photos[0] = J.Str(game, "hostPhoto");
            r.Photos[1] = J.Str(game, "participantPhoto");
            string host = J.Str(game, "userId"), guest = J.Str(game, "participantId"), winner = J.Str(game, "winnerId");
            r.Me = string.IsNullOrEmpty(uid) ? 0 : uid == host ? 1 : uid == guest ? 2 : 0;
            r.Winner = winner.Length == 0 ? 0 : winner == host ? 1 : winner == guest ? 2 : 0;
            r.Dealt = TournamentMath.Putts(game);
            object state = LiveMath.State(game);
            if (state != null)
            {
                r.Putts = LiveMath.PlayerPutts(state);
                r.Overtime = J.Flag(state, "isOvertime");
                r.PuttCounter = J.Int(state, "puttCounter");
                r.CenterCut = J.Str(state, "rules") == "centercut";
            }
            return r;
        }

        public string Banner => Winner == 0 ? "Final" : Me == 0 ? Names[Winner - 1] + " won" : Winner == Me ? "You won" : "You lost";

        /// <summary>The match on the shared game over page: who won, both scores, then putt by putt as the second column.</summary>
        public GameOverSpec Spec(Action exit)
        {
            VisualElement view = View(false, out VisualElement table);
            return new GameOverSpec { Eyebrow = "Final   ·   Live game", Outcome = Banner, Extra = view, Detail = table, DetailTitle = table != null ? "Putt by putt" : null, Exit = exit, RateAs = "Live game" };
        }

        /// <summary>The whole final screen but its buttons.</summary>
        public VisualElement View() => View(true, out _);

        /// <param name="whole">With the banner on top and the putt table under it; false for the shared game over page, which
        /// shows the banner itself and the table (handed back) as its own column.</param>
        VisualElement View(bool whole, out VisualElement puttTable)
        {
            puttTable = null;
            VisualElement root = new VisualElement();
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Live");
            if (sheet != null) root.styleSheets.Add(sheet);

            if (whole)
            {
                Label banner = Ui.Title(Banner, "title center");
                if (Winner != 0 && Me != 0) banner.style.color = Winner == Me ? Ui.Bright : Ui.Red;
                root.Add(banner);
            }

            VisualElement Side(int p)
            {
                Label name = Ui.Text(Names[p - 1], "vs-name vs-name-mid", Ui.Bold);
                if (Winner == p) name.style.color = Ui.Bright;
                int made = LiveMath.MakePercent(Putts[p - 1]);
                return Ui.Box("vs-half center-box", Ui.Avatar(Names[p - 1], Photos[p - 1]), name,
                    Ui.Num(RoundSession.Points(LiveMath.Score(Putts[p - 1])), "num-44 center"),
                    Ui.Caps(made < 0 ? "–" : made + "% made", "caps center"));
            }
            root.Add(Ui.Box("sheet-card row", Side(1), Ui.Caps("vs", "caps"), Side(2)).Margin(12));

            int extra = LiveMath.SuddenDeathPutts(PuttCounter, Dealt.Count, Overtime);
            if (extra > 0) root.Add(Ui.Caps("Decided in sudden death: " + extra + (extra == 1 ? " extra putt" : " extra putts"), "live-final-note"));

            // Putt by putt: the host's result under the host, the putt in the middle, the participant's on the right.
            // Sudden-death putts go round the same list again, so they follow it here.
            int rows = Dealt.Count == 0 ? 0 : Math.Max(Dealt.Count, Math.Max(Putts[0].Count, Putts[1].Count));
            if (rows == 0) return root;
            VisualElement table = Ui.Box("live-table");
            for (int i = 0; i < rows; i++)
            {
                TournamentMath.Dealt putt = Dealt[i % Dealt.Count];
                VisualElement middle = Ui.Box("live-putt", Ui.Text(putt.Feet + " ft", "vs-name vs-name-mid", Ui.Bold), Ui.Text(PracticeSession.TeeName(putt.Tee) + " tee", "muted center", Ui.Regular));
                if (i >= Dealt.Count) middle.Insert(0, Ui.Text("SUDDEN DEATH", "live-extra", Ui.Bold));
                table.Add(Ui.Box(i == 0 ? "live-row live-row-first" : "live-row", Mark(1, i), middle, Mark(2, i)));
            }
            if (whole) root.Add(table);
            else { if (sheet != null) table.styleSheets.Add(sheet); puttTable = table; }
            return root;
        }

        // A tick for a putt that scored, a cross for one that did not (a miss, or a make outside the window).
        // Center cut scores by how central the make was, so its points sit under the tick.
        VisualElement Mark(int player, int index)
        {
            List<double> putts = Putts[player - 1];
            if (index >= putts.Count) return Ui.Box("live-mark", Ui.Text("–", "muted center", Ui.Regular));
            double points = putts[index];
            if (points == 0) return Ui.Box("live-mark", Ui.Icon("X", 20, Ui.Red));
            VisualElement mark = Ui.Box("live-mark", Ui.Icon("Check", 20, Ui.Bright));
            if (CenterCut) mark.Add(Ui.Text(RoundSession.Points(points), "muted center", Ui.Medium));
            return mark;
        }
    }

    /// <summary>A finished live game opened from the Live games list: the final screen, to read. Nothing to leave or forfeit.</summary>
    public sealed class LiveResultScreen : AppScreen
    {
        public LiveResultScreen(App app, object game)
        {
            Root = GameOver.Build(app, LiveResult.From(game, Auth.Uid).Spec(app.Pop));
        }
    }

    /// <summary>Beside the 3D ramp during a live game: both Players' scores, whose turn, the putt, what just happened. Over the whole screen once it ends.</summary>
    public sealed class LivePanel
    {
        public readonly VisualElement Root;
        readonly LiveMatch _match;
        readonly VisualElement _sheet, _content;
        readonly LiveVideo _video;
        readonly Action _leave;
        bool _final;

        public LivePanel(LiveMatch match, Action leave)
        {
            _match = match;
            _leave = leave;
            Root = Ui.Box("frame");
            Root.pickingMode = PickingMode.Ignore;
            _sheet = Ui.Box("sheet live-sheet");
            Root.Add(_sheet);
            // The opponent's video above the scores (GameLobby), kept while the scores redraw.
            _video = new LiveVideo(match.Id);
            _content = new VisualElement();
            _sheet.Add(_video.Root);
            _sheet.Add(_content);
            match.Changed += Show;
            Root.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Show();
        }

        void Place()
        {
            if (_final)
            {
                // The final screen takes the whole screen, clear of the notch and the home bar.
                Vector4 safe = App.I != null ? App.I.SafeInsets() : Vector4.zero;   // left, bottom, right, top
                _sheet.RemoveFromClassList("sheet-wide");
                _sheet.style.left = 0; _sheet.style.top = 0; _sheet.style.right = 0; _sheet.style.bottom = 0;
                _sheet.style.paddingLeft = safe.x + 12; _sheet.style.paddingBottom = safe.y + 12; _sheet.style.paddingRight = safe.z + 12; _sheet.style.paddingTop = safe.w + 12;
                return;
            }
            bool wide = Screen.width > Screen.height;
            float picture = wide ? FreePuttMode.WideShare : FreePuttMode.UprightShare;
            _sheet.EnableInClassList("sheet-wide", wide);
            _sheet.style.left = wide ? Length.Percent(picture * 100f) : 0;
            _sheet.style.top = wide ? 0 : Length.Percent(picture * 100f);
            _sheet.style.right = 0;
            _sheet.style.bottom = 0;
        }

        public void Show()
        {
            Perf.Wake(2f);
            LiveMatch m = _match;
            _content.Clear();
            if (m.Over) { Final(); return; }
            VisualElement Side(int p)
            {
                Label name = Ui.Text(m.Name(p), "vs-name vs-name-mid", Ui.Bold);
                if (m.CurrentPlayer == p) name.style.color = Ui.Bright;
                return Ui.Box("vs-half center-box", Ui.Avatar(m.Name(p), m.Photo(p)), name, Ui.Num(RoundSession.Points(m.Score(p)), "score-mid"));
            }
            TournamentMath.Dealt now = m.Now;
            string middle = m.Overtime ? "SUDDEN DEATH" : "PUTT " + (Math.Min(m.PuttCounter, Math.Max(0, m.Putts.Count - 1)) + 1) + " OF " + Math.Max(1, m.Putts.Count);
            _content.Add(Ui.Box("sheet-card row", Side(1), Ui.Box("center-box", Ui.Caps(middle, "caps caps-green"), Ui.Num(now.Feet + " ft", "num-20"), Ui.Text(PracticeSession.TeeName(now.Tee) + " tee", "muted", Ui.Regular),
                Ui.Text(m.Rules == "centercut" ? "Center cut" : Mathf.RoundToInt((float)m.EntryAngle) + "°", "muted", Ui.Regular)), Side(2)));
            if (m.Net == LiveMatch.Link.Lost)
            {
                // Only after the quiet tries ran out: the Player chooses.
                _content.Add(Ui.Box("sheet-card", Ui.Title("Connection lost", "title-20"), Ui.Text("We could not get back to the match. Check your connection, then try again.", "body-2", Ui.Regular).Margin(4),
                    Ui.Button("Retry", m.Retry), Ui.Secondary("Leave", _leave).Margin(12)));
                return;
            }
            string turn, said = m.Said ?? "";
            if (m.Net == LiveMatch.Link.Connecting) { turn = "Connecting..."; said = ""; }
            else if (m.Net == LiveMatch.Link.Reconnecting) { turn = "Reconnecting..."; said = "Putting is paused until you are back in the match."; }
            else if (m.MyTurn) turn = "Your turn. Putt when you are ready.";
            else if (m.Waiting) turn = "Scoring your putt...";
            else turn = "Waiting on " + m.Name(m.CurrentPlayer) + ".";
            // Leaving a match is pause, then Exit, as in every game (only a lost connection offers Leave, above).
            _content.Add(Ui.Box("sheet-card", Ui.Text(turn, "body", Ui.Bold), Ui.Text(said, "body-2", Ui.Regular)));
        }

        // The match is over: the video call ends (its native views would sit over this), the panel takes the
        // whole screen, and Done leaves the game (GameBridge.OnExit, which goes back to the tab: App.ExitToTab).
        void Final()
        {
            if (!_final)
            {
                _final = true;
                _video.End();
                _content.style.flexGrow = 1;
                _content.style.flexShrink = 1;
                Place();
            }
            // The same game over page as every game, over the whole screen; Exit leaves the match.
            VisualElement page = GameOver.Build(App.I, _match.Result().Spec(_leave));
            page.style.flexGrow = 1;
            _content.Add(page);
        }
    }

    public sealed class LiveLobbyScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;

        public LiveLobbyScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Live games", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown()
        {
            if (Kid.Blocked(_app, Kid.Online, _body)) return;
            if (!Club.Member(_app.Profile)) { _body.Clear(); _body.Add(Page.Note("Live games are part of Puttr Club.")); _body.Add(Ui.Box("pad", Ui.Button("See Puttr Club", () => _app.Push(new ClubScreen(_app, "Live games"))))); return; }
            object stats = null; List<object> active = null, done = null;
            void Then() { if (stats != null && active != null && done != null) Draw(stats, active, done); }
            App.Run(Api.Get("/multiplayer/stats/", d => { stats = J.First(d) ?? new Dictionary<string, object>(); Then(); }, _ => { stats = new Dictionary<string, object>(); Then(); }));
            App.Run(Api.Get("/multiplayer/active", d => { active = Fresh(J.List(d)); Then(); }, _ => { active = new List<object>(); Then(); }));
            App.Run(Api.Get("/multiplayer/completed", d => { done = J.List(d); Then(); }, _ => { done = new List<object>(); Then(); }));
        }

        // Rows untouched for a week are dropped, as the app does.
        static List<object> Fresh(List<object> rows) => rows.FindAll(r => (ProfileMath.Date(r, "updated_at") ?? ProfileMath.Date(r, "created_at") ?? DateTime.UtcNow) > DateTime.UtcNow.AddDays(-7));

        void Draw(object stats, List<object> active, List<object> done)
        {
            _body.Clear();
            _body.Add(Ui.Box("pad", Ui.Text("Real-time 1v1 matches against friends.", "sub", Ui.Regular)));
            int total = J.Int(stats, "total"), wins = J.Int(stats, "wins");
            _body.Add(Ui.Box("pad", Ui.StatStrip((wins.ToString(), "Wins"), ((total - wins).ToString(), "Losses"), (total > 0 ? Mathf.RoundToInt(wins * 100f / total) + "%" : "–", "Win rate"))).Margin(12));
            _body.Add(Ui.Box("pad", Ui.Button("New live game", () => _app.Push(new LiveSetupScreen(_app)))).Margin(12));
            Section("Active", active, "No active games. Start one above.");
            Section("Completed", done, "Finished matches show up here.");
        }

        void Section(string title, List<object> rows, string empty)
        {
            _body.Add(Ui.Section(title + "   ·   " + rows.Count));
            if (rows.Count == 0) { _body.Add(Page.Note(empty)); return; }
            VisualElement cols = Ui.Box("cols pad");
            foreach (object r in rows) cols.Add(Ui.Box("col", Card(r)));
            _body.Add(cols);
        }

        VisualElement Card(object r)
        {
            bool done = J.Str(r, "winnerId").Length > 0;
            bool host = J.Str(r, "userId") == Auth.Uid;
            string badge = done ? (J.Str(r, "winnerId") == Auth.Uid ? "WON" : "LOST") : J.Str(r, "status") == "1" ? "LIVE" : J.Str(r, "participantId") == "0" ? "WAITING" : "OPEN";
            VisualElement Who(string n, string photo) => Ui.Box("vs-half center-box", Ui.Avatar(n, photo), Ui.Text(n, "vs-name vs-name-mid", Ui.Bold));
            string range = J.Str(r, "distanceRange").Replace(",", "–");
            // The status is a label whose colour is its meaning (won green, live and lost red); the game's three facts
            // are one line of tags joined with dots, as on a game tile.
            VisualElement card = Ui.Box("card match-card",
                Ui.Box("row", Ui.Caps(badge, badge == "WON" ? "caps caps-green" : badge == "LIVE" || badge == "LOST" ? "caps live-red" : "caps"), Ui.Box("grow"), Ui.Text(Ago.Words(ProfileMath.Date(r, "created_at"), DateTime.UtcNow), "muted", Ui.Regular)),
                Ui.Box("row", Who(J.Str(r, "hostName", "Host"), J.Str(r, "hostPhoto")), Ui.Caps("vs", "caps"), Who(J.Str(r, "participantName", "Waiting..."), J.Str(r, "participantPhoto"))).Margin(12),
                Ui.Caps(J.Int(r, "numPutts") + " putts  ·  " + range + " ft  ·  " + (J.Int(r, "entryAngle") >= 180 ? "Center cut" : J.Int(r, "entryAngle") + "° entry"), "caps center").Margin(12));
            object row = r;
            // A finished game opens its final screen to read: it is not a match to join, so nothing asks about leaving.
            if (done) card.OnTap(() => _app.Push(new LiveResultScreen(_app, row)));
            else card.OnTap(() => _app.Games.PlayLive(row));
            if (!done && host && J.Str(r, "status") == "0")
                card.Add(Ui.Button("Delete", () => Dialog.Ask(Root, "Delete this game?", "It is gone for both of you.", "Delete",
                    () => App.Run(Api.Post("/multiplayer/game/delete", new Dictionary<string, object> { { "gameId", J.Str(row, "id") } }, _ => Shown(), _ => Shown())), true), "btn btn-red vs-small"));
            return card;
        }
    }

    /// <summary>GameSetup: who, how many putts, from how far, and the scoring.</summary>
    public sealed class LiveSetupScreen : AppScreen
    {
        static readonly int[] Counts = { 9, 18, 27, 36 };
        static readonly (string name, int min, int max)[] Ranges = { ("3–11 ft", 3, 11), ("3–6 ft", 3, 6), ("6–9 ft", 6, 9), ("8–11 ft", 8, 11) };
        readonly App _app;
        readonly VisualElement _friends;
        string _friend, _friendName, _friendPhoto;
        int _count, _range, _rules, _angle = 2;
        static readonly int[] Angles = { 30, 60, 90, 180 };
        bool _busy;

        public LiveSetupScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "New live game", out VisualElement body);
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Live");
            if (sheet != null) Root.styleSheets.Add(sheet);
            _friends = Ui.Box("pad");
            body.Add(Ui.Section("Opponent"));
            body.Add(_friends);
            body.Add(Ui.Section("Putts"));
            body.Add(Ui.Box("pad", Pick(new[] { "9", "18", "27", "36" }, _count, i => _count = i)));
            body.Add(Ui.Section("Distance"));
            body.Add(Ui.Box("pad", Pick(new[] { Ranges[0].name, Ranges[1].name, Ranges[2].name, Ranges[3].name }, _range, i => _range = i)));
            body.Add(Ui.Section("Scoring"));
            VisualElement angles = Pick(new[] { "30°", "60°", "90°", "180°" }, _angle, i => _angle = i).Margin(8);
            Label words = Ui.Text(RulesWords(), "muted", Ui.Regular).Margin(8);
            body.Add(Ui.Box("pad", Pick(new[] { "Entry angle", "Center cut" }, _rules, i =>
                {
                    _rules = i;
                    words.text = RulesWords();
                    angles.style.display = i == 1 ? DisplayStyle.None : DisplayStyle.Flex; // center cut has no window
                }), words, angles));
            body.Add(Ui.Box("pad", Ui.Button("Start game", Start)).Margin(16));
            _friends.Add(Spinner.Small());
            App.Run(Api.Get("/profile/following/", d => Friends(J.List(d))));
        }

        string RulesWords() => _rules == 1
            ? "Center cut: a make scores its distance, less the share it comes in off line."
            : "Entry angle: a make that comes in inside the window scores its distance.";

        /// <summary>A row of options where the tapped one lights up (FreePuttScreen.Choice leaves the redraw to its caller).</summary>
        static VisualElement Pick(string[] names, int chosen, Action<int> pick)
        {
            VisualElement row = Ui.Box("choice");
            var items = new List<VisualElement>();
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                VisualElement item = Ui.Box(i == chosen ? "choice-item choice-on" : "choice-item", Ui.Text(names[i], "choice-text", Ui.Bold));
                item.OnTap(() =>
                {
                    for (int k = 0; k < items.Count; k++) items[k].EnableInClassList("choice-on", k == index);
                    pick(index);
                });
                items.Add(item);
                row.Add(item);
            }
            return row;
        }

        void Friends(List<object> people)
        {
            _friends.Clear();
            if (people.Count == 0) { _friends.Add(Ui.Text("Follow Players to play them live.", "sub", Ui.Regular)); return; }
            ScrollView row = Ui.Scroll(true);
            row.AddToClassList("live-friends");
            var rings = new Dictionary<string, VisualElement>();
            foreach (object p in people)
            {
                string id = J.Str(p, "userId"), name = J.Str(p, "displayName", "A Player");
                // Selected = a green ring round the avatar, nothing else.
                VisualElement ring = Ui.Box(id == _friend ? "live-ring live-ring-on" : "live-ring", Ui.Avatar(name, J.Str(p, "photoURL")));
                rings[id] = ring;
                row.Add(Ui.Box("live-friend", ring, Ui.Text(name, "live-friend-name", Ui.Medium)).OnTap(() =>
                {
                    _friend = id; _friendName = name; _friendPhoto = J.Str(p, "photoURL");
                    foreach (KeyValuePair<string, VisualElement> r in rings) r.Value.EnableInClassList("live-ring-on", r.Key == id);
                }));
            }
            _friends.Add(row);
        }

        void Start()
        {
            if (string.IsNullOrEmpty(_friend)) { _app.Toast("Pick your opponent."); return; }
            if (_busy) return;
            _busy = true;
            var dice = new System.Random();
            var putts = new List<object>();
            (string _, int min, int max) = Ranges[_range];
            for (int i = 0; i < Counts[_count]; i++) putts.Add(new Dictionary<string, object> { { "id", dice.Next(10000000) }, { "distance", dice.Next(min, max + 1) }, { "tee", dice.Next(1, 4) } });
            string rules = _rules == 1 ? "centercut" : "entry";
            var state = new Dictionary<string, object> { { "currentPlayer", 1 }, { "playerPutts", new List<object> { new List<object>(), new List<object>() } }, { "puttCounter", 0 }, { "isOvertime", false }, { "rules", rules } };
            var body = new Dictionary<string, object>
            {
                { "participantId", _friend }, { "numPutts", Counts[_count] }, { "distanceRange", new List<object> { min, max } },
                { "entryAngle", _rules == 1 ? 180 : Angles[_angle] }, { "puttData", J.Write(putts) }, { "gameState", J.Write(state) },
            };
            App.Run(Api.Post("/multiplayer/game/create", body, data =>
            {
                _busy = false;
                object game = J.First(data) ?? data;
                // Under -app2HoldPlay the game is not written (Api.Held) and the answer is only a local id: the row is
                // filled in here from what was asked for, so the host can still play it through on this phone.
                if (game is Dictionary<string, object> row && J.Str(game, "userId").Length == 0)
                {
                    // Numbers go in as doubles, as parsed JSON has them (J reads no other kind).
                    foreach (KeyValuePair<string, object> field in body) if (!row.ContainsKey(field.Key)) row[field.Key] = field.Value is int whole ? (double)whole : field.Value;
                    row["userId"] = Auth.Uid;
                    row["status"] = "0";
                    row["hostName"] = J.Str(_app.Profile, "displayName", "You");
                    row["hostPhoto"] = J.Str(_app.Profile, "photoURL");
                    row["participantName"] = _friendName ?? "Opponent";
                    row["participantPhoto"] = _friendPhoto ?? "";
                }
                _app.Pop();
                _app.Games.PlayLive(game);
            }, _ => { _busy = false; _app.Toast("Couldn't start the game. Try again."); }));
        }
    }

    /// <summary>Home's "Live now": active live games.</summary>
    public static class LiveCard
    {
        public static void Fill(App app, VisualElement slot)
        {
            if (Kid.IsKid(app.Profile) || !Club.Member(app.Profile)) return;
            App.Run(Api.Get("/multiplayer/active", data =>
            {
                slot.Clear();
                List<object> rows = J.List(data).FindAll(r => (ProfileMath.Date(r, "updated_at") ?? ProfileMath.Date(r, "created_at") ?? DateTime.UtcNow) > DateTime.UtcNow.AddDays(-7));
                if (rows.Count == 0) return;
                slot.Add(Ui.Section("Live now", "See all", () => app.Push(new LiveLobbyScreen(app))));
                ScrollView strip = Ui.Scroll(true);
                for (int i = 0; i < rows.Count; i++)
                {
                    object r = rows[i];
                    bool host = J.Str(r, "userId") == Auth.Uid;
                    string name = J.Str(r, host ? "participantName" : "hostName", "Waiting for opponent");
                    VisualElement card = Ui.Box("card versus-card live-card", Ui.Box("row", Ui.Avatar(name, J.Str(r, host ? "participantPhoto" : "hostPhoto")),
                        Ui.Box("vs-words", Ui.Text(name, "vs-name", Ui.Bold), Ui.Text("Started " + Ago.Words(ProfileMath.Date(r, "created_at"), DateTime.UtcNow), "muted", Ui.Regular)), Ui.Caps("Live", "caps live-red")));
                    card.OnTap(() => app.Games.PlayLive(r));
                    if (i == 0) card.style.marginLeft = 16;
                    strip.Add(card);
                }
                slot.Add(strip);
            }));
        }
    }
}
