// Putt Star (puttr-app Rock.js, rebuilt on a stage): you are the lead singer. Every putt you hole wins the crowd
// over; every miss gets you booed.
//
// The crowd starts at 50. A make (in through the front of the cup) adds its distance; a miss, or a make through the
// back, takes its distance away. Easy doubles what a make is worth and halves a miss; Hard halves a make and doubles a
// miss. Past 100 the crowd is yours and the show is a hit. Below 0 you're booed off. Three makes in a row sets the
// stage on fire. A new random putt every time, 3 to 11 feet, any tee.
//
// The set: the crowd in the foreground, backs to the camera, arms going up and jumping as they get into it, turning
// on you when you miss. The band behind, you at the mic. Sounds are the app's own (rock_make_*, rock_miss_*, rock_horn).
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class PuttStarMode : StageMode
    {
        public override string Name => "Putt Star";
        public override string Blurb => "You're the lead singer.\nHole putts, win the crowd.\nMiss and they boo.";
        public override string Rule => "Every make wins the crowd over, every miss loses them. Get them past 100 before they boo you off.";
        public override string Icon => "Megaphone";
        public override Color Accent => new Color(0.98f, 0.3f, 0.82f);
        public override string[] Tags => new[] { "Crowd meter", "Makes" };
        public override string[] HowTo => new[]
        {
            "You're the lead singer. The crowd starts at 50.",
            "Hole a putt and the crowd gets into it by its distance. Miss and they boo you down by the same.",
            "Get them past 100 to win the night. Hit zero and you're booed off.",
        };
        public override int MaxPlayers => 1;
        public override bool DealsTee => false;
        public override bool ShowsAim => false;
        public override bool HasPortrait => true;
        public override string Tee => null; // the tee sign says it
        public override GameAudio.Scene Sound => GameAudio.Scene.Stadium;
        public override int Score(int player) => Mathf.Clamp(Mathf.RoundToInt(_level), 0, 100);

        public override Vector3 CameraPosition => Portrait ? At(0f, 46f, -128f) : At(0f, 44f, -120f);
        public override Vector3 CameraLook => Portrait ? At(0f, 28f, 30f) : At(0f, 28f, 30f);
        public override float CameraFov => Portrait ? 50f : 42f;
        public override float ShadowDistance => 500f;

        const float StageY = 12f, Start = 50f;
        const int Recent = 10;

        sealed class Fan
        {
            public Transform Body, ArmL, ArmR;
            public Vector3 Home;
            public float Phase, Rate;
            public Light Phone;
        }

        readonly List<Fan> _fans = new List<Fan>();
        readonly List<Transform> _beams = new List<Transform>();
        readonly List<Transform> _thrown = new List<Transform>();
        readonly List<Vector3> _throwFrom = new List<Vector3>(), _throwTo = new List<Vector3>();
        readonly System.Random _dice = new System.Random();
        Transform _singer, _micArm, _guitarL, _guitarR, _drummer;
        TextMesh _shout, _meterText;
        Transform _meterFill;
        Material _meterMat;
        readonly List<Transform> _flames = new List<Transform>();
        float _level = Start, _shown = Start, _boo, _cheer, _throwT = -1f;
        int _streak, _putts, _made;
        readonly List<bool> _last = new List<bool>();
        bool _onFire, _done, _live;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            Material floor = Make.Lit(new Color(0.03f, 0.03f, 0.04f), 0.25f);
            Material deck = Make.Lit(new Color(0.06f, 0.05f, 0.06f), 0.7f, 0.1f);
            Material metal = Make.Lit(new Color(0.18f, 0.18f, 0.2f), 0.7f, 0.8f);
            Material black = Make.Lit(new Color(0.02f, 0.02f, 0.025f), 0.4f);
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -1f, -60f), new Vector3(900f, 2f, 600f), floor);
            Shape(PrimitiveType.Cube, "Stage", Root, new Vector3(0, StageY / 2f, 40f), new Vector3(200f, StageY, 80f), deck);
            Shape(PrimitiveType.Cube, "Stage lip", Root, new Vector3(0, StageY - 0.4f, 0.5f), new Vector3(200f, 0.8f, 1.5f), Make.Emissive(Color.black, new Color(1f, 0.3f, 0.8f) * 2f));
            // Speaker stacks, a riser, the screen behind.
            for (int s = -1; s <= 1; s += 2)
            {
                Shape(PrimitiveType.Cube, "Speakers", Root, new Vector3(s * 88f, StageY + 18f, 14f), new Vector3(22f, 36f, 16f), black);
                for (int k = 0; k < 3; k++) Shape(PrimitiveType.Cylinder, "Cone", Root, new Vector3(s * 88f, StageY + 7f + k * 11f, 5.8f), new Vector3(9f, 0.4f, 9f), metal).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            }
            Shape(PrimitiveType.Cube, "Riser", Root, new Vector3(0, StageY + 3f, 56f), new Vector3(46f, 6f, 26f), black);
            Shape(PrimitiveType.Cube, "Screen", Root, new Vector3(0, StageY + 46f, 79f), new Vector3(190f, 82f, 2f), Make.Emissive(Color.black, new Color(0.25f, 0.05f, 0.35f)));
            Words(Root, "PUTTR", new Vector3(0, StageY + 62f, 77.5f), 9f, new Color(0.48f, 0.83f, 0.27f));
            Words(Root, "LIVE TONIGHT", new Vector3(0, StageY + 52f, 77.5f), 3.5f, Color.white);
            // The truss and its beams.
            Shape(PrimitiveType.Cube, "Truss", Root, new Vector3(0, StageY + 92f, 20f), new Vector3(220f, 4f, 4f), metal);
            Color[] beamColour = { new Color(1f, 0.3f, 0.8f), new Color(0.3f, 0.6f, 1f), new Color(0.48f, 0.83f, 0.27f), new Color(1f, 0.6f, 0.2f) };
            for (int i = 0; i < 8; i++)
            {
                float x = (i - 3.5f) * 26f;
                Transform pivot = new GameObject("Beam").transform;
                pivot.SetParent(Root, false);
                pivot.localPosition = new Vector3(x, StageY + 90f, 20f);
                Transform cone = Shape(PrimitiveType.Cylinder, "Light", pivot, new Vector3(0, -60f, 0), new Vector3(10f, 60f, 10f), Make.Glow(beamColour[i % 4] * 0.22f)).transform;
                _beams.Add(pivot);
            }

            // The band.
            _singer = Person("Singer", new Vector3(0, StageY, 12f), new Color(0.08f, 0.08f, 0.1f), new Color(0.85f, 0.65f, 0.5f), 1.15f, out _micArm, out _);
            Shape(PrimitiveType.Cylinder, "Mic stand", Root, new Vector3(0, StageY + 9f, 6f), new Vector3(0.6f, 9f, 0.6f), metal);
            Shape(PrimitiveType.Sphere, "Mic", Root, new Vector3(0, StageY + 19f, 6f), Vector3.one * 1.8f, metal);
            Transform g = Person("Guitar", new Vector3(-44f, StageY, 30f), new Color(0.35f, 0.08f, 0.08f), new Color(0.8f, 0.6f, 0.45f), 1f, out _, out _);
            Shape(PrimitiveType.Cube, "Guitar body", g, new Vector3(1.5f, 10f, -3f), new Vector3(8f, 10f, 2f), Make.Lit(new Color(0.9f, 0.2f, 0.15f), 0.8f)).transform.localRotation = Quaternion.Euler(0, 0, 30f);
            _guitarL = g;
            Transform b = Person("Bass", new Vector3(44f, StageY, 30f), new Color(0.1f, 0.15f, 0.35f), new Color(0.55f, 0.38f, 0.28f), 1f, out _, out _);
            Shape(PrimitiveType.Cube, "Bass body", b, new Vector3(-1.5f, 10f, -3f), new Vector3(8f, 10f, 2f), Make.Lit(new Color(0.15f, 0.4f, 0.95f), 0.8f)).transform.localRotation = Quaternion.Euler(0, 0, -30f);
            _guitarR = b;
            _drummer = Person("Drums", new Vector3(0, StageY + 6f, 62f), new Color(0.2f, 0.2f, 0.22f), new Color(0.75f, 0.55f, 0.4f), 0.9f, out _, out _);
            Material drum = Make.Lit(new Color(0.85f, 0.85f, 0.88f), 0.6f, 0.2f);
            Shape(PrimitiveType.Cylinder, "Kick", Root, new Vector3(0, StageY + 13f, 50f), new Vector3(14f, 3f, 14f), drum).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            foreach (float x in new[] { -12f, 12f }) Shape(PrimitiveType.Cylinder, "Tom", Root, new Vector3(x, StageY + 16f, 54f), new Vector3(8f, 3f, 8f), drum);
            foreach (float x in new[] { -18f, 18f }) Shape(PrimitiveType.Cylinder, "Cymbal", Root, new Vector3(x, StageY + 24f, 56f), new Vector3(10f, 0.2f, 10f), Make.Lit(new Color(0.95f, 0.75f, 0.3f), 0.9f, 0.9f));

            // Flames along the stage front for a hot streak.
            for (int i = -2; i <= 2; i++)
            {
                if (i == 0) continue;
                Transform f = Shape(PrimitiveType.Sphere, "Flame", Root, new Vector3(i * 34f, StageY + 10f, 3f), new Vector3(7f, 20f, 7f), Make.Glow(new Color(1f, 0.45f, 0.1f) * 1.4f)).transform;
                f.gameObject.SetActive(false);
                _flames.Add(f);
            }

            // The crowd, backs to the camera.
            Color[] shirts = { new Color(0.1f, 0.1f, 0.12f), new Color(0.2f, 0.06f, 0.08f), new Color(0.06f, 0.1f, 0.22f), new Color(0.15f, 0.15f, 0.15f), new Color(0.05f, 0.14f, 0.08f) };
            for (int row = 0; row < 7; row++)
                for (int i = -10; i <= 10; i++)
                {
                    float x = i * 11f + (row % 2) * 5.5f + (float)(_dice.NextDouble() - 0.5) * 3f;
                    float z = -16f - row * 12f + (float)(_dice.NextDouble() - 0.5) * 3f;
                    float size = 0.9f + (float)_dice.NextDouble() * 0.25f;
                    Transform p = Person("Fan", new Vector3(x, 0f, z), shirts[_dice.Next(shirts.Length)], new Color(0.12f, 0.09f, 0.08f), size, out Transform armL, out Transform armR);
                    p.localRotation = Quaternion.Euler(0, 180f, 0);
                    var fan = new Fan { Body = p, ArmL = armL, ArmR = armR, Home = p.localPosition, Phase = (float)_dice.NextDouble() * 6f, Rate = 5.5f + (float)_dice.NextDouble() * 2.5f };
                    if (_dice.Next(5) == 0)
                    {
                        Shape(PrimitiveType.Cube, "Phone", armR, new Vector3(0, -8.5f, 0), new Vector3(1.4f, 2.4f, 0.4f), Make.Emissive(Color.white, Color.white * 2f));
                    }
                    _fans.Add(fan);
                }

            // What the crowd shouts, and the meter, standing over the crowd.
            _shout = Words(Root, "", new Vector3(0, 30f, -30f), 4.5f, Color.white);

            Sun(Root, new Vector3(30f, 180f, 0f), new Color(0.5f, 0.45f, 0.7f), 0.5f);
            Light front = World.NewLight(Root, "Front of house", LightType.Spot, new Color(1f, 0.85f, 0.75f), 45000f);
            front.transform.localPosition = new Vector3(0, 90f, -60f);
            front.transform.localRotation = Quaternion.LookRotation(new Vector3(0, StageY + 10f, 20f) - new Vector3(0, 90f, -60f));
            front.spotAngle = 50f;
            front.range = 400f;
            Light back = World.NewLight(Root, "Back light", LightType.Point, new Color(0.9f, 0.4f, 1f), 120000f);
            back.transform.localPosition = new Vector3(0, StageY + 40f, 70f);
            back.range = 260f;
        }

        // A person from shapes: body, head, two arms on shoulder pivots. Stands at feet.
        Transform Person(string name, Vector3 feet, Color clothes, Color skin, float size, out Transform armL, out Transform armR)
        {
            Transform p = new GameObject(name).transform;
            p.SetParent(Root, false);
            p.localPosition = feet;
            p.localScale = Vector3.one * size;
            Material cloth = Make.Lit(clothes, 0.3f), face = Make.Lit(skin, 0.4f);
            Shape(PrimitiveType.Capsule, "Body", p, new Vector3(0, 9f, 0), new Vector3(7f, 7.5f, 5f), cloth);
            Shape(PrimitiveType.Sphere, "Head", p, new Vector3(0, 18.5f, 0), Vector3.one * 5.2f, face);
            Shape(PrimitiveType.Sphere, "Hair", p, new Vector3(0, 19.6f, 0.4f), new Vector3(5.4f, 4f, 5.4f), Make.Lit(new Color(0.06f, 0.04f, 0.03f), 0.3f));
            armL = Arm(p, -4.2f, cloth);
            armR = Arm(p, 4.2f, cloth);
            return p;
        }

        static Transform Arm(Transform body, float x, Material cloth)
        {
            Transform pivot = new GameObject("Shoulder").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(x, 14f, 0);
            Shape(PrimitiveType.Capsule, "Arm", pivot, new Vector3(0, -4.5f, 0), new Vector3(1.8f, 4.5f, 1.8f), cloth);
            return pivot;
        }

        // ── The show ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _level = _shown = Start;
            _streak = _putts = _made = 0;
            _last.Clear();
            _onFire = _done = false;
            _boo = _cheer = 0f;
            _throwT = -1f;
            foreach (Transform t in _thrown) if (t != null) Object.Destroy(t.gameObject);
            _thrown.Clear(); _throwFrom.Clear(); _throwTo.Clear();
            foreach (Transform f in _flames) f.gameObject.SetActive(false);
            if (_shout != null) _shout.text = "";
            if (Arcade != null && Arcade.Hud != null)
            {
                Arcade.Hud.BuildBoard(new List<string> { Arcade.PlayerName(0) }, Recent, "CROWD", false, "LAST 10", 30f);
                Arcade.Hud.SetTotal(0, (int)_level);
                Arcade.Hud.SetTurn(0, 0);
                Deal();
                Arcade.Audio.Clip("rock_horn");
                Arcade.Audio.Library("rock_riff", 0.8f);
                Arcade.Audio.GameBed("concert_loop", 0.25f);
                _live = true;
            }
        }

        void Deal()
        {
            DealtFeetNow = _dice.Next(3, 12);
            DealtTee = _dice.Next(1, 4);
            ShowTeeSign();
            if (Arcade != null) Arcade.Audio.Voice(VoicePrefix + "_distance_" + DealtFeetNow + "_feet", VoicePrefix + "_tee_" + DealtTeeWord[DealtTee - 1]);
        }

        public override string Prompt => _done ? "" : (_onFire ? "On fire   ·   " : "") + "Putt when ready";

        public override void Tick()
        {
            float t = Time.time, dt = Time.deltaTime;
            _shown = Mathf.MoveTowards(_shown, Mathf.Clamp(_level, 0f, 100f), dt * 40f);
            float energy = Mathf.Clamp01(_shown / 100f);
            _cheer = Mathf.Max(0f, _cheer - dt * 0.5f);
            _boo = Mathf.Max(0f, _boo - dt * 0.45f);
            float hype = Mathf.Clamp01(energy * energy + _cheer);

            foreach (Fan f in _fans)
            {
                // Into it: jumping, both arms up. Booing: still, arms down, leaning back. In between: a nod.
                float beat = Mathf.Max(0f, Mathf.Sin(t * f.Rate + f.Phase));
                float jump = hype > 0.45f ? beat * (hype - 0.45f) * 9f : 0f;
                float nod = Mathf.Sin(t * 4f + f.Phase) * (0.3f + hype) * 0.6f;
                f.Body.localPosition = f.Home + new Vector3(0, jump + nod * 0.3f, _boo * 2f);
                f.Body.localRotation = Quaternion.Euler(-_boo * 14f, 180f + Mathf.Sin(t * 0.7f + f.Phase) * 8f * (1f - _boo), 0);
                float up = Mathf.Lerp(10f, 168f, Mathf.Clamp01(hype * 1.15f)) + Mathf.Sin(t * f.Rate * 0.5f + f.Phase) * 12f * hype;
                if (_boo > 0.2f) up = Mathf.Lerp(up, f.Phase > 3f ? 150f : 20f, _boo); // some shake a fist
                f.ArmL.localRotation = Quaternion.Euler(0, 0, -up);
                f.ArmR.localRotation = Quaternion.Euler(0, 0, up);
            }

            // The band plays harder as the crowd gets into it.
            float groove = 0.4f + hype;
            if (_live && !_done) Arcade.Audio.GameBed("concert_loop", 0.12f + 0.4f * hype);
            if (_singer != null)
            {
                _singer.localPosition = new Vector3(Mathf.Sin(t * 1.3f) * 3f, StageY + Mathf.Abs(Mathf.Sin(t * 4f)) * groove * 1.5f, 12f);
                _micArm.localRotation = Quaternion.Euler(-60f - Mathf.Sin(t * 2f) * 10f, 0, 20f + _cheer * 120f);
                _guitarL.localRotation = Quaternion.Euler(0, 15f, Mathf.Sin(t * 4f) * 6f * groove);
                _guitarR.localRotation = Quaternion.Euler(0, -15f, Mathf.Sin(t * 4f + 1f) * 6f * groove);
                _drummer.localPosition = new Vector3(0, StageY + 6f + Mathf.Abs(Mathf.Sin(t * 8f)) * groove, 62f);
            }
            for (int i = 0; i < _beams.Count; i++)
                _beams[i].localRotation = Quaternion.Euler(Mathf.Sin(t * (0.6f + hype) + i) * (12f + 18f * hype), 0, Mathf.Cos(t * (0.5f + hype) + i * 1.7f) * (14f + 20f * hype));
            foreach (Transform f in _flames)
                if (f.gameObject.activeSelf) f.localScale = new Vector3(7f, 16f + Mathf.Abs(Mathf.Sin(t * 13f + f.localPosition.x)) * 12f, 7f);

            // The meter.
            if (_meterFill != null)
            {
                float w = Mathf.Max(0.5f, energy * 120f);
                _meterFill.localScale = new Vector3(w, 5f, 1f);
                _meterFill.localPosition = new Vector3(-60f + w / 2f, StageY + 78f, -0.8f);
                Color c = energy < 0.3f ? new Color(1f, 0.25f, 0.25f) : energy < 0.6f ? Make.Amber : Make.Bright;
                _meterMat.SetColor("_EmissionColor", c * 2f);
                _meterText.text = "CROWD " + Mathf.RoundToInt(_shown);
            }
            if (_shout != null) _shout.transform.localPosition = new Vector3(0, 30f + Mathf.Sin(t * 3f) * 1.5f, -30f);

            // What they throw when they don't like it.
            if (_throwT >= 0f)
            {
                _throwT += dt / 0.9f;
                for (int i = 0; i < _thrown.Count; i++)
                {
                    float k = Mathf.Clamp01(_throwT - i * 0.06f);
                    _thrown[i].localPosition = Vector3.Lerp(_throwFrom[i], _throwTo[i], k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 40f;
                }
                if (_throwT > 2.2f) { foreach (Transform x in _thrown) Object.Destroy(x.gameObject); _thrown.Clear(); _throwFrom.Clear(); _throwTo.Clear(); _throwT = -1f; }
            }
        }

        public override IEnumerator Play(PuttResult putt)
        {
            if (_done) yield break;
            int feet = DealtFeetNow;
            int level = Arcade != null ? Arcade.Level : 1; // 0 Easy, 1 Standard, 2 Hard
            bool backDoor = putt.Made && putt.Launch.Reached && Mathf.Abs(putt.Launch.HeadingDeg) >= 90f;
            bool made = putt.Made && !backDoor;
            float change = feet;
            if (made) change *= level == 0 ? 2f : level == 2 ? 0.5f : 1f;
            else change *= level == 0 ? 0.5f : level == 2 ? 2f : 1f;
            _putts++;
            _last.Add(made);
            if (_last.Count > Recent) _last.RemoveAt(0);
            if (made)
            {
                _made++;
                _streak++;
                _level += change;
                _cheer = 0.9f;
                _boo = 0f;
                Shout(_level > 85f ? "ENCORE!" : _streak >= 3 ? "ON FIRE!" : "YEAH!", Make.Bright);
                Arcade.Audio.Library("concert_cheer", 0.95f);
                Arcade.Celebrate(_level > 80f ? 90 : 40, At(0, StageY + 30f, 10f));
                ShowPutt(putt, "+" + Mathf.RoundToInt(change) + " crowd", Make.Bright);
                if (_streak == 3) { _onFire = true; Arcade.Audio.Library("rock_screech"); Arcade.Audio.Library("fire_whoosh"); foreach (Transform f in _flames) f.gameObject.SetActive(true); Arcade.Hud.Banner("ON FIRE", "Three in a row. The place is shaking.", new Color(1f, 0.55f, 0.15f), 2f); }
                else Arcade.Hud.Banner("+" + Mathf.RoundToInt(change), "The crowd loves it", Make.Bright, 1.6f);
            }
            else
            {
                _streak = 0;
                _onFire = false;
                foreach (Transform f in _flames) f.gameObject.SetActive(false);
                _level -= change;
                _boo = 1f;
                _cheer = 0f;
                Shout("BOOOO!", new Color(1f, 0.35f, 0.35f));
                Arcade.Audio.Library("concert_boo", 0.95f);
                Throw();
                Arcade.Shake(0.1f);
                ShowPutt(putt, "-" + Mathf.RoundToInt(change) + " crowd", Make.Amber);
                Arcade.Hud.Banner("-" + Mathf.RoundToInt(change), backDoor ? "In the back door. They saw that." : "They're booing", new Color(1f, 0.4f, 0.4f), 1.6f);
            }
            ShowBoard();
            if (_level > 100f) { _done = true; Shout("YOU CRUSHED IT!", Make.Bright); Arcade.Audio.Clip("rock_horn"); Arcade.Audio.Library("rock_riff"); Arcade.Celebrate(160, At(0, StageY + 40f, 10f)); }
            else if (_level < 0f) { _done = true; Shout("BOOED OFF", new Color(1f, 0.35f, 0.35f)); }
            else Deal();
            yield return new WaitForSecondsRealtime(_done ? 2.5f : 1.2f);
        }

        void Shout(string words, Color c) { if (_shout == null) return; _shout.text = words; _shout.color = c; }

        // Tomatoes and cups, from the crowd onto the stage.
        void Throw()
        {
            Material red = Make.Lit(new Color(0.85f, 0.12f, 0.1f), 0.7f), cup = Make.Lit(new Color(0.9f, 0.9f, 0.9f), 0.4f);
            for (int i = 0; i < 7; i++)
            {
                Fan f = _fans[_dice.Next(_fans.Count)];
                Vector3 from = f.Home + Vector3.up * 22f;
                Vector3 to = new Vector3((float)(_dice.NextDouble() - 0.5) * 70f, StageY + 1f, 6f + (float)_dice.NextDouble() * 20f);
                Transform t = Shape(i % 3 == 2 ? PrimitiveType.Cylinder : PrimitiveType.Sphere, "Thrown", Root, from, i % 3 == 2 ? new Vector3(2f, 2.5f, 2f) : Vector3.one * 2.6f, i % 3 == 2 ? cup : red).transform;
                _thrown.Add(t); _throwFrom.Add(from); _throwTo.Add(to);
            }
            _throwT = 0f;
        }

        void ShowBoard()
        {
            Hud hud = Arcade.Hud;
            for (int i = 0; i < Recent; i++) hud.SetPip(0, i, i >= _last.Count ? Hud.Pip.Empty : _last[i] ? Hud.Pip.Good : Hud.Pip.Bad);
            hud.SetTotal(0, Mathf.Clamp(Mathf.RoundToInt(_level), 0, 100));
            hud.SetTurn(_done ? -1 : 0, _done ? -1 : Mathf.Min(_last.Count, Recent - 1));
        }

        public override bool Over => _done;
        public override void End() { _live = false; if (Arcade != null) Arcade.Audio.GameBed(null); }

        public override void Final(out string title, out string body)
        {
            bool won = _level > 100f;
            title = won ? "Y O U   C R U S H E D   I T" : "B O O E D   O F F";
            body = (won ? "The crowd is yours." : "The crowd wasn't impressed.") + "\n" + _made + " of " + _putts + " putts holed";
        }
    }
}
