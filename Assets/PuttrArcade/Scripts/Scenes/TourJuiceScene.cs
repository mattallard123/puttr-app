// Tour Juice on a tour green in the afternoon: striped grass, the pin, and beside the green a broadcast Juice Meter,
// two arcs that fill with the putt's line (outer) and its pace (inner) and the juice in the middle, with the tour
// average marked. A ruler runs through the cup along the line: where the putt would have finished, short or past,
// is the leave, marked with a gold puck. Units are inches; the cup is at the origin, the putter looks up +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class TourJuiceScene : RuleScene
    {
        const float CupR = 3.4f, BallR = 1.7f, Near = 0.6f, TourAverage = 0.8f;
        const int Segs = 26;

        Transform _ball, _leave, _gauge;
        LineRenderer _trail;
        TextMesh _juice, _leaveTag;
        readonly List<Renderer> _lineSegs = new List<Renderer>(), _paceSegs = new List<Renderer>();
        readonly List<Material> _segLit = new List<Material>();
        Material _segDim;
        Transform _flag;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(Portrait ? -4f : 16f, 58f, Portrait ? -165f : -160f);
        public override Vector3 CameraLook => At(12f, 12f, 8f);
        public override float CameraFov => Portrait ? 50f : 42f;
        public override float ShadowDistance => 500f;

        public override void Build()
        {
            // Sky, a treeline, grandstand-green hills.
            GameObject sky = Make.MeshObject("Sky", Root, Gradient(9000f, new[] { -100f, 150f, 600f, 1500f, 3200f },
                new[] { new Color(0.82f, 0.9f, 0.97f), new Color(0.72f, 0.84f, 0.96f), new Color(0.5f, 0.7f, 0.93f), new Color(0.33f, 0.55f, 0.88f), new Color(0.22f, 0.42f, 0.8f) }), Make.Glow(Color.white, false));
            sky.transform.localPosition = new Vector3(0, 0, 3000f);
            Material tree = Make.Lit(new Color(0.08f, 0.2f, 0.09f), 0.2f);
            Material tree2 = Make.Lit(new Color(0.12f, 0.27f, 0.11f), 0.2f);
            var dice = new System.Random(4);
            for (int i = 0; i < 30; i++)
            {
                float x = -2600f + i * 180f + (float)dice.NextDouble() * 70f, h = 140f + (float)dice.NextDouble() * 160f;
                Shape(PrimitiveType.Sphere, "Tree", Root, new Vector3(x, h * 0.4f, 1600f + (float)dice.NextDouble() * 200f), new Vector3(200f + (float)dice.NextDouble() * 120f, h, 160f), i % 2 == 0 ? tree : tree2);
            }

            // Grass: mowing stripes to the trees, a fringe and the green.
            Material light = Make.Lit(new Color(0.3f, 0.6f, 0.24f), 0.3f);
            Material dark = Make.Lit(new Color(0.22f, 0.5f, 0.19f), 0.3f);
            for (int i = -40; i <= 40; i++)
                Shape(PrimitiveType.Cube, "Stripe", Root, new Vector3(i * 36f, -1f, 700f), new Vector3(36f, 2f, 2400f), i % 2 == 0 ? light : dark);
            Shape(PrimitiveType.Cylinder, "Green", Root, new Vector3(0, 0.1f, -20f), new Vector3(320f, 0.1f, 300f), Make.Lit(new Color(0.3f, 0.62f, 0.24f), 0.4f));
            Material greenStripe = Make.Lit(new Color(0.36f, 0.68f, 0.28f), 0.4f);
            for (int i = -7; i <= 7; i += 2)
            {
                float x = i * 20f, half = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(x / 160f, 2f))) * 150f - 4f;
                if (half > 8f) Shape(PrimitiveType.Cube, "Stripe", Root, new Vector3(x, 0.22f, -20f), new Vector3(20f, 0.04f, half * 2f), greenStripe);
            }

            // The cup and pin.
            GameObject cup = Make.MeshObject("Cup", Root, Make.Disc(CupR, 48), Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f));
            cup.transform.localPosition = new Vector3(0, 0.26f, 0);
            Hoop("Liner", Root, new Vector3(0, 0.28f, 0), CupR, CupR + 0.5f, Make.Lit(Color.white, 0.5f));
            Shape(PrimitiveType.Cylinder, "Pin", Root, new Vector3(0, 30f, 0), new Vector3(0.8f, 30f, 0.8f), Make.Lit(new Color(0.96f, 0.96f, 0.94f), 0.6f));
            _flag = new GameObject("Flag").transform;
            _flag.SetParent(Root, false);
            _flag.localPosition = new Vector3(0, 54f, 0);
            Shape(PrimitiveType.Cube, "Cloth", _flag, new Vector3(11f, 0, 0), new Vector3(22f, 13f, 0.4f), Make.Lit(new Color(0.92f, 0.12f, 0.12f), 0.3f));

            // The leave ruler: through the cup along the line, ticks every 3 in to 24 either side.
            Material chalk = Make.Glow(new Color(1f, 1f, 1f, 0.55f), false);
            LineRenderer spine = Stroke(Root, "Ruler", Color.white, 0.45f, false); spine.sharedMaterial = chalk;
            Points(spine, new[] { new Vector3(0, 0.35f, -26f), new Vector3(0, 0.35f, -CupR - 0.6f) });
            LineRenderer spine2 = Stroke(Root, "Ruler", Color.white, 0.45f, false); spine2.sharedMaterial = chalk;
            Points(spine2, new[] { new Vector3(0, 0.35f, CupR + 0.6f), new Vector3(0, 0.35f, 26f) });
            for (int k = -8; k <= 8; k++)
            {
                if (k == 0) continue;
                float z = k * 3f, w = k % 4 == 0 ? 3.2f : 1.6f;
                if (Mathf.Abs(z) < CupR + 1f) continue;
                LineRenderer tick = Stroke(Root, "Tick", Color.white, 0.4f, false); tick.sharedMaterial = chalk;
                Points(tick, new[] { new Vector3(-w, 0.35f, z), new Vector3(w, 0.35f, z) });
            }
            TextMesh past = Words(Root, "PAST", new Vector3(-8f, 0.4f, 24f), 2.6f, new Color(1f, 1f, 1f, 0.75f));
            past.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            TextMesh shortW = Words(Root, "SHORT", new Vector3(-9f, 0.4f, -24f), 2.6f, new Color(1f, 1f, 1f, 0.75f));
            shortW.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            _leave = new GameObject("Leave").transform;
            _leave.SetParent(Root, false);
            Hoop("Puck", _leave, new Vector3(0, 0.4f, 0), 0f, 2.6f, Make.Emissive(new Color(1f, 0.75f, 0.2f), new Color(1.6f, 1.1f, 0.25f)));
            Hoop("Halo", _leave, new Vector3(0, 0.38f, 0), 3.2f, 3.8f, Make.Emissive(new Color(1f, 0.75f, 0.2f), new Color(1f, 0.7f, 0.15f)));
            _leave.gameObject.SetActive(false);
            _leaveTag = Words(Root, "", Vector3.zero, 4.2f, new Color(1f, 0.82f, 0.35f), TextAnchor.MiddleLeft);
            _leaveTag.transform.localRotation = Quaternion.Euler(18f, 0, 0);

            // The Juice Meter: a broadcast board on a stand beside the green.
            _gauge = new GameObject("Juice Meter").transform;
            _gauge.SetParent(Root, false);
            _gauge.localPosition = new Vector3(44f, 25f, 24f);
            _gauge.localRotation = Quaternion.Euler(4f, 20f, 0);
            _gauge.localScale = Vector3.one * 0.95f;
            Material housing = Make.Lit(new Color(0.05f, 0.06f, 0.08f), 0.7f, 0.3f);
            Material steel = Make.Lit(new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.9f);
            GameObject box = Make.MeshObject("Housing", _gauge, Make.RoundedBox(new Vector3(66f, 46f, 4f), 3f), housing);
            box.transform.localPosition = new Vector3(0, 0, 2f);
            foreach (float x in new[] { -24f, 24f }) Shape(PrimitiveType.Cylinder, "Leg", _gauge, new Vector3(x, -27f, 3f), new Vector3(2f, 12f, 2f), steel);
            Shape(PrimitiveType.Cube, "Accent", _gauge, new Vector3(0, 21.6f, -0.2f), new Vector3(66f, 1.2f, 0.4f), Make.Emissive(new Color(1f, 0.6f, 0.15f), new Color(2f, 1.1f, 0.2f)));
            Words(_gauge, "JUICE METER", new Vector3(-29f, 17.5f, -0.4f), 2.6f, new Color(1f, 0.75f, 0.3f), TextAnchor.MiddleLeft);
            _segDim = Make.Lit(new Color(0.14f, 0.15f, 0.18f), 0.4f);
            for (int i = 0; i < Segs; i++)
            {
                float u = (i + 0.5f) / Segs;
                Color c = Color.Lerp(new Color(1f, 0.38f, 0.12f), new Color(1f, 0.86f, 0.3f), u);
                _segLit.Add(Make.Emissive(c, c * 1.8f, 0.5f));
                float a0 = Mathf.Lerp(-90f, 90f, i / (float)Segs) + 0.9f, a1 = Mathf.Lerp(-90f, 90f, (i + 1f) / Segs) - 0.9f;
                _lineSegs.Add(Make.MeshObject("Line", _gauge, Arc(15.5f, 20f, a0, a1), _segDim).GetComponent<Renderer>());
                _paceSegs.Add(Make.MeshObject("Pace", _gauge, Arc(9.5f, 14f, a0, a1), _segDim).GetComponent<Renderer>());
                _lineSegs[i].transform.localPosition = new Vector3(0, -9f, -0.3f);
                _paceSegs[i].transform.localPosition = new Vector3(0, -9f, -0.3f);
            }
            // The tour average, marked on both arcs.
            float avg = Mathf.Lerp(-90f, 90f, TourAverage) * Mathf.Deg2Rad;
            LineRenderer mark = Stroke(_gauge, "Tour average", Color.white, 0.6f, false);
            Points(mark, new[] { new Vector3(Mathf.Sin(avg) * 8.5f, Mathf.Cos(avg) * 8.5f - 9f, -0.6f), new Vector3(Mathf.Sin(avg) * 21.5f, Mathf.Cos(avg) * 21.5f - 9f, -0.6f) });
            Words(_gauge, "TOUR", new Vector3(Mathf.Sin(avg) * 24.5f, Mathf.Cos(avg) * 24.5f - 9f, -0.6f), 1.8f, new Color(1f, 1f, 1f, 0.8f));
            Words(_gauge, "LINE", new Vector3(-17.8f, -11.2f, -0.4f), 1.5f, new Color(1f, 1f, 1f, 0.75f));
            Words(_gauge, "PACE", new Vector3(-11.8f, -11.2f, -0.4f), 1.5f, new Color(1f, 1f, 1f, 0.75f));
            _juice = Words(_gauge, "", new Vector3(0, -6.5f, -0.4f), 8f, Color.white);
            Words(_gauge, "JUICE", new Vector3(0, -12.5f, -0.4f), 2f, new Color(1f, 0.75f, 0.3f));

            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(new Color(0.98f, 0.98f, 0.97f), 0.65f)).transform;
            _trail = Stroke(Root, "Line", new Color(1f, 0.62f, 0.2f, 0.95f), 1.2f, false);

            Sun(Root, new Vector3(42f, -35f, 0f), new Color(1f, 0.96f, 0.88f), 1.25f);
            Lamp(Root, new Vector3(-120f, 200f, -260f), new Vector3(30f, 20f, 30f), new Color(0.85f, 0.9f, 1f), 120000f, 50f, 900f);
        }

        // A segment of a ring standing in XY, angles from straight up towards +X.
        static Mesh Arc(float inner, float outer, float fromDeg, float toDeg)
        {
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>(); var cols = new List<Color>();
            const int segs = 6;
            for (int s = 0; s <= segs; s++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, s / (float)segs) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0);
                verts.Add(d * inner); verts.Add(d * outer);
                norms.Add(Vector3.back); norms.Add(Vector3.back); cols.Add(Color.white); cols.Add(Color.white);
            }
            for (int s = 0; s < segs; s++) { int i = s * 2; tris.Add(i); tris.Add(i + 1); tris.Add(i + 3); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
            var m = new Mesh();
            m.SetVertices(verts); m.SetNormals(norms); m.SetColors(cols); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

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

        void Fill(float line, float pace)
        {
            for (int i = 0; i < Segs; i++)
            {
                float u = (i + 0.5f) / Segs;
                _lineSegs[i].sharedMaterial = u <= line ? _segLit[i] : _segDim;
                _paceSegs[i].sharedMaterial = u <= pace ? _segLit[i] : _segDim;
            }
        }

        void Tee(int feet, int tee)
        {
            Vector2 s = TeeAt(feet, tee);
            _ball.gameObject.SetActive(true);
            _ball.localPosition = new Vector3(s.x, BallR + 0.3f, s.y);
        }

        public override void Begin(ShotView start)
        {
            Fill(0f, 0f);
            _juice.text = "--";
            Tee(start.NextFeet, start.NextTee);
        }

        public override void Ready(ShotView v) { if (!v.Over) Tee(v.NextFeet, v.NextTee); }

        public override IEnumerator Play(ShotView v)
        {
            Fill(0f, 0f);
            _juice.text = "";
            _leave.gameObject.SetActive(false);
            _leaveTag.text = "";
            List<Vector2> roll = RollOf(v);
            var line = new List<Vector3>();
            _ball.gameObject.SetActive(true);
            float seconds = Mathf.Lerp(1.1f, 1.8f, Mathf.InverseLerp(3f, 11f, v.Feet));
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                Vector2 p = Along(roll, Roll(t, v.Made));
                _ball.localPosition = new Vector3(p.x, BallR + 0.3f, p.y);
                line.Add(new Vector3(p.x, 0.5f, p.y));
                Points(_trail, line);
                yield return null;
            }
            Vector2 end = roll[roll.Count - 1];
            if (v.Made)
            {
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f) { _ball.localPosition = new Vector3(0, Mathf.Lerp(BallR + 0.3f, -4f, t), 0); yield return null; }
                _ball.gameObject.SetActive(false);
                Sound("golf_cup", 0.7f);
            }
            // The leave: where it would have finished, along the line.
            if (v.HasRollout || !v.Made)
            {
                float leave = v.HasRollout ? v.RolloutIn : end.y;
                float shown = Mathf.Clamp(leave < 0f ? leave * Near : leave, -26f, 26f);
                Vector3 at = v.Made ? new Vector3(0, 0, shown) : new Vector3(end.x, 0, end.y);
                _leave.localPosition = at;
                _leave.gameObject.SetActive(true);
                int inches = Mathf.Abs(Mathf.RoundToInt(leave));
                _leaveTag.text = inches == 0 ? "DEAD WEIGHT" : inches + " IN " + (leave > 0 ? "PAST" : "SHORT");
                _leaveTag.transform.localPosition = at + new Vector3(6f, 4f, 0);
            }

            // The meter fills: line, then pace, the juice counting up.
            float lineV = Mathf.Clamp01(v.Num("line")), paceV = Mathf.Clamp01(v.Num("pace"));
            float juice = Mathf.Clamp01(v.Num("juice"));
            if (juice > 0f) Sound("riser", 0.5f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.9f)
            {
                float e = Ease(t);
                Fill(lineV * Mathf.Clamp01(e * 1.6f), paceV * Mathf.Clamp01(e * 1.6f - 0.6f));
                _juice.text = Mathf.RoundToInt(juice * 100f * e).ToString();
                yield return null;
            }
            Fill(lineV, paceV);
            _juice.text = Mathf.RoundToInt(juice * 100f).ToString();
            _juice.color = juice >= 0.9f ? new Color(1f, 0.85f, 0.3f) : juice >= TourAverage ? Color.white : new Color(0.85f, 0.85f, 0.85f);
            if (juice >= 0.9f) Arcade.Sparks(At(_gauge.localPosition + new Vector3(0, 6f, -3f)), 18);
            yield return new WaitForSeconds(0.7f);
        }

        public override void Tick()
        {
            if (_flag != null) _flag.localRotation = Quaternion.Euler(0, -30f + Mathf.Sin(Time.time * 1.5f) * 10f, 0);
        }
    }
}
