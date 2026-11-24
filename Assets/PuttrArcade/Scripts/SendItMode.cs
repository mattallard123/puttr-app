// Send It: a stunt jump, and a game of nothing but pace.
//
// The putt is the run-up. A bike leaves a ramp at 26 degrees and has to come
// down inside the green window on the landing ramp, on the far side of a row
// of buses. Life size, in inches, with real gravity.
//
//   pace   how far past the hole the putt would finish. The first round (three
//          buses) wants a putt that dies in, about 3 in past, and each bus adds
//          1 in (Matt, 2026-11-23: 3 in a bus could not be played far), so the
//          eighth round wants 10 in. The window either side of that is as wide
//          as it was: about 3 in of rollout at first, 2 at the end.
//   line   lean. A putt more than a couple of inches off the center of the
//          hole lands crooked, and that is a fall too.
//
// Every round adds a bus and shortens the window, so every round asks for a
// different, firmer putt. Two lives each; last one riding wins.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class SendItMode : StageMode
    {
        public override string Name => "Send It";
        public override bool Branded => true;
        public override string VoicePrefix => "sendit";
        public override int Putter => _turn; // whose difficulty applies to this putt
        public override string Blurb => "Putt harder to jump further.\nLand in the green window.\nOne more bus every round.";
        public override GameAudio.Scene Sound => GameAudio.Scene.Arena;
        // puttr-app-2: a supercross crowd of its own (the stadium crowd, pitched down, until it is recorded); it ends on the landing roar.
        public override string BedSlot => "sendit_arena_loop";
        public override string EndSlot => "sendit_landing";
        public override int Score(int player) => _best[player];
        public override string ScoreWord(int n) => n == 1 ? "bus" : "buses";
        public override string ScoreUnit => "buses";
        public override string Rule => "The harder the putt, the longer the jump. Land in the green window.";
        public override string Icon => "Motorcycle";
        public override Color Accent => new Color(0.4f, 0.73f, 0.42f);
        public override string[] Tags => new[] { "8 rounds", "Pace" };
        public override int Difficulty => 3;
        public override string[] HowTo => new[]
        {
            "The harder you putt, the further the bike jumps. Too soft lands on the buses, too firm flies past the landing ramp.",
            "Land in the green window. The gauge under the stage shows the pace it needs. The first round wants a putt that dies in the hole, and each bus adds 1 in past it.",
            "Keep it straight: more than about 3 inches off the center of the hole lands crooked. Two lives each.",
        };

        const int Rounds = 8, Lives = 2, FirstBuses = 3;
        const float LipY = 140f, TakeoffDeg = 26f, LandingDeg = 20f;
        const float BusPitch = 102f, BusWidth = 96f, BusTop = 120f, FirstBusX = 30f;
        const float RunUpX = -560f;
        const float GaugeLow = 0f, GaugeHigh = 56f;
        // What each round asks for, and how much a putt off it moves the landing: an inch more a round, and three
        // inches of putt either way carries the bike a bus long or short (so the window is no tighter than it was).
        const float FirstPast = 3f, PastPerRound = 1f, PastPerBusLength = 3f;
        // Takeoff speed squared per inch of rollout off the round's own.
        static readonly float BikeGain = BusPitch / PastPerBusLength * LaneSpec.Gravity / Mathf.Sin(2f * TakeoffDeg * Mathf.Deg2Rad);
        float _middleSpeed2;
        float WantedPast => FirstPast + PastPerRound * _roundIndex;
        // The speed off the lip for this putt: the wanted rollout lands in the middle of the window.
        float BikeSpeed(float pace) => Mathf.Sqrt(Mathf.Max(0f, _middleSpeed2 + BikeGain * (PaceWords.Past(pace) - WantedPast)));
        static float PaceFor(float past) => Mathf.Sqrt(2f * PuttrAnalysis.RampDecelInPerSec2 * past);
        public override bool AimPace(out float low, out float high, out float min, out float max)
        {
            low = _paceLow; high = _paceHigh; min = GaugeLow; max = GaugeHigh;
            return _paceHigh > _paceLow;
        }

        static readonly float Cos = Mathf.Cos(TakeoffDeg * Mathf.Deg2Rad), Sin = Mathf.Sin(TakeoffDeg * Mathf.Deg2Rad);
        static readonly float LandTan = Mathf.Tan(LandingDeg * Mathf.Deg2Rad);
        static readonly float RampBaseX = -LipY / Mathf.Tan(TakeoffDeg * Mathf.Deg2Rad);
        static readonly float LandingRun = LipY / LandTan;

        // The frame: wide enough for the run-up, the buses and the run-out.
        float _centerX = 500f, _width = 1800f;
        public override Vector3 CameraPosition => At(_centerX, _width * 0.21f, -_width * 1.04f);
        public override Vector3 CameraLook => At(_centerX, _width * 0.088f, 0f);
        public override float CameraFov => 30f;
        public override float ShadowDistance => _width * 1.8f;

        Transform _bike, _landing, _zone, _round;
        TrailRenderer _trail;
        readonly List<GameObject> _buses = new List<GameObject>();
        readonly List<GameObject> _newMarks = new List<GameObject>();
        GameObject _needle;

        int _players, _roundIndex, _turn;
        int[] _lives, _best;
        float _landingX, _zoneLength, _paceLow, _paceHigh;

        int Buses => FirstBuses + _roundIndex;
        float LeanLimit => Mathf.Max(2.2f, 3.2f - _roundIndex * 0.15f);

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // An arena at night: one hard key light from in front, a cool one from behind.
            // The key light is high and behind, so shadows fall forward onto
            // the floor where they can be seen; a softer one lights the faces.
            Sun(Root, new Vector3(54f, 152f, 0f), new Color(1f, 0.97f, 0.92f), 1.8f);
            Sun(Root, new Vector3(34f, -24f, 0f), new Color(0.8f, 0.88f, 1f), 1.05f).shadows = LightShadows.None;

            // Poured concrete, with a front edge so the pace gauge can hang below it.
            Props.Concrete(out Texture2D slab, out Texture2D pits);
            Material concrete = Make.Lit(new Color(0.42f, 0.43f, 0.45f), 0.42f, 0f, slab, pits, 0.8f);
            concrete.SetTextureScale("_BaseMap", new Vector2(14000f / 220f, 3000f / 220f));
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(1000f, -3f, 1300f), new Vector3(14000f, 6f, 3000f), concrete);
            Shape(PrimitiveType.Cube, "Floor edge", Root, new Vector3(1000f, -0.6f, -200.5f), new Vector3(14000f, 1.2f, 1f), Make.Emissive(Color.black, Make.Green * 1.2f));
            Material paint = Make.Glow(new Color(1f, 1f, 1f, 0.32f), false);
            Shape(PrimitiveType.Cube, "Lane line", Root, new Vector3(1000f, 0.05f, -72f), new Vector3(14000f, 0.02f, 4f), paint);
            Shape(PrimitiveType.Cube, "Lane line", Root, new Vector3(1000f, 0.05f, 72f), new Vector3(14000f, 0.02f, 4f), paint);

            Material steel = Make.Lit(new Color(0.34f, 0.35f, 0.37f), 0.55f, 0.85f);
            Material panel = Make.Lit(new Color(0.02f, 0.022f, 0.025f), 0.45f, 0.2f);
            Material ply = Make.Lit(new Color(0.16f, 0.12f, 0.08f), 0.2f);
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material carpet = Make.Lit(new Color(0.1f, 0.34f, 0.2f), 0.1f, 0f, pile, pileNormal, 0.9f);
            carpet.SetTextureScale("_BaseMap", new Vector2(12f, 4f));
            Material edge = Make.Emissive(Color.black, Make.Bright * 1.6f);

            // The takeoff ramp: Puttr carpet on a plywood deck, on a steel frame.
            float length = LipY / Sin;
            var takeoff = new GameObject("Takeoff").transform;
            takeoff.SetParent(Root, false);
            takeoff.localPosition = new Vector3(RampBaseX * 0.5f, LipY * 0.5f, 0f);
            takeoff.localRotation = Quaternion.Euler(0, 0, TakeoffDeg);
            Shape(PrimitiveType.Cube, "Deck", takeoff, new Vector3(0, -3.5f, 0), new Vector3(length, 5f, 96f), ply);
            Shape(PrimitiveType.Cube, "Carpet", takeoff, new Vector3(0, -0.6f, 0), new Vector3(length - 2f, 1.2f, 92f), carpet);
            Shape(PrimitiveType.Cube, "Edge light", takeoff, new Vector3(0, 0.3f, -47.4f), new Vector3(length, 1.2f, 1.2f), edge);
            Truss(Root, RampBaseX, steel);
            Make.MeshObject("Takeoff banner", Root, Prism(new Vector2(RampBaseX + 30f, 2f), new Vector2(-4f, LipY - 12f), new Vector2(-4f, 2f), 84f), panel);
            var logo = Brand.Logo;
            if (logo != null)
                Card("Ramp logo", Root, new Vector3(-66f, 34f, -42.6f), Brand.Size(logo, 96f), Make.Glow(new Color(1f, 1f, 1f, 0.9f), false, logo));

            // The landing ramp. It moves out as buses are added.
            _landing = new GameObject("Landing").transform;
            _landing.SetParent(Root, false);
            float run = LandingRun / Mathf.Cos(LandingDeg * Mathf.Deg2Rad);
            var slope = new GameObject("Slope").transform;
            slope.SetParent(_landing, false);
            slope.localPosition = new Vector3(LandingRun * 0.5f, LipY * 0.5f, 0f);
            slope.localRotation = Quaternion.Euler(0, 0, -LandingDeg);
            Shape(PrimitiveType.Cube, "Deck", slope, new Vector3(0, -3.5f, 0), new Vector3(run, 5f, 96f), ply);
            Shape(PrimitiveType.Cube, "Surface", slope, new Vector3(0, -0.6f, 0), new Vector3(run - 2f, 1.2f, 92f), Make.Lit(new Color(0.07f, 0.075f, 0.08f), 0.3f));
            _zone = Shape(PrimitiveType.Cube, "Window", slope, Vector3.zero, new Vector3(100f, 1.6f, 92.5f), Make.Emissive(Make.Green, Make.Bright * 1.5f)).transform;
            Truss(_landing, LandingRun, steel);
            Make.MeshObject("Landing banner", _landing, Prism(new Vector2(4f, 2f), new Vector2(4f, LipY - 12f), new Vector2(LandingRun - 40f, 2f), 84f), panel);

            Color[] colors =
            {
                new Color(0.86f, 0.55f, 0.04f), new Color(0.55f, 0.05f, 0.04f), new Color(0.82f, 0.83f, 0.8f), new Color(0.04f, 0.16f, 0.5f),
                new Color(0.05f, 0.32f, 0.16f), new Color(0.86f, 0.55f, 0.04f), new Color(0.5f, 0.5f, 0.52f),
            };
            for (int i = 0; i < FirstBuses + Rounds; i++) BuildBus(i, colors[i % colors.Length], steel);

            BuildBike(steel);
        }

        // A steel frame under a ramp that climbs from (baseX, 0) to (0, LipY).
        static void Truss(Transform parent, float baseX, Material steel)
        {
            const float tube = 1.7f;
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * 46f;
                Props.Rod("Top chord", parent, new Vector3(baseX, 0, z), new Vector3(0, LipY - 7f, z), tube, steel);
                Props.Rod("Bottom chord", parent, new Vector3(baseX, tube, z), new Vector3(0, tube, z), tube, steel);
                for (int i = 1; i <= 4; i++)
                {
                    float x = baseX * (1f - i / 4f), y = (LipY - 7f) * i / 4f;
                    Props.Rod("Post", parent, new Vector3(x, 0, z), new Vector3(x, y, z), tube, steel);
                    float px = baseX * (1f - (i - 1) / 4f);
                    Props.Rod("Brace", parent, new Vector3(px, tube, z), new Vector3(x, y, z), tube * 0.75f, steel);
                }
            }
            for (int i = 2; i <= 4; i++)
            {
                float x = baseX * (1f - i / 4f), y = (LipY - 7f) * i / 4f;
                Props.Rod("Cross tube", parent, new Vector3(x, y, -46f), new Vector3(x, y, 46f), tube, steel);
            }
        }

        // A bus, nose toward the camera: 8 feet wide, 10 high, 25 long.
        void BuildBus(int index, Color color, Material steel)
        {
            var bus = new GameObject("Bus " + (index + 1)).transform;
            bus.SetParent(Root, false);
            bus.localPosition = new Vector3(FirstBusX + index * BusPitch + BusPitch * 0.5f, 0f, 0f);
            const float nose = -150f, bottom = 15f;
            Material body = Make.Lit(color, 0.78f, 0.15f);
            Material black = Make.Lit(new Color(0.012f, 0.012f, 0.014f), 0.5f);
            Material glass = Make.Lit(new Color(0.02f, 0.03f, 0.04f), 0.95f, 0.6f);
            Material rubber = Make.Lit(new Color(0.012f, 0.012f, 0.012f), 0.15f);
            Material chrome = Make.Lit(new Color(0.7f, 0.7f, 0.72f), 0.85f, 1f);
            Material lamp = Make.Emissive(Color.white, new Color(1f, 0.9f, 0.68f) * 4f);
            Material amber = Make.Emissive(new Color(0.4f, 0.2f, 0f), Make.Amber * 1.4f);
            Material sign = Make.Emissive(Color.black, new Color(1f, 0.62f, 0.1f) * 0.55f);

            Make.Panel("Body", bus, new Vector3(0, bottom + (BusTop - bottom) * 0.5f, 0), new Vector3(BusWidth, BusTop - bottom, 300f), 7f, body);
            Make.Panel("Roof unit", bus, new Vector3(0, BusTop + 3f, 30f), new Vector3(60f, 8f, 110f), 3f, Make.Lit(new Color(0.75f, 0.75f, 0.75f), 0.4f));
            // The face: a black mask with two panes of glass, a route sign above and a grille below.
            Make.Panel("Mask", bus, new Vector3(0, 86f, nose - 0.4f), new Vector3(BusWidth - 10f, 58f, 1.2f), 0.5f, black);
            for (int side = -1; side <= 1; side += 2)
            {
                Make.Panel("Windscreen", bus, new Vector3(side * 21f, 82f, nose - 1.1f), new Vector3(39f, 42f, 0.8f), 0.3f, glass);
                Props.Rod("Wiper", bus, new Vector3(side * 21f, 62f, nose - 1.8f), new Vector3(side * 12f, 80f, nose - 1.8f), 0.45f, black);
                Shape(PrimitiveType.Sphere, "Headlight", bus, new Vector3(side * 34f, 40f, nose - 0.4f), new Vector3(11f, 11f, 3.5f), lamp);
                Shape(PrimitiveType.Cylinder, "Headlight ring", bus, new Vector3(side * 34f, 40f, nose + 0.3f), new Vector3(13.5f, 0.8f, 13.5f), chrome).transform.localRotation = Quaternion.Euler(90f, 0, 0);
                Shape(PrimitiveType.Cube, "Indicator", bus, new Vector3(side * 43.5f, 40f, nose - 0.4f), new Vector3(4f, 7f, 2f), amber);
                Props.Rod("Mirror arm", bus, new Vector3(side * 47f, 98f, nose + 6f), new Vector3(side * 55f, 96f, nose - 4f), 0.7f, black);
                Make.Panel("Mirror", bus, new Vector3(side * 55.5f, 91f, nose - 4.5f), new Vector3(5f, 13f, 2f), 0.8f, black);
                Make.Panel("Side glass", bus, new Vector3(side * (BusWidth * 0.5f + 0.2f), 88f, 8f), new Vector3(0.8f, 34f, 262f), 0.3f, glass);
                for (int axle = 0; axle < 2; axle++)
                {
                    float z = axle == 0 ? -100f : 95f;
                    Shape(PrimitiveType.Cylinder, "Tyre", bus, new Vector3(side * 39f, 19f, z), new Vector3(38f, 6.5f, 38f), rubber).transform.localRotation = Quaternion.Euler(0, 0, 90f);
                    Shape(PrimitiveType.Cylinder, "Hub", bus, new Vector3(side * 45.6f, 19f, z), new Vector3(20f, 0.5f, 20f), chrome).transform.localRotation = Quaternion.Euler(0, 0, 90f);
                }
            }
            Make.Panel("Route sign", bus, new Vector3(0, 110f, nose - 1f), new Vector3(58f, 9f, 0.8f), 0.3f, sign);
            Words(bus, (index % 2 == 0 ? "PUTTR " : "ARCADE ") + (11 + index * 7 % 80), new Vector3(0, 110f, nose - 1.6f), 4.2f, new Color(1f, 0.7f, 0.2f));
            Make.Panel("Grille", bus, new Vector3(0, 41f, nose - 0.5f), new Vector3(44f, 13f, 1f), 0.4f, black);
            Make.Panel("Bumper", bus, new Vector3(0, 22f, nose - 2f), new Vector3(BusWidth + 1f, 10f, 6f), 2f, steel);
            Make.Panel("Plate", bus, new Vector3(0, 22f, nose - 5.2f), new Vector3(18f, 5.5f, 0.4f), 0.2f, Make.Lit(new Color(0.9f, 0.9f, 0.85f), 0.4f));

            GameObject mark = Shape(PrimitiveType.Cube, "New this round", bus, new Vector3(0, BusTop + 1.2f, nose + 2f), new Vector3(BusWidth - 12f, 2.4f, 2.4f), Make.Emissive(Color.black, Make.Amber * 3f));
            _buses.Add(bus.gameObject);
            _newMarks.Add(mark);
        }

        // A motocross bike and its rider, facing +X, standing on the origin.
        void BuildBike(Material steel)
        {
            _bike = new GameObject("Bike").transform;
            _bike.SetParent(Root, false);
            var body = new GameObject("Body").transform;
            body.SetParent(_bike, false);
            body.localScale = Vector3.one * 1.3f;

            Material rubber = Make.Lit(new Color(0.015f, 0.015f, 0.015f), 0.2f);
            Material chrome = Make.Lit(new Color(0.75f, 0.75f, 0.78f), 0.85f, 1f);
            Material plastic = Make.Lit(new Color(0.18f, 0.62f, 0.2f), 0.7f);
            Material engine = Make.Lit(new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.7f);
            Material seat = Make.Lit(new Color(0.02f, 0.02f, 0.02f), 0.3f);
            Material plate = Make.Lit(new Color(0.92f, 0.92f, 0.9f), 0.5f);
            Mesh tyre = Props.Torus(10.6f, 2.5f);
            for (int side = -1; side <= 1; side += 2)
            {
                var axle = new Vector3(side * 28f, 13f, 0);
                Make.MeshObject("Tyre", body, tyre, rubber).transform.SetLocalPositionAndRotation(axle, Quaternion.Euler(90f, 0, 0));
                Shape(PrimitiveType.Cylinder, "Rim", body, axle, new Vector3(17.5f, 0.5f, 17.5f), chrome).transform.localRotation = Quaternion.Euler(90f, 0, 0);
                Shape(PrimitiveType.Cylinder, "Hub", body, axle, new Vector3(4.5f, 2.2f, 4.5f), engine).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * 3.4f;
                Props.Rod("Fork", body, new Vector3(18.5f, 40f, z), new Vector3(28f, 13f, z), 1.3f, chrome);
                Props.Rod("Swing arm", body, new Vector3(-28f, 13f, z), new Vector3(-3f, 19f, z), 1.3f, steel);
            }
            Props.Rod("Frame", body, new Vector3(18f, 37f, 0), new Vector3(2f, 12f, 0), 1.2f, plastic);
            Props.Rod("Frame", body, new Vector3(18f, 37f, 0), new Vector3(-6f, 33f, 0), 1.2f, plastic);
            Props.Rod("Subframe", body, new Vector3(-6f, 31f, 0), new Vector3(-27f, 34f, 0), 1.1f, steel);
            Props.Rod("Shock", body, new Vector3(-8f, 30f, 0), new Vector3(-14f, 17f, 0), 1.5f, chrome);
            Make.Panel("Engine", body, new Vector3(1f, 20f, 0), new Vector3(15f, 14f, 9f), 2.5f, engine);
            Make.Panel("Tank", body, new Vector3(7f, 33.5f, 0), new Vector3(19f, 8.5f, 9.5f), 3.5f, plastic);
            Make.Panel("Seat", body, new Vector3(-11f, 35.6f, 0), new Vector3(23f, 4f, 7.5f), 1.8f, seat);
            Make.Panel("Rear fender", body, new Vector3(-26f, 34f, 0), new Vector3(20f, 2.4f, 6.5f), 1f, plastic).transform.localRotation = Quaternion.Euler(0, 0, 12f);
            Make.Panel("Front fender", body, new Vector3(28f, 28.5f, 0), new Vector3(20f, 2f, 6f), 0.9f, plastic).transform.localRotation = Quaternion.Euler(0, 0, -6f);
            Make.Panel("Number plate", body, new Vector3(22.5f, 37f, 0), new Vector3(1f, 10f, 9f), 0.4f, plate).transform.localRotation = Quaternion.Euler(0, 0, -20f);
            Props.Rod("Bars", body, new Vector3(18.5f, 42.5f, -11f), new Vector3(18.5f, 42.5f, 11f), 0.8f, engine);
            Props.Rod("Exhaust", body, new Vector3(4f, 15f, -4.6f), new Vector3(-22f, 27f, -4.6f), 1.7f, chrome);
            Props.Rod("Pegs", body, new Vector3(-4f, 13.5f, -8f), new Vector3(-4f, 13.5f, 8f), 0.7f, steel);

            // The rider: crouched over the bars, feet on the pegs.
            Material suit = Make.Lit(new Color(0.9f, 0.9f, 0.9f), 0.4f);
            var rider = new Figure(body, new Figure.Look
            {
                Top = suit, Bottom = suit, Sock = suit, Skin = suit,
                Boot = Make.Lit(new Color(0.03f, 0.03f, 0.03f), 0.5f),
                Glove = Make.Lit(new Color(0.05f, 0.05f, 0.05f), 0.4f),
                Helmet = Make.Lit(new Color(0.95f, 0.6f, 0.02f), 0.85f),
                Visor = Make.Lit(new Color(0.02f, 0.02f, 0.03f), 0.95f, 0.5f),
            });
            rider.Root.localPosition = new Vector3(-9.5f, 39f, 0f);
            rider.Root.localRotation = Quaternion.Euler(0, -90f, 0);
            rider.Chest.localRotation = Quaternion.Euler(-34f, 0, 0);
            rider.Head.localRotation = Quaternion.Euler(26f, 0, 0);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                rider.Arm[i].localRotation = Quaternion.Euler(80f, 0, side * 5f);
                rider.Fore[i].localRotation = Quaternion.Euler(-14f, 0, 0);
                rider.Leg[i].localRotation = Quaternion.Euler(66f, 0, side * 9f);
                rider.Shin[i].localRotation = Quaternion.Euler(-112f, 0, 0);
            }

            var tail = new GameObject("Trail");
            tail.transform.SetParent(_bike, false);
            tail.transform.localPosition = new Vector3(-30f, 30f, 0f);
            _trail = tail.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = Make.Glow(new Color(0.6f, 0.9f, 0.5f, 0.45f));
            _trail.time = 0.9f;
            _trail.startWidth = 9f;
            _trail.endWidth = 0f;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Lay the round out: the buses, the landing ramp, the window and the gauge.
        void SetRound()
        {
            int n = Buses;
            for (int i = 0; i < _buses.Count; i++)
            {
                _buses[i].SetActive(i < n);
                _newMarks[i].SetActive(i == n - 1 && _roundIndex > 0);
            }
            _landingX = FirstBusX + n * BusPitch + 20f;
            _landing.localPosition = new Vector3(_landingX, 0, 0);
            _zoneLength = Mathf.Max(150f, 230f - 11f * _roundIndex);
            float along = _zoneLength / Mathf.Cos(LandingDeg * Mathf.Deg2Rad);
            float run = LandingRun / Mathf.Cos(LandingDeg * Mathf.Deg2Rad);
            _zone.localPosition = new Vector3(-run * 0.5f + along * 0.5f, 0.2f, 0f);
            _zone.localScale = new Vector3(along, 1.6f, 92.5f);

            // The putts that make it, found by flying every pace.
            float lowSpeed = 100f, highSpeed = 1600f, middle = _landingX + _zoneLength * 0.5f;
            for (int i = 0; i < 28; i++)
            {
                float mid = (lowSpeed + highSpeed) * 0.5f;
                Fly(mid, out Vector2 down, out _);
                if (down.x < middle) lowSpeed = mid; else highSpeed = mid;
            }
            _middleSpeed2 = lowSpeed * lowSpeed;
            _paceLow = _paceHigh = 0f;
            for (float pace = 0.5f; pace < 95f; pace += 0.05f)
            {
                if (Fly(BikeSpeed(pace), out _, out _) != Landing.Clear) continue;
                if (_paceLow == 0f) _paceLow = pace;
                _paceHigh = pace;
            }

            float left = RampBaseX - 170f, right = _landingX + LandingRun + 110f;
            _centerX = (left + right) * 0.5f;
            _width = right - left;

            if (_round != null) Make.Kill(_round.gameObject);
            _round = new GameObject("Round").transform;
            _round.SetParent(Root, false);
            float w = _width, z = -206f;
            float gaugeW = w * 0.4f, gaugeY = -w * 0.046f, bar = w * 0.011f;
            float X(float pace) => _centerX - gaugeW * 0.5f + gaugeW * Mathf.InverseLerp(GaugeLow, GaugeHigh, pace);
            Card("Track", _round, new Vector3(_centerX, gaugeY, z), new Vector2(gaugeW, bar), Make.Glow(new Color(1f, 1f, 1f, 0.14f), false));
            Card("Window", _round, new Vector3((X(_paceLow) + X(_paceHigh)) * 0.5f, gaugeY, z - 1f), new Vector2(X(_paceHigh) - X(_paceLow), bar), Make.Glow(new Color(0.48f, 0.83f, 0.27f, 1f), false));
            _needle = Card("Needle", _round, new Vector3(_centerX, gaugeY, z - 2f), new Vector2(w * 0.0035f, bar * 2.6f), Make.Glow(Color.white, false));
            _needle.SetActive(false);
            Color quiet = new Color(1f, 1f, 1f, 0.6f);
            float text = w * 0.0085f;
            Words(_round, "SHORT", new Vector3(X(GaugeLow) + text * 2f, gaugeY + bar * 2f, z), text, quiet);
            Words(_round, "LONG", new Vector3(X(GaugeHigh) - text * 2f, gaugeY + bar * 2f, z), text, quiet);
            Words(_round, "CLEAR", new Vector3((X(_paceLow) + X(_paceHigh)) * 0.5f, gaugeY + bar * 2f, z), text, Make.Bright);
            foreach (float past in new[] { 3f, 6f, 12f, 18f })
                Words(_round, PaceWords.Length(past) + " past", new Vector3(X(PaceFor(past)), gaugeY - bar * 2f, z), text * 0.9f, quiet);
            ShowAim(); // the card's pace window is this round's, now that it is known
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = players;
            _roundIndex = 0;
            _turn = 0;
            _lives = new int[players];
            _best = new int[players];
            var names = new List<string>();
            for (int i = 0; i < players; i++) { names.Add(Arcade.PlayerName(i)); _lives[i] = Lives; }
            Arcade.Hud.BuildBoard(names, Lives, "BEST", true, "LIVES");
            SetRound();
            ShowBoard();
            Park();
        }

        // Behind the home screen and the game card: the jump, without the pace gauge, whose
        // words sat over the row of game cards. Begin lays a new one out.
        public override void Preview()
        {
            if (_round != null) _round.gameObject.SetActive(false);
            Park();
        }

        public override IEnumerator Prepare()
        {
            Park();
            yield break;
        }

        void Park()
        {
            _trail.emitting = false;
            _trail.Clear();
            PlaceBike(new Vector2(RunUpX, 0f), 0f);
            if (_needle != null) _needle.SetActive(false);
        }

        public override string Prompt
        {
            get
            {
                string who = _players > 1 ? Arcade.PlayerName(_turn) + "   ·   " : "";
                return who + Buses + " buses   ·   " + (_roundIndex == 0 ? "Die it in the hole" : "Putt to finish about " + PaceWords.Length(WantedPast) + " past the hole");
            }
        }

        enum Landing { Clear, Short, Long }

        // Fly the bike from the lip at this speed. Where does it come down?
        Landing Fly(float speed, out Vector2 where, out float seconds)
        {
            const float dt = 1f / 480f;
            float vx = speed * Cos, vy = speed * Sin;
            var p = new Vector2(0f, LipY);
            float busEnd = FirstBusX + Buses * BusPitch;
            for (seconds = 0f; seconds < 12f; seconds += dt)
            {
                var last = p;
                vy -= LaneSpec.Gravity * dt;
                p += new Vector2(vx, vy) * dt;
                where = p;
                if (p.x < _landingX)
                {
                    bool overBus = p.x > FirstBusX && p.x < busEnd;
                    if (p.y <= (overBus ? BusTop : 0f)) return Landing.Short;
                    continue;
                }
                // Reaching the ramp below its top is a hit on its front.
                if (last.x < _landingX && p.y < LipY) return Landing.Short;
                float surface = Mathf.Max(0f, LipY - (p.x - _landingX) * LandTan);
                if (p.y <= surface)
                {
                    where = new Vector2(p.x, surface);
                    return p.x <= _landingX + _zoneLength ? Landing.Clear : Landing.Long;
                }
            }
            where = p;
            return Landing.Long;
        }

        public override IEnumerator Play(PuttResult putt)
        {
            int player = _turn, round = _roundIndex, buses = Buses;
            float pace = Pace(putt), line = Line(putt);
            float speed = BikeSpeed(pace);
            // A putt that never reached the hole is short of every window, the first round's "dies in" too.
            if (!putt.Launch.Reached) { pace = Mathf.Min(pace, 6f); speed = Mathf.Sqrt(Mathf.Max(80f * 80f, _middleSpeed2 - BikeGain * (WantedPast + 5f))); }
            Landing landing = Fly(speed, out Vector2 touch, out float airtime);
            bool crooked = landing == Landing.Clear && Mathf.Abs(line) > LeanLimit;

            _needle.SetActive(true);
            Vector3 needle = _needle.transform.localPosition;
            float gaugeW = _width * 0.4f;
            needle.x = _centerX - gaugeW * 0.5f + gaugeW * Mathf.InverseLerp(GaugeLow, GaugeHigh, pace);
            _needle.transform.localPosition = needle;

            // The run-up: along the floor and up the ramp, reaching full speed at the lip.
            _trail.Clear();
            _trail.emitting = true;
            float flat = RampBaseX - RunUpX, ramp = LipY / Sin, total = flat + ramp;
            float gone = 0f;
            while (gone < total)
            {
                // However weak the putt, the run-up does not dawdle.
                float v = Mathf.Max(speed, 300f) * Mathf.Lerp(0.5f, 1f, gone / total);
                gone += v * Time.deltaTime;
                if (gone < flat) PlaceBike(new Vector2(RunUpX + gone, 0f), 0f);
                else
                {
                    float up = Mathf.Min(gone - flat, ramp);
                    PlaceBike(new Vector2(RampBaseX + up * Cos, up * Sin), TakeoffDeg);
                }
                Arcade.Audio.SetRoll(v * 0.12f);
                yield return null;
            }

            // In the air.
            // Wheels off the ground: nothing to push against, so it revs.
            Arcade.Audio.SetEngine(95f);
            bool slowed = false, shot = false;
            float lean = Mathf.Clamp(line, -6f, 6f) * 6f;
            for (float t = 0; t < airtime; t += Time.deltaTime)
            {
                var p = new Vector2(speed * Cos * t, LipY + speed * Sin * t - 0.5f * LaneSpec.Gravity * t * t);
                float vy = speed * Sin - LaneSpec.Gravity * t;
                PlaceBike(p, Mathf.Atan2(vy, speed * Cos) * Mathf.Rad2Deg * 0.6f + 8f, lean * Mathf.Sin(t * 5f) * Mathf.Clamp01(t * 2f));
                if (!slowed && vy < 40f && airtime > 0.5f) { slowed = true; Arcade.SlowMotion(0.45f); }
                if (!shot && t > airtime * 0.55f) { shot = true; Arcade.Shot("action"); }
                yield return null;
            }

            Hud hud = Arcade.Hud;
            bool cleared = landing == Landing.Clear && !crooked;
            string cell;
            if (cleared)
            {
                cell = "CLEAR";
                float middle = Mathf.Abs(touch.x - (_landingX + _zoneLength * 0.5f)) / (_zoneLength * 0.5f);
                ShowPutt(putt, "Cleared " + buses, Make.Bright);
                hud.Banner("SENT IT", middle < 0.3f ? "Middle of the window: " + buses + " buses" : "Cleared " + buses + " buses", Make.Bright, 2.2f);
                Arcade.Audio.Land();
                Arcade.Audio.SetEngine(30f);
                if (Arcade.Audio.TryLibrary("sendit_landing", 1f)) Arcade.Audio.Excite(1f); else Arcade.Audio.Strike();   // puttr-app-2
                Arcade.Celebrate(80, At(touch.x, touch.y, -60f));
                Arcade.Sparks(At(touch.x, touch.y, -40f), 14);
                _best[player] = Mathf.Max(_best[player], buses);
            }
            else
            {
                string sub;
                if (crooked) { cell = "WIDE"; sub = "Right pace, but " + Mathf.Abs(line).ToString("0.0") + " in off line: it landed leaning"; hud.Banner("CROOKED", sub, Make.Amber, 2.2f); }
                else if (landing == Landing.Short)
                {
                    cell = "SHORT";
                    int bus = Mathf.Clamp(Mathf.FloorToInt((touch.x - FirstBusX) / BusPitch) + 1, 1, buses);
                    sub = touch.x < FirstBusX ? "Barely left the ramp" : touch.x > FirstBusX + buses * BusPitch ? "Into the front of the landing ramp" : "Came down on bus " + bus;
                    hud.Banner("SHORT", sub, Make.Red, 2.2f);
                }
                else { cell = "LONG"; sub = "Flew the window"; hud.Banner("LONG", sub, Make.Red, 2.2f); }
                ShowPutt(putt, crooked ? "Crooked" : landing == Landing.Short ? "Short" : "Long", crooked ? Make.Amber : Make.Red);
                Arcade.Audio.Crash();
                Arcade.Audio.SetEngine(0f);
                if (Arcade.Audio.TryLibrary("sendit_crash_groan", 0.85f)) Arcade.Audio.Excite(0.25f); else Arcade.Audio.Groan();   // puttr-app-2
                Arcade.Shake(0.9f);
                Arcade.Sparks(At(touch.x, touch.y + 20f, -40f), 60);
                _lives[player]--;
            }
            ShowBoard();

            // After the landing: ride it out, or go over the bars.
            float away = speed * 0.8f, fall = cleared ? 0f : 260f, spin = 0f, roll = TiltAt(touch.x, cleared);
            var at = touch;
            bool shotResult = false;
            for (float t = 0; t < 1.5f; t += Time.deltaTime)
            {
                if (cleared)
                {
                    away = Mathf.Max(0f, away - speed * 0.55f * Time.deltaTime);
                    at.x += away * Time.deltaTime;
                    at.y = FloorAt(at.x);
                    roll = Mathf.Lerp(roll, TiltAt(at.x, true), Time.deltaTime * 10f);
                }
                else
                {
                    fall -= LaneSpec.Gravity * Time.deltaTime;
                    at += new Vector2(away * 0.45f, fall) * Time.deltaTime;
                    float floor = FloorAt(at.x);
                    if (at.y < floor) { at.y = floor; fall = Mathf.Abs(fall) * 0.4f; away *= 0.6f; }
                    spin += (crooked ? 240f : 620f) * Time.deltaTime * Mathf.Clamp01(away / 100f + 0.2f);
                    roll = -spin;
                }
                PlaceBike(at, roll, crooked ? Mathf.Min(80f, t * 220f) : 0f);
                if (!shotResult && t > 0.35f) { shotResult = true; Arcade.Shot("result"); }
                yield return null;
            }
            _trail.emitting = false;
            Arcade.Audio.SetEngine(0f);
            yield return new WaitForSecondsRealtime(0.6f);

            // Whose turn next, and is the round over?
            int next = player;
            bool wrapped = false;
            for (int i = 0; i < _players; i++)
            {
                next++;
                if (next >= _players) { next = 0; wrapped = true; }
                if (_lives[next] > 0) break;
            }
            _turn = next;
            if (wrapped && !Over)
            {
                _roundIndex++;
                if (!Over) SetRound();
            }
            ShowBoard();
        }

        // Lives as marks, the best jump as the number, and the round across the top.
        void ShowBoard()
        {
            Hud hud = Arcade.Hud;
            for (int i = 0; i < _players; i++)
            {
                for (int life = 0; life < Lives; life++) hud.SetPip(i, life, life < _lives[i] ? Hud.Pip.Good : Hud.Pip.Bad);
                hud.SetTotal(i, _best[i]);
            }
            hud.SetTurn(Over ? -1 : _turn, -1);
            if (!Over) hud.SetBoardTitle("ROUND " + (_roundIndex + 1) + " OF " + Rounds + "      " + Buses + " BUSES");
        }

        int Riding()
        {
            int n = 0;
            for (int i = 0; i < _players; i++) if (_lives[i] > 0) n++;
            return n;
        }

        public override bool Over => _roundIndex >= Rounds || Riding() == 0 || (_players > 1 && Riding() == 1);

        public override void Final(out string title, out string body)
        {
            body = "";
            if (_players == 1)
            {
                title = _best[0] > 0 ? _best[0] + "  B U S E S" : "N O   B U S E S";
                body = _lives[0] > 0 ? "Every round cleared.\n" : _best[0] > 0 ? "The best jump cleared " + _best[0] + ".\n" : "No clean landing this time.\n";
                return;
            }
            title = "L A S T   O N E   R I D I N G";
            int best = 0;
            for (int i = 0; i < _players; i++)
                if (_lives[i] > _lives[best] || (_lives[i] == _lives[best] && _best[i] > _best[best])) best = i;
            for (int i = 0; i < _players; i++)
                body += Arcade.PlayerName(i) + "     " + _best[i] + " buses" + (i == best ? "     winner" : _lives[i] == 0 ? "     out" : "") + "\n";
        }

        public override void End() => Park();

        // ── Where things are ────────────────────────────────────────────────

        // A triangle seen from the front (a, b, c clockwise), pushed back to make a solid.
        static Mesh Prism(Vector2 a, Vector2 b, Vector2 c, float depth)
        {
            float f = -depth * 0.5f, k = depth * 0.5f;
            var verts = new List<Vector3> { new Vector3(a.x, a.y, f), new Vector3(b.x, b.y, f), new Vector3(c.x, c.y, f) };
            var tris = new List<int> { 0, 1, 2 };
            Vector2[] ring = { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                Vector2 p = ring[i], q = ring[(i + 1) % 3];
                int n = verts.Count;
                verts.Add(new Vector3(p.x, p.y, f)); verts.Add(new Vector3(p.x, p.y, k));
                verts.Add(new Vector3(q.x, q.y, k)); verts.Add(new Vector3(q.x, q.y, f));
                tris.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
            }
            var mesh = new Mesh { name = "Prism" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // What is under this x: floor, bus roof or landing ramp.
        float FloorAt(float x)
        {
            if (x > FirstBusX && x < FirstBusX + Buses * BusPitch) return BusTop;
            if (x >= _landingX && x < _landingX + LandingRun) return LipY - (x - _landingX) * LandTan;
            return 0f;
        }

        float TiltAt(float x, bool riding) => riding && x >= _landingX && x < _landingX + LandingRun ? -LandingDeg : 0f;

        // pitch turns the bike in the plane of the jump; lean tips it toward the camera.
        void PlaceBike(Vector2 p, float pitch, float lean = 0f)
        {
            _bike.localPosition = new Vector3(p.x, p.y, 0f);
            _bike.localRotation = Quaternion.Euler(lean, 0f, pitch);
        }
    }
}
