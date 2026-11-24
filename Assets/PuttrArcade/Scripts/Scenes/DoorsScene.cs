// Doors, up close on the hole (Matt, 2026-11-23: any hole-entry game is drawn on the real 4.25 inch hole from Center
// Cut's viewpoint, see HoleView): small jade-temple archways stand round the front of the cup's rim, one per door
// (3, 5 or 7), each with its sill marked on the mat between the arch and the lip. The called door glows jade. The
// putt rolls up to the cup and drops in through the door it entered by: the called door blazes, a wrong door
// flashes amber, a back-door make sneaks round behind, a miss rolls on by.
// Units: inches x K; the cup is at the origin, the golfer is down -Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class DoorsScene : RuleScene
    {
        const float K = 5f, CupR = 2.125f * K, BallR = 0.84f * K, DoorR = CupR + 1.7f * K, SillIn = CupR + 0.25f * K, Lead = 80f, Reach = 240f;

        sealed class Door
        {
            public Transform Root;
            public readonly List<Renderer> Stone = new List<Renderer>();
            public GameObject Portal;
            public Renderer Sill;
            public Light Light;
            public TextMesh Numeral;
            public float Deg;
        }

        readonly List<Door> _doors = new List<Door>();
        float[] _bounds = new float[0];
        int _called = -1, _count;
        Transform _doorRoot, _ball, _tee;
        LineRenderer _trail;
        Material _stone, _jade, _amber, _sillOff, _sillOn, _sillWrong;
        float _teeX, _teeTo;

        public override Vector3 CameraFrom => At(HoleView.From(CupR));
        public override Vector3 CameraLook => At(HoleView.Look(CupR));
        public override float CameraFov => HoleView.Fov;
        public override float CardLens => 54f;
        public override float ShadowDistance => 500f;

        // The doors by name, short, as the app's panel calls them (LEFT, CENTER, RIGHT...).
        static readonly string[][] Short = { new[] { "L", "C", "R" }, new[] { "FL", "L", "C", "R", "FR" }, new[] { "L3", "L2", "L1", "C", "R1", "R2", "R3" } };
        static string NameOf(int i, int count) { string[] set = count <= 3 ? Short[0] : count <= 5 ? Short[1] : Short[2]; return i < set.Length ? set[i] : (i + 1).ToString(); }

        public override void Build()
        {
            Material rimStone = Make.Lit(new Color(0.45f, 0.5f, 0.47f), 0.3f);
            _stone = Make.Lit(new Color(0.5f, 0.54f, 0.52f), 0.25f);
            _jade = Make.Emissive(new Color(0.3f, 1f, 0.75f), new Color(0.15f, 1.2f, 0.75f), 0.6f);
            _amber = Make.Emissive(new Color(1f, 0.7f, 0.25f), new Color(1.6f, 0.85f, 0.18f), 0.6f);
            _sillOff = Make.Glow(new Color(0.75f, 0.85f, 0.8f, 0.13f), false);
            _sillOn = Make.Glow(new Color(0.3f, 1f, 0.75f, 0.6f), false);
            _sillWrong = Make.Glow(new Color(1f, 0.7f, 0.25f, 0.6f), false);

            // The temple floor round the mat, the mat and its real hole, a stone step with a jade inlay behind it.
            HoleView.Floor(Root, CupR, -3.2f, Make.Lit(new Color(0.04f, 0.16f, 0.1f), 0.35f));
            HoleView.Build(Root, CupR, new Color(0.04f, 0.13f, 0.1f));
            Shape(PrimitiveType.Cube, "Step", Root, new Vector3(0, 3f, 7f * K + 7f), new Vector3(24f * K + 10f, 12f, 14f), Make.Lit(new Color(0.1f, 0.13f, 0.12f), 0.3f));   // dark: it lies behind the logo row upright
            Shape(PrimitiveType.Cube, "Inlay", Root, new Vector3(0, 7f, 7f * K - 0.2f), new Vector3(24f * K + 6f, 1.2f, 0.4f), Make.Emissive(Color.black, new Color(0.05f, 0.4f, 0.25f)));
            Hoop("Ring", Root, new Vector3(0, 0.1f, 0), DoorR + 2.6f * K, DoorR + 2.72f * K, Make.Glow(new Color(0.3f, 1f, 0.7f, 0.16f)));

            _doorRoot = new GameObject("Doors").transform;
            _doorRoot.SetParent(Root, false);

            // The tee down the mat, and the ball.
            _tee = Shape(PrimitiveType.Cube, "Tee", Root, new Vector3(0, 0.4f, -Reach), new Vector3(26f, 0.8f, 14f), Make.Lit(new Color(0.25f, 0.28f, 0.27f), 0.4f)).transform;
            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.6f, 0f, bc, bn)).transform;
            _ball.gameObject.SetActive(false);
            _trail = Stroke(Root, "Trail", new Color(0.4f, 1f, 0.8f, 0.55f), 0.9f);

            Sun(Root, new Vector3(50f, 30f, 0f), new Color(0.75f, 1f, 0.92f), 0.55f);
            Lamp(Root, new Vector3(0, 380f, -160f), new Vector3(0, 0, 10f), new Color(0.85f, 1f, 0.95f), 380000f, 50f, 900f);
            Lamp(Root, new Vector3(-240f, 200f, -120f), new Vector3(0, 20f, 0), new Color(0.6f, 0.9f, 1f), 160000f, 40f, 700f);
        }

        void Lay(int count, float[] bounds)
        {
            if (count == _count && _doors.Count == count) return;
            _count = count;
            _bounds = bounds;
            foreach (Door old in _doors) { Object.Destroy(old.Root.gameObject); Object.Destroy(old.Sill.gameObject); }
            _doors.Clear();
            for (int i = 0; i < count; i++)
            {
                float lo = i == 0 ? -90f : bounds[i - 1], hi = i == count - 1 ? 90f : bounds[i];
                var door = new Door { Deg = (lo + hi) / 2f };
                // The sill: the door's own part of the rim, marked on the mat from the lip out to the arch.
                GameObject sill = Make.MeshObject("Sill " + (i + 1), _doorRoot, Sector(SillIn, DoorR - 0.2f * K, lo + 1.2f, hi - 1.2f), _sillOff);
                sill.transform.localPosition = new Vector3(0, 0.1f, 0);
                sill.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                door.Sill = sill.GetComponent<Renderer>();
                // The arch: as wide as its part of the rim (no wider than 3 inches), kept low so it never hides the lip.
                float w = Mathf.Min(3f * K, 2f * DoorR * Mathf.Sin((hi - lo) * 0.5f * Mathf.Deg2Rad) * 0.8f), post = w * 0.13f, h = w * 0.22f, rise = w * 0.26f;
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
                    GameObject stone = Shape(PrimitiveType.Cube, "Voussoir", door.Root, new Vector3(Mathf.Cos(a) * w / 2f, h + Mathf.Sin(a) * rise, 0), new Vector3(post, w * 0.2f, post * 1.6f), _stone);
                    stone.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(Mathf.Sin(a) * rise, Mathf.Cos(a) * w / 2f) * Mathf.Rad2Deg);
                    door.Stone.Add(stone.GetComponent<Renderer>());
                }
                door.Portal = Shape(PrimitiveType.Cube, "Portal", door.Root, new Vector3(0, (h + rise) * 0.5f, 0), new Vector3(w - post, h + rise, 0.15f), Make.Glow(new Color(0.3f, 1f, 0.75f, 0.2f)));
                door.Portal.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                door.Light = World.NewLight(door.Root, "Door light", LightType.Point, new Color(0.35f, 1f, 0.75f), LightOn);
                door.Light.transform.localPosition = new Vector3(0, h + rise + 3f, 2f);
                door.Light.range = 30f;
                door.Light.shadows = LightShadows.None;
                // The door's number, flat on the mat outside the arch, upright on the screen whatever way the door faces.
                door.Numeral = Words(door.Root, NameOf(i, count), new Vector3(0, 0.2f, -(count <= 3 ? 1.1f : 0.9f) * K), (count <= 3 ? 1f : count <= 5 ? 0.8f : 0.62f) * K, Color.white);
                door.Numeral.transform.rotation = Root.rotation * Quaternion.Euler(90f, 0, 0);
                _doors.Add(door);
            }
        }

        const float LightOn = 700f;

        static Mesh Sector(float inner, float outer, float fromDeg, float toDeg, int seg = 24)
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
            var mesh = new Mesh { name = "Sill" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        void Call(int called)
        {
            _called = called;
            for (int i = 0; i < _doors.Count; i++)
            {
                bool on = i == called;
                foreach (Renderer r in _doors[i].Stone) r.sharedMaterial = on ? _jade : _stone;
                _doors[i].Sill.sharedMaterial = on ? _sillOn : _sillOff;
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

        static float TeeX(int tee) => tee == 1 ? -8f * K : tee == 3 ? 8f * K : 0f;

        public override void Begin(ShotView start) { Set(start); _teeX = _teeTo; _tee.localPosition = new Vector3(_teeX, 0.4f, -Reach); }
        public override void Ready(ShotView v) { Set(v); }

        public override void Tick()
        {
            _teeX = Mathf.Lerp(_teeX, _teeTo, Time.deltaTime * 4f);
            _tee.localPosition = new Vector3(_teeX, 0.4f, -Reach);
            float pulse = 0.8f + Mathf.Sin(Time.time * 3f) * 0.2f;
            if (_called >= 0 && _called < _doors.Count) _doors[_called].Light.intensity = LightOn * pulse;
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
                if (inside) pts = Bezier(start, Vector3.Lerp(start, outward * Lead * 2.2f, 0.5f), outward * Lead * 1.7f, rim);
                else
                {
                    // Round the side of the doors and in through the back.
                    Vector3 side = Rim(Mathf.Sign(deg) * 100f, DoorR + 1.6f * K);
                    var a = Bezier(start, Vector3.Lerp(start, side, 0.6f), side + Vector3.back * 12f, side, 24);
                    var b = Bezier(side, side + Vector3.forward * 12f, outward * (CupR + 8f), rim, 20);
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
                Sound("doors_make", "cup_plop", 0.7f);   // puttr-app-2: the game's own sound, the shared one until recorded
                yield return Drop(_ball, rim, BallR);
                int door = inside ? DoorFor(v.EntryDeg) : -1;
                Sound(door >= 0 ? "gong" : "stone_grind", door >= 0 ? 0.7f : 0.5f);
                if (door >= 0 && door < _doors.Count)
                {
                    Door d = _doors[door];
                    bool called = door == _called;
                    Arcade.Sparks(At(d.Root.localPosition + Vector3.up * 5f), called ? 40 : 14);
                    if (called) Arcade.Celebrate(24, At(0, 10f, 0));
                    foreach (Renderer r in d.Stone) r.sharedMaterial = called ? _jade : _amber;
                    d.Sill.sharedMaterial = called ? _sillOn : _sillWrong;
                    d.Light.enabled = true;
                    d.Light.color = called ? new Color(0.35f, 1f, 0.75f) : new Color(1f, 0.7f, 0.25f);
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.7f)
                    {
                        d.Light.intensity = Mathf.Lerp(LightOn * 6f, LightOn, t);
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
