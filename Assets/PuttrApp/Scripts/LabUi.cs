// The pieces the 3D session screens share (LabViewer.cs, LabExplore.cs, SessionOverview.cs), ported from puttr-app:
//
//   common/PuttCard.js            the putt card (grade, pace bar, entry) and its ball-path card
//   lab/LabSessionView.js         Ball overview tiles with their help card, Body overview with the golfer silhouettes,
//                                 the body chips under each putt
//   common/AnnotationControls.js  the overlay switches (Eye line, Shoulder drop, Body, Shoulders, Hips, Wrist, Head)
//
// Styles in Resources/PuttrApp/Lab.uss; icons the app has and the arcade set does not are in Resources/PuttrApp/LabIcons
// (Phosphor, white on clear, tinted here), with the two golfer silhouettes from puttr-app assets/images.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class LabUi
    {
        // The guidelines' colours under the names these screens grew up with.
        public static readonly Color Card = Ui.Surface, Tile = Ui.Surface, Line = Ui.Line;
        public static readonly Color Amber = Ui.Amber;
        public static readonly Color Soft = Ui.Faint;
        /// <summary>surface-3: a control's pressed or picked ground.</summary>
        public static readonly Color Surface3 = new Color32(48, 53, 58, 255);

        static StyleSheet _sheet;

        /// <summary>Lab.uss on a screen's root.</summary>
        public static T Style<T>(T root) where T : VisualElement
        {
            if (_sheet == null) _sheet = Resources.Load<StyleSheet>("PuttrApp/Lab");
            if (_sheet != null && !root.styleSheets.Contains(_sheet)) root.styleSheets.Add(_sheet);
            return root;
        }

        /// <summary>An icon: the lab's own set first (Pause, Eye, Faders, SkipBack ...), else the app's.</summary>
        public static Image Icon(string name, float size, Color tint)
        {
            Texture2D own = Resources.Load<Texture2D>("PuttrApp/LabIcons/Lab" + name);
            if (own == null) return Ui.Icon(name, size, tint);
            var image = new Image { image = own, tintColor = tint, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.width = size;
            image.style.height = size;
            return image;
        }

        /// <summary>An icon button (the replay's control bar, the drawing tools): a flat square, never under 44 points.</summary>
        public static VisualElement Round(string icon, float size, Color fill, Action tap, float iconSize = 18, Color? tint = null)
        {
            VisualElement b = Ui.Box("lab-round", Icon(icon, iconSize, tint ?? Color.white));
            b.style.width = b.style.height = Mathf.Max(44f, size);
            b.style.backgroundColor = fill;
            return tap != null ? b.OnTap(tap) : b;
        }

        public static void Radius(VisualElement e, float r) =>
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;

        public static void Border(VisualElement e, float width, Color colour)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = width;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = colour;
        }

        public static Label Coloured(string text, string classes, Color colour, Font font = null)
        {
            Label l = Ui.Text(text, classes, font ?? Ui.Bold);
            l.style.color = colour;
            return l;
        }

        /// <summary>"5 months ago" (react-native-timeago) for a row's created_at.</summary>
        public static string Ago(object row) => PuttrApp.Ago.Words(ProfileMath.Date(row, "created_at"), DateTime.UtcNow);

        /// <summary>A chip that can be on (the 3D model's putt and phase chips): the guidelines' chip, white when on.</summary>
        public static VisualElement Pill(string text, bool on, Action tap, string classes = "lab-pill")
        {
            VisualElement p = Ui.Box("scope " + classes + (on ? " scope-on" : ""), Ui.Text((text ?? "").ToUpperInvariant(), "scope-text", Ui.Bold));
            return tap != null ? p.OnTap(tap) : p;
        }

        /// <summary>One cell of a Grid: a value in its colour over a caption; a tap when it opens something.</summary>
        public static (string value, string caption, Color colour, Action tap) Cell(string value, string caption, Color colour, Action tap = null) => (value, caption, colour, tap);

        /// <summary>One bordered block of reads, perRow to a row, split by dividers. numbers: the values are scores
        /// (the numeral face), else words.</summary>
        public static VisualElement Grid(int perRow, bool numbers, IList<(string value, string caption, Color colour, Action tap)> cells)
        {
            VisualElement grid = Ui.Box("lab-grid");
            VisualElement row = null;
            for (int i = 0; i < cells.Count; i++)
            {
                if (i % perRow == 0) { row = Ui.Box(i == 0 ? "lab-grid-row lab-grid-row-first" : "lab-grid-row"); grid.Add(row); }
                Label value = numbers ? Ui.Num(cells[i].value, "lab-cell-num") : Ui.Text(cells[i].value, "lab-cell-word", Ui.Bold);
                value.style.color = cells[i].colour;
                VisualElement cell = Ui.Box(i % perRow == 0 ? "lab-cell lab-cell-first" : "lab-cell", value, Ui.Caps(cells[i].caption, "caps lab-cell-cap"));
                Action tap = cells[i].tap;
                if (tap != null) { cell.AddToClassList("lab-cell-tap"); cell.OnTap(tap); }
                row.Add(cell);
            }
            return grid;
        }

        /// <summary>A putt's pace in the guidelines' colours: ideal green, short amber, too firm red. text: for words
        /// (bright green), else for a fill (brand green).</summary>
        public static Color PaceColour(LabPuttRead d, bool text)
        {
            if (d.Rollout == null) return Ui.Faint;
            if (d.PaceColour == Ui.Green) return text ? Ui.Bright : Ui.Green;
            return d.PaceColour == Ui.Blue ? Ui.Amber : Ui.Red;
        }

        // ── A putt's card (PuttCard) ────────────────────────────────────────────────────────────────────────

        /// <summary>The putt card: grade and score, the putt's number, distance and break, the pace bar, the entry or miss.
        /// A tap opens the ball-path card over `over` unless the caller takes the tap.</summary>
        public static VisualElement PuttCard(object putt, int index, bool compact, VisualElement over, Action tap = null)
        {
            LabPuttRead d = LabPuttRead.Of(putt);
            VisualElement grade = Ui.Box("lab-putt-grade", Coloured(d.Grade, "lab-putt-letter ital", d.GradeColour), Coloured(d.Rounded.ToString(), "lab-putt-score", Ui.Faint, Ui.Bold));
            VisualElement head = Ui.Box("row", Ui.Caps("Putt " + (index + 1)), Ui.Text(J.Int(putt, "distance") + " ft", "lab-putt-ft", Ui.Bold), Ui.Text(d.Break, "lab-putt-break", Ui.Medium));
            VisualElement fill = Ui.Box("lab-pace-fill");
            fill.style.width = Length.Percent(d.PaceShare * 100f);
            fill.style.backgroundColor = PaceColour(d, false);
            VisualElement pace = Ui.Box("lab-pace",
                Ui.Box("row", Ui.Caps("Pace", "caps lab-pace-label"), Coloured(d.Pace, "lab-pace-value", PaceColour(d, true))),
                Ui.Box("lab-pace-bar", fill));
            VisualElement mark;
            if (d.Made) { mark = Ui.Box("lab-putt-made"); mark.style.backgroundColor = d.ResultColour; }
            else mark = Ui.Icon("X", 12, Ui.Red);
            VisualElement result = Ui.Box("row lab-putt-result", mark, Coloured(d.ResultLine, "lab-putt-result-text", d.ResultColour, Ui.Medium));
            VisualElement card = Ui.Box("lab-putt" + (compact ? " lab-putt-compact" : ""), grade, Ui.Box("lab-putt-main", head, pace, result));
            int i = index;
            return card.OnTap(tap ?? (() => PuttModal(over, putt, i)));
        }

        /// <summary>The ball-path card over the screen (PuttCard's modal): the path into the hole in a window, the read
        /// under it, and Replay.</summary>
        public static void PuttModal(VisualElement over, object putt, int index)
        {
            LabPuttRead d = LabPuttRead.Of(putt);
            var path = new BallPathView(d);
            VisualElement stage = Ui.Box("window lab-path-stage", path);
            VisualElement head = Ui.Box("row lab-modal-head", Ui.Title("Putt " + (index + 1) + " · " + J.Int(putt, "distance") + " ft · " + d.Break, "title-20 lab-modal-title"), Ui.Num(d.Grade, "num"));
            head.Q<Label>(className: "num").style.color = d.GradeColour;
            VisualElement card = Ui.Box("modal lab-modal", stage, head,
                Grid(2, false, new[] { Cell(d.Pace, "Pace", PaceColour(d, true)), Cell(d.Result, "Entry", d.ResultColour) }));
            if (d.Made && !string.IsNullOrEmpty(d.EntryLine)) card.Add(Ui.Text(d.EntryLine, "body-2 lab-modal-sub", Ui.Medium));
            card.Add(Ui.Button("Replay", path.Replay, "btn btn-ghost lab-modal-replay"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            VisualElement dim = null;
            dim = Style(Ui.Box("modal-dim lab-dim", card)).OnBackdropTap(() => { path.Stop(); dim.RemoveFromHierarchy(); });
            App.Layer(over).Add(dim);
            path.Replay();
        }

        // ── Ball overview (LabSessionView renderOverview) ───────────────────────────────────────────────────

        public static VisualElement BallOverview(List<object> putts, VisualElement over)
        {
            LabBall.Overview o = LabBall.Of(putts);
            Color gradeColour = o.Score <= 50 ? Ui.Red : o.Score <= 80 ? Color.white : (Color)Ui.Bright;
            (string, string, Color, Action) Stat(string title, string help, string value, Color colour) => Cell(value, title, colour, () => Help(over, help, value));
            return Grid(3, true, new[]
            {
                Stat("Grade", "Session grade", o.Grade, gradeColour), Stat("Accuracy", "Accuracy score", o.Accuracy, Color.white), Stat("Speed", "Speed score", o.Speed, Color.white),
                Stat("Total", "Total score", o.Total, Color.white), Stat("Speed diff", "Speed differential", o.SpeedDiff, Color.white), Stat("Path diff", "Path differential", o.PathDiff, Color.white),
            });
        }

        static void Help(VisualElement over, string title, string value)
        {
            Label number = Ui.Num(value, "num lab-help-value");
            number.style.color = Ui.Bright;
            VisualElement card = Ui.Box("modal", Ui.Title(title, "title-20"), number, Ui.Text(LabBall.Help.TryGetValue(title, out string words) ? words : "", "body-2", Ui.Medium));
            VisualElement dim = null;
            dim = Style(Ui.Box("modal-dim lab-dim", card));
            dim.OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
        }

        // ── Body overview (LabSessionView renderBodyOverview; coach BodyAnalysis SessionBodyMetrics) ─────────

        static Color GradeColour(int grade) => grade == 0 ? Ui.Bright : grade == 1 ? Ui.Amber : Ui.Red;

        // The camera angle a group of reads was filmed from: the small golfer and the angle's name.
        static VisualElement AngleHead(bool front, string words, bool next)
        {
            var image = new Image { image = Resources.Load<Texture2D>("PuttrApp/LabIcons/LabGolfer" + (front ? "Front" : "Rear")), scaleMode = ScaleMode.ScaleToFit, tintColor = Ui.Faint, pickingMode = PickingMode.Ignore };
            image.AddToClassList("lab-golfer-small");
            return Ui.Box(next ? "lab-angle lab-angle-next" : "lab-angle", image, Ui.Caps(words));
        }

        /// <summary>The session's body averages for each camera angle filmed, a block of reads under each angle's name.
        /// coach: the coach screens' words and the putt count in each heading ("DOWN THE LINE · 2 PUTTS").</summary>
        public static VisualElement BodyOverview(BodyFaults.Session s, bool lefty, bool coach)
        {
            VisualElement card = new VisualElement();
            if (s == null || !s.Any) return null;
            string Count(int n) => coach ? " · " + n + " PUTT" + (n == 1 ? "" : "S") : "";
            if (s.DownTheLine is BodyFaults.Average dtl)
            {
                string sa = BodyFaults.ShoulderWords(dtl.ShoulderAlignment), el = BodyFaults.EyeWords(dtl.EyeLine, lefty, coach), st = BodyFaults.ToeWords(dtl.ShoulderToe, coach), kb = BodyFaults.KneeWords(dtl.KneeBend, coach);
                card.Add(AngleHead(false, "DOWN THE LINE" + Count(dtl.Count), false));
                card.Add(Grid(2, false, new[]
                {
                    Cell(sa, "Shoulders", GradeColour(BodyFaults.Grade(sa))), Cell(el, "Eye line", GradeColour(BodyFaults.Grade(el))),
                    Cell(st, "Shoulder / toe", GradeColour(BodyFaults.Grade(st))), Cell(BodyFaults.KneeValue(dtl.KneeBend, coach), "Knee bend", GradeColour(BodyFaults.Grade(kb))),
                }));
            }
            if (s.FaceOn is BodyFaults.Average fo)
            {
                string bp = BodyFaults.BallWords(fo.BallPosition), wb = BodyFaults.WeightWords(fo.WeightBias, coach), sw = BodyFaults.StanceWords(fo.StanceWidth);
                card.Add(AngleHead(true, "FACE ON" + Count(fo.Count), s.DownTheLine != null));
                card.Add(Grid(2, false, new[]
                {
                    Cell(bp, "Ball position", GradeColour(BodyFaults.Grade(bp))), Cell(wb, "Weight bias", GradeColour(BodyFaults.Grade(wb))),
                    Cell(sw, "Stance width", GradeColour(BodyFaults.Grade(sw))), Cell(BodyFaults.Seconds(fo.StrokeLength, coach ? "0s" : "—"), "Stroke time", Color.white),
                }));
            }
            return card;
        }

        /// <summary>The four reads under a putt on the session page (its camera angle's).</summary>
        public static VisualElement BodyChips(BodyFaults.Reading r, bool lefty)
        {
            if (r == null) return null;
            var cells = new List<(string value, string caption, Color colour, Action tap)>();
            void Chip(string label, string value, Color colour) => cells.Add(Cell(value, label, colour));
            if (r.DownTheLine)
            {
                string sa = BodyFaults.ShoulderWords(r.ShoulderAlignment), el = BodyFaults.EyeWords(r.EyeLine, lefty, false), st = BodyFaults.ToeWords(r.ShoulderToe, false), kb = BodyFaults.KneeWords(r.KneeBend, false);
                Chip("Shoulders", sa, GradeColour(BodyFaults.Grade(sa)));
                Chip("Eye line", el, GradeColour(BodyFaults.Grade(el)));
                Chip("Shldr/toe", st, GradeColour(BodyFaults.Grade(st)));
                Chip("Knees", BodyFaults.KneeValue(r.KneeBend, false), GradeColour(BodyFaults.Grade(kb)));
            }
            else
            {
                string bp = BodyFaults.BallWords(r.BallPosition), wb = BodyFaults.WeightWords(r.WeightBias, false), sw = BodyFaults.StanceWords(r.StanceWidth);
                Chip("Ball pos", bp, GradeColour(BodyFaults.Grade(bp)));
                Chip("Weight", wb, GradeColour(BodyFaults.Grade(wb)));
                Chip("Stance", sw, GradeColour(BodyFaults.Grade(sw)));
                Chip("Stroke", BodyFaults.Seconds(r.StrokeLength), Color.white);
            }
            return Grid(4, false, cells);
        }

        /// <summary>The replay's and the coach's per-putt reads (LabSessionReplay bodyPills, BodyAnalysis MetricRows): value,
        /// label and colour, green when good and amber when not.</summary>
        public static List<(string value, string label, Color colour)> Reads(BodyFaults.Reading r, bool lefty)
        {
            var list = new List<(string, string, Color)>();
            if (r == null) return list;
            Color C(bool good) => good ? (Color)Ui.Bright : Amber;
            if (r.DownTheLine)
            {
                list.Add((BodyFaults.ShoulderWords(r.ShoulderAlignment), "Shoulders", C(BodyFaults.Good("shoulders", r.ShoulderAlignment))));
                list.Add((BodyFaults.EyeWords(r.EyeLine, lefty, true), "Eye line", C(BodyFaults.Good("eye", r.EyeLine))));
                list.Add((BodyFaults.ToeWords(r.ShoulderToe, true), "Shoulder/toe", C(BodyFaults.Good("toe", r.ShoulderToe))));
                list.Add((BodyFaults.KneeWords(r.KneeBend, true), "Knees", C(BodyFaults.Good("knees", r.KneeBend))));
            }
            else
            {
                list.Add((BodyFaults.BallWords(r.BallPosition), "Ball position", C(BodyFaults.Good("ball", r.BallPosition))));
                list.Add((BodyFaults.WeightWords(r.WeightBias, true), "Weight", C(BodyFaults.Good("weight", r.WeightBias))));
                list.Add((BodyFaults.StanceWords(r.StanceWidth), "Stance", C(BodyFaults.Good("stance", r.StanceWidth))));
                list.Add((BodyFaults.Seconds(r.StrokeLength), "Stroke time", Ui.Bright));
            }
            return list;
        }

        // ── The overlay switches (AnnotationControls) ───────────────────────────────────────────────────────

        /// <summary>"Overlays" with Hide all (or Show all) and the seven switches; changed runs after every switch.</summary>
        public static VisualElement OverlaySwitches(BlazeOverlay.Lines lines, Action changed)
        {
            VisualElement box = new VisualElement();
            void Draw()
            {
                box.Clear();
                bool any = lines.Any;
                box.Add(Ui.Box("row lab-switch-head", Ui.Caps("Overlays", "caps lab-switch-title"),
                    Ui.Text(any ? "Hide all" : "Show all", "lab-switch-all", Ui.Medium).OnTap(() => { lines.SetAll(!any); Draw(); changed?.Invoke(); })));
                VisualElement chips = Ui.Box("lab-switches");
                foreach (var t in BlazeOverlay.Lines.All)
                {
                    string key = t.key;
                    chips.Add(Ui.Box("scope lab-switch" + (lines[key] ? " scope-on" : ""), Ui.Text(t.label.ToUpperInvariant(), "scope-text", Ui.Bold))
                        .OnTap(() => { lines[key] = !lines[key]; Draw(); changed?.Invoke(); }));
                }
                box.Add(chips);
            }
            Draw();
            return box;
        }

        // ── Saving the replay (LabSessionReplay downloadVideo, SessionInfoPanel saveVideo) ──────────────────

        static bool _saving;

        /// <summary>Downloads the replay and hands it to the share sheet (Save Video on the iPhone, Photos or Files on Android).</summary>
        public static IEnumerator SaveVideo(App app, string url, string id, Action<bool> busy)
        {
            if (_saving || string.IsNullOrEmpty(url)) yield break;
            _saving = true;
            busy?.Invoke(true);
            string folder = Path.Combine(Application.temporaryCachePath, "lab-replays");
            string file = Path.Combine(folder, "session-" + (string.IsNullOrEmpty(id) ? "replay" : id) + ".mp4");
            bool ok = false;
            try { Directory.CreateDirectory(folder); } catch (Exception) { }
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerFile(file) { removeFileOnAbort = true };
                req.timeout = 120;
                yield return req.SendWebRequest();
                ok = req.result == UnityWebRequest.Result.Success;
            }
            _saving = false;
            busy?.Invoke(false);
            if (!ok) { app.Toast("Couldn't download. The video failed to save. Check your connection and try again."); yield break; }
            Share.File(file, "video/mp4");
        }
    }

    /// <summary>
    /// A putt's ball path on its own (PuttCard EnhancedPath and PuttReplay): the grid, the hole, the path
    /// from the start dot to where it went in (a green dot on the lip) or stopped (a red cross). Replay draws it again.
    /// </summary>
    public sealed class BallPathView : VisualElement
    {
        readonly LabPuttRead _d;
        float _share = 1f;
        IVisualElementScheduledItem _run;

        public BallPathView(LabPuttRead d)
        {
            _d = d;
            AddToClassList("lab-path");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Replay()
        {
            _run?.Pause();
            float began = Time.realtimeSinceStartup;
            _share = 0f;
            _run = schedule.Execute(() =>
            {
                _share = Mathf.Clamp01((Time.realtimeSinceStartup - began) / 1.4f);
                MarkDirtyRepaint();
                Perf.Wake(0.2f);
                if (_share >= 1f) _run.Pause();
            }).Every(16);
        }

        public void Stop() => _run?.Pause();

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0) return;
            Painter2D p = ctx.painter2D;
            p.fillColor = Ui.Base;
            p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(w, 0)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0, h)); p.ClosePath(); p.Fill();
            p.strokeColor = Ui.Line;
            p.lineWidth = 1;
            foreach (float s in new[] { 0.25f, 0.5f, 0.75f })
            {
                p.BeginPath(); p.MoveTo(new Vector2(0, h * s)); p.LineTo(new Vector2(w, h * s)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(w * s, 0)); p.LineTo(new Vector2(w * s, h)); p.Stroke();
            }
            if (_d.Hb == null) return;

            // Fit the hole and the path, with a margin, into the box (the camera frame's own coordinates, y down).
            float minX = (float)(_d.Hb[0] - _d.Hb[2]), maxX = (float)(_d.Hb[0] + _d.Hb[2]), minY = (float)(_d.Hb[1] - _d.Hb[2]), maxY = (float)(_d.Hb[1] + _d.Hb[2]);
            foreach (int[] pt in _d.Bp) { minX = Mathf.Min(minX, pt[0]); maxX = Mathf.Max(maxX, pt[0]); minY = Mathf.Min(minY, pt[1]); maxY = Mathf.Max(maxY, pt[1]); }
            float bw = Mathf.Max(1, maxX - minX), bh = Mathf.Max(1, maxY - minY), k = Mathf.Min((w - 40) / bw, (h - 40) / bh);
            Vector2 off = new Vector2((w - bw * k) / 2 - minX * k, (h - bh * k) / 2 - minY * k);
            Vector2 P(double x, double y) => new Vector2((float)x * k, (float)y * k) + off;
            Vector2 hole = P(_d.Hb[0], _d.Hb[1]);
            float hr = (float)(_d.Hb[2] / 2) * k;

            p.fillColor = Color.black; p.BeginPath(); p.Arc(hole, hr, 0, 360); p.Fill();
            p.strokeColor = Ui.Faint; p.lineWidth = 1.5f; p.BeginPath(); p.Arc(hole, hr, 0, 360); p.Stroke();

            int count = _d.Bp.Length;
            if (count < 2) return;
            int shown = Mathf.Clamp(Mathf.CeilToInt(_share * count), 2, count);
            var pts = new List<Vector2>();
            for (int i = 0; i < shown; i++) pts.Add(P(_d.Bp[i][0], _d.Bp[i][1]));
            p.strokeColor = Color.white;
            p.lineWidth = 3; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            p.BeginPath(); p.MoveTo(pts[0]); for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]); p.Stroke();
            p.fillColor = Ui.Faint; p.BeginPath(); p.Arc(pts[0], 3, 0, 360); p.Fill();
            if (shown < count) { p.fillColor = Color.white; p.BeginPath(); p.Arc(pts[pts.Count - 1], 5, 0, 360); p.Fill(); return; }

            if (_d.Made)
            {
                // Where the path first crossed the lip.
                Vector2 entry = pts[pts.Count - 1];
                for (int i = 1; i < pts.Count; i++)
                    if (Vector2.Distance(pts[i], hole) <= hr)
                    {
                        Vector2 a = pts[i - 1], v = pts[i] - a, o = a - hole;
                        float qa = Vector2.Dot(v, v), qb = 2 * Vector2.Dot(o, v), qc = Vector2.Dot(o, o) - hr * hr, disc = qb * qb - 4 * qa * qc;
                        float t = disc >= 0 && qa > 0 ? (-qb - Mathf.Sqrt(disc)) / (2 * qa) : 1;
                        entry = t >= 0 && t <= 1 ? a + v * t : pts[i];
                        break;
                    }
                p.fillColor = Ui.Green; p.BeginPath(); p.Arc(entry, 5, 0, 360); p.Fill();
            }
            else
            {
                Vector2 end = pts[pts.Count - 1];
                p.fillColor = Ui.Red; p.BeginPath(); p.Arc(end, 5, 0, 360); p.Fill();
                p.strokeColor = Color.white; p.lineWidth = 1.5f;
                p.BeginPath(); p.MoveTo(end + new Vector2(-2.5f, -2.5f)); p.LineTo(end + new Vector2(2.5f, 2.5f)); p.Stroke();
                p.BeginPath(); p.MoveTo(end + new Vector2(2.5f, -2.5f)); p.LineTo(end + new Vector2(-2.5f, 2.5f)); p.Stroke();
            }
        }
    }
}
