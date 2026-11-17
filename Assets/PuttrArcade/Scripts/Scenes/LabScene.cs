// The Lab: a putt-testing readout. A dark instrument screen inked with a faint blueprint grid, laid out like a test
// bench's monitor: a TRACE plot of the putt from the spot to the cup (the latest roll bright, earlier ones faint, a
// dashed ring where each would have come to rest), and beside it the data panel. ENTRY SCORE and SPEED SCORE are the
// two hero readings (out of ten, each with a segmented meter); under them every measured property of the putt
// (distance and tee, entry angle and its description, rollout, line at the hole, launch heading), and beside the tee
// sign the result, the putt's total out of twenty and the running average. After each putt the panel clears, the
// trace rolls in, a scan line sweeps the readout and the values type in, then the heroes count up.
//
// The screen faces the camera square on, so the layout is drawn in viewport fractions (clear of the app's tee sign
// and score bug) and re-laid if the screen changes shape. The putt itself is in inches: the cup at the origin and
// the spot down -Z, scaled into the plot.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class LabScene : RuleScene
    {
        const float CupR = 3.4f, BallR = 1.6f, Tilt = 16f, Fov = 40f, ViewH = 200f;
        const int Segs = 10;
        static readonly Color EntryCol = new Color(0.48f, 0.86f, 0.3f), SpeedCol = new Color(0.38f, 0.62f, 1f), Amber = new Color(1f, 0.76f, 0.25f);
        static readonly Color Ink = new Color(0.016f, 0.03f, 0.05f), PanelFill = new Color(0.03f, 0.065f, 0.1f, 0.94f);
        static readonly Color Edge = new Color(0.36f, 0.62f, 0.9f, 0.45f), Label = new Color(0.52f, 0.68f, 0.84f), Value = new Color(0.93f, 0.96f, 1f);
        static readonly Color Dim = new Color(0.3f, 0.4f, 0.52f, 0.7f), Red = new Color(1f, 0.36f, 0.36f);
        static readonly string[] PropLabels = { "OFF LINE", "ROLLOUT", "ENTRY ANGLE", "LINE AT HOLE", "ENTRY", "LAUNCH" };

        // The putt's spot, and every roll so far (inches), to redraw on a new layout.
        int _feet = 5, _tee = 2;
        readonly List<List<Vector2>> _rolls = new List<List<Vector2>>();
        readonly List<Vector2> _restsAt = new List<Vector2>();
        List<Vector2> _rolling = new List<Vector2>();

        // What the readout shows (the layout is rebuilt from this).
        readonly string[] _prop = new string[6];
        readonly float[] _propShown = new float[6];          // 0..1 of each value typed in
        readonly float[] _hero = new float[2];               // the hero readings as they count up
        readonly float[] _heroFill = new float[2];
        bool _heroLive;
        string _result = "", _total = "", _avg = "";
        Color _resultCol = Value;
        string _status = "READY";
        Color _statusCol = EntryCol;

        // The layout.
        Transform _board, _frame, _plotRoot, _ball;
        float _W, _H = ViewH, _D = 300f, _u = 1f;
        Vector2 _shape;
        Rect _plot, _plotArea, _stack, _heroes, _props;
        float _k = 1f, _halfIn = 20f;
        const float ZFar = 26f;
        float ZNear => -_feet * 12f - 10f;
        TextMesh[] _propText = new TextMesh[6];
        readonly TextMesh[] _heroNum = new TextMesh[2], _heroOf = new TextMesh[2];
        readonly Transform[][] _heroSeg = { new Transform[Segs], new Transform[Segs] };
        readonly float[] _heroNumX = new float[2], _heroNumZ = new float[2], _heroOfDrop = new float[2];
        TextMesh _resultText, _totalText, _avgText, _totalOf, _avgOf, _statusText;
        Transform _statusDot, _scan;
        float _dot = 1f;
        bool _statusRight;
        float _stackGap;
        LineRenderer _live;
        Material _liveMat, _oldMat, _restMat, _restOld, _ballMat, _scanMat;
        readonly Dictionary<Color, Material> _fills = new Dictionary<Color, Material>();
        static Mesh _quad;

        public override Vector3 CameraFrom => At(new Vector3(0, Mathf.Cos(Tilt * Mathf.Deg2Rad), -Mathf.Sin(Tilt * Mathf.Deg2Rad)) * _D);
        public override Vector3 CameraLook => At(Vector3.zero);
        public override float CameraFov => Fov;

        public override void Build()
        {
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -60f, 0), new Vector3(3000f, 2f, 3000f), Make.Lit(new Color(0.01f, 0.015f, 0.02f), 0.2f));
            _board = new GameObject("Screen").transform;
            _board.SetParent(Root, false);
            _board.localRotation = Quaternion.Euler(-Tilt, 0, 0);
            _liveMat = Make.Glow(new Color(EntryCol.r, EntryCol.g, EntryCol.b, 1f));
            _oldMat = Make.Glow(new Color(EntryCol.r, EntryCol.g, EntryCol.b, 0.22f));
            _restMat = Make.Glow(new Color(Amber.r, Amber.g, Amber.b, 1f));
            _restOld = Make.Glow(new Color(Amber.r, Amber.g, Amber.b, 0.28f));
            _ballMat = Make.Lit(new Color(0.97f, 0.97f, 0.96f), 0.7f);
            _scanMat = Make.Glow(new Color(0.55f, 0.85f, 1f, 0.55f));
            Sun(Root, new Vector3(70f, 20f, 0f), new Color(0.85f, 0.92f, 1f), 0.5f);
            Lamp(Root, new Vector3(-60f, 300f, 60f), Vector3.zero, new Color(0.85f, 0.92f, 1f), 60000f, 70f, 900f);
            Clear();
        }

        // ── The layout ──────────────────────────────────────────────────────────

        static Vector2 ViewShape()
        {
            bool upright = Screen.height > Screen.width;
            return new Vector2(Screen.width * (upright ? 1f : FreePuttMode.WideShare), Screen.height * (upright ? FreePuttMode.UprightShare : 1f));
        }

        // A point on the screen from viewport fractions (0,0 top left).
        Vector3 Q(float fx, float fy, float lift = 0.2f) => new Vector3((fx - 0.5f) * _W, lift, (0.5f - fy) * _H);
        Rect R(float fx0, float fy0, float fx1, float fy1) => new Rect((fx0 - 0.5f) * _W, (0.5f - fy1) * _H, (fx1 - fx0) * _W, (fy1 - fy0) * _H);
        // Text sizes are given in a phone's points.
        float Pt(float px) => px * _u;

        void Layout()
        {
            _shape = ViewShape();
            bool upright = Screen.height > Screen.width;
            float aspect = Mathf.Max(0.3f, _shape.x / Mathf.Max(1f, _shape.y));
            float tanV = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            // Off a phone held upright the arcade widens the lens to a 16:9 frame's width (Arcade.Fitted).
            if (!upright && aspect < 16f / 9f) tanV *= (16f / 9f) / aspect;
            _H = ViewH; _W = ViewH * aspect;
            _D = _H * 0.5f / tanV;
            _u = ViewH / 485f * Mathf.Min(1f, aspect / 0.887f);

            if (upright)
            {
                // Clear of the score bug (top 24%) and the tee sign (left 44%, down to 53%).
                _stack = R(0.465f, 0.255f, 0.70f, 0.535f);
                _heroes = R(0.035f, 0.555f, 0.70f, 0.75f);
                _props = R(0.035f, 0.765f, 0.70f, 0.98f);
                _plot = R(0.72f, 0.255f, 0.97f, 0.98f);
            }
            else
            {
                _stack = R(0.275f, 0.14f, 0.565f, 0.305f);
                _heroes = R(0.035f, 0.325f, 0.565f, 0.585f);
                _props = R(0.035f, 0.605f, 0.565f, 0.955f);
                _plot = R(0.59f, 0.14f, 0.965f, 0.955f);
            }

            if (_frame != null) Make.Kill(_frame.gameObject);
            _segFull.Clear(); _baseSize.Clear();
            _frame = new GameObject("Layout").transform;
            _frame.SetParent(_board, false);

            Backdrop();
            StackPanel();
            HeroPanel();
            PropsPanel();
            PlotPanel();
            Render();
        }

        void Backdrop()
        {
            Quad(_frame, new Rect(-_W, -_H, _W * 2f, _H * 2f), Ink, -0.4f, 0);
            float step = 8f;
            for (float x = Mathf.Ceil(-_W / 2f / step) * step; x < _W / 2f; x += step)
            {
                bool major = Mathf.Abs(Mathf.Round(x / step)) % 5 == 0;
                Line(_frame, new Color(0.3f, 0.55f, 0.85f, major ? 0.1f : 0.045f), Pt(major ? 0.9f : 0.6f), -0.3f, new Vector2(x, -_H / 2f), new Vector2(x, _H / 2f));
            }
            for (float z = Mathf.Ceil(-_H / 2f / step) * step; z < _H / 2f; z += step)
            {
                bool major = Mathf.Abs(Mathf.Round(z / step)) % 5 == 0;
                Line(_frame, new Color(0.3f, 0.55f, 0.85f, major ? 0.1f : 0.045f), Pt(major ? 0.9f : 0.6f), -0.3f, new Vector2(-_W / 2f, z), new Vector2(_W / 2f, z));
            }
        }

        // A readout panel: a dark plate, a hairline edge and bright corner brackets.
        void Panel(Rect r, Color edge, Color fill)
        {
            Quad(_frame, r, fill, 0f, 1);
            Line(_frame, edge, Pt(1f), 0.05f, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin));
            float b = Mathf.Min(r.width, r.height) * 0.14f;
            Color bright = new Color(edge.r, edge.g, edge.b, 1f);
            foreach (Vector2 c in new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) })
            {
                float sx = c.x < r.center.x ? 1f : -1f, sz = c.y < r.center.y ? 1f : -1f;
                Line(_frame, bright, Pt(2f), 0.1f, c + new Vector2(0, sz * b), c, c + new Vector2(sx * b, 0));
            }
        }

        void StackPanel()
        {
            Rect r = _stack;
            Panel(r, Edge, PanelFill);
            float pad = Mathf.Min(r.width * 0.09f, Pt(10f)), rowH = (r.height - pad * 0.6f) / 3f;
            float lab = Mathf.Min(Pt(8.5f), rowH * 0.2f), big = Mathf.Min(Pt(20f), rowH * 0.42f);
            string[] labels = { "RESULT", "PUTT TOTAL", "BEST PUTT" };   // the average is in the score bar
            var vals = new TextMesh[3];
            var ofs = new TextMesh[3];
            for (int i = 0; i < 3; i++)
            {
                float top = r.yMax - pad * 0.6f - rowH * i;
                if (i > 0) Line(_frame, new Color(Edge.r, Edge.g, Edge.b, 0.2f), Pt(0.8f), 0.1f, new Vector2(r.xMin + pad, top), new Vector2(r.xMax - pad, top));
                Text(labels[i], new Vector2(r.xMin + pad, top - rowH * 0.24f), lab, Label, TextAnchor.MiddleLeft);
                vals[i] = Text("", new Vector2(r.xMin + pad, top - rowH * 0.64f), big, Value, TextAnchor.MiddleLeft, 128);
                if (i > 0) ofs[i] = Text("/20", new Vector2(r.xMin + pad, top - rowH * 0.64f - big * 0.27f), big * 0.45f, Dim, TextAnchor.MiddleLeft);
            }
            _resultText = vals[0]; _totalText = vals[1]; _avgText = vals[2]; _totalOf = ofs[1]; _avgOf = ofs[2];
            _stackGap = big * 0.15f;
        }

        void HeroPanel()
        {
            Rect r = _heroes;
            float gap = Mathf.Min(r.width * 0.04f, Pt(8f)), w = (r.width - gap) / 2f;
            string[] names = { "ENTRY SCORE", "SPEED SCORE" };
            Color[] cols = { EntryCol, SpeedCol };
            for (int d = 0; d < 2; d++)
            {
                var cell = new Rect(r.xMin + d * (w + gap), r.yMin, w, r.height);
                Color c = cols[d];
                Panel(cell, new Color(c.r, c.g, c.b, 0.55f), new Color(c.r * 0.12f + 0.02f, c.g * 0.12f + 0.04f, c.b * 0.12f + 0.06f, 0.96f));
                float pad = Mathf.Min(cell.width * 0.08f, cell.height * 0.1f, Pt(11f));
                float lab = Mathf.Min(Pt(9.5f), cell.height * 0.1f, cell.width * 0.068f);
                Text(names[d], new Vector2(cell.xMin + pad, cell.yMax - pad - lab * 0.5f), lab, c, TextAnchor.MiddleLeft);
                float barH = Mathf.Min(cell.height * 0.085f, Pt(8f));
                float num = Mathf.Min(cell.height * 0.4f, cell.width * 0.3f);
                float numZ = cell.yMin + pad + barH + (cell.height - pad * 2f - barH - lab) * 0.42f;
                _heroNum[d] = Text("--", new Vector2(cell.xMin + pad, numZ), num, Value, TextAnchor.MiddleLeft, 192);
                _heroOf[d] = Text("/10", new Vector2(cell.xMin + pad, numZ), num * 0.34f, Dim, TextAnchor.MiddleLeft);
                _heroNumX[d] = cell.xMin + pad; _heroNumZ[d] = numZ; _heroOfDrop[d] = num * 0.33f;
                // The meter: ten segments along the foot of the cell.
                float bw = (cell.width - pad * 2f), sg = bw / Segs, inset = Mathf.Max(Pt(1.2f), sg * 0.1f);
                for (int i = 0; i < Segs; i++)
                {
                    var seg = new Rect(cell.xMin + pad + i * sg + inset * 0.5f, cell.yMin + pad, sg - inset, barH);
                    Quad(_frame, seg, new Color(c.r * 0.25f + 0.05f, c.g * 0.25f + 0.07f, c.b * 0.25f + 0.1f, 1f), 0.15f, 2);
                    Transform lit = Quad(_frame, seg, new Color(c.r, c.g, c.b, 1f), 0.25f, 3).transform;
                    _heroSeg[d][i] = lit;
                }
            }
        }

        void PropsPanel()
        {
            Rect r = _props;
            Panel(r, Edge, PanelFill);
            float padX = Mathf.Min(r.width * 0.04f, Pt(10f)), padZ = Mathf.Min(r.height * 0.05f, Pt(5f));
            float colW = (r.width - padX * 2f) / 2f, rowH = (r.height - padZ * 2f) / 3f;
            float val = Mathf.Min(Pt(12.5f), rowH * 0.36f, colW / 10.5f), lab = Mathf.Min(Pt(8.5f), rowH * 0.22f, val * 0.64f);
            Line(_frame, new Color(Edge.r, Edge.g, Edge.b, 0.2f), Pt(0.8f), 0.1f, new Vector2(r.center.x, r.yMin + padZ * 1.5f), new Vector2(r.center.x, r.yMax - padZ * 1.5f));
            for (int i = 0; i < 6; i++)
            {
                int col = i % 2, row = i / 2;
                float x = r.xMin + padX + col * colW + (col == 1 ? padX * 0.6f : 0f), top = r.yMax - padZ - row * rowH;
                if (row > 0) Line(_frame, new Color(Edge.r, Edge.g, Edge.b, 0.2f), Pt(0.8f), 0.1f, new Vector2(r.xMin + col * (r.width / 2f) + padX * (col == 0 ? 1f : 0.6f), top), new Vector2(r.xMin + (col + 1) * (r.width / 2f) - padX * (col == 0 ? 0.6f : 1f), top));
                Text(PropLabels[i], new Vector2(x, top - rowH * 0.27f), lab, Label, TextAnchor.MiddleLeft);
                _propText[i] = Text("", new Vector2(x, top - rowH * 0.7f), val, Value, TextAnchor.MiddleLeft, 128);
                _propText[i].gameObject.name = "Prop " + i;
                _propMax[i] = (colW - padX * 1.2f) * 0.92f;
            }
        }
        readonly float[] _propMax = new float[6];

        void PlotPanel()
        {
            Rect r = _plot;
            Panel(r, Edge, new Color(0.02f, 0.045f, 0.075f, 0.96f));
            float pad = Mathf.Min(r.width * 0.07f, Pt(8f));
            float lab = Mathf.Min(Pt(8.5f), r.width * 0.075f), head = lab * 3.2f;
            // A wide plot names itself with its status on the right; a narrow one shows the status alone.
            _statusRight = r.width > Pt(120f);
            if (_statusRight) Text("TRACE", new Vector2(r.xMin + pad, r.yMax - head * 0.5f), lab, Label, TextAnchor.MiddleLeft);
            _statusText = _statusRight
                ? Text("", new Vector2(r.xMax - pad, r.yMax - head * 0.5f), lab * 0.95f, EntryCol, TextAnchor.MiddleRight)
                : Text("", new Vector2(r.xMin + pad + lab * 1.3f, r.yMax - head * 0.5f), lab * 0.95f, EntryCol, TextAnchor.MiddleLeft);
            GameObject dot = Make.MeshObject("Status dot", _frame, Painted(Make.Disc(1f, 20)), Fill(Color.white));
            dot.GetComponent<MeshRenderer>().sortingOrder = 4;
            _statusDot = dot.transform;
            _dot = lab * 0.42f;
            _statusDot.localScale = Vector3.one * _dot;
            _statusDot.localPosition = new Vector3(r.xMax - pad, 0.3f, r.yMax - head * 0.5f); // set in Render, beside the words
            Line(_frame, new Color(Edge.r, Edge.g, Edge.b, 0.3f), Pt(0.8f), 0.1f, new Vector2(r.xMin, r.yMax - head), new Vector2(r.xMax, r.yMax - head));
            _plotArea = new Rect(r.xMin + pad * 0.5f, r.yMin + pad * 0.5f, r.width - pad, r.height - head - pad);

            // Inches into the plot: the putt from the spot to two feet past the cup, as large as fits.
            float spanIn = ZFar - ZNear;
            _k = Mathf.Min(_plotArea.height / spanIn, _plotArea.width / 20f);
            _halfIn = _plotArea.width / 2f / _k;

            if (_plotRoot != null) Make.Kill(_plotRoot.gameObject);
            _plotRoot = new GameObject("Plot").transform;
            _plotRoot.SetParent(_frame, false);

            // The blueprint: every 6 inches, the feet stronger and numbered.
            float zTop = ZFar, zBot = ZFar - _plotArea.height / _k;
            for (float x = -Mathf.Floor(_halfIn / 6f) * 6f; x <= _halfIn; x += 6f)
            {
                bool foot = Mathf.Abs(x % 12f) < 0.1f;
                Line(_plotRoot, new Color(0.35f, 0.62f, 0.92f, foot ? 0.24f : 0.11f), Pt(foot ? 0.9f : 0.6f), 0.12f, P(new Vector2(x, zBot)), P(new Vector2(x, zTop)));
            }
            for (float z = Mathf.Ceil(zBot / 6f) * 6f; z <= zTop; z += 6f)
            {
                bool foot = Mathf.Abs(z % 12f) < 0.1f;
                Line(_plotRoot, new Color(0.35f, 0.62f, 0.92f, foot ? 0.24f : 0.11f), Pt(foot ? 0.9f : 0.6f), 0.12f, P(new Vector2(-_halfIn, z)), P(new Vector2(_halfIn, z)));
                if (foot && z < -1f && z >= -_feet * 12f - 1f)
                {
                    Vector2 at = P(new Vector2(_halfIn, z)) + new Vector2(-Pt(2f), Pt(3.2f));
                    Text(Mathf.RoundToInt(-z / 12f) + " FT", at, Mathf.Min(Pt(5.5f), 3.2f * _k), new Color(0.55f, 0.75f, 0.95f, 0.75f), TextAnchor.MiddleRight).transform.SetParent(_plotRoot, true);
                }
            }
            // The line from the spot to the cup, and on past it.
            Vector2 s = TeeAt(_feet, _tee);
            Vector2 dir = (-s).normalized;
            Line(_plotRoot, new Color(0.8f, 0.84f, 0.9f, 0.45f), Pt(1f), 0.15f, P(s), P(dir * 22f));
            Ring(_plotRoot, s, 2.2f, 2.9f, new Color(0.85f, 0.9f, 0.95f, 0.9f), 0.2f);
            GameObject cup = Make.MeshObject("Cup", _plotRoot, Painted(Make.Disc(CupR * _k, 48)), Fill(new Color(0f, 0f, 0f, 1f)));
            cup.transform.localPosition = new Vector3(P(Vector2.zero).x, 0.16f, P(Vector2.zero).y);
            cup.GetComponent<MeshRenderer>().sortingOrder = 2;
            Ring(_plotRoot, Vector2.zero, CupR, CupR + 0.55f, new Color(0.9f, 0.94f, 1f, 1f), 0.2f);
            Ring(_plotRoot, Vector2.zero, CupR + 4f, CupR + 4.3f, new Color(0.6f, 0.7f, 0.8f, 0.45f), 0.2f);

            // Every roll so far, the last bright, and where each would have rested.
            for (int i = 0; i < _rolls.Count; i++)
            {
                bool latest = i == _rolls.Count - 1 && _status != "TRACKING";
                LineRenderer l = Pen(_plotRoot, "Roll", latest ? _liveMat : _oldMat, Pt(latest ? 2.2f : 1.4f));
                l.numCapVertices = 4;
                l.sortingOrder = 5;
                Points(l, Plotted(_rolls[i], 0.3f));
            }
            for (int i = 0; i < _restsAt.Count; i++)
            {
                GameObject ring = Make.MeshObject("Rest", _plotRoot, Dashes(3.2f * _k, 10, 0.35f * _k), i == _restsAt.Count - 1 && _status != "TRACKING" ? _restMat : _restOld);
                ring.transform.localPosition = new Vector3(P(Clip(_restsAt[i])).x, 0.35f, P(Clip(_restsAt[i])).y);
                ring.GetComponent<MeshRenderer>().sortingOrder = 5;
            }
            _live = Pen(_plotRoot, "Live roll", _liveMat, Pt(2.2f));
            _live.numCapVertices = 4;
            _live.sortingOrder = 6;
            Points(_live, Plotted(_rolling, 0.32f));

            if (_ball != null) Make.Kill(_ball.gameObject);
            _ball = Shape(PrimitiveType.Sphere, "Ball", _plotRoot, Vector3.zero, Vector3.one * Mathf.Max(BallR * 2f * _k, Pt(5f)), _ballMat).transform;
            _ball.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            _ball.gameObject.SetActive(false);

            // The scan line that sweeps the readout as it is read.
            GameObject scan = Make.MeshObject("Scan", _frame, UnitQuad(), _scanMat);
            scan.GetComponent<MeshRenderer>().sortingOrder = 12;
            _scan = scan.transform;
            _scan.gameObject.SetActive(false);
        }

        // ── Drawing helpers ─────────────────────────────────────────────────────

        Vector2 P(Vector2 inches) => new Vector2(_plotArea.center.x + inches.x * _k, _plotArea.yMax - (ZFar - inches.y) * _k);
        Vector2 Clip(Vector2 inches) => new Vector2(Mathf.Clamp(inches.x, -_halfIn + 1.5f, _halfIn - 1.5f), Mathf.Clamp(inches.y, ZFar - _plotArea.height / _k + 1f, ZFar - 1.5f));
        List<Vector3> Plotted(List<Vector2> roll, float lift)
        {
            var pts = new List<Vector3>(roll.Count);
            foreach (Vector2 p in roll) { Vector2 q = P(Clip(p)); pts.Add(new Vector3(q.x, lift, q.y)); }
            return pts;
        }

        // The Glow shader is tinted by vertex colour: give a mesh white ones.
        static Mesh Painted(Mesh m)
        {
            var c = new Color[m.vertexCount];
            for (int i = 0; i < c.Length; i++) c[i] = Color.white;
            m.colors = c;
            return m;
        }

        Material Fill(Color c)
        {
            if (!_fills.TryGetValue(c, out Material m)) { m = Make.Glow(c, false); _fills[c] = m; }
            return m;
        }

        static Mesh UnitQuad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "Lab quad" };
            _quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) });
            _quad.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            _quad.SetUVs(0, new List<Vector2> { Vector2.zero, Vector2.right, Vector2.up, Vector2.one });
            _quad.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            _quad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            _quad.RecalculateBounds();
            return _quad;
        }

        GameObject Quad(Transform parent, Rect r, Color c, float lift, int order)
        {
            GameObject q = Make.MeshObject("Plate", parent, UnitQuad(), Fill(c));
            q.transform.localPosition = new Vector3(r.center.x, lift, r.center.y);
            q.transform.localScale = new Vector3(r.width, 1f, r.height);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sortingOrder = order;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return q;
        }

        LineRenderer Line(Transform parent, Color c, float width, float lift, params Vector2[] pts)
        {
            LineRenderer l = Pen(parent, "Ink", Fill(c), width);
            l.sortingOrder = 2;
            var v = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++) v[i] = new Vector3(pts[i].x, lift, pts[i].y);
            Points(l, v);
            return l;
        }

        static LineRenderer Pen(Transform parent, string name, Material m, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = m;
            line.widthMultiplier = width;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 0;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.positionCount = 0;
            return line;
        }

        void Ring(Transform parent, Vector2 inches, float inner, float outer, Color c, float lift)
        {
            GameObject g = Make.MeshObject("Ring", parent, Painted(Make.Ring(inner * _k, outer * _k, 64)), Fill(c));
            Vector2 at = P(inches);
            g.transform.localPosition = new Vector3(at.x, lift, at.y);
            g.GetComponent<MeshRenderer>().sortingOrder = 3;
        }

        TextMesh Text(string s, Vector2 at, float height, Color c, TextAnchor anchor, int res = 96)
        {
            TextMesh t = Words(_frame, s, new Vector3(at.x, 0.5f, at.y), height, c, anchor, res);
            t.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            return t;
        }

        // How wide a line of text is drawn (board units).
        static float WidthOf(TextMesh t)
        {
            if (string.IsNullOrEmpty(t.text) || t.font == null) return 0f;
            t.font.RequestCharactersInTexture(t.text, t.fontSize, t.fontStyle);
            float px = 0f;
            foreach (char ch in t.text) if (t.font.GetCharacterInfo(ch, out CharacterInfo ci, t.fontSize, t.fontStyle)) px += ci.advance;
            return px * t.characterSize * 0.1f;
        }

        static Mesh Dashes(float r, int count, float thick)
        {
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            for (int k = 0; k < count; k++)
            {
                for (int s = 0; s <= 3; s++)
                {
                    float a = (k + s / 3f * 0.55f) / count * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    verts.Add(d * (r - thick)); verts.Add(d * (r + thick)); cols.Add(Color.white); cols.Add(Color.white);
                }
                int b = k * 8;
                for (int s = 0; s < 3; s++) { int i = b + s * 2; tris.Add(i); tris.Add(i + 3); tris.Add(i + 1); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            }
            var m = new Mesh();
            m.SetVertices(verts); m.SetColors(cols); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ── The readout ─────────────────────────────────────────────────────────

        void Clear()
        {
            for (int i = 0; i < 6; i++) { _prop[i] = ""; _propShown[i] = 0f; }
            _hero[0] = _hero[1] = 0f; _heroFill[0] = _heroFill[1] = 0f; _heroLive = false;
            _result = ""; _total = ""; _avg = "";
        }

        void Render()
        {
            if (_frame == null) return;
            for (int i = 0; i < 6; i++)
            {
                TextMesh t = _propText[i];
                string s = _prop[i] ?? "";
                int n = Mathf.Clamp(Mathf.CeilToInt(s.Length * _propShown[i]), 0, s.Length);
                t.text = s.Length == 0 ? "--" : s.Substring(0, n);
                t.color = s.Length == 0 ? Dim : Value;
                // Shrink a long value to its cell.
                t.characterSize = _propBase(t);
                float w = WidthOf(t), max = _propMax[i];
                if (w > max && w > 0f) t.characterSize *= max / w;
            }
            for (int d = 0; d < 2; d++)
            {
                float v = _hero[d];
                _heroNum[d].text = _heroLive ? Reading(v) : "--";
                _heroNum[d].color = _heroLive ? Value : Dim;
                float w = WidthOf(_heroNum[d]);
                _heroOf[d].transform.localPosition = new Vector3(_heroNumX[d] + w + _heroOfDrop[d] * 0.25f, 0.5f, _heroNumZ[d] - _heroOfDrop[d] * 0.55f);
                float fill = _heroFill[d] * 10f;
                for (int i = 0; i < Segs; i++)
                {
                    Transform seg = _heroSeg[d][i];
                    float f = Mathf.Clamp01(fill - i);
                    seg.gameObject.SetActive(f > 0.01f);
                    Rect full = SegRect(seg);
                    seg.localScale = new Vector3(full.width * f, 1f, full.height);
                    seg.localPosition = new Vector3(full.xMin + full.width * f / 2f, seg.localPosition.y, full.center.y);
                }
            }
            _resultText.text = _result.Length == 0 ? "--" : _result;
            _resultText.color = _result.Length == 0 ? Dim : _resultCol;
            _totalText.text = _total.Length == 0 ? "--" : _total;
            _totalText.color = _total.Length == 0 ? Dim : Value;
            _avgText.text = _avg.Length == 0 ? "--" : _avg;
            _avgText.color = _avg.Length == 0 ? Dim : Value;
            foreach (var (val, of) in new[] { (_totalText, _totalOf), (_avgText, _avgOf) })
            {
                Vector3 at = of.transform.localPosition;
                of.transform.localPosition = new Vector3(val.transform.localPosition.x + WidthOf(val) + _stackGap, at.y, at.z);
            }
            _statusText.text = _status;
            _statusText.color = _statusCol;
            Vector3 sp = _statusText.transform.localPosition;
            _statusDot.localPosition = new Vector3(_statusRight ? sp.x - WidthOf(_statusText) - _dot * 2f : sp.x - _dot * 2f, 0.5f, sp.z);
            _statusDot.GetComponent<MeshRenderer>().sharedMaterial = Fill(new Color(_statusCol.r, _statusCol.g, _statusCol.b, 1f));
        }

        // Each meter segment remembers its full size the first time it is drawn.
        readonly Dictionary<Transform, Rect> _segFull = new Dictionary<Transform, Rect>();
        Rect SegRect(Transform seg)
        {
            if (!_segFull.TryGetValue(seg, out Rect r))
            {
                r = new Rect(seg.localPosition.x - seg.localScale.x / 2f, seg.localPosition.z - seg.localScale.z / 2f, seg.localScale.x, seg.localScale.z);
                _segFull[seg] = r;
            }
            return r;
        }

        readonly Dictionary<TextMesh, float> _baseSize = new Dictionary<TextMesh, float>();
        float _propBase(TextMesh t)
        {
            if (!_baseSize.TryGetValue(t, out float s)) { s = t.characterSize; _baseSize[t] = s; }
            return s;
        }

        static string Reading(float v) => Mathf.Abs(v - Mathf.Round(v)) < 0.05f ? Mathf.Round(v).ToString("0") : v.ToString("0.0");
        static string Twenty(float v) => v.ToString("0.0");

        static string Side(float v, string left, string right) => v < 0 ? left : right;
        static string Deg(float v) => Mathf.Abs(v) < 0.5f ? "STRAIGHT" : Mathf.Abs(v).ToString("0.0") + "° " + Side(v, "LEFT", "RIGHT");
        static string Inches(float v, string neg, string pos, string zero)
        {
            float a = Mathf.Abs(v);
            if (a < 0.25f) return zero;
            return (a >= 24f ? (a / 12f).ToString("0.0") + " FT " : a.ToString("0.0") + " IN ") + (v < 0 ? neg : pos);
        }

        void Read(ShotView v)
        {
            string tee = v.Str("teeName", v.Tee == 1 ? "Left" : v.Tee == 3 ? "Right" : "Center").ToUpperInvariant();
            // Not the distance: the tee sign shows it. How far the entry was off the tee's own line instead.
            _prop[0] = v.Made && v.Data.ContainsKey("offLine") ? Mathf.Abs(v.Num("offLine")).ToString("0.0") + "\u00B0" : "--";
            _prop[1] = v.HasRollout ? Inches(v.RolloutIn, "SHORT", "PAST", "DEAD CENTER") : "NO READ";
            _prop[2] = v.Made && v.HasEntry ? Deg(v.EntryDeg) : "--";
            _prop[3] = v.Reached || Mathf.Abs(v.LineIn) > 0.01f ? Inches(v.LineIn, "LEFT", "RIGHT", "CENTER") : "SHORT";
            string desc = v.Str("entryDesc");
            _prop[4] = v.Made ? (desc.Length > 0 ? desc : "MADE")
                : v.MissSide == "LEFT" ? "MISSED LEFT" : v.MissSide == "RIGHT" ? "MISSED RIGHT"
                : v.HasRollout && v.RolloutIn < 0 ? "SHORT" : "LIP OUT";
            _prop[5] = v.Data.ContainsKey("launch") ? Deg(v.Num("launch")) : "NO READ";
            _result = v.Made ? "MADE" : "MISS";
            _resultCol = v.Made ? EntryCol : Red;
        }

        // ── The roll ────────────────────────────────────────────────────────────

        static Vector2 TeeAt(int feet, int tee) => new Vector2(tee == 1 ? -8f : tee == 3 ? 8f : 0f, -Mathf.Max(3, feet) * 12f);

        static Vector2 Finish(ShotView v)
        {
            if (v.Made) return Vector2.zero;
            float past = v.HasRollout ? v.RolloutIn : v.Reached ? 10f : -10f;
            float side = v.LineIn;
            if (v.Reached && Mathf.Abs(side) < CupR + 0.5f) side = (v.MissSide == "LEFT" || (v.MissSide == "" && side < 0)) ? -(CupR + 1f) : CupR + 1f;
            if (v.Reached && past > 0) side *= 1f + past / 30f;
            return new Vector2(Mathf.Clamp(side, -30f, 30f), Mathf.Clamp(past, -60f, 30f));
        }

        static List<Vector2> RollOf(ShotView v)
        {
            Vector2 start = TeeAt(v.Feet, v.Tee), end = Finish(v);
            var pts = new List<Vector2>();
            if (v.Path != null && v.Path.Count >= 2)
            {
                if (v.Path[0].y > start.y + 12f) pts.Add(start);
                pts.AddRange(v.Path);
                if (Vector2.Distance(pts[pts.Count - 1], end) > 3f && (v.Made || v.HasRollout)) pts.Add(end);
                return pts;
            }
            Vector2 bend = new Vector2((start.x + end.x) * 0.5f + Mathf.Clamp(v.LineIn, -6f, 6f) * 0.9f, (start.y + end.y) * 0.5f);
            for (int i = 0; i <= 30; i++) { float t = i / 30f; pts.Add((1 - t) * (1 - t) * start + 2 * (1 - t) * t * bend + t * t * end); }
            return pts;
        }

        static float Roll(float t, bool made) { float slow = 1f - (1f - t) * (1f - t); return made ? Mathf.Lerp(t, slow, 0.6f) : slow; }

        static Vector2 Along(List<Vector2> pts, float t)
        {
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
            float want = Mathf.Clamp01(t) * total;
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector2.Distance(pts[i - 1], pts[i]);
                if (want <= seg && seg > 0f) return Vector2.Lerp(pts[i - 1], pts[i], want / seg);
                want -= seg;
            }
            return pts[pts.Count - 1];
        }

        void BallAt(Vector2 inches, float drop = 0f)
        {
            Vector2 q = P(Clip(inches));
            _ball.localPosition = new Vector3(q.x, _ball.localScale.x * 0.5f + 0.4f - drop, q.y);
        }

        // ── State ───────────────────────────────────────────────────────────────

        void Lay(int feet, int tee, bool force = false)
        {
            feet = Mathf.Clamp(feet, 3, 11);
            if (!force && feet == _feet && tee == _tee && _frame != null && ViewShape() == _shape) return;
            _feet = feet; _tee = tee;
            Layout();
        }

        void Spot()
        {
            _ball.gameObject.SetActive(true);
            BallAt(TeeAt(_feet, _tee));
        }

        void Status(string s, Color c) { _status = s; _statusCol = c; }

        public override void Begin(ShotView start)
        {
            _rolls.Clear(); _restsAt.Clear(); _rolling = new List<Vector2>();
            Clear();
            Status("READY", EntryCol);
            Lay(start.Int("feet", start.NextFeet), start.Int("tee", start.NextTee), true);
            Spot();
        }

        public override void Ready(ShotView v)
        {
            Status(v.Over ? "COMPLETE" : "READY", v.Over ? SpeedCol : EntryCol);
            Render();
            if (!v.Over) Spot();
        }

        public override void Tick()
        {
            if (_frame == null) return;
            if (ViewShape() != _shape) { Layout(); if (_status == "READY") Spot(); }
            // The status light breathes.
            float a = 0.55f + 0.45f * Mathf.Sin(Time.time * (_status == "READY" ? 3f : 9f));
            _statusDot.localScale = Vector3.one * _dot * (0.75f + 0.25f * a);
        }

        public override IEnumerator Play(ShotView v)
        {
            _feet = Mathf.Clamp(v.Int("feet", v.Feet), 3, 11); _tee = v.Int("tee", v.Tee);
            Sound("lab_beep", 0.5f);
            // The panel clears and the last roll joins the faint ones.
            _rolling = new List<Vector2>();
            Clear();
            Status("TRACKING", Amber);
            Layout();

            List<Vector2> roll = RollOf(v);
            _ball.gameObject.SetActive(true);
            float seconds = Mathf.Lerp(1.1f, 1.8f, Mathf.InverseLerp(3f, 11f, v.Feet));
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                Vector2 p = Along(roll, Roll(t, v.Made));
                BallAt(p);
                _rolling.Add(p);
                Points(_live, Plotted(_rolling, 0.32f));
                yield return null;
            }
            Vector2 end = roll[roll.Count - 1];
            BallAt(end);
            if (v.Made)
            {
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localScale = Vector3.one * Mathf.Max(BallR * 2f * _k, Pt(5f)) * (1f - t * 0.8f); yield return null; }
                _ball.gameObject.SetActive(false);
                _ball.localScale = Vector3.one * Mathf.Max(BallR * 2f * _k, Pt(5f));
            }
            // Where it would have rested.
            if (v.HasRollout || !v.Made)
            {
                float leave = v.HasRollout ? v.RolloutIn : end.y;
                _restsAt.Add(v.Made ? new Vector2(end.x * 0.5f, Mathf.Clamp(leave, -40f, 30f)) : end);
            }
            _rolls.Add(_rolling);
            _rolling = new List<Vector2>();
            Status("ANALYZING", Amber);
            Layout();
            if (!v.Made) _ball.gameObject.SetActive(true);
            if (!v.Made) BallAt(end);

            // The scan line sweeps the readout.
            Read(v);
            float left = Mathf.Min(_heroes.xMin, _props.xMin), right = Mathf.Max(_stack.xMax, _props.xMax);
            float top = _stack.yMax, bottom = _props.yMin;
            _scan.gameObject.SetActive(true);
            _scan.localScale = new Vector3(right - left + Pt(8f), 1f, Pt(1.6f));
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.32f)
            {
                _scan.localPosition = new Vector3((left + right) / 2f, 0.8f, Mathf.Lerp(top, bottom, Ease(t)));
                yield return null;
            }
            _scan.gameObject.SetActive(false);

            // The two hero readings count up and their meters fill while the measured values type in, one after
            // another.
            float[] paces = v.Get<float[]>("pace") ?? new float[0];
            float[] entries = v.Get<float[]>("entry") ?? new float[0];
            float pace = paces.Length > 0 ? paces[paces.Length - 1] : 0f, entry = entries.Length > 0 ? entries[entries.Length - 1] : 0f;
            _heroLive = true;
            Sound("lab_beep", 0.35f, 1.25f);
            const float each = 0.14f, stagger = 0.06f, count = 0.7f;
            for (float t = 0f; t < count; t += Time.deltaTime)
            {
                for (int i = 0; i < 6; i++) _propShown[i] = Mathf.Clamp01((t - i * stagger) / each);
                float e = Ease(t / count);
                _hero[0] = entry * e; _hero[1] = pace * e;
                _heroFill[0] = entry / 10f * e; _heroFill[1] = pace / 10f * e;
                Render();
                yield return null;
            }
            for (int i = 0; i < 6; i++) _propShown[i] = 1f;
            _hero[0] = entry; _hero[1] = pace; _heroFill[0] = entry / 10f; _heroFill[1] = pace / 10f;

            // Then the putt's total and the running average.
            float total = v.Num("total", entry + pace), avg = v.Num("best", total);
            Render();
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
            {
                float e = Ease(t);
                _total = Twenty(total * e); _avg = Twenty(avg * e);
                Render();
                yield return null;
            }
            _total = Twenty(total); _avg = Twenty(avg);
            Status("COMPLETE", SpeedCol);
            Render();
            if (entry + pace >= 18f && Arcade != null) Arcade.Sparks(_board.TransformPoint(new Vector3(_heroes.center.x, 2f, _heroes.center.y)), 14);
            yield return new WaitForSeconds(0.35f);
        }
    }
}
