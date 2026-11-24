// Putt Star's toolbox: meshes merged by material (so the whole set is a dozen draw calls), the shapes it is
// made of, and the textures, all made in code like the rest of the arcade.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    /// <summary>Pieces added in the stage's own coordinates, built into one mesh per material.</summary>
    sealed class StarBatch
    {
        sealed class Part
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<int> T = new List<int>();
            public bool Shadows;
        }

        readonly Dictionary<Material, Part> _parts = new Dictionary<Material, Part>();
        readonly List<Material> _order = new List<Material>();
        static readonly Dictionary<Mesh, (Vector3[] v, Vector3[] n, Vector2[] u, int[] t)> Cache = new Dictionary<Mesh, (Vector3[], Vector3[], Vector2[], int[])>();

        /// <summary>worldUv above zero maps the texture from the stage's own axes (box mapping), so pieces line up.</summary>
        public void Add(Mesh mesh, Matrix4x4 m, Material mat, float worldUv = 0f, bool shadows = true)
        {
            if (!_parts.TryGetValue(mat, out Part p)) { p = new Part(); _parts[mat] = p; _order.Add(mat); }
            p.Shadows |= shadows;
            if (!Cache.TryGetValue(mesh, out var a)) { a = (mesh.vertices, mesh.normals, mesh.uv, mesh.triangles); Cache[mesh] = a; }
            int start = p.V.Count;
            Matrix4x4 nm = m.inverse.transpose;
            for (int i = 0; i < a.v.Length; i++)
            {
                Vector3 pw = m.MultiplyPoint3x4(a.v[i]);
                Vector3 nw = nm.MultiplyVector(a.n[i]).normalized;
                p.V.Add(pw);
                p.N.Add(nw);
                if (worldUv > 0f)
                {
                    float ax = Mathf.Abs(nw.x), ay = Mathf.Abs(nw.y), az = Mathf.Abs(nw.z);
                    Vector2 uv = ax >= ay && ax >= az ? new Vector2(pw.z, pw.y) : ay >= az ? new Vector2(pw.x, pw.z) : new Vector2(pw.x, pw.y);
                    p.U.Add(uv * worldUv);
                }
                else p.U.Add(i < a.u.Length ? a.u[i] : Vector2.zero);
            }
            bool flip = m.determinant < 0f;
            for (int i = 0; i < a.t.Length; i += 3)
            {
                p.T.Add(start + a.t[i]);
                p.T.Add(start + (flip ? a.t[i + 2] : a.t[i + 1]));
                p.T.Add(start + (flip ? a.t[i + 1] : a.t[i + 2]));
            }
        }

        public void Box(Material mat, Vector3 center, Vector3 size, Quaternion rot, float worldUv = 0f, bool shadows = true) =>
            Add(StarKit.Cube, Matrix4x4.TRS(center, rot, size), mat, worldUv, shadows);
        public void Box(Material mat, Vector3 center, Vector3 size, float worldUv = 0f, bool shadows = true) => Box(mat, center, size, Quaternion.identity, worldUv, shadows);

        /// <summary>A round bar from a to b.</summary>
        public void Tube(Material mat, Vector3 a, Vector3 b, float radius, bool shadows = true, Mesh mesh = null)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Add(mesh ?? StarKit.TubeMesh, Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, d / len), new Vector3(radius, len, radius)), mat, 0f, shadows);
        }

        /// <summary>A capped cylinder standing on its base at foot, along axis.</summary>
        public void Drum(Material mat, Vector3 foot, Vector3 axis, float radius, float height, bool shadows = true)
        {
            Add(StarKit.CylinderMesh, Matrix4x4.TRS(foot, Quaternion.FromToRotation(Vector3.up, axis.normalized), new Vector3(radius, height, radius)), mat, 0f, shadows);
        }

        public void Build(Transform parent, string name)
        {
            foreach (Material mat in _order)
            {
                Part p = _parts[mat];
                var mesh = new Mesh { name = name + " " + mat.name, indexFormat = p.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(p.V);
                mesh.SetNormals(p.N);
                mesh.SetUVs(0, p.U);
                mesh.SetTriangles(p.T, 0);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                GameObject go = Make.MeshObject(name, parent, mesh, mat);
                var r = go.GetComponent<MeshRenderer>();
                r.shadowCastingMode = p.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            _parts.Clear();
            _order.Clear();
        }
    }

    static class StarKit
    {
        // ── Shapes ──────────────────────────────────────────────────────────

        static Mesh _cube, _tube, _thin, _cyl, _ball;

        public static Mesh Cube
        {
            get
            {
                if (_cube != null) return _cube;
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = go.GetComponent<MeshFilter>().sharedMesh;
                Make.Kill(go);
                return _cube;
            }
        }

        /// <summary>A six-sided open tube, radius 1, from y = 0 to 1: truss chords and bracing.</summary>
        public static Mesh TubeMesh => _tube != null ? _tube : (_tube = Tube(6, false));
        /// <summary>A sixteen-sided capped cylinder, radius 1, from y = 0 to 1.</summary>
        public static Mesh CylinderMesh => _cyl != null ? _cyl : (_cyl = Tube(18, true));
        /// <summary>A smooth ball, diameter 1.</summary>
        public static Mesh Ball => _ball != null ? _ball : (_ball = Sphere(14, 9));
        public static Mesh RoundTube => _thin != null ? _thin : (_thin = Tube(10, false));

        static readonly Dictionary<Vector4, Mesh> Roundeds = new Dictionary<Vector4, Mesh>();
        /// <summary>Make.RoundedBox, made once per size.</summary>
        public static Mesh Rounded(Vector3 size, float radius)
        {
            var key = new Vector4(size.x, size.y, size.z, radius);
            if (!Roundeds.TryGetValue(key, out Mesh m)) { m = Make.RoundedBox(size, radius); Roundeds[key] = m; }
            return m;
        }

        static Mesh Tube(int sides, bool caps)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<int>();
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d); v.Add(d + Vector3.up);
                n.Add(d); n.Add(d);
                u.Add(new Vector2(s / (float)sides, 0)); u.Add(new Vector2(s / (float)sides, 1));
            }
            for (int s = 0; s < sides; s++)
            {
                int a = s * 2;
                t.Add(a); t.Add(a + 1); t.Add(a + 2);
                t.Add(a + 2); t.Add(a + 1); t.Add(a + 3);
            }
            if (caps)
            {
                for (int end = 0; end < 2; end++)
                {
                    int c = v.Count;
                    Vector3 up = end == 0 ? Vector3.down : Vector3.up;
                    v.Add(new Vector3(0, end, 0)); n.Add(up); u.Add(new Vector2(0.5f, 0.5f));
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s / (float)sides * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Cos(a), end, Mathf.Sin(a))); n.Add(up);
                        u.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
                    }
                    for (int s = 0; s < sides; s++)
                    {
                        if (end == 1) { t.Add(c); t.Add(c + 2 + s); t.Add(c + 1 + s); }
                        else { t.Add(c); t.Add(c + 1 + s); t.Add(c + 2 + s); }
                    }
                }
            }
            var mesh = new Mesh { name = caps ? "Cylinder" : "Tube" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, u); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Sphere(int segments, int rings)
        {
            var profile = new List<Vector2>();
            for (int k = 0; k <= rings; k++)
            {
                float a = -Mathf.PI / 2f + Mathf.PI * k / rings;
                profile.Add(new Vector2(Mathf.Max(0.0005f, Mathf.Cos(a) * 0.5f), Mathf.Sin(a) * 0.5f));
            }
            return Make.Lathe(profile, segments);
        }

        /// <summary>A rounded limb hanging down from its joint: radius r0 at the top, r1 at the bottom, len long.</summary>
        public static Mesh Limb(float len, float r0, float r1, int segments = 10)
        {
            var p = new List<Vector2>
            {
                new Vector2(0.0005f, -len - r1 * 0.95f), new Vector2(r1 * 0.72f, -len - r1 * 0.65f), new Vector2(r1, -len),
                new Vector2(Mathf.Lerp(r0, r1, 0.55f) * 1.04f, -len * 0.5f), new Vector2(r0, 0f), new Vector2(r0 * 0.72f, r0 * 0.65f), new Vector2(0.0005f, r0 * 0.95f),
            };
            return Make.Lathe(p, segments);
        }

        /// <summary>A square truss from a to b: four chords, laced on every face, size across.</summary>
        public static void Truss(StarBatch b, Material m, Vector3 a, Vector3 z, float size, Vector3 upHint)
        {
            Vector3 dir = z - a;
            float len = dir.magnitude;
            dir /= len;
            Vector3 u = Vector3.Cross(dir, upHint);
            if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(dir, Vector3.forward);
            u.Normalize();
            Vector3 w = Vector3.Cross(u, dir).normalized;
            float h = size * 0.5f;
            Vector3[] c = { (u + w) * h, (u - w) * h, (-u - w) * h, (-u + w) * h };
            float chord = size * 0.075f, lace = size * 0.032f;
            for (int k = 0; k < 4; k++) b.Tube(m, a + c[k], z + c[k], chord);
            int bays = Mathf.Max(1, Mathf.RoundToInt(len / size));
            float step = len / bays;
            for (int i = 0; i < bays; i++)
            {
                Vector3 p0 = a + dir * (step * i), p1 = p0 + dir * step;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 ca = c[k], cb = c[(k + 1) % 4];
                    bool alt = i % 2 == 0;
                    b.Tube(m, p0 + (alt ? ca : cb), p1 + (alt ? cb : ca), lace);
                    b.Tube(m, p0 + ca, p0 + cb, lace);
                }
            }
            for (int k = 0; k < 4; k++) b.Tube(m, z + c[k], z + c[(k + 1) % 4], lace);
        }

        // ── Textures ────────────────────────────────────────────────────────

        static Texture2D Tex(int w, int h, Color[] px, bool repeat, bool mips = true, FilterMode filter = FilterMode.Trilinear)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mips) { wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, filterMode = filter, anisoLevel = 4 };
            t.SetPixels(px);
            t.Apply(mips, true);
            return t;
        }

        /// <summary>The stage deck: black-painted ply in sheets, worn grey where people walk, scuffs, a few spike marks.</summary>
        public static void Deck(out Texture2D color, out Texture2D normal)
        {
            const int n = 512;
            var rng = new System.Random(11);
            var shade = new float[n * n];
            var h = new float[n * n];
            var tint = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float wear = Make.TileNoise(x / 48f, y / 48f, n / 48 + 1, 21) * 0.6f + Make.TileNoise(x / 12f, y / 12f, n / 12 + 1, 22) * 0.4f;
                    float grain = Make.Hash(x, y, 23);
                    shade[i] = 0.10f + wear * 0.07f + grain * 0.025f;
                    h[i] = grain * 0.25f;
                    tint[i] = Color.white;
                }
            // Sheet seams, every quarter.
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (x % 128 < 2 || y % 256 < 2) { shade[y * n + x] *= 0.45f; h[y * n + x] -= 1.2f; }
            // Scuffs: short pale arcs from boots and cases.
            for (int s = 0; s < 260; s++)
            {
                float cx = (float)rng.NextDouble() * n, cy = (float)rng.NextDouble() * n;
                float ang = (float)rng.NextDouble() * Mathf.PI, len = 6f + (float)rng.NextDouble() * 34f, bend = ((float)rng.NextDouble() - 0.5f) * 0.05f;
                float strength = 0.04f + (float)rng.NextDouble() * 0.08f;
                for (int k = 0; k < len; k++)
                {
                    float a = ang + bend * k;
                    int px = ((int)(cx + Mathf.Cos(a) * k) % n + n) % n, py = ((int)(cy + Mathf.Sin(a) * k) % n + n) % n;
                    shade[py * n + px] += strength * (1f - Mathf.Abs(k / len - 0.5f));
                    h[py * n + px] -= 0.3f;
                }
            }
            // Spike tape: little crosses and Ls in white, yellow and fluoro pink.
            Color[] tape = { new Color(0.9f, 0.9f, 0.85f), new Color(0.95f, 0.8f, 0.1f), new Color(1f, 0.3f, 0.6f), new Color(0.3f, 0.9f, 0.4f) };
            for (int s = 0; s < 9; s++)
            {
                int cx = rng.Next(n), cy = rng.Next(n);
                Color c = tape[rng.Next(tape.Length)];
                int arm = 5 + rng.Next(4);
                for (int k = -arm; k <= arm; k++)
                    for (int w = -1; w <= 1; w++)
                    {
                        int ax = ((cx + k) % n + n) % n, ay = ((cy + w) % n + n) % n, bx = ((cx + w) % n + n) % n, by = ((cy + k) % n + n) % n;
                        tint[ay * n + ax] = c; shade[ay * n + ax] = 0.8f;
                        if (s % 2 == 0) { tint[by * n + bx] = c; shade[by * n + bx] = 0.8f; }
                    }
            }
            var px2 = new Color[n * n];
            for (int i = 0; i < px2.Length; i++) { float v = shade[i]; Color c = tint[i] * v; c.a = 1f; px2[i] = c; }
            color = Tex(n, n, px2, true);
            normal = Make.NormalFromHeight(h, n, n, 1.2f);
        }

        /// <summary>Plain concrete for the arena floor.</summary>
        public static Texture2D Concrete()
        {
            const int n = 256;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0.32f + Make.TileNoise(x / 32f, y / 32f, 8, 31) * 0.18f + Make.TileNoise(x / 6f, y / 6f, 43, 32) * 0.08f + Make.Hash(x, y, 33) * 0.06f;
                    px[y * n + x] = new Color(v, v, v * 1.02f, 1f);
                }
            return Tex(n, n, px, true);
        }

        /// <summary>Black wool drape: deep vertical folds. Returns color and normal.</summary>
        public static void Drape(out Texture2D color, out Texture2D normal)
        {
            const int n = 256;
            var px = new Color[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fold = Mathf.Sin(x / (float)n * Mathf.PI * 2f * 6f + Mathf.Sin(x / (float)n * Mathf.PI * 2f * 2f) * 1.3f);
                    float fuzz = Make.Hash(x, y, 41) * 0.15f;
                    float v = 0.5f + fold * 0.35f + fuzz;
                    px[y * n + x] = new Color(v, v, v, 1f);
                    h[y * n + x] = fold * 3f + fuzz;
                }
            color = Tex(n, n, px, true);
            normal = Make.NormalFromHeight(h, n, n, 1.5f);
        }

        /// <summary>Perforated steel speaker grille: holes in staggered rows. Returns color and normal.</summary>
        public static void Grille(out Texture2D color, out Texture2D normal)
        {
            const int n = 128, pitch = 8;
            var px = new Color[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int row = y / pitch;
                    float cx = (x + (row % 2) * pitch * 0.5f) % pitch - pitch * 0.5f, cy = y % pitch - pitch * 0.5f;
                    float d = Mathf.Sqrt(cx * cx + cy * cy);
                    float hole = Mathf.Clamp01((2.6f - d) * 1.5f);
                    float v = Mathf.Lerp(0.55f, 0.08f, hole);
                    px[y * n + x] = new Color(v, v, v, 1f);
                    h[y * n + x] = -hole;
                }
            color = Tex(n, n, px, true);
            normal = Make.NormalFromHeight(h, n, n, 2f);
        }

        /// <summary>Amp tolex: pebbled black vinyl. Returns color and normal.</summary>
        public static void Tolex(out Texture2D color, out Texture2D normal)
        {
            const int n = 128;
            var px = new Color[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float pebble = Make.TileNoise(x / 2.2f, y / 2.2f, 58, 51);
                    float v = 0.35f + pebble * 0.25f;
                    px[y * n + x] = new Color(v, v, v, 1f);
                    h[y * n + x] = pebble * 1.4f;
                }
            color = Tex(n, n, px, true);
            normal = Make.NormalFromHeight(h, n, n, 1.6f);
        }

        public static Texture2D Weave()
        {
            const int n = 64;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool across = ((x / 4) + (y / 4)) % 2 == 0;
                    float thread = across ? Mathf.Sin((y % 4) / 4f * Mathf.PI) : Mathf.Sin((x % 4) / 4f * Mathf.PI);
                    float v = 0.22f + thread * 0.45f + Make.Hash(x, y, 61) * 0.1f;
                    px[y * n + x] = new Color(v * 0.95f, v * 0.9f, v * 0.8f, 1f);
                }
            return Tex(n, n, px, true);
        }

        /// <summary>
        /// The light atlas. Left half: a beam seen side on, bright down the middle and fading along its length
        /// (u across, v from the lamp outwards). Right half: a lens flare, a hot core in a soft glow.
        /// </summary>
        public static Texture2D LightAtlas()
        {
            const int w = 256, h = 128;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a;
                    if (x < w / 2)
                    {
                        float u = (x + 0.5f) / (w / 2f), v = (y + 0.5f) / h;
                        float across = (u - 0.5f) * 2f;
                        float core = Mathf.Exp(-across * across / 0.03f), halo = Mathf.Exp(-across * across / 0.22f);
                        float along = Mathf.Pow(1f - v, 1.6f) * Mathf.Clamp01(v * 30f);
                        a = (core * 0.55f + halo * 0.45f) * along * Mathf.Clamp01((1f - Mathf.Abs(across)) * 6f);
                    }
                    else
                    {
                        float u = (x - w / 2 + 0.5f) / (w / 2f), v = (y + 0.5f) / h;
                        float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                        float core = Mathf.Exp(-d * d / 0.012f), glow = Mathf.Exp(-d * d / 0.12f) * 0.45f;
                        a = Mathf.Clamp01(core + glow) * Mathf.Clamp01((1f - d) * 4f);
                    }
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            return Tex(w, h, px, false);
        }

        /// <summary>Slow, soft smoke, tiling both ways.</summary>
        public static Texture2D Haze()
        {
            const int n = 256;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = Make.TileNoise(x / 64f, y / 64f, 4, 71) * 0.55f + Make.TileNoise(x / 24f, y / 24f, 11, 72) * 0.3f + Make.TileNoise(x / 9f, y / 9f, 29, 73) * 0.15f;
                    a = Mathf.Clamp01((a - 0.3f) * 1.8f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            return Tex(n, n, px, true);
        }

        /// <summary>One LED of a video wall: the pixel lit, the gap around it dark. Tiled once per LED.</summary>
        public static Texture2D LedGrid()
        {
            const int n = 16;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - n / 2f) / (n / 2f), dy = Mathf.Abs(y + 0.5f - n / 2f) / (n / 2f);
                    float d = Mathf.Max(dx, dy);
                    px[y * n + x] = new Color(0f, 0f, 0f, Mathf.Clamp01((d - 0.68f) * 5f) * 0.6f);
                }
            return Tex(n, n, px, true);
        }

        /// <summary>The wall's moving picture: soft bars of light rising from a glow along the bottom. Tiles across.</summary>
        public static Texture2D Visual()
        {
            const int w = 512, h = 256;
            var px = new Color[w * h];
            var rng = new System.Random(81);
            var bars = new float[24];
            for (int i = 0; i < bars.Length; i++) bars[i] = 0.3f + (float)rng.NextDouble() * 0.7f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float bu = u * bars.Length;
                    int bi = (int)bu;
                    float inBar = Mathf.Sin((bu - bi) * Mathf.PI);
                    float tall = bars[bi];
                    float bar = Mathf.Pow(inBar, 3f) * Mathf.Clamp01((tall - v) * 3f) * 0.75f;
                    float floor = Mathf.Exp(-v * 4.5f) * 0.6f;
                    float sky = Make.TileNoise(u * 8f, v * 4f, 8, 82) * 0.2f;
                    float a = Mathf.Clamp01(bar + floor + sky);
                    px[y * w + x] = new Color(a, a, a, 1f);
                }
            var t = Tex(w, h, px, true);
            t.wrapModeV = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>The crowd meter: 40 segments, red through amber to green, clear between them.</summary>
        public static Texture2D Meter()
        {
            const int w = 640, h = 16, segments = 40;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float s = u * segments;
                    bool gap = s - Mathf.Floor(s) > 0.78f || y < 1 || y >= h - 1;
                    Color c = u < 0.5f ? Color.Lerp(new Color(1f, 0.15f, 0.12f), new Color(1f, 0.7f, 0.05f), u / 0.5f) : Color.Lerp(new Color(1f, 0.7f, 0.05f), Make.Bright, (u - 0.5f) / 0.5f);
                    c.a = gap ? 0f : 1f;
                    px[y * w + x] = c;
                }
            return Tex(w, h, px, false);
        }

        /// <summary>A square of solid colours (one per swatch) for the band: picked by uv, never blended.</summary>
        public static Texture2D Palette(Color[] swatches, int side)
        {
            var px = new Color[side * side];
            for (int i = 0; i < px.Length; i++) px[i] = i < swatches.Length ? swatches[i] : Color.magenta;
            return Tex(side, side, px, false, false, FilterMode.Point);
        }

        public static Texture2D White()
        {
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            return Tex(4, 4, px, false, false, FilterMode.Bilinear);
        }
    }
}
