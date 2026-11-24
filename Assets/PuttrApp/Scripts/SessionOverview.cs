// The coach's side of a Player's 3D session, ported from puttr-app coaching/:
//
//   dashboard/SessionPage.js      phone: the recap (review/SessionRecap.js: Video analysis, Ball overview, Body overview,
//                                 All putts); tablet: the review stage, three columns in landscape (Player | Session
//                                 overview | Player video) and two in portrait (Player video | Session overview)
//   dashboard/SessionAnalysis.js  phone Video analysis: the video above a drawer, the drawing tools, the putt dots and
//                                 the transport floating over it, the Putts and Body tabs in the drawer
//   review/ReviewContext.js       the shared playback state (ReviewState below)
//   review/SessionInfoPanel.js, review/BodyAnalysis.js, review/PuttRail.js, review/PlaybackControls.js
//
// Reached from Player Review and the lesson workspace (CoachDesk.cs) through LabSessionScreen.ForCoach. The Player
// column on a tablet in landscape is the caller's (playerPane). The coach's screen recording (REC) is not here.
//
// Putt timing follows LabReplay (a 3 s intro, 5 s a putt), the same clock the Player's screens use. The app's coach
// screens put putt N's stroke at 9 + 5N (ReviewContext getPuttTimes), a putt later than the replay's own layout
// (LabReplayTiming); the replay's layout is used here so the coach and the Player see the same putt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>
    /// ReviewContext: one session's playback shared by every part of the coach's review (the video, the transport, the
    /// putt selector, the body analysis): the selected putt, pause, the overlay switches, and the seeks.
    /// </summary>
    public sealed class ReviewState
    {
        public readonly App App;
        public readonly LabSessionData Data;
        public readonly BlazeOverlay.Lines Lines = new BlazeOverlay.Lines { Wrist = false, Head = false };
        public SessionVideo Video;
        public int Putt;
        public bool Paused;
        public string Problem;
        public event Action Changed;
        readonly List<ReviewStage> _stages = new List<ReviewStage>();
        readonly Dictionary<int, BodyFaults.Stroke> _strokes = new Dictionary<int, BodyFaults.Stroke>();
        float _lastSeek = -10f;
        int _run;
        bool _gone, _wanted;

        public ReviewState(App app, LabSessionData data)
        {
            App = app;
            Data = data;
            Data.Changed += () =>
            {
                _strokes.Clear();
                // The video arrives with the row once the cloud has finished (a session still processing when opened).
                if (_wanted && Video == null && LabReplay.Url(Data.Row).Length > 0) { Problem = null; Begin(); }
                Fire();
            };
        }

        public void Fire() { if (!_gone) Changed?.Invoke(); }

        public int Total => Data.Putts != null && Data.Putts.Count > 0 ? Data.Putts.Count : 0;

        /// <summary>Starts the replay (once the session row has its video).</summary>
        public void Begin()
        {
            if (Video != null || _gone) return;
            _wanted = true;
            string url = LabReplay.Url(Data.Row);
            if (url.Length == 0) { Problem = Data.Ready ? "This session's video is not ready." : "Still processing in the cloud. Check back in a few minutes."; Fire(); return; }
            Video = new SessionVideo(url) { Loop = true };
            foreach (ReviewStage s in _stages) s.Picture.image = Video.Texture;
            App.Run(Open(++_run));
        }

        IEnumerator Open(int my)
        {
            yield return Video.WaitReady(20);
            if (my != _run || Video == null) yield break;
            if (!Video.Ready) { Problem = "Could not load the video. Check your connection."; Fire(); yield break; }
            Problem = null;
            Video.Play();
            Fire();
        }

        public void Close()
        {
            _gone = true;
            _run++;
            Changed = null;
            Data.Close();
            Video?.Dispose();
            Video = null;
        }

        public void Pause() { if (Video != null && Video.Ready && !Paused) { Paused = true; Video.Pause(); Fire(); } }

        internal void Add(ReviewStage s) { if (!_stages.Contains(s)) _stages.Add(s); if (Video != null) s.Picture.image = Video.Texture; }
        internal void Remove(ReviewStage s) => _stages.Remove(s);

        /// <summary>The selected putt's stroke (phases, camera angle), read once.</summary>
        public BodyFaults.Stroke Stroke(int j)
        {
            if (Data.Pose == null) return null;
            if (_strokes.TryGetValue(j, out BodyFaults.Stroke a)) return a;
            (double s, double e) = BodyFaults.Window(j);
            return _strokes[j] = BodyFaults.Analyze(Data.Pose, s, e, Data.Lefty);
        }

        /// <summary>Where the wrist trail and head marker are anchored: the putt's address, else its window's start.</summary>
        public double Anchor
        {
            get
            {
                BodyFaults.Stroke a = Stroke(Putt);
                return a != null && !double.IsNaN(a.Setup) ? a.Setup : BodyFaults.Window(Putt).start;
            }
        }

        static float Now => Time.realtimeSinceStartup;

        /// <summary>PlaybackControls: PLAY, PAUSE, BACK (0.1 s), BACK_FAST (1 s), FORWARD, FORWARD_FAST.</summary>
        public void Control(string action)
        {
            if (Video == null || !Video.Ready) return;
            switch (action)
            {
                case "PAUSE": Paused = true; Video.Pause(); break;
                case "PLAY": Paused = false; _run++; Video.Play(); break;
                default:
                    double step = action == "BACK" ? -0.1 : action == "BACK_FAST" ? -1 : action == "FORWARD" ? 0.1 : 1;
                    _lastSeek = Now;
                    double t = Math.Max(0, Video.Time + step);
                    if (Paused) App.Run(Freeze(++_run, t)); else Video.Jump(t);
                    break;
            }
            Fire();
        }

        /// <summary>pressPutt: stops on the putt's lead-up (a second into its slot) so the coach can scrub or jump to a phase.</summary>
        public void PressPutt(int j)
        {
            if (j < 0 || j >= Math.Max(1, Total)) return;
            Putt = j;
            SeekTo(LabReplay.SlotStart(j) + 1);
        }

        /// <summary>seekToTime: pauses there.</summary>
        public void SeekTo(double t)
        {
            if (double.IsNaN(t)) return;
            _lastSeek = Now;
            Paused = true;
            if (Video != null && Video.Ready) { Video.Pause(); App.Run(Freeze(++_run, t)); }
            Fire();
        }

        IEnumerator Freeze(int my, double t)
        {
            yield return Video.Freeze(t);
            if (my != _run) yield break;
            foreach (ReviewStage s in _stages) s.Picture.MarkDirtyRepaint();
        }

        /// <summary>Every frame: the pictures and overlays follow the video; while it plays, the selected putt follows the
        /// putt being played (not for 4 s after a seek, so a tapped putt is not snapped back).</summary>
        public void Tick()
        {
            if (_gone || Video == null || !Video.Ready) return;
            double t = Video.Time;
            double anchor = Anchor;
            foreach (ReviewStage s in _stages)
            {
                s.Overlay.Time = t;
                s.Overlay.Anchor = anchor;
                if (Video.Playing) s.Picture.MarkDirtyRepaint();
            }
            if (!Video.Playing) return;
            Perf.Wake(0.5f);
            if (Paused || Now - _lastSeek < 4f) return;
            for (int i = 0; i < Total; i++)
                if (t >= LabReplay.StrokeStart(i) - 0.5 && t <= LabReplay.ResultTime(i) + 0.5)
                {
                    if (Putt != i) { Putt = i; Fire(); }
                    break;
                }
        }
    }

    /// <summary>The video (contain-fitted 9:16) with the pose overlay and the coach's drawing layer, and the tools above it.</summary>
    public sealed class ReviewStage : VisualElement
    {
        public readonly Image Picture;
        public readonly BlazeOverlay Overlay;
        public readonly VideoDraw Draw;
        readonly VisualElement _frame;
        readonly Label _note;
        readonly ReviewState _state;
        object _loaded;

        public ReviewStage(ReviewState state)
        {
            _state = state;
            AddToClassList("lab-stage");
            Picture = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            Overlay = new BlazeOverlay { Settings = state.Lines };
            Overlay.Load(_loaded = state.Data.Blaze);
            Draw = new VideoDraw();
            _frame = Ui.Box("lab-stage-frame", Picture, Overlay, Draw);
            Picture.style.position = Position.Absolute;
            Picture.style.left = Picture.style.top = Picture.style.right = Picture.style.bottom = 0;
            _note = Ui.Text("", "lab-stage-note", Ui.Medium);
            _note.pickingMode = PickingMode.Ignore;
            // The tool bar centred over the top of the video; the strip around it still takes drawing.
            VisualElement tools = Ui.Box("lab-stage-tools", Draw.Bar());
            tools.pickingMode = PickingMode.Ignore;
            Add(_frame);
            Add(_note);
            Add(tools);
            RegisterCallback<GeometryChangedEvent>(_ => Fit());
            RegisterCallback<AttachToPanelEvent>(_ => state.Add(this));
            RegisterCallback<DetachFromPanelEvent>(_ => state.Remove(this));
            state.Add(this);
        }

        /// <summary>The overlay reads the pose file again (it arrived after the stage was built).</summary>
        public void Refresh(string problem)
        {
            // Reading the pose file is costly: only when a new one has arrived.
            if (_state.Data.Blaze != _loaded) Overlay.Load(_loaded = _state.Data.Blaze);
            Overlay.Changed();
            _note.text = problem ?? (_state.Video == null || !_state.Video.Ready ? "Loading Video" : "");
        }

        void Fit()
        {
            float W = layout.width, H = layout.height;
            if (float.IsNaN(W) || W <= 0 || H <= 0) return;
            float w = Mathf.Min(W, H * 9f / 16f), h = w * 16f / 9f;
            _frame.style.width = w;
            _frame.style.height = h;
            _frame.style.left = (W - w) / 2;
            _frame.style.top = (H - h) / 2;
        }
    }

    /// <summary>The pieces of the coach's review panels (SessionInfoPanel, BodyAnalysis, PuttRail, PlaybackControls).</summary>
    static class ReviewParts
    {
        public static VisualElement Kick(string text) => Ui.Caps(text, "caps lab-panel-label");

        /// <summary>The putt selector: a chip per putt with its number, distance and grade.</summary>
        public static VisualElement PuttChips(ReviewState s)
        {
            VisualElement row = Ui.Box("lab-putt-chips");
            for (int i = 0; i < s.Total; i++)
            {
                object p = s.Data.Putts[i];
                LabPuttRead d = LabPuttRead.Of(p);
                int j = i;
                row.Add(Ui.Box("lab-putt-chip" + (i == s.Putt ? " lab-putt-chip-on" : ""),
                    Ui.Text((i + 1).ToString(), "lab-putt-chip-num", Ui.Bold), Ui.Text(J.Int(p, "distance") + "ft", "lab-putt-chip-ft", Ui.Regular),
                    LabUi.Coloured(d.Grade, "lab-putt-chip-grade", d.GradeColour)).OnTap(() => s.PressPutt(j)));
            }
            return row;
        }

        /// <summary>PuttRail: numbered dots with a made or missed dot under each, always near the video.</summary>
        public static VisualElement Rail(ReviewState s)
        {
            VisualElement rail = Ui.Box("row lab-rail-dots");
            for (int i = 0; i < s.Total; i++)
            {
                bool on = i == s.Putt, made = LabBall.Made(s.Data.Putts[i]);
                VisualElement dot = Ui.Box("lab-rail-dot");
                dot.style.backgroundColor = made ? Ui.Green : Ui.Red;
                int j = i;
                rail.Add(Ui.Box("lab-rail-num" + (on ? " lab-rail-num-on" : ""), Ui.Text((i + 1).ToString(), "lab-rail-num-text", Ui.Bold), dot).OnTap(() => s.PressPutt(j)));
            }
            return rail;
        }

        /// <summary>PlaybackControls: rewind 1 s, back a frame, play or pause, forward a frame, forward 1 s.</summary>
        public static VisualElement Transport(ReviewState s, bool docked)
        {
            VisualElement Btn(string icon, string action, float size) => LabUi.Round(icon, 44, Ui.Surface2, () => s.Control(action), size);
            bool paused = s.Paused || s.Video == null || !s.Video.Ready;
            VisualElement main = LabUi.Round(paused ? "Play" : "Pause", 52, LabUi.Surface3, () => s.Control(paused ? "PLAY" : "PAUSE"), 24);
            return Ui.Box("row lab-transport" + (docked ? " lab-transport-docked" : ""),
                Btn("Rewind", "BACK_FAST", 18), Btn("SkipBack", "BACK", 16), main, Btn("SkipForward", "FORWARD", 16), Btn("FastForward", "FORWARD_FAST", 18));
        }

        /// <summary>PhaseJumps: the camera angle (a golfer and its name) and Setup, Backstroke, Impact, Finish, each a seek.</summary>
        public static VisualElement Phases(ReviewState s)
        {
            BodyFaults.Stroke a = s.Stroke(s.Putt);
            VisualElement empty = Empty(s, a);
            if (empty != null) return empty;
            var golfer = new Image { image = Resources.Load<Texture2D>("PuttrApp/LabIcons/LabGolfer" + (a.DownTheLine ? "Rear" : "Front")), tintColor = Ui.Faint, scaleMode = ScaleMode.ScaleToFit };
            golfer.AddToClassList("lab-golfer-small");
            VisualElement seg = Ui.Box("row lab-seg");
            foreach (var (label, time) in new[] { ("Setup", a.Setup), ("Backstroke", a.Back), ("Impact", a.Impact), ("Finish", a.Follow) })
            {
                double t = time;
                VisualElement b = Ui.Box("lab-seg-btn", Ui.Text(label.ToUpperInvariant(), "lab-seg-text", Ui.Bold));
                if (double.IsNaN(t)) b.style.opacity = 0.3f; else b.OnTap(() => s.SeekTo(t));
                seg.Add(b);
            }
            return Ui.Box("row lab-phase-row", golfer, Ui.Text(a.DownTheLine ? "Down the line" : "Face on", "lab-angle-name", Ui.Medium), seg);
        }

        static VisualElement Empty(ReviewState s, BodyFaults.Stroke a)
        {
            if (s.Data.Pose == null) return Ui.Text(s.Data.PoseTried ? "No body data available for this putt" : "Loading body data...", "lab-empty-text", Ui.Regular);
            if (a == null) return Ui.Text("Unable to analyze this putt", "lab-empty-text", Ui.Regular);
            return null;
        }

        /// <summary>PuttBodyMetrics: the putt's four reads, then the tempo (back : through; 2 : 1 is the benchmark).</summary>
        public static VisualElement Metrics(ReviewState s)
        {
            BodyFaults.Stroke a = s.Stroke(s.Putt);
            VisualElement empty = Empty(s, a);
            if (empty != null) return empty;
            BodyFaults.Reading r = BodyFaults.Read(a, s.Data.Lefty);
            if (r == null) return Ui.Text("No body data available for this putt", "lab-empty-text", Ui.Regular);
            // One block: the four reads, two to a row, then the tempo across a row of its own.
            var cells = new List<(string value, string caption, Color colour, Action tap)>();
            foreach (var m in LabUi.Reads(r, s.Data.Lefty)) cells.Add(LabUi.Cell(m.value, m.label, m.colour));
            double back = (double.IsNaN(a.Back) ? 0 : a.Back) - (double.IsNaN(a.Setup) ? 0 : a.Setup), through = (double.IsNaN(a.Impact) ? 0 : a.Impact) - (double.IsNaN(a.Back) ? 0 : a.Back);
            if (back > 0 && through > 0)
            {
                double tempo = back / through;
                cells.Add(LabUi.Cell(tempo.ToString("0.0", CultureInfo.InvariantCulture) + " : 1", "Tempo, back : through (2:1 is the benchmark)", tempo >= 1.7 && tempo <= 2.4 ? (Color)Ui.Bright : Ui.Amber));
            }
            return LabUi.Grid(2, false, cells);
        }

        public static VisualElement Overlays(ReviewState s) => LabUi.OverlaySwitches(s.Lines, () => s.Fire());

        /// <summary>Save video: the replay to the share sheet.</summary>
        public static VisualElement Save(ReviewState s, Func<bool> busy, Action<bool> setBusy)
        {
            return Ui.Button(busy() ? "Saving..." : "Save video", () =>
            {
                string url = J.Str(s.Data.Row, "replayUrl");
                if (!url.StartsWith("http")) { s.App.Toast("This session's video is not ready."); return; }
                App.Run(LabUi.SaveVideo(s.App, url, s.Data.Id, setBusy));
            }, "btn btn-ghost lab-save");
        }
    }

    /// <summary>
    /// A Player's session as their coach sees it. Phone: the recap (Video analysis, Ball overview, Body overview with the
    /// putt count per camera angle, All putts); Video analysis opens VideoAnalysisScreen. Tablet: the review stage, the
    /// session overview beside the Player's video (and the caller's Player column first, in landscape).
    /// </summary>
    public sealed class CoachSessionScreen : AppScreen
    {
        readonly App _app;
        readonly string _name;
        readonly Func<VisualElement> _playerPane;
        readonly VisualElement _body;
        LabSessionData _data;
        ReviewState _state;
        string _layout;
        bool _left, _saving;
        VisualElement _overview, _transport, _stageHost;
        ReviewStage _stage;

        /// <summary>Something for the tablet's last column header (the lesson recording's REC pill, which is the lesson
        /// workspace's): set by its owner, called each time the stage is laid out.</summary>
        public static Func<VisualElement> HeaderExtra;

        public CoachSessionScreen(App app, string sessionId, string playerName, Func<VisualElement> playerPane) : this(app, (object)null, playerName, playerPane)
        {
            App.Run(Api.Get("/lab/session/status/" + sessionId, d =>
            {
                object row = J.First(d);
                if (_left) return;
                if (row == null) { _body.Clear(); _body.Add(Page.Note("Could not load this session.")); return; }
                Start(row);
            }, _ => { if (!_left) { _body.Clear(); _body.Add(Page.Note("Could not load this session. Check your connection.")); } }));
        }

        public CoachSessionScreen(App app, object session, string playerName, Func<VisualElement> playerPane)
        {
            _app = app;
            _name = playerName ?? "";
            _playerPane = playerPane;
            _body = Ui.Box("lab-coach-body");
            Root = LabUi.Style(Ui.Box("lab-coach", Ui.Header(_name.Length > 0 ? _name : "Session", Leave), _body));
            _body.Add(Page.Loading());
            Root.RegisterCallback<GeometryChangedEvent>(_ => { if (_state != null && Wanted() != _layout) Lay(); });
            Root.schedule.Execute(() => _state?.Tick()).Every(16);
            if (session != null) Start(session);
        }

        void Start(object row)
        {
            _data = new LabSessionData(row, true);
            _state = new ReviewState(_app, _data);
            _state.Changed += Draw;
            _data.Start();
            Lay();
        }

        // The phone's recap, or the tablet's columns (three in landscape when there is a Player column, else two).
        string Wanted()
        {
            if (!App.Tablet) return "phone";
            bool landscape = Root.layout.width > Root.layout.height && Root.layout.width > 0;
            return landscape && _playerPane != null ? "three" : "two";
        }

        void Lay()
        {
            _layout = Wanted();
            _body.Clear();
            _stage = null;
            if (_layout == "phone") { _state.Pause(); DrawRecap(); return; }
            _state.Begin();
            VisualElement cols = Ui.Box("lab-cols");
            _stage = new ReviewStage(_state);
            _overview = new VisualElement();
            _overview.AddToClassList("grow");
            _transport = new VisualElement();
            VisualElement Column(string title, VisualElement body, bool extra)
            {
                VisualElement head = Ui.Box("row lab-col-head", Ui.Caps(title, "caps lab-col-title"));
                if (extra && HeaderExtra?.Invoke() is VisualElement pill) head.Add(pill);
                return Ui.Box("lab-col", head, body);
            }
            ScrollView scroll = Ui.Scroll();
            scroll.Add(_overview);
            VisualElement info = Ui.Box("lab-col-fill", scroll, Ui.Box("lab-col-foot", _transport));
            _stageHost = Ui.Box("lab-col-fill", _stage);
            if (_layout == "three")
            {
                cols.Add(Column("Player", Ui.Box("lab-col-fill", _playerPane()), false));
                cols.Add(Column("Session overview", info, false));
                cols.Add(Column("Player video", _stageHost, true));
            }
            else
            {
                cols.Add(Column("Player video", _stageHost, false));
                cols.Add(Column("Session overview", info, true));
            }
            _body.Add(cols);
            Draw();
        }

        public override void Hidden() => _state?.Pause();

        void Leave()
        {
            _left = true;
            _state?.Close();
            _data?.Close();
            _app.Pop();
        }

        void Draw()
        {
            if (_left || _state == null) return;
            if (_layout == "phone") { DrawRecap(); return; }
            _stage?.Refresh(_state.Problem);
            _transport.Clear();
            _transport.Add(ReviewParts.Transport(_state, true));
            _overview.Clear();
            VisualElement col = Ui.Box("lab-panel");
            if (_data.Putts == null) { col.Add(Page.Loading()); _overview.Add(col); return; }
            col.Add(LabUi.BallOverview(_data.Putts, Root));
            col.Add(ReviewParts.Kick("SESSION BODY AVERAGES"));
            col.Add(LabUi.BodyOverview(_data.Body, _data.Lefty, true) ?? Ui.Text(_data.Pose == null && !_data.PoseTried ? "Loading body data..." : "No body data available for this session", "lab-empty-text", Ui.Regular));
            col.Add(ReviewParts.Save(_state, () => _saving, b => { _saving = b; Draw(); }));
            if (_state.Total > 0)
            {
                col.Add(ReviewParts.Kick("PUTTS"));
                col.Add(ReviewParts.PuttChips(_state));
                col.Add(LabUi.PuttCard(_data.Putts[Mathf.Clamp(_state.Putt, 0, _state.Total - 1)], _state.Putt, true, Root));
                col.Add(Ui.Box("lab-section-card", ReviewParts.Phases(_state), Ui.Box("lab-divider"), ReviewParts.Overlays(_state)));
                col.Add(ReviewParts.Kick("BODY ANALYSIS · PUTT " + (_state.Putt + 1)));
                col.Add(ReviewParts.Metrics(_state));
            }
            _overview.Add(col);
        }

        // SessionRecap: Video analysis first, then the overviews and every putt.
        void DrawRecap()
        {
            _body.Clear();
            if (_data.Putts == null || _data.Putts.Count == 0)
            {
                _body.Add(_data.Putts == null ? Page.Loading() : Page.Note("There are no putts in this session yet."));
                return;
            }
            ScrollView scroll = Ui.Scroll();
            VisualElement go = Ui.Button("Video analysis", () => _app.Push(new VideoAnalysisScreen(_app, _state, _name)));
            scroll.Add(Ui.Box("pad", go));
            scroll.Add(Ui.Section("Ball overview"));
            scroll.Add(Ui.Box("pad", LabUi.BallOverview(_data.Putts, Root)));
            scroll.Add(Ui.Section("Body overview"));
            scroll.Add(Ui.Box("pad", LabUi.BodyOverview(_data.Body, _data.Lefty, true) ?? Ui.Text(_data.Pose == null && !_data.PoseTried ? "Loading body data..." : "No body data available for this session", "lab-empty-text", Ui.Regular)));
            scroll.Add(Ui.Section("All putts"));
            VisualElement list = Ui.Box("lab-block");
            for (int i = 0; i < _data.Putts.Count; i++) list.Add(Ui.Box(i == 0 ? "lab-pitem lab-pitem-first" : "lab-pitem", LabUi.PuttCard(_data.Putts[i], i, false, Root)));
            scroll.Add(Ui.Box("pad", list));
            scroll.Add(FreePuttScreen.Space(32));
            _body.Add(scroll);
        }
    }

    /// <summary>
    /// The phone's Video analysis (SessionAnalysis): the video fills the space above the drawer, with the drawing tools
    /// at its top and, floating above the drawer, a collapse button, the putt dots and the transport. The drawer peeks
    /// as two tabs, Putts and Body; a tap (or a drag on its handle) opens it.
    /// </summary>
    public sealed class VideoAnalysisScreen : AppScreen
    {
        const float Peek = 62f;
        readonly App _app;
        readonly ReviewState _state;
        readonly ReviewStage _stage;
        readonly VisualElement _area, _stageBox, _floating, _shown, _drawer, _tabs, _content;
        readonly ScrollView _scroll;
        string _tab = "putts";
        bool _open, _hidden, _saving;
        float _extent;

        public VideoAnalysisScreen(App app, ReviewState state, string playerName)
        {
            _app = app;
            _state = state;
            _stage = new ReviewStage(state);
            _stageBox = Ui.Box("lab-va-stage", _stage);
            _floating = Ui.Box("lab-va-float");
            _floating.pickingMode = PickingMode.Ignore;
            _shown = LabUi.Round("CaretUp", 44, Ui.Surface, () => { _hidden = false; Draw(); }, 20);
            _shown.AddToClassList("lab-va-show");
            _tabs = Ui.Box("row lab-drawer-tabs");
            _scroll = Ui.Scroll();
            _content = Ui.Box("lab-drawer-content", _scroll);
            VisualElement handle = Ui.Box("lab-drawer-handle", Ui.Box("lab-drawer-grip"));
            _drawer = Ui.Box("lab-drawer", handle, _tabs, _content);
            DragDrawer(handle);
            _area = Ui.Box("lab-va-area", _stageBox, _floating, _shown, _drawer);
            Root = LabUi.Style(Ui.Box("lab-coach", Ui.Header("Video analysis", () => { _state.Pause(); app.Pop(); }), _area));
            Root.schedule.Execute(() => _state.Tick()).Every(16);
            _area.RegisterCallback<GeometryChangedEvent>(_ => Size());
            _state.Changed += Draw;
            _state.Begin();
            Draw();
        }

        public override void Shown() => Draw();
        public override void Hidden() => _state.Pause();

        float OpenPx => Mathf.Max(200f, _area.layout.height * 0.45f);

        // The video takes everything above the closed drawer; the drawer opens over it.
        void Size()
        {
            _stageBox.style.bottom = Peek;
            _drawer.style.height = Peek + _extent;
        }

        void Draw()
        {
            if (Root.panel == null) return;
            _stage.Refresh(_state.Problem);
            _floating.Clear();
            _shown.style.display = _hidden ? DisplayStyle.Flex : DisplayStyle.None;
            _floating.style.bottom = Peek + 14;
            _shown.style.bottom = Peek + 14;
            if (!_hidden)
            {
                VisualElement collapse = LabUi.Round("CaretDown", 44, Ui.Surface, () => { _hidden = true; Draw(); }, 18);
                _floating.Add(collapse);
                if (_state.Total > 0) _floating.Add(ReviewParts.Rail(_state));
                _floating.Add(ReviewParts.Transport(_state, false));
            }

            _tabs.Clear();
            foreach (var (key, label, icon) in new[] { ("putts", "Putts", "ListBullets"), ("body", "Body", "PersonArmsSpread") })
            {
                bool on = _tab == key && _open;
                string k = key;
                _tabs.Add(Ui.Box("row lab-drawer-tab" + (on ? " lab-drawer-tab-on" : ""), LabUi.Icon(icon, 16, on ? Color.black : Color.white), Ui.Text(label, "lab-drawer-tab-text", Ui.Bold)).OnTap(() => PressTab(k)));
            }
            // The drawer keeps its scroll position while its content is drawn again.
            _scroll.Clear();
            VisualElement col = Ui.Box("lab-panel");
            if (_tab == "putts")
            {
                if (_state.Total == 0) col.Add(_state.Data.Putts == null ? Page.Loading() : Ui.Text("No putts in this session.", "lab-empty-text", Ui.Regular));
                else
                {
                    col.Add(ReviewParts.Kick("PUTTS"));
                    col.Add(ReviewParts.PuttChips(_state));
                    col.Add(LabUi.PuttCard(_state.Data.Putts[Mathf.Clamp(_state.Putt, 0, _state.Total - 1)], _state.Putt, true, Root));
                    col.Add(ReviewParts.Save(_state, () => _saving, b => { _saving = b; Draw(); }));
                }
            }
            else
            {
                col.Add(ReviewParts.Kick("BODY ANALYSIS · PUTT " + (_state.Putt + 1)));
                col.Add(ReviewParts.Phases(_state));
                col.Add(ReviewParts.Metrics(_state));
                col.Add(Ui.Box("lab-section-card", ReviewParts.Overlays(_state)));
            }
            _scroll.Add(col);
        }

        void PressTab(string key)
        {
            if (key == _tab && _open) { Snap(false); return; }
            _tab = key;
            if (!_open) Snap(true); else Draw();
        }

        void Snap(bool open)
        {
            _open = open;
            _extent = open ? OpenPx : 0;
            _drawer.style.height = Peek + _extent;
            Draw();
        }

        // Dragging the handle moves the drawer; let go and it lands open or closed, whichever is nearer.
        void DragDrawer(VisualElement handle)
        {
            float? startY = null, startExtent = null;
            handle.RegisterCallback<PointerDownEvent>(e => { startY = e.position.y; startExtent = _extent; handle.CapturePointer(e.pointerId); });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (startY == null || !handle.HasPointerCapture(e.pointerId)) return;
                _extent = Mathf.Clamp(startExtent.Value - (e.position.y - startY.Value), 0, OpenPx);
                _drawer.style.height = Peek + _extent;
                Perf.Wake(0.3f);
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (handle.HasPointerCapture(e.pointerId)) handle.ReleasePointer(e.pointerId);
                if (startY == null) return;
                startY = null;
                Snap(Mathf.Abs(OpenPx - _extent) < Mathf.Abs(_extent));
            });
        }
    }
}
