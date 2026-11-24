// Leapfrog on a moonlit pond: the putt is a leap from the lily pad the Player stands on to the pad with the cup in
// it. Short putts are hops from a near pad, long ones are leaps from far back. The dotted arc lands where the ball
// finished: in the cup, on the pad, or in the water with a splash. A glowing ring round the cup marks the pace the
// Player chose; a leap that lands in it sets the frog hopping. Units are inches; the cup is at the origin.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class LeapfrogScene : RuleScene
    {
        const float CupR = 3.4f, BallR = 1.6f, PadR = 26f, StartR = 17f, PadY = 0.7f;

        Transform _ball, _start, _frog, _zone;
        readonly List<Transform> _dots = new List<Transform>(), _oldDots = new List<Transform>();
        readonly List<Transform> _ripples = new List<Transform>();
        readonly List<float> _rippleAge = new List<float>();
        readonly List<Transform> _flies = new List<Transform>();
        Material _dotNew, _dotOld, _rippleMat, _zoneMat;
        Mesh _dotMesh;
        float _hop = -1f, _zoneFlash;
        Vector3 _frogHome;

        static bool Portrait => Screen.height > Screen.width;
        public override float WideFrame => 0.74f;   // a lane down the middle: nothing of it is under the side cards
        public override Vector3 CameraFrom => At(Portrait ? 0f : 20f, 70f, Portrait ? -190f : -180f);
        public override Vector3 CameraLook => At(-16f, 40f, 20f);
        public override float CameraFov => Portrait ? 58f : 46f;
        public override float ShadowDistance => 400f;

        public override void Build()
        {
            // Night sky, the moon and its halo, a treeline.
            GameObject sky = Make.MeshObject("Sky", Root, Gradient(9000f, new[] { -100f, 200f, 700f, 1600f, 3200f },
                new[] { new Color(0.16f, 0.22f, 0.33f), new Color(0.11f, 0.16f, 0.27f), new Color(0.06f, 0.09f, 0.18f), new Color(0.03f, 0.05f, 0.11f), new Color(0.015f, 0.025f, 0.06f) }), Make.Glow(Color.white, false));
            sky.transform.localPosition = new Vector3(0, 0, 3000f);
            Material soft = Make.Glow(Color.white, true, Make.SoftDot());
            Place(Make.MeshObject("Halo", Root, Quad(420f, new Color(0.6f, 0.68f, 0.9f, 0.3f)), soft), new Vector3(900f, 420f, 2900f));
            Place(Make.MeshObject("Moon", Root, Disc(52f, new Color(0.98f, 0.95f, 0.82f)), Make.Glow(Color.white, false)), new Vector3(900f, 420f, 2880f));
            var dice = new System.Random(9);
            for (int i = 0; i < 70; i++)
                Place(Make.MeshObject("Star", Root, Quad(6f + (float)dice.NextDouble() * 8f, new Color(1f, 1f, 1f, 0.3f + (float)dice.NextDouble() * 0.6f)), soft),
                    new Vector3((float)dice.NextDouble() * 4400f - 2200f, 500f + (float)dice.NextDouble() * 1800f, 2890f));
            Material trees = Make.Lit(new Color(0.015f, 0.03f, 0.035f), 0.1f);
            for (int i = 0; i < 26; i++)
            {
                float x = -2400f + i * 190f + (float)dice.NextDouble() * 60f;
                float h = 80f + (float)dice.NextDouble() * 90f;
                Shape(PrimitiveType.Sphere, "Tree", Root, new Vector3(x, h * 0.25f, 1700f), new Vector3(260f + (float)dice.NextDouble() * 120f, h, 120f), trees);
            }

            // The water: dark and glassy, the moon's path across it.
            Shape(PrimitiveType.Cube, "Water", Root, new Vector3(0, -1f, 800f), new Vector3(6000f, 2f, 2400f), Make.Lit(new Color(0.04f, 0.12f, 0.15f), 0.6f));

            // Lily pads: the cup's pad, the one the Player leaps from, more out on the pond.
            Material pad = Make.Lit(new Color(0.22f, 0.55f, 0.2f), 0.5f);
            Material padDark = Make.Lit(new Color(0.16f, 0.42f, 0.17f), 0.5f);
            Material vein = Make.Lit(new Color(0.24f, 0.52f, 0.22f), 0.5f);
            GameObject target = Make.MeshObject("Pad", Root, Sector(0f, PadR, 200f, 520f, Color.white, 48), pad);
            target.transform.localPosition = new Vector3(0, PadY - 0.2f, 0);
            GameObject rim = Make.MeshObject("Pad rim", Root, Sector(PadR - 1.2f, PadR, 200f, 520f, Color.white, 48), vein);
            rim.transform.localPosition = new Vector3(0, PadY - 0.15f, 0);
            GameObject cup = Make.MeshObject("Cup", Root, Make.Disc(CupR, 40), Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f));
            cup.transform.localPosition = new Vector3(0, PadY, 0);
            Hoop("Liner", Root, new Vector3(0, PadY + 0.02f, 0), CupR, CupR + 0.45f, Make.Lit(Color.white, 0.5f));
            _zone = new GameObject("Pace ring").transform;
            _zone.SetParent(Root, false);
            _zone.localPosition = new Vector3(0, PadY + 0.05f, 0);
            _zoneMat = Make.Glow(new Color(0.6f, 1f, 0.5f, 0.35f));
            _start = Make.MeshObject("Start pad", Root, Sector(0f, StartR, 30f, 330f, Color.white, 40), padDark).transform;
            for (int i = 0; i < 16; i++)
            {
                float x = (float)dice.NextDouble() * 900f - 450f, z = 80f + (float)dice.NextDouble() * 700f;
                if (Mathf.Abs(x) < 70f && z < 160f) continue;
                float r = 10f + (float)dice.NextDouble() * 22f, a = (float)dice.NextDouble() * 360f;
                GameObject p = Make.MeshObject("Pad", Root, Sector(0f, r, a + 20f, a + 340f, Color.white, 28), i % 2 == 0 ? pad : padDark);
                p.transform.localPosition = new Vector3(x, 0.15f, z);
            }
            // A lotus on its own pad.
            GameObject lotusPad = Make.MeshObject("Lotus pad", Root, Sector(0f, 11f, 40f, 340f, Color.white, 28), pad);
            lotusPad.transform.localPosition = new Vector3(-36f, 0.3f, 30f);
            Material petal = Make.Lit(new Color(1f, 0.55f, 0.72f), 0.5f);
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * 360f;
                GameObject pe = Shape(PrimitiveType.Sphere, "Petal", Root, new Vector3(-36f, 3.2f, 30f), new Vector3(2.6f, 7f, 1.6f), petal);
                pe.transform.localRotation = Quaternion.Euler(0, a, 0) * Quaternion.Euler(28f, 0, 0) * Quaternion.Euler(0, 0, 0);
                pe.transform.localPosition += pe.transform.localRotation * new Vector3(0, 0.5f, 1.6f);
            }
            Shape(PrimitiveType.Sphere, "Heart", Root, new Vector3(-36f, 3.4f, 30f), Vector3.one * 2.4f, Make.Emissive(new Color(1f, 0.85f, 0.3f), new Color(1.2f, 0.9f, 0.2f)));

            // Reeds and cattails at the sides.
            Material reed = Make.Lit(new Color(0.1f, 0.18f, 0.08f), 0.3f);
            Material cat = Make.Lit(new Color(0.3f, 0.17f, 0.08f), 0.3f);
            foreach (Vector3 r in new[] { new Vector3(-92f, 0, -110f), new Vector3(-100f, 0, -60f), new Vector3(110f, 0, -100f), new Vector3(118f, 0, -40f), new Vector3(-120f, 0, 20f), new Vector3(130f, 0, 40f) })
            {
                for (int k = 0; k < 3; k++)
                {
                    float h = 40f + k * 9f, lean = (k - 1) * 6f;
                    Transform stem = Shape(PrimitiveType.Cylinder, "Reed", Root, r + new Vector3(k * 3f, h * 0.5f, k * 2f), new Vector3(0.7f, h * 0.5f, 0.7f), reed).transform;
                    stem.localRotation = Quaternion.Euler(0, 0, lean);
                    if (k != 1) Shape(PrimitiveType.Capsule, "Cattail", Root, r + new Vector3(k * 3f - Mathf.Sin(lean * Mathf.Deg2Rad) * h, h - 4f, k * 2f), new Vector3(2.2f, 5f, 2.2f), cat).transform.localRotation = Quaternion.Euler(0, 0, lean);
                }
            }

            // The frog, on the cup's pad.
            Material skin = Make.Lit(new Color(0.35f, 0.7f, 0.22f), 0.55f);
            Material belly = Make.Lit(new Color(0.75f, 0.85f, 0.45f), 0.5f);
            Material eye = Make.Lit(new Color(0.98f, 0.96f, 0.85f), 0.8f);
            Material pupil = Make.Lit(Color.black, 0.9f);
            _frog = new GameObject("Frog").transform;
            _frog.SetParent(Root, false);
            _frogHome = new Vector3(14f, PadY, 9f);
            _frog.localPosition = _frogHome;
            _frog.localRotation = Quaternion.Euler(0, 200f, 0);
            Shape(PrimitiveType.Sphere, "Body", _frog, new Vector3(0, 4.2f, 0), new Vector3(9f, 6.5f, 11f), skin);
            Shape(PrimitiveType.Sphere, "Belly", _frog, new Vector3(0, 3.6f, 2.6f), new Vector3(7f, 5f, 6f), belly);
            Shape(PrimitiveType.Sphere, "Head", _frog, new Vector3(0, 7.2f, 3.6f), new Vector3(8f, 5f, 6.5f), skin);
            foreach (float s in new[] { -1f, 1f })
            {
                Shape(PrimitiveType.Sphere, "Eye", _frog, new Vector3(s * 2.6f, 9.6f, 4.2f), Vector3.one * 3f, eye);
                Shape(PrimitiveType.Sphere, "Pupil", _frog, new Vector3(s * 2.8f, 9.9f, 5.5f), Vector3.one * 1.3f, pupil);
                Shape(PrimitiveType.Sphere, "Thigh", _frog, new Vector3(s * 4.6f, 2.6f, -2.5f), new Vector3(3.5f, 3.5f, 7f), skin);
                Shape(PrimitiveType.Sphere, "Foot", _frog, new Vector3(s * 5.4f, 0.6f, 2f), new Vector3(3.6f, 1f, 5f), skin);
                Shape(PrimitiveType.Sphere, "Hand", _frog, new Vector3(s * 3f, 0.8f, 5.6f), new Vector3(2.4f, 1f, 2.8f), skin);
            }

            // Fireflies.
            for (int i = 0; i < 16; i++)
            {
                GameObject f = Make.MeshObject("Firefly", Root, Quad(3.5f, new Color(0.9f, 1f, 0.5f, 0.9f)), soft);
                f.transform.localPosition = new Vector3((float)dice.NextDouble() * 260f - 130f, 10f + (float)dice.NextDouble() * 50f, (float)dice.NextDouble() * 200f - 60f);
                _flies.Add(f.transform);
            }

            _dotMesh = Quad(2.2f, Color.white);
            _dotNew = Make.Glow(new Color(1f, 1f, 1f, 0.95f), true, Make.SoftDot());
            _dotOld = Make.Glow(new Color(0.5f, 0.9f, 0.4f, 0.55f), true, Make.SoftDot());
            _rippleMat = Make.Glow(new Color(0.8f, 0.9f, 1f, 0.6f));
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(new Color(0.98f, 0.98f, 0.96f), 0.6f)).transform;

            Sun(Root, new Vector3(40f, -30f, 0f), new Color(0.65f, 0.72f, 1f), 0.75f);
            Sun(Root, new Vector3(25f, 190f, 0f), new Color(0.8f, 0.85f, 1f), 0.35f);
            Lamp(Root, new Vector3(-20f, 160f, 10f), new Vector3(0, 0, -30f), new Color(0.8f, 0.9f, 1f), 150000f, 70f, 500f);
        }

        static void Place(GameObject go, Vector3 at) { go.transform.localPosition = at; }

        static Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a)); }

        static Mesh Sector(float inner, float outer, float fromDeg, float toDeg, Color color, int segs)
        {
            var verts = new List<Vector3>(); var cols = new List<Color>(); var norms = new List<Vector3>(); var tris = new List<int>();
            if (inner <= 0f)
            {
                verts.Add(Vector3.zero); cols.Add(color); norms.Add(Vector3.up);
                for (int s = 0; s <= segs; s++) { verts.Add(Dir(Mathf.Lerp(fromDeg, toDeg, s / (float)segs)) * outer); cols.Add(color); norms.Add(Vector3.up); }
                for (int s = 0; s < segs; s++) { tris.Add(0); tris.Add(s + 2); tris.Add(s + 1); }
            }
            else
            {
                for (int s = 0; s <= segs; s++)
                {
                    Vector3 d = Dir(Mathf.Lerp(fromDeg, toDeg, s / (float)segs));
                    verts.Add(d * inner); verts.Add(d * outer);
                    cols.Add(color); cols.Add(color); norms.Add(Vector3.up); norms.Add(Vector3.up);
                }
                for (int s = 0; s < segs; s++) { int i = s * 2; tris.Add(i); tris.Add(i + 3); tris.Add(i + 1); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            }
            var m = new Mesh();
            m.SetVertices(verts); m.SetColors(cols); m.SetNormals(norms); m.SetTriangles(tris, 0);
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

        // A flat disc standing in XY (for the moon).
        static Mesh Disc(float r, Color c)
        {
            var verts = new List<Vector3> { Vector3.zero }; var cols = new List<Color> { c }; var tris = new List<int>();
            for (int s = 0; s <= 48; s++) { float a = s / 48f * Mathf.PI * 2f; verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0)); cols.Add(c); }
            for (int s = 0; s < 48; s++) { tris.Add(0); tris.Add(s + 1); tris.Add(s + 2); }
            var m = new Mesh();
            m.SetVertices(verts); m.SetColors(cols); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ── The roll ──

        // Short side squeezed so a 3 ft hop and an 11 ft leap both fit: the near pad never overlaps the cup's.
        static float K(int feet) { float f = Mathf.Max(3, feet) * 12f; return (22f + f * 0.62f) / f; }

        static Vector2 TeeAt(int feet, int tee) => new Vector2(tee == 1 ? -10f : tee == 3 ? 10f : 0f, -Mathf.Max(3, feet) * 12f * K(feet));

        static Vector2 Finish(ShotView v)
        {
            if (v.Made) return Vector2.zero;
            float past = v.HasRollout ? v.RolloutIn : v.Reached ? 10f : -10f;
            float side = v.LineIn;
            if (v.Reached && Mathf.Abs(side) < CupR + 0.5f) side = (v.MissSide == "LEFT" || (v.MissSide == "" && side < 0)) ? -(CupR + 1f) : CupR + 1f;
            if (v.Reached && past > 0) side *= 1f + past / 30f;
            return new Vector2(Mathf.Clamp(side, -30f, 30f), Mathf.Clamp(past < 0f ? past * K(v.Feet) : past, -60f, 60f));
        }

        static List<Vector2> RollOf(ShotView v)
        {
            Vector2 start = TeeAt(v.Feet, v.Tee), end = Finish(v);
            var pts = new List<Vector2>();
            if (v.Path != null && v.Path.Count >= 2)
            {
                if (v.Path[0].y * K(v.Feet) > start.y + 12f) pts.Add(start);
                foreach (Vector2 q in v.Path) pts.Add(new Vector2(q.x, q.y < 0f ? q.y * K(v.Feet) : q.y));
                if (Vector2.Distance(pts[pts.Count - 1], end) > 3f && (v.Made || v.HasRollout)) pts.Add(end);
                return pts;
            }
            Vector2 bend = new Vector2((start.x + end.x) * 0.5f + Mathf.Clamp(v.LineIn, -6f, 6f) * 0.9f, (start.y + end.y) * 0.5f);
            for (int i = 0; i <= 30; i++) { float t = i / 30f; pts.Add((1 - t) * (1 - t) * start + 2 * (1 - t) * t * bend + t * t * end); }
            return pts;
        }

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

        void Zone(float target)
        {
            foreach (Transform c in _zone) Make.Kill(c.gameObject);
            float r = CupR + target;
            Make.MeshObject("Band", _zone, Sector(Mathf.Max(CupR + 0.5f, r - 3f), r + 3f, -180f, 180f, Color.white, 64), _zoneMat);
        }

        void Perch(int feet, int tee)
        {
            Vector2 s = TeeAt(feet, tee);
            _start.localPosition = new Vector3(s.x, 0.4f, s.y - 4f);
            _ball.gameObject.SetActive(true);
            _ball.localPosition = new Vector3(s.x, BallR + 0.6f, s.y);
        }

        void Ripple(Vector3 at)
        {
            for (int k = 0; k < 3; k++)
            {
                GameObject r = Make.MeshObject("Ripple", Root, Sector(0.86f, 1f, -180f, 180f, Color.white, 64), _rippleMat);
                r.transform.localPosition = new Vector3(at.x, 0.25f, at.z);
                r.transform.localScale = Vector3.one * 0.01f;
                _ripples.Add(r.transform);
                _rippleAge.Add(-k * 0.25f);
            }
        }

        public override void Begin(ShotView start)
        {
            Zone(start.Num("target"));
            Perch(start.NextFeet, start.NextTee);
        }

        public override void Ready(ShotView v)
        {
            if (!v.Over) Perch(v.NextFeet, v.NextTee);
        }

        public override IEnumerator Play(ShotView v)
        {
            // Last leap's dots fade to green; the leap before that goes.
            foreach (Transform d in _oldDots) Make.Kill(d.gameObject);
            _oldDots.Clear();
            foreach (Transform d in _dots) { d.GetComponent<MeshRenderer>().sharedMaterial = _dotOld; _oldDots.Add(d); }
            _dots.Clear();

            Vector2 tee = TeeAt(v.Feet, v.Tee);
            _start.localPosition = new Vector3(tee.x, 0.4f, tee.y - 4f);
            List<Vector2> roll = RollOf(v);
            Vector2 end = roll[roll.Count - 1];
            bool onPad = end.magnitude <= PadR - 1f, onStart = Vector2.Distance(end, tee) < StartR;
            float dist = Vector2.Distance(roll[0], end);
            float apex = 10f + dist * 0.22f;
            float seconds = Mathf.Lerp(0.9f, 1.5f, Mathf.InverseLerp(30f, 160f, dist));
            _ball.gameObject.SetActive(true);
            Sound("frog_croak", 0.6f);
            float nextDot = 0f;
            Vector3 last = _ball.localPosition;
            float travelled = 0f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                Vector2 p = Along(roll, t);
                float baseY = (onPad || v.Made) ? PadY : onStart ? 0.4f : 0f;
                float y = Mathf.Lerp(PadY, baseY, t) + BallR + 4f * apex * t * (1f - t);
                Vector3 at = new Vector3(p.x, y, p.y);
                travelled += Vector3.Distance(at, last);
                last = at;
                _ball.localPosition = at;
                if (travelled >= nextDot)
                {
                    GameObject dot = Make.MeshObject("Dot", Root, _dotMesh, _dotNew);
                    dot.transform.localPosition = at;
                    _dots.Add(dot.transform);
                    nextDot += 5f;
                }
                yield return null;
            }
            Vector3 land = new Vector3(end.x, 0f, end.y);
            if (v.Made)
            {
                _ball.localPosition = new Vector3(0, PadY + BallR, 0);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localPosition = new Vector3(0, Mathf.Lerp(PadY + BallR, -3f, t), 0); yield return null; }
                _ball.gameObject.SetActive(false);
                Sound("leapfrog_make", "cup_plop", 0.8f);   // puttr-app-2: the game's own sound, the shared one until recorded
                Ripple(Vector3.zero);
            }
            else if (onPad || onStart)
            {
                _ball.localPosition = land + Vector3.up * ((onPad ? PadY : 0.4f) + BallR);
                Sound("leapfrog_lilypad", "ball_land", 0.5f);   // puttr-app-2: the game's own sound, the shared one until recorded
                Ripple(onPad ? Vector3.zero : land);
            }
            else
            {
                // In the water.
                Sound("water_plop", 0.8f);
                Ripple(land);
                Arcade.Sparks(At(land + Vector3.up * 2f), 10);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f) { _ball.localPosition = land + Vector3.up * Mathf.Lerp(BallR, -BallR * 2f, t); yield return null; }
                _ball.gameObject.SetActive(false);
            }
            if (v.Good)
            {
                _hop = 0f;
                _zoneFlash = 1f;
                if (v.Made) Arcade.Sparks(At(0, 6f, 0), 18);
            }
            yield return new WaitForSeconds(0.8f);
        }

        public override void Tick()
        {
            float dt = Time.deltaTime;
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                _rippleAge[i] += dt;
                float a = _rippleAge[i];
                if (a > 2.2f) { Make.Kill(_ripples[i].gameObject); _ripples.RemoveAt(i); _rippleAge.RemoveAt(i); continue; }
                _ripples[i].gameObject.SetActive(a > 0f);
                if (a > 0f) _ripples[i].localScale = Vector3.one * (4f + a * 24f);
            }
            if (_rippleMat != null) _rippleMat.SetColor("_Color", new Color(0.8f, 0.9f, 1f, 0.45f));
            _zoneFlash = Mathf.Max(0f, _zoneFlash - dt * 0.7f);
            if (_zoneMat != null) _zoneMat.SetColor("_Color", new Color(0.6f, 1f, 0.5f, 0.22f + 0.08f * Mathf.Sin(Time.time * 2f) + 0.6f * _zoneFlash));
            if (_hop >= 0f && _frog != null)
            {
                _hop += dt / 0.7f;
                float h = _hop < 1f ? 4f * 16f * _hop * (1f - _hop) : 0f;
                _frog.localPosition = _frogHome + Vector3.up * h;
                _frog.localRotation = Quaternion.Euler(-h * 1.5f, 200f + (_hop < 1f ? _hop * 360f : 0f), 0);
                if (_hop >= 1f) _hop = -1f;
            }
            for (int i = 0; i < _flies.Count; i++)
            {
                Transform f = _flies[i];
                float n = Time.time * 0.25f + i * 7.3f;
                f.localPosition += new Vector3(Mathf.PerlinNoise(n, 0f) - 0.5f, (Mathf.PerlinNoise(0f, n) - 0.5f) * 0.6f, Mathf.PerlinNoise(n, n) - 0.5f) * dt * 30f;
                Vector3 p = f.localPosition;
                if (p.y < 6f || p.y > 70f || Mathf.Abs(p.x) > 160f || p.z < -90f || p.z > 160f) f.localPosition = new Vector3(Mathf.Clamp(p.x, -150f, 150f) * 0.9f, 30f, Mathf.Clamp(p.z, -80f, 150f) * 0.9f);
                f.localScale = Vector3.one * (0.6f + 0.6f * Mathf.PerlinNoise(n * 3f, 1f));
            }
        }
    }
}
