// Doors as a jade temple: stone archways ring the front of the cup, one per door (3, 5 or 7). The called door glows
// jade with a shaft of light over it. The putt rolls up to the cup and drops in through the door it entered by: the
// called door blazes, a wrong door flashes amber, a back-door make sneaks round behind, a miss rolls on by.
// Units: the cup is drawn K times life size (inches x K); the cup is at the origin, the golfer is down -Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class DoorsScene : RuleScene
    {
        const float K = 5f, CupR = 2.125f * K, BallR = 0.84f * K, DoorR = 80f, Reach = 240f;

        sealed class Door
        {
            public Transform Root;
            public readonly List<Renderer> Stone = new List<Renderer>();
            public GameObject Beam, Portal;
            public Light Light;
            public TextMesh Numeral;
            public float Deg;
        }

        readonly List<Door> _doors = new List<Door>();
        float[] _bounds = new float[0];
        int _called = -1, _count;
        Transform _doorRoot, _ball, _tee;
        LineRenderer _trail;
        Material _stone, _jade, _amber, _numeralOff;
        float _teeX, _teeTo;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(Portrait ? 30f : 50f, Portrait ? 250f : 200f, Portrait ? -235f : -230f);
        public override Vector3 CameraLook => At(Portrait ? 0f : 0f, 10f, Portrait ? 60f : 40f);
        public override float CameraFov => Portrait ? 54f : 42f;

        static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII" };

        public override void Build()
        {
            Material floor = Make.Lit(new Color(0.04f, 0.16f, 0.1f), 0.35f);
            Material dais = Make.Lit(new Color(0.09f, 0.3f, 0.19f), 0.55f);
            Material rimStone = Make.Lit(new Color(0.45f, 0.5f, 0.47f), 0.3f);
            Material wall = Make.Lit(new Color(0.035f, 0.08f, 0.075f), 0.2f);
            Material hall = Make.Emissive(Color.black, new Color(0.05f, 0.2f, 0.17f));
            _stone = Make.Lit(new Color(0.38f, 0.41f, 0.4f), 0.25f);
            _jade = Make.Emissive(new Color(0.3f, 1f, 0.75f), new Color(0.15f, 1.2f, 0.75f), 0.6f);
            _amber = Make.Emissive(new Color(1f, 0.7f, 0.25f), new Color(1.6f, 0.85f, 0.18f), 0.6f);

            // Sky, and the temple standing in it.
            Shape(PrimitiveType.Cube, "Sky", Root, new Vector3(0, 300f, 900f), new Vector3(4000f, 1400f, 2f), Make.Emissive(Color.black, new Color(0.012f, 0.05f, 0.045f)));
            for (int i = 0; i < 40; i++)
            {
                var r = new System.Random(i * 31 + 7);
                GameObject star = Shape(PrimitiveType.Sphere, "Star", Root, new Vector3((float)(r.NextDouble() * 1400 - 700), (float)(260 + r.NextDouble() * 500), 880f), Vector3.one * (1.5f + (float)r.NextDouble() * 2.5f), Make.Emissive(Color.white, new Color(1.2f, 1.4f, 1.3f)));
                star.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            Shape(PrimitiveType.Cube, "Temple", Root, new Vector3(0, 110f, 380f), new Vector3(360f, 220f, 30f), wall);
            GameObject roof = Shape(PrimitiveType.Cube, "Roof", Root, new Vector3(0, 220f, 380f), new Vector3(255f, 255f, 28f), wall);
            roof.transform.localRotation = Quaternion.Euler(0, 0, 45f);
            roof.transform.localScale = new Vector3(255f, 255f, 28f);
            Shape(PrimitiveType.Cube, "Tower L", Root, new Vector3(-260f, 130f, 420f), new Vector3(70f, 260f, 30f), wall);
            Shape(PrimitiveType.Cube, "Tower R", Root, new Vector3(260f, 130f, 420f), new Vector3(70f, 260f, 30f), wall);
            Shape(PrimitiveType.Cube, "Hall", Root, new Vector3(0, 70f, 364f), new Vector3(110f, 140f, 1f), hall);
            GameObject arch = Make.MeshObject("Hall arch", Root, Make.Disc(55f, 48), hall);
            arch.transform.localPosition = new Vector3(0, 140f, 363.5f);
            arch.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            GameObject shaft = Shape(PrimitiveType.Cube, "Shaft", Root, new Vector3(30f, 260f, 340f), new Vector3(90f, 600f, 1f), Make.Glow(new Color(0.3f, 1f, 0.8f, 0.07f)));
            shaft.transform.localRotation = Quaternion.Euler(0, 0, -12f);

            // The floor, the dais round the cup, faint rings of light on it.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -1f, 300f), new Vector3(3000f, 2f, 1600f), floor);
            GameObject d = Make.MeshObject("Dais", Root, Make.Disc(150f, 96), dais);
            d.transform.localPosition = new Vector3(0, 0.05f, 0);
            Hoop("Dais edge", Root, new Vector3(0, 0.1f, 0), 150f, 156f, rimStone);
            Material faint = Make.Glow(new Color(0.3f, 1f, 0.7f, 0.12f));
            Hoop("Ring", Root, new Vector3(0, 0.12f, 0), 96f, 97.2f, faint);
            Hoop("Ring", Root, new Vector3(0, 0.12f, 0), 200f, 201.5f, faint);
            for (int i = -6; i <= 6; i++)
            {
                LineRenderer l = Stroke(Root, "Grid", new Color(0.3f, 1f, 0.7f, 0.08f), 1f);
                Points(l, new[] { new Vector3(i * 60f, 0.1f, -320f), new Vector3(i * 60f, 0.1f, 500f) });
            }

            // Columns round the back of the dais, lanterns hung between them.
            Material lantern = Make.Emissive(new Color(0.4f, 1f, 0.75f), new Color(0.5f, 2.2f, 1.4f));
            for (int i = 0; i < 9; i++)
            {
                float deg = 100f + i * 20f;
                Vector3 at = Rim(deg, 190f);
                Shape(PrimitiveType.Cylinder, "Column", Root, at + Vector3.up * 80f, new Vector3(16f, 80f, 16f), rimStone);
                Shape(PrimitiveType.Cube, "Capital", Root, at + Vector3.up * 163f, new Vector3(26f, 8f, 26f), rimStone);
                Shape(PrimitiveType.Cube, "Plinth", Root, at + Vector3.up * 4f, new Vector3(24f, 8f, 24f), rimStone);
                if (i < 8)
                {
                    Vector3 mid = Rim(deg + 10f, 186f);
                    GameObject lamp = Shape(PrimitiveType.Sphere, "Lantern", Root, mid + Vector3.up * 120f, new Vector3(9f, 12f, 9f), lantern);
                    lamp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            // The cup.
            GameObject hole = Make.MeshObject("Hole", Root, Make.Disc(CupR, 48), Make.Lit(new Color(0.005f, 0.006f, 0.006f), 0.1f));
            hole.transform.localPosition = new Vector3(0, 0.2f, 0);
            Hoop("Lip", Root, new Vector3(0, 0.25f, 0), CupR, CupR + 1.4f, Make.Emissive(Color.white, new Color(0.6f, 0.7f, 0.65f)));

            _doorRoot = new GameObject("Doors").transform;
            _doorRoot.SetParent(Root, false);

            // The tee down the hall, and the ball.
            _tee = Shape(PrimitiveType.Cube, "Tee", Root, new Vector3(0, 2f, -Reach), new Vector3(26f, 4f, 14f), Make.Lit(new Color(0.25f, 0.28f, 0.27f), 0.4f)).transform;
            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.6f, 0f, bc, bn)).transform;
            _ball.gameObject.SetActive(false);
            _trail = Stroke(Root, "Trail", new Color(0.4f, 1f, 0.8f, 0.55f), 2.2f);

            Sun(Root, new Vector3(50f, 30f, 0f), new Color(0.75f, 1f, 0.92f), 0.55f);
            Lamp(Root, new Vector3(0, 380f, -160f), new Vector3(0, 0, 10f), new Color(0.85f, 1f, 0.95f), 380000f, 50f, 900f);
            Lamp(Root, new Vector3(-240f, 200f, -120f), new Vector3(0, 20f, 0), new Color(0.6f, 0.9f, 1f), 160000f, 40f, 700f);
        }

        void Lay(int count, float[] bounds)
        {
            if (count == _count && _doors.Count == count) return;
            _count = count;
            _bounds = bounds;
            foreach (Door old in _doors) Object.Destroy(old.Root.gameObject);
            _doors.Clear();
            float w = count <= 3 ? 36f : count <= 5 ? 28f : 20f, h = w * 0.6f + 10f, post = count <= 5 ? 5f : 4f;
            for (int i = 0; i < count; i++)
            {
                float lo = i == 0 ? -90f : bounds[i - 1], hi = i == count - 1 ? 90f : bounds[i];
                var door = new Door { Deg = (lo + hi) / 2f };
                door.Root = new GameObject("Door " + (i + 1)).transform;
                door.Root.SetParent(_doorRoot, false);
                Vector3 at = Rim(door.Deg, DoorR);
                door.Root.localPosition = at;
                door.Root.localRotation = Quaternion.LookRotation(-at.normalized);
                door.Stone.Add(Shape(PrimitiveType.Cube, "Post", door.Root, new Vector3(-w / 2f, h / 2f, 0), new Vector3(post, h, post * 1.6f), _stone).GetComponent<Renderer>());
                door.Stone.Add(Shape(PrimitiveType.Cube, "Post", door.Root, new Vector3(w / 2f, h / 2f, 0), new Vector3(post, h, post * 1.6f), _stone).GetComponent<Renderer>());
                for (int s = 0; s < 9; s++)
                {
                    float a = Mathf.Lerp(180f, 0f, (s + 0.5f) / 9f) * Mathf.Deg2Rad;
                    GameObject stone = Shape(PrimitiveType.Cube, "Voussoir", door.Root, new Vector3(Mathf.Cos(a) * w / 2f, h + Mathf.Sin(a) * w / 2f, 0), new Vector3(post, w * 0.2f, post * 1.6f), _stone);
                    stone.transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg);
                    door.Stone.Add(stone.GetComponent<Renderer>());
                }
                door.Stone.Add(Shape(PrimitiveType.Cube, "Step", door.Root, new Vector3(0, 0.6f, 0), new Vector3(w + post * 2f, 1.2f, post * 2.4f), _stone).GetComponent<Renderer>());
                door.Portal = Shape(PrimitiveType.Cube, "Portal", door.Root, new Vector3(0, h * 0.62f, 0), new Vector3(w - post, h * 1.2f + w * 0.3f, 0.4f), Make.Glow(new Color(0.3f, 1f, 0.75f, 0.14f)));
                door.Beam = Shape(PrimitiveType.Cylinder, "Beam", door.Root, new Vector3(0, 200f, 0), new Vector3(w * 0.9f, 200f, w * 0.9f), Make.Glow(new Color(0.3f, 1f, 0.75f, 0.06f)));
                door.Beam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                door.Portal.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                door.Light = World.NewLight(door.Root, "Door light", LightType.Point, new Color(0.35f, 1f, 0.75f), 5000f);
                door.Light.transform.localPosition = new Vector3(0, h + 10f, 6f);
                door.Light.range = 90f;
                door.Light.shadows = LightShadows.None;
                door.Numeral = Words(door.Root, Roman[Mathf.Min(i, Roman.Length - 1)], new Vector3(0, h + w / 2f + 9f, 0), count <= 5 ? 9f : 7f, Color.white);
                door.Numeral.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                _doors.Add(door);
            }
        }

        void Call(int called)
        {
            _called = called;
            for (int i = 0; i < _doors.Count; i++)
            {
                bool on = i == called;
                foreach (Renderer r in _doors[i].Stone) r.sharedMaterial = on ? _jade : _stone;
                _doors[i].Beam.SetActive(on);
                _doors[i].Portal.SetActive(on);
                _doors[i].Light.enabled = on;
                _doors[i].Numeral.color = on ? new Color(0.6f, 1f, 0.85f) : new Color(0.85f, 0.9f, 0.88f);
            }
        }

        int DoorFor(float deg) { int n = 0; foreach (float b in _bounds) if (deg > b) n++; return n; }

        void Set(ShotView v)
        {
            float[] b = v.Get<float[]>("bounds") ?? new[] { -18f, 18f };
            Lay(b.Length + 1, b);
            Call(v.Over ? -1 : v.Int("called"));
            _teeTo = TeeX(v.NextTee);
        }

        static float TeeX(int tee) => tee == 1 ? -110f : tee == 3 ? 110f : 0f;

        public override void Begin(ShotView start) { Set(start); _teeX = _teeTo; _tee.localPosition = new Vector3(_teeX, 2f, -Reach); }
        public override void Ready(ShotView v) { Set(v); }

        public override void Tick()
        {
            _teeX = Mathf.Lerp(_teeX, _teeTo, Time.deltaTime * 4f);
            _tee.localPosition = new Vector3(_teeX, 2f, -Reach);
            Vector3 cam = CameraFrom;
            foreach (Door d in _doors) d.Numeral.transform.rotation = Quaternion.LookRotation(d.Numeral.transform.position - cam);
            float pulse = 0.8f + Mathf.Sin(Time.time * 3f) * 0.2f;
            if (_called >= 0 && _called < _doors.Count) _doors[_called].Light.intensity = 5000f * pulse;
        }

        public override IEnumerator Play(ShotView v)
        {
            Vector3 start = new Vector3(TeeX(v.Tee), 0, -Reach);
            bool inside = v.Made && v.HasEntry && Mathf.Abs(v.EntryDeg) <= 90f;
            List<Vector3> pts;
            Vector3 rim = Vector3.zero;
            if (v.Made)
            {
                float deg = v.HasEntry ? v.EntryDeg : 0f;
                if (!inside) deg = v.HasEntry ? Mathf.Sign(v.EntryDeg) * Mathf.Max(120f, Mathf.Abs(v.EntryDeg)) : 180f;
                rim = Rim(deg, CupR - BallR * 0.4f);
                Vector3 outward = Rim(deg, 1f);
                if (inside) pts = Bezier(start, Vector3.Lerp(start, outward * DoorR * 2.2f, 0.5f), outward * DoorR * 1.7f, rim);
                else
                {
                    // Round the side of the doors and in through the back.
                    Vector3 side = Rim(Mathf.Sign(deg) * 100f, DoorR + 30f);
                    var a = Bezier(start, Vector3.Lerp(start, side, 0.6f), side + Vector3.back * 30f, side, 24);
                    var b = Bezier(side, side + Vector3.forward * 40f, outward * (CupR + 30f), rim, 20);
                    b.RemoveAt(0);
                    a.AddRange(b);
                    pts = a;
                }
            }
            else
            {
                float endZ = v.HasRollout ? Mathf.Clamp(v.RolloutIn * K, -120f, 160f) : 70f;
                float endX = Mathf.Clamp(v.LineIn * K, -90f, 90f);
                if (Mathf.Abs(endX) < CupR + BallR) endX = (v.MissSide == "LEFT" ? -1f : 1f) * (CupR + BallR + 3f);
                pts = Track(v, K, Reach, start, new Vector3(endX, 0, endZ));
            }
            Lift(pts, BallR);
            _ball.gameObject.SetActive(true);
            _ball.localPosition = pts[0];
            yield return Roll(_ball, pts, v.Made ? 1.5f : 1.6f, _trail, BallR);

            if (v.Made)
            {
                Sound("cup_plop", 0.7f);
                yield return Drop(_ball, rim, BallR);
                int door = inside ? DoorFor(v.EntryDeg) : -1;
                Sound(door >= 0 ? "gong" : "stone_grind", door >= 0 ? 0.7f : 0.5f);
                if (door >= 0 && door < _doors.Count)
                {
                    Door d = _doors[door];
                    bool called = door == _called;
                    Arcade.Sparks(At(d.Root.localPosition + Vector3.up * 20f), called ? 40 : 14);
                    if (called) Arcade.Celebrate(24, At(0, 10f, 0));
                    foreach (Renderer r in d.Stone) r.sharedMaterial = called ? _jade : _amber;
                    d.Light.enabled = true;
                    d.Light.color = called ? new Color(0.35f, 1f, 0.75f) : new Color(1f, 0.7f, 0.25f);
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.7f)
                    {
                        d.Light.intensity = Mathf.Lerp(30000f, 5000f, t);
                        d.Root.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.08f);
                        yield return null;
                    }
                    d.Root.localScale = Vector3.one;
                    d.Light.color = new Color(0.35f, 1f, 0.75f);
                }
                yield return new WaitForSeconds(0.4f);
            }
            else yield return new WaitForSeconds(0.7f);
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

        // The roll the camera saw (scaled, from reach back), or a made-up one from start to end when there isn't one.
        static List<Vector3> Track(ShotView v, float k, float reach, Vector3 start, Vector3 end)
        {
            var pts = new List<Vector3>();
            foreach (Vector2 p in v.Path) if (p.y * k >= -reach) pts.Add(new Vector3(Mathf.Clamp(p.x * k, -reach, reach), 0f, Mathf.Min(p.y * k, reach)));
            if (pts.Count < 4) return Bezier(start, Vector3.Lerp(start, end, 0.4f) + Vector3.right * (start.x * -0.2f), Vector3.Lerp(start, end, 0.75f), end);
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
                    for (int i = 0; i < drawn.Count; i++) trail.SetPosition(i, drawn[i] - Vector3.up * (r - 0.4f));
                    trail.SetPosition(drawn.Count, p - Vector3.up * (r - 0.4f));
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
