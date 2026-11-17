// Reducer as a synthwave arena: a neon grid to a purple horizon, a green strip lit in cyan, and at the cup a gate of
// two laser walls set to the entry window the Player chose (120° to 30°). Inside the walls the floor glows cyan;
// outside it is red. A make through the gate lights it up; a make outside it flares the red on that side.
// Units are inches; the cup is at the origin, the putter looks up +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class ReducerScene : RuleScene
    {
        const float CupR = 3.4f, BallR = 1.6f, Beam = 46f, Near = 0.75f;
        static readonly Color Cyan = new Color(0.25f, 1f, 0.9f), Violet = new Color(0.62f, 0.3f, 1f), Hot = new Color(1f, 0.18f, 0.35f);

        Transform _ball, _gate, _halo;
        LineRenderer _trail;
        readonly List<Transform> _posts = new List<Transform>();
        readonly List<LineRenderer> _beams = new List<LineRenderer>(), _wallTops = new List<LineRenderer>();
        readonly List<MeshFilter> _walls = new List<MeshFilter>();
        MeshFilter _inside, _outL, _outR;
        Material _insideMat, _outLMat, _outRMat;
        TextMesh _sign;
        int _window = -1;
        float _flashIn, _flashL, _flashR;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(Portrait ? 4f : 20f, 84f, Portrait ? -176f : -165f);
        public override Vector3 CameraLook => At(-4f, 30f, 12f);
        public override float CameraFov => Portrait ? 54f : 44f;

        public override void Build()
        {
            // Sky: deep violet to black, stars.
            GameObject sky = Make.MeshObject("Sky", Root, Gradient(3000f, new[] { -40f, 60f, 220f, 600f, 1400f },
                new[] { new Color(0.42f, 0.12f, 0.5f), new Color(0.22f, 0.06f, 0.36f), new Color(0.08f, 0.03f, 0.2f), new Color(0.03f, 0.02f, 0.09f), new Color(0.01f, 0.01f, 0.04f) }), Make.Glow(Color.white, false));
            sky.transform.localPosition = new Vector3(0, 0, 1100f);
            Material star = Make.Glow(Color.white, true, Make.SoftDot());
            var dice = new System.Random(5);
            for (int i = 0; i < 90; i++)
            {
                float x = (float)(dice.NextDouble() * 2400 - 1200), y = (float)(150 + dice.NextDouble() * 900), s = (float)(3 + dice.NextDouble() * 6);
                GameObject st = Make.MeshObject("Star", Root, Quad(s, new Color(1f, 1f, 1f, (float)(0.4 + dice.NextDouble() * 0.6))), star);
                st.transform.localPosition = new Vector3(x, y, 1090f);
            }
            // A sun on the horizon, banded.
            GameObject sun = Make.MeshObject("Sun", Root, Quad(420f, new Color(1f, 0.35f, 0.6f, 0.55f)), Make.Glow(Color.white, true, Make.SoftDot()));
            sun.transform.localPosition = new Vector3(0, 40f, 1080f);

            // The floor: near black, a violet grid to the horizon.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -1.5f, 400f), new Vector3(4000f, 2f, 1600f), Make.Lit(new Color(0.02f, 0.01f, 0.04f), 0.85f));
            Material grid = Make.Glow(new Color(Violet.r, Violet.g, Violet.b, 0.55f));
            for (int i = -30; i <= 30; i++)
            {
                LineRenderer l = Stroke(Root, "Grid", Violet, 0.9f); l.sharedMaterial = grid;
                Points(l, new[] { new Vector3(i * 40f, 0.05f, -300f), new Vector3(i * 40f, 0.05f, 1100f) });
            }
            for (int i = 0; i < 36; i++)
            {
                float z = -300f + i * 40f;
                LineRenderer l = Stroke(Root, "Grid", Violet, 0.9f); l.sharedMaterial = grid;
                Points(l, new[] { new Vector3(-1200f, 0.05f, z), new Vector3(1200f, 0.05f, z) });
            }

            // The strip: dark green, cyan edges, the cup with a target ring.
            Shape(PrimitiveType.Cube, "Strip", Root, new Vector3(0, 0.25f, -36f), new Vector3(96f, 0.5f, 190f), Make.Lit(new Color(0.03f, 0.2f, 0.11f), 0.6f));
            Material edge = Make.Glow(new Color(Cyan.r, Cyan.g, Cyan.b, 0.9f));
            foreach (float x in new[] { -48f, 48f })
            {
                LineRenderer l = Stroke(Root, "Edge", Cyan, 1.1f); l.sharedMaterial = edge;
                Points(l, new[] { new Vector3(x, 0.6f, -131f), new Vector3(x, 0.6f, 59f) });
            }
            LineRenderer back = Stroke(Root, "Edge", Cyan, 1.1f); back.sharedMaterial = edge;
            Points(back, new[] { new Vector3(-48f, 0.6f, 59f), new Vector3(48f, 0.6f, 59f) });
            GameObject cup = Make.MeshObject("Cup", Root, Make.Disc(CupR, 48), Make.Lit(Color.black, 0.2f));
            cup.transform.localPosition = new Vector3(0, 0.55f, 0);
            Hoop("Liner", Root, new Vector3(0, 0.6f, 0), CupR, CupR + 0.5f, Make.Emissive(Color.white, new Color(1.5f, 1.5f, 1.5f)));
            Hoop("Target", Root, new Vector3(0, 0.6f, 0), CupR + 3f, CupR + 3.6f, Make.Emissive(Color.black, Cyan * 1.6f));
            Hoop("Target", Root, new Vector3(0, 0.6f, 0), CupR + 7f, CupR + 7.4f, Make.Emissive(Color.black, Cyan * 0.8f));

            // The gate: rebuilt for the window. Its floor zones: cyan inside, red outside.
            _gate = new GameObject("Gate").transform;
            _gate.SetParent(Root, false);
            _gate.localPosition = new Vector3(0, 0.58f, 0);
            _insideMat = Make.Glow(new Color(Cyan.r, Cyan.g, Cyan.b, 0.22f));
            _outLMat = Make.Glow(new Color(Hot.r, Hot.g, Hot.b, 0.22f));
            _outRMat = Make.Glow(new Color(Hot.r, Hot.g, Hot.b, 0.22f));
            _inside = Make.MeshObject("Inside", _gate, new Mesh(), _insideMat).GetComponent<MeshFilter>();
            _outL = Make.MeshObject("Outside left", _gate, new Mesh(), _outLMat).GetComponent<MeshFilter>();
            _outR = Make.MeshObject("Outside right", _gate, new Mesh(), _outRMat).GetComponent<MeshFilter>();
            Material post = Make.Lit(new Color(0.1f, 0.12f, 0.16f), 0.8f, 0.7f);
            Material cap = Make.Emissive(Color.white, new Color(2.5f, 3f, 3f));
            Material wall = Make.Glow(Color.white);
            for (int s = 0; s < 2; s++)
            {
                Transform p = new GameObject("Post").transform;
                p.SetParent(_gate, false);
                Shape(PrimitiveType.Cylinder, "Pillar", p, new Vector3(0, 13f, 0), new Vector3(3.2f, 13f, 3.2f), post);
                Shape(PrimitiveType.Sphere, "Lamp", p, new Vector3(0, 27.5f, 0), Vector3.one * 3.6f, cap);
                Shape(PrimitiveType.Cylinder, "Foot", p, new Vector3(0, 0.6f, 0), new Vector3(5.5f, 0.6f, 5.5f), post);
                _posts.Add(p);
                LineRenderer b = Stroke(_gate, "Beam", Cyan, 1.2f); b.sharedMaterial = Make.Glow(new Color(Cyan.r, Cyan.g, Cyan.b, 1f));
                _beams.Add(b);
                LineRenderer top = Stroke(_gate, "Wall top", Cyan, 0.7f); top.sharedMaterial = Make.Glow(new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f));
                _wallTops.Add(top);
                _walls.Add(Make.MeshObject("Laser wall", _gate, new Mesh(), wall).GetComponent<MeshFilter>());
            }
            _sign = Words(Root, "", new Vector3(10f, 46f, 44f), 9f, Cyan);

            // A halo over the arena: a tilted neon ring with ticks, like a protractor.
            _halo = new GameObject("Halo").transform;
            _halo.SetParent(Root, false);
            _halo.localPosition = new Vector3(0, 120f, 120f);
            _halo.localRotation = Quaternion.Euler(-12f, 0, 0);
            LineRenderer ring = Stroke(_halo, "Ring", Violet, 2.2f);
            var pts = new List<Vector3>();
            for (int i = 0; i <= 96; i++) { float a = i / 96f * Mathf.PI * 2f; pts.Add(new Vector3(Mathf.Cos(a) * 170f, 0, Mathf.Sin(a) * 170f)); }
            Points(ring, pts);
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                LineRenderer tick = Stroke(_halo, "Tick", Violet, 1.6f);
                Points(tick, new[] { new Vector3(Mathf.Cos(a) * 158f, 0, Mathf.Sin(a) * 158f), new Vector3(Mathf.Cos(a) * 170f, 0, Mathf.Sin(a) * 170f) });
            }

            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Emissive(Color.white, new Color(0.6f, 0.9f, 0.9f), 0.7f)).transform;
            _trail = Stroke(Root, "Trace", new Color(0.85f, 1f, 1f, 0.95f), 1.3f);

            Sun(Root, new Vector3(45f, 10f, 0f), new Color(0.7f, 0.55f, 1f), 0.35f);
            Lamp(Root, new Vector3(0, 160f, -60f), new Vector3(0, 0, -10f), new Color(0.5f, 1f, 0.95f), 140000f, 60f, 500f);
            Lamp(Root, new Vector3(-180f, 120f, 120f), new Vector3(0, 0, 0), new Color(1f, 0.3f, 0.8f), 160000f, 50f, 600f);
        }

        static Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a)); }

        static Mesh Sector(float inner, float outer, float fromDeg, float toDeg, Color color, int segs)
        {
            var verts = new List<Vector3>(); var cols = new List<Color>(); var norms = new List<Vector3>(); var tris = new List<int>();
            for (int s = 0; s <= segs; s++)
            {
                Vector3 d = Dir(Mathf.Lerp(fromDeg, toDeg, s / (float)segs));
                verts.Add(d * inner); verts.Add(d * outer);
                cols.Add(color); cols.Add(color); norms.Add(Vector3.up); norms.Add(Vector3.up);
            }
            for (int s = 0; s < segs; s++) { int i = s * 2; tris.Add(i); tris.Add(i + 3); tris.Add(i + 1); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            var m = new Mesh();
            m.SetVertices(verts); m.SetColors(cols); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // A vertical wall from the cup out along a direction: bright at the floor, fading up.
        static Mesh Curtain(Vector3 dir, float from, float to, float height)
        {
            var m = new Mesh();
            m.SetVertices(new List<Vector3> { dir * from, dir * to, dir * from + Vector3.up * height, dir * to + Vector3.up * height });
            Color lo = new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f), hi = new Color(Cyan.r, Cyan.g, Cyan.b, 0.05f);
            m.SetColors(new List<Color> { lo, lo, hi, hi });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            m.RecalculateBounds();
            return m;
        }

        // A vertical gradient sheet in XY, bands at the given heights.
        static Mesh Gradient(float width, float[] ys, Color[] cs)
        {
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            for (int i = 0; i < ys.Length; i++)
            {
                verts.Add(new Vector3(-width / 2, ys[i], 0)); verts.Add(new Vector3(width / 2, ys[i], 0));
                cols.Add(cs[i]); cols.Add(cs[i]);
                if (i > 0) { int a = (i - 1) * 2; tris.Add(a); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3); }
            }
            var m = new Mesh();
            m.SetVertices(verts); m.SetColors(cols); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh Quad(float size, Color c)
        {
            float h = size / 2;
            var m = new Mesh();
            m.SetVertices(new List<Vector3> { new Vector3(-h, -h, 0), new Vector3(h, -h, 0), new Vector3(-h, h, 0), new Vector3(h, h, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetColors(new List<Color> { c, c, c, c });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            m.RecalculateBounds();
            return m;
        }

        void Gate(int window)
        {
            if (window == _window) return;
            _window = window;
            float half = window * 0.5f;
            _inside.sharedMesh = Sector(CupR + 0.6f, Beam, -half, half, Color.white, 32);
            _outL.sharedMesh = Sector(CupR + 0.6f, Beam, -90f, -half, Color.white, 24);
            _outR.sharedMesh = Sector(CupR + 0.6f, Beam, half, 90f, Color.white, 24);
            for (int s = 0; s < 2; s++)
            {
                Vector3 d = Dir(s == 0 ? -half : half);
                _posts[s].localPosition = d * (Beam + 2f);
                Points(_beams[s], new[] { d * (CupR + 0.5f) + Vector3.up * 0.3f, d * (Beam + 2f) + Vector3.up * 0.3f });
                Points(_wallTops[s], new[] { d * (CupR + 0.5f) + Vector3.up * 3f, d * (Beam + 2f) + Vector3.up * 26f });
                _walls[s].sharedMesh = Curtain(d, CupR + 0.5f, Beam + 2f, 26f);
            }
            _sign.text = window + "°";
        }

        // ── The roll ──

        static Vector2 TeeAt(int feet, int tee) => new Vector2(tee == 1 ? -8f : tee == 3 ? 8f : 0f, -Mathf.Max(3, feet) * 12f * Near);

        static Vector2 Finish(ShotView v)
        {
            if (v.Made) return Vector2.zero;
            float past = v.HasRollout ? v.RolloutIn : v.Reached ? 10f : -10f;
            float side = v.LineIn;
            if (v.Reached && Mathf.Abs(side) < CupR + 0.5f) side = (v.MissSide == "LEFT" || (v.MissSide == "" && side < 0)) ? -(CupR + 1f) : CupR + 1f;
            if (v.Reached && past > 0) side *= 1f + past / 30f;
            return new Vector2(Mathf.Clamp(side, -30f, 30f), Mathf.Clamp(past < 0f ? past * Near : past, -60f, 60f));
        }

        static List<Vector2> RollOf(ShotView v)
        {
            Vector2 start = TeeAt(v.Feet, v.Tee), end = Finish(v);
            var pts = new List<Vector2>();
            if (v.Path != null && v.Path.Count >= 2)
            {
                if (v.Path[0].y * Near > start.y + 12f) pts.Add(start);
                foreach (Vector2 q in v.Path) pts.Add(new Vector2(q.x, q.y < 0f ? q.y * Near : q.y));
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

        // ── State ──

        void Aim(ShotView v, int feet, int tee)
        {
            Gate(v.Int("window", 120));
            Sound("laser_zap", 0.4f);
            Vector2 s = TeeAt(feet, tee);
            _gate.localRotation = Quaternion.Euler(0, -Mathf.Atan2(s.x, -s.y) * Mathf.Rad2Deg, 0);
            _ball.gameObject.SetActive(true);
            _ball.localPosition = new Vector3(s.x, BallR + 0.5f, s.y);
        }

        public override void Begin(ShotView start) { Aim(start, start.NextFeet, start.NextTee); _trail.positionCount = 0; }
        public override void Ready(ShotView v) { Aim(v, v.NextFeet, v.NextTee); }
        public override void Changed(ShotView v) { Aim(v, v.NextFeet, v.NextTee); }

        public override IEnumerator Play(ShotView v)
        {
            Gate(v.Int("window", 120));
            Vector2 tee = TeeAt(v.Feet, v.Tee);
            _gate.localRotation = Quaternion.Euler(0, -Mathf.Atan2(tee.x, -tee.y) * Mathf.Rad2Deg, 0);
            List<Vector2> roll = RollOf(v);
            var trace = new List<Vector3>();
            _ball.gameObject.SetActive(true);
            float seconds = Mathf.Lerp(1.1f, 1.8f, Mathf.InverseLerp(3f, 11f, v.Feet));
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                Vector2 p = Along(roll, Roll(t, v.Made));
                _ball.localPosition = new Vector3(p.x, BallR + 0.5f, p.y);
                trace.Add(new Vector3(p.x, 0.9f, p.y));
                Points(_trail, trace);
                yield return null;
            }
            Vector2 end = roll[roll.Count - 1];
            _ball.localPosition = new Vector3(end.x, BallR + 0.5f, end.y);
            if (v.Made)
            {
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localPosition = new Vector3(0, Mathf.Lerp(BallR + 0.5f, -3f, t), 0); yield return null; }
                _ball.gameObject.SetActive(false);
                if (v.Good)
                {
                    _flashIn = 1f;
                    Sound("rule_bonus", 0.35f, 1.4f);
                    Arcade.Sparks(At(0, 4f, 0), 26);
                }
                else
                {
                    float rel = v.Num("rel", v.EntryDeg);
                    if (rel < 0) _flashL = 1f; else _flashR = 1f;
                    Sound("laser_zap", 0.6f, 0.6f);
                    Arcade.Shake(0.25f);
                }
            }
            yield return new WaitForSeconds(0.8f);
        }

        public override void Tick()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
            _flashIn = Mathf.Max(0f, _flashIn - Time.deltaTime * 0.9f);
            _flashL = Mathf.Max(0f, _flashL - Time.deltaTime * 0.9f);
            _flashR = Mathf.Max(0f, _flashR - Time.deltaTime * 0.9f);
            if (_insideMat != null) _insideMat.SetColor("_Color", new Color(Cyan.r, Cyan.g, Cyan.b, 0.16f + 0.06f * pulse + 0.5f * _flashIn));
            if (_outLMat != null) _outLMat.SetColor("_Color", new Color(Hot.r, Hot.g, Hot.b, 0.2f + 0.6f * _flashL));
            if (_outRMat != null) _outRMat.SetColor("_Color", new Color(Hot.r, Hot.g, Hot.b, 0.2f + 0.6f * _flashR));
            if (_halo != null) _halo.localRotation = Quaternion.Euler(-12f, Time.time * 4f, 0);
        }
    }
}
