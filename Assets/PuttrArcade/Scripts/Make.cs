// Everything in the scene is built in code: materials, textures and meshes.
// The project carries no art files.
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public static class Make
    {
        // Brand colors, from the Puttr design system.
        public static readonly Color Green = Hex(0x2D8C3C);      // primary
        public static readonly Color Bright = Hex(0x7AD446);     // bright accent
        public static readonly Color Deep = Hex(0x036B36);
        public static readonly Color Red = Hex(0xFF5252);
        public static readonly Color Amber = Hex(0xFFB300);
        public static readonly Color Ink = new Color(0.035f, 0.045f, 0.05f);

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        // ── Materials ───────────────────────────────────────────────────────
        //
        // The base materials are assets in Resources (made by Puttr > Set Up
        // Project) so that their shader variants are included in a build.

        static Material _lit, _litEmissive, _litNormal, _litTransparent;
        static Shader _glow;

        static Material Base(ref Material cache, string name)
        {
            if (cache == null) cache = Resources.Load<Material>("PuttrArcade/" + name);
            Material m = cache != null ? new Material(cache) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            // An old Apple chip draws the pipeline's Lit shader white: it gets the arcade's own (see PlainLit.shader).
            if (Arcade.Plain && PlainLights.Lit != null) m.shader = PlainLights.Lit;
            return m;
        }

        public static Material Lit(Color color, float smoothness = 0.5f, float metallic = 0f, Texture texture = null, Texture normal = null, float bump = 1f)
        {
            // An old Apple chip (Arcade.Plain) draws every normal-mapped surface pure white, whatever lights it (an A9X
            // iPad Pro, 2026-11-20: the mat and the floors, the only normal-mapped things, with every light off). There
            // the normal map is kept on the material and never switched on (the arcade's own shader there does not use one).
            bool bumps = normal != null && !Arcade.Plain;
            Material m = bumps ? Base(ref _litNormal, "LitNormal") : Base(ref _lit, "Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (texture != null) m.SetTexture("_BaseMap", texture);
            if (normal != null)
            {
                if (bumps) m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", bump);
            }
            return m;
        }

        /// <summary>Lit and see-through: color.a is how solid it is.</summary>
        public static Material Transparent(Color color, float smoothness, Texture texture = null)
        {
            Material m = Base(ref _litTransparent, "LitTransparent");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            if (texture != null) m.SetTexture("_BaseMap", texture);
            return m;
        }

        public static Material Emissive(Color color, Color emission, float smoothness = 0.5f)
        {
            Material m = Base(ref _litEmissive, "LitEmissive");
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_BaseColor", color);
            m.SetColor("_EmissionColor", emission);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>Unlit and see-through. Additive for light, alpha blended for paint.</summary>
        public static Material Glow(Color color, bool additive = true, Texture texture = null)
        {
            if (_glow == null) _glow = Shader.Find("PuttrArcade/Glow");
            var m = new Material(_glow);
            m.SetColor("_Color", color);
            m.SetFloat("_DstBlend", additive ? 1f : 10f);
            if (texture != null) m.SetTexture("_MainTex", texture);
            return m;
        }

        // ── Objects ─────────────────────────────────────────────────────────

        public static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material material, PhysicsMaterial physics = null, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            var box = go.GetComponent<BoxCollider>();
            if (!collider) Kill(box);
            else if (physics != null) box.sharedMaterial = physics;
            return go;
        }

        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        public static PhysicsMaterial Physics(float friction, float bounce)
        {
            return new PhysicsMaterial
            {
                dynamicFriction = friction,
                staticFriction = friction,
                bounciness = bounce,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Maximum,
            };
        }

        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        // ── Meshes ──────────────────────────────────────────────────────────

        /// <summary>
        /// A ribbon that follows a height profile along Z: the carpet. heightAt
        /// and slopeAt give the surface; the ribbon runs from x0 to x1.
        /// </summary>
        public static Mesh Ribbon(float z0, float z1, float x0, float x1, System.Func<float, float> heightAt, System.Func<float, float> slopeAt, float step, float uvPerInch)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt((z1 - z0) / step) + 1);
            var verts = new Vector3[n * 2];
            var normals = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n; i++)
            {
                float z = Mathf.Lerp(z0, z1, i / (float)(n - 1));
                float y = heightAt(z);
                Vector3 normal = new Vector3(0f, 1f, -slopeAt(z)).normalized;
                verts[i * 2] = new Vector3(x0, y, z);
                verts[i * 2 + 1] = new Vector3(x1, y, z);
                normals[i * 2] = normals[i * 2 + 1] = normal;
                // Mapped from above, so separate pieces of carpet line up.
                uvs[i * 2] = new Vector2(x0 * uvPerInch, z * uvPerInch);
                uvs[i * 2 + 1] = new Vector2(x1 * uvPerInch, z * uvPerInch);
                if (i == n - 1) break;
                int a = i * 2, t = i * 6;
                tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 1;
                tris[t + 3] = a + 1; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
            }
            var mesh = new Mesh { name = "Ribbon", vertices = verts, normals = normals, uv = uvs, triangles = tris };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A flat square of carpet in the XZ plane with a round hole in the
        /// middle, mapped like Ribbon. origin is where its center sits in the
        /// world before any tilt, for the mapping only.
        /// </summary>
        public static Mesh HolePatch(float half, float holeRadius, float uvPerInch, Vector3 origin, float cosTilt, int segments = 72)
        {
            var verts = new Vector3[(segments + 1) * 2];
            var normals = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segments * 6];
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                float reach = half / Mathf.Max(Mathf.Abs(c), Mathf.Abs(sn));
                verts[s * 2] = new Vector3(c * holeRadius, 0, sn * holeRadius);
                verts[s * 2 + 1] = new Vector3(c * reach, 0, sn * reach);
                for (int k = 0; k < 2; k++)
                {
                    Vector3 v = verts[s * 2 + k];
                    normals[s * 2 + k] = Vector3.up;
                    uvs[s * 2 + k] = new Vector2((origin.x + v.x) * uvPerInch, (origin.z + v.z * cosTilt) * uvPerInch);
                }
            }
            for (int s = 0; s < segments; s++)
            {
                int a = s * 2, t = s * 6;
                tris[t] = a; tris[t + 1] = a + 3; tris[t + 2] = a + 1;
                tris[t + 3] = a; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
            }
            var mesh = new Mesh { name = "Hole patch", vertices = verts, normals = normals, uv = uvs, triangles = tris };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The inside of a cup: a tube seen from within, closed at the bottom. The rim is at y = 0.</summary>
        public static Mesh Cup(float radius, float depth, bool closed = true, int segments = 48)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts.Add(dir * radius); verts.Add(dir * radius + Vector3.down * depth);
                normals.Add(-dir); normals.Add(-dir);
                uvs.Add(new Vector2(s / (float)segments, 1)); uvs.Add(new Vector2(s / (float)segments, 0));
            }
            for (int s = 0; s < segments; s++)
            {
                int a = s * 2;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
                tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 3);
            }
            if (closed)
            {
            int center = verts.Count;
            verts.Add(Vector3.down * depth); normals.Add(Vector3.up); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, -depth, Mathf.Sin(a) * radius));
                normals.Add(Vector3.up); uvs.Add(new Vector2(0.5f, 0.5f));
            }
            for (int s = 0; s < segments; s++) { tris.Add(center); tris.Add(center + 2 + s); tris.Add(center + 1 + s); }
            }
            var mesh = new Mesh { name = "Cup" };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A box with rounded edges, so they catch the light the way folded sheet metal does.</summary>
        public static Mesh RoundedBox(Vector3 size, float radius)
        {
            Vector3 h = size * 0.5f;
            radius = Mathf.Min(radius, Mathf.Min(h.x, Mathf.Min(h.y, h.z)));
            float[] Stops(float half) => new[] { -half, -half + radius * 0.3f, -half + radius * 0.65f, -half + radius, half - radius, half - radius * 0.65f, half - radius * 0.3f, half };
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // Each face: the axis it faces, and the two axes that span it.
            int[,] faces = { { 0, 2, 1, 1 }, { 0, 1, 2, -1 }, { 1, 0, 2, 1 }, { 1, 2, 0, -1 }, { 2, 1, 0, 1 }, { 2, 0, 1, -1 } };
            for (int f = 0; f < 6; f++)
            {
                int ax = faces[f, 0], au = faces[f, 1], av = faces[f, 2], sign = faces[f, 3];
                float[] us = Stops(h[au]), vs = Stops(h[av]);
                int start = verts.Count, n = us.Length;
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p = Vector3.zero;
                        p[ax] = sign * h[ax]; p[au] = us[i]; p[av] = vs[j];
                        var inner = new Vector3(Mathf.Clamp(p.x, -h.x + radius, h.x - radius), Mathf.Clamp(p.y, -h.y + radius, h.y - radius), Mathf.Clamp(p.z, -h.z + radius, h.z - radius));
                        Vector3 normal = (p - inner).normalized;
                        verts.Add(inner + normal * radius);
                        normals.Add(normal);
                        uvs.Add(new Vector2(us[i] / (2 * h[au]) + 0.5f, vs[j] / (2 * h[av]) + 0.5f));
                    }
                }
                for (int j = 0; j < n - 1; j++)
                {
                    for (int i = 0; i < n - 1; i++)
                    {
                        int a = start + j * n + i, b = a + 1, c = a + n, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
                }
            }
            var mesh = new Mesh { name = "Rounded box" };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A rounded box as an object, with a plain box collider if asked for.</summary>
        public static GameObject Panel(string name, Transform parent, Vector3 center, Vector3 size, float radius, Material material, PhysicsMaterial physics = null, bool collider = false)
        {
            GameObject go = MeshObject(name, parent, RoundedBox(size, radius), material);
            go.transform.localPosition = center;
            if (collider)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = size;
                if (physics != null) box.sharedMaterial = physics;
            }
            return go;
        }

        /// <summary>
        /// Spin a profile around the Y axis. Each profile point is (radius, height).
        /// </summary>
        public static Mesh Lathe(IList<Vector2> profile, int segments)
        {
            int rings = profile.Count;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float height = profile[rings - 1].y - profile[0].y;

            for (int r = 0; r < rings; r++)
            {
                Vector2 prev = profile[Mathf.Max(0, r - 1)], next = profile[Mathf.Min(rings - 1, r + 1)];
                Vector2 tangent = (next - prev).normalized;
                var n2 = new Vector2(tangent.y, -tangent.x); // outward in (radius, height)
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    verts.Add(new Vector3(profile[r].x * cos, profile[r].y, profile[r].x * sin));
                    normals.Add(new Vector3(n2.x * cos, n2.y, n2.x * sin).normalized);
                    uvs.Add(new Vector2(s / (float)segments, (profile[r].y - profile[0].y) / height));
                }
            }
            int stride = segments + 1;
            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * stride + s, b = a + 1, c = a + stride, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
            var mesh = new Mesh { name = "Lathe" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat ring in the XZ plane, facing up.</summary>
        public static Mesh Ring(float inner, float outer, int segments = 64)
        {
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segments * 6];
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts[s * 2] = dir * inner;
                verts[s * 2 + 1] = dir * outer;
                uvs[s * 2] = new Vector2(s / (float)segments, 0);
                uvs[s * 2 + 1] = new Vector2(s / (float)segments, 1);
            }
            for (int s = 0; s < segments; s++)
            {
                int a = s * 2, t = s * 6;
                tris[t] = a; tris[t + 1] = a + 3; tris[t + 2] = a + 1;
                tris[t + 3] = a; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
            }
            var mesh = new Mesh { name = "Ring", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Disc(float radius, int segments = 48) => Ring(0f, radius, segments);

        // ── Textures ────────────────────────────────────────────────────────

        internal static Texture2D NewTexture(int w, int h, bool repeat)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                anisoLevel = 8,
            };
        }

        /// <summary>Maple boards running along the lane (texture v).</summary>
        static readonly System.Collections.Generic.Dictionary<int, Texture2D> _woods = new System.Collections.Generic.Dictionary<int, Texture2D>();
        /// <summary>Floorboards. One texture a seed, made once and shared.</summary>
        public static Texture2D Wood(int seed = 7)
        {
            if (_woods.TryGetValue(seed, out Texture2D have) && have != null) return have;
            return _woods[seed] = WoodMade(seed);
        }

        static Texture2D WoodMade(int seed)
        {
            const int w = 512, h = 512, boards = 12;
            var rng = new System.Random(seed);
            var tones = new Color[boards];
            for (int i = 0; i < boards; i++)
            {
                float t = (float)rng.NextDouble();
                tones[i] = Color.Lerp(new Color(0.80f, 0.60f, 0.36f), new Color(0.93f, 0.76f, 0.50f), t);
            }
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w * boards;
                    int board = Mathf.Min(boards - 1, (int)u);
                    float edge = Mathf.Abs(u - board - 0.5f) * 2f;
                    float grain = Mathf.PerlinNoise(x * 0.35f + board * 13.1f, y * 0.012f + board * 3.7f) * 0.12f - 0.06f;
                    float fine = Mathf.PerlinNoise(x * 1.9f, y * 0.05f) * 0.05f - 0.025f;
                    Color c = tones[board] * (1f + grain + fine);
                    if (edge > 0.94f) c *= 0.72f; // the seam between boards
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            }
            Texture2D tex = NewTexture(w, h, true);
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        // Smooth noise that repeats every `period` cells, so textures tile.
        internal static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        internal static float TileNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period;
            int ya = ((y0 % period) + period) % period, yb = (ya + 1) % period;
            return Mathf.Lerp(Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), fx), Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), fx), fy);
        }

        // A normal map from a height field (wrapping at the edges).
        internal static Texture2D NormalFromHeight(float[] h, int w, int ht, float strength)
        {
            var px = new Color[w * ht];
            for (int y = 0; y < ht; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float l = h[y * w + (x + w - 1) % w], r = h[y * w + (x + 1) % w];
                    float d = h[((y + ht - 1) % ht) * w + x], u = h[((y + 1) % ht) * w + x];
                    Vector3 n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    px[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }
            var tex = new Texture2D(w, ht, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// A texture cut from one of the product photographs (a .bytes file in
        /// Resources), with a normal map worked out from its light and shade.
        /// Each file is the crop and its three mirror images, so it repeats
        /// without a visible join. False if the file is missing.
        /// </summary>
        public static bool Photo(string name, float bump, out Texture2D color, out Texture2D normal)
        {
            color = normal = null;
            var file = Resources.Load<TextAsset>("PuttrArcade/" + name);
            if (file == null) return false;
            color = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!color.LoadImage(file.bytes)) return false;
            color.wrapMode = TextureWrapMode.Repeat;
            color.anisoLevel = 8;
            Color[] px = color.GetPixels();
            var h = new float[px.Length];
            for (int i = 0; i < px.Length; i++) h[i] = px[i].grayscale * 4f;
            normal = NormalFromHeight(h, color.width, color.height, bump);
            return true;
        }

        /// <summary>The Puttr carpet: a short, dense pile. Returns color and normal maps.</summary>
        // Made once and shared: every scene that asks gets the same pair (they used to be painted again, pixel by
        // pixel, for every scene and every rebuild of one, and never freed).
        static Texture2D _carpetColor, _carpetNormal;
        public static void Carpet(out Texture2D color, out Texture2D normal)
        {
            if (_carpetColor == null || _carpetNormal == null) CarpetMade(out _carpetColor, out _carpetNormal);
            color = _carpetColor; normal = _carpetNormal;
        }

        static void CarpetMade(out Texture2D color, out Texture2D normal)
        {
            const int n = 512;
            var px = new Color[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float fiber = Hash(x, y, 1) * 0.6f + TileNoise(x * 0.5f, y * 0.5f, n / 2, 2) * 0.4f;
                    // The pile lies in soft patches that catch the light differently.
                    float lay = TileNoise(x / 64f, y / 64f, 8, 3) * 0.6f + TileNoise(x / 20f, y / 20f, 26, 4) * 0.4f;
                    float v = 0.72f + fiber * 0.22f + lay * 0.2f;
                    px[y * n + x] = new Color(v, v, v, 1f);
                    h[y * n + x] = fiber;
                }
            }
            color = NewTexture(n, n, true);
            color.SetPixels(px);
            color.Apply(true, true);
            normal = NormalFromHeight(h, n, n, 1.6f);
        }

        // How deep a golf ball's surface is at (u, v): 0 on the surface, 1 at the bottom of a dimple.
        static float Dimple(float u, float v)
        {
            float lat = (v - 0.5f) * Mathf.PI;
            float cosLat = Mathf.Max(0.12f, Mathf.Cos(lat));
            float row = v * 26f;
            int ri = Mathf.FloorToInt(row);
            float cols = Mathf.Max(5f, Mathf.Round(52f * cosLat));
            float col = u * cols + (ri % 2) * 0.5f;
            float dx = col - Mathf.Floor(col) - 0.5f, dy = row - ri - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / 0.42f;
            return d >= 1f ? 0f : Mathf.Cos(d * Mathf.PI * 0.5f);
        }

        /// <summary>A golf ball: white, dimpled, with one short green alignment line so the roll shows.</summary>
        // Made once and shared: every scene that asks gets the same pair (they used to be painted again, pixel by
        // pixel, for every scene and every rebuild of one, and never freed).
        static Texture2D _ballColor, _ballNormal;
        public static void Ball(out Texture2D color, out Texture2D normal)
        {
            if (_ballColor == null || _ballNormal == null) BallMade(out _ballColor, out _ballNormal);
            color = _ballColor; normal = _ballNormal;
        }

        static void BallMade(out Texture2D color, out Texture2D normal)
        {
            const int w = 1024, ht = 512;
            var px = new Color[w * ht];
            var h = new float[w * ht];
            for (int y = 0; y < ht; y++)
            {
                float v = y / (float)ht;
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w;
                    float d = Dimple(u, v);
                    h[y * w + x] = -d;
                    float shade = 1f - d * 0.05f;
                    var c = new Color(shade, shade, shade * 0.985f, 1f);
                    // The line: a short stroke on the equator.
                    if (Mathf.Abs(v - 0.5f) < 0.012f && Mathf.Abs(u - 0.5f) < 0.11f) c = Color.Lerp(c, Green, 0.9f);
                    px[y * w + x] = c;
                }
            }
            color = NewTexture(w, ht, true);
            color.SetPixels(px);
            color.Apply(true, true);
            normal = NormalFromHeight(h, w, ht, 3.2f);
        }

        /// <summary>A pin: white with two red bands at the neck. v runs up the pin.</summary>
        public static Texture2D Pin()
        {
            const int w = 4, h = 256;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)h;
                bool band = (v > 0.615f && v < 0.648f) || (v > 0.672f && v < 0.705f);
                Color c = band ? new Color(0.85f, 0.1f, 0.1f, 1f) : new Color(0.96f, 0.95f, 0.92f, 1f);
                for (int x = 0; x < w; x++) px[y * w + x] = c;
            }
            Texture2D tex = NewTexture(w, h, false);
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>A soft round dot, for glows and sparks.</summary>
        static Texture2D _softDot;
        public static Texture2D SoftDot() => _softDot != null ? _softDot : _softDot = SoftDotMade();

        static Texture2D SoftDotMade()
        {
            const int n = 64;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color(1, 1, 1, a * a);
                }
            }
            Texture2D tex = NewTexture(n, n, false);
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// A rounded rectangle that stretches cleanly, for interface panels.
        /// With outline, only a one pixel border is drawn.
        /// </summary>
        public static Sprite RoundedSprite(int radius = 16, bool outline = false, float line = 1.6f)
        {
            int n = radius * 2 + 4;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, n - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, n - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    if (outline) a *= Mathf.Clamp01(d - (radius - line) + 0.5f);
                    px[y * n + x] = new Color(1, 1, 1, a);
                }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        /// <summary>A small sky to reflect in the lane: dark, with a bright band overhead.</summary>
        public static Cubemap ReflectionSky()
        {
            const int n = 32;
            var cube = new Cubemap(n, TextureFormat.RGBA32, true);
            for (int face = 0; face < 6; face++)
            {
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        Vector3 dir;
                        switch ((CubemapFace)face)
                        {
                            case CubemapFace.PositiveX: dir = new Vector3(1, -v, -u); break;
                            case CubemapFace.NegativeX: dir = new Vector3(-1, -v, u); break;
                            case CubemapFace.PositiveY: dir = new Vector3(u, 1, v); break;
                            case CubemapFace.NegativeY: dir = new Vector3(u, -1, -v); break;
                            case CubemapFace.PositiveZ: dir = new Vector3(u, -v, 1); break;
                            default: dir = new Vector3(-u, -v, -1); break;
                        }
                        dir.Normalize();
                        float up = Mathf.Clamp01(dir.y);
                        Color c = Color.Lerp(new Color(0.03f, 0.04f, 0.05f), new Color(0.16f, 0.2f, 0.24f), up);
                        // Light bars across the ceiling.
                        if (dir.y > 0.55f && Mathf.Abs(Mathf.Sin(dir.z / Mathf.Max(0.2f, dir.y) * 3.2f)) > 0.86f) c += new Color(1.2f, 1.15f, 1.0f);
                        // A glow down the lane, toward the pins.
                        c += new Color(0.25f, 0.3f, 0.12f) * Mathf.Pow(Mathf.Clamp01(dir.z), 6f) * Mathf.Clamp01(1f - Mathf.Abs(dir.y) * 2f);
                        c.a = 1f;
                        px[y * n + x] = c;
                    }
                }
                cube.SetPixels(px, (CubemapFace)face);
            }
            cube.Apply(true, true);
            return cube;
        }
    }
}
