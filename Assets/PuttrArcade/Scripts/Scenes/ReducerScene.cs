// Reducer, up close on the hole (Matt, 2026-11-23: the gate and its circle sit on the 4.25 inch hole, from Center
// Cut's viewpoint, see HoleView): the mat's real cup, lit in synthwave neon, and round its front a gate of two laser
// walls set to the entry window the Player chose (120° to 30°). Inside the walls the floor glows cyan; outside it
// is red. A make through the gate lights it up; a make outside it flares the red on that side.
// Units are inches x K; the cup is at the origin, the putter looks up +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class ReducerScene : RuleScene
    {
        const float K = 1.6f, CupR = 2.125f * K, BallR = 0.84f * K, Beam = CupR * 2.7f, WallH = 2.4f;
        static readonly Color Cyan = new Color(0.25f, 1f, 0.9f), Violet = new Color(0.62f, 0.3f, 1f), Hot = new Color(1f, 0.18f, 0.35f);

        Transform _ball, _gate;
        LineRenderer _trail;
        readonly List<Transform> _posts = new List<Transform>();
        readonly List<LineRenderer> _beams = new List<LineRenderer>(), _wallTops = new List<LineRenderer>();
        readonly List<MeshFilter> _walls = new List<MeshFilter>();
        MeshFilter _inside, _outL, _outR;
        Material _insideMat, _outLMat, _outRMat;
        TextMesh _sign;
        int _window = -1;
        float _flashIn, _flashL, _flashR;

        public override Vector3 CameraFrom => At(HoleView.From(CupR));
        public override Vector3 CameraLook => At(HoleView.Look(CupR));
        public override float CameraFov => HoleView.Fov;
        public override float CardLens => 54f;

        public override void Build()
        {
            // The floor round the mat: near black, a violet grid (none of it over the mat or the cup).
            HoleView.Floor(Root, CupR, -0.6f, Make.Lit(new Color(0.02f, 0.01f, 0.04f), 0.85f));
            Material grid = Make.Glow(new Color(Violet.r, Violet.g, Violet.b, 0.55f));
            const float matHalf = 12f * K;
            for (int i = -12; i <= 12; i++)
            {
                if (Mathf.Abs(i * 8f) < matHalf + 1f) continue;
                LineRenderer l = Stroke(Root, "Grid", Violet, 0.25f); l.sharedMaterial = grid;
                Points(l, new[] { new Vector3(i * 8f, -0.5f, -120f), new Vector3(i * 8f, -0.5f, 120f) });
            }
            for (int i = -15; i <= 15; i++)
                foreach (int sd in new[] { -1, 1 })
                {
                    LineRenderer l = Stroke(Root, "Grid", Violet, 0.25f); l.sharedMaterial = grid;
                    Points(l, new[] { new Vector3(sd * (matHalf + 1f), -0.5f, i * 8f), new Vector3(sd * 100f, -0.5f, i * 8f) });
                }

            // The mat and its real hole, cyan edges.
            HoleView.Build(Root, CupR, new Color(0.03f, 0.14f, 0.1f));
            Material edge = Make.Glow(new Color(Cyan.r, Cyan.g, Cyan.b, 0.9f));
            foreach (float x in new[] { -matHalf, matHalf })
            {
                LineRenderer l = Stroke(Root, "Edge", Cyan, 0.3f); l.sharedMaterial = edge;
                Points(l, new[] { new Vector3(x, 0.1f, -200f), new Vector3(x, 0.1f, 7f * K) });
            }
            LineRenderer back = Stroke(Root, "Edge", Cyan, 0.3f); back.sharedMaterial = edge;
            Points(back, new[] { new Vector3(-matHalf, 0.1f, 7f * K), new Vector3(matHalf, 0.1f, 7f * K) });

            // The gate: rebuilt for the window. Its floor zones: cyan inside, red outside.
            _gate = new GameObject("Gate").transform;
            _gate.SetParent(Root, false);
            _gate.localPosition = new Vector3(0, 0.06f, 0);
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
                Shape(PrimitiveType.Cylinder, "Pillar", p, new Vector3(0, WallH / 2f, 0), new Vector3(0.7f, WallH / 2f, 0.7f), post);
                Shape(PrimitiveType.Sphere, "Lamp", p, new Vector3(0, WallH + 0.35f, 0), Vector3.one * 0.9f, cap);
                Shape(PrimitiveType.Cylinder, "Foot", p, new Vector3(0, 0.1f, 0), new Vector3(1.3f, 0.1f, 1.3f), post);
                _posts.Add(p);
                LineRenderer b = Stroke(_gate, "Beam", Cyan, 0.3f); b.sharedMaterial = Make.Glow(new Color(Cyan.r, Cyan.g, Cyan.b, 1f));
                _beams.Add(b);
                LineRenderer top = Stroke(_gate, "Wall top", Cyan, 0.16f); top.sharedMaterial = Make.Glow(new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f));
                _wallTops.Add(top);
                _walls.Add(Make.MeshObject("Laser wall", _gate, new Mesh(), wall).GetComponent<MeshFilter>());
            }
            // The window's size, flat on the mat behind the cup.
            _sign = Words(Root, "", new Vector3(0f, 0.12f, CupR + 1.5f * K), 1.25f * K, Cyan);
            _sign.transform.localRotation = Quaternion.Euler(90f, 0, 0);

            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.65f, 0f, bc, bn)).transform;
            _trail = Stroke(Root, "Trace", new Color(0.85f, 1f, 1f, 0.95f), 0.3f);

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

        void Gate(int window)
        {
            if (window == _window) return;
            if (_window > 0 && window < _window) Sound("reducer_shrink", 0.55f);   // puttr-app-2: the window narrows (silent until recorded)
            _window = window;
            float half = window * 0.5f;
            _inside.sharedMesh = Sector(CupR + 0.15f, Beam, -half, half, Color.white, 32);
            _outL.sharedMesh = Sector(CupR + 0.15f, Beam, -90f, -half, Color.white, 24);
            _outR.sharedMesh = Sector(CupR + 0.15f, Beam, half, 90f, Color.white, 24);
            for (int s = 0; s < 2; s++)
            {
                Vector3 d = Dir(s == 0 ? -half : half);
                _posts[s].localPosition = d * (Beam + 0.5f);
                Points(_beams[s], new[] { d * (CupR + 0.15f) + Vector3.up * 0.05f, d * (Beam + 0.5f) + Vector3.up * 0.05f });
                Points(_wallTops[s], new[] { d * (CupR + 0.15f) + Vector3.up * 0.5f, d * (Beam + 0.5f) + Vector3.up * WallH });
                _walls[s].sharedMesh = Curtain(d, CupR + 0.15f, Beam + 0.5f, WallH);
            }
            _sign.text = window + "°";
        }

        // ── The roll ──

        // Everything below is in inches x K, true to scale, so the line the ball comes in on is the line it took.
        static Vector2 TeeAt(int feet, int tee) => new Vector2(tee == 1 ? -8f : tee == 3 ? 8f : 0f, -Mathf.Max(3, feet) * 12f) * K;

        static Vector2 Finish(ShotView v)
        {
            if (v.Made) return Vector2.zero;
            float past = v.HasRollout ? v.RolloutIn : v.Reached ? 10f : -10f;
            float side = v.LineIn;
            if (v.Reached && Mathf.Abs(side) < 2.6f) side = (v.MissSide == "LEFT" || (v.MissSide == "" && side < 0)) ? -3.1f : 3.1f;
            if (v.Reached && past > 0) side *= 1f + past / 30f;
            return new Vector2(Mathf.Clamp(side, -30f, 30f), Mathf.Clamp(past, -60f, 60f)) * K;
        }

        static List<Vector2> RollOf(ShotView v)
        {
            Vector2 start = TeeAt(v.Feet, v.Tee), end = Finish(v);
            var pts = new List<Vector2>();
            if (v.Path != null && v.Path.Count >= 2)
            {
                if (v.Path[0].y * K > start.y + 12f * K) pts.Add(start);
                foreach (Vector2 q in v.Path) pts.Add(q * K);
                if (Vector2.Distance(pts[pts.Count - 1], end) > 1.5f * K && (v.Made || v.HasRollout)) pts.Add(end);
                return pts;
            }
            Vector2 bend = new Vector2((start.x + end.x) * 0.5f + Mathf.Clamp(v.LineIn, -6f, 6f) * 0.9f * K, (start.y + end.y) * 0.5f);
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
            _ball.localPosition = new Vector3(s.x, BallR, s.y);
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
                _ball.localPosition = new Vector3(p.x, BallR, p.y);
                trace.Add(new Vector3(p.x, 0.1f, p.y));
                Points(_trail, trace);
                yield return null;
            }
            Vector2 end = roll[roll.Count - 1];
            _ball.localPosition = new Vector3(end.x, BallR, end.y);
            if (v.Made)
            {
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localPosition = new Vector3(0, Mathf.Lerp(BallR, -BallR * 3f, t), 0); yield return null; }
                _ball.gameObject.SetActive(false);
                if (v.Good)
                {
                    _flashIn = 1f;
                    Sound("reducer_pass", "rule_bonus", 0.35f, 1.4f);   // puttr-app-2: the game's own sound, the shared one until recorded
                    Arcade.Sparks(At(0, 1.5f, 0), 26);
                }
                else
                {
                    float rel = v.Num("rel", v.EntryDeg);
                    if (rel < 0) _flashL = 1f; else _flashR = 1f;
                    Sound("reducer_alarm", "laser_zap", 0.6f, 0.6f);   // puttr-app-2: the game's own sound, the shared one until recorded
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
        }
    }
}
