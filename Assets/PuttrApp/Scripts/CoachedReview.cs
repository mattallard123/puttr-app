// The coach's review of a 3D session, ported from puttr-app components/lab/CoachedReview.js: a lean-back,
// narrated walkthrough of the tape. A short greeting, the best stroke, then each thing to fix (deduped by
// fault, grouped per putt, worst first), then a clean word for any camera angle with nothing to fix. For each
// moment the replay freezes on the telling frame with ONE overlay while the coach speaks over the avatar and
// tip box, then the putt plays through to the ball dropping. Then the plan: the top fault and the coach's
// written fix (ai.puttr.co /v2/coach/analyze), the lessons it points at, and Ask {coach} to keep talking.
//
//   timing   LabReplayTiming.js        (LabReplay below)
//   faults   LabFunctions.js           (BodyFaults.Analyze)
//   overlay  AnnotationOverlayBlaze.js (BlazeOverlay below: eyes, shoulders, body, all)
//   voice    coachVoice.js speakAsCoach (CoachVoice.Say; with the voice off a line stays up for a reading time)
//   words    POST /v2/coach/narrate {personaId, firstName, moments: facts}: used only when one line comes back per moment
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace PuttrApp
{
    /// <summary>
    /// Where each putt lives in a lab replay (puttr-app LabReplayTiming.js; Shotstack in puttr-api video.js): a 3 s intro
    /// card, then a 5 s slot per putt ending on the result (the ball at the hole). The pose file shares this clock.
    /// </summary>
    public static class LabReplay
    {
        public const double Intro = 3, Clip = 5;
        public static double SlotStart(int j) => Intro + j * Clip;
        public static double ResultTime(int j) => Intro + (j + 1) * Clip;
        /// <summary>The start of putt j's stroke window (the last 3 s of its slot).</summary>
        public static double StrokeStart(int j) => SlotStart(j) + Clip - 3;

        /// <summary>The stitched replay, or the raw upload when there is none yet; "" when neither is a link.</summary>
        public static string Url(object session)
        {
            string url = J.Str(session, "replayUrl");
            if (!url.StartsWith("http")) url = J.Str(session, "videoUrl");
            return url.StartsWith("http") ? url : "";
        }

        /// <summary>Whether a lab putt dropped: its entry is a hole entry ([side, ...]) and not the empty [0,0...].</summary>
        public static bool Made(object putt)
        {
            object e = J.Get(putt, "entry");
            if (e is string s) return s.StartsWith("[") ? !s.StartsWith("[0,0") : s.Length > 0 && s != "0";
            return e != null && J.Num(putt, "entry") != 0;
        }
    }

    /// <summary>A session's replay video drawn into a texture: freeze on a frame, play to a time, pause.</summary>
    public sealed class SessionVideo
    {
        readonly GameObject _go;
        readonly VideoPlayer _p;
        public readonly RenderTexture Texture;
        public bool Ready, Failed;
        bool _seeked, _frame;

        public SessionVideo(string url)
        {
            _go = new GameObject("Session video");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _p = _go.AddComponent<VideoPlayer>();
            Texture = new RenderTexture(540, 960, 0);
            _p.playOnAwake = false;
            _p.renderMode = VideoRenderMode.RenderTexture;
            _p.targetTexture = Texture;
            _p.source = VideoSource.Url;
            _p.url = url;
            _p.audioOutputMode = VideoAudioOutputMode.None;
            // The picture keeps its own clock. Left on the sound system's clock (the default), a phone whose audio has
            // been taken or let go by the camera's recorder never moves the video on: it loads and then sits still.
            _p.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            _p.isLooping = false;
            _p.waitForFirstFrame = true;
            _p.sendFrameReadyEvents = true;
            _p.prepareCompleted += _ => Ready = true;
            _p.errorReceived += (_, message) => { Failed = true; Perf.Note("video: " + message); };
            _p.seekCompleted += _ => { _seeked = true; _frame = false; };
            _p.frameReady += (_, __) => _frame = true;
            _p.Prepare();
        }

        public double Time => Ready ? _p.time : 0;
        public bool Playing => Ready && _p.isPlaying;

        static float Now => UnityEngine.Time.realtimeSinceStartup;

        public IEnumerator WaitReady(float seconds)
        {
            float since = Now;
            while (!Ready && !Failed && Now - since < seconds) yield return null;
        }

        /// <summary>Shows the frame at t and holds it.</summary>
        public IEnumerator Freeze(double t)
        {
            if (!Ready) yield break;
            _seeked = false; _frame = false;
            _p.time = Math.Max(0, t);
            _p.Play();
            float since = Now;
            while (!(_seeked && _frame) && Now - since < 2.5f) { Perf.Wake(0.5f); yield return null; }
            _p.Pause();
        }

        /// <summary>Plays from where it is to end (with a wall-clock net in case the clock never gets there).</summary>
        public IEnumerator PlayTo(double end, Func<bool> alive)
        {
            if (!Ready) yield break;
            _p.Play();
            float since = Now, most = (float)Math.Max(1.5, end - _p.time + 1.2);
            while (alive() && _p.time < end && Now - since < most) { Perf.Wake(0.5f); yield return null; }
            _p.Pause();
        }

        public void Pause() { if (Ready) _p.Pause(); }

        // ── Free playback, for the manual replay and the coach's analysis (react-native-video with repeat on) ──

        /// <summary>The replay's length in seconds (0 until it is prepared).</summary>
        public double Length => Ready ? _p.length : 0;

        /// <summary>Plays from where it is; with Loop on it starts over at the end, as the app's repeat does.</summary>
        public void Play() { if (Ready) _p.Play(); }

        public bool Loop { get => _p.isLooping; set => _p.isLooping = value; }

        /// <summary>Moves the playhead while playing (the picture follows on the next frame).</summary>
        public void Jump(double t)
        {
            if (!Ready) return;
            double end = Length > 0 ? Length - 0.05 : t;
            _p.time = Math.Max(0, Math.Min(end, t));
        }

        /// <summary>The playback rate (1, 0.5, 0.25).</summary>
        public float Rate { get => _p.playbackSpeed; set => _p.playbackSpeed = value; }

        /// <summary>The replay's address (to save it).</summary>
        public string Url => _p.url;

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            Texture.Release();
        }
    }

    /// <summary>
    /// The pose lines over the video (puttr-app AnnotationOverlayBlaze.js), from the pose file's image landmarks at a
    /// time. Mode lights up only what the coach is talking about: eyes (eye lines to the floor), shoulders (the
    /// shoulder line), body (the skeleton, shoulder lines to the floor, the hip line), all, or none.
    /// </summary>
    public sealed class BlazeOverlay : VisualElement
    {
        sealed class Frame { public double T; public Dictionary<string, Vector3> J; } // x, y (0 to 1 of the picture), visibility

        static readonly string[,] Bones =
        {
            { "right_ankle", "right_heel" }, { "left_ankle", "left_heel" }, { "left_foot_index", "left_heel" }, { "right_foot_index", "right_heel" },
            { "left_knee", "left_ankle" }, { "right_knee", "right_ankle" }, { "left_hip", "left_knee" }, { "right_hip", "right_knee" },
            { "left_hip", "left_shoulder" }, { "right_hip", "right_shoulder" },
            { "left_shoulder", "left_elbow" }, { "right_shoulder", "right_elbow" }, { "left_elbow", "left_wrist" }, { "right_elbow", "right_wrist" },
            { "right_wrist", "right_thumb" }, { "left_wrist", "left_thumb" },
        };
        static readonly string[] Joints = { "left_shoulder", "right_shoulder", "left_elbow", "right_elbow", "left_wrist", "right_wrist", "left_hip", "right_hip", "left_knee", "right_knee", "left_ankle", "right_ankle", "left_heel", "right_heel", "left_thumb", "right_thumb" };
        static readonly HashSet<string> Primary = new HashSet<string> { "left_shoulder", "right_shoulder", "left_hip", "right_hip", "left_elbow", "right_elbow", "left_wrist", "right_wrist", "left_knee", "right_knee" };

        readonly List<Frame> _frames = new List<Frame>();
        string _mode = "none";
        double _time = -1;

        public BlazeOverlay()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent += Draw;
        }

        public string Mode { get => _mode; set { if (_mode == value) return; _mode = value; MarkDirtyRepaint(); } }
        public double Time { get => _time; set { if (Math.Abs(_time - value) < 0.001) return; _time = value; MarkDirtyRepaint(); } }

        public void Load(object blaze)
        {
            _frames.Clear();
            var map = J.Obj(blaze);
            if (map == null) return;
            foreach (KeyValuePair<string, object> e in map)
            {
                var lm = J.Obj(J.Get(e.Value, "landmarks"));
                if (lm == null || J.Get(e.Value, "timestamp") == null) continue;
                var f = new Frame { T = J.Num(e.Value, "timestamp"), J = new Dictionary<string, Vector3>() };
                foreach (KeyValuePair<string, object> p in lm)
                    f.J[p.Key] = new Vector3((float)J.Num(p.Value, "x"), (float)J.Num(p.Value, "y"), (float)J.Num(p.Value, "visibility", 1));
                _frames.Add(f);
            }
            _frames.Sort((a, b) => a.T.CompareTo(b.T));
            MarkDirtyRepaint();
        }

        // The latest frame at or before t.
        Frame At(double t)
        {
            int lo = 0, hi = _frames.Count - 1, found = -1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (_frames[mid].T <= t) { found = mid; lo = mid + 1; } else hi = mid - 1; }
            return found < 0 ? null : _frames[found];
        }

        /// <summary>
        /// The seven overlay switches of the manual replay and the coach's analysis (AnnotationControls): eye lines and
        /// shoulder lines to the floor, the skeleton, the shoulder and hip lines, the wrist trail and the head marker.
        /// The wrist trail and the head marker are drawn only around the putt's setup (Anchor).
        /// </summary>
        public sealed class Lines
        {
            public bool EyeLine = true, ShoulderDrop = true, Body = true, Shoulders = true, Hips = true, Wrist, Head;

            public static readonly (string key, string label)[] All =
            {
                ("eye", "Eye line"), ("drop", "Shoulder drop"), ("body", "Body"), ("shoulders", "Shoulders"), ("hips", "Hips"), ("wrist", "Wrist"), ("head", "Head"),
            };

            public bool this[string key]
            {
                get
                {
                    switch (key)
                    {
                        case "eye": return EyeLine; case "drop": return ShoulderDrop; case "body": return Body; case "shoulders": return Shoulders;
                        case "hips": return Hips; case "wrist": return Wrist; case "head": return Head; default: return false;
                    }
                }
                set
                {
                    switch (key)
                    {
                        case "eye": EyeLine = value; break; case "drop": ShoulderDrop = value; break; case "body": Body = value; break; case "shoulders": Shoulders = value; break;
                        case "hips": Hips = value; break; case "wrist": Wrist = value; break; case "head": Head = value; break;
                    }
                }
            }

            public bool Any { get { foreach (var t in All) if (this[t.key]) return true; return false; } }
            public void SetAll(bool on) { foreach (var t in All) this[t.key] = on; }
        }

        Lines _lines;
        double _anchor = double.NaN;

        /// <summary>When set, the overlay draws by these switches (the manual replay, the coach's analysis) instead of Mode.</summary>
        public Lines Settings { get => _lines; set { _lines = value; MarkDirtyRepaint(); } }

        /// <summary>The selected putt's setup time: the wrist trail and head marker show from 1 s before it to 5 s after.</summary>
        public double Anchor { get => _anchor; set { if (_anchor.Equals(value)) return; _anchor = value; MarkDirtyRepaint(); } }

        /// <summary>Redraws with the switches as they are now.</summary>
        public void Changed() => MarkDirtyRepaint();

        static readonly Color Outline = new Color32(0x14, 0x14, 0x14, 0xFF);
        static readonly Color Guide = new Color32(0xC8, 0xC8, 0xC8, 0xFF);

        // AnnotationOverlayBlaze.js with its switches, drawn in solid colours (dark outlines under white lines).
        void DrawLines(MeshGenerationContext mgc)
        {
            Frame f = At(_time);
            if (f == null) return;
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0) return;
            Painter2D p = mgc.painter2D;
            Vector2? PtOf(Frame fr, string n) => fr != null && fr.J.TryGetValue(n, out Vector3 v) && v.z > 0.4f ? new Vector2(v.x * w, v.y * h) : (Vector2?)null;
            Vector2? Pt(string n) => PtOf(f, n);

            void Floor(string n)
            {
                if (!(Pt(n) is Vector2 a)) return;
                var b = new Vector2(a.x, h);
                Line(p, a, b, Outline, 3f, 3, 4);
                Line(p, a, b, Guide, 1.75f, 3, 4);
            }
            void Join(string n1, string n2)
            {
                if (!(Pt(n1) is Vector2 a) || !(Pt(n2) is Vector2 b)) return;
                Line(p, a, b, Outline, 3.5f, 4, 3);
                Line(p, a, b, Color.white, 2f, 4, 3);
            }
            Vector2? Wrists(Frame fr)
            {
                Vector2? l = PtOf(fr, "left_wrist"), r = PtOf(fr, "right_wrist");
                if (l != null && r != null) return (l.Value + r.Value) / 2;
                return l ?? r;
            }
            Vector2? HeadOf(Frame fr)
            {
                if (PtOf(fr, "nose") is Vector2 nose) return nose;
                Vector2? le = PtOf(fr, "left_eye"), re = PtOf(fr, "right_eye");
                if (le != null && re != null) return (le.Value + re.Value) / 2;
                return le ?? re;
            }
            bool inWindow = !double.IsNaN(_anchor) && _time >= _anchor - 1 && _time <= _anchor + 5;

            if (_lines.EyeLine) { Floor("left_eye"); Floor("right_eye"); }
            if (_lines.ShoulderDrop) { Floor("left_shoulder"); Floor("right_shoulder"); }

            // The wrist trail through the putt, from the setup to now (at most 4 s), every other frame.
            if (_lines.Wrist && inWindow)
            {
                var pts = new List<Vector2>();
                double to = Math.Min(_time, _anchor + 4);
                int k = 0;
                foreach (Frame fr in _frames)
                {
                    if (fr.T < _anchor || fr.T > to) continue;
                    if (Wrists(fr) is Vector2 v && (k++ % 2 == 0)) pts.Add(v);
                }
                if (pts.Count >= 2)
                {
                    foreach (var (colour, width) in new[] { (Outline, 5f), (Ui.Green, 3f) })
                    {
                        p.strokeColor = colour; p.lineWidth = width; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                        p.BeginPath(); p.MoveTo(pts[0]); for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]); p.Stroke();
                    }
                    Dot(p, pts[pts.Count - 1], 5, Ui.Green);
                }
            }

            if (_lines.Body)
            {
                for (int i = 0; i < Bones.GetLength(0); i++)
                {
                    if (!(Pt(Bones[i, 0]) is Vector2 a) || !(Pt(Bones[i, 1]) is Vector2 b)) continue;
                    Line(p, a, b, Outline, 4f, 0, 0);
                    Line(p, a, b, Color.white, 2.5f, 0, 0);
                }
                foreach (string n in Joints)
                {
                    if (!(Pt(n) is Vector2 c)) continue;
                    float r = Primary.Contains(n) ? 6 : 4;
                    Dot(p, c, r + 1.5f, Outline);
                    Dot(p, c, r, Color.white);
                }
            }
            if (_lines.Hips) Join("left_hip", "right_hip");
            if (_lines.Shoulders) Join("left_shoulder", "right_shoulder");

            // Head stillness: the head at address, and where it is now, green, amber then red as it drifts.
            if (_lines.Head && inWindow && HeadOf(At(_anchor)) is Vector2 anchor)
            {
                Vector2? head = HeadOf(f);
                Color drift = Ui.Bright;
                if (head != null)
                {
                    float d = Vector2.Distance(head.Value, anchor), tol = w * 0.03f;
                    drift = d > tol * 2 ? Ui.Red : d > tol ? Ui.Amber : (Color)Ui.Bright;
                }
                Ring(p, anchor, 13, Outline, 3.5f);
                Ring(p, anchor, 13, drift, 2f);
                Dot(p, anchor, 2.5f, drift);
                if (head != null)
                {
                    Line(p, anchor, head.Value, Outline, 3f, 0, 0);
                    Line(p, anchor, head.Value, drift, 1.75f, 3, 3);
                    Dot(p, head.Value, 6.5f, Outline);
                    Dot(p, head.Value, 5f, drift);
                }
            }
        }

        static void Ring(Painter2D p, Vector2 c, float r, Color colour, float width)
        {
            p.strokeColor = colour;
            p.lineWidth = width;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360));
            p.Stroke();
        }

        void Draw(MeshGenerationContext mgc)
        {
            if (_lines != null) { if (_frames.Count > 0) DrawLines(mgc); return; }
            if (_mode == "none" || _frames.Count == 0) return;
            Frame f = At(_time);
            if (f == null) return;
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0) return;
            Painter2D p = mgc.painter2D;
            Vector2? Pt(string n) => f.J.TryGetValue(n, out Vector3 v) && v.z > 0.4f ? new Vector2(v.x * w, v.y * h) : (Vector2?)null;
            bool all = _mode == "all", body = all || _mode == "body";
            // Solid colours, as DrawLines above: dark outlines under white lines, no halo.
            Color shade = Outline;

            void Guide(string n)
            {
                if (!(Pt(n) is Vector2 a)) return;
                var b = new Vector2(a.x, h);
                Line(p, a, b, shade, 3f, 3, 4);
                Line(p, a, b, BlazeOverlay.Guide, 1.75f, 3, 4);
            }
            void Join(string n1, string n2)
            {
                if (!(Pt(n1) is Vector2 a) || !(Pt(n2) is Vector2 b)) return;
                Line(p, a, b, shade, 3.5f, 4, 3);
                Line(p, a, b, Color.white, 2f, 4, 3);
            }

            if (all || _mode == "eyes") { Guide("left_eye"); Guide("right_eye"); }
            if (body) { Guide("left_shoulder"); Guide("right_shoulder"); }
            if (body)
            {
                for (int i = 0; i < Bones.GetLength(0); i++)
                {
                    if (!(Pt(Bones[i, 0]) is Vector2 a) || !(Pt(Bones[i, 1]) is Vector2 b)) continue;
                    Line(p, a, b, shade, 4f, 0, 0);
                    Line(p, a, b, Color.white, 2.5f, 0, 0);
                }
                foreach (string n in Joints)
                {
                    if (!(Pt(n) is Vector2 c)) continue;
                    float r = Primary.Contains(n) ? 6 : 4;
                    Dot(p, c, r + 1.5f, shade);
                    Dot(p, c, r, Color.white);
                }
                Join("left_hip", "right_hip");
            }
            if (all || _mode == "shoulders") Join("left_shoulder", "right_shoulder");
        }

        static void Line(Painter2D p, Vector2 a, Vector2 b, Color colour, float width, float dash, float gap)
        {
            p.strokeColor = colour;
            p.lineWidth = width;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            float len = Vector2.Distance(a, b);
            if (dash <= 0 || len < 0.5f) { p.MoveTo(a); p.LineTo(b); }
            else
            {
                Vector2 d = (b - a) / len;
                for (float s = 0; s < len; s += dash + gap) { p.MoveTo(a + d * s); p.LineTo(a + d * Mathf.Min(len, s + dash)); }
            }
            p.Stroke();
        }

        static void Dot(Painter2D p, Vector2 c, float r, Color colour)
        {
            p.fillColor = colour;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360));
            p.Fill();
        }
    }

    /// <summary>The coach's narrated walkthrough of a 3D session, then the plan (puttr-app CoachedReview.js).</summary>
    public sealed class CoachedReviewScreen : AppScreen
    {
        sealed class Moment
        {
            public string Kind, Overlay = "all", Verdict = "neutral", Label, Line;
            public int Putt = -1;
            public double Seek = 1;
            public List<string> Captions = new List<string>();
            public Dictionary<string, object> Facts;
        }

        sealed class PerPutt { public object Row; public int Index; public bool Made; public BodyFaults.Result Data; public VertexPutt Club; }

        enum Stage { Loading, Play, Plan }

        // Fault → which overlay best illustrates it.
        static readonly Dictionary<string, string> FaultOverlay = new Dictionary<string, string>
        {
            { "shoulder_open", "shoulders" }, { "shoulder_closed", "shoulders" }, { "shoulder_over_toe", "shoulders" }, { "shoulder_inside_toe", "shoulders" },
            { "eye_line_inside", "eyes" }, { "eye_line_outside", "eyes" },
            { "knees_straight", "body" }, { "ball_forward", "body" }, { "ball_back", "body" }, { "weight_left", "body" }, { "weight_right", "body" }, { "stance_narrow", "body" }, { "stance_wide", "body" },
        };

        // The tip box and its tags: solid, the screen's own ground (the project is linear colour: nothing see-through).
        static readonly Color Panel = Ui.Base;
        static readonly Color Surface3 = new Color32(48, 53, 58, 255);

        readonly App _app;
        readonly object _session;
        readonly string _id;
        readonly bool _lefty;
        readonly Coaches.Persona _coach;
        List<object> _putts;
        object _blaze;
        List<Moment> _moments;
        BodyFaults.Fault _topFault;

        SessionVideo _video;
        readonly VisualElement _stageBox, _frame, _top, _bottom, _loading, _planBox;
        readonly Image _picture;
        readonly BlazeOverlay _overlay;
        IVisualElementScheduledItem _tick;

        Stage _stage = Stage.Loading;
        int _index, _run;
        bool _paused, _showTip = true, _playing, _left;

        // The plan
        bool _planAsked, _planLoading, _helpOpen;
        string _planHelp, _planConvo;
        List<object> _recLessons = new List<object>();

        public CoachedReviewScreen(App app, object session, List<object> putts)
        {
            _app = app;
            _session = session;
            _id = J.Str(session, "id");
            _lefty = J.Str(session, "handedness").ToUpperInvariant().StartsWith("L");
            _coach = CoachVoice.Persona;
            _putts = putts != null && putts.Count > 0 ? putts : null;

            Root = new VisualElement();
            Root.style.flexGrow = 1;
            Root.style.backgroundColor = Color.black;

            // The video, cover-fitted, with the overlay on exactly the same rectangle. A tap pauses or resumes the coach.
            _picture = new Image { scaleMode = ScaleMode.StretchToFill };
            Fill(_picture);
            _overlay = new BlazeOverlay();
            _frame = new VisualElement();
            _frame.style.position = Position.Absolute;
            _frame.Add(_picture);
            _frame.Add(_overlay);
            _stageBox = new VisualElement();
            Fill(_stageBox);
            _stageBox.style.overflow = Overflow.Hidden;
            _stageBox.Add(_frame);
            _stageBox.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            _stageBox.OnTap(TogglePause);
            Root.Add(_stageBox);

            _loading = Ui.Box("", Spinner.Big(), Ui.Text(_coach.Name + " is reviewing your session", "body-2 center", Ui.Medium).Margin(16));
            _loading.pickingMode = PickingMode.Ignore;
            _loading.style.position = Position.Absolute;
            _loading.style.left = _loading.style.right = 0;
            _loading.style.bottom = 80;
            _loading.style.alignItems = Align.Center;
            Root.Add(_loading);

            _bottom = new VisualElement();
            _bottom.style.position = Position.Absolute;
            _bottom.style.left = _bottom.style.right = _bottom.style.bottom = 0;
            Root.Add(_bottom);

            _planBox = new VisualElement();
            Fill(_planBox);
            _planBox.style.display = DisplayStyle.None;
            Root.Add(_planBox);

            _top = new VisualElement();
            _top.style.position = Position.Absolute;
            _top.style.left = 16; _top.style.right = 16; _top.style.top = 8;
            _top.style.flexDirection = FlexDirection.Row;
            _top.style.alignItems = Align.Center;
            Root.Add(_top);
            DrawTop();

            // While the clip plays: the texture and the overlay follow the video's clock.
            _tick = Root.schedule.Execute(() =>
            {
                if (_video == null || !_video.Playing) return;
                _overlay.Time = _video.Time;
                _picture.MarkDirtyRepaint();
                Perf.Wake(0.5f);
            }).Every(16);

            string url = LabReplay.Url(session);
            if (url.Length > 0) { _video = new SessionVideo(url); _picture.image = _video.Texture; }
            App.Run(Load(++_run));
        }

        static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = e.style.top = e.style.right = e.style.bottom = 0;
        }

        // The portrait replay (540 x 960) covering the screen, centred, as the app's resizeMode cover.
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

        public override void Hidden()
        {
            // Another screen on top (the coach chat), or this one gone: never talk over it.
            _run++;
            CoachVoice.Hush();
            _video?.Pause();
            _playing = false;
            if (_stage == Stage.Play && !_left) { _paused = true; DrawBottom(); }
        }

        void Leave()
        {
            _left = true;
            _run++;
            CoachVoice.Hush();
            _tick?.Pause();
            _video?.Dispose();
            _video = null;
            _app.Pop();
        }

        // ── Loading: the putts, the pose file, the script, the coach's words, the video ─────────────────

        IEnumerator Load(int my)
        {
            if (_putts == null)
            {
                List<object> rows = null;
                yield return Api.Get("/lab/session/putts/" + _id, d => rows = J.List(d), _ => { });
                // A coach reviewing a Player's session reads the putts from the coaching side.
                if (rows == null || rows.Count == 0) yield return Api.Get("/coaching/session/putts/" + _id, d => rows = J.List(d), _ => { });
                _putts = rows ?? new List<object>();
            }
            if (my != _run) yield break;
            if (_putts.Count == 0) { Stuck("There are no putts in this session to review yet."); yield break; }

            string poseUrl = J.Str(_session, "blazePoseUrl");
            if (poseUrl.StartsWith("http"))
                using (UnityWebRequest req = UnityWebRequest.Get(poseUrl))
                {
                    req.timeout = 60;
                    yield return req.SendWebRequest();
                    // Without it the review still runs: the greeting and the putts, no body faults.
                    if (req.result == UnityWebRequest.Result.Success) _blaze = J.Parse(req.downloadHandler.text);
                }
            if (my != _run) yield break;
            _overlay.Load(_blaze);

            Build();

            // The coach composes each line from the moment's facts in their own voice; the templates stand in on any failure.
            var body = new Dictionary<string, object>
            {
                { "personaId", _coach.Id }, { "firstName", HomeScreen.FirstName(_app.Profile) },
                { "moments", _moments.Select(m => (object)m.Facts).ToList() },
            };
            List<object> lines = null;
            yield return ChatUi.Post(ChatUi.Ai + "/v2/coach/narrate", body, (s, text) => { if (s == 200) lines = J.List(J.Get(J.Parse(text), "lines")); }, 12);
            if (my != _run) yield break;
            if (lines != null && lines.Count == _moments.Count && lines.All(l => l is string s && s.Trim().Length > 0))
                for (int i = 0; i < _moments.Count; i++) _moments[i].Line = ((string)lines[i]).Trim();
            // Fetch every line but the first ahead (the first is spoken at once).
            CoachVoice.Warm(_moments.Skip(1).Select(m => m.Line));

            if (_video != null) yield return _video.WaitReady(15);
            if (my != _run) yield break;
            if (_video != null && !_video.Ready) { Perf.Note("review: video not ready, going on without it"); _video.Dispose(); _video = null; _picture.image = null; }

            _loading.style.display = DisplayStyle.None;
            _stage = Stage.Play;
            _index = 0;
            DrawTop();
            Begin();
        }

        void Stuck(string words)
        {
            _loading.Clear();
            _loading.Add(Ui.Text(words, "body-2 center", Ui.Medium));
        }

        /// <summary>buildScript: the moments, in order, with the template line for each.</summary>
        void Build()
        {
            string first = HomeScreen.FirstName(_app.Profile);
            var per = new List<PerPutt>();
            for (int j = 0; j < _putts.Count; j++)
            {
                object row = _putts[j];
                BodyFaults.Result data = null;
                if (_blaze != null) { try { data = BodyFaults.Analyze(_blaze, j, _lefty); } catch (Exception) { data = null; } }
                VertexPutt club = null;
                try { club = VertexSummary.Read(row); } catch (Exception) { club = null; }
                per.Add(new PerPutt { Row = row, Index = j, Made = LabReplay.Made(row), Data = data, Club = club });
            }
            var moments = new List<Moment>();
            var signals = new Dictionary<string, object> { { "club", per.Any(x => x.Club != null) }, { "body", per.Any(x => x.Data != null) } };

            // ── Intro ──
            int made = per.Count(x => x.Made);
            string lean = Lean();
            string intro = "Nice work" + (first.Length > 0 ? ", " + first : "") + ". You made " + made + " of " + per.Count + " this session.";
            intro += lean != null ? " I'm seeing a " + lean + " creeping in. Let's look at a few together." : " Let's look at a few of these together.";
            moments.Add(new Moment
            {
                Kind = "intro", Overlay = "none", Seek = 1, Line = intro, Label = "Session review",
                Facts = new Dictionary<string, object> { { "kind", "intro" }, { "made", made }, { "total", per.Count }, { "lean", lean }, { "signals", signals } },
            });

            // ── Best stroke (lead with a win) ──
            PerPutt good = per.FirstOrDefault(x => x.Made && x.Data != null) ?? per.FirstOrDefault(x => x.Made) ?? per[0];
            {
                int dist = J.Int(good.Row, "distance");
                string line = good.Made
                    ? "Start with this one. You buried it from " + dist + " feet and the stroke looked repeatable. That's the feel we want to bottle."
                    : "This was your most solid stroke of the set. Good base, good rhythm. Remember this one.";
                if (good.Club != null && Math.Abs(good.Club.FaceChange) < 1)
                    line += " Your Vertex shows the face came through square" + (good.Club.Tempo > 0 ? ", " + R1(good.Club.Tempo) + " beats a minute" : "") + ".";
                var facts = new Dictionary<string, object> { { "kind", "good" }, { "putt", good.Index + 1 }, { "distance", dist }, { "made", good.Made } };
                if (good.Club != null) facts["club"] = Club(good.Club);
                moments.Add(new Moment
                {
                    Kind = "good", Putt = good.Index, Seek = good.Data?.SetupTime ?? LabReplay.StrokeStart(good.Index), Overlay = "all", Verdict = "good",
                    Label = "Putt " + (good.Index + 1) + " · your best", Captions = { good.Made ? "Made it, clean roll" : "Your most solid stroke" }, Line = line, Facts = facts,
                });
            }

            // ── Problems to fix: every fault once (its worst instance), grouped by the putt it is shown on ──
            var byKey = new Dictionary<string, (PerPutt x, BodyFaults.Fault f)>();
            foreach (PerPutt x in per)
            {
                if (x.Data == null) continue;
                foreach (BodyFaults.Fault f in x.Data.Faults)
                    if (!byKey.TryGetValue(f.Key, out var prev) || f.Severity > prev.f.Severity) byKey[f.Key] = (x, f);
            }
            var groups = byKey.Values.GroupBy(v => v.x.Index)
                .Select(g => (x: g.First().x, faults: g.Select(v => v.f).OrderByDescending(f => f.Severity).ToList()))
                .OrderByDescending(g => g.faults[0].Severity).ToList();
            foreach (var g in groups)
            {
                string miss = null;
                if (!g.x.Made) { try { miss = AppPutt.MissSide(PuttAssessment.Bp(g.x.Row), PuttAssessment.Hb(g.x.Row)); } catch (Exception) { miss = null; } }
                string lead = g.x.Made ? "This one dropped, but watch this. " : miss != null ? "This one leaked " + miss.ToLowerInvariant() + ", and here's why. " : "Look at why this one missed. ";
                var parts = new List<string>();
                for (int i = 0; i < g.faults.Count; i++)
                {
                    BodyFaults.Fault f = g.faults[i];
                    string l = BodyFaults.Lines.TryGetValue(f.Key, out string known) ? known : "At setup, " + f.Label.ToLowerInvariant() + ".";
                    parts.Add(i == 0 ? l : "Also, " + char.ToLowerInvariant(l[0]) + l.Substring(1));
                }
                string view = g.x.Data.DownTheLine ? "down the line" : "face on";
                var facts = new Dictionary<string, object>
                {
                    { "kind", "fix" }, { "putt", g.x.Index + 1 }, { "made", g.x.Made }, { "miss", g.x.Made || miss == null ? null : miss.ToLowerInvariant() },
                    { "faults", g.faults.Select(f => (object)f.Label).ToList() },
                };
                if (g.x.Club != null) facts["club"] = Club(g.x.Club);
                moments.Add(new Moment
                {
                    Kind = "fix", Putt = g.x.Index, Seek = g.x.Data.SetupTime, Verdict = "bad",
                    Overlay = g.faults.Count > 1 ? "all" : FaultOverlay.TryGetValue(g.faults[0].Key, out string o) ? o : "all",
                    Label = "Putt " + (g.x.Index + 1) + " · " + view, Captions = g.faults.Select(f => f.Label).ToList(),
                    Line = lead + string.Join(" ", parts) + FaceLine(g.x.Club), Facts = facts,
                });
            }
            _topFault = groups.Count > 0 ? groups[0].faults[0] : null;

            // ── Every camera angle filmed gets a word, even with nothing to fix ──
            var covered = new HashSet<bool>(groups.Select(g => g.x.Data.DownTheLine));
            if (good.Data != null) covered.Add(good.Data.DownTheLine);
            foreach (bool dtl in per.Where(x => x.Data != null).Select(x => x.Data.DownTheLine).Distinct().ToList())
            {
                if (covered.Contains(dtl)) continue;
                PerPutt c = per.FirstOrDefault(x => x.Data != null && x.Data.DownTheLine == dtl && x.Made) ?? per.FirstOrDefault(x => x.Data != null && x.Data.DownTheLine == dtl);
                if (c == null) continue;
                string view = dtl ? "down the line" : "face on";
                moments.Add(new Moment
                {
                    Kind = "positive", Putt = c.Index, Seek = c.Data.SetupTime, Overlay = "all", Verdict = "good",
                    Label = "Putt " + (c.Index + 1) + " · " + view, Captions = { dtl ? "Setup looks clean" : "Setup checks out" },
                    Line = dtl ? BodyFaults.CleanDtl : BodyFaults.CleanFaceOn,
                    Facts = new Dictionary<string, object> { { "kind", "positive" }, { "putt", c.Index + 1 }, { "view", view } },
                });
            }
            _moments = moments;
        }

        // getPuttAssessment's push or pull, when the session leans one way.
        string Lean()
        {
            var rows = new List<object>();
            foreach (object p in _putts)
            {
                var copy = J.Obj(p) != null ? new Dictionary<string, object>(J.Obj(p)) : new Dictionary<string, object>();
                copy["made"] = LabReplay.Made(p) ? 1 : 0;
                rows.Add(copy);
            }
            PuttAssessment a = PuttAssessment.Of(rows, _lefty ? "LEFTY" : "RIGHTY");
            return a.PushPull != "none" && a.PushPullPercent > 0 ? a.PushPull : null;
        }

        static double? R1(double v) => double.IsNaN(v) || double.IsInfinity(v) ? (double?)null : Math.Round(v * 10) / 10;

        static Dictionary<string, object> Club(VertexPutt v) =>
            new Dictionary<string, object> { { "faceChange", R1(v.FaceChange) }, { "tempo", R1(v.Tempo) }, { "rhythm", R1(v.Rhythm) } };

        // The spoken face-change aside: empty when the face was square.
        static string FaceLine(VertexPutt v)
        {
            if (v == null || Math.Abs(v.FaceChange) < 1) return "";
            string deg = Math.Abs(v.FaceChange).ToString("0.0", CultureInfo.InvariantCulture).Replace(".0", "");
            return " Your Vertex shows the face " + (v.FaceChange > 0 ? "closed" : "open") + " " + deg + " degrees at impact.";
        }

        // ── The walkthrough: freeze and speak, then play the putt, then the next moment ─────────────────

        static float Now => Time.realtimeSinceStartup;

        static IEnumerator Wait(float seconds)
        {
            float end = Now + seconds;
            while (Now < end) yield return null;
        }

        void Begin()
        {
            int my = ++_run;
            _paused = false;
            _playing = false;
            App.Run(RunMoment(my));
        }

        IEnumerator RunMoment(int my)
        {
            Moment m = _moments[_index];
            CoachVoice.Hush();
            _overlay.Mode = m.Overlay;
            _showTip = true;
            DrawBottom();

            // 1. Hold the telling frame (the setup frame for a putt).
            _overlay.Time = m.Seek;
            if (_video != null) { yield return _video.Freeze(m.Seek); _picture.MarkDirtyRepaint(); }
            if (my != _run) yield break;
            yield return Wait(0.35f);
            if (my != _run) yield break;

            // 2. The coach says it over the still. With the voice off, or a line that never arrives, the tip stays up
            // for a reading time so the walkthrough never stalls or flashes past.
            float began = Now;
            if (CoachVoice.On) { CoachVoice.Say(m.Line); while (CoachVoice.Speaking && my == _run) yield return null; }
            if (my != _run) yield break;
            float reading = Mathf.Max(3.8f, m.Line.Length * 0.055f);
            if (!CoachVoice.On || Now - began < m.Line.Length * 0.03f)
                while (Now - began < reading && my == _run) yield return null;
            if (my != _run) yield break;

            // 3. Then the putt plays from the still through the ball dropping (the clip runs about a second past the
            // result), with the tip box out of the way.
            if (m.Putt >= 0 && _video != null)
            {
                _showTip = false;
                DrawBottom();
                yield return Wait(0.15f);
                if (my != _run) yield break;
                _playing = true;
                yield return _video.PlayTo(LabReplay.ResultTime(m.Putt) + 1, () => my == _run);
                _playing = false;
                if (my != _run) yield break;
            }

            // 4. A beat, then on.
            yield return Wait(0.45f);
            if (my != _run) yield break;
            if (_index + 1 < _moments.Count) { _index++; Begin(); } else ToPlan();
        }

        void TogglePause()
        {
            if (_stage != Stage.Play) return;
            if (_paused) { Begin(); return; }
            _paused = true;
            _run++;
            CoachVoice.Hush();
            _video?.Pause();
            _playing = false;
            DrawBottom();
        }

        void Next()
        {
            if (_stage != Stage.Play) return;
            CoachVoice.Hush();
            if (_index + 1 < _moments.Count) { _index++; Begin(); } else ToPlan();
        }

        void Previous()
        {
            if (_stage != Stage.Play || _index == 0) return;
            CoachVoice.Hush();
            _index--;
            Begin();
        }

        // ── Drawing the walkthrough ─────────────────────────────────────────────────────────────────────

        static VisualElement Square(float size, float radius, Color colour)
        {
            var e = new VisualElement();
            e.style.width = e.style.height = size;
            SetRadius(e, radius);
            e.style.backgroundColor = colour;
            e.style.alignItems = Align.Center;
            e.style.justifyContent = Justify.Center;
            return e;
        }

        static void SetRadius(VisualElement e, float r) =>
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;

        static void SetBorder(VisualElement e, float width, Color colour)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = width;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = colour;
        }

        static VisualElement CoachFace(Coaches.Persona p, float size)
        {
            VisualElement a = Coaches.Avatar(p);
            a.style.width = a.style.height = size;
            SetRadius(a, 4);
            a.style.marginRight = 0;
            return a;
        }

        static Image Back(float size)
        {
            Image i = Ui.Icon("CaretRight", size, Color.white);
            i.style.rotate = new Rotate(180);
            return i;
        }

        void DrawTop()
        {
            _top.Clear();
            // A close X through the walkthrough, a back arrow on the plan (the end of the flow).
            VisualElement close = Square(44, 4, Ui.Surface);
            close.Add(_stage == Stage.Plan ? (VisualElement)Back(20) : Ui.Icon("X", 20, Color.white));
            _top.Add(close.OnTap(Leave));
            _top.Add(Ui.Box("grow"));
            if (_stage == Stage.Play)
            {
                // A secondary button on a solid ground, not words over the video.
                VisualElement skip = Ui.Secondary("Skip to plan", () => { CoachVoice.Hush(); ToPlan(); });
                skip.style.marginTop = 0;
                skip.style.paddingLeft = skip.style.paddingRight = 20;
                _top.Add(skip);
            }
        }

        void DrawBottom()
        {
            _bottom.Clear();
            if (_stage != Stage.Play) return;
            Moment m = _moments[_index];
            Color verdict = m.Verdict == "good" ? Ui.Bright : m.Verdict == "bad" ? Ui.Red : Ui.Faint;

            // The coach's tip box while they talk: the faults as tags, then the avatar, what this is, and the words.
            if (_showTip)
            {
                var bar = new VisualElement();
                bar.style.marginLeft = 16; bar.style.marginRight = 16; bar.style.marginBottom = 16;
                if (m.Captions.Count > 0)
                {
                    var tags = new VisualElement();
                    tags.style.flexDirection = FlexDirection.Row;
                    tags.style.flexWrap = Wrap.Wrap;
                    foreach (string c in m.Captions)
                    {
                        // A tag: the fault in caps, good in green and a fault in red, on the solid ground.
                        Label t = Ui.Caps(c);
                        t.style.color = verdict;
                        var tag = Ui.Box("", t);
                        tag.style.backgroundColor = Panel;
                        SetBorder(tag, 1, Ui.Line);
                        SetRadius(tag, 4);
                        tag.style.paddingLeft = tag.style.paddingRight = 8;
                        tag.style.paddingTop = tag.style.paddingBottom = 4;
                        tag.style.marginRight = 8; tag.style.marginBottom = 8;
                        tags.Add(tag);
                    }
                    bar.Add(tags);
                }
                Label title = Ui.Caps(m.Label);
                title.style.marginLeft = 12;
                title.style.flexGrow = 1;
                title.style.flexBasis = 0;
                VisualElement head = Ui.Box("row", CoachFace(_coach, 32), title);
                head.style.alignItems = Align.Center;
                Label words = Ui.Text(m.Line, "body", Ui.Medium);
                words.style.marginTop = 12;
                VisualElement card = Ui.Box("", head, words);
                card.style.backgroundColor = Panel;
                SetRadius(card, 4);
                SetBorder(card, 1, Ui.Line);
                card.style.paddingLeft = card.style.paddingRight = card.style.paddingTop = card.style.paddingBottom = 16;
                bar.Add(card);
                _bottom.Add(bar);
            }

            // Putt progress, by outcome: made in green, missed in red, the putt being looked at wide and white.
            var dots = new VisualElement();
            dots.style.flexDirection = FlexDirection.Row;
            dots.style.justifyContent = Justify.Center;
            dots.style.marginBottom = 16;
            for (int i = 0; i < _putts.Count; i++)
            {
                bool on = m.Putt == i, made = LabReplay.Made(_putts[i]);
                VisualElement d = Square(4, 0, on ? Color.white : made ? Ui.Green : Ui.Red);
                d.style.width = on ? 24 : 8;
                d.style.marginLeft = d.style.marginRight = 2;
                dots.Add(d);
            }
            _bottom.Add(dots);

            // Previous, pause or play, next.
            var transport = new VisualElement();
            transport.style.flexDirection = FlexDirection.Row;
            transport.style.justifyContent = Justify.Center;
            transport.style.alignItems = Align.Center;
            transport.style.marginBottom = 16;
            // Flat icon squares on solid grounds (the video is behind them).
            VisualElement prev = Square(44, 4, Ui.Surface2);
            Image prevIcon = Back(24);
            prevIcon.tintColor = _index == 0 ? Ui.Faint : Color.white;
            prev.Add(prevIcon);
            VisualElement main = Square(52, 4, Surface3);
            if (_paused) main.Add(Ui.Icon("Play", 24, Color.white));
            else
            {
                var bars = Ui.Box("row");
                for (int i = 0; i < 2; i++) { VisualElement b = Square(6, 0, Color.white); b.style.height = 20; b.style.marginLeft = b.style.marginRight = 3; bars.Add(b); }
                main.Add(bars);
            }
            main.style.marginLeft = main.style.marginRight = 16;
            VisualElement next = Square(44, 4, Ui.Surface2);
            next.Add(Ui.Icon("CaretRight", 24, Color.white));
            transport.Add(prev.OnTap(Previous));
            transport.Add(main.OnTap(TogglePause));
            transport.Add(next.OnTap(Next));
            _bottom.Add(transport);
        }

        // ── The plan ────────────────────────────────────────────────────────────────────────────────────

        void ToPlan()
        {
            if (_stage == Stage.Plan) return;
            _run++;
            CoachVoice.Hush();
            _stage = Stage.Plan;
            // The video is done with: free it.
            _tick?.Pause();
            _video?.Dispose();
            _video = null;
            _stageBox.style.display = DisplayStyle.None;
            _loading.style.display = DisplayStyle.None;
            _bottom.Clear();
            _planBox.style.display = DisplayStyle.Flex;
            DrawTop();
            if (!_planAsked) { _planAsked = true; App.Run(FetchPlan()); }
            DrawPlan();
        }

        // The coach's written fix for this session, grounded in the coaching knowledge base, and the lessons it points at.
        IEnumerator FetchPlan()
        {
            _planLoading = true;
            DrawPlan();
            var body = new Dictionary<string, object> { { "personaId", _coach.Id } };
            body["sessionId"] = long.TryParse(_id, out long n) ? (object)n : _id;
            long status = 0; string text = null;
            yield return ChatUi.Post(ChatUi.Ai + "/v2/coach/analyze", body, (s, t) => { status = s; text = t; }, 90);
            _planLoading = false;
            if (status == 200)
            {
                object r = J.Parse(text);
                string answer = J.Str(r, "answer");
                _planHelp = answer.Length > 0 ? ChatUi.StripIds(answer).Trim() : null;
                string convo = J.Str(r, "conversationId");
                _planConvo = convo.Length > 0 ? convo : null;
                _recLessons = J.List(J.Get(r, "recommendedLessons"));
            }
            else Perf.Note("review: plan analyze failed (" + status + ")");
            if (!_left) DrawPlan();
        }

        void DrawPlan()
        {
            _planBox.Clear();
            ScrollView scroll = Ui.Scroll();
            scroll.style.flexGrow = 1;
            var col = new VisualElement();
            col.style.paddingLeft = col.style.paddingRight = 16;
            col.style.paddingTop = 64;   // clear of the back button over the page
            col.style.paddingBottom = 16;
            scroll.Add(col);

            VisualElement names = Ui.Box("", Ui.Title("Your plan"), Ui.Caps(_coach.Name + " · this session"));
            names.style.marginLeft = 12;
            names.style.flexGrow = 1;
            names.style.flexBasis = 0;
            VisualElement head = Ui.Box("row", CoachFace(_coach, 44), names);
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 24;
            col.Add(head);

            // The one thing to fix, as a flat card: a green label, the fault, and why.
            VisualElement Highlight(string label, string title, string words)
            {
                Label t = Ui.Text(title, "body", Ui.Bold);
                t.style.marginTop = 8;
                Label w = Ui.Text(words, "body-2", Ui.Medium);
                w.style.marginTop = 4;
                VisualElement card = Ui.Box("card", Ui.Caps(label, "caps caps-green"), t, w);
                card.style.marginBottom = 12;
                return card;
            }
            if (_topFault != null) col.Add(Highlight("Your one thing", _topFault.Label, "Your biggest setup leak this session. Clean this up and the rest follows."));
            else col.Add(Highlight("Nothing to fix", "Setup is looking clean", "No big fundamentals to fix. Keep grooving your speed and reps."));

            if (_planLoading)
            {
                Label w = Ui.Text(_coach.Name + " is working out your fix...", "body-2", Ui.Medium);
                w.style.marginLeft = 12;
                w.style.flexGrow = 1;
                w.style.flexBasis = 0;
                VisualElement row = Ui.Box("card row", Spinner.Small(), w);
                row.style.alignItems = Align.Center;
                col.Add(row);
            }
            else if (_planHelp != null)
            {
                VisualElement card = Ui.Box("card");
                var clip = new VisualElement();
                Label help = Ui.Text(ChatUi.Rich(_planHelp), "body", Ui.Medium);
                help.enableRichText = true;
                clip.Add(help);
                // Collapsed to about ten rows until opened, when it runs longer than that.
                bool longer = _planHelp.Length > 520 || _planHelp.Split('\n').Length > 10;
                if (longer && !_helpOpen) { clip.style.maxHeight = 220; clip.style.overflow = Overflow.Hidden; }
                card.Add(clip);
                if (longer)
                {
                    // A text link: 13, text-2, tall enough to tap.
                    Label more = Ui.Text(_helpOpen ? "Show less" : "Read more", "body-2", Ui.Bold);
                    more.style.minHeight = 44;
                    more.style.unityTextAlign = TextAnchor.MiddleLeft;
                    card.Add(more.OnTap(() => { _helpOpen = !_helpOpen; DrawPlan(); }));
                }
                // The lessons the coach's fix points at.
                if (_recLessons.Count > 0)
                {
                    // The lessons as rows under one label, split by dividers.
                    VisualElement recs = Ui.Box("", Ui.Caps("Coach recommendations", "caps caps-green"));
                    recs.style.marginTop = 16;
                    foreach (object l in _recLessons)
                    {
                        string id = J.Str(l, "id");
                        Label name = Ui.Text(J.Str(l, "title"), "body", Ui.Bold), what = Ui.Text("Lesson · watch and practice", "body-2", Ui.Medium);
                        what.style.marginTop = 4;
                        VisualElement lessonWords = Ui.Box("", name, what);
                        lessonWords.style.flexGrow = 1;
                        lessonWords.style.flexBasis = 0;
                        VisualElement row = Ui.Box("row", lessonWords, Ui.Icon("CaretRight", 16, Ui.Faint));
                        row.style.alignItems = Align.Center;
                        row.style.marginTop = 12;
                        row.style.paddingTop = 12;
                        row.style.minHeight = 44;
                        row.style.borderTopWidth = 1;
                        row.style.borderTopColor = Ui.Line;
                        recs.Add(row.OnTap(() => Library.Load(() =>
                        {
                            object lesson = (Library.Lessons ?? new List<object>()).Find(x => J.Str(x, "id") == id) ?? (Library.Workouts ?? new List<object>()).Find(x => J.Str(x, "id") == id);
                            if (lesson != null) { CoachVoice.Hush(); _app.Push(new LessonScreen(_app, lesson)); }
                        })));
                    }
                    card.Add(recs);
                }
                col.Add(card);
            }
            _planBox.Add(scroll);

            // Pinned: keep talking with the coach, from the fix (the same conversation when there is one).
            // The plan's one main button.
            var foot = new VisualElement();
            foot.style.paddingLeft = foot.style.paddingRight = 16;
            foot.style.paddingBottom = 16;
            foot.Add(Ui.Button("Ask " + _coach.Name, AskCoach));
            _planBox.Add(foot);
        }

        void AskCoach()
        {
            CoachVoice.Hush();
            if (Kid.IsKid(_app.Profile)) { _app.Toast("Chat is off for Kid profiles."); return; }
            if (_planConvo != null) _app.Push(new CoachChatScreen(_app, _planConvo, false));
            else CoachChatScreen.Open(_app);
        }
    }
}
