// Balloon Pop (puttr-app BalloonPop.js, rebuilt in 3D): a stage game, like Turf War. A square board stands for the
// mat around the cup (no mat, no cup), with balloons floating over it in rings. The putt's ball rolls across the
// board on the path the camera saw and pops every balloon it rolls under. Made or missed doesn't matter, only the path.
//
//   gold   1 over the middle (where the cup is)   100
//   red    6 in the first ring                     75
//   blue  10 in the second                         50
//   white 14 in the third                          25
//
// Ten putts each, 1 to 4 Players, a new random spot every putt. Popped balloons come back before the next putt.
// The board is laid out in console inches (x right, d past the cup), Scale units to the inch.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class BalloonPopMode : StageMode
    {
        public override string Name => "Balloon Pop";
        public override string Blurb => "Balloons over the green.\nYour ball pops every one\nit rolls through.";
        public override string Rule => "Ten putts. Pop as many balloons as you can: gold is 100, red 75, blue 50, white 25.";
        public override string Icon => "Heart";
        public override Color Accent => new Color(0.95f, 0.36f, 0.42f);
        public override string[] Tags => new[] { "10 putts", "Line" };
        public override string[] HowTo => new[]
        {
            "Balloons float in rings over the board. The middle is where the cup is.",
            "Your putt rolls across the board and pops every balloon it passes. Made or missed, only the path counts.",
            "Gold in the middle is 100, then red 75, blue 50 and white 25.",
        };
        public override int Putter => Active;
        public override bool HasLevels => false;
        public override bool DealsTee => false;
        public override bool ShowsAim => false;
        public override bool HasPortrait => true;
        public override string Tee => null; // the tee sign says it
        public override GameAudio.Scene Sound => GameAudio.Scene.Table;
        public override int Score(int player) => _total[player];

        const float Scale = 10f;                    // units to the inch
        const float HalfW = 13f, Near = -15f, Far = 15f; // the board, in inches
        const int Putts = 10;
        const float HitR = 2.1f, FloatH = 3.2f;

        public override Vector3 CameraPosition => Portrait ? At(0f, 560f, -340f) : At(0f, 330f, -330f);
        public override Vector3 CameraLook => Portrait ? At(0f, 0f, -20f) : At(0f, 0f, 0f);
        public override float CameraFov => Portrait ? 44f : 40f;
        public override float ShadowDistance => 900f;

        sealed class Balloon
        {
            public Transform Body;
            public Vector2 At;        // inches: x right, y past the cup
            public int Value;
            public bool Popped;
            public float Phase;
            public Vector2 Sway, Push;  // inches off its spot, and how fast it is going (a pop nearby shoves it)
        }

        readonly List<Balloon> _balloons = new List<Balloon>();
        readonly List<Balloon> _thisPutt = new List<Balloon>();
        readonly System.Random _dice = new System.Random();
        Transform _ball;
        LineRenderer _trail;
        int _players = 1, _thrown;
        int[] _total = new int[1];
        int[,] _scores = new int[1, Putts];

        int Active => _thrown % _players;
        int Turn => _thrown / _players;

        static Vector3 W(Vector2 inches, float up = 0f) => new Vector3(inches.x * Scale, up, inches.y * Scale);

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            Material studio = Make.Lit(new Color(0.035f, 0.04f, 0.05f), 0.3f);
            Material slab = Make.Lit(new Color(0.11f, 0.32f, 0.2f), 0.35f);
            Material rim = Make.Lit(new Color(0.03f, 0.03f, 0.035f), 0.6f, 0.3f);
            Shape(PrimitiveType.Cube, "Studio floor", Root, new Vector3(0, -12f, 0), new Vector3(2400f, 2f, 2400f), studio);
            Shape(PrimitiveType.Cube, "Rim", Root, new Vector3(0, -5f, (Near + Far) * 0.5f * Scale), new Vector3(HalfW * 2 * Scale + 16f, 10f, (Far - Near) * Scale + 16f), rim);
            Shape(PrimitiveType.Cube, "Board", Root, new Vector3(0, -2f, (Near + Far) * 0.5f * Scale), new Vector3(HalfW * 2 * Scale, 4.2f, (Far - Near) * Scale), slab);
            // The rings, painted faintly on the board, and a glow round its edge.
            foreach (float r in new[] { 4.2f, 7.4f, 10.4f })
            {
                LineRenderer ring = Stroke(Root, "Ring", new Color(1f, 1f, 1f, 0.12f), 0.6f);
                ring.positionCount = 97;
                for (int i = 0; i <= 96; i++) { float a = i / 96f * Mathf.PI * 2f; ring.SetPosition(i, W(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r), 0.3f)); }
            }
            LineRenderer edge = Stroke(Root, "Edge", Make.Green * 1.6f, 1.6f);
            edge.positionCount = 5;
            edge.SetPosition(0, W(new Vector2(-HalfW, Near), 0.4f)); edge.SetPosition(1, W(new Vector2(HalfW, Near), 0.4f));
            edge.SetPosition(2, W(new Vector2(HalfW, Far), 0.4f)); edge.SetPosition(3, W(new Vector2(-HalfW, Far), 0.4f)); edge.SetPosition(4, W(new Vector2(-HalfW, Near), 0.4f));
            Words(Root, "PUTT THIS WAY", new Vector3(0, 0.5f, Near * Scale - 18f), 7f, new Color(1f, 1f, 1f, 0.5f)).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            Ring(0f, 1, 100, new Color(1f, 0.78f, 0.18f), 0f);
            Ring(4.2f, 6, 75, new Color(0.93f, 0.2f, 0.25f), -Mathf.PI / 2f);
            Ring(7.4f, 10, 50, new Color(0.24f, 0.5f, 0.98f), -Mathf.PI / 6f);
            Ring(10.4f, 14, 25, new Color(0.95f, 0.95f, 0.97f), 0f);

            // The cup, under the gold balloon: a holed putt drops in.
            Shape(PrimitiveType.Cylinder, "Cup", Root, new Vector3(0f, 0.12f, 0f), new Vector3(LaneSpec.HoleRadius * 2f * Scale, 0.05f, LaneSpec.HoleRadius * 2f * Scale), Make.Lit(new Color(0.01f, 0.015f, 0.012f), 0.2f));
            // A real ball's skin (its markings show it turning as it rolls).
            Make.Ball(out Texture2D ballColor, out Texture2D ballDimples);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * 1.68f * Scale, Make.Lit(Color.white, 0.8f, 0f, ballColor, ballDimples, 0.45f)).transform;
            _ball.gameObject.SetActive(false);
            _trail = Stroke(Root, "Trail", new Color(1f, 1f, 1f, 0.55f), 3f);

            Sun(Root, new Vector3(58f, 25f, 0f), new Color(1f, 0.97f, 0.92f), 1.6f);
            Light fill = World.NewLight(Root, "Fill", LightType.Point, new Color(1f, 0.88f, 0.95f), 90000f);
            fill.transform.localPosition = new Vector3(0, 260f, -120f);
            fill.range = 900f;
        }

        void Ring(float radius, int count, int value, Color colour, float start)
        {
            Material skin = Make.Lit(colour, 0.92f, 0.05f);
            Material knot = Make.Lit(colour * 0.8f, 0.6f);
            Material line = Make.Lit(new Color(0.85f, 0.85f, 0.85f), 0.3f);
            Material shadow = Make.Glow(new Color(0f, 0f, 0f, 0.35f), false);
            for (int i = 0; i < count; i++)
            {
                float a = start + i / (float)count * Mathf.PI * 2f;
                var b = new Balloon { At = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius), Value = value, Phase = (float)_dice.NextDouble() * 6f };
                b.Body = new GameObject("Balloon " + value).transform;
                b.Body.SetParent(Root, false);
                float size = (value == 100 ? 2.1f : value == 75 ? 1.8f : 1.6f) * Scale;
                Shape(PrimitiveType.Sphere, "Skin", b.Body, Vector3.zero, new Vector3(size, size * 1.18f, size), skin);
                Shape(PrimitiveType.Sphere, "Knot", b.Body, new Vector3(0, -size * 0.62f, 0), Vector3.one * size * 0.18f, knot);
                Shape(PrimitiveType.Cylinder, "String", b.Body, new Vector3(0, -size * 0.62f - FloatH * Scale * 0.5f, 0), new Vector3(0.5f, FloatH * Scale * 0.5f, 0.5f), line);
                Shape(PrimitiveType.Cylinder, "Shadow", b.Body, new Vector3(0, -size * 0.62f - FloatH * Scale + 0.2f, 0), new Vector3(size * 0.8f, 0.05f, size * 0.8f), shadow);
                _balloons.Add(b);
            }
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = Mathf.Max(1, players);
            _thrown = 0;
            _total = new int[_players];
            _scores = new int[_players, Putts];
            foreach (Balloon b in _balloons) { b.Popped = false; b.Body.gameObject.SetActive(true); b.Body.localScale = Vector3.one; }
            _ball.gameObject.SetActive(false);
            _trail.positionCount = 0;
            if (Arcade != null && Arcade.Hud != null)
            {
                var names = new List<string>();
                for (int i = 0; i < _players; i++) names.Add(Arcade.PlayerName(i));
                Arcade.Hud.BuildBoard(names, Putts, "TOTAL", false, "PUTTS", 38f);
                ShowScores();
                Deal();
                Arcade.Audio.GameBed("carnival_loop", 0.22f);
            }
            Tick();
        }

        void Deal()
        {
            DealtFeetNow = _dice.Next(3, 12);
            DealtTee = _dice.Next(1, 4);
            ShowTeeSign();
        }

        public override IEnumerator Prepare()
        {
            _thisPutt.Clear();
            foreach (Balloon b in _balloons)
                if (b.Popped) { b.Popped = false; b.Body.gameObject.SetActive(true); b.Body.localScale = Vector3.one * 0.15f; }
            yield break;
        }

        public override string Prompt
        {
            get
            {
                string who = _players > 1 ? Arcade.PlayerName(Active) + "   ·   " : "";
                return who + "Putt " + (Turn + 1) + " of " + Putts + "   ·   Putt when ready";
            }
        }

        public override void Tick()
        {
            float t = Time.time;
            foreach (Balloon b in _balloons)
            {
                if (b.Popped) continue;
                float bob = Mathf.Sin(t * 1.7f + b.Phase) * 0.25f;
                // On its string: a shove swings it out, and it settles back.
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                b.Push += (-b.Sway * 28f - b.Push * 2.6f) * dt;
                b.Sway += b.Push * dt;
                b.Body.localPosition = W(b.At + b.Sway, (FloatH + 1.2f + bob) * Scale);
                b.Body.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.1f + b.Phase) * 5f, 0f, Mathf.Cos(t * 0.9f + b.Phase) * 5f);
                if (b.Body.localScale.x < 1f) b.Body.localScale = Vector3.one * Mathf.Min(1f, b.Body.localScale.x + Time.deltaTime * 2.5f);
            }
        }

        // The putt's path on the board: inches right and past the cup, inside the board, one point per
        // camera frame with the time the camera saw it (seconds from the first frame on the board).
        static List<Vector2> PathOf(PuttResult putt, List<float> times = null)
        {
            var pts = new List<Vector2>();
            if (putt.Path == null) return pts;
            // One point per camera frame: a frame read twice is the same frame, and the frames are the only clock.
            List<BallPoint> frames = PuttrAnalysis.DistinctFrames(putt.Path);
            float dt = 1f / PuttrAnalysis.DefaultFps, clock = 0f;
            int lastIn = -1;
            for (int i = 0; i < frames.Count; i++)
            {
                Vector2 at = PuttrAnalysis.ToInchesIntegrated(frames[i], putt.Hole);
                var v = new Vector2(at.x, -at.y);
                if (Mathf.Abs(v.x) <= HalfW && v.y >= Near && v.y <= Far)
                {
                    // A gap (the ball out of sight for a few frames) is crossed at no more than three frames' time.
                    if (lastIn >= 0) clock += dt * Mathf.Min(3, i - lastIn);
                    lastIn = i;
                    pts.Add(v);
                    times?.Add(clock);
                }
            }
            return pts;
        }

        // Where a Catmull-Rom curve through the frames is at time t (seconds), and the index of the segment.
        static Vector2 PathAt(List<Vector2> p, List<float> times, float t, ref int seg)
        {
            while (seg < p.Count - 2 && t > times[seg + 1]) seg++;
            float span = Mathf.Max(1e-4f, times[seg + 1] - times[seg]);
            float u = Mathf.Clamp01((t - times[seg]) / span);
            Vector2 p0 = p[Mathf.Max(0, seg - 1)], p1 = p[seg], p2 = p[seg + 1], p3 = p[Mathf.Min(p.Count - 1, seg + 2)];
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
        }

        // The nearest a straight piece of the path comes to a balloon, and how far along it that is (0 to 1).
        static float Nearest(Vector2 a, Vector2 b, Vector2 c, out float along)
        {
            Vector2 ab = b - a;
            along = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(c - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(a + ab * along, c);
        }

        const float BallR = 0.84f;           // inches
        const float Slow = 64f;              // in/s^2: how hard the ramp slowed it, the camera's own measure
        Quaternion _spin = Quaternion.identity;

        // Roll the ball by what it moved: turned about the axis across its path, one radian per radius rolled.
        void RollTo(Vector2 from, Vector2 to)
        {
            Vector2 step = to - from;
            float dist = step.magnitude;
            if (dist > 1e-5f)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(step.x, 0f, step.y) / dist);
                _spin = Quaternion.AngleAxis(dist / BallR * Mathf.Rad2Deg, axis) * _spin;
            }
            _ball.localPosition = W(to, BallR * Scale);
            _ball.localRotation = _spin;
        }

        public override IEnumerator Play(PuttResult putt)
        {
            var times = new List<float>();
            List<Vector2> path = PathOf(putt, times);
            _trail.positionCount = 0;
            if (path.Count >= 2)
            {
                // Which balloons the ball rolls under, and when: every one its path passes within reach of,
                // in the order it gets there. (Measured on the path itself, not frame by frame.)
                var pops = new List<KeyValuePair<float, Balloon>>();
                foreach (Balloon b in _balloons)
                {
                    if (b.Popped) continue;
                    for (int i = 1; i < path.Count; i++)
                    {
                        float near = Nearest(path[i - 1], path[i], b.At, out float along);
                        if (near > HitR) continue;
                        // The moment it first comes within reach on this piece.
                        Vector2 ab = path[i] - path[i - 1];
                        float len = ab.magnitude, back = Mathf.Sqrt(Mathf.Max(0f, HitR * HitR - near * near));
                        float enter = len > 1e-4f ? Mathf.Clamp01(along - back / len) : 0f;
                        pops.Add(new KeyValuePair<float, Balloon>(Mathf.Lerp(times[i - 1], times[i], enter), b));
                        break;
                    }
                }
                pops.Sort((x, y) => x.Key.CompareTo(y.Key));

                // The ball as the camera saw it, frame by frame at the camera's own pace: it slows as it really
                // slowed, and turns as far as it rolls.
                _ball.gameObject.SetActive(true);
                _spin = Quaternion.Euler(Random.value * 360f, Random.value * 360f, 0f);
                Vector2 now = path[0], was = now;
                RollTo(now, now);
                var trail = new List<Vector3> { W(now, 0.5f) };
                int seg = 0, popped = 0;
                float end = times[times.Count - 1];
                for (float t = 0f; ; t += Time.deltaTime)
                {
                    float at = Mathf.Min(t, end);
                    was = now;
                    now = PathAt(path, times, at, ref seg);
                    RollTo(was, now);
                    if ((now - was).sqrMagnitude > 0.0025f) { trail.Add(W(now, 0.5f)); _trail.positionCount = trail.Count; _trail.SetPositions(trail.ToArray()); }
                    while (popped < pops.Count && pops[popped].Key <= at) Pop(pops[popped++].Value, now);
                    if (t >= end) break;
                    yield return null;
                }
                while (popped < pops.Count) Pop(pops[popped++].Value, now);

                int n = path.Count;
                Vector2 velocity = n >= 2 ? (path[n - 1] - path[Mathf.Max(0, n - 3)]) / Mathf.Max(1e-3f, end - times[Mathf.Max(0, n - 3)]) : Vector2.zero;
                if (putt.Made)
                {
                    // Into the cup: over its lip and down, with gravity.
                    yield return Sink(now, velocity);
                    Arcade.Audio.Coin();
                }
                else yield return Coast(now, velocity);
            }
            int value = 0;
            foreach (Balloon b in _thisPutt) value += b.Value;
            Hud hud = Arcade.Hud;
            if (value == 0) { hud.Banner("NO POP", path.Count < 2 ? "It never reached the balloons" : "Roll it through the balloons", Make.Amber, 1.6f); Arcade.Audio.Library("rule_miss"); }
            else
            {
                hud.Banner("+" + value, _thisPutt.Count == 1 ? "1 balloon" : _thisPutt.Count + " balloons", value >= 175 ? Make.Bright : Color.white, 1.8f);
                Arcade.Audio.Library(value >= 175 ? "rule_bonus" : "rule_make");
            }
            ShowPutt(putt, value > 0 ? "+" + value : "No pop", value > 0 ? Make.Bright : Make.Amber);
            _scores[Active, Turn] = value;
            _total[Active] += value;
            _thrown++;
            ShowScores();
            if (!Over) Deal();
            yield return new WaitForSecondsRealtime(1.4f);
            _ball.gameObject.SetActive(false);
        }

        // A holed putt: it runs on to the cup, tips over the lip and drops out of sight.
        IEnumerator Sink(Vector2 from, Vector2 velocity)
        {
            float speed = Mathf.Max(velocity.magnitude, 8f);
            Vector2 dir = from.sqrMagnitude > 0.01f ? -from.normalized : velocity.sqrMagnitude > 0.01f ? velocity.normalized : Vector2.up;
            Vector2 now = from;
            // To the middle of the cup, if the camera lost it just short.
            while (now.magnitude > 0.3f && Vector2.Dot(-now, dir) > 0f)
            {
                Vector2 next = now + dir * Mathf.Min(speed * Time.deltaTime, now.magnitude);
                RollTo(now, next);
                now = next;
                yield return null;
            }
            // Down, with gravity, still turning.
            float y = BallR, fall = 0f;
            while (y > -BallR * 3f)
            {
                fall += LaneSpec.Gravity * Time.deltaTime;
                y -= fall * Time.deltaTime;
                _spin = Quaternion.AngleAxis(speed / BallR * Mathf.Rad2Deg * Time.deltaTime, Vector3.Cross(Vector3.up, new Vector3(dir.x, 0f, dir.y))) * _spin;
                _ball.localPosition = W(now, y * Scale);
                _ball.localRotation = _spin;
                yield return null;
            }
            _ball.gameObject.SetActive(false);
        }

        // A missed putt still moving where the camera last saw it: it rolls on and slows the way the ramp
        // slowed it, on its last heading, to a stop. It pops nothing more (the camera did not see that part),
        // so it stops short of a balloon still standing in its way, and inside the board.
        IEnumerator Coast(Vector2 from, Vector2 velocity)
        {
            float speed = velocity.magnitude;
            if (speed < 2f) yield break;
            Vector2 dir = velocity / speed;
            float reach = speed * speed / (2f * Slow);
            foreach (Balloon b in _balloons)
            {
                if (b.Popped) continue;
                float ahead = Vector2.Dot(b.At - from, dir);
                if (ahead <= 0f) continue;
                float off = Vector2.Distance(from + dir * ahead, b.At);
                if (off > HitR) continue;
                reach = Mathf.Min(reach, Mathf.Max(0f, ahead - Mathf.Sqrt(HitR * HitR - off * off) - 0.1f));
            }
            // The board's edge.
            if (dir.x > 0.001f) reach = Mathf.Min(reach, (HalfW - BallR - from.x) / dir.x);
            if (dir.x < -0.001f) reach = Mathf.Min(reach, (-HalfW + BallR - from.x) / dir.x);
            if (dir.y > 0.001f) reach = Mathf.Min(reach, (Far - BallR - from.y) / dir.y);
            if (dir.y < -0.001f) reach = Mathf.Min(reach, (Near + BallR - from.y) / dir.y);
            if (reach < 0.05f) yield break;
            float slow = speed * speed / (2f * reach); // the measured pace, brought to rest in the room it has
            Vector2 now = from;
            float gone = 0f;
            while (speed > 0.2f && gone < reach)
            {
                speed = Mathf.Max(0f, speed - slow * Time.deltaTime);
                float step = Mathf.Min(reach - gone, speed * Time.deltaTime);
                gone += step;
                Vector2 next = from + dir * gone;
                RollTo(now, next);
                now = next;
                yield return null;
            }
        }

        // A balloon bursts as the ball reaches it; the air from it pushes its neighbours away.
        void Pop(Balloon b, Vector2 ballAt)
        {
            foreach (Balloon o in _balloons)
            {
                if (o == b || o.Popped) continue;
                Vector2 away = o.At - b.At;
                float d = away.magnitude;
                if (d < 7f && d > 0.01f) o.Push += away / d * (7f - d) * 1.6f;
            }
            Pop(b);
        }

        void Pop(Balloon b)
        {
            b.Popped = true;
            _thisPutt.Add(b);
            Vector3 at = Root.TransformPoint(b.Body.localPosition);
            b.Body.gameObject.SetActive(false);
            Arcade.Sparks(at, b.Value >= 75 ? 18 : 10);
            Arcade.Audio.Library("balloon_pop", 0.9f, 0.9f + Random.value * 0.25f);
            if (b.Value == 100) Arcade.Celebrate(80, at);
        }

        public override bool Over => _thrown >= _players * Putts;
        public override void End() { if (Arcade != null) Arcade.Audio.GameBed(null); }

        public override void Final(out string title, out string body)
        {
            title = _players == 1 ? _total[0] + "  P O I N T S" : "G A M E   O V E R";
            body = "";
            int best = 0;
            for (int i = 0; i < _players; i++) if (_total[i] > _total[best]) best = i;
            if (_players > 1)
                for (int i = 0; i < _players; i++)
                    body += Arcade.PlayerName(i) + "     " + _total[i] + (i == best ? "     winner" : "") + "\n";
        }

        void ShowScores()
        {
            Hud hud = Arcade.Hud;
            for (int i = 0; i < _players; i++)
            {
                int done = _thrown / _players + (i < _thrown % _players ? 1 : 0);
                for (int t = 0; t < Putts; t++) hud.SetPip(i, t, t >= done ? Hud.Pip.Empty : _scores[i, t] > 0 ? Hud.Pip.Good : Hud.Pip.Over, t < done && _scores[i, t] > 0 ? _scores[i, t].ToString() : "");
                hud.SetTotal(i, _total[i]);
            }
            hud.SetTurn(Over ? -1 : Active, Over ? -1 : Turn);
        }
    }
}
