// Putt Star's crowd as two meshes: the people (dark, rim-lit silhouettes seen from behind) and the phone torches
// held up among them. Both are animated by the PuttStarCrowd shader, so a crowd of any size is two draw calls and
// no work on the CPU. See PuttStarCrowd.shader for what each vertex carries.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    sealed class StarCrowd
    {
        public Material People, Phones;
        public int Count;

        readonly List<Vector3> _pos = new List<Vector3>();
        readonly List<Vector4> _shape = new List<Vector4>(), _who = new List<Vector4>(), _more = new List<Vector4>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Color> _col = new List<Color>();
        readonly List<int> _tri = new List<int>();
        readonly List<Vector3> _ppos = new List<Vector3>();
        readonly List<Vector4> _pshape = new List<Vector4>(), _pwho = new List<Vector4>(), _pmore = new List<Vector4>();
        readonly List<Vector2> _puv = new List<Vector2>();
        readonly List<Color> _pcol = new List<Color>();
        readonly List<int> _ptri = new List<int>();
        readonly System.Random _rng = new System.Random(5);

        // The outline of a back (shoulders down to the hips), and of a head, in units for a person 1.0 tall.
        static readonly Vector2[] Back =
        {
            new Vector2(-2.5f, 0f), new Vector2(-2.45f, 7.5f), new Vector2(-2.9f, 12.3f), new Vector2(-3.65f, 15.0f), new Vector2(-3.35f, 16.2f),
            new Vector2(-2.2f, 16.8f), new Vector2(-0.95f, 17.1f), new Vector2(0.95f, 17.1f), new Vector2(2.2f, 16.8f), new Vector2(3.35f, 16.2f),
            new Vector2(3.65f, 15.0f), new Vector2(2.9f, 12.3f), new Vector2(2.45f, 7.5f), new Vector2(2.5f, 0f),
        };
        static readonly Vector2[] ArmShape =
        {
            new Vector2(-0.66f, 0.6f), new Vector2(0.66f, 0.6f), new Vector2(-0.52f, -4.6f), new Vector2(0.52f, -4.6f), new Vector2(-0.38f, -8.2f), new Vector2(0.38f, -8.2f),
            new Vector2(-0.56f, -8.9f), new Vector2(0.56f, -8.9f), new Vector2(-0.28f, -9.9f), new Vector2(0.28f, -9.9f),
        };
        const float ShoulderX = 3.05f, ShoulderY = 15.0f, Height = 20.8f;

        float R => (float)_rng.NextDouble();

        /// <summary>One person, feet at feet, scale tall, holding a phone up or not.</summary>
        public void Add(Vector3 feet, float scale, bool phone, float rateBoost = 1f)
        {
            float phase = R * 6.283f, rate = (5.6f + R * 2.4f) * rateBoost;
            float keen = 0.65f + R * 0.6f;
            if (R < 0.18f) keen = -keen; // one arm up only
            float wide = 0.88f + R * 0.28f;
            float g = R * 0.012f;
            Color clothes = new Color(g + R * 0.01f, g, g + R * 0.014f, 0f);
            Count++;

            // The back: a fan from its middle, the middle dark and the outline lit.
            int c = _pos.Count;
            AddVert(feet, new Vector2(0f, 9f) * scale, Vector2.zero, phase, rate, 0f, keen, scale, clothes, 0f);
            foreach (Vector2 p in Back) AddVert(feet, new Vector2(p.x * wide, p.y) * scale, Vector2.zero, phase, rate, 0f, keen, scale, clothes, 1f);
            for (int i = 0; i < Back.Length - 1; i++) { _tri.Add(c); _tri.Add(c + 1 + i); _tri.Add(c + 2 + i); }
            _tri.Add(c); _tri.Add(c + Back.Length); _tri.Add(c + 1);

            // The head, a little different on everyone (hair).
            float hw = 1.25f + R * 0.35f, hh = 1.55f + R * 0.25f, hy = 18.9f + R * 0.3f;
            c = _pos.Count;
            AddVert(feet, new Vector2(0f, hy) * scale, Vector2.zero, phase, rate, 0f, keen, scale, clothes, 0f);
            const int ring = 16;
            for (int i = 0; i < ring; i++)
            {
                float a = i / (float)ring * Mathf.PI * 2f;
                AddVert(feet, new Vector2(Mathf.Cos(a) * hw, hy + Mathf.Sin(a) * hh) * scale, Vector2.zero, phase, rate, 0f, keen, scale, clothes, 1f);
            }
            for (int i = 0; i < ring; i++) { _tri.Add(c); _tri.Add(c + 1 + i); _tri.Add(c + 1 + (i + 1) % ring); }

            // Arms: hung from the shoulders, turned up by the shader.
            for (int side = -1; side <= 1; side += 2)
            {
                c = _pos.Count;
                Vector2 pivot = new Vector2(side * ShoulderX * wide, ShoulderY) * scale;
                foreach (Vector2 p in ArmShape) AddVert(feet, p * scale, pivot, phase, rate, side, keen, scale, clothes, 0.75f);
                for (int i = 0; i < ArmShape.Length - 2; i += 2)
                {
                    _tri.Add(c + i); _tri.Add(c + i + 1); _tri.Add(c + i + 2);
                    _tri.Add(c + i + 1); _tri.Add(c + i + 3); _tri.Add(c + i + 2);
                }
            }

            if (phone)
            {
                c = _ppos.Count;
                Vector2 pivot = new Vector2(ShoulderX * wide, ShoulderY) * scale, hand = new Vector2(0f, -9.6f) * scale;
                float size = 1.25f * scale;
                var seed = new Color(R, 0, 0, 0);
                for (int k = 0; k < 4; k++)
                {
                    var corner = new Vector2(k % 2 == 0 ? -size : size, k < 2 ? -size : size);
                    _ppos.Add(feet);
                    _pshape.Add(new Vector4(hand.x, hand.y, pivot.x, pivot.y));
                    _pwho.Add(new Vector4(phase, rate, 1f, keen));
                    _pmore.Add(new Vector4(corner.x, corner.y + 0.4f * scale, Height * scale, 1f));
                    _puv.Add(new Vector2(0.5f + (k % 2 == 0 ? 0f : 0.5f), k < 2 ? 0f : 1f));
                    _pcol.Add(seed);
                }
                _ptri.Add(c); _ptri.Add(c + 2); _ptri.Add(c + 1);
                _ptri.Add(c + 1); _ptri.Add(c + 2); _ptri.Add(c + 3);
            }
        }

        void AddVert(Vector3 feet, Vector2 offset, Vector2 pivot, float phase, float rate, float arm, float keen, float scale, Color clothes, float edge)
        {
            _pos.Add(feet);
            _shape.Add(new Vector4(offset.x, offset.y, pivot.x, pivot.y));
            // A phone holder's raising arm is the right one; mark the person so the shader keeps it up.
            _who.Add(new Vector4(phase, rate, arm, keen));
            _more.Add(new Vector4(0f, 0f, Height * scale, 0f));
            _uv.Add(Vector2.zero);
            clothes.a = edge;
            _col.Add(clothes);
        }

        /// <summary>Mark the last person added as holding a phone up (their right arm stays raised).</summary>
        void MarkPhone(int from)
        {
            for (int i = from; i < _more.Count; i++) { Vector4 m = _more[i]; m.w = 1f; _more[i] = m; }
        }

        public void AddWithPhone(Vector3 feet, float scale, float rateBoost = 1f)
        {
            int from = _pos.Count;
            Add(feet, scale, true, rateBoost);
            MarkPhone(from);
        }

        public void Build(Transform parent, Texture phoneDot)
        {
            Shader shader = Shader.Find("PuttrArcade/PuttStarCrowd");
            if (shader == null) { Debug.LogWarning("Putt Star: crowd shader missing"); return; }
            People = new Material(shader) { name = "Crowd" };
            People.SetFloat("_Mode", 0f);
            People.SetFloat("_SrcBlend", (float)BlendMode.One);
            People.SetFloat("_DstBlend", (float)BlendMode.Zero);
            People.SetFloat("_ZWrite", 1f);
            Phones = new Material(shader) { name = "Crowd phones", renderQueue = 3005 };
            Phones.SetFloat("_Mode", 1f);
            Phones.SetFloat("_SrcBlend", (float)BlendMode.One);
            Phones.SetFloat("_DstBlend", (float)BlendMode.One);
            Phones.SetFloat("_ZWrite", 0f);
            Phones.SetTexture("_MainTex", phoneDot);
            Phones.SetColor("_Glow", new Color(0.85f, 0.92f, 1f) * 1.3f);

            MakeMesh(parent, "Crowd", People, _pos, _shape, _who, _more, _uv, _col, _tri);
            MakeMesh(parent, "Crowd phones", Phones, _ppos, _pshape, _pwho, _pmore, _puv, _pcol, _ptri);
        }

        static void MakeMesh(Transform parent, string name, Material mat, List<Vector3> pos, List<Vector4> shape, List<Vector4> who, List<Vector4> more, List<Vector2> uv, List<Color> col, List<int> tri)
        {
            if (pos.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(pos);
            mesh.SetUVs(0, shape);
            mesh.SetUVs(1, who);
            mesh.SetUVs(2, more);
            mesh.SetUVs(3, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            Bounds b = mesh.bounds;
            b.Expand(new Vector3(30f, 60f, 10f));
            b.center += Vector3.up * 20f;
            mesh.bounds = b;
            GameObject go = Make.MeshObject(name, parent, mesh, mat);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
