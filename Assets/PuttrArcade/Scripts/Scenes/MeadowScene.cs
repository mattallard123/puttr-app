// Greenlands: the green every stage is played on, dressed for its world. The camera sits low behind the ball.
//
// The rules are the app's (PuttrApp Greenlands.cs, StageGame); this only shows them. view.Data says what to show:
//   world      1 to 8: the sky, the land round the green, the scenery, the boss
//   kind       "Boss": the world's boss stands behind the cup with a heart for each hit it can take
//   window     degrees: two lines out of the front of the cup mark the way in (a gate, a boss that narrows it)
//   paceMin    inches: a band behind the cup from there on (the putt must still be rolling that far past)
//   paceMax    inches: a band behind the cup up to there (the putt must die inside it)
//   lamp       the mill stands over the cup: its sails sweep the hole and its lamp is green while they are clear
//   why        why the last putt did not count: "blocked", "wide", "soft", "firm", "back", "missed"
//
// The green is the same in every world: scanned mown grass, a rougher fringe, and a cup cut into it (a soil lip, a
// white liner set below it, a soft shadow round the hole). There is no flagstick. Units are inches; the cup is at
// the origin and the ball comes from -z.
//
// Sounds are the stage's own slots (gl_*, a boss's gl_wN_boss_*), falling back to shared ones until recorded.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class MeadowScene : RuleScene
    {
        const float LaneW = 48f, BallR = 1.1f, CupR = 2.4f;
        static readonly Color Amber = new Color(1f, 0.7f, 0f), Heart = new Color(1f, 0.32f, 0.32f);

        // A world's look: the sky, the land, the two rows of hills, the sun, and what stands for trees.
        struct Look
        {
            public Color Sky, Land, HillNear, HillFar, Sun;
            public float SunPower;
            public int Trees;   // 0 round trees, 1 pines, 2 cacti, 3 none
        }

        static Look LookFor(int world)
        {
            switch (world)
            {
                case 2: return new Look { Sky = new Color(0.6f, 0.83f, 0.97f), Land = new Color(0.2f, 0.58f, 0.27f), HillNear = new Color(0.27f, 0.64f, 0.31f), HillFar = new Color(0.4f, 0.73f, 0.44f), Sun = new Color(1f, 0.97f, 0.9f), SunPower = 1f, Trees = 0 };
                case 3: return new Look { Sky = new Color(0.93f, 0.8f, 0.58f), Land = new Color(0.84f, 0.7f, 0.42f), HillNear = new Color(0.8f, 0.64f, 0.36f), HillFar = new Color(0.9f, 0.77f, 0.5f), Sun = new Color(1f, 0.93f, 0.78f), SunPower = 1.15f, Trees = 2 };
                case 4: return new Look { Sky = new Color(0.74f, 0.85f, 0.95f), Land = new Color(0.9f, 0.94f, 0.98f), HillNear = new Color(0.82f, 0.88f, 0.95f), HillFar = new Color(0.93f, 0.96f, 1f), Sun = new Color(0.92f, 0.96f, 1f), SunPower = 0.95f, Trees = 1 };
                case 5: return new Look { Sky = new Color(0.38f, 0.26f, 0.5f), Land = new Color(0.47f, 0.26f, 0.24f), HillNear = new Color(0.56f, 0.29f, 0.26f), HillFar = new Color(0.4f, 0.24f, 0.36f), Sun = new Color(1f, 0.78f, 0.6f), SunPower = 0.9f, Trees = 3 };
                case 6: return new Look { Sky = new Color(0.22f, 0.05f, 0.04f), Land = new Color(0.14f, 0.12f, 0.12f), HillNear = new Color(0.18f, 0.13f, 0.12f), HillFar = new Color(0.26f, 0.1f, 0.08f), Sun = new Color(1f, 0.7f, 0.5f), SunPower = 0.85f, Trees = 3 };
                case 7: return new Look { Sky = new Color(0.42f, 0.7f, 1f), Land = new Color(0.94f, 0.96f, 1f), HillNear = new Color(1f, 1f, 1f), HillFar = new Color(0.9f, 0.94f, 1f), Sun = new Color(1f, 0.99f, 0.95f), SunPower = 1.1f, Trees = 3 };
                case 8: return new Look { Sky = new Color(0.07f, 0.06f, 0.17f), Land = new Color(0.2f, 0.2f, 0.25f), HillNear = new Color(0.16f, 0.16f, 0.22f), HillFar = new Color(0.12f, 0.12f, 0.2f), Sun = new Color(0.72f, 0.78f, 1f), SunPower = 0.8f, Trees = 3 };
                default: return new Look { Sky = new Color(0.53f, 0.80f, 0.96f), Land = new Color(0.18f, 0.56f, 0.26f), HillNear = new Color(0.24f, 0.62f, 0.3f), HillFar = new Color(0.35f, 0.7f, 0.41f), Sun = new Color(1f, 0.97f, 0.9f), SunPower = 1f, Trees = 0 };
            }
        }

        Transform _ball, _start, _boss, _body, _gate, _mill, _millBody, _rotor, _band;
        GameObject _lampGreen, _lampRed;
        LineRenderer _gateL, _gateR;
        readonly List<Transform> _hearts = new List<Transform>();
        readonly List<Transform> _spinners = new List<Transform>();   // far windmills, turning slowly
        Vector3 _cam = new Vector3(0, 30f, -100f), _look = new Vector3(0, 2f, 4f);
        int _feet = -1, _tee = -1, _world = 1;
        bool _isBoss, _down, _lamp;
        double _lampStart;
        float _lampOpen = 4f, _lampShut = 2.5f, _bossTop = 24f;

        public override Vector3 CameraFrom => At(_cam);
        public override Vector3 CameraLook => At(_look);
        public override float CameraFov => 40f;
        public override float ShadowDistance => 600f;

        static Vector3 SpotFor(int feet, int tee) => new Vector3((tee - 2) * 8f, 0f, -feet * 12f);

        public override void Build()
        {
            // The world is known before the set is built: the app sets the opening view first (RuleStageMode.StartView).
            _world = RuleStageMode.StartView != null ? Mathf.Clamp(RuleStageMode.StartView.Int("world", 1), 1, 8) : 1;
            Look look = LookFor(_world);

            // Sky: a wall behind everything, which casts no shadow.
            GameObject sky = Shape(PrimitiveType.Cube, "Sky", Root, new Vector3(0, 1500f, 2600f), new Vector3(16000f, 7000f, 2f), Make.Emissive(Color.black, look.Sky));
            sky.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Material land = _world == 7 ? Make.Emissive(look.Land * 0.8f, look.Land * 0.3f) : Make.Lit(look.Land, 0.05f);
            Shape(PrimitiveType.Cube, "Land", Root, new Vector3(0, -1.9f, 600f), new Vector3(6000f, 2f, 6000f), land);
            Material hillNear = Make.Lit(look.HillNear, 0.05f), hillFar = Make.Lit(look.HillFar, 0.05f);
            Shape(PrimitiveType.Sphere, "Hill", Root, new Vector3(-620f, -90f, 900f), new Vector3(1300f, 420f, 700f), hillNear);
            Shape(PrimitiveType.Sphere, "Hill", Root, new Vector3(700f, -110f, 1000f), new Vector3(1500f, 460f, 800f), hillNear);
            Shape(PrimitiveType.Sphere, "Hill", Root, new Vector3(40f, -160f, 1500f), new Vector3(2400f, 640f, 900f), hillFar);
            Scenery(look);

            BuildGreen();

            // Where to putt from, and the ball.
            _start = new GameObject("Start").transform;
            _start.SetParent(Root, false);
            Hoop("Spot", _start, new Vector3(0, 0.08f, 0), 2.7f, 3.3f, Make.Emissive(Amber, Amber * 0.9f));
            Make.Ball(out Texture2D dimples, out Texture2D dimplesNormal);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.6f, 0f, dimples, dimplesNormal, 0.6f)).transform;

            // The gate: two lines out of the front of the cup.
            _gate = new GameObject("Gate").transform;
            _gate.SetParent(Root, false);
            _gateL = Stroke(_gate, "Gate left", Amber, 0.5f, false);
            _gateR = Stroke(_gate, "Gate right", Amber, 0.5f, false);
            _gate.gameObject.SetActive(false);

            // The pace band behind the cup.
            Material band = Make.Glow(new Color(0.48f, 0.83f, 0.27f, 0.42f), false);
            _band = Shape(PrimitiveType.Quad, "Pace band", Root, new Vector3(0, 0.07f, 10f), new Vector3(LaneW - 6f, 10f, 1f), band).transform;
            _band.localRotation = Quaternion.Euler(90f, 0, 0);
            _band.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            _band.gameObject.SetActive(false);

            BuildMill();
            BuildBoss();

            Sun(Root, new Vector3(52f, 28f, 0f), look.Sun, look.SunPower);
            // The dark worlds keep the green itself readable.
            if (_world == 6) Lamp(Root, new Vector3(0, 240f, -80f), new Vector3(0, 0, -30f), new Color(1f, 0.75f, 0.55f), 90000f, 70f, 800f);
            if (_world == 8) Lamp(Root, new Vector3(0, 240f, -80f), new Vector3(0, 0, -30f), new Color(0.8f, 0.85f, 1f), 90000f, 70f, 800f);
        }

        // ── The green ────────────────────────────────────────────────────────

        void BuildGreen()
        {
            // Scanned grass (the Tracks ground scans); the arcade's carpet if they are not in the build.
            Texture2D mown = Resources.Load<Texture2D>("PuttrApp/Tracks/GroundMown"), mownN = Resources.Load<Texture2D>("PuttrApp/Tracks/GroundMownNormal");
            Texture2D rough = Resources.Load<Texture2D>("PuttrApp/Tracks/GroundRough"), roughN = Resources.Load<Texture2D>("PuttrApp/Tracks/GroundRoughNormal");
            if (mown == null) Make.Carpet(out mown, out mownN);
            if (rough == null) { rough = mown; roughN = mownN; }
            // The scans are neutral grey (the Tracks shader colours them): the tint is the grass colour itself.
            Color tint = new Color(0.42f, 0.88f, 0.38f);
            Material Turf(float u, float v)
            {
                Material m = Make.Lit(tint, 0.08f, 0f, mown, mownN, 1.1f);
                m.SetTextureScale("_BaseMap", new Vector2(u, v));
                m.SetTextureScale("_BumpMap", new Vector2(u, v));
                return m;
            }
            Material fringe = Make.Lit(new Color(0.24f, 0.6f, 0.24f), 0.05f, 0f, rough, roughN, 1.2f);
            fringe.SetTextureScale("_BaseMap", new Vector2(3f, 13f));
            fringe.SetTextureScale("_BumpMap", new Vector2(3f, 13f));

            // One tile of grass is 24 in: the strips and the patch round the cup line up.
            Shape(PrimitiveType.Cube, "Fringe", Root, new Vector3(0, -0.75f, -58f), new Vector3(LaneW + 16f, 1f, 276f), fringe);
            Shape(PrimitiveType.Cube, "Green", Root, new Vector3(0, -0.5f, -100f), new Vector3(LaneW, 1f, 152f), Turf(2f, 152f / 24f));
            Shape(PrimitiveType.Cube, "Green behind", Root, new Vector3(0, -0.5f, 48f), new Vector3(LaneW, 1f, 48f), Turf(2f, 2f));
            Make.MeshObject("Green by the cup", Root, Make.HolePatch(LaneW / 2f, CupR, 1f / 24f, Vector3.zero, 1f, 96), Turf(1f, 1f));

            // The cup: a lip of soil where the turf was cut, a white liner set an inch down, a dark bottom, and the
            // soft shade a hole has round its edge.
            Material soil = Make.Lit(new Color(0.2f, 0.14f, 0.09f), 0.05f);
            Make.MeshObject("Soil lip", Root, Make.Cup(CupR, 1f, false, 72), soil);
            GameObject edge = Make.MeshObject("Cut edge", Root, Make.Ring(CupR - 0.02f, CupR + 0.16f, 72), soil);
            edge.transform.localPosition = new Vector3(0, 0.03f, 0);
            edge.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Make.MeshObject("Liner", Root, Make.Cup(CupR - 0.06f, 3.2f, false, 72), Make.Lit(new Color(0.93f, 0.93f, 0.9f), 0.5f)).transform.localPosition = new Vector3(0, -1f, 0);
            Make.MeshObject("Cup bottom", Root, Make.Cup(CupR - 0.06f, 0.4f, true, 72), Make.Lit(new Color(0.02f, 0.025f, 0.02f), 0.2f)).transform.localPosition = new Vector3(0, -4.2f, 0);
            GameObject shade = Shape(PrimitiveType.Quad, "Cup shade", Root, new Vector3(0, 0.05f, 0), new Vector3(CupR * 4.6f, CupR * 4.6f, 1f), Make.Glow(new Color(0f, 0f, 0f, 0.5f), false, Make.SoftDot()));
            shade.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            shade.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ── The world round the green ────────────────────────────────────────

        static Mesh Cone(float radius, float height, float baseY = 0f) => Make.Lathe(new List<Vector2> { new Vector2(0.01f, baseY), new Vector2(radius, baseY), new Vector2(0.01f, baseY + height) }, 20);

        void Scenery(Look look)
        {
            Material cloud = Make.Emissive(Color.white, new Color(0.9f, 0.9f, 0.9f));
            if (_world <= 4 || _world == 7)
            {
                Shape(PrimitiveType.Sphere, "Cloud", Root, new Vector3(-520f, 520f, 1700f), new Vector3(520f, 90f, 200f), cloud);
                Shape(PrimitiveType.Sphere, "Cloud", Root, new Vector3(-380f, 560f, 1700f), new Vector3(300f, 80f, 160f), cloud);
                Shape(PrimitiveType.Sphere, "Cloud", Root, new Vector3(640f, 430f, 1700f), new Vector3(460f, 80f, 180f), cloud);
            }
            Vector3[] spots = { new Vector3(-150f, 0, 150f), new Vector3(-230f, 0, 330f), new Vector3(170f, 0, 210f), new Vector3(260f, 0, 420f), new Vector3(-90f, 0, 520f), new Vector3(110f, 0, 600f) };
            Material trunk = Make.Lit(new Color(0.36f, 0.24f, 0.13f), 0.1f);
            if (look.Trees == 0)
            {
                Material leaf = Make.Lit(new Color(0.11f, 0.4f, 0.17f), 0.05f);
                foreach (Vector3 at in spots)
                {
                    Shape(PrimitiveType.Cylinder, "Trunk", Root, at + Vector3.up * 26f, new Vector3(9f, 26f, 9f), trunk);
                    Shape(PrimitiveType.Sphere, "Leaves", Root, at + Vector3.up * 74f, new Vector3(84f, 76f, 84f), leaf);
                }
            }
            else if (look.Trees == 1)
            {
                Material needle = Make.Lit(new Color(0.1f, 0.3f, 0.22f), 0.05f), snow = Make.Lit(new Color(0.95f, 0.97f, 1f), 0.2f);
                foreach (Vector3 at in spots)
                {
                    Shape(PrimitiveType.Cylinder, "Trunk", Root, at + Vector3.up * 12f, new Vector3(7f, 12f, 7f), trunk);
                    for (int k = 0; k < 3; k++)
                        Make.MeshObject("Boughs", Root, Cone(42f - k * 11f, 52f - k * 6f, 18f + k * 30f), needle).transform.localPosition = at;
                    Make.MeshObject("Snow cap", Root, Cone(11f, 16f, 106f), snow).transform.localPosition = at;
                }
            }
            else if (look.Trees == 2)
            {
                Material cactus = Make.Lit(new Color(0.2f, 0.45f, 0.24f), 0.1f);
                foreach (Vector3 at in spots)
                {
                    Shape(PrimitiveType.Cylinder, "Cactus", Root, at + Vector3.up * 30f, new Vector3(11f, 30f, 11f), cactus);
                    Shape(PrimitiveType.Cylinder, "Arm", Root, at + new Vector3(13f, 34f, 0), new Vector3(6f, 12f, 6f), cactus);
                    Shape(PrimitiveType.Cylinder, "Arm", Root, at + new Vector3(-12f, 26f, 0), new Vector3(6f, 9f, 6f), cactus);
                }
            }

            switch (_world)
            {
                case 2:
                    // Windmills on the skyline, turning.
                    foreach (Vector3 at in new[] { new Vector3(-330f, 0, 620f), new Vector3(360f, 0, 760f), new Vector3(-60f, 0, 980f) }) FarMill(at, 3.2f);
                    break;
                case 3:
                    Material dune = Make.Lit(new Color(0.88f, 0.74f, 0.45f), 0.05f);
                    Shape(PrimitiveType.Sphere, "Dune", Root, new Vector3(-300f, -40f, 380f), new Vector3(520f, 130f, 300f), dune);
                    Shape(PrimitiveType.Sphere, "Dune", Root, new Vector3(340f, -46f, 520f), new Vector3(600f, 150f, 340f), dune);
                    break;
                case 5:
                    // Canyon walls either side and a mesa ahead.
                    Material rock = Make.Lit(new Color(0.55f, 0.28f, 0.25f), 0.05f), rockDark = Make.Lit(new Color(0.42f, 0.22f, 0.24f), 0.05f);
                    Shape(PrimitiveType.Cube, "Wall", Root, new Vector3(-520f, 200f, 500f), new Vector3(420f, 520f, 2400f), rock);
                    Shape(PrimitiveType.Cube, "Wall", Root, new Vector3(520f, 170f, 500f), new Vector3(420f, 460f, 2400f), rock);
                    Shape(PrimitiveType.Cube, "Ledge", Root, new Vector3(-340f, 60f, 420f), new Vector3(120f, 120f, 500f), rockDark);
                    Shape(PrimitiveType.Cube, "Ledge", Root, new Vector3(350f, 46f, 600f), new Vector3(110f, 92f, 420f), rockDark);
                    Shape(PrimitiveType.Cube, "Mesa", Root, new Vector3(60f, 140f, 1500f), new Vector3(620f, 320f, 300f), rockDark);
                    Shape(PrimitiveType.Sphere, "Low sun", Root, new Vector3(-180f, 520f, 2500f), Vector3.one * 260f, Make.Emissive(Color.black, new Color(1f, 0.72f, 0.4f))).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    break;
                case 6:
                    // A volcano ahead, and lava in pools round the green.
                    Make.MeshObject("Volcano", Root, Cone(620f, 520f, -20f), Make.Lit(new Color(0.16f, 0.11f, 0.1f), 0.05f)).transform.localPosition = new Vector3(120f, 0, 1500f);
                    Material lava = Make.Emissive(new Color(1f, 0.35f, 0.05f), new Color(1.6f, 0.5f, 0.08f));
                    Shape(PrimitiveType.Sphere, "Crater glow", Root, new Vector3(120f, 500f, 1500f), new Vector3(150f, 60f, 150f), lava).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    foreach (Vector3 at in new[] { new Vector3(-110f, -0.8f, 60f), new Vector3(120f, -0.8f, 150f), new Vector3(-190f, -0.8f, 300f), new Vector3(220f, -0.8f, 420f), new Vector3(-70f, -0.8f, 470f) })
                    {
                        GameObject pool = Make.MeshObject("Lava", Root, Make.Disc(46f + Mathf.Abs(at.x) * 0.12f, 28), lava);
                        pool.transform.localPosition = at;
                        pool.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    }
                    break;
                case 7:
                    // Above the clouds: the green sits on a bank of them, with more drifting past.
                    foreach (Vector3 at in new[] { new Vector3(-200f, -30f, 120f), new Vector3(210f, -36f, 240f), new Vector3(-320f, -20f, 460f), new Vector3(330f, -30f, 560f), new Vector3(0f, -60f, 760f), new Vector3(-90f, 220f, 1300f), new Vector3(420f, 300f, 1500f) })
                        Shape(PrimitiveType.Sphere, "Cloud bank", Root, at, new Vector3(340f, 120f, 260f), cloud);
                    break;
                case 8:
                    // The keep: a curtain wall with battlements, two towers, lit windows, a moon.
                    Material stone = Make.Lit(new Color(0.26f, 0.26f, 0.32f), 0.05f), window = Make.Emissive(Color.black, new Color(1f, 0.7f, 0.25f));
                    Shape(PrimitiveType.Cube, "Curtain wall", Root, new Vector3(0, 110f, 900f), new Vector3(1500f, 220f, 60f), stone);
                    for (int i = -9; i <= 9; i++) Shape(PrimitiveType.Cube, "Merlon", Root, new Vector3(i * 80f, 240f, 900f), new Vector3(44f, 40f, 60f), stone);
                    foreach (int s in new[] { -1, 1 })
                    {
                        Shape(PrimitiveType.Cylinder, "Tower", Root, new Vector3(s * 420f, 190f, 880f), new Vector3(170f, 190f, 170f), stone);
                        Make.MeshObject("Tower roof", Root, Cone(110f, 150f, 380f), Make.Lit(new Color(0.3f, 0.14f, 0.34f), 0.1f)).transform.localPosition = new Vector3(s * 420f, 0, 880f);
                        Shape(PrimitiveType.Cube, "Window", Root, new Vector3(s * 420f, 250f, 794f), new Vector3(22f, 40f, 2f), window);
                        // A torch either side of the green.
                        Shape(PrimitiveType.Cylinder, "Torch post", Root, new Vector3(s * 44f, 12f, 30f), new Vector3(1.6f, 12f, 1.6f), stone);
                        Shape(PrimitiveType.Sphere, "Torch flame", Root, new Vector3(s * 44f, 27f, 30f), new Vector3(5f, 7f, 5f), Make.Emissive(Color.black, new Color(1.6f, 0.8f, 0.2f)));
                    }
                    Shape(PrimitiveType.Cube, "Gate", Root, new Vector3(0, 70f, 868f), new Vector3(120f, 140f, 4f), Make.Lit(new Color(0.08f, 0.07f, 0.1f), 0.1f));
                    Shape(PrimitiveType.Sphere, "Moon", Root, new Vector3(520f, 900f, 2500f), Vector3.one * 220f, Make.Emissive(Color.black, new Color(0.9f, 0.92f, 1f))).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    break;
            }
        }

        void FarMill(Vector3 at, float size)
        {
            Transform mill = new GameObject("Far mill").transform;
            mill.SetParent(Root, false);
            mill.localPosition = at;
            mill.localScale = Vector3.one * size;
            Make.MeshObject("Tower", mill, Make.Lathe(new List<Vector2> { new Vector2(0.01f, 0f), new Vector2(11f, 0f), new Vector2(7f, 40f), new Vector2(0.01f, 40f) }, 16), Make.Lit(new Color(0.96f, 0.92f, 0.82f), 0.1f));
            Make.MeshObject("Cap", mill, Cone(8.5f, 11f, 40f), Make.Lit(new Color(0.78f, 0.27f, 0.18f), 0.1f));
            Transform rotor = new GameObject("Rotor").transform;
            rotor.SetParent(mill, false);
            rotor.localPosition = new Vector3(0, 38f, -9f);
            Material sail = Make.Lit(new Color(0.97f, 0.97f, 0.95f), 0.1f);
            for (int k = 0; k < 4; k++)
            {
                Transform arm = Shape(PrimitiveType.Cube, "Sail", rotor, Vector3.zero, new Vector3(5f, 26f, 0.6f), sail).transform;
                arm.localRotation = Quaternion.Euler(0, 0, k * 90f);
                arm.localPosition = arm.localRotation * new Vector3(0, 15f, 0);
            }
            _spinners.Add(rotor);
        }

        // ── The mill over the cup, and its lamp ──────────────────────────────

        void BuildMill()
        {
            _mill = new GameObject("Mill").transform;
            _mill.SetParent(Root, false);
            _millBody = new GameObject("Mill body").transform;
            _millBody.SetParent(_mill, false);
            _millBody.localPosition = new Vector3(0, 0, 30f);
            Material wall = Make.Lit(new Color(0.96f, 0.92f, 0.82f), 0.1f), cap = Make.Lit(new Color(0.78f, 0.27f, 0.18f), 0.1f), wood = Make.Lit(new Color(0.42f, 0.29f, 0.17f), 0.1f);
            Make.MeshObject("Tower", _millBody, Make.Lathe(new List<Vector2> { new Vector2(0.01f, 0f), new Vector2(11f, 0f), new Vector2(7.5f, 36f), new Vector2(0.01f, 36f) }, 20), wall);
            Make.MeshObject("Cap", _millBody, Cone(9f, 11f, 36f), cap);
            Shape(PrimitiveType.Cube, "Door", _millBody, new Vector3(0, 6f, -10.4f), new Vector3(6f, 12f, 0.6f), wood);
            Shape(PrimitiveType.Cube, "Shaft", _millBody, new Vector3(0, 27f, -17.5f), new Vector3(1.3f, 1.3f, 35f), wood);
            _rotor = new GameObject("Rotor").transform;
            _rotor.SetParent(_mill, false);
            _rotor.localPosition = new Vector3(0, 27f, -5f);
            Material sail = Make.Lit(new Color(0.97f, 0.97f, 0.95f), 0.1f);
            for (int k = 0; k < 4; k++)
            {
                Transform arm = Shape(PrimitiveType.Cube, "Sail", _rotor, Vector3.zero, new Vector3(5.5f, 24f, 0.5f), sail).transform;
                arm.localRotation = Quaternion.Euler(0, 0, k * 90f);
                arm.localPosition = arm.localRotation * new Vector3(0, 13.4f, 0);
            }
            Shape(PrimitiveType.Sphere, "Hub", _rotor, Vector3.zero, Vector3.one * 3.4f, wood);
            // The lamp, to the right of the green level with the cup.
            Material post = Make.Lit(new Color(0.06f, 0.07f, 0.08f), 0.3f);
            Vector3 lampAt = new Vector3(LaneW / 2f + 5f, 0, -3f);
            Shape(PrimitiveType.Cylinder, "Lamp post", _mill, lampAt + Vector3.up * 9f, new Vector3(1.1f, 9f, 1.1f), post);
            Shape(PrimitiveType.Sphere, "Lamp housing", _mill, lampAt + Vector3.up * 20f, Vector3.one * 7.4f, post);
            _lampGreen = Shape(PrimitiveType.Sphere, "Lamp green", _mill, lampAt + new Vector3(0, 20f, -1.2f), Vector3.one * 5.8f, Make.Emissive(Color.black, new Color(0.5f, 1.7f, 0.3f)));
            _lampRed = Shape(PrimitiveType.Sphere, "Lamp red", _mill, lampAt + new Vector3(0, 20f, -1.2f), Vector3.one * 5.8f, Make.Emissive(Color.black, new Color(1.7f, 0.25f, 0.2f)));
            _mill.gameObject.SetActive(false);
        }

        bool LampGreenNow()
        {
            double period = _lampOpen + _lampShut, phase = (Time.realtimeSinceStartupAsDouble - _lampStart) % period;
            if (phase < 0) phase += period;
            return phase < _lampOpen;
        }

        // ── The bosses ───────────────────────────────────────────────────────

        void BuildBoss()
        {
            _boss = new GameObject("Boss").transform;
            _boss.SetParent(Root, false);
            _boss.localPosition = new Vector3(0, 0, 30f);
            _body = new GameObject("Body").transform;
            _body.SetParent(_boss, false);
            Material white = Make.Lit(Color.white, 0.5f), dark = Make.Lit(new Color(0.02f, 0.02f, 0.02f), 0.6f), pink = Make.Lit(new Color(0.95f, 0.55f, 0.6f), 0.4f);

            // Eyes on a body: where, how far apart, how big.
            void Eyes(Transform on, float y, float z, float apart, float size, Material ball, Material pupil)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Shape(PrimitiveType.Sphere, "Eye", on, new Vector3(s * apart, y, z), Vector3.one * size, ball);
                    if (pupil != null) Shape(PrimitiveType.Sphere, "Pupil", on, new Vector3(s * apart * 0.94f, y - size * 0.05f, z - size * 0.42f), Vector3.one * size * 0.45f, pupil);
                }
            }
            void Mound(Color colour) => Shape(PrimitiveType.Sphere, "Mound", _boss, new Vector3(0, 0f, 2f), new Vector3(34f, 9f, 26f), Make.Lit(colour, 0.05f));

            switch (_world)
            {
                case 2:
                    // The Grinder is the mill itself: it gets a face, and the mill's own tower is what is hit.
                    Eyes(_millBody, 24f, -8.6f, 3.4f, 3.6f, white, dark);
                    for (int s = -1; s <= 1; s += 2)
                        Shape(PrimitiveType.Cube, "Brow", _millBody, new Vector3(s * 3.4f, 27.2f, -9.2f), new Vector3(4.6f, 1f, 1f), dark).transform.localRotation = Quaternion.Euler(0, 0, s * -22f);
                    _bossTop = 52f;
                    break;
                case 3:
                    Mound(new Color(0.8f, 0.64f, 0.36f));
                    Shape(PrimitiveType.Sphere, "Body", _body, new Vector3(0, 12f, 0), new Vector3(19f, 20f, 17f), Make.Lit(new Color(0.72f, 0.55f, 0.3f), 0.15f));
                    Eyes(_body, 17f, -7f, 3.8f, 3.4f, white, dark);
                    Shape(PrimitiveType.Sphere, "Nose", _body, new Vector3(0, 13f, -8.4f), new Vector3(5f, 4f, 5f), pink);
                    Material gold = Make.Lit(new Color(1f, 0.76f, 0.1f), 0.7f, 0.6f);
                    Shape(PrimitiveType.Cylinder, "Crown band", _body, new Vector3(0, 22.6f, 0), new Vector3(11f, 1.4f, 11f), gold);
                    for (int k = 0; k < 5; k++) { float a = k * 72f * Mathf.Deg2Rad; Make.MeshObject("Crown point", _body, Cone(1.8f, 5f), gold).transform.localPosition = new Vector3(Mathf.Sin(a) * 4.6f, 24f, Mathf.Cos(a) * 4.6f); }
                    _bossTop = 34f;
                    break;
                case 4:
                    Mound(new Color(0.93f, 0.96f, 1f));
                    Material snow = Make.Lit(new Color(0.97f, 0.98f, 1f), 0.3f);
                    Shape(PrimitiveType.Sphere, "Belly", _body, new Vector3(0, 9f, 0), Vector3.one * 18f, snow);
                    Shape(PrimitiveType.Sphere, "Head", _body, new Vector3(0, 22f, 0), Vector3.one * 12f, snow);
                    Eyes(_body, 23.6f, -5f, 2.4f, 1.6f, dark, null);
                    Make.MeshObject("Carrot", _body, Cone(1.3f, 6f), Make.Lit(new Color(1f, 0.5f, 0.1f), 0.3f)).transform.SetLocalPositionAndRotation(new Vector3(0, 21.6f, -5.4f), Quaternion.Euler(-90f, 0, 0));
                    Shape(PrimitiveType.Cylinder, "Scarf", _body, new Vector3(0, 16.6f, 0), new Vector3(11f, 1.2f, 11f), Make.Lit(new Color(0.29f, 0.66f, 1f), 0.2f));
                    _bossTop = 34f;
                    break;
                case 5:
                    // The Lip: a stone head, the cup at its chin.
                    Material stone = Make.Lit(new Color(0.55f, 0.56f, 0.64f), 0.1f);
                    Shape(PrimitiveType.Sphere, "Head", _body, new Vector3(0, 13f, 2f), new Vector3(30f, 30f, 22f), stone);
                    Eyes(_body, 19f, -8f, 5.4f, 4.2f, Make.Emissive(Color.black, Amber * 1.4f), null);
                    for (int s = -1; s <= 1; s += 2)
                        Shape(PrimitiveType.Cube, "Brow", _body, new Vector3(s * 5.6f, 23f, -8.6f), new Vector3(8f, 1.6f, 2f), Make.Lit(new Color(0.23f, 0.23f, 0.3f), 0.1f)).transform.localRotation = Quaternion.Euler(0, 0, s * -20f);
                    Shape(PrimitiveType.Cube, "Mouth", _body, new Vector3(0, 6f, -8.8f), new Vector3(13f, 3f, 2f), dark);
                    _bossTop = 38f;
                    break;
                case 6:
                    Mound(new Color(0.16f, 0.12f, 0.11f));
                    Material fire = Make.Emissive(new Color(1f, 0.4f, 0.05f), new Color(1.7f, 0.6f, 0.1f)), flame = Make.Emissive(new Color(1f, 0.8f, 0.2f), new Color(1.8f, 1.3f, 0.3f));
                    Shape(PrimitiveType.Sphere, "Body", _body, new Vector3(0, 12f, 0), new Vector3(18f, 19f, 17f), fire);
                    Eyes(_body, 15f, -7.4f, 3.6f, 3f, dark, null);
                    foreach (Vector3 at in new[] { new Vector3(0, 19f, 0), new Vector3(-5f, 17f, 0), new Vector3(5f, 17f, 0) })
                        Make.MeshObject("Flame", _body, Cone(3.6f, 11f), flame).transform.localPosition = at;
                    _bossTop = 38f;
                    break;
                case 7:
                    // The Long Shadow: tall, thin, two white eyes.
                    Shape(PrimitiveType.Sphere, "Pool", _boss, new Vector3(0, 0f, 2f), new Vector3(30f, 2f, 22f), dark);
                    Shape(PrimitiveType.Capsule, "Body", _body, new Vector3(0, 22f, 0), new Vector3(11f, 22f, 9f), Make.Lit(new Color(0.05f, 0.05f, 0.09f), 0.3f));
                    Eyes(_body, 36f, -4.2f, 2.4f, 2.2f, Make.Emissive(Color.black, new Color(1.5f, 1.5f, 1.6f)), null);
                    _bossTop = 54f;
                    break;
                case 8:
                    // The Yips: a ghost that cannot keep still.
                    Material ghost = Make.Lit(new Color(0.55f, 0.36f, 0.86f), 0.3f);
                    Shape(PrimitiveType.Sphere, "Head", _body, new Vector3(0, 17f, 0), Vector3.one * 18f, ghost);
                    Make.MeshObject("Skirt", _body, Make.Lathe(new List<Vector2> { new Vector2(0.01f, 3f), new Vector2(10f, 3f), new Vector2(9f, 17f), new Vector2(0.01f, 17f) }, 18), ghost);
                    Eyes(_body, 19f, -7.4f, 3.6f, 4.4f, white, dark);
                    Shape(PrimitiveType.Sphere, "Mouth", _body, new Vector3(0, 12.6f, -8.2f), new Vector3(4f, 5f, 2f), dark);
                    _bossTop = 38f;
                    break;
                default:
                    // Three-Putt, a mole on his mound.
                    Mound(new Color(0.34f, 0.22f, 0.12f));
                    Shape(PrimitiveType.Sphere, "Fur", _body, new Vector3(0, 12f, 0), new Vector3(17f, 20f, 16f), Make.Lit(new Color(0.26f, 0.2f, 0.2f), 0.15f));
                    Shape(PrimitiveType.Sphere, "Nose", _body, new Vector3(0, 13f, -8f), new Vector3(5f, 4f, 5f), pink);
                    Eyes(_body, 17f, -6.6f, 3.6f, 3.4f, white, dark);
                    Material tooth = Make.Lit(new Color(0.97f, 0.96f, 0.9f), 0.5f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Shape(PrimitiveType.Cube, "Tooth", _body, new Vector3(s * 1.1f, 9.6f, -7.6f), new Vector3(1.9f, 2.6f, 0.8f), tooth);
                        Shape(PrimitiveType.Sphere, "Paw", _body, new Vector3(s * 8.4f, 6f, -5f), new Vector3(5f, 3.4f, 5f), pink);
                    }
                    _bossTop = 31f;
                    break;
            }
            Material heart = Make.Emissive(Heart, Heart * 0.2f);
            for (int i = 0; i < 5; i++) _hearts.Add(Shape(PrimitiveType.Sphere, "Heart", _boss, Vector3.zero, Vector3.one * 4.6f, heart).transform);
            _boss.gameObject.SetActive(false);
        }

        /// <summary>What a hit lands on: the mill's tower in world 2, the creature anywhere else.</summary>
        Transform Struck => _world == 2 ? _millBody : _body;

        // ── Setting up for a putt ────────────────────────────────────────────

        void Dress(ShotView v)
        {
            _isBoss = v.Str("kind") == "Boss";
            _lamp = v.Flag("lamp");
            _lampStart = v.Data.TryGetValue("lampStart", out object at) && at != null ? System.Convert.ToDouble(at) : 0;
            _lampOpen = v.Num("lampOpen", 4f); _lampShut = v.Num("lampShut", 2.5f);
            _mill.gameObject.SetActive(_lamp);
            _boss.gameObject.SetActive(_isBoss);
            _body.gameObject.SetActive(_isBoss && _world != 2);
            int goal = v.Int("goal"), left = Mathf.Max(0, goal - v.Int("makes"));
            for (int i = 0; i < _hearts.Count; i++)
            {
                _hearts[i].gameObject.SetActive(_isBoss && i < left);
                _hearts[i].localPosition = new Vector3((i - (goal - 1) * 0.5f) * 7f, _bossTop, 0);
            }
            // The pace band: from the least a putt may roll past, or up to the most.
            float min = v.Num("paceMin"), max = v.Num("paceMax");
            _band.gameObject.SetActive(min > 0f || max > 0f);
            if (min > 0f) { _band.localPosition = new Vector3(0, 0.07f, (min + 44f) * 0.5f); _band.localScale = new Vector3(LaneW - 6f, 44f - min, 1f); }
            else if (max > 0f) { _band.localPosition = new Vector3(0, 0.07f, CupR + (max - CupR) * 0.5f); _band.localScale = new Vector3(LaneW - 6f, max - CupR, 1f); }
        }

        void Aim(int feet, int tee, float window)
        {
            Vector3 from = SpotFor(feet, tee);
            _start.localPosition = from;
            _ball.localPosition = from + Vector3.up * BallR;
            _ball.localRotation = Quaternion.identity;
            _ball.gameObject.SetActive(true);
            Vector3 back = new Vector3(from.x, 0, from.z).normalized;
            _cam = from + back * 54f + Vector3.up * (25f + feet * 1.2f);
            _look = new Vector3(0, _isBoss || _lamp ? 9f : 3f, 6f);
            _feet = feet; _tee = tee;
            _gate.gameObject.SetActive(window > 0f);
            if (window <= 0f) return;
            // The window is centred on the line from the tee into the cup.
            float line = Mathf.Atan2(from.x, -from.z) * Mathf.Rad2Deg;
            for (int s = -1; s <= 1; s += 2)
            {
                float a = (line + s * window * 0.5f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a));
                Points(s < 0 ? _gateL : _gateR, new[] { dir * CupR + Vector3.up * 0.12f, dir * (CupR + 15f) + Vector3.up * 0.12f });
            }
        }

        public override void Begin(ShotView start)
        {
            _down = false;
            if (_body != null) { _body.localPosition = Vector3.zero; _body.localScale = Vector3.one; _body.localRotation = Quaternion.identity; }
            if (_millBody != null) { _millBody.localPosition = new Vector3(0, 0, 30f); _millBody.localScale = Vector3.one; }
            Dress(start);
            Aim(start.NextFeet, start.NextTee, start.Num("window"));
        }

        public override void Ready(ShotView view)
        {
            if (view.Over) return;   // the last picture stays: the ball in the cup, or the empty bag
            Dress(view);
            Aim(view.NextFeet, view.NextTee, view.Num("window"));
        }

        public override void Tick()
        {
            foreach (Transform far in _spinners) far.Rotate(0, 0, 14f * Time.deltaTime, Space.Self);
            if (_lamp && _rotor != null)
            {
                // The sails turn a quarter turn a lamp cycle, one hanging in front of the cup at the middle of red.
                double period = _lampOpen + _lampShut, phase = (Time.realtimeSinceStartupAsDouble - _lampStart) % period;
                if (phase < 0) phase += period;
                float turn = (float)((phase - (_lampOpen + _lampShut * 0.5)) / period) * 90f;
                _rotor.localRotation = Quaternion.Euler(0, 0, _down ? 45f : 180f + turn);
                bool green = LampGreenNow();
                _lampGreen.SetActive(green); _lampRed.SetActive(!green);
            }
            if (!_isBoss || _down || _body == null) return;
            // A boss is never quite still: most bob, The Yips shakes, The Long Shadow sways.
            float t = Time.time;
            if (_world == 8) _body.localPosition = new Vector3(Mathf.Sin(t * 31f) * 0.5f, 2f + Mathf.Sin(t * 2f) * 1.2f, 0);
            else if (_world == 7) _body.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 1.3f) * 5f);
            else if (_world != 2 && _world != 5) _body.localPosition = new Vector3(0, Mathf.Sin(t * 2.2f) * 0.9f, 0);
        }

        // ── The putt ─────────────────────────────────────────────────────────

        public override IEnumerator Play(ShotView v)
        {
            int feet = v.Feet > 0 ? v.Feet : 3, tee = v.Tee > 0 ? v.Tee : 2;
            if (feet != _feet || tee != _tee) Aim(feet, tee, v.Num("window"));
            string why = v.Str("why");
            bool won = v.Flag("won"), lost = v.Flag("lost"), blocked = why == "blocked";
            List<Vector3> roll = Roll(v, SpotFor(feet, tee), BallR);
            // A putt the sails stopped never gets to the hole.
            if (blocked) { int cut = roll.FindIndex(p => p.z > -7f); if (cut > 0) roll = roll.GetRange(0, cut); roll.Add(new Vector3(roll[roll.Count - 1].x * 0.6f, BallR, -7f)); }
            yield return Along(_ball, roll, Mathf.Lerp(0.8f, 1.5f, Mathf.InverseLerp(3f, 11f, feet)));

            if (blocked)
            {
                Sound("gl_blocked", "rim_clang", 0.7f);
                yield return Arc(_ball, _ball.localPosition, new Vector3(_ball.localPosition.x + 7f, BallR, -26f), 0.5f, 6f);
            }
            else if (v.Made)
            {
                Vector3 top = _ball.localPosition;
                Sound("golf_cup", 0.8f);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.16f) { _ball.localPosition = Vector3.Lerp(top, new Vector3(0, -BallR * 2f, 0), t); yield return null; }
                if (!v.Good)
                {
                    // In, but not the way the stage asks: the cup spits it out (on past the hole for one hit too hard).
                    Sound("gl_side_door", "rim_clang", 0.6f);
                    float side = v.EntryDeg < 0f ? -1f : 1f;
                    Vector3 to = why == "firm" ? new Vector3(side * 3f, BallR, 22f) : new Vector3(side * 13f, BallR, -9f);
                    yield return Arc(_ball, new Vector3(0, BallR, 0), to, 0.45f, 9f);
                }
                else _ball.gameObject.SetActive(false);
            }

            if (v.Good && _isBoss)
            {
                // A hit: it is squashed flat, a heart goes, and it pops back up unless that was the last one.
                Sound("gl_w" + _world + "_boss_hit", "gl_boss_hit", 0.8f);
                Arcade.Sparks(At(0, 14f, 30f), 14);
                int left = Mathf.Max(0, v.Int("goal") - v.Int("makes"));
                for (int i = 0; i < _hearts.Count; i++) _hearts[i].gameObject.SetActive(i < left);
                _down = true;
                yield return Squash(1f, 0.35f, 0.12f);
                if (won)
                {
                    Sound("gl_w" + _world + "_boss_down", "gl_boss_down", 0.9f);
                    Transform struck = Struck;
                    Vector3 from = struck.localPosition;
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f) { struck.localPosition = from + new Vector3(0, -(_bossTop + 8f) * Ease(t), 0); yield return null; }
                    Arcade.Sparks(At(0, 8f, 30f), 30);
                }
                else { yield return Squash(0.35f, 1f, 0.25f); _down = false; }
            }
            else if (won)
            {
                Sound("gl_clear", "rule_win", 0.9f);
                Arcade.Sparks(At(0, 2f, 0), 30);
            }
            else if (v.Good) Sound("gl_make", "rule_make", 0.7f);
            else if (lost) Sound("gl_out", "rule_miss", 0.8f);
            else if (!v.Made) Sound("gl_miss", "rule_miss", 0.6f);

            // A boss whose hearts came back (a run broken): show them.
            if (_isBoss && !v.Good && !v.Over)
            {
                int left = Mathf.Max(0, v.Int("goal") - v.Int("makes"));
                for (int i = 0; i < _hearts.Count; i++) _hearts[i].gameObject.SetActive(i < left);
            }
            yield return new WaitForSeconds(v.Over ? 1.2f : 0.7f);
        }

        IEnumerator Squash(float from, float to, float seconds)
        {
            Transform struck = Struck;
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float y = Mathf.Lerp(from, to, Ease(t));
                struck.localScale = new Vector3(1f + (1f - y) * 0.35f, y, 1f + (1f - y) * 0.35f);
                yield return null;
            }
            struck.localScale = new Vector3(1f + (1f - to) * 0.35f, to, 1f + (1f - to) * 0.35f);
        }

        // The roll, as the camera saw the last of it (ShotView.Path), joined to the tee by a straight run: the green
        // only sees the last 2 ft, so the start of the line is drawn, not measured.
        static List<Vector3> Roll(ShotView v, Vector3 start, float y)
        {
            var pts = new List<Vector3> { new Vector3(start.x, y, start.z) };
            float half = LaneW / 2f - 2f;
            if (v.Path != null && v.Path.Count >= 2)
            {
                int step = Mathf.Max(1, v.Path.Count / 40);
                for (int i = 0; i < v.Path.Count; i += step)
                {
                    Vector2 p = v.Path[i];
                    if (p.y < start.z + 4f) continue;
                    pts.Add(new Vector3(Mathf.Clamp(p.x, -half, half), y, Mathf.Min(p.y, 20f)));
                }
                Vector2 last = v.Path[v.Path.Count - 1];
                pts.Add(new Vector3(Mathf.Clamp(last.x, -half, half), y, Mathf.Min(last.y, 20f)));
            }
            else
            {
                float endZ = v.Reached ? (v.Made ? 0f : Mathf.Clamp(v.HasRollout ? v.RolloutIn : 8f, 2f, 20f)) : Mathf.Min(-3f, v.HasRollout ? v.RolloutIn : -8f);
                Vector3 at = new Vector3(v.Made ? v.LineIn * 0.4f : v.LineIn + (v.MissSide == "LEFT" ? -3f : v.MissSide == "RIGHT" ? 3f : 0f), y, endZ);
                Vector3 mid = Vector3.Lerp(pts[0], at, 0.5f) + new Vector3(-at.x * 0.5f, 0, 0);
                for (int i = 1; i <= 16; i++) { float t = i / 16f; pts.Add((1 - t) * (1 - t) * pts[0] + 2 * (1 - t) * t * mid + t * t * at); }
            }
            Vector3 end = pts[pts.Count - 1];
            if (v.Made && new Vector2(end.x, end.z).magnitude > 0.6f) pts.Add(new Vector3(0, y, 0));
            return pts;
        }

        static IEnumerator Along(Transform ball, List<Vector3> pts, float seconds)
        {
            var cum = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            float total = Mathf.Max(0.01f, cum[pts.Count - 1]);
            int seg = 1;
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float s = (1f - (1f - t) * (1f - t)) * total;
                while (seg < pts.Count - 1 && cum[seg] < s) seg++;
                Vector3 p = Vector3.Lerp(pts[seg - 1], pts[seg], Mathf.InverseLerp(cum[seg - 1], cum[seg], s));
                Vector3 step = p - ball.localPosition;
                if (step.sqrMagnitude > 0.0001f) ball.Rotate(Vector3.Cross(Vector3.up, step.normalized), step.magnitude / BallR * Mathf.Rad2Deg, Space.World);
                ball.localPosition = p;
                yield return null;
            }
            ball.localPosition = pts[pts.Count - 1];
        }
    }
}
