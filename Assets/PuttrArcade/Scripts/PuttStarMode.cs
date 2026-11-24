// Putt Star (puttr-app Rock.js, rebuilt on a stage): you are the lead singer. Every putt you hole wins the crowd
// over; every miss gets you booed.
//
// The crowd starts at 50. A make (in through the front of the cup) adds its distance; a miss, or a make through the
// back, takes its distance away. Easy doubles what a make is worth and halves a miss; Hard halves a make and doubles a
// miss. Past 100 the crowd is yours and the show is a hit. Below 0 you're booed off. Three makes in a row sets the
// stage on fire. A new random putt every time, 3 to 11 feet, any tee.
//
// The set is an arena show: a black deck under a ground-support truss rig, an LED wall behind the band showing the
// crowd meter, moving-head beams in the haze, flown PA, wedges, a drum riser and backline, pyro, CO2 and confetti
// for the big moments, and a crowd of about two thousand on the floor and in the stands, backs to the camera, arms
// going up as they get into it, phones held high, turning on you when you miss. The lighting runs to the music
// (what the listener hears, and a 128 bpm clock). Built to be cheap on a phone: the set is merged into one mesh per
// material (PuttStarStage), the band is four skinned meshes (PuttStarRig), the crowd is two meshes animated in its
// shader (PuttStarCrowd), and every beam, flare and pool of light is one dynamic mesh (PuttStarLights). Four real
// lights. Sounds are the app's own (rock_*, concert_*, fire_whoosh, boom, synth_whoosh).
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class PuttStarMode : StageMode
    {
        public override string Name => "Putt Star";
        public override bool Branded => true;
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
        // puttr-app-2: the level is the crowd's mood (Easy doubles a make and halves a miss, Hard the reverse), as in
        // the app's Rock.js, which never widened the hole: the putt is judged as it really was at every level.
        public override bool OwnLevels => true;
        public override bool ShowsAim => false;
        public override bool HasPortrait => true;
        public override string Tee => null; // the tee sign says it
        // puttr-app-2: the concert is its own crowd (concert_loop, from Begin), so the arcade's stadium crowd no longer runs under it.
        public override GameAudio.Scene Sound => GameAudio.Scene.Quiet;
        // puttr-app-2: the night ends on the crowd: a cheer for a hit show, booed off the stage (the arcade's game-over until recorded).
        public override string EndSlot => _won ? "concert_cheer" : "puttstar_booed_off";
        public override int Score(int player) => Mathf.Clamp(Mathf.RoundToInt(_level), 0, 100);

        // From the back of the floor, over the crowd's heads: the whole rig and the wall in an upright phone, the PA
        // and the stands too on a wide screen.
        public override Vector3 CameraPosition => Portrait ? At(0f, 36f, -105f) : At(0f, 44f, -124f);
        public override Vector3 CameraLook => Portrait ? At(0f, 40f, 40f) : At(0f, 48f, 40f);
        public override float CameraFov => Portrait ? 58f : 44f;
        public override Vector3 CardFrom => At(0f, 31f, -150f);
        public override Vector3 CardLook => At(0f, 58f, 40f);
        public override float CardFov => 40f;
        public override float ShadowDistance => 420f;

        const float S = StarStage.Deck, Start = 50f, Bpm = 128f;
        const int Recent = 10;

        StarStage _set;
        StarCrowd _crowd;
        StarLights _lights;
        StarRig _singer, _guitar, _bass, _drummer;
        Light _sun, _follow, _back, _wash;
        ParticleSystem[] _pyro, _co2;
        ParticleSystem _confetti;
        readonly List<Transform> _thrown = new List<Transform>();
        readonly Vector3[] _throwFrom = new Vector3[7], _throwTo = new Vector3[7];
        readonly System.Random _dice = new System.Random();
        readonly float[] _samples = new float[256];

        float _level = Start, _shown = Start, _boo, _cheer, _throwT = -1f;
        int _streak, _putts, _made;
        readonly List<bool> _last = new List<bool>();
        bool _onFire, _done, _live, _won, _pose;

        // The show: what the lights are doing.
        float _flash, _dip, _pyroLeft, _co2Left, _confettiLeft, _strobeLeft, _env, _agc = 0.02f, _lastPuff;
        Color _main = new Color(1f, 0.2f, 0.75f), _second = new Color(0.2f, 0.7f, 1f);
        static readonly Color[] Moods =
        {
            new Color(1f, 0.18f, 0.72f), new Color(0.18f, 0.68f, 1f),
            new Color(0.3f, 0.32f, 1f), new Color(1f, 0.58f, 0.14f),
            new Color(0.48f, 0.83f, 0.27f), new Color(0.85f, 0.92f, 1f),
            new Color(1f, 0.32f, 0.1f), new Color(0.62f, 0.2f, 1f),
        };
        static readonly int StateId = Shader.PropertyToID("_State"), RimId = Shader.PropertyToID("_Rim"), SweepId = Shader.PropertyToID("_Sweep"),
            SweepColorId = Shader.PropertyToID("_SweepColor"), ColorId = Shader.PropertyToID("_Color"), EmissionId = Shader.PropertyToID("_EmissionColor"),
            MainTexId = Shader.PropertyToID("_MainTex");

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            _crowd = new StarCrowd();
            _set = StarStage.Build(Root, _crowd);
            _crowd.Build(Root, _set.Atlas);

            // The band.
            _singer = StarRig.Build("Singer", Root, new Vector3(0, S, 13f), new StarRig.Look
            {
                Skin = StarRig.SkinLight, Shirt = StarRig.Leather, Sleeve = StarRig.Leather, Fore = StarRig.Leather, Pants = StarRig.Black,
                Hair = StarRig.HairDark, Shoes = StarRig.Black, Shades = true, Width = 0.98f,
            }, 1.04f);
            _singer.Mic();
            _singer.Finish();
            _guitar = StarRig.Build("Guitar", Root, new Vector3(-30f, S, 28f), new StarRig.Look
            {
                Skin = StarRig.SkinMid, Shirt = StarRig.RedShirt, Sleeve = StarRig.RedShirt, Fore = StarRig.SkinMid, Pants = StarRig.Denim,
                Hair = StarRig.HairBlond, Shoes = StarRig.Black, LongHair = true, Width = 0.95f,
            });
            _guitar.Guitar(StarRig.Sunburst, StarRig.Cream, 10.5f);
            _guitar.Finish();
            _bass = StarRig.Build("Bass", Root, new Vector3(30f, S, 28f), new StarRig.Look
            {
                Skin = StarRig.SkinDeep, Shirt = StarRig.WhiteTee, Sleeve = StarRig.WhiteTee, Fore = StarRig.SkinDeep, Pants = StarRig.Black,
                Hair = StarRig.HairDark, Shoes = StarRig.Black, Beard = true, Width = 1.06f,
            });
            _bass.Guitar(StarRig.BassBody, StarRig.Grey, 13f);
            _bass.Finish();
            _drummer = StarRig.Build("Drums", Root, new Vector3(0, StarStage.RiserTop - 4.8f, 70.5f), new StarRig.Look
            {
                Skin = StarRig.SkinLight, Shirt = StarRig.Grey, Sleeve = StarRig.SkinLight, Fore = StarRig.SkinLight, Pants = StarRig.Black,
                Hair = StarRig.HairRed, Shoes = StarRig.Black, Beard = true, Width = 1.04f,
            });
            _drummer.Sticks();
            _drummer.Finish();
            foreach (StarRig r in new[] { _guitar, _bass, _drummer }) r.Root.localRotation = Quaternion.identity;

            // The light show.
            var beams = new List<StarLights.Beam>();
            foreach (float x in StarStage.FanX) beams.Add(new StarLights.Beam { From = new Vector3(x, StarStage.FanY, StarStage.FanZ), Length = 175f, Near = 1.6f, Far = 24f, Flare = 7f, Group = 0 });
            foreach (float x in StarStage.FrontX) beams.Add(new StarLights.Beam { From = new Vector3(x, StarStage.FrontY, StarStage.FrontZ), Length = 120f, Near = 1.4f, Far = 17f, Flare = 5.5f, Group = 1, Pool = true });
            foreach (float x in StarStage.BackX) beams.Add(new StarLights.Beam { From = new Vector3(x, StarStage.BackY, StarStage.BackZ), Length = 300f, Near = 1.6f, Far = 26f, Flare = 6f, Group = 2 });
            beams.Add(new StarLights.Beam { From = new Vector3(0f, 150f, -380f), Length = 420f, Near = 3f, Far = 15f, Flare = 0f, Group = 3, Pool = true });
            for (int s = -1; s <= 1; s += 2) beams.Add(new StarLights.Beam { From = new Vector3(s * 102f, StarStage.TrussY - 7f, -1f), Length = 140f, Near = 1.5f, Far = 18f, Flare = 6f, Group = 4, Pool = true });
            _lights = new StarLights(Root, beams.ToArray(), StarStage.Blinders, _set.Atlas, S);

            // Effects for the big moments.
            Texture2D soft = Make.SoftDot();
            Material fire = Make.Glow(new Color(1f, 0.62f, 0.28f) * 2.4f, true, soft);
            fire.renderQueue = 3012;
            Material gas = Make.Glow(new Color(0.92f, 0.95f, 1f, 0.5f), false, soft);
            gas.renderQueue = 3012;
            _pyro = new ParticleSystem[StarStage.PyroX.Length];
            for (int i = 0; i < _pyro.Length; i++) _pyro[i] = Jet("Pyro", new Vector3(StarStage.PyroX[i], S + 1.8f, 2.6f), fire, true);
            _co2 = new ParticleSystem[2];
            for (int i = 0; i < 2; i++) _co2[i] = Jet("CO2", new Vector3(i == 0 ? -30f : 30f, S + 1f, 3f), gas, false);
            _confetti = Confetti();

            // What they throw when they don't like it.
            Material red = Make.Lit(new Color(0.85f, 0.12f, 0.1f), 0.7f), cup = Make.Lit(new Color(0.9f, 0.9f, 0.9f), 0.4f);
            for (int i = 0; i < 7; i++)
            {
                Transform t = Shape(i % 3 == 2 ? PrimitiveType.Cylinder : PrimitiveType.Sphere, "Thrown", Root, Vector3.zero, i % 3 == 2 ? new Vector3(1.3f, 1.6f, 1.3f) : Vector3.one * 1.7f, i % 3 == 2 ? cup : red).transform;
                t.gameObject.SetActive(false);
                _thrown.Add(t);
            }

            // Four real lights, none casting shadows: a dim key from behind the rig, the follow spot on the singer
            // from the back of the hall, a coloured wash from the front truss, and the wall's glow behind the band.
            _sun = Sun(Root, new Vector3(42f, 180f, 0f), new Color(0.55f, 0.5f, 0.85f), 0.45f);
            _sun.shadows = LightShadows.None; // a dim backlight: its shadows hardly show, and a phone is better off without the pass
            _follow = World.NewLight(Root, "Follow spot", LightType.Spot, new Color(1f, 0.9f, 0.78f), 95000f);
            _follow.transform.localPosition = new Vector3(0, 150f, -380f);
            _follow.spotAngle = 8f;
            _follow.innerSpotAngle = 4f;
            _follow.range = 900f;
            _follow.shadows = LightShadows.None;
            _wash = World.NewLight(Root, "Wash", LightType.Spot, Color.white, 30000f);
            _wash.transform.localPosition = new Vector3(0, StarStage.TrussY - 6f, -6f);
            _wash.transform.localRotation = Quaternion.LookRotation(new Vector3(0, S, 40f) - _wash.transform.localPosition);
            _wash.spotAngle = 95f;
            _wash.range = 300f;
            _wash.shadows = LightShadows.None;
            _back = World.NewLight(Root, "Wall glow", LightType.Point, _main, 60000f);
            _back.transform.localPosition = new Vector3(0, S + 34f, 74f);
            _back.range = 240f;
            _back.shadows = LightShadows.None;
        }

        ParticleSystem Jet(string name, Vector3 at, Material mat, bool fire)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = fire ? new ParticleSystem.MinMaxCurve(0.5f, 0.8f) : new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
            main.startSpeed = fire ? new ParticleSystem.MinMaxCurve(85f, 115f) : new ParticleSystem.MinMaxCurve(120f, 150f);
            main.startSize = fire ? new ParticleSystem.MinMaxCurve(3.5f, 6.5f) : new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 220;
            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = fire ? 4f : 7f;
            shape.radius = 0.8f;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = fire ? 20f : 14f;
            drag.dampen = fire ? 0.06f : 0.07f;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            if (fire)
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.3f), new GradientColorKey(new Color(0.75f, 0.12f, 0.03f), 0.75f), new GradientColorKey(Color.black, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, fire ? 2.1f : 4.5f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        ParticleSystem Confetti()
        {
            var go = new GameObject("Confetti");
            go.transform.SetParent(Root, false);
            go.transform.localPosition = new Vector3(0, 95f, -30f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var g = new Gradient { mode = GradientMode.Fixed };
            g.SetKeys(new[]
            {
                new GradientColorKey(new Color(1f, 0.82f, 0.3f), 0.2f), new GradientColorKey(new Color(0.95f, 0.95f, 1f), 0.4f),
                new GradientColorKey(new Color(1f, 0.25f, 0.7f), 0.6f), new GradientColorKey(Make.Bright, 0.8f), new GradientColorKey(new Color(0.3f, 0.75f, 1f), 1f),
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 1800;
            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(250f, 100f, 150f);
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
            vel.y = new ParticleSystem.MinMaxCurve(-15f, -10f);
            vel.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = spin.y = spin.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.04f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var r = go.GetComponent<ParticleSystemRenderer>();
            Material paper = Make.Glow(Color.white, false, StarKit.White());
            paper.renderQueue = 3012;
            r.sharedMaterial = paper;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.World;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ── The show ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _level = _shown = Start;
            _streak = _putts = _made = 0;
            _last.Clear();
            _onFire = _done = _won = false;
            _boo = _cheer = 0f;
            _throwT = -1f;
            foreach (Transform t in _thrown) t.gameObject.SetActive(false);
            _flash = _dip = _pyroLeft = _co2Left = _confettiLeft = _strobeLeft = 0f;
            StopEffects();
            if (_set != null) _set.Shout.text = "";
            if (Arcade != null && Arcade.Hud != null)
            {
                Arcade.Hud.BuildBoard(new List<string> { Arcade.PlayerName(0) }, Recent, "CROWD", false, "LAST 10", 30f);
                Arcade.Hud.SetTotal(0, (int)_level);
                Arcade.Hud.SetTurn(0, 0);
                Deal();
                Arcade.Audio.Clip("rock_horn");
                // puttr-app-2 (Matt, 2026-11-21): the band plays a song, its tempo following the crowd (Tick), with the
                // crowd's own noise low under it.
                _song?.Dispose();
                StopPreview();
                _song = new PuttStarSong(Root);
                _song.Genre = EmbeddedGenre;
                _form = 0;
                _song.Bpm = SongBpm(_level);
                // The band is the loudest thing in the hall; the crowd sits under it (Matt, 2026-11-23).
                _song.Volume = SongVolume;
                _song.Play();
                Arcade.Audio.GameBed("concert_loop", 0.05f);
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

        public override void PoseForCard()
        {
            _pose = true;
            _level = _shown = 96f;
            _cheer = 1f;
            _flash = 0f;
            foreach (ParticleSystem p in _pyro) Rate(p, 140f);
            foreach (ParticleSystem p in _co2) Rate(p, 0f);
            Rate(_confetti, 260f);
            _set.Shout.text = "ENCORE!";
            _set.Shout.color = Make.Bright;
        }

        public override void AfterCard()
        {
            _pose = false;
            _level = _shown = Start;
            _cheer = _flash = 0f;
            StopEffects();
            _set.Shout.text = "";
        }

        void StopEffects()
        {
            if (_pyro == null) return;
            foreach (ParticleSystem p in _pyro) { Rate(p, 0f); p.Clear(); }
            foreach (ParticleSystem p in _co2) { Rate(p, 0f); p.Clear(); }
            Rate(_confetti, 0f);
            _confetti.Clear();
        }

        static void Rate(ParticleSystem p, float rate)
        {
            ParticleSystem.EmissionModule em = p.emission;
            em.rateOverTime = rate;
        }

        public override void Tick()
        {
            float t = Time.time, dt = Time.deltaTime;
            _shown = Mathf.MoveTowards(_shown, Mathf.Clamp(_level, 0f, 100f), dt * 40f);
            float energy = Mathf.Clamp01(_shown / 100f);
            _cheer = Mathf.Max(0f, _cheer - dt * 0.5f);
            _boo = Mathf.Max(0f, _boo - dt * 0.45f);
            float hype = Mathf.Clamp01(energy * energy + _cheer);
            bool lost = _done && !_won;
            if (_pose) hype = 1f;

            // The beat: the song's own (its tempo follows the crowd), else a 128 bpm clock; pushed by what the listener hears.
            if (_song != null && _song.Playing && _live && !_done) _song.Bpm = Mathf.MoveTowards(_song.Bpm, SongBpm(_shown), dt * 16f);
            float beats = _song != null && _song.Playing ? _song.Beat : t * Bpm / 60f;
            float onBeat = Mathf.Exp(-(beats - Mathf.Floor(beats)) * 5f);
            float heard = 0f;
            if (Arcade != null && !_pose)
            {
                AudioListener.GetOutputData(_samples, 0);
                float sum = 0f;
                for (int i = 0; i < _samples.Length; i++) sum += _samples[i] * _samples[i];
                float rms = Mathf.Sqrt(sum / _samples.Length);
                _agc = Mathf.Max(_agc * (1f - dt * 0.2f), rms, 0.02f);
                _env = Mathf.Max(rms / _agc, _env - dt * 3f);
                heard = Mathf.Clamp01(_env * 1.2f - 0.25f);
            }
            float pulse = Mathf.Clamp01(onBeat * (0.35f + 0.45f * hype) + heard * 0.5f);

            _flash = Mathf.Max(0f, _flash - dt * 1.6f);
            _dip = Mathf.Max(0f, _dip - dt * 0.6f);
            _strobeLeft = Mathf.Max(0f, _strobeLeft - dt);
            _lights.Blind = Mathf.Max(_pose ? 0.8f : 0f, _lights.Blind - dt * 1.4f);

            // Colour: a new pair of colours every eight bars; red for a boo, fire on a streak, everything for a win.
            int mood = (int)(beats / 32f) % (Moods.Length / 2);
            Color wantMain = Moods[mood * 2], wantSecond = Moods[mood * 2 + 1];
            if (_onFire) { wantMain = new Color(1f, 0.38f, 0.08f); wantSecond = new Color(1f, 0.75f, 0.25f); }
            if (_won) { int k = (int)(t * 2f) % (Moods.Length / 2); wantMain = Moods[k * 2]; wantSecond = Moods[k * 2 + 1]; }
            if (_boo > 0.05f) { wantMain = Color.Lerp(wantMain, new Color(1f, 0.06f, 0.04f), Mathf.Clamp01(_boo * 1.6f)); wantSecond = Color.Lerp(wantSecond, new Color(0.5f, 0.02f, 0.04f), Mathf.Clamp01(_boo * 1.6f)); }
            if (_pose) { wantMain = Moods[0]; wantSecond = Moods[1]; }
            if (lost) { wantMain = new Color(0.25f, 0.06f, 0.08f); wantSecond = new Color(0.12f, 0.12f, 0.2f); }
            float ck = _pose ? 1f : 1f - Mathf.Exp(-dt * 3f);
            _main = Color.Lerp(_main, wantMain, ck);
            _second = Color.Lerp(_second, wantSecond, ck);
            float strobe = _strobeLeft > 0f ? (Mathf.Repeat(t * 11f, 1f) < 0.35f ? 1f : 0.05f) : 0f;
            float level = (lost ? 0.15f : 1f) * (1f - _dip * 0.6f);

            // ── The crowd ──
            if (_crowd.People != null)
            {
                _crowd.People.SetVector(StateId, new Vector4(t, hype, _boo, pulse));
                _crowd.Phones.SetVector(StateId, new Vector4(t, hype, _boo, pulse));
                _crowd.People.SetColor(RimId, Color.Lerp(_main, _second, 0.35f) * (0.4f + _flash * 0.5f) * level);
            }

            // ── The band ──
            float groove = lost ? 0.1f : 0.35f + hype * 0.8f;
            float bt = beats * Mathf.PI, bob = Mathf.Abs(Mathf.Sin(bt));
            float slump = Mathf.Max(_boo, lost ? 1f : 0f);
            {
                StarRig r = _singer;
                float drop = r.Knees(5f + bob * 9f * groove);
                r.Root.localPosition = r.Home + new Vector3(Mathf.Sin(t * 0.55f) * 3f * (1f - slump), -drop, 0f);
                r.Hips.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.8f) * 10f, Mathf.Sin(bt) * 3f * groove);
                r.Chest.localRotation = Quaternion.Euler(4f + bob * 5f * groove + slump * 16f, Mathf.Sin(t * 1.1f) * 8f, 0f);
                r.Head.localRotation = Quaternion.Euler(Mathf.Lerp(10f - bob * 6f * groove, -24f, slump), Mathf.Sin(t * 0.9f) * 10f, 0f);
                float sing = 1f - slump * 0.7f;
                r.Arm(false, Mathf.Lerp(12f, 58f, sing), 4f, Mathf.Lerp(0f, 26f, sing), Mathf.Lerp(20f, 128f, sing));
                float raise = Mathf.Clamp01(_cheer * 1.6f) * (1f - slump);
                r.Arm(true, Mathf.Lerp(30f + bob * 22f * groove, 170f, raise) * (1f - slump * 0.8f), Mathf.Lerp(28f, 14f, raise), 0f, Mathf.Lerp(35f + bob * 20f * groove, 12f, raise));
            }
            Player(_guitar, bt, bob, groove, slump, -1f, t);
            Player(_bass, bt + 0.6f, bob, groove * 0.8f, slump, 1f, t);
            {
                StarRig r = _drummer;
                r.ThighL.localRotation = r.ThighR.localRotation = Quaternion.Euler(84f, 0f, 0f);
                r.ShinL.localRotation = r.ShinR.localRotation = Quaternion.Euler(-82f, 0f, 0f);
                float hitL = Mathf.Max(0f, Mathf.Sin(bt * 2f)), hitR = Mathf.Max(0f, Mathf.Sin(bt * 2f + Mathf.PI));
                float play = lost ? 0.15f : 1f;
                r.Chest.localRotation = Quaternion.Euler(10f + bob * 4f * groove, Mathf.Sin(t * 0.7f) * 6f, 0f);
                r.Head.localRotation = Quaternion.Euler(-bob * 14f * groove, 0f, 0f);
                r.Arm(true, 22f, 20f, 12f, 62f + hitL * 38f * play);
                r.Arm(false, 22f, 20f, 12f, 62f + hitR * 38f * play);
            }

            // ── The beams ──
            Vector3 eye, right, up;
            if (_pose) { eye = CardFrom; Quaternion q = Quaternion.LookRotation(CardLook - CardFrom); right = q * Vector3.right; up = q * Vector3.up; }
            else
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                eye = cam.transform.position; right = cam.transform.right; up = cam.transform.up;
            }
            eye = Root.InverseTransformPoint(eye);
            right = Root.InverseTransformDirection(right);
            up = Root.InverseTransformDirection(up);

            float sweepA = 0f, sweepB = 0f;
            StarLights.Beam[] beams = _lights.Beams;
            for (int i = 0; i < beams.Length; i++)
            {
                ref StarLights.Beam b = ref beams[i];
                Color c;
                float power;
                switch (b.Group)
                {
                    case 0: // the fan from the deck, behind the band
                    {
                        int k = i;
                        float across = (k - 4.5f) / 4.5f;
                        float spread = Mathf.Lerp(0.12f, 0.6f, hype) + Mathf.Sin(beats * Mathf.PI / 16f) * 0.14f;
                        float theta = across * spread + Mathf.Sin(t * (0.45f + hype) + k * 0.6f) * 0.12f * hype;
                        if (_won) theta = across * 0.65f + Mathf.Sin(t * 3f + k) * 0.3f;
                        b.Dir = new Vector3(Mathf.Sin(theta), Mathf.Cos(theta), -0.3f + 0.14f * Mathf.Sin(t * 0.6f + k));
                        c = k % 2 == 0 ? _main : _second;
                        power = 0.1f + 0.3f * hype + pulse * 0.15f;
                        break;
                    }
                    case 1: // the front truss, onto the band
                    {
                        int k = i - 10;
                        StarRig on = k % 4 == 0 ? _guitar : k % 4 == 1 ? _singer : k % 4 == 2 ? _bass : _singer;
                        Vector3 aim = on.Root.localPosition + new Vector3(Mathf.Sin(t * 0.6f + k) * 16f * hype, 0f, Mathf.Cos(t * 0.5f + k * 2f) * 12f * hype + 6f);
                        b.Dir = aim - b.From;
                        b.Length = b.Dir.magnitude;
                        c = Color.Lerp(k % 2 == 0 ? _second : _main, Color.white, 0.25f);
                        power = 0.14f + 0.22f * hype + pulse * 0.12f;
                        break;
                    }
                    case 2: // the back truss, sweeping the crowd
                    {
                        int k = i - 16;
                        float swing = Mathf.Sin(t * 0.42f * (1f + hype) + k * 1.3f);
                        Vector3 aim = new Vector3(swing * 140f, 16f, -70f + Mathf.Cos(t * 0.3f + k) * 34f);
                        if (_boo > 0.3f) aim = new Vector3(b.From.x * 0.4f, S, 40f);
                        b.Dir = aim - b.From;
                        c = k % 2 == 0 ? _main : _second;
                        power = 0.05f + 0.2f * hype + pulse * 0.1f;
                        Vector3 d = b.Dir.normalized;
                        float hitX = b.From.x + d.x * ((20f - b.From.y) / Mathf.Min(-0.05f, d.y));
                        if (k == 1) sweepA = hitX;
                        if (k == 4) sweepB = hitX;
                        break;
                    }
                    case 3: // the follow spot, from the back of the hall
                    {
                        Vector3 aim = _singer.Root.localPosition + new Vector3(0f, 2f, 0f);
                        b.Dir = aim - b.From;
                        b.Length = b.Dir.magnitude;
                        c = new Color(1f, 0.9f, 0.78f);
                        power = 0.055f + _cheer * 0.03f;
                        break;
                    }
                    default: // the towers, crossing onto centre stage
                    {
                        float s = Mathf.Sign(b.From.x);
                        Vector3 aim = new Vector3(-s * 18f + Mathf.Sin(t * 0.7f + s) * 20f * hype, S, 28f + Mathf.Cos(t * 0.5f) * 10f);
                        b.Dir = aim - b.From;
                        b.Length = b.Dir.magnitude;
                        c = _main;
                        power = 0.12f + 0.2f * hype;
                        break;
                    }
                }
                c = Color.Lerp(c, Color.white, _flash * 0.6f + strobe * 0.8f);
                power = (power + _flash * 0.4f) * level;
                if (_strobeLeft > 0f) power *= strobe;
                if (b.Group == 3) power = (0.055f + _cheer * 0.03f) * (lost ? 1.6f : 1f);
                c.a = Mathf.Clamp01(power);
                b.Color = c;
            }
            _lights.Draw(eye, right, up);
            if (_crowd.People != null)
            {
                _crowd.People.SetVector(SweepId, new Vector4(sweepA, sweepB, 16f, (0.25f + 0.6f * hype) * level));
                _crowd.People.SetColor(SweepColorId, Color.Lerp(_main, Color.white, 0.3f));
            }

            // ── Real lights ──
            _back.color = Color.Lerp(_main, _second, 0.3f);
            _back.intensity = 26000f * (0.45f + hype * 0.8f) * (0.65f + pulse * 0.7f + _flash) * level + strobe * 60000f;
            _wash.color = Color.Lerp(_second, Color.white, 0.45f);
            _wash.intensity = 9000f * (0.5f + hype * 0.6f) * level + _flash * 12000f;
            _follow.transform.localRotation = Quaternion.LookRotation(_singer.Root.localPosition + new Vector3(0, 15f, 0) - _follow.transform.localPosition);
            _sun.color = Color.Lerp(new Color(0.55f, 0.5f, 0.85f), _main, 0.35f);
            _sun.intensity = 0.4f * (lost ? 0.3f : 1f);

            // ── The wall and the strips ──
            float swing2 = Mathf.Sin(t * 0.3f) * 0.5f + 0.5f;
            Color wall = Color.Lerp(_main, _second, swing2) * (0.75f + 0.35f * pulse + _flash * 0.6f) * level;
            if (_boo > 0.05f && Mathf.Repeat(t * 3f, 1f) < 0.5f) wall *= 1f + _boo;
            wall.a = 1f;
            _set.Wall.SetColor(ColorId, wall);
            _set.Wall.SetTextureOffset(MainTexId, new Vector2(t * 0.025f * (1f + hype * 2f), 0f));
            float f = Mathf.Max(0.001f, energy);
            _set.MeterBar.localScale = new Vector3(StarStage.MeterW * f, StarStage.MeterH, 1f);
            _set.MeterBar.localPosition = new Vector3(StarStage.MeterW * 0.5f * (f - 1f), StarStage.MeterY, StarStage.WallZ - 0.2f);
            _set.MeterFill.SetTextureScale(MainTexId, new Vector2(f, 1f));
            _set.MeterFill.SetColor(ColorId, Color.white * (1.3f + pulse * 0.5f));
            _set.Strip.SetColor(EmissionId, _main * (1.2f + pulse * 2.2f) * level);
            _set.HazeBack.SetTextureOffset(MainTexId, new Vector2(t * 0.006f, t * 0.004f));
            _set.HazeFront.SetTextureOffset(MainTexId, new Vector2(-t * 0.005f, t * 0.003f));
            Color hz = Color.Lerp(_main, _second, 0.5f) * level;
            hz.a = 0.05f + 0.04f * hype;
            _set.HazeBack.SetColor(ColorId, hz);
            hz.a = 0.035f + 0.03f * hype;
            _set.HazeFront.SetColor(ColorId, hz);
            _set.Shout.transform.localPosition = new Vector3(0, StarStage.ShoutY + Mathf.Sin(t * 3f) * 0.8f, StarStage.WallZ - 0.3f);

            // ── Pyro, CO2, confetti ──
            if (!_pose)
            {
                // On fire, a short burst from the lip every eight beats.
                if (_onFire && !_done && beats - _lastPuff >= 8f && beats - Mathf.Floor(beats) < 0.2f) { _lastPuff = Mathf.Floor(beats); _pyroLeft = Mathf.Max(_pyroLeft, 0.28f); }
                _pyroLeft = Mathf.Max(0f, _pyroLeft - dt);
                _co2Left = Mathf.Max(0f, _co2Left - dt);
                _confettiLeft = Mathf.Max(0f, _confettiLeft - dt);
                foreach (ParticleSystem p in _pyro) Rate(p, _pyroLeft > 0f ? 150f : 0f);
                foreach (ParticleSystem p in _co2) Rate(p, _co2Left > 0f ? 110f : 0f);
                Rate(_confetti, _confettiLeft > 0f ? 320f : 0f);
            }

            // What they throw when they don't like it.
            if (_throwT >= 0f)
            {
                _throwT += dt / 0.9f;
                for (int i = 0; i < _thrown.Count; i++)
                {
                    float k = Mathf.Clamp01(_throwT - i * 0.06f);
                    _thrown[i].localPosition = Vector3.Lerp(_throwFrom[i], _throwTo[i], k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 40f;
                }
                if (_throwT > 2.2f) { foreach (Transform x in _thrown) x.gameObject.SetActive(false); _throwT = -1f; }
            }

            if (_live && !_done) Arcade.Audio.GameBed("concert_loop", 0.05f + 0.16f * hype);
            if (_song != null && _song.Playing && _done && !_won) _song.Stop();   // booed off: the band stops
        }

        // A guitarist: rocking on the beat, picking hand strumming, the other on the neck; heads down when it's loud.
        static void Player(StarRig r, float bt, float bob, float groove, float slump, float facing, float t)
        {
            float b = Mathf.Abs(Mathf.Sin(bt));
            float drop = r.Knees(8f + b * 9f * groove);
            r.Root.localPosition = r.Home + new Vector3(0f, -drop, 0f);
            r.Root.localRotation = Quaternion.Euler(0f, facing * 16f, 0f);
            r.Hips.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.6f + facing) * 6f, Mathf.Sin(bt) * 2f * groove);
            r.Chest.localRotation = Quaternion.Euler(8f + b * 8f * groove, 0f, 0f);
            r.Head.localRotation = Quaternion.Euler(-b * 30f * Mathf.Clamp01(groove - 0.4f) - 6f - slump * 10f, facing * 8f, 0f);
            float strum = Mathf.Sin(bt * 4f) * 12f * (1f - slump * 0.8f);
            r.Arm(false, 34f, 6f, 30f, 72f + strum);
            r.Arm(true, 50f, 42f, 30f, 58f);
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
                _form = Mathf.Max(_form, 0) + 1;
                _level += change;
                _cheer = 0.9f;
                _boo = 0f;
                _flash = 1f;
                _lights.Blind = 1f;
                Shout(_level > 85f ? "ENCORE!" : _streak >= 3 ? "ON FIRE!" : "YEAH!", Make.Bright);
                Arcade.Audio.Library("concert_cheer", 0.55f);
                Arcade.Celebrate(_level > 80f ? 90 : 40, At(0, S + 30f, 10f));
                ShowPutt(putt, "+" + Mathf.RoundToInt(change) + " crowd", Make.Bright);
                if (change >= 7f || _level > 75f) { _co2Left = 0.7f; Arcade.Audio.Library("puttstar_co2", "synth_whoosh", 0.55f); }   // puttr-app-2: its own CO2 jets
                if (_streak == 3) { _onFire = true; _pyroLeft = 1.3f; Arcade.Audio.Library("rock_screech"); Arcade.Audio.Library("puttstar_pyro", "fire_whoosh"); Arcade.Audio.Library("boom", 0.6f); Arcade.Hud.Banner("ON FIRE", "Three in a row. The place is shaking.", new Color(1f, 0.55f, 0.15f), 2f); }
                else
                {
                    if (_onFire) { _pyroLeft = 0.6f; Arcade.Audio.Library("puttstar_pyro", "fire_whoosh", 0.7f); }
                    Arcade.Hud.Banner("+" + Mathf.RoundToInt(change), "The crowd loves it", Make.Bright, 1.6f);
                }
            }
            else
            {
                _streak = 0;
                _form = Mathf.Min(_form, 0) - 1;
                _onFire = false;
                _pyroLeft = 0f;
                _level -= change;
                _boo = 1f;
                _cheer = 0f;
                _dip = 1f;
                Shout("BOOOO!", new Color(1f, 0.35f, 0.35f));
                Arcade.Audio.Library("concert_boo", 0.55f);
                Throw();
                Arcade.Shake(0.1f);
                ShowPutt(putt, "-" + Mathf.RoundToInt(change) + " crowd", Make.Amber);
                Arcade.Hud.Banner("-" + Mathf.RoundToInt(change), backDoor ? "In the back door. They saw that." : "They're booing", new Color(1f, 0.4f, 0.4f), 1.6f);
            }
            ShowBoard();
            if (_level > 100f)
            {
                _done = _won = true;
                Shout("YOU CRUSHED IT!", Make.Bright);
                _pyroLeft = 2.4f; _co2Left = 1.4f; _confettiLeft = 6f; _strobeLeft = 2.2f; _lights.Blind = 1.6f;
                Arcade.Audio.Clip("rock_horn"); Arcade.Audio.Library("rock_riff"); Arcade.Audio.Library("boom", 0.8f); Arcade.Audio.Library("puttstar_pyro", "fire_whoosh", 0.8f);   // puttr-app-2
                Arcade.Celebrate(160, At(0, S + 40f, 10f));
            }
            else if (_level < 0f) { _done = true; Shout("BOOED OFF", new Color(1f, 0.35f, 0.35f)); }
            else Deal();
            yield return new WaitForSecondsRealtime(_done ? 2.5f : 1.2f);
        }

        void Shout(string words, Color c)
        {
            if (_set == null) return;
            _set.Shout.text = words;
            _set.Shout.color = c;
        }

        // Tomatoes and cups, from the crowd onto the stage.
        void Throw()
        {
            for (int i = 0; i < _thrown.Count; i++)
            {
                _throwFrom[i] = new Vector3((float)(_dice.NextDouble() - 0.5) * 120f, 22f, -16f - (float)_dice.NextDouble() * 50f);
                _throwTo[i] = new Vector3((float)(_dice.NextDouble() - 0.5) * 70f, S + 1f, 6f + (float)_dice.NextDouble() * 20f);
                _thrown[i].localPosition = _throwFrom[i];
                _thrown[i].gameObject.SetActive(true);
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
        public override void End() { _live = false; StopEffects(); _song?.Stop(); if (Arcade != null) Arcade.Audio.GameBed(null); }

        PuttStarSong _song;
        const float SongVolume = 0.85f;

        // puttr-app-2 (Matt, 2026-11-23): picking the music on the game page plays it there and then, so the Player hears
        // what they chose. It stops when the page goes or the game starts (which plays its own).
        static PuttStarSong _preview;
        static GameObject _previewHome;
        public static void PreviewGenre(PuttStarGenre genre)
        {
            if (_previewHome == null)
            {
                _previewHome = new GameObject("Putt Star preview");
                Object.DontDestroyOnLoad(_previewHome);
            }
            StopPreview();   // only ever one song: the last one is gone before this one starts
            _preview = new PuttStarSong(_previewHome.transform);
            _preview.Genre = genre;
            _preview.Bpm = PuttStarSong.Tempo(genre, 50f, 0);
            _preview.Volume = SongVolume;
            _preview.Play();
        }
        public static void StopPreview() { _preview?.Dispose(); _preview = null; }
        /// <summary>puttr-app-2: the genre picked on the game page (GameLevels.Launch).</summary>
        public static PuttStarGenre EmbeddedGenre = PuttStarGenre.Rock;
        int _form;   // makes in a row above zero, misses in a row below
        /// <summary>The song's tempo: the crowd's level plus the current run, so every putt is heard.</summary>
        float SongBpm(float crowd) => PuttStarSong.Tempo(EmbeddedGenre, crowd, _form);

        public override void Final(out string title, out string body)
        {
            bool won = _level > 100f;
            title = won ? "Y O U   C R U S H E D   I T" : "B O O E D   O F F";
            body = (won ? "The crowd is yours." : "The crowd wasn't impressed.") + "\n" + _made + " of " + _putts + " putts holed";
        }
    }
}
