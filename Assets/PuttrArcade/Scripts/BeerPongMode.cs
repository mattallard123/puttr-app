// Beer Pong (puttr-app BeerPong.js, rebuilt in 3D): two Players, a table, ten red cups racked 4-3-2-1 at each end.
//
// Each cup is a putt (a distance and a tee, the same cup for both sides). Hole it through the front of the cup and a
// ping-pong ball drops into that cup, landing as far off center as the putt went into the hole; the cup comes off.
// Miss and the ball clips the rim on the side you missed and bounces away; in the back door, it spins round the rim
// and pops out. Two putts a turn. Clear the rack while shooting first and the other side gets a rebuttal: they shoot
// until they miss, and if they clear theirs too it's overtime, three cups each.
//
// Alone, you play Puttr: it shoots its own two putts a turn, making more of the short cups than the long ones.
//
// Fewer cups, smaller target: a make only drops in the cup when it went into the hole within the entry window, which
// narrows from the whole front of the hole (a full rack, ±90°) to ±20° on the last cup. A make outside it rims out.
// The window is drawn top right (the HUD's aim card): the hole, the window lit green, and where the last putt went in.
//
// The table is black, with the Puttr logo facing each shooter, and it stands outside on a practice putting green.
//
// Units are inches. The table runs along z, the shooter's end is -z for Player 1 and +z for Player 2.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class BeerPongMode : StageMode
    {
        public override string Name => "Beer Pong";
        public override bool Branded => true;
        public override string Blurb => "Ten cups each.\nHole it and the ball\ndrops in the cup.";
        public override string Rule => "Two putts a turn. Each make sinks a cup. Clear all ten first.";
        public override string Icon => "Trophy";
        public override Color Accent => new Color(0.86f, 0.16f, 0.2f);
        public override string[] Tags => new[] { "1 or 2 players", "Makes" };
        public override string[] HowTo => new[]
        {
            "Ten cups each, against a friend or against Puttr. Every cup is its own putt.",
            "Hole it through the front and the ball drops in the cup. Two putts a turn.",
            "Clear the rack first. Clear it shooting first and they get a rebuttal.",
        };
        public override int MaxPlayers => 2;
        public override string SoloOpponent => "Puttr";
        public override bool DealsTee => false;
        public override bool HasLevels => false;
        public override bool ShowsAim => false;
        public override bool HasPortrait => true;
        public override string Tee => null;
        public override int Putter => _p;
        // puttr-app-2: no background sound (Matt, 2026-11-21). The winner's cheer plays at the win (End), so the arcade
        // adds no game-over once it is recorded.
        public override GameAudio.Scene Sound => GameAudio.Scene.Quiet;
        public override string VoicePrefix => "english";   // the announcer who calls the makes (GameVoice.English)
        public override string EndSlot => GameAudio.Has("beerpong_win") ? "" : "game_over";
        public override float ShadowDistance => 260f;
        public override int Score(int player) => player < 2 ? Count(player) : 0;

        const float HalfLen = 48f, HalfW = 12f, TableY = 30f;
        const float CupTop = 1.85f, CupBottom = 1.25f, CupH = 4.7f, Gap = 3.95f, BallR = 0.79f;

        // The camera stands behind whoever is shooting.
        float _side = -1f, _sideShown = -1f;
        public override Vector3 CameraPosition => At(0f, TableY + (Portrait ? 58f : 36f), _sideShown * (HalfLen + (Portrait ? 52f : 40f)));
        public override Vector3 CameraLook => At(0f, TableY, -_sideShown * 22f);
        public override float CameraFov => Portrait ? 46f : 38f;
        public override Vector3 CardFrom => At(26f, TableY + 26f, -HalfLen - 22f);
        public override Vector3 CardLook => At(0f, TableY + 2f, 28f);
        public override float CardFov => 34f;

        readonly Transform[][] _cups = { new Transform[10], new Transform[10] };   // [rack][cup]: rack 0 is at +z (Player 1 shoots at it)
        readonly bool[][] _sunk = { new bool[10], new bool[10] };                  // [player][cup]: cups that Player has sunk
        readonly System.Random _dice = new System.Random();
        readonly int[] _feet = new int[10], _tee = new int[10];
        Transform _ball;
        LineRenderer _trail;
        int _p, _inTurn, _first, _winner = -1;
        bool _turnAllMade = true, _rebuttal, _overtime, _over, _house;

        // Player 2 is Puttr when you play alone.
        string Who(int p) => _house && p == 1 ? "Puttr" : Arcade.PlayerName(p);

        int Count(int p) => System.Array.FindAll(_sunk[p], s => s).Length;

        // ── The entry window ────────────────────────────────────────────────

        /// <summary>Degrees either side of a straight-in entry that still drop in the cup: ±90 with ten cups left, ±20 with one.</summary>
        public static float WindowFor(int cupsLeft) => Mathf.Lerp(20f, 90f, Mathf.Clamp01((cupsLeft - 1) / 9f));
        float Window => WindowFor(10 - Count(_p));
        int _calledCup = -1;

        /// <summary>puttr-app-2: whether this putt drops a cup, as Play decides it: holed, not in the back, and in through the
        /// window. The announcer's make or miss follows it, not the putt alone (Matt, 2026-11-21).</summary>
        public bool Cups(PuttResult putt)
        {
            if (!putt.Made) return false;
            bool backDoor = putt.Launch.Reached && Mathf.Abs(putt.Launch.HeadingDeg) >= 80f || putt.HasEntry && Mathf.Abs(putt.OffCenter) >= 90f;
            return !backDoor && !(putt.HasEntry && Mathf.Abs(putt.OffCenter) > Window);
        }


        // The hole seen from above, the shooter's side at the bottom: the rim, the window lit green round the front,
        // and (after a putt) a dot where it went in, white for a cup and amber for a rim-out.
        void Gauge(float window, float? entry, bool made)
        {
            // puttr-app-2: on the 3D cup, close in, with the ideal line, the window lit on the rim and where the putt went in (Matt, 2026-11-21).
            if (Arcade == null || Arcade.Hud == null) return;
            int c = Target;
            int feet = c >= 0 ? _feet[c] : DealtFeetNow, tee = c >= 0 ? _tee[c] : DealtTee;
            string words = window >= 89.5f ? "Any make off the front drops in" : "Go in within " + Mathf.RoundToInt(window) + "\u00B0 of center";
            Arcade.ShowEntryWindow(feet, tee, window, entry, made, words, "REQUIRED ENTRY");
        }

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // An afternoon on the practice green. The sun is off to one side, so neither shooter looks into it and the
            // table throws its shadow across the grass; a cool light fills the shade, and the sky lights from above.
            Sun(Root, new Vector3(46f, 70f, 0f), new Color(1f, 0.94f, 0.82f), 1.5f);
            Sun(Root, new Vector3(30f, -110f, 0f), new Color(0.62f, 0.74f, 1f), 0.4f).shadows = LightShadows.None;
            Sun(Root, new Vector3(90f, 0f, 0f), new Color(0.7f, 0.82f, 1f), 0.3f).shadows = LightShadows.None;
            BuildGreen();

            // The table: black, lined in white. The legs stop under the top (a leg that ends level with it shimmers through).
            Material top = Make.Lit(new Color(0.012f, 0.012f, 0.014f), 0.6f, 0.05f);
            Material white = Make.Emissive(new Color(0.9f, 0.9f, 0.9f), new Color(0.35f, 0.35f, 0.35f));
            Material leg = Make.Lit(new Color(0.02f, 0.02f, 0.022f), 0.6f, 0.7f);
            const float Thick = 1.2f, LogoZ = 7f, LineFrom = 14f;
            Shape(PrimitiveType.Cube, "Table", Root, new Vector3(0, TableY - Thick / 2f, 0), new Vector3(HalfW * 2, Thick, HalfLen * 2), top);
            // The lines sit a little into the top and stand a little proud of it. The center line stops short of the logos.
            foreach (float end in new[] { -1f, 1f }) Stripe(new Vector3(0, 0, end * (LineFrom + HalfLen - 0.5f) / 2f), new Vector3(0.4f, 0.12f, HalfLen - 0.5f - LineFrom), white);
            foreach (float x in new[] { -HalfW + 0.3f, HalfW - 0.3f }) Stripe(new Vector3(x, 0, 0), new Vector3(0.5f, 0.12f, HalfLen * 2), white);
            foreach (float z in new[] { -HalfLen + 0.3f, HalfLen - 0.3f, 0f }) Stripe(new Vector3(0, 0, z), new Vector3(HalfW * 2, 0.12f, 0.5f), white);
            // The Puttr logo, green mark and white letters, once on each half and the right way up for whoever shoots from that end.
            var logo = Brand.Logo;
            if (logo != null)
            {
                Material mark = Make.Glow(new Color(1f, 1f, 1f, 0.95f), false, logo);
                Vector2 size = Brand.Size(logo, 15f);
                Card("Logo", Root, new Vector3(0, TableY + 0.1f, -LogoZ), size, mark).transform.localRotation = Quaternion.Euler(90f, 0, 0);
                Card("Logo", Root, new Vector3(0, TableY + 0.1f, LogoZ), size, mark).transform.localRotation = Quaternion.Euler(90f, 180f, 0);
            }
            foreach (float x in new[] { -HalfW + 2f, HalfW - 2f })
                foreach (float z in new[] { -HalfLen + 3f, HalfLen - 3f })
                    Shape(PrimitiveType.Cylinder, "Leg", Root, new Vector3(x, (TableY - Thick) / 2f, z), new Vector3(1.6f, (TableY - Thick) / 2f, 1.6f), leg);

            Mesh cup = CupMesh();
            Material red = Make.Lit(new Color(0.78f, 0.06f, 0.08f), 0.75f);
            Material rim = Make.Lit(new Color(0.95f, 0.95f, 0.95f), 0.6f);
            Material beer = Make.Emissive(new Color(0.85f, 0.55f, 0.1f), new Color(0.25f, 0.14f, 0.02f), 0.9f);
            for (int r = 0; r < 2; r++)
                for (int i = 0; i < 10; i++)
                {
                    Transform c = new GameObject("Cup").transform;
                    c.SetParent(Root, false);
                    c.localPosition = RackSpot(r, i);
                    Make.MeshObject("Body", c, cup, red);
                    GameObject lip = Make.MeshObject("Rim", c, Make.Ring(CupTop - 0.12f, CupTop + 0.05f, 40), rim);
                    lip.transform.localPosition = new Vector3(0, CupH + 0.01f, 0);
                    GameObject b = Make.MeshObject("Beer", c, Make.Ring(0f, CupTop - 0.35f, 32), beer);
                    b.transform.localPosition = new Vector3(0, CupH * 0.62f, 0);
                    _cups[r][i] = c;
                }

            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Emissive(new Color(1f, 0.98f, 0.94f), new Color(0.45f, 0.42f, 0.38f), 0.6f)).transform;
            _ball.gameObject.SetActive(false);
            _trail = Stroke(Root, "Trail", new Color(1f, 1f, 1f, 0.35f), 0.35f);

        }

        // A painted line on the table top.
        void Stripe(Vector3 at, Vector3 size, Material paint)
        {
            GameObject line = Shape(PrimitiveType.Cube, "Line", Root, new Vector3(at.x, TableY + 0.02f, at.z), size, paint);
            line.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // The practice green: a closely mown oval with a collar round it, rough beyond, a few short pins,
        // and past each end a line of trees, far hills and the sky.
        void BuildGreen()
        {
            Props.Grass(out Texture2D turf, out Texture2D blades);
            Material rough = Make.Lit(new Color(0.5f, 0.6f, 0.46f), 0.1f, 0f, turf, blades, 1.3f);
            rough.SetTextureScale("_BaseMap", new Vector2(6000f / 150f, 6000f / 150f));
            Shape(PrimitiveType.Cube, "Rough", Root, new Vector3(0, -1.6f, 0), new Vector3(6000f, 2f, 6000f), rough);
            const float GreenX = 150f, GreenZ = 230f, Collar = 18f;
            Make.MeshObject("Collar", Root, Oval(GreenX + Collar, GreenZ + Collar, 90f), Make.Lit(new Color(0.68f, 0.8f, 0.62f), 0.12f, 0f, turf, blades, 0.8f)).transform.localPosition = new Vector3(0, -0.3f, 0);
            Make.MeshObject("Green", Root, Oval(GreenX, GreenZ, 60f), Make.Lit(new Color(0.9f, 1.02f, 0.82f), 0.2f, 0f, turf, blades, 0.35f));

            // Practice pins: a hole, a short white stick and a small flag.
            Material hole = Make.Lit(new Color(0.01f, 0.012f, 0.01f), 0.1f), stick = Make.Lit(new Color(0.93f, 0.93f, 0.9f), 0.5f);
            foreach (Vector2 at in new[] { new Vector2(-58f, 150f), new Vector2(64f, -160f), new Vector2(96f, 62f), new Vector2(-98f, -74f) })
            {
                GameObject cup = Make.MeshObject("Hole", Root, Make.Disc(2.125f), hole);
                cup.transform.localPosition = new Vector3(at.x, 0.04f, at.y);
                cup.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                Props.Rod("Pin", Root, new Vector3(at.x, 0f, at.y), new Vector3(at.x, 34f, at.y), 0.25f, stick);
                Make.Box("Flag", Root, new Vector3(at.x + 4.4f, 30.5f, at.y), new Vector3(8.4f, 5.6f, 0.15f), stick, null, false);
            }

            Material sky = Make.Glow(Color.white, false, ArcheryMode.Sky());
            Material hills = Make.Glow(Color.white, false, ArcheryMode.Hills());
            hills.SetTextureScale("_MainTex", new Vector2(2f, 1f));
            Material trees = Make.Glow(Color.white, false, ArcheryMode.Trees());
            trees.SetTextureScale("_MainTex", new Vector2(4f, 1f));
            foreach (float end in new[] { 1f, -1f })
            {
                Quaternion facing = Quaternion.Euler(0, end > 0 ? 0f : 180f, 0);
                Card("Sky", Root, new Vector3(0f, 700f, end * 1700f), new Vector2(5600f, 1700f), sky).transform.localRotation = facing;
                Card("Hills", Root, new Vector3(0f, 122f, end * 1500f), new Vector2(5600f, 260f), hills).transform.localRotation = facing;
                Card("Tree line", Root, new Vector3(0f, 70f, end * 760f), new Vector2(3600f, 150f), trees).transform.localRotation = facing;
            }
        }

        // A flat oval in the XZ plane, facing up, textured by the inch so the mowing lines run straight across it.
        static Mesh Oval(float halfX, float halfZ, float inchesATile, int segments = 96)
        {
            var v = new Vector3[segments + 1]; var uv = new Vector2[segments + 1]; var t = new int[segments * 3];
            for (int s = 0; s < segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                v[s + 1] = new Vector3(Mathf.Cos(a) * halfX, 0, Mathf.Sin(a) * halfZ);
                uv[s + 1] = new Vector2(v[s + 1].x, v[s + 1].z) / inchesATile;
                t[s * 3] = 0; t[s * 3 + 1] = 1 + (s + 1) % segments; t[s * 3 + 2] = 1 + s;
            }
            var m = new Mesh { name = "Oval", vertices = v, uv = uv, triangles = t };
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        // A solo cup: a tapered tube, red outside and in, open at the top, with a floor.
        static Mesh CupMesh()
        {
            const int n = 36;
            var v = new List<Vector3>(); var nm = new List<Vector3>(); var t = new List<int>();
            // Outside and inside are separate rings of vertices, so each side is lit as its own surface.
            for (int side = 0; side < 2; side++)
            {
                float shrink = side == 0 ? 0f : 0.06f;
                for (int k = 0; k <= n; k++)
                {
                    float a = k / (float)n * Mathf.PI * 2f, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    var normal = new Vector3(c, (CupTop - CupBottom) / CupH, s).normalized * (side == 0 ? 1f : -1f);
                    v.Add(new Vector3(c * (CupBottom - shrink), shrink, s * (CupBottom - shrink))); nm.Add(normal);
                    v.Add(new Vector3(c * (CupTop - shrink), CupH, s * (CupTop - shrink))); nm.Add(normal);
                }
            }
            int inner = (n + 1) * 2;
            for (int k = 0; k < n; k++)
            {
                int i = k * 2, j = inner + k * 2;
                t.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
                t.AddRange(new[] { j, j + 2, j + 1, j + 2, j + 3, j + 1 });
            }
            int centre = v.Count; v.Add(new Vector3(0, 0.07f, 0)); nm.Add(Vector3.up);
            for (int k = 0; k < n; k++) t.AddRange(new[] { centre, inner + k * 2 + 2, inner + k * 2 });
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(nm); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        // Cup i in a rack (0 at +z, 1 at -z): 4-3-2-1, the single cup nearest the middle of the table.
        static Vector3 RackSpot(int rack, int i)
        {
            int row = i < 4 ? 0 : i < 7 ? 1 : i < 9 ? 2 : 3, inRow = i - new[] { 0, 4, 7, 9 }[row], count = 4 - row;
            float x = (inRow - (count - 1) / 2f) * Gap;
            float z = (HalfLen - 4f - row * Gap * 0.87f) * (rack == 0 ? 1f : -1f);
            return new Vector3(x, TableY, z);
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _calledCup = -1;
            for (int i = 0; i < 10; i++) { _feet[i] = Arcade.DealFeet(_dice); _tee[i] = _dice.Next(1, 4); }
            for (int p = 0; p < 2; p++) for (int i = 0; i < 10; i++) { _sunk[p][i] = false; _cups[p][i].gameObject.SetActive(true); _cups[p][i].localPosition = RackSpot(p, i); }
            _house = players <= 1;
            _p = _first = Mathf.Clamp(Arcade.EmbeddedFirst, 0, 1); _inTurn = 0; _winner = -1; _turnAllMade = true; _rebuttal = _overtime = _over = false;
            _side = _sideShown = _p == 0 ? -1f : 1f;
            _ball.gameObject.SetActive(false);
            _trail.positionCount = 0;
            if (Arcade != null && Arcade.Hud != null)
            {
                Arcade.Hud.BuildBoard(new List<string> { Who(0), Who(1) }, 10, "CUPS", false, "CUPS", 30f);
                ShowBoard();
                Aim();
            }
            // Puttr chosen to go first: it opens the game.
            if (_house && _p == 1 && Arcade != null) Arcade.StartCoroutine(PuttrOpens());
        }

        IEnumerator PuttrOpens()
        {
            yield return new WaitForSecondsRealtime(1.6f);
            if (_house && _p == 1 && !_over) yield return PuttrTurn();
        }

        // The cup to shoot at: the open cup nearest the shooter (the single at the front first).
        int Target
        {
            get
            {
                for (int i = 9; i >= 0; i--) if (!_sunk[_p][i]) return i;
                return -1;
            }
        }

        void Aim()
        {
            int c = Target;
            if (c < 0) return;
            DealtFeetNow = _feet[c];
            DealtTee = _tee[c];
            ShowTeeSign();
            // puttr-app-2: a new cup to shoot at (or a new shooter) is called out: its distance and tee (Matt, 2026-11-21).
            int said = _p * 100 + c;
            if (said != _calledCup && Arcade != null)
            {
                _calledCup = said;
                Arcade.Audio.Voice(VoicePrefix + "_distance_" + DealtFeetNow + "_feet", VoicePrefix + "_tee_" + DealtTeeWord[DealtTee - 1]);
            }
            Gauge(Window, null, false);
        }

        public override string Prompt => _over ? "" : _house && _p == 1 ? "Puttr is shooting" : Who(_p) + "   ·   " + (_overtime ? "Overtime   ·   " : _rebuttal ? "Rebuttal   ·   " : "Putt " + (_inTurn + 1) + " of 2   ·   ") + "Putt when ready";

        public override void Tick()
        {
            _sideShown = Mathf.MoveTowards(_sideShown, _side, Time.deltaTime * 1.6f);
        }

        public override IEnumerator Play(PuttResult putt)
        {
            if (_over || (_house && _p == 1)) yield break;   // Puttr's turn: it shoots for itself
            if (Target < 0) yield break;
            float window = Window;
            bool backDoor = putt.Made && (putt.Launch.Reached && Mathf.Abs(putt.Launch.HeadingDeg) >= 80f || putt.HasEntry && Mathf.Abs(putt.OffCenter) >= 90f);
            bool wide = putt.Made && !backDoor && putt.HasEntry && Mathf.Abs(putt.OffCenter) > window;   // in the hole, outside the window: rims out
            bool make = putt.Made && !backDoor && !wide;
            float side = putt.MissSide == MissSide.Left ? -1f : putt.MissSide == MissSide.Right ? 1f : (Line(putt) < 0 ? -1f : 1f);
            if (wide) side = putt.OffCenter < 0 ? -1f : 1f;
            yield return Shoot(make, backDoor || wide, Mathf.Clamp(Line(putt) / 2.1f, -1f, 1f), side);
            string head = make ? "CUP!" : backDoor ? "BACK DOOR" : wide ? "TOO WIDE" : "MISS";
            ShowPutt(putt, make ? "Cup!" : backDoor ? "Back door" : wide ? "Too wide" : "Miss", make ? Make.Bright : Make.Amber);
            Gauge(window, putt.HasEntry ? putt.OffCenter : (float?)null, make);
            Decide(make, head);
            ShowBoard();
            yield return new WaitForSecondsRealtime(_over ? 2f : 1.2f);
            if (!_over) Aim();
            if (_house && _p == 1 && !_over) yield return PuttrTurn();
        }

        // Puttr shoots until the turn comes back (two putts, or a whole rebuttal). Short cups go in more often:
        // about 70% from 3 ft down to 30% from 11 ft, a little like a decent Player.
        IEnumerator PuttrTurn()
        {
            while (_house && _p == 1 && !_over)
            {
                yield return new WaitForSecondsRealtime(0.9f);
                int cup = Target;
                if (cup < 0) yield break;
                float window = Window;
                // Fewer cups, smaller window: Puttr makes fewer too.
                float chance = Mathf.Lerp(0.7f, 0.3f, Mathf.InverseLerp(3f, 11f, _feet[cup])) * Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(20f, 90f, window));
                double roll = _dice.NextDouble();
                bool make = roll < chance, rimmed = !make && roll < chance + 0.12;
                float entry = make ? (float)(_dice.NextDouble() * 2.0 - 1.0) * window * 0.9f
                    : rimmed ? (window + (float)_dice.NextDouble() * Mathf.Max(5f, 85f - window)) * (_dice.Next(2) == 0 ? -1f : 1f) : 0f;
                float across = make || rimmed ? Mathf.Clamp(entry / 90f, -1f, 1f) : (float)(_dice.NextDouble() * 2.0 - 1.0);
                yield return Shoot(make, rimmed, across, across < 0 ? -1f : 1f);
                Gauge(window, make || rimmed ? entry : (float?)null, make);
                Decide(make, make ? "CUP!" : rimmed ? "TOO WIDE" : "MISS");
                ShowBoard();
                Arcade.Shot("house");
                yield return new WaitForSecondsRealtime(_over ? 2f : 1.2f);
                if (!_over) Aim();
            }
        }

        // One shot at the front cup: into the cup on a make; off the rim on the side it missed (across: -1 left to 1
        // right, where it went into the hole); round the rim and out for a back door.
        IEnumerator Shoot(bool make, bool backDoor, float across, float side)
        {
            int cup = Target;
            int rack = _p == 0 ? 0 : 1;
            Transform target = _cups[rack][cup];

            // The flight: from the shooter's end, up and over, to the cup.
            Vector3 cupTop = target.localPosition + Vector3.up * CupH;
            float facing = _p == 0 ? 1f : -1f;                                 // the shooter's right is +x for Player 1
            Vector3 from = new Vector3(across * 3f, TableY + 10f, -facing * (HalfLen + 6f));
            Vector3 aim;
            if (make) aim = cupTop + new Vector3(across * (CupTop - BallR - 0.2f) * facing, -0.2f, 0);
            else if (backDoor) aim = cupTop + new Vector3(0, 0.2f, facing * (CupTop - 0.2f));
            else aim = cupTop + new Vector3(side * facing * (CupTop + 0.3f), 0.1f, 0);
            yield return Fly(from, aim, 0.75f, 16f);

            if (make)
            {
                // Down into the beer, a splash, the cup comes off.
                yield return Fly(aim, target.localPosition + Vector3.up * (CupH * 0.62f + BallR), 0.18f, 0f);
                Arcade.Audio.Library("cup_plop", 1f);
                Arcade.Sparks(At(target.localPosition.x, target.localPosition.y + CupH, target.localPosition.z), 10);
                _sunk[_p][cup] = true;
                yield return new WaitForSeconds(0.35f);
                _ball.gameObject.SetActive(false);
                yield return Lift(target);
            }
            else
            {
                // Off the rim, onto the table, away.
                Arcade.Audio.Library("pingpong_bounce", 0.9f);
                Vector3 off = aim + new Vector3((aim.x - target.localPosition.x) * 3f + (float)(_dice.NextDouble() - 0.5) * 4f, 0, facing * (backDoor ? 10f : 6f));
                if (backDoor) { Arcade.Audio.Library("beerpong_rim_spin", 0.8f); yield return Spin(target, aim); }   // puttr-app-2: round the rim (silent until recorded)
                off.y = TableY + BallR;
                yield return Fly(aim, off, 0.35f, 4f);
                Arcade.Audio.Library("pingpong_bounce", 0.6f);
                Vector3 away = off + new Vector3(off.x - target.localPosition.x, 0, facing * 14f);
                yield return Fly(off, away, 0.4f, 3f);
                _ball.gameObject.SetActive(false);
            }
            _trail.positionCount = 0;
        }

        void Decide(bool make, string head)
        {
            Hud hud = Arcade.Hud;
            if (_rebuttal)
            {
                if (!make) { End(1 - _p, "Rebuttal over"); return; }
                if (Count(_p) == 10) { Overtime(); hud.Banner("OVERTIME", "Three cups each", Make.Amber, 2f); return; }
                hud.Banner(head, "Keep shooting", Make.Bright, 1.2f); return;
            }
            if (Count(_p) == 10)
            {
                if (_p == _first) { _rebuttal = true; Switch(); _inTurn = 0; hud.Banner("TABLE CLEARED", Who(_p) + " gets a rebuttal", Make.Bright, 2f); return; }
                End(_p, "Table cleared"); return;
            }
            _turnAllMade &= make;
            _inTurn++;
            if (_inTurn >= 2)
            {
                _inTurn = 0;
                bool ballsBack = false; // the app's Balls back rule is off unless it's chosen
                _turnAllMade = true;
                if (!ballsBack) Switch();
                hud.Banner(head, Who(_p) + "'s turn", make ? Make.Bright : Make.Amber, 1.4f);
                return;
            }
            hud.Banner(head, "", make ? Make.Bright : Make.Amber, 1f);
            if (!make && !Arcade.Audio.TryLibrary("beerpong_miss", 0.75f)) Arcade.Audio.Miss();   // puttr-app-2
        }

        void Switch() { _p = 1 - _p; _side = _p == 0 ? -1f : 1f; }

        void Overtime()
        {
            for (int p = 0; p < 2; p++)
                for (int i = 0; i < 10; i++)
                {
                    _sunk[p][i] = i < 7;
                    Transform c = _cups[p == 0 ? 0 : 1][i];
                    c.gameObject.SetActive(i >= 7);
                    c.localPosition = RackSpot(p == 0 ? 0 : 1, i);
                }
            _overtime = true; _rebuttal = false; _inTurn = 0;
            Switch();
            _first = _p;
        }

        void End(int winner, string why)
        {
            _over = true; _winner = winner;
            Arcade.Hud.SetAimCard(null, "");
            Arcade.Hud.Banner(Who(winner).ToUpperInvariant() + " WINS", why, Make.Bright, 3f);
            Arcade.Audio.Library("beerpong_win", "rule_win");   // puttr-app-2: the party's cheer, the rule games' win until recorded
            Arcade.Celebrate(120, At(0, TableY + 10f, 0));
        }

        IEnumerator Fly(Vector3 a, Vector3 b, float seconds, float apex)
        {
            _ball.gameObject.SetActive(true);
            var pts = new List<Vector3>();
            for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
            {
                Vector3 p = Vector3.Lerp(a, b, t) + Vector3.up * (4f * apex * t * (1f - t));
                _ball.localPosition = p;
                pts.Add(p);
                _trail.positionCount = pts.Count;
                _trail.SetPositions(pts.ToArray());
                yield return null;
            }
            _ball.localPosition = b;
        }

        // Round the rim and out.
        IEnumerator Spin(Transform cup, Vector3 start)
        {
            Vector3 c = cup.localPosition + Vector3.up * (CupH + BallR * 0.6f);
            float a0 = Mathf.Atan2(start.z - c.z, start.x - c.x);
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.7f)
            {
                float a = a0 + t * Mathf.PI * 2.2f;
                _ball.localPosition = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (CupTop - BallR * 0.4f);
                yield return null;
            }
        }

        // A sunk cup comes off the table.
        IEnumerator Lift(Transform cup)
        {
            Vector3 a = cup.localPosition;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.5f)
            {
                cup.localPosition = a + new Vector3(0, Ease(t) * 18f, 0);
                cup.localScale = Vector3.one * (1f - t * 0.6f);
                yield return null;
            }
            cup.gameObject.SetActive(false);
            cup.localScale = Vector3.one;
            cup.localPosition = a;
        }

        void ShowBoard()
        {
            Hud hud = Arcade.Hud;
            for (int p = 0; p < 2; p++)
            {
                for (int i = 0; i < 10; i++) hud.SetPip(p, i, _sunk[p][i] ? Hud.Pip.Good : Hud.Pip.Empty);
                hud.SetTotal(p, Count(p));
            }
            hud.SetTurn(_over ? -1 : _p, _over ? -1 : Count(_p));
        }

        public override bool Over => _over;

        public override void Final(out string title, out string body)
        {
            title = _winner >= 0 ? Who(_winner).ToUpperInvariant() + "   W I N S" : "G A M E   O V E R";
            body = Who(0) + "     " + Count(0) + " cups\n" + Who(1) + "     " + Count(1) + " cups";
        }
    }
}
