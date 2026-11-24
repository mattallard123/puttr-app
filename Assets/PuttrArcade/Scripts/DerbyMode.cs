// Derby: a horse race at the fairground.
//
// Each Player has a horse in a lane of its own, and every putt moves it down
// the track. Turns go round, one putt each.
//
//   short        never reached the hole: the horse stays where it is.
//   reached      missed, but got there: 1 length (Hard: nothing).
//   close        lipped out, or missed within two inches of the line: 2 lengths
//                (Hard: a lip-out only, 1 length).
//   holed        a gallop: 3 lengths. Dead center and the Player putts again.
//
// Every putt is from a new distance and tee, dealt from the putter's difficulty.
//
// The post is at 12 lengths. The round is finished so that everyone has had
// the same number of turns, and the horse furthest past the post wins. One
// Player races the clock instead: the score is the putts it took to get home.
//
// The track runs left to right along X, the lanes are stacked along Z with
// the first Player's furthest from the camera, and one length is 64 units.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class DerbyMode : StageMode
    {
        public override string Name => "Derby";
        public override bool Branded => true;
        public override string VoicePrefix => "derby";
        public override int Putter => _turn; // whose difficulty applies to this putt
        public override string Blurb => "A horse race at the fairground.\nReach the hole and your horse moves.\nHole it and it gallops.";
        public override string Rule => "Reach the hole to move, hole it to gallop. First horse to 12 lengths wins.";
        public override GameAudio.Scene Sound => GameAudio.Scene.Stadium;
        // puttr-app-2: a racecourse crowd of its own (the stadium's until it is recorded); the ending is the grandstand's roar.
        public override string BedSlot => "derby_crowd_loop";
        public override string EndSlot => "derby_home";
        public override string Icon => "Trophy";
        public override Color Accent => Make.Amber;
        public override string[] Tags => new[] { "First to 12", "New tee every putt" };
        public override int Difficulty => 1;
        public override int MaxPlayers => 4;
        public override string SoloOpponent => "Three Puttr horses";
        public override string[] HowTo => new[]
        {
            "Take turns, one putt each, from a new distance and tee every time. Hole it: gallop 3 lengths. Hole it dead center: gallop and putt again.",
            "Easy and Medium: lip out or miss within two inches for 2 lengths, reach the hole for 1. Hard: only a lip-out moves you, 1 length. Anything else and your horse stays put.",
            "First horse past the post at 12 lengths wins. If two cross in the same round, the one furthest past wins.",
        };

        // One Player counts putts (fewer is better); several count lengths run.
        public override int Score(int player)
        {
            if (_pos == null || player >= _players) return 0;
            if (_players == 1) return _pos[0] >= Lengths ? _putts[0] : 0;
            return _pos[player];
        }
        public override bool LowerIsBetter => _players == 1;
        public override string ScoreWord(int n) => _players == 1 ? (n == 1 ? "putt" : "putts") : (n == 1 ? "length" : "lengths");

        const int Lengths = 12, MaxHorses = 4;
        const float Unit = 64f;                 // one length along the track
        const float LaneDepth = 84f;
        const float TrackFrom = -180f, TrackTo = 1040f;
        const float FinishX = Lengths * Unit;
        const float HalfTrack = LaneDepth * MaxHorses * 0.5f;
        const float RailZ = HalfTrack + 6f;
        const float HorseScale = 1.1f, BodyY = 44f;
        const float Nose = 54f * HorseScale;    // from the middle of a horse to the tip of its nose
        const float CloseInches = 2f, CenterInches = 0.5f;

        // Raised and three-quarters on: the start is nearer than the post.
        public override Vector3 CameraPosition => At(300f, 800f, -1100f);
        public override Vector3 CameraLook => At(450f, 0f, 20f);
        public override float CameraFov => 30f;
        public override float ShadowDistance => 2600f;

        sealed class Horse
        {
            public Transform Root, Body, Neck, Tail;
            public readonly Transform[] Leg = new Transform[4], Shank = new Transform[4];
        }

        struct Puff
        {
            public Transform T;
            public Material M;
            public float Age, Life, Size;
            public Vector3 Drift;
        }

        static readonly Color DustColor = new Color(0.78f, 0.64f, 0.46f, 1f);

        readonly Horse[] _horses = new Horse[MaxHorses];
        Puff[] _puffs;
        int _nextPuff;
        Material _dark, _skin, _breeches;
        Mesh _upperLeg, _lowerLeg, _tailMesh;

        int _players = 1, _turn;
        // Every lane always has a horse (Matt, 2026-11-23): the lanes the Players leave are filled with horses that
        // run themselves, Puttr 1, 2 and 3, and they run after every round of the Players' putts. _field is how
        // many horses are racing, Players and Puttr's together.
        int _field = 1;
        bool _ai;
        readonly System.Random _dice = new System.Random();

        string HorseName(int i) => i < _players ? Arcade.PlayerName(i) : "Puttr " + (i - _players + 1);
        int[] _pos, _putts;
        bool _bonus, _over;

        static float LaneZ(int player) => (1.5f - player) * LaneDepth;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // Late in the day: a low warm sun from behind the camera's left
            // shoulder, so shadows run long across the lanes, and a cool
            // light from the far end to fill the shade.
            Sun(Root, new Vector3(26f, 52f, 0f), new Color(1f, 0.8f, 0.56f), 2f);
            Sun(Root, new Vector3(35f, -70f, 0f), new Color(0.62f, 0.74f, 1f), 0.55f).shadows = LightShadows.None;

            float mid = (TrackFrom + TrackTo) * 0.5f, run = TrackTo - TrackFrom;

            // Grass all round.
            Props.Grass(out Texture2D turf, out Texture2D blades);
            Material grass = Make.Lit(new Color(0.8f, 0.86f, 0.8f), 0.14f, 0f, turf, blades, 0.9f);
            grass.SetTextureScale("_BaseMap", new Vector2(5000f / 240f, 3000f / 240f));
            Shape(PrimitiveType.Cube, "Grass", Root, new Vector3(mid, -1.5f, 300f), new Vector3(5000f, 2f, 3000f), grass);

            // The dirt: four lanes in two shades.
            Props.Concrete(out Texture2D grit, out Texture2D pits);
            Material[] dirt =
            {
                Make.Lit(new Color(1.37f, 1.04f, 0.66f), 0.08f, 0f, grit, pits, 1f),
                Make.Lit(new Color(1.2f, 0.9f, 0.56f), 0.08f, 0f, grit, pits, 1f),
            };
            foreach (Material m in dirt) m.SetTextureScale("_BaseMap", new Vector2(run / 160f, LaneDepth / 160f));
            for (int i = 0; i < MaxHorses; i++)
                Shape(PrimitiveType.Cube, "Lane " + (i + 1), Root, new Vector3(mid, -1f, LaneZ(i)), new Vector3(run, 2f, LaneDepth), dirt[i % 2]);

            // Chalk: between the lanes, and across the track at every length.
            Material chalk = Make.Glow(new Color(1f, 0.98f, 0.92f, 0.34f), false);
            for (int i = 1; i < MaxHorses; i++)
                Shape(PrimitiveType.Cube, "Lane line", Root, new Vector3(mid, 0.05f, HalfTrack - i * LaneDepth), new Vector3(run, 0.02f, 1.6f), chalk);
            for (int i = 0; i < Lengths; i++)
            {
                Shape(PrimitiveType.Cube, "Length line", Root, new Vector3(i * Unit, 0.05f, 0f), new Vector3(i == 0 ? 3.5f : 2.2f, 0.02f, HalfTrack * 2f), chalk);
                if (i == 0) continue;
                TextMesh number = Words(Root, i.ToString(), new Vector3(i * Unit, 5f, -RailZ - 26f), 10f, new Color(1f, 1f, 1f, 0.75f));
                number.transform.localRotation = Quaternion.Euler(36f, 0f, 0f); // leaning back, to face the camera
            }

            // White rails down both sides.
            Material white = Make.Lit(new Color(0.93f, 0.92f, 0.88f), 0.45f);
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * RailZ;
                Shape(PrimitiveType.Cube, "Rail", Root, new Vector3(mid, 23f, z), new Vector3(run, 3.2f, 3.2f), white);
                Shape(PrimitiveType.Cube, "Rail", Root, new Vector3(mid, 12f, z), new Vector3(run, 2f, 2f), white);
                for (float x = TrackFrom + 20f; x < TrackTo; x += Unit)
                    Shape(PrimitiveType.Cube, "Rail post", Root, new Vector3(x, 11.5f, z), new Vector3(3f, 23f, 3f), white);
            }

            BuildFinish(white);
            BuildGate(white);
            BuildBackdrop();

            // The horses: one coat each, and a jockey in the Player's color.
            _dark = Make.Lit(new Color(0.035f, 0.03f, 0.028f), 0.35f);
            _skin = Make.Lit(new Color(0.76f, 0.56f, 0.43f), 0.4f);
            _breeches = Make.Lit(new Color(0.9f, 0.9f, 0.87f), 0.35f);
            _upperLeg = Props.Limb(18f, 4.4f, 3f);
            _lowerLeg = Props.Limb(17f, 2.8f, 2.1f);
            _tailMesh = Props.Limb(28f, 3.2f, 1.2f);
            int[] coats = { 0x4A3524, 0xC9C2B2, 0x241F1B, 0x8A5526 };
            for (int i = 0; i < MaxHorses; i++)
            {
                Color silk = Hud.PlayerColors[i % Hud.PlayerColors.Length];
                _horses[i] = BuildHorse(i, Make.Lit(Make.Hex(coats[i]), 0.42f), Make.Lit(silk, 0.5f));
                PlaceHorse(i, 0f);
                Pose(_horses[i], 0f, 0f);
            }

            // Dust kicked up behind the hooves: a few soft dots, used in turn.
            Texture2D dot = Make.SoftDot();
            _puffs = new Puff[16];
            for (int i = 0; i < _puffs.Length; i++)
            {
                Material m = Make.Glow(new Color(DustColor.r, DustColor.g, DustColor.b, 0f), false, dot);
                GameObject puff = Card("Dust", Root, Vector3.zero, Vector2.one, m);
                puff.transform.localScale = Vector3.zero;
                _puffs[i] = new Puff { T = puff.transform, M = m, Age = 1f, Life = 1f };
            }
        }

        // The line, and the winning post with its checkered board.
        void BuildFinish(Material white)
        {
            Texture2D check = Checker();
            Material line = Make.Lit(Color.white, 0.2f, 0f, check);
            line.SetTextureScale("_BaseMap", new Vector2(1f, HalfTrack * 2f / 16f));
            Shape(PrimitiveType.Cube, "Finish line", Root, new Vector3(FinishX + 8f, 0f, 0f), new Vector3(16f, 0.3f, HalfTrack * 2f), line);

            Material board = Make.Lit(Color.white, 0.3f, 0f, check);
            board.SetTextureScale("_BaseMap", new Vector2(5f, 3f));
            Material red = Make.Lit(Make.Red, 0.6f);
            float far = RailZ + 14f;
            Shape(PrimitiveType.Cylinder, "Winning post", Root, new Vector3(FinishX + 8f, 78f, far), new Vector3(6f, 78f, 6f), white);
            Shape(PrimitiveType.Cube, "Checkered board", Root, new Vector3(FinishX + 8f, 128f, far - 4f), new Vector3(72f, 44f, 3f), board);
            Shape(PrimitiveType.Cube, "Finish plate", Root, new Vector3(FinishX + 8f, 96f, far - 4f), new Vector3(72f, 17f, 3f), Make.Lit(new Color(0.03f, 0.04f, 0.035f), 0.4f));
            Words(Root, "FINISH", new Vector3(FinishX + 8f, 96f, far - 6f), 8f, Color.white);
            Shape(PrimitiveType.Sphere, "Post ball", Root, new Vector3(FinishX + 8f, 160f, far), Vector3.one * 15f, red);
            // A short post on the near side, so the line has two ends.
            Shape(PrimitiveType.Cylinder, "Near post", Root, new Vector3(FinishX + 8f, 30f, -RailZ - 10f), new Vector3(5f, 30f, 5f), white);
            Shape(PrimitiveType.Sphere, "Post ball", Root, new Vector3(FinishX + 8f, 64f, -RailZ - 10f), Vector3.one * 11f, red);
        }

        // The starting gate: a frame over the first line, with a plate in each Player's color.
        void BuildGate(Material white)
        {
            const float x = 3f, top = 104f;
            for (int i = 0; i <= MaxHorses; i++)
                Shape(PrimitiveType.Cylinder, "Gate post", Root, new Vector3(x, top * 0.5f, HalfTrack - i * LaneDepth), new Vector3(4f, top * 0.5f, 4f), white);
            Shape(PrimitiveType.Cube, "Gate beam", Root, new Vector3(x, top, 0f), new Vector3(6f, 6f, HalfTrack * 2f + 6f), white);
            for (int i = 0; i < MaxHorses; i++)
                Shape(PrimitiveType.Cube, "Gate plate", Root, new Vector3(x, top + 9f, LaneZ(i)), new Vector3(9f, 13f, 34f), Make.Lit(Hud.PlayerColors[i % Hud.PlayerColors.Length], 0.5f));
        }

        // Beyond the far rail: lamp posts strung with bulbs, a board, and the stand.
        void BuildBackdrop()
        {
            const float lampZ = 250f, wallZ = 312f, center = 440f, wide = 2800f;

            Material iron = Make.Lit(new Color(0.03f, 0.05f, 0.04f), 0.4f, 0.4f);
            Material lamp = Make.Emissive(Color.white, new Color(1f, 0.82f, 0.5f) * 7f);
            Material warm = Make.Emissive(Color.white, new Color(1f, 0.74f, 0.36f) * 4f);
            Material amber = Make.Emissive(Make.Amber, Make.Amber * 3f);
            Material halo = Make.Glow(new Color(1f, 0.78f, 0.45f, 0.3f), true, Make.SoftDot());
            for (int i = 0; i < 6; i++)
            {
                float x = -110f + i * 220f;
                Shape(PrimitiveType.Cylinder, "Lamp post", Root, new Vector3(x, 62f, lampZ), new Vector3(3.5f, 62f, 3.5f), iron);
                Quiet(Shape(PrimitiveType.Sphere, "Lamp", Root, new Vector3(x, 130f, lampZ), Vector3.one * 15f, lamp));
                Card("Lamp glow", Root, new Vector3(x, 130f, lampZ - 9f), new Vector2(150f, 150f), halo);
                if (i == 5) break;
                // A string of bulbs sagging to the next post.
                for (int b = 1; b <= 6; b++)
                {
                    float t = b / 7f;
                    Quiet(Shape(PrimitiveType.Sphere, "Bulb", Root, new Vector3(x + 220f * t, 122f - 26f * 4f * t * (1f - t), lampZ), Vector3.one * 5.5f, b % 2 == 0 ? amber : warm));
                }
            }

            // The board in the infield.
            Shape(PrimitiveType.Cube, "Board", Root, new Vector3(center, 52f, lampZ + 30f), new Vector3(186f, 50f, 4f), Make.Lit(new Color(0.02f, 0.035f, 0.028f), 0.4f));
            Shape(PrimitiveType.Cube, "Board light", Root, new Vector3(center, 78.5f, lampZ + 29f), new Vector3(190f, 3f, 5f), amber);
            for (int side = -1; side <= 1; side += 2)
                Shape(PrimitiveType.Cylinder, "Board leg", Root, new Vector3(center + side * 70f, 14f, lampZ + 31f), new Vector3(4f, 14f, 4f), iron);
            Words(Root, "PUTTR DERBY", new Vector3(center, 58f, lampZ + 27f), 13f, Make.Amber);
            Words(Root, "FIRST TO 12 LENGTHS", new Vector3(center, 38f, lampZ + 27f), 5.5f, new Color(1f, 1f, 1f, 0.8f));

            // The stand: a wall with a lit top edge, and a bank of people going up and away.
            Shape(PrimitiveType.Cube, "Stand wall", Root, new Vector3(center, 18f, wallZ), new Vector3(wide, 36f, 6f), Make.Lit(new Color(0.03f, 0.1f, 0.06f), 0.35f));
            Shape(PrimitiveType.Cube, "Stand edge", Root, new Vector3(center, 37f, wallZ), new Vector3(wide, 2.5f, 7f), amber);
            // Four wall boards (Brand.Panel): the host's, with Puttr's on the last. With no kit, all Puttr.
            var marks = new Dictionary<Texture2D, Material>();
            for (int i = -2, n = 0; i <= 2; i++)
            {
                if (i == 0) continue;
                Texture2D logo = Brand.Panel(n++, 4);
                if (logo == null) continue;
                if (!marks.TryGetValue(logo, out Material mark)) marks[logo] = mark = Make.Glow(new Color(1.1f, 1.1f, 1.1f, 0.95f), false, logo);
                Card("Wall logo", Root, new Vector3(center + i * 260f, 18f, wallZ - 3.5f), Brand.Size(logo, 84f), mark);
            }
            Material crowd = Make.Glow(Color.white, false, Crowd());
            crowd.SetTextureScale("_MainTex", new Vector2(5f, 1f));
            Card("Stand", Root, new Vector3(center, 146f, wallZ + 136f), new Vector2(wide, 350f), crowd).transform.localRotation = Quaternion.Euler(51f, 0f, 0f);
        }

        static void Quiet(GameObject go) => go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        static Transform Node(string name, Transform parent, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        // A horse facing +X, standing on its origin, with a jockey up.
        Horse BuildHorse(int index, Material coat, Material silk)
        {
            var h = new Horse();
            h.Root = Node("Horse " + (index + 1), Root, Vector3.zero);
            h.Root.localScale = Vector3.one * HorseScale;
            h.Body = Node("Body", h.Root, new Vector3(0f, BodyY, 0f));

            Shape(PrimitiveType.Sphere, "Barrel", h.Body, Vector3.zero, new Vector3(54f, 25f, 21f), coat);
            Shape(PrimitiveType.Sphere, "Chest", h.Body, new Vector3(17f, 1f, 0f), new Vector3(25f, 27f, 21.5f), coat);
            Shape(PrimitiveType.Sphere, "Rump", h.Body, new Vector3(-17f, 1.5f, 0f), new Vector3(27f, 28f, 22.5f), coat);

            // Neck and head turn together about the withers.
            h.Neck = Node("Neck", h.Body, new Vector3(22f, 6f, 0f));
            Props.Rod("Neck", h.Neck, new Vector3(-2f, -2f, 0f), new Vector3(15f, 24f, 0f), 6.2f, coat);
            Props.Rod("Mane", h.Neck, new Vector3(-8f, 4f, 0f), new Vector3(9f, 30f, 0f), 3f, _dark);
            Shape(PrimitiveType.Sphere, "Head", h.Neck, new Vector3(22f, 22f, 0f), new Vector3(23f, 11.5f, 10.5f), coat).transform.localRotation = Quaternion.Euler(0f, 0f, -32f);
            Shape(PrimitiveType.Sphere, "Muzzle", h.Neck, new Vector3(30f, 17.5f, 0f), new Vector3(9f, 8.5f, 8.5f), _dark);
            for (int side = -1; side <= 1; side += 2)
                Shape(PrimitiveType.Sphere, "Ear", h.Neck, new Vector3(14f, 31f, side * 3.2f), new Vector3(3.2f, 8f, 2.6f), coat);

            h.Tail = Node("Tail", h.Body, new Vector3(-29f, 7f, 0f));
            Make.MeshObject("Tail", h.Tail, _tailMesh, _dark);

            // Legs hang from the body: 0 and 1 in front, 2 and 3 behind.
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                float side = i % 2 == 0 ? -1f : 1f;
                h.Leg[i] = Node("Leg", h.Body, new Vector3(front ? 17f : -19f, -6f, side * 6.5f));
                Make.MeshObject("Upper leg", h.Leg[i], _upperLeg, coat);
                h.Shank[i] = Node("Knee", h.Leg[i], new Vector3(0f, -18f, 0f));
                Make.MeshObject("Lower leg", h.Shank[i], _lowerLeg, coat);
                Shape(PrimitiveType.Cube, "Hoof", h.Shank[i], new Vector3(0f, -17.5f, 0f), new Vector3(5.5f, 4.5f, 5.5f), _dark);
            }

            // The jockey, crouched forward over the neck.
            Shape(PrimitiveType.Cube, "Saddle cloth", h.Body, new Vector3(-3f, 5f, 0f), new Vector3(17f, 14f, 22.4f), silk);
            Shape(PrimitiveType.Cube, "Saddle", h.Body, new Vector3(-2f, 13f, 0f), new Vector3(12f, 3f, 12f), _dark);
            Shape(PrimitiveType.Sphere, "Seat", h.Body, new Vector3(-4f, 18f, 0f), new Vector3(9.5f, 8f, 10.5f), _breeches);
            Shape(PrimitiveType.Capsule, "Silks", h.Body, new Vector3(2f, 25f, 0f), new Vector3(11f, 8.5f, 11f), silk).transform.localRotation = Quaternion.Euler(0f, 0f, -52f);
            Shape(PrimitiveType.Sphere, "Cap", h.Body, new Vector3(11f, 33.5f, 0f), Vector3.one * 9.5f, silk);
            Shape(PrimitiveType.Sphere, "Face", h.Body, new Vector3(13f, 31.5f, 0f), Vector3.one * 6.6f, _skin);
            for (int side = -1; side <= 1; side += 2)
            {
                Props.Rod("Arm", h.Body, new Vector3(6f, 28f, side * 5.2f), new Vector3(18f, 19f, side * 3.5f), 1.7f, silk);
                Props.Rod("Thigh", h.Body, new Vector3(-4f, 17f, side * 6.5f), new Vector3(5f, 11f, side * 11.5f), 2.3f, _breeches);
                Props.Rod("Boot", h.Body, new Vector3(5f, 11f, side * 11.5f), new Vector3(0f, 1f, side * 11.8f), 1.9f, _dark);
            }
            return h;
        }

        // Black and white squares, two by two.
        static Texture2D Checker()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            Color black = new Color(0.03f, 0.035f, 0.03f, 1f);
            tex.SetPixels(new[] { Color.white, black, black, Color.white });
            tex.Apply(false);
            return tex;
        }

        // Rows of people in the evening light: hats, shirts and a few flags of color.
        static Texture2D Crowd()
        {
            const int w = 512, h = 128;
            var rng = new System.Random(12);
            Color[] shirts =
            {
                new Color(0.5f, 0.42f, 0.32f), new Color(0.6f, 0.56f, 0.5f), new Color(0.22f, 0.3f, 0.5f), new Color(0.6f, 0.22f, 0.18f),
                new Color(0.7f, 0.5f, 0.12f), new Color(0.2f, 0.4f, 0.24f), new Color(0.34f, 0.3f, 0.3f),
            };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                bool row = y % 8 < 5;                       // people, then the step they stand on
                float high = 1f - y / (float)h * 0.55f;      // the back rows are in shade
                for (int x = 0; x < w; x += 2)
                {
                    Color c = new Color(0.035f, 0.05f, 0.045f);
                    if (row && rng.NextDouble() < 0.8) c = shirts[rng.Next(shirts.Length)] * (0.35f + (float)rng.NextDouble() * 0.5f) * high;
                    c.a = 1f;
                    px[y * w + x] = c;
                    px[y * w + x + 1] = c;
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            HideTeeSign();
            DealtFeetNow = 0;
            _players = Mathf.Clamp(players, 1, MaxHorses);
            _turn = 0;
            _bonus = false;
            _over = false;
            _ai = _players < MaxHorses;
            _field = MaxHorses;
            _pos = new int[_field];
            _putts = new int[_field];
            var names = new List<string>();
            for (int i = 0; i < _field; i++) names.Add(Arcade != null && i < _players ? Arcade.PlayerName(i) : i < _players ? "Player " + (i + 1) : "Puttr " + (i - _players + 1));
            Arcade.Hud.BuildBoard(names, Lengths, "LENGTHS", false, "TRACK", 16f);
            Arcade.Audio.Library("derby_bugle", 0.75f);   // puttr-app-2: the call to post (nothing until it is recorded)
            for (int i = 0; i < MaxHorses; i++)
            {
                _horses[i].Root.gameObject.SetActive(i < _field);
                PlaceHorse(i, 0f);
                Pose(_horses[i], 0f, 0f);
            }
            ClearDust();
            ShowBoard();
        }

        // Behind the home screen: a full field, part way round.
        public override void Preview()
        {
            Begin(1);
            int[] sample = { 8, 10, 4, 6 };
            for (int i = 0; i < MaxHorses; i++)
            {
                _horses[i].Root.gameObject.SetActive(true);
                PlaceHorse(i, sample[i]);
            }
        }

        public override string Prompt
        {
            get
            {
                if (_pos == null) return "";
                if (_players == 1)
                {
                    int left = Mathf.Max(0, Lengths - _pos[0]);
                    return "Putt " + (_putts[0] + 1) + "   ·   " + left + (left == 1 ? " length" : " lengths") + " to the post";
                }
                return Arcade.PlayerName(_turn) + (_bonus ? " putts again" : " is next") + "   ·   Any putt that reaches the hole moves you";
            }
        }

        public override IEnumerator Play(PuttResult putt)
        {
            int player = _turn;
            bool holed = putt.Made, reached = putt.Launch.Reached;
            float line = Line(putt);
            bool lip = !holed && putt.HasEntry;
            // On Hard a horse only moves for a holed putt or a lip-out.
            bool hard = Arcade.Level == Arcade.Hard;
            int lengths = holed ? 3 : !reached ? 0 : hard ? (lip ? 1 : 0) : lip || Mathf.Abs(line) <= CloseInches ? 2 : 1;

            bool firstHome = !AnyHome();
            int leader = Leader();
            int from = _pos[player], to = from + lengths;

            // The horse does it first; the words come after.
            if (lengths == 0) yield return Refuse(_horses[player]);
            else
            {
                if (holed && !GameAudio.Has("derby_home")) Arcade.Audio.Bell();   // puttr-app-2: the shared bell until the racecourse is recorded
                yield return Gallop(player, from, to, holed);
            }
            _pos[player] = to;
            _putts[player]++;

            bool home = from < Lengths && to >= Lengths;
            // Putting again for a dead-center make is between Players only: alone, Puttr's horses run after every putt
            // (a tester holing every one dead center never saw them move).
            bool again = holed && Mathf.Abs(line) < CenterInches && to < Lengths && _players > 1;
            bool leads = _field > 1 && lengths > 0 && leader != player && Leader() == player;
            string lead = leads ? "   ·   " + Arcade.PlayerName(player) + " takes the lead" : "";

            Hud hud = Arcade.Hud;
            string result;
            Color color;
            if (home)
            {
                result = holed ? "Holed" : "Home"; color = Make.Bright;
                string who = _players > 1 ? Arcade.PlayerName(player) + " is home" : "Home in " + _putts[0] + (_putts[0] == 1 ? " putt" : " putts");
                hud.Banner("PAST THE POST", firstHome ? who : who + " too", Make.Amber, 2.4f);
                // puttr-app-2: the grandstand and the horse (the shared stings until they are recorded).
                if (Arcade.Audio.TryLibrary("derby_home", 1f)) Arcade.Audio.Excite(1f);
                else { Arcade.Audio.Strike(); Arcade.Audio.Roar(true); }
                Arcade.Audio.Library("horse_neigh", 0.6f);
                Arcade.Shake(0.5f);
                Arcade.Celebrate(110, At(FinishX, 50f, LaneZ(player)));
            }
            else if (holed)
            {
                result = "Holed"; color = Make.Bright;
                bool center = Mathf.Abs(line) < CenterInches;
                hud.Banner("GALLOP!  +3", again ? "Dead center: putt again" : (center ? "Dead center" : "Holed") + lead, Make.Amber, 2.2f);
                if (Arcade.Audio.TryLibrary("derby_home", center ? 0.85f : 0.65f)) Arcade.Audio.Excite(center ? 1f : 0.65f);   // puttr-app-2
                else if (center) Arcade.Audio.Strike(); else Arcade.Audio.Spare();
                Arcade.Celebrate(center ? 70 : 40, At(to * Unit - Nose, 60f, LaneZ(player)));
            }
            else if (lengths == 2)
            {
                result = lip ? "Lipped out" : "Close"; color = Make.Amber;
                hud.Banner("+2", (lip ? "Lipped out" : "Within two inches") + lead, Make.Bright, 2f);
                if (Arcade.Audio.TryLibrary("derby_home", 0.45f, 1.05f)) Arcade.Audio.Excite(0.65f);   // puttr-app-2
                else { Arcade.Audio.Coin(); Arcade.Audio.Roar(false); }
            }
            else if (lengths == 1)
            {
                result = lip ? "Lipped out" : "Reached"; color = Color.white;
                hud.Banner("+1", (lip ? "Lipped out" : "Reached the hole") + lead, Color.white, 2f);
                if (GameAudio.Has("derby_home")) Arcade.Audio.Excite(0.35f); else Arcade.Audio.Coin();   // puttr-app-2: the hooves said it
            }
            else if (reached)
            {
                result = "Missed"; color = Make.Red;
                hud.Banner("MISSED", "On Hard only a holed putt or a lip-out moves your horse", Make.Red, 2f);
                if (!Arcade.Audio.TryLibrary("derby_snort", 0.8f)) Arcade.Audio.Miss();   // puttr-app-2
            }
            else
            {
                result = "Short"; color = Make.Red;
                hud.Banner("LEFT SHORT", "Your horse stays where it is", Make.Red, 2f);
                if (!Arcade.Audio.TryLibrary("derby_snort", 0.85f, 0.95f)) { Arcade.Audio.Miss(); Arcade.Audio.Groan(); }   // puttr-app-2
            }
            ShowPutt(putt, result, color);

            // Whose turn next. A round is played out before the race is called.
            bool roundDone = false;
            if (!again)
            {
                _turn++;
                if (_turn >= _players)
                {
                    _turn = 0;
                    roundDone = true;
                    if (!_ai && AnyHome()) _over = true;   // with Puttr's horses in the field it is decided below, once they have run
                }
            }
            _bonus = again;
            ShowBoard();

            yield return new WaitForSeconds(0.35f);
            Arcade.Shot("result");
            yield return new WaitForSeconds(0.9f);

            // Against Puttr AI: its three horses each take their turn, and the round is played
            // out before the race is called, so it gets its chance to catch a Player who is home.
            if (_ai && roundDone)
            {
                yield return RunPuttrAi();
                _over = AnyHome();
                ShowBoard();
            }
            if (_over) HideTeeSign();
        }

        // How far a Puttr AI horse runs in a turn: none, 1, 2 or 3 lengths. It is tuned to the
        // Player's own difficulty, since on Easy nearly every putt that gets there is a gallop.
        int AiLengths()
        {
            int level = Arcade.Level;
            int[] odds = level == Arcade.Easy ? new[] { 10, 22, 33, 35 } : level == Arcade.Medium ? new[] { 12, 33, 30, 25 } : new[] { 52, 18, 0, 30 };
            int roll = _dice.Next(100);
            for (int n = 0; n < 4; n++) { if (roll < odds[n]) return n; roll -= odds[n]; }
            return 1;
        }

        IEnumerator RunPuttrAi()
        {
            Hud hud = Arcade.Hud;
            hud.SetPrompt("Puttr's horses are running");
            for (int i = _players; i < _field; i++)
            {
                if (_pos[i] >= Lengths) continue;
                int lengths = AiLengths();
                int leader = Leader();
                int from = _pos[i], to = from + lengths;
                bool firstHome = !AnyHome();
                if (lengths == 0) yield return Refuse(_horses[i]);
                else yield return Gallop(i, from, to, lengths == 3);
                _pos[i] = to;
                _putts[i]++;
                bool home = from < Lengths && to >= Lengths;
                Color ink = Hud.PlayerColors[i % Hud.PlayerColors.Length];
                if (home)
                {
                    hud.Banner("PAST THE POST", HorseName(i) + (firstHome ? " is home" : " is home too"), ink, 1.8f);
                    if (Arcade.Audio.TryLibrary("derby_home", 0.55f)) Arcade.Audio.Excite(0.65f); else Arcade.Audio.Roar(false);   // puttr-app-2
                }
                else if (lengths > 0 && leader != i && Leader() == i) hud.Banner(HorseName(i).ToUpperInvariant(), "+" + lengths + "   ·   takes the lead", ink, 1.3f);
                ShowBoard();
                yield return new WaitForSeconds(home ? 1.2f : 0.25f);
            }
        }

        public override bool Over => _over;

        // Against Puttr AI the card at the end is the finishing order, not a score.
        public override Hud.Line[] Results(out string headline)
        {
            headline = null;
            if (!_ai) return null;
            int best = 0, ties = 0;
            for (int i = 0; i < _field; i++) if (_pos[i] > _pos[best]) best = i;
            for (int i = 0; i < _field; i++) if (_pos[i] == _pos[best]) ties++;
            headline = ties > 1 ? "A dead heat" : best == 0 && _players == 1 ? "You win" : HorseName(best) + " wins";
            var lines = new Hud.Line[_field];
            for (int i = 0; i < _field; i++)
                lines[i] = new Hud.Line { Name = HorseName(i), Value = Mathf.Min(_pos[i], Lengths) + (_pos[i] == 1 ? " length" : " lengths"), Dot = Hud.PlayerColors[i % Hud.PlayerColors.Length], Lit = ties == 1 && i == best, Tag = ties == 1 && i == best ? "WINNER" : null };
            return lines;
        }

        public override void Final(out string title, out string body)
        {
            body = "";
            title = "P A S T   T H E   P O S T";
            int best = 0, ties = 0;
            for (int i = 0; i < _field; i++) if (_pos[i] > _pos[best]) best = i;
            for (int i = 0; i < _field; i++) if (_pos[i] == _pos[best]) ties++;
            for (int i = 0; i < _field; i++)
                body += HorseName(i) + "     " + _pos[i] + (_pos[i] == 1 ? " length" : " lengths") + (ties == 1 && i == best ? "     winner" : "") + "\n";
            if (ties > 1) body += "A dead heat\n";
        }

        public override void Tick()
        {
            if (_puffs == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _puffs.Length; i++)
            {
                if (_puffs[i].Age >= _puffs[i].Life) continue;
                _puffs[i].Age += dt;
                float k = Mathf.Clamp01(_puffs[i].Age / _puffs[i].Life);
                _puffs[i].T.localPosition += _puffs[i].Drift * dt;
                _puffs[i].T.localScale = k >= 1f ? Vector3.zero : Vector3.one * _puffs[i].Size * (0.5f + k);
                _puffs[i].M.SetColor("_Color", new Color(DustColor.r, DustColor.g, DustColor.b, 0.5f * (1f - k)));
            }
        }

        public override void End() { ClearDust(); HideTeeSign(); }

        // ── The board ───────────────────────────────────────────────────────

        // One mark for every length run, and the number beside them.
        void ShowBoard()
        {
            Hud hud = Arcade.Hud;
            for (int i = 0; i < _field; i++)
            {
                for (int p = 0; p < Lengths; p++) hud.SetPip(i, p, p < _pos[i] ? Hud.Pip.Good : Hud.Pip.Empty);
                hud.SetTotal(i, _pos[i]);
            }
            hud.SetTurn(_over ? -1 : _turn, _over || _pos[_turn] >= Lengths ? -1 : _pos[_turn]);
        }

        bool AnyHome()
        {
            for (int i = 0; i < _field; i++) if (_pos[i] >= Lengths) return true;
            return false;
        }

        // Who is in front on their own, or -1 when it is level.
        int Leader()
        {
            int best = 0, ties = 0;
            for (int i = 0; i < _field; i++) if (_pos[i] > _pos[best]) best = i;
            for (int i = 0; i < _field; i++) if (_pos[i] == _pos[best]) ties++;
            return ties == 1 ? best : -1;
        }

        // ── The horses ──────────────────────────────────────────────────────

        // Stand a horse with its nose on this many lengths.
        void PlaceHorse(int index, float lengths)
        {
            _horses[index].Root.localPosition = new Vector3(lengths * Unit - Nose, 0f, LaneZ(index));
        }

        // Run from one mark to another: about a second a length, quicker at a gallop.
        IEnumerator Gallop(int player, int from, int to, bool gallop)
        {
            Horse h = _horses[player];
            float seconds = (to - from) * (gallop ? 0.8f : 1f);
            float stride = gallop ? 58f : 34f, power = gallop ? 1f : 0.55f;
            float phase = 0f, last = from;
            int beats = 0;
            bool shot = false;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float u = t / seconds;
                float at = Mathf.Lerp(from, to, Ease(u));
                // The legs turn over with the ground covered, so the hooves do not slide.
                phase += (at - last) * Unit / stride * Mathf.PI * 2f;
                last = at;
                PlaceHorse(player, at);
                Pose(h, phase, power * Mathf.Clamp01(Mathf.Sin(Mathf.PI * u) * 3f));
                int beat = Mathf.FloorToInt(phase / Mathf.PI);
                if (beat != beats)
                {
                    beats = beat;
                    if (!Arcade.Audio.TryLibrary("derby_hooves", gallop ? 0.55f : 0.35f, 1f + Random.Range(-0.08f, 0.08f))) Arcade.Audio.Thump(gallop ? 16f : 9f);   // puttr-app-2
                    Dust(new Vector3(at * Unit - Nose - 22f * HorseScale, 5f, LaneZ(player) - 5f), gallop ? 26f : 16f);
                }
                if (!shot && u > 0.5f) { shot = true; Arcade.Shot("action"); }
                yield return null;
            }
            PlaceHorse(player, to);
            Pose(h, 0f, 0f);
        }

        // Going nowhere: a shake of the head and a flick of the tail.
        IEnumerator Refuse(Horse h)
        {
            const float seconds = 0.8f;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float fade = 1f - t / seconds;
                h.Neck.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 20f) * 16f * fade, -6f * fade);
                h.Tail.localRotation = Quaternion.Euler(Mathf.Sin(t * 14f) * 24f * fade, 0f, -30f);
                yield return null;
            }
            Pose(h, 0f, 0f);
        }

        // One moment of the stride. phase is in radians, a full turn a stride;
        // power is 0 standing still, about a half at a trot and 1 at a gallop.
        // Joints turn about Z: positive swings a limb forward.
        static void Pose(Horse h, float phase, float power)
        {
            h.Body.localPosition = new Vector3(0f, BodyY + power * 3f * Mathf.Sin(phase), 0f);
            h.Body.localRotation = Quaternion.Euler(0f, 0f, power * 5f * Mathf.Sin(phase + 0.8f));
            h.Neck.localRotation = Quaternion.Euler(0f, 0f, -power * (8f + 10f * Mathf.Sin(phase + 0.4f)));
            h.Tail.localRotation = Quaternion.Euler(0f, 0f, -30f - power * (45f - 8f * Mathf.Sin(phase * 2f)));
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                // Left and right land a little apart, and the hind pair half a stride after the front.
                float p = phase + (i % 2 == 0 ? 0f : 0.6f) + (front ? 0f : Mathf.PI * 0.85f);
                float lift = Mathf.Max(0f, Mathf.Cos(p)); // the leg folds as it comes forward
                if (front)
                {
                    h.Leg[i].localRotation = Quaternion.Euler(0f, 0f, power * 42f * Mathf.Sin(p));
                    h.Shank[i].localRotation = Quaternion.Euler(0f, 0f, -power * 60f * lift);
                }
                else
                {
                    h.Leg[i].localRotation = Quaternion.Euler(0f, 0f, 10f + power * 38f * Mathf.Sin(p));
                    h.Shank[i].localRotation = Quaternion.Euler(0f, 0f, -18f - power * 45f * lift);
                }
            }
        }

        // ── Dust ────────────────────────────────────────────────────────────

        void Dust(Vector3 at, float size)
        {
            int i = _nextPuff;
            _nextPuff = (_nextPuff + 1) % _puffs.Length;
            _puffs[i].Age = 0f;
            _puffs[i].Life = Random.Range(0.55f, 0.9f);
            _puffs[i].Size = size * Random.Range(0.8f, 1.25f);
            _puffs[i].Drift = new Vector3(Random.Range(-26f, -8f), Random.Range(10f, 22f), Random.Range(-6f, 6f));
            _puffs[i].T.localPosition = at + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-4f, 4f));
            _puffs[i].T.localScale = Vector3.one * _puffs[i].Size * 0.5f;
        }

        void ClearDust()
        {
            if (_puffs == null) return;
            for (int i = 0; i < _puffs.Length; i++)
            {
                _puffs[i].Age = _puffs[i].Life;
                _puffs[i].T.localScale = Vector3.zero;
            }
        }
    }
}
