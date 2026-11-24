// Tug of War: two teams, one rope, and a mud pit in the middle.
//
// The putt is the pull. Everything is life size and in inches: the flag on
// the rope starts over the middle of the pit and moves one step (16 in) at
// a time. Five steps to your side wins the round; best of three.
//
//   holed    3 steps toward the putting team.
//   lip out  2 steps: missed inside 2 in of center, or in and out again.
//   missed   1 step, as long as the ball reached the hole.
//   short    the team slips, and the flag goes 1 step the other way.
//
// Players 1 and 2 pull for blue (left), Players 3 and 4 for amber (right).
// With one Player the right team is Puttr AI, which pulls by itself.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class TugOfWarMode : StageMode
    {
        public override string Name => "Tug of War";
        public override string VoicePrefix => "tugofwar";
        public override int Putter => Putting; // whose difficulty applies to this putt
        public override string Blurb => "Two teams, one rope, one mud pit.\nEvery putt hauls the flag your way.\nFirst to win two rounds.";
        public override int Score(int player) => _teamOf != null && player < _teamOf.Length ? _rounds[_teamOf[player]] : 0;
        public override string ScoreWord(int n) => n == 1 ? "round" : "rounds";
        public override string ScoreUnit => "rounds";
        public override GameAudio.Scene Sound => GameAudio.Scene.Stadium;
        // puttr-app-2: a field day of its own (the stadium's crowd until it is recorded). The air horn plays as the last round
        // is won, so the arcade adds no game-over once it is recorded.
        public override string BedSlot => "tugofwar_field_loop";
        public override string EndSlot => GameAudio.Has("tugofwar_win") ? "" : "game_over";
        public override string Rule => "Every putt pulls the flag your way. Pull it 5 steps to win the round.";
        public override string Icon => "UsersThree";
        public override Color Accent => Blue;
        public override string[] Tags => new[] { "Teams", "Best of 3" };
        public override int Difficulty => 1;
        public override int MaxPlayers => 4;
        public override string SoloOpponent => "Puttr";
        public override string[] HowTo => new[]
        {
            "Teams take turns, and partners take turns within a team. Reach the hole and the flag comes 1 step your way. Lip out: 2 steps. Hole it: 3 steps.",
            "Every putt is from a new distance and tee. Leave it short and your team slips: the flag moves 1 step the other way.",
            "The flag starts in the middle. Pull it 5 steps to your line to win the round. Best of three rounds, and the losing team starts the next one.",
        };

        const int Steps = 5, RoundsToWin = 2, RoundsInAll = 3;
        const float StepX = 16f;                // one step of the flag
        const float PitX = 50f, PitZ = 38f;     // the pit, half across and half deep
        const float Front = 134f, Gap = 46f;    // from the flag to a team's first puller, and on to the second
        const float RopeY = 36f, RopeZ = -9.4f, RopeR = 1.7f, Tail = 30f;
        const float LipLine = 2f;               // inches off center inside which a miss is a lip out
        const float Stand = 22f;                // degrees a puller leans back at rest

        static readonly Color Blue = Make.Hex(0x5687FF), Amber = Make.Hex(0xFFB300);
        static readonly Color BlueLight = Make.Hex(0x8FB0FF), AmberLight = Make.Hex(0xFFC94A);

        public override Vector3 CameraPosition => At(0f, 135f, -700f);
        public override Vector3 CameraLook => At(0f, 62f, 0f);
        public override float CameraFov => 30f;
        public override float ShadowDistance => 1200f;
        public override float BlurFrom => 1000f;
        public override float BlurTo => 1900f;

        // The set.
        Transform _rope, _flagMark;
        Transform[] _strands;
        readonly Figure[][] _pullers = { new Figure[2], new Figure[2] };

        // The game. Team 0 is blue, on the left; team 1 is amber, on the right.
        int _players, _turn, _flag;
        bool _house, _busy;
        int[] _teamOf;
        readonly int[][] _members = new int[2][];
        readonly int[] _rounds = new int[2], _next = new int[2], _count = { 2, 2 };
        readonly System.Random _rng = new System.Random(23);

        // What the scene is showing: where the flag is (in steps, negative to
        // the left), who is heaving (1) or being dragged (-1), who has fallen
        // (0 to 1) and who has let go of the rope (0 to 1).
        float _shown;
        readonly float[] _heave = new float[2], _fall = new float[2], _slack = new float[2], _hold = { Front + Gap, Front + Gap };

        static float Side(int team) => team == 0 ? -1f : 1f;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // Dusk, under floodlights: a warm key from in front and to one
            // side, and a cool one from behind to pick out edges.
            Sun(Root, new Vector3(40f, 28f, 0f), new Color(1f, 0.95f, 0.86f), 1.6f);
            Sun(Root, new Vector3(28f, -150f, 0f), new Color(0.62f, 0.76f, 1f), 0.6f).shadows = LightShadows.None;

            // The field. One tile of the texture is two mown bands, 24 feet.
            Props.Grass(out Texture2D turf, out Texture2D blades);
            Material grass = Make.Lit(Color.white, 0.14f, 0f, turf, blades, 0.9f);
            grass.SetTextureScale("_BaseMap", new Vector2(4320f / 288f, 2592f / 288f));
            Shape(PrimitiveType.Cube, "Field", Root, new Vector3(0, -1f, 300f), new Vector3(4320f, 2f, 2592f), grass);

            // The mud pit: a bank of churned earth, brown mud, and a darker wet middle.
            Material bank = Make.Lit(Make.Hex(0x4A3423), 0.15f);
            Material mud = Make.Lit(Make.Hex(0x5C4129), 0.35f);
            Material wet = Make.Lit(Make.Hex(0x2E2016), 0.9f);
            GameObject rim = Make.MeshObject("Pit bank", Root, Props.Torus(PitX, 2.6f), bank);
            rim.transform.localScale = new Vector3(1f, 0.7f, PitZ / PitX);
            GameObject pit = Make.MeshObject("Mud", Root, Make.Disc(1f, 72), mud);
            pit.transform.localPosition = new Vector3(0, 0.1f, 0);
            pit.transform.localScale = new Vector3(PitX, 1f, PitZ);
            GameObject pool = Make.MeshObject("Wet mud", Root, Make.Disc(1f, 72), wet);
            pool.transform.localPosition = new Vector3(3f, 0.16f, -2f);
            pool.transform.localScale = new Vector3(PitX * 0.66f, 1f, PitZ * 0.6f);
            // A few clods thrown out onto the grass.
            var rng = new System.Random(7);
            for (int i = 0; i < 16; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, far = 1.08f + (float)rng.NextDouble() * 0.3f;
                float size = 2.5f + (float)rng.NextDouble() * 4f;
                Shape(PrimitiveType.Sphere, "Clod", Root, new Vector3(Mathf.Cos(a) * PitX * far, 0.3f, Mathf.Sin(a) * PitZ * far), new Vector3(size, size * 0.45f, size * 0.8f), bank);
            }

            // Each team's winning line, and a tick for every step between them.
            Material chalk = Make.Glow(new Color(0.9f, 0.95f, 0.88f, 0.8f), false);
            Material faint = Make.Glow(new Color(0.9f, 0.95f, 0.88f, 0.4f), false);
            for (int side = -1; side <= 1; side += 2)
                Shape(PrimitiveType.Cube, "Winning line", Root, new Vector3(side * Steps * StepX, 0.05f, 0f), new Vector3(3.5f, 0.02f, 150f), chalk);
            for (int i = -Steps; i <= Steps; i++)
                Shape(PrimitiveType.Cube, "Step", Root, new Vector3(i * StepX, 0.05f, -62f), new Vector3(i == 0 ? 2.4f : 1.6f, 0.02f, i == 0 ? 16f : 9f), i == 0 ? chalk : faint);

            // A corner flag in each team's color at the back of its line.
            Material pole = Make.Lit(new Color(0.9f, 0.89f, 0.84f), 0.4f);
            Mesh pennant = Pennant(20f, 13f);
            for (int team = 0; team < 2; team++)
            {
                float x = Side(team) * Steps * StepX;
                Props.Rod("Line flag pole", Root, new Vector3(x, 0f, 78f), new Vector3(x, 58f, 78f), 0.7f, pole);
                Color color = team == 0 ? Blue : Amber;
                GameObject cloth = Make.MeshObject("Line flag", Root, pennant, Make.Emissive(color, color * 0.3f, 0.2f));
                cloth.transform.localPosition = new Vector3(x + 0.7f, 50f, 78f);
            }

            BuildRope(pole);
            BuildTeams();
            BuildSurround();
            Pose();
        }

        // The rope is a line of short lengths, laid again whenever it moves.
        void BuildRope(Material pole)
        {
            _rope = new GameObject("Rope").transform;
            _rope.SetParent(Root, false);
            Material light = Make.Lit(Make.Hex(0xD8B77A), 0.2f);
            Material dark = Make.Lit(Make.Hex(0x8E6E3C), 0.2f);
            _strands = new Transform[64];
            for (int i = 0; i < _strands.Length; i++)
                _strands[i] = Shape(PrimitiveType.Cylinder, "Rope length", _rope, Vector3.zero, Vector3.one, i % 2 == 0 ? light : dark).transform;

            // The flag tied at its middle.
            _flagMark = new GameObject("Flag").transform;
            _flagMark.SetParent(_rope, false);
            Color red = Make.Hex(0xFF5252);
            Material cloth = Make.Emissive(red, red * 0.35f, 0.2f);
            Shape(PrimitiveType.Sphere, "Knot", _flagMark, Vector3.zero, Vector3.one * 5.6f, cloth);
            Shape(PrimitiveType.Cylinder, "Flag pole", _flagMark, new Vector3(0, 16f, 0), new Vector3(1.3f, 16f, 1.3f), pole);
            Make.MeshObject("Flag cloth", _flagMark, Pennant(24f, 15f), cloth).transform.localPosition = new Vector3(0.6f, 24f, 0f);
        }

        void BuildTeams()
        {
            Material shorts = Make.Lit(new Color(0.04f, 0.05f, 0.07f), 0.3f);
            Material sock = Make.Lit(new Color(0.9f, 0.9f, 0.88f), 0.3f);
            Material boot = Make.Lit(new Color(0.02f, 0.02f, 0.02f), 0.5f);
            Color[] shirts = { Blue, Make.Hex(0x3D68D8), Amber, Make.Hex(0xD99400) };
            Color[] skins = { Make.Hex(0xE8C7A6), Make.Hex(0xC99A72), Make.Hex(0xE8C7A6), Make.Hex(0x8A5E3C) };
            Color[] hairs = { new Color(0.1f, 0.07f, 0.05f), new Color(0.3f, 0.2f, 0.08f), new Color(0.05f, 0.04f, 0.04f), new Color(0.02f, 0.02f, 0.02f) };
            for (int team = 0; team < 2; team++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int k = team * 2 + j;
                    _pullers[team][j] = new Figure(Root, new Figure.Look
                    {
                        Top = Make.Lit(shirts[k], 0.35f),
                        Bottom = shorts,
                        Skin = Make.Lit(skins[k], 0.4f),
                        Sock = sock,
                        Boot = boot,
                        Hair = Make.Lit(hairs[k], 0.5f),
                    });
                }
            }
        }

        // Behind the field: a rail, a line of trees, the floodlights and the last of the light.
        void BuildSurround()
        {
            Material wood = Make.Lit(new Color(0.8f, 0.78f, 0.72f), 0.3f);
            for (int i = -9; i <= 9; i++)
                Shape(PrimitiveType.Cube, "Rail post", Root, new Vector3(i * 72f, 17f, 190f), new Vector3(4f, 34f, 4f), wood);
            Shape(PrimitiveType.Cube, "Rail", Root, new Vector3(0, 31f, 190f), new Vector3(1340f, 3f, 2.4f), wood);
            Shape(PrimitiveType.Cube, "Rail", Root, new Vector3(0, 17f, 190f), new Vector3(1340f, 3f, 2.4f), wood);

            // Each tree is a trunk and a crown of a few overlapping clumps in slightly different
            // greens, so the line reads as trees at dusk rather than a row of black eggs.
            var greens = new[]
            {
                Make.Lit(new Color(0.035f, 0.075f, 0.04f), 0.08f),
                Make.Lit(new Color(0.05f, 0.1f, 0.045f), 0.08f),
                Make.Lit(new Color(0.028f, 0.06f, 0.038f), 0.08f),
            };
            Material bark = Make.Lit(new Color(0.06f, 0.045f, 0.035f), 0.1f);
            var rng = new System.Random(4);
            for (int i = 0; i < 46; i++)
            {
                float x = -1900f + i * 84f + (float)rng.NextDouble() * 40f;
                float z = 760f + (float)rng.NextDouble() * 60f;
                float tall = 150f + (float)rng.NextDouble() * 130f;
                Shape(PrimitiveType.Cylinder, "Trunk", Root, new Vector3(x, tall * 0.2f, z), new Vector3(10f, tall * 0.2f, 10f), bark);
                int clumps = 3 + rng.Next(3);
                for (int c = 0; c < clumps; c++)
                {
                    float s = tall * (0.42f + (float)rng.NextDouble() * 0.2f);
                    var at = new Vector3(x + ((float)rng.NextDouble() - 0.5f) * 90f, tall * (0.5f + (float)rng.NextDouble() * 0.38f), z + ((float)rng.NextDouble() - 0.5f) * 40f);
                    Shape(PrimitiveType.Sphere, "Leaves", Root, at, new Vector3(s * 1.15f, s, s * 0.8f), greens[rng.Next(greens.Length)]);
                }
            }

            Material mast = Make.Lit(new Color(0.015f, 0.016f, 0.018f), 0.4f, 0.5f);
            Material lamp = Make.Emissive(Color.white, new Color(0.9f, 0.96f, 1f) * 9f);
            Material lampGlow = Make.Glow(new Color(0.85f, 0.93f, 1f, 0.26f), true, Make.SoftDot());
            for (int side = -1; side <= 1; side += 2)
            {
                var bank = new Vector3(side * 470f, 330f, 640f);
                Props.Rod("Floodlight mast", Root, new Vector3(bank.x, 0f, bank.z + 6f), new Vector3(bank.x, bank.y, bank.z + 6f), 4f, mast);
                Shape(PrimitiveType.Cube, "Floodlight frame", Root, bank, new Vector3(130f, 62f, 6f), mast);
                for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 3; j++)
                        Shape(PrimitiveType.Sphere, "Floodlight", Root, bank + new Vector3((i - 2f) * 23f, (j - 1f) * 18f, -4f), new Vector3(10f, 10f, 4f), lamp);
                Card("Floodlight glow", Root, bank + new Vector3(0, 0, -8f), new Vector2(520f, 400f), lampGlow);
            }

            Card("Sky", Root, new Vector3(0, 700f, 830f), new Vector2(4400f, 1400f), Make.Glow(Color.white, false, Dusk()));
        }

        // The sky after sunset: a warm band low down, going to deep blue.
        static Texture2D Dusk()
        {
            const int h = 128;
            var px = new Color[h * 2];
            var low = new Color(0.42f, 0.2f, 0.09f, 1f);
            var mid = new Color(0.07f, 0.08f, 0.14f, 1f);
            var high = new Color(0.008f, 0.012f, 0.03f, 1f);
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                Color c = v < 0.22f ? Color.Lerp(low, mid, Ease(v / 0.22f)) : Color.Lerp(mid, high, Ease((v - 0.22f) / 0.78f));
                px[y * 2] = px[y * 2 + 1] = c;
            }
            var tex = new Texture2D(2, h, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        // A triangle of cloth flying to the right of its pole, seen from both sides.
        static Mesh Pennant(float length, float height)
        {
            var top = new Vector3(0, height * 0.5f, 0);
            var tip = new Vector3(length, 0, 0);
            var foot = new Vector3(0, -height * 0.5f, 0);
            var mesh = new Mesh { name = "Pennant" };
            mesh.vertices = new[] { top, tip, foot, top, tip, foot };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.triangles = new[] { 0, 1, 2, 3, 5, 4 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── The game ────────────────────────────────────────────────────────


        public override void Begin(int players)
        {
            HideTeeSign();
            Coat(0, 0f);
            Coat(1, 0f);
            DealtFeetNow = 0;
            _players = Mathf.Clamp(players, 1, 4);
            _house = _players == 1;
            // Players 1 and 2 are a couple, and so are 3 and 4.
            _members[0] = _players >= 3 ? new[] { 0, 1 } : new[] { 0 };
            _members[1] = _players == 4 ? new[] { 2, 3 } : _players == 3 ? new[] { 2 } : _players == 2 ? new[] { 1 } : new int[0];
            _teamOf = new int[_players];
            for (int team = 0; team < 2; team++)
                foreach (int player in _members[team]) _teamOf[player] = team;

            _turn = 0;
            _flag = 0;
            _busy = false;
            for (int team = 0; team < 2; team++)
            {
                _rounds[team] = 0;
                _next[team] = 0;
                // Puttr AI turns out two pullers.
                _count[team] = _house && team == 1 ? 2 : Mathf.Max(1, _members[team].Length);
                _hold[team] = Front + (_count[team] - 1) * Gap;
                _pullers[team][1].Root.gameObject.SetActive(_count[team] > 1);
            }
            Arcade.Hud.BuildBoard(new List<string> { TeamName(0), TeamName(1) }, Steps, "ROUNDS", true, "FLAG");
            ResetSet();
            ShowBoard();
        }

        void ResetSet()
        {
            _shown = _flag;
            for (int team = 0; team < 2; team++) _heave[team] = _fall[team] = _slack[team] = 0f;
            Pose();
        }

        string TeamName(int team)
        {
            int[] members = _members[team];
            if (members.Length == 0) return "Puttr";
            string first = Arcade.PlayerName(members[0]);
            if (members.Length == 1) return first;
            string second = Arcade.PlayerName(members[1]);
            // Two Players who have not given their names.
            if (first == "Player " + (members[0] + 1) && second == "Player " + (members[1] + 1)) return "Players " + (members[0] + 1) + " & " + (members[1] + 1);
            string both = first + " & " + second;
            return both.Length <= 14 ? both : team == 0 ? "Blue team" : "Amber team";
        }

        int Putting => _members[_turn][_next[_turn] % _members[_turn].Length];

        public override string Prompt
        {
            get
            {
                if (_house) return "Round " + Round + " of " + RoundsInAll + "   ·   Putt to pull against Puttr";
                return Arcade.PlayerName(Putting) + " to putt for the " + (_turn == 0 ? "blue" : "amber") + " team";
            }
        }

        int Round => Mathf.Min(RoundsInAll, _rounds[0] + _rounds[1] + 1);

        public override IEnumerator Play(PuttResult putt)
        {
            _busy = true;
            Hud hud = Arcade.Hud;
            int team = _turn, other = 1 - team;
            bool holed = putt.Made;
            bool slipped = !holed && !putt.Launch.Reached;
            bool lip = !holed && !slipped && (putt.HasEntry || Mathf.Abs(Line(putt)) < LipLine);
            int steps = holed ? 3 : slipped ? 1 : lip ? 2 : 1;
            int gains = slipped ? other : team;
            Color color = team == 0 ? BlueLight : AmberLight;

            int from = _flag;
            _flag = Mathf.Clamp(_flag + (int)Side(gains) * steps, -Steps, Steps);

            if (slipped)
            {
                hud.Banner("SLIPPED", "Left short: the flag goes 1 step to " + TeamName(other), Make.Red, 2f);
                ShowPutt(putt, "Short", Make.Red);
                // The tugofwar_heave and tugofwar_slip recordings are not played: they crackle (Matt, 2026-11-23).
                Arcade.Audio.Miss(); Arcade.Audio.Groan();
            }
            else
            {
                string sub = holed ? "Holed it: 3 steps to " + TeamName(team) : lip ? "Lipped out: 2 steps to " + TeamName(team) : "Reached the hole: 1 step to " + TeamName(team);
                hud.Banner("HEAVE!  " + steps + (steps == 1 ? " STEP" : " STEPS"), sub, color, 2f);
                ShowPutt(putt, holed ? "Holed" : lip ? "Lipped out" : "Reached", Make.Bright);
                // puttr-app-2: the rope and the team's shout (the shared thud and stings until they are recorded).
                Arcade.Audio.Thump(24f + steps * 12f);
                if (holed) { if (Arcade.Audio.TryLibrary("tugofwar_big_pull", 1f)) Arcade.Audio.Excite(1f); else { Arcade.Audio.Strike(); Arcade.Audio.Roar(true); } Arcade.Shake(0.4f); }
                else if (lip) { if (Arcade.Audio.TryLibrary("tugofwar_big_pull", 0.75f, 1.05f)) Arcade.Audio.Excite(0.65f); else { Arcade.Audio.Spare(); Arcade.Audio.Roar(false); } }
            }

            yield return Pull(gains, from, _flag);
            ShowBoard();
            Arcade.Shot("result");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return TurnDone(team);

            // Puttr AI needs no putt: it pulls as soon as it is its turn.
            while (_house && !Over && _turn == 1)
            {
                yield return new WaitForSecondsRealtime(0.9f);
                int pull = _rng.Next(3) < 2 ? 1 : 2;
                from = _flag;
                _flag = Mathf.Clamp(_flag + pull, -Steps, Steps);
                hud.Banner("PUTTR PULLS", pull == 1 ? "1 step" : "2 steps", AmberLight, 1.8f);
                Arcade.Audio.Thump(24f + pull * 12f);
                yield return Pull(1, from, _flag);
                ShowBoard();
                Arcade.Shot("house");
                yield return new WaitForSecondsRealtime(0.4f);
                yield return TurnDone(1);
            }
            _busy = false;
        }

        // Move the flag over about 0.8 s: one team heaves back, the other is dragged forward.
        IEnumerator Pull(int toward, int from, int to)
        {
            const float seconds = 0.8f;
            bool shot = false;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float u = t / seconds;
                // A sharp tug, then the rope settles.
                _shown = Mathf.Lerp(from, to, Ease(Mathf.Clamp01(u * 1.2f)));
                float effort = Mathf.Sin(Mathf.PI * u);
                _heave[toward] = effort;
                _heave[1 - toward] = -effort;
                Pose();
                if (!shot && u > 0.5f) { shot = true; Arcade.Shot("action"); }
                yield return null;
            }
            _shown = to;
            _heave[0] = _heave[1] = 0f;
            Pose();
        }

        // After a team's turn: is the round won, and who pulls next?
        IEnumerator TurnDone(int team)
        {
            _next[team]++;
            if (Mathf.Abs(_flag) < Steps)
            {
                _turn = 1 - team;
                ShowBoard();
                yield break;
            }

            int winner = _flag < 0 ? 0 : 1, loser = 1 - winner;
            _rounds[winner]++;
            bool match = Over;
            Hud hud = Arcade.Hud;
            hud.SetTotal(winner, _rounds[winner]);
            hud.SetTurn(-1, -1);
            string won = TeamName(winner) + (_members[winner].Length == 2 ? " win" : " wins");
            if (Arcade.Audio.TryLibrary("tugofwar_win", 1f)) Arcade.Audio.Excite(1f);   // puttr-app-2: the air horn, the shared bell and roar until recorded
            else { Arcade.Audio.Bell(); Arcade.Audio.Roar(true); }

            // The losers are hauled to the edge of the pit, let go, and go in face first.
            bool landed = false;
            for (float t = 0; t < 1f; t += Time.deltaTime)
            {
                _fall[loser] = Ease(t);
                _slack[loser] = Ease(Mathf.Clamp01(t * 1.6f));
                _heave[winner] = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.3f)) * 0.8f;
                Pose();
                if (t > 0.7f) Coat(loser, Mathf.Max(_mud[loser], Mathf.InverseLerp(0.7f, 1f, t)));
                if (!landed && t > 0.7f)
                {
                    landed = true;
                    if (!Arcade.Audio.TryLibrary("tugofwar_mud_splat", 1f)) Arcade.Audio.Land();   // puttr-app-2: into the mud, Send It's landing until recorded
                    Arcade.Shake(0.6f);
                    for (int j = 0; j < _count[loser]; j++) Arcade.StartCoroutine(Splash(new Vector3(Side(loser) * MudSpot(j) - Side(loser) * 20f, 1f, 0f)));
                    Arcade.Celebrate(match ? 120 : 70, At(_shown * StepX + Side(winner) * (Front + Gap * 0.5f), 70f, -20f));
                }
                yield return null;
            }
            _fall[loser] = _slack[loser] = 1f;
            Coat(loser, 1f);
            // The banner waits for the splash: it sits across the pit and would hide the fall.
            hud.Banner(match ? "IN THE MUD" : "ROUND " + (_rounds[0] + _rounds[1]), match ? won + " the tug of war" : won + " the round", winner == 0 ? BlueLight : AmberLight, 2.6f);
            _heave[winner] = 0f;
            Pose();
            Arcade.Shot("round");
            yield return new WaitForSecondsRealtime(2.2f);
            if (match) { HideTeeSign(); yield break; } // they stay in the mud for the end card

            // Back on their feet and back to the middle. The losers pull first.
            _flag = 0;
            _turn = loser;
            float start = _shown;
            for (float t = 0; t < 0.7f; t += Time.deltaTime)
            {
                float k = Ease(t / 0.7f);
                _shown = Mathf.Lerp(start, 0f, k);
                _fall[loser] = _slack[loser] = 1f - k;
                Pose();
                yield return null;
            }
            ResetSet();
            ShowBoard();
        }

        // Each row's marks are how far the flag is on that team's side.
        void ShowBoard()
        {
            Hud hud = Arcade.Hud;
            for (int team = 0; team < 2; team++)
            {
                int ahead = Mathf.Max(0, (int)Side(team) * _flag);
                for (int i = 0; i < Steps; i++) hud.SetPip(team, i, i < ahead ? Hud.Pip.Good : Hud.Pip.Empty);
                hud.SetTotal(team, _rounds[team]);
            }
            hud.SetTurn(Over ? -1 : _turn, -1);
            hud.SetBoardTitle("ROUND " + Round + " OF " + RoundsInAll);
        }

        public override bool Over => _rounds[0] >= RoundsToWin || _rounds[1] >= RoundsToWin;

        public override Hud.Line[] Results(out string headline)
        {
            int winner = _rounds[0] > _rounds[1] ? 0 : _rounds[1] > _rounds[0] ? 1 : -1;
            headline = winner < 0 ? "All square" : TeamName(winner) + " win";
            var lines = new Hud.Line[2];
            for (int team = 0; team < 2; team++)
                lines[team] = new Hud.Line { Name = TeamName(team), Value = _rounds[team] + " " + ScoreWord(_rounds[team]), Dot = team == 0 ? Make.Hex(0x5687FF) : Make.Hex(0xFFB300), Lit = team == winner, Tag = team == winner ? "WINNER" : "" };
            return lines;
        }

        public override void Final(out string title, out string body)
        {
            int winner = _rounds[0] > _rounds[1] ? 0 : _rounds[1] > _rounds[0] ? 1 : -1;
            body = "";
            if (_house) title = winner == 0 ? "Y O U   W I N" : winner == 1 ? "I N   T H E   M U D" : "A L L   S Q U A R E";
            else title = winner < 0 ? "A L L   S Q U A R E" : Hud.Spaced(TeamName(winner) + (_members[winner].Length == 2 ? " win" : " wins"));
            for (int team = 0; team < 2; team++)
                body += TeamName(team) + "     " + _rounds[team] + " " + ScoreWord(_rounds[team]) + (team == winner ? "     winner" : "") + "\n";
        }

        // Between putts the teams hold the strain, and sway a little.
        public override void Tick()
        {
            if (!_busy && _rope != null) Pose();
        }

        public override void End()
        {
            _busy = false;
            HideTeeSign();
        }

        // ── In the mud ──────────────────────────────────────────────────────

        static readonly Color MudBrown = Make.Hex(0x4A321E);
        readonly float[] _mud = new float[2];
        MaterialPropertyBlock _coatBlock;
        Material _splashPaint;

        // Where a fallen puller's hips land, inches from the middle of the pit on their side.
        static float MudSpot(int j) => 14f + j * 24f;

        // A team in mud from 0 (clean) to 1 (covered): every part tinted toward wet mud, unevenly,
        // and a little glossier. It stays on them for the rest of the match.
        void Coat(int team, float amount)
        {
            _mud[team] = amount;
            if (_coatBlock == null) _coatBlock = new MaterialPropertyBlock();
            foreach (Figure f in _pullers[team])
            {
                if (f == null) continue;
                int part = 0;
                foreach (Renderer r in f.Root.GetComponentsInChildren<Renderer>(true))
                {
                    part++;
                    if (amount <= 0f) { r.SetPropertyBlock(null); continue; }
                    Material m = r.sharedMaterial;
                    Color was = m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    // Patchy: some parts take the mud full on, some only a streak of it.
                    float patch = 0.55f + 0.45f * Make.Hash(part, team, 7);
                    _coatBlock.Clear();
                    _coatBlock.SetColor("_BaseColor", Color.Lerp(was, MudBrown, amount * patch));
                    _coatBlock.SetFloat("_Smoothness", Mathf.Lerp(0.3f, 0.62f, amount));
                    r.SetPropertyBlock(_coatBlock);
                }
            }
        }

        // A body hitting the mud: clods thrown up and out, falling back, and gone.
        IEnumerator Splash(Vector3 at)
        {
            if (_splashPaint == null) _splashPaint = Make.Lit(MudBrown, 0.6f);
            const int n = 26;
            var clods = new Transform[n];
            var vel = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Make.Kill(go.GetComponent<Collider>());
                go.name = "Mud";
                go.transform.SetParent(Root, false);
                go.GetComponent<MeshRenderer>().sharedMaterial = _splashPaint;
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float size = Random.Range(1.2f, 3.6f);
                go.transform.localScale = new Vector3(size, size * 0.8f, size);
                go.transform.localPosition = at + new Vector3(Random.Range(-10f, 10f), 0f, Random.Range(-8f, 8f));
                float a = Random.Range(0f, Mathf.PI * 2f), out_ = Random.Range(25f, 85f);
                vel[i] = new Vector3(Mathf.Cos(a) * out_, Random.Range(110f, 230f), Mathf.Sin(a) * out_ * 0.6f);
                clods[i] = go.transform;
            }
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                for (int i = 0; i < n; i++)
                {
                    if (clods[i] == null) continue;
                    vel[i].y -= LaneSpec.Gravity * 0.9f * Time.deltaTime;
                    Vector3 p = clods[i].localPosition + vel[i] * Time.deltaTime;
                    if (p.y < 0.5f) { p.y = 0.5f; vel[i] = Vector3.zero; } // back in the mud: it stays a moment
                    clods[i].localPosition = p;
                    if (t > 1.1f) clods[i].localScale *= 1f - Time.deltaTime * 4f; // sinks back in
                }
                yield return null;
            }
            foreach (Transform c in clods) if (c != null) Make.Kill(c.gameObject);
        }

        // ── The rope and the pullers ────────────────────────────────────────

        // Put everything where the numbers say it is.
        void Pose()
        {
            float x = _shown * StepX;
            _rope.localPosition = new Vector3(x, 0f, RopeZ);

            // The rope: taut while someone is heaving, a little slack otherwise.
            float sag = Mathf.Lerp(5f, 1.2f, Mathf.Max(Mathf.Abs(_heave[0]), Mathf.Abs(_heave[1])));
            float left = -(_hold[0] + Tail), right = _hold[1] + Tail;
            Vector3 last = RopeAt(left, sag);
            for (int i = 0; i < _strands.Length; i++)
            {
                Vector3 next = RopeAt(Mathf.Lerp(left, right, (i + 1f) / _strands.Length), sag);
                _strands[i].localPosition = (last + next) * 0.5f;
                _strands[i].localRotation = Quaternion.FromToRotation(Vector3.up, next - last);
                _strands[i].localScale = new Vector3(RopeR * 2f, (next - last).magnitude * 0.5f, RopeR * 2f);
                last = next;
            }
            _flagMark.localPosition = RopeAt(0f, sag);

            for (int team = 0; team < 2; team++)
            {
                for (int j = 0; j < _count[team]; j++)
                {
                    float sway = Mathf.Sin(Time.time * 1.7f + j * 1.9f + team * 0.8f) * 1.6f;
                    float lean = Stand + sway + (_heave[team] > 0 ? 17f : 15f) * _heave[team];
                    // Falling, they are dragged on into the pit and land in it, face first: the
                    // first a little way in, the second just behind.
                    float standing = x + Side(team) * (Front + j * Gap);
                    float at = Mathf.Lerp(standing, Side(team) * MudSpot(j), Ease(_fall[team]));
                    PosePuller(_pullers[team][j], team, at, lean, at / 11f + j * 1.4f, _fall[team]);
                }
            }
        }

        // A point on the rope, x inches from the flag. It sags between the
        // teams, runs level through their hands, and trails on the grass behind.
        Vector3 RopeAt(float x, float sag)
        {
            int team = x < 0 ? 0 : 1;
            float d = Mathf.Abs(x), y;
            if (d <= Front) y = RopeY - sag * (1f - d * d / (Front * Front));
            else if (d <= _hold[team]) y = RopeY;
            else y = Mathf.Lerp(RopeY, RopeR, Ease(Mathf.Clamp01((d - _hold[team]) / Tail)));
            // A team that has let go drops its end.
            y = Mathf.Lerp(y, RopeR, _slack[team] * Mathf.Clamp01((d - 20f) / 50f));
            return new Vector3(x, y, 0f);
        }

        // How far below the hips a foot is, for a body leaning back by body
        // degrees with the thigh and shin turned forward by leg and shin.
        static float Drop(float body, float leg, float shin)
        {
            return 2f * Mathf.Cos(body * Mathf.Deg2Rad) + 17f * Mathf.Cos((body + leg) * Mathf.Deg2Rad) + 19.1f * Mathf.Cos((body + leg + shin) * Mathf.Deg2Rad);
        }

        // One puller: hips at x, facing the pit, leaning back by lean degrees
        // with both hands on the rope. phase walks the legs as the rope moves.
        // fall is 0 on their feet, 1 face down in the mud.
        void PosePuller(Figure f, int team, float x, float lean, float phase, float fall)
        {
            float body = Mathf.Lerp(lean, -84f, fall);
            float swing = Mathf.Sin(phase) * 12f * (1f - fall);

            // One leg braced out in front, the other bent under the body. The
            // back knee bends as far as it takes to put both feet on the grass.
            float frontLeg = Mathf.Lerp(8f + swing, -4f, fall), backLeg = Mathf.Lerp(-26f - swing, -4f, fall);
            float frontDrop = Drop(body, frontLeg, 0f);
            float reach = (frontDrop - 2f * Mathf.Cos(body * Mathf.Deg2Rad) - 17f * Mathf.Cos((body + backLeg) * Mathf.Deg2Rad)) / 19.1f;
            float backShin = reach >= 1f ? 0f : Mathf.Min(0f, -Mathf.Acos(Mathf.Clamp(reach, -1f, 1f)) * Mathf.Rad2Deg - (body + backLeg));
            backShin = Mathf.Lerp(backShin, -8f, fall);
            float hips = Mathf.Max(frontDrop, Drop(body, backLeg, backShin));
            hips = Mathf.Lerp(hips, 6.5f, Ease(fall));

            f.Root.localPosition = new Vector3(x, hips, 0f);
            f.Root.localRotation = Quaternion.Euler(0f, team == 0 ? -90f : 90f, 0f) * Quaternion.Euler(body, 0f, 0f);
            f.Chest.localRotation = Quaternion.identity;
            // Eyes on the other team; out of the mud once down.
            f.Head.localRotation = Quaternion.Euler(Mathf.Lerp(-lean * 0.55f, 24f, fall), 0f, 0f);

            // Arms down and forward to the rope, which runs past the hip
            // nearer the camera. The far arm reaches across to it.
            float shoulder = hips + 21.4f * Mathf.Cos(body * Mathf.Deg2Rad);
            float down = Mathf.Acos(Mathf.Clamp((shoulder - RopeY) / 24.5f, -1f, 1f)) * Mathf.Rad2Deg;
            int near = team == 0 ? 0 : 1;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                bool far = i != near;
                float arm = Mathf.Lerp(down - body + (far ? 7f : 0f), 150f, fall);
                float across = (far ? 36f : 2f) * (1f - fall);
                f.Arm[i].localRotation = Quaternion.Euler(arm, 0f, -side * across);
                f.Fore[i].localRotation = Quaternion.Euler(0f, 0f, -side * (far ? 16f : 0f) * (1f - fall));
                bool front = i == near;
                f.Leg[i].localRotation = Quaternion.Euler(front ? frontLeg : backLeg, 0f, side * 3f);
                f.Shin[i].localRotation = Quaternion.Euler(front ? 0f : backShin, 0f, 0f);
            }
        }
    }
}
