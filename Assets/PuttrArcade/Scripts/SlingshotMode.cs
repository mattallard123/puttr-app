// Slingshot: a gravity puzzle, seen from above.
//
// The putt launches a probe from the rail on the left. Planets pull it off
// its line: too soft and it falls in, too firm and it leaves the sector.
// Reach the relay in as few putts as you can.
//
//   line   where on the rail the probe starts. A putt that crosses the
//          hole left of center starts higher up the screen, as if the screen
//          were the mat turned a quarter turn. An inch is 18 units, so the
//          width of the cup covers most of the rail.
//   pace   speed. The probe always sets off straight across.
//
// Every attempt stays on screen as a ghost, so the next putt can be read off
// the last. Each sector was checked with the same sum that flies the probe:
// between 3 and 10 putts in every 100 ordinary ones reach the relay.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class SlingshotMode : StageMode
    {
        public override string Name => "Slingshot";
        public override string VoicePrefix => "slingshot";
        public override int Putter => _player; // whose difficulty applies to this putt
        public override string Blurb => "A gravity puzzle.\nLine sets the start, pace the speed.\nBend it round to the relay.";
        public override GameAudio.Scene Sound => GameAudio.Scene.Space;
        // puttr-app-2: mission complete (the arcade's game-over until it is recorded).
        public override string EndSlot => "slingshot_win";
        public override int Score(int player) => Totals()[player];
        public override string ScoreWord(int n) => n == 1 ? "putt" : "putts";
        public override bool LowerIsBetter => true;
        public override string ScoreUnit => "putts";
        public override string Rule => "Line sets where the probe starts. Pace sets how fast it goes.";
        public override string Icon => "Planet";
        public override Color Accent => new Color(0.48f, 0.83f, 0.27f);
        public override string[] Tags => new[] { "5 sectors", "Line and pace" };
        public override string[] HowTo => new[]
        {
            "Each putt launches the probe straight across from the rail on the left. Get it into the green relay ring on the right.",
            "Where the putt crosses the hole sets where on the rail it starts: left of center starts higher, right of center starts lower.",
            "Pace is speed. Planets pull the probe toward them: slow putts bend a lot, firm putts bend a little. Fewest putts wins.",
        };

        const int MaxPutts = 5;
        const float RailX = -138f, RailHalf = 40f, GateRadius = 11f;
        const float UnitsPerInchOfLine = 18f, UnitsPerPace = 1.5f;
        const float Step = 1f / 240f;

        struct Planet { public float X, Y, Radius, Pull; public Color Color; }
        sealed class Sector { public string Name; public Planet[] Planets; public Vector2 Gate; }

        static readonly Color Blue = new Color(0.25f, 0.5f, 0.72f), Rust = new Color(0.75f, 0.45f, 0.16f), Jade = new Color(0.2f, 0.6f, 0.45f), Rose = new Color(0.65f, 0.3f, 0.36f);

        static readonly Sector[] Sectors =
        {
            new Sector { Name = "First light", Gate = new Vector2(128, -52), Planets = new[] { new Planet { X = 10, Y = -38, Radius = 14, Pull = 50000, Color = Blue } } },
            new Sector { Name = "The long way up", Gate = new Vector2(120, 40), Planets = new[] { new Planet { X = -20, Y = 25, Radius = 16, Pull = 70000, Color = Rust } } },
            new Sector { Name = "Slalom", Gate = new Vector2(130, 0), Planets = new[] { new Planet { X = -30, Y = -30, Radius = 14, Pull = 50000, Color = Jade }, new Planet { X = 50, Y = 30, Radius = 14, Pull = 50000, Color = Blue } } },
            new Sector { Name = "Eclipse", Gate = new Vector2(130, 0), Planets = new[] { new Planet { X = 0, Y = 0, Radius = 13, Pull = 42000, Color = Rose } } },
            new Sector { Name = "Thread the needle", Gate = new Vector2(128, 10), Planets = new[] { new Planet { X = -45, Y = 14, Radius = 14, Pull = 50000, Color = Blue }, new Planet { X = 60, Y = -24, Radius = 11, Pull = 36000, Color = Rust } } },
        };

        public override Vector3 CameraPosition => At(0f, 0f, -355f);
        public override Vector3 CameraLook => At(0f, 0f, 0f);
        public override float CameraFov => 30f;
        // The card: the first sector's planet, close, with the probe's course bending round it.
        public override Vector3 CardFrom => At(22f, -47f, -355f);
        public override Vector3 CardLook => At(22f, -47f, 0f);
        public override float CardFov => 10.5f;

        Transform _level, _ghosts, _probe, _craft, _station;
        GameObject _flame;
        readonly List<Transform> _spinning = new List<Transform>();
        LineRenderer _trail;
        Material _gateRing;
        readonly List<Vector3> _points = new List<Vector3>();
        int _players, _sector, _player, _putts;
        int[,] _score;
        bool _announce;
        float _gatePulse;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // One sun, off to the left: every planet shows the same phase.
            Sun(Root, new Vector3(18f, 58f, 0f), new Color(1f, 0.97f, 0.92f), 2.4f).shadows = LightShadows.None;

            // Stars at many depths, so they drift apart as the camera sways,
            // and a few drifts of gas a long way behind them.
            Texture2D dot = Make.SoftDot();
            Material[] star =
            {
                Make.Glow(new Color(1f, 1f, 1f, 0.9f), true, dot), Make.Glow(new Color(0.75f, 0.88f, 1f, 0.6f), true, dot),
                Make.Glow(new Color(1f, 0.9f, 0.74f, 0.5f), true, dot), Make.Glow(new Color(1f, 1f, 1f, 0.28f), true, dot),
                Make.Glow(new Color(1f, 1f, 1f, 0.16f), true, dot),
            };
            var rng = new System.Random(3);
            var sky = new GameObject("Stars").transform;
            sky.SetParent(Root, false);
            for (int i = 0; i < 520; i++)
            {
                float depth = 40f + (float)rng.NextDouble() * 420f, spread = 1f + depth / 355f;
                var at = new Vector3(((float)rng.NextDouble() - 0.5f) * 390f * spread, ((float)rng.NextDouble() - 0.5f) * 220f * spread, depth);
                float bright = (float)(rng.NextDouble() * rng.NextDouble() * rng.NextDouble());
                float size = (0.45f + bright * 3.2f) * spread;
                Card("Star", sky, at, new Vector2(size, size), star[bright > 0.35f ? rng.Next(3) : 3 + rng.Next(2)]);
            }
            Color[] gas = { new Color(0.16f, 0.26f, 0.6f, 0.2f), new Color(0.5f, 0.18f, 0.36f, 0.14f), new Color(0.1f, 0.4f, 0.36f, 0.12f) };
            Vector3[] where = { new Vector3(-240f, 110f, 520f), new Vector3(270f, -90f, 560f), new Vector3(60f, 170f, 600f) };
            for (int i = 0; i < gas.Length; i++)
                Card("Nebula", sky, where[i], new Vector2(620f, 520f), Make.Glow(gas[i], true, Props.Nebula(40 + i * 9)));

            // The launch rail: a thin scale, nothing more.
            Color rail = new Color(0.48f, 0.83f, 0.27f, 0.5f);
            Material railPaint = Make.Glow(rail, false);
            Card("Rail", Root, new Vector3(RailX, 0, 0.5f), new Vector2(0.3f, RailHalf * 2f), railPaint);
            for (int i = -4; i <= 4; i++)
                Card("Tick", Root, new Vector3(RailX, i * RailHalf / 4f, 0.5f), new Vector2(i % 2 == 0 ? 3.4f : 2f, 0.28f), railPaint);
            Words(Root, "LEFT OF CENTER", new Vector3(RailX, RailHalf + 5f, 0), 1.8f, rail);
            Words(Root, "CENTER", new Vector3(RailX - 9f, 0, 0), 1.6f, rail);
            Words(Root, "RIGHT OF CENTER", new Vector3(RailX, -RailHalf - 5f, 0), 1.8f, rail);

            // The probe: a foil-wrapped box, a dish, and two solar panels.
            _probe = new GameObject("Probe").transform;
            _probe.SetParent(Root, false);
            _craft = new GameObject("Craft").transform;
            _craft.SetParent(_probe, false);
            _craft.localScale = Vector3.one * 1.15f;
            Material foil = Make.Lit(new Color(0.85f, 0.62f, 0.2f), 0.7f, 0.9f);
            Material cell = Make.Emissive(new Color(0.02f, 0.05f, 0.2f), new Color(0.02f, 0.06f, 0.2f), 0.85f);
            Material white = Make.Lit(new Color(0.9f, 0.9f, 0.9f), 0.5f, 0.2f);
            Material strut = Make.Lit(new Color(0.5f, 0.5f, 0.52f), 0.6f, 0.9f);
            Make.Panel("Bus", _craft, Vector3.zero, new Vector3(3.2f, 2.3f, 2.3f), 0.4f, foil);
            for (int side = -1; side <= 1; side += 2)
            {
                Props.Rod("Boom", _craft, new Vector3(0, side * 1f, 0), new Vector3(0, side * 2.2f, 0), 0.12f, strut);
                Shape(PrimitiveType.Cube, "Solar panel", _craft, new Vector3(0, side * 4.3f, 0), new Vector3(2.1f, 4.2f, 0.12f), cell);
            }
            Shape(PrimitiveType.Sphere, "Dish", _craft, new Vector3(1.9f, 0, -0.5f), new Vector3(0.7f, 2.6f, 2.6f), white);
            Props.Rod("Nozzle", _craft, new Vector3(-1.5f, 0, 0), new Vector3(-2.3f, 0, 0), 0.5f, strut);
            _flame = Card("Engine", _craft, new Vector3(-3.6f, 0, 0), new Vector2(5f, 2.4f), Make.Glow(new Color(1f, 0.8f, 0.5f, 0.9f), true, dot));
            Card("Beacon", _probe, new Vector3(0, 0, 3f), new Vector2(15f, 15f), Make.Glow(new Color(0.48f, 0.83f, 0.27f, 0.3f), true, dot));
            _trail = Stroke(Root, "Trail", new Color(0.48f, 0.83f, 0.27f, 0.85f), 0.55f);

            _ghosts = new GameObject("Ghosts").transform;
            _ghosts.SetParent(Root, false);
        }

        // Each world's surface is made once and kept.
        static readonly Dictionary<Color, (Texture2D, Texture2D)> Surfaces = new Dictionary<Color, (Texture2D, Texture2D)>();

        static void Surface(Color kind, out Texture2D color, out Texture2D normal, out bool gas, out Color air)
        {
            gas = kind == Jade || kind == Rose;
            air = kind == Blue ? new Color(0.45f, 0.7f, 1f) : kind == Rust ? new Color(1f, 0.62f, 0.4f) : kind == Jade ? new Color(0.55f, 1f, 0.8f) : new Color(1f, 0.75f, 0.7f);
            if (!Surfaces.TryGetValue(kind, out (Texture2D, Texture2D) made))
            {
                Texture2D c, n;
                if (kind == Blue) Props.Planet(11, new Color(0.01f, 0.05f, 0.2f), new Color(0.05f, 0.24f, 0.45f), new Color(0.7f, 0.78f, 0.74f), false, out c, out n);
                else if (kind == Rust) Props.Planet(23, new Color(0.2f, 0.07f, 0.03f), new Color(0.52f, 0.24f, 0.1f), new Color(0.82f, 0.56f, 0.36f), false, out c, out n);
                else if (kind == Jade) Props.Planet(37, new Color(0.03f, 0.16f, 0.13f), new Color(0.16f, 0.46f, 0.36f), new Color(0.74f, 0.86f, 0.7f), true, out c, out n);
                else Props.Planet(51, new Color(0.26f, 0.08f, 0.12f), new Color(0.62f, 0.32f, 0.3f), new Color(0.96f, 0.82f, 0.7f), true, out c, out n);
                made = (c, n);
                Surfaces[kind] = made;
            }
            color = made.Item1;
            normal = made.Item2;
        }

        void BuildSector(int index)
        {
            if (_level != null) Make.Kill(_level.gameObject);
            _level = new GameObject("Sector").transform;
            _level.SetParent(Root, false);
            _spinning.Clear();
            Sector sector = Sectors[index];
            Texture2D dot = Make.SoftDot();

            int count = 0;
            foreach (Planet planet in sector.Planets)
            {
                var at = new Vector3(planet.X, planet.Y, 0f);
                float r = planet.Radius;
                Surface(planet.Color, out Texture2D skin, out Texture2D relief, out bool gas, out Color air);
                var axis = new GameObject("Planet").transform;
                axis.SetParent(_level, false);
                axis.localPosition = at;
                axis.localRotation = Quaternion.Euler(12f, 0, count++ % 2 == 0 ? 18f : -24f);
                Transform globe = Shape(PrimitiveType.Sphere, "Globe", axis, Vector3.zero, Vector3.one * r * 2f, Make.Lit(Color.white, gas ? 0.3f : 0.18f, 0f, skin, relief, 1f)).transform;
                _spinning.Add(globe);
                // Air: a thin bright edge over the disc, and a faint glow beyond it.
                Color edge = air; edge.a = 0.55f;
                Card("Atmosphere", _level, at + Vector3.back * (r + 0.5f), Vector2.one * (r * 2f / 0.86f), Make.Glow(edge, true, Props.Rim()));
                Color halo = air; halo.a = 0.14f;
                Card("Halo", _level, at + Vector3.forward * 6f, new Vector2(r * 6f, r * 6f), Make.Glow(halo, true, dot));
                if (planet.Color == Rose)
                {
                    GameObject ring = Hoop("Ring", _level, at, r * 1.28f, r * 1.72f, Make.Glow(new Color(0.95f, 0.82f, 0.72f, 0.4f), false));
                    ring.transform.localRotation = Quaternion.Euler(-20f, 0f, 14f);
                }
                // How far its pull reaches, drawn as fainter and fainter rings.
                float reach = Mathf.Sqrt(planet.Pull) / 13f;
                for (int i = 1; i <= 3; i++)
                {
                    Color ringColor = Color.Lerp(air, Color.white, 0.4f); ringColor.a = 0.2f - i * 0.05f;
                    float rr = r + reach * i * 0.6f;
                    Hoop("Well", _level, at + Vector3.forward * 2f, rr - 0.12f, rr + 0.12f, Make.Glow(ringColor, false));
                }
            }

            // The relay: a ring station, turning.
            var gate = new Vector3(sector.Gate.x, sector.Gate.y, 0f);
            _station = new GameObject("Relay").transform;
            _station.SetParent(_level, false);
            _station.localPosition = gate;
            Material hull = Make.Lit(new Color(0.62f, 0.64f, 0.68f), 0.55f, 0.85f);
            Material beacon = Make.Emissive(Color.black, Make.Bright * 3f);
            GameObject wheel = Make.MeshObject("Wheel", _station, Props.Torus(GateRadius - 0.5f, 0.6f), hull);
            wheel.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                Props.Rod("Spoke", _station, dir * 1.4f, dir * (GateRadius - 1f), 0.18f, hull);
                Shape(PrimitiveType.Sphere, "Light", _station, dir * (GateRadius - 0.5f) + Vector3.back * 0.7f, Vector3.one * 0.9f, beacon);
            }
            Shape(PrimitiveType.Sphere, "Hub", _station, Vector3.zero, Vector3.one * 3f, hull);
            _gateRing = Make.Glow(Make.Bright);
            Hoop("Relay light", _level, gate + Vector3.back * 0.8f, GateRadius - 1.5f, GateRadius - 1.25f, _gateRing);
            Hoop("Relay outer", _level, gate, GateRadius + 3.2f, GateRadius + 3.4f, Make.Glow(new Color(0.48f, 0.83f, 0.27f, 0.3f), false));
            Card("Relay glow", _level, gate + Vector3.forward * 3f, new Vector2(GateRadius * 5f, GateRadius * 5f), Make.Glow(new Color(0.48f, 0.83f, 0.27f, 0.12f), true, dot));
            Words(_level, "RELAY", gate + new Vector3(0, -GateRadius - 7f, 0), 2f, Make.Bright);
            BuildGuide(sector);
            ShowTitle();
        }

        // ── The putt that lands it ──────────────────────────────────────────
        //
        // Every sector has a line and pace that reach the relay. Work it out
        // by flying the probe from every point on the rail, then show it on
        // the rail: the entry line, the putt that gets there, and the course it flies.

        /// <summary>Fly the probe from this height on the rail. True if it reaches the relay.</summary>
        static bool Flies(Sector sector, float y0, float speed, List<Vector3> path)
        {
            var p = new Vector2(RailX, y0);
            var v = new Vector2(speed, 0f);
            const float step = 1f / 120f;
            for (float clock = 0f; clock < 10f; clock += step)
            {
                Vector2 pull = Vector2.zero;
                foreach (Planet planet in sector.Planets)
                {
                    Vector2 d = new Vector2(planet.X, planet.Y) - p;
                    float r = d.magnitude;
                    if (r < planet.Radius) return false;
                    pull += d * (planet.Pull / (r * r * r));
                }
                v += pull * step;
                p += v * step;
                if (path != null && (path.Count == 0 || (path[path.Count - 1] - (Vector3)p).sqrMagnitude > 4f)) path.Add(p);
                if ((p - sector.Gate).sqrMagnitude < GateRadius * GateRadius) return true;
                if (Mathf.Abs(p.x) > 175f || Mathf.Abs(p.y) > 105f) return false;
            }
            return false;
        }

        // The ideal-putt card shows the putt the guide asks for (Matt, 2026-11-23: the guide said 2.1 in right of center
        // while the card drew it straight in the middle).
        float _aimLine;
        public override float AimTargetInches => _aimLine;
        public override float AimGoalInches => PuttrArcade.Arcade.HoleInches;

        void BuildGuide(Sector sector)
        {
            _aimLine = 0f;
            // The widest stretch of rail that works, at the pace nearest a normal putt that has one.
            float bestPace = 0f, bestLow = 0f, bestHigh = 0f;
            foreach (float pace in new[] { 40f, 44f, 36f, 48f, 32f, 52f, 28f, 56f })
            {
                float low = 0f, runLow = 0f;
                bool inRun = false;
                float wide = 0f, at0 = 0f, at1 = 0f;
                for (float y = -RailHalf; y <= RailHalf + 0.01f; y += 0.25f)
                {
                    bool ok = Flies(sector, y, pace * UnitsPerPace, null);
                    if (ok && !inRun) { inRun = true; runLow = y; }
                    if ((!ok || y + 0.25f > RailHalf) && inRun)
                    {
                        inRun = false;
                        low = runLow;
                        float high = ok ? y : y - 0.25f;
                        if (high - low > wide) { wide = high - low; at0 = low; at1 = high; }
                    }
                }
                if (wide > bestHigh - bestLow || bestPace == 0f && wide > 0f) { bestPace = pace; bestLow = at0; bestHigh = at1; }
                if (bestHigh - bestLow >= 1.5f) break; // a normal pace does it: no need to ask for anything odd
            }
            if (bestPace == 0f) { ShowAim(); return; }

            float aimY = (bestLow + bestHigh) * 0.5f;
            float line = -aimY / UnitsPerInchOfLine;                       // inches right of center
            float zone = Mathf.Max(0.06f, (bestHigh - bestLow) / UnitsPerInchOfLine);
            Color green = new Color(0.48f, 0.83f, 0.27f);
            _aimLine = Mathf.Abs(line) < 0.1f ? 0f : line;
            ShowAim();

            // On the rail: where to launch from.
            Card("Aim zone", _level, new Vector3(RailX, aimY, 0.4f), new Vector2(1.6f, Mathf.Max(0.8f, bestHigh - bestLow)), Make.Glow(new Color(green.r, green.g, green.b, 0.55f), false));
            Card("Aim mark", _level, new Vector3(RailX + 8.2f, aimY, 0.4f), new Vector2(13f, 0.45f), Make.Glow(green, false));
            // Above that line: the putt that sends it there.
            float off = Mathf.Abs(line);
            string where = off < 0.1f ? "Dead center" : off.ToString("0.0") + " in " + (line < 0f ? "left" : "right") + " of center";
            Words(_level, where, new Vector3(RailX + 2.2f, aimY + 7.2f, 0), 3.2f, Color.white, TextAnchor.MiddleLeft);
            Words(_level, "to finish " + PaceWords.Finish(bestPace).ToLowerInvariant(), new Vector3(RailX + 2.2f, aimY + 3.3f, 0), 2.3f, new Color(green.r, green.g, green.b, 0.95f), TextAnchor.MiddleLeft);

            // Across the sector: the course it flies.
            var course = new List<Vector3>();
            Flies(sector, aimY, bestPace * UnitsPerPace, course);
            LineRenderer path = Stroke(_level, "Course", new Color(green.r, green.g, green.b, 0.16f), 0.5f, false);
            path.positionCount = course.Count;
            path.SetPositions(course.ToArray());
        }

        void ShowTitle()
        {
            if (Over) return;
            Arcade.Hud.SetBoardTitle("SECTOR " + (_sector + 1) + " OF " + Sectors.Length + "      " + Sectors[_sector].Name.ToUpperInvariant() + "      PAR 3");
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = players;
            _sector = 0;
            _player = 0;
            _putts = 0;
            _score = new int[players, Sectors.Length];
            var names = new List<string>();
            for (int i = 0; i < players; i++) names.Add(Arcade.PlayerName(i));
            Arcade.Hud.BuildBoard(names, Sectors.Length, "TOTAL", true, "PUTTS IN EACH SECTOR");
            for (int i = 0; i < players; i++) Arcade.Hud.SetTotal(i, 0);
            Arcade.Hud.SetTurn(0, 0);
            ClearGhosts();
            BuildSector(0);
            Park();
            _announce = false; // the arcade announces the game itself
        }

        public override IEnumerator Prepare()
        {
            Park();
            if (_announce && !Over)
            {
                _announce = false;
                string who = _players > 1 ? Arcade.PlayerName(_player) + "   ·   " : "";
                Arcade.Hud.Banner("SECTOR " + (_sector + 1), who + Sectors[_sector].Name, Color.white, 1.6f);
            }
            yield break;
        }

        void Park()
        {
            _probe.localPosition = new Vector3(RailX, 0, 0);
            _craft.localRotation = Quaternion.identity;
            _flame.SetActive(false);
            _points.Clear();
            _trail.positionCount = 0;
        }

        public override string Prompt
        {
            get
            {
                string who = _players > 1 ? Arcade.PlayerName(_player) + "   ·   " : "";
                return who + "Sector " + (_sector + 1) + "   ·   Putt " + (_putts + 1) + " of " + MaxPutts;
            }
        }

        enum Fate { Relay, FellIn, Escaped, Adrift }

        public override IEnumerator Play(PuttResult putt)
        {
            Sector sector = Sectors[_sector];
            _putts++;
            float pace = Pace(putt);
            float speed = Mathf.Clamp(pace, 15f, 90f) * UnitsPerPace;
            var p = new Vector2(RailX, Mathf.Clamp(-Line(putt) * UnitsPerInchOfLine, -RailHalf, RailHalf));
            var v = new Vector2(speed, 0f); // always straight across

            Arcade.Audio.Launch();
            _points.Clear();
            _points.Add(p);
            Fate end = Fate.Adrift;
            bool done = false, shot = false;
            float clock = 0f, owed = 0f;
            while (!done)
            {
                owed += Time.deltaTime;
                while (owed >= Step && !done)
                {
                    owed -= Step;
                    clock += Step;
                    Vector2 pull = Vector2.zero;
                    foreach (Planet planet in sector.Planets)
                    {
                        Vector2 d = new Vector2(planet.X, planet.Y) - p;
                        float r = d.magnitude;
                        if (r < planet.Radius) { end = Fate.FellIn; done = true; }
                        pull += d * (planet.Pull / (r * r * r));
                    }
                    if (done) break;
                    v += pull * Step;
                    p += v * Step;
                    if ((p - sector.Gate).sqrMagnitude < GateRadius * GateRadius) { end = Fate.Relay; done = true; }
                    else if (Mathf.Abs(p.x) > 175f || Mathf.Abs(p.y) > 105f) { end = Fate.Escaped; done = true; }
                    else if (clock > 10f) { end = Fate.Adrift; done = true; }
                }
                _probe.localPosition = p;
                _craft.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
                _flame.SetActive(clock < 0.6f);
                if ((_points[_points.Count - 1] - (Vector3)p).sqrMagnitude > 0.5f) _points.Add(p);
                _trail.positionCount = _points.Count;
                _trail.SetPositions(_points.ToArray());
                Arcade.Audio.SetRoll(v.magnitude * 0.3f);
                if (!shot && clock > 1.3f) { shot = true; Arcade.Shot("action"); }
                yield return null;
            }
            Arcade.Audio.SetRoll(0f);

            Hud hud = Arcade.Hud;
            bool last = _putts >= MaxPutts;
            switch (end)
            {
                case Fate.Relay:
                    ShowPutt(putt, "Relay locked", Make.Bright);
                    hud.Banner("RELAY LOCKED", _putts == 1 ? "First putt" : "In " + _putts + " putts", Make.Bright, 2.2f);
                    if (Arcade.Audio.TryLibrary("slingshot_relay_lock", 0.9f)) Arcade.Audio.Excite(1f);   // puttr-app-2: its own lock-on, the shared bell and coin until recorded
                    else { Arcade.Audio.Bell(); Arcade.Audio.Coin(); }
                    Arcade.Celebrate(90, At(sector.Gate.x, sector.Gate.y - 3f, 0f));
                    _gatePulse = 1f;
                    break;
                case Fate.FellIn:
                    ShowPutt(putt, "Fell in", Make.Amber);
                    hud.Banner("PULLED IN", "Putt firmer, or start further from the planet", Make.Amber, 2.2f);
                    if (!Arcade.Audio.TryLibrary("slingshot_pulled_in", 0.9f)) Arcade.Audio.Boom();   // puttr-app-2
                    Arcade.Sparks(At(p.x, p.y, -1f), 40);
                    Arcade.Shake(0.4f);
                    break;
                case Fate.Escaped:
                    ShowPutt(putt, "Escaped", Make.Red);
                    hud.Banner("MISSED", p.y > sector.Gate.y ? "Went above the relay" : "Went below the relay", Make.Red, 2.2f);
                    if (!Arcade.Audio.TryLibrary("slingshot_adrift", 0.8f)) Arcade.Audio.Miss();   // puttr-app-2
                    break;
                default:
                    ShowPutt(putt, "Adrift", Make.Amber);
                    hud.Banner("ADRIFT", "Caught in orbit", Make.Amber, 1.8f);
                    if (!Arcade.Audio.TryLibrary("slingshot_adrift", 0.8f, 0.92f)) Arcade.Audio.Miss();   // puttr-app-2
                    break;
            }
            if (end != Fate.Relay) LeaveGhost(_putts + (end == Fate.FellIn ? "  fell in" : end == Fate.Escaped ? "  escaped" : "  adrift"), end == Fate.FellIn);
            Arcade.Shot("result");
            yield return new WaitForSecondsRealtime(1.5f);

            if (end == Fate.Relay || last)
            {
                _score[_player, _sector] = end == Fate.Relay ? _putts : MaxPutts + 1;
                ShowScores();
                _putts = 0;
                ClearGhosts();
                _player++;
                if (_player >= _players)
                {
                    _player = 0;
                    _sector++;
                    if (!Over) BuildSector(_sector);
                }
                _announce = true;
            }
            hud.SetTurn(Over ? -1 : _player, Over ? -1 : _sector);
        }

        public override bool Over => _sector >= Sectors.Length;

        public override void Final(out string title, out string body)
        {
            int[] total = Totals();
            body = "";
            if (_players == 1)
            {
                title = total[0] + "  P U T T S";
                body = "Three a sector is par: " + Sectors.Length * 3 + ".\n";
                return;
            }
            title = "A L L   S E C T O R S";
            int best = 0;
            for (int i = 0; i < _players; i++) if (total[i] < total[best]) best = i;
            for (int i = 0; i < _players; i++)
                body += Arcade.PlayerName(i) + "     " + total[i] + " putts" + (i == best ? "     winner" : "") + "\n";
        }

        public override void Tick()
        {
            // The relay breathes, and flares when the probe arrives.
            if (_gateRing == null) return;
            _gatePulse = Mathf.MoveTowards(_gatePulse, 0f, Time.unscaledDeltaTime * 0.8f);
            Color c = Make.Bright * (1.1f + 0.25f * Mathf.Sin(Time.unscaledTime * 2.4f) + _gatePulse * 2.5f);
            c.a = 1f;
            _gateRing.SetColor("_Color", c);
            if (_station != null) _station.Rotate(0, 0, Time.deltaTime * 14f, Space.Self);
            foreach (Transform globe in _spinning) if (globe != null) globe.Rotate(0, Time.deltaTime * -5f, 0, Space.Self);
        }

        public override void End()
        {
            ClearGhosts();
            Park();
        }

        // ── Scores and ghosts ───────────────────────────────────────────────

        int[] Totals()
        {
            var total = new int[_players];
            for (int i = 0; i < _players; i++)
                for (int s = 0; s < Sectors.Length; s++) total[i] += _score[i, s];
            return total;
        }

        void ShowScores()
        {
            int[] total = Totals();
            for (int i = 0; i < _players; i++)
            {
                for (int s = 0; s < Sectors.Length; s++)
                    Arcade.Hud.SetPip(i, s, _score[i, s] == 0 ? Hud.Pip.Empty : _score[i, s] <= 3 ? Hud.Pip.Good : Hud.Pip.Over, _score[i, s] > 0 ? _score[i, s].ToString() : "");
                Arcade.Hud.SetTotal(i, total[i]);
            }
        }

        // Keep this attempt on screen: a faint line, and a note at its end.
        void LeaveGhost(string note, bool crashed)
        {
            LineRenderer ghost = Stroke(_ghosts, "Ghost", new Color(1f, 1f, 1f, 0.3f), 0.5f, false);
            ghost.positionCount = _points.Count;
            ghost.SetPositions(_points.ToArray());
            Vector3 tip = _points[_points.Count - 1];
            tip.x = Mathf.Clamp(tip.x, -150f, 132f);
            tip.y = Mathf.Clamp(tip.y, -58f, 58f);
            if (crashed) Words(_ghosts, "x", tip, 2.6f, Make.Red);
            Words(_ghosts, note, tip + new Vector3(0, crashed ? 6f : -5f, 0), 2.1f, new Color(1f, 1f, 1f, 0.75f));
            _points.Clear();
            _trail.positionCount = 0;
        }

        void ClearGhosts()
        {
            if (_ghosts == null) return;
            for (int i = _ghosts.childCount - 1; i >= 0; i--) Make.Kill(_ghosts.GetChild(i).gameObject);
        }
    }
}
