// A minimal Socket.IO client for receiving events, with no dependencies.
//
// Unity has no built-in Socket.IO client, but listening to a console needs
// very little of the protocol. Over one WebSocket:
//
//   connect   ws://<host>:3000/socket.io/?EIO=4&transport=websocket
//   receive   0{"sid":"...","pingInterval":25000,"pingTimeout":20000}
//   send      40                                   join the default namespace
//   receive   40{"sid":"..."}                      joined
//   receive   2            then send  3            heartbeat, every pingInterval
//   receive   42["BALL_PATH","[[340, 299, 13], ...]"]   one putt
//
// The socket runs on a background task. Call Pump() from Update: events are
// raised there, on the main thread.
//
// Not available on WebGL, where System.Net.WebSockets does not exist.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Puttr
{
    public sealed class SocketIOClient : IDisposable
    {
        public enum SocketState { Closed, Connecting, Open }

        /// <summary>Joined the namespace: events will now arrive.</summary>
        public event Action Connected;
        /// <summary>The socket closed or failed. The argument says why.</summary>
        public event Action<string> Disconnected;
        /// <summary>An event arrived: its name and its first argument, already parsed from JSON.</summary>
        public event Action<string, object> Event;

        public SocketState State => (SocketState)_state;

        readonly ConcurrentQueue<Action> _main = new ConcurrentQueue<Action>();
        CancellationTokenSource _cts;
        int _state;
        int _generation;

        /// <summary>Open the socket. Any socket already open is closed first.</summary>
        public void Connect(string host, int port)
        {
            Close();
            var cts = new CancellationTokenSource();
            _cts = cts;
            int generation = ++_generation;
            Interlocked.Exchange(ref _state, (int)SocketState.Connecting);
            var uri = new Uri("ws://" + host + ":" + port + "/socket.io/?EIO=4&transport=websocket");
            Task.Run(() => Run(uri, generation, cts.Token));
        }

        public void Close()
        {
            _generation++;
            CancellationTokenSource cts = _cts;
            _cts = null;
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
            Interlocked.Exchange(ref _state, (int)SocketState.Closed);
        }

        public void Dispose() => Close();

        /// <summary>Raise queued events. Call once per frame from the main thread.</summary>
        public void Pump()
        {
            while (_main.TryDequeue(out Action action)) action();
        }

        void Post(int generation, Action action)
        {
            _main.Enqueue(() => { if (generation == _generation) action(); });
        }

        async Task Run(Uri uri, int generation, CancellationToken ct)
        {
            string reason = "closed";
            try
            {
                using (var ws = new ClientWebSocket())
                {
                    await ws.ConnectAsync(uri, ct).ConfigureAwait(false);
                    var buffer = new ArraySegment<byte>(new byte[8192]);
                    var message = new MemoryStream();
                    // Until the handshake says otherwise.
                    int quietLimitMs = 30000;

                    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        message.SetLength(0);
                        WebSocketReceiveResult part;
                        do
                        {
                            Task<WebSocketReceiveResult> receive = ws.ReceiveAsync(buffer, ct);
                            // A console that loses power never closes its socket: give up
                            // when it has been quiet for longer than its own heartbeat.
                            if (await Task.WhenAny(receive, Task.Delay(quietLimitMs, ct)).ConfigureAwait(false) != receive)
                                throw new TimeoutException("no heartbeat");
                            part = await receive.ConfigureAwait(false);
                            if (part.MessageType == WebSocketMessageType.Close) { reason = "closed by console"; return; }
                            message.Write(buffer.Array, 0, part.Count);
                        } while (!part.EndOfMessage);

                        if (part.MessageType != WebSocketMessageType.Text) continue;
                        string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                        if (text.Length == 0) continue;

                        switch (text[0])
                        {
                            case '0': // Engine.IO open
                                quietLimitMs = HeartbeatLimit(text.Substring(1));
                                await Send(ws, "40", ct).ConfigureAwait(false);
                                break;
                            case '2': // ping
                                await Send(ws, "3", ct).ConfigureAwait(false);
                                break;
                            case '1': // close
                                reason = "closed by console";
                                return;
                            case '4': // a Socket.IO packet
                                HandlePacket(text, generation);
                                break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { reason = "closed"; }
            catch (Exception e) { reason = e.Message; }
            finally
            {
                if (generation == _generation)
                {
                    Interlocked.Exchange(ref _state, (int)SocketState.Closed);
                    string why = reason;
                    Post(generation, () => Disconnected?.Invoke(why));
                }
            }
        }

        static Task Send(ClientWebSocket ws, string text, CancellationToken ct)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        static int HeartbeatLimit(string openJson)
        {
            double interval = 25000, timeout = 20000;
            if (PuttrJson.TryParse(openJson, out object value) && value is Dictionary<string, object> open)
            {
                if (open.TryGetValue("pingInterval", out object pi)) interval = PuttrJson.Number(pi, interval);
                if (open.TryGetValue("pingTimeout", out object pt)) timeout = PuttrJson.Number(pt, timeout);
            }
            return (int)(interval + timeout + 2000);
        }

        // text[0] is '4' (message). text[1] is the Socket.IO packet type.
        void HandlePacket(string text, int generation)
        {
            if (text.Length < 2) return;
            switch (text[1])
            {
                case '0': // joined the namespace
                    Interlocked.Exchange(ref _state, (int)SocketState.Open);
                    Post(generation, () => Connected?.Invoke());
                    break;
                case '2': // event: 42["NAME", arg, ...]
                    int start = text.IndexOf('[');
                    if (start < 0) return;
                    if (!PuttrJson.TryParse(text.Substring(start), out object value)) return;
                    if (!(value is List<object> args) || args.Count == 0 || !(args[0] is string name)) return;
                    object arg = args.Count > 1 ? args[1] : null;
                    Post(generation, () => Event?.Invoke(name, arg));
                    break;
            }
        }
    }
}
