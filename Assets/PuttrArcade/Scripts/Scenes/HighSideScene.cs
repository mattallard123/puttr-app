// High Side as a green slab tilted across an alpine ridge under the moon. The slab leans with the tee: from the left
// tee the left edge is up, from the right the right. The high half of the cup glows ice blue, the low half red, and
// the low edge drops into a red chasm. A make from the high side rings the blue half, a make from below the red; a
// high miss stops safe up the slope, a low miss runs off the low edge and falls into the chasm.
// Units: inches x K on the slab; the cup is at the slab's origin, the golfer is down -Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class HighSideScene : RuleScene
    {
        const float K = 5f, CupR = 2.125f * K, BallR = 0.84f * K, Reach = 210f, Tilt = 8f, HalfW = 125f;

        Transform _slab, _ball, _tee;
        LineRenderer _trail;
        GameObject _highArc, _lowArc;
        Material _ice, _red, _iceHot, _redHot;
        Renderer _chasmL, _chasmR;
        Material _chasm, _cliff;
        Light _chasmLight;
        TextMesh _high, _low;
        readonly List<Transform> _chevrons = new List<Transform>();
        float _tilt, _tiltTo;
        int _side = 1;   // +1: the high side is right (right tee); -1: left

        static bool Portrait => Screen.height > Screen.width;
        public override float WideFrame => 0.74f;   // a lane down the middle: nothing of it is under the side cards
        public override Vector3 CameraFrom => At(Portrait ? 130f : 190f, Portrait ? 290f : 250f, Portrait ? -470f : -460f);
        public override Vector3 CameraLook => At(Portrait ? -35f : 0f, Portrait ? 40f : 0f, Portrait ? 120f : 60f);
        public override float CameraFov => Portrait ? 56f : 42f;

        public override void Build()
        {
            _ice = Make.Emissive(new Color(0.6f, 0.9f, 1f), new Color(0.6f, 1.4f, 2.2f), 0.7f);
            _iceHot = Make.Emissive(new Color(0.8f, 0.95f, 1f), new Color(2f, 3.6f, 5f), 0.7f);
            _red = Make.Emissive(new Color(1f, 0.3f, 0.3f), new Color(1.8f, 0.25f, 0.25f), 0.6f);
            _redHot = Make.Emissive(new Color(1f, 0.4f, 0.4f), new Color(5f, 0.8f, 0.6f), 0.6f);
            _chasm = Make.Emissive(Color.black, new Color(0.55f, 0.05f, 0.04f));
            _cliff = Make.Lit(new Color(0.06f, 0.07f, 0.11f), 0.2f);

            // Night sky, moon, stars, mountains.
            Shape(PrimitiveType.Cube, "Sky", Root, new Vector3(0, 200f, 1400f), new Vector3(6000f, 3000f, 2f), Make.Emissive(Color.black, new Color(0.03f, 0.045f, 0.1f)));
            GameObject moon = Make.MeshObject("Moon", Root, Make.Disc(60f, 48), Make.Emissive(Color.white, new Color(2.2f, 2.3f, 2.5f)));
            moon.transform.localPosition = new Vector3(330f, 560f, 1380f);
            moon.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            GameObject halo = Make.MeshObject("Halo", Root, Make.Ring(60f, 120f, 48), Make.Glow(new Color(0.6f, 0.7f, 1f, 0.12f)));
            halo.transform.localPosition = new Vector3(330f, 560f, 1379f);
            halo.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            var r = new System.Random(5);
            for (int i = 0; i < 50; i++)
            {
                GameObject star = Shape(PrimitiveType.Sphere, "Star", Root, new Vector3((float)(r.NextDouble() * 2400 - 1200), (float)(250 + r.NextDouble() * 700), 1370f), Vector3.one * (2f + (float)r.NextDouble() * 3f), Make.Emissive(Color.white, new Color(1.4f, 1.5f, 1.8f)));
                star.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            Material rock = Make.Emissive(new Color(0.12f, 0.15f, 0.25f), new Color(0.07f, 0.09f, 0.17f), 0.2f);
            Material rock2 = Make.Emissive(new Color(0.08f, 0.1f, 0.18f), new Color(0.045f, 0.06f, 0.12f), 0.2f);
            Material snow = Make.Emissive(new Color(0.9f, 0.93f, 1f), new Color(0.75f, 0.8f, 0.95f), 0.4f);
            float[][] peaks = { new[] { -700f, 380f, 1250f }, new[] { -380f, 520f, 1200f }, new[] { -60f, 430f, 1230f }, new[] { 250f, 480f, 1200f }, new[] { 560f, 400f, 1240f }, new[] { 860f, 470f, 1220f }, new[] { -560f, 260f, 1050f }, new[] { 120f, 280f, 1000f }, new[] { 700f, 250f, 1020f } };
            for (int i = 0; i < peaks.Length; i++)
            {
                float x = peaks[i][0], top = peaks[i][1], z = peaks[i][2], s = top * 1.15f;
                GameObject m = Shape(PrimitiveType.Cube, "Peak", Root, new Vector3(x, top - s * 0.7071f, z), new Vector3(s, s, 20f), i >= 6 ? rock2 : rock);
                m.transform.localRotation = Quaternion.Euler(0, 0, 45f);
                if (i < 6)
                {
                    float c = s * 0.22f;
                    GameObject cap = Shape(PrimitiveType.Cube, "Snow", Root, new Vector3(x, top - c * 0.7071f, z - 11f), new Vector3(c, c, 2f), snow);
                    cap.transform.localRotation = Quaternion.Euler(0, 0, 45f);
                }
            }

            // The land the far range stands on, hiding the mountains' feet.
            Shape(PrimitiveType.Cube, "Range", Root, new Vector3(0, -240f, 1100f), new Vector3(6000f, 600f, 600f), Make.Emissive(new Color(0.04f, 0.05f, 0.09f), new Color(0.02f, 0.025f, 0.05f), 0.2f));

            // Far below: the chasm on the low side, dark rock on the high. The ridge holds the slab up.
            Shape(PrimitiveType.Cube, "Below", Root, new Vector3(0, -430f, 300f), new Vector3(4000f, 2f, 2400f), _cliff);
            _chasmL = Shape(PrimitiveType.Cube, "Chasm L", Root, new Vector3(-240f, -420f, -40f), new Vector3(150f, 2f, 700f), _chasm).GetComponent<Renderer>();
            _chasmR = Shape(PrimitiveType.Cube, "Chasm R", Root, new Vector3(240f, -420f, -40f), new Vector3(150f, 2f, 700f), _cliff).GetComponent<Renderer>();
            GameObject ridge = Shape(PrimitiveType.Cube, "Ridge", Root, new Vector3(0, -260f, 0), new Vector3(300f, 300f, 700f), _cliff);
            ridge.transform.localRotation = Quaternion.Euler(0, 0, 45f);
            _chasmLight = World.NewLight(Root, "Chasm", LightType.Point, new Color(1f, 0.2f, 0.15f), 400000f);
            _chasmLight.range = 700f;
            _chasmLight.shadows = LightShadows.None;

            // The slab: dark stone with a green top, mowing stripes, the cup.
            _slab = new GameObject("Slab").transform;
            _slab.SetParent(Root, false);
            Shape(PrimitiveType.Cube, "Stone", _slab, new Vector3(0, -16f, -50f), new Vector3(HalfW * 2f, 32f, 390f), Make.Lit(new Color(0.11f, 0.12f, 0.15f), 0.3f));
            Material turf = Make.Lit(new Color(0.24f, 0.56f, 0.2f), 0.35f), turf2 = Make.Lit(new Color(0.28f, 0.62f, 0.23f), 0.35f);
            for (int i = 0; i < 6; i++)
                Shape(PrimitiveType.Cube, "Stripe", _slab, new Vector3(0, 0.25f, -245f + i * 65f + 32.5f), new Vector3(HalfW * 2f, 0.5f, 65f), i % 2 == 0 ? turf : turf2);
            GameObject hole = Make.MeshObject("Hole", _slab, Make.Disc(CupR, 48), Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f));
            hole.transform.localPosition = new Vector3(0, 0.6f, 0);
            _highArc = Make.MeshObject("High half", _slab, new Mesh(), _ice);
            _lowArc = Make.MeshObject("Low half", _slab, new Mesh(), _red);
            _highArc.transform.localPosition = _lowArc.transform.localPosition = new Vector3(0, 0.7f, 0);
            _highArc.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            _lowArc.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            for (int i = 0; i < 4; i++)
            {
                TextMesh c = Words(_slab, ">", new Vector3(i % 2 == 0 ? -60f : 60f, 0.8f, i < 2 ? -110f : 70f), 12f, Color.white);
                c.color = new Color(1f, 1f, 1f, 0.45f);
                _chevrons.Add(c.transform);
            }
            _high = Words(_slab, "HIGH", Vector3.zero, 12f, new Color(0.65f, 0.92f, 1f));
            _low = Words(_slab, "LOW SIDE  -3", Vector3.zero, 10f, new Color(1f, 0.45f, 0.42f));

            _tee = Shape(PrimitiveType.Cube, "Tee", _slab, new Vector3(0, 1.5f, -Reach), new Vector3(20f, 3f, 10f), Make.Lit(new Color(0.85f, 0.88f, 0.9f), 0.5f)).transform;
            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", _slab, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.6f, 0f, bc, bn)).transform;
            _ball.gameObject.SetActive(false);
            _trail = Stroke(_slab, "Trail", new Color(0.7f, 0.9f, 1f, 0.6f), 2.2f);

            Sun(Root, new Vector3(40f, -30f, 0f), new Color(0.75f, 0.82f, 1f), 0.9f);
            Lamp(Root, new Vector3(-100f, 420f, -200f), new Vector3(0, 0, 0), new Color(0.85f, 0.9f, 1f), 260000f, 45f, 900f);
        }

        void Lean(int tee, bool snap)
        {
            _side = tee == 1 ? -1 : 1;
            _tiltTo = Tilt * _side;
            if (snap) _tilt = _tiltTo;
            // The high half of the cup is the side the tee is on, past 18 degrees round the front.
            float edge = 18f * -_side;
            _highArc.GetComponent<MeshFilter>().sharedMesh = _side < 0 ? Sector(CupR, CupR + 2.4f, -180f, edge) : Sector(CupR, CupR + 2.4f, edge, 180f);
            _lowArc.GetComponent<MeshFilter>().sharedMesh = _side < 0 ? Sector(CupR, CupR + 2.4f, edge, 180f) : Sector(CupR, CupR + 2.4f, -180f, edge);
            _high.transform.localPosition = new Vector3(_side * 34f, 14f, 22f);
            _low.transform.localPosition = new Vector3(-_side * (HalfW - 10f), 10f, -150f);
            _chasmL.sharedMaterial = _side > 0 ? _chasm : _cliff;
            _chasmR.sharedMaterial = _side > 0 ? _cliff : _chasm;
            _chasmLight.transform.localPosition = new Vector3(-_side * 260f, -160f, -40f);
            foreach (Transform c in _chevrons) c.localRotation = Quaternion.Euler(90f, _side > 0 ? 180f : 0f, 0f);
            _tee.localPosition = new Vector3(_side * 80f, 1.5f, -Reach);
        }

        public override void Begin(ShotView start) { Lean(start.NextTee, true); Pose(); }
        public override void Ready(ShotView v) { Lean(v.NextTee, false); }

        void Pose()
        {
            // Lean the slab about its long axis: a positive z rotation raises +x.
            _slab.localRotation = Quaternion.Euler(0, 0, _tilt);
        }

        public override void Tick()
        {
            _tilt = Mathf.Lerp(_tilt, _tiltTo, Time.deltaTime * 3f);
            Pose();
            Vector3 cam = CameraFrom;
            _high.transform.rotation = Quaternion.LookRotation(_high.transform.position - cam);
            _low.transform.rotation = Quaternion.LookRotation(_low.transform.position - cam);
            _chasmLight.intensity = 400000f * (0.85f + Mathf.Sin(Time.time * 2.3f) * 0.15f);
        }

        public override IEnumerator Play(ShotView v)
        {
            int side = v.Tee == 1 ? -1 : 1;
            Vector3 start = new Vector3(side * 80f, 0, -Reach);
            List<Vector3> pts;
            Vector3 rim = Vector3.zero;
            bool lowMiss = false;
            if (v.Made)
            {
                float deg = v.HasEntry ? v.EntryDeg : Mathf.Clamp(v.LineIn * 30f, -80f, 80f);
                rim = Rim(deg, CupR - BallR * 0.4f);
                Vector3 outward = Rim(Mathf.Clamp(deg, -100f, 100f), 1f);
                pts = Bezier(start, Vector3.Lerp(start, Vector3.zero, 0.45f) + Vector3.right * side * 50f, outward * 70f, rim);
            }
            else
            {
                string ms = v.MissSide;
                int missSign = ms == "LEFT" ? -1 : ms == "RIGHT" ? 1 : (v.LineIn < 0 ? -1 : 1);
                lowMiss = (ms == "LEFT" || ms == "RIGHT") && missSign != side;
                float endX = missSign * Mathf.Clamp(Mathf.Abs(v.LineIn) * K + CupR + BallR + 2f, CupR + BallR + 2f, 80f);
                float endZ = v.HasRollout ? Mathf.Clamp(v.RolloutIn * K, -100f, 110f) : 50f;
                pts = Track(v, K, Reach, start, new Vector3(endX, 0, endZ));
                // Make sure the end is on the side it missed.
                Vector3 last = pts[pts.Count - 1];
                if (Mathf.Sign(last.x) != missSign || Mathf.Abs(last.x) < CupR + BallR) pts = Bezier(start, Vector3.Lerp(start, new Vector3(endX, 0, endZ), 0.4f), Vector3.Lerp(start, new Vector3(endX, 0, endZ), 0.8f), new Vector3(endX, 0, endZ));
            }
            Lift(pts, BallR + 0.5f);
            _ball.gameObject.SetActive(true);
            _ball.localPosition = pts[0];
            yield return Roll(_ball, pts, 1.5f, _trail, BallR);

            if (v.Made)
            {
                bool low = v.Title == "LOW SIDE";
                Sound("highside_make", "cup_plop", 0.7f);   // puttr-app-2: the game's own sound, the shared one until recorded
                yield return Drop(_ball, rim, BallR);
                Renderer arc = (low ? _lowArc : _highArc).GetComponent<Renderer>();
                arc.sharedMaterial = low ? _redHot : _iceHot;
                Arcade.Sparks(_slab.TransformPoint(rim + Vector3.up * 4f), low ? 12 : 30);
                if (!low) Arcade.Celebrate(18, _slab.TransformPoint(Vector3.up * 8f));
                yield return new WaitForSeconds(0.7f);
                arc.sharedMaterial = low ? _red : _ice;
            }
            else if (lowMiss)
            {
                // Down the slope, off the low edge, into the chasm.
                Vector3 from = _ball.localPosition;
                Vector3 edge = new Vector3(-side * (HalfW + BallR), from.y, from.z + 30f);
                var down = new List<Vector3> { from, Vector3.Lerp(from, edge, 0.5f) + Vector3.forward * 8f, edge };
                yield return Roll(_ball, down, 0.8f, null, BallR);
                Vector3 vel = new Vector3(-side * 120f, 30f, 40f);
                Vector3 p = _ball.localPosition;
                for (float t = 0f; t < 1.1f; t += Time.deltaTime)
                {
                    vel += Vector3.down * 900f * Time.deltaTime;
                    p += vel * Time.deltaTime;
                    _ball.localPosition = p;
                    yield return null;
                }
                Arcade.Shake(0.25f);
                Renderer arc = _lowArc.GetComponent<Renderer>();
                arc.sharedMaterial = _redHot;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f) { _chasmLight.intensity = Mathf.Lerp(2000000f, 400000f, t); yield return null; }
                arc.sharedMaterial = _red;
            }
            else
            {
                Renderer arc = _highArc.GetComponent<Renderer>();
                arc.sharedMaterial = _iceHot;
                yield return new WaitForSeconds(0.7f);
                arc.sharedMaterial = _ice;
            }
            yield return new WaitForSeconds(0.2f);
            _ball.gameObject.SetActive(false);
            _trail.positionCount = 0;
        }

        // ── Shapes and rolling ───────────────────────────────────────────────

        static Vector3 Rim(float deg, float r) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a) * r, 0f, -Mathf.Cos(a) * r); }

        // A flat ring sector facing up, from fromDeg to toDeg (0 = front, toward the golfer; + round to the right).
        static Mesh Sector(float inner, float outer, float fromDeg, float toDeg, int seg = 48)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                Vector3 d = Rim(Mathf.Lerp(fromDeg, toDeg, i / (float)seg), 1f);
                verts.Add(d * inner); verts.Add(d * outer);
            }
            for (int i = 0; i < seg; i++)
            {
                int b = i * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 2); tris.Add(b + 3); tris.Add(b + 1);
            }
            var mesh = new Mesh { name = "Sector" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

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
            foreach (Vector2 p in v.Path) if (p.y * k >= -reach) pts.Add(new Vector3(Mathf.Clamp(p.x * k, -HalfW + 8f, HalfW - 8f), 0f, Mathf.Min(p.y * k, 130f)));
            if (pts.Count < 4) return Bezier(start, Vector3.Lerp(start, end, 0.4f), Vector3.Lerp(start, end, 0.75f), end);
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
