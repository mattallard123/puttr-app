// "Explore manually" (puttr-app lab/LabSessionReplay.js): the session's replay full screen with the pose drawn over the
// Player, a coaching HUD that follows the video, and one control bar.
//
//   top      the current putt's compact card (it changes as the video reaches each putt)
//   side     the camera angle and the putt's four body reads, on the side away from the Player
//   above    the stroke phases (SETUP, BACKSTROKE, IMPACT, FOLLOW) with the time into each: a tap freezes the video there
//   bottom   overlays on or off, the overlay switches (Overlay lines), previous putt, play or pause, next putt, save
//
// A tap on the video hides or shows the HUD. The replay loops, as the app's repeat does. Putt timing is the Shotstack
// layout (LabReplay: a 3 s intro, then 5 s a putt); the pose file shares its clock.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class LabReplayScreen : AppScreen
    {
        readonly App _app;
        readonly LabSessionData _data;
        readonly bool _ownData;
        readonly VisualElement _stageBox, _frame, _hud, _card, _rail, _phases, _bar, _panel;
        readonly Image _picture;
        readonly BlazeOverlay _overlay;
        readonly BlazeOverlay.Lines _lines = new BlazeOverlay.Lines();
        readonly Label _note;
        SessionVideo _video;
        VisualElement _play, _eye, _faders, _save, _prev, _next;
        int _putt = -1, _run;
        bool _paused, _hudHidden, _showLines, _linesOn = true, _left, _saving;
        string _held;
        BodyFaults.Stroke _stroke;
        BodyFaults.Reading _reading;
        string _phaseKey;

        /// <summary>From the session page, sharing what it has loaded.</summary>
        public LabReplayScreen(App app, LabSessionData data) : this(app, data, false) { }

        /// <summary>From a session row (a deep link, a list): the screen loads the session itself.</summary>
        public LabReplayScreen(App app, object row) : this(app, new LabSessionData(row, false), true) { }

        LabReplayScreen(App app, LabSessionData data, bool own)
        {
            _app = app;
            _data = data;
            _ownData = own;

            Root = LabUi.Style(Ui.Box("lab-replay"));
            _picture = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            Fill(_picture);
            _overlay = new BlazeOverlay { Settings = _lines };
            _frame = new VisualElement { pickingMode = PickingMode.Ignore };
            _frame.style.position = Position.Absolute;
            _frame.Add(_picture);
            _frame.Add(_overlay);
            _stageBox = Ui.Box("lab-replay-stage", _frame);
            _stageBox.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            _stageBox.OnTap(() => { _hudHidden = !_hudHidden; _hud.style.display = _hudHidden ? DisplayStyle.None : DisplayStyle.Flex; });
            Root.Add(_stageBox);
            _note = Ui.Text("Loading the video...", "lab-replay-note", Ui.Medium);
            _note.pickingMode = PickingMode.Ignore;
            Root.Add(_note);

            _card = Ui.Box("lab-replay-card");
            _rail = Ui.Box("lab-replay-rail");
            _rail.pickingMode = PickingMode.Ignore;
            _phases = Ui.Box("lab-replay-phases");
            _panel = Ui.Box("lab-replay-panel");
            _bar = BuildBar();
            VisualElement bottom = Ui.Box("lab-replay-bottom", _phases, _panel, _bar);
            // Only the tiles, the panel and the bar take taps: the space around them is the video's (a tap there hides the HUD).
            bottom.pickingMode = _phases.pickingMode = PickingMode.Ignore;
            _hud = Ui.Box("lab-replay-hud", _card, _rail, bottom);
            _hud.pickingMode = PickingMode.Ignore;
            Root.Add(_hud);
            VisualElement close = LabUi.Round("X", 44, Ui.Surface, Leave, 20);
            close.AddToClassList("lab-replay-close");
            Root.Add(close);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Pad(close, bottom));

            // The texture and the overlay follow the video's clock; the putt and the HUD follow the playhead.
            Root.schedule.Execute(Tick).Every(16);
            DrawPanel();
            Begin();
            if (_ownData) _data.Start();
        }

        static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = e.style.top = e.style.right = e.style.bottom = 0;
        }

        void Pad(VisualElement close, VisualElement bottom)
        {
            Vector4 safe = _app.SafeInsets();
            close.style.top = 12 + safe.w;
            _card.style.top = 12 + safe.w;
            bottom.style.bottom = 18 + safe.y;
            _rail.style.top = 100 + safe.w;
        }

        void Begin()
        {
            _data.Changed += Loaded;
            Loaded();
            string url = LabReplay.Url(_data.Row);
            if (url.Length == 0) { _note.text = "This session's video is not ready."; return; }
            _video = new SessionVideo(url);
            _video.Loop = true;
            _picture.image = _video.Texture;
            App.Run(Open(++_run));
        }

        IEnumerator Open(int my)
        {
            yield return _video.WaitReady(20);
            if (my != _run || _video == null) yield break;
            if (!_video.Ready) { _note.text = "Could not load the video. Check your connection."; yield break; }
            _note.text = "";
            _video.Play();
            DrawBar();
        }

        void Loaded()
        {
            if (_left) return;
            _overlay.Load(_data.Blaze);
            _putt = -1; // read the current putt again with what arrived
        }

        // The portrait replay covering the screen, centred (resizeMode cover).
        void Fit()
        {
            float W = _stageBox.layout.width, H = _stageBox.layout.height;
            if (float.IsNaN(W) || W <= 0 || H <= 0) return;
            float h = H, w = h * 9f / 16f;
            if (w < W) { w = W; h = w * 16f / 9f; }
            _frame.style.width = w;
            _frame.style.height = h;
            _frame.style.left = (W - w) / 2;
            _frame.style.top = (H - h) / 2;
        }

        public override void Hidden() { if (_video != null && _video.Ready && !_paused) { _video.Pause(); _paused = true; DrawBar(); } }

        void Close()
        {
            if (_left) return;
            _left = true;
            _run++;
            _data.Changed -= Loaded;
            if (_ownData) _data.Close();
            _video?.Dispose();
            _video = null;
        }

        void Leave()
        {
            Close();
            _app.Pop();
        }

        int Total => _data.Putts != null && _data.Putts.Count > 0 ? _data.Putts.Count : 6;

        // puttIndexAtTime.
        int PuttAt(double t) => Mathf.Clamp((int)Math.Floor((t - LabReplay.Intro) / LabReplay.Clip), 0, Total - 1);

        void Tick()
        {
            if (_left) return;
            double t = _video != null && _video.Ready ? _video.Time : LabReplay.SlotStart(Math.Max(0, _putt));
            if (_video != null && _video.Playing) { _picture.MarkDirtyRepaint(); Perf.Wake(0.5f); }
            _overlay.Time = t;
            int at = _video != null && _video.Ready ? PuttAt(t) : Math.Max(0, _putt);
            if (at != _putt) SetPutt(at);
            // A phase lights its border as the playhead crosses it.
            string key = (_paused ? "p" : "") + _held + "|" + PhaseCrossing(t);
            if (key != _phaseKey) { _phaseKey = key; DrawPhases(t); }
        }

        void SetPutt(int j)
        {
            _putt = j;
            (double s, double e) = BodyFaults.Window(j);
            _stroke = BodyFaults.Analyze(_data.Pose, s, e, _data.Lefty);
            _reading = BodyFaults.Read(_stroke, _data.Lefty);
            // The anchor for the wrist trail and head marker: the putt's address, else its window's start.
            _overlay.Anchor = _stroke != null && !double.IsNaN(_stroke.Setup) ? _stroke.Setup : s;
            _held = null;
            DrawCard();
            DrawRail();
            DrawBar();
        }

        // ── The HUD ─────────────────────────────────────────────────────────────────────────────────────

        void DrawCard()
        {
            _card.Clear();
            if (_data.Putts == null || _putt < 0 || _putt >= _data.Putts.Count) return;
            _card.Add(LabUi.PuttCard(_data.Putts[_putt], _putt, true, Root));
        }

        void DrawRail()
        {
            _rail.Clear();
            List<(string value, string label, Color colour)> reads = LabUi.Reads(_reading, _data.Lefty);
            if (reads.Count == 0) return;
            // Opposite the Player: right for a right-handed putter, left for a left-handed one.
            _rail.EnableInClassList("lab-replay-rail-left", _data.Lefty);
            _rail.Add(Ui.Caps(_reading.DownTheLine ? "Down the line" : "Face on"));
            foreach (var r in reads)
                _rail.Add(Ui.Box("lab-rail-pill", LabUi.Coloured(r.value, "lab-rail-value", r.colour), Ui.Text(r.label, "lab-rail-text", Ui.Medium)));
        }

        List<(string key, string label, double time, string value)> PhaseList()
        {
            var list = new List<(string, string, double, string)>();
            BodyFaults.Stroke a = _stroke;
            if (a == null || double.IsNaN(a.Setup) || double.IsNaN(a.Back) || double.IsNaN(a.Impact)) return list;
            string Sec(double v) => v >= 0 ? v.ToString("0.00", CultureInfo.InvariantCulture) + "s" : null;
            list.Add(("setup", "SETUP", a.Setup, null));
            list.Add(("back", "BACKSTROKE", a.Back, Sec(a.Back - a.Setup)));
            list.Add(("impact", "IMPACT", a.Impact, Sec(a.Impact - a.Back)));
            if (!double.IsNaN(a.Follow)) list.Add(("follow", "FOLLOW", a.Follow, Sec(a.Follow - a.Impact)));
            return list;
        }

        string PhaseCrossing(double t)
        {
            if (_paused) return "";
            foreach (var ph in PhaseList()) if (Math.Abs(t - ph.time) < 0.12) return ph.key;
            return "";
        }

        void DrawPhases(double t)
        {
            _phases.Clear();
            if (_showLines) return;
            string crossing = PhaseCrossing(t);
            foreach (var ph in PhaseList())
            {
                var p = ph;
                VisualElement tile = Ui.Box("lab-phase", Ui.Text(p.label, "lab-phase-label", Ui.Bold));
                if (p.value != null) tile.Add(Ui.Text(p.value, "lab-phase-value", Ui.Bold));
                bool held = _paused && _held == p.key;
                LabUi.Border(tile, 1, held ? Color.white : crossing == p.key ? Ui.Faint : Ui.Line);
                tile.OnTap(() => SeekPhase(p.key, p.time));
                _phases.Add(tile);
            }
        }

        VisualElement BuildBar()
        {
            _eye = LabUi.Round("Eye", 44, LabUi.Surface3, () => { _linesOn = !_linesOn; _overlay.style.display = _linesOn ? DisplayStyle.Flex : DisplayStyle.None; DrawBar(); });
            _faders = LabUi.Round("Faders", 44, Ui.Surface2, () => { _showLines = !_showLines; DrawPanel(); DrawBar(); _phaseKey = null; });
            _prev = LabUi.Round("SkipBack", 44, Ui.Surface2, () => JumpToPutt(_putt - 1));
            _play = LabUi.Round("Pause", 52, LabUi.Surface3, TogglePlay, 24);
            _next = LabUi.Round("SkipForward", 44, Ui.Surface2, () => JumpToPutt(_putt + 1));
            _save = LabUi.Round("DownloadSimple", 44, Ui.Surface2, Save);
            return Ui.Box("lab-ctrl",
                Ui.Box("row grow lab-ctrl-left", _eye, _faders),
                Ui.Box("row lab-ctrl-mid", _prev, _play, _next),
                Ui.Box("row grow lab-ctrl-right", _save));
        }

        void DrawBar()
        {
            if (_bar == null) return;
            Swap(_eye, _linesOn ? "Eye" : "EyeSlash", 18, _linesOn ? Color.white : Ui.Faint);
            _eye.style.backgroundColor = _linesOn ? LabUi.Surface3 : Ui.Surface2;
            // The overlay panel open: the picked state, white with a black icon, as a chip that is on.
            Swap(_faders, "Faders", 18, _showLines ? Color.black : Color.white);
            _faders.style.backgroundColor = _showLines ? Color.white : Ui.Surface2;
            Swap(_play, _paused || _video == null ? "Play" : "Pause", 24, Color.white);
            _prev.style.opacity = _putt <= 0 ? 0.4f : 1f;
            _next.style.opacity = _putt >= Total - 1 ? 0.4f : 1f;
            _save.Clear();
            _save.Add(_saving ? Spinner.Small() : (VisualElement)LabUi.Icon("DownloadSimple", 18, Color.white));
        }

        static void Swap(VisualElement button, string icon, float size, Color tint)
        {
            button.Clear();
            button.Add(LabUi.Icon(icon, size, tint));
        }

        void DrawPanel()
        {
            _panel.Clear();
            _panel.style.display = _showLines ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_showLines) return;
            _panel.Add(Ui.Box("row lab-panel-head", Ui.Text("Overlay lines", "lab-panel-title", Ui.Bold),
                Ui.Box("lab-panel-x", Ui.Icon("X", 16, Ui.Faint)).OnTap(() => { _showLines = false; DrawPanel(); DrawBar(); _phaseKey = null; })));
            _panel.Add(LabUi.OverlaySwitches(_lines, _overlay.Changed));
        }

        // ── Controls ────────────────────────────────────────────────────────────────────────────────────

        void TogglePlay()
        {
            if (_video == null || !_video.Ready) return;
            _held = null;
            _run++;
            if (_paused) { _video.Play(); _paused = false; }
            else { _video.Pause(); _paused = true; }
            DrawBar();
        }

        void JumpToPutt(int i)
        {
            int j = Mathf.Clamp(i, 0, Total - 1);
            if (_video == null || !_video.Ready) return;
            _held = null;
            double t = LabReplay.SlotStart(j);
            if (_paused) App.Run(FreezeAt(++_run, t)); else _video.Jump(t);
            SetPutt(j);
        }

        // A phase tile: freeze on that moment of the stroke.
        void SeekPhase(string key, double t)
        {
            if (_video == null || !_video.Ready) return;
            _held = key;
            _paused = true;
            _video.Pause();
            App.Run(FreezeAt(++_run, t));
            DrawBar();
        }

        IEnumerator FreezeAt(int my, double t)
        {
            yield return _video.Freeze(t);
            if (my != _run || _video == null) yield break;
            _picture.MarkDirtyRepaint();
        }

        void Save()
        {
            string url = J.Str(_data.Row, "replayUrl");
            if (!url.StartsWith("http")) { _app.Toast("This session's video is not ready."); return; }
            App.Run(LabUi.SaveVideo(_app, url, _data.Id, busy => { _saving = busy; DrawBar(); }));
        }
    }
}
