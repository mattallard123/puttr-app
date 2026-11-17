// Archery: the Puttr app's target game, on an outdoor range.
//
// Every putt looses an arrow at the target, and the hole is the gold. What
// counts is the closest the ball came to the center of the hole:
//
//   holed                  100   Bullseye
//   under 5.075 inches      75   Inner ring (red)
//   under 8.025 inches      50   Mid ring (blue)
//   under 10.975 inches     25   Outer ring (white)
//   anything else            0   Miss
//
// The hole is 2.125 inches in radius and each ring is 2.95 inches wide, and
// the face is painted to the same proportions, so an arrow lands where the
// ball passed: left of the hole is left of the gold, and short of it is
// below it. A putt that goes wide flies past the boss into the hay, and one
// left short drops into the grass in front of the target.
//
// One to four Players, ten arrows each, one arrow a turn. Highest total wins.
// The target only ever shows the arrows of the Player whose turn it is.
//
// The range runs away from the camera along Z. The face of the target is at
// the origin, one unit is about an inch at the target, and the shooting line
// is the plank in the foreground.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class ArcheryMode : StageMode
    {
        public override string Name => "Archery";
        public override string VoicePrefix => "archery";
        public override int Putter => _turn; // whose difficulty applies to this putt
        public override string Blurb => "An archery range, with a putter.\nThe closer to the hole,\nthe closer to the gold.";
        public override string Rule => "Ten arrows each. The closer to the hole, the closer to the gold.";
        public override string Icon => "Target";
        public override Color Accent => new Color(0.96f, 0.58f, 0.2f);
        public override string[] Tags => new[] { "10 arrows", "Closest counts" };
        public override int Difficulty => 1;
        public override int MaxPlayers => 4;
        public override GameAudio.Scene Sound => GameAudio.Scene.Range;
        public override string[] HowTo => new[]
        {
            "Take turns, one arrow each. Every putt looses an arrow at the target, and the hole is the gold.",
            "Easy: hole it for a bullseye, 100, and a near miss scores 75, 50 or 25 by how close. Medium: only a center-cut putt is a bullseye, and the rings are tighter.",
            "Hard: the hole is the whole target. Center cut is the bullseye, and the further off center it goes in, the further out the ring. Miss the hole and you miss the target.",
        };
        // The rings are the difficulty here, so this game takes the putt as it was and sizes
        // them itself: how far from the center still counts as gold, and how wide each ring
        // is beyond it, in inches at the hole. Medium is the Puttr app's own target.
        public override bool OwnLevels => true;
        // Hard is measured across the hole itself (see Measure): gold within 0.2 in of center
        // cut, then 0.6, 1.0, and any other holed putt the outer ring.
        float GoldInches => Arcade.Level == Arcade.Easy ? 4f : Arcade.Level == Arcade.Medium ? HoleInches : 0.2f;
        float BandInches => Arcade.Level == Arcade.Easy ? 3f : Arcade.Level == Arcade.Medium ? 1.5f : 0.4f;

        public override int Score(int player) => _total == null || player < 0 || player >= _players ? 0 : _total[player];

        const int Arrows = 10, MaxArchers = 4;

        // The putt, in inches from the center of the hole.
        const float HoleInches = 2.125f, RingInches = 2.95f;
        const float InnerInches = HoleInches + RingInches, MidInches = HoleInches + RingInches * 2f, OuterInches = HoleInches + RingInches * 3f;

        // The target, in units. The face keeps the proportions of the putt.
        const float FaceR = 30f, BossR = 34f, BossThick = 10f;
        const float PerInch = FaceR / OuterInches;
        const float BullR = HoleInches * PerInch, RingW = RingInches * PerInch;
        const float FaceZ = -BossThick * 0.5f - 0.08f;
        const float Bury = 2.2f;                 // how far a point goes in
        static readonly Vector3 TargetAt = new Vector3(0f, 56f, 0f);

        // The hay behind it.
        const float HayZ = 62f;
        const int BaleRows = 3;
        static readonly Vector3 Bale = new Vector3(40f, 18f, 22f);

        // The app's colors for the face.
        static readonly Color Gold = Make.Hex(0xFFD700), Red = Make.Hex(0xE53935), Blue = Make.Hex(0x1E88E5);

        // Behind the shooting line, looking down the range. With three or four
        // Players the board at the top is taller, so the camera looks a little
        // higher and the target sits clear of it.
        public override Vector3 CameraPosition => At(0f, 70f, -265f);
        public override Vector3 CameraLook => At(0f, _players >= 4 ? 56f : _players == 3 ? 53f : 49f, 0f);
        public override float CameraFov => 30f;
        public override float ShadowDistance => 700f;
        // The target and the hay are sharp; the trees and the hills are not.
        public override float BlurFrom => 520f;
        public override float BlurTo => 1400f;

        enum Lands { Face, Hay, Grass }

        int _players = 1, _turn;
        int[] _total, _shot, _bulls;
        int[,] _scores;
        bool _over;
        readonly System.Random _rng = new System.Random(11);

        // The set.
        readonly Transform[,] _arrows = new Transform[MaxArchers, Arrows];
        readonly Transform[] _flags = new Transform[2];
        Mesh _pointMesh, _shaftMesh, _crestMesh, _nockMesh, _vaneMesh;
        Material _steel, _carbon, _bullGlow;
        TextMesh _float, _floatShade;
        float _bullAt = -100f;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // Late afternoon: a warm sun over the archer's left shoulder, so
            // the face is well lit and the target throws its shadow on the
            // hay, and a cool light from the right to fill the shade.
            Sun(Root, new Vector3(34f, 22f, 0f), new Color(1f, 0.9f, 0.74f), 1.5f);
            Sun(Root, new Vector3(24f, -64f, 0f), new Color(0.62f, 0.74f, 1f), 0.45f).shadows = LightShadows.None;

            // The field: mown in bands across the range.
            Props.Grass(out Texture2D turf, out Texture2D blades);
            Material grass = Make.Lit(new Color(0.8f, 0.86f, 0.8f), 0.14f, 0f, turf, blades, 0.9f);
            grass.SetTextureScale("_BaseMap", new Vector2(3000f / 120f, 2400f / 120f));
            Shape(PrimitiveType.Cube, "Grass", Root, new Vector3(0f, -1f, 600f), new Vector3(3000f, 2f, 2400f), grass);

            // The shooting line: a plank across the foreground, and a chalk line beyond it.
            Material plank = Make.Lit(new Color(0.62f, 0.44f, 0.28f), 0.3f, 0f, Make.Wood(4));
            plank.SetTextureScale("_BaseMap", new Vector2(1f, 6f));
            Make.Box("Shooting line", Root, new Vector3(0f, 0.7f, -40f), new Vector3(460f, 1.4f, 9f), plank, null, false);
            Shape(PrimitiveType.Cube, "Chalk line", Root, new Vector3(0f, 0.06f, -30f), new Vector3(460f, 0.04f, 1.6f), Make.Glow(new Color(1f, 0.98f, 0.92f, 0.4f), false))
                .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            Texture2D straw = Straw(3);
            BuildHay(straw);
            BuildTarget(straw);
            BuildFlags();
            BuildBackdrop();

            // The arrows: ten for each Player, fletched in that Player's color.
            _steel = Make.Lit(new Color(0.8f, 0.82f, 0.85f), 0.9f, 1f);
            _carbon = Make.Lit(new Color(0.06f, 0.06f, 0.07f), 0.5f);
            _pointMesh = Make.Lathe(new List<Vector2> { new Vector2(0.02f, 0f), new Vector2(0.52f, 2.6f), new Vector2(0.42f, 3.4f) }, 12);
            _shaftMesh = Make.Lathe(new List<Vector2> { new Vector2(0.42f, 3.4f), new Vector2(0.42f, 30.2f) }, 10);
            _crestMesh = Make.Lathe(new List<Vector2> { new Vector2(0.47f, 19.6f), new Vector2(0.47f, 21.6f) }, 10);
            _nockMesh = Make.Lathe(new List<Vector2> { new Vector2(0.42f, 30.2f), new Vector2(0.54f, 30.4f), new Vector2(0.54f, 31.4f), new Vector2(0.25f, 31.8f) }, 10);
            _vaneMesh = Vane();
            for (int p = 0; p < MaxArchers; p++)
            {
                Material fin = Make.Lit(Hud.PlayerColors[p % Hud.PlayerColors.Length], 0.45f);
                for (int i = 0; i < Arrows; i++)
                {
                    _arrows[p, i] = BuildArrow(fin);
                    _arrows[p, i].gameObject.SetActive(false);
                }
            }

            // The number that lifts off an arrow as it lands, with a shade behind it.
            _floatShade = Words(Root, "", Vector3.zero, 7f, new Color(0f, 0f, 0f, 0f), TextAnchor.MiddleCenter, 96);
            _float = Words(Root, "", Vector3.zero, 7f, Color.white, TextAnchor.MiddleCenter, 96);
        }

        // A straw boss on a wooden tripod, and its painted face.
        void BuildTarget(Texture2D straw)
        {
            Material wood = Make.Lit(new Color(0.5f, 0.34f, 0.2f), 0.35f, 0f, Make.Wood(6));
            Quaternion facing = Quaternion.Euler(-90f, 0f, 0f); // a lathe's height runs toward the camera

            // The stand: two legs in front, one behind, and the ledge the boss sits on.
            const float top = 100f, legZ = 8.6f;
            for (int side = -1; side <= 1; side += 2)
                Props.Rod("Leg", Root, new Vector3(side * 7f, top, legZ), new Vector3(side * 36f, 0f, legZ), 1.9f, wood);
            Props.Rod("Back leg", Root, new Vector3(0f, top, legZ + 1f), new Vector3(0f, 0f, 44f), 1.9f, wood);
            Make.Box("Head", Root, new Vector3(0f, top - 1f, legZ + 0.4f), new Vector3(19f, 5f, 5.4f), wood, null, false);
            Make.Box("Ledge", Root, new Vector3(0f, TargetAt.y - BossR - 1.5f, 3f), new Vector3(72f, 3f, 9f), wood, null, false);

            // The boss: coiled straw, a little rounded at the rim.
            Material coil = Make.Lit(new Color(0.95f, 0.88f, 0.72f), 0.1f, 0f, straw);
            coil.SetTextureScale("_BaseMap", new Vector2(6f, 1f));
            var profile = new List<Vector2>
            {
                new Vector2(0.01f, 0f), new Vector2(BossR - 1.6f, 0f), new Vector2(BossR, 1.6f),
                new Vector2(BossR, BossThick - 1.6f), new Vector2(BossR - 1.6f, BossThick), new Vector2(0.01f, BossThick),
            };
            Make.MeshObject("Boss", Root, Make.Lathe(profile, 96), coil).transform.SetLocalPositionAndRotation(TargetAt + new Vector3(0f, 0f, BossThick * 0.5f), facing);
            Material rope = Make.Lit(new Color(0.78f, 0.68f, 0.5f), 0.1f, 0f, straw);
            rope.SetTextureScale("_BaseMap", new Vector2(8f, 1f));
            foreach (float z in new[] { -3f, 0f, 3f })
                Make.MeshObject("Coil", Root, Props.Torus(BossR - 0.3f, 1.05f, 96, 8), rope).transform.SetLocalPositionAndRotation(TargetAt + new Vector3(0f, 0f, z), facing);
            Make.MeshObject("Rim coil", Root, Props.Torus((FaceR + BossR) * 0.5f + 0.4f, 1.3f, 96, 8), rope).transform.SetLocalPositionAndRotation(TargetAt + new Vector3(0f, 0f, -BossThick * 0.5f + 0.3f), facing);

            // The face, painted, and the four pins that hold it on.
            Make.MeshObject("Face", Root, FaceMesh(FaceR, 96), Make.Lit(Color.white, 0.12f, 0f, Face())).transform.localPosition = TargetAt + new Vector3(0f, 0f, FaceZ);
            Material pin = Make.Lit(new Color(0.75f, 0.77f, 0.8f), 0.85f, 1f);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Shape(PrimitiveType.Sphere, "Pin", Root, TargetAt + new Vector3(Mathf.Cos(a) * (FaceR - 1.1f), Mathf.Sin(a) * (FaceR - 1.1f), FaceZ - 0.1f), Vector3.one * 1.1f, pin);
            }

            // The gold lights up for a bullseye.
            _bullGlow = Make.Glow(new Color(1f, 0.85f, 0.3f, 0f), true);
            Hoop("Bull glow", Root, TargetAt + new Vector3(0f, 0f, FaceZ - 0.2f), 0f, BullR + RingW * 0.5f, _bullGlow);
        }

        // A low wall of bales behind the target, laid like bricks, to catch what misses.
        void BuildHay(Texture2D straw)
        {
            Material hay = Make.Lit(new Color(1f, 0.95f, 0.82f), 0.08f, 0f, straw);
            Material twine = Make.Lit(new Color(0.2f, 0.13f, 0.07f), 0.3f);
            var rng = new System.Random(8);
            for (int row = 0; row < BaleRows; row++)
            {
                float shift = row % 2 == 0 ? 0f : Bale.x * 0.5f;
                for (float x = -260f + shift; x <= 260f; x += Bale.x + 0.7f)
                {
                    // Nobody stacks bales dead straight.
                    Transform bale = Node("Hay bale", Root, new Vector3(x, (row + 0.5f) * Bale.y, HayZ + ((float)rng.NextDouble() - 0.5f) * 2.4f));
                    bale.localRotation = Quaternion.Euler(0f, ((float)rng.NextDouble() - 0.5f) * 5f, 0f);
                    Make.Box("Hay", bale, Vector3.zero, Bale, hay, null, false);
                    for (int side = -1; side <= 1; side += 2)
                        Make.Box("Twine", bale, new Vector3(side * Bale.x * 0.27f, 0f, 0f), new Vector3(0.7f, Bale.y + 0.3f, Bale.z + 0.3f), twine, null, false);
                }
            }

            // The range's sign, standing on the wall to the right of the target.
            const float SignX = 80f;
            Material board = Make.Lit(new Color(0.24f, 0.15f, 0.09f), 0.4f, 0f, Make.Wood(9));
            float top = BaleRows * Bale.y;
            for (int side = -1; side <= 1; side += 2)
                Props.Rod("Sign post", Root, new Vector3(SignX + side * 28f, top - 4f, HayZ), new Vector3(SignX + side * 28f, top + 12f, HayZ), 1.3f, board);
            Make.Box("Sign", Root, new Vector3(SignX, top + 20f, HayZ), new Vector3(76f, 20f, 2f), board, null, false);
            Words(Root, "PUTTR ARCHERY", new Vector3(SignX, top + 22.5f, HayZ - 1.3f), 5.2f, new Color(0.98f, 0.86f, 0.5f));
            Words(Root, "AIM FOR THE GOLD", new Vector3(SignX, top + 14f, HayZ - 1.3f), 2.6f, new Color(1f, 1f, 1f, 0.75f));
        }

        // A flag on a tall pole behind the hay, either side of the target, to show the wind.
        void BuildFlags()
        {
            Material pole = Make.Lit(new Color(0.92f, 0.91f, 0.87f), 0.5f);
            Color[] cloth = { Red, Gold };
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -120f : 120f;
                const float z = HayZ + 18f, high = 108f;
                Props.Rod("Flag pole", Root, new Vector3(x, 0f, z), new Vector3(x, high, z), 1.1f, pole);
                Shape(PrimitiveType.Sphere, "Finial", Root, new Vector3(x, high + 1.4f, z), Vector3.one * 3.4f, Make.Lit(Gold, 0.8f, 1f));
                _flags[i] = Node("Flag", Root, new Vector3(x, high - 10f, z));
                Make.Box("Cloth", _flags[i], new Vector3(13.5f, 0f, 0f), new Vector3(25f, 15f, 0.3f), Make.Lit(cloth[i], 0.3f), null, false);
                Make.Box("Stripe", _flags[i], new Vector3(13.5f, 0f, 0f), new Vector3(25.1f, 3.4f, 0.4f), Make.Lit(new Color(0.95f, 0.94f, 0.9f), 0.3f), null, false);
            }
        }

        // Beyond the hay: a line of trees, hills in the haze, and the evening sky.
        void BuildBackdrop()
        {
            Card("Sky", Root, new Vector3(0f, 700f, 1700f), new Vector2(5600f, 1700f), Make.Glow(Color.white, false, Sky()));
            Material hills = Make.Glow(Color.white, false, Hills());
            hills.SetTextureScale("_MainTex", new Vector2(2f, 1f));
            Card("Hills", Root, new Vector3(0f, 122f, 1500f), new Vector2(5600f, 260f), hills);
            Material trees = Make.Glow(Color.white, false, Trees());
            trees.SetTextureScale("_MainTex", new Vector2(4f, 1f));
            Card("Tree line", Root, new Vector3(0f, 70f, 760f), new Vector2(3600f, 150f), trees);
        }

        static Transform Node(string name, Transform parent, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        // A disc in the XY plane, facing the camera, textured edge to edge.
        static Mesh FaceMesh(float radius, int segments)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var normals = new List<Vector3> { Vector3.back };
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * 360f / segments * Mathf.Deg2Rad;
                float x = Mathf.Sin(a), y = Mathf.Cos(a); // clockwise from the top
                verts.Add(new Vector3(x * radius, y * radius, 0f));
                uvs.Add(new Vector2(0.5f + x * 0.5f, 0.5f + y * 0.5f));
                normals.Add(Vector3.back);
                if (i > 0) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            }
            var mesh = new Mesh { name = "Face" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // One fletching: a swept vane standing off the shaft, seen from both sides.
        static Mesh Vane()
        {
            Vector3 a = new Vector3(0f, 0.4f, -22.6f), b = new Vector3(0f, 2.9f, -26.4f), c = new Vector3(0f, 2.9f, -29.2f), d = new Vector3(0f, 0.4f, -30f);
            var mesh = new Mesh { name = "Vane" };
            mesh.vertices = new[] { a, b, c, d, a, b, c, d };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // An arrow, point at the origin, flying along +Z.
        Transform BuildArrow(Material fin)
        {
            var arrow = new GameObject("Arrow").transform;
            arrow.SetParent(Root, false);
            Quaternion back = Quaternion.Euler(-90f, 0f, 0f); // a lathe's height runs back from the point
            Make.MeshObject("Point", arrow, _pointMesh, _steel).transform.localRotation = back;
            Make.MeshObject("Shaft", arrow, _shaftMesh, _carbon).transform.localRotation = back;
            Make.MeshObject("Crest", arrow, _crestMesh, fin).transform.localRotation = back;
            Make.MeshObject("Nock", arrow, _nockMesh, fin).transform.localRotation = back;
            for (int i = 0; i < 3; i++)
                Make.MeshObject("Fletching", arrow, _vaneMesh, fin).transform.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
            return arrow;
        }

        // ── Paint ───────────────────────────────────────────────────────────

        static float Smooth(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        // The face: gold, red, blue and white, ruled in black, on paper with a little grain.
        static Texture2D Face()
        {
            const int n = 1024;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            var px = new Color32[n * n];
            Color white = new Color(0.94f, 0.93f, 0.89f), ink = new Color(0.05f, 0.05f, 0.06f);
            const float pixels = n * 0.5f / OuterInches;  // to an inch
            const float half = 0.085f;                      // half the width of a ruled line, inches
            var rng = new System.Random(4);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                    float inches = Mathf.Sqrt(dx * dx + dy * dy) / pixels;
                    Color c = inches < HoleInches ? Gold : inches < InnerInches ? Red : inches < MidInches ? Blue : white;
                    // A line between each zone, one round the edge, and a cross in the middle of the gold.
                    float line = Mathf.Min(Mathf.Abs(inches - HoleInches), Mathf.Min(Mathf.Abs(inches - InnerInches), Mathf.Abs(inches - MidInches)));
                    line = Mathf.Min(line, Mathf.Abs(inches - (OuterInches - half)));
                    float k = Mathf.Clamp01((half - line) * pixels + 0.5f);
                    if (inches > OuterInches - half) k = 1f;
                    if (inches < 0.42f && (Mathf.Abs(dx) < 1.7f || Mathf.Abs(dy) < 1.7f)) k = 1f;
                    c = Color.Lerp(c, ink, k);
                    // Paper over straw: a speckle, the weave showing through, and some weather.
                    float grain = ((float)rng.NextDouble() - 0.5f) * 0.05f
                        + (Mathf.PerlinNoise(x * 0.9f, y * 0.045f) - 0.5f) * 0.07f
                        + (Mathf.PerlinNoise(x * 0.011f + 7f, y * 0.011f + 3f) - 0.5f) * 0.07f;
                    px[y * n + x] = new Color(Mathf.Clamp01(c.r + grain), Mathf.Clamp01(c.g + grain), Mathf.Clamp01(c.b + grain), 1f);
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // Straw: long stalks lying along u, for the bales and the coils of the boss.
        static Texture2D Straw(int seed)
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color32[n * n];
            var rng = new System.Random(seed);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float stalk = Mathf.PerlinNoise(x * 0.035f + seed, y * 0.55f);
                    float clump = Mathf.PerlinNoise(x * 0.02f, y * 0.03f + seed) - 0.5f;
                    float tone = 0.7f + stalk * 0.42f + clump * 0.25f + ((float)rng.NextDouble() - 0.5f) * 0.09f;
                    px[y * n + x] = new Color(Mathf.Clamp01(0.8f * tone), Mathf.Clamp01(0.63f * tone), Mathf.Clamp01(0.27f * tone), 1f);
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // The sky toward evening: warm haze on the horizon, blue overhead, a few thin clouds.
        internal static Texture2D Sky() // puttr-app-2: shared with Beer Pong's green
        {
            const int w = 512, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            Color low = new Color(1f, 0.86f, 0.66f), mid = new Color(0.66f, 0.8f, 0.93f), high = new Color(0.2f, 0.42f, 0.78f), cloud = new Color(1f, 0.94f, 0.86f);
            for (int y = 0; y < h; y++)
            {
                // The horizon is an eighth of the way up the card.
                float v = y / (h - 1f);
                Color sky = Color.Lerp(Color.Lerp(low, mid, Smooth(0.12f, 0.25f, v)), high, Smooth(0.24f, 0.62f, v));
                float band = Smooth(0.15f, 0.22f, v) * (1f - Smooth(0.42f, 0.6f, v));
                for (int x = 0; x < w; x++)
                {
                    float wisp = Mathf.PerlinNoise(x * 0.018f + 11f, y * 0.075f + 5f) * 0.65f + Mathf.PerlinNoise(x * 0.06f, y * 0.22f) * 0.35f;
                    Color c = Color.Lerp(sky, cloud, Smooth(0.52f, 0.78f, wisp) * band * 0.8f);
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // Far hills in the haze. The outline is made of whole waves, so it repeats without a seam.
        internal static Texture2D Hills() // puttr-app-2: shared with Beer Pong's green
        {
            const int w = 1024, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            Color low = new Color(0.47f, 0.6f, 0.52f), high = new Color(0.52f, 0.64f, 0.68f);
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w * Mathf.PI * 2f;
                float top = (0.52f + 0.2f * Mathf.Sin(u + 0.7f) + 0.12f * Mathf.Sin(u * 3f + 2.1f) + 0.05f * Mathf.Sin(u * 7f + 4f)) * h;
                for (int y = 0; y < h; y++)
                {
                    Color c = Color.Lerp(low, high, y / (float)h);
                    c.a = Mathf.Clamp01((top - y) / 1.5f);
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // The tree line: round crowns, dark below and catching the sun along the top.
        internal static Texture2D Trees() // puttr-app-2: shared with Beer Pong's green
        {
            const int w = 1024, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            Color low = new Color(0.04f, 0.12f, 0.06f), high = new Color(0.12f, 0.29f, 0.11f), lit = new Color(0.3f, 0.44f, 0.16f);
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                float turn = u * Mathf.PI * 2f, bump = u * Mathf.PI;
                float big = Mathf.Abs(Mathf.Sin(bump * 23f + 0.4f)), small = Mathf.Abs(Mathf.Sin(bump * 37f + 1.3f));
                float top = (0.42f + 0.07f * Mathf.Sin(turn * 2f + 1f) + 0.04f * Mathf.Sin(turn * 5f + 3f)
                    + 0.2f * Mathf.Pow(big, 0.7f) + 0.1f * small + 0.04f * Mathf.Abs(Mathf.Sin(bump * 71f))) * h;
                float shade = 0.82f + 0.18f * big;
                for (int y = 0; y < h; y++)
                {
                    Color c = Color.Lerp(low, high, Mathf.Clamp01(y / top)) * shade;
                    c = Color.Lerp(c, lit, (1f - Smooth(0f, 16f, top - y)) * 0.7f);
                    c.a = Mathf.Clamp01((top - y) / 1.5f);
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = Mathf.Clamp(players, 1, MaxArchers);
            _turn = 0;
            _over = false;
            _total = new int[_players];
            _shot = new int[_players];
            _bulls = new int[_players];
            _scores = new int[_players, Arrows];
            PutAway();
            if (Arcade != null && Arcade.Hud != null)
            {
                var names = new List<string>();
                for (int i = 0; i < _players; i++) names.Add(Arcade.PlayerName(i));
                Arcade.Hud.BuildBoard(names, Arrows, "TOTAL", true, "ARROWS", 32f);
                ShowBoard();
            }
        }

        // Behind the home screen: a target part way through a round, with an arrow or two from everyone.
        public override void Preview()
        {
            Begin(1);
            Stick(_arrows[0, 0], FacePoint(new Vector2(1.7f, -1.5f)), Rest());
            Stick(_arrows[0, 1], FacePoint(new Vector2(-8.6f, 4.4f)), Rest());
            Stick(_arrows[1, 0], FacePoint(new Vector2(12f, -13f)), Rest());
            Stick(_arrows[1, 1], FacePoint(new Vector2(-3.4f, -10.2f)), Rest());
            Stick(_arrows[2, 0], FacePoint(new Vector2(17f, 15f)), Rest());
            Stick(_arrows[3, 0], FacePoint(new Vector2(-22f, -9f)), Rest());
            Stick(_arrows[2, 1], HayPoint(1f), Rest());
        }

        // Each Player sees their own arrows in the target, and nobody else's.
        public override IEnumerator Prepare()
        {
            if (!_over) ShowArrows(_turn);
            yield break;
        }

        public override string Prompt
        {
            get
            {
                if (_shot == null) return "";
                int arrow = Mathf.Min(_shot[_turn] + 1, Arrows);
                if (_players == 1) return "Arrow " + arrow + " of " + Arrows + "   ·   " + _total[0] + (_total[0] == 1 ? " point" : " points");
                return Arcade.PlayerName(_turn) + "   ·   arrow " + arrow + " of " + Arrows;
            }
        }

        public override IEnumerator Play(PuttResult putt)
        {
            int player = _turn, shot = Mathf.Min(_shot[player], Arrows - 1);
            int points = Read(putt, out Vector3 to, out Lands lands);

            // The arrow first; the words come once it has landed.
            yield return Shoot(_arrows[player, shot], to, lands);

            _scores[player, shot] = points;
            _total[player] += points;
            _shot[player] = shot + 1;

            Hud hud = Arcade.Hud;
            string result;
            Color color;
            if (points == 100)
            {
                result = "Bullseye"; color = Gold;
                _bulls[player]++;
                hud.Banner("BULLSEYE", "+100", Gold, 2.4f);
                Arcade.Audio.Strike();
                Arcade.Audio.Roar(true);
                Arcade.Audio.Bell();
                Arcade.SlowMotion(1f);
                Arcade.Shake(0.45f);
                Arcade.Celebrate(150, At(TargetAt.x, TargetAt.y, FaceZ - 6f));
                _bullAt = Time.time;
            }
            else if (points == 75)
            {
                result = "Inner ring"; color = Red;
                hud.Banner("INNER RING", "+75", Red, 1.8f);
                Arcade.Audio.Spare();
                Arcade.Audio.Roar(false);
                Arcade.Celebrate(30, At(to.x, to.y, FaceZ - 6f));
            }
            else if (points == 50)
            {
                result = "Mid ring"; color = Blue;
                hud.Banner("MID RING", "+50", Blue, 1.8f);
                Arcade.Audio.Coin();
            }
            else if (points == 25)
            {
                result = "Outer ring"; color = Color.white;
                hud.Banner("OUTER RING", "+25", Color.white, 1.8f);
                Arcade.Audio.Coin();
            }
            else
            {
                result = "Miss"; color = Make.Red;
                hud.Banner("MISS", lands == Lands.Grass ? "Left short: it never reached the target" : "Wide of the target, into the hay", Make.Red, 1.8f);
                Arcade.Audio.Miss();
                Arcade.Audio.Groan();
            }
            ShowPutt(putt, result, color);

            // Whose arrow next. Everyone has shot the same number when the turn comes back round.
            _turn = (player + 1) % _players;
            if (_shot[_turn] >= Arrows) _over = true;
            ShowBoard();

            // The score lifts off the arrow and fades. It stays over the face, toward the middle.
            float hold = points == 100 ? 2.4f : 1.3f;
            Vector3 from = new Vector3(Mathf.Clamp(to.x, -16f, 16f), to.y + (to.y > TargetAt.y ? -10f : 8f), FaceZ - 1.5f);
            bool shown = false;
            for (float t = 0f; t < hold; t += Time.deltaTime)
            {
                if (points > 0)
                {
                    float k = Mathf.Clamp01(t / 1.15f);
                    Float("+" + points, from + Vector3.up * 5f * Ease(k), color, k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f);
                }
                if (!shown && t > 0.35f) { shown = true; Arcade.Shot("result"); }
                yield return null;
            }
            Float("", from, color, 0f);
        }

        public override bool Over => _over;

        public override void Final(out string title, out string body)
        {
            body = "";
            if (_players == 1)
            {
                title = _total[0] + "  P O I N T S";
                body = "Out of 1,000\n" + _bulls[0] + (_bulls[0] == 1 ? " bullseye\n" : " bullseyes\n");
                return;
            }
            int best = 0, level = 0;
            for (int i = 0; i < _players; i++) if (_total[i] > _total[best]) best = i;
            for (int i = 0; i < _players; i++) if (_total[i] == _total[best]) level++;
            title = level > 1 ? "A draw" : Arcade.PlayerName(best) + " wins";
            for (int i = 0; i < _players; i++)
                body += Arcade.PlayerName(i) + "     " + _total[i] + (_total[i] == 1 ? " point" : " points") + (level == 1 && i == best ? "     winner" : "") + "\n";
        }

        public override void Tick()
        {
            // The flags lift and fall in a light wind.
            for (int i = 0; i < _flags.Length; i++)
                if (_flags[i] != null) _flags[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 2.1f + i * 1.7f) * 13f + Mathf.Sin(Time.time * 5.3f + i) * 4f, 0f);
            // After a bullseye the gold flashes, then settles.
            if (_bullGlow == null) return;
            float since = Time.time - _bullAt;
            float glow = since >= 0f && since < 2.2f ? (1f - since / 2.2f) * (0.35f + 0.35f * Mathf.Abs(Mathf.Sin(since * 9f))) : 0f;
            _bullGlow.SetColor("_Color", new Color(1f, 0.85f, 0.3f, glow));
        }

        public override void End()
        {
            PutAway();
        }

        // Every arrow back in the quiver, and nothing left glowing.
        void PutAway()
        {
            foreach (Transform arrow in _arrows) if (arrow != null) arrow.gameObject.SetActive(false);
            _bullAt = -100f;
            if (_bullGlow != null) _bullGlow.SetColor("_Color", new Color(1f, 0.85f, 0.3f, 0f));
            if (_float != null) Float("", Vector3.zero, Color.white, 0f);
        }

        // ── The board ───────────────────────────────────────────────────────

        // What each arrow scored, and the totals. A bullseye is solid, a miss is a red dot.
        void ShowBoard()
        {
            if (Arcade == null || Arcade.Hud == null || _total == null) return;
            Hud hud = Arcade.Hud;
            for (int p = 0; p < _players; p++)
            {
                for (int i = 0; i < Arrows; i++)
                {
                    bool done = i < _shot[p];
                    int s = _scores[p, i];
                    hud.SetPip(p, i, !done ? Hud.Pip.Empty : s == 100 ? Hud.Pip.Good : s > 0 ? Hud.Pip.Over : Hud.Pip.Bad, done ? s.ToString() : "");
                }
                hud.SetTotal(p, _total[p]);
            }
            hud.SetTurn(_over ? -1 : _turn, _over ? -1 : _shot[_turn]);
            hud.SetBoardTitle(_over ? "GAME OVER" : "ARROW " + (_shot[_turn] + 1) + " OF " + Arrows);
        }

        // ── The putt ────────────────────────────────────────────────────────

        /// <summary>What a putt scores, the closest its path came to the center of the hole (inches), and where that was.</summary>
        int Points(PuttResult putt, out float closest, out Vector2 where)
        {
            closest = float.MaxValue;
            where = Vector2.zero;
            Vector2[] path = putt.PathInches;
            if (path != null)
                for (int i = 0; i < path.Length; i++)
                {
                    float d = path[i].magnitude;
                    if (d < closest) { closest = d; where = path[i]; }
                }
            float m = Measure(putt, closest), gold = GoldInches, band = BandInches;
            if (m < gold) return 100;
            if (m < gold + band) return 75;
            if (m < gold + band * 2f) return 50;
            if (m < gold + band * 3f) return 25;
            return 0;
        }

        // How far off center a holed putt went in, in inches across the hole: from where it
        // entered the cup, or failing that the line it held to the hole.
        static float CutInches(PuttResult putt) =>
            putt.HasEntry ? HoleInches * Mathf.Sin(Mathf.Min(Mathf.Abs(putt.EntryAngle), 90) * Mathf.Deg2Rad) : Mathf.Abs(Line(putt));

        // What the rings are measured on. Easy: the closest the ball came, and holed is gold.
        // Hard: the hole is the whole target, so only a holed putt scores, gold only when it
        // goes in center cut, and the rings by how far off center it went in. Medium: gold only
        // for a holed putt that is near center cut; any other holed putt is the inner ring,
        // and near misses the rings outside it.
        float Measure(PuttResult putt, float closest)
        {
            if (Arcade.Level == Arcade.Hard) return putt.Made ? Mathf.Min(CutInches(putt), 1.39f) : float.MaxValue;
            if (Arcade.Level == Arcade.Medium)
            {
                if (putt.Made) return CutInches(putt) < 0.6f ? 0f : GoldInches;
                return closest < HoleInches ? GoldInches : closest;
            }
            return putt.Made ? 0f : closest;
        }

        // Score a putt, and work out where its arrow comes down.
        int Read(PuttResult putt, out Vector3 to, out Lands lands)
        {
            int points = Points(putt, out float closest, out Vector2 where);

            // On the face, right is right, and toward the Player is down.
            var way = new Vector2(where.x, -where.y);
            if (closest < float.MaxValue && way.sqrMagnitude > 0.0001f) way.Normalize();
            else
            {
                float a = (float)_rng.NextDouble() * Mathf.PI * 2f;
                way = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            lands = Lands.Face;
            if (points == 100)
            {
                // In the gold: as far off center as the ball was across the hole.
                float x = Mathf.Clamp(Line(putt) / HoleInches, -1f, 1f) * BullR * 0.72f;
                float y = ((float)_rng.NextDouble() - 0.5f) * BullR * 0.7f;
                to = FacePoint(new Vector2(x, y));
            }
            else if (points > 0)
            {
                // In its ring, as far across it as the ball was across that band, and clear of the lines.
                int band = points == 75 ? 0 : points == 50 ? 1 : 2;
                float across = Mathf.Clamp01((Measure(putt, closest) - (GoldInches + band * BandInches)) / BandInches);
                to = FacePoint(way * (BullR + RingW * (band + 0.14f + 0.72f * across)));
            }
            else if (!putt.Launch.Reached || closest == float.MaxValue)
            {
                // Left short: into the grass in front of the target.
                lands = Lands.Grass;
                float side = _rng.NextDouble() < 0.5 ? -1f : 1f;
                to = new Vector3(side * (8f + (float)_rng.NextDouble() * 30f), 0f, -26f + (float)_rng.NextDouble() * 20f);
            }
            else
            {
                // Wide: past the edge of the boss on the side it missed, and into the hay.
                lands = Lands.Hay;
                float side = Mathf.Abs(way.x) > 0.15f ? Mathf.Sign(way.x) : putt.MissSide == MissSide.Left ? -1f : putt.MissSide == MissSide.Right ? 1f : _rng.NextDouble() < 0.5 ? -1f : 1f;
                to = HayPoint(side);
            }
            return points;
        }

        /// <summary>A point on the face, from the middle of the gold.</summary>
        static Vector3 FacePoint(Vector2 offset) => TargetAt + new Vector3(offset.x, offset.y, FaceZ);

        /// <summary>Somewhere on the front of the bales, clear of the boss on this side (-1 left, +1 right).</summary>
        Vector3 HayPoint(float side)
        {
            return new Vector3(side * (48f + (float)_rng.NextDouble() * 26f), 9f + (float)_rng.NextDouble() * (BaleRows * Bale.y - 18f), HayZ - Bale.z * 0.5f);
        }

        // ── Arrows ──────────────────────────────────────────────────────────

        // Only this Player's arrows are on show.
        void ShowArrows(int player)
        {
            for (int p = 0; p < MaxArchers; p++)
                for (int i = 0; i < Arrows; i++)
                    _arrows[p, i].gameObject.SetActive(p == player && p < _players && i < _shot[p]);
        }

        // How an arrow sits in the target: nock a little high, and a touch to one side.
        Vector3 Rest() => new Vector3(((float)_rng.NextDouble() - 0.5f) * 0.14f, -0.06f - (float)_rng.NextDouble() * 0.07f, 1f);

        void Stick(Transform arrow, Vector3 at, Vector3 forward)
        {
            forward.Normalize();
            arrow.gameObject.SetActive(true);
            arrow.localPosition = at + forward * Bury; // the point is buried
            arrow.localRotation = Quaternion.LookRotation(forward);
        }

        // Loose an arrow from beside the camera. It arcs a little, thuds in, and quivers.
        IEnumerator Shoot(Transform arrow, Vector3 to, Lands lands)
        {
            var from = new Vector3(15f, 58f, -252f);
            // One left short is lobbed, so that it comes down into the turf.
            float flight = lands == Lands.Grass ? 0.5f : 0.42f, arc = lands == Lands.Grass ? 24f : 7f;
            Vector3 last = from, way = (to - from).normalized;
            arrow.gameObject.SetActive(true);
            arrow.localPosition = from;
            arrow.localRotation = Quaternion.LookRotation(way);
            Arcade.Audio.ArrowShot();
            for (float t = 0f; t < 1f; t += Time.deltaTime / flight)
            {
                Vector3 p = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * arc;
                arrow.localPosition = p;
                Vector3 step = p - last;
                if (step.sqrMagnitude > 0.0001f)
                {
                    way = step.normalized;
                    arrow.localRotation = Quaternion.LookRotation(way) * Quaternion.Euler(0f, 0f, t * 240f);
                }
                last = p;
                yield return null;
            }
            Arcade.Audio.ArrowHit(lands == Lands.Face);
            Arcade.Shake(lands == Lands.Face ? 0.12f : 0.06f);
            // In the grass it stays as it fell; in the face or the hay it sits nearly square.
            Stick(arrow, to, lands == Lands.Grass ? way : Rest());

            // It quivers where it lands, about its point.
            Quaternion rest = arrow.localRotation;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
            {
                float fade = 1f - t;
                arrow.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * 52f) * 3.6f * fade, Mathf.Sin(t * 37f) * 1.4f * fade, 0f);
                yield return null;
            }
            arrow.localRotation = rest;
        }

        // The number over an arrow: where it is, what it says, and how solid it is.
        void Float(string text, Vector3 at, Color color, float alpha)
        {
            _float.text = text;
            _floatShade.text = text;
            _float.transform.localPosition = at;
            _floatShade.transform.localPosition = at + new Vector3(0.35f, -0.35f, 0.1f);
            _float.color = new Color(color.r, color.g, color.b, alpha);
            _floatShade.color = new Color(0f, 0f, 0f, alpha * 0.7f);
        }
    }
}
