// Gears as a steel gearbox standing beside a lane. Six gears mesh on a riveted plate: the ones already reached are
// brushed metal and turn, the gear being driven for is molten orange, the rest wait dark. The pace band for that gear
// is painted on the lane past the cup. The putt rolls out and stops where its pace says (a make drops, and a marker
// shows where it would have stopped). In the band: the gear cools to metal, spins up, and the next one melts. Out of
// it: the gearbox stalls and drops a gear.
// Units: inches x K on the lane; the cup is at the origin, the golfer down -Z, the gearbox up +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class GearsScene : RuleScene
    {
        const float K = 6f, CupR = 2.125f * K, BallR = 0.84f * K, Reach = 230f, LaneW = 64f, PlateZ = 250f;
        static readonly Vector3 BoxAt = new Vector3(LaneW + 100f, 0f, 20f);
        static readonly Vector2[] Spots = { new Vector2(-72f, 46f), new Vector2(0f, 46f), new Vector2(72f, 46f), new Vector2(108f, 110f), new Vector2(36f, 110f), new Vector2(-36f, 110f) };
        const int Count = 6;

        sealed class Gear
        {
            public Transform Spin;
            public readonly List<Renderer> Parts = new List<Renderer>();
            public TextMesh Number;
            public float Dir, Speed;
            public Vector3 Home;
        }

        readonly List<Gear> _gears = new List<Gear>();
        Material _brushed, _molten, _dark, _hot;
        Transform _band, _ball, _marker;
        Renderer _bandFill;
        Transform _bandFar;
        Material _bandMat, _bandGood, _bandBad;
        TextMesh _bandTag, _bandRange;
        LineRenderer _trail;
        Light _forge;
        Transform _box;
        int _gear = 1;
        float _lo = -2f, _hi = 4f, _bandLo = -2f, _bandHi = 4f, _shake;

        static bool Portrait => Screen.height > Screen.width;
        // Upright the camera sits closer than it did, on the cup and the pace band past it: the band is the whole game
        // and it was a thin stripe a long way off (Matt, 2026-11-23: the wanted speed has to be far clearer).
        public override Vector3 CameraFrom => At(Portrait ? 14f : 40f, Portrait ? 235f : 240f, Portrait ? -215f : -270f);
        public override Vector3 CameraLook => At(Portrait ? 34f : 40f, Portrait ? 30f : 30f, Portrait ? 52f : 60f);
        public override float CameraFov => Portrait ? 54f : 44f;

        public override void Build()
        {
            _brushed = Make.Lit(new Color(0.78f, 0.8f, 0.84f), 0.55f, 0.75f);
            _dark = Make.Lit(new Color(0.16f, 0.17f, 0.19f), 0.45f, 0.6f);
            _molten = Make.Emissive(new Color(1f, 0.55f, 0.15f), new Color(3.2f, 1.2f, 0.18f), 0.6f);
            _hot = Make.Emissive(new Color(1f, 0.8f, 0.5f), new Color(6f, 3.4f, 1f), 0.6f);
            Material steel = Make.Lit(new Color(0.2f, 0.21f, 0.23f), 0.5f, 0.5f);
            Material plate = Make.Lit(new Color(0.12f, 0.12f, 0.13f), 0.45f, 0.4f);
            Material rivet = Make.Lit(new Color(0.45f, 0.46f, 0.5f), 0.7f, 0.8f);

            // The workshop: a dark concrete floor, a back wall.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -3f, 200f), new Vector3(2400f, 2f, 1600f), Make.Lit(new Color(0.07f, 0.07f, 0.075f), 0.3f));
            Shape(PrimitiveType.Cube, "Wall", Root, new Vector3(0, 300f, PlateZ + 200f), new Vector3(2400f, 800f, 4f), Make.Lit(new Color(0.06f, 0.055f, 0.05f), 0.2f));

            // The lane: green with stripes, steel rails, the cup.
            Material turf = Make.Lit(new Color(0.2f, 0.52f, 0.2f), 0.35f), turf2 = Make.Lit(new Color(0.24f, 0.58f, 0.23f), 0.35f);
            for (int i = 0; i < 9; i++)
                Shape(PrimitiveType.Cube, "Lane", Root, new Vector3(0, -1f, -Reach - 20f + i * 50f + 25f), new Vector3(LaneW * 2f, 2f, 50f), i % 2 == 0 ? turf : turf2);
            Shape(PrimitiveType.Cube, "Rail", Root, new Vector3(-LaneW - 4f, 2f, -40f), new Vector3(8f, 8f, 460f), steel);
            Shape(PrimitiveType.Cube, "Rail", Root, new Vector3(LaneW + 4f, 2f, -40f), new Vector3(8f, 8f, 460f), steel);
            GameObject hole = Make.MeshObject("Hole", Root, Make.Disc(CupR, 48), Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f));
            hole.transform.localPosition = new Vector3(0, 0.78f, 0);   // over the pace band, which runs across the cup in the low gears
            Hoop("Lip", Root, new Vector3(0, 0.82f, 0), CupR, CupR + 1.2f, Make.Lit(Color.white, 0.5f));
            // Inches past the cup, on the right rail.
            for (int i = 0; i <= 5; i++)
            {
                float z = i * 4f * K;
                Shape(PrimitiveType.Cube, "Tick", Root, new Vector3(LaneW + 4f, 6.2f, z), new Vector3(8f, 0.4f, 1.2f), Make.Emissive(Color.white, new Color(0.8f, 0.8f, 0.8f)));
                TextMesh label = Words(Root, i == 0 ? "0" : "+" + (i * 4), new Vector3(LaneW + 18f, 7f, z), 6f, new Color(0.85f, 0.85f, 0.85f));
                label.transform.localRotation = Quaternion.Euler(60f, 0, 0);
            }

            // The pace band.
            _bandMat = Make.Glow(new Color(1f, 0.6f, 0.12f, 0.8f), false);
            _bandGood = Make.Glow(new Color(1f, 0.85f, 0.4f, 0.75f));
            _bandBad = Make.Glow(new Color(1f, 0.15f, 0.1f, 0.6f));
            _band = new GameObject("Band").transform;
            _band.SetParent(Root, false);
            _bandFill = Shape(PrimitiveType.Cube, "Fill", _band, new Vector3(0, 0.5f, 0.5f), new Vector3(LaneW * 2f, 0.4f, 1f), _bandMat).GetComponent<Renderer>();
            _bandFill.shadowCastingMode = ShadowCastingMode.Off;
            Material edge = Make.Emissive(new Color(1f, 0.6f, 0.15f), new Color(3f, 1.3f, 0.25f));
            Shape(PrimitiveType.Cube, "Near", _band, new Vector3(0, 0.6f, 0), new Vector3(LaneW * 2f, 0.5f, 1.4f), edge).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            _bandFar = Shape(PrimitiveType.Cube, "Far", _band, new Vector3(0, 0.6f, 1f), new Vector3(LaneW * 2f, 0.5f, 1.4f), edge).transform;
            _bandFar.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            // The band says what it is, in words on the lane: STOP HERE to the left of the ball's line, the inches past
            // the cup to the right.
            // (On the lane just past the band, never on it: in the low gears the cup sits in the band.)
            _bandTag = Words(_band, "STOP HERE", Vector3.zero, 9.5f, Color.white);
            _bandTag.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            _bandRange = Words(_band, "", Vector3.zero, 7.5f, new Color(1f, 0.85f, 0.5f));
            _bandRange.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            // Drawn after the band's glow, or the glow washes the words out.
            foreach (TextMesh t in new[] { _bandTag, _bandRange }) { MeshRenderer m = t.GetComponent<MeshRenderer>(); m.sharedMaterial = new Material(m.sharedMaterial) { renderQueue = 3600 }; }
            // Arrows down both rails, pointing at the band from the Player's side.
            Material arrow = Make.Emissive(new Color(1f, 0.6f, 0.15f), new Color(3f, 1.3f, 0.25f));
            foreach (int sd in new[] { -1, 1 })
                for (int k = 0; k < 3; k++)
                {
                    GameObject a = Make.MeshObject("Arrow", _band, Chevron(7f), arrow);
                    a.transform.localPosition = new Vector3(sd * (LaneW - 9f), 0.7f, -10f - k * 11f);
                    a.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }

            // The gearbox: a riveted plate standing at the end of the lane, six gears on it in a zigzag.
            _box = new GameObject("Gearbox").transform;
            _box.SetParent(Root, false);
            _box.localPosition = BoxAt;
            _box.localRotation = Quaternion.Euler(0, 25f, 0);
            Shape(PrimitiveType.Cube, "Plate", _box, new Vector3(18f, 80f, 8f), new Vector3(260f, 160f, 10f), plate);
            Shape(PrimitiveType.Cube, "Lintel", _box, new Vector3(18f, 163f, 4f), new Vector3(276f, 10f, 22f), steel);
            Shape(PrimitiveType.Cube, "Base", _box, new Vector3(18f, 4f, 4f), new Vector3(276f, 8f, 22f), steel);
            foreach (float x in new[] { -104f, 140f })
                foreach (float y in new[] { 14f, 148f })
                    Shape(PrimitiveType.Sphere, "Rivet", _box, new Vector3(x, y, 2f), Vector3.one * 6f, rivet);
            Mesh body = Body(29f, 8f, 10f);
            for (int i = 0; i < Count; i++)
            {
                var g = new Gear { Dir = i % 2 == 0 ? 1f : -1f };
                Vector3 at = new Vector3(Spots[i].x, Spots[i].y, -6f);
                g.Home = at;
                Transform axle = new GameObject("Gear " + (i + 1)).transform;
                axle.SetParent(_box, false);
                axle.localPosition = at;
                g.Spin = new GameObject("Spin").transform;
                g.Spin.SetParent(axle, false);
                g.Spin.localRotation = Quaternion.Euler(0, 0, i * 11f);
                GameObject disc = Make.MeshObject("Body", g.Spin, body, _dark);
                disc.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
                g.Parts.Add(disc.GetComponent<Renderer>());
                for (int t = 0; t < 12; t++)
                {
                    float a = t / 12f * 360f;
                    GameObject tooth = Shape(PrimitiveType.Cube, "Tooth", g.Spin, Quaternion.Euler(0, 0, a) * new Vector3(0, 31.5f, 0) + new Vector3(0, 0, -4f), new Vector3(8f, 8f, 8f), _dark);
                    tooth.transform.localRotation = Quaternion.Euler(0, 0, a);
                    g.Parts.Add(tooth.GetComponent<Renderer>());
                }
                Shape(PrimitiveType.Cylinder, "Hub", axle, new Vector3(0, 0, -10f), new Vector3(13f, 2f, 13f), steel).transform.localRotation = Quaternion.Euler(90f, 0, 0);
                g.Number = Words(axle, (i + 1).ToString(), new Vector3(0, -17f, -6.8f), 8f, new Color(0.7f, 0.7f, 0.72f));
                _gears.Add(g);
            }
            _forge = World.NewLight(Root, "Forge", LightType.Point, new Color(1f, 0.5f, 0.15f), 80000f);
            _forge.range = 220f;
            _forge.shadows = LightShadows.None;

            _marker = new GameObject("Marker").transform;
            _marker.SetParent(Root, false);
            Hoop("Ring", _marker, new Vector3(0, 0.8f, 0), BallR * 1.1f, BallR * 1.6f, Make.Emissive(Color.white, new Color(2f, 2f, 2f))).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            _marker.gameObject.SetActive(false);
            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.6f, 0f, bc, bn)).transform;
            _ball.gameObject.SetActive(false);
            _trail = Stroke(Root, "Trail", new Color(1f, 0.85f, 0.5f, 0.6f), 2.4f);

            Sun(Root, new Vector3(55f, 20f, 0f), new Color(1f, 0.92f, 0.82f), 0.8f);
            Lamp(Root, new Vector3(0, 420f, -120f), new Vector3(0, 0, 40f), new Color(1f, 0.95f, 0.88f), 600000f, 45f, 900f);
            Lamp(Root, new Vector3(-150f, 380f, -200f), BoxAt + new Vector3(0, 80f, 0), new Color(0.85f, 0.9f, 1f), 500000f, 45f, 900f);
        }

        // A gear's body: a thick disc with a bevelled rim and a dished face, spun round Y.
        static Mesh Body(float r, float thick, float hub)
        {
            var p = new List<Vector2>
            {
                new Vector2(0.01f, 0f), new Vector2(r - 2f, 0f), new Vector2(r - 2f, 0f), new Vector2(r, 1.5f), new Vector2(r, 1.5f),
                new Vector2(r, thick - 1.5f), new Vector2(r, thick - 1.5f), new Vector2(r - 2f, thick), new Vector2(r - 2f, thick),
                new Vector2(r * 0.72f, thick), new Vector2(r * 0.65f, thick - 2.5f), new Vector2(hub, thick - 2.5f), new Vector2(hub, thick), new Vector2(0.01f, thick),
            };
            return Make.Lathe(p, 48);
        }

        void Paint(int gear, bool won)
        {
            for (int i = 0; i < _gears.Count; i++)
            {
                int n = i + 1;
                Material m = n < gear || (won && n <= gear) ? _brushed : n == gear ? _molten : _dark;
                foreach (Renderer r in _gears[i].Parts) r.sharedMaterial = m;
                _gears[i].Speed = n < gear || (won && n <= gear) ? 40f : n == gear ? 12f : 0f;
                _gears[i].Number.color = n == gear && !won ? new Color(1f, 0.9f, 0.6f) : n < gear ? new Color(0.15f, 0.15f, 0.17f) : new Color(0.55f, 0.55f, 0.58f);
            }
            int at = Mathf.Clamp(gear, 1, Count) - 1;
            _forge.transform.position = _gears[at].Spin.parent.TransformPoint(new Vector3(0, 0, -45f));
            _forge.enabled = !won;
        }

        void Set(ShotView v)
        {
            _gear = Mathf.Max(1, v.Int("gear", 1));
            _lo = v.Num("lo", -2f);
            _hi = v.Num("hi", 4f);
            Paint(_gear, v.Int("won", -1) >= 0);
            _bandTag.text = v.Over ? "" : "STOP HERE";
            _bandRange.text = v.Over ? "" : Inches(_lo) + " TO " + Inches(_hi) + " IN PAST THE HOLE";
        }

        public override void Begin(ShotView start) { Set(start); _bandLo = _lo; _bandHi = _hi; Band(); }
        public override void Ready(ShotView v) { Set(v); }

        static string Inches(float v) => (v > 0f ? "+" : "") + Mathf.RoundToInt(v);

        // A flat arrowhead pointing +Z (up the lane).
        static Mesh Chevron(float size)
        {
            var mesh = new Mesh { name = "Chevron" };
            mesh.SetVertices(new List<Vector3> { new Vector3(0, 0, size), new Vector3(size, 0, 0), new Vector3(size * 0.45f, 0, 0), new Vector3(0, 0, size * 0.55f), new Vector3(-size * 0.45f, 0, 0), new Vector3(-size, 0, 0) });
            mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void Band()
        {
            _band.localPosition = new Vector3(0, 0, _bandLo * K);
            float len = Mathf.Max(1f, (_bandHi - _bandLo) * K);
            _bandFill.transform.localPosition = new Vector3(0, 0.5f, len / 2f);
            _bandFill.transform.localScale = new Vector3(LaneW * 2f, 0.4f, len);
            _bandFar.localPosition = new Vector3(0, 0.6f, len);
            _bandTag.transform.localPosition = new Vector3(0f, 1.2f, len + 22f);
            _bandRange.transform.localPosition = new Vector3(0f, 1.2f, len + 9f);
        }

        public override void Tick()
        {
            _bandLo = Mathf.Lerp(_bandLo, _lo, Time.deltaTime * 4f);
            _bandHi = Mathf.Lerp(_bandHi, _hi, Time.deltaTime * 4f);
            Band();
            foreach (Gear g in _gears) g.Spin.Rotate(0, 0, g.Dir * g.Speed * Time.deltaTime, Space.Self);
            _forge.intensity = 80000f * (0.85f + Mathf.Sin(Time.time * 5f) * 0.1f + Mathf.Sin(Time.time * 13f) * 0.05f);
            if (_shake > 0f)
            {
                _shake -= Time.deltaTime;
                foreach (Gear g in _gears) g.Spin.parent.localPosition = g.Home + new Vector3(Mathf.Sin(Time.time * 70f) * _shake * 4f, 0, 0);
            }
        }

        public override IEnumerator Play(ShotView v)
        {
            int was = _gear;
            Vector3 start = new Vector3(v.Tee == 1 ? -36f : v.Tee == 3 ? 36f : 0f, 0, -Reach);
            float stopZ = v.HasRollout ? Mathf.Clamp(v.RolloutIn * K, -Reach + 40f, 200f) : (v.Reached ? 40f : -60f);
            float stopX = Mathf.Clamp(v.LineIn * K * 0.5f, -LaneW + 10f, LaneW - 10f);
            Vector3 stop = new Vector3(stopX, 0, stopZ);
            _marker.gameObject.SetActive(false);
            List<Vector3> pts;
            if (v.Made)
            {
                Vector3 rim = new Vector3(0, 0, -CupR * 0.3f);
                pts = Bezier(start, Vector3.Lerp(start, rim, 0.35f), Vector3.Lerp(start, rim, 0.7f), rim);
                Lift(pts, BallR);
                _ball.gameObject.SetActive(true);
                yield return Roll(_ball, pts, 1.2f, _trail, BallR);
                Sound("gears_make", "cup_plop", 0.6f); Sound("ratchet", 0.5f);   // puttr-app-2: the game's own sound, the shared one until recorded
                yield return Drop(_ball, rim, BallR);
                // Where the pace would have taken it.
                _marker.localPosition = stop;
                _marker.gameObject.SetActive(true);
            }
            else
            {
                pts = Bezier(start, Vector3.Lerp(start, stop, 0.35f), Vector3.Lerp(start, stop, 0.7f), stop);
                Lift(pts, BallR);
                _ball.gameObject.SetActive(true);
                yield return Roll(_ball, pts, 1.4f, _trail, BallR);
            }

            Gear g = _gears[Mathf.Clamp(was, 1, Count) - 1];
            if (v.Good)
            {
                _bandFill.sharedMaterial = _bandGood;
                foreach (Renderer r in g.Parts) r.sharedMaterial = _hot;
                Arcade.Sparks(g.Spin.parent.TransformPoint(new Vector3(0, 0, -20f)), 40);
                if (v.Title == "TOP GEAR!") Arcade.Celebrate(40, _box.TransformPoint(new Vector3(18f, 90f, -40f)));
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
                {
                    g.Spin.Rotate(0, 0, g.Dir * 600f * Time.deltaTime * (1f - t), Space.Self);
                    yield return null;
                }
                int next = Mathf.Min(was + 1, Count);
                Paint(next, v.Title == "TOP GEAR!");
            }
            else
            {
                _bandFill.sharedMaterial = _bandBad;
                _shake = 0.4f;
                Arcade.Shake(0.15f);
                foreach (Gear x in _gears) x.Speed = 0f;
                yield return new WaitForSeconds(0.5f);
                Paint(Mathf.Max(1, was - 1), false);
            }
            yield return new WaitForSeconds(0.4f);
            _bandFill.sharedMaterial = _bandMat;
            foreach (Gear x in _gears) x.Spin.parent.localPosition = x.Home;
            _ball.gameObject.SetActive(false);
            _marker.gameObject.SetActive(false);
            _trail.positionCount = 0;
        }

        // ── Rolling ──────────────────────────────────────────────────────────

        static List<Vector3> Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int n = 40)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= n; i++) { float t = i / (float)n, u = 1f - t; pts.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d); }
            return pts;
        }

        static void Lift(List<Vector3> pts, float y) { for (int i = 0; i < pts.Count; i++) pts[i] = new Vector3(pts[i].x, y, pts[i].z); }

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
