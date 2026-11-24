// The browser's WebSocket, for the WebGL build (Assets/Plugins/WebGL/PuttrSocket.jslib).
// System.Net.WebSockets.ClientWebSocket does not work there and WebGL has no threads, so this
// is polled from the main thread: State and Receive once a frame. Elsewhere it does nothing
// (Supported is false) and the callers keep their ClientWebSocket on a background task.
using System;
using System.Runtime.InteropServices;

namespace Puttr
{
    public sealed class JsWebSocket : IDisposable
    {
        public enum SocketState { Connecting = 0, Open = 1, Closed = 2 }

#if UNITY_WEBGL && !UNITY_EDITOR
        public const bool Supported = true;
        [DllImport("__Internal")] static extern int PuttrSocket_Open(string url);
        [DllImport("__Internal")] static extern int PuttrSocket_State(int id);
        [DllImport("__Internal")] static extern void PuttrSocket_Send(int id, string text);
        [DllImport("__Internal")] static extern string PuttrSocket_Receive(int id);
        [DllImport("__Internal")] static extern string PuttrSocket_Reason(int id);
        [DllImport("__Internal")] static extern void PuttrSocket_Close(int id);
#else
        public const bool Supported = false;
        static int PuttrSocket_Open(string url) => 0;
        static int PuttrSocket_State(int id) => 2;
        static void PuttrSocket_Send(int id, string text) { }
        static string PuttrSocket_Receive(int id) => null;
        static string PuttrSocket_Reason(int id) => "WebSocket bridge is WebGL only";
        static void PuttrSocket_Close(int id) { }
#endif

        int _id;

        /// <summary>Opens a socket to a ws:// or wss:// address.</summary>
        public JsWebSocket(string url) { _id = PuttrSocket_Open(url); }

        public SocketState State => _id == 0 ? SocketState.Closed : (SocketState)PuttrSocket_State(_id);

        /// <summary>Why it closed (once State is Closed).</summary>
        public string Reason => PuttrSocket_Reason(_id);

        public void Send(string text) { if (_id != 0) PuttrSocket_Send(_id, text); }

        /// <summary>The next text message that has arrived, or null.</summary>
        public string Receive() => _id == 0 ? null : PuttrSocket_Receive(_id);

        public void Dispose()
        {
            if (_id != 0) PuttrSocket_Close(_id);
            _id = 0;
        }
    }
}
