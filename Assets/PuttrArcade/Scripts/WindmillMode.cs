// Windmill: crazy golf, six holes.
//
// Each real putt is one stroke on the hole on screen. The ball plays on from
// where it stops, so a hole takes as many putts as it takes, up to five.
//
//   aim    every hole has a route from tee to cup, a short list of points.
//          The stroke sets off toward the next point on it: the dotted line.
//   mat    as in Tracks in the Puttr app: a stroke of 11 ft or less is
//          putted from its real length, a longer one from a quarter of it.
//   miss   the mat putt's miss, inches wide and inches long or short, is the
//          miss here, scaled back up by that ratio.
//   make   inside the level's window (real putt: Easy any, Medium front 90°,
//          Hard front 60°; long: 10, 5 or 3° at 20 ft) it runs the dotted
//          line exactly; outside it burns the edge and runs on.
//   gimme  a ball that stops under 3 ft from the cup taps in, for a stroke.
//
// The ball is moved here, a 240th of a second at a time, on the plan of the
// hole: x across the screen, z away from the camera. Four units are a foot.
// Walls take 30% of its speed, bumpers give a little back, sand slows it
// three times as fast, water costs a stroke, and the windmill's doorway is
// shut whenever a blade is hanging in front of it.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class WindmillMode : StageMode
    {
        public override string Name => "Windmill";
        public override string VoicePrefix => "windmill";
        public override bool DealsTee => false;
        public override int Putter => _player; // whose difficulty applies to this putt
        public override string Blurb => "Crazy golf, six holes.\nPace sets the distance, line bends it.\nFewest strokes wins.";
        public override GameAudio.Scene Sound => GameAudio.Scene.Table;
        public override int Score(int player) => Totals()[player];
        public override string ScoreWord(int n) => n == 1 ? "stroke" : "strokes";
        public override bool LowerIsBetter => true;
        public override string ScoreUnit => "strokes";
        public override string Rule => "Each putt is one stroke. Pace sets how far it rolls, line bends the shot.";
        public override string Icon => "Joystick";
        public override Color Accent => new Color(0.92f, 0.38f, 0.32f);
        public override string[] Tags => new[] { "6 holes", "Line and pace" };
        public override int Difficulty => 2;
        public override int MaxPlayers => 4;
        public override string[] HowTo => new[]
        {
            "Six holes, and every Player plays a hole out before the next. Each putt is one stroke, and the ball plays on from where it stops.",
            "Putt from the distance on screen. Inside 11 feet it is the real putt: make it and it drops (Medium: in the front 90° of the cup, Hard: the front 60°), miss and it rolls out just as far and as wide as it did on the mat.",
            "Longer, putt a quarter of the way: the miss is scaled back up, and only a make near center cut runs the dotted line. Under 3 feet is a gimme for one stroke. Water costs a stroke, a blade sends it back, five strokes is the most. Fewest wins.",
        };

        const int MaxStrokes = 5;
        const float UnitsPerFoot = 4f;
        const float BallR = 1.5f, CupR = 3.2f;
        const float Gimme = 3f * UnitsPerFoot;            // a ball that stops under 3 ft from the cup taps in, for a stroke

        // Where the putt is played from, as in Tracks in the Puttr app: a shot of 11 ft or less
        // is putted from its real length (3 ft at least), a longer one from a quarter of it
        // (3 to 11 ft), and the mat putt's miss is scaled back up by the ratio.
        const float RealUpToFt = 11f, LagDivisor = 4f;
        int _matFt;
        float _shotFt, _ratio = 1f;
        bool _toCup;
        const float FeltY = 1.2f, KerbTop = 3.4f, KerbHalf = 1f;
        const float Reach = BallR + KerbHalf; // how close the ball's center gets to the middle of a kerb

        // The stroke.
        const float Decel = 43f, SandDrag = 3f, StopSpeed = 1.2f;
        const float WallKeep = 0.7f, BumperKick = 12f, CaptureSpeed = 62f;
        const int MaxKicks = 4;                          // bumpers stop kicking after this many in one stroke
        const float NearPoint = 8f;                      // this close to a point on the route, aim at the one after
        const float Step = 1f / 240f;

        // The windmill.
        const float BladeSpeed = 45f, BlockDeg = 12f, BladeLength = 13f;
        const float DoorTop = 5.5f, HubUp = 9f, BladesOut = 7.8f;

        // ── The holes ───────────────────────────────────────────────────────

        // Everything here is on the plan: a Vector2 is (x, z).
        struct Seg
        {
            public Vector2 A, B;
            public Seg(float ax, float az, float bx, float bz) { A = new Vector2(ax, az); B = new Vector2(bx, bz); }
        }

        struct Bumper { public float X, Z, R; }

        struct Oval
        {
            public float X, Z, RX, RZ;
            public bool Holds(Vector2 p)
            {
                float dx = (p.x - X) / RX, dz = (p.y - Z) / RZ;
                return dx * dx + dz * dz < 1f;
            }
        }

        sealed class Hole
        {
            public string Name;
            public int Par;
            /// <summary>The outer kerb, as a closed loop. The felt fills it.</summary>
            public Vector2[] Outline;
            /// <summary>The route: tee first, cup last.</summary>
            public Vector2[] Way;
            /// <summary>Kerbs inside the outline.</summary>
            public Seg[] Walls = new Seg[0];
            /// <summary>Walls with no kerb of their own: the sides of the windmill's tunnel.</summary>
            public Seg[] Unseen = new Seg[0];
            public Bumper[] Bumpers = new Bumper[0];
            public Oval[] Sand = new Oval[0], Water = new Oval[0];
            /// <summary>A windmill stands at Mill, facing the camera. Gate is its doorway.</summary>
            public bool HasMill;
            public Vector2 Mill;
            public Seg Gate;
            public Vector2 Tee => Way[0];
            public Vector2 Cup => Way[Way.Length - 1];
        }

        static Vector2 P(float x, float z) => new Vector2(x, z);

        static readonly Hole[] Holes =
        {
            // 1. Straight down the middle. A firm putt can hole it in one.
            new Hole
            {
                Name = "The Runway", Par = 2,
                Outline = new[] { P(-48, -10), P(48, -10), P(48, 10), P(-48, 10) },
                Way = new[] { P(-38, 0), P(34, 0) },
            },
            // 2. A dog-leg. The angled kerb in the corner turns a straight putt up the second leg.
            new Hole
            {
                Name = "Bank Shot", Par = 3,
                Outline = new[] { P(-55, -30), P(20.5f, -30), P(35, -15.5f), P(35, 30), P(17, 30), P(17, -12), P(-55, -12) },
                Way = new[] { P(-47, -21), P(26, -21), P(26, 22) },
            },
            // 3. The windmill: through the doorway while no blade is in front of it.
            new Hole
            {
                Name = "The Windmill", Par = 2,
                Outline = new[] { P(-30, -32), P(30, -32), P(30, 32), P(-30, 32) },
                Way = new[] { P(0, -24), P(0, -10), P(0, 26) },
                Walls = new[] { new Seg(-30, 0, -4.5f, 0), new Seg(4.5f, 0, 30, 0) },
                Unseen = new[] { new Seg(-4.5f, -6.5f, -4.5f, 6.5f), new Seg(4.5f, -6.5f, 4.5f, 6.5f) },
                HasMill = true, Mill = P(0, 0), Gate = new Seg(-4.5f, -7.1f, 4.5f, -7.1f),
            },
            // 4. Two bumpers, then a funnel down to the cup. The straight line goes between them.
            new Hole
            {
                Name = "Pinball", Par = 3,
                Outline = new[] { P(-55, -24), P(10, -24), P(34, -8), P(55, -8), P(55, 8), P(34, 8), P(10, 24), P(-55, 24) },
                Way = new[] { P(-46, 0), P(47, 0) },
                Bumpers = new[] { new Bumper { X = -8, Z = -6.5f, R = 4 }, new Bumper { X = 12, Z = 6.5f, R = 4 } },
            },
            // 5. A pond in the way. The route goes round the far side of it.
            new Hole
            {
                Name = "The Pond", Par = 3,
                Outline = new[] { P(-55, -28), P(55, -28), P(55, 28), P(-55, 28) },
                Way = new[] { P(-46, -6), P(0, 19), P(46, -6) },
                Water = new[] { new Oval { X = 0, Z = -4, RX = 20, RZ = 14 } },
            },
            // 6. A long S with a bank at each turn, and sand beside the line all the way.
            new Hole
            {
                Name = "The Long S", Par = 4,
                Outline = new[] { P(-55, -30), P(-5.5f, -30), P(9, -15.5f), P(9, 12), P(55, 12), P(55, 30), P(5.5f, 30), P(-9, 15.5f), P(-9, -12), P(-55, -12) },
                Way = new[] { P(-46, -21), P(0, -21), P(0, 21), P(46, 21) },
                Sand = new[]
                {
                    new Oval { X = -20, Z = -26, RX = 8, RZ = 3.5f },
                    new Oval { X = 5, Z = 0, RX = 4.5f, RZ = 8 },
                    new Oval { X = 26, Z = 16.5f, RX = 8, RZ = 4 },
                },
            },
        };

        // What was built for one hole.
        sealed class Course
        {
            public GameObject Root;
            public Seg[] Solid;
            public Transform Blades;
            public Transform[] Bumpers;
            public float[] Pulse;
        }

        // Raised and tilted about 55 degrees down, so kerbs and the mill show their height.
        public override Vector3 CameraPosition => At(0f, 133f, -98f);
        public override Vector3 CameraLook => At(0f, 0f, -3f);
        public override float CameraFov => 30f;
        public override float ShadowDistance => 320f;

        // The card: the hole with the windmill on it, from low enough to see the mill stand up.
        public override Vector3 CardFrom => At(0f, 62f, -78f);
        public override Vector3 CardLook => At(0f, 9f, 2f);
        public override float CardFov => 30f;
        public override void PoseForCard() { for (int i = 0; i < _courses.Length; i++) _courses[i].Root.SetActive(i == 2); }
        public override void AfterCard() { for (int i = 0; i < _courses.Length; i++) _courses[i].Root.SetActive(i == 0); }

        Course[] _courses;
        Transform _ball;
        GameObject _shadow;
        LineRenderer _aim;
        int _players, _hole, _player, _strokes;
        int[,] _score;
        Vector2 _lie;
        float _spin;
        bool _announce;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // The sun is high and behind, so shadows fall toward the camera;
            // a softer light from the front fills the faces it leaves dark.
            Sun(Root, new Vector3(50f, 150f, 0f), new Color(1f, 0.97f, 0.9f), 1.6f);
            Sun(Root, new Vector3(42f, -24f, 0f), new Color(0.82f, 0.9f, 1f), 0.85f).shadows = LightShadows.None;

            // A dark patio, with a few round paving stones.
            Props.Concrete(out Texture2D slab, out Texture2D pits);
            Material patio = Make.Lit(new Color(0.1f, 0.22f, 0.29f), 0.3f, 0f, slab, pits, 0.8f);
            patio.SetTextureScale("_BaseMap", new Vector2(700f / 60f, 500f / 60f));
            Shape(PrimitiveType.Cube, "Patio", Root, new Vector3(0f, -1f, 10f), new Vector3(700f, 2f, 500f), patio);
            Material stone = Make.Lit(new Color(0.13f, 0.28f, 0.36f), 0.25f);
            float[,] stones = { { -78f, 44f, 9f }, { 80f, -40f, 12f }, { 76f, 46f, 7f }, { -84f, -34f, 6f } };
            for (int i = 0; i < stones.GetLength(0); i++)
                Make.MeshObject("Paving stone", Root, Make.Disc(stones[i, 2]), stone).transform.localPosition = new Vector3(stones[i, 0], 0.04f, stones[i, 1]);

            // Shared paint.
            Props.Grass(out Texture2D turf, out Texture2D blades);
            var paint = new Paint
            {
                Felt = Make.Lit(new Color(0.85f, 1f, 1.15f), 0.12f, 0f, turf, blades, 0.8f),
                Wood = Make.Lit(new Color(0.5f, 0.32f, 0.16f), 0.3f),
                Red = Make.Lit(new Color(0.76f, 0.17f, 0.14f), 0.4f),
                White = Make.Lit(new Color(0.95f, 0.94f, 0.9f), 0.4f),
                Dark = Make.Lit(new Color(0.02f, 0.05f, 0.03f), 0.1f),
                Sand = Make.Lit(new Color(0.82f, 0.71f, 0.48f), 0.08f),
                Water = Make.Lit(new Color(0.1f, 0.4f, 0.78f), 0.92f),
                Bank = Make.Lit(new Color(0.42f, 0.44f, 0.46f), 0.3f),
                Flag = Make.Lit(Make.Amber, 0.3f),
                Tee = Make.Glow(new Color(1f, 1f, 1f, 0.4f), false),
            };

            _courses = new Course[Holes.Length];
            for (int i = 0; i < Holes.Length; i++)
            {
                _courses[i] = BuildHole(i, paint);
                _courses[i].Root.SetActive(i == 0);
            }

            // The ball, and a soft shadow that goes where it goes.
            Make.Ball(out Texture2D dimples, out Texture2D dimpleNormal);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.6f, 0f, dimples, dimpleNormal, 0.6f)).transform;
            _shadow = Card("Ball shadow", Root, Vector3.zero, new Vector2(BallR * 3.4f, BallR * 3.4f), Make.Glow(new Color(0f, 0f, 0f, 0.5f), false, Make.SoftDot()));
            _shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // The aim line: a white line, broken into dots by its texture.
            _aim = Stroke(Root, "Aim", new Color(1f, 1f, 1f, 0.9f), 0.7f, false);
            Material dots = Make.Glow(new Color(1f, 1f, 1f, 0.9f), false, Dashes());
            dots.SetTextureScale("_MainTex", new Vector2(0.4f, 1f));
            _aim.sharedMaterial = dots;
            _aim.textureMode = LineTextureMode.Tile;
            _aim.numCapVertices = 0;

            _lie = Holes[0].Tee;
            PlaceBall(_lie, 0f);
        }

        sealed class Paint { public Material Felt, Wood, Red, White, Dark, Sand, Water, Bank, Flag, Tee; }

        Course BuildHole(int index, Paint paint)
        {
            Hole hole = Holes[index];
            var course = new Course { Root = new GameObject("Hole " + (index + 1)) };
            Transform root = course.Root.transform;
            root.SetParent(Root, false);

            // The felt, and a kerb along every edge of it with a post at every corner.
            var solid = new List<Seg>();
            Make.MeshObject("Felt", root, Felt(hole.Outline), paint.Felt).transform.localPosition = new Vector3(0f, FeltY, 0f);
            for (int i = 0; i < hole.Outline.Length; i++)
            {
                var edge = new Seg { A = hole.Outline[i], B = hole.Outline[(i + 1) % hole.Outline.Length] };
                solid.Add(edge);
                Kerb(root, edge, paint.Wood);
                Post(root, edge.A, paint.Wood);
            }
            foreach (Seg wall in hole.Walls)
            {
                solid.Add(wall);
                Kerb(root, wall, paint.Wood);
                Post(root, wall.A, paint.Wood);
                Post(root, wall.B, paint.Wood);
            }
            solid.AddRange(hole.Unseen);
            course.Solid = solid.ToArray();

            // Sand and water lie just proud of the felt.
            foreach (Oval sand in hole.Sand) Patch("Sand", root, sand, 0f, 0.05f, paint.Sand);
            foreach (Oval water in hole.Water)
            {
                Patch("Pond edge", root, water, 1.2f, 0.04f, paint.Bank);
                Patch("Water", root, water, 0f, 0.07f, paint.Water);
            }

            // Bumpers: a wooden drum with a red dome on it.
            course.Bumpers = new Transform[hole.Bumpers.Length];
            course.Pulse = new float[hole.Bumpers.Length];
            for (int i = 0; i < hole.Bumpers.Length; i++)
            {
                Bumper b = hole.Bumpers[i];
                var bumper = new GameObject("Bumper").transform;
                bumper.SetParent(root, false);
                bumper.localPosition = new Vector3(b.X, FeltY, b.Z);
                Shape(PrimitiveType.Cylinder, "Drum", bumper, new Vector3(0f, 1.3f, 0f), new Vector3(b.R * 2f, 1.3f, b.R * 2f), paint.White);
                Shape(PrimitiveType.Sphere, "Dome", bumper, new Vector3(0f, 2.6f, 0f), new Vector3(b.R * 1.8f, 3f, b.R * 1.8f), paint.Red);
                course.Bumpers[i] = bumper;
            }

            if (hole.HasMill) course.Blades = BuildMill(root, hole.Mill, paint);

            // The tee: a pale spot and a word.
            GameObject tee = Make.MeshObject("Tee", root, Make.Disc(2.2f), paint.Tee);
            tee.transform.localPosition = Flat(hole.Tee, 0.06f);
            tee.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Lay(Words(root, "TEE", Flat(hole.Tee + new Vector2(0f, -5f), 0.1f), 1.5f, new Color(1f, 1f, 1f, 0.8f)));

            // The cup: a dark hole with a white rim, a stick and a flag.
            Vector2 cup = hole.Cup;
            Make.MeshObject("Cup", root, Make.Disc(CupR), paint.Dark).transform.localPosition = Flat(cup, 0.05f);
            Make.MeshObject("Cup rim", root, Make.Ring(CupR, CupR + 0.35f), paint.White).transform.localPosition = Flat(cup, 0.06f);
            Props.Rod("Flag stick", root, Flat(cup, 0f), Flat(cup, 14f), 0.2f, paint.White);
            Make.MeshObject("Flag", root, Pennant(6f, 3.4f), paint.Flag).transform.localPosition = Flat(cup, 10.4f);

            // Its name goes on the board above (ShowHole). Painted on the patio in front of the
            // hole it sat under the banners and behind the home screen's words.
            return course;
        }

        // Words stand facing the camera. This lays them flat, to be read from above.
        static void Lay(TextMesh words) => words.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        static void Kerb(Transform parent, Seg s, Material wood)
        {
            Vector2 along = s.B - s.A, mid = (s.A + s.B) * 0.5f;
            GameObject kerb = Shape(PrimitiveType.Cube, "Kerb", parent, new Vector3(mid.x, KerbTop * 0.5f, mid.y), new Vector3(along.magnitude, KerbTop, KerbHalf * 2f), wood);
            kerb.transform.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg, 0f);
        }

        // A round post where kerbs meet, so a corner at any angle closes up.
        static void Post(Transform parent, Vector2 at, Material wood)
        {
            Shape(PrimitiveType.Cylinder, "Post", parent, new Vector3(at.x, KerbTop * 0.5f, at.y), new Vector3(KerbHalf * 2f, KerbTop * 0.5f, KerbHalf * 2f), wood);
        }

        static void Patch(string name, Transform parent, Oval oval, float grow, float lift, Material material)
        {
            GameObject patch = Make.MeshObject(name, parent, Make.Disc(1f), material);
            patch.transform.localPosition = new Vector3(oval.X, FeltY + lift, oval.Z);
            patch.transform.localScale = new Vector3(oval.RX + grow, 1f, oval.RZ + grow);
            patch.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // A red tower on two piers, a white cap, and four white blades on the
        // side the ball comes from. Returns what turns.
        static Transform BuildMill(Transform parent, Vector2 at, Paint paint)
        {
            var mill = new GameObject("Windmill").transform;
            mill.SetParent(parent, false);
            mill.localPosition = new Vector3(at.x, 0f, at.y);
            float sill = FeltY + DoorTop;
            for (int side = -1; side <= 1; side += 2)
                Shape(PrimitiveType.Cube, "Pier", mill, new Vector3(side * 4.5f, sill * 0.5f, 0f), new Vector3(2.2f, sill, 13.2f), paint.Red);
            Shape(PrimitiveType.Cube, "Lintel", mill, new Vector3(0f, sill + 0.4f, 0f), new Vector3(13.6f, 0.8f, 13.6f), paint.White);
            Make.MeshObject("Tower", mill, Frustum(6.5f, 4f, 14f), paint.Red).transform.localPosition = new Vector3(0f, sill + 0.8f, 0f);
            Make.MeshObject("Cap", mill, Frustum(4.7f, 0.2f, 5f), paint.White).transform.localPosition = new Vector3(0f, sill + 14.8f, 0f);
            Shape(PrimitiveType.Cube, "Window", mill, new Vector3(0f, sill + 4f, -5.75f), new Vector3(2.4f, 3f, 0.4f), paint.Dark);

            // The axle comes out of the front of the tower; the blades turn on the end of it.
            float hub = sill + HubUp;
            Props.Rod("Axle", mill, new Vector3(0f, hub, -4.5f), new Vector3(0f, hub, -BladesOut - 0.5f), 0.5f, paint.Dark);
            var blades = new GameObject("Blades").transform;
            blades.SetParent(mill, false);
            blades.localPosition = new Vector3(0f, hub, -BladesOut);
            for (int i = 0; i < 4; i++)
            {
                // Each blade is built hanging straight down, then turned to its place.
                var blade = new GameObject("Blade").transform;
                blade.SetParent(blades, false);
                blade.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
                Shape(PrimitiveType.Cube, "Spar", blade, new Vector3(0f, -BladeLength * 0.5f, 0f), new Vector3(0.7f, BladeLength, 0.5f), paint.Wood);
                Shape(PrimitiveType.Cube, "Sail", blade, new Vector3(0f, -BladeLength + 4.2f, -0.1f), new Vector3(5f, 8.4f, 0.3f), paint.White);
            }
            Shape(PrimitiveType.Sphere, "Hub", blades, Vector3.zero, Vector3.one * 2.2f, paint.Dark);
            return blades;
        }

        // ── Meshes ──────────────────────────────────────────────────────────

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        // The outline filled in, flat and facing up, by cutting off one
        // corner at a time. The mown stripes run away from the camera.
        static Mesh Felt(Vector2[] outline)
        {
            int n = outline.Length;
            float area = 0f;
            for (int i = 0; i < n; i++) area += Cross(outline[i], outline[(i + 1) % n]);
            var left = new List<int>();
            for (int i = 0; i < n; i++) left.Add(area > 0f ? i : n - 1 - i); // counter-clockwise on the plan
            var tris = new List<int>();
            for (int guard = 0; left.Count > 3 && guard < 500; guard++)
            {
                bool cut = false;
                for (int i = 0; i < left.Count && !cut; i++)
                {
                    int ia = left[(i + left.Count - 1) % left.Count], ib = left[i], ic = left[(i + 1) % left.Count];
                    Vector2 a = outline[ia], b = outline[ib], c = outline[ic];
                    if (Cross(b - a, c - b) <= 0f) continue; // the corner turns inward: not one to cut off
                    bool clear = true;
                    foreach (int k in left)
                    {
                        if (k == ia || k == ib || k == ic) continue;
                        Vector2 p = outline[k];
                        if (Cross(b - a, p - a) > 0.0001f && Cross(c - b, p - b) > 0.0001f && Cross(a - c, p - c) > 0.0001f) { clear = false; break; }
                    }
                    if (!clear) continue;
                    tris.Add(ia); tris.Add(ic); tris.Add(ib); // clockwise from above faces up
                    left.RemoveAt(i);
                    cut = true;
                }
                if (!cut) break;
            }
            if (left.Count == 3) { tris.Add(left[0]); tris.Add(left[2]); tris.Add(left[1]); }

            var verts = new Vector3[n];
            var normals = new Vector3[n];
            var uvs = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3(outline[i].x, 0f, outline[i].y);
                normals[i] = Vector3.up;
                uvs[i] = new Vector2(outline[i].y / 20f, outline[i].x / 20f); // one stripe every 10 units across
            }
            var mesh = new Mesh { name = "Felt", vertices = verts, normals = normals, uv = uvs, triangles = tris.ToArray() };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Four corners in order round a face, turned to face the way given.
        static void Face(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int at = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            tris.Add(at); tris.Add(flip ? at + 2 : at + 1); tris.Add(flip ? at + 1 : at + 2);
            tris.Add(at); tris.Add(flip ? at + 3 : at + 2); tris.Add(flip ? at + 2 : at + 3);
        }

        // A square tower that narrows as it rises: half its width at the foot and at the top.
        static Mesh Frustum(float foot, float top, float height)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float[] sx = { -1f, 1f, 1f, -1f }, sz = { -1f, -1f, 1f, 1f };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                var a = new Vector3(sx[i] * foot, 0f, sz[i] * foot);
                var b = new Vector3(sx[j] * foot, 0f, sz[j] * foot);
                var c = new Vector3(sx[j] * top, height, sz[j] * top);
                var d = new Vector3(sx[i] * top, height, sz[i] * top);
                Face(verts, tris, a, b, c, d, a + b);
            }
            Face(verts, tris, new Vector3(-top, height, -top), new Vector3(top, height, -top), new Vector3(top, height, top), new Vector3(-top, height, top), Vector3.up);
            var mesh = new Mesh { name = "Frustum" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A triangular flag, flying to the right of its stick, seen from both sides.
        static Mesh Pennant(float length, float height)
        {
            Vector3 a = new Vector3(0f, height, 0f), b = new Vector3(length, height * 0.5f, 0f), c = Vector3.zero;
            var mesh = new Mesh { name = "Pennant", vertices = new[] { a, b, c, a, b, c }, triangles = new[] { 0, 1, 2, 3, 5, 4 } };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Half white, half clear: tiled along a line it makes dots.
        static Texture2D Dashes()
        {
            var tex = new Texture2D(4, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels(new[] { Color.white, Color.white, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0f) });
            tex.Apply(false);
            return tex;
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = players;
            _hole = 0;
            _player = 0;
            _strokes = 0;
            _score = new int[players, Holes.Length];
            var names = new List<string>();
            for (int i = 0; i < players; i++) names.Add(Arcade.PlayerName(i));
            Arcade.Hud.BuildBoard(names, Holes.Length, "TOTAL", true, "STROKES ON EACH HOLE");
            ShowScores();
            Arcade.Hud.SetTurn(0, 0);
            ShowHole(0);
            TeeUp();
            _announce = false; // the arcade announces the game itself
        }

        // Only the hole being played is switched on.
        void ShowHole(int index)
        {
            for (int i = 0; i < _courses.Length; i++) _courses[i].Root.SetActive(i == index);
            Arcade.Hud.SetBoardTitle("HOLE " + (index + 1) + " OF " + Holes.Length + "   ·   " + Holes[index].Name.ToUpperInvariant() + "   ·   PAR " + Holes[index].Par);
        }

        void TeeUp()
        {
            _lie = Holes[_hole].Tee;
            PlaceBall(_lie, 0f);
            Aim();
        }

        public override string Tee => Over || _matFt == 0 ? null : _matFt + " feet, center tee";

        // Work out this stroke's mat putt from where the ball lies, put it up, and call it.
        void PlanShot(bool aloud)
        {
            Hole hole = Holes[_hole];
            Vector2 target = Target(hole, _lie);
            _toCup = target == hole.Cup;
            _shotFt = Mathf.Max(0.5f, (target - _lie).magnitude / UnitsPerFoot);
            float mat = _shotFt <= RealUpToFt ? _shotFt : _shotFt / LagDivisor;
            _matFt = Mathf.Clamp(Mathf.RoundToInt(mat), 3, 11);
            _ratio = _shotFt / _matFt;
            Arcade.Hud.SetTeeSign(_matFt, 2);
            if (aloud) Arcade.Audio.Voice(VoicePrefix + "_distance_" + _matFt + "_feet", VoicePrefix + "_tee_center");
        }

        // How far either side of center cut a make may enter and still count, in degrees.
        // A real putt: Easy anywhere, Medium the front 90°, Hard the front 60°. A lag: Easy 10,
        // Medium 5, Hard 3 at 20 ft, tighter in proportion beyond.
        float Window()
        {
            int level = Arcade.Level;
            if (_shotFt <= RealUpToFt) return level == Arcade.Easy ? 180f : level == Arcade.Medium ? 45f : 30f;
            float at20 = level == Arcade.Easy ? 10f : level == Arcade.Medium ? 5f : 3f;
            return at20 * Mathf.Min(1f, 20f / _shotFt);
        }

        public override IEnumerator Prepare()
        {
            if (Over) { Arcade.Hud.SetTeeSign(0, 0); yield break; }
            PlaceBall(_lie, 0f);
            Aim();
            PlanShot(true);
            if (_announce)
            {
                _announce = false;
                string who = _players > 1 ? Arcade.PlayerName(_player) + "   ·   " : "";
                Arcade.Hud.Banner("HOLE " + (_hole + 1), who + Holes[_hole].Name + "   ·   Par " + Holes[_hole].Par, Color.white, 1.6f);
            }
        }

        public override string Prompt
        {
            get
            {
                if (Over) return "";
                string who = _players > 1 ? Arcade.PlayerName(_player) + "   ·   " : "";
                string hint = _shotFt > RealUpToFt ? "   ·   " + Mathf.RoundToInt(_shotFt) + " ft: putt a quarter of it" : "";
                return who + "Hole " + (_hole + 1) + "   ·   Stroke " + (_strokes + 1) + " of " + MaxStrokes + hint;
            }
        }

        // Where a stroke from here sets off toward: the end of the nearest
        // leg of the route, or the point after it if that one is already close.
        static Vector2 Target(Hole hole, Vector2 from)
        {
            int leg = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < hole.Way.Length - 1; i++)
            {
                float d = Distance(from, hole.Way[i], hole.Way[i + 1]);
                if (d < nearest) { nearest = d; leg = i; }
            }
            int next = leg + 1;
            while (next < hole.Way.Length - 1 && (hole.Way[next] - from).magnitude < NearPoint) next++;
            return hole.Way[next];
        }

        // How far p is from the line between a and b.
        static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        // Turn a direction on the plan to the right, as the ball sees it.
        static Vector2 Turn(Vector2 d, float degrees)
        {
            float cos = Mathf.Cos(degrees * Mathf.Deg2Rad), sin = Mathf.Sin(degrees * Mathf.Deg2Rad);
            return new Vector2(d.x * cos + d.y * sin, -d.x * sin + d.y * cos);
        }

        // Keep the ball off a wall. True if it was going into it, and has been turned back.
        static bool Bounce(ref Vector2 p, ref Vector2 v, Seg wall, float keep)
        {
            Vector2 ab = wall.B - wall.A;
            float t = Mathf.Clamp01(Vector2.Dot(p - wall.A, ab) / ab.sqrMagnitude);
            Vector2 on = wall.A + ab * t, away = p - on;
            float gap = away.magnitude;
            if (gap >= Reach || gap < 0.0001f) return false;
            Vector2 n = away / gap;
            p = on + n * Reach;
            float into = Vector2.Dot(v, n);
            if (into >= 0f) return false;
            v = (v - 2f * into * n) * keep;
            return true;
        }

        // A blade is in front of the doorway when it hangs within a few degrees of straight down.
        bool MillShut()
        {
            float a = Mathf.Repeat(_spin, 90f);
            return Mathf.Min(a, 90f - a) < BlockDeg;
        }

        enum Fate { Rolling, Stopped, Holed, Splash, Lost }

        public override IEnumerator Play(PuttResult putt)
        {
            Hole hole = Holes[_hole];
            Course course = _courses[_hole];
            Hud hud = Arcade.Hud;
            _strokes++;
            ShowScores();
            _aim.positionCount = 0;

            // The stroke, read the Tracks way: the mat putt was played at this shot's length (or a
            // quarter of it), so its miss, in inches wide and inches long or short, is the miss
            // here, scaled back up by the ratio. A make inside the level's window runs the dotted
            // line exactly; a make outside it burns the edge and runs on.
            Vector2 from = _lie, p = from;
            Vector2 aimAt = Target(hole, p);
            Vector2 dir = aimAt - p;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
            float pace = Pace(putt);
            float legIn = Vector2.Distance(aimAt, p) / UnitsPerFoot * 12f;
            float off = putt.HasEntry ? Mathf.Abs(putt.OffCenter) : 999f;
            bool clean = putt.Made && off <= Window();
            float wideIn, longIn;
            if (clean) { wideIn = 0f; longIn = 0f; }
            else if (putt.Made)
            {
                // Burned the edge: half a foot to the side it went in, a foot and a half past.
                float scale = _ratio > 1f ? _ratio * 0.5f : 1f;
                wideIn = (putt.OffCenter < 0f ? -6f : 6f) * scale;
                longIn = 18f * scale;
            }
            else
            {
                float shortBy = 0f;
                if (!putt.Launch.Reached)
                {
                    shortBy = 60f;
                    if (putt.PathInches != null) foreach (Vector2 q in putt.PathInches) shortBy = Mathf.Min(shortBy, q.magnitude);
                }
                float speed = putt.Launch.HandoffSpeedInPerSec > 0f ? putt.Launch.HandoffSpeedInPerSec : putt.Launch.SpeedInPerSec;
                wideIn = Line(putt) * _ratio;
                longIn = (putt.Launch.Reached ? PaceWords.Past(speed) : -shortBy) * _ratio;
            }
            // The cup here is drawn far bigger than a real one, so a near miss that gets there is
            // sent just round its rim rather than through it.
            float rimIn = (CupR + BallR) / UnitsPerFoot * 12f;
            if (!clean && _toCup && longIn > -rimIn && Mathf.Abs(wideIn) < rimIn) wideIn = (wideIn < 0f ? -1f : 1f) * rimIn;
            dir = Turn(dir, Mathf.Atan2(wideIn, Mathf.Max(6f, legIn)) * Mathf.Rad2Deg);
            float carryIn = Mathf.Sqrt(legIn * legIn + wideIn * wideIn) + longIn;
            float carry = Mathf.Max(0.5f, carryIn / 12f * UnitsPerFoot);
            // Only a clean make drops on a putt at the cup; a miss rolls by at its real width.
            bool cupOpen = clean || !_toCup;
            if (clean && _toCup) carry += CupR; // dies in the middle of the cup
            Vector2 v = dir * Mathf.Sqrt(2f * Decel * carry);
            Arcade.Audio.Thump(pace);

            Fate fate = Fate.Rolling;
            bool inGate = false, shut = false, knockedBack = false, lipped = false, inSand = false, shot = false;
            int kicks = 0;
            float clock = 0f, owed = 0f, lastKnock = -1f;
            Vector2 shown = p;
            while (fate == Fate.Rolling)
            {
                float knock = 0f;
                bool ping = false;
                owed += Time.deltaTime;
                while (owed >= Step && fate == Fate.Rolling)
                {
                    owed -= Step;
                    clock += Step;

                    // Slow down: three times as fast in sand.
                    float speed = v.magnitude;
                    inSand = false;
                    foreach (Oval sand in hole.Sand) if (sand.Holds(p)) inSand = true;
                    float slower = speed - Decel * (inSand ? SandDrag : 1f) * Step;
                    if (slower <= StopSpeed || clock > 12f) { fate = Fate.Stopped; break; }
                    v *= slower / speed;
                    p += v * Step;

                    // Kerbs.
                    foreach (Seg wall in course.Solid)
                        if (Bounce(ref p, ref v, wall, WallKeep)) knock = Mathf.Max(knock, slower);

                    // The windmill's doorway: open or shut is settled as the ball arrives.
                    if (hole.HasMill)
                    {
                        if (Distance(p, hole.Gate.A, hole.Gate.B) < Reach)
                        {
                            if (!inGate) { inGate = true; shut = MillShut(); }
                            if (shut && Bounce(ref p, ref v, hole.Gate, WallKeep)) { knockedBack = true; knock = Mathf.Max(knock, 50f); }
                        }
                        else inGate = false;
                    }

                    // Bumpers give a little back, the first few times.
                    for (int i = 0; i < hole.Bumpers.Length; i++)
                    {
                        Bumper b = hole.Bumpers[i];
                        var center = new Vector2(b.X, b.Z);
                        Vector2 away = p - center;
                        float gap = away.magnitude, reach = b.R + BallR;
                        if (gap >= reach || gap < 0.0001f) continue;
                        Vector2 n = away / gap;
                        p = center + n * reach;
                        float into = Vector2.Dot(v, n);
                        if (into >= 0f) continue;
                        v -= 2f * into * n;
                        if (kicks++ < MaxKicks) v += n * BumperKick; else v *= WallKeep;
                        course.Pulse[i] = 1f;
                        ping = true;
                    }

                    // Water, the cup, and the edge of the world.
                    foreach (Oval water in hole.Water) if (water.Holds(p)) fate = Fate.Splash;
                    if (fate == Fate.Rolling && cupOpen && (p - hole.Cup).sqrMagnitude < CupR * CupR)
                    {
                        if (v.magnitude < CaptureSpeed) fate = Fate.Holed;
                        else lipped = true; // too fast: over the top
                    }
                    if (fate == Fate.Rolling && (Mathf.Abs(p.x) > 75f || Mathf.Abs(p.y) > 50f)) fate = Fate.Lost;
                }

                // Show it: roll the ball over the ground it covered.
                Vector2 moved = p - shown;
                if (moved.sqrMagnitude > 0f) _ball.Rotate(new Vector3(moved.y, 0f, -moved.x), moved.magnitude / BallR * Mathf.Rad2Deg, Space.World);
                shown = p;
                PlaceBall(p, 0f);
                Arcade.Audio.SetRoll(v.magnitude * 0.5f);
                if (ping) Arcade.Audio.Post();
                else if (knock > 0f && clock - lastKnock > 0.06f) { lastKnock = clock; Arcade.Audio.Thump(knock * 0.5f); }
                if (!shot && clock > 0.5f) { shot = true; Arcade.Shot("action"); }
                yield return null;
            }
            Arcade.Audio.SetRoll(0f);

            // Into the cup or the pond: the ball goes down.
            // Under 3 ft: a gimme. It taps in, for a stroke.
            bool gimme = false;
            if (fate == Fate.Stopped && (p - hole.Cup).magnitude < Gimme) { fate = Fate.Holed; gimme = true; _strokes++; }
            if (fate == Fate.Holed || fate == Fate.Splash)
            {
                Vector2 to = fate == Fate.Holed ? hole.Cup : p;
                float depth = fate == Fate.Holed ? BallR * 1.5f : BallR * 2.4f;
                for (float t = 0f; t < 0.25f; t += Time.deltaTime)
                {
                    float k = Ease(t / 0.25f);
                    PlaceBall(Vector2.Lerp(p, to, k), depth * k);
                    yield return null;
                }
                PlaceBall(to, depth);
                _shadow.SetActive(false);
            }

            // Which side of the doorway it started and finished on.
            bool through = hole.HasMill && fate != Fate.Splash && fate != Fate.Lost
                && Cross(hole.Gate.B - hole.Gate.A, from - hole.Gate.A) * Cross(hole.Gate.B - hole.Gate.A, p - hole.Gate.A) < 0f;

            if (fate == Fate.Splash) _strokes++; // one stroke penalty
            bool holed = fate == Fate.Holed;
            bool pickedUp = !holed && _strokes >= MaxStrokes;
            float left = (p - hole.Cup).magnitude;
            string feet = Mathf.Max(1, Mathf.RoundToInt(left / UnitsPerFoot)) + " ft to the cup";
            Color blue = new Color(0.45f, 0.75f, 1f);

            if (holed)
            {
                ShowPutt(putt, gimme ? "Gimme" : "In the cup", Make.Bright);
                hud.Banner(gimme ? "GIMME" : "IN THE CUP", (gimme ? "Under 3 feet: tapped in   ·   " : "") + Verdict(_strokes, hole.Par), Make.Bright, 2.2f);
                Arcade.Audio.Holed();
                if (_strokes <= hole.Par) Arcade.Audio.Coin();
                Arcade.Celebrate(_strokes <= hole.Par ? 90 : 40, At(hole.Cup.x, FeltY + 2f, hole.Cup.y));
            }
            else if (pickedUp)
            {
                ShowPutt(putt, "Picked up", Make.Red);
                hud.Banner("Picked up", (fate == Fate.Splash ? "In the water.   " : "") + "Five strokes is the most: that is a " + (MaxStrokes + 1), Make.Red, 2.2f);
                if (fate == Fate.Splash) Arcade.Audio.Douse();
                Arcade.Audio.Miss();
            }
            else if (fate == Fate.Splash)
            {
                ShowPutt(putt, "In the water", blue);
                hud.Banner("SPLASH", "One stroke penalty. Play it again from the same spot", blue, 2.2f);
                Arcade.Audio.Douse();
                Arcade.Shake(0.3f);
            }
            else if (fate == Fate.Lost)
            {
                ShowPutt(putt, "Off the course", Make.Amber);
                hud.Banner("OFF THE COURSE", "Play it again from the same spot", Make.Amber, 2f);
                Arcade.Audio.Miss();
            }
            else if (through)
            {
                ShowPutt(putt, feet, Make.Bright);
                hud.Banner("Through the mill!", feet, Make.Bright, 2f);
                Arcade.Audio.Coin();
            }
            else
            {
                string why = knockedBack ? "The blade sent it back" : lipped ? "Too fast: it ran over the cup" : putt.Made && !clean ? "Burned the edge: it has to go in nearer center cut" : inSand ? "In the sand" : "";
                ShowPutt(putt, feet, knockedBack ? Make.Amber : Color.white);
                hud.Banner(feet, why, knockedBack ? Make.Amber : Color.white, 1.8f);
            }
            ShowScores();
            Arcade.Shot("result");
            yield return new WaitForSecondsRealtime(1.5f);

            // Where the next stroke is played from, and by whom.
            if (holed || pickedUp)
            {
                _score[_player, _hole] = holed ? _strokes : MaxStrokes + 1;
                _strokes = 0;
                _player++;
                if (_player >= _players)
                {
                    _player = 0;
                    _hole++;
                    if (!Over) ShowHole(_hole);
                }
                if (!Over) TeeUp();
                _announce = true;
            }
            else
            {
                _lie = fate == Fate.Splash || fate == Fate.Lost ? from : p;
                PlaceBall(_lie, 0f);
                Aim();
            }
            ShowScores();
            hud.SetTurn(Over ? -1 : _player, Over ? -1 : _hole);
        }

        static string Verdict(int strokes, int par)
        {
            if (strokes == 1) return "Hole in one";
            int over = strokes - par;
            string name = over <= -2 ? "Eagle" : over == -1 ? "Birdie" : over == 0 ? "Par" : over == 1 ? "Bogey" : over == 2 ? "Double bogey" : "";
            return (name.Length > 0 ? name + "   ·   " : "") + "In " + strokes + " strokes";
        }

        public override bool Over => _hole >= Holes.Length;

        public override void Final(out string title, out string body)
        {
            int[] total = Totals();
            int par = 0;
            foreach (Hole hole in Holes) par += hole.Par;
            body = "";
            if (_players == 1)
            {
                title = total[0] + "  S T R O K E S";
                body = "Par for the six holes is " + par + ".\n";
                return;
            }
            title = "S I X   H O L E S";
            int best = 0, ties = 0;
            for (int i = 0; i < _players; i++) if (total[i] < total[best]) best = i;
            for (int i = 0; i < _players; i++) if (total[i] == total[best]) ties++;
            for (int i = 0; i < _players; i++)
                body += Arcade.PlayerName(i) + "     " + total[i] + " strokes" + (ties == 1 && i == best ? "     winner" : "") + "\n";
            if (ties > 1) body += "A draw\n";
        }

        public override void Tick()
        {
            if (_courses == null) return;
            // The blades never stop, and a bumper that was hit swells and settles.
            _spin = Mathf.Repeat(_spin + Time.deltaTime * BladeSpeed, 360f);
            Course course = _courses[Mathf.Clamp(_hole, 0, _courses.Length - 1)];
            if (course.Blades != null) course.Blades.localRotation = Quaternion.Euler(0f, 0f, _spin);
            for (int i = 0; i < course.Bumpers.Length; i++)
            {
                course.Pulse[i] = Mathf.MoveTowards(course.Pulse[i], 0f, Time.deltaTime * 5f);
                course.Bumpers[i].localScale = Vector3.one * (1f + course.Pulse[i] * 0.22f);
            }
        }

        public override void End()
        {
            if (_aim != null) _aim.positionCount = 0;
        }

        // ── Ball, aim line and scores ───────────────────────────────────────

        /// <summary>A point on the plan, this far above the felt.</summary>
        static Vector3 Flat(Vector2 p, float lift) => new Vector3(p.x, FeltY + lift, p.y);

        void PlaceBall(Vector2 p, float sunk)
        {
            _ball.localPosition = Flat(p, BallR - sunk);
            _shadow.SetActive(sunk <= 0f);
            _shadow.transform.localPosition = Flat(p + new Vector2(0.5f, -0.5f), 0.09f);
        }

        // The dotted line, from just in front of the ball to the point it is aimed at.
        void Aim()
        {
            if (Over) { _aim.positionCount = 0; return; }
            Vector2 to = Target(Holes[_hole], _lie);
            Vector2 dir = to - _lie;
            if (dir.magnitude < BallR * 2f) { _aim.positionCount = 0; return; }
            _aim.positionCount = 2;
            _aim.SetPosition(0, Flat(_lie + dir.normalized * (BallR + 0.8f), 0.4f));
            _aim.SetPosition(1, Flat(to, 0.4f));
        }

        // Strokes on finished holes, plus the ones taken so far on the hole in play.
        int[] Totals()
        {
            var total = new int[_players];
            for (int i = 0; i < _players; i++)
                for (int h = 0; h < Holes.Length; h++) total[i] += _score[i, h];
            if (!Over) total[_player] += _strokes;
            return total;
        }

        void ShowScores()
        {
            int[] total = Totals();
            for (int i = 0; i < _players; i++)
            {
                for (int h = 0; h < Holes.Length; h++)
                {
                    int s = _score[i, h];
                    if (s > 0) Arcade.Hud.SetPip(i, h, s <= Holes[h].Par ? Hud.Pip.Good : Hud.Pip.Over, s.ToString());
                    else if (i == _player && h == _hole && _strokes > 0) Arcade.Hud.SetPip(i, h, Hud.Pip.Empty, _strokes.ToString());
                    else Arcade.Hud.SetPip(i, h, Hud.Pip.Empty, "");
                }
                Arcade.Hud.SetTotal(i, total[i]);
            }
        }
    }
}
