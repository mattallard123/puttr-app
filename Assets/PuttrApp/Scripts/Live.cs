// Live 1v1 (puttr-app MultiplayerScreen, multiplayer/MultiplayerGame): two Players on their own Puttrs,
// in turns, in real time. The host (the Player who made the game) is the referee, exactly as the app:
// both send each ball path to the room, the host scores it and sends the new state. So a Player here
// and a Player on the current app can play each other. Puttr Club only; off for Kid profiles.
//
//   socket.io  wss://puttr-multiplayer.wn.r.appspot.com   JOIN_ROOM "MP_<id>", MESSAGE {room, event}
//              events: GAME_JOINED, BALL_PATH {player 1|2, ballPath, holeBounds}, GAME_STATE {gameState}, GAME_OVER {winnerId}
//   POST /multiplayer/game/create {participantId, numPutts, distanceRange, entryAngle, puttData, gameState}
//   GET  /multiplayer/game/:id, /multiplayer/active, /multiplayer/completed, /multiplayer/stats/
//   POST /multiplayer/savePutt/, /multiplayer/game/save, /multiplayer/game/over {gameId, winnerId}, /multiplayer/game/delete
//
// Not here yet: the opponent's video (the app shows it full screen behind the score).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        public event Action Connected;
        public event Action<string, object> Event;
        public event Action<string> Closed;

        public void Connect()
        {
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;
            Task.Run(() => Run(ct));
        }

        public void Emit(string name, object arg) => _out.Enqueue("42" + J.Write(new List<object> { name, arg }));

        public void Pump() { while (_main.TryDequeue(out Action a)) a(); }

        public void Dispose() { try { _cts?.Cancel(); } catch (ObjectDisposedException) { } Open = false; }

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
    }

    /// <summary>One live game: its row, its state, the socket, and the referee (when this Player is host).</summary>
    public sealed class LiveMatch : MonoBehaviour
    {
        public object Game;                 // the multiplayer_games row
        public List<TournamentMath.Dealt> Putts = new List<TournamentMath.Dealt>();
        public int CurrentPlayer = 1, PuttCounter;
        public List<double>[] PlayerPutts = { new List<double>(), new List<double>() };
        public bool Overtime, Over;
        public string Rules = "entry", WinnerId;
        public double EntryAngle = 180;
        public string Said;                 // the last thing that happened, for the panel
        public event Action Changed;
        LiveSocket _socket;

        public string Id => J.Str(Game, "id");
        public bool Host => J.Str(Game, "userId") == Auth.Uid;
        public int Me => Host ? 1 : 2;
        public bool MyTurn => !Over && CurrentPlayer == Me && (_socket != null && _socket.Open || Fixtures.On);
        public TournamentMath.Dealt Now => Putts.Count == 0 ? new TournamentMath.Dealt { Feet = 6, Tee = 2 } : Putts[(Overtime ? PuttCounter % Putts.Count : Math.Min(PuttCounter, Putts.Count - 1))];
        public double Score(int player) { double s = 0; foreach (double v in PlayerPutts[player - 1]) s += v; return Math.Round(s, 1); }
        public string Name(int player) => J.Str(Game, player == 1 ? "hostName" : "participantName", player == 1 ? "Host" : "Opponent");
        public string Photo(int player) => J.Str(Game, player == 1 ? "hostPhoto" : "participantPhoto");

        public static LiveMatch Start(object game)
        {
            var go = new GameObject("Live match");
            DontDestroyOnLoad(go);
            LiveMatch m = go.AddComponent<LiveMatch>();
            m.Load(game);
            m.Connect();
            return m;
        }

        public void Load(object game)
        {
            Game = game;
            Putts = TournamentMath.Putts(game);
            EntryAngle = J.Num(game, "entryAngle", 180);
            object state = J.Get(game, "gameState");
            if (state is string s) state = J.Parse(s);
            ReadState(state);
        }

        void ReadState(object state)
        {
            if (state == null) return;
            CurrentPlayer = J.Int(state, "currentPlayer", 1);
            PuttCounter = J.Int(state, "puttCounter");
            Overtime = J.Flag(state, "isOvertime");
            Rules = J.Str(state, "rules", Rules);
            List<object> pp = J.List(J.Get(state, "playerPutts"));
            for (int p = 0; p < 2; p++)
            {
                PlayerPutts[p] = new List<double>();
                if (p < pp.Count) foreach (object v in J.List(pp[p])) PlayerPutts[p].Add(v == null ? 0 : PuttrJson.Number(v, 0));
            }
            Changed?.Invoke();
        }

        object StateObject() => new Dictionary<string, object>
        {
            { "currentPlayer", CurrentPlayer }, { "puttCounter", PuttCounter }, { "isOvertime", Overtime }, { "rules", Rules },
            { "playerPutts", new List<object> { new List<double>(PlayerPutts[0]), new List<double>(PlayerPutts[1]) } },
        };

        void Connect()
        {
            if (Fixtures.On) return;
            _socket = new LiveSocket();
            _socket.Connected += () =>
            {
                _socket.Emit("JOIN_ROOM", "MP_" + Id);
                Send(new Dictionary<string, object> { { "messageType", "GAME_JOINED" } });
                Changed?.Invoke();
            };
            _socket.Event += (name, arg) => { if (name == "message" && arg is Dictionary<string, object>) Received(arg); };
            _socket.Closed += why => { Said = "Connection lost. Leave and open the game again."; Changed?.Invoke(); };
            _socket.Connect();
        }

        void Update() => _socket?.Pump();

        void Send(object evt) => _socket?.Emit("MESSAGE", new Dictionary<string, object> { { "room", "MP_" + Id }, { "event", evt } });

        void Received(object evt)
        {
            switch (J.Str(evt, "messageType"))
            {
                case "GAME_JOINED":
                    if (Host) App.Run(Api.Get("/multiplayer/game/" + Id, data => { object g = J.First(data); if (g != null) { Game = g; Changed?.Invoke(); } }));
                    break;
                case "BALL_PATH":
                    if (Host) Referee(J.Int(evt, "player"), J.Get(evt, "ballPath"), J.Get(evt, "holeBounds"));
                    break;
                case "GAME_STATE":
                    ReadState(J.Get(evt, "gameState"));
                    TournamentMath.Dealt n = Now;
                    Said = (CurrentPlayer == Me ? "Your turn: " : Name(CurrentPlayer) + "'s turn: ") + n.Feet + " ft, " + PracticeSession.TeeName(n.Tee) + " tee";
                    Changed?.Invoke();
                    break;
                case "GAME_OVER":
                    Over = true;
                    WinnerId = J.Str(evt, "winnerId");
                    Changed?.Invoke();
                    break;
            }
        }

        /// <summary>This Player's putt: to the room (the host judges it, here or on the other phone).</summary>
        public void Mine(PuttResult putt)
        {
            if (!MyTurn) return;
            object path = J.Parse(RoundSaver.PathJson(putt.Path)), hole = J.Parse(RoundSaver.HoleJson(putt.Hole));
            if (Fixtures.On) { Referee(Me, path, hole); return; }
            Send(new Dictionary<string, object> { { "messageType", "BALL_PATH" }, { "player", Me }, { "ballPath", path }, { "holeBounds", hole } });
        }

        // MultiplayerGame receivedSocketMessage, the host's half: score it, save it, move the turn on, tell the room.
        void Referee(int player, object pathValue, object holeValue)
        {
            if (Over || player != CurrentPlayer) { Said = "Putt hit out of turn"; Changed?.Invoke(); return; }
            TournamentMath.Dealt putt = Now;
            List<BallPoint> path = PuttrAnalysis.ParsePath(pathValue is string ps ? ps : J.Write(pathValue));
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
            if (!Fixtures.On)
                App.Run(Api.Post("/multiplayer/savePutt/", new Dictionary<string, object>
                {
                    { "userId", userId }, { "mpGameId", Id }, { "distance", putt.Feet }, { "tee", putt.Tee }, { "made", v.Count > 1 ? 1 : 0 },
                    { "ballPath", pathValue is string p1 ? p1 : J.Write(pathValue) }, { "holeBounds", holeValue is string h1 ? h1 : J.Write(holeValue) },
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
            Destroy(gameObject);
        }
    }

    /// <summary>Beside the 3D ramp during a live game: both Players' scores, whose turn, the putt, what just happened.</summary>
    public sealed class LivePanel
    {
        public readonly VisualElement Root;
        readonly LiveMatch _match;
        readonly VisualElement _sheet, _content;
        readonly LiveVideo _video;

        public LivePanel(LiveMatch match, Action leave)
        {
            _match = match;
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
            _leave = leave;
        }

        readonly Action _leave;

        void Place()
        {
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
            VisualElement Side(int p)
            {
                Label name = Ui.Text(m.Name(p), "feed-name center", Ui.Bold);
                if (!m.Over && m.CurrentPlayer == p) name.style.color = Ui.Bright;
                return Ui.Box("grow center-box", Ui.Avatar(m.Name(p), m.Photo(p)), name, Ui.Text(RoundSession.Points(m.Score(p)), "score-mid", Ui.Bold));
            }
            TournamentMath.Dealt now = m.Now;
            string middle = m.Over ? "FINAL" : (m.Overtime ? "SUDDEN DEATH" : "PUTT " + (Math.Min(m.PuttCounter, m.Putts.Count - 1) + 1) + " OF " + m.Putts.Count);
            _content.Add(Ui.Box("sheet-card row", Side(1), Ui.Box("center-box", Ui.Text(middle, "kick", Ui.Bold), Ui.Text(now.Feet + " ft", "h2", Ui.Bold), Ui.Text(PracticeSession.TeeName(now.Tee) + " tee", "muted", Ui.Regular),
                Ui.Text(m.Rules == "centercut" ? "Center cut" : Mathf.RoundToInt((float)m.EntryAngle) + "°", "muted", Ui.Regular)), Side(2)));
            string turn = m.Over ? (m.WinnerId == Auth.Uid ? "You won!" : "You lost.") : m.MyTurn ? "Your turn. Putt when you are ready." : m.CurrentPlayer == m.Me ? "Connecting..." : "Waiting on " + m.Name(m.CurrentPlayer) + ".";
            _content.Add(Ui.Box("sheet-card", Ui.Text(turn, "h2", Ui.Bold), Ui.Text(m.Said ?? "", "sub", Ui.Regular)));
            _content.Add(Ui.Box("sheet-card", Ui.Button(m.Over ? "Done" : "Leave", _leave, "btn btn-ghost")));
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
            _body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Box("row", FreePuttScreen.Stat(wins.ToString(), "Wins"), FreePuttScreen.Stat((total - wins).ToString(), "Losses"), FreePuttScreen.Stat(total > 0 ? Mathf.RoundToInt(wins * 100f / total) + "%" : "–", "Win rate")))));
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
            VisualElement Who(string n, string photo) => Ui.Box("grow center-box", Ui.Avatar(n, photo), Ui.Text(n, "feed-name center", Ui.Bold));
            string range = J.Str(r, "distanceRange").Replace(",", "–");
            VisualElement card = Ui.Box("glass match-card",
                Ui.Box("row", Ui.Text(badge, badge == "WON" ? "chip chip-green" : badge == "LIVE" || badge == "LOST" ? "chip chip-red" : "chip chip-amber", Ui.Bold), Ui.Box("grow"), Ui.Text(Ago.Words(ProfileMath.Date(r, "created_at"), DateTime.UtcNow), "muted", Ui.Regular)),
                Ui.Box("row", Who(J.Str(r, "hostName", "Host"), J.Str(r, "hostPhoto")), Ui.Text("vs", "muted", Ui.Bold), Who(J.Str(r, "participantName", "Waiting..."), J.Str(r, "participantPhoto"))).Margin(8),
                Ui.Box("chips", Ui.Text(J.Int(r, "numPutts") + " putts", "chip"), Ui.Text(range + " ft", "chip"), Ui.Text(J.Int(r, "entryAngle") >= 180 ? "Center cut" : J.Int(r, "entryAngle") + "° entry", "chip")));
            object row = r;
            if (!done) card.OnTap(() => _app.Games.PlayLive(row));
            if (!done && host && J.Str(r, "status") == "0")
                card.Add(Ui.Box("btn-small", Ui.Text("Delete", "btn-text", Ui.Bold)).Margin(8).OnTap(() => Dialog.Ask(Root, "Delete this game?", "It is gone for both of you.", "Delete",
                    () => App.Run(Api.Post("/multiplayer/game/delete", new Dictionary<string, object> { { "gameId", J.Str(row, "id") } }, _ => Shown(), _ => Shown())), true)));
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
        string _friend;
        int _count, _range, _rules, _angle = 2;
        static readonly int[] Angles = { 30, 60, 90, 180 };
        bool _busy;

        public LiveSetupScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "New live game", out VisualElement body);
            _friends = Ui.Box("pad");
            body.Add(Ui.Section("Opponent"));
            body.Add(_friends);
            body.Add(Ui.Section("Putts"));
            body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "9", "18", "27", "36" }, 0, i => _count = i)));
            body.Add(Ui.Section("Distance"));
            body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { Ranges[0].name, Ranges[1].name, Ranges[2].name, Ranges[3].name }, 0, i => _range = i)));
            body.Add(Ui.Section("Scoring"));
            body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "Entry angle", "Center cut" }, 0, i => _rules = i),
                Ui.Text("Entry angle: a make inside the window scores its distance. Center cut: the distance, less the share it comes in off line.", "muted", Ui.Regular).Margin(8),
                FreePuttScreen.Choice(new[] { "30°", "60°", "90°", "180°" }, 2, i => _angle = i).Margin(8)));
            body.Add(Ui.Box("pad", Ui.Button("Start game", Start)).Margin(14));
            _friends.Add(Spinner.Small());
            App.Run(Api.Get("/profile/following/", d => Friends(J.List(d))));
        }

        void Friends(List<object> people)
        {
            _friends.Clear();
            if (people.Count == 0) { _friends.Add(Ui.Text("Follow Players to play them live.", "sub", Ui.Regular)); return; }
            ScrollView row = Ui.Scroll(true);
            foreach (object p in people)
            {
                string id = J.Str(p, "userId"), name = J.Str(p, "displayName", "A Player");
                row.Add(Ui.Box(id == _friend ? "friend friend-on" : "friend", Ui.Avatar(name, J.Str(p, "photoURL"), true), Ui.Text(name, "muted center", Ui.Medium)).OnTap(() => { _friend = id; Friends(people); }));
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
                    VisualElement card = Ui.Box("glass versus-card live-card", Ui.Box("row", Ui.Avatar(name, J.Str(r, host ? "participantPhoto" : "hostPhoto")),
                        Ui.Box("grow set-words", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text("Started " + Ago.Words(ProfileMath.Date(r, "created_at"), DateTime.UtcNow), "muted", Ui.Regular)), Ui.Text("LIVE", "chip chip-red", Ui.Bold)));
                    card.OnTap(() => app.Games.PlayLive(r));
                    if (i == 0) card.style.marginLeft = 16;
                    strip.Add(card);
                }
                slot.Add(strip);
            }));
        }
    }
}
