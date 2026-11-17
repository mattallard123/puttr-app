// On Fire: the Puttr app's streak game.
//
// The rules are the app's (components/games/ArcadeStreak.js):
//
//   - the screen calls a distance, 3 to 11 feet, and a tee: left, center or
//     right. Both are picked at random for every putt
//   - a holed putt adds its distance in feet to the score
//   - one miss ends the game. So does a "back door": a ball that drops in
//     more than 90 degrees round from the side it was hit from
//   - the score is the total feet holed in one unbroken run. One Player
//
// Here the run is a fire. It starts as a few embers in a steel bowl and
// every putt holed feeds it, until it is taller than the score it burns
// behind. A miss puts it out.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class OnFireMode : StageMode
    {
        public override string Name => "On Fire";
        public override bool DealsTee => false;
        public override int PuttFeet => _feet; // puttr-app-2
        public override int PuttTee => _tee;
        public override string Blurb => "Hole it from where the screen says.\nEvery make feeds the fire.\nOne miss and it is out.";
        public override GameAudio.Scene Sound => GameAudio.Scene.Fire;
        public override int Score(int player) => _score;
        public override string ScoreWord(int n) => n == 1 ? "foot" : "feet";
        public override string ScoreUnit => "ft";
        public override string Rule => "Hole it from where the screen says. One miss and the fire is out.";
        public override string Icon => "Trophy";
        public override Color Accent => Make.Hex(0xFF6B35);
        public override string[] Tags => new[] { "Until you miss", FixedFeet > 0 ? FixedFeet + " ft" : "3 to 11 ft" };

        /// <summary>puttr-app-2: a fixed distance for every putt (3 to 11 ft, the old streak games), or 0 for the
        /// usual mix. The tee still changes every putt.</summary>
        public static int FixedFeet;
        public override int MaxPlayers => 1;
        public override bool HasLevels => false; // holing it is the whole game
        public override string Tee => null;       // it calls the distance and the tee itself
        public override string[] HowTo => new[]
        {
            "The screen calls a distance from 3 to 11 feet and a tee: left, center or right. Putt from there.",
            "Every putt you hole adds its distance in feet to your score, and the fire grows.",
            "One miss ends the run. A putt that drops in the back of the hole does not count either.",
        };

        const int MinFeet = 3, MaxFeet = 11;
        const string BestKey = "puttr.arcade.onfire.best";
        // A best for each fixed distance, apart from the mixed one.
        static string BestKeyNow => FixedFeet > 0 ? BestKey + "." + FixedFeet : BestKey;
        static readonly string[] TeeNames = { "Left", "Center", "Right" };
        static readonly Color Ember = Make.Hex(0xFF6B35);

        // puttr-app-2: upright, the camera stands further back and looks at a tall slice: where to
        // putt from at the top, the run under it, then the score over the fire.
        public override bool HasPortrait => true;
        public override Vector3 CameraPosition => Portrait ? At(0f, 150f, -520f) : At(0f, 64f, -330f);
        public override Vector3 CameraLook => Portrait ? At(0f, 150f, 0f) : At(0f, 62f, 0f);
        public override float CameraFov => Portrait ? 49f : 30f;
        Transform _fromGroup, _runGroup;
        bool _stacked;

        // Wide: where to putt from on the left, the run on the right. Upright: both moved to the
        // middle, one above the other. The offsets carry each block from its wide place.
        void Arrange(bool force = false)
        {
            if (_fromGroup == null || (!force && _stacked == Portrait)) return;
            _stacked = Portrait;
            const float side = 122f;
            _fromGroup.localPosition = _stacked ? new Vector3(side, 194f, 0f) : Vector3.zero;
            _runGroup.localPosition = _stacked ? new Vector3(-side, 95f, 0f) : Vector3.zero;
            // Upright, a unit of the scene is under two points on a phone: the captions are drawn
            // at 6 units (about 11 points) or they cannot be read. The three tee names would run
            // into each other at that size, so the line under them says which tee instead.
            foreach (Caption caption in _captions)
                caption.Text.transform.localScale = caption.Scale * (_stacked ? Mathf.Max(1f, UprightCaption / caption.Height) : 1f);
            foreach (TextMesh name in _teeText) if (name != null) name.gameObject.SetActive(!_stacked);
            if (_teeCaption != null) _teeCaption.transform.localPosition = new Vector3(-side, _stacked ? 27f : 19f, 0f);
        }

        const float UprightCaption = 6f;
        struct Caption { public TextMesh Text; public Vector3 Scale; public float Height; }
        readonly List<Caption> _captions = new List<Caption>();

        // A small label, remembered so it can be drawn larger when the phone is upright.
        TextMesh Small(Transform parent, string text, Vector3 position, float height, Color color)
        {
            TextMesh mesh = Words(parent, text, position, height, color);
            _captions.Add(new Caption { Text = mesh, Scale = mesh.transform.localScale, Height = height });
            return mesh;
        }
        // The card: the fire, filling it.
        public override Vector3 CardFrom => At(0f, 78f, -330f);
        public override Vector3 CardLook => At(0f, 74f, 0f);
        public override float CardFov => 21f;
        public override float ShadowDistance => 700f;

        ParticleSystem _flames, _tongues, _embers, _smoke;
        Light _glow;
        Transform _readout;
        Material _coals, _logEnds;
        TextMesh _scoreText, _feetText, _rowText, _bestText, _teeCaption;
        readonly GameObject[] _teeOn = new GameObject[3];
        readonly TextMesh[] _teeText = new TextMesh[3];

        int _score, _streak, _best, _feet, _tee;
        bool _over;
        float _heat, _heatWanted, _flare;
        readonly System.Random _rng = new System.Random();

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // Almost no light but the fire's own.
            Sun(Root, new Vector3(35f, -160f, 0f), new Color(0.55f, 0.7f, 1f), 0.12f).shadows = LightShadows.None;

            Props.Concrete(out Texture2D slab, out Texture2D pits);
            Material ground = Make.Lit(new Color(0.3f, 0.3f, 0.31f), 0.5f, 0f, slab, pits, 1f);
            ground.SetTextureScale("_BaseMap", new Vector2(10f, 10f));
            Shape(PrimitiveType.Cube, "Ground", Root, new Vector3(0, -2f, 300f), new Vector3(2400f, 4f, 1600f), ground);

            // A steel fire bowl on three legs.
            Material steel = Make.Lit(new Color(0.1f, 0.085f, 0.075f), 0.42f, 0.85f);
            var bowl = new List<Vector2>
            {
                new Vector2(0.01f, 5f), new Vector2(20f, 5.4f), new Vector2(34f, 9f), new Vector2(43f, 15f), new Vector2(46f, 20f),
            };
            Make.MeshObject("Bowl", Root, Make.Lathe(bowl, 56), steel);
            var inside = new List<Vector2>
            {
                new Vector2(45f, 20f), new Vector2(42f, 16f), new Vector2(33f, 10.5f), new Vector2(20f, 7f), new Vector2(0.01f, 6.6f),
            };
            Mesh lining = Make.Lathe(inside, 56);
            int[] tris = lining.triangles;
            for (int i = 0; i < tris.Length; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
            lining.triangles = tris;
            lining.RecalculateNormals();
            Make.MeshObject("Bowl lining", Root, lining, Make.Lit(new Color(0.03f, 0.028f, 0.026f), 0.2f, 0.4f));
            GameObject rim = Make.MeshObject("Rim", Root, Props.Torus(45.5f, 1.3f), steel);
            rim.transform.localPosition = new Vector3(0, 20f, 0);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Props.Rod("Leg", Root, dir * 30f + Vector3.up * 8f, dir * 38f, 1.8f, steel);
            }

            // Coals, and split logs stacked across each other. Both glow hotter as the fire grows.
            _coals = Make.Emissive(new Color(0.02f, 0.01f, 0.008f), Ember * 0.4f, 0.1f);
            GameObject bed = Make.MeshObject("Coals", Root, Make.Disc(31f), _coals);
            bed.transform.localPosition = new Vector3(0, 10.2f, 0);
            Material bark = Make.Lit(new Color(0.07f, 0.045f, 0.03f), 0.12f);
            _logEnds = Make.Emissive(new Color(0.05f, 0.02f, 0.01f), Ember * 0.6f, 0.1f);
            var rng = new System.Random(4);
            for (int i = 0; i < 7; i++)
            {
                float a = (i * 51f + 10f) * Mathf.Deg2Rad;
                float lift = 12f + (i % 3) * 5.5f;
                var dir = new Vector3(Mathf.Cos(a), 0.16f + (i % 2) * 0.22f, Mathf.Sin(a));
                Vector3 mid = new Vector3((float)rng.NextDouble() * 8f - 4f, lift, (float)rng.NextDouble() * 8f - 4f);
                float half = 20f + (float)rng.NextDouble() * 6f, thick = 3.6f + (float)rng.NextDouble() * 1.6f;
                Props.Rod("Log", Root, mid - dir * half, mid + dir * half, thick, bark);
                Shape(PrimitiveType.Sphere, "Log end", Root, mid - dir * half, Vector3.one * thick * 1.7f, _logEnds);
                Shape(PrimitiveType.Sphere, "Log end", Root, mid + dir * half, Vector3.one * thick * 1.7f, _logEnds);
            }

            BuildFire();

            _glow = World.NewLight(Root, "Fire light", LightType.Point, new Color(1f, 0.55f, 0.22f), 0f);
            _glow.transform.localPosition = new Vector3(0, 42f, -8f);
            _glow.range = 900f;
            _glow.shadows = LightShadows.Soft;

            // Everything written in the scene. It is put away while the home screen is over it.
            _readout = new GameObject("Readout").transform;
            _readout.SetParent(Root, false);

            // The score, in the middle of it all.
            _scoreText = Words(_readout, "0", new Vector3(0, 76f, -30f), 58f, Color.white, TextAnchor.MiddleCenter, 420);
            Small(_readout, "TOTAL FEET", new Vector3(0, 121f, -30f), 3.6f, new Color(1f, 1f, 1f, 0.6f));

            _fromGroup = new GameObject("Putt from").transform;
            _fromGroup.SetParent(_readout, false);
            _runGroup = new GameObject("The run").transform;
            _runGroup.SetParent(_readout, false);

            // Left: where to putt from.
            const float side = 122f;
            Color quiet = new Color(1f, 1f, 1f, 0.5f);
            Small(_fromGroup, "PUTT FROM", new Vector3(-side, 106f, 0), 3.2f, quiet);
            _feetText = Words(_fromGroup, "", new Vector3(-side, 82f, 0), 27f, Color.white, TextAnchor.MiddleCenter, 240);
            Small(_fromGroup, "FEET", new Vector3(-side, 60f, 0), 3.6f, quiet);
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector3(-side + (i - 1) * 20f, 42f, 0);
                Hoop("Tee", _fromGroup, at, 6.2f, 6.7f, Make.Glow(new Color(1f, 1f, 1f, 0.22f), false));
                Hoop("Tee dot", _fromGroup, at, 0f, 1.6f, Make.Glow(new Color(1f, 1f, 1f, 0.2f), false));
                var on = new GameObject("Tee on");
                on.transform.SetParent(_fromGroup, false);
                Hoop("Fill", on.transform, at, 0f, 6.2f, Make.Glow(new Color(0.176f, 0.549f, 0.235f, 0.45f), false));
                Hoop("Edge", on.transform, at, 6.1f, 6.9f, Make.Glow(Make.Bright, false));
                Hoop("Dot", on.transform, at + Vector3.back * 0.2f, 0f, 1.8f, Make.Glow(Color.white, false));
                _teeOn[i] = on;
                _teeText[i] = Words(_fromGroup, TeeNames[i].ToUpperInvariant(), at + Vector3.down * 11f, 2.2f, quiet);
            }
            _teeCaption = Small(_fromGroup, "", new Vector3(-side, 19f, 0), 2.8f, Make.Bright);

            // Right: the run, and the best there has been.
            Small(_runGroup, "IN A ROW", new Vector3(side, 106f, 0), 3.2f, quiet);
            _rowText = Words(_runGroup, "0", new Vector3(side, 82f, 0), 27f, Ember, TextAnchor.MiddleCenter, 240);
            Small(_runGroup, "PERSONAL BEST", new Vector3(side, 52f, 0), 2.8f, quiet);
            _bestText = Words(_runGroup, "0 ft", new Vector3(side, 41f, 0), 8f, new Color(1f, 0.72f, 0.2f));
            Arrange(true);
        }

        void BuildFire()
        {
            Material flame = Make.Glow(new Color(2.6f, 1.9f, 1.25f, 1f), true, Props.Flame(3));
            Material tongue = Make.Glow(new Color(2.2f, 1.5f, 0.9f, 1f), true, Props.Flame(11));
            Material spark = Make.Glow(new Color(3f, 2f, 1f, 1f), true, Make.SoftDot());
            Material soot = Make.Glow(new Color(0.03f, 0.03f, 0.035f, 0.55f), false, Props.Flame(19));

            // Yellow-white at the root, orange, then red as it thins.
            var burn = new Gradient();
            burn.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.72f), 0f), new GradientColorKey(new Color(1f, 0.62f, 0.16f), 0.35f), new GradientColorKey(new Color(0.9f, 0.24f, 0.05f), 0.75f), new GradientColorKey(new Color(0.35f, 0.06f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            var swell = new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.25f));

            _flames = NewFire("Flames", flame, 900, burn, swell, new Vector3(0, 13f, 0), 14f, 0.7f);
            _tongues = NewFire("Tongues", tongue, 500, burn, swell, new Vector3(0, 16f, 2f), 22f, 0.45f);

            // Sparks: small, quick, and thrown about by the heat.
            var glowing = new Gradient();
            glowing.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            _embers = NewFire("Embers", spark, 600, glowing, AnimationCurve.Linear(0, 1, 1, 0.4f), new Vector3(0, 18f, 0), 42f, 0.35f);
            ParticleSystem.MainModule em = _embers.main;
            em.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            em.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 3f);

            // And smoke above it, which is all that is left after a miss.
            var grey = new Gradient();
            grey.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.25f), new GradientAlphaKey(0f, 1f) });
            _smoke = NewFire("Smoke", soot, 300, grey, AnimationCurve.Linear(0, 0.6f, 1, 1.8f), new Vector3(0, 40f, 6f), 16f, 0.3f);
            ParticleSystem.MainModule sm = _smoke.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        }

        ParticleSystem NewFire(string name, Material material, int max, Gradient color, AnimationCurve size, Vector3 at, float turbulence, float spin)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.transform.localPosition = at;
            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 5f;
            shape.radius = 8f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            fade.color = color;
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, size);
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = turbulence;
            noise.frequency = 0.035f;
            noise.scrollSpeed = 0.9f;
            ParticleSystem.RotationOverLifetimeModule turn = ps.rotationOverLifetime;
            turn.enabled = true;
            turn.z = new ParticleSystem.MinMaxCurve(-spin, spin);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _score = 0;
            _streak = 0;
            _over = false;
            _best = PlayerPrefs.GetInt(BestKeyNow, 0);
            _heatWanted = HeatFor(0);
            _heat = 0f;
            Arcade.Hud.ClearBoard();
            _readout.gameObject.SetActive(true);
            foreach (ParticleSystem ps in new[] { _flames, _tongues, _embers, _smoke }) ps.Clear();
            NextPutt();
            if (!_quiet) { Arcade.Audio.Say("english_start_" + (1 + _rng.Next(4))); Call(); }
            Show();
        }

        // On the home screen, show it well alight.
        public override void Preview()
        {
            _quiet = true;
            Begin(1);
            _quiet = false;
            _heatWanted = _heat = HeatFor(7);
            _readout.gameObject.SetActive(false);
        }

        // How big the fire is after this many in a row: it never stops growing, but slows.
        static float HeatFor(int streak) => streak == 0 ? 0.06f : 1f - 0.9f * Mathf.Exp(-streak / 4.2f);

        // The announcer calls the putt, as in the app: "eight feet", "left tee".
        void Call() => Arcade.Audio.Say("english_distance_" + _feet + "_feet", "english_tee_" + TeeNames[_tee - 1].ToLowerInvariant());

        bool _quiet;

        void NextPutt()
        {
            _feet = FixedFeet >= MinFeet && FixedFeet <= MaxFeet ? FixedFeet : MinFeet + _rng.Next(MaxFeet - MinFeet + 1);
            _tee = 1 + _rng.Next(3);
        }

        // puttr-app-2: the scene already says where to putt from, large. In the app the line along
        // the bottom would only say it again.
        public override string Prompt => Arcade.Embedded ? "" : "Putt from " + _feet + " feet   ·   " + TeeNames[_tee - 1] + " tee";

        public override IEnumerator Play(PuttResult putt)
        {
            Hud hud = Arcade.Hud;
            // In from behind is no putt at all: more than a quarter turn from
            // the line the tee gives.
            bool backDoor = putt.Made && putt.HasEntry && Mathf.Abs(Mathf.DeltaAngle(PuttrAnalysis.ExpectedEntryAngle(_feet, _tee), putt.EntryAngle)) >= 90f;

            if (putt.Made && !backDoor)
            {
                _score += _feet;
                _streak++;
                _heatWanted = HeatFor(_streak);
                _flare = 1f;
                ShowPutt(putt, "+" + _feet + " ft", Make.Bright);
                hud.Banner("+" + _feet + " FT", _streak == 1 ? "The fire is lit" : _streak + " in a row", Make.Bright, 1.8f);
                Arcade.Audio.Whoosh();
                Arcade.Audio.Spare();
                if (_streak % 5 == 0) { Arcade.Audio.Coin(); Arcade.Audio.Say("english_streak_" + (1 + _rng.Next(7))); }
                else if (_rng.Next(3) == 0) Arcade.Audio.Say("english_made_" + (1 + _rng.Next(8)));
                _embers.Emit(40 + _streak * 12);
                Arcade.Shake(0.25f);
                if (_score > _best) { _best = _score; PlayerPrefs.SetInt(BestKeyNow, _best); PlayerPrefs.Save(); }
                NextPutt();
                Call();
                Show();
                yield return new WaitForSecondsRealtime(0.9f);
                Arcade.Shot("action");
                yield return new WaitForSecondsRealtime(0.9f);
                yield break;
            }

            // Out.
            _over = true;
            _heatWanted = 0f;
            _smoke.Emit(60);
            if (backDoor)
            {
                ShowPutt(putt, "Back door", Make.Amber);
                hud.Banner("BACK DOOR", "Putts that enter the back of the hole don't count", Make.Amber, 2.6f);
            }
            else
            {
                ShowPutt(putt, !putt.Launch.Reached ? "Short" : "Missed", Make.Red);
                hud.Banner("STREAK OVER", !putt.Launch.Reached ? "Left short from " + _feet + " feet" : "Missed from " + _feet + " feet", Make.Red, 2.4f);
            }
            Arcade.Audio.Douse();
            if (backDoor) Arcade.Audio.Buzzer(); else Arcade.Audio.Say("english_miss_" + (1 + _rng.Next(8)));
            yield return new WaitForSecondsRealtime(1f);
            Arcade.Shot("result");
            yield return new WaitForSecondsRealtime(1.2f);
        }

        public override bool Over => _over;

        public override void Final(out string title, out string body)
        {
            title = _score + "  F E E T";
            body = (_streak == 1 ? "1 putt" : _streak + " putts") + " in a row\n" + (_score > 0 && _score >= _best ? "A new personal best\n" : "Personal best: " + _best + " feet\n");
        }

        void Show()
        {
            _scoreText.text = _score.ToString();
            _rowText.text = _streak.ToString();
            _bestText.text = _best + " ft";
            _feetText.text = _feet.ToString();
            for (int i = 0; i < 3; i++)
            {
                bool on = i == _tee - 1;
                _teeOn[i].SetActive(on);
                _teeText[i].color = on ? Make.Bright : new Color(1f, 1f, 1f, 0.35f);
            }
            _teeCaption.text = TeeNames[_tee - 1].ToUpperInvariant() + " TEE";
        }

        // ── The fire ────────────────────────────────────────────────────────

        public override void Tick()
        {
            Arrange(); // puttr-app-2: follows the phone as it is turned
            float dt = Time.deltaTime;
            // It catches quickly and dies slowly.
            _heat = Mathf.MoveTowards(_heat, _heatWanted, dt * (_heatWanted > _heat ? 0.5f : 0.9f));
            _flare = Mathf.MoveTowards(_flare, 0f, dt * 0.9f);
            float h = Mathf.Clamp01(_heat + _flare * 0.12f * Mathf.Clamp01(_heat * 4f));
            Arcade.Audio.SetLevel(h);

            Burn(_flames, Mathf.Lerp(0f, 190f, Mathf.Sqrt(h)), Mathf.Lerp(10f, 50f, h), Mathf.Lerp(16f, 100f, h), Mathf.Lerp(0.5f, 1.15f, h), Mathf.Lerp(5f, 30f, h));
            float tall = Mathf.Clamp01((h - 0.3f) / 0.7f);
            Burn(_tongues, tall * 70f, Mathf.Lerp(22f, 60f, tall), Mathf.Lerp(90f, 175f, tall), 1.25f, Mathf.Lerp(8f, 22f, tall));
            ParticleSystem.EmissionModule embers = _embers.emission;
            embers.rateOverTime = h < 0.02f ? 0f : Mathf.Lerp(3f, 70f, h);
            ParticleSystem.MainModule em = _embers.main;
            em.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Lerp(20f, 60f, h), Mathf.Lerp(45f, 170f, h));
            ParticleSystem.ShapeModule es = _embers.shape;
            es.radius = Mathf.Lerp(6f, 28f, h);
            es.angle = 14f;
            Burn(_smoke, _over ? Mathf.Max(0f, 14f - (Time.time % 1000f) * 0f) * Mathf.Clamp01(_heat * 6f + 0.25f) : Mathf.Lerp(0f, 9f, h), Mathf.Lerp(30f, 70f, h), Mathf.Lerp(22f, 50f, h), 3.5f, Mathf.Lerp(8f, 26f, h));

            // Firelight never holds still.
            float flicker = 0.78f + 0.3f * Mathf.PerlinNoise(Time.time * 7.3f, 0.5f) + 0.12f * Mathf.PerlinNoise(Time.time * 19f, 3.1f);
            _glow.intensity = Mathf.Lerp(0f, 42000f, h) * flicker + 900f * Mathf.Clamp01(h * 10f);
            _glow.transform.localPosition = new Vector3(Mathf.PerlinNoise(Time.time * 3f, 9f) * 8f - 4f, Mathf.Lerp(30f, 70f, h), -10f);
            _coals.SetColor("_EmissionColor", Ember * Mathf.Lerp(0.25f, 5f, h) * flicker);
            _logEnds.SetColor("_EmissionColor", Ember * Mathf.Lerp(0.15f, 1.1f, h) * flicker);
            _scoreText.color = Color.Lerp(Color.white, new Color(1f, 0.93f, 0.72f), h);
        }

        static void Burn(ParticleSystem ps, float rate, float size, float speed, float life, float radius)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            ParticleSystem.MainModule main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed * 1.1f);
            if (ps.name != "Smoke" && ps.name != "Embers") main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.radius = radius;
        }

        public override void End()
        {
            _heatWanted = 0f;
            _heat = 0f;
            foreach (ParticleSystem ps in new[] { _flames, _tongues, _embers, _smoke }) ps.Clear();
        }
    }
}
