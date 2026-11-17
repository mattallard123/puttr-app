// What the stage games are dressed with: surfaces, a ball, planets, and a
// jointed figure for the keeper and the rider. All of it is made in code,
// like everything else here.
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public static class Props
    {
        // ── Meshes ──────────────────────────────────────────────────────────

        /// <summary>An arm or a leg: it hangs down from its origin, round at both ends, r0 at the top.</summary>
        public static Mesh Limb(float length, float r0, float r1)
        {
            var profile = new List<Vector2>();
            for (int i = 0; i <= 5; i++)
            {
                float a = Mathf.Lerp(-90f, 0f, i / 5f) * Mathf.Deg2Rad;
                profile.Add(new Vector2(Mathf.Max(0.001f, r1 * Mathf.Cos(a)), -length + r1 * Mathf.Sin(a)));
            }
            for (int i = 0; i <= 5; i++)
            {
                float a = Mathf.Lerp(0f, 90f, i / 5f) * Mathf.Deg2Rad;
                profile.Add(new Vector2(Mathf.Max(0.001f, r0 * Mathf.Cos(a)), r0 * Mathf.Sin(a)));
            }
            return Make.Lathe(profile, 16);
        }

        /// <summary>A ring of tube, lying flat (its axis is Y).</summary>
        public static Mesh Torus(float radius, float tube, int around = 56, int across = 12)
        {
            var profile = new List<Vector2>();
            for (int i = 0; i <= across; i++)
            {
                float a = (i / (float)across * 360f - 90f) * Mathf.Deg2Rad;
                profile.Add(new Vector2(radius + tube * Mathf.Cos(a), tube * Mathf.Sin(a)));
            }
            // Lathe wants a profile that climbs; a closed one climbs then falls,
            // so its normals are set here from the tube's own center.
            Mesh mesh = Make.Lathe(profile, around);
            Vector3[] verts = mesh.vertices;
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var flat = new Vector3(verts[i].x, 0, verts[i].z);
                normals[i] = (verts[i] - flat.normalized * radius).normalized;
            }
            mesh.normals = normals;
            // Both faces, since half the profile runs the other way.
            int[] tris = mesh.triangles;
            var both = new int[tris.Length * 2];
            tris.CopyTo(both, 0);
            for (int i = 0; i < tris.Length; i += 3) { both[tris.Length + i] = tris[i]; both[tris.Length + i + 1] = tris[i + 2]; both[tris.Length + i + 2] = tris[i + 1]; }
            mesh.triangles = both;
            return mesh;
        }

        /// <summary>A tube from a to b.</summary>
        public static GameObject Rod(string name, Transform parent, Vector3 a, Vector3 b, float radius, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Make.Kill(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (a + b) * 0.5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            go.transform.localScale = new Vector3(radius * 2f, (b - a).magnitude * 0.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        // ── Textures ────────────────────────────────────────────────────────

        // Noise on a sphere with no seam: three flat noises, one for each pair of axes.
        static float Sphere(Vector3 p, float frequency, int seed)
        {
            p = p * frequency + Vector3.one * 64f;
            return (Make.TileNoise(p.x, p.y, 4096, seed) + Make.TileNoise(p.y, p.z, 4096, seed + 1) + Make.TileNoise(p.z, p.x, 4096, seed + 2)) / 3f;
        }

        static float Cloud(Vector3 p, float frequency, int seed)
        {
            float sum = 0f, weight = 0.5f;
            for (int i = 0; i < 5; i++) { sum += Sphere(p, frequency, seed + i * 7) * weight; frequency *= 2.1f; weight *= 0.5f; }
            return sum / 0.97f;
        }

        static float Flat(float x, float y, int period, int seed)
        {
            float sum = 0f, weight = 0.5f;
            int p = period;
            for (int i = 0; i < 4; i++) { sum += Make.TileNoise(x, y, p, seed + i) * weight; x *= 2f; y *= 2f; p *= 2; weight *= 0.5f; }
            return sum / 0.94f;
        }

        static Vector3 OnSphere(float u, float v)
        {
            float lat = (v - 0.5f) * Mathf.PI, lon = u * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
        }

        /// <summary>
        /// Turf, mown in two bands across v, with the blades standing up in
        /// the normal map. One tile is two bands: 24 feet.
        /// </summary>
        public static void Grass(out Texture2D color, out Texture2D normal)
        {
            const int n = 512;
            var px = new Color[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                // The mower lays the grass one way, then the other.
                float band = y < n / 2 ? 1f : 0.8f;
                float edge = Mathf.Min(Mathf.Abs(y - n / 2), Mathf.Min(y, n - y)) / 3f;
                if (edge < 1f) band = Mathf.Lerp(0.9f, band, edge);
                for (int x = 0; x < n; x++)
                {
                    float blades = Make.TileNoise(x * 0.97f, y * 0.61f, 512, 3);
                    float fine = Make.TileNoise(x * 0.5f, y * 0.5f, 256, 9);
                    float patch = Flat(x / 64f, y / 64f, 8, 21);
                    float tone = band * (0.9f + blades * 0.14f + fine * 0.06f) * (0.92f + patch * 0.16f);
                    px[y * n + x] = new Color(0.2f * tone, 0.5f * tone, 0.13f * tone + patch * 0.02f, 1f);
                    h[y * n + x] = blades * 0.7f + fine * 0.3f;
                }
            }
            color = Make.NewTexture(n, n, true);
            color.SetPixels(px);
            color.Apply(true);
            normal = Make.NormalFromHeight(h, n, n, 1.1f);
        }

        /// <summary>Poured concrete: pale, blotched and a little pitted.</summary>
        public static void Concrete(out Texture2D color, out Texture2D normal)
        {
            const int n = 512;
            var px = new Color[n * n];
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float blotch = Flat(x / 96f, y / 96f, 6, 31);
                    float grit = Make.TileNoise(x * 0.8f, y * 0.8f, 512, 5);
                    float pit = Mathf.Max(0f, Make.TileNoise(x * 0.21f, y * 0.21f, 128, 17) - 0.78f) * 4f;
                    float tone = 0.3f + blotch * 0.2f + grit * 0.06f - pit * 0.2f;
                    px[y * n + x] = new Color(tone, tone * 1.01f, tone * 1.04f, 1f);
                    h[y * n + x] = grit * 0.35f + blotch * 0.3f - pit;
                }
            }
            color = Make.NewTexture(n, n, true);
            color.SetPixels(px);
            color.Apply(true);
            normal = Make.NormalFromHeight(h, n, n, 1.6f);
        }

        /// <summary>A football: white, with the twelve black patches.</summary>
        public static Texture2D Football()
        {
            const int w = 512, ht = 256;
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var corners = new List<Vector3>();
            foreach (float a in new[] { -1f, 1f })
                foreach (float b in new[] { -t, t })
                {
                    corners.Add(new Vector3(0, a, b).normalized);
                    corners.Add(new Vector3(a, b, 0).normalized);
                    corners.Add(new Vector3(b, 0, a).normalized);
                }
            var px = new Color[w * ht];
            for (int y = 0; y < ht; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Vector3 p = OnSphere((x + 0.5f) / w, (y + 0.5f) / ht);
                    float best = -1f, second = -1f;
                    foreach (Vector3 c in corners)
                    {
                        float d = Vector3.Dot(p, c);
                        if (d > best) { second = best; best = d; } else if (d > second) second = d;
                    }
                    float angle = Mathf.Acos(Mathf.Clamp(best, -1f, 1f)) * Mathf.Rad2Deg;
                    float shade = angle < 19f ? 0.03f : angle < 20.5f ? 0.5f : 0.93f;
                    // A seam where two patches meet.
                    if (angle >= 20.5f && best - second < 0.012f) shade = 0.6f;
                    px[y * w + x] = new Color(shade, shade, shade, 1f);
                }
            }
            Texture2D tex = Make.NewTexture(w, ht, true);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// A planet's surface. Banded for a gas giant (storms pulled out along
        /// the latitudes), or rough for a rock with seas and high ground.
        /// </summary>
        public static void Planet(int seed, Color low, Color mid, Color high, bool banded, out Texture2D color, out Texture2D normal)
        {
            const int w = 512, ht = 256;
            var px = new Color[w * ht];
            var h = new float[w * ht];
            for (int y = 0; y < ht; y++)
            {
                float v = (y + 0.5f) / ht;
                for (int x = 0; x < w; x++)
                {
                    Vector3 p = OnSphere((x + 0.5f) / w, v);
                    float s, height;
                    if (banded)
                    {
                        float swirl = Cloud(new Vector3(p.x, p.y * 3.5f, p.z), 2.2f, seed);
                        float lat = p.y * 6.5f + (swirl - 0.5f) * 1.6f;
                        s = 0.5f + 0.5f * Mathf.Sin(lat * Mathf.PI) * (0.6f + 0.4f * Cloud(p, 1.3f, seed + 40));
                        s = Mathf.Lerp(s, swirl, 0.35f);
                        height = s * 0.15f;
                    }
                    else
                    {
                        s = Cloud(p, 2.4f, seed);
                        s = Mathf.SmoothStep(0.32f, 0.72f, s);
                        float craters = Mathf.Max(0f, Cloud(p, 9f, seed + 60) - 0.62f) * 3f;
                        height = s + Cloud(p, 14f, seed + 80) * 0.25f - craters * 0.5f;
                        s = Mathf.Clamp01(s - craters * 0.25f);
                    }
                    Color c = s < 0.5f ? Color.Lerp(low, mid, s * 2f) : Color.Lerp(mid, high, s * 2f - 2f * 0.5f);
                    c.a = 1f;
                    px[y * w + x] = c;
                    h[y * w + x] = height;
                }
            }
            color = Make.NewTexture(w, ht, true);
            color.SetPixels(px);
            color.Apply(true);
            normal = Make.NormalFromHeight(h, w, ht, banded ? 1.2f : 5f);
        }

        /// <summary>The thin bright edge of an atmosphere, to lay over a planet's disc.</summary>
        public static Texture2D Rim()
        {
            const int n = 256;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    // The planet's edge is at 0.86 of the card.
                    float inside = Mathf.Pow(Mathf.Clamp01(d / 0.86f), 7f) * 0.55f;
                    float outside = Mathf.Pow(Mathf.Clamp01(1f - (d - 0.86f) / 0.14f), 3f) * 0.55f;
                    px[y * n + x] = new Color(1, 1, 1, d < 0.86f ? inside : d < 1f ? outside : 0f);
                }
            }
            Texture2D tex = Make.NewTexture(n, n, false);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>One lick of flame: bright in the middle, ragged and wispy at the edge.</summary>
        public static Texture2D Flame(int seed)
        {
            const int n = 128;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(u * u * 1.25f + v * v) * 2f;
                    float wisp = Flat(x / 22f, y / 30f, 6, seed);
                    float a = Mathf.Clamp01(1.25f - d - (1f - wisp) * 0.75f);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color(1, 1, 1, a * Mathf.Clamp01((1f - d) * 3f));
                }
            }
            Texture2D tex = Make.NewTexture(n, n, false);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>A drift of gas a long way off.</summary>
        public static Texture2D Nebula(int seed)
        {
            const int n = 256;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float cloud = Flat(x / 48f, y / 48f, 8, seed);
                    float a = Mathf.Clamp01((cloud - 0.38f) * 2.4f) * Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color(1, 1, 1, a);
                }
            }
            Texture2D tex = Make.NewTexture(n, n, false);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }

    /// <summary>
    /// A jointed figure, six feet and a bit, facing the camera (down -Z). Its
    /// origin is the hips. Limbs hang from their joints: turn a joint about Z
    /// to swing a limb sideways, about X to swing it forward (positive) or
    /// back.
    /// </summary>
    public sealed class Figure
    {
        public struct Look
        {
            public Material Top, Bottom, Skin, Sock, Boot, Glove, Hair;
            /// <summary>If set, a helmet of this material instead of a bare head.</summary>
            public Material Helmet, Visor;
        }

        /// <summary>From the hips to the ground when standing straight.</summary>
        public const float HipHeight = 38f;
        /// <summary>From the hips to the hands when both arms are straight up.</summary>
        public const float Reach = 47f;

        public readonly Transform Root, Chest, Head;
        public readonly Transform[] Arm = new Transform[2], Fore = new Transform[2], Leg = new Transform[2], Shin = new Transform[2];

        public Figure(Transform parent, Look look)
        {
            Root = Joint("Figure", parent, Vector3.zero);
            Part(Make.RoundedBox(new Vector3(13.2f, 8.5f, 8.6f), 3.4f), "Hips", Root, new Vector3(0, -0.5f, 0), look.Bottom);

            Chest = Joint("Chest", Root, new Vector3(0, 3f, 0));
            Part(Make.RoundedBox(new Vector3(15.6f, 20.5f, 9.2f), 4.2f), "Torso", Chest, new Vector3(0, 10.2f, 0), look.Top);
            Ball("Neck", Chest, new Vector3(0, 21.6f, 0), new Vector3(4.6f, 5f, 4.6f), look.Skin);
            Head = Joint("Head", Chest, new Vector3(0, 23.5f, 0));
            if (look.Helmet != null)
            {
                Ball("Helmet", Head, new Vector3(0, 5f, 0.4f), new Vector3(11.6f, 11.6f, 12.6f), look.Helmet);
                Ball("Visor", Head, new Vector3(0, 4.4f, -2.2f), new Vector3(9.8f, 5.6f, 9.6f), look.Visor);
                Part(Make.RoundedBox(new Vector3(7f, 3.4f, 5f), 1.4f), "Chin guard", Head, new Vector3(0, 0.6f, -4.6f), look.Helmet);
            }
            else
            {
                Ball("Face", Head, new Vector3(0, 4.8f, 0), new Vector3(8.4f, 10.2f, 9.2f), look.Skin);
                Ball("Hair", Head, new Vector3(0, 6.5f, 1.1f), new Vector3(8.9f, 8.2f, 9.6f), look.Hair);
            }

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Arm[i] = Joint("Shoulder", Chest, new Vector3(side * 9.4f, 18.4f, 0));
                Ball("Shoulder cap", Arm[i], Vector3.zero, Vector3.one * 6.4f, look.Top);
                Part(Props.Limb(12f, 2.6f, 2.1f), "Upper arm", Arm[i], Vector3.zero, look.Top);
                Fore[i] = Joint("Elbow", Arm[i], new Vector3(0, -12f, 0));
                Part(Props.Limb(10.5f, 2.1f, 1.7f), "Forearm", Fore[i], Vector3.zero, look.Glove != null ? look.Top : look.Skin);
                Ball("Hand", Fore[i], new Vector3(0, -13.2f, 0), look.Glove != null ? new Vector3(5.4f, 7.4f, 3.6f) : new Vector3(3.6f, 5.4f, 2.4f), look.Glove != null ? look.Glove : look.Skin);

                Leg[i] = Joint("Hip", Root, new Vector3(side * 3.9f, -2f, 0));
                Part(Props.Limb(8.5f, 3.9f, 3.7f), "Shorts leg", Leg[i], Vector3.zero, look.Bottom);
                Part(Props.Limb(17f, 3.5f, 2.7f), "Thigh", Leg[i], Vector3.zero, look.Sock == look.Bottom ? look.Bottom : look.Skin);
                Shin[i] = Joint("Knee", Leg[i], new Vector3(0, -17f, 0));
                Part(Props.Limb(16.6f, 2.8f, 2f), "Shin", Shin[i], Vector3.zero, look.Sock);
                Part(Make.RoundedBox(new Vector3(4f, 3f, 10f), 1.4f), "Boot", Shin[i], new Vector3(0, -17.6f, -2.6f), look.Boot);
            }
        }

        static Transform Joint(string name, Transform parent, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        static void Part(Mesh mesh, string name, Transform parent, Vector3 at, Material material)
        {
            Make.MeshObject(name, parent, mesh, material).transform.localPosition = at;
        }

        static void Ball(string name, Transform parent, Vector3 at, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Make.Kill(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
