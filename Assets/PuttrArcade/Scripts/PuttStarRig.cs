// Putt Star's band: stylised people of real proportions (about eight heads tall), each one skinned mesh on a small
// skeleton, so a performer is one draw call however many parts they are made of. Every part is bound to one bone;
// colours come from a palette texture shared by the whole band (one material).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    sealed class StarRig
    {
        // Swatches in the shared palette.
        public const int SkinLight = 0, SkinMid = 1, SkinDeep = 2, Black = 3, Denim = 4, Leather = 5, WhiteTee = 6, RedShirt = 7,
            HairDark = 8, HairBlond = 9, HairRed = 10, Chrome = 11, Sunburst = 12, Cream = 13, BassBody = 14, Grey = 15, Olive = 16, Lens = 17;

        public static readonly Color[] Swatches =
        {
            new Color(0.86f, 0.66f, 0.53f), new Color(0.66f, 0.45f, 0.32f), new Color(0.36f, 0.23f, 0.16f), new Color(0.03f, 0.03f, 0.035f),
            new Color(0.12f, 0.15f, 0.22f), new Color(0.07f, 0.06f, 0.06f), new Color(0.8f, 0.8f, 0.78f), new Color(0.45f, 0.06f, 0.07f),
            new Color(0.06f, 0.045f, 0.035f), new Color(0.75f, 0.6f, 0.35f), new Color(0.45f, 0.17f, 0.07f), new Color(0.75f, 0.76f, 0.8f),
            new Color(0.62f, 0.12f, 0.04f), new Color(0.9f, 0.86f, 0.75f), new Color(0.04f, 0.05f, 0.08f), new Color(0.3f, 0.3f, 0.32f),
            new Color(0.2f, 0.22f, 0.15f), new Color(0.02f, 0.02f, 0.02f),
        };
        const int PaletteSide = 8;

        public struct Look
        {
            public int Skin, Shirt, Sleeve, Fore, Pants, Hair, Shoes;
            public bool LongHair, Beard, Shades;
            public float Width;
        }

        public Transform Root, Hips, Chest, Head, UpperL, LowerL, UpperR, LowerR, ThighL, ShinL, ThighR, ShinR;
        public Vector3 Home;

        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<BoneWeight> _w = new List<BoneWeight>();
        readonly List<int> _t = new List<int>();
        Transform[] _bones;

        static Material _material;
        public static Material Material
        {
            get
            {
                if (_material == null) { _material = Make.Lit(Color.white, 0.38f, 0f, StarKit.Palette(Swatches, PaletteSide)); _material.name = "Band"; }
                return _material;
            }
        }

        // The skeleton, standing, arms down. Heights in stage units: the top of the head is about 23.
        public const float ShoulderY = 18.3f, ShoulderX = 2.35f, ElbowY = 14.0f, HipY = 11.6f, HipX = 1.05f, KneeY = 6.3f, NeckY = 19.4f;

        public static StarRig Build(string name, Transform parent, Vector3 feet, Look look, float scale = 1f)
        {
            var rig = new StarRig();
            rig.Root = new GameObject(name).transform;
            rig.Root.SetParent(parent, false);
            rig.Root.localPosition = feet;
            rig.Root.localScale = Vector3.one * scale;
            rig.Home = feet;
            Transform Bone(string n, Transform p, Vector3 at) { var t = new GameObject(n).transform; t.SetParent(p, false); t.position = rig.Root.TransformPoint(at); return t; }
            rig.Hips = Bone("Hips", rig.Root, new Vector3(0, 12.2f, 0));
            rig.Chest = Bone("Chest", rig.Hips, new Vector3(0, 13.4f, 0));
            rig.Head = Bone("Head", rig.Chest, new Vector3(0, NeckY, 0));
            rig.UpperL = Bone("Upper L", rig.Chest, new Vector3(ShoulderX, ShoulderY, 0));
            rig.LowerL = Bone("Lower L", rig.UpperL, new Vector3(ShoulderX + 0.1f, ElbowY, 0));
            rig.UpperR = Bone("Upper R", rig.Chest, new Vector3(-ShoulderX, ShoulderY, 0));
            rig.LowerR = Bone("Lower R", rig.UpperR, new Vector3(-ShoulderX - 0.1f, ElbowY, 0));
            rig.ThighL = Bone("Thigh L", rig.Hips, new Vector3(HipX, HipY, 0));
            rig.ShinL = Bone("Shin L", rig.ThighL, new Vector3(HipX + 0.05f, KneeY, 0));
            rig.ThighR = Bone("Thigh R", rig.Hips, new Vector3(-HipX, HipY, 0));
            rig.ShinR = Bone("Shin R", rig.ThighR, new Vector3(-HipX - 0.05f, KneeY, 0));
            rig._bones = new[] { rig.Hips, rig.Chest, rig.Head, rig.UpperL, rig.LowerL, rig.UpperR, rig.LowerR, rig.ThighL, rig.ShinL, rig.ThighR, rig.ShinR };
            rig.Body(look);
            return rig;
        }

        public const int BHips = 0, BChest = 1, BHead = 2, BUpperL = 3, BLowerL = 4, BUpperR = 5, BLowerR = 6, BThighL = 7, BShinL = 8, BThighR = 9, BShinR = 10;

        /// <summary>Add a part, in the rig's standing coordinates, bound to one bone.</summary>
        public void Part(Mesh mesh, Matrix4x4 m, int bone, int swatch)
        {
            Vector3[] v = mesh.vertices, n = mesh.normals;
            int[] t = mesh.triangles;
            int start = _v.Count;
            Matrix4x4 nm = m.inverse.transpose;
            var uv = new Vector2((swatch % PaletteSide + 0.5f) / PaletteSide, (swatch / PaletteSide + 0.5f) / PaletteSide);
            var w = new BoneWeight { boneIndex0 = bone, weight0 = 1f };
            for (int i = 0; i < v.Length; i++)
            {
                _v.Add(m.MultiplyPoint3x4(v[i]));
                _n.Add(nm.MultiplyVector(n[i]).normalized);
                _uv.Add(uv);
                _w.Add(w);
            }
            bool flip = m.determinant < 0f;
            for (int i = 0; i < t.Length; i += 3)
            {
                _t.Add(start + t[i]);
                _t.Add(start + (flip ? t[i + 2] : t[i + 1]));
                _t.Add(start + (flip ? t[i + 1] : t[i + 2]));
            }
        }

        public void Part(Mesh mesh, Vector3 at, Vector3 size, int bone, int swatch, Quaternion? rot = null) =>
            Part(mesh, Matrix4x4.TRS(at, rot ?? Quaternion.identity, size), bone, swatch);

        static Mesh _torso, _pelvis, _upper, _fore, _thigh, _shin, _neck;

        void Body(Look k)
        {
            float wd = k.Width <= 0f ? 1f : k.Width;
            if (_torso == null)
            {
                _torso = Make.Lathe(new List<Vector2>
                {
                    new Vector2(0.0005f, 12.5f), new Vector2(1.55f, 12.7f), new Vector2(1.68f, 13.5f), new Vector2(1.8f, 14.6f), new Vector2(2.08f, 16.2f),
                    new Vector2(2.25f, 17.4f), new Vector2(2.18f, 18.3f), new Vector2(1.7f, 18.95f), new Vector2(0.75f, 19.35f), new Vector2(0.0005f, 19.45f),
                }, 14);
                _pelvis = Make.Lathe(new List<Vector2>
                {
                    new Vector2(0.0005f, 10.4f), new Vector2(1.3f, 10.7f), new Vector2(1.72f, 11.5f), new Vector2(1.78f, 12.4f), new Vector2(1.62f, 13.1f), new Vector2(0.0005f, 13.2f),
                }, 14);
                _upper = StarKit.Limb(4.25f, 0.72f, 0.56f);
                _fore = StarKit.Limb(3.85f, 0.56f, 0.42f);
                _thigh = StarKit.Limb(5.25f, 1.05f, 0.74f);
                _shin = StarKit.Limb(5.3f, 0.74f, 0.5f);
                _neck = StarKit.Limb(1.3f, 0.62f, 0.66f, 8);
            }
            Mesh ball = StarKit.Ball;
            Part(_torso, Matrix4x4.Scale(new Vector3(wd, 1f, 0.62f)), BChest, k.Shirt);
            Part(_pelvis, Matrix4x4.Scale(new Vector3(wd, 1f, 0.72f)), BHips, k.Pants);
            Part(StarKit.RoundTube, Matrix4x4.TRS(new Vector3(0, 12.85f, 0), Quaternion.identity, new Vector3(1.66f * wd, 0.4f, 1.66f * 0.72f)), BHips, Black); // belt
            Part(_neck, new Vector3(0, 20.5f, 0.05f), Vector3.one, BHead, k.Skin);
            Part(ball, new Vector3(0, 21.75f, -0.05f), new Vector3(2.25f, 2.8f, 2.5f), BHead, k.Skin);
            Part(ball, new Vector3(0, 21.55f, -1.25f), new Vector3(0.38f, 0.6f, 0.45f), BHead, k.Skin); // nose
            for (int s = -1; s <= 1; s += 2) Part(ball, new Vector3(s * 1.12f, 21.7f, 0.05f), new Vector3(0.35f, 0.7f, 0.5f), BHead, k.Skin); // ears
            Part(ball, new Vector3(0, 22.45f, 0.2f), new Vector3(2.45f, 2.3f, 2.68f), BHead, k.Hair);
            if (k.LongHair) Part(ball, new Vector3(0, 20.4f, 0.75f), new Vector3(2.7f, 4.6f, 1.7f), BHead, k.Hair);
            if (k.Beard) Part(ball, new Vector3(0, 20.85f, -0.55f), new Vector3(1.95f, 1.5f, 1.7f), BHead, k.Hair);
            if (k.Shades) Part(StarKit.Cube, new Vector3(0, 22.0f, -1.12f), new Vector3(2.0f, 0.5f, 0.3f), BHead, Lens);
            for (int s = -1; s <= 1; s += 2)
            {
                bool left = s > 0;
                int up = left ? BUpperL : BUpperR, low = left ? BLowerL : BLowerR, thigh = left ? BThighL : BThighR, shin = left ? BShinL : BShinR;
                float sx = s * ShoulderX * Mathf.Lerp(1f, wd, 0.7f);
                Part(ball, new Vector3(sx, ShoulderY + 0.1f, 0), new Vector3(1.75f, 1.75f, 1.6f), up, k.Sleeve);
                Part(_upper, new Vector3(sx, ShoulderY, 0), Vector3.one, up, k.Sleeve);
                Part(_fore, new Vector3(sx + s * 0.1f, ElbowY, 0), Vector3.one, low, k.Fore);
                Part(ball, new Vector3(sx + s * 0.1f, ElbowY - 4.55f, -0.05f), new Vector3(0.78f, 1.25f, 0.58f), low, k.Skin);
                Part(_thigh, new Vector3(s * HipX, HipY, 0), Vector3.one, thigh, k.Pants);
                Part(_shin, new Vector3(s * (HipX + 0.05f), KneeY, 0), Vector3.one, shin, k.Pants);
                Part(StarKit.Rounded(new Vector3(1.35f, 1.15f, 2.8f), 0.45f), new Vector3(s * (HipX + 0.05f), 0.58f, -0.55f), Vector3.one, shin, k.Shoes);
            }
        }

        /// <summary>A guitar or bass on its strap, in the rig's standing coordinates (the neck to the player's left).</summary>
        public void Guitar(int body, int guard, float neckLength)
        {
            var c = new Vector3(-0.5f, 13.0f, -2.15f);
            Quaternion tilt = Quaternion.Euler(0, 0, 28f);
            Vector3 d = tilt * Vector3.right;
            Part(StarKit.Ball, c, new Vector3(5.4f, 4.7f, 1.15f), BChest, body);
            Part(StarKit.Ball, c + d * 2.6f + tilt * new Vector3(0, 0.35f, 0), new Vector3(4.2f, 3.6f, 1.15f), BChest, body);
            Part(StarKit.Ball, c + new Vector3(0.2f, -0.2f, -0.45f), new Vector3(3.0f, 2.6f, 0.4f), BChest, guard);
            Part(StarKit.Cube, c + d * (2.4f + neckLength * 0.5f) + new Vector3(0, 0, -0.1f), new Vector3(neckLength, 0.75f, 0.42f), BChest, Leather, tilt);
            Part(StarKit.Cube, c + d * (2.6f + neckLength + 1f) + new Vector3(0, 0, -0.1f), new Vector3(2.2f, 1.1f, 0.38f), BChest, Black, tilt);
            Part(StarKit.Cube, c + new Vector3(-0.6f, 0.2f, -0.62f), new Vector3(0.5f, 1.6f, 0.2f), BChest, Chrome); // bridge
            // The strap, over the left shoulder.
            Part(StarKit.Cube, new Vector3(0.6f, 16.2f, -1.42f), new Vector3(0.5f, 7.2f, 0.18f), BChest, Black, Quaternion.Euler(0, 0, -38f));
        }

        /// <summary>A handheld microphone in the right hand, pointing on along the forearm.</summary>
        public void Mic()
        {
            float x = -ShoulderX - 0.1f;
            Part(StarKit.CylinderMesh, Matrix4x4.TRS(new Vector3(x, ElbowY - 4.4f, -0.1f), Quaternion.Euler(180f, 0, 0), new Vector3(0.32f, 2.6f, 0.32f)), BLowerR, Black);
            Part(StarKit.Ball, new Vector3(x, ElbowY - 7.25f, -0.1f), Vector3.one * 1.05f, BLowerR, Chrome);
        }

        /// <summary>Drumsticks in both hands, pointing forward and down from the grip.</summary>
        public void Sticks()
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (ShoulderX + 0.1f);
                Vector3 grip = new Vector3(x, ElbowY - 4.5f, -0.2f), tip = grip + new Vector3(-s * 0.6f, -1.2f, -6.2f);
                Part(StarKit.CylinderMesh, Matrix4x4.TRS(grip, Quaternion.FromToRotation(Vector3.up, tip - grip), new Vector3(0.16f, (tip - grip).magnitude, 0.16f)), s > 0 ? BLowerL : BLowerR, Cream);
            }
        }

        /// <summary>Make the skinned mesh. Call once every part is on.</summary>
        public SkinnedMeshRenderer Finish()
        {
            var mesh = new Mesh { name = Root.name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_t, 0);
            mesh.boneWeights = _w.ToArray();
            var poses = new Matrix4x4[_bones.Length];
            for (int i = 0; i < _bones.Length; i++) poses[i] = _bones[i].worldToLocalMatrix * Root.localToWorldMatrix;
            mesh.bindposes = poses;
            mesh.RecalculateBounds();
            var smr = Root.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = _bones;
            smr.rootBone = Hips;
            smr.sharedMaterial = Material;
            smr.quality = SkinQuality.Bone1;
            smr.localBounds = new Bounds(Vector3.zero, new Vector3(40f, 40f, 40f));
            smr.lightProbeUsage = LightProbeUsage.Off;
            smr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _v.Clear(); _n.Clear(); _uv.Clear(); _w.Clear(); _t.Clear();
            return smr;
        }

        // ── Moving ─────────────────────────────────────────────────────────

        /// <summary>
        /// An arm. forward raises it in front (degrees), outward lifts it to the side, yaw turns it across the body,
        /// elbow bends the forearm forward.
        /// </summary>
        public void Arm(bool left, float forward, float outward, float yaw, float elbow)
        {
            float s = left ? 1f : -1f;
            (left ? UpperL : UpperR).localRotation = Quaternion.Euler(forward, yaw * s, outward * s);
            (left ? LowerL : LowerR).localRotation = Quaternion.Euler(elbow, 0, 0);
        }

        /// <summary>Knees bent by bend degrees, the feet kept on the floor (the root drops to match).</summary>
        public float Knees(float bend)
        {
            ThighL.localRotation = ThighR.localRotation = Quaternion.Euler(bend, 0, 0);
            ShinL.localRotation = ShinR.localRotation = Quaternion.Euler(-bend * 2f, 0, 0);
            float r = bend * Mathf.Deg2Rad;
            return (HipY - KneeY) * (1f - Mathf.Cos(r)) + (KneeY - 0.6f) * (1f - Mathf.Cos(r));
        }
    }
}
