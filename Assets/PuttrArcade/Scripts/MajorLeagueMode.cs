// Major League: the Puttr app's baseball game, under the lights.
//
// Every putt is one at-bat. Before it the game deals a distance and a tee
// (left, center or right) and the Player putts from there. A putt that is
// not holed is a strikeout. A holed one is judged by how straight it went
// in: the angle it entered the cup at, against the angle a dead straight
// putt from that tee would enter at.
//
//             distance   home run   triple   double   single   beyond
//   Rookie    3 to 6 ft    18         42       66       90     single
//   Pro       5 to 9 ft     9         36       63       90     out
//   All-Star  8 to 11 ft    5         12       21       48     out
//
// (degrees off the line, either side). Easy is Rookie, Medium is Pro and Hard
// is All-Star. A hit moves every runner and the batter 1, 2, 3 or 4 bases,
// and anyone who gets home scores. Three outs and the next Player bats; when
// everyone has batted the inning is over. Three innings, most runs wins.
//
// A Player alone plays Puttr AI, which bats first in each inning and plays
// its at-bats out by itself.
//
// The ballpark is seen from right behind the catcher. Home is at the origin,
// center field is up +Z, one unit is about an inch for the figures (which are
// drawn nearly twice life size so they can be seen), and the bases are 600
// units apart.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class MajorLeagueMode : StageMode
    {
        public override string Name => "Major League";
        public override bool DealsTee => false;
        public override string Blurb => "A night at the ballpark.\nEvery putt is an at-bat.\nHole it straight to go deep.";
        public override string Rule => "Every putt is an at-bat: hole it for a hit, and the straighter it goes in, the further it flies.";
        public override string Icon => "Trophy";
        public override Color Accent => new Color(0.13f, 0.52f, 0.31f);
        public override string[] Tags => new[] { "3 innings", "3 to 11 ft" };
        public override int Difficulty => 2;
        public override int MaxPlayers => 4;
        public override string SoloOpponent => "Puttr";
        public override GameAudio.Scene Sound => GameAudio.Scene.Ballpark;
        public override string[] HowTo => new[]
        {
            "Each putt is an at-bat. Putt from the distance and the tee the screen calls: left, center or right.",
            "Hole it for a hit. How straight it went in decides single, double, triple or home run: the wheel shows the bands. Miss and you are out.",
            "Three outs a side, three innings, and the most runs wins. Alone, you are the home side against Puttr.",
        };

        // The bands are the difficulty here, so this game takes the putt as it really was.
        public override bool OwnLevels => true;
        public override bool SmallBanner => true; // the infield is where the big banner would be
        public override int Putter => _ai || _runs == null ? 0 : Mathf.Clamp(_side, 0, _players - 1); // whose difficulty applies to this putt
        public override string Tee => _over || _feet == 0 ? null : _feet + " feet, " + TeeWord[_tee - 1] + " tee";

        public override int Score(int player)
        {
            if (_runs == null || player < 0) return 0;
            if (_ai) return player == 0 ? _runs[1] : 0;
            return player < _sides ? _runs[player] : 0;
        }
        public override string ScoreWord(int n) => n == 1 ? "run" : "runs";

        const int Innings = 3;

        // A play is the number of bases it is worth: none for an out, four for a home run.
        const int Out = 0, Base1 = 1, Base2 = 2, Base3 = 3, Homer = 4;
        static readonly string[] HitName = { "Out", "Single", "Double", "Triple", "Home run" };
        static readonly string[] HitShout = { "OUT", "SINGLE!", "DOUBLE!", "TRIPLE!", "HOME RUN!" };
        static readonly string[] HitVoice = { "ml_fieldout_", "ml_single_", "ml_double_", "ml_triple_", "ml_homerun_" };
        static readonly Color[] HitInk = { Make.Hex(0xFF5252), new Color(0.95f, 0.95f, 0.95f), Make.Hex(0x5687FF), Make.Hex(0x3DBE55), Make.Hex(0xF2B705) };
        static readonly string[] TeeWord = { "left", "center", "right" };
        static readonly string[] LevelName = { "ROOKIE", "PRO", "ALL-STAR" };
        static readonly int[,] Feet = { { 3, 6 }, { 5, 9 }, { 8, 11 } };

        // The field.
        const float BaseGap = 600f;                  // from one base to the next
        const float Diag = 424.26f;                  // the same, measured along X or Z
        const float MoundZ = 403f, MoundTop = 7f;
        const float WallMid = 2050f, WallLine = 1750f, WallHigh = 110f;
        const float SideX = 950f, SideZ = -700f;     // where the wall along each foul line ends, behind home
        const float FieldHalf = 1700f, FieldZ0 = -800f; // what the painted ground covers
        const float PlateHalf = 256f;                // the finer painting round home plate, which the camera sits over
        // The stands: a lower bowl, the face of the upper deck hanging out over the back of
        // it, the upper deck, and the roof. Off is how far behind the wall, Y how high.
        const float LowOff0 = 10f, LowY0 = 96f, LowOff1 = 440f, LowY1 = 350f;
        const float FaceOff = 360f, FaceY0 = 420f, HighY0 = 474f, HighOff1 = 770f, HighY1 = 720f;
        const float RoofOff = 560f, RoofY = 800f;
        const float BoardAngle = 24f;                // the scoreboard is over right center, clear of the score table and the batter

        // The figures.
        const float Tall = 1.9f, FielderTall = 1.55f;    // the fielders are drawn smaller, to put some distance into the field
        const float Stride = 55f;                    // ground covered for each radian of a running stride
        const float SwingTime = 0.24f, SwingLead = 0.09f;
        const float BallSize = 12f;                  // how far off the ground a ball on the grass is drawn
        static readonly float GloveHigh = (Ballplayer.HipHeight + Ballplayer.Reach) * FielderTall;
        static readonly Vector3 Mound = new Vector3(0f, 0f, MoundZ);
        static readonly Vector3 BatterAt = new Vector3(-56f, 0f, 6f);
        static readonly Vector3 CatcherAt = new Vector3(3f, 0f, -66f);
        static readonly Vector3 Contact = new Vector3(4f, 74f, 6f);   // where bat meets ball
        // First, second, short, third, then left, center and right field.
        static readonly Vector3[] Spots =
        {
            new Vector3(360f, 0f, 560f), new Vector3(190f, 0f, 800f), new Vector3(-190f, 0f, 800f), new Vector3(-360f, 0f, 560f),
            new Vector3(-760f, 0f, 1330f), new Vector3(0f, 0f, 1560f), new Vector3(760f, 0f, 1330f),
        };

        // Right behind the catcher, where the umpire stands: over his shoulder at the pitcher,
        // with the batter large on the left. A ball that is hit is followed a little way.
        static readonly Vector3 Eye = new Vector3(24f, 140f, -236f), Aim = new Vector3(-4f, 80f, 403f);
        const float Lens = 44f;
        public override Vector3 CameraPosition => At(Eye.x, Eye.y, Eye.z);
        public override Vector3 CameraLook
        {
            get
            {
                Vector3 look = Aim;
                if (_follow && _ball != null && _ball.gameObject.activeSelf)
                {
                    Vector3 to = _ball.localPosition - Eye, rest = Aim - Eye;
                    float yaw0 = Mathf.Atan2(rest.x, rest.z) * Mathf.Rad2Deg, pitch0 = Mathf.Atan2(rest.y, new Vector2(rest.x, rest.z).magnitude) * Mathf.Rad2Deg;
                    float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                    // Part of the way round to the ball, and never so far up that the field is lost.
                    yaw = Mathf.Lerp(yaw0, Mathf.Clamp(yaw, -34f, 34f), 0.62f);
                    pitch = Mathf.Clamp(Mathf.Lerp(pitch0, pitch, 0.8f), pitch0 - 2f, pitch0 + 16f);
                    look = Eye + Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward * rest.magnitude;
                }
                return At(look.x, look.y, look.z);
            }
        }
        public override float CameraFov => Lens;
        public override float ShadowDistance => 1500f;
        // The crowd is a long way off, and a little soft.
        public override float BlurFrom => 1900f;
        public override float BlurTo => 5200f;

        // The card: the same place, drawn in a little, as the pitch is on its way.
        public override Vector3 CardFrom => At(22f, 122f, -226f);
        public override Vector3 CardLook => At(-12f, 92f, 403f);
        public override float CardFov => 40f;
        public override void PoseForCard()
        {
            _wheel.gameObject.SetActive(false);
            ResetField();
            _pitchAt = Time.time - _pitchLen * 0.8f;   // the pitcher has just let it go
            MoveBall(new Vector3(-6f, 112f, 190f));
        }
        public override void AfterCard()
        {
            _pitchAt = -1f;
            HideBall();
        }

        sealed class Runner
        {
            public Ballplayer Body;
            public bool On, Running;
            public float At, From, To, Start, Speed;  // in bases: 0 is home, 1 first, 4 home again
        }

        struct Spark
        {
            public Transform T;
            public Vector3 V;
            public float Age, Life, Size;
        }

        // The game.
        int _players = 1, _sides = 2, _side, _inning, _outs;
        bool _ai, _over, _opened, _newHalf;
        int[,] _line;                 // runs by side and inning
        bool[,] _batted;
        int[] _runs, _hits;
        readonly bool[] _bases = new bool[3];
        int _feet, _tee;
        float _expected;
        readonly System.Random _rng = new System.Random();

        // The set.
        Ballplayer _batter, _pitcher, _catcher;
        readonly Ballplayer[] _fielders = new Ballplayer[7];
        readonly Runner[] _runners = new Runner[4];
        Transform _bat, _ball;
        LineRenderer _trail;
        readonly List<Vector3> _tail = new List<Vector3>();
        Material _batTop, _batCap, _fieldTop, _fieldCap;
        float _pitchAt = -1f, _pitchLen = 1f, _swingAt = -1f;
        int _chase = -1, _glove = -1;
        bool _follow;                 // the camera is going with the ball
        Vector3 _chaseFrom, _chaseTo;
        float _chaseAt, _chaseLen;

        // The scoreboard.
        TextMesh _sbInning, _sbAtBat, _sbRuns;
        readonly MeshRenderer[] _sbOut = new MeshRenderer[3], _sbBase = new MeshRenderer[3];
        Material _lampOff, _outOn, _baseOn;

        // The hit wheel.
        const float HoleR = 17f, WheelR = 40f;
        Transform _wheel, _marker, _teeDot;
        readonly MeshFilter[] _wedge = new MeshFilter[5];
        TextMesh _wheelLevel, _wheelSays;
        float _markAt = -100f;

        // Fireworks, and the cameras going off in the crowd.
        Spark[] _sparks;
        Material[] _sparkInk;
        int _nextSpark;
        float _showUntil, _nextBurst, _buzz;
        Transform[] _flash;
        float[] _flashAt;

        // ── Sides ───────────────────────────────────────────────────────────

        // Alone, Puttr AI is the visitors and bats first; the Player is the home side.
        int PlayerOf(int side) => _ai ? (side == 1 ? 0 : -1) : side;
        string SideName(int side)
        {
            int p = PlayerOf(side);
            return p < 0 ? "Puttr" : Arcade != null ? Arcade.PlayerName(p) : "Player " + (p + 1);
        }
        Color SideInk(int side)
        {
            int p = PlayerOf(side);
            return Hud.PlayerColors[(p < 0 ? 1 : p) % Hud.PlayerColors.Length];
        }
        static string Short(string name) => name.Length > 12 ? name.Substring(0, 11) + "…" : name;

        // ── Geometry ────────────────────────────────────────────────────────

        /// <summary>A unit vector along the ground, degrees right of center field.</summary>
        static Vector3 Dir(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        /// <summary>How far the outfield wall is from home: deepest in center, shortest down the lines.</summary>
        static float Wall(float degrees)
        {
            float k = Mathf.Clamp01(Mathf.Abs(degrees) / 45f);
            return Mathf.Lerp(WallMid, WallLine, k * k);
        }

        /// <summary>Home, first, second, third, and home again.</summary>
        static Vector3 Bag(int index)
        {
            switch (index)
            {
                case 1: return new Vector3(Diag, 0f, Diag);
                case 2: return new Vector3(0f, 0f, Diag * 2f);
                case 3: return new Vector3(-Diag, 0f, Diag);
                default: return Vector3.zero;
            }
        }

        static float Smooth(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        static Transform Node(string name, Transform parent, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        static GameObject Quiet(GameObject go)
        {
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // What is drawn over what, among things that are see-through.
        static GameObject Order(GameObject go, int order)
        {
            go.GetComponent<Renderer>().sortingOrder = order;
            return go;
        }

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // A night game. The floods behind the plate do the work, the ones over the
            // outfield put an edge of light on everyone, and two warm ones fill from the sides.
            Sun(Root, new Vector3(52f, 16f, 0f), new Color(0.94f, 0.97f, 1f), 1.55f);
            Sun(Root, new Vector3(40f, -160f, 0f), new Color(0.78f, 0.87f, 1f), 0.85f).shadows = LightShadows.None;
            Sun(Root, new Vector3(36f, 96f, 0f), new Color(1f, 0.95f, 0.86f), 0.38f).shadows = LightShadows.None;
            Sun(Root, new Vector3(34f, -78f, 0f), new Color(1f, 0.95f, 0.88f), 0.3f).shadows = LightShadows.None;

            // The ground the park stands on, and the field itself, painted: all of it once, and
            // the ground round home plate again, finer, since the camera sits right over it.
            Shape(PrimitiveType.Cube, "Lot", Root, new Vector3(0f, -3f, 900f), new Vector3(9000f, 4f, 9000f), Make.Lit(new Color(0.02f, 0.025f, 0.03f), 0.1f));
            Quiet(Make.MeshObject("Field", Root, Ground(-FieldHalf, FieldZ0, FieldHalf, FieldZ0 + FieldHalf * 2f, 0f),
                Make.Lit(Color.white, 0.08f, 0f, Paint(2048, -FieldHalf, FieldZ0, FieldHalf * 2f))));
            Quiet(Make.MeshObject("Home", Root, Ground(-PlateHalf, -PlateHalf, PlateHalf, PlateHalf, 0.35f),
                Make.Lit(Color.white, 0.08f, 0f, Paint(1024, -PlateHalf, -PlateHalf, PlateHalf * 2f))));

            BuildDiamond();
            BuildPark();
            BuildTowers();
            BuildScoreboard();
            BuildFigures();
            BuildEffects();
            BuildWheel();
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        // A flat piece of painted ground, mapped from above.
        static Mesh Ground(float x0, float z0, float x1, float z1, float y)
        {
            var mesh = new Mesh { name = "Ground" };
            mesh.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // The bags, the plate, and the mound with its rubber.
        void BuildDiamond()
        {
            Material white = Make.Lit(new Color(0.93f, 0.93f, 0.9f), 0.25f);
            for (int i = 1; i <= 3; i++)
                Make.Panel("Base", Root, Bag(i) + new Vector3(0f, 2.6f, 0f), new Vector3(32f, 6f, 32f), 2.4f, white).transform.localRotation = Quaternion.Euler(0f, 45f, 0f);

            // Home plate: square to the pitcher, and a point toward the catcher.
            var plate = new Mesh { name = "Plate" };
            plate.vertices = new[] { new Vector3(-16f, 1.3f, 15f), new Vector3(16f, 1.3f, 15f), new Vector3(16f, 1.3f, -1f), new Vector3(0f, 1.3f, -17f), new Vector3(-16f, 1.3f, -1f) };
            plate.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            plate.triangles = new[] { 0, 1, 2, 0, 2, 4, 4, 2, 3 };
            plate.RecalculateBounds();
            Quiet(Make.MeshObject("Home plate", Root, plate, white));

            Shape(PrimitiveType.Sphere, "Mound", Root, Mound + Vector3.down * 1f, new Vector3(200f, (MoundTop + 1f) * 2f, 200f), Make.Lit(new Color(0.5f, 0.335f, 0.2f), 0.06f));
            Shape(PrimitiveType.Cube, "Rubber", Root, Mound + new Vector3(0f, MoundTop, 0f), new Vector3(40f, 2f, 10f), white);
        }

        // Round the field: the padded wall and what is written on it, the foul poles, the
        // batter's eye, the stands in their tiers, the haze under the lights and the night sky.
        void BuildPark()
        {
            var line = new List<Vector3>();
            var outward = new List<Vector3>();
            var along = new List<float>();
            Outline(line, outward, along);

            Order(Card("Sky", Root, new Vector3(0f, 1500f, 5600f), new Vector2(18000f, 4500f), Make.Glow(Color.white, false, Sky())), -9);

            // The stands, from the back: the roof and the lights along its edge, the upper deck,
            // the dark of the concourse under it, the face of the upper deck with its ribbon of
            // light, and the lower bowl.
            Texture2D people = Crowd();
            Material low = Make.Glow(new Color(1.05f, 1.05f, 1.05f, 1f), false, people);
            Material high = Make.Glow(new Color(0.66f, 0.7f, 0.8f, 1f), false, people);
            high.SetTextureOffset("_MainTex", new Vector2(0.37f, 0f));
            Material dark = Make.Glow(new Color(0.016f, 0.02f, 0.03f, 1f), false);
            Deck("Roof", line, outward, along, RoofOff, RoofY, HighOff1 + 60f, RoofY - 46f, Make.Glow(new Color(0.03f, 0.036f, 0.048f, 1f), false), 5000f, -8);
            Deck("Back wall", line, outward, along, HighOff1, HighY1, HighOff1 + 60f, RoofY - 46f, dark, 5000f, -8);
            Deck("Upper deck", line, outward, along, FaceOff, HighY0, HighOff1, HighY1, high, 3328f, -7);
            Deck("Roof edge", line, outward, along, RoofOff, RoofY, RoofOff, RoofY + 16f, Make.Glow(new Color(0.07f, 0.08f, 0.1f, 1f), false), 5000f, -6);
            Deck("Roof lights", line, outward, along, RoofOff - 2f, RoofY - 7f, RoofOff - 2f, RoofY - 1f, Make.Glow(new Color(0.8f, 0.9f, 1.1f, 0.8f), true), 5000f, -6);
            Deck("Concourse", line, outward, along, LowOff1, LowY1, LowOff1, FaceY0, Make.Glow(new Color(0.34f, 0.26f, 0.15f, 1f), false, Concourse()), 520f, -6);
            Deck("Under the deck", line, outward, along, FaceOff, FaceY0, LowOff1, FaceY0, dark, 5000f, -6);
            Deck("Deck face", line, outward, along, FaceOff, FaceY0, FaceOff, HighY0, Make.Glow(new Color(0.03f, 0.045f, 0.075f, 1f), false), 5000f, -5);
            Deck("Ribbon", line, outward, along, FaceOff - 1f, FaceY0 + 14f, FaceOff - 1f, FaceY0 + 34f, Make.Glow(new Color(0.55f, 1.15f, 0.32f, 0.95f), true), 5000f, -4); // Puttr green
            Deck("Rail", line, outward, along, FaceOff - 1f, HighY0 - 3f, FaceOff - 1f, HighY0 + 3f, Make.Glow(new Color(0.75f, 0.8f, 0.9f, 0.8f), false), 5000f, -4);
            Deck("Lower bowl", line, outward, along, LowOff0, LowY0, LowOff1, LowY1, low, 3328f, -5);
            // The Puttr lockup along the face of the upper deck (the mark and PUTTR, as Spot Kick's boards).
            foreach (float a in new[] { -31f, 0f, 31f })
                Lockup(Root, Dir(a) * (Wall(a) + FaceOff - 3f) + Vector3.up * (FaceY0 + 24f), 26f, -3).transform.localRotation = Quaternion.Euler(0f, a, 0f);

            // The batter's eye: nothing but dark green behind the pitcher, so the ball can be seen.
            Quiet(Shape(PrimitiveType.Cube, "Batter's eye", Root, Dir(0f) * (WallMid + 64f) + Vector3.up * (WallHigh + 150f), new Vector3(800f, 300f, 30f), Make.Lit(new Color(0.012f, 0.045f, 0.03f), 0.15f)));

            // The wall: green padding, with a yellow line along the top.
            Deck("Wall", line, outward, along, 0f, 0f, 0f, WallHigh, Make.Glow(Color.white, false, Padding()), 520f, -3);

            // What is written on it: dark paint on light boards.
            Color ink = new Color(0.03f, 0.03f, 0.04f);
            // Puttr's own colours: the lockup on dark boards, the distances on Puttr green.
            Color puttrGreen = new Color(0.48f, 0.83f, 0.27f);
            OnWallLogo(-14f, 250f);
            OnWallLogo(13f, 330f);
            OnWallLogo(33f, 250f);
            OnWallLogo(-33f, 330f);
            OnWall("400", 0f, 34f, ink, 120f, puttrGreen);
            OnWall("375", -24f, 34f, ink, 120f, puttrGreen);
            OnWall("375", 24f, 34f, ink, 120f, puttrGreen);
            OnWall("330", -41f, 34f, ink, 120f, puttrGreen);
            OnWall("330", 41f, 34f, ink, 120f, puttrGreen);

            // The foul poles.
            Material pole = Make.Emissive(new Color(0.98f, 0.85f, 0.1f), new Color(0.98f, 0.85f, 0.1f) * 1.2f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 foot = Dir(side * 45f) * (WallLine + 12f);
                Quiet(Props.Rod("Foul pole", Root, foot, foot + Vector3.up * 420f, 4.5f, pole));
                Quiet(Shape(PrimitiveType.Cube, "Foul screen", Root, foot + Dir(side * 45f) * 16f + Vector3.up * 300f, new Vector3(3f, 220f, 30f), pole)).transform.localRotation = Quaternion.Euler(0f, side * 45f, 0f);
            }

            // Haze: the air itself lit up, thickest low down in front of the seats.
            Deck("Haze", line, outward, along, -40f, 0f, -40f, 760f, Make.Glow(new Color(0.62f, 0.76f, 1f, 0.11f), true, Fade()), 5000f, 1);

            // Cameras in the crowd: dots of light that go off now and then, and all at once for a home run.
            Material pop = Make.Glow(new Color(1f, 1f, 1f, 0.95f), true, Make.SoftDot());
            _flash = new Transform[36];
            _flashAt = new float[_flash.Length];
            for (int i = 0; i < _flash.Length; i++)
            {
                float a = Random.Range(-44f, 44f), t = Random.Range(0.1f, 0.92f);
                bool upper = i % 3 == 0;
                float off = upper ? Mathf.Lerp(FaceOff, HighOff1, t) : Mathf.Lerp(LowOff0, LowOff1, t);
                float y = upper ? Mathf.Lerp(HighY0, HighY1, t) : Mathf.Lerp(LowY0, LowY1, t);
                _flash[i] = Order(Card("Flash", Root, Dir(a) * (Wall(a) + off - 6f) + Vector3.up * (y + 12f), Vector2.one, pop), 2).transform;
                _flash[i].localScale = Vector3.zero;
                _flashAt[i] = Random.Range(0f, 6f);
            }
        }

        // Words on the wall, on a painted board if one is asked for.
        // The Puttr lockup (the mark and PUTTR, white letters) as a flat card, the way Spot Kick
        // puts it on its boards. It wants a dark ground behind it.
        static Texture2D _logo;
        static Material _logoPaint;
        GameObject Lockup(Transform parent, Vector3 at, float height, int order)
        {
            if (_logo == null) _logo = Resources.Load<Texture2D>("PuttrArcade/LogoHeader");
            if (_logoPaint == null) _logoPaint = Make.Glow(new Color(1.15f, 1.15f, 1.15f, 1f), false, _logo);
            float width = _logo == null ? height * 4f : height * _logo.width / _logo.height;
            return Order(Card("Puttr", parent, at, new Vector2(width, height), _logoPaint), order);
        }

        void OnWallLogo(float degrees, float board)
        {
            Quaternion turn = Quaternion.Euler(0f, degrees, 0f);
            Vector3 at = Dir(degrees) * (Wall(degrees) - 6f) + Vector3.up * (WallHigh * 0.47f);
            Order(Card("Board", Root, at, new Vector2(board, WallHigh * 0.7f), Make.Glow(new Color(0.035f, 0.045f, 0.05f), false)), -2).transform.localRotation = turn;
            Lockup(Root, at - Dir(degrees) * 3f, WallHigh * 0.4f, -1).transform.localRotation = turn;
        }

        void OnWall(string text, float degrees, float height, Color color, float board, Color paint)
        {
            Quaternion turn = Quaternion.Euler(0f, degrees, 0f);
            Vector3 at = Dir(degrees) * (Wall(degrees) - 6f) + Vector3.up * (WallHigh * 0.47f);
            Order(Card("Board", Root, at, new Vector2(board, WallHigh * 0.7f), Make.Glow(paint, false)), -2).transform.localRotation = turn;
            Painted(Words(Root, text, at - Dir(degrees) * 4f, height, color)).transform.localRotation = turn;
        }

        static Material _paint;

        // Words painted on the park. Words in a scene are drawn over whatever stands in front
        // of them, and here the batter stands in front of the wall; these are drawn with the
        // park's own shader instead, which takes only the shape of the letters from the font.
        // That leaves them dark, so they go on light boards.
        static TextMesh Painted(TextMesh words)
        {
            var renderer = words.GetComponent<MeshRenderer>();
            if (_paint == null) _paint = Make.Glow(Color.white, false, renderer.sharedMaterial.mainTexture);
            renderer.sharedMaterial = _paint;
            renderer.sortingOrder = 0;
            return words;
        }

        // The line the wall follows, seen from above: up the third base side, round the
        // outfield, and back down the first base side. outward points away from the field,
        // and along is the distance covered, for laying the crowd on evenly.
        static void Outline(List<Vector3> line, List<Vector3> outward, List<float> along)
        {
            float run = 0f;
            void Add(Vector3 p, Vector3 n, float extra)
            {
                if (line.Count > 0) run += (p - line[line.Count - 1]).magnitude + extra;
                line.Add(p);
                outward.Add(n);
                along.Add(run);
            }
            // At a foul pole the stands turn a corner: the same point, facing round a little at a time.
            void Corner(Vector3 p, Vector3 from, Vector3 to)
            {
                float step = Vector3.Angle(from, to) * Mathf.Deg2Rad / 6f * 400f;
                for (int j = 0; j <= 6; j++) Add(p, Vector3.Slerp(from, to, j / 6f).normalized, j == 0 ? 0f : step);
            }
            Vector3 Square(Vector3 d) => new Vector3(-d.z, 0f, d.x).normalized;

            Vector3 poleL = Dir(-45f) * WallLine, poleR = Dir(45f) * WallLine;
            Vector3 endL = new Vector3(-SideX, 0f, SideZ), endR = new Vector3(SideX, 0f, SideZ);
            Vector3 outL = Square(poleL - endL), outR = Square(endR - poleR);
            Add(endL, outL, 0f);
            Corner(poleL, outL, Dir(-45f));
            for (float a = -42f; a < 43.5f; a += 3f) Add(Dir(a) * Wall(a), Dir(a), 0f);
            Corner(poleR, Dir(45f), outR);
            Add(endR, outR, 0f);
        }

        // A band that follows the outline, from one distance behind the wall and height to
        // another. tile is how far one copy of its texture goes along it.
        void Deck(string name, List<Vector3> line, List<Vector3> outward, List<float> along, float off0, float y0, float off1, float y1, Material material, float tile, int order)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < line.Count; i++)
            {
                verts.Add(line[i] + outward[i] * off0 + Vector3.up * y0);
                verts.Add(line[i] + outward[i] * off1 + Vector3.up * y1);
                uvs.Add(new Vector2(along[i] / tile, 0f));
                uvs.Add(new Vector2(along[i] / tile, 1f));
                if (i == 0) continue;
                int a = (i - 1) * 2;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
                tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Order(Quiet(Make.MeshObject(name, Root, mesh, material)), order);
        }

        // Four towers of floodlights behind the upper deck, each with its glare.
        void BuildTowers()
        {
            Material iron = Make.Lit(new Color(0.05f, 0.06f, 0.07f), 0.4f, 0.5f);
            Material lamps = Make.Glow(new Color(2.6f, 2.75f, 3f, 1f), true, Lamps());
            Material glare = Make.Glow(new Color(0.8f, 0.9f, 1f, 0.3f), true, Make.SoftDot());
            Material beam = Make.Glow(new Color(0.8f, 0.9f, 1f, 0.1f), true, Make.SoftDot());
            foreach (float a in new[] { -36f, -13f, 13f, 36f })
            {
                Vector3 foot = Dir(a) * (Wall(a) + HighOff1 + 90f);
                Quiet(Props.Rod("Tower", Root, foot, foot + Vector3.up * 900f, 13f, iron));
                Transform head = Node("Lights", Root, foot + Vector3.up * 930f);
                head.localRotation = Quaternion.LookRotation(new Vector3(0f, 150f, 600f) - head.localPosition);
                Quiet(Shape(PrimitiveType.Cube, "Frame", head, Vector3.zero, new Vector3(330f, 186f, 22f), iron));
                Order(Card("Lamps", head, new Vector3(0f, 0f, 13f), new Vector2(310f, 170f), lamps), 1);
                Order(Card("Glare", head, new Vector3(0f, 0f, 40f), new Vector2(1000f, 1000f), glare), 1);
                Order(Card("Beam", head, new Vector3(0f, -260f, 60f), new Vector2(2300f, 1500f), beam), 1);
            }
        }

        // The scoreboard, standing in the right center field seats and turned to face the plate:
        // the inning, three lamps for the outs, and a diamond that lights where there are
        // runners. From behind the catcher this is where the bases are read.
        void BuildScoreboard()
        {
            Transform board = Node("Scoreboard", Root, Dir(BoardAngle) * (Wall(BoardAngle) + 250f) + Vector3.down * 150f);
            board.localRotation = Quaternion.Euler(0f, BoardAngle, 0f);
            Material iron = Make.Lit(new Color(0.05f, 0.06f, 0.07f), 0.4f, 0.5f);
            Quiet(Shape(PrimitiveType.Cube, "Frame", board, new Vector3(0f, 490f, 8f), new Vector3(748f, 358f, 22f), iron));
            Quiet(Shape(PrimitiveType.Cube, "Face", board, new Vector3(0f, 490f, 0f), new Vector3(720f, 330f, 24f), Make.Lit(new Color(0.012f, 0.02f, 0.035f), 0.3f)));

            const float z = -22f;   // everything on the face stands this far off it
            Color dim = new Color(1f, 1f, 1f, 0.7f);
            Lockup(board, new Vector3(0f, 630f, z), 34f, 2);
            Quiet(Card("Rule", board, new Vector3(0f, 601f, z), new Vector2(660f, 3f), Make.Glow(new Color(1f, 1f, 1f, 0.3f), false)));

            Words(board, "INNING", new Vector3(-258f, 572f, z), 15f, dim, TextAnchor.MiddleCenter, 64);
            _sbInning = Words(board, "1", new Vector3(-258f, 496f, z), 74f, Color.white);
            Words(board, "OUTS", new Vector3(248f, 572f, z), 15f, dim, TextAnchor.MiddleCenter, 64);

            _lampOff = Make.Glow(new Color(1f, 1f, 1f, 0.12f), false);
            _outOn = Make.Glow(new Color(1.5f, 0.3f, 0.24f, 1f), true);
            _baseOn = Make.Glow(new Color(1.5f, 1.05f, 0.14f, 1f), true);
            for (int i = 0; i < 3; i++)
                _sbOut[i] = Order(Hoop("Out", board, new Vector3(190f + i * 58f, 496f, z), 0f, 24f, _lampOff), 2).GetComponent<MeshRenderer>();
            // First is to the right, as it is from behind the plate.
            Vector3[] at = { new Vector3(74f, 492f, z), new Vector3(0f, 566f, z), new Vector3(-74f, 492f, z) };
            for (int i = 0; i < 3; i++)
            {
                GameObject bag = Order(Card("On base", board, at[i], new Vector2(60f, 60f), _lampOff), 2);
                bag.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                _sbBase[i] = bag.GetComponent<MeshRenderer>();
            }
            Order(Card("Plate", board, new Vector3(0f, 420f, z), new Vector2(26f, 26f), Make.Glow(new Color(1f, 1f, 1f, 0.55f), false)), 2).transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            _sbRuns = Words(board, "", new Vector3(0f, 375f, z), 19f, Color.white, TextAnchor.MiddleCenter, 64);
            _sbAtBat = Words(board, "", new Vector3(0f, 342f, z), 13f, Make.Amber, TextAnchor.MiddleCenter, 64);
        }

        // Everyone on the field. The side at bat wears its own color; the bat is the batter's.
        void BuildFigures()
        {
            Texture2D shirt = Jersey();
            _batTop = Make.Lit(Color.white, 0.3f, 0f, shirt);
            _batCap = Make.Lit(Color.white, 0.72f);
            _fieldTop = Make.Lit(Color.white, 0.3f, 0f, shirt);
            _fieldCap = Make.Lit(Color.white, 0.4f);
            Material blob = Make.Glow(new Color(0f, 0f, 0f, 0.5f), false, Make.SoftDot());
            var kit = new Ballplayer.Kit
            {
                Skin = Make.Lit(new Color(0.74f, 0.54f, 0.41f), 0.35f),
                Pants = Make.Lit(new Color(0.84f, 0.85f, 0.86f), 0.25f),
                Shoe = Make.Lit(new Color(0.03f, 0.03f, 0.035f), 0.45f),
                Belt = Make.Lit(new Color(0.04f, 0.04f, 0.05f), 0.5f),
                Leather = Make.Lit(new Color(0.4f, 0.23f, 0.11f), 0.35f),
                Gear = Make.Lit(new Color(0.05f, 0.06f, 0.09f), 0.5f),
                Hair = Make.Lit(new Color(0.09f, 0.06f, 0.04f), 0.3f),
                Blob = blob,
            };
            Ballplayer.Kit hitter = kit, fielder = kit;
            hitter.Jersey = _batTop; hitter.Trim = _batCap;
            fielder.Jersey = _fieldTop; fielder.Trim = _fieldCap;

            for (int i = 0; i < _fielders.Length; i++) _fielders[i] = NewFigure("Fielder", fielder, Ballplayer.Fielder, FielderTall);
            _pitcher = NewFigure("Pitcher", fielder, Ballplayer.Fielder);
            _catcher = NewFigure("Catcher", fielder, Ballplayer.Catcher);
            _batter = NewFigure("Batter", hitter, Ballplayer.Batter);
            for (int i = 0; i < _runners.Length; i++)
            {
                _runners[i] = new Runner { Body = NewFigure("Runner", hitter, Ballplayer.Batter) };
                _runners[i].Body.Visible = false;
            }

            // The bat is held by the knob: PoseBatter puts it in the hands.
            _bat = Node("Bat", _batter.Root, new Vector3(0f, 21f, -7f));
            var profile = new List<Vector2>
            {
                new Vector2(0.02f, 0f), new Vector2(1.4f, 0.3f), new Vector2(1.1f, 1.3f), new Vector2(0.95f, 13f),
                new Vector2(2.1f, 24f), new Vector2(2.4f, 33f), new Vector2(2.1f, 35.5f), new Vector2(0.02f, 36f),
            };
            Make.MeshObject("Wood", _bat, Make.Lathe(profile, 14), Make.Lit(new Color(0.78f, 0.6f, 0.36f), 0.55f));

            // The ball, and the light and the streak that let it be seen against the crowd.
            _ball = Quiet(Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one, Make.Emissive(Color.white, Color.white * 1.3f))).transform;
            Order(Card("Ball glow", _ball, Vector3.zero, Vector2.one * 3.4f, Make.Glow(new Color(1f, 1f, 1f, 0.3f), true, Make.SoftDot())), 3);
            _trail = Stroke(Root, "Ball trail", new Color(1f, 1f, 1f, 0.42f), 10f);
            _trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            _trail.sortingOrder = 3;
            HideBall();
        }

        Ballplayer NewFigure(string name, Ballplayer.Kit kit, int role, float tall = Tall)
        {
            var figure = new Ballplayer(Root, kit, role);
            figure.Root.name = name;
            figure.Root.localScale = Vector3.one * tall;
            figure.Shadow.localScale *= tall / Tall;
            return figure;
        }

        // Sparks for the fireworks: soft dots, used in turn.
        void BuildEffects()
        {
            Texture2D dot = Make.SoftDot();
            Color[] inks = { HitInk[Homer], Color.white, Make.Bright, HitInk[Base2], Make.Red };
            _sparkInk = new Material[inks.Length];
            for (int i = 0; i < inks.Length; i++) _sparkInk[i] = Make.Glow(inks[i], true, dot);
            _sparks = new Spark[96];
            for (int i = 0; i < _sparks.Length; i++)
            {
                GameObject spark = Order(Card("Spark", Root, Vector3.zero, Vector2.one, _sparkInk[0]), 2);
                spark.transform.localScale = Vector3.zero;
                _sparks[i] = new Spark { T = spark.transform, Age = 1f, Life = 1f };
            }
        }

        // The hit wheel: the hole from above, the Player below it, and round the front of the
        // hole a wedge for each kind of hit. It is fixed to the lens, low on the right, clear
        // of the batter and the pitcher: BeforeCamera puts it there for every picture.
        void BuildWheel()
        {
            _wheel = Node("Hit wheel", Root, Eye + new Vector3(30f, -10f, WheelAt));
            _wheel.localScale = Vector3.one * 0.1f;

            Order(Card("Panel", _wheel, new Vector3(-42f, 0f, 1f), new Vector2(186f, 126f), Make.Glow(new Color(0.01f, 0.015f, 0.02f, 0.74f), false)), 20);
            for (int k = 0; k < _wedge.Length; k++)
            {
                GameObject wedge = Order(Quiet(Make.MeshObject("Wedge", _wheel, new Mesh(), Make.Glow(HitInk[k], false))), 21);
                _wedge[k] = wedge.GetComponent<MeshFilter>();
            }
            Order(Hoop("Cup", _wheel, Vector3.zero, 0f, HoleR, Make.Glow(new Color(0f, 0f, 0f, 0.92f), false)), 22);
            Order(Hoop("Lip", _wheel, Vector3.zero, HoleR - 0.9f, HoleR + 0.9f, Make.Glow(Color.white, false)), 23);
            Order(Hoop("Edge", _wheel, Vector3.zero, WheelR - 0.5f, WheelR + 0.5f, Make.Glow(new Color(1f, 1f, 1f, 0.5f), false)), 23);

            // Where the ball comes from: a small one, outside the wheel on the line of a straight putt.
            _teeDot = Order(Hoop("Ball", _wheel, Vector3.zero, 0f, 2.4f, Make.Glow(Color.white, false)), 23).transform;

            // Where the last putt went in.
            _marker = Node("Marker", _wheel, Vector3.zero);
            Order(Hoop("Marker edge", _marker, new Vector3(0f, 0f, -0.3f), 0f, 4.4f, Make.Glow(new Color(0f, 0f, 0f, 0.95f), false)), 24);
            Order(Hoop("Marker dot", _marker, new Vector3(0f, 0f, -0.4f), 0f, 3f, Make.Glow(Color.white, false)), 25);
            _marker.gameObject.SetActive(false);

            Label("HIT WHEEL", new Vector3(-126f, 51f, -1f), 5.2f, Color.white, TextAnchor.MiddleLeft);
            _wheelLevel = Label("", new Vector3(42f, 51f, -1f), 4.2f, Make.Amber, TextAnchor.MiddleRight);
            _wheelSays = Label("", new Vector3(-42f, -54f, -1f), 4.2f, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter);
            string[] names = { "OUT", "SINGLE", "DOUBLE", "TRIPLE", "HOME RUN" };
            for (int k = Homer; k >= Out; k--)
            {
                float y = 28f - (Homer - k) * 14f;
                Order(Hoop("Key", _wheel, new Vector3(-122f, y, -1f), 0f, 3.2f, Make.Glow(HitInk[k], false)), 22);
                Label(names[k], new Vector3(-114f, y, -1f), 4.8f, Color.white, TextAnchor.MiddleLeft);
            }
        }

        const float WheelAt = 60f;   // how far in front of the lens the wheel hangs

        void BeforeCamera(ScriptableRenderContext context, Camera eye)
        {
            if (_wheel == null || eye == null || eye.targetTexture != null || eye.cameraType != CameraType.Game) return;
            if (!_wheel.gameObject.activeInHierarchy) return;
            // The panel is 186 across and 126 high, with its middle 42 left of the wheel's own.
            Transform lens = eye.transform;
            float high = Mathf.Tan(eye.fieldOfView * 0.5f * Mathf.Deg2Rad) * WheelAt, wide = high * eye.aspect;
            float scale = wide * 0.5f / 186f;                       // a quarter of the screen across
            _wheel.position = lens.position + lens.forward * WheelAt + lens.right * (wide * 0.975f - 51f * scale) - lens.up * (high * 0.4f);
            _wheel.rotation = lens.rotation;
            _wheel.localScale = Vector3.one * scale;
        }

        TextMesh Label(string text, Vector3 at, float height, Color color, TextAnchor anchor)
        {
            TextMesh words = Words(_wheel, text, at, height, color, anchor, 64);
            words.GetComponent<MeshRenderer>().sortingOrder = 30;
            return words;
        }

        // ── Paint ───────────────────────────────────────────────────────────

        // A square of the field from above, painted from Turf: n pixels a side, from (x0, z0), size across.
        static Texture2D Paint(int n, float x0, float z0, float size)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 12 };
            var px = new Color32[n * n];
            float per = size / n;
            for (int py = 0; py < n; py++)
            {
                float z = z0 + (py + 0.5f) * per;
                for (int qx = 0; qx < n; qx++) px[py * n + qx] = Turf(x0 + (qx + 0.5f) * per, z);
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // Up one stripe of the mower and down the next, with a soft edge between.
        static float Mown(float across) => Mathf.Clamp(Mathf.Sin(across * Mathf.PI / 118f) * 5f, -1f, 1f);

        static bool InBox(float x, float z, float x0, float x1, float z0, float z1, float line)
        {
            if (x < x0 || x > x1 || z < z0 || z > z1) return false;
            return x < x0 + line || x > x1 - line || z < z0 + line || z > z1 - line;
        }

        // The color of the ground at a place: mown grass in two close greens, the groomed dirt
        // of the infield, the mound, the circle round the plate and the two on deck, chalk
        // down the lines and round the boxes, and the warning track under the wall.
        static Color32 Turf(float x, float z)
        {
            float ax = Mathf.Abs(x), far = Mathf.Sqrt(x * x + z * z), mound = Mathf.Sqrt(x * x + (z - MoundZ) * (z - MoundZ));
            bool fair = z >= ax;
            float broad = Mathf.PerlinNoise(x * 0.0041f + 37.3f, z * 0.0041f + 11.7f) - 0.5f;
            float patch = Mathf.PerlinNoise(x * 0.033f + 91.1f, z * 0.033f + 53.9f) - 0.5f;
            float grit = Mathf.PerlinNoise(x * 0.37f + 5.3f, z * 0.37f + 77.1f) - 0.5f;

            // Grass: the mower went both ways across the diamond.
            float mow = (Mown((x + z) * 0.7071f) + Mown((x - z) * 0.7071f)) * 0.5f;
            Color c = Color.Lerp(new Color(0.135f, 0.36f, 0.105f), new Color(0.175f, 0.42f, 0.125f), 0.5f + 0.5f * mow);
            float rough = broad * 0.2f + patch * 0.1f + grit * 0.1f;
            bool chalk = false;

            // Dirt: the arc of the infield, less the grass inside the base paths; the circle
            // round the plate; the mound; and the two circles on deck.
            bool inside = ax + Mathf.Abs(z - Diag) < Diag - 46f;
            bool deck = (ax - 250f) * (ax - 250f) + (z - 120f) * (z - 120f) < 38f * 38f;
            if ((mound < 633f && z > ax - 44f && !inside) || far < 150f || deck)
            {
                // Dragged smooth, in arcs round the mound, and a shade darker where it was watered.
                float drag = Mathf.Sin(mound * 0.19f) * 0.035f;
                c = Color.Lerp(new Color(0.43f, 0.275f, 0.16f), new Color(0.5f, 0.33f, 0.2f), 0.5f + broad * 1.4f);
                rough = drag + patch * 0.16f + grit * 0.12f;
                if (far < 150f) { c = new Color(0.5f, 0.335f, 0.205f); rough = patch * 0.14f + grit * 0.14f; }
            }
            if (mound < 100f) { c = new Color(0.5f, 0.335f, 0.2f); rough = patch * 0.12f + grit * 0.12f; }
            if (deck && Mathf.Abs(Mathf.Sqrt((ax - 250f) * (ax - 250f) + (z - 120f) * (z - 120f)) - 35f) < 2f) chalk = true;

            // The warning track, and nothing but concrete beyond the wall.
            bool beyond = false, track = false;
            if (fair)
            {
                float wall = Wall(Mathf.Atan2(ax, z) * Mathf.Rad2Deg);
                if (far > wall + 8f) beyond = true;
                else if (far > wall - 105f) track = true;
            }
            else
            {
                float side = (ax - SideX) * 0.9892f - (z - SideZ) * 0.1467f;   // how far outside the wall down the line
                if (side > 8f || z < SideZ) beyond = true;
                else if (side > -90f) track = true;
            }
            if (track) { c = new Color(0.44f, 0.29f, 0.18f); rough = patch * 0.18f + grit * 0.14f; }

            // Chalk: the foul lines, the two batter's boxes and the catcher's.
            if (Mathf.Abs(z - ax) < 3.6f && far > 44f && !beyond) chalk = true;
            if (InBox(ax, z, 23f, 82f, -52f, 58f, 3.2f)) chalk = true;
            if (InBox(x, z, -40f, 40f, -150f, -52f, 3.2f) && z < -55.2f) chalk = true;
            if (chalk) { c = new Color(0.9f, 0.9f, 0.86f); rough = patch * 0.1f + grit * 0.16f; }
            if (beyond) { c = new Color(0.07f, 0.08f, 0.09f); rough = grit * 0.2f; }

            float k = 1f + rough;
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);
        }

        // People in their seats under the lights: a head and shoulders in every seat, here and
        // there an empty one or a phone held up, and the steps between the blocks.
        static Texture2D Crowd()
        {
            const int w = 1024, h = 256, seat = 8, row = 16;
            var rng = new System.Random(27);
            Color[] shirts =
            {
                new Color(0.6f, 0.61f, 0.66f), new Color(0.2f, 0.3f, 0.6f), new Color(0.66f, 0.2f, 0.18f), new Color(0.82f, 0.81f, 0.76f),
                new Color(0.16f, 0.4f, 0.25f), new Color(0.76f, 0.56f, 0.14f), new Color(0.3f, 0.3f, 0.35f), new Color(0.5f, 0.37f, 0.28f),
                new Color(0.12f, 0.14f, 0.24f), new Color(0.85f, 0.85f, 0.88f),
            };
            Color[] skins = { new Color(0.72f, 0.52f, 0.4f), new Color(0.56f, 0.38f, 0.27f), new Color(0.8f, 0.62f, 0.5f), new Color(0.36f, 0.24f, 0.17f) };
            var px = new Color[w * h];
            for (int r = 0; r < h / row; r++)
            {
                float lit = 1f - r / (float)(h / row) * 0.38f;     // the back rows get less of the light
                for (int s = 0; s < w / seat; s++)
                {
                    int x0 = s * seat, y0 = r * row;
                    bool steps = s % 16 == 0;
                    Color back = steps ? new Color(0.085f, 0.09f, 0.1f) * lit : new Color(0.022f, 0.03f, 0.045f);
                    for (int y = 0; y < row; y++)
                        for (int x = 0; x < seat; x++)
                            px[(y0 + y) * w + x0 + x] = steps && y < 2 ? back * 1.7f : back;
                    if (steps) continue;
                    if (rng.NextDouble() < 0.09)
                    {
                        // Nobody here: the back of the seat.
                        for (int y = 2; y < 8; y++) for (int x = 1; x < 7; x++) px[(y0 + y) * w + x0 + x] = new Color(0.03f, 0.075f, 0.06f) * lit;
                        continue;
                    }
                    Color shirt = shirts[rng.Next(shirts.Length)] * (0.42f + (float)rng.NextDouble() * 0.6f) * lit;
                    Color skin = skins[rng.Next(skins.Length)] * (0.6f + (float)rng.NextDouble() * 0.4f) * lit;
                    Color cap = rng.NextDouble() < 0.4 ? shirts[rng.Next(shirts.Length)] * 0.6f * lit : skin * 0.3f;
                    int lean = rng.Next(2), tall = rng.Next(2);
                    for (int y = 0; y < 9 + tall; y++)
                        for (int x = 1; x < 7; x++)
                        {
                            if (y == 8 + tall && (x == 1 || x == 6)) continue;     // round shoulders
                            px[(y0 + y) * w + x0 + x] = shirt * (y < 2 ? 0.55f : x == 1 || x == 6 ? 0.75f : 1f);
                        }
                    for (int y = 9 + tall; y < 14 + tall; y++)
                        for (int x = 2 + lean; x < 5 + lean; x++)
                            px[(y0 + y) * w + x0 + x] = y >= 12 + tall ? cap : skin * (x == 3 + lean ? 1.1f : 0.85f);
                    if (rng.NextDouble() < 0.02) px[(y0 + 11) * w + x0 + 6] = px[(y0 + 12) * w + x0 + 6] = new Color(0.9f, 0.96f, 1f);
                }
            }
            for (int i = 0; i < px.Length; i++) px[i].a = 1f;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // The padding on the wall: four pads to a tile, a yellow line along the top.
        static Texture2D Padding()
        {
            const int w = 256, h = 64;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                for (int x = 0; x < w; x++)
                {
                    int u = x % 64;
                    float seam = u < 2 ? 0.45f : u < 4 ? 1.18f : u > 61 ? 0.8f : 1f;
                    float round = 0.8f + 0.3f * Mathf.Sin(Mathf.Clamp01(v / 0.9f) * Mathf.PI) + (Make.Hash(x, y, 3) - 0.5f) * 0.08f;
                    Color c = new Color(0.02f, 0.135f, 0.085f) * seam * round;
                    if (v < 0.06f) c *= 0.5f;
                    if (v > 0.91f) c = new Color(0.48f, 0.83f, 0.27f) * (v > 0.97f ? 1f : 0.85f); // Puttr green
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            }
            Texture2D tex = Make.NewTexture(w, h, true);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // The concourse behind the lower bowl: dark, with the lights of the stalls along it.
        static Texture2D Concourse()
        {
            const int w = 128, h = 32;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = y / (h - 1f);
                    float stall = Mathf.Clamp01(1f - Mathf.Abs((x % 32) - 16f) / 11f) * Mathf.Clamp01(1f - Mathf.Abs(v - 0.45f) / 0.4f);
                    float c = 0.06f + stall * stall * (0.7f + Make.Hash(x / 32, 0, 9) * 0.6f);
                    px[y * w + x] = new Color(c, c, c, 1f);
                }
            Texture2D tex = Make.NewTexture(w, h, true);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // A bank of floodlights: three rows of five.
        static Texture2D Lamps()
        {
            const int w = 160, h = 96;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x % 32) - 15.5f, dy = (y % 32) - 15.5f;
                    float a = Mathf.Clamp01((13f - Mathf.Sqrt(dx * dx + dy * dy)) / 5f);
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            Texture2D tex = Make.NewTexture(w, h, false);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // Thick at the bottom, nothing at the top: for the haze.
        static Texture2D Fade()
        {
            const int h = 64;
            var px = new Color[4 * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f), a = Mathf.Pow(1f - v, 1.6f) * Mathf.Clamp01(v / 0.06f);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a);
            }
            Texture2D tex = Make.NewTexture(4, h, false);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // A jersey, wrapped once round the body: the front is at three quarters of the way
        // across and the back at a quarter. Piping down the front and the sides, a dark
        // collar, and a number on the back. It is tinted by the side's own color.
        static Texture2D Jersey()
        {
            const int n = 256;
            // Seven bars to a figure: top, top left, top right, middle, bottom left, bottom right, bottom.
            bool[] two = { true, false, true, true, true, false, true }, seven = { true, false, true, false, false, true, false };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                float v = y / (n - 1f);
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n;
                    float c = 0.92f + (Make.Hash(x, y, 13) - 0.5f) * 0.06f;
                    if (Mathf.Abs(u - 0.75f) < 0.006f && v < 0.9f) c = 0.3f;                    // the placket
                    if (Mathf.Abs(Mathf.Abs(u - 0.75f) - 0.03f) < 0.006f && v < 0.9f) c = 0.36f; // piping either side of it
                    if (Mathf.Abs(u - 0.5f) < 0.008f || u < 0.008f || u > 0.992f) c = 0.36f;    // the side seams
                    if (v > 0.9f) c = 0.3f;                                                      // the collar
                    for (int digit = 0; digit < 2; digit++)
                    {
                        float du = (u - (digit == 0 ? 0.205f : 0.295f)) / 0.07f, dv = (v - 0.62f) / 0.3f;   // -0.5 to 0.5 across a figure
                        if (Mathf.Abs(du) > 0.5f || Mathf.Abs(dv) > 0.5f) continue;
                        bool[] bars = digit == 0 ? two : seven;
                        const float t = 0.11f;
                        bool on = (bars[0] && dv > 0.5f - t) || (bars[3] && Mathf.Abs(dv) < t * 0.5f) || (bars[6] && dv < -0.5f + t)
                            || (bars[1] && du < -0.5f + t * 1.6f && dv > 0f) || (bars[2] && du > 0.5f - t * 1.6f && dv > 0f)
                            || (bars[4] && du < -0.5f + t * 1.6f && dv < 0f) || (bars[5] && du > 0.5f - t * 1.6f && dv < 0f);
                        if (on) c = 0.28f;
                    }
                    px[y * n + x] = new Color(c, c, c, 1f);
                }
            }
            Texture2D tex = Make.NewTexture(n, n, true);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // Night over the park: the glow of the lights low down, dark above, and a few stars.
        static Texture2D Sky()
        {
            const int w = 1024, n = 256;
            var tex = new Texture2D(w, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * n];
            Color low = new Color(0.16f, 0.2f, 0.33f), mid = new Color(0.05f, 0.075f, 0.16f), high = new Color(0.008f, 0.012f, 0.035f);
            for (int y = 0; y < n; y++)
            {
                float v = y / (n - 1f);
                Color sky = v < 0.42f ? Color.Lerp(low, mid, Smooth(0.2f, 0.42f, v)) : Color.Lerp(mid, high, Smooth(0.42f, 0.8f, v));
                for (int x = 0; x < w; x++)
                {
                    Color c = sky;
                    float star = Make.Hash(x, y, 91);
                    if (v > 0.5f && star > 0.9975f) c += Color.white * (0.25f + Make.Hash(x, y, 5) * 0.5f);
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ── The rules ───────────────────────────────────────────────────────

        // Where each band ends for a level, in degrees off the line. back is whether a putt
        // holed beyond the last band is out (or, on Rookie, still a single).
        static void Bands(int level, out float homer, out float triple, out float twice, out float last, out bool back)
        {
            if (level == Arcade.Hard) { homer = 5f; triple = 12f; twice = 21f; last = 48f; back = true; return; }
            homer = level == Arcade.Easy ? 18f : 9f;
            last = 90f;
            back = level != Arcade.Easy;
            // Rookie and Pro share out what is left in equal thirds.
            float third = (last - homer) / 3f;
            triple = homer + third;
            twice = homer + third * 2f;
        }

        static int Resolve(bool holed, float off, int level)
        {
            if (!holed) return Out;
            Bands(level, out float homer, out float triple, out float twice, out float last, out bool back);
            float away = Mathf.Abs(off);
            if (away <= homer) return Homer;
            if (away > last) return back ? Out : Base1;
            return away <= triple ? Base3 : away <= twice ? Base2 : Base1;
        }

        // Everyone moves up, the batter with them. Returns the runs that came home.
        int Advance(int bases)
        {
            int runs = 0;
            var next = new bool[3];
            for (int b = 2; b >= -1; b--)   // third, second, first, then the batter
            {
                if (b >= 0 && !_bases[b]) continue;
                int to = b + 1 + bases;     // 1 is first base, 4 is home
                if (to >= 4) runs++; else next[to - 1] = true;
            }
            for (int b = 0; b < 3; b++) _bases[b] = next[b];
            return runs;
        }

        bool Loaded => _bases[0] && _bases[1] && _bases[2];

        string OnBase()
        {
            string[] names = { "first", "second", "third" };
            var held = new List<string>();
            for (int b = 0; b < 3; b++) if (_bases[b]) held.Add(names[b]);
            if (held.Count == 0) return "";
            if (held.Count == 3) return "Bases loaded";
            return held.Count == 1 ? "Runner on " + held[0] : "Runners on " + held[0] + " and " + held[1];
        }

        // The side with the most runs, or -1 when it is level at the top.
        int Winner()
        {
            int best = 0, level = 0;
            for (int s = 1; s < _sides; s++) if (_runs[s] > _runs[best]) best = s;
            for (int s = 0; s < _sides; s++) if (_runs[s] == _runs[best]) level++;
            return level == 1 ? best : -1;
        }

        // ── The game ────────────────────────────────────────────────────────

        // Behind the home screen: a game under way, with runners at the corners.
        public override void Preview()
        {
            Begin(2);
            _inning = 1;
            _outs = 1;
            _line[0, 0] = 1; _runs[0] = 1; _hits[0] = 2;
            _line[1, 0] = 2; _runs[1] = 2; _hits[1] = 3;
            _batted[1, 0] = true;
            _batted[0, 1] = true;
            _bases[0] = true;
            _bases[2] = true;
            SetRunners();
            Show();
            _wheel.gameObject.SetActive(false);
        }

        public override void Begin(int players)
        {
            _players = Mathf.Clamp(players, 1, 4);
            _ai = _players == 1;
            _sides = _ai ? 2 : _players;
            _line = new int[_sides, Innings];
            _batted = new bool[_sides, Innings];
            _runs = new int[_sides];
            _hits = new int[_sides];
            _side = 0;
            _inning = 0;
            _outs = 0;
            _over = false;
            _opened = false;
            _newHalf = true;
            _batted[0, 0] = true;
            for (int b = 0; b < 3; b++) _bases[b] = false;

            ClearEffects();
            SetRunners();
            Colors();
            ResetField();
            _wheel.gameObject.SetActive(true);
            Deal(false);

            if (Arcade != null && Arcade.Hud != null)
            {
                // The line score: a row for each side, a box for each inning, and the runs.
                var names = new List<string>();
                for (int s = 0; s < _sides; s++) names.Add(Short(SideName(s)));
                Arcade.Hud.BuildTable(names, Innings, false, 78f, true, "INNING", "RUNS");
                Board();
            }
            Show();
        }

        // Deal the next at-bat: a distance from the level's range, and one of the three tees.
        void Deal(bool aloud)
        {
            int level = Arcade != null ? Mathf.Clamp(Arcade.Level, Arcade.Easy, Arcade.Hard) : Arcade.Easy;
            _feet = _rng.Next(Feet[level, 0], Feet[level, 1] + 1);
            _tee = _rng.Next(1, 4);
            _expected = PuttrAnalysis.ExpectedEntryAngle(_feet, _tee);
            ShowWheel(level);
            if (aloud) Arcade.Audio.Voice("ml_distance_" + _feet + "_feet", "ml_tee_" + TeeWord[_tee - 1]);
        }

        public override IEnumerator Prepare()
        {
            if (_over || _runs == null) yield break;
            if (!_opened)
            {
                // Let the rule be read, and the announcer welcome everyone.
                _opened = true;
                Arcade.Audio.Voice(Arcade.Audio.VoiceOne("ml_start_", 5));
                Arcade.Audio.Organ();
                yield return new WaitForSeconds(2.2f);
            }
            // Puttr AI bats for itself.
            while (!_over && PlayerOf(_side) < 0) yield return RunPuttrAi();
            if (_over) yield break;

            bool fresh = _newHalf;
            if (fresh) yield return Up();
            ResetField();
            Deal(true);
            if (!fresh && _rng.NextDouble() < 0.25) Arcade.Audio.Organ();
        }

        public override string Prompt
        {
            get
            {
                if (_runs == null) return "";
                return SideName(_side) + " at bat   ·   inning " + (_inning + 1) + " of " + Innings + "   ·   " + (_outs == 0 ? "no outs" : _outs == 1 ? "1 out" : "2 outs");
            }
        }

        public override IEnumerator Play(PuttResult putt)
        {
            int level = Arcade.Level;
            bool holed = putt.Made;
            // How far off the line of a straight putt from this tee it went in.
            float off = holed && putt.HasEntry ? putt.EntryAngle - _expected : 0f;
            int hit = Resolve(holed, off, level);
            Mark(putt, off, hit);

            // A putt that went in left of the line is pulled to left field.
            float spray = hit == Homer ? Random.Range(-30f, 30f) : Mathf.Clamp(off * 1.6f, -34f, 34f) + Random.Range(-7f, 7f);
            yield return AtBat(hit, !holed, spray, false, putt);
            Arcade.Shot("result");
            yield return new WaitForSeconds(hit == Homer ? 1.4f : 0.8f);
            yield return After(false);
        }

        // Who is up: the colors change over, the board says so, and the announcer calls them.
        IEnumerator Up()
        {
            _newHalf = false;
            _batted[_side, _inning] = true;
            Colors();
            ResetField();
            Show();
            Board();
            // Let the announcer finish with the last side first.
            for (float t = 0f; t < 3.5f && Arcade.Audio.VoiceBusy; t += Time.deltaTime) yield return null;
            int p = PlayerOf(_side);
            Arcade.Hud.Banner(SideName(_side).ToUpperInvariant(), "Now batting   ·   inning " + (_inning + 1) + " of " + Innings, SideInk(_side), 1.5f);
            Arcade.Audio.Voice(p < 0 ? Arcade.Audio.VoiceOne("ml_ai_up_", 3) : _ai ? Arcade.Audio.VoiceOne("ml_you_up_", 3) : "ml_batter_" + (p + 1));
            if (_side == 0 && _inning > 0) Arcade.Audio.Organ();
            yield return new WaitForSeconds(p < 0 ? 0.9f : 0.5f);
        }

        // Puttr AI's half of the inning: its at-bats play out by themselves, quickly, until
        // three are out.
        IEnumerator RunPuttrAi()
        {
            yield return Up();
            Arcade.Hud.SetPrompt("Puttr is batting");
            while (!_over && !_newHalf)
            {
                ResetField();
                yield return new WaitForSeconds(0.25f);
                int hit = AiHit();
                yield return AtBat(hit, hit == Out && _rng.NextDouble() < 0.5, Random.Range(-34f, 34f), true, null);
                Arcade.Shot("ai");
                yield return new WaitForSeconds(hit == Homer ? 1f : 0.45f);
                yield return After(true);
            }
        }

        // What Puttr AI does with an at-bat: out, single, double, triple or home run, in
        // chances out of a hundred. Played over three innings these come to about 3 runs a
        // game against an Easy Player, 3.3 on Medium and 3.8 on Hard.
        int AiHit()
        {
            int level = Arcade.Level;
            int[] odds = level == Arcade.Easy ? new[] { 59, 23, 10, 3, 5 } : level == Arcade.Medium ? new[] { 58, 23, 10, 3, 6 } : new[] { 56, 24, 10, 4, 6 };
            int roll = _rng.Next(100);
            for (int hit = Out; hit <= Homer; hit++) { if (roll < odds[hit]) return hit; roll -= odds[hit]; }
            return Out;
        }

        // After a play: three outs retire the side, and the last side of the last inning ends the game.
        IEnumerator After(bool quick)
        {
            if (_outs < 3) yield break;
            yield return new WaitForSeconds(quick ? 0.5f : 0.7f);
            for (int b = 0; b < 3; b++) _bases[b] = false;
            SetRunners();
            _outs = 0;
            bool last = _side == _sides - 1;
            GameAudio audio = Arcade.Audio;
            if (last && _inning == Innings - 1)
            {
                _over = true;
                int won = Winner();
                string call = won < 0 ? audio.VoiceOne("ml_tie_", 2)
                    : !_ai ? "ml_win_" + (won + 1)
                    : won == 1 ? audio.VoiceOne("ml_win_you_", 2) : audio.VoiceOne("ml_win_ai_", 2);
                audio.Voice(audio.VoiceOne("ml_gameover_", 3), call);
            }
            else if (last)
            {
                _side = 0;
                _inning++;
                audio.Voice(_inning == Innings - 1 ? audio.VoiceOne("ml_final_inning_", 2) : audio.VoiceOne("ml_inning_", 6));
                _newHalf = true;
            }
            else
            {
                _side++;
                _newHalf = true;
            }
            Show();
            Board();
        }

        public override bool Over => _over;

        public override void Final(out string title, out string body)
        {
            title = "F I N A L";
            body = "";
            if (_runs == null) return;
            for (int s = 0; s < _sides; s++) body += SideName(s) + "     " + _runs[s] + (_runs[s] == 1 ? " run" : " runs") + "\n";
        }

        // Against Puttr AI the card at the end shows both sides, not a score for the table.
        public override Hud.Line[] Results(out string headline)
        {
            headline = null;
            if (!_ai || _runs == null) return null;
            int won = Winner();
            headline = won < 0 ? "A draw" : won == 1 ? "You win" : "Puttr wins";
            var lines = new Hud.Line[2];
            for (int s = 0; s < 2; s++)
                lines[s] = new Hud.Line
                {
                    Name = SideName(s),
                    Value = _runs[s] + (_runs[s] == 1 ? " run" : " runs") + "   ·   " + _hits[s] + (_hits[s] == 1 ? " hit" : " hits"),
                    Dot = SideInk(s), Lit = s == won, Tag = s == won ? "WINNER" : "",
                };
            return lines;
        }

        public override void End()
        {
            if (_ball == null) return;
            HideBall();
            ClearEffects();
            _pitchAt = -1f;
            _swingAt = -1f;
            _chase = -1;
            _glove = -1;
            foreach (Runner r in _runners) r.Running = false;
        }

        // ── An at-bat ───────────────────────────────────────────────────────

        // The pitch, the swing, and what becomes of the ball. quick is Puttr AI's at-bat, at
        // better than twice the speed. putt is null when nobody putted.
        IEnumerator AtBat(int hit, bool missed, float spray, bool quick, PuttResult putt)
        {
            float k = quick ? 0.45f : 1f;
            GameAudio audio = Arcade.Audio;

            // The pitch comes in, and the batter swings as it gets there.
            float wind = 0.8f * k, flight = 0.44f * k;
            _pitchAt = Time.time;
            _pitchLen = wind / 0.7f;   // the ball leaves the hand at seven tenths of the motion
            yield return new WaitForSeconds(wind);
            Vector3 hand = Root.InverseTransformPoint(_pitcher.Hand[0].position);   // out of the hand, wherever the arm has got to
            bool swung = false;
            for (float t = 0f; t < 1f; t += Time.deltaTime / flight)
            {
                MoveBall(Vector3.Lerp(hand, Contact, t) + Vector3.up * 40f * t * (1f - t));
                if (!swung && (1f - t) * flight < SwingLead)
                {
                    swung = true;
                    _swingAt = Time.time;
                    if (missed) audio.Swing();
                }
                yield return null;
            }
            if (!swung) _swingAt = Time.time;

            if (missed)
            {
                // Swing and a miss: into the catcher's mitt.
                Vector3 mitt = Root.InverseTransformPoint(_catcher.Hand[1].position);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.07f) { MoveBall(Vector3.Lerp(Contact, mitt, t)); yield return null; }
                MoveBall(mitt);
                audio.Mitt();
                Call(Out, true, "Swing and a miss", putt, quick);
                yield return new WaitForSeconds(0.3f * k);
                HideBall();
                yield break;
            }

            audio.BatCrack(hit == Out ? 0.3f : hit * 0.25f);
            Arcade.Shake(hit == Homer ? 1.2f : 0.5f);
            _tail.Clear();
            _follow = true;   // the camera goes with it

            if (hit == Out) yield return FieldOut(k, putt, quick);
            else if (hit == Homer) yield return GoneDeep(spray, k, putt, quick);
            else yield return BaseHit(hit, spray, k, putt, quick);
        }

        // Holed, but not straight enough: a pop-up that somebody waits under, or a grounder
        // picked up and thrown to first.
        IEnumerator FieldOut(float k, PuttResult putt, bool quick)
        {
            GameAudio audio = Arcade.Audio;
            if (_rng.NextDouble() < 0.55)
            {
                int f = _rng.Next(_fielders.Length);
                bool deep = f >= 4;
                Vector3 under = Spots[f] + new Vector3(Random.Range(-50f, 50f), 0f, Random.Range(-50f, 50f));
                Vector3 glove = under + Vector3.up * GloveHigh;
                Chase(f, under, 0.9f * k);
                float seconds = (deep ? 2.3f : 1.8f) * k, high = deep ? 430f : 300f;   // kept low enough to stay in the picture
                bool ready = false;
                for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
                {
                    MoveBall(Vector3.Lerp(Contact, glove, t) + Vector3.up * high * 4f * t * (1f - t));
                    if (!ready && t > 0.6f) { ready = true; Reach(f); }
                    yield return null;
                }
                MoveBall(glove);
                audio.Mitt();
                Call(Out, false, deep ? "Caught in the outfield" : "Popped up", putt, quick);
            }
            else
            {
                int f = _rng.Next(4);
                Vector3 at = Spots[f], pick = at + Vector3.up * 38f, bounce = new Vector3(at.x * 0.5f, BallSize * 0.5f, at.z * 0.5f);
                Scoop(_fielders[f]);
                yield return Fly(Contact, bounce, 34f, 0.28f * k);
                yield return Fly(bounce, pick, 26f, 0.3f * k);
                audio.Mitt();
                if (f != 0)
                {
                    // Across the diamond to first.
                    Ready(_fielders[f]);
                    Reach(0);
                    yield return Fly(at + Vector3.up * 100f, Spots[0] + Vector3.up * GloveHigh, 40f, 0.4f * k);
                    audio.Mitt();
                }
                Call(Out, false, f == 0 ? "Out at first" : "Thrown out at first", putt, quick);
            }
            yield return new WaitForSeconds(0.3f * k);
            HideBall();
        }

        // A single goes through the infield, a double into the outfield, a triple to the wall.
        // The runners go as the ball does, and the nearest outfielder runs it down.
        IEnumerator BaseHit(int hit, float spray, float k, PuttResult putt, bool quick)
        {
            float a = Mathf.Clamp(spray, -40f, 40f);
            if (hit == Base1)
            {
                // Through one of the gaps: up the middle, or between two infielders.
                a = (a < -11f ? -23f : a > 11f ? 23f : 0f) + Random.Range(-3f, 3f);
            }
            float lands = hit == Base1 ? 600f : hit == Base2 ? 1230f : Wall(a) - 330f;
            float stops = hit == Base1 ? 1020f : hit == Base2 ? 1420f : Wall(a) - 60f;
            float high = hit == Base1 ? 34f : hit == Base2 ? 210f : 340f;
            float first = (hit == Base1 ? 0.5f : hit == Base2 ? 1.2f : 1.6f) * k, second = (hit == Base1 ? 0.75f : 0.55f) * k;
            float legs = (hit == Base1 ? 1.3f : hit == Base2 ? 2.2f : 3f) * k;   // how long the runners take
            Vector3 land = Dir(a) * lands + Vector3.up * BallSize * 0.5f, stop = Dir(a) * stops + Vector3.up * BallSize * 0.5f;

            int f = 4;
            for (int i = 5; i < _fielders.Length; i++) if ((Spots[i] - stop).sqrMagnitude < (Spots[f] - stop).sqrMagnitude) f = i;
            Chase(f, stop - Dir(a) * 36f - Vector3.up * BallSize * 0.5f, first + second + 0.1f);

            bool away = false;
            for (float t = 0f; t < 1f; t += Time.deltaTime / first)
            {
                MoveBall(Vector3.Lerp(Contact, land, t) + Vector3.up * high * 4f * t * (1f - t));
                if (!away && t * first > 0.14f * k) { away = true; StartRunners(hit, legs); }
                yield return null;
            }
            if (!away) StartRunners(hit, legs);
            MoveBall(land);
            Call(hit, false, null, putt, quick);
            Arcade.Shot("action");
            // It skips on across the grass.
            for (float t = 0f; t < 1f; t += Time.deltaTime / second)
            {
                MoveBall(Vector3.Lerp(land, stop, 1f - (1f - t) * (1f - t)) + Vector3.up * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * high * 0.14f * (1f - t));
                yield return null;
            }
            MoveBall(stop + Vector3.up * 70f);   // picked up
            _follow = false;                     // and back to the runners
            for (float t = 0f; t < 8f && AnyRunning(); t += Time.deltaTime) yield return null;
            HideBall();
        }

        // A home run: a high arc over the wall and into the seats, and everybody trots home.
        IEnumerator GoneDeep(float spray, float k, PuttResult putt, bool quick)
        {
            float a = Mathf.Clamp(spray, -40f, 40f);
            float off = Random.Range(130f, 400f);
            Vector3 land = Dir(a) * (Wall(a) + off) + Vector3.up * (Mathf.Lerp(LowY0, LowY1, (off - LowOff0) / (LowOff1 - LowOff0)) + 14f);
            float seconds = 2.6f * k;
            bool away = false, called = false;
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                MoveBall(Vector3.Lerp(Contact, land, t) + Vector3.up * 480f * 4f * t * (1f - t));
                if (!away && t * seconds > 0.14f * k) { away = true; StartRunners(Homer, 3.6f * k); }
                if (!called && t > 0.55f)
                {
                    // It is going, and everyone can see it.
                    called = true;
                    Call(Homer, false, null, putt, quick);
                    if (!quick) Arcade.SlowMotion(1.1f);
                    _showUntil = Time.time + (quick ? 1.3f : 3.2f);
                    _nextBurst = Time.time;
                    Arcade.Shot("action");
                }
                yield return null;
            }
            if (!away) StartRunners(Homer, 3.6f * k);
            HideBall();
            _buzz = 3f;
            // Confetti across the lens for a Player's home run.
            if (putt != null)
            {
                Vector3 ahead = (Aim - Eye).normalized;
                Vector3 near = Eye + ahead * 90f - Vector3.up * 16f;
                Arcade.Celebrate(150, At(near.x, near.y, near.z));
            }
            for (float t = 0f; t < 8f && AnyRunning(); t += Time.deltaTime) yield return null;
        }

        // The play is made: count it, and tell everyone. how is the kind of out.
        void Call(int hit, bool missed, string how, PuttResult putt, bool quick)
        {
            Hud hud = Arcade.Hud;
            GameAudio audio = Arcade.Audio;
            int side = _side;
            bool player = PlayerOf(side) >= 0;
            string word;
            if (hit == Out)
            {
                _outs++;
                string count = _outs >= 3 ? "Three outs" : _outs == 1 ? "One out" : "Two outs";
                hud.Banner(missed ? "STRIKE THREE" : "OUT", how + "   ·   " + count, Make.Red, quick ? 1.1f : 1.8f);
                if (player) audio.Groan();
                Say(!quick, missed ? audio.VoiceOne("ml_strikeout_", 10) : audio.VoiceOne("ml_fieldout_", 20), _outs < 3 ? "ml_outs_" + _outs : null);
                word = missed ? "Strikeout" : "Out";
            }
            else
            {
                bool full = Loaded;
                int runs = Advance(hit);
                _line[side, _inning] += runs;
                _runs[side] += runs;
                _hits[side]++;
                bool slam = hit == Homer && runs == 4;
                string standing = OnBase();
                string sub = runs == 0 ? standing : (runs == 1 ? "1 run scores" : runs + " runs score") + (standing.Length > 0 ? "   ·   " + standing : "");
                if (hit == Homer && runs == 1) sub = "Solo shot   ·   1 run scores";
                hud.Banner(slam ? "GRAND SLAM!" : HitShout[hit], sub, HitInk[hit], quick ? 1.3f : slam ? 3.4f : hit == Homer ? 2.8f : 2f);
                audio.Roar(hit == Homer);
                if (slam) audio.Bell();
                string then = runs >= 4 ? audio.VoiceOne("ml_grandslam_", 3) : runs > 0 ? "ml_runs_" + runs : Loaded && !full ? audio.VoiceOne("ml_loaded_", 2) : null;
                Say(!quick || hit == Homer, audio.VoiceOne(HitVoice[hit], 8), then);
                _buzz = Mathf.Max(_buzz, hit == Homer ? 3f : 1f);
                word = slam ? "Grand slam" : HitName[hit];
            }
            if (putt != null) ShowPutt(putt, word, HitInk[hit]);
            Show();
            Board();
        }

        // The announcer. Puttr AI's at-bats come too fast to call every one: those are only
        // called when nothing else is being said.
        void Say(bool always, params string[] lines)
        {
            if (!always && Arcade.Audio.VoiceBusy) return;
            Arcade.Audio.Voice(lines);
        }

        // ── The ball ────────────────────────────────────────────────────────

        void MoveBall(Vector3 at)
        {
            if (!_ball.gameObject.activeSelf) { _ball.gameObject.SetActive(true); _tail.Clear(); }
            _ball.localPosition = at;
            // Life size at the plate, and drawn bigger the further off it is, so it can
            // still be seen against the seats.
            float size = Mathf.Clamp((at - Eye).magnitude * 0.0085f, 6.5f, 22f);
            _ball.localScale = Vector3.one * size;
            _trail.widthMultiplier = size * 0.6f;
            _tail.Insert(0, at);
            if (_tail.Count > 14) _tail.RemoveAt(_tail.Count - 1);
            _trail.positionCount = _tail.Count;
            for (int i = 0; i < _tail.Count; i++) _trail.SetPosition(i, _tail[i]);
        }

        void HideBall()
        {
            _ball.gameObject.SetActive(false);
            _follow = false;
            _tail.Clear();
            _trail.positionCount = 0;
        }

        // From one place to another in an arc.
        IEnumerator Fly(Vector3 from, Vector3 to, float high, float seconds)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                MoveBall(Vector3.Lerp(from, to, t) + Vector3.up * high * 4f * t * (1f - t));
                yield return null;
            }
            MoveBall(to);
        }

        // ── The figures ─────────────────────────────────────────────────────

        // Stand a figure on the ground, facing a way. drop lowers the hips for a crouch.
        static void Place(Ballplayer figure, Vector3 ground, Vector3 facing, float drop)
        {
            figure.Root.localPosition = ground + Vector3.up * (Ballplayer.HipHeight - drop) * figure.Root.localScale.y;
            figure.Shadow.localPosition = new Vector3(ground.x, ground.y + 0.8f, ground.z);
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.001f) figure.Root.localRotation = Quaternion.LookRotation(-facing); // a figure faces down its own -Z
        }

        static Quaternion Bend(float degrees) => Quaternion.Euler(degrees, 0f, 0f);

        static void Rest(Ballplayer f)
        {
            f.Chest.localRotation = f.Head.localRotation = Quaternion.identity;
            for (int i = 0; i < 2; i++) f.Arm[i].localRotation = f.Fore[i].localRotation = f.Leg[i].localRotation = f.Shin[i].localRotation = Quaternion.identity;
        }

        // On the toes, knees bent, hands out in front: a fielder waiting on the pitch.
        static void Ready(Ballplayer f)
        {
            Rest(f);
            f.Chest.localRotation = Bend(-22f);
            f.Head.localRotation = Bend(14f);
            for (int i = 0; i < 2; i++)
            {
                float out_ = i == 0 ? -1f : 1f;
                f.Leg[i].localRotation = Quaternion.Euler(26f, 0f, 10f * out_);
                f.Shin[i].localRotation = Bend(-44f);
                f.Arm[i].localRotation = Quaternion.Euler(36f, 0f, 5f * out_);
                f.Fore[i].localRotation = Bend(34f);
            }
        }

        // Down for a ground ball.
        static void Scoop(Ballplayer f)
        {
            Rest(f);
            f.Chest.localRotation = Bend(-52f);
            f.Head.localRotation = Bend(26f);
            for (int i = 0; i < 2; i++)
            {
                f.Leg[i].localRotation = Quaternion.Euler(48f, 0f, i == 0 ? -12f : 12f);
                f.Shin[i].localRotation = Bend(-70f);
                f.Arm[i].localRotation = Bend(42f);
            }
        }

        // A runner holding a base.
        static void Lead(Ballplayer f)
        {
            Rest(f);
            f.Chest.localRotation = Bend(-10f);
            for (int i = 0; i < 2; i++)
            {
                f.Leg[i].localRotation = Quaternion.Euler(12f, 0f, i == 0 ? -8f : 8f);
                f.Shin[i].localRotation = Bend(-22f);
                f.Arm[i].localRotation = Bend(10f);
                f.Fore[i].localRotation = Bend(18f);
            }
        }

        // One moment of a run. phase is in radians, a full turn for two steps.
        static void Sprint(Ballplayer f, float phase)
        {
            f.Head.localRotation = Bend(8f);
            f.Chest.localRotation = Bend(-16f);
            for (int i = 0; i < 2; i++)
            {
                float s = Mathf.Sin(phase + i * Mathf.PI);
                f.Leg[i].localRotation = Bend(46f * s);
                f.Shin[i].localRotation = Bend(-(25f + 45f * (0.5f - 0.5f * s)));   // the leg folds as it trails
                f.Arm[i].localRotation = Bend(-38f * s);
                f.Fore[i].localRotation = Bend(70f);
            }
        }

        // A fielder with the glove up, under a ball.
        void Reach(int fielder)
        {
            _glove = fielder;
            Ballplayer f = _fielders[fielder];
            Rest(f);
            f.Head.localRotation = Bend(18f);
            f.Arm[1].localRotation = Bend(168f);
            f.Arm[0].localRotation = Bend(24f);
        }

        // Send a fielder running to a spot, to be there in this many seconds.
        void Chase(int fielder, Vector3 to, float seconds)
        {
            _chase = fielder;
            _chaseFrom = Spots[fielder];
            _chaseTo = to;
            _chaseAt = Time.time;
            _chaseLen = Mathf.Max(0.05f, seconds);
        }

        // Everyone back to where they stand for a pitch, and a batter in the box.
        void ResetField()
        {
            _pitchAt = -1f;
            _swingAt = -1f;
            _chase = -1;
            _glove = -1;
            for (int i = 0; i < _fielders.Length; i++)
            {
                Place(_fielders[i], Spots[i], -Spots[i], 4.2f);
                Ready(_fielders[i]);
            }

            // The catcher: down on his haunches with his knees wide, the mitt held out as a
            // target and the bare hand tucked away behind him.
            Place(_catcher, CatcherAt, Vector3.forward, 22f);
            Rest(_catcher);
            _catcher.Chest.localRotation = Bend(-24f);
            _catcher.Head.localRotation = Bend(18f);
            for (int i = 0; i < 2; i++)
            {
                _catcher.Leg[i].localRotation = Quaternion.Euler(92f, 0f, i == 0 ? -36f : 36f);
                _catcher.Shin[i].localRotation = Bend(-128f);
            }
            _catcher.Arm[1].localRotation = Quaternion.Euler(84f, 0f, 20f);
            _catcher.Fore[1].localRotation = Bend(16f);
            _catcher.Arm[0].localRotation = Quaternion.Euler(-26f, 0f, -6f);
            _catcher.Fore[0].localRotation = Bend(58f);

            _batter.Visible = true;
            Place(_batter, BatterAt, Vector3.right, 3.6f);
            PoseBatter(Time.time);
            PosePitcher(Time.time);
            HideBall();
        }

        // The side at bat in its own color. Against the visiting team the fielders wear the
        // other side's; among Players they are in gray, since everyone bats for themselves.
        void Colors()
        {
            Color bat = SideInk(_side);
            _batTop.SetColor("_BaseColor", bat);
            _batCap.SetColor("_BaseColor", Color.Lerp(bat, Color.black, 0.5f));
            Color field = _ai ? SideInk(1 - _side) : new Color(0.8f, 0.82f, 0.85f);
            _fieldTop.SetColor("_BaseColor", field);
            _fieldCap.SetColor("_BaseColor", _ai ? Color.Lerp(field, Color.black, 0.5f) : new Color(0.07f, 0.1f, 0.22f));
        }

        // Bring a hand to a point, by the shoulder and the elbow. to is in the chest's own
        // space; the elbow goes to the side that pole points to.
        static void Grip(Ballplayer f, int arm, Vector3 to, Vector3 pole)
        {
            const float upper = 12f, lower = 12.6f;
            Vector3 reach = to - f.Arm[arm].localPosition;
            float far = Mathf.Clamp(reach.magnitude, 3f, upper + lower - 0.3f);
            Vector3 dir = reach.normalized;
            Vector3 side = pole - dir * Vector3.Dot(pole, dir);
            side = side.sqrMagnitude < 0.0001f ? Vector3.Cross(dir, Vector3.right).normalized : side.normalized;
            float cos = Mathf.Clamp((upper * upper + far * far - lower * lower) / (2f * upper * far), -1f, 1f), sin = Mathf.Sqrt(1f - cos * cos);
            Vector3 elbow = (dir * cos + side * sin) * upper;
            Vector3 down = elbow.normalized, fore = (dir * far - elbow).normalized;
            Vector3 ahead = fore - down * Vector3.Dot(fore, down);
            if (ahead.sqrMagnitude < 0.0001f) { f.Arm[arm].localRotation = Quaternion.FromToRotation(Vector3.down, down); f.Fore[arm].localRotation = Quaternion.identity; return; }
            // A limb hangs down its own -Y, and its joint folds it toward its own -Z.
            f.Arm[arm].localRotation = Quaternion.LookRotation(-ahead.normalized, -down);
            f.Fore[arm].localRotation = Bend(Vector3.Angle(down, fore));
        }

        // The batter: side on, knees bent, the bat cocked up behind the back shoulder in both
        // hands; then the hips and shoulders come round and the bat goes through the ball
        // and on over the front shoulder.
        void PoseBatter(float now)
        {
            if (!_batter.Visible) return;
            Ballplayer f = _batter;
            bool waiting = _swingAt < 0f;
            float u = waiting ? 0f : Mathf.Clamp01((now - _swingAt) / SwingTime), e = Ease(u);
            float sway = waiting ? Mathf.Sin(now * 2.4f) : 0f;

            // The bat's way round, seen from above: 0 points back at the catcher, 90 over the plate.
            float around = (Mathf.Lerp(-58f, 232f, e) + sway * 4f) * Mathf.Deg2Rad;
            float lift = u < 0.45f ? Mathf.Lerp(1.9f, -0.1f, u / 0.45f) : Mathf.Lerp(-0.1f, 0.6f, (u - 0.45f) / 0.55f);
            Vector3 along = new Vector3(-Mathf.Cos(around), lift, -Mathf.Sin(around)).normalized;
            // The hands: by the back shoulder, out in front at the ball, and up by the front shoulder.
            Vector3 back = new Vector3(-7.5f, 21.5f + sway * 0.4f, -5f), meet = new Vector3(4.5f, 9f, -12.5f), finish = new Vector3(8f, 21f, -3.5f);
            Vector3 hands = e < 0.5f ? Vector3.Lerp(back, meet, Ease(e * 2f)) : Vector3.Lerp(meet, finish, Ease(e * 2f - 1f));
            _bat.localPosition = hands;
            _bat.localRotation = Quaternion.FromToRotation(Vector3.up, along);

            float turn = Mathf.Lerp(22f, -76f, e);
            f.Chest.localRotation = Quaternion.Euler(-9f, turn, 0f);
            f.Head.localRotation = Quaternion.Euler(4f, Mathf.Clamp(-74f - turn, -84f, 84f), 0f);   // eyes on the pitcher
            Quaternion toChest = Quaternion.Inverse(f.Chest.localRotation);
            Vector3 origin = f.Chest.localPosition;
            Grip(f, 1, toChest * (hands + along * 1.6f - origin), new Vector3(0.5f, -1f, 0.2f));
            Grip(f, 0, toChest * (hands + along * 5f - origin), new Vector3(-0.6f, -1f, 0.3f));

            // The front leg braces and the back knee drives in.
            f.Leg[1].localRotation = Quaternion.Euler(Mathf.Lerp(17f, 7f, e), 0f, Mathf.Lerp(15f, 21f, e));
            f.Shin[1].localRotation = Bend(Mathf.Lerp(-32f, -9f, e));
            f.Leg[0].localRotation = Quaternion.Euler(Mathf.Lerp(17f, 30f, e), 0f, Mathf.Lerp(-15f, -3f, e));
            f.Shin[0].localRotation = Bend(Mathf.Lerp(-32f, -52f, e));
        }

        // The pitcher, a right-hander: set with his hands together; the front leg comes up as
        // he turns side on and the arm goes back; then the stride down the mound, the arm over
        // the top, and the follow-through with the back leg coming round.
        void PosePitcher(float now)
        {
            Ballplayer f = _pitcher;
            float t = _pitchAt < 0f ? 0f : Mathf.Clamp01((now - _pitchAt) / _pitchLen);
            float lift = Smooth(0f, 0.4f, t), whip = Smooth(0.46f, 0.72f, t), settle = Smooth(0.72f, 1f, t);
            float turn = 76f * lift * (1f - whip) - 24f * settle;
            Vector3 ground = Mound + Vector3.up * MoundTop + Vector3.back * (34f * whip);
            f.Root.localPosition = ground + Vector3.up * (Ballplayer.HipHeight - 5.6f * whip) * Tall;
            f.Root.localRotation = Quaternion.Euler(0f, turn, 0f);
            f.Shadow.localPosition = ground + Vector3.up * 0.8f;
            f.Head.localRotation = Quaternion.Euler(0f, -turn * 0.9f, 0f);
            f.Chest.localRotation = Bend(Mathf.Lerp(Mathf.Lerp(0f, 7f, lift), Mathf.Lerp(-32f, -50f, settle), whip));

            f.Leg[1].localRotation = Quaternion.Euler(Mathf.Lerp(84f * lift, Mathf.Lerp(40f, 46f, settle), whip), 0f, 6f * (1f - lift));
            f.Shin[1].localRotation = Bend(Mathf.Lerp(-100f * lift, Mathf.Lerp(-28f, -40f, settle), whip));
            f.Leg[0].localRotation = Quaternion.Euler(Mathf.Lerp(0f, Mathf.Lerp(-28f, -50f, settle), whip), 0f, -6f * (1f - lift));
            f.Shin[0].localRotation = Bend(Mathf.Lerp(0f, Mathf.Lerp(-14f, -70f, settle), whip));

            Quaternion set = Quaternion.Euler(42f, 0f, 20f), back = Quaternion.Euler(-55f, 0f, -58f), over = Quaternion.Euler(158f, 0f, -22f), down = Quaternion.Euler(62f, 0f, 34f);
            f.Arm[0].localRotation = Quaternion.Slerp(Quaternion.Slerp(set, back, lift), Quaternion.Slerp(over, down, settle), whip);
            f.Fore[0].localRotation = Bend(Mathf.Lerp(Mathf.Lerp(100f, 70f, lift), Mathf.Lerp(14f, 26f, settle), whip));
            Quaternion glove = Quaternion.Euler(42f, 0f, -20f), lead = Quaternion.Euler(78f, 0f, 12f), tuck = Quaternion.Euler(18f, 0f, 22f);
            f.Arm[1].localRotation = Quaternion.Slerp(Quaternion.Slerp(glove, lead, lift), tuck, whip);
            f.Fore[1].localRotation = Bend(Mathf.Lerp(Mathf.Lerp(100f, 35f, lift), 118f, whip));
        }

        // ── Runners ─────────────────────────────────────────────────────────

        // Put a runner on every base that has one, standing.
        void SetRunners()
        {
            int slot = 0;
            foreach (Runner r in _runners) { r.On = r.Running = false; r.Body.Visible = false; }
            for (int b = 0; b < 3; b++)
            {
                if (!_bases[b]) continue;
                Runner r = _runners[slot++];
                r.On = true;
                r.At = b + 1;
                r.Body.Visible = true;
                Hold(r);
            }
        }

        // Standing a step off the bag, toward the next one.
        static void Hold(Runner r)
        {
            int at = Mathf.Clamp(Mathf.RoundToInt(r.At), 1, 3);
            Vector3 next = Bag(at + 1) - Bag(at);
            Place(r.Body, Bag(at) + next * 0.06f, next, 2f);
            Lead(r.Body);
        }

        // Everyone on base, and the batter, sets off round the bases: so many bases in so many seconds.
        void StartRunners(int bases, float seconds)
        {
            float now = Time.time, speed = bases / Mathf.Max(0.05f, seconds);
            foreach (Runner r in _runners)
            {
                if (!r.On) continue;
                r.From = r.At; r.To = r.At + bases; r.Start = now; r.Speed = speed; r.Running = true;
            }
            // The batter drops the bat and goes: the first figure that is not on a base.
            foreach (Runner r in _runners)
            {
                if (r.On) continue;
                r.On = r.Running = true;
                r.At = r.From = 0f; r.To = bases; r.Start = now; r.Speed = speed;
                r.Body.Visible = true;
                break;
            }
            _batter.Visible = false;
            MoveRunners(now);
        }

        bool AnyRunning()
        {
            foreach (Runner r in _runners) if (r.Running) return true;
            return false;
        }

        void MoveRunners(float now)
        {
            foreach (Runner r in _runners)
            {
                if (!r.On || !r.Running) continue;
                float end = Mathf.Min(r.To, 4f);
                float at = r.From + (now - r.Start) * r.Speed;
                if (at >= end)
                {
                    r.Running = false;
                    r.At = end;
                    // Home: the run is in, and the runner is off to the dugout.
                    if (end >= 4f) { r.On = false; r.Body.Visible = false; }
                    else Hold(r);
                    continue;
                }
                r.At = at;
                int leg = Mathf.Clamp(Mathf.FloorToInt(at), 0, 3);
                Vector3 from = Bag(leg), to = Bag(leg + 1);
                Place(r.Body, Vector3.Lerp(from, to, at - leg), to - from, 0f);
                Sprint(r.Body, at * BaseGap / Stride);
            }
        }

        // ── Every frame ─────────────────────────────────────────────────────

        public override void Tick()
        {
            if (_batter == null) return;
            float now = Time.time, dt = Time.deltaTime;
            PoseBatter(now);
            PosePitcher(now);
            MoveRunners(now);

            // A fielder on the run.
            if (_chase >= 0)
            {
                Ballplayer f = _fielders[_chase];
                float u = Mathf.Clamp01((now - _chaseAt) / _chaseLen);
                Vector3 at = Vector3.Lerp(_chaseFrom, _chaseTo, Ease(u));
                if (u < 1f)
                {
                    Place(f, at, _chaseTo - _chaseFrom, 0f);
                    Sprint(f, u * (_chaseTo - _chaseFrom).magnitude / Stride);
                }
                else
                {
                    Place(f, at, -at, 4f);
                    if (_glove != _chase) Ready(f);
                    _chase = -1;
                }
            }

            // The marker on the wheel lands with a bump, then breathes.
            if (_marker != null && _marker.gameObject.activeSelf)
                _marker.localScale = Vector3.one * (1f + 0.9f * Mathf.Clamp01(1f - (now - _markAt) / 0.4f) + 0.08f * Mathf.Sin(now * 5f));

            // Cameras in the crowd, more of them after a hit.
            _buzz = Mathf.MoveTowards(_buzz, 0f, dt * 0.8f);
            for (int i = 0; i < _flash.Length; i++)
            {
                if (now > _flashAt[i] + 0.09f) _flashAt[i] = now + Random.Range(0.4f, 7f) / (1f + _buzz * 5f);
                _flash[i].localScale = now >= _flashAt[i] ? Vector3.one * 44f : Vector3.zero;
            }

            // Fireworks over the outfield seats.
            if (now < _showUntil && now >= _nextBurst)
            {
                _nextBurst = now + 0.3f;
                float a = Random.Range(-40f, 40f);
                Burst(Dir(a) * (Wall(a) + Random.Range(200f, 700f)) + Vector3.up * Random.Range(820f, 1080f));
            }
            for (int i = 0; i < _sparks.Length; i++)
            {
                if (_sparks[i].Age >= _sparks[i].Life) continue;
                _sparks[i].Age += dt;
                float k = Mathf.Clamp01(_sparks[i].Age / _sparks[i].Life);
                _sparks[i].V += Vector3.down * 150f * dt;
                _sparks[i].V *= 1f - Mathf.Clamp01(dt * 1.2f);
                _sparks[i].T.localPosition += _sparks[i].V * dt;
                _sparks[i].T.localScale = k >= 1f ? Vector3.zero : Vector3.one * _sparks[i].Size * (1f - k * k);
            }
        }

        // One shell: a ring of sparks in one color, thrown out from a point.
        void Burst(Vector3 at)
        {
            Material ink = _sparkInk[Random.Range(0, _sparkInk.Length)];
            for (int n = 0; n < 24; n++)
            {
                int i = _nextSpark;
                _nextSpark = (_nextSpark + 1) % _sparks.Length;
                _sparks[i].T.GetComponent<MeshRenderer>().sharedMaterial = ink;
                _sparks[i].T.localPosition = at;
                _sparks[i].V = Random.onUnitSphere * Random.Range(190f, 330f);
                _sparks[i].Age = 0f;
                _sparks[i].Life = Random.Range(0.9f, 1.5f);
                _sparks[i].Size = Random.Range(26f, 44f);
            }
        }

        void ClearEffects()
        {
            _showUntil = 0f;
            _buzz = 0f;
            if (_sparks == null) return;
            for (int i = 0; i < _sparks.Length; i++)
            {
                _sparks[i].Age = _sparks[i].Life;
                _sparks[i].T.localScale = Vector3.zero;
            }
        }

        // ── The hit wheel ───────────────────────────────────────────────────

        /// <summary>A direction on the wheel: 0 is the front of the hole (toward the Player, at the bottom), negative is the Player's left.</summary>
        static Vector2 Around(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
        }

        // Part of a ring, between two angles.
        static void Band(List<Vector3> verts, List<int> tris, float r0, float r1, float a0, float a1)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / 4f));
            int first = verts.Count;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 d = Around(Mathf.Lerp(a0, a1, i / (float)steps));
                verts.Add(new Vector3(d.x * r0, d.y * r0, 0f));
                verts.Add(new Vector3(d.x * r1, d.y * r1, 0f));
                if (i == 0) continue;
                int a = first + (i - 1) * 2;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
                tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
            }
        }

        // Draw the wedges for a level, turned so the home run is on the line of a straight
        // putt from the tee that has been dealt.
        void ShowWheel(int level)
        {
            Bands(level, out float homer, out float triple, out float twice, out float last, out bool back);
            // Each kind of hit covers from lo to hi degrees off the line, on both sides of it.
            float[] lo = { last, twice, triple, homer, 0f }, hi = { 180f, last, twice, triple, homer };
            if (!back) { hi[Base1] = 180f; lo[Out] = 180f; }   // on Rookie the back of the cup is still a single
            for (int k = 0; k < _wedge.Length; k++)
            {
                var verts = new List<Vector3>();
                var tris = new List<int>();
                if (hi[k] > lo[k])
                {
                    Band(verts, tris, HoleR + 1.5f, WheelR, _expected + lo[k], _expected + hi[k]);
                    Band(verts, tris, HoleR + 1.5f, WheelR, _expected - hi[k], _expected - lo[k]);
                }
                Mesh mesh = _wedge[k].sharedMesh;
                mesh.Clear();
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
            }
            Vector2 from = Around(_expected) * (WheelR + 4.5f);
            _teeDot.localPosition = new Vector3(from.x, from.y, -0.2f);
            _marker.gameObject.SetActive(false);
            _wheelLevel.text = LevelName[level];
            _wheelSays.text = "STRAIGHTER IS BETTER";
        }

        // Show where the putt went in, and what that made it.
        void Mark(PuttResult putt, float off, int hit)
        {
            bool shown = putt.Made && putt.HasEntry;
            _marker.gameObject.SetActive(shown);
            if (shown)
            {
                Vector2 at = Around(putt.EntryAngle) * ((HoleR + WheelR) * 0.5f);
                _marker.localPosition = new Vector3(at.x, at.y, -0.2f);
                _markAt = Time.time;
            }
            float away = Mathf.Abs(off);
            _wheelSays.text = !putt.Made ? "MISSED THE HOLE   ·   OUT"
                : !putt.HasEntry ? HitName[hit].ToUpperInvariant()
                : (away < 0.5f ? "DEAD STRAIGHT" : away.ToString("0") + "° " + (off < 0f ? "LEFT" : "RIGHT") + " OF THE LINE") + "   ·   " + HitName[hit].ToUpperInvariant();
        }

        // ── What is on show ─────────────────────────────────────────────────

        // The scoreboard in the outfield.
        void Show()
        {
            if (_runs == null || _sbInning == null) return;
            _sbInning.text = (Mathf.Min(_inning, Innings - 1) + 1).ToString();
            for (int i = 0; i < 3; i++)
            {
                _sbOut[i].sharedMaterial = !_over && i < _outs ? _outOn : _lampOff;
                _sbBase[i].sharedMaterial = _bases[i] ? _baseOn : _lampOff;
            }
            var runs = new System.Text.StringBuilder();
            for (int s = 0; s < _sides; s++)
            {
                if (s > 0) runs.Append("      ");
                runs.Append(_sides > 2 ? "P" + (s + 1) : Short(SideName(s)).ToUpperInvariant()).Append("  ").Append(_runs[s]);
            }
            _sbRuns.text = runs.ToString();
            _sbAtBat.text = _over ? "FINAL" : "AT BAT   " + Short(SideName(_side)).ToUpperInvariant();
        }

        // The line score at the top: runs by inning, and the total.
        void Board()
        {
            if (Arcade == null || Arcade.Hud == null || _runs == null) return;
            Hud hud = Arcade.Hud;
            for (int s = 0; s < _sides; s++)
            {
                for (int i = 0; i < Innings; i++) hud.SetCell(s, i, "", _batted[s, i] ? _line[s, i].ToString() : "");
                hud.SetTotal(s, _runs[s]);
            }
            hud.SetTurn(_over ? -1 : _side, _over ? -1 : _inning);
        }

        // ── A ballplayer ────────────────────────────────────────────────────

        /// <summary>
        /// A jointed ballplayer in his kit, built like Props.Figure and posed the same way: he
        /// faces down his own -Z, his origin is the hips, and his limbs hang from their joints
        /// (turn a joint about X to swing a limb forward, about Z to swing it sideways). Arm 0
        /// and leg 0 are his right. Every part of him is round.
        /// </summary>
        sealed class Ballplayer
        {
            public const float HipHeight = 38f, Reach = 47f;
            public const int Fielder = 0, Batter = 1, Catcher = 2;

            public struct Kit
            {
                /// <summary>Trim is the dark of the side's color: cap or helmet, sleeves and socks.</summary>
                public Material Jersey, Trim, Pants, Skin, Shoe, Belt, Leather, Gear, Hair, Blob;
            }

            public readonly Transform Root, Chest, Head, Shadow;
            public readonly Transform[] Arm = new Transform[2], Fore = new Transform[2], Hand = new Transform[2], Leg = new Transform[2], Shin = new Transform[2];

            static Mesh _torso, _upper, _lower, _thigh, _calf;

            /// <summary>On the field or not, and the shadow under him with him.</summary>
            public bool Visible
            {
                get => Root.gameObject.activeSelf;
                set { Root.gameObject.SetActive(value); Shadow.gameObject.SetActive(value); }
            }

            public Ballplayer(Transform parent, Kit kit, int role)
            {
                if (_torso == null)
                {
                    // Waist, ribs, chest, and in over the shoulders to the neck.
                    _torso = Make.Lathe(new List<Vector2>
                    {
                        new Vector2(0.02f, -0.6f), new Vector2(6.5f, -0.4f), new Vector2(6.8f, 3f), new Vector2(7.5f, 9f), new Vector2(8.3f, 14f),
                        new Vector2(8.2f, 17f), new Vector2(7.2f, 19.6f), new Vector2(4.2f, 21.2f), new Vector2(2.4f, 22f), new Vector2(0.02f, 22.3f),
                    }, 24);
                    _upper = Props.Limb(12f, 3f, 2.4f);
                    _lower = Props.Limb(10.5f, 2.35f, 1.8f);
                    _thigh = Props.Limb(17f, 4.5f, 3.3f);
                    _calf = Props.Limb(16.6f, 3.2f, 2.1f);
                }

                Root = Node("Figure", parent, Vector3.zero);
                // A soft shadow on the ground under him, whatever the lights are doing.
                Shadow = Card("Shadow", parent, Vector3.zero, new Vector2(92f, 92f), kit.Blob).transform;
                Shadow.localRotation = Quaternion.Euler(90f, 0f, 0f);

                Round("Hips", Root, new Vector3(0f, -0.6f, 0f), new Vector3(14.6f, 10.6f, 9.8f), kit.Pants);
                Round("Belt", Root, new Vector3(0f, 3.5f, 0f), new Vector3(13.9f, 2.6f, 8.9f), kit.Belt);
                Chest = Node("Chest", Root, new Vector3(0f, 3f, 0f));
                Part(_torso, "Jersey", Chest, new Vector3(0f, 0.6f, 0f), kit.Jersey).localScale = new Vector3(1f, 1f, 0.6f);
                Round("Neck", Chest, new Vector3(0f, 22f, 0f), new Vector3(4.8f, 5.4f, 4.8f), kit.Skin);

                Head = Node("Head", Chest, new Vector3(0f, 23.5f, 0f));
                Round("Face", Head, new Vector3(0f, 4.8f, 0f), new Vector3(8.2f, 10f, 9f), kit.Skin);
                if (role == Fielder)
                {
                    Round("Hair", Head, new Vector3(0f, 4.9f, 1.3f), new Vector3(8.5f, 7f, 8.2f), kit.Hair);
                    Round("Cap", Head, new Vector3(0f, 7.3f, 0.2f), new Vector3(9.3f, 6.8f, 10f), kit.Trim);
                    Round("Bill", Head, new Vector3(0f, 6.2f, -6.2f), new Vector3(7.8f, 1.1f, 7.6f), kit.Trim);
                }
                else if (role == Batter)
                {
                    // A helmet, with the flap over the ear that faces the pitcher.
                    Round("Helmet", Head, new Vector3(0f, 6.4f, 0.3f), new Vector3(10.6f, 9.4f, 11.4f), kit.Trim);
                    Round("Bill", Head, new Vector3(0f, 5.4f, -6.6f), new Vector3(8.2f, 1.1f, 7.4f), kit.Trim);
                    Round("Ear flap", Head, new Vector3(4.5f, 3.4f, 0.2f), new Vector3(3.2f, 7f, 7f), kit.Trim);
                }
                else
                {
                    // The catcher: a helmet with a mask, a chest protector strapped across his
                    // back, and guards down his shins.
                    Round("Helmet", Head, new Vector3(0f, 5.8f, 0.5f), new Vector3(10.4f, 10.4f, 11.2f), kit.Gear);
                    Round("Mask", Head, new Vector3(0f, 4.2f, -4.2f), new Vector3(8.8f, 9.8f, 5.4f), kit.Gear);
                    Round("Protector", Chest, new Vector3(0f, 10f, -2.6f), new Vector3(16.8f, 20.5f, 7.6f), kit.Gear);
                    for (int side = -1; side <= 1; side += 2)
                        Shape(PrimitiveType.Cube, "Strap", Chest, new Vector3(0f, 11.5f, 4.7f), new Vector3(1.9f, 21f, 1f), kit.Gear).transform.localRotation = Quaternion.Euler(0f, 0f, side * 33f);
                    Shape(PrimitiveType.Cube, "Strap", Chest, new Vector3(0f, 5f, 4.2f), new Vector3(14.6f, 1.9f, 1f), kit.Gear);
                }

                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1f : 1f;
                    Arm[i] = Node("Shoulder", Chest, new Vector3(side * 9.3f, 18.2f, 0f));
                    Round("Shoulder cap", Arm[i], Vector3.zero, Vector3.one * 6.3f, kit.Trim);
                    Part(_upper, "Sleeve", Arm[i], Vector3.zero, kit.Trim);
                    Fore[i] = Node("Elbow", Arm[i], new Vector3(0f, -12f, 0f));
                    Part(_lower, "Forearm", Fore[i], Vector3.zero, kit.Skin);
                    Hand[i] = Node("Hand", Fore[i], new Vector3(0f, -12.6f, 0f));
                    if (role == Catcher && i == 1) Round("Mitt", Hand[i], new Vector3(0f, -1.6f, -1f), new Vector3(9.6f, 10.6f, 5.2f), kit.Leather);
                    else if (role == Fielder && i == 1) Round("Glove", Hand[i], new Vector3(0f, -1.3f, -0.6f), new Vector3(6.6f, 8.6f, 4.4f), kit.Leather);
                    else Round("Hand", Hand[i], Vector3.zero, new Vector3(3.7f, 5.3f, 2.7f), role == Batter ? kit.Gear : kit.Skin);

                    Leg[i] = Node("Hip", Root, new Vector3(side * 4.1f, -2f, 0f));
                    Part(_thigh, "Thigh", Leg[i], Vector3.zero, kit.Pants);
                    Shin[i] = Node("Knee", Leg[i], new Vector3(0f, -17f, 0f));
                    Part(_calf, "Calf", Shin[i], Vector3.zero, kit.Pants);
                    Round("Shoe", Shin[i], new Vector3(0f, -17.3f, -2.6f), new Vector3(4.7f, 3.9f, 10.6f), kit.Shoe);
                    if (role != Catcher) continue;
                    Round("Shin guard", Shin[i], new Vector3(0f, -8f, -1.4f), new Vector3(6.3f, 17.5f, 5.5f), kit.Gear);
                    Round("Knee cap", Shin[i], new Vector3(0f, 0.5f, -1.8f), new Vector3(6.5f, 6.5f, 5.7f), kit.Gear);
                }
            }

            static Transform Part(Mesh mesh, string name, Transform parent, Vector3 at, Material material)
            {
                Transform part = Make.MeshObject(name, parent, mesh, material).transform;
                part.localPosition = at;
                return part;
            }

            static void Round(string name, Transform parent, Vector3 at, Vector3 size, Material material) => Shape(PrimitiveType.Sphere, name, parent, at, size, material);
        }
    }
}
