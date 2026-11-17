// The AI chats (puttr-app coach/PuttrProChat, CoachChatHistory, CaddieScreen), on ai.puttr.co v2:
//   POST /v2/coach/chat {conversationId?, message, personaId?}    streamed (SSE: meta, delta, recs, done), or whole
//   GET  /v2/coach/conversations?limit=&before=                    the history, newest first
//   GET  /v2/coach/conversations/:id                               one conversation (messages carry role, text, at)
//   POST /v2/caddie/chat {message, conversationId?, user}          product help, which can open a support ticket
// Coach chat is for Puttr Club members; a past member can read old chats. Both are off for Kid profiles
// (the server refuses kid tokens too). Replies are the app's simple Markdown: bold (green), headings, bullets.
//
// Both screens are a ChatThread: texting as on a phone. Bubbles (mine right in green, theirs left), a time
// between runs, the sent message at once with Sending / Sent / Not sent under it, a typing indicator while
// the reply is coming, and a composer that rides on top of the keyboard and keeps it up after a send.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class ChatUi
    {
        public const string Ai = "https://ai.puttr.co";

        /// <summary>The app's Markdown, as UI Toolkit rich text: **bold** in green, # headings, - bullets, (#id) citations dropped.</summary>
        public static string Rich(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("<", "‹").Replace(">", "›"); // nothing the text holds is markup
            var lines = text.Replace("\r", "").Split('\n');
            var sb = new StringBuilder();
            foreach (string raw in lines)
            {
                string line = raw;
                Match h = Regex.Match(line, @"^\s*#{1,3}\s+(.*)$");
                if (h.Success) line = "<b>" + h.Groups[1].Value + "</b>";
                line = Regex.Replace(line, @"^\s*[-*]\s+", "<color=#7AD446>•</color> ");
                line = Regex.Replace(line, @"\*\*(.+?)\*\*", "<b><color=#7AD446>$1</color></b>");
                line = Regex.Replace(line, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<i>$1</i>");
                sb.Append(line).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        public static string StripIds(string text) => Regex.Replace(text ?? "", @"\s*\(#\d+[^)]*\)", "");

        /// <summary>A POST to ai.puttr.co as the signed-in Player. uploaded fires once the message has left the phone (the "Sent" under it).</summary>
        public static IEnumerator Post(string url, object body, Action<long, string> done, int timeout = 60, Action uploaded = null)
        {
            if (Fixtures.On)
            {
                yield return new WaitForSecondsRealtime(0.35f);
                uploaded?.Invoke();
                yield return new WaitForSecondsRealtime(1.4f + Fixtures.Delay);
                object fake = Fixtures.Chat(url.Replace(Ai, ""));
                done(fake != null ? 200 : 404, fake != null ? J.Write(fake) : "");
                yield break;
            }
            string token = null;
            yield return Auth.Token(t => token = t);
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(J.Write(body)));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                if (token != null) req.SetRequestHeader("Authorization", "Bearer " + token);
                req.timeout = timeout;
                UnityWebRequestAsyncOperation op = req.SendWebRequest();
                bool up = false;
                while (!op.isDone)
                {
                    if (!up && req.uploadProgress >= 1f) { up = true; uploaded?.Invoke(); }
                    yield return null;
                }
                if (!up && req.result == UnityWebRequest.Result.Success) uploaded?.Invoke();
                done(req.result == UnityWebRequest.Result.ConnectionError ? 0 : req.responseCode, req.downloadHandler.text);
            }
        }

        /// <summary>A GET from ai.puttr.co as the signed-in Player (made-up answers under -app2Fixture).</summary>
        public static IEnumerator Get(string url, Action<long, string> done)
        {
            if (Fixtures.On)
            {
                yield return new WaitForSecondsRealtime(Fixtures.Delay);
                object fake = Fixtures.Chat(url.Replace(Ai, ""));
                done(fake != null ? 200 : 404, fake != null ? J.Write(fake) : "");
                yield break;
            }
            string token = null;
            yield return Auth.Token(t => token = t);
            yield return Api.Send("GET", url, null, token, done);
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The time over a run of messages, as Messages writes it: Today 2:41 PM, Yesterday, a weekday, then a date.</summary>
        public static string When(DateTime at)
        {
            DateTime now = DateTime.Now;
            string time = at.ToString("h:mm tt", Inv);
            if (at.Date == now.Date) return "Today " + time;
            if (at.Date == now.Date.AddDays(-1)) return "Yesterday " + time;
            if (at.Date > now.Date.AddDays(-6)) return at.ToString("dddd", Inv) + " " + time;
            return at.ToString(at.Year == now.Year ? "ddd, MMM d" : "MMM d, yyyy", Inv) + " at " + time;
        }

        /// <summary>A message's time from the server (ISO, UTC) in local time, or now.</summary>
        public static DateTime At(object row, string key = "at") => ProfileMath.Date(row, key)?.ToLocalTime() ?? DateTime.Now;
    }

    /// <summary>
    /// One texting thread: the messages in a scroll that follows the newest, and the composer under it.
    /// The thread only scrolls to offsets that exist: the old screens set the offset to the whole content
    /// height, and an elastic ScrollView takes that as is, so after a send everything was scrolled out of
    /// view and the screen went black.
    /// </summary>
    public sealed class ChatThread
    {
        /// <summary>One of my messages, and the line under it: Sending..., Sent, or Not sent.</summary>
        public sealed class Sent
        {
            internal VisualElement Row;
            internal Label Status;
            internal ChatThread Thread;
            internal bool FailedState;

            public void Sending() { FailedState = false; Show("Sending...", false); }
            public void Delivered() { FailedState = false; Show("Sent", false); }

            /// <summary>Not sent: the bubble dims and a tap on it sends it again.</summary>
            public void Failed(Action retry)
            {
                FailedState = true;
                Show("Not sent. Tap to try again.", true);
                Thread._retry = () => { Thread._retry = null; Row.RemoveFromClassList("chat-row-failed"); retry(); };
            }

            void Show(string text, bool failed)
            {
                if (Status == null) return;
                Status.text = text;
                Status.style.display = DisplayStyle.Flex;
                Status.EnableInClassList("chat-status-failed", failed);
                Row.EnableInClassList("chat-row-failed", failed);
                Perf.Wake(0.5f);
            }
        }

        public readonly VisualElement Root;
        public readonly ScrollView Scroll;
        readonly VisualElement _list, _room, _pill, _send;
        readonly TextField _field;
        readonly Func<VisualElement> _avatar;
        readonly Action<string> _onSend;
        VisualElement _typing;
        Sent _last;
        Action _retry;
        DateTime _lastAt = DateTime.MinValue;
        int _lastSide = -1; // 0 theirs, 1 mine
        bool _busy, _stick = true;
        float _blurAt = -10f;
        int _tallest;

        /// <param name="avatar">Their face, beside the first bubble of each of their runs.</param>
        /// <param name="placeholder">The composer's hint; with onSend null the locked strip shows instead.</param>
        public ChatThread(Func<VisualElement> avatar, VisualElement intro, string placeholder, Action<string> onSend, VisualElement locked = null)
        {
            _avatar = avatar;
            _onSend = onSend;
            Scroll = Ui.Scroll();
            Scroll.AddToClassList("chat-scroll");
            if (intro != null) Scroll.Add(intro);
            _list = Ui.Box("chat-list");
            Scroll.Add(_list);
            // Follow the newest message while the Player is at the bottom; leave them be once they scroll up to read.
            Scroll.verticalScroller.valueChanged += v => _stick = v >= Max() - 24f;
            Scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => { if (_stick) Snap(); });
            Scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => { if (_stick) Snap(); });
            // A tap on the thread puts the keyboard away, as in Messages.
            Scroll.RegisterCallback<PointerDownEvent>(_ => { if (_field != null && _field.focusController?.focusedElement == _field) _field.Blur(); }, TrickleDown.TrickleDown);

            VisualElement composer;
            if (onSend == null) composer = locked ?? Ui.Box("chat-locked");
            else
            {
                _field = new TextField { multiline = true };
                _field.textEdition.placeholder = placeholder;
                _field.textEdition.hidePlaceholderOnFocus = false;
                _field.textEdition.hideMobileInput = true; // the composer is the input: no system text bar over the keyboard
                _field.textSelection.cursorColor = Ui.Bright;
                _field.textSelection.selectionColor = new Color(0.48f, 0.83f, 0.27f, 0.35f);
                _field.AddToClassList("chat-field");
                _field.Face(Ui.Regular);
                _field.RegisterValueChangedCallback(_ => Refresh());
                _field.RegisterCallback<FocusInEvent>(_ => { _pill.AddToClassList("chat-focus"); Perf.Wake(1f); });
                _field.RegisterCallback<FocusOutEvent>(_ => { _pill.RemoveFromClassList("chat-focus"); _blurAt = Time.realtimeSinceStartup; Perf.Wake(1f); });
                // Return sends on a keyboard (Shift+Return for a new line). Unity sends Return twice, as the key and as
                // the '\n' character; both stop here so no stray line is left in the box. A phone's return key types a new line.
                _field.RegisterCallback<KeyDownEvent>(e =>
                {
                    bool enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n' || e.character == '\r';
                    if (!enter || e.shiftKey || Application.isMobilePlatform) return;
                    e.StopPropagation();
                    if (e.keyCode != KeyCode.None) Go();
                }, TrickleDown.TrickleDown);
                _pill = Ui.Box("chat-pill-input", _field);
                _pill.RegisterCallback<PointerDownEvent>(_ => _field.Focus()); // the whole pill is the target, not only the text
                _send = Ui.Box("chat-send-btn chat-send-off", Ui.Icon("PaperPlaneRight", 18, Color.white)).OnTap(Go);
                composer = Ui.Box("chat-bar", _pill, _send);
                DevDrive(_field);
            }
            _room = Ui.Box("chat-keyboard-room");
            Root = Ui.Box("chat-thread", Scroll, composer, _room);
            Root.schedule.Execute(Lift).Every(50);
        }

        // ── Messages ─────────────────────────────────────────────────────────

        public void Clear()
        {
            _list.Clear();
            _typing = null;
            _last = null;
            _retry = null;
            _lastAt = DateTime.MinValue;
            _lastSide = -1;
            _stick = true;
        }

        /// <summary>Anything else in the thread (a spinner, recommendation cards), above the typing indicator.</summary>
        public void Append(VisualElement element)
        {
            if (_typing != null && _typing.parent == _list) _list.Insert(_list.IndexOf(_typing), element);
            else _list.Add(element);
            Perf.Wake(0.5f);
        }

        /// <summary>Their message; the label is returned so a streamed reply can grow in place.</summary>
        public Label Theirs(string text, DateTime at)
        {
            bool cont = Run(0, at);
            Label words = Words(ChatUi.Rich(text), true);
            VisualElement bubble = Ui.Box("chat-bubble-theirs", words);
            Copyable(bubble, () => words.text.Length > 0 ? Regex.Replace(words.text, "<.*?>", "") : "");
            VisualElement face = _avatar != null ? _avatar() : Ui.Box("chat-face");
            face.AddToClassList("chat-face");
            if (cont) face.style.visibility = Visibility.Hidden;
            Append(Ui.Box(cont ? "chat-line chat-line-cont" : "chat-line", face, bubble));
            return words;
        }

        /// <summary>My message. quiet: from history, with no Sending / Sent line.</summary>
        public Sent Mine(string text, DateTime at, bool quiet = false)
        {
            bool cont = Run(1, at);
            Label words = Words(text, false);
            VisualElement bubble = Ui.Box("chat-bubble-mine", words);
            Copyable(bubble, () => text);
            VisualElement row = Ui.Box(cont ? "chat-line chat-line-mine chat-line-cont" : "chat-line chat-line-mine", bubble);
            var sent = new Sent { Row = row, Thread = this };
            row.RegisterCallback<ClickEvent>(_ => { if (sent.FailedState) _retry?.Invoke(); });
            Append(row);
            if (quiet) return sent;
            // Only the newest message says how it went, as in Messages (one that failed keeps saying so).
            if (_last != null && _last.Status != null && !_last.FailedState) _last.Status.style.display = DisplayStyle.None;
            sent.Status = Ui.Text("", "chat-status", Ui.Medium);
            Append(sent.Status);
            _last = sent;
            ToEnd();
            return sent;
        }

        /// <summary>The three dots while a reply is on its way.</summary>
        public void Typing(bool on)
        {
            if (!on) { _typing?.RemoveFromHierarchy(); _typing = null; return; }
            if (_typing != null) return;
            var dots = new List<VisualElement>();
            VisualElement bubble = Ui.Box("chat-bubble-theirs chat-typing-bubble");
            for (int i = 0; i < 3; i++) { VisualElement d = Ui.Box("chat-dot"); dots.Add(d); bubble.Add(d); }
            VisualElement face = _avatar != null ? _avatar() : Ui.Box("chat-face");
            face.AddToClassList("chat-face");
            _typing = Ui.Box("chat-line", face, bubble);
            _list.Add(_typing);
            VisualElement mine = _typing;
            int step = 0;
            mine.schedule.Execute(() =>
            {
                step++;
                for (int i = 0; i < 3; i++) dots[i].style.opacity = (step % 3) == i ? 1f : 0.35f;
                Perf.Wake(0.3f);
            }).Every(220).Until(() => mine.panel == null);
            ToEnd();
        }

        /// <summary>A reply is pending: the send button rests (typing ahead is fine).</summary>
        public bool Busy { get => _busy; set { _busy = value; Refresh(); } }

        /// <summary>Scroll to the newest message, and keep following it.</summary>
        public void ToEnd()
        {
            _stick = true;
            Snap();
            Scroll.schedule.Execute(Snap); // and once the new message has its size
        }

        public void Blur() => _field?.Blur();

        // ── Inside ───────────────────────────────────────────────────────────

        float Max()
        {
            float max = Scroll.contentContainer.layout.height - Scroll.contentViewport.layout.height;
            return float.IsNaN(max) ? 0f : Mathf.Max(0f, max);
        }

        void Snap()
        {
            float max = Max();
            if (Mathf.Abs(Scroll.scrollOffset.y - max) < 0.5f) return;
            Scroll.scrollOffset = new Vector2(0f, max);
            Perf.Wake(0.5f);
        }

        // A time over the message when it starts a new run (15 minutes on); a tighter gap inside one sender's run.
        bool Run(int side, DateTime at)
        {
            bool stamp = _lastAt == DateTime.MinValue || (at - _lastAt).TotalMinutes >= 15;
            if (stamp) Append(Ui.Text(ChatUi.When(at), "chat-stamp", Ui.Medium));
            bool cont = !stamp && side == _lastSide;
            _lastAt = at;
            _lastSide = side;
            return cont;
        }

        static Label Words(string text, bool rich)
        {
            var words = new Label(text) { enableRichText = rich };
            words.AddToClassList("chat-words");
            words.Face(Ui.Regular);
            return words;
        }

        // A long press copies the message (a tap is for scrolling and putting the keyboard away).
        static void Copyable(VisualElement bubble, Func<string> text)
        {
            IVisualElementScheduledItem pending = null;
            bubble.RegisterCallback<PointerDownEvent>(_ =>
            {
                pending?.Pause();
                pending = bubble.schedule.Execute(() =>
                {
                    string t = text();
                    if (t.Length == 0) return;
                    GUIUtility.systemCopyBuffer = t;
                    App.I?.Toast("Copied");
                });
                pending.ExecuteLater(550);
            });
            bubble.RegisterCallback<PointerUpEvent>(_ => pending?.Pause());
            bubble.RegisterCallback<PointerLeaveEvent>(_ => pending?.Pause());
            bubble.RegisterCallback<PointerMoveEvent>(e => { if (e.deltaPosition.sqrMagnitude > 16f) pending?.Pause(); });
        }

        void Refresh()
        {
            if (_send == null) return;
            bool ready = !_busy && _field.value.Trim().Length > 0;
            _send.EnableInClassList("chat-send-off", !ready);
            Perf.Wake(0.5f);
        }

        void Go()
        {
            if (_field == null || _busy) return;
            string t = _field.value.Trim();
            if (t.Length == 0) return;
            // Keep the keyboard up after a send, as Messages does (the tap on the send button took the focus).
            bool keep = _field.focusController?.focusedElement == _field || Time.realtimeSinceStartup - _blurAt < 0.6f;
            _field.value = "";
            _onSend(t);
            if (keep) _field.schedule.Execute(() => _field.Focus());
            ToEnd();
        }

        // Lift the composer by the keyboard: the room under it is the keyboard's height less the safe area the app already keeps clear.
        void Lift()
        {
            if (!Application.isMobilePlatform || Root.panel == null || Screen.width <= 0) return;
            _tallest = Mathf.Max(_tallest, Screen.height);
            float px = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0f;
            // A window the system already shrank for the keyboard (Android adjustResize) needs no room of its own.
            if (Screen.height < _tallest - 100) px = 0f;
            float k = Root.panel.visualTree.layout.width / Screen.width;
            if (float.IsNaN(k) || k <= 0f) return;
            float want = Mathf.Max(0f, (px - Screen.safeArea.yMin) * k);
            if (Mathf.Abs(_room.resolvedStyle.height - want) < 0.5f) return;
            _room.style.height = want;
            Perf.Wake(0.6f);
        }

        /// <summary>-app2ChatSay words_with_underscores: types into the composer 3 s after it opens and presses Return a second later (for checking a send from a script).</summary>
        static void DevDrive(TextField input)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-app2ChatSay");
            if (i < 0 || i + 1 >= args.Length) return;
            string words = args[i + 1].Replace('_', ' ');
            input.schedule.Execute(() => { input.Focus(); input.value = words; Perf.Wake(2f); }).ExecuteLater(3000);
            input.schedule.Execute(() =>
            {
                using (KeyDownEvent e = KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None)) { e.target = input; input.SendEvent(e); }
                Perf.Wake(2f);
            }).ExecuteLater(4000);
        }
    }

    /// <summary>Reads a server-sent event stream as it arrives (the coach's reply, word by word).</summary>
    sealed class SseHandler : DownloadHandlerScript
    {
        readonly StringBuilder _buffer = new StringBuilder();
        readonly Action<string, object> _event;
        public SseHandler(Action<string, object> onEvent) : base(new byte[4096]) { _event = onEvent; }

        protected override bool ReceiveData(byte[] data, int length)
        {
            _buffer.Append(Encoding.UTF8.GetString(data, 0, length));
            string all = _buffer.ToString();
            int split;
            while ((split = all.IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
            {
                string block = all.Substring(0, split);
                all = all.Substring(split + 2);
                string name = "message", payload = "";
                foreach (string line in block.Split('\n'))
                {
                    if (line.StartsWith("event:")) name = line.Substring(6).Trim();
                    else if (line.StartsWith("data:")) payload += line.Substring(5).Trim();
                }
                _event(name, J.Parse(payload));
            }
            _buffer.Clear();
            _buffer.Append(all);
            return true;
        }
    }

    /// <summary>PuttrProChat: the Player's coach, by name and face, with history and new chat.</summary>
    public sealed class CoachChatScreen : AppScreen
    {
        readonly App _app;
        readonly ChatThread _thread;
        string _conversationId;
        bool _sending;

        /// <summary>openCoachChat: a member chats; a past member with chats reads them; anyone else meets the paywall.</summary>
        public static void Open(App app)
        {
            if (Kid.IsKid(app.Profile)) { app.Toast("Chat is off for Kid profiles."); return; }
            if (Club.Member(app.Profile)) { app.Push(new CoachChatScreen(app, null, false)); return; }
            App.Run(History(app));
        }

        static IEnumerator History(App app)
        {
            long status = 0; string text = null;
            yield return ChatUi.Get(ChatUi.Ai + "/v2/coach/conversations?limit=1", (s, t) => { status = s; text = t; });
            if (status == 200 && J.List(J.Get(J.Parse(text), "conversations")).Count > 0) app.Push(new CoachHistoryScreen(app));
            else app.Push(new ClubScreen(app, "Your putting coach"));
        }

        public CoachChatScreen(App app, string conversationId, bool readOnly)
        {
            _app = app;
            Coaches.Persona coach = CoachVoice.Persona;
            VisualElement top = Ui.Box("row chat-top",
                Ui.IconButton("Clock", "History", () => app.Push(new CoachHistoryScreen(app))),
                Ui.Box("grow"),
                readOnly ? null : Ui.IconButton("ChatCircle", "New chat", () => { if (_sending) return; _conversationId = null; Greet(); }));
            VisualElement intro = Ui.Box("pad center-box chat-intro", Ui.Avatar(coach.Name, null, true), Ui.Text("Putting Coach", "feed-name", Ui.Bold).Margin(6),
                Ui.Text("Get personalized coaching on grip, stance, stroke mechanics, green reading, and more.", "muted center", Ui.Regular).Margin(4));
            VisualElement locked = readOnly
                ? Ui.Box("chat-locked", Ui.Text("This chat is read-only. Become a member again to keep talking with your coach.", "sub", Ui.Medium)).OnTap(() => app.Push(new ClubScreen(app, "Your putting coach")))
                : null;
            _thread = new ChatThread(() => Ui.Avatar(CoachVoice.Persona.Name, null), intro, "Message your coach", readOnly ? null : (Action<string>)Send, locked);
            Root = Ui.Box("chat-screen", Ui.Header(coach.Name, app.Pop), top, _thread.Root);
            App.Run(Api.Get("/coach/settings", d => CoachVoice.Load(d), _ => { }));
            if (conversationId != null) App.Run(LoadConversation(conversationId));
            else { Greet(); App.Run(ResumeLatest()); } // the greeting at once; the last chat replaces it if there is one
        }

        public override void Hidden() => _thread.Blur();

        string First => HomeScreen.FirstName(_app.Profile);

        void Greet()
        {
            _thread.Clear();
            _thread.Theirs("Hey " + (First.Length > 0 ? First : "there") + ", what part of your putting can I help with today?", DateTime.Now);
        }

        IEnumerator ResumeLatest()
        {
            long status = 0; string text = null;
            yield return ChatUi.Get(ChatUi.Ai + "/v2/coach/conversations?limit=1", (s, t) => { status = s; text = t; });
            if (_sending) yield break; // the Player has started this chat already
            List<object> rows = J.List(J.Get(J.Parse(text), "conversations"));
            if (status == 200 && rows.Count > 0) yield return LoadConversation(J.Str(rows[0], "conversationId"));
        }

        IEnumerator LoadConversation(string id)
        {
            _thread.Clear();
            _thread.Append(Ui.Box("chat-loading", Spinner.Small()));
            long status = 0; string text = null;
            yield return ChatUi.Get(ChatUi.Ai + "/v2/coach/conversations/" + UnityWebRequest.EscapeURL(id), (s, t) => { status = s; text = t; });
            if (_sending) yield break;
            _thread.Clear();
            if (status != 200) { _thread.Theirs("I couldn't load this conversation. Check your connection and try again.", DateTime.Now); yield break; }
            object convo = J.Parse(text);
            _conversationId = J.Str(convo, "conversationId", id);
            foreach (object m in J.List(J.Get(convo, "messages")))
            {
                DateTime at = ChatUi.At(m);
                if (J.Str(m, "role") == "user") _thread.Mine(J.Str(m, "text"), at, true);
                else { _thread.Theirs(J.Str(m, "text"), at); Recs(m); }
            }
            _thread.ToEnd();
        }

        void Send(string message)
        {
            if (_sending) return;
            ChatThread.Sent sent = _thread.Mine(message, DateTime.Now);
            App.Run(Stream(message, sent));
        }

        // The reply, streamed as it is written; the whole reply at once if streaming fails.
        IEnumerator Stream(string message, ChatThread.Sent sent)
        {
            _sending = true;
            _thread.Busy = true;
            sent.Sending();
            _thread.Typing(true);
            var body = new Dictionary<string, object> { { "message", message }, { "personaId", CoachVoice.Persona.Id } };
            if (_conversationId != null) body["conversationId"] = _conversationId;
            var answer = new StringBuilder();
            Label words = null;
            object recs = null;
            bool delivered = false;
            void Delivered() { if (delivered) return; delivered = true; sent.Delivered(); }
            void Show()
            {
                Delivered();
                if (words == null) { _thread.Typing(false); words = _thread.Theirs("", DateTime.Now); }
                words.text = ChatUi.Rich(answer.ToString());
                Perf.Wake(0.5f);
            }
            if (Fixtures.On)
            {
                // Made up: the fixture answer, a few words at a time.
                yield return new WaitForSecondsRealtime(0.4f);
                Delivered();
                yield return new WaitForSecondsRealtime(1.2f);
                object fake = Fixtures.Chat("/v2/coach/chat");
                _conversationId = J.Str(fake, "conversationId", _conversationId);
                foreach (string w in J.Str(fake, "answer").Split(' ')) { answer.Append(w).Append(' '); Show(); yield return new WaitForSecondsRealtime(0.03f); }
                recs = fake;
            }
            else
            {
                string token = null;
                yield return Auth.Token(t => token = t);
                var handler = new SseHandler((name, data) =>
                {
                    if (name == "meta" || name == "done") { Delivered(); string c = J.Str(data, "conversationId"); if (c.Length > 0) _conversationId = c; }
                    else if (name == "delta") { answer.Append(J.Str(data, "text")); Show(); }
                    else if (name == "recs") recs = data;
                });
                using (var req = new UnityWebRequest(ChatUi.Ai + "/v2/coach/chat", "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(J.Write(body)));
                    req.downloadHandler = handler;
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Accept", "text/event-stream");
                    if (token != null) req.SetRequestHeader("Authorization", "Bearer " + token);
                    req.timeout = 60;
                    yield return req.SendWebRequest();
                    if (req.responseCode != 200 || answer.Length == 0)
                    {
                        // Not streamed: the whole answer.
                        answer.Clear();
                        body["stream"] = false;
                        long status = 0; string text = null;
                        yield return ChatUi.Post(ChatUi.Ai + "/v2/coach/chat", body, (s, t) => { status = s; text = t; });
                        object whole = J.Parse(text);
                        if (status == 200 && J.Str(whole, "answer").Length > 0) { _conversationId = J.Str(whole, "conversationId", _conversationId); answer.Append(J.Str(whole, "answer")); recs = whole; }
                    }
                }
            }
            _thread.Typing(false);
            if (answer.Length == 0)
            {
                // Nothing came back: the message is marked, and a tap sends it again.
                sent.Failed(() => App.Run(Stream(message, sent)));
            }
            else
            {
                if (recs != null && (J.List(J.Get(recs, "recommendedLessons")).Count > 0 || J.List(J.Get(recs, "recommendedGames")).Count > 0)) { string clean = ChatUi.StripIds(answer.ToString()); answer.Clear(); answer.Append(clean.TrimEnd()); }
                Show();
                if (recs != null) Recs(recs);
            }
            _sending = false;
            _thread.Busy = false;
        }

        // CoachRecCards: the lessons and games the coach pointed to.
        void Recs(object m)
        {
            List<object> lessons = J.List(J.Get(m, "recommendedLessons")), games = J.List(J.Get(m, "recommendedGames"));
            if (lessons.Count + games.Count == 0) return;
            VisualElement box = Ui.Box("chat-recs", Ui.Text("COACH RECOMMENDATIONS", "kick", Ui.Bold));
            foreach (object l in lessons)
            {
                string id = J.Str(l, "id");
                box.Add(Ui.Box("glass list-row", Ui.Box("grow", Ui.Text("LESSON", "kick", Ui.Bold), Ui.Text(J.Str(l, "title"), "feed-name", Ui.Bold), Ui.Text("Watch and practice", "muted", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                    .OnTap(() => Library.Load(() =>
                    {
                        object lesson = (Library.Lessons ?? new List<object>()).Find(x => J.Str(x, "id") == id) ?? (Library.Workouts ?? new List<object>()).Find(x => J.Str(x, "id") == id);
                        if (lesson != null) _app.Push(new LessonScreen(_app, lesson));
                    })));
            }
            foreach (object g in games)
            {
                string id = J.Str(g, "id");
                box.Add(Ui.Box("glass list-row", Ui.Box("grow", Ui.Text("DRILL GAME", "kick", Ui.Bold), Ui.Text(J.Str(g, "title"), "feed-name", Ui.Bold), Ui.Text("Play it now", "muted", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                    .OnTap(() => App.Run(Api.Get("/games/ALL", d => { object game = J.List(d).Find(x => J.Str(x, "id") == id); if (game != null) _app.Push(new GameScreen(_app, game)); }))));
            }
            _thread.Append(box);
        }
    }

    /// <summary>CoachChatHistory: every chat with the coach, Today, This week, Earlier.</summary>
    public sealed class CoachHistoryScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _list;
        readonly List<object> _rows = new List<object>();
        bool _more, _loading;

        public CoachHistoryScreen(App app)
        {
            _app = app;
            bool member = Club.Member(app.Profile);
            Root = Page.Make(app, "Chat history", out VisualElement body);
            body.Add(Ui.Box("pad", Ui.Text(member ? "Pick up any conversation with your coach where you left off." : "Everything your coach has told you so far.", "sub", Ui.Regular)));
            if (member) body.Add(Ui.Box("pad", Ui.Button("New chat", () => { app.Pop(); app.Push(new CoachChatScreen(app, null, false)); })));
            else body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("Your chats are read-only", "feed-name", Ui.Bold), Ui.Text("Become a member again to keep talking with your coach.", "sub", Ui.Regular)).OnTap(() => app.Push(new ClubScreen(app, "Your putting coach")))));
            _list = new VisualElement();
            body.Add(_list);
        }

        public override void Shown() { _rows.Clear(); App.Run(Load(null)); }

        IEnumerator Load(string before)
        {
            if (_loading) yield break;
            _loading = true;
            long status = 0; string text = null;
            yield return ChatUi.Get(ChatUi.Ai + "/v2/coach/conversations?limit=30" + (before != null ? "&before=" + UnityWebRequest.EscapeURL(before) : ""), (s, t) => { status = s; text = t; });
            _loading = false;
            if (status != 200) { _list.Clear(); _list.Add(Page.Note("Couldn't load your chats. Check your connection and try again.")); yield break; }
            object data = J.Parse(text);
            foreach (object r in J.List(J.Get(data, "conversations"))) if (!_rows.Exists(x => J.Str(x, "conversationId") == J.Str(r, "conversationId"))) _rows.Add(r);
            _more = J.Flag(data, "hasMore");
            Draw();
        }

        void Draw()
        {
            _list.Clear();
            if (_rows.Count == 0) { _list.Add(Ui.Box("pad center-box", Ui.Text("No chats yet", "h2", Ui.Bold).Margin(30), Ui.Text("Conversations with your coach are saved here so you can come back to them.", "sub center", Ui.Regular))); return; }
            DateTime now = DateTime.Now;
            string section = null;
            VisualElement card = null;
            foreach (object r in _rows)
            {
                DateTime at = ProfileMath.Date(r, "lastTurnAt")?.ToLocalTime() ?? now;
                string s = at.Date == now.Date ? "TODAY" : at.Date > now.Date.AddDays(-6) ? "THIS WEEK" : "EARLIER";
                if (s != section) { section = s; _list.Add(Ui.Section(s)); card = Ui.Box("glass set-card"); _list.Add(Ui.Box("pad", card)); }
                string when = at.Date == now.Date ? at.ToString("h:mm tt", CultureInfo.InvariantCulture) : at.Date > now.Date.AddDays(-6) ? at.ToString("dddd", CultureInfo.InvariantCulture) : at.ToString(at.Year == now.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture);
                string id = J.Str(r, "conversationId");
                card.Add(Ui.Box("note-row", Ui.Box("grow set-words", Ui.Box("row", Ui.Text(J.Str(r, "title", "Chat with your coach"), "feed-name grow", Ui.Bold), Ui.Text(when, "muted", Ui.Regular)),
                    Ui.Text((J.Str(r, "previewRole") == "user" ? "You: " : "") + J.Str(r, "preview"), "sub", Ui.Regular))).OnTap(() => _app.Push(new CoachChatScreen(_app, id, !Club.Member(_app.Profile)))));
            }
            if (_more) _list.Add(Ui.Box("pad", Ui.Button("Load more", () => App.Run(Load(J.Str(_rows[_rows.Count - 1], "lastTurnAt"))), "btn btn-ghost")));
        }
    }

    /// <summary>CaddieScreen: help with Puttr itself, games and hardware. Each visit is a new chat.</summary>
    public sealed class CaddieScreen : AppScreen
    {
        readonly App _app;
        readonly ChatThread _thread;
        string _conversationId;
        bool _sending;

        static VisualElement Face()
        {
            VisualElement face = Ui.Box("avatar chat-caddie-face", Ui.Icon("FillTarget", 15, Ui.Bright));
            face.style.backgroundColor = Color.Lerp(Ui.Ink, Ui.Bright, 0.2f);
            return face;
        }

        public CaddieScreen(App app)
        {
            _app = app;
            VisualElement intro = Ui.Box("pad center-box chat-intro", Ui.Bubble("FillTarget", Ui.Bright), Ui.Text("Puttr Caddie", "feed-name", Ui.Bold).Margin(6),
                Ui.Text("Ask me anything about using Puttr, game rules, or getting the most out of your practice.", "muted center", Ui.Regular).Margin(4));
            bool kid = Kid.IsKid(app.Profile);
            _thread = new ChatThread(Face, intro, "Message the Caddie", kid ? null : (Action<string>)Send, Ui.Box("chat-locked", Ui.Text("Chat is off for Kid profiles.", "sub", Ui.Medium)));
            string first = HomeScreen.FirstName(app.Profile);
            _thread.Theirs("Hey " + (first.Length > 0 ? first : "there") + ", how can I help you with your Puttr today?", DateTime.Now);
            Root = Ui.Box("chat-screen", Ui.Header("Caddie", app.Pop), _thread.Root);
        }

        public override void Hidden() => _thread.Blur();

        void Send(string message)
        {
            if (_sending) return;
            Ask(message, _thread.Mine(message, DateTime.Now));
        }

        void Ask(string message, ChatThread.Sent sent)
        {
            _sending = true;
            _thread.Busy = true;
            sent.Sending();
            _thread.Typing(true);
            var body = new Dictionary<string, object> { { "message", message }, { "user", _app.Profile } };
            if (_conversationId != null) body["conversationId"] = _conversationId;
            App.Run(ChatUi.Post(ChatUi.Ai + "/v2/caddie/chat", body, (status, text) =>
            {
                _thread.Typing(false);
                object answer = J.Parse(text);
                if (status == 200 && J.Str(answer, "answer").Length > 0)
                {
                    sent.Delivered();
                    _conversationId = J.Str(answer, "conversationId", _conversationId);
                    _thread.Theirs(J.Str(answer, "answer"), DateTime.Now);
                }
                else { Perf.Note("caddie chat failed: " + status + " " + (text ?? "").Substring(0, Mathf.Min(200, (text ?? "").Length))); sent.Failed(() => Ask(message, sent)); }
                _sending = false;
                _thread.Busy = false;
            }, 30, sent.Delivered));
        }
    }
}
