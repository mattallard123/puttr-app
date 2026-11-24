// Putt Star's light show without real lights: every moving-head beam, lens flare, blinder and pool of light on the
// deck is a quad in one dynamic mesh, drawn additively in one call. A beam is a flat strip turned about its own axis
// to face the camera, bright down the middle and fading along its length (the haze catching it); seen end on it
// opens out and its lamp flares. The vertices are rewritten each frame into arrays made once, so nothing is allocated.
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    sealed class StarLights
    {
        public struct Beam
        {
            public Vector3 From, Aim;      // where the lamp is, the way it points at rest
            public float Length, Near, Far; // how long, how wide at the lamp and at the far end
            public float Flare;            // size of the lamp's glow
            public int Group;
            public Vector3 Dir;            // where it points now (set by the show)
            public Color Color;            // colour, with brightness in alpha (set by the show)
            public bool Pool;              // draws a pool of light where it meets the deck
        }

        public Beam[] Beams;
        readonly Vector3[] _v;
        readonly Vector2[] _uv;
        readonly Color[] _c;
        readonly Mesh _mesh;
        public readonly Material Material;
        readonly float _deckY;
        int _quads;

        // Blinders: lamps that face the crowd and flash on the big moments.
        public Vector3[] Blinders = new Vector3[0];
        public float Blind;
        public Color BlindColor = new Color(1f, 0.72f, 0.38f);

        public StarLights(Transform parent, Beam[] beams, Vector3[] blinders, Texture atlas, float deckY)
        {
            Beams = beams;
            Blinders = blinders;
            _deckY = deckY;
            int quads = beams.Length * 3 + blinders.Length;
            _v = new Vector3[quads * 4];
            _uv = new Vector2[quads * 4];
            _c = new Color[quads * 4];
            var tris = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                int a = q * 4, t = q * 6;
                tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 1;
                tris[t + 3] = a + 1; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
            }
            _mesh = new Mesh { name = "Light show" };
            _mesh.MarkDynamic();
            _mesh.vertices = _v;
            _mesh.uv = _uv;
            _mesh.colors = _c;
            _mesh.triangles = tris;
            _mesh.bounds = new Bounds(new Vector3(0, 80f, 0), new Vector3(800f, 400f, 800f));
            Material = Make.Glow(Color.white * 1.7f, true, atlas);
            Material.name = "Light show";
            Material.renderQueue = 3010;
            GameObject go = Make.MeshObject("Light show", parent, _mesh, Material);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Write every quad for a camera at eye (in the stage's coordinates).</summary>
        public void Draw(Vector3 eye, Vector3 camRight, Vector3 camUp)
        {
            _quads = 0;
            for (int i = 0; i < Beams.Length; i++)
            {
                ref Beam b = ref Beams[i];
                float power = b.Color.a;
                if (power < 0.003f) continue;
                Vector3 dir = b.Dir.sqrMagnitude > 1e-6f ? b.Dir.normalized : Vector3.down;
                Vector3 end = b.From + dir * b.Length;
                Vector3 toEye = eye - b.From;
                float dist = toEye.magnitude;
                Vector3 toEyeN = toEye / Mathf.Max(0.01f, dist);
                Vector3 side = Vector3.Cross(dir, toEyeN);
                float sideLen = side.magnitude;
                side = sideLen > 0.05f ? side / sideLen : camRight;
                float facing = Mathf.Clamp01(Vector3.Dot(dir, toEyeN));
                // Seen end on, a beam is shorter on screen and wider, and its lamp is blinding.
                Color c = b.Color;
                c.a = power * Mathf.Lerp(1f, 0.55f, facing);
                Quad(b.From + side * (b.Near * 0.5f), b.From - side * (b.Near * 0.5f), end + side * (b.Far * 0.5f), end - side * (b.Far * 0.5f), 0f, 0.5f, c);

                float flare = b.Flare * (0.6f + facing * facing * 1.8f);
                Color fc = Color.Lerp(b.Color, Color.white, 0.55f);
                fc.a = Mathf.Clamp01(power * (0.45f + facing * 1.2f));
                Billboard(b.From, flare, camRight, camUp, fc);

                if (b.Pool && dir.y < -0.15f)
                {
                    float k = (_deckY + 0.15f - b.From.y) / dir.y;
                    if (k > 0f && k < b.Length * 1.1f)
                    {
                        Vector3 hit = b.From + dir * k;
                        if (Mathf.Abs(hit.x) < 98f && hit.z > 1f && hit.z < 88f)
                        {
                            float r = Mathf.Lerp(b.Near, b.Far, k / b.Length) * 0.75f;
                            float stretch = 1f / Mathf.Max(0.35f, -dir.y);
                            Vector3 ax = new Vector3(dir.x, 0, dir.z);
                            ax = ax.sqrMagnitude > 1e-4f ? ax.normalized : Vector3.forward;
                            Vector3 bx = Vector3.Cross(Vector3.up, ax);
                            Color pc = b.Color;
                            pc.a = power * 0.55f * (1f - k / (b.Length * 1.1f));
                            Vector3 l = ax * (r * stretch), w = bx * r;
                            Quad(hit - l - w, hit - l + w, hit + l - w, hit + l + w, 0.5f, 1f, pc, true);
                        }
                    }
                }
            }
            if (Blind > 0.003f)
            {
                Color bc = BlindColor;
                bc.a = Mathf.Clamp01(Blind);
                foreach (Vector3 p in Blinders) Billboard(p, 9f + Blind * 6f, camRight, camUp, bc);
            }
            // Park the quads not used this frame.
            for (int q = _quads; q < _v.Length / 4; q++)
            {
                int a = q * 4;
                _c[a].a = _c[a + 1].a = _c[a + 2].a = _c[a + 3].a = 0f;
                _v[a] = _v[a + 1] = _v[a + 2] = _v[a + 3] = Vector3.zero;
            }
            _mesh.SetVertices(_v, 0, _v.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetUVs(0, _uv, 0, _uv.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetColors(_c, 0, _c.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        void Billboard(Vector3 at, float size, Vector3 right, Vector3 up, Color c)
        {
            Vector3 r = right * (size * 0.5f), u = up * (size * 0.5f);
            Quad(at - r - u, at + r - u, at - r + u, at + r + u, 0.5f, 1f, c, true);
        }

        // a and b at the near end (v = 0), c and d at the far end (v = 1). u0..u1 is the atlas half to use.
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float u1, Color col, bool square = false)
        {
            if (_quads * 4 >= _v.Length) return;
            int i = _quads * 4;
            _v[i] = a; _v[i + 1] = b; _v[i + 2] = c; _v[i + 3] = d;
            _uv[i] = new Vector2(u0, 0f); _uv[i + 1] = new Vector2(u1, 0f); _uv[i + 2] = new Vector2(u0, 1f); _uv[i + 3] = new Vector2(u1, 1f);
            _c[i] = _c[i + 1] = _c[i + 2] = _c[i + 3] = col;
            _quads++;
        }
    }
}
