// Lanes: ten-pin bowling.
//
// The lane starts under the back edge of the mat, runs downhill at the ramp's
// own 12 degrees, and levels out for ten pins. Line picks the pins: the strike
// pocket is in line with the middle of the hole.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class LanesMode : GameMode
    {
        public override string Name => "Lanes";
        public override string VoicePrefix => "lanes";
        public override int Putter => _active; // whose difficulty applies to this putt
        public override string Blurb => "Ten-pin bowling.\nPutt along the green line\nand the rack goes down.";
        public override bool SkipMat => true; // only the lane and the pins are shown
        public override float HoledOffsetX => 0.62f; // so a center-cut putt finds the strike pocket
        public override float AimTargetInches => Arcade == null ? 0f : _aimX / Reach * Arcade.Goal * 0.5f;
        public override string Icon => "BowlingBall";
        public override Color Accent => new Color(0.3f, 0.69f, 0.31f);
        public override string[] Tags => new[] { "10 frames", "Line" };
        public override int Difficulty => 1;
        public override string[] HowTo => new[]
        {
            "Your putt is the bowl. Where it crosses the hole is where the ball rolls down the lane, at the pace you gave it.",
            "The green line is the best putt for the pins standing. On Easy the whole width of the mat is the lane; on Hard only the hole is.",
            "Ten frames, two balls a frame, scored as bowling is. Wide of the goal is a gutter ball.",
        };
        public override GameAudio.Scene Sound => GameAudio.Scene.Alley;
        public override int Score(int player) => _scores[player].Total;
        public override string ScoreWord(int n) => n == 1 ? "pin" : "pins";
        public override string Rule => "Ten frames. Putt along the green line to find the pocket.";
        public override float HoledMinSpeed => LaneSpec.HoledMinSpeed;

        public override Vector3 CameraPosition => new Vector3(0f, LaneSpec.LaneHeadY + 7.5f, LaneSpec.LaneHeadZ + 1.5f);
        public override Vector3 CameraLook => new Vector3(0f, LaneSpec.DeckY + 1.2f, LaneSpec.HeadPinZ + 2f);

        PinDeck _pins;
        Transform _mirror;
        readonly List<Transform> _pinMirrors = new List<Transform>();
        Transform _ballMirror;
        Ball _ball;

        readonly List<BowlingScore> _scores = new List<BowlingScore>();
        int _active, _players, _standingBefore, _knocks;
        float _firstKnockTime, _lastKnockTime;
        bool _ballDied, _needRack;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            _mirror = BuildLane(Root);
            _pins = new PinDeck();
            _pins.Build(Root);
            _pins.Knock += OnPinKnock;
            BuildGuide(Root);

            // The pin deck is see-through, and under it stands a mirror image
            // of every pin and of the ball. That is all the reflection is.
            foreach (Pin pin in _pins.Pins)
            {
                GameObject m = Make.MeshObject("Pin reflection", _mirror, _pins.PinMesh, _pins.PinPaint);
                m.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                _pinMirrors.Add(m.transform);
            }
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Ball reflection";
            Make.Kill(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(_mirror, false);
            sphere.transform.localScale = Vector3.one * LaneSpec.BallRadius * 2f;
            sphere.GetComponent<MeshRenderer>().sharedMaterial = Make.Lit(Color.white, 0.8f);
            sphere.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            _ballMirror = sphere.transform;
        }

        /// <summary>
        /// The lane, pit and lights, under parent. Returns the transform that
        /// mirrors its children in the pin deck. Also used by the tuning tool.
        /// </summary>
        public static Transform BuildLane(Transform parent)
        {
            Texture2D boards = Make.Wood();
            Material wood = Make.Lit(Color.white, 0.93f, 0f, boards);
            Material deckWood = Make.Transparent(new Color(1f, 1f, 1f, 0.8f), 0.95f, boards);
            Material dark = Make.Lit(new Color(0.014f, 0.015f, 0.018f), 0.5f, 0.2f);
            Material gutter = Make.Lit(new Color(0.03f, 0.032f, 0.036f), 0.82f, 0.4f);
            Material rail = Make.Lit(new Color(0.012f, 0.012f, 0.014f), 0.6f, 0.3f);
            Material edgeLight = Make.Emissive(Color.black, Make.Green * 3.2f);
            PhysicsMaterial lanePhysics = Make.Physics(0.28f, 0.1f);
            PhysicsMaterial wallPhysics = Make.Physics(0.3f, 0.45f);
            PhysicsMaterial softPhysics = Make.Physics(0.6f, 0.05f);

            float w = LaneSpec.MatHalfWidth;
            float outer = LaneSpec.LaneHalfWidth + LaneSpec.GutterWidth;
            float cos = Mathf.Cos(LaneSpec.RampAngleDeg * Mathf.Deg2Rad);

            // The slope is built flat and then tipped 12 degrees down about its head.
            float slopeLen = (LaneSpec.SlopeEndZ - LaneSpec.LaneHeadZ) / cos;
            Transform slope = new GameObject("Slope").transform;
            slope.SetParent(parent, false);
            slope.localPosition = new Vector3(0, LaneSpec.LaneHeadY, LaneSpec.LaneHeadZ);
            slope.localRotation = Quaternion.Euler(LaneSpec.RampAngleDeg, 0, 0);
            World.LaneSection(slope, slopeLen + 0.3f, 1.2f, wood, gutter, dark, edgeLight, lanePhysics, wallPhysics, true);

            float deckLen = LaneSpec.LaneEndZ - LaneSpec.SlopeEndZ;
            Transform deck = new GameObject("Deck").transform;
            deck.SetParent(parent, false);
            deck.localPosition = new Vector3(0, LaneSpec.DeckY, LaneSpec.SlopeEndZ);
            World.LaneSection(deck, deckLen, 0.06f, deckWood, gutter, dark, edgeLight, lanePhysics, wallPhysics, false);
            Make.Box("Deck end", deck, new Vector3(0, -3.1f, deckLen - 0.05f), new Vector3(LaneSpec.LaneHalfWidth * 2, 6f, 0.1f), dark, null, false);
            Make.Box("Deck front", deck, new Vector3(0, -3.1f, 0.05f), new Vector3(LaneSpec.LaneHalfWidth * 2, 6f, 0.1f), dark, null, false);
            for (int side = -1; side <= 1; side += 2)
                Make.Box("Deck skirt", deck, new Vector3(side * (LaneSpec.LaneHalfWidth - 0.02f), -3.1f, deckLen * 0.5f), new Vector3(0.04f, 6f, deckLen), dark, null, false);

            Transform mirror = new GameObject("Mirror").transform;
            mirror.SetParent(parent, false);
            mirror.localPosition = new Vector3(0, LaneSpec.DeckY, 0);
            mirror.localScale = new Vector3(1, -1, 1);

            // Kickbacks beside the pins, and the pit behind them.
            float kickStart = LaneSpec.HeadPinZ - 3f - LaneSpec.SlopeEndZ;
            float kickLen = LaneSpec.PitEndZ - LaneSpec.SlopeEndZ - kickStart;
            float pitLen = LaneSpec.PitEndZ - LaneSpec.LaneEndZ;
            for (int side = -1; side <= 1; side += 2)
            {
                Make.Box("Kickback", deck, new Vector3(side * (outer + 0.5f), 2.2f, kickStart + kickLen * 0.5f), new Vector3(1f, 4.4f, kickLen), rail, wallPhysics);
                Make.Box("Pit side", deck, new Vector3(side * (outer + 1.5f), -1.5f, deckLen + pitLen * 0.5f), new Vector3(3f, 5f, pitLen), dark, wallPhysics);
            }
            Make.Box("Pit floor", deck, new Vector3(0, -LaneSpec.PitDepth - 0.5f, deckLen + pitLen * 0.5f), new Vector3(outer * 2, 1f, pitLen), dark, softPhysics);
            Make.Box("Pit curtain", deck, new Vector3(0, 3f, deckLen + pitLen + 0.5f), new Vector3(w * 2 + 8, 22f, 1f), dark, softPhysics);

            // The masking unit above the pins carries the logo.
            Make.Box("Masking unit", deck, new Vector3(0, 7.2f, deckLen + 3f), new Vector3(w * 2 + 8, 5.4f, 1.2f), dark, null, false);
            Sign(deck, new Vector3(0, 6.5f, deckLen + 2.38f), 11f);

            Light deckLight = World.NewLight(parent, "Deck light", LightType.Spot, new Color(1f, 0.93f, 0.82f), 620f);
            deckLight.transform.position = new Vector3(0, LaneSpec.DeckY + 17f, LaneSpec.HeadPinZ + 0.5f);
            deckLight.transform.rotation = Quaternion.Euler(80f, 0, 0);
            deckLight.spotAngle = 76f;
            deckLight.innerSpotAngle = 38f;
            deckLight.range = 60f;

            // Down the slope, so the ball is never lost in the dark. Narrow, and
            // far enough back that none of it falls on the carpet: the only
            // light on the carpet is the console's own.
            Light run = World.NewLight(parent, "Slope light", LightType.Spot, new Color(0.9f, 0.96f, 1f), 220f);
            run.transform.position = new Vector3(0, LaneSpec.LaneHeadY + 15f, 12f);
            run.transform.rotation = Quaternion.Euler(90f, 0, 0);
            run.spotAngle = 44f;
            run.innerSpotAngle = 24f;
            run.range = 40f;
            run.shadows = LightShadows.None;
            return mirror;
        }

        /// <summary>The Puttr logo as a lit sign, facing the Player.</summary>
        public static void Sign(Transform parent, Vector3 localPosition, float width)
        {
            var logo = Resources.Load<Texture2D>("PuttrArcade/LogoHeader");
            if (logo == null) return;
            GameObject sign = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sign.name = "Logo";
            Make.Kill(sign.GetComponent<Collider>());
            sign.transform.SetParent(parent, false);
            sign.transform.localPosition = localPosition;
            sign.transform.localScale = new Vector3(width, width * logo.height / logo.width, 1f);
            sign.GetComponent<MeshRenderer>().sharedMaterial = Make.Glow(new Color(1.25f, 1.25f, 1.25f, 1f), false, logo);
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = players;
            _scores.Clear();
            var names = new List<string>();
            for (int i = 0; i < players; i++)
            {
                _scores.Add(new BowlingScore());
                names.Add(Arcade.PlayerName(i));
            }
            _active = 0;
            _needRack = true;
            Arcade.Hud.BuildTable(names, 10, true, 72f); // narrow enough to sit between the title and the console status
            ShowScores();
        }

        public override IEnumerator Prepare()
        {
            if (_needRack)
            {
                // Lower a fresh rack onto the deck.
                _pins.SetKinematic(true);
                const float lift = 5f;
                for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.5f)
                {
                    _pins.Rack(lift * (1f - Mathf.SmoothStep(0, 1, t)));
                    yield return null;
                }
                _pins.Rack();
                _pins.SetKinematic(false);
                Arcade.Audio.FrameTick();
            }
            else _pins.ClearFallen();
            _needRack = false;
            _standingBefore = _pins.StandingCount();
            _knocks = 0;
            _ballDied = false;
            Aim();
        }

        public override string Prompt
        {
            get
            {
                BowlingScore score = _scores[_active];
                string who = _players > 1 ? Arcade.PlayerName(_active) + "   ·   " : "";
                string ball = score.BallInFrame == 0 ? "" : score.BallInFrame == 1 ? "   ·   Second ball" : "   ·   Bonus ball";
                return who + "Frame " + (score.FrameIndex + 1) + ball + "   ·   " + AimWords();
            }
        }

        // ── Where to putt ───────────────────────────────────────────────────
        //
        // The goal (the whole mat on Easy, the hole on Hard) is laid across the lane, edge to
        // edge. A green line runs from where the best putt would start to the pin it is for,
        // and the prompt says the same thing in inches on the mat.

        const float Reach = LaneSpec.LaneHalfWidth - 1.1f; // the goal's edge on the lane: the corner pins are inside it
        Transform _guide;
        LineRenderer _guideLine;
        float _aimX;

        void BuildGuide(Transform root)
        {
            _guide = new GameObject("Aim guide").transform;
            _guide.SetParent(root, false);
            var green = new Color(0.48f, 0.83f, 0.27f);
            var line = new GameObject("Line");
            line.transform.SetParent(_guide, false);
            _guideLine = line.AddComponent<LineRenderer>();
            _guideLine.useWorldSpace = false;
            _guideLine.alignment = LineAlignment.TransformZ;
            line.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // flat on the lane
            _guideLine.material = Make.Glow(new Color(green.r, green.g, green.b, 0.9f), false);
            _guideLine.widthMultiplier = 0.6f;
            _guideLine.numCapVertices = 4;
            _guideLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _guide.gameObject.SetActive(false);
        }

        // The best line for the pins standing: the pocket for a full rack, otherwise the pin
        // nearest the bowler, leaning a little toward the rest.
        void Aim()
        {
            float front = float.MaxValue, frontX = 0f, sum = 0f;
            int standing = 0;
            foreach (Pin pin in _pins.Pins)
            {
                if (!pin.Standing) continue;
                Vector3 p = pin.transform.position;
                standing++;
                sum += p.x;
                if (p.z < front - 0.2f || (Mathf.Abs(p.z - front) <= 0.2f && Mathf.Abs(p.x) < Mathf.Abs(frontX))) { front = p.z; frontX = p.x; }
            }
            float pocket = 0.62f; // just right of the head pin
            _aimX = standing == 0 || standing == 10 ? pocket : Mathf.Clamp(frontX + 0.25f * (sum / standing - frontX), -Reach, Reach);
            float end = standing == 0 || standing == 10 ? LaneSpec.HeadPinZ : front;

            var points = new List<Vector3>();
            for (float z = LaneSpec.LaneHeadZ + 1f; z < end - 1.2f; z += 0.5f)
                points.Add(new Vector3(_aimX, z, -(LaneSpec.SurfaceY(z) + 0.04f))); // the line's own axes: x across, y along, z down
            _guideLine.positionCount = points.Count;
            _guideLine.SetPositions(points.ToArray());
            _guide.gameObject.SetActive(points.Count > 1);
            // The same putt on the ramp, top right: where it crosses and how it comes in from this tee.
            ShowAim();
        }

        string AimWords()
        {
            float inches = _aimX / Reach * Arcade.Goal * 0.5f; // on the mat, from the hole's center
            if (Mathf.Abs(inches) < 0.2f) return "Putt dead center";
            return "Putt " + Mathf.Abs(inches).ToString(Mathf.Abs(inches) < 3f ? "0.0" : "0") + " in " + (inches < 0f ? "left" : "right") + " of center";
        }

        public override void Send(Ball ball, float across, float speed)
        {
            if (_guide != null) _guide.gameObject.SetActive(false);
            // Inside the goal it rolls down the lane where it crossed. Wide of it, the gutter.
            float x = Mathf.Abs(across) <= 1f ? across * Reach : Mathf.Sign(across) * (LaneSpec.LaneHalfWidth + LaneSpec.GutterWidth * 0.5f);
            float z = LaneSpec.LaneHeadZ + 1f;
            ball.Launch(new Vector3(x, LaneSpec.LaneHeadY + LaneSpec.BallRadius + 0.02f, z), Vector3.forward * Mathf.Max(HoledMinSpeed, speed));
            ball.Trail.Clear();
        }

        // Wait until the ball is gone and the pins have stopped moving.
        public override IEnumerator Run(Ball ball, PuttResult putt, bool holed)
        {
            _ball = ball;
            ball.SetCharged(holed); // a holed ball glows, and hits half as hard again
            float start = Time.time, stillFor = 0f, deadFor = 0f;
            bool shot = false;
            while (true)
            {
                yield return null;
                Vector3 p = ball.transform.position;
                float speed = ball.Body.linearVelocity.magnitude;
                bool onSurface = p.z < LaneSpec.LaneEndZ && Mathf.Abs(p.y - LaneSpec.BallRadius - LaneSpec.SurfaceY(p.z)) < 0.4f;
                Arcade.Audio.SetRoll(onSurface ? speed : 0f);

                deadFor = speed < 0.8f ? deadFor + Time.deltaTime : 0f;
                bool ballDone = p.z > LaneSpec.LaneEndZ + 0.3f || p.y < LaneSpec.DeckY - 2f || deadFor > 0.5f;
                if (deadFor > 0.5f && _knocks == 0 && p.z < LaneSpec.HeadPinZ - 1.5f) _ballDied = true;

                if (_knocks > 0 && !shot && Time.time - _firstKnockTime > 0.1f) { shot = true; Arcade.Shot("impact"); }

                bool quiet = _knocks == 0 || (_pins.Settled() && Time.time - _lastKnockTime > 0.35f);
                stillFor = ballDone && quiet ? stillFor + Time.deltaTime : 0f;
                if (stillFor > 0.45f || Time.time - start > 9f) yield break;
            }
        }

        public override IEnumerator Finish(Ball ball, PuttResult putt, bool holed)
        {
            BowlingScore score = _scores[_active];
            int pinsDown = ball == null ? 0 : Mathf.Clamp(_standingBefore - _pins.StandingCount(), 0, score.PinsStanding);
            if (ball != null) Arcade.Release(ball);
            _ball = null;

            int stood = score.PinsStanding;
            bool strike = stood == 10 && pinsDown == 10;
            bool spare = stood < 10 && pinsDown == stood;
            int frameBefore = score.FrameIndex;
            score.Add(pinsDown);
            ShowScores();

            Hud hud = Arcade.Hud;
            Vector3 deck = new Vector3(0, LaneSpec.DeckY, LaneSpec.HeadPinZ + 3f);
            if (ball == null) { hud.Banner("SHORT", putt.Launch.Reached ? "Not enough pace to reach the lane" : "It never reached the hole", Make.Amber); Arcade.Audio.Miss(); }
            else if (strike) { hud.Banner("STRIKE", holed ? "Holed, and all ten" : "All ten", Make.Bright, 2.2f); Arcade.Audio.Strike(); Arcade.Celebrate(140, deck); }
            else if (spare) { hud.Banner("SPARE", "", Make.Bright, 2f); Arcade.Audio.Spare(); Arcade.Celebrate(60, deck); }
            else if (pinsDown == 0) { hud.Banner("MISS", _ballDied ? "Ran out of pace" : "", Make.Red); Arcade.Audio.Miss(); }
            else hud.Banner(pinsDown.ToString(), pinsDown == 1 ? "pin" : "pins", Color.white, 1.5f);
            Arcade.Shot("result");
            yield return new WaitForSecondsRealtime(1.5f);

            // Whose turn, and on what rack?
            bool frameOver = score.Complete || score.FrameIndex != frameBefore;
            if (frameOver && _players > 1)
            {
                for (int k = 1; k <= _players; k++)
                {
                    int next = (_active + k) % _players;
                    if (!_scores[next].Complete) { _active = next; break; }
                }
            }
            _needRack = !Over && _scores[_active].PinsStanding == 10;
            ShowScores();
        }

        public override bool Over => _scores.TrueForAll(s => s.Complete);

        public override void Final(out string title, out string body)
        {
            title = _players == 1 ? _scores[0].Total + "  P I N S" : "G A M E   O V E R";
            body = "";
            int best = 0;
            for (int i = 0; i < _scores.Count; i++) if (_scores[i].Total > _scores[best].Total) best = i;
            if (_players > 1)
                for (int i = 0; i < _scores.Count; i++)
                    body += Arcade.PlayerName(i) + "     " + _scores[i].Total + (i == best ? "     winner" : "") + "\n";
        }

        void ShowScores()
        {
            Hud hud = Arcade.Hud;
            for (int r = 0; r < _scores.Count; r++)
            {
                BowlingScore.Frame[] frames = _scores[r].Frames();
                for (int f = 0; f < 10; f++)
                    hud.SetCell(r, f, string.Join("  ", frames[f].Marks), frames[f].Total.HasValue ? frames[f].Total.Value.ToString() : "");
                hud.SetTotal(r, _scores[r].Total);
            }
            hud.SetTurn(_active, Over ? -1 : _scores[_active].FrameIndex);
        }

        void OnPinKnock(Pin pin, float speed)
        {
            if (_ball == null) return;
            if (_knocks == 0)
            {
                _firstKnockTime = Time.time;
                // The first hit: slow everything down so the pin action can be seen.
                if (speed > 10f) Arcade.SlowMotion(0.85f);
                // No camera shake here: seen down the length of the lane, even a small one
                // makes the whole floor shudder. The slow motion and the pins carry the hit.
            }
            _knocks++;
            _lastKnockTime = Time.time;
            Arcade.Audio.PinKnock(speed);
            if (speed > 18f) Arcade.Sparks(pin.transform.position + Vector3.up * 1.4f, Mathf.Min(6, (int)(speed / 8f)));
        }

        public override void Tick()
        {
            Vector3 origin = _mirror.position;
            for (int i = 0; i < _pinMirrors.Count; i++)
            {
                Transform pin = _pins.Pins[i].transform;
                Vector3 p = pin.position;
                bool onDeck = pin.gameObject.activeSelf && p.z > LaneSpec.SlopeEndZ && p.z < LaneSpec.LaneEndZ + 1f && Mathf.Abs(p.x) < LaneSpec.LaneHalfWidth + 1f && p.y > LaneSpec.DeckY - 0.3f;
                Transform m = _pinMirrors[i];
                if (m.gameObject.activeSelf != onDeck) m.gameObject.SetActive(onDeck);
                if (onDeck) { m.localPosition = p - origin; m.localRotation = pin.rotation; }
            }
            bool ballOnDeck = false;
            if (_ball != null)
            {
                Vector3 bp = _ball.transform.position;
                ballOnDeck = _ball.gameObject.activeSelf && bp.z > LaneSpec.SlopeEndZ + 0.5f && bp.z < LaneSpec.LaneEndZ && Mathf.Abs(bp.x) < LaneSpec.LaneHalfWidth && bp.y > LaneSpec.DeckY;
                if (ballOnDeck) { _ballMirror.localPosition = bp - origin; _ballMirror.localRotation = _ball.transform.rotation; }
            }
            if (_ballMirror.gameObject.activeSelf != ballOnDeck) _ballMirror.gameObject.SetActive(ballOnDeck);
        }

        public override void End()
        {
            _ball = null;
            if (_guide != null) _guide.gameObject.SetActive(false);
        }
    }
}
