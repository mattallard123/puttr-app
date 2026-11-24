// HORSE as basketball: a shoot-around in an empty NBA arena. The called spot is a spot on the floor (a longer putt is
// a deeper shot, the tee swings it left, middle or right), the putt is the shot. A make swishes (or banks in when the
// entry wasn't clean), a back door or a missed swish rattles out, a miss clangs off the side it missed, a putt that
// never reached the hole is an airball. Letters light up on the video board hung over the baseline.
// The arena (hardwood, markings, stanchion, the bowl, LED boards with Puttr as the sponsor) is built from HorseArena.
// Calling the shot: the lane runs on toward the shooter with the Puttr mat painted in it (a line for each distance,
// 3 to 11 ft, and the Left, Center and Right tee spots on each). While a shot is being called the camera looks down
// on the paint and a ball dribbles on the called spot; tapping another spot bounces the ball over to it and calls
// it (SpotPicked, which the app's HorseGame listens to). Puttr's own call bounces the ball over the same way.
// Units are inches; the hoop is at the origin of the floor plan, 120 in up.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class HorseScene : RuleScene
    {
        const float RimH = 120f, RimR = 9f, BallR = 4.7f, BoardZ = 15f;
        static readonly Vector3 RimAt = new Vector3(0, RimH, 0);

        // The home side's colours: Puttr green, black and a bright accent.
        static readonly Color Team = new Color(0.02f, 0.36f, 0.18f);
        static readonly Color TeamDark = new Color(0.015f, 0.2f, 0.1f);

        /// <summary>A spot tapped on the court while the shot is being called: feet, tee.</summary>
        public static System.Action<int, int> SpotPicked;

        Transform _ball, _net, _spot, _spotX;
        TextMesh _call, _hint;
        readonly List<TextMesh> _rows = new List<TextMesh>();
        LineRenderer _trail;
        Vector3 _shotFrom, _cam, _look;
        int _feet = 6, _tee = 2;
        // Calling: the shot is being called (the camera looks down on the paint, the ball dribbles on the spot), and
        // whether the one calling may tap a spot (not while Puttr calls). Playing: a putt is on show.
        bool _calling, _pick, _playing;
        Vector3 _hopFrom;
        float _hopT = 1f, _dribbleT;
        const float HopSec = 0.45f, HopUp = 34f, DribbleSec = 0.5f, DribbleUp = 28f;

        bool Overhead => _calling && !_playing;
        public override Vector3 CameraFrom => At(Overhead ? OverheadFrom() : _cam);
        public override Vector3 CameraLook => At(Overhead ? OverheadLook : _look);
        public override float CameraFov => Portrait ? 58f : 44f;
        public override float ShadowDistance => 900f;
        static bool Portrait => Screen.height > Screen.width;

        // Where a putt from feet / tee is shot from on the floor: its spot on the mat painted in the lane.
        static Vector3 SpotFor(int feet, int tee) => new Vector3(HorseArena.TeeX(tee), 0, HorseArena.MatZ(feet));

        // Looking down on the paint, tipped a little so the hoop shows at the top: the lane, its mat and the labels fill
        // the frame whichever way the screen is held.
        static readonly Vector3 OverheadLook = new Vector3(0, 0, -140f);
        const float OverheadTilt = 16f;
        Vector3 OverheadFrom()
        {
            Camera cam = Camera.main;
            float aspect = cam != null ? cam.aspect : 0.9f;
            float tanV = Mathf.Tan(CameraFov * 0.5f * Mathf.Deg2Rad), tanH;
            // The arcade widens a landscape lens to show what a 16:9 screen would (Arcade.Fitted).
            if (Portrait || aspect >= 16f / 9f) tanH = tanV * aspect;
            else { tanH = tanV * 16f / 9f; tanV = tanH / aspect; }
            float d = Mathf.Max(250f / tanV, 112f / tanH);
            float a = OverheadTilt * Mathf.Deg2Rad;
            return OverheadLook + new Vector3(0, Mathf.Cos(a), -Mathf.Sin(a)) * d;
        }

        static Texture2D _logo, _mark, _markWhite;

        public override void Build()
        {
            // The sponsor is the brand (Brand.cs): Puttr with no kit, the host's logo with one.
            _logo = Brand.Logo;
            _mark = Brand.MarkColour;
            _markWhite = Brand.Mark;

            BuildCourt();
            BuildHoop();
            BuildBowl();
            BuildBoards();
            BuildLights();

            // The called spot: a glowing ring and an X on the floor.
            _spot = new GameObject("Spot").transform;
            _spot.SetParent(Root, false);
            Hoop("Ring", _spot, new Vector3(0, 0.4f, 0), 10f, 12.5f, Make.Emissive(Color.black, new Color(1f, 0.6f, 0.1f) * 2f));
            _spotX = Words(_spot, "X", new Vector3(0, 0.5f, 0), 12f, new Color(1f, 0.8f, 0.3f)).transform;
            _spotX.localRotation = Quaternion.Euler(90f, 0, 0);

            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.38f, 0f, HorseArena.Basketball())).transform;
            _ball.gameObject.SetActive(false);
            _trail = Stroke(Root, "Arc", new Color(1f, 0.6f, 0.2f, 0.6f), 1.4f);
        }

        // ── The set ─────────────────────────────────────────────────────────

        static Mesh _apron, _court, _paint, _lines, _mat;

        static GameObject Flat(string name, Transform parent, Mesh mesh, Material m, bool shadows = false)
        {
            GameObject go = Make.MeshObject(name, parent, mesh, m);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A flat card lying on the floor, reading the right way for a camera looking up the court (+z), turned by yaw.</summary>
        static void Decal(HorseArena.Builder b, Vector3 c, float w, float h, float yaw = 0f)
        {
            Quaternion q = Quaternion.Euler(0, yaw, 0);
            Vector3 r = q * Vector3.right * (w / 2f), f = q * Vector3.forward * (h / 2f);
            b.Card(c - r - f, c - r + f, c + r + f, c + r - f, Vector3.up);
        }

        void BuildCourt()
        {
            Texture2D wood = HorseArena.Hardwood(out Texture2D woodNormal);
            const float tx = 36f, tz = 192f;
            if (_apron == null)
            {
                _apron = HorseArena.Floor(-HorseArena.BowlHX - 40f, HorseArena.BowlHX + 40f, HorseArena.Cut - 500f, HorseArena.BowlFront + 40f, 0f, tx, tz);
                _court = HorseArena.Floor(-HorseArena.HalfWidth, HorseArena.HalfWidth, HorseArena.FarBaseline, HorseArena.Baseline, 0.05f, tx, tz);
                var paint = new HorseArena.Builder();
                foreach (float end in new[] { 1f, -1f })
                {
                    float bl = end > 0 ? HorseArena.Baseline : HorseArena.FarBaseline, ft = bl - end * 228f;
                    paint.Quad(new Vector3(-HorseArena.LaneHalf, 0.1f, bl), new Vector3(-HorseArena.LaneHalf, 0.1f, ft), new Vector3(HorseArena.LaneHalf, 0.1f, ft), new Vector3(HorseArena.LaneHalf, 0.1f, bl), Vector3.up,
                        new Vector2(-HorseArena.LaneHalf / tx, bl / tz), new Vector2(-HorseArena.LaneHalf / tx, ft / tz), new Vector2(HorseArena.LaneHalf / tx, ft / tz), new Vector2(HorseArena.LaneHalf / tx, bl / tz));
                }
                // The lane runs on toward the shooter for the Puttr mat.
                {
                    float x0 = -HorseArena.LaneHalf, x1 = HorseArena.LaneHalf, z0 = HorseArena.PaintEnd, z1 = HorseArena.FreeThrow;
                    paint.Quad(new Vector3(x0, 0.1f, z1), new Vector3(x0, 0.1f, z0), new Vector3(x1, 0.1f, z0), new Vector3(x1, 0.1f, z1), Vector3.up,
                        new Vector2(x0 / tx, z1 / tz), new Vector2(x0 / tx, z0 / tz), new Vector2(x1 / tx, z0 / tz), new Vector2(x1 / tx, z1 / tz));
                }
                _paint = paint.Build("Paint");
                _lines = HorseArena.Lines(0.16f);
                _mat = HorseArena.MatMarks(0.18f);
            }
            // Polished maple on the court, a dark stain outside the lines, the lane painted in the team green (the
            // grain still shows through the paint).
            Flat("Apron", Root, _apron, Make.Lit(new Color(0.36f, 0.22f, 0.13f), 0.86f, 0f, wood, woodNormal, 0.5f)).GetComponent<MeshRenderer>().receiveShadows = true;
            Flat("Court", Root, _court, Make.Lit(new Color(1f, 0.97f, 0.93f), 0.86f, 0f, wood, woodNormal, 0.5f));
            Flat("Paint", Root, _paint, Make.Lit(new Color(0.05f, 0.3f, 0.17f), 0.6f, 0f, wood, woodNormal, 0.4f));
            Flat("Lines", Root, _lines, Make.Lit(new Color(0.96f, 0.96f, 0.94f), 0.8f));
            Flat("Mat marks", Root, _mat, Make.Lit(new Color(0.96f, 0.96f, 0.94f), 0.8f));
            // The tees as on the Puttr mat: a black spot with a thin white rim, the distance in the centre one. L C R past
            // the end of the paint.
            Color label = new Color(0.97f, 0.97f, 0.95f);
            Flat("Tee rims", Root, HorseArena.TeeSpots(0.2f, 9.6f), Make.Lit(new Color(0.9f, 0.9f, 0.88f), 0.6f));
            Flat("Tee spots", Root, HorseArena.TeeSpots(0.22f, 8.6f), Make.Lit(new Color(0.02f, 0.02f, 0.025f), 0.35f));
            // puttr-app-2 (Matt, 2026-11-21): as the Puttr mat prints it, the distance once, in the centre spot of its row;
            // the left and right spots are plain circles to tap.
            for (int f = 3; f <= 11; f++)
                Words(Root, f.ToString(), new Vector3(HorseArena.TeeX(2), 0.32f, HorseArena.MatZ(f)), 8.5f, label).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            string[] tees = { "L", "C", "R" };
            for (int t = 1; t <= 3; t++)
                Words(Root, tees[t - 1], new Vector3(HorseArena.TeeX(t), 0.3f, HorseArena.PaintEnd - 16f), 14f, label).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            _hint = Words(Root, "", new Vector3(0, 0.3f, HorseArena.PaintEnd - 42f), 12f, new Color(1f, 0.75f, 0.2f));
            _hint.transform.localRotation = Quaternion.Euler(90f, 0, 0);

            // The sponsor on the floor: the mark at centre court, the wordmark either side of the stanchion and along the sidelines.
            if (_logo != null)
            {
                float aspect = _logo.width / (float)_logo.height;
                var words = new HorseArena.Builder();
                Decal(words, new Vector3(-190f, 0.2f, HorseArena.Baseline + 52f), 130f, 130f / aspect);
                Decal(words, new Vector3(190f, 0.2f, HorseArena.Baseline + 52f), 130f, 130f / aspect);
                Decal(words, new Vector3(-HorseArena.HalfWidth - 62f, 0.2f, -260f), 170f, 170f / aspect, -90f);
                Decal(words, new Vector3(HorseArena.HalfWidth + 62f, 0.2f, -260f), 170f, 170f / aspect, 90f);
                Flat("Floor logos", Root, words.Build("Floor logos"), Make.Transparent(new Color(1f, 1f, 1f, 0.92f), 0.8f, _logo));
            }
            if (_mark != null)
            {
                var centre = new HorseArena.Builder();
                Vector2 centreSize = Brand.Fit(_mark, 100f, 100f);
                Decal(centre, new Vector3(0, 0.2f, HorseArena.HalfCourt), centreSize.x, centreSize.y);
                Flat("Centre logo", Root, centre.Build("Centre logo"), Make.Transparent(Color.white, 0.8f, _mark));
            }
        }

        void BuildHoop()
        {
            Material pad = Make.Lit(Team, 0.35f);
            Material steel = Make.Lit(new Color(0.12f, 0.13f, 0.14f), 0.6f, 0.7f);
            Material black = Make.Lit(new Color(0.02f, 0.02f, 0.025f), 0.5f);
            Material white = Make.Lit(new Color(0.95f, 0.95f, 0.95f), 0.6f);
            Material glass = Make.Transparent(new Color(0.82f, 0.9f, 0.95f, 0.13f), 0.97f);
            Material orange = Make.Emissive(new Color(1f, 0.38f, 0.06f), new Color(0.28f, 0.08f, 0.01f), 0.7f);

            // The stanchion: a padded base behind the baseline, a padded post, and the boom out to the glass.
            const float baseZ = 190f;
            Shape(PrimitiveType.Cube, "Stanchion base", Root, new Vector3(0, 27f, baseZ), new Vector3(78f, 54f, 92f), pad);
            Shape(PrimitiveType.Cube, "Base top", Root, new Vector3(0, 55f, baseZ), new Vector3(70f, 2f, 84f), black);
            Shape(PrimitiveType.Cube, "Post pad", Root, new Vector3(0, 92f, baseZ + 18f), new Vector3(26f, 76f, 26f), pad);
            Shape(PrimitiveType.Cube, "Post", Root, new Vector3(0, 150f, baseZ + 18f), new Vector3(14f, 70f, 14f), steel);
            Vector3 boomFrom = new Vector3(0, 182f, baseZ + 18f), boomTo = new Vector3(0, RimH + 26f, BoardZ + 6f);
            GameObject boom = Shape(PrimitiveType.Cube, "Boom", Root, (boomFrom + boomTo) / 2f, new Vector3(10f, 10f, Vector3.Distance(boomFrom, boomTo) + 8f), steel);
            boom.transform.localRotation = Quaternion.LookRotation(boomTo - boomFrom);
            Shape(PrimitiveType.Cube, "Brace", Root, new Vector3(0, RimH + 15f, BoardZ + 5f), new Vector3(44f, 30f, 3f), steel);
            if (_logo != null)
            {
                // The sponsor on the stanchion pad, facing the shooter.
                float aspect = _logo.width / (float)_logo.height;
                var card = new HorseArena.Builder();
                Vector3 c = new Vector3(0, 30f, baseZ - 46.3f);
                card.Card(c + new Vector3(-30f, -30f / aspect, 0), c + new Vector3(-30f, 30f / aspect, 0), c + new Vector3(30f, 30f / aspect, 0), c + new Vector3(30f, -30f / aspect, 0), Vector3.back);
                Flat("Pad logo", Root, card.Build("Pad logo"), Make.Transparent(Color.white, 0.4f, _logo));
            }

            // The glass: 72 x 42, a white border and the shooter's square, padding along the bottom, the shot clock on top.
            float gy = RimH + 15f;
            Shape(PrimitiveType.Cube, "Glass", Root, new Vector3(0, gy, BoardZ), new Vector3(72f, 42f, 1f), glass);
            foreach (float s in new[] { -1f, 1f })
            {
                Shape(PrimitiveType.Cube, "Frame", Root, new Vector3(s * 36.4f, gy, BoardZ), new Vector3(0.8f, 42.8f, 1.6f), steel);
                Shape(PrimitiveType.Cube, "Frame", Root, new Vector3(0, gy + s * 21.4f, BoardZ), new Vector3(73.6f, 0.8f, 1.6f), steel);
                Shape(PrimitiveType.Cube, "Border", Root, new Vector3(s * 34.5f, gy, BoardZ - 0.6f), new Vector3(2f, 39f, 0.15f), white);
                Shape(PrimitiveType.Cube, "Border", Root, new Vector3(0, gy + s * 19.5f, BoardZ - 0.6f), new Vector3(71f, 2f, 0.15f), white);
                Shape(PrimitiveType.Cube, "Square", Root, new Vector3(s * 11f, RimH + 9f, BoardZ - 0.6f), new Vector3(2f, 18f, 0.15f), white);
            }
            Shape(PrimitiveType.Cube, "Square", Root, new Vector3(0, RimH + 17f, BoardZ - 0.6f), new Vector3(24f, 2f, 0.15f), white);
            Shape(PrimitiveType.Cube, "Square", Root, new Vector3(0, RimH + 1f, BoardZ - 0.6f), new Vector3(24f, 2f, 0.15f), white);
            Shape(PrimitiveType.Cube, "Board pad", Root, new Vector3(0, gy - 23.5f, BoardZ + 0.5f), new Vector3(75f, 5f, 4f), pad);
            foreach (float s in new[] { -1f, 1f })
                Shape(PrimitiveType.Cube, "Side pad", Root, new Vector3(s * 37.5f, gy - 15f, BoardZ + 0.5f), new Vector3(3f, 17f, 4f), pad);
            Shape(PrimitiveType.Cube, "Shot clock", Root, new Vector3(0, gy + 30f, BoardZ + 3f), new Vector3(30f, 15f, 9f), black);
            Words(Root, "24", new Vector3(0, gy + 30f, BoardZ - 1.7f), 9f, new Color(1f, 0.15f, 0.1f));

            // The rim: a round iron with a flange back to the glass, and a white net.
            var tube = new List<Vector2>();
            for (int i = 0; i <= 12; i++) { float a = i / 12f * Mathf.PI * 2f; tube.Add(new Vector2(RimR + 0.55f + Mathf.Cos(a) * 0.55f, Mathf.Sin(a) * 0.55f)); }
            GameObject rim = Make.MeshObject("Rim", Root, Make.Lathe(tube, 64), orange);
            rim.transform.localPosition = RimAt;
            Shape(PrimitiveType.Cube, "Flange", Root, new Vector3(0, RimH - 0.5f, (RimR + BoardZ) / 2f + 0.5f), new Vector3(5f, 2f, BoardZ - RimR + 0.5f), orange);
            Shape(PrimitiveType.Cube, "Flange plate", Root, new Vector3(0, RimH - 3f, BoardZ - 1.2f), new Vector3(9f, 8f, 1.2f), orange);

            _net = new GameObject("Net").transform;
            _net.SetParent(Root, false);
            _net.localPosition = RimAt;
            // A diamond net in one stroke: down a strand twisting one way, back up one twisting the other, round all 12 hooks.
            const int hooks = 12, levels = 5;
            const float depth = 17f;
            Vector3 Knot(float slot, int level)
            {
                float a = slot / hooks * Mathf.PI * 2f, t = level / (float)levels;
                float r = RimR * (1f - 0.38f * t + 0.06f * t * t * t);
                return new Vector3(Mathf.Cos(a) * r, -depth * t - 0.3f, Mathf.Sin(a) * r);
            }
            var knots = new List<Vector3>();
            float at = 0f;
            for (int strand = 0; strand < hooks; strand++)
            {
                for (int l = 0; l <= levels; l++) knots.Add(Knot(at + l * 0.5f, l));
                at += levels * 0.5f;
                for (int l = levels - 1; l >= 0; l--) knots.Add(Knot(at + (levels - l) * 0.5f, l));
                at += levels * 0.5f;
            }
            LineRenderer net = Stroke(_net, "Strands", Color.white, 0.55f, false);
            net.sharedMaterial = Make.Glow(new Color(1f, 1f, 1f, 0.85f), false);
            net.numCapVertices = 0;
            net.numCornerVertices = 0;
            Points(net, knots);
        }

        static int _ribbonBrand = -1;
        static Mesh _treadsLow, _treadsHigh, _seatsA, _seatsB, _seatsC, _seatsD, _seatsE, _fascia, _ribbon, _ribbonLogos, _suites, _frontWall, _frontCap, _backWall;

        void BuildBowl()
        {
            if (_treadsLow == null)
            {
                const float D = HorseArena.RowDepth;
                var low = new HorseArena.Builder();
                var high = new HorseArena.Builder();
                var seats = new[] { new HorseArena.Builder(), new HorseArena.Builder(), new HorseArena.Builder(), new HorseArena.Builder(), new HorseArena.Builder() };
                for (int i = 0; i < HorseArena.LowerRows; i++)
                {
                    float y = HorseArena.LowerY(i), prev = i == 0 ? 0f : HorseArena.LowerY(i - 1);
                    HorseArena.Tread(low, i * D, (i + 1) * D, y);
                    HorseArena.Wall(low, i * D, prev, y);
                    HorseArena.SeatRow(seats[i < 7 ? 0 : 1], i * D, y, true);
                }
                // The club level: the fascia, its concourse, then the upper bowl, steeper.
                HorseArena.Tread(high, HorseArena.ClubD, HorseArena.UpperD0, HorseArena.ClubTop);
                for (int j = 0; j < HorseArena.UpperRows; j++)
                {
                    float d = HorseArena.UpperD0 + j * D, y = HorseArena.UpperY(j), prev = j == 0 ? HorseArena.ClubTop : HorseArena.UpperY(j - 1);
                    HorseArena.Tread(high, d, d + D, y);
                    HorseArena.Wall(high, d, prev, y);
                    HorseArena.SeatRow(seats[j < 6 ? 2 : j < 12 ? 3 : 4], d, y, j < 6);
                }
                _treadsLow = low.Build("Lower treads");
                _treadsHigh = high.Build("Upper treads");
                _seatsA = seats[0].Build("Seats A"); _seatsB = seats[1].Build("Seats B"); _seatsC = seats[2].Build("Seats C");
                _seatsD = seats[3].Build("Seats D"); _seatsE = seats[4].Build("Seats E");

                var front = new HorseArena.Builder();
                HorseArena.Wall(front, -6f, 0f, 44f);
                _frontWall = front.Build("Front wall");
                var cap = new HorseArena.Builder();
                HorseArena.Tread(cap, -8f, 0f, 46f);
                HorseArena.Wall(cap, -8f, 40f, 46f);
                _frontCap = cap.Build("Front pad");

                float clubB = HorseArena.ClubBottom, clubT = HorseArena.ClubTop;
                var fascia = new HorseArena.Builder();
                HorseArena.Wall(fascia, HorseArena.ClubD, clubB, clubT);
                _fascia = fascia.Build("Fascia");
                // The ribbon board runs along the top of the fascia, where it clears the lower bowl's back rows.
                var ribbon = new HorseArena.Builder();
                HorseArena.Wall(ribbon, HorseArena.ClubD - 0.5f, clubT - 46f, clubT - 4f);
                _ribbon = ribbon.Build("Ribbon");
                var suites = new HorseArena.Builder();
                HorseArena.Wall(suites, HorseArena.ClubD - 0.5f, clubT - 50f, clubT - 48f);
                HorseArena.Wall(suites, HorseArena.ClubD - 0.5f, clubT - 2f, clubT);
                _suites = suites.Build("Ribbon trim");
                var back = new HorseArena.Builder();
                HorseArena.Wall(back, HorseArena.BackD, HorseArena.UpperY(HorseArena.UpperRows - 1), HorseArena.UpperY(HorseArena.UpperRows - 1) + 500f);
                _backWall = back.Build("Back wall");
            }

            // The ribbon's logos are cut to the logo's shape, so they are made again when the brand changes.
            if (_logo != null && (_ribbonLogos == null || _ribbonBrand != Brand.Version))
            {
                _ribbonBrand = Brand.Version;
                var logos = new HorseArena.Builder();
                Vector2 size = Brand.Fit(_logo, 30f * Brand.Aspect(Brand.Puttr), 30f);
                HorseArena.Logos(logos, HorseArena.ClubD - 1f, HorseArena.ClubTop - 25f, size.x, size.y, 230f);
                _ribbonLogos = logos.Build("Ribbon logos");
            }

            // Seats in the team green, fading toward black as the bowl climbs into the dark.
            Flat("Lower treads", Root, _treadsLow, Make.Lit(new Color(0.13f, 0.13f, 0.14f), 0.2f));
            Flat("Upper treads", Root, _treadsHigh, Make.Lit(new Color(0.06f, 0.06f, 0.07f), 0.2f));
            Flat("Seats", Root, _seatsA, Make.Lit(Team * 1.1f, 0.45f));
            Flat("Seats", Root, _seatsB, Make.Lit(Team * 0.85f, 0.45f));
            Flat("Seats", Root, _seatsC, Make.Lit(TeamDark * 0.9f, 0.4f));
            Flat("Seats", Root, _seatsD, Make.Lit(TeamDark * 0.6f, 0.35f));
            Flat("Seats", Root, _seatsE, Make.Lit(TeamDark * 0.35f, 0.3f));
            Flat("Front wall", Root, _frontWall, Make.Lit(new Color(0.03f, 0.03f, 0.035f), 0.4f));
            Flat("Front pad", Root, _frontCap, Make.Lit(Team, 0.3f));
            Flat("Fascia", Root, _fascia, Make.Lit(new Color(0.02f, 0.02f, 0.025f), 0.4f));
            Flat("Ribbon", Root, _ribbon, Make.Glow(new Color(0.01f, 0.05f, 0.03f, 1f), false)).GetComponent<MeshRenderer>().sortingOrder = -2;
            Flat("Ribbon trim", Root, _suites, Make.Glow(Make.Green, false)).GetComponent<MeshRenderer>().sortingOrder = -2;
            if (_ribbonLogos != null) Flat("Ribbon logos", Root, _ribbonLogos, Make.Glow(Color.white, false, _logo)).GetComponent<MeshRenderer>().sortingOrder = -1;
            Flat("Back wall", Root, _backWall, Make.Lit(new Color(0.015f, 0.015f, 0.02f), 0.2f));
        }

        void BuildBoards()
        {
            // Courtside LED boards along the baseline: Puttr wordmarks on black, the mark on green.
            Material housing = Make.Lit(new Color(0.02f, 0.02f, 0.025f), 0.5f);
            const float z = 262f, half = 330f, h = 34f;
            Shape(PrimitiveType.Cube, "LED board", Root, new Vector3(0, h / 2f + 2f, z + 4f), new Vector3(half * 2f, h + 4f, 8f), housing);
            var dark = new HorseArena.Builder();
            var green = new HorseArena.Builder();
            var words = new HorseArena.Builder();
            var marks = new HorseArena.Builder();
            const int panels = 5;
            float pw = half * 2f / panels;
            for (int i = 0; i < panels; i++)
            {
                float x0 = -half + i * pw, cx = x0 + pw / 2f, y0 = 3f, y1 = 3f + h;
                bool isGreen = i % 2 == 1;
                (isGreen ? green : dark).Card(new Vector3(x0, y0, z - 0.2f), new Vector3(x0, y1, z - 0.2f), new Vector3(x0 + pw, y1, z - 0.2f), new Vector3(x0 + pw, y0, z - 0.2f), Vector3.back);
                if (!isGreen && _logo != null)
                {
                    float lh = 20f, lw = lh * _logo.width / _logo.height, cy = y0 + h / 2f;
                    words.Card(new Vector3(cx - lw / 2f, cy - lh / 2f, z - 0.5f), new Vector3(cx - lw / 2f, cy + lh / 2f, z - 0.5f), new Vector3(cx + lw / 2f, cy + lh / 2f, z - 0.5f), new Vector3(cx + lw / 2f, cy - lh / 2f, z - 0.5f), Vector3.back);
                }
                if (isGreen && _markWhite != null)
                {
                    float mw = Brand.Fit(_markWhite, 34f, 24f).x, mh = mw / Brand.Aspect(_markWhite), cy = y0 + h / 2f;
                    for (int k = -1; k <= 1; k++)
                    {
                        float mx = cx + k * 40f;
                        marks.Card(new Vector3(mx - mw / 2f, cy - mh / 2f, z - 0.5f), new Vector3(mx - mw / 2f, cy + mh / 2f, z - 0.5f), new Vector3(mx + mw / 2f, cy + mh / 2f, z - 0.5f), new Vector3(mx + mw / 2f, cy - mh / 2f, z - 0.5f), Vector3.back);
                    }
                }
            }
            // The polished floor picks up the boards: a soft smear of each panel's light in front of it.
            var shine = new HorseArena.Builder();
            for (int i = 0; i < panels; i++)
            {
                float x0 = -half + i * pw + 4f, x1 = x0 + pw - 8f;
                Color c = i % 2 == 1 ? Make.Green * 0.8f : new Color(0.22f, 0.22f, 0.22f);
                Color off = new Color(c.r, c.g, c.b, 0f); c.a = 1f;
                Vector3 a = new Vector3(x0, 0.3f, z - 2f), b = new Vector3(x1, 0.3f, z - 2f), fa = new Vector3(x0 - 6f, 0.3f, z - 70f), fb = new Vector3(x1 + 6f, 0.3f, z - 70f);
                shine.Tri(a, b, fb, c, c, off);
                shine.Tri(a, fb, fa, c, off, off);
            }
            Flat("LED shine", Root, shine.Build("LED shine"), Make.Glow(Color.white, true));
            Flat("LED dark", Root, dark.Build("LED dark"), Make.Glow(new Color(0.01f, 0.012f, 0.014f, 1f), false)).GetComponent<MeshRenderer>().sortingOrder = -2;
            Flat("LED green", Root, green.Build("LED green"), Make.Glow(Make.Green * 1.1f, false)).GetComponent<MeshRenderer>().sortingOrder = -2;
            if (_logo != null) Flat("LED words", Root, words.Build("LED words"), Make.Glow(Color.white, false, _logo)).GetComponent<MeshRenderer>().sortingOrder = -1;
            if (_markWhite != null) Flat("LED marks", Root, marks.Build("LED marks"), Make.Glow(Color.white, false, _markWhite)).GetComponent<MeshRenderer>().sortingOrder = -1;

            // The video board hung over the baseline: the letters, who is up, the sponsor round its rim.
            Material shell = Make.Lit(new Color(0.03f, 0.03f, 0.035f), 0.6f, 0.4f);
            const float vz = 252f, vy = 281f, vw = 316f, vh = 160f;
            Shape(PrimitiveType.Cube, "Video board", Root, new Vector3(0, vy, vz + 22f), new Vector3(vw + 16f, vh + 16f, 40f), shell);
            var face = new HorseArena.Builder();
            face.Card(new Vector3(-vw / 2f, vy - vh / 2f, vz + 1.5f), new Vector3(-vw / 2f, vy + vh / 2f, vz + 1.5f), new Vector3(vw / 2f, vy + vh / 2f, vz + 1.5f), new Vector3(vw / 2f, vy - vh / 2f, vz + 1.5f), Vector3.back);
            Flat("Screen", Root, face.Build("Screen"), Make.Glow(new Color(0.012f, 0.025f, 0.03f, 1f), false)).GetComponent<MeshRenderer>().sortingOrder = -2;
            var trim = new HorseArena.Builder();
            foreach (float s in new[] { -1f, 1f })
            {
                float ty = vy + s * (vh / 2f - 3f);
                trim.Card(new Vector3(-vw / 2f, ty - 1.2f, vz + 1f), new Vector3(-vw / 2f, ty + 1.2f, vz + 1f), new Vector3(vw / 2f, ty + 1.2f, vz + 1f), new Vector3(vw / 2f, ty - 1.2f, vz + 1f), Vector3.back);
            }
            Flat("Trim", Root, trim.Build("Trim"), Make.Glow(Make.Bright, false)).GetComponent<MeshRenderer>().sortingOrder = -1;
            // A ring of LED under the screen with the sponsor.
            Shape(PrimitiveType.Cube, "Ring", Root, new Vector3(0, vy - vh / 2f - 20f, vz + 14f), new Vector3(vw - 30f, 24f, 24f), shell);
            if (_logo != null)
            {
                var ring = new HorseArena.Builder();
                float lh = 15f, lw = lh * _logo.width / _logo.height, ry = vy - vh / 2f - 20f;
                ring.Card(new Vector3(-(vw - 34f) / 2f, ry - 10f, vz + 1.8f), new Vector3(-(vw - 34f) / 2f, ry + 10f, vz + 1.8f), new Vector3((vw - 34f) / 2f, ry + 10f, vz + 1.8f), new Vector3((vw - 34f) / 2f, ry - 10f, vz + 1.8f), Vector3.back);
                Flat("Ring LED", Root, ring.Build("Ring LED"), Make.Glow(new Color(0.01f, 0.05f, 0.03f, 1f), false)).GetComponent<MeshRenderer>().sortingOrder = -2;
                var ringLogos = new HorseArena.Builder();
                foreach (float x in new[] { -90f, 90f })
                    ringLogos.Card(new Vector3(x - lw / 2f, ry - lh / 2f, vz + 1.5f), new Vector3(x - lw / 2f, ry + lh / 2f, vz + 1.5f), new Vector3(x + lw / 2f, ry + lh / 2f, vz + 1.5f), new Vector3(x + lw / 2f, ry - lh / 2f, vz + 1.5f), Vector3.back);
                Flat("Ring logos", Root, ringLogos.Build("Ring logos"), Make.Glow(Color.white, false, _logo)).GetComponent<MeshRenderer>().sortingOrder = -1;
            }
            foreach (float x in new[] { -130f, 130f })
                Shape(PrimitiveType.Cylinder, "Cable", Root, new Vector3(x, vy + vh / 2f + 500f, vz + 22f), new Vector3(1.2f, 500f, 1.2f), shell);

            for (int i = 0; i < 4; i++) _rows.Add(Words(Root, "", new Vector3(0, vy + 50f - i * 27f, vz), 10.5f, Color.white));
            _call = Words(Root, "", new Vector3(0, vy - 59f, vz), 12f, new Color(1f, 0.75f, 0.2f));
        }

        // The flood over the key: looking straight down it would glare off the polished paint, so it dims while the shot
        // is being called (Tick).
        Light _floodlight;
        const float FloodBright = 2.6e6f, FloodDim = 0.5e6f;

        void BuildLights()
        {
            // The court is lit from the catwalk; the bowl gets only the spill, so it falls off into the dark.
            Light sun = Sun(Root, new Vector3(72f, 18f, 0f), new Color(1f, 0.96f, 0.9f), 0.42f);
            sun.shadowStrength = 0.75f;
            Light fill = World.NewLight(Root, "Fill", LightType.Directional, new Color(0.75f, 0.82f, 1f), 0.22f);
            fill.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
            fill.shadows = LightShadows.None;
            Light Spot(Vector3 from, Vector3 to, Color c, float intensity, float angle, float range)
            {
                Light l = Lamp(Root, from, to, c, intensity, angle, range);
                l.shadows = LightShadows.None;
                l.innerSpotAngle = angle * 0.55f;
                return l;
            }
            _floodlight = Spot(new Vector3(0, 1000f, -260f), new Vector3(0, 0, -150f), new Color(1f, 0.95f, 0.86f), FloodBright, 82f, 2400f);
            Spot(new Vector3(0, 700f, -420f), new Vector3(0, RimH, 0), new Color(1f, 0.97f, 0.92f), 7e5f, 22f, 1800f);
            Spot(new Vector3(0, 900f, -200f), new Vector3(0, 120f, 520f), new Color(0.85f, 0.9f, 1f), 9e5f, 58f, 2400f);

            // Haze: shafts of light from the catwalk.
            Material haze = Make.Glow(Color.white, true);
            foreach (float s in new[] { -1f, 1f })
                Flat("Shaft", Root, HorseArena.Shaft(new Vector3(s * 420f, 1150f, 520f), new Vector3(s * 110f, 0f, -140f), 14f, 120f, 0.07f), haze);
            Flat("Shaft", Root, HorseArena.Shaft(new Vector3(0f, 1150f, 300f), new Vector3(0f, 0f, -80f), 12f, 110f, 0.045f), haze);
            // The catwalk lights themselves, high over the court.
            var lamps = new HorseArena.Builder();
            for (int i = -3; i <= 3; i++)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 c = new Vector3(i * 110f, 1150f, 520f - k * 380f);
                    lamps.Box(c, Vector3.right, Vector3.up, Vector3.forward, new Vector3(16f, 3f, 16f), true);
                }
            Flat("Catwalk lamps", Root, lamps.Build("Catwalk lamps"), Make.Glow(new Color(1f, 0.96f, 0.88f, 1f), false));
        }

        void Aim(ShotView v)
        {
            int feet = v.NextFeet, tee = v.NextTee;
            bool moved = feet != _feet || tee != _tee, wasCalling = _calling;
            _feet = feet; _tee = tee;
            _shotFrom = SpotFor(feet, tee);
            _calling = v.Flag("calling") && !v.Over;
            _pick = _calling && v.Flag("pick");
            if (!_playing)
            {
                bool up = _ball.gameObject.activeSelf;
                if (_calling)
                {
                    // Bounce over to a new call; otherwise start dribbling on it.
                    if (wasCalling && up && moved) { _hopFrom = _ball.localPosition; _hopT = 0f; }
                    else if (!up || !wasCalling) { _hopT = 1f; _dribbleT = 0f; _ball.localPosition = _shotFrom + Vector3.up * BallR; _spot.localPosition = _shotFrom; }
                    _ball.gameObject.SetActive(true);
                }
                else { _ball.gameObject.SetActive(false); _spot.localPosition = _shotFrom; }
            }
            _spotX.gameObject.SetActive(!_calling);
            Vector3 back = new Vector3(_shotFrom.x, 0, _shotFrom.z).normalized;
            if (back.sqrMagnitude < 0.01f) back = Vector3.back;
            // Behind and a little to the left of the shooter, so the hoop sits right of centre (the tee sign is top left).
            Vector3 side = Vector3.Cross(Vector3.up, -back).normalized;
            _cam = _shotFrom + back * 210f - side * 70f + Vector3.up * (Portrait ? 120f : 105f);
            _look = Vector3.Lerp(_shotFrom, RimAt, 0.85f) + Vector3.up * 5f - side * 45f;
        }

        void Show(ShotView v)
        {
            int[] letters = v.Get<int[]>("letters") ?? new int[v.Names.Length];
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= v.Names.Length) { _rows[i].text = ""; continue; }
                int n = i < letters.Length ? letters[i] : 0;
                string word = "";
                for (int k = 0; k < 5; k++) word += (k < n ? "HORSE"[k].ToString() : "_") + " ";
                _rows[i].text = v.Names[i].ToUpperInvariant() + "   " + word.Trim();
                _rows[i].color = n >= 5 ? new Color(0.6f, 0.6f, 0.6f) : i == v.Next ? new Color(1f, 0.75f, 0.2f) : Color.white;
            }
            bool calling = v.Flag("calling");
            string who = v.Next < v.Names.Length ? v.Names[v.Next] : "";
            bool you = who == "You";
            _call.text = v.Over ? "" : calling ? (you ? "Your call" : who + " calls it") : (you ? "You" : who) + " to match" + (v.Flag("swish") ? ": swish" : "");
            string upper = who.ToUpperInvariant();
            _hint.text = v.Over || !calling ? "" : _pick ? (you ? "YOUR CALL" : upper + "'S CALL") + ": TAP A SPOT" : upper + " CALLS IT";
            _hint.gameObject.SetActive(!_playing);
        }

        public override void Begin(ShotView start) { Aim(start); Show(start); }
        public override void Changed(ShotView view) { Aim(view); Show(view); }
        public override void Ready(ShotView view) { Aim(view); Show(view); }

        public override void Tick()
        {
            if (_floodlight != null) _floodlight.intensity = Mathf.MoveTowards(_floodlight.intensity, Overhead ? FloodDim : FloodBright, FloodBright * 2f * Time.deltaTime);
            if (!_calling || _playing || !_ball.gameObject.activeSelf) return;
            Vector3 ground = _shotFrom + Vector3.up * BallR;
            if (_hopT < 1f)
            {
                // The hop to a new spot: an arc over, the ring sliding along under it.
                _hopT = Mathf.Min(1f, _hopT + Time.deltaTime / HopSec);
                float t = _hopT;
                _ball.localPosition = Vector3.Lerp(_hopFrom, ground, t) + Vector3.up * (4f * HopUp * t * (1f - t));
                _ball.Rotate(Vector3.right, 540f * Time.deltaTime, Space.World);
                Vector3 under = Vector3.Lerp(new Vector3(_hopFrom.x, 0, _hopFrom.z), _shotFrom, Ease(t));
                _spot.localPosition = under;
                if (_hopT >= 1f) { _dribbleT = 0f; Sound("court_bounce", 0.55f); }
            }
            else
            {
                // A dribble in place: hard off the floor, easing at the top of the bounce.
                float before = _dribbleT / DribbleSec;
                _dribbleT += Time.deltaTime;
                float ph = _dribbleT / DribbleSec;
                if (Mathf.Floor(ph) > Mathf.Floor(before)) Sound("court_bounce", 0.35f, Random.Range(0.95f, 1.05f));
                _ball.localPosition = ground + Vector3.up * (DribbleUp * Mathf.Abs(Mathf.Sin(ph * Mathf.PI)));
                _ball.Rotate(Vector3.right, 120f * Time.deltaTime, Space.World);
                _spot.localPosition = _shotFrom;
            }
            if (_pick) Taps();
        }

        // A tap on the paint while calling: the nearest mat spot is the call.
        void Taps()
        {
            Pointer p = Pointer.current;
            if (p == null || !p.press.wasPressedThisFrame || SpotPicked == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector2 at = p.position.ReadValue();
            if (!cam.pixelRect.Contains(at)) return;
            Ray ray = cam.ScreenPointToRay(at);
            Vector3 o = Root.InverseTransformPoint(ray.origin), d = Root.InverseTransformVector(ray.direction);
            if (d.y > -1e-5f) return;
            Vector3 hit = o + d * (-o.y / d.y);
            if (Mathf.Abs(hit.x) > HorseArena.LaneHalf + 6f || hit.z > HorseArena.MatZ(3) + 18f || hit.z < HorseArena.PaintEnd - 8f) return;
            int feet = Mathf.Clamp(Mathf.RoundToInt((-hit.z - HorseArena.MatTop) / HorseArena.MatStep) + 3, 3, 11);
            int tee = hit.x < -HorseArena.TeeOff / 2f ? 1 : hit.x > HorseArena.TeeOff / 2f ? 3 : 2;
            if (feet == _feet && tee == _tee) return;
            SpotPicked(feet, tee);
        }

        public override IEnumerator Play(ShotView v)
        {
            // The caller's shot: the camera swings back down to the shooter as the ball comes up off the dribble.
            bool fromCall = _calling && _ball.gameObject.activeSelf;
            _playing = true;
            _hint.gameObject.SetActive(false);
            _spot.localPosition = _shotFrom;
            _spotX.gameObject.SetActive(true);
            Vector3 hand = _shotFrom + Vector3.up * 84f;
            if (fromCall) yield return Arc(_ball, _ball.localPosition, hand, 0.6f, 12f);
            Vector3 toRim = (RimAt - hand); toRim.y = 0; toRim.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, toRim);
            float across = Mathf.Clamp(v.LineIn / 2.1f, -1f, 1f);
            _ball.gameObject.SetActive(true);
            var trail = new List<Vector3>();
            float apex = 70f + Vector3.Distance(hand, RimAt) * 0.12f;

            IEnumerator FlyTo(Vector3 a, Vector3 b, float sec, float up)
            {
                for (float t = 0f; t < 1f; t += Time.deltaTime / sec)
                {
                    Vector3 p = Vector3.Lerp(a, b, t) + Vector3.up * (4f * up * t * (1f - t));
                    _ball.localPosition = p;
                    _ball.Rotate(Vector3.right, -720f * Time.deltaTime, Space.Self);
                    trail.Add(p);
                    Points(_trail, trail);
                    yield return null;
                }
                _ball.localPosition = b;
            }

            if (!v.Reached)
            {
                // Airball: drops short of the rim.
                Vector3 shortOf = RimAt - toRim * 40f + Vector3.down * 60f;
                yield return FlyTo(hand, shortOf, 1.0f, apex * 0.8f);
                yield return FlyTo(shortOf, shortOf + toRim * 30f + Vector3.down * (shortOf.y - BallR), 0.5f, 10f);
                Sound("court_bounce", 0.8f);
            }
            else if (v.Good || (v.Made && !v.BackDoor && v.Title != "NO SWISH"))
            {
                bool bank = v.OffLineDeg > 30f;
                Vector3 entry = RimAt + right * across * (RimR - BallR - 0.5f) + Vector3.up * 2f;
                if (bank)
                {
                    Vector3 glass = new Vector3(entry.x, RimH + 16f, BoardZ - BallR);
                    yield return FlyTo(hand, glass, 1.05f, apex);
                    Sound("court_bounce", 0.6f, 1.4f);
                    yield return FlyTo(glass, entry, 0.18f, 2f);
                }
                else yield return FlyTo(hand, entry, 1.1f, apex);
                // Through the net: it stretches as the ball goes.
                Sound("swish", 1f);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
                {
                    _ball.localPosition = entry + Vector3.down * (t * 30f);
                    _net.localScale = new Vector3(1f, 1f + Mathf.Sin(t * Mathf.PI) * 0.5f, 1f);
                    yield return null;
                }
                _net.localScale = Vector3.one;
                Vector3 floor = new Vector3(entry.x, BallR, entry.z - 10f);
                yield return FlyTo(_ball.localPosition, floor, 0.35f, 0f);
                Sound("court_bounce", 0.6f);
                Sound("horse_cheer", "golf_clap", 0.4f);   // puttr-app-2: the game's own sound, the shared one until recorded
            }
            else
            {
                // Off the rim: the back iron for a back door or a missed swish, the side it missed otherwise.
                float side = v.MissSide == "LEFT" ? -1f : v.MissSide == "RIGHT" ? 1f : (across < 0 ? -1f : 1f);
                Vector3 hit = v.Made ? RimAt + toRim * (RimR + 1f) + Vector3.up * 3f : RimAt + right * side * (RimR + 1.5f) + Vector3.up * 3f;
                yield return FlyTo(hand, hit, 1.1f, apex);
                Sound("horse_rim", "rim_clang", 1f);   // puttr-app-2: the game's own sound, the shared one until recorded
                if (v.Made)
                {
                    // Round the rim and out.
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
                    {
                        float a = Mathf.PI * 0.5f + t * Mathf.PI * 1.6f;
                        _ball.localPosition = RimAt + (right * Mathf.Cos(a) + toRim * Mathf.Sin(a)) * (RimR + 1f) + Vector3.up * 4f;
                        yield return null;
                    }
                }
                Vector3 away = _ball.localPosition + right * side * 60f - toRim * 50f;
                away.y = BallR;
                yield return FlyTo(_ball.localPosition, away, 0.7f, 26f);
                Sound("court_bounce", 0.7f);
            }
            yield return new WaitForSeconds(0.5f);
            _ball.gameObject.SetActive(false);
            _trail.positionCount = 0;
            _playing = false;
        }
    }
}
