// Under the Cup: a pin table.
//
// Behind the console a table slopes away at the ramp's own 12 degrees, set
// with brass pins, with seven pockets at the bottom. A holed ball rolls out of
// the cup onto it at the speed and on the line it reached the hole with.
//
// There is a clear channel down the middle a ball and a bit wide, ending in a
// chute: a dead straight center cut putt runs untouched into it, and that is
// the only way to score the 100. Pins steer a ball that clips them back toward
// the middle (Puttr > Print Pin Table Odds: up to three in ten off-line balls
// end up there), so a ball that touched anything on the way scores 60.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.UI;

namespace PuttrArcade
{
    public sealed class UnderTheCupMode : GameMode
    {
        public override string Name => "Under the Cup";
        public override bool Branded => true;
        // puttr-app-2: a pin table's hum (the room tone until it is recorded); it ends on the jackpot bells.
        public override string BedSlot => "underthecup_machine_loop";
        public override string EndSlot => "underthecup_jackpot";
        public override string VoicePrefix => "underthecup";
        public override int Putter => Active; // whose difficulty applies to this putt
        public override string Blurb => "A pin table under the hole.\nRun the middle untouched\nfor the hundred.";
        public override string Icon => "Joystick";
        public override Color Accent => new Color(0.18f, 0.55f, 0.24f);
        public override string[] Tags => new[] { "10 putts", "Line and pace" };
        public override string[] HowTo => new[]
        {
            "Hole the putt and the ball drops onto a pin table under the cup.",
            "Pockets along the bottom score 10 to 100. The 100 is in the middle.",
            "A ball that reaches the middle without touching a pin scores the 100. If it touched one on the way it scores 60.",
        };
        public override int Score(int player) => _total[player];
        public override string Rule => "Ten putts. The hundred is for a ball that reaches the middle without touching anything.";

        const int Putts = 10;
        const float Length = 25f, HalfWidth = 7f, PocketZ = 20.5f;
        static readonly int[] Values = { 10, 30, 60, 100, 60, 30, 10 };

        static float Top => LaneSpec.LaneHeadY;

        public override Vector3 CameraPosition => new Vector3(0f, Top + 23f, LaneSpec.LaneHeadZ + 3.5f);
        public override Vector3 CameraLook => new Vector3(0f, Top - 2.5f, LaneSpec.LaneHeadZ + 13.5f);
        public override float CameraFov => 58f;

        /// <summary>
        /// Where the pins stand: for each row, how far each side of the center
        /// line. No pin leaves a gap to the rail that a ball can wedge in.
        /// Puttr > Print Pin Table Odds shows what a change here does.
        /// </summary>
        public static float[][] Layout =
        {
            new[] { 1.3f, 3.9f, 6.3f }, new[] { 2.6f, 5.0f }, new[] { 1.3f, 3.9f, 6.3f },
            new[] { 2.6f, 5.0f }, new[] { 1.3f, 3.9f, 6.3f },
        };
        /// <summary>The 100 pocket is a chute this wide that starts well above the others, so a ball has to arrive straight to get in.</summary>
        public static float ChuteWidth = 2.3f;
        public static float ChuteStartZ = 17f;

        // Pocket edges across the table, left to right.
        static float[] Edges()
        {
            float side = (HalfWidth * 2 - ChuteWidth) / (Values.Length - 1);
            var edges = new float[Values.Length + 1];
            for (int i = 0; i <= 3; i++) { edges[i] = -HalfWidth + i * side; edges[Values.Length - i] = HalfWidth - i * side; }
            return edges;
        }

        public Transform Table => _table;
        Transform _table;
        int _players, _thrown;
        int[] _total;
        int[,] _scores;
        float _lastTing;
        int _tings;
        bool _touched, _nudged;

        int Active => _thrown % _players;
        int Turn => _thrown / _players;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // Built flat, then tipped 12 degrees down about its head.
            _table = new GameObject("Table").transform;
            _table.SetParent(Root, false);
            _table.localPosition = new Vector3(0, Top, LaneSpec.LaneHeadZ);
            _table.localRotation = Quaternion.Euler(LaneSpec.RampAngleDeg, 0, 0);

            Material felt = Make.Lit(new Color(0.02f, 0.075f, 0.058f), 0.16f);
            Material rail = Make.Lit(new Color(0.014f, 0.014f, 0.016f), 0.6f, 0.4f);
            Material dark = Make.Lit(new Color(0.014f, 0.015f, 0.018f), 0.5f, 0.2f);
            Material brass = Make.Lit(new Color(1f, 0.78f, 0.36f), 0.8f, 0.65f);
            PhysicsMaterial feltPhysics = Make.Physics(0.5f, 0.1f);
            PhysicsMaterial railPhysics = Make.Physics(0.2f, 0.55f);
            PhysicsMaterial pinPhysics = Make.Physics(0.15f, 0.62f);

            Make.Box("Bed", _table, new Vector3(0, -0.6f, Length * 0.5f), new Vector3(HalfWidth * 2, 1.2f, Length), felt, feltPhysics);
            for (int side = -1; side <= 1; side += 2)
            {
                Make.Panel("Rail", _table, new Vector3(side * (HalfWidth + 0.35f), 0.2f, Length * 0.5f), new Vector3(0.7f, 1.8f, Length + 0.7f), 0.12f, rail, railPhysics, true);
                Make.Box("Edge light", _table, new Vector3(side * (HalfWidth + 0.35f), 1.115f, Length * 0.5f), new Vector3(0.1f, 0.025f, Length), Make.Emissive(Color.black, Make.Green * 3.2f), null, false);
                Make.Box("Apron", _table, new Vector3(side * (HalfWidth + 5.7f), -1.4f, Length * 0.5f), new Vector3(10f, 2.4f, Length), dark, railPhysics);
            }
            Make.Panel("End rail", _table, new Vector3(0, 0.2f, Length + 0.35f), new Vector3(HalfWidth * 2 + 1.4f, 1.8f, 0.7f), 0.12f, rail, railPhysics, true);

            // Pins. Rows alternate: one leaves a channel 2.6 inches wide down
            // the middle, the next one 5.2.
            for (int r = 0; r < Layout.Length; r++)
            {
                float z = 5f + r * 2.5f;
                foreach (float x in Layout[r])
                    for (int side = -1; side <= 1; side += 2)
                    {
                        GameObject pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        pin.name = "Pin";
                        pin.transform.SetParent(_table, false);
                        pin.transform.localPosition = new Vector3(side * x, 0.65f, z);
                        pin.transform.localScale = new Vector3(0.32f, 0.65f, 0.32f);
                        pin.GetComponent<MeshRenderer>().sharedMaterial = brass;
                        pin.GetComponent<Collider>().sharedMaterial = pinPhysics;
                    }
            }

            // Pockets, and what each is worth. The two walls of the middle
            // one run higher up the table than the rest.
            float[] edges = Edges();
            for (int i = 1; i < Values.Length; i++)
            {
                float from = i == 3 || i == 4 ? ChuteStartZ : PocketZ;
                Make.Panel("Divider", _table, new Vector3(edges[i], 0.55f, (from + Length) * 0.5f), new Vector3(0.14f, 1.1f, Length - from), 0.06f, brass, railPhysics, true);
            }
            Make.Box("Back board", _table, new Vector3(0, 2.2f, Length + 0.75f), new Vector3(HalfWidth * 2 + 1.4f, 2.6f, 0.2f), dark, null, false);
            BuildLabels(edges);

            LanesMode.Sign(Root, new Vector3(0, Top + 2.4f, World.HallEndZ - 0.2f), 11f);

            Light lamp = World.NewLight(Root, "Table light", LightType.Spot, new Color(1f, 0.95f, 0.86f), 760f);
            lamp.transform.position = new Vector3(0, Top + 16f, LaneSpec.LaneHeadZ + 12f);
            lamp.transform.rotation = Quaternion.Euler(88f, 0, 0);
            lamp.spotAngle = 86f;
            lamp.innerSpotAngle = 46f;
            lamp.range = 60f;
        }

        // The numbers over the pockets: a small canvas standing on the back board.
        void BuildLabels(float[] edges)
        {
            var go = new GameObject("Values", typeof(Canvas));
            go.transform.SetParent(_table, false);
            go.transform.localPosition = new Vector3(0, 2.2f, Length + 0.63f);
            go.transform.localScale = Vector3.one * 0.02f;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(HalfWidth * 2 / 0.02f, 2.4f / 0.02f);

            Font font = Resources.Load<Font>("PuttrArcade/Fonts/Montserrat-Bold");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < Values.Length; i++)
            {
                var label = new GameObject(Values[i].ToString(), typeof(RectTransform)).AddComponent<Text>();
                label.transform.SetParent(go.transform, false);
                label.font = font;
                label.text = Values[i].ToString();
                label.fontSize = 50;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Values[i] == 100 ? Make.Bright : new Color(1, 1, 1, 0.85f);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.raycastTarget = false;
                var rt = (RectTransform)label.transform;
                rt.sizeDelta = new Vector2((edges[i + 1] - edges[i]) / 0.02f, 100);
                rt.anchoredPosition = new Vector2((edges[i] + edges[i + 1]) * 0.5f / 0.02f, Values[i] == 100 ? 12 : 0);
                if (Values[i] != 100) continue;
                // The hundred has a condition.
                Text clean = Object.Instantiate(label, go.transform);
                clean.text = "CLEAN";
                clean.fontSize = 17;
                ((RectTransform)clean.transform).anchoredPosition = new Vector2(rt.anchoredPosition.x, -32);
            }
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = players;
            _thrown = 0;
            _total = new int[players];
            _scores = new int[players, Putts];
            var names = new List<string>();
            for (int i = 0; i < players; i++) names.Add(Arcade.PlayerName(i));
            Arcade.Hud.BuildBoard(names, Putts, "TOTAL", false, "PUTTS", 38f);
            ShowScores();
        }

        public override string Prompt
        {
            get
            {
                string who = _players > 1 ? Arcade.PlayerName(Active) + "   ·   " : "";
                return who + "Putt " + (Turn + 1) + " of " + Putts + "   ·   Putt when ready";
            }
        }

        public override IEnumerator Run(Ball ball, PuttResult putt, bool holed)
        {
            ball.SetCharged(false);
            _tings = 0;
            _touched = _nudged = false;
            ball.Hit = collision =>
            {
                // Anything but the felt, on the way down, spoils a clean run.
                Vector3 at = _table.InverseTransformPoint(ball.transform.position);
                bool brass = collision.collider is CapsuleCollider || collision.collider.name == "Divider";
                if (collision.collider.name != "Bed" && at.z > 1.5f && at.z < ChuteStartZ + 0.8f) _touched = true;

                // Brass rings; felt and rails thud.
                float speed = collision.relativeVelocity.magnitude;
                if (brass && speed > 3f && Time.time - _lastTing > 0.03f)
                {
                    _lastTing = Time.time;
                    _tings++;
                    // puttr-app-2: steel on a steel pin, the shared click until it is recorded.
                    if (!Arcade.Audio.TryLibrary("underthecup_pin", Mathf.InverseLerp(4f, 45f, speed) * 0.7f + 0.15f, 1f + Random.Range(-0.12f, 0.12f))) Arcade.Audio.PinKnock(speed);
                    if (speed > 16f) Arcade.Sparks(collision.GetContact(0).point, 3);
                }
                else if (speed > 14f) Arcade.Audio.Thump(speed);
            };

            float start = Time.time, stillFor = 0f, hungFor = 0f;
            while (Time.time - start < 10f)
            {
                yield return null;
                Vector3 local = _table.InverseTransformPoint(ball.transform.position);
                float speed = ball.Body.linearVelocity.magnitude;
                Arcade.Audio.SetRoll(local.y < 1.2f && local.y > 0f ? speed : 0f);
                if (local.y < -3f) break; // off the table
                stillFor = local.z > PocketZ && speed < 1.2f ? stillFor + Time.deltaTime : 0f;
                if (stillFor > 0.35f) break;

                // A ball can come to rest on a pin or the tip of a divider. Tap the table.
                hungFor = local.z <= PocketZ && speed < 0.6f ? hungFor + Time.deltaTime : 0f;
                if (hungFor > 0.6f && !_nudged)
                {
                    _nudged = true;
                    hungFor = 0f;
                    ball.Body.AddForce(_table.TransformDirection(new Vector3(local.x >= 0 ? 7f : -7f, 0f, 3f)), ForceMode.VelocityChange);
                    Arcade.Shake(0.15f);
                    Arcade.Audio.Thump(30f);
                }
            }
        }

        public override IEnumerator Finish(Ball ball, PuttResult putt, bool holed)
        {
            Hud hud = Arcade.Hud;
            int value = 0;
            if (ball == null)
            {
                hud.Banner(putt.Launch.Reached ? "NO DROP" : "SHORT", putt.Launch.Reached ? "It has to go in, or over the top" : "It never reached the hole", Make.Amber);
                if (!Arcade.Audio.TryLibrary("underthecup_zero", 0.7f)) Arcade.Audio.Miss();   // puttr-app-2
            }
            else
            {
                Vector3 local = _table.InverseTransformPoint(ball.transform.position);
                value = PocketValue(local);
                bool middle = value == 100;
                if (middle && _touched) value = 60;
                if (value == 100) { hud.Banner("100", "A clean run", Make.Bright, 2.2f); if (!Arcade.Audio.TryLibrary("underthecup_jackpot", 1f)) Arcade.Audio.Strike(); Arcade.Celebrate(110, ball.transform.position); }   // puttr-app-2: the table's own, the shared stings until recorded
                else if (middle) { hud.Banner("60", "In the middle, but it touched on the way", Color.white, 1.8f); if (!Arcade.Audio.TryLibrary("underthecup_pocket", 0.85f)) Arcade.Audio.Spare(); }
                else if (value > 0) { hud.Banner(value.ToString(), "", Color.white, 1.5f); if (!Arcade.Audio.TryLibrary("underthecup_pocket", 0.75f)) Arcade.Audio.Spare(); }
                else { hud.Banner("0", "It did not find a pocket", Make.Amber, 1.5f); if (!Arcade.Audio.TryLibrary("underthecup_zero", 0.7f)) Arcade.Audio.Miss(); }
                Arcade.Shot("result");
                yield return new WaitForSecondsRealtime(0.5f);
                Arcade.Release(ball);
            }
            _scores[Active, Turn] = value;
            _total[Active] += value;
            _thrown++;
            ShowScores(true);
            yield return new WaitForSecondsRealtime(1.2f);
        }

        public override bool Over => _thrown >= _players * Putts;

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

        /// <summary>What a ball at this spot on the table (in the table's own space) is worth.</summary>
        public static int PocketValue(Vector3 local)
        {
            if (local.z < PocketZ - 0.5f || local.y < -1f) return 0;
            float[] edges = Edges();
            for (int i = 0; i < Values.Length; i++) if (local.x < edges[i + 1]) return Values[i];
            return Values[Values.Length - 1];
        }

        void ShowScores(bool justScored = false)
        {
            Hud hud = Arcade.Hud;
            for (int i = 0; i < _players; i++)
            {
                int done = _thrown / _players + (i < _thrown % _players ? 1 : 0);
                for (int t = 0; t < Putts; t++) hud.SetPip(i, t, t >= done ? Hud.Pip.Empty : _scores[i, t] > 0 ? Hud.Pip.Good : Hud.Pip.Over, t < done ? _scores[i, t].ToString() : "");
                hud.SetTotal(i, _total[i]);
            }
            hud.SetTurn(Over ? -1 : Active, Over ? -1 : Turn);
        }
    }
}
