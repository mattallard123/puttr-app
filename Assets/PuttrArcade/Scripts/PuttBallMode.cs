// Putt-ball (Matt, 2026-11-21, rebuilt 2026-11-23): an arcade alley roller, played as real skee-ball. A wooden cabinet, a
// lane as wide as the rings, a hop at the top of it, and a tilted board of nested rings: 10, 20, 30 and 40 from the
// outside in, 50 above them, 100 in both top corners. Nine balls each; most points wins.
//
// The board is the Puttr's own ramp, drawn large (Matt, 2026-11-23): the bullseye is the hole, the top of the board is
// the back of the mat, and the ball is lobbed off the hop to land where the putt went (PuttBallPhysics.Target). From
// there it is simulated: it bounces, hits the ring walls and rolls down the tilted board to a cup.
//   make    lands in the bullseye: 40. Hit too firm it carries past the 40 and falls back into a lower ring.
//   short   lands below the bullseye by how short it was: the 30, the 20, the 10, then the gutter.
//   miss    runs on to the back of the mat: the top of the board, on the putt's own line. Just past the edge of the cup
//           comes down into the 50, and the back corners of the mat are the 100s.
//
// The level only changes the distance dealt: 3 to 5 ft on Easy, 6 to 8 on Medium, 9 to 11 on Hard.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class PuttBallMode : StageMode
    {
        public override string Name => "Putt-ball";
        public override bool Branded => true;
        public override string VoicePrefix => "puttball";
        public override int Putter => Active; // whose level applies to this putt
        public override string Blurb => "Real skee-ball.\nThe bullseye is the hole.\nThe back corners are worth 100.";
        public override bool OwnLevels => true;
        protected override void DealRange(int level, out int nearest, out int furthest) { nearest = 3 + level * 3; furthest = 5 + level * 3; }
        public override string Icon => "Joystick";
        public override Color Accent => Make.Bright;
        public override string[] Tags => new[] { "9 balls", "Line and pace" };
        public override string Rule => "The board is your Puttr's ramp: the ball lands where your putt went. The bullseye is the hole.";
        public override string[] HowTo => new[]
        {
            "Each putt rolls a ball up the lane and off the hop. The board is your Puttr's ramp: the ball lands where your putt went.",
            "The bullseye is the hole: a make is 40. Short of the hole lands in the lower rings, and a make hit too firm carries past the 40 and falls back into a lower ring.",
            "A miss that runs to the back of the mat lands at the top of the board. Just past the edge of the cup finds the 50, and the back corners are worth 100.",
        };
        public override int Score(int player) => _total[player];
        public override string BedSlot => "puttball_arcade_loop";
        public override string EndSlot => "puttball_hundred";
        public override GameAudio.Scene Sound => GameAudio.Scene.Table;

        // The view from behind the Player's end of the cabinet, a little to the left, as in the showroom photo.
        // Upright screens (a phone, a tablet held tall) stand closer and higher with a wider lens, so the whole cabinet
        // still fills the space between the scoreboard and the putt card.
        public override bool HasPortrait => true;
        static bool Upright => Portrait;
        // Centered at the back of the lane; on a putt it follows the ball, then sits square over the rings to see where it
        // went (Matt, 2026-11-23: as Dead Weight does).
        enum View { Lane, Ball, Rings }
        View _view = View.Lane;
        Vector3 _ballAt;
        public override Vector3 CameraPosition =>
            _view == View.Rings ? Vec(OnBoard(0f, 24f, Upright ? 88f : 62f))
            : _view == View.Ball ? At(_ballAt.x * 0.7f, _ballAt.y + (Upright ? 34f : 24f), _ballAt.z - (Upright ? 44f : 36f))
            : Upright ? At(0f, 120f, -52f) : At(0f, 80f, -48f);
        public override Vector3 CameraLook =>
            _view == View.Rings ? Vec(OnBoard(0f, 25f, 0f))
            : _view == View.Ball ? At(_ballAt.x * 0.8f, _ballAt.y + 4f, _ballAt.z + 44f)
            : Upright ? At(0f, 48f, 86f) : At(0f, 42f, 88f);
        Vector3 Vec(Vector3 v) => At(v.x, v.y, v.z);
        public override float CameraFov => _view == View.Rings ? (Upright ? 40f : 42f) : Upright ? 54f : 40f;
        public override Vector3 CardFrom => At(-14f, 72f, 30f);
        public override Vector3 CardLook => At(0f, 46f, 118f);
        public override float CardFov => 40f;
        public override float ShadowDistance => 600f;

        // The ideal-putt card's pace bar: the makes that stay in the 40, from died in up to too firm.
        public override bool AimPace(out float low, out float high, out float min, out float max)
        {
            float d = PuttrAnalysis.RampDecelInPerSec2;
            PuttBallPhysics.FortyWindow(out float near, out float far);
            low = Mathf.Sqrt(2f * d * near);
            high = Mathf.Sqrt(2f * d * far);
            min = 0f; max = Mathf.Sqrt(2f * d * 72f);
            return true;
        }

        const int Balls = 9;
        // Inches. The lane runs from z 0 (the Player's end) to LaneEnd; the hop lifts the ball onto the board.
        const float LaneY = 30f, LaneEnd = 96f, LaneHalf = PuttBallPhysics.Half, RailW = 2.4f, BallR = PuttBallPhysics.BallR;
        const float BoardZ = 100f, BoardY = 33f, Tilt = PuttBallPhysics.TiltDeg, BoardLen = PuttBallPhysics.Length, BoardHalf = PuttBallPhysics.Half;
        // Machines either side of the Player's, down the arcade's row (Matt, 2026-11-22: "other putt-ball machines next to it").
        const float Pitch = 44f;
        static readonly float[] Neighbours = { -3f, -2f, -1f, 1f, 2f };

        // The board, in its own plane: x across, z up the board from its bottom edge. The rings and cups are PuttBallPhysics',
        // so what is drawn is what the ball hits. Each cup stands upright at the bottom of its ring, open on the uphill
        // side, so a ball rolling down the ring runs into it, and its number is on the front.
        const float CupBelow = 1.8f, CupAbove = 2.3f;   // the cylinder runs from this far below the board at its centre to this far above

        Transform _set, _machine, _board, _ball;
        TextMesh _scoreWords, _whoWords;
        readonly List<GameObject> _tray = new List<GameObject>();
        readonly List<Renderer> _bulbs = new List<Renderer>();
        Material _bulbOn, _bulbOff;
        int _players, _thrown;
        int[] _total;
        int[,] _scores;

        int Active => _players == 0 ? 0 : _thrown % _players;
        int Turn => _players == 0 ? 0 : _thrown / _players;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            _set = new GameObject("Putt-ball").transform;
            _set.SetParent(Root, false);
            _machine = new GameObject("Machine").transform;
            _machine.SetParent(_set, false);
            _bulbs.Clear();
            BuildRoom();
            BuildCabinet();
            BuildBoard();
            BuildHead();
            BuildTray();
            BuildRow();

            _ball = Shape(PrimitiveType.Sphere, "Ball", _set, new Vector3(0, LaneY + BallR, 6f), Vector3.one * BallR * 2f, BallMaterial()).transform;
            _ball.gameObject.SetActive(false);

            // An arcade: dim overall, cool fill from above, a warm key on the Player's machine, coloured light off the row.
            Sun(Root, new Vector3(55f, -25f, 0f), new Color(0.78f, 0.82f, 1f), 0.55f);
            Light key = World.NewLight(Root, "Cabinet light", LightType.Spot, new Color(1f, 0.93f, 0.84f), 70000f);
            key.transform.position = At(-24f, 160f, 30f);
            key.transform.rotation = Quaternion.LookRotation(At(0f, 40f, 96f) - key.transform.position);
            key.spotAngle = 56f;
            key.innerSpotAngle = 30f;
            key.range = 420f;
            key.shadows = LightShadows.Soft;
            Light board = World.NewLight(Root, "Board light", LightType.Point, new Color(1f, 0.95f, 0.88f), 6000f);
            board.transform.position = At(0f, 78f, 108f);
            board.range = 90f;
            Color[] neon = { Make.Bright, Color.white, Make.Green };
            for (int i = 0; i < 3; i++)
            {
                Light wash = World.NewLight(Root, "Arcade wash", LightType.Point, neon[i], 9000f);
                wash.transform.position = At(-120f + i * 120f, 120f, 170f);
                wash.range = 220f;
            }
        }

        static Material _ballMat;
        static Material BallMaterial() => _ballMat != null ? _ballMat : _ballMat = Make.Lit(Color.white, 0.62f);

        void BuildRoom()
        {
            // An arcade at night: patterned carpet, a dark wall with neon along the top, the ceiling lost in the dark.
            Material carpet = Make.Lit(Color.white, 0.12f, 0f, ArcadeCarpet());
            carpet.SetTextureScale("_BaseMap", new Vector2(7f, 7f));
            Make.Box("Floor", _set, new Vector3(0, -0.5f, 80f), new Vector3(700f, 1f, 600f), carpet, null, false);
            Material wall = Make.Lit(Make.Hex(0x15181B), 0.3f);
            Make.Box("Wall", _set, new Vector3(0, 110f, 200f), new Vector3(700f, 220f, 2f), wall, null, false);
            Make.Box("Skirting", _set, new Vector3(0, 3f, 198.6f), new Vector3(700f, 6f, 1f), Make.Lit(Make.Hex(0x0A0C0D), 0.6f), null, false);
            Color[] tube = { Make.Bright, Color.white };
            for (int k = 0; k < 2; k++)
            {
                float y = 150f + k * 9f;
                Card("Neon", _set, new Vector3(0, y, 198.4f), new Vector2(700f, 1.1f), Make.Glow(tube[k] * 1.6f, true));
                Card("Neon spill", _set, new Vector3(0, y, 198.2f), new Vector2(700f, 26f), Make.Glow(new Color(tube[k].r, tube[k].g, tube[k].b, 0.12f), true, Make.SoftDot()));
            }
            // A lit sign on the wall over the row: ARCADE in neon.
            Words(_set, "ARCADE", new Vector3(-150f, 128f, 198f), 9f, Make.Bright, TextAnchor.MiddleCenter, 160);
            Card("Sign glow", _set, new Vector3(-150f, 128f, 198.2f), new Vector2(90f, 30f), Make.Glow(new Color(Make.Bright.r, Make.Bright.g, Make.Bright.b, 0.18f), true, Make.SoftDot()));
        }

        static Texture2D _carpet;
        // Arcade carpet: near-black with bright confetti (squiggles, rings and dots), as every arcade floor.
        static Texture2D ArcadeCarpet()
        {
            if (_carpet != null) return _carpet;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Arcade carpet" };
            var px = new Color[N * N];
            var rng = new System.Random(19);
            for (int i = 0; i < px.Length; i++) { float n = 0.035f + (float)rng.NextDouble() * 0.03f; px[i] = new Color(n, n * 1.12f, n * 1.2f, 1f); }
            Color[] ink = { Make.Bright, Make.Green, Make.Green, Color.white, Make.Hex(0x22262A) };
            void Dot(float cx, float cy, float r, Color c)
            {
                for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
                    for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (d > r) continue;
                        px[((y % N + N) % N) * N + ((x % N + N) % N)] = c * 0.8f;
                    }
            }
            for (int s = 0; s < 60; s++)
            {
                Color c = ink[rng.Next(ink.Length)];
                float x = rng.Next(N), y = rng.Next(N);
                int kind = rng.Next(3);
                if (kind == 0) Dot(x, y, 2.5f + (float)rng.NextDouble() * 2f, c);                     // a dot
                else if (kind == 1)                                                                     // a ring
                    for (int a = 0; a < 40; a++) { float t = a / 40f * Mathf.PI * 2f; Dot(x + Mathf.Cos(t) * 7f, y + Mathf.Sin(t) * 7f, 1.2f, c); }
                else                                                                                    // a squiggle
                    for (int a = 0; a < 40; a++) Dot(x + a * 0.9f, y + Mathf.Sin(a * 0.35f) * 5f, 1.3f, c);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return _carpet = tex;
        }

        void BuildCabinet()
        {
            Transform m = _machine;
            Material wood = GreyWood();
            Material dark = Make.Lit(new Color(0.03f, 0.03f, 0.035f), 0.6f, 0.2f);
            Material felt = Make.Lit(new Color(0.05f, 0.05f, 0.055f), 0.15f);
            Material stripe = Make.Emissive(Color.white, new Color(0.35f, 0.35f, 0.35f));
            Material pin = Make.Emissive(Make.Bright, Make.Bright * 0.6f);
            Material chrome = Make.Lit(new Color(0.75f, 0.75f, 0.78f), 0.85f, 0.9f);
            Material metal = Make.Lit(new Color(0.12f, 0.12f, 0.13f), 0.7f, 0.8f);

            // The body under the lane, on two plinths, bright green light under it.
            Make.Box("Body", m, new Vector3(0, 18f, LaneEnd * 0.5f), new Vector3(LaneHalf * 2 + RailW * 2, 22f, LaneEnd), wood, null, false);
            Make.Box("Plinth front", m, new Vector3(0, 3.5f, 4f), new Vector3(LaneHalf * 2 + RailW * 2, 7f, 8f), dark, null, false);
            Make.Box("Plinth back", m, new Vector3(0, 3.5f, LaneEnd + 10f), new Vector3(LaneHalf * 2 + RailW * 2, 7f, 26f), dark, null, false);
            GameObject glow = Card("Underglow", m, new Vector3(0, 0.08f, LaneEnd * 0.5f), new Vector2(60f, LaneEnd + 30f), Make.Glow(new Color(Make.Bright.r, Make.Bright.g, Make.Bright.b, 0.5f), true, Make.SoftDot()));
            glow.transform.localRotation = Quaternion.Euler(90f, 0, 0);

            // The front: a coin door with two lit slots, and the ticket mouth beside it.
            Make.Box("Coin door", m, new Vector3(-4f, 15f, -0.4f), new Vector3(11f, 13f, 0.8f), metal, null, false);
            for (int k = 0; k < 2; k++)
            {
                Make.Box("Coin slot", m, new Vector3(-6.5f + k * 5f, 18f, -0.9f), new Vector3(2.4f, 3f, 0.4f), Make.Emissive(Make.Green, Make.Bright * 1.4f), null, false);
                Make.Box("Coin return", m, new Vector3(-6.5f + k * 5f, 11.5f, -0.9f), new Vector3(2.6f, 1.6f, 0.5f), chrome, null, false);
            }
            Make.Box("Ticket mouth", m, new Vector3(7f, 14f, -0.4f), new Vector3(6f, 4f, 0.8f), metal, null, false);
            Make.Box("Ticket slot", m, new Vector3(7f, 14f, -0.85f), new Vector3(4f, 0.5f, 0.2f), Make.Lit(Color.black, 0f), null, false);
            Words(m, "TICKETS", new Vector3(7f, 17.2f, -0.9f), 0.55f, Make.Hex(0xFFD24A));

            // The lane: black felt, a thin white line down each side.
            Make.Box("Lane", m, new Vector3(0, LaneY - 0.5f, LaneEnd * 0.5f), new Vector3(LaneHalf * 2, 1f, LaneEnd), felt, null, false);
            for (int s = -1; s <= 1; s += 2)
            {
                Make.Box("Lane line", m, new Vector3(s * (LaneHalf - 2.5f), LaneY + 0.02f, LaneEnd * 0.55f), new Vector3(0.18f, 0.02f, LaneEnd * 0.5f), stripe, null, false);
                // Rails: charcoal wood, two bright green pinstripes, a black cap and a chrome post at the Player's end.
                Make.Box("Rail", m, new Vector3(s * (LaneHalf + RailW * 0.5f), LaneY + 1.5f, LaneEnd * 0.5f), new Vector3(RailW, 6f, LaneEnd), wood, null, false);
                for (int k = 0; k < 2; k++)
                    Make.Box("Pinstripe", m, new Vector3(s * (LaneHalf + 0.7f + k * 0.9f), LaneY + 4.52f, LaneEnd * 0.5f + 4f), new Vector3(0.22f, 0.04f, LaneEnd - 14f), pin, null, false);
                Make.Box("Rail cap", m, new Vector3(s * (LaneHalf + RailW * 0.5f), LaneY + 1.6f, 2.5f), new Vector3(RailW + 0.4f, 6.4f, 5f), dark, null, false);
                Make.Box("Corner post", m, new Vector3(s * (LaneHalf + RailW * 0.5f), LaneY * 0.5f, 2.5f), new Vector3(RailW + 0.6f, LaneY, 5f), chrome, null, false);
            }
            // The hop at the top of the lane.
            GameObject hop = Shape(PrimitiveType.Cube, "Hop", m, new Vector3(0, LaneY + 0.9f, LaneEnd - 3f), new Vector3(LaneHalf * 2, 1.2f, 7f), felt);
            hop.transform.localRotation = Quaternion.Euler(-14f, 0, 0);
            Words(m, "PUTT-BALL", new Vector3(0, LaneY + 0.06f, 14f), 1.4f, new Color(1f, 1f, 1f, 0.55f)).transform.localRotation = Quaternion.Euler(90f, 0, 0);
        }

        void BuildBoard()
        {
            Transform m = _machine;
            Material wood = GreyWood();
            Material dark = Make.Lit(new Color(0.025f, 0.025f, 0.03f), 0.45f);
            Material green = Make.Lit(Make.Green, 0.3f);   // the board is the green: the one big branded surface
            Material ringMat = Make.Lit(Color.white, 0.6f);
            Material hole = Make.Lit(Color.black, 0f);

            // The back: tall curved sides, the tilted board between them, a clear guard over each side.
            float backZ = BoardZ + BoardLen * Mathf.Cos(Tilt * Mathf.Deg2Rad);
            for (int s = -1; s <= 1; s += 2)
                Make.MeshObject("Side", m, SidePanel(s * (BoardHalf + 0.8f), 1.6f, LaneEnd - 14f, backZ + 3f, LaneY - 8f, LaneY + 4.5f, 104f), wood);
            Material guard = Make.Transparent(new Color(0.8f, 0.88f, 0.95f, 0.08f), 0.95f);
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject pane = Make.MeshObject("Guard", m, SidePanel(s * (BoardHalf + 0.6f), 0.3f, LaneEnd - 6f, backZ, LaneY + 4f, LaneY + 18f, 96f), guard);
                pane.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            _board = new GameObject("Board").transform;
            _board.SetParent(m, false);
            _board.localPosition = new Vector3(0, BoardY, BoardZ);
            _board.localRotation = Quaternion.Euler(-Tilt, 0, 0);
            Make.Box("Board face", _board, new Vector3(0, -0.5f, BoardLen * 0.5f), new Vector3(BoardHalf * 2, 1f, BoardLen), green, null, false);
            // The apron between the hop and the board.
            Make.Box("Apron", m, new Vector3(0, LaneY + 1.4f, (LaneEnd + BoardZ) * 0.5f), new Vector3(BoardHalf * 2, 1f, BoardZ - LaneEnd + 1f), dark, null, false);

            for (int i = 0; i < PuttBallPhysics.Rings.Length; i++)
            {
                PuttBallPhysics.Ring r = PuttBallPhysics.Rings[i];
                // The outer ring is open across its top: a ball coming back down from above falls into the 10.
                GameObject wall = Make.MeshObject("Ring", _board, Tube(r.R, PuttBallPhysics.WallThick, r.Wall, 72, i == 0 ? PuttBallPhysics.OpenAbove : 2f), ringMat);
                wall.transform.localPosition = new Vector3(0f, 0f, PuttBallPhysics.RingZ);
            }
            // The rail each side that turns a ball coming down the cabinet's wall into the rings.
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 a = new Vector2(s * PuttBallPhysics.GuideTop.x, PuttBallPhysics.GuideTop.y), b = new Vector2(s * PuttBallPhysics.GuideEnd.x, PuttBallPhysics.GuideEnd.y);
                Vector2 mid = (a + b) * 0.5f, run = b - a;
                GameObject rail = Shape(PrimitiveType.Cube, "Guide", _board, new Vector3(mid.x, PuttBallPhysics.GuideWall * 0.5f, mid.y), new Vector3(PuttBallPhysics.WallThick, PuttBallPhysics.GuideWall, run.magnitude), ringMat);
                rail.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(run.x, run.y) * Mathf.Rad2Deg, 0f);
            }
            Material cupMat = Make.Lit(Color.white, 0.55f);
            Color ink = Make.Hex(0x0A0C0D);
            foreach (PuttBallPhysics.Pocket p in PuttBallPhysics.Pockets)
            {
                // The cup: an upright white cylinder sunk into the board, black inside, its number on the front.
                float outer = p.R + (p.Value <= 40 ? 0.12f : 0.35f);
                var cup = new GameObject("Cup " + p.Value).transform;
                cup.SetParent(_board, false);
                cup.localRotation = Quaternion.Euler(Tilt, 0f, 0f);   // undo the board's tilt: square to the floor
                cup.localPosition = new Vector3(p.C.x, 0f, p.C.y) + cup.localRotation * (Vector3.down * CupBelow);
                Make.MeshObject("Cylinder", cup, Tube(outer, 0.35f, CupBelow + CupAbove, 48), cupMat);
                GameObject pit = Make.MeshObject("Hole", cup, Make.Ring(0f, p.R, 40), hole);
                pit.transform.localPosition = new Vector3(0f, CupBelow + CupAbove - 0.6f, 0f);
                pit.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                // The front of the cylinder shows from the board up to its rim: the number sits in the middle of that.
                float frontLow = CupBelow - outer * Mathf.Tan(Tilt * Mathf.Deg2Rad);
                Words(cup, p.Value.ToString(), new Vector3(0f, (frontLow + CupBelow + CupAbove) * 0.5f, -outer - 0.06f), p.Size, ink);
            }
        }

        void BuildHead()
        {
            // The head: a black hood over the board, the marquee with the Puttr logo lit in it and chaser bulbs round
            // it, and a score display on top.
            Transform m = _machine;
            Material dark = Make.Lit(new Color(0.02f, 0.02f, 0.024f), 0.22f);
            Material wood = GreyWood();
            Material green = Make.Lit(Make.Green, 0.3f);
            float backZ = BoardZ + BoardLen * Mathf.Cos(Tilt * Mathf.Deg2Rad);
            float topY = BoardY + BoardLen * Mathf.Sin(Tilt * Mathf.Deg2Rad);
            float w = BoardHalf * 2;
            Make.Box("Back", m, new Vector3(0, (LaneY + 108f) * 0.5f, backZ + 1.5f), new Vector3(w + 3.2f, 108f - LaneY, 3f), wood, null, false);
            Make.Box("Marquee", m, new Vector3(0, topY + 14f, backZ - 0.2f), new Vector3(w, 26f, 1f), green, null, false);
            Texture2D logo = Resources.Load<Texture2D>("PuttrArcade/LogoHeader");
            if (logo != null)
            {
                float lw = w * 0.74f, lh = lw * logo.height / logo.width;
                Card("Puttr logo", m, new Vector3(0, topY + 21.5f, backZ - 0.8f), new Vector2(lw, lh), Make.Glow(new Color(1f, 1f, 1f, 1f), false, logo));
            }
            Words(m, "PUTT-BALL", new Vector3(0, topY + 11.6f, backZ - 0.9f), 3.6f, Color.white);
            Words(m, "ROLL  ·  CLIMB  ·  SCORE", new Vector3(0, topY + 5.6f, backZ - 0.9f), 0.95f, new Color(1f, 1f, 1f, 0.8f));
            // Chaser bulbs round the marquee.
            _bulbOn = Make.Emissive(Make.Bright, Make.Bright * 2.4f);
            _bulbOff = Make.Emissive(Make.Green * 0.4f, Make.Green * 0.12f);
            float x0 = -w * 0.5f + 1.2f, x1 = w * 0.5f - 1.2f, y0 = topY + 2f, y1 = topY + 26f;
            var spots = new List<Vector3>();
            for (int i = 0; i <= 12; i++) spots.Add(new Vector3(Mathf.Lerp(x0, x1, i / 12f), y1, backZ - 1f));
            for (int i = 1; i <= 8; i++) spots.Add(new Vector3(x1, Mathf.Lerp(y1, y0, i / 9f), backZ - 1f));
            for (int i = 0; i <= 12; i++) spots.Add(new Vector3(Mathf.Lerp(x1, x0, i / 12f), y0, backZ - 1f));
            for (int i = 1; i <= 8; i++) spots.Add(new Vector3(x0, Mathf.Lerp(y0, y1, i / 9f), backZ - 1f));
            foreach (Vector3 at in spots)
            {
                GameObject bulb = Shape(PrimitiveType.Sphere, "Bulb", m, at, Vector3.one * 0.9f, _bulbOff);
                bulb.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                _bulbs.Add(bulb.GetComponent<Renderer>());
            }
            // The score display: a crowned board on top, white score under a bright green name.
            Make.Box("Display", m, new Vector3(0, topY + 37f, backZ + 0.4f), new Vector3(w + 3f, 18f, 3f), dark, null, false);
            Card("Display face", m, new Vector3(0, topY + 37f, backZ - 1.15f), new Vector2(w - 2f, 15f), Make.Glow(new Color(0.04f, 0.047f, 0.05f, 1f), false));
            _whoWords = Words(m, "PLAYER 1", new Vector3(0, topY + 41.5f, backZ - 1.3f), 1.3f, Make.Bright);
            _scoreWords = Words(m, "0", new Vector3(0, topY + 34.5f, backZ - 1.3f), 5f, Color.white);
        }

        void BuildTray()
        {
            // The ball return at the Player's end: a black window with the balls still to roll lined up in it.
            Transform m = _machine;
            Material dark = Make.Lit(new Color(0.03f, 0.03f, 0.035f), 0.6f, 0.2f);
            Make.Box("Return", m, new Vector3(0, LaneY - 5f, -2.5f), new Vector3(LaneHalf * 2, 8f, 5f), dark, null, false);
            Card("Return light", m, new Vector3(0, LaneY - 5f, -5.1f), new Vector2(LaneHalf * 2 - 2f, 6f), Make.Glow(new Color(Make.Bright.r, Make.Bright.g, Make.Bright.b, 0.3f), true));
            for (int i = 0; i < Balls; i++)
            {
                GameObject b = Shape(PrimitiveType.Sphere, "Waiting ball", m, new Vector3(-9f + i * 2.25f, LaneY - 5.6f, -3.4f), Vector3.one * 2.2f, BallMaterial());
                _tray.Add(b);
            }
        }

        // The rest of the row: the same machine either side, each showing a score of its own, bulbs chasing out of step.
        readonly List<Renderer[]> _rowBulbs = new List<Renderer[]>();
        void BuildRow()
        {
            _rowBulbs.Clear();
            var rng = new System.Random(7);
            foreach (float n in Neighbours)
            {
                GameObject twin = Object.Instantiate(_machine.gameObject, _set);
                twin.name = "Machine " + n;
                twin.transform.localPosition = new Vector3(n * Pitch, 0f, 0f);
                foreach (TextMesh t in twin.GetComponentsInChildren<TextMesh>())
                {
                    if (t.text == "PLAYER 1") t.text = rng.Next(3) == 0 ? "HIGH SCORE" : "SCORE";
                    else if (t.text == "0") t.text = (rng.Next(9, 46) * 10).ToString();
                }
                var bulbs = new List<Renderer>();
                foreach (Renderer r in twin.GetComponentsInChildren<Renderer>(true))
                    if (r.gameObject.name == "Bulb") bulbs.Add(r);
                _rowBulbs.Add(bulbs.ToArray());
                // Nobody playing these: no balls waiting.
                foreach (Transform c in twin.GetComponentsInChildren<Transform>(true))
                    if (c.name == "Waiting ball") c.gameObject.SetActive(rng.Next(2) == 0);
            }
        }

        // The chaser: every third bulb lit, stepping round the marquee; the neighbours on slower, offset steps.
        float _chase;
        public override void Tick()
        {
            if (_bulbs.Count == 0 || _bulbOn == null) return;
            _chase += Time.deltaTime;
            int step = (int)(_chase * 8f);
            for (int i = 0; i < _bulbs.Count; i++) if (_bulbs[i] != null) _bulbs[i].sharedMaterial = (i + step) % 3 == 0 ? _bulbOn : _bulbOff;
            for (int k = 0; k < _rowBulbs.Count; k++)
            {
                int s = (int)(_chase * (5f + k)) + k * 2;
                Renderer[] row = _rowBulbs[k];
                for (int i = 0; i < row.Length; i++) if (row[i] != null) row[i].sharedMaterial = (i + s) % 3 == 0 ? _bulbOn : _bulbOff;
            }
        }

        static Material _grey;
        // Brushed charcoal wood: the showroom cabinets' grain, stained dark so the green board and trim read.
        static Material GreyWood()
        {
            if (_grey != null) return _grey;
            var tex = new Texture2D(64, 256, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Grey grain" };
            var rng = new System.Random(5);
            var cols = new float[64];
            for (int x = 0; x < 64; x++) cols[x] = 0.78f + (float)rng.NextDouble() * 0.22f - (rng.NextDouble() < 0.12 ? 0.25f : 0f);
            var px = new Color[64 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 64; x++)
                {
                    float g = cols[x] * (0.94f + 0.06f * Mathf.Sin(y * 0.07f + x * 0.9f)) + ((float)rng.NextDouble() - 0.5f) * 0.05f;
                    px[y * 64 + x] = new Color(g, g, g * 1.02f, 1f);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            _grey = Make.Lit(Make.Hex(0x2A2F34), 0.45f, 0f, tex);
            _grey.SetTextureScale("_BaseMap", new Vector2(2f, 1f));
            return _grey;
        }

        // A cabinet side at the back: flat bottom, a top edge that sweeps up from the rail to the head (y rises with the
        // square of the way along), thick in x. Built as columns, each face wound to face out.
        static Mesh SidePanel(float x, float thick, float z0, float z1, float bottom, float low, float high)
        {
            const int N = 24;
            var v = new List<Vector3>();
            var t = new List<int>();
            float xl = x - thick * 0.5f, xr = x + thick * 0.5f;
            float Top(int i) { float k = i / (float)N; return low + (high - low) * k * k; }
            float Z(int i) => Mathf.Lerp(z0, z1, i / (float)N);
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) // clockwise as seen from outside
            {
                int o = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                t.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 });
            }
            for (int i = 0; i < N; i++)
            {
                float za = Z(i), zb = Z(i + 1), ta = Top(i), tb = Top(i + 1);
                Quad(new Vector3(xr, ta, za), new Vector3(xr, tb, zb), new Vector3(xr, bottom, zb), new Vector3(xr, bottom, za));   // +x face
                Quad(new Vector3(xl, tb, zb), new Vector3(xl, ta, za), new Vector3(xl, bottom, za), new Vector3(xl, bottom, zb));   // -x face
                Quad(new Vector3(xl, tb, zb), new Vector3(xr, tb, zb), new Vector3(xr, ta, za), new Vector3(xl, ta, za));           // top edge
            }
            Quad(new Vector3(xl, Top(0), z0), new Vector3(xr, Top(0), z0), new Vector3(xr, bottom, z0), new Vector3(xl, bottom, z0)); // the front end
            var mesh = new Mesh { name = "Cabinet side" };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            // Texture runs along the length.
            var uv = new List<Vector2>();
            foreach (Vector3 p in v) uv.Add(new Vector2((p.y - bottom) / 40f, (p.z - z0) / 40f));
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A ring wall: a short tube standing on the board, outer and inner faces and a rounded-off top.
        static Mesh Tube(float radius, float thick, float height, int segments, float openAbove = 2f)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            float ri = radius - thick;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * radius); n.Add(d);                                    // outer bottom
                v.Add(d * radius + Vector3.up * height); n.Add(d);              // outer top
                v.Add(d * ri + Vector3.up * height); n.Add(Vector3.up);         // rim inner
                v.Add(d * radius + Vector3.up * height); n.Add(Vector3.up);     // rim outer
                v.Add(d * ri); n.Add(-d);                                       // inner bottom
                v.Add(d * ri + Vector3.up * height); n.Add(-d);                 // inner top
            }
            for (int i = 0; i < segments; i++)
            {
                if (Mathf.Sin(i * Mathf.PI * 2f / segments) > openAbove && Mathf.Sin((i + 1) * Mathf.PI * 2f / segments) > openAbove) continue;
                int a = i * 6, b = (i + 1) * 6;
                t.AddRange(new[] { a, b + 1, a + 1, a, b, b + 1 });             // outer
                t.AddRange(new[] { a + 2, a + 3, b + 3, a + 2, b + 3, b + 2 }); // rim
                t.AddRange(new[] { a + 4, a + 5, b + 5, a + 4, b + 5, b + 4 }); // inner
            }
            // Both windings, so the wall shows whichever way round the faces were wound.
            int count = t.Count;
            for (int i = 0; i < count; i += 3) t.AddRange(new[] { t[i], t[i + 2], t[i + 1] });
            var mesh = new Mesh { name = "Ring wall" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = Mathf.Max(1, players);
            _thrown = 0;
            _total = new int[_players];
            _scores = new int[_players, Balls];
            var names = new List<string>();
            for (int i = 0; i < _players; i++) names.Add(Arcade.PlayerName(i));
            Arcade.Hud.BuildBoard(names, Balls, "TOTAL", false, "BALLS", 34f);
            ShowScores();
            ResetSet();
        }

        public override IEnumerator Prepare()
        {
            ResetSet();
            yield break;
        }

        void ResetSet()
        {
            _view = View.Lane;
            if (_ball != null) _ball.gameObject.SetActive(false);
            int left = Over ? 0 : Balls - Turn;
            for (int i = 0; i < _tray.Count; i++) _tray[i].SetActive(i < left);
            if (_whoWords != null && _total != null)
            {
                _whoWords.text = _players > 1 ? Arcade.PlayerName(Active).ToUpperInvariant() : "SCORE";
                _scoreWords.text = _total[Active].ToString();
            }
        }

        public override string Prompt
        {
            get
            {
                string who = _players > 1 ? Arcade.PlayerName(Active) + "   ·   " : "";
                return who + "Ball " + (Turn + 1) + " of " + Balls + "   ·   Putt when ready";
            }
        }

        /// <summary>Rollout: how far past the hole the putt would have finished, in inches.</summary>
        static float Past(PuttResult putt)
        {
            PuttLaunch launch = putt.Launch;
            float speed = launch.HandoffSpeedInPerSec > 0f ? launch.HandoffSpeedInPerSec : launch.SpeedInPerSec;
            return PaceWords.Past(speed);
        }

        public override IEnumerator Play(PuttResult putt)
        {
            int player = Active, ball = Turn;
            PuttResult real = Arcade.TruePutt(putt);
            float line = Line(real), past = Past(real);
            int value = 0;

            int left = Balls - ball;
            if (left - 1 >= 0 && left - 1 < _tray.Count) _tray[left - 1].SetActive(false);
            _ball.gameObject.SetActive(true);
            _ball.localScale = Vector3.one * BallR * 2f;
            Place(new Vector3(0f, LaneY + BallR, PuttBallPhysics.LaneStart));
            _view = View.Ball;
            Arcade.Audio.TryLibrary("puttball_roll", 0.7f);

            // Short: how close it came to the hole.
            float shortBy = -1f;
            if (!real.Launch.Reached)
            {
                shortBy = 24f;
                if (putt.PathInches != null) { shortBy = float.MaxValue; foreach (Vector2 p in putt.PathInches) shortBy = Mathf.Min(shortBy, p.magnitude); }
            }
            if (shortBy > PuttBallPhysics.OffTheBoard)
            {
                // Nowhere near the ramp: part way up the lane and back down to the Player, on the putt's own line.
                float reach = Mathf.Lerp(LaneEnd * 0.85f, LaneEnd * 0.3f, Mathf.InverseLerp(PuttBallPhysics.OffTheBoard, 48f, shortBy));
                Vector3 start = _ball.localPosition;
                float side = Mathf.Clamp(line * reach / LaneEnd, -(LaneHalf - BallR), LaneHalf - BallR);
                Vector3 top = new Vector3(side, start.y, reach);
                yield return Roll(start, top, 1.1f, true);
                yield return Roll(top, start, 0.9f, true);
                Arcade.Audio.SetRoll(0f);
                Arcade.Hud.Banner("SHORT", "It never reached the ramp", Make.Amber);
                if (!Arcade.Audio.TryLibrary("puttball_gutter", 0.7f)) Arcade.Audio.Miss();
                ShowPutt(putt, "Short", Make.Amber);
            }
            else
            {
                PuttBallPhysics.Flight flight = PuttBallPhysics.Run(line, past, real.Made, shortBy);
                value = flight.Value;
                int knock = 0, shown = -1;
                float clock = 0f;
                bool hopped = false;
                while (shown < flight.Frames.Count - 1)
                {
                    clock += Time.deltaTime;
                    shown = Mathf.Min(flight.Frames.Count - 1, (int)(clock * PuttBallPhysics.FramesPerSecond));
                    Vector3 f = flight.Frames[shown];
                    if (shown < flight.LaneFrames)
                    {
                        // Along the lane, lifting onto the hop at the top of it.
                        float lift = Mathf.SmoothStep(0f, 1.4f, Mathf.InverseLerp(LaneEnd - 9f, LaneEnd - 1f, f.z));
                        Place(new Vector3(f.x, LaneY + BallR + lift, f.z));
                        Arcade.Audio.SetRoll(flight.Launch);
                    }
                    else
                    {
                        if (!hopped)
                        {
                            hopped = true;
                            _view = View.Rings;
                            Arcade.Audio.SetRoll(0f);
                            if (!Arcade.Audio.TryLibrary("puttball_hop", 0.6f)) Arcade.Audio.Thump(24f);
                        }
                        Place(OnBoard(f.x, f.y, f.z));
                    }
                    _ball.Rotate(Vector3.right, flight.Launch * 4f * Time.deltaTime, Space.Self);
                    while (knock < flight.Knocks.Count && flight.Knocks[knock] <= shown) { knock++; Arcade.Audio.Thump(14f); }
                    yield return null;
                }
                Arcade.Audio.SetRoll(0f);

                if (value == 0)
                {
                    // Off the bottom of the board and back down the lane.
                    Vector3 from = _ball.localPosition;
                    yield return Roll(from, new Vector3(Mathf.Clamp(from.x, -(LaneHalf - BallR), LaneHalf - BallR), LaneY + BallR, LaneEnd * 0.5f), 0.7f, true);
                    Arcade.Audio.SetRoll(0f);
                    _ball.gameObject.SetActive(false);
                    Arcade.Hud.Banner("GUTTER", shortBy >= 0f ? "Short of the rings" : "It missed every ring", Make.Amber);
                    if (!Arcade.Audio.TryLibrary("puttball_gutter", 0.7f)) Arcade.Audio.Miss();
                    ShowPutt(putt, "Gutter", Make.Amber);
                }
                else
                {
                    yield return Drop(_ball.localPosition);
                    string sub = value == 100 ? (flight.EndX < 0f ? "The left corner" : "The right corner")
                        : value == 50 ? "Off the back and into the 50"
                        : shortBy >= 0f ? "Short of the hole: the " + value + " ring"
                        : !real.Made ? "Missed: it rolled back into the " + value
                        : flight.Fell ? "Too firm: it fell back into the " + value
                        : "In the hole: the bullseye";
                    Color tone = value == 100 ? Make.Hex(0xFFD24A) : value >= 50 ? Make.Bright : Color.white;
                    Arcade.Hud.Banner(value.ToString(), sub, tone, value == 100 ? 2.2f : 1.6f);
                    if (value == 100)
                    {
                        if (!Arcade.Audio.TryLibrary("puttball_hundred", 1f)) Arcade.Audio.Strike();
                        Arcade.Celebrate(90, _set.TransformPoint(OnBoard(flight.EndX, flight.EndZ, BallR)));
                    }
                    else if (!Arcade.Audio.TryLibrary("puttball_ring", 0.4f + value / 120f)) Arcade.Audio.Spare();
                    ShowPutt(putt, value.ToString(), tone);
                }
            }

            _scores[player, ball] = value;
            _total[player] += value;
            _thrown++;
            ShowScores();
            if (_scoreWords != null) _scoreWords.text = _total[player].ToString();
            Arcade.Shot("result");
            yield return new WaitForSecondsRealtime(1.6f);
            ResetSet();
        }

        void Place(Vector3 at) { _ball.localPosition = at; _ballAt = at; }

        // A point on the board's face (x across, z up the board, up off its face), in the set's space.
        static Vector3 OnBoard(float x, float z, float up)
        {
            float t = Tilt * Mathf.Deg2Rad, sin = Mathf.Sin(t), cos = Mathf.Cos(t);
            return new Vector3(x, BoardY + z * sin + up * cos, BoardZ + z * cos - up * sin);
        }

        IEnumerator Roll(Vector3 from, Vector3 to, float seconds, bool ease)
        {
            float t = 0f, dist = Vector3.Distance(from, to);
            Vector3 axis = Vector3.Cross(Vector3.up, (to - from).normalized);
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / seconds);
                float e = ease ? Ease(k) : k;
                Place(Vector3.Lerp(from, to, e));
                if (axis.sqrMagnitude > 0.01f) _ball.localRotation = Quaternion.AngleAxis(dist * e / BallR * Mathf.Rad2Deg, axis);
                Arcade.Audio.SetRoll(dist / Mathf.Max(0.05f, seconds));
                yield return null;
            }
        }

        // The ball settles into the cup and drops through the board.
        IEnumerator Drop(Vector3 at)
        {
            if (!Arcade.Audio.TryLibrary("puttball_drop", 0.6f)) Arcade.Audio.Thump(16f);
            Vector3 down = _set.InverseTransformDirection(-_board.up);
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 0.35f);
                _ball.localPosition = at + down * (BallR * 2.4f * k * k);
                _ball.localScale = Vector3.one * BallR * 2f * (1f - 0.3f * k);
                yield return null;
            }
            _ball.gameObject.SetActive(false);
        }

        public override bool Over => _players > 0 && _thrown >= _players * Balls;

        public override void Final(out string title, out string body)
        {
            title = _players == 1 ? _total[0] + "  P O I N T S" : "G A M E   O V E R";
            body = "";
            if (_players == 1) { body = "Nine balls. The corners are worth 100.\n"; return; }
            int best = 0, ties = 0;
            for (int i = 0; i < _players; i++) if (_total[i] > _total[best]) best = i;
            for (int i = 0; i < _players; i++) if (_total[i] == _total[best]) ties++;
            for (int i = 0; i < _players; i++)
                body += Arcade.PlayerName(i) + "     " + _total[i] + (ties == 1 && i == best ? "     winner" : "") + "\n";
            if (ties > 1) body += "A draw\n";
        }

        void ShowScores()
        {
            Hud hud = Arcade.Hud;
            for (int i = 0; i < _players; i++)
            {
                int done = _thrown / _players + (i < _thrown % _players ? 1 : 0);
                for (int t = 0; t < Balls; t++)
                    hud.SetPip(i, t, t >= done ? Hud.Pip.Empty : _scores[i, t] > 0 ? Hud.Pip.Good : Hud.Pip.Over, t < done ? _scores[i, t].ToString() : "");
                hud.SetTotal(i, _total[i]);
            }
            hud.SetTurn(Over ? -1 : Active, Over ? -1 : Turn);
        }
    }

    /// <summary>
    /// The ball on a Putt-ball machine, stepped in inches and seconds. Nothing here is random, so one putt always plays the
    /// same way. On the lane a frame is (x, 0, lane z); on the board it is (x, z up the board, height off its face).
    /// </summary>
    public static class PuttBallPhysics
    {
        public const float Half = 16f, Length = 50f, TiltDeg = 32f, BallR = 1.5f, LaneStart = 4f, WallThick = 0.45f;
        public const float RingZ = 17f, OpenAbove = 0.3f;
        public const int FramesPerSecond = 120;
        const float Gravity = 386f, Step = 1f / 480f, LaneTop = 95f;
        const float HopZ = -4.3f, HopH = 2.6f;   // where the ball leaves the hop, in the board's own space
        const float Bounce = 0.45f, WallBounce = 0.55f;

        // The board is the Puttr's ramp, drawn large. The bullseye is the hole; the top of the board (the 100s' row) is the
        // back of the mat, BackOfMat inches past it; across, the mat from the edge of the cup to its own edge is the board
        // from its middle to its side.
        public const float XPerInch = 1.39f, BackOfMat = 5f, TopZ = 44.6f, HoleR = 2.125f;
        /// <summary>A rail each side, from the cabinet's wall down to where the outer ring opens: a ball coming down the
        /// wall is turned into the rings, not left sitting between the ring and the wall. (x, z up the board), right side.</summary>
        public static readonly Vector2 GuideTop = new Vector2(Half, 31f), GuideEnd = new Vector2(13.35f, 21.2f);
        public const float GuideWall = 2.9f;
        /// <summary>A putt left shorter than this still lands on the board; shorter never leaves the lane.</summary>
        public const float OffTheBoard = 14f;
        // A make lands on the bullseye at DeadPast of rollout, and this far up the board for each inch firmer.
        const float DeadPast = 9f, ZPerPast = 0.18f;

        public struct Ring { public float R, Wall; public int Value; }
        public static readonly Ring[] Rings =
        {
            new Ring { R = 14f, Wall = 3.2f, Value = 10 },
            new Ring { R = 10.2f, Wall = 2.9f, Value = 20 },
            new Ring { R = 6.4f, Wall = 2.7f, Value = 30 },
            new Ring { R = 2.6f, Wall = 2.5f, Value = 40 },
        };
        public struct Pocket { public int Value; public Vector2 C; public float R, Size; }
        public static readonly Pocket[] Pockets =
        {
            new Pocket { Value = 10, C = new Vector2(0f, 5.05f), R = 1.6f, Size = 1f },
            new Pocket { Value = 20, C = new Vector2(0f, 8.85f), R = 1.6f, Size = 1f },
            new Pocket { Value = 30, C = new Vector2(0f, 12.65f), R = 1.6f, Size = 1f },
            new Pocket { Value = 40, C = new Vector2(0f, 16.45f), R = 1.6f, Size = 1f },
            new Pocket { Value = 50, C = new Vector2(0f, 37.6f), R = 2.3f, Size = 1.3f },
            new Pocket { Value = 100, C = new Vector2(-12.4f, 44.6f), R = 2.3f, Size = 1f },
            new Pocket { Value = 100, C = new Vector2(12.4f, 44.6f), R = 2.3f, Size = 1f },
        };

        public sealed class Flight
        {
            public int Value, LaneFrames;
            public float Launch, EndX, EndZ;
            public bool Fell;                                   // it had been further up the board than the ring it ended in
            public Vector2 Target;                              // where it was lobbed to: x across, z up the board
            public readonly List<Vector3> Frames = new List<Vector3>();
            public readonly List<int> Knocks = new List<int>(); // the frames where it hit a wall
        }

        /// <summary>Where on the board the ball lands (x across, z up it) for this putt. line: inches right of center at
        /// the hole. past: the rollout past the hole, inches. shortBy: how close a putt that never reached the hole came
        /// (below 0 for one that reached).</summary>
        public static Vector2 Target(float line, float past, bool made, float shortBy = -1f)
        {
            float reach = Half - BallR - 0.3f;
            if (shortBy >= 0f)
            {
                // Short: below the bullseye. The 30 from just under an inch short, then the 20; the 10 from about 3.5 in.
                float drop = Mathf.Min(Mathf.Min(3f * shortBy, 9.5f + 0.25f * shortBy), 13.2f);
                return new Vector2(Mathf.Clamp(line, -reach, reach), RingZ - drop);
            }
            // Holed: the bullseye, a little low for one that died in, and up the board for one hit too firm.
            if (made) return new Vector2(Mathf.Clamp(line, -2.1f, 2.1f), RingZ + Mathf.Clamp((past - DeadPast) * ZPerPast, -1.6f, 14.5f));
            // Missed: on to the back of the mat, as far across as it passed outside the cup.
            float across = Mathf.Max(0f, Mathf.Abs(line) - HoleR) * XPerInch;
            return new Vector2(Mathf.Clamp(Mathf.Sign(line) * across, -reach, reach), Mathf.Lerp(RingZ, TopZ, Mathf.Clamp01(past / BackOfMat)));
        }

        public static Flight Run(float line, float past, bool made = true, float shortBy = -1f)
        {
            var f = new Flight();
            Vector2 target = f.Target = Target(line, past, made, shortBy);
            float tilt = TiltDeg * Mathf.Deg2Rad, sin = Mathf.Sin(tilt), cos = Mathf.Cos(tilt);

            // The lob off the hop: it comes down on the target just as it stops climbing the board, so it lands softly
            // and what happens next is the board's doing.
            float air = Mathf.Sqrt(2f * Mathf.Max(1f, target.y - HopZ) / (Gravity * sin));
            float vz = Gravity * sin * air, vh = (BallR - HopH + 0.5f * Gravity * cos * air * air) / air;
            float speed = Mathf.Sqrt(vz * vz + vh * vh);
            f.Launch = speed;

            // Up the lane on one straight line to the hop, drifting across at the pace it keeps in the air: no rail.
            float laneTime = (LaneTop - LaneStart) / speed;
            float vx = target.x / (laneTime + air);
            float x = 0f, along = LaneStart;
            int steps = 0, every = Mathf.RoundToInt(1f / (Step * FramesPerSecond));
            f.Frames.Add(new Vector3(x, 0f, along));
            while (along < LaneTop)
            {
                x += vx * Step; along += speed * Step;
                if (++steps % every == 0) f.Frames.Add(new Vector3(x, 0f, along));
            }
            f.LaneFrames = f.Frames.Count;

            float z = HopZ, h = HopH, still = 0f, highest = z;
            for (float t = 0f; t < 6f; t += Step)
            {
                bool rolling = h <= BallR + 0.01f && Mathf.Abs(vh) < 8f;
                vz -= Gravity * sin * (rolling ? 5f / 7f : 1f) * Step;
                if (!rolling) vh -= Gravity * cos * Step;
                else
                {
                    float s = Mathf.Sqrt(vx * vx + vz * vz);
                    if (s > 0f) { float k = Mathf.Max(0f, s - 10f * Step) / s; vx *= k; vz *= k; }
                }
                float ox = x, oz = z;
                x += vx * Step; z += vz * Step; h += vh * Step;
                highest = Mathf.Max(highest, z);
                if (h < BallR)
                {
                    h = BallR;
                    if (Mathf.Abs(vh) > 8f) { if (vh < -30f) Knock(f); vh = -vh * Bounce; vx *= 0.92f; vz *= 0.92f; } else vh = 0f;
                }
                if (Mathf.Abs(x) > Half - BallR) { x = Mathf.Sign(x) * (Half - BallR); vx *= -0.5f; Knock(f); }
                if (z > Length - BallR) { z = Length - BallR; vz *= -0.45f; Knock(f); }
                if (z < -1f && vz < 0f && h <= BallR + 0.5f) { Finish(f, 0, x, z, h, highest); return f; }

                for (int i = 0; i < Rings.Length; i++)
                {
                    Ring r = Rings[i];
                    if (h > r.Wall + BallR) continue;
                    float dz = z - RingZ, d = Mathf.Sqrt(x * x + dz * dz);
                    if (d < 0.001f) continue;
                    if (i == 0 && dz / d > OpenAbove) continue;
                    float reach = BallR + WallThick * 0.5f;
                    if (Mathf.Abs(d - r.R) >= reach) continue;
                    float was = Mathf.Sqrt(ox * ox + (oz - RingZ) * (oz - RingZ)), side = was >= r.R ? 1f : -1f;
                    float nx = x / d, nz = dz / d;
                    x = nx * (r.R + side * reach); z = RingZ + nz * (r.R + side * reach);
                    float into = vx * nx + vz * nz;
                    if (into * side < 0f)
                    {
                        if (Mathf.Abs(into) > 12f) Knock(f);
                        vx -= (1f + WallBounce) * into * nx; vz -= (1f + WallBounce) * into * nz;
                        vx *= 0.985f; vz *= 0.985f;   // a wall takes a little of what runs along it too
                    }
                }

                if (h <= GuideWall + BallR)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float ax = side * GuideTop.x, az = GuideTop.y, ex = side * GuideEnd.x - ax, ez = GuideEnd.y - az;
                        float u = Mathf.Clamp01(((x - ax) * ex + (z - az) * ez) / (ex * ex + ez * ez));
                        float px = ax + ex * u, pz = az + ez * u, dx = x - px, dz = z - pz, d = Mathf.Sqrt(dx * dx + dz * dz);
                        float reach = BallR + WallThick * 0.5f;
                        if (d >= reach || d < 0.001f) continue;
                        float nx = dx / d, nz = dz / d;
                        x = px + nx * reach; z = pz + nz * reach;
                        float into = vx * nx + vz * nz;
                        if (into < 0f) { if (into < -12f) Knock(f); vx -= (1f + WallBounce) * into * nx; vz -= (1f + WallBounce) * into * nz; }
                    }

                float speedNow = Mathf.Sqrt(vx * vx + vz * vz);
                if (h < BallR + 1.2f && speedNow < 75f)
                    foreach (Pocket p in Pockets)
                        if ((x - p.C.x) * (x - p.C.x) + (z - p.C.y) * (z - p.C.y) < p.R * p.R) { Finish(f, p.Value, p.C.x, p.C.y, h, highest); return f; }

                still = speedNow < 4f && h < BallR + 0.2f ? still + Step : 0f;
                if (still > 0.35f) break;
                if (++steps % every == 0) f.Frames.Add(new Vector3(x, z, h));
            }
            // Wedged: it drops in the ring it is sitting in, or it is out.
            int value = 0;
            for (int i = Rings.Length - 1; i >= 0 && value == 0; i--)
                if (Mathf.Sqrt(x * x + (z - RingZ) * (z - RingZ)) < Rings[i].R) value = Rings[i].Value;
            if (value == 0 && z > RingZ) value = Rings[0].Value;
            Finish(f, value, x, z, h, highest);
            return f;
        }

        static void Knock(Flight f) { if (f.Knocks.Count == 0 || f.Frames.Count - f.Knocks[f.Knocks.Count - 1] > 6) f.Knocks.Add(f.Frames.Count); }

        static void Finish(Flight f, int value, float x, float z, float h, float highest)
        {
            f.Value = value; f.EndX = x; f.EndZ = z;
            f.Frames.Add(new Vector3(x, z, h));
            f.Fell = value > 0 && value < 40 && highest > RingZ + Rings[3].R;   // it went past the 40 and came back
        }

        static bool _windowRead;
        static float _near, _far;
        /// <summary>The rollouts of a make dead center that stay in the 40: the ideal-putt card's pace window.</summary>
        public static void FortyWindow(out float near, out float far)
        {
            if (!_windowRead)
            {
                _windowRead = true;
                bool found = false;
                for (float past = 0f; past <= 72f; past += 0.5f)
                {
                    if (Run(0f, past).Value != 40) { if (found) break; continue; }
                    if (!found) { found = true; _near = past; }
                    _far = past;
                }
            }
            near = _near; far = _far;
        }
    }
}
