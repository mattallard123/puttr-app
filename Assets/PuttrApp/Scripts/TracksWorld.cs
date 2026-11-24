// Tracks in 3D, broadcast style: one hole at a time built from the course's own shapes (as the St Andrews concept
// page does in the browser). The lie of every patch of ground comes from the course polygons, the ground texture is
// painted from it (mown stripes along the hole, speckled rough, sand, water), and the height is a sketch of links
// ground: long swells, humps by lie, dished bunkers. Drawn by its own camera into a texture the play screen shows.
//
// Two looks. The realistic one (TracksReal: scanned turf and sand over the lies' colours, a photographed sky, haze,
// cut-out trees) where the device can carry it; the painted one (flat colours, spheres for trees, a plain blue sky)
// everywhere else. The holes, heights, cameras and ball are the same in both.
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
        bool _real;   // set in Build, not here: deciding it asks the device about itself, which Unity forbids while a component is being constructed
        const float BallSize = 0.7f, BallLift = 0.38f;   // yards across; its centre above the ground (resting on it, with a little to spare on a slope)
        // What a hole's build made that is not a scene object (its ground maps, mesh, material, trees): freed when the next hole is built.
        readonly List<Object> _holeMade = new List<Object>();

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
            _real = TracksReal.On;
            PuttrArcade.Arcade.SettlePipeline();
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
            skirt.GetComponent<MeshRenderer>().sharedMaterial = _real ? TracksReal.Field() : PuttrArcade.Make.Lit(PuttrArcade.Make.Hex(0x47662a), 0.1f);
            if (_real) TracksReal.Dress(_camera, sun);
            // The ball: a dimpled golf ball with its alignment line, lit by the sun and sky and throwing a shadow, sitting on
            // the turf (it was a plain white sphere that gave its own light and hung a little above the ground). It stays
            // far bigger than life, as before: a real ball would be a speck from these cameras.
            if (_ballTex == null) PuttrArcade.Make.Ball(out _ballTex, out _ballNormal);
            _ball = Shape(PrimitiveType.Sphere, PuttrArcade.Make.Lit(Color.white, 0.5f, 0f, _ballTex, _ballNormal, 0.9f), BallSize);
            _ball.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            _ball.localRotation = Quaternion.Euler(18f, 40f, 0f);   // the line off to one side, not dead on to the camera
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
            EndCloseUp();
            HideOthers();
            if (_holeRoot != null) Destroy(_holeRoot.gameObject);
            foreach (Object o in _holeMade) if (o != null) Destroy(o);
            _holeMade.Clear();
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
            if (_real) cols = TracksReal.Colours();
            // Texel to texel speckle is the painted look's grain; the realistic look has the scans for that.
            float speckle = _real ? 0f : 1f;
            for (int r = 0; r < th; r++)
            {
                for (int c = 0; c < tw; c++)
                {
                    int id = ids[r * tw + c];
                    float wx = minX + c * tc, wz = minZ + r * tc, k = 0;
                    if (id == 5 || id == 6 || id == 7) k = Mathf.FloorToInt((wx * dir.x + wz * dir.y) / (id == 7 ? 3f : 9f)) % 2 == 0 ? 0.035f : -0.025f;
                    else if (id == 0 || id == 4 || id == 2 || id == 3) k = (Hash(c, r) - 0.5f) * 0.13f * speckle + (Fbm(wx / 6, wz / 6) - 0.5f) * 0.12f;
                    else if (id == 1 || id == 8) k = (Hash(c, r) - 0.5f) * 0.05f * speckle;
                    Color col = cols[id];
                    px[r * tw + c] = new Color(col.r + k, col.g + k, col.b + k * 0.6f);
                }
                if (r % 256 == 0) yield return null;
            }
            // Roads and paths, stamped along their lines (and marked, so the realistic ground does not grow grass on them).
            byte[] road = _real ? new byte[tw * th] : null;
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
                            { int x = cx + dx, z = cz + dz; if (x >= 0 && z >= 0 && x < tw && z < th && dx * dx + dz * dz <= rad * rad) { px[z * tw + x] = col; if (road != null) road[z * tw + x] = 1; } }
                        }
                    }
            }
            Stroke("road", PuttrArcade.Make.Hex(_real ? 0x77786f : 0x8d8f8a), 6f);
            Stroke("cartpath", PuttrArcade.Make.Hex(_real ? 0xa8a28c : 0xb9b39c), 2.4f);
            tex.SetPixels32(px);
            tex.Apply(true);
            _holeMade.Add(tex);
            yield return null;
            Texture2D control = _real ? TracksReal.Control(ids, tw, th, road) : null;
            if (control != null) { _holeMade.Add(control); yield return null; }

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
            _ground.sharedMaterial = _real ? TracksReal.Ground(tex, control) : PuttrArcade.Make.Lit(Color.white, 0.12f, 0f, tex);
            _ground.receiveShadows = true;
            if (_real) _ground.shadowCastingMode = ShadowCastingMode.Off;   // gentle ground: nothing it would shade but itself, badly
            _holeMade.Add(mesh); _holeMade.Add(_ground.sharedMaterial);
            yield return null;
            Dress();
            // The putting set for the close-ups, made once while the first hole loads (not when the first putt goes).
            if (_set == null) { yield return null; BuildSet(); }
        }

        public float HeightAt(Vector2 p)
        {
            if (_h == null) return 0;
            int i = Mathf.Clamp(Mathf.RoundToInt((p.x - _x0) / _cell), 0, _nx - 1), j = Mathf.Clamp(Mathf.RoundToInt((p.y - _z0) / _cell), 0, _nz - 1);
            return _h[j * _nx + i];
        }

        Vector3 W(Vector2 p, float lift = 0) => new Vector3(p.x, HeightAt(p) + lift, p.y);

        // Flag, gorse and pines (no tee markers: Matt, 2026-11-23).
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
            if (_real) { Plant(900, 1300, 7f, 3f, _holeMade); return; }
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

        // The realistic look's trees and bushes: a tree about every `treeGap` yards of woodland and a bush every `bushGap`
        // of scrub, each a little off its grid, thinned evenly over the whole ground if that would pass the caps (so a
        // big wood is thinner all over, not planted from one corner until the trees run out). Woodland grows in groves:
        // conifers together, broadleaves together, with the odd stranger.
        void Plant(int maxTrees, int maxBushes, float treeGap, float bushGap, List<Object> made)
        {
            int ts = Mathf.Max(1, Mathf.RoundToInt(treeGap / _cell)), bs = Mathf.Max(1, Mathf.RoundToInt(bushGap / _cell));
            int woods = 0, scrubs = 0;
            for (int j = 0; j < _nz; j += ts) for (int i = 0; i < _nx; i += ts) if (_id[j * _nx + i] == 3) woods++;
            for (int j = 0; j < _nz; j += bs) for (int i = 0; i < _nx; i += bs) if (_id[j * _nx + i] == 2) scrubs++;
            float keepTree = Mathf.Min(1f, maxTrees / Mathf.Max(1f, woods)), keepBush = Mathf.Min(1f, maxBushes / Mathf.Max(1f, scrubs));
            var grove = new TracksReal.Grove();
            void Row(byte lie, int step, float keep)
            {
                float spread = step * _cell;
                for (int j = 0; j < _nz; j += step) for (int i = 0; i < _nx; i += step)
                {
                    if (_id[j * _nx + i] != lie || TracksArt.IHash(i * 3 + 1, j * 5 + lie) > keep) continue;
                    Vector2 p = new Vector2(_x0 + i * _cell + (TracksArt.IHash(j, i) - 0.5f) * spread, _z0 + j * _cell + (TracksArt.IHash(i + 3, j) - 0.5f) * spread);
                    float a = TracksArt.IHash(i + 11, j + 7), b = TracksArt.IHash(i + 5, j + 13), c = TracksArt.IHash(i + 17, j + 3);
                    float pick = a;
                    if (lie == 3)
                    {
                        bool conifer = TracksArt.Fbm(p.x / 80f + 31f, p.y / 80f) > 0.5f;
                        if (b > 0.85f) conifer = !conifer;
                        pick = (conifer ? 0f : 0.5f) + a * 0.5f;
                    }
                    grove.Add(W(p), lie == 2, pick, b, 0.82f + 0.3f * c, c > 0.5f);
                }
            }
            Row(3, ts, keepTree);
            Row(2, bs, keepBush);
            grove.Finish(_holeRoot, 1f, made);
        }

        // ── The ball ──

        public void PlaceBall(Vector2 p) { EndCloseUp(); _ball.gameObject.SetActive(true); _ball.localPosition = W(p, BallLift); _tracer.positionCount = 0; }

        readonly List<Transform> _others = new List<Transform>();
        readonly Dictionary<Color, Material> _otherMats = new Dictionary<Color, Material>();

        /// <summary>
        /// The other Players' balls where they lie, each in that Player's colour (Tracks.js HoleView `others`), while one
        /// Player plays. Null or empty hides them all. They are hidden for a close-up putt, and on a new hole until the
        /// screen places them again.
        /// </summary>
        public void ShowOthers(IList<(Vector2 at, Color colour)> balls)
        {
            int n = balls != null && _hole != null && _close == null ? balls.Count : 0;
            for (int i = 0; i < Mathf.Max(n, _others.Count); i++)
            {
                if (i >= n) { _others[i].gameObject.SetActive(false); continue; }
                Color c = balls[i].colour;
                if (!_otherMats.TryGetValue(c, out Material m))
                {
                    // Solid and a little self-lit, so a coloured ball reads at a distance like the white one.
                    m = PuttrArcade.Make.Emissive(c, c * 0.45f);
                    _otherMats[c] = m;
                    _made.Add(m);
                }
                if (i >= _others.Count) _others.Add(Shape(PrimitiveType.Sphere, m, 0.7f));
                Transform t = _others[i];
                t.GetComponent<MeshRenderer>().sharedMaterial = m;
                t.localPosition = W(balls[i].at, BallLift);
                t.gameObject.SetActive(true);
            }
        }

        void HideOthers() { foreach (Transform t in _others) if (t != null) t.gameObject.SetActive(false); }

        /// <summary>
        /// A holed putt that played at yard scale (a lag): the ball has reached the hole on the course, so the view cuts to the
        /// putting set for its last inches, where it topples in and lies in the cup under the same look down as a short putt.
        /// (It used to be hidden over the course's flat disc of a hole, so there was nothing to see.) The caller plays the
        /// cup's note itself unless cupSound is set.
        /// </summary>
        public void SinkBall(bool cupSound = false)
        {
            _ball.gameObject.SetActive(false);
            if (_close != null) return;
            BuildSet();
            const float d = 30f;
            var pb = new Playback { D = d, Made = true, Run = CupSim.Holed(d, 0f, Mathf.Sqrt(2f * CupSim.Green * 5f)) };
            if (pb.Run.DropAt < 0) return;   // no run that drops (it cannot happen from dead centre): the ball is simply gone, as before
            // Picked up a fifth of a second before it tips over the lip: the lag's own roll was shown on the course.
            pb.T = Mathf.Max(0f, pb.Run.DropAt * CupSim.Step - 0.2f);
            AddSounds(pb, cupSound);
            int start = Mathf.FloorToInt(pb.T / CupSim.Step);
            while (pb.NextSound < pb.Sounds.Count && pb.Sounds[pb.NextSound].i < start) pb.NextSound++;
            BeginCloseUp(pb);
        }

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

        /// <summary>Holds the world still and stops drawing it (the setup's flyover while the round loads over a copy of its
        /// last frame), so the round's own world gets the frame time. False carries on where it stopped.</summary>
        public void Pause(bool paused)
        {
            _paused = paused;
            if (_camera != null) _camera.enabled = !paused;
        }
        bool _paused;

        void LateUpdate()
        {
            if (_paused) return;
            if (_blimp) { BlimpTick(); return; }
            if (_close != null) { CloseTick(); return; }
            if (_hole == null) return;
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            if (_flight != null)
            {
                Flight f = _flight;
                f.T = Mathf.Min(1, f.T + dt / f.Dur);
                float e = f.Roll ? 1 - Mathf.Pow(1 - f.T, 2.2f) : f.T;
                Vector2 at = Vector2.Lerp(f.From, f.To, e);
                Vector3 pos = W(at, BallLift + (f.Roll ? 0 : 4 * f.Apex * e * (1 - e)));
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
            if (_close != null && _shadowWas > 0f) ShadowDistance(_shadowWas);   // left in the middle of a close-up
            _close = null;
            foreach (Object o in _made) if (o != null) Destroy(o);
            _made.Clear();
            foreach (Object o in _holeMade) if (o != null) Destroy(o);
            _holeMade.Clear();
            TracksReal.Forget(_camera);
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
            foreach (Object o in _holeMade) if (o != null) Destroy(o);
            _holeMade.Clear();
            _holeRoot = new GameObject("Course").transform;
            _holeRoot.SetParent(_root, false);
            _ball.gameObject.SetActive(false);
            HideOthers();
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
            if (_real) cols = TracksReal.Colours();
            float speckle = _real ? 0f : 1f;
            float stripe = 9f / tc;
            float[] tile = TracksArt.Tile();
            byte[] road = _real ? new byte[tw * th] : null;
            for (int r = 0; r < th; r++)
            {
                for (int c = 0; c < tw; c++)
                {
                    int id = ids[r * tw + c];
                    float k = 0;
                    if (id == 5 || id == 6 || id == 7) k = 0.005f + 0.03f * Mathf.Clamp((Mathf.PingPong((c + r * 0.55f) / (id == 7 ? stripe / 3 : stripe), 1f) - 0.5f) * 5f, -1f, 1f);   // soft-edged, so the stripes do not stair-step up close
                    else if (id == 0 || id == 4 || id == 2 || id == 3) k = (TracksArt.IHash(c, r) - 0.5f) * 0.1f * speckle + (tile[(c & 255) + ((r & 255) << 8)] - 0.5f) * 0.16f;
                    else if (id == 1 || id == 8) k = (TracksArt.IHash(c, r) - 0.5f) * 0.05f * speckle;
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
                            { int x = cx + dx, z = cz + dz; if (x >= 0 && z >= 0 && x < tw && z < th && dx * dx + dz * dz <= rad * rad) { px[z * tw + x] = col; if (road != null) road[z * tw + x] = 1; } }
                        }
                    }
            }
            Stroke("road", PuttrArcade.Make.Hex(0x707568), 4f);   // quieter than on a hole: from the blimp the town's roads would take over
            Stroke("cartpath", PuttrArcade.Make.Hex(0xa49f88), 2f);
            tex.SetPixels32(px);
            tex.Apply(true);
            yield return null;
            Texture2D control = _real ? TracksReal.Control(ids, tw, th, road) : null;
            if (control != null) { _made.Add(control); yield return null; }

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
            _ground.sharedMaterial = _real ? TracksReal.Ground(tex, control, 1f, 1.6f) : PuttrArcade.Make.Lit(Color.white, 0.12f, 0f, tex);
            _ground.receiveShadows = true;
            if (_real) _ground.shadowCastingMode = ShadowCastingMode.Off;
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
            if (_real) { Plant(1600, 900, Mathf.Max(8f, _cell * 2), Mathf.Max(5f, _cell), _made); yield break; }
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

        // ── A putt up close, at true scale ──
        //
        // From 11 ft and in, a putt on the green leaves the yard-scale course for a putting set built to scale in inches
        // round a regulation hole: the 4.25 in hole cut clean in close-mown green, an inch of soil, then the white liner
        // down to a grey floor 4 in below the green with the ferrule's socket in it, and a 1.68 in ball. The cup is lit
        // from inside (a small lamp under the rim, and a liner, floor and holed ball that give a little light of their
        // own), so a ball lying in it is plain to see from above and not a dark shape in a black hole. The flagstick is
        // out, lying on the green past the hole. The roll and what happens at the hole are simulated ahead (after
        // PuttrArcade's CupPhysics, but this cup has a floor and keeps the ball) and played back: the run is always one
        // that agrees with TracksEngine's verdict (made is in, a lip-out rides the rim and stays out, a miss stops where
        // the engine left it). The camera follows low behind the ball, then settles on the hole and, for a holed putt,
        // comes down over the cup until its rim fills most of the view with the ball lying in it.
        // Units: inches; the hole is at the set's origin, the putt rolls along +z, +x is the Player's right.

        public sealed class GreenPutt
        {
            public double Ft, Xin, Yin;          // the putt's length; where a miss or lip-out stops (inches across, + right, and past, + long)
            public double? RolloutIn, EntryDeg;  // pace (inches past the centre had there been no hole); where a make went in (0 the front, + right)
            public bool Made, Lip;
        }

        public bool CloseUp => _close != null;

        sealed class Playback
        {
            public CupSim.Run Run;
            public readonly List<(int i, string slot, float vol, float pitch)> Sounds = new List<(int, string, float, float)>();
            public int NextSound;
            public float T, After, D;
            public bool Made, Finished, Snap = true;
            public System.Action Done;
        }

        Transform _set, _setBall;
        Material _ballMat, _ballInCup;   // the set's ball on the green, and the same ball giving a little light once it is in the cup's shade
        Light _cupLight;
        const float CupLightIdle = 1.5f, CupLightHeld = 5f;
        Playback _close;
        float _nearWas, _farWas, _fovWas, _shadowWas = -1f;
        static Texture2D _grass, _grassNormal, _ballTex, _ballNormal;

        /// <summary>Play a green putt of 11 ft or less up close; done() once the ball is at rest (in the cup, or where it stopped).</summary>
        public void PuttUpClose(GreenPutt putt, System.Action done)
        {
            BuildSet();
            float d = Mathf.Max(6f, (float)putt.Ft * 12f);
            var pb = new Playback { D = d, Made = putt.Made, Done = done };
            float roll = (float)(putt.RolloutIn ?? 6.0);
            if (putt.Made)
            {
                // In along the line it entered on: a straight putt crossing the front of the rim at the measured entry.
                float c = putt.EntryDeg != null ? CupSim.Rh * Mathf.Sin(Mathf.Clamp((float)putt.EntryDeg.Value, -80f, 80f) * Mathf.Deg2Rad) : Mathf.Clamp((float)putt.Xin, -CupSim.Rh, CupSim.Rh);
                pb.Run = CupSim.Holed(d, c, Mathf.Sqrt(2f * CupSim.Green * Mathf.Max(3f, roll)));
            }
            else if (putt.Lip)
            {
                // It catches the edge on the side it came in on and rides round the rim; it stops as far from the hole
                // as the engine left it (the next putt's length), along the line it spun off on.
                float side = putt.EntryDeg != null ? (putt.EntryDeg.Value < 0 ? -1f : 1f) : putt.Xin < 0 ? -1f : 1f;
                float leave = Mathf.Max(3f, new Vector2((float)putt.Xin, (float)putt.Yin).magnitude);
                pb.Run = CupSim.LipOut(d, side, Mathf.Sqrt(2f * CupSim.Green * Mathf.Max(20f, roll)));
                if (pb.Run != null) CupSim.RollOut(pb.Run, leave);
                else pb.Run = CupSim.Miss(d, side * (CupSim.Rh + 0.3f), (float)putt.Yin);
            }
            else pb.Run = CupSim.Miss(d, (float)putt.Xin, (float)putt.Yin);

            AddSounds(pb, true);
            BeginCloseUp(pb);
        }

        // The sounds, from what the ball hit: the cup's own note when it lands on the floor (unless the caller plays it), a rattle off the liner.
        static void AddSounds(Playback pb, bool cupNote)
        {
            float last = -1f; bool landed = false;
            foreach (var hit in pb.Run.Hits)
            {
                float at = hit.i * CupSim.Step;
                if (hit.kind == 2 && !landed) { landed = true; if (cupNote) pb.Sounds.Add((hit.i, "golf_cup", 1f, 1f)); last = at; continue; }
                if (at - last < 0.06f) continue;
                if (hit.kind == 2 && hit.v > 14f) pb.Sounds.Add((hit.i, "knock", Mathf.Clamp(hit.v / 160f, 0.08f, 0.25f), 1.6f));
                else if (hit.kind == 1 && hit.v > 10f) { pb.Sounds.Add((hit.i, "rim_clang", Mathf.Clamp(hit.v / 140f, 0.12f, 0.4f), 1.6f)); pb.Sounds.Add((hit.i, "knock", Mathf.Clamp(hit.v / 200f, 0.08f, 0.3f), 1.4f)); }
                else if (hit.kind == 0 && hit.v > 8f) pb.Sounds.Add((hit.i, "knock", Mathf.Clamp(hit.v / 220f, 0.06f, 0.22f), 1.7f));
                else continue;
                last = at;
            }
        }

        // The view leaves the course for the set and this run plays (from pb.T, which is 0 for a whole putt).
        void BeginCloseUp(Playback pb)
        {
            _set.gameObject.SetActive(true);
            _ball.gameObject.SetActive(false);
            HideOthers();
            _tracer.positionCount = 0;
            _flight = null;
            _setBall.GetComponent<MeshRenderer>().sharedMaterial = _ballMat;   // the last putt may have left it as the ball in the cup
            _cupLight.intensity = CupLightIdle;
            if (_close == null)
            {
                _nearWas = _camera.nearClipPlane; _farWas = _camera.farClipPlane; _fovWas = _camera.fieldOfView;
                _shadowWas = ShadowDistance(220f);
            }
            _camera.nearClipPlane = 0.25f; _camera.farClipPlane = 4400f;
            _close = pb;
            CloseTick();
        }

        void EndCloseUp()
        {
            if (_close == null) return;
            _close = null;
            if (_set != null) _set.gameObject.SetActive(false);
            _camera.nearClipPlane = _nearWas; _camera.farClipPlane = _farWas; _camera.fieldOfView = _fovWas;
            if (_shadowWas > 0f) ShadowDistance(_shadowWas);
            _shadowWas = -1f;
            _camPos = _wantPos; _camLook = _wantLook;
        }

        // The pipeline's shadow reach, by name (this assembly does not reference URP): the set is in inches, the course in yards.
        static float ShadowDistance(float d)
        {
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            System.Reflection.PropertyInfo prop = rp != null ? rp.GetType().GetProperty("shadowDistance") : null;
            if (prop == null || !prop.CanWrite) return -1f;
            float was = (float)prop.GetValue(rp);
            prop.SetValue(rp, d);
            return was;
        }

        void CloseTick()
        {
            Playback pb = _close;
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            if (!pb.Snap) pb.T += dt;
            List<Vector3> P = pb.Run.P;
            int i = Mathf.Clamp(Mathf.FloorToInt(pb.T / CupSim.Step), 0, P.Count - 1);
            Vector3 b = P[i];
            // The liner stands a tenth of an inch inside the cut hole the roll was worked out in: below its top the ball is
            // drawn clear of it, so a ball lying against the side does not sink into the white wall.
            if (b.y < -0.4f)
            {
                float clear = CupSim.Rh - CupSim.R - 0.1f * Mathf.InverseLerp(-0.4f, -1f, b.y), off = new Vector2(b.x, b.z).magnitude;
                if (off > clear) { b.x *= clear / off; b.z *= clear / off; }
            }
            _setBall.localPosition = b;
            _setBall.localRotation = pb.Run.Q[i];
            while (pb.NextSound < pb.Sounds.Count && pb.Sounds[pb.NextSound].i <= i) { var s = pb.Sounds[pb.NextSound++]; Sfx(s.slot, s.vol, s.pitch); }

            // The camera: low behind the ball down the line, onto the hole as the ball gets there, then into the cup.
            float aspect = Texture != null ? Texture.width / (float)Texture.height : 1f;
            _camera.fieldOfView = aspect < 1f ? 50f : 36f;
            float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-48f, -12f, b.z));
            Vector3 followPos = new Vector3(b.x * 0.7f, 6.5f, b.z - 22f), followLook = new Vector3(b.x * 0.4f, 0.3f, b.z + 26f);
            Vector3 holePos = new Vector3(3f, 16f, -27f), holeLook = new Vector3(0.2f, -1f, 2.5f);
            // On its way in the ball stays in the shot: the camera looks partway down to it until it gets there.
            float coming = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-14f, -4f, b.z));
            holeLook = Vector3.Lerp(holeLook, new Vector3(b.x, 0.5f, b.z), 0.3f * coming);
            if (pb.Run.DropAt < 0)
            {
                // A miss rolling on past, or a lip-out spinning off: the hole and the ball both in the shot, backing off as it goes.
                float away = b.z > -2.5f ? new Vector2(b.x, b.z).magnitude : 0f;
                float past = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 20f, away));
                float half = Mathf.Atan(Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Min(1f, aspect));   // the narrower half-angle
                var mid = new Vector3(b.x * 0.5f, 0.3f, b.z * 0.5f);
                Vector3 view = (holeLook - holePos).normalized;
                float back = Mathf.Max((holeLook - holePos).magnitude, (away * 0.5f + 2.5f) / Mathf.Tan(half * 0.9f));
                holePos = Vector3.Lerp(holePos, mid - view * back, past);
                holeLook = Vector3.Lerp(holeLook, mid, past);
            }
            Vector3 pos = Vector3.Lerp(followPos, holePos, w), look = Vector3.Lerp(followLook, holeLook, w);
            pos.z = Mathf.Min(pos.z, Mathf.Min(b.z, -10f) - 17f);   // never close over the ball on its way in
            if (pb.Run.DropAt >= 0)
            {
                // Into the cup: from straight above (an inch and a half behind, so the view keeps its way up), between the hole's
                // centre and where the ball lies, and as low as puts the rim across seven tenths of the view's narrower side.
                // That is about 14 in up on an upright phone and 9 in on a wide screen, and the ball is then a fifth of that
                // side across. (It was 19 in up and off to the back, where the ball was a small dark shape under the near wall.)
                Vector3 rest = P[P.Count - 1];
                float u = Mathf.SmoothStep(0f, 1f, (pb.T - pb.Run.DropAt * CupSim.Step - 0.1f) / 1.2f);
                float tanHalf = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Min(1f, aspect);
                float up = Mathf.Clamp(CupSim.Rh / (0.7f * tanHalf), 8f, 22f);
                pos = Vector3.Lerp(pos, new Vector3(rest.x * 0.5f, up, rest.z * 0.5f - 1.6f), u);
                look = Vector3.Lerp(look, new Vector3(rest.x * 0.5f, CupSim.Floor + CupSim.R, rest.z * 0.5f), u);
                // The lamp in the cup comes up as the camera comes over, and once the ball is under the lip (out of the sun)
                // it is the ball that gives a little light of its own.
                _cupLight.intensity = Mathf.Lerp(CupLightIdle, CupLightHeld, u);
                if (i >= pb.Run.DropAt)
                {
                    var mr = _setBall.GetComponent<MeshRenderer>();
                    if (mr.sharedMaterial != _ballInCup) mr.sharedMaterial = _ballInCup;
                }
            }
            pos += _set.localPosition; look += _set.localPosition;
            if (pb.Snap) { _camPos = pos; _camLook = look; pb.Snap = false; }
            else { float k = 1f - Mathf.Pow(0.0002f, dt); _camPos = Vector3.Lerp(_camPos, pos, k); _camLook = Vector3.Lerp(_camLook, look, k); }
            _camera.transform.localPosition = _camPos;
            _camera.transform.LookAt(_root.TransformPoint(_camLook));
            Perf.Wake(0.2f);

            if (i >= P.Count - 1 && !pb.Finished)
            {
                pb.After += dt;
                if (pb.After >= (pb.Made ? 1.1f : 0.5f)) { pb.Finished = true; pb.Done?.Invoke(); }
            }
        }

        // The set: the green in mown stripes, the collar and rough round it, trees far off, the cup, the flagstick, the ball.
        void BuildSet()
        {
            if (_set != null) return;
            _set = new GameObject("Putting green").transform;
            _set.SetParent(_root, false);
            _set.localPosition = new Vector3(0f, -6000f, 0f);   // inches, far below the course and past the camera's reach from it
            Grass(out Texture2D grass, out Texture2D grassNormal);
            Material Turf(Color tint, float smooth) { Material m = PuttrArcade.Make.Lit(tint, smooth, 0f, grass, grassNormal, 1f); _made.Add(m); return m; }
            Material light = Turf(new Color(1f, 1f, 0.95f), 0.16f), dark = Turf(new Color(0.83f, 0.9f, 0.8f), 0.12f);
            Material collar = Turf(new Color(0.78f, 0.92f, 0.66f), 0.08f), rough = Turf(new Color(0.55f, 0.72f, 0.42f), 0.04f);
            const float uv = 1f / 6f, stripe = 20f;
            const int n = 21;
            const float edge = n * stripe + stripe / 2f;  // 430 in: the green runs 36 ft each way from the hole
            for (int s = -n; s <= n; s++)
            {
                float x0 = s * stripe - stripe / 2f, x1 = x0 + stripe;
                Material m = (s & 1) == 0 ? light : dark;
                if (s != 0) { Piece(x0, x1, -edge, edge, uv, m); continue; }
                Piece(x0, x1, -edge, -stripe / 2f, uv, m);
                Piece(x0, x1, stripe / 2f, edge, uv, m);
                SetMesh("Green by the hole", PuttrArcade.Make.HolePatch(stripe / 2f, CupSim.Rh, uv, Vector3.zero, 1f, 128), m, Vector3.zero);
            }
            Frame(edge, edge + 34f, 1f / 9f, collar);
            Frame(edge + 34f, 4000f, 1f / 18f, rough);

            // The cup: the green's cut edge, an inch of soil, then the white liner all the way down to a mid-grey floor with
            // the ferrule's socket in it. The floor is in the green's shadow whatever the sun does, so nothing in the cup
            // depends on the sun to be seen: the liner and floor give a little light of their own (as the arcade's lit
            // trims do: the project is linear colour, so these are modest sRGB greys, not white), and a small lamp hangs
            // just under the rim, which shades the ball round and leaves the floor darker than the ball.
            Material Plain(Color c, float smooth, float metal = 0f) { Material m = PuttrArcade.Make.Lit(c, smooth, metal); _made.Add(m); return m; }
            const float r = CupSim.Rh;
            SetMesh("Cut edge", PuttrArcade.Make.Cup(r, 0.28f, false, 128), light, Vector3.zero);
            SetMesh("Soil", PuttrArcade.Make.Cup(r, 0.75f, false, 128), Plain(new Color(0.19f, 0.13f, 0.08f), 0.05f), new Vector3(0f, -0.27f, 0f));
            Material Lamp(Color c, Color glow, float smooth) { Material m = PuttrArcade.Make.Emissive(c, glow, smooth); _made.Add(m); return m; }
            Material white = Lamp(new Color(0.93f, 0.93f, 0.9f), new Color(0.42f, 0.42f, 0.4f), 0.45f);
            SetMesh("Liner top", PuttrArcade.Make.Ring(r - 0.12f, r + 0.002f, 128), white, new Vector3(0f, -1f, 0f));
            SetMesh("Liner", PuttrArcade.Make.Cup(r - 0.1f, -CupSim.Floor - 1f, false, 128), white, new Vector3(0f, -1f, 0f));
            SetMesh("Cup floor", PuttrArcade.Make.Disc(r - 0.1f, 128), Lamp(new Color(0.42f, 0.42f, 0.42f), new Color(0.2f, 0.2f, 0.2f), 0.3f), new Vector3(0f, CupSim.Floor, 0f));
            // The floor of the cup is the Puttr icon (it is drawn from one): its center circle on the center of the hole, its
            // top toward the far side of the cup, the whole mark inside the liner (Matt, 2026-11-23).
            Texture2D icon = Resources.Load<Texture2D>("PuttrArcade/LogoMark");
            if (icon != null)
            {
                Material mark = PuttrArcade.Make.Glow(Color.white, false, icon);
                _made.Add(mark);
                GameObject floorMark = PuttrArcade.Make.MeshObject("Puttr icon", _set, CupIcon(icon, r - 0.1f), mark);
                floorMark.transform.localPosition = new Vector3(0f, CupSim.Floor + 0.012f, 0f);
                floorMark.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            // The lamp: a point light 0.6 in under the green (so it never touches the turf, which faces away from it), reaching
            // 10 in, without shadows so the ball does not put the floor beside it in the dark. Brightness falls with the square
            // of the distance in inches: at 5 the top of the ball, 1.7 in off, is lit about as the sun lights the green.
            _cupLight = new GameObject("Cup light").AddComponent<Light>();
            _cupLight.transform.SetParent(_set, false);
            _cupLight.transform.localPosition = new Vector3(0f, -0.6f, 0f);
            _cupLight.type = LightType.Point;
            _cupLight.color = new Color(1f, 0.98f, 0.94f);
            _cupLight.range = 10f;
            _cupLight.intensity = CupLightIdle;
            _cupLight.shadows = LightShadows.None;

            // The flagstick, pulled and laid on the green past the hole: a 7 ft pole, its ferrule, the flag.
            var stick = new GameObject("Flagstick").transform;
            stick.SetParent(_set, false);
            stick.localPosition = new Vector3(-30f, 0.25f, 58f);
            stick.localRotation = Quaternion.Euler(0f, -62f, 0f);
            Part(PrimitiveType.Cylinder, stick, new Vector3(0f, 0f, 42f), new Vector3(90f, 0f, 0f), new Vector3(0.5f, 42f, 0.5f), Plain(new Color(0.95f, 0.94f, 0.9f), 0.55f));
            Part(PrimitiveType.Cylinder, stick, new Vector3(0f, 0f, -0.6f), new Vector3(90f, 0f, 0f), new Vector3(0.56f, 0.6f, 0.56f), Plain(new Color(0.75f, 0.75f, 0.78f), 0.85f, 1f));
            Part(PrimitiveType.Cube, stick, new Vector3(7.5f, -0.2f, 76f), new Vector3(0f, 8f, 0f), new Vector3(14f, 0.06f, 19f), Plain(PuttrArcade.Make.Hex(0xff3b30), 0.2f));

            // Trees round the edge of the view, so the ground does not stop at a line against the sky.
            if (_real)
            {
                // Two loose rings of real trees (the set is in inches: 36 to the yard).
                var ring = new TracksReal.Grove();
                for (int t = 0; t < 150; t++)
                {
                    float a = t / 150f * Mathf.PI * 2f + SetHash(t, 3) * 0.05f, dist = 2700f + SetHash(t, 7) * 1100f;
                    ring.Add(new Vector3(Mathf.Sin(a) * dist, 0f, Mathf.Cos(a) * dist), false, SetHash(t, 11), SetHash(t, 13), 0.85f + 0.25f * SetHash(t, 17), SetHash(t, 19) > 0.5f, 36f);
                }
                ring.Finish(_set, 36f, _made);
            }
            Material[] leaves = { Plain(PuttrArcade.Make.Hex(0x3a5236), 0.05f), Plain(PuttrArcade.Make.Hex(0x435c3a), 0.05f), Plain(PuttrArcade.Make.Hex(0x324a32), 0.05f) };   // hazed by the distance
            for (int t = 0; t < (_real ? 0 : 90); t++)
            {
                float a = t / 90f * Mathf.PI * 2f + SetHash(t, 3) * 0.06f, dist = 3000f + SetHash(t, 7) * 700f, sx = 240f + SetHash(t, 11) * 220f, sy = 300f + SetHash(t, 13) * 300f;
                Transform tree = Part(PrimitiveType.Sphere, _set, new Vector3(Mathf.Sin(a) * dist, sy * 0.32f, Mathf.Cos(a) * dist), Vector3.zero, new Vector3(sx, sy, sx), leaves[t % 3]);
                tree.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            if (_ballTex == null) PuttrArcade.Make.Ball(out _ballTex, out _ballNormal);
            _ballMat = PuttrArcade.Make.Lit(Color.white, 0.62f, 0f, _ballTex, _ballNormal, 0.6f);
            _made.Add(_ballMat);
            // The same ball once it has dropped: white with some light of its own, so it stands off the grey floor in the shade.
            _ballInCup = PuttrArcade.Make.Emissive(Color.white, new Color(0.6f, 0.6f, 0.6f), 0.62f);
            _ballInCup.SetTexture("_BaseMap", _ballTex);
            _made.Add(_ballInCup);
            _setBall = Part(PrimitiveType.Sphere, _set, new Vector3(0f, CupSim.R, -60f), Vector3.zero, Vector3.one * CupSim.R * 2f, _ballMat);
            _set.gameObject.SetActive(false);
        }

        // A flat quad of the icon with the center of its center circle at the origin. In the 437 x 479 art that circle is at
        // (218, 263), and the top of the mark's stem, 263 px from it, is the furthest it reaches.
        Mesh CupIcon(Texture2D icon, float radius)
        {
            float k = radius * 0.96f / (263f / 479f * icon.height);
            float left = -218f / 437f * icon.width * k, right = left + icon.width * k;
            float top = 263f / 479f * icon.height * k, bottom = top - icon.height * k;
            var mesh = new Mesh { name = "Puttr icon" };
            mesh.vertices = new[] { new Vector3(left, 0f, bottom), new Vector3(left, 0f, top), new Vector3(right, 0f, top), new Vector3(right, 0f, bottom) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            _made.Add(mesh);
            return mesh;
        }

        void SetMesh(string name, Mesh mesh, Material m, Vector3 at)
        {
            _made.Add(mesh);
            GameObject go = PuttrArcade.Make.MeshObject(name, _set, mesh, m);
            go.transform.localPosition = at;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
        }

        Transform Part(PrimitiveType type, Transform parent, Vector3 at, Vector3 euler, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localRotation = Quaternion.Euler(euler); go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
            return go.transform;
        }

        // A flat strip of ground, mapped by where it lies (so strips join without a seam).
        void Piece(float x0, float x1, float z0, float z1, float uv, Material m)
        {
            var mesh = new Mesh { name = "Green" };
            mesh.vertices = new[] { new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { new Vector2(x0 * uv, z0 * uv), new Vector2(x0 * uv, z1 * uv), new Vector2(x1 * uv, z1 * uv), new Vector2(x1 * uv, z0 * uv) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            SetMesh("Green", mesh, m, Vector3.zero);
        }

        // A square frame of ground between two half-widths.
        void Frame(float inner, float outer, float uv, Material m)
        {
            Piece(-outer, -inner, -outer, outer, uv, m);
            Piece(inner, outer, -outer, outer, uv, m);
            Piece(-inner, inner, -outer, -inner, uv, m);
            Piece(-inner, inner, inner, outer, uv, m);
        }

        static float SetHash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + 1013904223);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static float TileNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period, ya = ((y0 % period) + period) % period, yb = (ya + 1) % period;
            return Mathf.Lerp(Mathf.Lerp(SetHash(xa + seed * 7919, ya), SetHash(xb + seed * 7919, ya), fx), Mathf.Lerp(SetHash(xa + seed * 7919, yb), SetHash(xb + seed * 7919, yb), fx), fy);
        }

        // Six inches of green seen from above, 1024 px a side: a dark thatch under short blades of a few greens, lighter to
        // their tips and lying mostly one way (the grain), each about a millimetre wide. It tiles without a join.
        static void Grass(out Texture2D color, out Texture2D normal)
        {
            if (_grass == null)
            {
                const int n = 1024;
                var px = new Color[n * n];
                var h = new float[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float v = 0.7f + 0.6f * (TileNoise(x / 32f, y / 32f, n / 32, 1) * 0.6f + TileNoise(x / 8f, y / 8f, n / 8, 2) * 0.4f);
                        px[y * n + x] = new Color(0.05f * v, 0.1f * v, 0.035f * v, 1f);
                    }
                var rng = new System.Random(17);
                const float grain = 1.1f;
                Color young = new Color(0.17f, 0.37f, 0.1f), old = new Color(0.29f, 0.45f, 0.13f), deep = new Color(0.11f, 0.27f, 0.08f);
                for (int b = 0; b < 52000; b++)
                {
                    float x = (float)rng.NextDouble() * n, y = (float)rng.NextDouble() * n;
                    float a = grain + ((float)rng.NextDouble() - 0.5f) * 2.4f, len = 9f + (float)rng.NextDouble() * 18f, wid = 2.5f + (float)rng.NextDouble() * 2.5f;
                    float pick = (float)rng.NextDouble();
                    Color c = pick < 0.2f ? deep : Color.Lerp(young, old, (float)rng.NextDouble());
                    c *= 0.75f + 0.5f * (float)rng.NextDouble();
                    float top = 0.4f + 0.6f * (float)rng.NextDouble();
                    float dx = Mathf.Cos(a), dy = Mathf.Sin(a);
                    for (float s = 0f; s <= len; s += 0.7f)
                    {
                        float f = s / len, half = wid * 0.5f * (1f - 0.75f * f * f);
                        float hh = top * (0.5f + 0.5f * f);
                        Color cc = c * (0.72f + 0.45f * f); cc.a = 1f;
                        for (float q = -half; q <= half; q += 0.7f)
                        {
                            int ix = (((int)(x + dx * s - dy * q)) % n + n) % n, iy = (((int)(y + dy * s + dx * q)) % n + n) % n;
                            int o = iy * n + ix;
                            float edgeShade = 1f - 0.25f * Mathf.Abs(q) / Mathf.Max(0.5f, half);
                            if (hh * edgeShade < h[o]) continue;   // under a taller blade
                            h[o] = hh * edgeShade;
                            px[o] = cc * edgeShade;
                        }
                    }
                }
                for (int i = 0; i < px.Length; i++) px[i].a = 1f;
                _grass = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, name = "Green grass" };
                _grass.SetPixels(px);
                _grass.Apply(true);
                var np = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float l = h[y * n + (x + n - 1) % n], rr = h[y * n + (x + 1) % n], dn = h[((y + n - 1) % n) * n + x], up = h[((y + 1) % n) * n + x];
                        Vector3 nv = new Vector3((l - rr) * 2.2f, (dn - up) * 2.2f, 1f).normalized;
                        np[y * n + x] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
                    }
                _grassNormal = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, name = "Green grass normal" };
                _grassNormal.SetPixels(np);
                _grassNormal.Apply(true);
            }
            color = _grass; normal = _grassNormal;
        }

        /// <summary>
        /// The ball and the hole, worked out ahead of time. A 1.68 in ball rolling on a green at stimp 10 (it slows at
        /// 21.6 in/s^2: a ball off the stimpmeter at 72 in/s rolls 120 in), a 4.25 in hole whose rim is a sharp edge, a
        /// wall straight down and a floor 4 in below the green. A slow ball topples over the front lip and drops; a firm
        /// one flies the gap, hits the back wall and rattles down; one that catches the edge rides round the rim. Once
        /// it is under the lip it stays in (a real cup keeps the ball).
        /// </summary>
        internal static class CupSim
        {
            public const float Step = 1f / 600f, R = 0.84f, Rh = 2.125f, G = 386.09f;
            public const float Green = 21.6f;  // in/s^2: stimp 10, about 0.056 g
            public const float Floor = -4f;    // the rules' depth: at least 4 in, the liner starting an inch down. A ball at rest has its top 2.3 in under the green
            const float FloorRoll = 30f, RimBounce = 0.18f, WallBounce = 0.42f, FloorBounce = 0.32f;

            public enum End { Holed, Stopped, Escaped, Timeout }

            public sealed class Run
            {
                public readonly List<Vector3> P = new List<Vector3>();
                public readonly List<Quaternion> Q = new List<Quaternion>();
                public readonly List<(int i, int kind, float v)> Hits = new List<(int, int, float)>();   // kind 0 the rim, 1 the wall, 2 the floor
                public End Ended;
                public int DropAt = -1;
                public bool Dipped;
                public Vector3 V;
            }

            /// <summary>A putt from (c, -d) straight up the line: run until it is at rest in the cup or on the green (or, untilEscaped, clear of the hole going away).</summary>
            public static Run Simulate(float c, float d, float v0, bool untilEscaped)
            {
                var run = new Run();
                var p = new Vector3(c, R, -d);
                var v = new Vector3(0f, 0f, v0);
                Quaternion spin = Quaternion.identity;
                Vector3 w = Vector3.zero;
                bool dropped = false;
                float still = 0f;
                int max = Mathf.RoundToInt(10f / Step);
                for (int i = 0; i < max; i++)
                {
                    float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                    bool onTurf = r >= Rh && p.y >= 0f && p.y <= R + 0.002f;
                    bool onFloor = p.y <= Floor + R + 0.002f && Mathf.Abs(v.y) < 3f;
                    if (onTurf || onFloor)
                    {
                        p.y = onTurf ? R : Floor + R;
                        if (v.y < 0f) v.y = 0f;
                        float slow = (onTurf ? Green : FloorRoll) * Step;
                        var h = new Vector2(v.x, v.z);
                        float sp = h.magnitude;
                        if (sp <= slow) { v.x = 0f; v.z = 0f; }
                        else { h -= h / sp * slow; v.x = h.x; v.z = h.y; }
                    }
                    else v.y -= G * Step;
                    p += v * Step;

                    r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                    Vector3 radial = r > 1e-5f ? new Vector3(p.x / r, 0f, p.z / r) : Vector3.forward;
                    bool rolling = false;
                    Vector3 n = Vector3.up;
                    if (p.y >= 0f && r >= Rh && p.y < R) { p.y = R; if (v.y < 0f) v.y = -v.y * 0.1f; rolling = true; }
                    if (p.y > -R)
                    {
                        // The rim: a sharp circle; the ball touches its nearest point.
                        Vector3 rim = radial * Rh, q = p - rim;
                        float m = q.magnitude;
                        if (m < R && m > 1e-5f)
                        {
                            Vector3 nn = q / m;
                            p = rim + nn * R;
                            float vn = Vector3.Dot(v, nn);
                            if (vn < 0f) { if (-vn > 6f) run.Hits.Add((run.P.Count, 0, -vn)); v -= (1f + RimBounce) * vn * nn; }
                            rolling = true; n = nn;
                        }
                    }
                    if (p.y < 0f && r > Rh - R)
                    {
                        // The wall: soil, then the liner.
                        p.x = radial.x * (Rh - R); p.z = radial.z * (Rh - R);
                        Vector3 nn = -radial;
                        float vn = Vector3.Dot(v, nn);
                        if (vn < 0f)
                        {
                            if (-vn > 6f) run.Hits.Add((run.P.Count, 1, -vn));
                            v -= (1f + WallBounce) * vn * nn;
                            v.y *= 0.9f;
                            w *= 0.7f;
                        }
                    }
                    if (p.y < Floor + R)
                    {
                        p.y = Floor + R;
                        if (v.y < 0f) { if (-v.y > 4f) run.Hits.Add((run.P.Count, 2, -v.y)); v.y = -v.y * FloorBounce; if (v.y < 3f) v.y = 0f; }
                        rolling = true; n = Vector3.up;
                    }
                    if (dropped && p.y > -R * 1.02f) { p.y = -R * 1.02f; if (v.y > 0f) v.y = 0f; }   // in is in

                    if (rolling) w = Vector3.Cross(n, v) / R;
                    float angle = w.magnitude * Step * Mathf.Rad2Deg;
                    if (angle > 1e-4f) spin = Quaternion.AngleAxis(angle, w.normalized) * spin;
                    run.P.Add(p); run.Q.Add(spin);
                    if (p.y < R - 0.05f) run.Dipped = true;

                    if (!dropped && p.y < -R * 1.05f) { dropped = true; run.DropAt = run.P.Count - 1; }
                    if (dropped)
                    {
                        still = p.y <= Floor + R + 0.01f && v.magnitude < 0.8f ? still + Step : 0f;
                        if (still > 0.25f) { run.Ended = End.Holed; return run; }
                        continue;
                    }
                    bool turf = p.y >= R - 0.01f && r >= Rh;
                    if (turf && v.sqrMagnitude < 1e-6f) { run.Ended = End.Stopped; run.V = v; return run; }
                    if (untilEscaped && turf && r > Rh + R && Vector3.Dot(new Vector3(v.x, 0f, v.z), radial) > 0f) { run.Ended = End.Escaped; run.V = v; return run; }
                }
                run.Ended = dropped ? End.Holed : End.Timeout;
                return run;
            }

            /// <summary>A holed putt: on the measured line and pace; if that would not drop, drawn a little further into the cup and eased a little at a time until it does.</summary>
            public static Run Holed(float d, float c0, float vHole)
            {
                float least = Mathf.Sqrt(2f * Green * Mathf.Max(0.5f, d - Rh + 0.6f));
                for (int k = 0; k < 16; k++)
                {
                    float c = k >= 8 ? 0f : c0 * Mathf.Pow(0.8f, k);
                    float vh = vHole * Mathf.Pow(0.9f, k);
                    Run run = Simulate(c, d, Mathf.Max(least, Mathf.Sqrt(vh * vh + 2f * Green * d)), false);
                    if (run.Ended == End.Holed) return run;
                }
                return Simulate(0f, d, least, false);
            }

            /// <summary>A lip-out: a line that catches the edge, moved out a little at a time until the ball stays out, taking the first
            /// that rides the rim round and spins off (turned 15 to 120 degrees), not one that only shaves the edge or comes straight back.</summary>
            public static Run LipOut(float d, float side, float vHole)
            {
                Run first = null;
                float v0 = Mathf.Sqrt(vHole * vHole + 2f * Green * d);
                for (int k = 0; k < 60; k++)
                {
                    Run run = Simulate(side * (Rh - 0.7f + 0.025f * k), d, v0, true);
                    if (run.Ended != End.Escaped || !run.Dipped) continue;
                    if (first == null) first = run;
                    float turned = Mathf.Abs(Mathf.Atan2(run.V.x, run.V.z) * Mathf.Rad2Deg);
                    if (turned >= 15f && turned <= 120f) return run;
                }
                return first;
            }

            /// <summary>After a lip-out: on along the line it left the hole on, slowing to a stop leave inches from the hole.</summary>
            public static void RollOut(Run run, float leave)
            {
                Vector3 p0 = run.P[run.P.Count - 1];
                var a = new Vector2(p0.x, p0.z);
                var vel = new Vector2(run.V.x, run.V.z);
                float u = vel.magnitude;
                Vector2 dir = u > 0.01f ? vel / u : a.normalized;
                float b = Vector2.Dot(a, dir), disc = b * b - (a.sqrMagnitude - leave * leave);
                float dist = disc > 0f ? -b + Mathf.Sqrt(disc) : u * u / (2f * Green);
                dist = Mathf.Max(0.5f, dist);
                u = Mathf.Max(u, 2f);
                float slow = Mathf.Clamp(u * u / (2f * dist), 8f, 140f);
                u = Mathf.Sqrt(2f * slow * dist);
                float time = u / slow, was = 0f;
                Quaternion spin = run.Q[run.Q.Count - 1];
                Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(dir.x, 0f, dir.y)).normalized;
                for (float t = Step; ; t += Step)
                {
                    float s = t >= time ? dist : u * t - slow * t * t * 0.5f;
                    spin = Quaternion.AngleAxis((s - was) / R * Mathf.Rad2Deg, axis) * spin;
                    was = s;
                    Vector2 q = a + dir * s;
                    run.P.Add(new Vector3(q.x, R, q.y)); run.Q.Add(spin);
                    if (t >= time) break;
                }
                run.Ended = End.Stopped;
            }

            /// <summary>A miss: straight up the line at x = c to a stop z inches past the hole (short of it if negative). A short one in line with the hole stops hanging on the front lip, never over it.</summary>
            public static Run Miss(float d, float c, float stopZ)
            {
                const float hang = Rh + 0.06f;
                if (Mathf.Abs(c) < hang)
                {
                    float lip = Mathf.Sqrt(hang * hang - c * c);
                    if (stopZ > -lip && stopZ < lip) stopZ = -lip;
                }
                stopZ = Mathf.Max(stopZ, -d + 0.5f);
                float travel = stopZ + d, v0 = Mathf.Sqrt(2f * Green * travel), time = v0 / Green;
                var run = new Run();
                Quaternion spin = Quaternion.identity;
                float was = 0f;
                for (float t = 0f; ; t += Step)
                {
                    float s = t >= time ? travel : v0 * t - Green * t * t * 0.5f;
                    spin = Quaternion.AngleAxis((s - was) / R * Mathf.Rad2Deg, Vector3.right) * spin;
                    was = s;
                    run.P.Add(new Vector3(c, R, -d + s)); run.Q.Add(spin);
                    if (t >= time) break;
                }
                run.Ended = End.Stopped;
                return run;
            }
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

        /// <summary>The arcade's camera carries the app's only listener, and the arcade is switched off here: without one
        /// nothing is heard, the booth's voices (TracksVoice) included, so whoever is about to make a sound asks for it.</summary>
        public void EnsureListener()
        {
            if (Object.FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        }

        float _bedVolume, _bedShare = 1f;

        /// <summary>The course's bed at this share of its volume (TracksVoice turns it down under a voice, and back up after).</summary>
        public void Duck(float share)
        {
            _bedShare = Mathf.Clamp01(share);
            if (_bedSource != null) _bedSource.volume = _bedVolume * _bedShare;
        }

        void EnsureSound()
        {
            if (_voices != null) return;
            EnsureListener();
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

        /// <summary>puttr-app-2: the gallery as a hole is finished at par or better: applause, and Tracks' own roar for an eagle
        /// or better, or a hole in one (silent until tracks_roar is recorded, so the applause stays as it was).</summary>
        public void Gallery(int strokes, int par)
        {
            if (strokes > par) return;
            Sfx("golf_clap", strokes < par ? 0.9f : 0.5f);
            if (strokes == 1 || strokes <= par - 2) Sfx("tracks_roar", 0.9f);
        }

        /// <summary>
        /// puttr-app-2: the gallery's reaction to a moment the booth calls (Tracks.Booth), under the line: a groan at a
        /// lip-out, a sympathetic sigh at a miss or the water, applause as an approach or a chip finishes by the flag, more
        /// for a long one holed, polite claps for a made putt or a good shot. A slot not yet in the library is silent.
        /// </summary>
        public void Crowd(string category)
        {
            switch (category)
            {
                case "putt_lip": Sfx("tracks_gallery_ooh", 0.85f); break;
                case "putt_miss_short": case "putt_miss_line": case "putt_miss_long": Sfx("tracks_gallery_aww", 0.6f); break;
                case "tee_water": case "approach_water": Sfx("tracks_gallery_aww", 0.7f); break;   // real gallery recordings from Soundly (Matt, 2026-11-23)
                case "tee_trees": case "tee_sand": case "approach_sand": Sfx("tracks_gallery_aww", 0.45f); break;
                case "approach_tight": case "chip_tight": Sfx("tracks_gallery_rise", 0.85f); break;
                case "putt_made_long": Sfx("tracks_gallery_cheer", 0.9f); break;
                case "putt_made": Sfx("tracks_gallery_polite", 0.6f); break;
                case "tee_fairway": case "approach_on": case "lag_good": if (Random.value < 0.6f) Sfx("tracks_gallery_polite", 0.5f); break;
            }
        }

        /// <summary>The course under everything: a *_loop slot.</summary>
        public void Bed(string slot, float volume = 0.3f)
        {
            AudioClip[] t = TakesOf(slot);
            if (t.Length == 0) return;
            EnsureSound();
            if (_bedSource.clip != t[0]) { _bedSource.clip = t[0]; _bedSource.Play(); }
            _bedVolume = volume;
            _bedSource.volume = volume * _bedShare;
        }
    }
}
