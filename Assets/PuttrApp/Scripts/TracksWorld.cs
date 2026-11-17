// Tracks in 3D, broadcast style: one hole at a time built from the course's own shapes (as the St Andrews concept
// page does in the browser). The lie of every patch of ground comes from the course polygons, the ground texture is
// painted from it (mown stripes along the hole, speckled rough, sand, water), and the height is a sketch of links
// ground: long swells, humps by lie, dished bunkers. Drawn by its own camera into a texture the play screen shows.
//
// One unit is one yard. Course x stays x, course y becomes z.
using System.Collections;
using System.Collections.Generic;
using PuttrArcade;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrApp
{
    public sealed class TracksWorld : MonoBehaviour
    {
        public enum Cam { Broadcast, Follow, Plan, Green }

        public RenderTexture Texture { get; private set; }
        public Cam Mode { get; private set; } = Cam.Broadcast;
        public bool Flying => _flight != null;

        TracksCourse _course;
        TracksCourse.Hole _hole;
        Camera _camera;
        Transform _root, _holeRoot, _ball, _flag;
        LineRenderer _tracer;
        MeshRenderer _ground;
        float _x0, _z0, _cell;
        int _nx, _nz;
        float[] _h;
        byte[] _id;
        Vector3 _camPos, _camLook, _wantPos, _wantLook;
        Flight _flight;

        sealed class Flight { public Vector2 From, To; public float Apex, T, Dur; public bool Roll; public System.Action Done; public readonly List<Vector3> Pts = new List<Vector3>(); }

        // Lies by id, in paint order, with their ground colour and height (base, hump amplitude).
        internal static readonly (string key, byte id, Color col, float h, float amp)[] Paint =
        {
            ("sand", 1, PuttrArcade.Make.Hex(0xd8c893), -0.4f, 1.2f), ("scrub", 2, PuttrArcade.Make.Hex(0x3e4f22), 1.4f, 3.2f), ("wood", 3, PuttrArcade.Make.Hex(0x2f4421), 1.8f, 3.4f),
            ("rough", 4, PuttrArcade.Make.Hex(0x53742f), 0.2f, 1.8f), ("fairway", 5, PuttrArcade.Make.Hex(0x6faf45), 0f, 0.8f), ("tee", 6, PuttrArcade.Make.Hex(0x79bb4c), 0.35f, 0.1f),
            ("green", 7, PuttrArcade.Make.Hex(0x82c654), 0.1f, 0.25f), ("bunker", 8, PuttrArcade.Make.Hex(0xe8dcb2), -1.3f, 0.15f), ("water", 9, PuttrArcade.Make.Hex(0x3b6f98), -1.0f, 0f),
        };
        internal static readonly Color Base = PuttrArcade.Make.Hex(0x4e6e2c);

        public static TracksWorld Create()
        {
            var go = new GameObject("Tracks world");
            DontDestroyOnLoad(go);
            go.transform.position = new Vector3(0, -20000, 0);
            return go.AddComponent<TracksWorld>().Build();
        }

        TracksWorld Build()
        {
            _root = transform;
            Texture = new RenderTexture(1280, 1280, 24) { antiAliasing = 4 };
            _camera = new GameObject("Tracks camera").AddComponent<Camera>();
            _camera.transform.SetParent(_root, false);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = PuttrArcade.Make.Hex(0x9cc2de);
            _camera.fieldOfView = 50f;
            _camera.nearClipPlane = 0.5f;
            _camera.farClipPlane = 4000f;
            _camera.targetTexture = Texture;
            var sun = new GameObject("Tracks sun").AddComponent<Light>();
            sun.transform.SetParent(_root, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sun.transform.localRotation = Quaternion.Euler(42f, -35f, 0f);
            // A wide field round the hole so the horizon is not a cliff.
            var skirt = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Destroy(skirt.GetComponent<Collider>());
            skirt.transform.SetParent(_root, false);
            skirt.transform.localScale = new Vector3(600, 1, 600);
            skirt.transform.localPosition = new Vector3(0, -3f, 0);
            skirt.GetComponent<MeshRenderer>().sharedMaterial = PuttrArcade.Make.Lit(PuttrArcade.Make.Hex(0x47662a), 0.1f);
            _ball = Shape(PrimitiveType.Sphere, PuttrArcade.Make.Emissive(Color.white, new Color(0.4f, 0.4f, 0.4f)), 0.7f);
            _tracer = new GameObject("Tracer").AddComponent<LineRenderer>();
            _tracer.transform.SetParent(_root, false);
            _tracer.useWorldSpace = true;
            _tracer.widthMultiplier = 0.32f;
            _tracer.widthCurve = new AnimationCurve(new Keyframe(0, 0.15f), new Keyframe(0.35f, 1f), new Keyframe(1, 1f)); // thin where it leaves the camera
            _tracer.numCapVertices = 4;
            _tracer.sharedMaterial = PuttrArcade.Make.Glow(new Color(1f, 0.55f, 0.24f, 1f));
            _tracer.positionCount = 0;
            return this;
        }

        Transform Shape(PrimitiveType type, Material m, float size, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent ?? _root, false);
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        // ── The hole ──

        internal static float Hash(float x, float y) { float s = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f; return s - Mathf.Floor(s); }
        static float Noise(float x, float y)
        {
            float xi = Mathf.Floor(x), yi = Mathf.Floor(y), xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }
        internal static float Fbm(float x, float y) { float s = 0, a = 0.5f, f = 1; for (int i = 0; i < 4; i++) { s += a * Noise(x * f, y * f); f *= 2.03f; a *= 0.5f; } return s; }

        // Scanline fill of a polygon into an id grid (cell = yards per cell).
        internal static void Fill(byte[] grid, int w, int h, float x0, float z0, float cell, Vector2[] poly, byte id)
        {
            if (poly.Length < 3) return;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector2 p in poly) { minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            int r0 = Mathf.Max(0, Mathf.FloorToInt((minZ - z0) / cell)), r1 = Mathf.Min(h - 1, Mathf.CeilToInt((maxZ - z0) / cell));
            var xs = new List<float>(8);
            for (int r = r0; r <= r1; r++)
            {
                float z = z0 + (r + 0.5f) * cell;
                xs.Clear();
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    Vector2 a = poly[i], b = poly[j];
                    if ((a.y > z) != (b.y > z)) xs.Add(a.x + (z - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int c0 = Mathf.Max(0, Mathf.CeilToInt((xs[k] - x0) / cell - 0.5f)), c1 = Mathf.Min(w - 1, Mathf.FloorToInt((xs[k + 1] - x0) / cell - 0.5f));
                    for (int c = c0; c <= c1; c++) grid[r * w + c] = id;
                }
            }
        }

        public IEnumerator BuildHole(TracksCourse course, TracksCourse.Hole hole)
        {
            _course = course; _hole = hole;
            if (_holeRoot != null) Destroy(_holeRoot.gameObject);
            _holeRoot = new GameObject("Hole " + hole.N).transform;
            _holeRoot.SetParent(_root, false);
            // The hole's box: the line, tee and pin, plus a margin.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector2 p in hole.Line) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            const float margin = 110f;
            minX -= margin; maxX += margin; minZ -= margin; maxZ += margin;
            float w = maxX - minX, d = maxZ - minZ;
            // Lie ids at texture resolution (a third of a yard, at most 2048 a side).
            float tc = Mathf.Max(1f / 3f, Mathf.Max(w, d) / 2048f);
            int tw = Mathf.CeilToInt(w / tc), th = Mathf.CeilToInt(d / tc);
            var ids = new byte[tw * th];
            foreach (var p in Paint) foreach (Vector2[] poly in course.F(p.key)) Fill(ids, tw, th, minX, minZ, tc, poly, p.id);
            yield return null;
            // The ground's colour: the lie's own, with stripes and speckle.
            var tex = new Texture2D(tw, th, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            var px = new Color32[tw * th];
            Vector2 dir = (hole.Pin - hole.Tee).normalized;
            var cols = new Color[10]; cols[0] = Base; foreach (var p in Paint) cols[p.id] = p.col;
            for (int r = 0; r < th; r++)
            {
                for (int c = 0; c < tw; c++)
                {
                    int id = ids[r * tw + c];
                    float wx = minX + c * tc, wz = minZ + r * tc, k = 0;
                    if (id == 5 || id == 6 || id == 7) k = Mathf.FloorToInt((wx * dir.x + wz * dir.y) / (id == 7 ? 3f : 9f)) % 2 == 0 ? 0.035f : -0.025f;
                    else if (id == 0 || id == 4 || id == 2 || id == 3) k = (Hash(c, r) - 0.5f) * 0.13f + (Fbm(wx / 6, wz / 6) - 0.5f) * 0.12f;
                    else if (id == 1 || id == 8) k = (Hash(c, r) - 0.5f) * 0.05f;
                    Color col = cols[id];
                    px[r * tw + c] = new Color(col.r + k, col.g + k, col.b + k * 0.6f);
                }
                if (r % 256 == 0) yield return null;
            }
            // Roads and paths, stamped along their lines.
            void Stroke(string key, Color col, float width)
            {
                foreach (Vector2[] line in course.F(key))
                    for (int i = 1; i < line.Length; i++)
                    {
                        float len = Vector2.Distance(line[i - 1], line[i]);
                        for (float s = 0; s <= len; s += tc)
                        {
                            Vector2 q = Vector2.Lerp(line[i - 1], line[i], len > 0 ? s / len : 0);
                            int cx = Mathf.RoundToInt((q.x - minX) / tc), cz = Mathf.RoundToInt((q.y - minZ) / tc), rad = Mathf.CeilToInt(width / 2 / tc);
                            for (int dz = -rad; dz <= rad; dz++) for (int dx = -rad; dx <= rad; dx++)
                            { int x = cx + dx, z = cz + dz; if (x >= 0 && z >= 0 && x < tw && z < th && dx * dx + dz * dz <= rad * rad) px[z * tw + x] = col; }
                        }
                    }
            }
            Stroke("road", PuttrArcade.Make.Hex(0x8d8f8a), 6f);
            Stroke("cartpath", PuttrArcade.Make.Hex(0xb9b39c), 2.4f);
            tex.SetPixels32(px);
            tex.Apply(true);
            yield return null;

            // Heights on a 1.5 yd grid from the lie, smoothed so bunkers dish and greens roll.
            _cell = 1.5f; _x0 = minX; _z0 = minZ;
            _nx = Mathf.CeilToInt(w / _cell) + 1; _nz = Mathf.CeilToInt(d / _cell) + 1;
            _h = new float[_nx * _nz]; _id = new byte[_nx * _nz];
            var hByLie = new float[10]; var aByLie = new float[10]; hByLie[0] = 0.4f; aByLie[0] = 2.3f;
            foreach (var p in Paint) { hByLie[p.id] = p.h; aByLie[p.id] = p.amp; }
            for (int j = 0; j < _nz; j++) for (int i = 0; i < _nx; i++)
            {
                float wx = minX + i * _cell, wz = minZ + j * _cell;
                int c = Mathf.Clamp(Mathf.RoundToInt(i * _cell / tc), 0, tw - 1), r = Mathf.Clamp(Mathf.RoundToInt(j * _cell / tc), 0, th - 1);
                byte id = ids[r * tw + c];
                _id[j * _nx + i] = id;
                _h[j * _nx + i] = (Fbm(wx / 70, wz / 70) - 0.5f) * 7f + hByLie[id] + (Fbm(wx / 14 + 9, wz / 14) - 0.5f) * aByLie[id] * 2f;
            }
            for (int pass = 0; pass < 3; pass++)
            {
                float[] c = (float[])_h.Clone();
                for (int j = 1; j < _nz - 1; j++) for (int i = 1; i < _nx - 1; i++)
                {
                    int o = j * _nx + i;
                    if (_id[o] == 8 && pass > 1) continue;
                    _h[o] = (c[o] * 4 + c[o - 1] + c[o + 1] + c[o - _nx] + c[o + _nx]) / 8f;
                }
            }
            yield return null;

            var verts = new Vector3[_nx * _nz]; var uv = new Vector2[verts.Length];
            for (int j = 0; j < _nz; j++) for (int i = 0; i < _nx; i++)
            {
                int o = j * _nx + i;
                verts[o] = new Vector3(minX + i * _cell, _h[o], minZ + j * _cell);
                uv[o] = new Vector2(i * _cell / w, j * _cell / d);
            }
            var tris = new int[(_nx - 1) * (_nz - 1) * 6];
            int t = 0;
            for (int j = 0; j < _nz - 1; j++) for (int i = 0; i < _nx - 1; i++)
            {
                int a = j * _nx + i;
                tris[t++] = a; tris[t++] = a + _nx; tris[t++] = a + 1;
                tris[t++] = a + 1; tris[t++] = a + _nx; tris[t++] = a + _nx + 1;
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateNormals();
            var ground = new GameObject("Ground");
            ground.transform.SetParent(_holeRoot, false);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            _ground = ground.AddComponent<MeshRenderer>();
            _ground.sharedMaterial = PuttrArcade.Make.Lit(Color.white, 0.12f, 0f, tex);
            _ground.receiveShadows = true;
            yield return null;
            Dress();
        }

        public float HeightAt(Vector2 p)
        {
            if (_h == null) return 0;
            int i = Mathf.Clamp(Mathf.RoundToInt((p.x - _x0) / _cell), 0, _nx - 1), j = Mathf.Clamp(Mathf.RoundToInt((p.y - _z0) / _cell), 0, _nz - 1);
            return _h[j * _nx + i];
        }

        Vector3 W(Vector2 p, float lift = 0) => new Vector3(p.x, HeightAt(p) + lift, p.y);

        // Flag, tee markers, gorse and pines.
        void Dress()
        {
            Vector3 pin = W(_hole.Pin);
            _flag = new GameObject("Flag").transform;
            _flag.SetParent(_holeRoot, false);
            _flag.localPosition = pin;
            Transform pole = Shape(PrimitiveType.Cylinder, PuttrArcade.Make.Lit(new Color(0.95f, 0.95f, 0.95f)), 1f, _flag);
            pole.localScale = new Vector3(0.16f, 3.5f, 0.16f); pole.localPosition = new Vector3(0, 3.5f, 0);
            Transform cloth = Shape(PrimitiveType.Cube, PuttrArcade.Make.Lit(PuttrArcade.Make.Hex(0xff3b30), 0.2f), 1f, _flag);
            cloth.localScale = new Vector3(2.4f, 1.5f, 0.05f); cloth.localPosition = new Vector3(1.2f, 6.2f, 0); cloth.name = "Cloth";
            Transform cup = Shape(PrimitiveType.Cylinder, PuttrArcade.Make.Lit(new Color(0.02f, 0.02f, 0.02f), 0f), 1f, _flag);
            cup.localScale = new Vector3(0.7f, 0.02f, 0.7f); cup.localPosition = new Vector3(0, 0.03f, 0);
            Vector2 dir = (_hole.Line[1] - _hole.Tee).normalized, side = new Vector2(-dir.y, dir.x);
            Material white = PuttrArcade.Make.Lit(Color.white, 0.4f);
            foreach (int s in new[] { -1, 1 }) { Transform m = Shape(PrimitiveType.Sphere, white, 0.9f, _holeRoot); m.localPosition = W(_hole.Tee + side * 4 * s, 0.4f); }
            // Gorse and pines on the scrub and wood ground near the hole: one in three cells of every other row.
            Material gorse = PuttrArcade.Make.Lit(PuttrArcade.Make.Hex(0x324a1c), 0.05f), bloom = PuttrArcade.Make.Lit(PuttrArcade.Make.Hex(0x8d8d2a), 0.05f), pine = PuttrArcade.Make.Lit(PuttrArcade.Make.Hex(0x22361a), 0.05f);
            int placed = 0;
            for (int j = 0; j < _nz && placed < 700; j += 3) for (int i = 0; i < _nx && placed < 700; i += 3)
            {
                byte id = _id[j * _nx + i];
                if ((id != 2 && id != 3) || Hash(i, j) > 0.4f) continue;
                Vector2 p = new Vector2(_x0 + i * _cell + (Hash(j, i) - 0.5f) * 3, _z0 + j * _cell + (Hash(i + 3, j) - 0.5f) * 3);
                float s = id == 3 ? 2.6f + Hash(p.x, p.y) * 2 : 1.2f + Hash(p.x, p.y) * 1.4f;
                Transform b = Shape(PrimitiveType.Sphere, id == 3 ? pine : gorse, 1f, _holeRoot);
                b.localScale = new Vector3(s * 1.2f, s * (id == 3 ? 1.7f : 0.8f), s);
                b.localPosition = W(p, s * (id == 3 ? 0.8f : 0.35f));
                b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
                if (id == 2 && placed % 4 == 0) { Transform f = Shape(PrimitiveType.Sphere, bloom, 1f, _holeRoot); f.localScale = b.localScale * 0.55f; f.localPosition = b.localPosition + Vector3.up * s * 0.3f; }
                placed++;
            }
        }

        // ── The ball ──

        public void PlaceBall(Vector2 p) { _ball.localPosition = W(p, 0.55f); _tracer.positionCount = 0; }

        /// <summary>A shot in the air (a putt rolls): the ball flies from one spot to another with a tracer, then done().</summary>
        public void Fly(Vector2 from, Vector2 to, bool roll, System.Action done)
        {
            float dist = Vector2.Distance(from, to);
            _flight = new Flight { From = from, To = to, Roll = roll, Apex = roll ? 0 : Mathf.Max(8f, dist * 0.16f), Dur = roll ? 1.6f : 2.6f + Mathf.Min(1.6f, dist * 0.16f / 30f), Done = done };
            _tracer.positionCount = 0;
        }

        // ── Cameras ──

        public void SetCam(Cam mode, Vector2 ball, bool snap = false)
        {
            Mode = mode;
            Vector3 b = W(ball), p = W(_hole.Pin);
            Vector3 dir = new Vector3(p.x - b.x, 0, p.z - b.z).normalized;
            switch (mode)
            {
                case Cam.Broadcast: _wantPos = b - dir * 24 + Vector3.up * 9; _wantLook = b + dir * Mathf.Min(40, Vector3.Distance(b, p) * 0.5f); break; // the ball low in frame, the hole running away above it
                case Cam.Follow: _wantPos = b - dir * 9 + Vector3.up * 4; _wantLook = b + dir * 60; break;
                case Cam.Plan:
                    Vector3 mid = (W(_hole.Tee) + p) / 2; float span = Vector3.Distance(W(_hole.Tee), p);
                    _wantPos = mid + new Vector3(0.01f, span * 1.15f + 60, -0.01f); _wantLook = mid; break;
                default:
                    Vector3 side = new Vector3(-dir.z, 0, dir.x);
                    _wantPos = p - dir * 22 + side * 6 + Vector3.up * 6; _wantLook = p; break;
            }
            if (snap) { _camPos = _wantPos; _camLook = _wantLook; }
        }

        void LateUpdate()
        {
            if (_blimp) { BlimpTick(); return; }
            if (_hole == null) return;
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            if (_flight != null)
            {
                Flight f = _flight;
                f.T = Mathf.Min(1, f.T + dt / f.Dur);
                float e = f.Roll ? 1 - Mathf.Pow(1 - f.T, 2.2f) : f.T;
                Vector2 at = Vector2.Lerp(f.From, f.To, e);
                Vector3 pos = W(at, 0.55f + (f.Roll ? 0 : 4 * f.Apex * e * (1 - e)));
                _ball.localPosition = pos;
                if (!f.Roll)
                {
                    f.Pts.Add(_root.TransformPoint(pos));
                    _tracer.positionCount = f.Pts.Count;
                    _tracer.SetPositions(f.Pts.ToArray());
                    if (Mode == Cam.Broadcast || Mode == Cam.Follow)
                    {
                        Vector3 dir = new Vector3(f.To.x - f.From.x, 0, f.To.y - f.From.y).normalized;
                        _wantPos = pos - dir * (Mode == Cam.Broadcast ? 45 : 14) + Vector3.up * (Mode == Cam.Broadcast ? 18 : 6);
                        _wantLook = pos + dir * 25;
                    }
                }
                if (f.T >= 1) { _flight = null; f.Done?.Invoke(); }
                Perf.Wake(0.2f);
            }
            float k = 1 - Mathf.Pow(0.002f, dt);
            _camPos = Vector3.Lerp(_camPos, _wantPos, k);
            _camLook = Vector3.Lerp(_camLook, _wantLook, k);
            _camera.transform.localPosition = _camPos;
            _camera.transform.LookAt(_root.TransformPoint(_camLook));
            if (_flag != null) { Transform cloth = _flag.Find("Cloth"); if (cloth != null) cloth.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.realtimeSinceStartup * 2.5f) * 14, 0); }
            if ((_camPos - _wantPos).sqrMagnitude > 0.05f) Perf.Wake(0.2f);
        }

        public void Close() { if (Texture != null) Texture.Release(); if (this != null) Destroy(gameObject); }

        // What the whole-course build made that is not a scene object (its ground texture, mesh and material): freed with the world.
        readonly List<Object> _made = new List<Object>();

        void OnDestroy()
        {
            foreach (Object o in _made) if (o != null) Destroy(o);
            _made.Clear();
            if (Texture != null) { Texture.Release(); Destroy(Texture); }
        }

        /// <summary>Draw at this size (the screen's own shape, so a full-screen view is not a cropped square).</summary>
        public void Resize(int w, int h)
        {
            w = Mathf.Clamp(w, 64, 2048); h = Mathf.Clamp(h, 64, 2048);
            if (Texture != null && Texture.width == w && Texture.height == h) return;
            RenderTexture old = Texture;
            Texture = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            _camera.targetTexture = Texture;
            if (old != null) { old.Release(); Destroy(old); }
        }

        // ── The whole course at once, for the blimp over the setup screen ──

        /// <summary>Every hole on one coarser ground (the setup screen's fly-over): the lies painted as for a hole, heights on a
        /// grid of a few yards, a flag on every green, the woods dressed sparsely. Built in slices of a few milliseconds.</summary>
        public IEnumerator BuildCourse(TracksCourse course)
        {
            _course = course; _hole = null;
            if (course.Holes.Count == 0) yield break;
            float sliceAt = Time.realtimeSinceStartup;   // not Stopwatch: it runs fast in the Mac player on Apple silicon
            int sliceFrame = Time.frameCount;
            bool Slice() { float now = Time.realtimeSinceStartup; if (Time.frameCount != sliceFrame) { sliceFrame = Time.frameCount; sliceAt = now; } return now - sliceAt >= 0.006f; }
            if (_holeRoot != null) Destroy(_holeRoot.gameObject);
            _holeRoot = new GameObject("Course").transform;
            _holeRoot.SetParent(_root, false);
            _ball.gameObject.SetActive(false);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (TracksCourse.Hole hole in course.Holes) foreach (Vector2 p in hole.Line) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            const float margin = 260f;
            minX -= margin; maxX += margin; minZ -= margin; maxZ += margin;
            float w = maxX - minX, d = maxZ - minZ;
            float tc = Mathf.Max(1.2f, Mathf.Max(w, d) / 1400f);
            int tw = Mathf.CeilToInt(w / tc), th = Mathf.CeilToInt(d / tc);
            var ids = new byte[tw * th];
            foreach (var p in Paint) { foreach (Vector2[] poly in course.F(p.key)) Fill(ids, tw, th, minX, minZ, tc, poly, p.id); if (Slice()) yield return null; }
            var tex = new Texture2D(tw, th, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            var px = new Color32[tw * th];
            var cols = new Color[10]; cols[0] = Base; foreach (var p in Paint) cols[p.id] = p.col;
            float stripe = 9f / tc;
            float[] tile = TracksArt.Tile();
            for (int r = 0; r < th; r++)
            {
                for (int c = 0; c < tw; c++)
                {
                    int id = ids[r * tw + c];
                    float k = 0;
                    if (id == 5 || id == 6 || id == 7) k = 0.005f + 0.03f * Mathf.Clamp((Mathf.PingPong((c + r * 0.55f) / (id == 7 ? stripe / 3 : stripe), 1f) - 0.5f) * 5f, -1f, 1f);   // soft-edged, so the stripes do not stair-step up close
                    else if (id == 0 || id == 4 || id == 2 || id == 3) k = (TracksArt.IHash(c, r) - 0.5f) * 0.1f + (tile[(c & 255) + ((r & 255) << 8)] - 0.5f) * 0.16f;
                    else if (id == 1 || id == 8) k = (TracksArt.IHash(c, r) - 0.5f) * 0.05f;
                    Color col = cols[id];
                    px[r * tw + c] = new Color(col.r + k, col.g + k, col.b + k * 0.6f);
                }
                if (Slice()) yield return null;
            }
            void Stroke(string key, Color col, float width)
            {
                foreach (Vector2[] line in course.F(key))
                    for (int i = 1; i < line.Length; i++)
                    {
                        float len = Vector2.Distance(line[i - 1], line[i]);
                        for (float s = 0; s <= len; s += tc)
                        {
                            Vector2 q = Vector2.Lerp(line[i - 1], line[i], len > 0 ? s / len : 0);
                            int cx = Mathf.RoundToInt((q.x - minX) / tc), cz = Mathf.RoundToInt((q.y - minZ) / tc), rad = Mathf.Max(1, Mathf.CeilToInt(width / 2 / tc));
                            for (int dz = -rad; dz <= rad; dz++) for (int dx = -rad; dx <= rad; dx++)
                            { int x = cx + dx, z = cz + dz; if (x >= 0 && z >= 0 && x < tw && z < th && dx * dx + dz * dz <= rad * rad) px[z * tw + x] = col; }
                        }
                    }
            }
            Stroke("road", PuttrArcade.Make.Hex(0x707568), 4f);   // quieter than on a hole: from the blimp the town's roads would take over
            Stroke("cartpath", PuttrArcade.Make.Hex(0xa49f88), 2f);
            tex.SetPixels32(px);
            tex.Apply(true);
            yield return null;

            // Heights on a grid of a few yards; the edge of the ground eases down to the field round it.
            _cell = Mathf.Max(2.5f, Mathf.Max(w, d) / 320f); _x0 = minX; _z0 = minZ;
            _nx = Mathf.CeilToInt(w / _cell) + 1; _nz = Mathf.CeilToInt(d / _cell) + 1;
            _h = new float[_nx * _nz]; _id = new byte[_nx * _nz];
            var hByLie = new float[10]; var aByLie = new float[10]; hByLie[0] = 0.4f; aByLie[0] = 2.3f;
            foreach (var p in Paint) { hByLie[p.id] = p.h; aByLie[p.id] = p.amp; }
            for (int j = 0; j < _nz; j++)
            {
                for (int i = 0; i < _nx; i++)
                {
                    float wx = minX + i * _cell, wz = minZ + j * _cell;
                    int c = Mathf.Clamp(Mathf.RoundToInt(i * _cell / tc), 0, tw - 1), r = Mathf.Clamp(Mathf.RoundToInt(j * _cell / tc), 0, th - 1);
                    byte id = ids[r * tw + c];
                    _id[j * _nx + i] = id;
                    float hgt = (TracksArt.Fbm(wx / 70, wz / 70) - 0.5f) * 7f + hByLie[id] + (TracksArt.Fbm(wx / 14 + 9, wz / 14) - 0.5f) * aByLie[id] * 2f;
                    float edge = Mathf.Min(Mathf.Min(wx - minX, maxX - wx), Mathf.Min(wz - minZ, maxZ - wz));
                    _h[j * _nx + i] = Mathf.Lerp(-3.2f, hgt, Mathf.SmoothStep(0, 1, edge / 80f));
                }
                if (Slice()) yield return null;
            }
            for (int pass = 0; pass < 2; pass++)
            {
                float[] c = (float[])_h.Clone();
                for (int j = 1; j < _nz - 1; j++) for (int i = 1; i < _nx - 1; i++)
                {
                    int o = j * _nx + i;
                    _h[o] = (c[o] * 4 + c[o - 1] + c[o + 1] + c[o - _nx] + c[o + _nx]) / 8f;
                }
                yield return null;
            }
            var verts = new Vector3[_nx * _nz]; var uv = new Vector2[verts.Length];
            for (int j = 0; j < _nz; j++) for (int i = 0; i < _nx; i++)
            {
                int o = j * _nx + i;
                verts[o] = new Vector3(minX + i * _cell, _h[o], minZ + j * _cell);
                uv[o] = new Vector2(i * _cell / w, j * _cell / d);
            }
            var tris = new int[(_nx - 1) * (_nz - 1) * 6];
            int t = 0;
            for (int j = 0; j < _nz - 1; j++) for (int i = 0; i < _nx - 1; i++)
            {
                int a = j * _nx + i;
                tris[t++] = a; tris[t++] = a + _nx; tris[t++] = a + 1;
                tris[t++] = a + 1; tris[t++] = a + _nx; tris[t++] = a + _nx + 1;
            }
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateNormals();
            var ground = new GameObject("Ground");
            ground.transform.SetParent(_holeRoot, false);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            _ground = ground.AddComponent<MeshRenderer>();
            _ground.sharedMaterial = PuttrArcade.Make.Lit(Color.white, 0.12f, 0f, tex);
            _ground.receiveShadows = true;
            _made.Add(tex); _made.Add(mesh); _made.Add(_ground.sharedMaterial);
            yield return null;

            // A flag on every green (big enough to read from the blimp) and the woods, sparsely.
            Material white = PuttrArcade.Make.Lit(new Color(0.95f, 0.95f, 0.95f)), red = PuttrArcade.Make.Lit(PuttrArcade.Make.Hex(0xff3b30), 0.2f);
            foreach (TracksCourse.Hole hole in course.Holes)
            {
                Transform flag = new GameObject("Flag " + hole.N).transform;
                flag.SetParent(_holeRoot, false);
                flag.localPosition = W(hole.Pin);
                Transform pole = Shape(PrimitiveType.Cylinder, white, 1f, flag);
                pole.localScale = new Vector3(0.45f, 6f, 0.45f); pole.localPosition = new Vector3(0, 6f, 0);
                Transform cloth = Shape(PrimitiveType.Cube, red, 1f, flag);
                cloth.localScale = new Vector3(4.5f, 2.8f, 0.15f); cloth.localPosition = new Vector3(2.25f, 10.6f, 0);
            }
            Material pine = PuttrArcade.Make.Lit(PuttrArcade.Make.Hex(0x22361a), 0.05f), gorse = PuttrArcade.Make.Lit(PuttrArcade.Make.Hex(0x324a1c), 0.05f);
            int placed = 0, every = Mathf.Max(1, Mathf.RoundToInt(5f / _cell));
            for (int j = 0; j < _nz && placed < 1100; j += every)
            {
                for (int i = 0; i < _nx && placed < 1100; i += every)
                {
                    byte id = _id[j * _nx + i];
                    if ((id != 2 && id != 3) || TracksArt.IHash(i, j) > 0.45f) continue;
                    Vector2 p = new Vector2(_x0 + i * _cell + (TracksArt.IHash(j, i) - 0.5f) * _cell * every, _z0 + j * _cell + (TracksArt.IHash(i + 3, j) - 0.5f) * _cell * every);
                    float s = id == 3 ? 3.2f + TracksArt.IHash((int)(p.x * 7), (int)(p.y * 7)) * 2.4f : 1.8f + TracksArt.IHash((int)(p.x * 7), (int)(p.y * 7)) * 1.6f;
                    Transform b = Shape(PrimitiveType.Sphere, id == 3 ? pine : gorse, 1f, _holeRoot);
                    b.localScale = new Vector3(s * 1.2f, s * (id == 3 ? 1.7f : 0.8f), s);
                    b.localPosition = W(p, s * (id == 3 ? 0.8f : 0.35f));
                    placed++;
                }
                if (Slice()) yield return null;
            }
        }

        // ── The blimp: high and slow down the holes in order, gently banking through the turns ──

        bool _blimp;
        readonly List<Vector2> _route = new List<Vector2>();
        readonly List<float> _routeCum = new List<float>();
        float _u, _roll;
        Vector3 _heading;

        /// <summary>How far round the course the blimp is (0..1), to pick the flight up there later.</summary>
        public float BlimpShare => _blimp && _routeCum.Count > 0 && _routeCum[_routeCum.Count - 1] > 0 ? Mathf.Repeat(_u, _routeCum[_routeCum.Count - 1]) / _routeCum[_routeCum.Count - 1] : 0f;

        /// <summary>Start the fly-over (after BuildCourse): tee to green, hole after hole, round and round.</summary>
        public void Blimp(float startShare = 0f)
        {
            if (_course == null || _course.Holes.Count == 0) return;
            _route.Clear(); _routeCum.Clear();
            foreach (TracksCourse.Hole h in _course.Holes) { _route.Add(h.Tee); _route.Add(Vector2.Lerp(h.Tee, h.Pin, 0.5f)); _route.Add(h.Pin); }
            _routeCum.Add(0);
            for (int i = 1; i < _route.Count; i++) _routeCum.Add(_routeCum[i - 1] + Vector2.Distance(_route[i - 1], _route[i]));
            // Round again from the last green to the first tee.
            _route.Add(_route[0]); _routeCum.Add(_routeCum[_routeCum.Count - 1] + Vector2.Distance(_route[_route.Count - 2], _route[0]));
            _u = _routeCum[_routeCum.Count - 1] * Mathf.Repeat(startShare, 1f);
            _blimp = true;
            Vector2 a = Along(_u), b = Along(_u + 160f);
            _heading = new Vector3(b.x - a.x, 0, b.y - a.y).normalized;
            if (_heading.sqrMagnitude < 0.01f) _heading = Vector3.forward;
            Aim(a, b, out _camPos, out _camLook);
            _roll = 0;
        }

        Vector2 Along(float u)
        {
            float total = _routeCum[_routeCum.Count - 1];
            if (total <= 0) return _route[0];
            u = Mathf.Repeat(u, total);
            for (int i = 1; i < _route.Count; i++)
                if (u <= _routeCum[i]) { float seg = Mathf.Max(1e-4f, _routeCum[i] - _routeCum[i - 1]); return Vector2.Lerp(_route[i - 1], _route[i], (u - _routeCum[i - 1]) / seg); }
            return _route[0];
        }

        void Aim(Vector2 a, Vector2 b, out Vector3 pos, out Vector3 look)
        {
            Vector2 mid = Vector2.Lerp(a, b, 0.5f);
            Vector3 focus = new Vector3(mid.x, HeightAt(mid), mid.y);
            float bob = Mathf.Sin(Time.realtimeSinceStartup * 0.21f) * 6f;
            pos = focus - _heading * 170f + Vector3.up * (155f + bob);
            look = focus + _heading * 40f;
        }

        void BlimpTick()
        {
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            _u += dt * 15f;   // yards a second: a blimp, not a drone
            Vector2 a = Along(_u), b = Along(_u + 160f);
            Vector3 want = new Vector3(b.x - a.x, 0, b.y - a.y);
            if (want.sqrMagnitude > 0.01f)
            {
                Vector3 was = _heading;
                _heading = Vector3.Slerp(_heading, want.normalized, 1 - Mathf.Pow(0.55f, dt)).normalized;
                float yawRate = Vector3.SignedAngle(was, _heading, Vector3.up) / Mathf.Max(1e-4f, dt);
                _roll = Mathf.Lerp(_roll, Mathf.Clamp(-yawRate * 0.6f, -7f, 7f), 1 - Mathf.Pow(0.4f, dt));
            }
            Aim(a, b, out _wantPos, out _wantLook);
            float k = 1 - Mathf.Pow(0.3f, dt);
            _camPos = Vector3.Lerp(_camPos, _wantPos, k);
            _camLook = Vector3.Lerp(_camLook, _wantLook, k);
            _camera.transform.localPosition = _camPos;
            _camera.transform.LookAt(_root.TransformPoint(_camLook));
            _camera.transform.Rotate(0f, 0f, _roll, Space.Self);
            Perf.Wake(0.2f);
        }

        // ── Sound: the Soundly library slots (SoundDrop), played flat ──────────
        AudioSource[] _voices;
        AudioSource _bedSource;
        int _nextVoice;
        static readonly System.Collections.Generic.Dictionary<string, AudioClip[]> Takes = new System.Collections.Generic.Dictionary<string, AudioClip[]>();

        static AudioClip[] TakesOf(string slot)
        {
            if (Takes.TryGetValue(slot, out AudioClip[] t)) return t;
            var found = new System.Collections.Generic.List<AudioClip>();
            for (int i = 1; i <= 8; i++) { AudioClip c = Resources.Load<AudioClip>("PuttrArcade/Audio/Library/" + slot + "_" + i); if (c == null) break; found.Add(c); }
            return Takes[slot] = found.ToArray();
        }

        void EnsureSound()
        {
            if (_voices != null) return;
            // The arcade's camera carries the app's only listener, and the arcade is switched off here.
            if (Object.FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            _voices = new AudioSource[6];
            for (int i = 0; i < _voices.Length; i++) { _voices[i] = gameObject.AddComponent<AudioSource>(); _voices[i].playOnAwake = false; _voices[i].spatialBlend = 0f; }
            _bedSource = gameObject.AddComponent<AudioSource>();
            _bedSource.loop = true; _bedSource.playOnAwake = false; _bedSource.spatialBlend = 0f;
        }

        /// <summary>One take of a library sound.</summary>
        public void Sfx(string slot, float volume = 0.9f, float pitch = 1f)
        {
            AudioClip[] t = TakesOf(slot);
            if (t.Length == 0) return;
            EnsureSound();
            AudioSource v = _voices[_nextVoice++ % _voices.Length];
            v.pitch = pitch;
            v.PlayOneShot(t[Random.Range(0, t.Length)], volume);
        }

        /// <summary>The course under everything: a *_loop slot.</summary>
        public void Bed(string slot, float volume = 0.3f)
        {
            AudioClip[] t = TakesOf(slot);
            if (t.Length == 0) return;
            EnsureSound();
            if (_bedSource.clip != t[0]) { _bedSource.clip = t[0]; _bedSource.Play(); }
            _bedSource.volume = volume;
        }
    }
}
