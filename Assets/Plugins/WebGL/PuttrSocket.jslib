// The browser's own WebSocket for the WebGL build (the Samsung TV app), where System.Net.WebSockets
// does not work. Puttr/Runtime/JsWebSocket.cs calls these; nothing here calls back into C#: the
// app polls (State, Receive) once a frame from the main thread, which is the only thread WebGL has.
var PuttrSocketLib = {
  $PuttrSockets: { next: 1, all: {} },

  // Opens a socket and returns its id (never 0).
  PuttrSocket_Open: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var id = PuttrSockets.next++;
    var s = { q: [], state: 0, reason: '' };
    PuttrSockets.all[id] = s;
    try {
      var ws = new WebSocket(url);
      s.ws = ws;
      ws.onopen = function () { s.state = 1; };
      ws.onmessage = function (e) { if (typeof e.data === 'string') s.q.push(e.data); };
      ws.onerror = function () { if (!s.reason) s.reason = 'could not reach ' + url.split('/socket.io')[0]; };
      ws.onclose = function (e) { s.state = 2; if (!s.reason) s.reason = 'closed (' + e.code + ')'; };
    } catch (e) {
      s.state = 2;
      s.reason = String((e && e.message) || e);
    }
    return id;
  },

  // 0 connecting, 1 open, 2 closed (or unknown id).
  PuttrSocket_State: function (id) {
    var s = PuttrSockets.all[id];
    return s ? s.state : 2;
  },

  PuttrSocket_Send: function (id, textPtr) {
    var s = PuttrSockets.all[id];
    if (s && s.ws && s.ws.readyState === 1) s.ws.send(UTF8ToString(textPtr));
  },

  // The next text message, or null when there is none waiting.
  PuttrSocket_Receive: function (id) {
    var s = PuttrSockets.all[id];
    if (!s || s.q.length === 0) return 0;
    var text = s.q.shift();
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  },

  // Why it closed.
  PuttrSocket_Reason: function (id) {
    var s = PuttrSockets.all[id];
    var text = s ? s.reason : 'closed';
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  },

  // Closes it and forgets it.
  PuttrSocket_Close: function (id) {
    var s = PuttrSockets.all[id];
    if (!s) return;
    delete PuttrSockets.all[id];
    try { if (s.ws) s.ws.close(); } catch (e) { }
  },
};

autoAddDeps(PuttrSocketLib, '$PuttrSockets');
mergeInto(LibraryManager.library, PuttrSocketLib);
