// Dead Weight: curling.
//
// The putt is the throw. A granite stone leaves the hack and slides the length of a sheet of ice,
// slowing evenly under the ice's friction until it stops, curling a little as it slows. Where it
// stops is the pace and line of the putt, scaled up onto the ice:
//
//   pace   how far. A putt that dies in the hole stops on the middle of the button: a make with
//          up to DiesIn inches of rollout (the putt card's "in past", v*v/128) is dead weight.
//          Every inch firmer than that puts the stone 6 in past the button; a putt left short
//          stops short of the button by 6 in for every inch it was short.
//   line   across: 6 in of ice for every inch off the middle of the hole.
//   tee    which way it curls: off the left tee it bends right, off the right tee left.
//
// Stones stay in play until the round ends. A moving stone that meets another passes on its
// momentum along the line between their centres (equal granite, a little lively, e = 0.86), so a
// firm stone knocks the other on, or out over the back line or into the boards, and a soft one
// just taps it. Everything that moves keeps sliding and slowing, and can hit the next.
//
// Scoring each round: every stone touching a ring scores 10, 6, 3 or 1, button outwards, where it
// lies after the last stone.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class DeadWeightMode : StageMode
    {
        public override string Name => "Dead Weight";
        public override bool Branded => true;
        // puttr-app-2: the rink's cheer ends the game. Neither of the rink's own loops is played (Matt, 2026-11-23, twice:
        // "that truck background sound"): the bed (deadweight_rink_loop) droned behind the game, and the stone's slide
        // (deadweight_slide_loop) was a low rumble for the whole of every throw. The rink is quiet between stones, and a
        // sliding stone has the shared soft roll. The rink's horn (deadweight_button) is the end of the game only, never
        // after a putt (Matt, 2026-11-23): a stone that scores has the shared cheer.
        public override GameAudio.Scene Sound => GameAudio.Scene.Quiet;
        public override string EndSlot => "deadweight_button";
        public override string VoicePrefix => "deadweight";
        public override int Putter => Active; // whose difficulty applies to this putt
        public override string Blurb => "Curling, with a putter.\nDie it on the button.\nKnock the others out.";
        public override string Icon => "Target";
        public override Color Accent => new Color(0.48f, 0.83f, 0.27f);
        public override string[] Tags => new[] { "3 rounds", "Pace" };
        public override int Difficulty => 3;
        public override string[] HowTo => new[]
        {
            "Your putt throws a stone down the ice. A putt that dies in the hole stops on the button; every inch past or short moves it on or back.",
            "Rings score 10, 6, 3 and 1 from the button out. Three stones each a round, and they stay where they stop.",
            "Hit a stone with pace and it is knocked on, or out of play. Gentle, and you only nudge it. Three rounds, most points wins.",
        };
        public override int Score(int player) => _banked != null && player < _banked.Length ? _banked[player] : 0;
        public override string SoloOpponent => "Puttr";
        public override string Rule => "Curling: slide your stone to the button, and knock theirs away. Three stones a round.";

        // Pace: the window that stops on the button, on a bar from soft to firm (the ideal-putt card).
        public override bool AimPace(out float low, out float high, out float min, out float max)
        {
            low = 4f; high = Mathf.Sqrt(2f * PuttrAnalysis.RampDecelInPerSec2 * (DiesIn + (ButtonR + StoneR) / Scale)); min = 0f; max = 60f;
            return true;
        }

        const int Rounds = 3, StonesEach = 3;
        // The sheet, in inches: the house is centred at the origin, the throw comes from -z.
        const float HalfWidth = 87f;            // 14 ft 6 in wide
        const float BackLine = 72f;             // the back of the 12-ft ring
        const float HogLine = -252f, Hack = -1150f, SheetFarZ = 150f;
        const float StoneR = 5.7f, StoneH = 4.6f;
        const float Scale = 6f;                 // inches of ice for each inch of mat
        // A make that dies into the hole still has a few inches of rollout left as it drops: up to this much is dead
        // weight, the middle of the button (Matt, 2026-11-23: it was stopping past the button).
        const float DiesIn = 6f;
        const float Friction = 140f;            // in/s^2: the ice's even slowing
        const float Bounce = 0.86f;             // granite on granite
        const float CurlInches = 16f;           // how far a full slide bends
        static readonly float ButtonR = 12f;
        static readonly float[] RingRadius = { 12f, 24f, 48f, 72f };
        static readonly int[] RingPoints = { 10, 6, 3, 1 };
        static readonly Color[] Handle = { new Color(0.86f, 0.16f, 0.16f), new Color(0.98f, 0.78f, 0.1f), new Color(0.22f, 0.46f, 0.96f), new Color(0.32f, 0.8f, 0.3f) };

        // Camera: behind the stone, following it, while it slides. Between throws it looks at the house from the Player's
        // end; once a stone lies in play it sits right above the house, looking down, so every stone (the other
        // side's too) can be seen where it lies (Matt, 2026-11-23).
        Vector3? _follow;
        static bool Upright => Screen.height > Screen.width;
        bool Lying => _stones.Count > 0 && !_clear;
        public override Vector3 CameraPosition => _follow is Vector3 f ? At(f.x * 0.6f, 74f, Mathf.Min(f.z - 210f, -150f)) : Lying ? At(0f, Upright ? 235f : 330f, Upright ? -32f : -44f) : At(0f, 205f, -255f);
        public override Vector3 CameraLook => _follow is Vector3 f ? At(f.x * 0.6f, 0f, Mathf.Min(f.z + 260f, 20f)) : Lying ? At(0f, 0f, -2f) : At(0f, 0f, -14f);
        public override float CameraFov => 36f;
        public override float ShadowDistance => 1400f;

        class Stone { public Transform T; public int Player; public Vector2 At, V; public bool Out; public float Spin; }
        readonly List<Stone> _stones = new List<Stone>();
        // _players is how many sides throw: the Players, and Puttr when it was added on the game page (_house: Puttr is
        // the last side, and throws its own stones).
        int _players, _round, _thrown;
        bool _house;
        string Who(int i) => _house && i == _players - 1 ? "Puttr" : Arcade.PlayerName(i);
        int[] _banked;
        int[,] _roundScore;
        bool _clear;
        Material _granite, _shine;
        Color _roomBefore;
        bool _lit;
        readonly Material[] _handlePaint = new Material[4];

        int Active => _thrown % _players;

        // ── The sheet ───────────────────────────────────────────────────────

        public override void Build()
        {
            // The ice: pale blue-white, pebbled, a little glossy.
            float len = SheetFarZ - (Hack - 60f), mid = (SheetFarZ + Hack - 60f) * 0.5f;
            Material ice = Make.Lit(new Color(0.84f, 0.9f, 0.95f), 0.42f, 0f, Pebble());   // matt enough that the lamps do not glare back off it
            ice.SetTextureScale("_BaseMap", new Vector2(8f, 8f * len / (HalfWidth * 2f)));
            Shape(PrimitiveType.Cube, "Ice", Root, new Vector3(0f, -1f, mid), new Vector3(HalfWidth * 2f, 2f, len), ice);

            // Boards along both sides and across the back; a dark arena beyond.
            Material board = Make.Lit(new Color(0.5f, 0.53f, 0.56f), 0.4f);
            Material cap = Make.Emissive(Color.black, Make.Green * 2.4f);
            for (int side = -1; side <= 1; side += 2)
            {
                Shape(PrimitiveType.Cube, "Board", Root, new Vector3(side * (HalfWidth + 3f), 4f, mid), new Vector3(6f, 8f, len), board);
                Shape(PrimitiveType.Cube, "Board light", Root, new Vector3(side * (HalfWidth + 0.4f), 8.1f, mid), new Vector3(0.6f, 0.3f, len), cap);
            }
            Shape(PrimitiveType.Cube, "Back board", Root, new Vector3(0f, 4f, SheetFarZ + 3f), new Vector3(HalfWidth * 2f + 12f, 8f, 6f), board);
            Shape(PrimitiveType.Cube, "Arena floor", Root, new Vector3(0f, -3f, mid), new Vector3(4000f, 2f, 4000f), Make.Lit(new Color(0.035f, 0.075f, 0.15f), 0.25f));   // the blue carpet round a televised sheet

            // The house: painted under the ice, 12-ft blue, 8-ft white, 4-ft red, white button.
            Color[] paint = { new Color(0.12f, 0.36f, 0.86f), new Color(0.95f, 0.97f, 1f), new Color(0.86f, 0.14f, 0.14f), new Color(0.95f, 0.97f, 1f) };
            float[] radius = { 72f, 48f, 24f, 12f };
            for (int i = 0; i < 4; i++)
            {
                GameObject ring = Make.MeshObject("House " + radius[i], Root, Make.Disc(radius[i], 96), Make.Lit(paint[i], 0.8f));
                ring.transform.localPosition = new Vector3(0f, 0.02f + i * 0.01f, 0f);
            }
            // The lines: centre, tee, back and hog (red and blue), drawn into the ice.
            Material red = Make.Lit(new Color(0.8f, 0.12f, 0.12f), 0.8f), blue = Make.Lit(new Color(0.12f, 0.3f, 0.8f), 0.8f), black = Make.Lit(new Color(0.08f, 0.08f, 0.1f), 0.8f);
            Line("Centre line", new Vector3(0f, 0.07f, (Hack + BackLine) * 0.5f), new Vector2(0.6f, BackLine - Hack), black);
            Line("Tee line", new Vector3(0f, 0.07f, 0f), new Vector2(HalfWidth * 2f, 0.6f), black);
            Line("Back line", new Vector3(0f, 0.07f, BackLine), new Vector2(HalfWidth * 2f, 0.6f), black);
            Line("Hog line", new Vector3(0f, 0.07f, HogLine), new Vector2(HalfWidth * 2f, 4f), red);
            Line("Far hog line", new Vector3(0f, 0.07f, Hack + 252f), new Vector2(HalfWidth * 2f, 4f), blue);

            // The brand's lockup (Puttr's with no kit) set into the ice between the hog lines, as the sponsor's would be.
            var logo = Brand.Logo;
            if (logo != null)
            {
                Material mark = Make.Glow(new Color(0.95f, 0.95f, 0.95f, 0.9f), false, logo);
                GameObject c = Card("Puttr in the ice", Root, new Vector3(0f, 0.08f, (HogLine + Hack + 252f) * 0.5f), Brand.Size(logo, 120f), mark);
                c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            // The hack the stones are thrown from.
            Shape(PrimitiveType.Cube, "Hack", Root, new Vector3(0f, 0.6f, Hack - 14f), new Vector3(14f, 1.2f, 5f), black);

            // Lights, as curling is lit for television (Matt, 2026-11-23: "very bright and well lit"): the whole sheet
            // evenly bright from straight overhead, no pools of light and no dark corners. A key from just off vertical
            // gives each stone a small soft shadow; a fill from the other side and a lifted room keep the far side of
            // every stone, and the colour of its handle, readable.
            Light key = World.NewLight(Root, "Arena key", LightType.Directional, new Color(1f, 0.99f, 0.97f), 1.15f);
            key.transform.localRotation = Quaternion.Euler(78f, 20f, 0f);
            Light fill = World.NewLight(Root, "Arena fill", LightType.Directional, new Color(0.92f, 0.96f, 1f), 0.35f);
            fill.transform.localRotation = Quaternion.Euler(60f, 200f, 0f);
            fill.shadows = LightShadows.None;

            _granite = Make.Lit(new Color(0.34f, 0.35f, 0.37f), 0.55f, 0f, Granite());
            _shine = Make.Lit(new Color(0.75f, 0.77f, 0.8f), 0.85f, 0.6f);
            for (int i = 0; i < 4; i++) _handlePaint[i] = Make.Lit(Handle[i], 0.6f);
        }

        void Line(string name, Vector3 at, Vector2 size, Material paint)
        {
            GameObject c = Card(name, Root, at, size, paint);
            c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // Ice pebble: faint speckle on white.
        static Texture2D Pebble()
        {
            const int n = 128;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float h = Make.Hash(x, y, 11), k = 0.93f + 0.07f * h;
                    if (Make.Hash(x, y, 5) > 0.985f) k = 1.05f;
                    px[y * n + x] = new Color(k, k, k, 1f);
                }
            Texture2D tex = Make.NewTexture(n, n, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // Granite: grey with darker and lighter flecks.
        static Texture2D Granite()
        {
            const int n = 64;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float h = Make.Hash(x, y, 3);
                    float k = h > 0.85f ? 0.55f : h < 0.12f ? 1.35f : 0.85f + 0.3f * Make.Hash(x, y, 9);
                    px[y * n + x] = new Color(k, k, k * 1.02f, 1f);
                }
            Texture2D tex = Make.NewTexture(n, n, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // A curling stone: a granite body, a polished running band, the player's handle on top.
        Transform NewStone(int player)
        {
            var t = new GameObject("Stone").transform;
            t.SetParent(Root, false);
            Shape(PrimitiveType.Cylinder, "Body", t, new Vector3(0f, StoneH * 0.5f, 0f), new Vector3(StoneR * 2f, StoneH * 0.5f, StoneR * 2f), _granite);
            Shape(PrimitiveType.Cylinder, "Band", t, new Vector3(0f, StoneH * 0.5f, 0f), new Vector3(StoneR * 2.04f, 0.7f, StoneR * 2.04f), _shine);
            Shape(PrimitiveType.Cylinder, "Top", t, new Vector3(0f, StoneH + 0.2f, 0f), new Vector3(StoneR * 1.5f, 0.2f, StoneR * 1.5f), _handlePaint[player % 4]);
            Shape(PrimitiveType.Cube, "Handle", t, new Vector3(0f, StoneH + 1.3f, -1.2f), new Vector3(1.6f, 1.2f, 6.5f), _handlePaint[player % 4]);
            Shape(PrimitiveType.Cube, "Handle post", t, new Vector3(0f, StoneH + 0.7f, 1.4f), new Vector3(1.4f, 1.4f, 1.4f), _handlePaint[player % 4]);
            return t;
        }

        void Place(Stone s) => s.T.localPosition = new Vector3(s.At.x, 0f, s.At.y);

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = Mathf.Clamp(players, 1, 4);
            // Against Puttr (added as the second Player on the game page): one Player comes in, and Puttr takes the other side.
            _house = _players == 1 && Arcade.Embedded && Arcade.EmbeddedPuttr;
            if (_house) _players = 2;
            if (!_lit) { _lit = true; _roomBefore = RenderSettings.ambientLight; RenderSettings.ambientLight = new Color(0.2f, 0.215f, 0.235f); }
            _round = 0;
            _thrown = 0;
            _banked = new int[_players];
            _roundScore = new int[_players, Rounds];
            _follow = null;
            ClearStones();
            var names = new List<string>();
            for (int i = 0; i < _players; i++) names.Add(Who(i));
            Arcade.Hud.BuildBoard(names, Rounds, "TOTAL", true, "ROUNDS", 34f);
            ShowScores();
        }

        void ClearStones()
        {
            foreach (Stone s in _stones) if (s.T != null) Make.Kill(s.T.gameObject);
            _stones.Clear();
            _clear = false;
        }

        public override IEnumerator Prepare()
        {
            _follow = null;
            if (_clear) ClearStones();
            yield break;
        }

        public override string Prompt
        {
            get
            {
                string who = _players > 1 ? Who(Active) + "   ·   " : "";
                return who + "Round " + (_round + 1) + "   ·   Stone " + (_thrown / _players + 1) + " of " + StonesEach + "   ·   Die it on the button";
            }
        }

        public override IEnumerator Play(PuttResult putt)
        {
            int player = Active;
            PuttLaunch launch = putt.Launch;

            // Where this putt sends the stone: how far past (or short of) the button, and how far across.
            float pastIn, acrossIn = Line(putt);
            if (launch.Reached)
            {
                float v = launch.HandoffSpeedInPerSec > 0f ? launch.HandoffSpeedInPerSec : launch.SpeedInPerSec;
                pastIn = PaceWords.Past(v);
                // Holed: the stone is on the centre line's side of the button too (half the cup's width of ice, not all of it).
                if (putt.Made) { pastIn = Mathf.Max(0f, pastIn - DiesIn); acrossIn *= 0.5f; }
            }
            else
            {
                // Short: how close it came.
                float nearest = 60f;
                if (putt.PathInches != null) foreach (Vector2 p in putt.PathInches) if (p.magnitude < nearest) { nearest = p.magnitude; acrossIn = p.x; }
                pastIn = -nearest;
            }
            var target = new Vector2(Mathf.Clamp(acrossIn * Scale, -HalfWidth + StoneR, HalfWidth - StoneR), Mathf.Clamp(pastIn * Scale, HogLine - 120f, SheetFarZ - StoneR - 1f)); // a firm one stops on the back boards
            yield return Throw(player, target, Mathf.Clamp(DealtTee, 1, 3), launch.HeadingDeg, putt);

            // Puttr needs no putt: it throws as soon as it is its turn.
            while (_house && !Over && Active == _players - 1 && _thrown > 0)
            {
                yield return new WaitForSecondsRealtime(0.9f);
                Arcade.Hud.SetPrompt("Puttr is throwing");
                yield return Throw(Active, PuttrTarget(), 1 + _dice.Next(3), _dice.Next(2) == 0 ? -1f : 1f, null);
            }
        }

        readonly System.Random _dice = new System.Random();

        // Where Puttr's stone stops: aimed at the button, off by a few inches of putt either way (less on Hard, more on
        // Easy), and now and then left short or sent through.
        Vector2 PuttrTarget()
        {
            float spread = Arcade.Level == Arcade.Easy ? 1.5f : Arcade.Level == Arcade.Hard ? 0.7f : 1f;
            float across = ((float)_dice.NextDouble() - (float)_dice.NextDouble()) * 3.2f * spread;
            float past = ((float)_dice.NextDouble() + (float)_dice.NextDouble() - 1f) * 5f * spread + 0.5f;
            if (_dice.Next(10) == 0) past += _dice.Next(2) == 0 ? -8f : 9f;
            return new Vector2(Mathf.Clamp(across * Scale, -HalfWidth + StoneR, HalfWidth - StoneR), Mathf.Clamp(past * Scale, HogLine - 120f, SheetFarZ - StoneR - 1f));
        }

        // One stone, from the hack to where it and everything it hits come to rest, then the score.
        IEnumerator Throw(int player, Vector2 target, int tee, float heading, PuttResult putt)
        {
            var stone = new Stone { T = NewStone(player), Player = player, At = new Vector2(0f, Hack) };
            Place(stone);
            if (!Arcade.Audio.TryLibrary("deadweight_release", 0.75f)) Arcade.Audio.Kick(0.2f);   // puttr-app-2: its own release, Spot Kick's kick until recorded

            // The slide: even slowing to a stop on the target, bending a little as it slows.
            float distance = target.y - Hack;
            float T = Mathf.Sqrt(2f * distance / Friction);
            float curl = tee == 1 ? CurlInches : tee == 3 ? -CurlInches : (heading >= 0f ? -CurlInches : CurlInches) * 0.6f;
            curl *= Mathf.Clamp01(distance / (BackLine - Hack));
            Vector2 Path(float u) // u: how far along in time, 0 to 1
            {
                float p = 1f - (1f - u) * (1f - u);  // even slowing: distance covered
                float x = target.x * p + curl * Mathf.Sin(Mathf.PI * p) * p;
                return new Vector2(x, Hack + distance * p);
            }

            bool hit = false;
            float t = 0f;
            Vector2 last = stone.At;
            while (t < T)
            {
                t += Time.deltaTime;
                Vector2 now = Path(Mathf.Min(1f, t / T));
                stone.V = (now - last) / Mathf.Max(Time.deltaTime, 0.0001f);
                stone.At = now;
                last = now;
                stone.Spin += Time.deltaTime * 40f * (1f - t / T);
                Place(stone);
                stone.T.localRotation = Quaternion.Euler(0f, stone.Spin * Mathf.Sign(curl == 0f ? 1f : curl), 0f);
                _follow = new Vector3(stone.At.x, 0f, stone.At.y);
                Arcade.Audio.SetRoll(stone.V.magnitude * 0.25f);
                // Does it meet a stone in play?
                foreach (Stone other in _stones)
                    if (!other.Out && (other.At - stone.At).sqrMagnitude < 4f * StoneR * StoneR) { hit = true; break; }
                if (hit) break;
                if (stone.At.y > -260f) _follow = null; // close to the house: settle over it
                yield return null;
            }
            _stones.Add(stone);
            if (!hit) stone.V = Vector2.zero;

            // From the first knock on: every stone slides, slows and strikes the next.
            if (hit) yield return Knocks();
            _follow = null;
            Arcade.Audio.SetRoll(0f);

            // Out of play: over the back line or into the boards.
            foreach (Stone s in _stones)
                if (!s.Out && (s.At.y - StoneR > BackLine || Mathf.Abs(s.At.x) + StoneR > HalfWidth))
                {
                    s.Out = true;
                    if (s.T != null) Make.Kill(s.T.gameObject);
                }
            _stones.RemoveAll(s => s.Out);

            Score(putt, stone, hit);
            _thrown++;
            Tally();
            Arcade.Shot("result");
            yield return new WaitForSecondsRealtime(1.6f);

            if (_thrown >= _players * StonesEach)
            {
                // The round is over: what lies in the house now is what counts.
                for (int i = 0; i < _players; i++) _banked[i] += _roundScore[i, _round];
                _round++;
                _thrown = 0;
                _clear = true;
            }
            ShowScores();
        }

        // Stones in motion: friction slows each along its own path, and touching stones pass on
        // momentum along the line of their centres.
        IEnumerator Knocks()
        {
            if (!Arcade.Audio.TryLibrary("deadweight_clack", 0.9f)) Arcade.Audio.PinKnock(60f);   // puttr-app-2
            Arcade.Shake(0.25f);
            for (float time = 0f; time < 8f; time += Time.deltaTime)
            {
                const int steps = 4;
                float dt = Time.deltaTime / steps;
                bool moving = false;
                for (int k = 0; k < steps; k++)
                {
                    foreach (Stone s in _stones)
                    {
                        if (s.Out) continue;
                        float speed = s.V.magnitude;
                        if (speed < 0.5f) { s.V = Vector2.zero; continue; }
                        moving = true;
                        s.V *= Mathf.Max(0f, speed - Friction * dt) / speed;
                        s.At += s.V * dt;
                        s.Spin += dt * speed * 0.2f;
                        // Off the sheet: gone from play once it is over the back line or into the boards.
                        if (s.At.y - StoneR > BackLine + 24f || Mathf.Abs(s.At.x) + StoneR > HalfWidth) { s.Out = true; s.V = Vector2.zero; }
                    }
                    for (int i = 0; i < _stones.Count; i++)
                        for (int j = i + 1; j < _stones.Count; j++)
                        {
                            Stone a = _stones[i], b = _stones[j];
                            if (a.Out || b.Out) continue;
                            Vector2 d = b.At - a.At;
                            float dist = d.magnitude;
                            if (dist >= 2f * StoneR || dist < 0.0001f) continue;
                            Vector2 n = d / dist;
                            float va = Vector2.Dot(a.V, n), vb = Vector2.Dot(b.V, n);
                            if (va - vb <= 0f) { Separate(a, b, n, dist); continue; } // already parting
                            // Equal masses: the normal speeds are shared out by the bounce.
                            float na = (1f - Bounce) * 0.5f * va + (1f + Bounce) * 0.5f * vb;
                            float nb = (1f + Bounce) * 0.5f * va + (1f - Bounce) * 0.5f * vb;
                            a.V += (na - va) * n;
                            b.V += (nb - vb) * n;
                            Separate(a, b, n, dist);
                            float knock = Mathf.Clamp(Mathf.Abs(va - vb), 10f, 80f);   // puttr-app-2: granite on granite, the shared click until recorded
                            if (!Arcade.Audio.TryLibrary("deadweight_clack", Mathf.InverseLerp(4f, 45f, knock) * 0.7f + 0.2f, 1f + Random.Range(-0.08f, 0.08f))) Arcade.Audio.PinKnock(knock);
                        }
                }
                foreach (Stone s in _stones)
                {
                    if (s.T == null) continue;
                    if (s.Out) { s.T.localPosition += Vector3.down * Time.deltaTime * 20f; continue; } // drops away behind the boards
                    Place(s);
                    s.T.localRotation = Quaternion.Euler(0f, s.Spin, 0f);
                }
                Arcade.Audio.SetRoll(MaxSpeed() * 0.25f);
                if (!moving) break;
                yield return null;
            }
        }

        static void Separate(Stone a, Stone b, Vector2 n, float dist)
        {
            float overlap = 2f * StoneR - dist;
            a.At -= n * overlap * 0.5f;
            b.At += n * overlap * 0.5f;
        }

        float MaxSpeed()
        {
            float m = 0f;
            foreach (Stone s in _stones) if (!s.Out) m = Mathf.Max(m, s.V.magnitude);
            return m;
        }

        void Score(PuttResult putt, Stone stone, bool hit)
        {
            Hud hud = Arcade.Hud;
            bool inPlay = _stones.Contains(stone);
            int points = inPlay ? Points(stone.At) : 0;
            float from = inPlay ? stone.At.y / Scale : 0f;
            string where = !inPlay ? "Out of play" : Mathf.Abs(from) < 0.5f && Mathf.Abs(stone.At.x) < ButtonR ? "Dead weight" : Mathf.Abs(from).ToString("0.0") + (from < 0 ? " in short" : " in past");
            if (!inPlay) { hud.Banner("OUT", stone.At.y > BackLine ? "Through the house" : "Into the boards", Make.Red, 1.8f); if (!Arcade.Audio.TryLibrary("deadweight_out", 0.8f)) Arcade.Audio.Miss(); }   // puttr-app-2: the rink's own sounds, the shared ones until recorded
            else if (points == RingPoints[0]) { hud.Banner("ON THE BUTTON", hit ? "And knocked one away" : "Dead weight", Make.Bright, 2.2f); Arcade.Audio.Strike(); Arcade.Celebrate(80, At(stone.At.x, 10f, stone.At.y)); }
            else if (points > 0) { hud.Banner(points.ToString(), hit ? "Knocked on: " + where : where, Color.white, 1.6f); Arcade.Audio.Spare(); }
            else { hud.Banner(hit ? "KNOCK" : "SHORT", hit ? "Moved the stones" : where, Make.Amber, 1.6f); if (!Arcade.Audio.TryLibrary("deadweight_out", 0.6f)) Arcade.Audio.Miss(); }
            if (putt != null) ShowPutt(putt, !inPlay ? "Out" : points + (points == 1 ? " point" : " points"), !inPlay ? Make.Red : points > 0 ? Make.Bright : Make.Amber);
        }

        public override bool Over => _round >= Rounds;

        public override void Final(out string title, out string body)
        {
            title = _players == 1 ? _banked[0] + "  P O I N T S" : "G A M E   O V E R";
            body = "";
            int best = 0;
            for (int i = 0; i < _players; i++) if (_banked[i] > _banked[best]) best = i;
            if (_players > 1)
                for (int i = 0; i < _players; i++)
                    body += Who(i) + "     " + _banked[i] + (i == best ? "     winner" : "") + "\n";
        }

        public override void End()
        {
            if (_lit) { _lit = false; RenderSettings.ambientLight = _roomBefore; }
            _follow = null;
            ClearStones();
        }

        // ── Scoring ─────────────────────────────────────────────────────────

        // A stone counts in a ring if any of it is over the ring.
        static int Points(Vector2 at)
        {
            float d = at.magnitude - StoneR;
            for (int i = 0; i < RingRadius.Length; i++) if (d <= RingRadius[i]) return RingPoints[i];
            return 0;
        }

        // Every stone where it lies now: a knock changes earlier scores.
        void Tally()
        {
            if (_round >= Rounds) return;
            for (int i = 0; i < _players; i++) _roundScore[i, _round] = 0;
            foreach (Stone s in _stones) if (!s.Out && s.Player < _players) _roundScore[s.Player, _round] += Points(s.At);
            ShowScores();
        }

        void ShowScores()
        {
            Hud hud = Arcade.Hud;
            for (int i = 0; i < _players; i++)
            {
                int live = _round < Rounds && !_clear ? _roundScore[i, _round] : 0;
                for (int r = 0; r < Rounds; r++)
                {
                    bool shown = r < _round || (r == _round && !_clear);
                    hud.SetPip(i, r, !shown ? Hud.Pip.Empty : _roundScore[i, r] > 0 ? Hud.Pip.Good : Hud.Pip.Over, shown ? _roundScore[i, r].ToString() : "");
                }
                hud.SetTotal(i, _banked[i] + live);
            }
            hud.SetTurn(Over ? -1 : Active, Over ? -1 : _round);
            if (!Over) hud.SetBoardTitle("ROUND " + (_round + 1) + " OF " + Rounds + "      STONE " + Mathf.Min(StonesEach, _thrown / _players + 1) + " OF " + StonesEach);
        }
    }
}
