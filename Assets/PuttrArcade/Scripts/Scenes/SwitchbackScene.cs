// Switchback as a synthwave valley: a neon grid floor running to a striped sunset between wireframe mountains. The
// putts still to come are a hairpin road zigzagging away to the horizon, each leg pink from the left tee and cyan
// from the right. The putt rolls in from its tee leaving a neon trail of its colour; a make drops through the glowing
// ring and fires it, a miss runs on by. Then the road slides one hairpin closer.
// Units: inches x K round the cup (at the origin); the golfer is down -Z, the horizon far up +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class SwitchbackScene : RuleScene
    {
        const float K = 4.5f, CupR = 2.125f * K, BallR = 0.84f * K, Reach = 200f;
        const float RoadStart = 100f, Leg = 170f, RoadX = 70f;
        static readonly Color Pink = new Color(1f, 0.25f, 0.65f), Cyan = new Color(0.2f, 0.85f, 1f);

        Transform _road, _ball;
        LineRenderer _trail;
        Material _ringMat, _ringHot;
        Renderer _ring;
        float _roadZ;
        int _index = -1;
        Light _ringLight;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(0f, Portrait ? 170f : 120f, Portrait ? -260f : -230f);
        public override Vector3 CameraLook => At(0f, Portrait ? 95f : 40f, 260f);
        public override float CameraFov => Portrait ? 60f : 44f;

        public override void Build()
        {
            // Sky, the striped sun, the mountains on the horizon.
            Shape(PrimitiveType.Cube, "Sky", Root, new Vector3(0, 600f, 3400f), new Vector3(9000f, 2400f, 2f), Make.Emissive(Color.black, new Color(0.05f, 0.008f, 0.09f)));
            Shape(PrimitiveType.Cube, "Haze", Root, new Vector3(0, 60f, 3300f), new Vector3(9000f, 260f, 2f), Make.Glow(new Color(1f, 0.2f, 0.6f, 0.25f)));
            const float sunR = 520f, sunY = 160f;
            for (int i = 0; i < 36; i++)
            {
                float y0 = sunR - i * (sunR * 2f / 36f), gap = i < 18 ? 0f : (i % 3 == 0 ? sunR * 2f / 36f : 0f);
                float yc = y0 - sunR / 36f;
                float w = 2f * Mathf.Sqrt(Mathf.Max(0f, sunR * sunR - yc * yc));
                float h = sunR * 2f / 36f + 0.5f - gap;
                if (h <= 0.5f) continue;
                Color c = Color.Lerp(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.2f, 0.55f), i / 35f);
                GameObject band = Shape(PrimitiveType.Cube, "Sun", Root, new Vector3(380f, sunY + yc, 3250f), new Vector3(w, h, 2f), Make.Emissive(c, c * 1.6f));
                band.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            Material hill = Make.Emissive(Color.black, new Color(0.03f, 0.005f, 0.05f));
            var ridge = new List<Vector3>();
            var rnd = new System.Random(3);
            for (int i = 0; i <= 26; i++)
            {
                float x = -2600f + i * 200f, y = Mathf.Abs(x) < 520f ? 40f + (float)rnd.NextDouble() * 60f : 120f + (float)rnd.NextDouble() * 320f;
                ridge.Add(new Vector3(x, y, 3000f));
                GameObject m = Shape(PrimitiveType.Cube, "Hill", Root, new Vector3(x, y - 150f * 0.7071f, 3010f), new Vector3(150f + y * 0.6f, 150f + y * 0.6f, 2f), hill);
                m.transform.localPosition = new Vector3(x, y - (150f + y * 0.6f) * 0.7071f, 3010f);
                m.transform.localRotation = Quaternion.Euler(0, 0, 45f);
            }
            LineRenderer outline = Stroke(Root, "Ridge", new Color(1f, 0.3f, 0.75f, 0.9f), 5f);
            Points(outline, ridge);

            // The grid floor.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -1f, 1400f), new Vector3(9000f, 2f, 3400f), Make.Lit(new Color(0.03f, 0.01f, 0.05f), 0.3f));
            for (int i = -24; i <= 24; i++)
            {
                LineRenderer l = Stroke(Root, "Grid", new Color(0.85f, 0.2f, 1f, 0.35f), 2f);
                Points(l, new[] { new Vector3(i * 80f, 0.2f, -400f), new Vector3(i * 80f, 0.2f, 3000f) });
            }
            for (int i = 0; i < 40; i++)
            {
                float z = -400f + i * 90f;
                if (z > 3000f) break;
                LineRenderer l = Stroke(Root, "Grid", new Color(0.85f, 0.2f, 1f, 0.35f), 2f);
                Points(l, new[] { new Vector3(-2000f, 0.2f, z), new Vector3(2000f, 0.2f, z) });
            }

            // The cup and its rings.
            GameObject hole = Make.MeshObject("Hole", Root, Make.Disc(CupR, 48), Make.Lit(new Color(0.005f, 0f, 0.01f), 0.2f));
            hole.transform.localPosition = new Vector3(0, 0.4f, 0);
            _ringMat = Make.Emissive(Pink, Pink * 2.2f);
            _ringHot = Make.Emissive(Color.white, new Color(5f, 2f, 4f));
            _ring = Hoop("Ring", Root, new Vector3(0, 0.5f, 0), CupR + 0.3f, CupR + 2.2f, _ringMat).GetComponent<Renderer>();
            Hoop("Target", Root, new Vector3(0, 0.3f, 0), 38f, 40f, Make.Glow(new Color(1f, 0.3f, 0.8f, 0.4f)));
            Hoop("Target", Root, new Vector3(0, 0.3f, 0), 72f, 73.5f, Make.Glow(new Color(1f, 0.3f, 0.8f, 0.22f)));
            GameObject pad = Make.MeshObject("Pad", Root, Make.Disc(74f, 64), Make.Glow(new Color(0.6f, 0.1f, 0.5f, 0.12f)));
            pad.transform.localPosition = new Vector3(0, 0.25f, 0);
            _ringLight = World.NewLight(Root, "Ring light", LightType.Point, Pink, 3000f);
            _ringLight.transform.localPosition = new Vector3(0, 20f, 0);
            _ringLight.range = 160f;
            _ringLight.shadows = LightShadows.None;

            _road = new GameObject("Road").transform;
            _road.SetParent(Root, false);

            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Emissive(Color.white, new Color(0.08f, 0.06f, 0.1f))).transform;
            _ball.GetComponent<Renderer>().sharedMaterial.SetTexture("_BaseMap", bc);
            _ball.gameObject.SetActive(false);
            _trail = Stroke(Root, "Trail", Cyan, 3f);

            Sun(Root, new Vector3(30f, 180f, 0f), new Color(1f, 0.6f, 0.85f), 0.5f);
            Lamp(Root, new Vector3(0, 300f, -200f), new Vector3(0, 0, 40f), new Color(0.9f, 0.7f, 1f), 300000f, 40f, 800f);
        }

        // The road: one leg a putt still to play, from the next one out to the horizon.
        void Lay(int[] tees, int index)
        {
            for (int i = _road.childCount - 1; i >= 0; i--) Object.Destroy(_road.GetChild(i).gameObject);
            for (int j = 0; index + j < tees.Length; j++)
            {
                int tee = tees[index + j];
                float x0 = tee == 1 ? -RoadX : RoadX;
                var a = new Vector3(x0, 0.6f, RoadStart + j * Leg);
                var b = new Vector3(-x0, 0.6f, RoadStart + (j + 1) * Leg);
                Color c = tee == 1 ? Pink : Cyan;
                bool next = j == 0;
                Vector3 mid = (a + b) / 2f, dir = b - a;
                GameObject leg = Shape(PrimitiveType.Cube, "Leg", _road, mid, new Vector3(next ? 30f : 24f, 1f, dir.magnitude + 20f), Make.Emissive(c * 0.6f, c * (next ? 2.2f : 1.2f)));
                leg.transform.localRotation = Quaternion.LookRotation(dir);
                leg.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                if (next)
                {
                    for (int k = 1; k < 8; k++)
                    {
                        GameObject dash = Shape(PrimitiveType.Cube, "Dash", _road, Vector3.Lerp(a, b, k / 8f) + Vector3.up * 0.8f, new Vector3(2.4f, 0.4f, dir.magnitude / 18f), Make.Emissive(Color.white, new Color(2f, 2f, 2f)));
                        dash.transform.localRotation = Quaternion.LookRotation(dir);
                    }
                    TextMesh tag = Words(_road, "NEXT  " + (tee == 1 ? "L" : "R"), mid + new Vector3(0, 30f, 0), 9f, c * 1.4f);
                    tag.transform.localRotation = Quaternion.Euler(10f, 0, 0);
                }
                TextMesh letter = Words(_road, tee == 1 ? "L" : "R", a + new Vector3(x0 > 0 ? 26f : -26f, 10f, 0), next ? 14f : 12f, c * 1.3f);
                letter.transform.localRotation = Quaternion.Euler(20f, 0, 0);
            }
        }

        void Set(ShotView v, bool snap)
        {
            int[] tees = v.Get<int[]>("tees") ?? new int[0];
            int index = v.Int("index");
            if (index != _index && _index >= 0 && !snap) _roadZ = Leg * (index - _index);
            _index = index;
            Lay(tees, v.Over ? tees.Length : index);
            Color c = v.NextTee == 1 ? Pink : Cyan;
            _ringMat.SetColor("_BaseColor", c);
            _ringMat.SetColor("_EmissionColor", c * 2.2f);
            _ringLight.color = c;
        }

        public override void Begin(ShotView start) { Set(start, true); }
        public override void Ready(ShotView v) { Set(v, false); }

        public override void Tick()
        {
            _roadZ = Mathf.Lerp(_roadZ, 0f, Time.deltaTime * 3f);
            _road.localPosition = new Vector3(0, 0, _roadZ);
            _ringLight.intensity = 3000f * (0.8f + Mathf.Sin(Time.time * 4f) * 0.2f);
        }

        public override IEnumerator Play(ShotView v)
        {
            int side = v.Tee == 1 ? -1 : 1;
            Color c = v.Tee == 1 ? Pink : Cyan;
            Sound("synth_whoosh", 0.5f, v.Tee == 1 ? 1.15f : 0.9f);
            _trail.sharedMaterial.SetColor("_Color", new Color(c.r, c.g, c.b, 0.9f));
            Vector3 start = new Vector3(side * 120f, 0, -Reach);
            List<Vector3> pts;
            Vector3 rim = Vector3.zero;
            if (v.Made)
            {
                float deg = v.HasEntry ? v.EntryDeg : 0f;
                rim = Rim(deg, CupR - BallR * 0.4f);
                Vector3 outward = Rim(Mathf.Clamp(deg, -110f, 110f), 1f);
                // In from the tee's side, breaking back across into the cup.
                pts = Bezier(start, new Vector3(side * 20f, 0, -Reach * 0.55f), outward * 60f + new Vector3(-side * 20f, 0, 0), rim);
            }
            else
            {
                float endX = Mathf.Clamp(v.LineIn * K, -90f, 90f);
                if (Mathf.Abs(endX) < CupR + BallR) endX = (v.MissSide == "LEFT" ? -1f : 1f) * (CupR + BallR + 3f);
                float endZ = v.HasRollout ? Mathf.Clamp(v.RolloutIn * K, -100f, 110f) : 60f;
                pts = Track(v, K, Reach, start, new Vector3(endX, 0, endZ));
            }
            Lift(pts, BallR);
            _ball.gameObject.SetActive(true);
            _ball.localPosition = pts[0];
            yield return Roll(_ball, pts, 1.5f, _trail, BallR);
            if (v.Made)
            {
                Sound("cup_plop", 0.7f);
                yield return Drop(_ball, rim, BallR);
                _ring.sharedMaterial = _ringHot;
                Arcade.Sparks(At(0, 6f, 0), v.Good ? 36 : 16);
                if (v.Good) Arcade.Celebrate(20, At(0, 10f, 0));
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
                {
                    _ring.transform.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.6f);
                    yield return null;
                }
                _ring.transform.localScale = Vector3.one;
                _ring.sharedMaterial = _ringMat;
            }
            else yield return new WaitForSeconds(0.6f);
            yield return new WaitForSeconds(0.2f);
            _ball.gameObject.SetActive(false);
            _trail.positionCount = 0;
        }

        // ── Rolling ──────────────────────────────────────────────────────────

        static Vector3 Rim(float deg, float r) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a) * r, 0f, -Mathf.Cos(a) * r); }

        static List<Vector3> Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int n = 40)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= n; i++) { float t = i / (float)n, u = 1f - t; pts.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d); }
            return pts;
        }

        static void Lift(List<Vector3> pts, float y) { for (int i = 0; i < pts.Count; i++) pts[i] = new Vector3(pts[i].x, y, pts[i].z); }

        static List<Vector3> Track(ShotView v, float k, float reach, Vector3 start, Vector3 end)
        {
            var pts = new List<Vector3>();
            foreach (Vector2 p in v.Path) if (p.y * k >= -reach) pts.Add(new Vector3(Mathf.Clamp(p.x * k, -reach, reach), 0f, Mathf.Min(p.y * k, 120f)));
            if (pts.Count < 4) return Bezier(start, Vector3.Lerp(start, end, 0.4f) + Vector3.right * (start.x * -0.3f), Vector3.Lerp(start, end, 0.75f), end);
            if (pts[0].z > -reach + 2f) pts.Insert(0, new Vector3(pts[0].x, 0f, -reach));
            return pts;
        }

        static IEnumerator Roll(Transform ball, List<Vector3> pts, float seconds, LineRenderer trail, float r)
        {
            int n = pts.Count;
            if (n < 2) yield break;
            var cum = new float[n];
            for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            float total = Mathf.Max(0.001f, cum[n - 1]);
            var drawn = new List<Vector3> { pts[0] };
            int seg = 0;
            Vector3 last = pts[0];
            for (float t = 0f; ; t += Time.deltaTime / seconds)
            {
                float tt = Mathf.Min(1f, t);
                float d = (1f - (1f - tt) * (1f - tt)) * total;
                while (seg < n - 2 && cum[seg + 1] < d) { seg++; drawn.Add(pts[seg]); }
                Vector3 p = Vector3.Lerp(pts[seg], pts[seg + 1], Mathf.InverseLerp(cum[seg], cum[seg + 1], d));
                Vector3 step = p - last;
                if (step.sqrMagnitude > 1e-6f) ball.localRotation = Quaternion.AngleAxis(step.magnitude / r * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, step.normalized)) * ball.localRotation;
                ball.localPosition = p;
                last = p;
                if (trail != null)
                {
                    trail.positionCount = drawn.Count + 1;
                    for (int i = 0; i < drawn.Count; i++) trail.SetPosition(i, drawn[i] - Vector3.up * (r - 0.6f));
                    trail.SetPosition(drawn.Count, p - Vector3.up * (r - 0.6f));
                }
                if (t >= 1f) break;
                yield return null;
            }
        }

        static IEnumerator Drop(Transform ball, Vector3 rim, float r)
        {
            Vector3 from = ball.localPosition;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
            {
                Vector3 p = Vector3.Lerp(from, new Vector3(rim.x * 0.2f, 0f, rim.z * 0.2f), t);
                p.y = from.y - t * t * r * 3.5f;
                ball.localPosition = p;
                yield return null;
            }
            ball.localPosition = new Vector3(0, -r * 4f, 0);
        }
    }
}
