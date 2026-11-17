// Sound.
//
// Three kinds of it, layered:
//
//   - a bed for wherever the game is: a fire, a stadium crowd, deep space, an
//     arena, or the quiet of a room. It loops, and swells with what happens
//     (the fire with the streak, the crowd with a goal)
//   - things that happen: a kick, the net, a pin, an engine, a landing
//   - recordings from the Puttr app, where it has the real thing: the ball
//     dropping in the cup, applause, the crowd's groan, the back door buzzer,
//     a rack of pins going over, and the announcer who calls each On Fire
//     putt ("eight feet... left tee")
//
// Everything else is made at start-up from noise and sine waves, so the
// project carries no sound files but those few.
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class GameAudio
    {
        /// <summary>Where a game is set, as far as the ear is concerned.</summary>
        public enum Scene { Quiet, Alley, Table, Fire, Stadium, Space, Arena, Range, Ballpark }

        const int Rate = 44100;

        readonly AudioSource[] _voices = new AudioSource[14];
        readonly AudioSource _roll, _bed, _swell, _engine, _announcer;
        int _next;

        // Made here.
        readonly AudioClip _clack, _click, _thud, _tick, _uiMove, _softMiss;
        readonly AudioClip _rollLoop, _whirLoop, _engineLoop;
        readonly AudioClip _roomTone, _fireRoar, _fireCrackle, _crowd, _crowdRoar, _space, _shimmer;
        readonly AudioClip _kick, _net, _ping, _whoosh, _douse, _launch, _bell, _boom, _land, _crash;
        // Recorded.
        readonly AudioClip _cup, _applause, _groan, _buzzer, _pins, _coin, _over;
        readonly AudioClip[] _made = new AudioClip[4];
        readonly Dictionary<string, AudioClip> _spoken = new Dictionary<string, AudioClip>();
        readonly Queue<AudioClip> _saying = new Queue<AudioClip>();

        Scene _scene = Scene.Quiet;
        float _level, _excitement, _bedWanted, _swellWanted, _pinsAt = -10f, _engineSpeed;
        readonly System.Random _rng = new System.Random();
        // puttr-app-2: a game's own sounds from the Soundly library (SoundDrop slots), and its own ambience bed.
        readonly AudioSource _gameBed;
        readonly Dictionary<string, AudioClip> _slots = new Dictionary<string, AudioClip>();

        public GameAudio(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            for (int i = 0; i < _voices.Length; i++) _voices[i] = Source(go, false);
            _roll = Source(go, true);
            _bed = Source(go, true);
            _swell = Source(go, true);
            _engine = Source(go, true);
            _announcer = Source(go, false);
            _gameBed = Source(go, true);

            _clack = Slot("knock", MakeClack());
            _click = Slot("click", MakeClick());
            _thud = Slot("thud", MakeThud());
            _tick = Slot("ui_select", MakeTones(new[] { 1500f }, 0.03f, 0.05f, 0.5f));
            _uiMove = Slot("ui_move", MakeUiMove());
            _softMiss = Slot("miss", MakeSoftMiss());
            _rollLoop = Slot("roll_loop", MakeRollLoop());
            _whirLoop = Slot("probe_loop", MakeWhirLoop());
            _engineLoop = Slot("engine_loop", MakeEngineLoop());
            _roomTone = Slot("room_loop", MakeRoomTone());
            _fireRoar = Slot("fire_big_loop", MakeFireRoar());
            _fireCrackle = Slot("fire_small_loop", MakeFireCrackle());
            _crowd = Slot("crowd_loop", MakeCrowd(false));
            _crowdRoar = Slot("crowd_roar_loop", MakeCrowd(true));
            _space = Slot("space_loop", MakeSpaceDrone());
            _shimmer = Slot("space_shimmer_loop", MakeSpaceShimmer());
            _kick = Slot("kick", MakeKick());
            _net = Slot("net", MakeNet());
            _ping = Slot("post", MakePing());
            _whoosh = Slot("fire_whoosh", MakeWhoosh());
            _douse = Slot("fire_out", MakeDouse());
            _launch = Slot("launch", MakeLaunch());
            _bell = Slot("gate", MakeBell());
            _boom = Slot("boom", MakeBoom());
            _land = Slot("land", MakeLand());
            _crash = Slot("crash", MakeCrash());
            // Archery. Until sounds are dropped in, the nearest things the arcade already has.
            _arrowShot = Slot("arrow_shot", _whoosh);
            _arrowHit = Slot("arrow_hit", _thud);
            _arrowMiss = Slot("arrow_miss", _arrowHit); // none dropped in: the same thud, played quieter
            _range = Slot("range_loop", _roomTone);
            // Baseball. The same: stand-ins until recordings are dropped in.
            _batCrack = Slot("bat_crack", _kick);
            _batSwing = Slot("bat_swing", _whoosh);
            _mitt = Slot("mitt", _thud);
            _organ = Slot("organ", MakeOrgan());
            _ballpark = Slot("ballpark_loop", _crowd);

            _cup = Slot("cup", Recorded("effects_golfball"));
            _applause = Slot("cheer", Recorded("effects_applause"));
            _groan = Slot("groan", Recorded("effects_crowd_ahh"));
            _buzzer = Slot("buzzer", Recorded("effects_backdoor_buzzer"));
            _pins = Slot("strike", Recorded("bowling_pin"));
            _coin = Slot("coin", Recorded("effects_made_coin"));
            _over = Slot("game_over", Recorded("effects_arcade_game_over"));
            for (int i = 0; i < _made.Length; i++) _made[i] = Recorded("effects_made_" + (i + 1));
            if (_cup == null || _applause == null || _pins == null) Debug.LogWarning("Puttr Arcade: recordings are missing from Resources/PuttrArcade/Audio");
            else Debug.Log("Puttr Arcade: sound ready");
        }

        static AudioSource Source(GameObject go, bool loop)
        {
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = loop;
            source.volume = loop ? 0f : 1f;
            return source;
        }

        static AudioClip Recorded(string name) => Resources.Load<AudioClip>("PuttrArcade/Audio/" + name);

        // ── Library recordings ──────────────────────────────────────────────
        //
        // Every sound has a named slot. A recording in
        // Resources/PuttrArcade/Audio/Library called <slot>_1 (and _2, _3 ...
        // for variations) takes the place of the sound made here; where there
        // are several, each playing picks one. Tools/import-sounds.py fills
        // that folder from SoundDrop/<slot>/.
        readonly Dictionary<AudioClip, AudioClip[]> _takes = new Dictionary<AudioClip, AudioClip[]>();

        AudioClip Slot(string slot, AudioClip made)
        {
            var found = new List<AudioClip>();
            for (int i = 1; i <= 8; i++)
            {
                AudioClip clip = Resources.Load<AudioClip>("PuttrArcade/Audio/Library/" + slot + "_" + i);
                if (clip == null) break;
                found.Add(clip);
            }
            if (found.Count == 0) return made;
            if (found.Count > 1) _takes[found[0]] = found.ToArray();
            return found[0];
        }

        /// <summary>puttr-app-2: a library sound by slot (balloon_pop, golf_drive...), one of its takes at random. Silent if the slot is empty.</summary>
        public void Library(string slot, float volume = 0.9f, float pitch = 1f)
        {
            if (!_slots.TryGetValue(slot, out AudioClip clip)) { clip = Slot(slot, null); _slots[slot] = clip; }
            if (clip != null) Play(clip, volume, pitch);
        }

        /// <summary>puttr-app-2: a game's ambience (a *_loop slot) under everything, or null to stop it.</summary>
        public void GameBed(string loopSlot, float volume = 0.35f)
        {
            if (loopSlot == null) { _gameBed.Stop(); _gameBed.clip = null; return; }
            if (!_slots.TryGetValue(loopSlot, out AudioClip clip)) { clip = Slot(loopSlot, null); _slots[loopSlot] = clip; }
            if (clip == null) return;
            if (_gameBed.clip != clip) { _gameBed.clip = clip; _gameBed.Play(); }
            _gameBed.volume = volume;
        }

        /// <summary>puttr-app-2: a recorded clip by name from Resources/PuttrArcade/Audio (Putt Star's crowd).</summary>
        public void Clip(string name, float volume = 0.9f) { AudioClip c = Recorded(name); if (c != null) Play(c, volume); }

        void Play(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null || volume <= 0.001f) return;
            if (_takes.TryGetValue(clip, out AudioClip[] takes)) clip = takes[_rng.Next(takes.Length)];
            AudioSource v = _voices[_next];
            _next = (_next + 1) % _voices.Length;
            v.pitch = pitch;
            v.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        float Vary(float around, float by) => around + ((float)_rng.NextDouble() * 2f - 1f) * by;

        // ── Where we are ────────────────────────────────────────────────────

        /// <summary>Change the bed. It fades across; nothing cuts.</summary>
        public void SetScene(Scene scene)
        {
            if (scene == _scene && _bed.clip != null) return;
            _scene = scene;
            _level = 0f;
            _excitement = 0f;
            _bed.volume = 0f;
            _swell.volume = 0f;
            _bed.pitch = _swell.pitch = 1f;
            _engine.volume = 0f;
            _engineSpeed = 0f;
            _engineWanted = 0f;
            _roll.volume = 0f;
            switch (scene)
            {
                case Scene.Fire: _bed.clip = _fireCrackle; _swell.clip = _fireRoar; break;
                case Scene.Stadium: _bed.clip = _crowd; _swell.clip = _crowdRoar; break;
                case Scene.Arena: _bed.clip = _crowd; _swell.clip = _crowdRoar; _bed.pitch = 0.88f; _swell.pitch = 0.92f; break;
                case Scene.Space: _bed.clip = _space; _swell.clip = _shimmer; break;
                case Scene.Range: _bed.clip = _range; _swell.clip = null; break;
                case Scene.Ballpark: _bed.clip = _ballpark; _swell.clip = _crowdRoar; break;
                default: _bed.clip = _roomTone; _swell.clip = null; break;
            }
            _roll.clip = scene == Scene.Space ? _whirLoop : _rollLoop;
            _engine.clip = _engineLoop;
            if (_bed.clip != null) _bed.Play();
            if (_swell.clip != null) _swell.Play(); else _swell.Stop();
        }

        /// <summary>How much is going on, 0 to 1: the size of the fire. The crowd sets its own.</summary>
        public void SetLevel(float level) => _level = Mathf.Clamp01(level);

        /// <summary>Call every frame.</summary>
        public void Tick(float dt)
        {
            TickRoll(dt);
            _excitement = Mathf.MoveTowards(_excitement, 0f, dt * 0.22f);
            switch (_scene)
            {
                case Scene.Fire:
                    // Embers tick quietly; a big fire roars under its own crackle.
                    _bedWanted = 0.1f + 0.5f * Mathf.Sqrt(_level);
                    _swellWanted = 0.75f * Mathf.Pow(_level, 1.2f);
                    _swell.pitch = Mathf.Lerp(0.75f, 1.15f, _level);
                    _bed.pitch = Mathf.Lerp(0.9f, 1.1f, _level);
                    break;
                case Scene.Stadium:
                    _bedWanted = 0.3f + 0.15f * _excitement;
                    _swellWanted = 0.75f * _excitement;
                    break;
                case Scene.Arena:
                    _bedWanted = 0.2f + 0.12f * _excitement;
                    _swellWanted = 0.6f * _excitement;
                    break;
                case Scene.Space:
                    _bedWanted = 0.42f;
                    _swellWanted = 0.1f + 0.3f * _excitement;
                    break;
                case Scene.Ballpark:
                    // The murmur of a full park, lower while the announcer is talking.
                    _bedWanted = (0.34f + 0.12f * _excitement) * (VoiceBusy ? 0.6f : 1f);
                    _swellWanted = 0.7f * _excitement;
                    break;
                case Scene.Range:
                    _bedWanted = 0.3f; // birds and a light wind: the range is outdoors
                    _swellWanted = 0f;
                    break;
                default:
                    _bedWanted = 0.07f;
                    _swellWanted = 0f;
                    break;
            }
            _bed.volume = Mathf.MoveTowards(_bed.volume, _bedWanted, dt * 0.5f);
            _swell.volume = Mathf.MoveTowards(_swell.volume, _swellWanted, dt * (_swellWanted > _swell.volume ? 2.2f : 0.5f));

            TickEngine(dt);

            // The announcer, one line after another.
            if (!_announcer.isPlaying && _saying.Count > 0)
            {
                _announcer.clip = _saying.Dequeue();
                _announcer.volume = 0.95f;
                _announcer.Play();
            }
        }

        // ── The announcer ───────────────────────────────────────────────────

        /// <summary>Say these recordings in turn (file names in Resources/PuttrArcade/Audio).</summary>
        public void Say(params string[] lines)
        {
            // The voice-over is out for now: the recordings were removed, so
            // this says nothing. Games still call it where a line would go.
        }

        /// <summary>The announcer says these in turn, after anything already waiting. Each is a
        /// recording in Resources/PuttrArcade/Audio/Voice. One that is missing is passed over.</summary>
        public void Voice(params string[] names)
        {
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (!_spoken.TryGetValue(name, out AudioClip clip))
                {
                    clip = Resources.Load<AudioClip>("PuttrArcade/Audio/Voice/" + name);
                    _spoken[name] = clip;
                }
                if (clip != null) _saying.Enqueue(clip);
            }
        }

        /// <summary>One of a numbered set, at random: VoiceOne("ml_single_", 8) is ml_single_1 to ml_single_8.</summary>
        public string VoiceOne(string prefix, int count) => prefix + (_rng.Next(Mathf.Max(1, count)) + 1);

        /// <summary>True while the announcer is talking or has more to say.</summary>
        public bool VoiceBusy => _announcer.isPlaying || _saying.Count > 0;

        public void Hush()
        {
            _saying.Clear();
            _announcer.Stop();
        }

        // ── Things that happen anywhere ─────────────────────────────────────

        /// <summary>Something hard struck. speed is the relative speed in inches per second.</summary>
        public void PinKnock(float speed)
        {
            float hard = Mathf.InverseLerp(4f, 45f, speed);
            if (_scene == Scene.Alley)
            {
                Play(_clack, hard * 0.8f + 0.12f, Vary(1f, 0.15f));
                // The first good hit of a ball is a whole rack going over, as recorded.
                if (hard > 0.35f && Time.unscaledTime - _pinsAt > 2.5f) { _pinsAt = Time.unscaledTime; Play(_pins, 0.35f + hard * 0.55f, Vary(1f, 0.05f)); }
            }
            else Play(_click, hard * 0.7f + 0.15f, Vary(1f, 0.12f));
        }

        public void Thump(float speed) => Play(_thud, Mathf.InverseLerp(5f, 60f, speed) * 0.7f + 0.1f, Vary(1f, 0.1f));

        /// <summary>The ball is in the cup.</summary>
        public void Holed()
        {
            if (_cup != null) Play(_cup, 0.9f, Vary(1f, 0.04f));
            else Play(_thud, 0.6f, 1.4f);
        }

        /// <summary>The best thing that can happen in this game.</summary>
        public void Strike()
        {
            Play(_made[3], 0.7f);
            if (_scene == Scene.Stadium || _scene == Scene.Arena) Roar(true);
            else Play(_applause, 0.55f);
        }

        /// <summary>Something good.</summary>
        public void Spare()
        {
            Play(_made[_rng.Next(3)], 0.6f);
            if (_scene == Scene.Stadium || _scene == Scene.Arena) Roar(false);
        }

        /// <summary>Something that did not come off.</summary>
        public void Miss()
        {
            if (_scene == Scene.Stadium || _scene == Scene.Arena) Groan();
            else Play(_softMiss, 0.5f);
        }

        public void FrameTick() => Play(_tick, 0.2f, Vary(1f, 0.05f));
        public void UiMove() => Play(_uiMove, 0.35f, Vary(1f, 0.04f));
        public void UiStart() => Play(_tick, 0.6f);
        public void GameOver() => Play(_over, 0.55f);
        public void Coin() => Play(_coin, 0.5f);
        public void Buzzer() => Play(_buzzer, 0.7f);

        /// <summary>
        /// Something rolling or moving: louder and higher the faster it goes.
        /// On the lanes it is the ball's rumble, in space the probe's whir, in
        /// the arena the bike's engine.
        /// </summary>
        public void SetRoll(float speedInPerSec)
        {
            if (_scene == Scene.Arena) { SetEngine(speedInPerSec); return; }
            float amount = Mathf.InverseLerp(2f, 55f, speedInPerSec);
            _rollTarget = amount * (_scene == Scene.Space ? 0.3f : 0.5f);
            _rollAskedAt = Time.unscaledTime;
            if (_rollTarget > 0.001f && !_roll.isPlaying) _roll.Play();
            _roll.pitch = Mathf.Lerp(0.7f, 1.35f, amount) * Mathf.Lerp(0.6f, 1f, Time.timeScale);
        }

        // The roll fades here, every frame, so it dies away once nothing is rolling: a game that
        // says SetRoll(0) once, or simply stops calling, is not left with a ball humming forever.
        float _rollTarget, _rollAskedAt = -100f;

        void TickRoll(float dt)
        {
            if (Time.unscaledTime - _rollAskedAt > 0.1f) _rollTarget = 0f;
            _roll.volume = Mathf.MoveTowards(_roll.volume, _rollTarget, dt * 3f);
            if (_roll.volume <= 0.001f && _rollTarget <= 0.001f && _roll.isPlaying) _roll.Stop();
        }

        // ── Stadium and arena ───────────────────────────────────────────────

        public void Kick(float power) => Play(_kick, 0.5f + 0.4f * Mathf.Clamp01(power), Vary(1f, 0.06f));
        public void Net() => Play(_net, 0.6f, Vary(1f, 0.08f));
        public void Post() => Play(_ping, 0.75f, Vary(1f, 0.05f));

        public void Roar(bool big)
        {
            _excitement = Mathf.Max(_excitement, big ? 1f : 0.65f);
            Play(_applause, big ? 0.8f : 0.5f, Vary(1f, 0.03f));
        }

        public void Groan()
        {
            _excitement = Mathf.Max(_excitement, 0.25f);
            Play(_groan, 0.7f, Vary(1f, 0.04f));
        }

        /// <summary>The bike. 0 is switched off; about 90 is flat out. It holds until told otherwise.</summary>
        public void SetEngine(float speed) => _engineWanted = speed;
        float _engineWanted;

        void TickEngine(float dt)
        {
            if (_scene != Scene.Arena) { _engineWanted = 0f; }
            _engineSpeed = Mathf.Lerp(_engineSpeed, _engineWanted, 1f - Mathf.Exp(-dt * 8f));
            float amount = Mathf.InverseLerp(4f, 90f, _engineSpeed);
            float target = _engineWanted > 1f ? 0.22f + 0.38f * amount : 0f;
            if (target > 0.001f && !_engine.isPlaying) _engine.Play();
            _engine.volume = Mathf.MoveTowards(_engine.volume, target, dt * (target > _engine.volume ? 4f : 1.4f));
            _engine.pitch = Mathf.Lerp(0.6f, 2.3f, amount) * Mathf.Lerp(0.7f, 1f, Time.timeScale);
            if (_engine.volume <= 0.001f && _engine.isPlaying) _engine.Stop();
        }

        public void Land() => Play(_land, 0.85f, Vary(1f, 0.05f));
        public void Crash() => Play(_crash, 0.9f, Vary(1f, 0.05f));

        // ── Fire ────────────────────────────────────────────────────────────

        /// <summary>The fire takes a breath: a putt went in.</summary>
        public void Whoosh() => Play(_whoosh, 0.8f, Vary(1f, 0.06f));
        /// <summary>And goes out.</summary>
        public void Douse() => Play(_douse, 0.8f);

        // ── Space ───────────────────────────────────────────────────────────

        // ── Baseball ────────────────────────────────────────────────────────

        AudioClip _batCrack, _batSwing, _mitt, _organ, _ballpark;

        /// <summary>Bat on ball. power 0 to 1: a dribbler to a home run.</summary>
        public void BatCrack(float power) => Play(_batCrack, 0.6f + 0.4f * Mathf.Clamp01(power), Vary(Mathf.Lerp(1.08f, 0.94f, Mathf.Clamp01(power)), 0.04f));
        /// <summary>A swing that finds nothing.</summary>
        public void Swing() => Play(_batSwing, 0.7f, Vary(1.1f, 0.06f));
        /// <summary>The ball into a glove.</summary>
        public void Mitt() => Play(_mitt, 0.8f, Vary(1f, 0.06f));
        /// <summary>The ballpark organ, between batters.</summary>
        public void Organ() { _excitement = Mathf.Max(_excitement, 0.5f); Play(_organ, 0.55f); }

        // ── Archery ─────────────────────────────────────────────────────────

        AudioClip _arrowShot, _arrowHit, _arrowMiss, _range;

        /// <summary>The bow is loosed.</summary>
        public void ArrowShot() => Play(_arrowShot, 0.8f, Vary(1f, 0.05f));
        /// <summary>The arrow lands: in the target, or in the hay or the grass.</summary>
        public void ArrowHit(bool inTarget) => Play(inTarget ? _arrowHit : _arrowMiss, inTarget ? 0.9f : 0.6f, Vary(1f, 0.06f));

        public void Launch() => Play(_launch, 0.7f, Vary(1f, 0.05f));
        public void Bell() { _excitement = 1f; Play(_bell, 0.75f); }
        public void Boom() => Play(_boom, 0.9f, Vary(1f, 0.05f));

        // ── Making sound from nothing ───────────────────────────────────────

        static AudioClip Clip(string name, float[] samples, float peak = 0.85f)
        {
            // Bring every clip to the same loudest point, so none clips and none is lost.
            float top = 0f;
            foreach (float s in samples) top = Mathf.Max(top, Mathf.Abs(s));
            if (top > 1e-5f) for (int i = 0; i < samples.Length; i++) samples[i] *= peak / top;
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static float[] Noise(int n, int seed)
        {
            var rng = new System.Random(seed);
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (float)(rng.NextDouble() * 2 - 1);
            return s;
        }

        // Keep what is below this frequency.
        static float[] Low(float[] s, float hz)
        {
            float k = 1f - Mathf.Exp(-2f * Mathf.PI * hz / Rate), y = 0f;
            var o = new float[s.Length];
            for (int i = 0; i < s.Length; i++) { y += (s[i] - y) * k; o[i] = y; }
            return o;
        }

        // Keep what is above it.
        static float[] High(float[] s, float hz)
        {
            float[] low = Low(s, hz);
            var o = new float[s.Length];
            for (int i = 0; i < s.Length; i++) o[i] = s[i] - low[i];
            return o;
        }

        static float[] Band(float[] s, float from, float to) => Low(Low(High(s, from), to), to);

        // Join the end of a loop to its start so it does not click.
        static float[] Seamless(float[] s, float seconds = 0.25f)
        {
            int fade = (int)(Rate * seconds), n = s.Length - fade;
            var loop = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i < fade) { float t = i / (float)fade; loop[i] = s[i] * Mathf.Sqrt(t) + s[n + i] * Mathf.Sqrt(1f - t); }
                else loop[i] = s[i];
            }
            return loop;
        }

        // A slow, wandering level between low and 1: nothing natural holds steady.
        static float[] Wander(int n, float hz, int seed, float low)
        {
            float[] w = Low(Low(Noise(n, seed), hz), hz);
            float top = 1e-6f;
            foreach (float v in w) top = Mathf.Max(top, Mathf.Abs(v));
            for (int i = 0; i < n; i++) w[i] = Mathf.Lerp(low, 1f, 0.5f + 0.5f * w[i] / top);
            return w;
        }

        // Hard maple on hard maple: a burst of noise and two short ringing tones.
        static AudioClip MakeClack()
        {
            var rng = new System.Random(3);
            var s = new float[(int)(Rate * 0.16f)];
            float lp = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                lp += ((float)(rng.NextDouble() * 2 - 1) - lp) * 0.55f;
                float click = lp * Mathf.Exp(-t * 90f) * 0.9f;
                float ring = Mathf.Sin(2 * Mathf.PI * 1650f * t) * Mathf.Exp(-t * 45f) * 0.45f + Mathf.Sin(2 * Mathf.PI * 2480f * t) * Mathf.Exp(-t * 70f) * 0.3f;
                s[i] = click + ring;
            }
            return Clip("Clack", s);
        }

        // Golf ball on golf ball, or on brass: a very short, bright tick.
        static AudioClip MakeClick()
        {
            float[] noise = High(Noise((int)(Rate * 0.09f), 7), 1800f);
            for (int i = 0; i < noise.Length; i++)
            {
                float t = i / (float)Rate;
                noise[i] = noise[i] * Mathf.Exp(-t * 260f) + Mathf.Sin(2 * Mathf.PI * 3100f * t) * Mathf.Exp(-t * 120f) * 0.6f + Mathf.Sin(2 * Mathf.PI * 5200f * t) * Mathf.Exp(-t * 200f) * 0.3f;
            }
            return Clip("Click", noise);
        }

        static AudioClip MakeThud()
        {
            float[] s = Low(Noise((int)(Rate * 0.25f), 5), 600f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                s[i] = s[i] * 2.2f * Mathf.Exp(-t * 30f) + Mathf.Sin(2 * Mathf.PI * 105f * t) * Mathf.Exp(-t * 22f) * 0.7f;
            }
            return Clip("Thud", s);
        }

        static AudioClip MakeUiMove()
        {
            float[] s = High(Noise((int)(Rate * 0.05f), 9), 2500f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                s[i] = s[i] * Mathf.Exp(-t * 500f) * 0.6f + Mathf.Sin(2 * Mathf.PI * 1250f * t) * Mathf.Exp(-t * 160f) * 0.5f;
            }
            return Clip("Move", s, 0.5f);
        }

        // Not a buzzer: a soft, low knock that falls away.
        static AudioClip MakeSoftMiss()
        {
            var s = new float[(int)(Rate * 0.45f)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(190f, 120f, t / 0.45f);
                s[i] = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Min(1f, t * 200f) * Mathf.Exp(-t * 9f);
            }
            return Clip("Miss", s, 0.6f);
        }

        static AudioClip MakeRollLoop()
        {
            int n = Rate * 3;
            float[] s = Low(Low(Noise(n, 11), 320f), 900f);
            float[] wobble = Wander(n, 6f, 12, 0.55f);
            for (int i = 0; i < n; i++) s[i] *= wobble[i] * (0.8f + 0.2f * Mathf.Sin(2 * Mathf.PI * 9f * i / Rate));
            return Clip("Roll", Seamless(s));
        }

        // The probe: two close tones beating against each other.
        static AudioClip MakeWhirLoop()
        {
            int n = Rate * 2;
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] = Mathf.Sin(2 * Mathf.PI * 180f * t) * 0.6f + Mathf.Sin(2 * Mathf.PI * 183f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * 360f * t) * 0.18f;
            }
            return Clip("Whir", s, 0.6f);
        }

        // A single-cylinder engine: a pulse eighty times a second, its
        // overtones, and the rasp of the exhaust. One second is a whole number
        // of pulses, so it loops as it is.
        static AudioClip MakeEngineLoop()
        {
            int n = Rate;
            float[] rasp = Band(Noise(n, 21), 400f, 3000f);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float phase = (t * 80f) % 1f;
                float pulse = Mathf.Exp(-phase * 9f);
                float tone = 0f;
                for (int h = 1; h <= 8; h++) tone += Mathf.Sin(2 * Mathf.PI * 80f * h * t) / (h * (h % 2 == 0 ? 1.6f : 1f));
                s[i] = tone * 0.55f + rasp[i] * pulse * 2.4f + (pulse - 0.1f) * 0.35f;
            }
            return Clip("Engine", s, 0.8f);
        }

        // A room with the lights off: almost nothing, but not nothing.
        static AudioClip MakeRoomTone()
        {
            int n = Rate * 6;
            float[] s = Low(Low(Noise(n, 31), 180f), 400f);
            float[] w = Wander(n, 0.3f, 32, 0.7f);
            for (int i = 0; i < n; i++) s[i] *= w[i];
            return Clip("Room", Seamless(s, 0.5f), 0.5f);
        }

        // The body of a fire: low, and never steady.
        static AudioClip MakeFireRoar()
        {
            int n = Rate * 8;
            float[] deep = Low(Low(Noise(n, 41), 140f), 380f);
            float[] air = Band(Noise(n, 42), 300f, 1400f);
            float[] flutter = Wander(n, 7f, 43, 0.35f), breath = Wander(n, 0.5f, 44, 0.6f);
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (deep[i] * 3f + air[i] * 0.5f) * flutter[i] * breath[i];
            return Clip("Fire roar", Seamless(s, 0.6f));
        }

        // Its crackle: sap and seams letting go, a dozen times a second, most
        // of them small and now and then one that makes you look.
        static AudioClip MakeFireCrackle()
        {
            int n = Rate * 8;
            var rng = new System.Random(45);
            float[] hiss = High(Noise(n, 46), 3500f);
            float[] bright = High(Noise(n, 47), 1200f);
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = hiss[i] * 0.02f;
            int at = 0;
            while (true)
            {
                at += (int)(-Mathf.Log(1f - (float)rng.NextDouble()) * Rate / 13f);
                if (at >= n - Rate / 10) break;
                float size = Mathf.Pow((float)rng.NextDouble(), 3f);
                float decay = Mathf.Lerp(900f, 180f, size);
                int length = (int)(Rate * Mathf.Lerp(0.004f, 0.03f, size));
                for (int i = 0; i < length; i++)
                {
                    float t = i / (float)Rate;
                    s[at + i] += bright[(at * 7 + i) % n] * Mathf.Exp(-t * decay) * (0.15f + size);
                }
                // A proper pop has a knock under it.
                if (size > 0.7f)
                    for (int i = 0; i < Rate / 25; i++)
                    {
                        float t = i / (float)Rate;
                        s[at + i] += Mathf.Sin(2 * Mathf.PI * 160f * t) * Mathf.Exp(-t * 80f) * size * 0.5f;
                    }
            }
            return Clip("Fire crackle", Seamless(s, 0.3f));
        }

        // Thousands of people talking at once (or, with roar, all shouting):
        // bands of noise, each rising and falling on its own.
        static AudioClip MakeCrowd(bool roar)
        {
            int n = Rate * 9;
            var s = new float[n];
            float[][] bands = roar
                ? new[] { new[] { 350f, 900f }, new[] { 700f, 1800f }, new[] { 1400f, 3200f }, new[] { 2600f, 5200f } }
                : new[] { new[] { 220f, 520f }, new[] { 420f, 900f }, new[] { 800f, 1500f }, new[] { 1300f, 2400f } };
            for (int b = 0; b < bands.Length; b++)
            {
                float[] band = Band(Noise(n, 60 + b * 3 + (roar ? 20 : 0)), bands[b][0], bands[b][1]);
                float[] swell = Wander(n, roar ? 0.9f : 0.35f, 61 + b * 3, roar ? 0.55f : 0.4f);
                float weight = roar ? 1f : 1f / (1f + b * 0.5f);
                for (int i = 0; i < n; i++) s[i] += band[i] * swell[i] * weight;
            }
            return Clip(roar ? "Crowd roar" : "Crowd", Seamless(s, 0.8f));
        }

        // Nothing carries sound in space, so this is the sound of the ship:
        // low tones a hair apart, turning slowly against each other.
        static AudioClip MakeSpaceDrone()
        {
            int n = Rate * 12;
            var s = new float[n];
            float[] freqs = { 55f, 55.25f, 82.5f, 82.75f, 110.5f, 165f };
            float[] weights = { 1f, 0.9f, 0.6f, 0.5f, 0.3f, 0.12f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0f;
                for (int k = 0; k < freqs.Length; k++) v += Mathf.Sin(2 * Mathf.PI * freqs[k] * t) * weights[k];
                s[i] = v * (0.8f + 0.2f * Mathf.Sin(2 * Mathf.PI * t / 6f));
            }
            return Clip("Space", s, 0.7f);
        }

        static AudioClip MakeSpaceShimmer()
        {
            int n = Rate * 10;
            float[] high = Band(Noise(n, 71), 3800f, 7000f);
            float[] w = Wander(n, 0.25f, 72, 0.1f);
            for (int i = 0; i < n; i++) high[i] *= w[i];
            return Clip("Shimmer", Seamless(high, 0.8f), 0.5f);
        }

        // A boot through a ball: a low thump that drops, and the slap on top.
        static AudioClip MakeKick()
        {
            float[] slap = Band(Noise((int)(Rate * 0.2f), 81), 800f, 4000f);
            for (int i = 0; i < slap.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(150f, 55f, Mathf.Clamp01(t / 0.09f));
                slap[i] = slap[i] * Mathf.Exp(-t * 110f) * 0.8f + Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * 26f);
            }
            return Clip("Kick", slap);
        }

        // The net taking the ball: a rush of cord, quickly gone.
        static AudioClip MakeNet()
        {
            int n = (int)(Rate * 0.45f);
            float[] s = Band(Noise(n, 83), 1800f, 7000f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] *= Mathf.Min(1f, t * 60f) * Mathf.Exp(-t * 11f) * (0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * 38f * t));
            }
            return Clip("Net", s);
        }

        // Steel tube: partials that are not multiples of each other, ringing.
        static AudioClip MakePing()
        {
            int n = (int)(Rate * 0.9f);
            var s = new float[n];
            float[] f = { 510f, 1290f, 2160f, 3170f, 4420f };
            float[] d = { 5f, 7f, 10f, 14f, 20f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                for (int k = 0; k < f.Length; k++) s[i] += Mathf.Sin(2 * Mathf.PI * f[k] * t) * Mathf.Exp(-t * d[k]) / (1f + k * 0.5f);
                s[i] += (i < 300 ? (1f - i / 300f) : 0f) * 0.8f * (i % 2 == 0 ? 1f : -1f);
            }
            return Clip("Ping", s);
        }

        // Fire fed: air drawn in, then let go.
        static AudioClip MakeWhoosh()
        {
            int n = (int)(Rate * 1.1f);
            float[] src = Noise(n, 85);
            var s = new float[n];
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float cutoff = Mathf.Lerp(200f, 2600f, Mathf.Sin(t * Mathf.PI));
                float k = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
                y += (src[i] - y) * k;
                s[i] = y * Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI);
            }
            return Clip("Whoosh", s);
        }

        // Fire put out: a long hiss.
        static AudioClip MakeDouse()
        {
            int n = (int)(Rate * 1.8f);
            float[] s = High(Noise(n, 87), 2800f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] *= Mathf.Min(1f, t * 30f) * Mathf.Exp(-t * 2.6f);
            }
            return Clip("Douse", s);
        }

        // A thruster firing: a burst that drops in pitch as it dies.
        static AudioClip MakeLaunch()
        {
            int n = (int)(Rate * 0.7f);
            float[] src = Noise(n, 89);
            var s = new float[n];
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float cutoff = Mathf.Lerp(2400f, 180f, Mathf.Clamp01(t / 0.6f));
                y += (src[i] - y) * (1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate));
                s[i] = y * Mathf.Min(1f, t * 80f) * Mathf.Exp(-t * 5f) + Mathf.Sin(2 * Mathf.PI * 70f * t) * Mathf.Exp(-t * 9f) * 0.5f;
            }
            return Clip("Launch", s);
        }

        // The relay locking on: two bells, a fifth apart.
        static AudioClip MakeBell()
        {
            int n = (int)(Rate * 2.2f);
            var s = new float[n];
            float[] ratio = { 1f, 2.76f, 5.4f, 8.93f };
            float[] roots = { 660f, 990f };
            for (int r = 0; r < roots.Length; r++)
            {
                int start = (int)(Rate * 0.14f * r);
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)Rate;
                    for (int k = 0; k < ratio.Length; k++)
                        s[i] += Mathf.Sin(2 * Mathf.PI * roots[r] * ratio[k] * t) * Mathf.Exp(-t * (2.2f + k * 2.5f)) / (1f + k * 1.2f) * Mathf.Min(1f, t * 600f);
                }
            }
            return Clip("Bell", s);
        }

        static AudioClip MakeBoom()
        {
            int n = (int)(Rate * 1.1f);
            float[] s = Low(Noise(n, 91), 260f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] = s[i] * 2.5f * Mathf.Exp(-t * 7f) + Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(70f, 38f, t) * t) * Mathf.Exp(-t * 4.5f);
            }
            return Clip("Boom", s);
        }

        // Two wheels coming down on a ramp: weight, and rubber catching.
        static AudioClip MakeLand()
        {
            int n = (int)(Rate * 0.5f);
            float[] s = Low(Noise(n, 93), 500f);
            float[] chirp = Band(Noise(n, 94), 1500f, 4200f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] = s[i] * 2.4f * Mathf.Exp(-t * 18f) + Mathf.Sin(2 * Mathf.PI * 75f * t) * Mathf.Exp(-t * 14f) + chirp[i] * Mathf.Exp(-Mathf.Abs(t - 0.05f) * 45f) * 0.5f;
            }
            return Clip("Land", s);
        }

        // And not coming down on it: a crunch, and steel ringing after.
        static AudioClip MakeCrash()
        {
            int n = (int)(Rate * 1.3f);
            float[] crunch = Band(Noise(n, 95), 300f, 5000f);
            var s = new float[n];
            var rng = new System.Random(96);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                s[i] = crunch[i] * Mathf.Exp(-t * 6f) * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 23f * t)) + Mathf.Sin(2 * Mathf.PI * 62f * t) * Mathf.Exp(-t * 10f);
            }
            for (int c = 0; c < 5; c++)
            {
                int start = (int)(Rate * (0.02f + 0.1f * c + (float)rng.NextDouble() * 0.06f));
                float f = 700f + (float)rng.NextDouble() * 2600f;
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)Rate;
                    s[i] += (Mathf.Sin(2 * Mathf.PI * f * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * f * 2.71f * t)) * Mathf.Exp(-t * 12f) * 0.22f;
                }
            }
            return Clip("Crash", s);
        }

        // A run of bell-like notes, each ringing into the next.
        // The ballpark organ's call to the crowd: six notes climbing, the last one held.
        static AudioClip MakeOrgan()
        {
            float[] notes = { 392f, 523.25f, 659.25f, 783.99f, 659.25f, 783.99f };
            float[] lasts = { 0.15f, 0.15f, 0.15f, 0.3f, 0.15f, 0.75f };
            float total = 0.25f;
            foreach (float d in lasts) total += d;
            var s = new float[(int)(Rate * total)];
            float[] stops = { 1f, 0.7f, 0.5f, 0.3f, 0.18f }; // drawbars: the note and what sits above it
            float at = 0f;
            for (int k = 0; k < notes.Length; k++)
            {
                int start = (int)(Rate * at), length = (int)(Rate * (lasts[k] + 0.06f));
                for (int i = 0; i < length && start + i < s.Length; i++)
                {
                    float t = i / (float)Rate;
                    float wobble = 1f + 0.004f * Mathf.Sin(2 * Mathf.PI * 6.2f * t);
                    float v = 0f;
                    for (int h = 0; h < stops.Length; h++) v += stops[h] * Mathf.Sin(2 * Mathf.PI * notes[k] * (h + 1) * wobble * t);
                    float on = Mathf.Min(1f, t * 90f), off = Mathf.Clamp01((lasts[k] + 0.06f - t) * 22f);
                    s[start + i] += v * on * off;
                }
                at += lasts[k];
            }
            return Clip("Organ", s, 0.6f);
        }

        static AudioClip MakeTones(float[] notes, float spacing, float tail, float peak)
        {
            int n = (int)(Rate * (spacing * notes.Length + tail));
            var s = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = (int)(Rate * spacing * k);
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)Rate;
                    s[i] += (Mathf.Sin(2 * Mathf.PI * notes[k] * t) + 0.35f * Mathf.Sin(2 * Mathf.PI * notes[k] * 2.01f * t)) * Mathf.Min(1f, t * 400f) * Mathf.Exp(-t * 7f);
                }
            }
            return Clip("Tones", s, peak);
        }
    }
}
