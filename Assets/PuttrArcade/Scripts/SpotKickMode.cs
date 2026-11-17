// Spot Kick: a penalty shootout.
//
// The putt is the kick. Everything is life size and in inches: the goal is
// 24 feet by 8, twelve yards away.
//
//   holed   the kick is on target. Where the ball went into the cup is
//           where it goes in the goal: left edge of the cup, left side.
//   missed  the kick misses on the same side. A lip-out hits the post.
//   short   it never reaches the hole, and rolls to the keeper.
//   pace    how high: up to 26 in/s it stays on the grass, and a holed putt
//           at about 4.7 ft/s goes in just under the bar.
//
// The keeper cannot see the kick coming, but remembers. It goes where this
// Player's last kicks went, so the same putt twice is a save.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class SpotKickMode : StageMode
    {
        public override string Name => "Spot Kick";
        public override string VoicePrefix => "spotkick";
        public override int Putter => Active; // whose difficulty applies to this putt
        public override string Blurb => "A penalty shootout.\nLine aims it, pace lifts it.\nThe keeper learns your habits.";
        public override int Score(int player) => _points[player];
        public override GameAudio.Scene Sound => GameAudio.Scene.Stadium;
        public override string Rule => "Hole it to hit the target. Miss the hole and you miss the goal.";
        public override string Icon => "SoccerBall";
        public override Color Accent => new Color(0.4f, 0.73f, 0.42f);
        public override string[] Tags => new[] { "5 kicks", "Line and pace" };
        public override string[] HowTo => new[]
        {
            "Hole the putt and the kick is on target. Miss left or right and it goes wide on that side. Leave it short and it rolls to the keeper.",
            "Where the ball enters the cup is where the kick goes: the left edge of the cup is the left side of the goal.",
            "A firmer putt flies higher. The keeper dives where your last kicks went, so change sides. Top corners count double.",
        };

        const int Kicks = 5;
        const float GoalZ = 432f, HalfGoal = 144f, BarY = 96f, BallR = 4.4f;
        const float InchesAcrossPerInch = 30f;
        const float GroundPace = 26f, RisePerPace = 2.8f;
        const float CornerX = 100f, CornerY = 66f;
        const float CupEdge = 1.9f; // inches off center at which a putt only just drops

        // A replay (top bins) looks in close from beside the corner the ball went into.
        Vector3? _replayFrom, _replayAt;
        public override Vector3 CameraPosition => _replayFrom is Vector3 f ? At(f.x, f.y, f.z) : At(0f, 70f, -260f);
        public override Vector3 CameraLook => _replayAt is Vector3 a ? At(a.x, a.y, a.z) : At(0f, 52f, GoalZ);
        public override float CameraFov => _replayFrom.HasValue ? 17f : 34f;
        public override float ShadowDistance => 1250f;
        public override float BlurFrom => 800f;
        public override float BlurTo => 1500f;

        Transform _ball;
        Figure _keeper;
        const float KeeperMid = Figure.HipHeight, KeeperReach = Figure.Reach;
        TrailRenderer _trail;
        int _players, _kick;
        int[] _points;
        List<Vector2>[] _history;
        readonly System.Random _rng = new System.Random(11);

        int Active => _kick % _players;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // Floodlit from in front and to one side, with a cooler light from
            // behind the goal to pick out edges.
            Sun(Root, new Vector3(44f, 26f, 0f), new Color(1f, 0.98f, 0.94f), 1.7f);
            Sun(Root, new Vector3(30f, -155f, 0f), new Color(0.7f, 0.82f, 1f), 0.5f).shadows = LightShadows.None;

            // Turf. One tile of the texture is two mown bands, 24 feet.
            Props.Grass(out Texture2D turf, out Texture2D blades);
            Material grass = Make.Lit(Color.white, 0.14f, 0f, turf, blades, 0.9f);
            grass.SetTextureScale("_BaseMap", new Vector2(4320f / 288f, 2592f / 288f));
            Shape(PrimitiveType.Cube, "Pitch", Root, new Vector3(0, -1f, 432f), new Vector3(4320f, 2f, 2592f), grass);

            // The lines.
            Material chalk = Make.Glow(new Color(0.9f, 0.95f, 0.88f, 0.72f), false);
            void Chalk(float x, float z, float w, float d) => Shape(PrimitiveType.Cube, "Line", Root, new Vector3(x, 0.05f, z), new Vector3(w, 0.02f, d), chalk);
            Chalk(0, GoalZ, 4200f, 5f);
            Chalk(0, GoalZ - 216f, 725f, 5f);
            Chalk(-360f, GoalZ - 108f, 5f, 216f);
            Chalk(360f, GoalZ - 108f, 5f, 216f);
            Chalk(0, GoalZ - 648f, 1589f, 5f);
            Chalk(-792f, GoalZ - 324f, 5f, 648f);
            Chalk(792f, GoalZ - 324f, 5f, 648f);
            GameObject spot = Make.MeshObject("Spot", Root, Make.Disc(4.5f), chalk);
            spot.transform.localPosition = new Vector3(0, 0.06f, 0);

            // The goal: round posts, five inches across.
            Material post = Make.Lit(new Color(0.93f, 0.93f, 0.93f), 0.75f);
            Shape(PrimitiveType.Cylinder, "Post", Root, new Vector3(-HalfGoal - 2.5f, 49.25f, GoalZ), new Vector3(5f, 49.25f, 5f), post);
            Shape(PrimitiveType.Cylinder, "Post", Root, new Vector3(HalfGoal + 2.5f, 49.25f, GoalZ), new Vector3(5f, 49.25f, 5f), post);
            Shape(PrimitiveType.Cylinder, "Bar", Root, new Vector3(0, BarY + 2.5f, GoalZ), new Vector3(5f, HalfGoal + 5f, 5f), post).transform.localRotation = Quaternion.Euler(0, 0, 90f);
            for (int side = -1; side <= 1; side += 2)
                Props.Rod("Stanchion", Root, new Vector3(side * (HalfGoal + 2.5f), BarY, GoalZ + 2f), new Vector3(side * (HalfGoal + 2.5f), 0f, GoalZ + 62f), 1.1f, post);
            BuildNet();

            // The corners that count double.
            Material zone = Make.Glow(new Color(0.48f, 0.83f, 0.27f, 0.07f), false);
            Material edge = Make.Glow(new Color(0.48f, 0.83f, 0.27f, 0.6f), false);
            for (int side = -1; side <= 1; side += 2)
            {
                float cx = side * (CornerX + HalfGoal) * 0.5f, cy = (CornerY + BarY) * 0.5f, w = HalfGoal - CornerX, h = BarY - CornerY;
                Card("Corner", Root, new Vector3(cx, cy, GoalZ + 1f), new Vector2(w, h), zone);
                Card("Corner edge", Root, new Vector3(cx, CornerY, GoalZ + 1f), new Vector2(w, 0.6f), edge);
                Card("Corner edge", Root, new Vector3(side * CornerX, cy, GoalZ + 1f), new Vector2(0.6f, h), edge);
                Words(Root, "x2", new Vector3(side * (CornerX + 9f), CornerY + 6f, GoalZ + 1f), 5f, new Color(0.48f, 0.83f, 0.27f, 0.9f));
            }

            // Behind: lit boards, a bank of seats going up and away, and the floodlights.
            Shape(PrimitiveType.Cube, "Boards", Root, new Vector3(0, 19f, 640f), new Vector3(4200f, 38f, 4f), Make.Emissive(new Color(0.01f, 0.02f, 0.015f), new Color(0.02f, 0.09f, 0.04f), 0.6f));
            var logo = Resources.Load<Texture2D>("PuttrArcade/LogoHeader");
            if (logo != null)
            {
                Material mark = Make.Glow(new Color(1.15f, 1.15f, 1.15f, 1f), false, logo); // the logo's own green mark and white letters
                for (int i = -3; i <= 3; i++)
                    Card("Board logo", Root, new Vector3(i * 330f, 19f, 637.5f), new Vector2(110f, 110f * logo.height / logo.width), mark);
            }
            Material stand = Make.Glow(Color.white, false, Crowd());
            stand.SetTextureScale("_MainTex", new Vector2(3f, 2f));
            Card("Stand", Root, new Vector3(0, 400f, 1250f), new Vector2(4800f, 1000f), stand).transform.localRotation = Quaternion.Euler(32f, 0, 0);
            Shape(PrimitiveType.Cube, "Stand wall", Root, new Vector3(0, 30f, 1000f), new Vector3(4800f, 60f, 6f), Make.Lit(new Color(0.02f, 0.022f, 0.025f), 0.3f));
            Material lampGlow = Make.Glow(new Color(0.85f, 0.93f, 1f, 0.28f), true, Make.SoftDot());
            Material lamp = Make.Emissive(Color.white, new Color(0.9f, 0.96f, 1f) * 9f);
            Material lampFrame = Make.Lit(new Color(0.015f, 0.016f, 0.018f), 0.4f, 0.5f);
            for (int side = -1; side <= 1; side += 2)
            {
                var bank = new Vector3(side * 400f, 300f, 1040f);
                Shape(PrimitiveType.Cube, "Floodlight frame", Root, bank, new Vector3(150f, 66f, 6f), lampFrame);
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 3; j++)
                        Shape(PrimitiveType.Sphere, "Floodlight", Root, bank + new Vector3((i - 2.5f) * 23f, (j - 1f) * 19f, -4f), new Vector3(10f, 10f, 4f), lamp);
                Card("Floodlight glow", Root, bank + new Vector3(0, 0, -8f), new Vector2(560f, 420f), lampGlow);
            }

            // The keeper.
            _keeper = new Figure(Root, new Figure.Look
            {
                Top = Make.Lit(new Color(0.98f, 0.62f, 0.02f), 0.35f),
                Bottom = Make.Lit(new Color(0.03f, 0.03f, 0.035f), 0.3f),
                Skin = Make.Lit(new Color(0.76f, 0.56f, 0.43f), 0.4f),
                Sock = Make.Lit(new Color(0.98f, 0.62f, 0.02f), 0.3f),
                Boot = Make.Lit(new Color(0.02f, 0.02f, 0.02f), 0.6f),
                Glove = Make.Lit(new Color(0.92f, 0.94f, 0.92f), 0.3f),
                Hair = Make.Lit(new Color(0.08f, 0.06f, 0.05f), 0.5f),
            });

            // The ball.
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, new Vector3(0, BallR, 0), Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.5f, 0f, Props.Football())).transform;
            _trail = _ball.gameObject.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = Make.Glow(new Color(1f, 1f, 1f, 0.22f), false);
            _trail.time = 0.22f;
            _trail.startWidth = BallR * 1.6f;
            _trail.endWidth = 0f;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.emitting = false;
            ResetSet();
        }

        void BuildNet()
        {
            const float depth = 60f, step = 12f, thick = 0.45f;
            Material cord = Make.Glow(new Color(1f, 1f, 1f, 0.2f), false);
            var net = new GameObject("Net").transform;
            net.SetParent(Root, false);
            void Cord(Vector3 center, Vector3 size) => Shape(PrimitiveType.Cube, "Cord", net, center, size, cord);
            float back = GoalZ + depth;
            for (float x = -HalfGoal; x <= HalfGoal + 0.1f; x += step)
            {
                Cord(new Vector3(x, BarY * 0.5f, back), new Vector3(thick, BarY, thick));
                Cord(new Vector3(x, BarY, GoalZ + depth * 0.5f), new Vector3(thick, thick, depth));
            }
            for (float y = 0f; y <= BarY + 0.1f; y += step)
            {
                Cord(new Vector3(0, y, back), new Vector3(HalfGoal * 2f, thick, thick));
                Cord(new Vector3(-HalfGoal, y, GoalZ + depth * 0.5f), new Vector3(thick, thick, depth));
                Cord(new Vector3(HalfGoal, y, GoalZ + depth * 0.5f), new Vector3(thick, thick, depth));
            }
            for (float z = GoalZ + step; z < back; z += step)
            {
                Cord(new Vector3(0, BarY, z), new Vector3(HalfGoal * 2f, thick, thick));
                Cord(new Vector3(-HalfGoal, BarY * 0.5f, z), new Vector3(thick, BarY, thick));
                Cord(new Vector3(HalfGoal, BarY * 0.5f, z), new Vector3(thick, BarY, thick));
            }
        }

        // Rows of people in the dark: only a speck of each catches the light.
        static Texture2D Crowd()
        {
            const int w = 1024, h = 256;
            var rng = new System.Random(5);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float row = y % 6 < 4 ? 1f : 0.25f;
                float high = 1f - y / (float)h * 0.6f;
                for (int x = 0; x < w; x++)
                {
                    float v = 0.008f;
                    if (rng.NextDouble() < 0.14 * row) v += (float)rng.NextDouble() * 0.11f * high;
                    px[y * w + x] = new Color(v * 0.9f, v, v * 0.95f, 1f);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _replayFrom = _replayAt = null;
            _players = players;
            _kick = 0;
            _points = new int[players];
            _history = new List<Vector2>[players];
            var names = new List<string>();
            for (int i = 0; i < players; i++) { names.Add(Arcade.PlayerName(i)); _history[i] = new List<Vector2>(); }
            Arcade.Hud.BuildBoard(names, Kicks, "GOALS", false, "KICKS");
            for (int i = 0; i < players; i++) Arcade.Hud.SetTotal(i, 0);
            Arcade.Hud.SetTurn(0, 0);
            ResetSet();
        }

        public override IEnumerator Prepare()
        {
            _replayFrom = _replayAt = null; // a game left mid-replay comes back to the normal view
            ResetSet();
            yield break;
        }

        void ResetSet()
        {
            _trail.emitting = false;
            _trail.Clear();
            PlaceBall(new Vector3(0, BallR, 0));
            PoseKeeper(new Vector2(0f, KeeperMid), 0f, 0f);
        }

        public override string Prompt
        {
            get
            {
                string who = _players > 1 ? Arcade.PlayerName(Active) + "   ·   " : "";
                return who + "Kick " + (_kick / _players + 1) + " of " + Kicks + "   ·   Putt when ready";
            }
        }

        enum Outcome { Goal, TopCorner, Saved, Wide, Over, Post, Bar }

        public override IEnumerator Play(PuttResult putt)
        {
            int player = Active, round = _kick / _players;
            bool scuffed = !putt.Launch.Reached, holed = putt.Made;
            float pace = Pace(putt);
            float rise = Mathf.Max(0f, (pace - GroundPace) * RisePerPace);
            float x, height;
            if (holed)
            {
                // On target. Where it crossed, as a share of what counts at this level (the cup on
                // Hard, 12 in on Medium, the mat on Easy), is where it goes across the goal. The
                // real line, not the one Easy and Medium draw in: that one is held to the cup, so
                // it could never reach a top corner.
                float counts = Mathf.Max(CupEdge, Arcade.Goal * 0.5f * 0.9f);
                x = Mathf.Clamp(Line(Arcade.TruePutt(putt)) / counts, -1f, 1f) * (HalfGoal - 10f);
                height = Mathf.Min(rise, 86f);
            }
            else if (scuffed)
            {
                // Never reached the hole: a tame roll into the keeper's hands.
                x = Mathf.Clamp(Line(putt) * 6f, -24f, 24f);
                height = 0f;
            }
            else
            {
                // Missed the hole, missed the goal, on the same side. A lip-out hits the post.
                float side = putt.MissSide == MissSide.Left ? -1f : putt.MissSide == MissSide.Right ? 1f : Line(putt) < 0 ? -1f : 1f;
                bool lip = putt.MissByInches <= 0.05f;
                x = side * (lip ? HalfGoal + 2.5f : Mathf.Min(340f, HalfGoal + 24f + putt.MissByInches * 20f));
                height = lip ? Mathf.Min(rise, 80f) : Mathf.Min(rise, 130f);
            }
            float curl = Mathf.Clamp(Push(putt) * 5f, -40f, 40f);
            float flight = scuffed ? 1.5f : Mathf.Lerp(0.95f, 0.55f, Mathf.InverseLerp(GroundPace, 65f, pace));
            float y = BallR + height;

            // The keeper commits before it can know.
            Vector2 dive = scuffed ? new Vector2(0f, 60f) : KeeperGuess(player);
            if (holed) _history[player].Add(new Vector2(x, y));

            Outcome outcome;
            float reach = pace < 36f ? 10f : 0f; // a slow kick gives the keeper time
            KeeperPose(dive, 0f, out Vector2 body, out Vector2 hands, out Vector2 legs);
            var at = new Vector2(x, y);
            if (scuffed) outcome = Outcome.Saved;
            else if (Mathf.Abs(x) > HalfGoal + 2.5f + 6.9f) outcome = Outcome.Wide;
            else if (y > BarY + 2.5f + 6.9f) outcome = Outcome.Over;
            else if (Mathf.Abs(Mathf.Abs(x) - (HalfGoal + 2.5f)) < 6.9f) outcome = Outcome.Post;
            else if (Mathf.Abs(y - (BarY + 2.5f)) < 6.9f) outcome = Outcome.Bar;
            else if (Vector2.Distance(at, hands) < 22f + reach || Vector2.Distance(at, body) < 22f + reach || Vector2.Distance(at, legs) < 15f + reach) outcome = Outcome.Saved;
            else outcome = Mathf.Abs(x) >= CornerX && y >= CornerY ? Outcome.TopCorner : Outcome.Goal;

            // Where the keeper ends up. For a save that is on the ball itself:
            // hands to it, or body behind it. Otherwise it is wherever it guessed.
            Vector2 goTo;
            float lean;
            _armsUp = 174f;
            if (outcome == Outcome.Saved)
            {
                float toward = x < 0 ? -1f : 1f;
                if (scuffed || (y < 34f && Mathf.Abs(x) < 28f))
                {
                    goTo = new Vector2(x * 0.9f, KeeperMid - 5f);
                    lean = 0f;
                    _armsUp = 14f;
                }
                else
                {
                    float need = Mathf.Acos(Mathf.Clamp((y - 13f) / KeeperReach, -1f, 1f)) * Mathf.Rad2Deg;
                    lean = toward * Mathf.Clamp(Mathf.Max(Mathf.Abs(x) * 0.8f, need), 0f, 86f);
                    var up = new Vector2(Mathf.Sin(lean * Mathf.Deg2Rad), Mathf.Cos(lean * Mathf.Deg2Rad));
                    goTo = at - up * KeeperReach;
                    goTo.y = Mathf.Max(goTo.y, 12f);
                }
            }
            else
            {
                goTo = body;
                lean = Mathf.Atan2(hands.x - body.x, hands.y - body.y) * Mathf.Rad2Deg;
                if (Mathf.Abs(dive.x) < 1f) _armsUp = 150f;
            }

            // The kick.
            Arcade.Audio.Kick(scuffed ? 0.1f : Mathf.InverseLerp(20f, 65f, pace));
            _trail.Clear();
            _trail.emitting = true;
            float lift = scuffed ? 0f : 8f + height * 0.12f;
            Vector3 PathAt(float u) => new Vector3(x * u - curl * Mathf.Sin(Mathf.PI * u), BallR + height * u + lift * 4f * u * (1f - u), GoalZ * u);
            // A save meets the keeper where the keeper is, in front of the line, not after
            // passing through: the ball stops a ball's width short of his hands and body.
            float stopU = outcome == Outcome.Saved ? Mathf.Clamp01((GoalZ - 8f - BallR - 5f) / GoalZ) : 1f;
            bool slowed = false, shot = false;
            for (float t = 0; t < flight * stopU; t += Time.deltaTime)
            {
                float u = t / flight;
                PlaceBall(PathAt(u));
                _ball.Rotate(Time.deltaTime * 900f, 0, 0, Space.Self);
                PoseKeeper(goTo, lean, Ease(Mathf.InverseLerp(0.2f, 0.95f * stopU, u)));
                if (!slowed && u > 0.62f && !scuffed) { slowed = true; Arcade.SlowMotion(0.5f); }
                if (!shot && u > 0.85f * stopU) { shot = true; Arcade.Shot("action"); }
                yield return null;
            }
            PoseKeeper(goTo, lean, 1f);
            Vector3 end = PathAt(stopU);
            Vector3 velocity = (PathAt(stopU) - PathAt(stopU - 0.03f)) / (0.03f * flight);

            Hud hud = Arcade.Hud;
            string result;
            Color color;
            int points = 0;
            switch (outcome)
            {
                case Outcome.TopCorner:
                    points = 2; result = "Top corner"; color = Make.Bright;
                    hud.Banner("TOP BINS", "Counts double", Make.Bright, 2.2f);
                    Arcade.Audio.Net();
                    Arcade.Audio.Strike();
                    Arcade.Shake(0.5f);
                    Arcade.Celebrate(110, At(0, 30f, GoalZ - 40f));
                    break;
                case Outcome.Goal:
                    points = 1; result = "Goal"; color = Make.Bright;
                    hud.Banner("GOAL", Mathf.Abs(dive.x) > 20f && Mathf.Sign(dive.x) != Mathf.Sign(x) ? "Sent the keeper the wrong way" : "Past the keeper", Color.white, 2f);
                    Arcade.Audio.Net();
                    Arcade.Audio.Spare();
                    Arcade.Celebrate(50, At(0, 30f, GoalZ - 40f));
                    break;
                case Outcome.Saved:
                    result = "Saved"; color = Make.Amber;
                    hud.Banner("SAVED", scuffed ? "Short of the hole: it rolled to the keeper" : _history[player].Count > 1 ? "The keeper has seen that one before" : "The keeper guessed right", Make.Amber, 2f);
                    Arcade.Audio.Thump(50f);
                    Arcade.Audio.Groan();
                    break;
                case Outcome.Post:
                    result = "Off the post"; color = Make.Red;
                    hud.Banner("OFF THE POST", holed ? "An inch from perfect" : "Lipped out", Make.Red, 2f);
                    Arcade.Audio.Post();
                    Arcade.Audio.Groan();
                    Arcade.Shake(0.4f);
                    break;
                case Outcome.Bar:
                    result = "Off the bar"; color = Make.Red;
                    hud.Banner("OFF THE BAR", "A touch too firm", Make.Red, 2f);
                    Arcade.Audio.Post();
                    Arcade.Audio.Groan();
                    Arcade.Shake(0.4f);
                    break;
                case Outcome.Over:
                    result = "Over the bar"; color = Make.Red;
                    hud.Banner("OVER", "Too much pace", Make.Red, 2f);
                    Arcade.Audio.Miss();
                    break;
                default:
                    result = "Wide"; color = Make.Red;
                    hud.Banner("WIDE", x < 0 ? "Missed the hole left, so the kick went left" : "Missed the hole right, so the kick went right", Make.Red, 2f);
                    Arcade.Audio.Miss();
                    break;
            }
            ShowPutt(putt, result, color);
            _points[player] += points;
            hud.SetPip(player, round, points == 0 ? Hud.Pip.Bad : Hud.Pip.Good, points == 2 ? "2" : "");
            hud.SetTotal(player, _points[player]);

            // What the ball does next.
            bool inNet = outcome == Outcome.Goal || outcome == Outcome.TopCorner;
            bool back = outcome == Outcome.Saved || outcome == Outcome.Post || outcome == Outcome.Bar;
            if (back) velocity = new Vector3(velocity.x * -0.3f + (float)(_rng.NextDouble() - 0.5) * 120f, 90f, -velocity.z * 0.3f);
            Vector3 p = end;
            bool shotResult = false;
            // The keeper does not hang in the air: a dive carries on down to the grass, lying
            // along the dive; a keeper who stayed up just settles.
            bool diving = Mathf.Abs(lean) > 20f;
            float bodyY = goTo.y, fallV = 0f, floorY = diving ? 11f : Mathf.Min(goTo.y, KeeperMid);
            float flatLean = diving ? Mathf.Sign(lean) * 86f : lean, nowLean = lean;
            for (float t = 0; t < 1.3f; t += Time.deltaTime)
            {
                velocity.y -= LaneSpec.Gravity * Time.deltaTime;
                p += velocity * Time.deltaTime;
                if (inNet && p.z > GoalZ + 54f) { p.z = GoalZ + 54f; velocity = new Vector3(velocity.x * 0.1f, velocity.y * 0.3f, 0f); }
                if (inNet) p.x = Mathf.Clamp(p.x, -HalfGoal + BallR, HalfGoal - BallR);
                if (p.y < BallR) { p.y = BallR; velocity.y *= -0.45f; velocity.x *= 0.8f; velocity.z *= 0.8f; }
                PlaceBall(p);
                if (bodyY > floorY)
                {
                    fallV += LaneSpec.Gravity * 0.6f * Time.deltaTime; // a body falls a little softer than a ball
                    bodyY = Mathf.Max(floorY, bodyY - fallV * Time.deltaTime);
                }
                nowLean = Mathf.MoveTowards(nowLean, flatLean, 160f * Time.deltaTime);
                PoseKeeper(new Vector2(goTo.x + (diving ? Mathf.Sign(lean) * 6f * Mathf.Clamp01(t / 0.5f) : 0f), bodyY), nowLean, 1f);
                if (!shotResult && t > 0.3f) { shotResult = true; Arcade.Shot("result"); }
                yield return null;
            }
            if (outcome == Outcome.TopCorner) yield return Replay(PathAt, flight, goTo, lean, x, y);
            _trail.emitting = false;
            yield return new WaitForSecondsRealtime(0.7f);

            _kick++;
            hud.SetTurn(Over ? -1 : Active, Over ? -1 : _kick / _players);
        }

        // Top bins, again, from close by: the camera drops in beside the corner and the last
        // part of the kick plays once more, slowly, keeper's dive and all.
        IEnumerator Replay(System.Func<float, Vector3> path, float flight, Vector2 goTo, float lean, float x, float y)
        {
            float side = Mathf.Sign(x);
            // A long lens from just behind the spot, down the line of the kick: the ball flies away
            // from it, and the keeper's dive and the corner both fill the frame.
            _replayFrom = new Vector3(x * 0.15f, 44f, -70f);
            _replayAt = new Vector3(x * 0.55f, y * 0.72f, GoalZ);
            Arcade.Hud.Banner("REPLAY", "Top bins", Make.Bright, 3.2f);
            yield return new WaitForSecondsRealtime(0.55f); // let the camera arrive
            _trail.Clear();
            const float from = 0.3f, slow = 2.8f;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / slow)
            {
                float u = Mathf.Lerp(from, 1f, t);
                PlaceBall(path(u));
                _ball.Rotate(Time.unscaledDeltaTime * 260f, 0, 0, Space.Self);
                PoseKeeper(goTo, lean, Ease(Mathf.InverseLerp(0.2f, 0.95f, u)));
                yield return null;
            }
            PlaceBall(path(1f) + new Vector3(0f, 0f, 20f)); // into the net
            yield return new WaitForSecondsRealtime(0.9f);
            _replayFrom = _replayAt = null;
            yield return new WaitForSecondsRealtime(0.4f);
        }

        public override bool Over => _kick >= Kicks * _players;

        public override void Final(out string title, out string body)
        {
            body = "";
            if (_players == 1)
            {
                title = _points[0] + "  P O I N T S";
                body = "Five kicks. A top corner is worth two.\n";
                return;
            }
            title = "F U L L   T I M E";
            int best = 0, ties = 0;
            for (int i = 0; i < _players; i++) if (_points[i] > _points[best]) best = i;
            for (int i = 0; i < _players; i++) if (_points[i] == _points[best]) ties++;
            for (int i = 0; i < _players; i++)
                body += Arcade.PlayerName(i) + "     " + _points[i] + (ties == 1 && i == best ? "     winner" : "") + "\n";
            if (ties > 1) body += "A draw\n";
        }

        // ── The keeper ──────────────────────────────────────────────────────

        // Where the keeper throws its hands: across (inches from the middle) and up.
        Vector2 KeeperGuess(int player)
        {
            List<Vector2> seen = _history[player];
            bool bluff = _rng.NextDouble() < 0.25;
            if (seen.Count == 0 || bluff)
            {
                int side = _rng.Next(3) - 1;
                return new Vector2(side * 84f, _rng.NextDouble() < 0.5 ? 24f : 62f);
            }
            Vector2 mean = seen[seen.Count - 1];
            if (seen.Count > 1) mean = (mean + seen[seen.Count - 2]) * 0.5f;
            if (Mathf.Abs(mean.x) < 30f) return new Vector2(0f, 60f);
            return new Vector2(Mathf.Sign(mean.x) * Mathf.Clamp(Mathf.Abs(mean.x), 50f, 84f), mean.y > 48f ? 62f : 24f);
        }

        // The keeper's body for a dive: standing is 1, full stretch is 0.
        void KeeperPose(Vector2 dive, float standing, out Vector2 body, out Vector2 hands, out Vector2 legs)
        {
            float k = 1f - standing;
            float far = Mathf.Abs(dive.x) / 84f;
            float angle = Mathf.Sign(dive.x) * Mathf.Min(72f, Mathf.Abs(dive.x) * 0.86f) * k;
            float midY = Mathf.Lerp(KeeperMid, Mathf.Lerp(KeeperMid, Mathf.Max(16f, dive.y - 14f), far), k);
            body = new Vector2(dive.x * 0.66f * k, midY);
            var up = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad));
            hands = body + up * KeeperReach;
            legs = body - up * 30f;
        }

        float _armsUp = 174f;

        // Move the keeper k of the way (0 to 1) from standing on its line to
        // this body position and lean. Lean is degrees from upright, positive to +X.
        void PoseKeeper(Vector2 body, float lean, float k)
        {
            Vector2 at = Vector2.Lerp(new Vector2(0f, KeeperMid), body, k);
            float angle = lean * k;
            _keeper.Root.localPosition = new Vector3(at.x, at.y, GoalZ - 8f);
            _keeper.Root.localRotation = Quaternion.Euler(0, 0, -angle);
            // Waiting: arms out and low, hands ready. Diving: both arms at full stretch.
            float arm = Mathf.Lerp(36f, _armsUp, k);
            float stretch = Mathf.Clamp01(Mathf.Abs(lean) / 40f) * k;
            int lead = lean < 0 ? 0 : 1;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                _keeper.Arm[i].localRotation = Quaternion.Euler(0, 0, side * arm);
                _keeper.Fore[i].localRotation = Quaternion.Euler(28f * (1f - k), 0, side * 42f * (1f - k));
                bool trailing = i != lead;
                _keeper.Leg[i].localRotation = Quaternion.Euler(trailing ? 24f * stretch : 0f, 0, side * Mathf.Lerp(9f, trailing ? 4f : 14f, stretch));
                _keeper.Shin[i].localRotation = Quaternion.Euler(trailing ? -46f * stretch : 0f, 0, 0);
            }
        }

        void PlaceBall(Vector3 p) => _ball.localPosition = p;
    }
}
