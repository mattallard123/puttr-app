// Lockdown, "The Vault": a hexagonal vault room with a radar on its floor. Six spoke rails (tempo, speed, back time,
// forward time, length, rotation) run out from the hub to the rim, where a corner sits when a stroke matches your own
// average exactly. The band is a low hexagonal wall: a corner pulled inside it broke the band. Each stroke rises from
// the hub as a glowing hexagon. A Lockdown slams the wall a notch closer to the rim (the band tightening), turns the
// vault door's dial and lights another combo ring; a break flashes the broken rails red, eases the wall back and lights
// a warning lamp (three in a row ends the run). Calibration strokes draw ghost hexagons; the third raises the wall.
// Units: inches; the hub is at the origin, TEMPO points down +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class LockdownScene : SensorStage
    {
        const float R = 40f, WallH = 4.5f, RiseH = 5f;
        static readonly Color Violet = Make.Hex(0xB57BFF), Break = Make.Hex(0xFF4A5A), Rail = new Color(0.85f, 0.8f, 1f);
        static readonly string[] Names = { "TEMPO", "SPEED", "BACK T", "FWD T", "LENGTH", "ROT" };

        Putter _putter;
        Transform _ball, _dial, _wallRoot;
        readonly Transform[] _walls = new Transform[6];
        readonly Material[] _railMat = new Material[6];
        readonly Transform[] _corners = new Transform[6];
        readonly Material[] _cornerMat = new Material[6];
        readonly List<Material> _combo = new List<Material>();
        readonly List<Material> _lamps = new List<Material>();
        readonly List<LineRenderer> _ghosts = new List<LineRenderer>();
        Mesh _hex;
        GameObject _hexObj;
        Material _hexMat;
        LineRenderer _hexLine, _wallLine;
        Readout _read;
        float _ring = 0.55f, _wallShown = 1f;

        public override Vector3 CameraFrom => Portrait ? At(0f, 128f, -96f) : At(0f, 104f, -84f);
        public override Vector3 CameraLook => Portrait ? At(-8f, 0f, 14f) : At(-4f, 0f, 8f);
        public override float CameraFov => Portrait ? 50f : 46f;
        public override float ShadowDistance => 400f;

        static Vector3 Axis(int i) { float a = (90f - 60f * i) * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
        /// <summary>The app's devToRadius on a deviation measured in base allowances: the rim is a perfect match.</summary>
        static float Radius(float dev) => Mathf.Clamp(1f - 0.45f * dev, 0.08f, 1f);

        public override void Build()
        {
            // The vault: a dark hexagonal floor with a lip, walls of steel round it.
            Material floor = Make.Lit(new Color(0.05f, 0.045f, 0.07f), 0.65f, 0.3f);
            Make.MeshObject("Floor", Root, Fan(new[] { 58f, 58f, 58f, 58f, 58f, 58f }, 0f), floor);
            Shape(PrimitiveType.Cube, "Ground", Root, new Vector3(0, -1.5f, 0), new Vector3(600f, 1f, 600f), Make.Lit(new Color(0.02f, 0.02f, 0.03f), 0.3f));
            Material steel = Make.Lit(new Color(0.12f, 0.11f, 0.15f), 0.75f, 0.7f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 a = Corner(i, 64f), b = Corner(i + 1, 64f);
                Transform wall = Shape(PrimitiveType.Cube, "Vault wall", Root, (a + b) / 2f + Vector3.up * 9f, new Vector3(64f, 18f, 2f), steel).transform;
                wall.localRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, b - a));
                GameObject trim = Shape(PrimitiveType.Cube, "Trim", wall, new Vector3(0, -0.36f, -0.6f), new Vector3(1f, 0.04f, 0.3f), Make.Emissive(Color.black, Violet * 1.2f));
                NoShadow(trim);
            }

            // The radar: the rim, three faint guide rings, the six rails and their names.
            LineRenderer rim = Stroke(Root, "Rim", new Color(1f, 1f, 1f, 0.5f), 0.5f);
            Loop(rim, R, 0.15f);
            foreach (float g in new[] { 0.3f, 0.55f, 0.8f })
            {
                LineRenderer ring = Stroke(Root, "Guide", new Color(0.7f, 0.6f, 1f, 0.12f), 0.25f, false);
                Loop(ring, R * g, 0.1f);
            }
            for (int i = 0; i < 6; i++)
            {
                _railMat[i] = Make.Glow(new Color(Rail.r, Rail.g, Rail.b, 0.55f), true);
                Transform rail = Shape(PrimitiveType.Cube, "Rail", Root, Axis(i) * R / 2f + Vector3.up * 0.1f, new Vector3(0.55f, 0.15f, R), _railMat[i]).transform;
                rail.localRotation = Quaternion.LookRotation(Axis(i));
                NoShadow(rail.gameObject);
                TextMesh label = Words(Root, Names[i], Axis(i) * (R + 7f) + Vector3.up * 0.3f, 3.4f, new Color(0.88f, 0.82f, 1f, 0.95f));
                label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            // The band: a low wall round the ring, with a bright line along its top.
            _wallRoot = new GameObject("Band").transform;
            _wallRoot.SetParent(Root, false);
            Material wallMat = Make.Glow(new Color(Violet.r, Violet.g, Violet.b, 0.32f), false);
            for (int i = 0; i < 6; i++) { _walls[i] = Shape(PrimitiveType.Cube, "Band wall", _wallRoot, Vector3.zero, Vector3.one, wallMat).transform; NoShadow(_walls[i].gameObject); }
            _wallLine = Stroke(_wallRoot, "Band top", Violet, 0.6f);

            // The stroke's hexagon and its corners.
            _hex = new Mesh { name = "Stroke" };
            _hexMat = Make.Glow(new Color(Violet.r, Violet.g, Violet.b, 0.3f), true);
            _hexObj = Make.MeshObject("Stroke", Root, _hex, _hexMat);
            NoShadow(_hexObj);
            _hexLine = Stroke(Root, "Stroke edge", Color.white, 0.5f);
            for (int i = 0; i < 6; i++)
            {
                _cornerMat[i] = Make.Emissive(Color.white, Color.white * 1.2f);
                _corners[i] = Shape(PrimitiveType.Sphere, "Corner", Root, Vector3.zero, Vector3.one * 2.2f, _cornerMat[i]).transform;
                NoShadow(_corners[i].gameObject);
                _corners[i].gameObject.SetActive(false);
            }

            // The vault door on the far wall: its dial and the combo rings.
            var door = new GameObject("Door").transform;
            door.SetParent(Root, false);
            door.localPosition = new Vector3(26f, 17f, 57f);
            door.localRotation = Quaternion.Euler(-12f, -30f, 0f);
            Transform slab = Cylinder("Door", door, Vector3.zero, 15f, 3f, Make.Lit(new Color(0.2f, 0.19f, 0.24f), 0.8f, 0.85f)).transform;
            slab.localRotation = Quaternion.Euler(90f, 0, 0);
            for (int k = 0; k < 6; k++)
            {
                Material m = Make.Glow(new Color(Violet.r, Violet.g, Violet.b, 0.18f), true);
                GameObject ring = Make.MeshObject("Combo ring", door, Make.Ring(5.4f + k * 1.5f, 6.2f + k * 1.5f, 72), m);
                ring.transform.localPosition = new Vector3(0, 0, -1.6f);
                ring.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
                NoShadow(ring);
                _combo.Add(m);
            }
            _dial = new GameObject("Dial").transform;
            _dial.SetParent(door, false);
            _dial.localPosition = new Vector3(0, 0, -2f);
            Material chrome = Make.Lit(new Color(0.8f, 0.8f, 0.85f), 0.9f, 1f);
            Transform hub = Cylinder("Hub", _dial, Vector3.zero, 2.6f, 1.6f, chrome).transform;
            hub.localRotation = Quaternion.Euler(90f, 0, 0);
            for (int k = 0; k < 3; k++)
            {
                Transform spoke = Shape(PrimitiveType.Cube, "Handle", _dial, Vector3.zero, new Vector3(9f, 0.7f, 0.7f), chrome).transform;
                spoke.localRotation = Quaternion.Euler(0, 0, k * 60f);
                spoke.localPosition = new Vector3(0, 0, -0.6f);
            }

            // Three warning lamps: breaks in a row.
            for (int k = 0; k < 3; k++)
            {
                Vector3 at = new Vector3(-44f + k * 7f, 0f, -44f);
                Cylinder("Lamp base", Root, at + Vector3.up * 0.6f, 2.2f, 1.2f, steel);
                Material m = Make.Emissive(new Color(0.25f, 0.05f, 0.05f), Color.black, 0.8f);
                GameObject bulb = Shape(PrimitiveType.Sphere, "Lamp", Root, at + Vector3.up * 2.4f, Vector3.one * 3.2f, m);
                NoShadow(bulb);
                _lamps.Add(m);
            }

            _ball = BuildBall(Root, Vector3.zero);
            _putter = BuildPutter(Root, new Vector3(0, 0, -1.75f), Violet);
            _read = new Readout(this, Portrait ? new Vector2(0.6f, -0.78f) : new Vector2(0.64f, -0.74f), Violet) { Size = Portrait ? 0.8f : 0.6f };

            Sun(Root, new Vector3(60f, 30f, 0f), new Color(0.95f, 0.92f, 1f), 0.5f);
            Lamp(Root, new Vector3(0, 150f, -30f), Vector3.zero, new Color(0.95f, 0.9f, 1f), 200000f, 50f, 600f);
            Lamp(Root, new Vector3(26f, 60f, 10f), new Vector3(26f, 17f, 57f), new Color(0.9f, 0.8f, 1f), 120000f, 35f, 300f);
        }

        // ── Geometry ─────────────────────────────────────────────────────────

        static Vector3 Corner(int i, float r) => Axis(i % 6) * r;

        static Mesh Fan(float[] radii, float h)
        {
            // One-sided, facing up (both faces would cancel the normals and light it black).
            var v = new List<Vector3> { new Vector3(0, h, 0) };
            for (int i = 0; i < 6; i++) v.Add(Axis(i) * radii[i] + Vector3.up * h);
            var t = new List<int>();
            for (int i = 0; i < 6; i++) t.AddRange(new[] { 0, 1 + i, 1 + (i + 1) % 6 });
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.SetNormals(v.ConvertAll(_ => Vector3.up));
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A hexagon with a corner at each radius, its top at h and sides down to 0 (both faces drawn).</summary>
        static void Hex(Mesh mesh, float[] radii, float h, float side)
        {
            var v = new List<Vector3> { new Vector3(0, h, 0) };
            for (int i = 0; i < 6; i++) v.Add(Axis(i) * radii[i] + Vector3.up * h);
            var t = new List<int>();
            for (int i = 0; i < 6; i++) { int a = 1 + i, b = 1 + (i + 1) % 6; t.AddRange(new[] { 0, b, a, 0, a, b }); }
            if (side > 0f)
                for (int i = 0; i < 6; i++)
                {
                    int s = v.Count;
                    Vector3 a = Axis(i) * radii[i], b = Axis((i + 1) % 6) * radii[(i + 1) % 6];
                    v.Add(a + Vector3.up * h); v.Add(b + Vector3.up * h); v.Add(b); v.Add(a);
                    t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3, s, s + 2, s + 1, s, s + 3, s + 2 });
                }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.SetNormals(v.ConvertAll(_ => Vector3.up));   // unlit: any normal will do, but never a zero one
            mesh.RecalculateBounds();
        }

        static void Loop(LineRenderer line, float r, float y)
        {
            var pts = new Vector3[7];
            for (int i = 0; i <= 6; i++) pts[i] = Corner(i, r) + Vector3.up * y;
            Points(line, pts);
        }

        void SetRing(float radius, float shown)
        {
            _ring = radius;
            _wallShown = shown;
            float r = radius * R, h = WallH * Mathf.Max(0.01f, shown);
            for (int i = 0; i < 6; i++)
            {
                Vector3 a = Corner(i, r), b = Corner(i + 1, r);
                _walls[i].localPosition = (a + b) / 2f + Vector3.up * h / 2f;
                _walls[i].localRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, b - a));
                _walls[i].localScale = new Vector3(r, h, 0.5f);
            }
            var pts = new Vector3[7];
            for (int i = 0; i <= 6; i++) pts[i] = Corner(i, r) + Vector3.up * h;
            Points(_wallLine, pts);
            _wallRoot.gameObject.SetActive(shown > 0.005f);
        }

        void ShowStroke(float[] radii, float h, Color col)
        {
            Hex(_hex, radii, h, h);
            _hexMat.SetColor("_Color", new Color(col.r, col.g, col.b, 0.3f));
            var pts = new Vector3[7];
            for (int i = 0; i <= 6; i++) pts[i] = Axis(i % 6) * radii[i % 6] * R / R + Vector3.up * (h + 0.05f);
            Points(_hexLine, pts);
            _hexObj.SetActive(true);
        }

        void HideStroke() { _hexObj.SetActive(false); _hexLine.positionCount = 0; foreach (Transform c in _corners) c.gameObject.SetActive(false); }

        void Ghosts(int n)
        {
            while (_ghosts.Count > n) { Make.Kill(_ghosts[_ghosts.Count - 1].gameObject); _ghosts.RemoveAt(_ghosts.Count - 1); }
            while (_ghosts.Count < n)
            {
                int k = _ghosts.Count;
                LineRenderer g = Stroke(Root, "Ghost", new Color(1f, 1f, 1f, 0.45f), 0.4f);
                var pts = new Vector3[7];
                for (int i = 0; i <= 6; i++) pts[i] = Corner(i, R * (0.97f - 0.035f * k - 0.02f * Mathf.Sin(i * 1.7f + k * 2.3f))) + Vector3.up * (0.4f + k * 0.3f);
                Points(g, pts);
                _ghosts.Add(g);
            }
        }

        void Lamps(int n) { for (int k = 0; k < _lamps.Count; k++) _lamps[k].SetColor("_EmissionColor", k < n ? Break * 3.2f : Color.black); }
        void Combo(int n) { for (int k = 0; k < _combo.Count; k++) _combo[k].SetColor("_Color", k < n ? new Color(Violet.r, Violet.g, Violet.b, 1f) * 1.6f : new Color(Violet.r, Violet.g, Violet.b, 0.18f)); }
        void Rails() { for (int i = 0; i < 6; i++) _railMat[i].SetColor("_Color", new Color(Rail.r, Rail.g, Rail.b, 0.55f)); }

        void State(ShotView v)
        {
            bool calibrating = v.Flag("calibrating");
            Ghosts(calibrating ? v.Int("calib") : 0);
            SetRing(Radius(v.Num("band", 1f)), calibrating ? 0f : 1f);
            Combo(v.Int("combo"));
            Lamps(v.Int("breakRun"));
            Rails();
        }

        public override void Begin(ShotView start)
        {
            State(start);
            HideStroke();
            _read.Hide();
            _ball.localPosition = new Vector3(0, 0.84f, 0);
        }

        public override void Ready(ShotView v)
        {
            State(v);
            _ball.localPosition = new Vector3(0, 0.84f, 0);
        }

        public override void Tick()
        {
            _read.Tick(Eye(), Root);
            if (_hexObj.activeSelf) _hexMat.SetColor("_Color", _hexMat.GetColor("_Color") * new Color(1, 1, 1, 0) + new Color(0, 0, 0, 0.26f + 0.06f * Mathf.Sin(Time.time * 3f)));
        }

        // ── The putt ─────────────────────────────────────────────────────────

        public override IEnumerator Play(ShotView v)
        {
            StrokeData s = StrokeOf(v);
            float[] ratios = Floats(v, "ratios");
            bool calibrating = v.Flag("calibrating"), bandSet = !calibrating && ratios.Length == 0 && _wallShown < 0.5f;
            bool scored = !calibrating && !bandSet && ratios.Length == 6;
            HideStroke();
            Rails();

            yield return Swing(_putter, s, () => Sound("golf_iron", 0.3f, 1.25f));
            Vector3 ballFrom = _ball.localPosition, ballTo = ballFrom + Vector3.forward * 22f;

            if (scored)
            {
                bool lockdown = v.Flag("lock");
                float before = v.Num("bandBefore", 1f), after = v.Num("band", 1f);
                var target = new float[6];
                for (int i = 0; i < 6; i++) target[i] = Radius(ratios[i] * before) * R;
                Color col = lockdown ? Violet : Color.Lerp(Violet, Break, 0.35f);
                Sound("synth_whoosh", 0.4f, 1.1f);
                var r = new float[6];
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.7f)
                {
                    float u = Ease(t);
                    for (int i = 0; i < 6; i++) r[i] = target[i] * u;
                    ShowStroke(r, RiseH * u, col);
                    _ball.localPosition = Vector3.Lerp(ballFrom, ballTo, 1f - (1f - t) * (1f - t));
                    yield return null;
                }
                ShowStroke(target, RiseH, col);
                float ring = Radius(before) * R;
                for (int i = 0; i < 6; i++)
                {
                    bool broke = ratios[i] > 1f;
                    _corners[i].localPosition = Axis(i) * target[i] + Vector3.up * (RiseH + 0.4f);
                    _corners[i].gameObject.SetActive(true);
                    _cornerMat[i].SetColor("_BaseColor", broke ? Break : Color.white);
                    _cornerMat[i].SetColor("_EmissionColor", broke ? Break * 2.6f : Color.white * 1.2f);
                    if (broke) { _railMat[i].SetColor("_Color", new Color(Break.r, Break.g, Break.b, 1f) * 1.8f); Arcade.Sparks(At(Axis(i) * target[i] + Vector3.up * RiseH), 10); }
                }
                if (lockdown)
                {
                    // The wall slams a notch closer to the rim, the dial turns, another combo ring lights.
                    float from = _ring, to = Radius(after);
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.22f) { SetRing(Mathf.Lerp(from, to, t * t), 1f); yield return null; }
                    SetRing(to, 1f);
                    Sound("door_slam", 0.6f);
                    Arcade.Shake(0.25f);
                    for (int i = 0; i < 6; i++) Arcade.Sparks(At(Corner(i, to * R) + Vector3.up * WallH), 8);
                    Combo(v.Int("combo"));
                    _read.Show("BAND", Mathf.RoundToInt(after * 100f).ToString(), "%", "", Violet);
                    Quaternion d0 = _dial.localRotation, d1 = d0 * Quaternion.Euler(0, 0, -60f);
                    Sound("ratchet", 0.5f);
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f) { _dial.localRotation = Quaternion.Slerp(d0, d1, Ease(t)); yield return null; }
                    _dial.localRotation = d1;
                    if (v.Int("combo") >= 3) Arcade.Celebrate(18, At(new Vector3(26f, 17f, 55f)));
                }
                else
                {
                    // A break: the wall eases back, a warning lamp lights.
                    Sound("thud", 0.5f);
                    Arcade.Shake(0.15f);
                    Lamps(v.Int("breakRun"));
                    Combo(0);
                    _read.Show("BAND", Mathf.RoundToInt(after * 100f).ToString(), "%", "", ScoreAmber);
                    float from = _ring, to = Radius(after);
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f) { SetRing(Mathf.Lerp(from, to, Ease(t)), 1f); yield return null; }
                    SetRing(to, 1f);
                    if (v.Int("breakRun") >= v.Int("endAfter", 3)) { Sound("game_over", 0.4f); Arcade.Shake(0.4f); }
                }
                yield return new WaitForSeconds(0.4f);
            }
            else
            {
                // Calibration: a ghost hexagon near the rim; the third raises the band.
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f) { _ball.localPosition = Vector3.Lerp(ballFrom, ballTo, 1f - (1f - t) * (1f - t)); yield return null; }
                Sound("lab_beep", 0.5f, 1f + 0.1f * _ghosts.Count);
                Ghosts(_ghosts.Count + 1);
                yield return new WaitForSeconds(0.3f);
                if (bandSet)
                {
                    Sound("riser", 0.5f);
                    float to = Radius(v.Num("band", 1f));
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f) { SetRing(Mathf.Lerp(1f, to, Ease(t)), Ease(t)); yield return null; }
                    SetRing(to, 1f);
                    Ghosts(0);
                    Arcade.Sparks(At(Vector3.up * 2f), 30);
                    Sound("gate", 0.5f);
                    _read.Show("BAND", Mathf.RoundToInt(v.Num("band", 1f) * 100f).ToString(), "%", "", Violet);
                }
            }
            yield return Settle(_putter, 0.45f);
        }
    }
}
