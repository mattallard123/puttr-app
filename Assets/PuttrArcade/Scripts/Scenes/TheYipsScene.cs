// The Yips: the Arena. A dark stormy stone arena ringed with fire braziers, and at the far end the Yips: a dark
// twitching thing of spikes and glowing eyes, hovering, never still, more violent and redder each stage. Its HP bar
// hangs over it; yours is by your putter, with your combo as flames beside it.
//
// Each putt replays the real stroke. At impact the ball goes up the arena as a glowing shot, as big as the damage it
// does: a hit knocks the monster back and a number flies off it, a crit explodes; a stroke that does nothing fizzles
// out short. If the stroke twitched (face) or collapsed (rhythm) the monster lunges and strikes back: red lightning
// at you, the screen flashes red, your bar drops. Beaten, it bursts into sparks; if it wins, the arena goes dark red.
// Units: inches; your ball at the origin, the monster up +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class TheYipsScene : SensorStage
    {
        static readonly Vector3 BossAt = new Vector3(28f, 22f, 84f);
        static readonly Color Accent = Make.Hex(0xFF3366), Fire = new Color(1f, 0.5f, 0.15f);

        Transform _boss, _body, _mouth, _ball, _shot, _flashT, _bossBar, _bossFill, _bossLag, _youBar, _youFill;
        readonly List<Transform> _spikes = new List<Transform>();
        readonly List<Vector3> _spikeDir = new List<Vector3>();
        readonly List<(Transform t, Light l, float seed)> _fires = new List<(Transform, Light, float)>();
        readonly List<Transform> _pips = new List<Transform>();
        Material _skin, _eyes, _mouthMat, _flashMat, _shotMat, _bossFillMat, _youFillMat;
        Light _shotLight, _storm, _doom;
        LineRenderer _bolt, _trail;
        TextMesh _bossName, _youName, _hit;
        Putter _putter;
        int _stage = 1;
        float _jitter = 0.6f, _redFlash, _stormAt = 3f, _stormFlash, _lunge, _recoil, _roar, _bossShown = 1f, _bossLagShown = 1f, _youShown = 1f;
        float _bossTo = 1f, _youTo = 1f, _scale = 1f, _hitAge = 9f;
        bool _dead, _lost;
        Vector3 _twitch;
        float _twitchAt;

        public override Vector3 CameraFrom => Portrait ? At(-14f, 38f, -62f) : At(-16f, 30f, -30f);
        public override Vector3 CameraLook => Portrait ? At(6f, 21f, 56f) : At(12f, 24f, 66f);
        public override float CameraFov => Portrait ? 62f : 48f;
        public override float ShadowDistance => 500f;

        static Mesh Cone(float r, float h)
        {
            return Make.Lathe(new List<Vector2> { new Vector2(r, 0f), new Vector2(r * 0.55f, h * 0.45f), new Vector2(0.01f, h) }, 10);
        }

        public override void Build()
        {
            // The arena: a stone floor, a ring of pillars, a stormy sky.
            Make.MeshObject("Floor", Root, Make.Disc(320f, 72), Make.Lit(new Color(0.11f, 0.095f, 0.1f), 0.55f)).transform.localPosition = new Vector3(0, 0, 110f);
            Hoop("Ring", Root, new Vector3(0, 0.05f, 110f), 150f, 154f, Make.Glow(new Color(1f, 0.3f, 0.35f, 0.35f), false));
            Hoop("Ring", Root, new Vector3(0, 0.05f, 110f), 60f, 61.5f, Make.Glow(new Color(1f, 0.3f, 0.35f, 0.25f), false));
            Shape(PrimitiveType.Sphere, "Sky", Root, new Vector3(0, 0, 110f), new Vector3(-1400f, -900f, -1400f), Make.Emissive(new Color(0.05f, 0.02f, 0.04f), new Color(0.07f, 0.02f, 0.05f), 0.1f));
            Material stone = Make.Lit(new Color(0.16f, 0.14f, 0.15f), 0.3f);
            for (int i = 0; i < 16; i++)
            {
                float a = (i / 16f) * Mathf.PI * 2f;
                Vector3 at = new Vector3(Mathf.Cos(a) * 240f, 0, 110f + Mathf.Sin(a) * 240f);
                if (at.z < 0f) continue;
                float h = 70f + (i % 3) * 25f;
                Shape(PrimitiveType.Cube, "Pillar", Root, at + Vector3.up * h / 2f, new Vector3(16f, h, 16f), stone);
            }

            // Braziers.
            Material iron = Make.Lit(new Color(0.12f, 0.1f, 0.09f), 0.6f, 0.7f);
            Vector3[] spots = { new Vector3(-70f, 0, 90f), new Vector3(90f, 0, 80f), new Vector3(-60f, 0, 190f), new Vector3(100f, 0, 185f) };
            for (int i = 0; i < spots.Length; i++)
            {
                Cylinder("Stand", Root, spots[i] + Vector3.up * 9f, 2.4f, 18f, iron);
                GameObject bowl = Make.MeshObject("Bowl", Root, Make.Lathe(new List<Vector2> { new Vector2(1f, 0f), new Vector2(7f, 3f), new Vector2(8f, 6f) }, 18), iron);
                bowl.transform.localPosition = spots[i] + Vector3.up * 18f;
                var fire = new GameObject("Fire").transform;
                fire.SetParent(Root, false);
                fire.localPosition = spots[i] + Vector3.up * 24f;
                for (int k = 0; k < 3; k++)
                {
                    GameObject f = Shape(PrimitiveType.Sphere, "Flame", fire, new Vector3(0, k * 3f, 0), new Vector3(9f - k * 2.5f, 12f - k * 2f, 9f - k * 2.5f),
                        Make.Glow(k == 0 ? new Color(1f, 0.35f, 0.08f, 0.7f) : new Color(1f, 0.75f, 0.3f, 0.6f)));
                    NoShadow(f);
                }
                Light l = World.NewLight(fire, "Fire light", LightType.Point, Fire, 25000f);
                l.range = 220f;
                l.shadows = i < 2 ? LightShadows.Soft : LightShadows.None;
                _fires.Add((fire, l, i * 1.7f));
            }

            // You: the putter, the ball.
            _putter = BuildPutter(Root, new Vector3(-0.4f, 0f, -2.4f), Accent);
            _putter.FaceScale = 2f;
            _ball = BuildBall(Root, Vector3.zero);
            Make.MeshObject("Tee pad", Root, Make.Disc(14f, 40), Make.Lit(new Color(0.2f, 0.06f, 0.09f), 0.6f)).transform.localPosition = new Vector3(0, 0.02f, -2f);

            // The Yips.
            _boss = new GameObject("The Yips").transform;
            _boss.SetParent(Root, false);
            _boss.localPosition = BossAt;
            _body = new GameObject("Body").transform;
            _body.SetParent(_boss, false);
            _skin = Make.Emissive(new Color(0.07f, 0.04f, 0.07f), new Color(0.25f, 0.02f, 0.08f), 0.85f);
            Shape(PrimitiveType.Sphere, "Core", _body, Vector3.zero, new Vector3(34f, 30f, 32f), _skin);
            Shape(PrimitiveType.Sphere, "Brow", _body, new Vector3(0, 8f, -8f), new Vector3(26f, 12f, 20f), _skin);
            Mesh cone = Cone(3.2f, 14f);
            Material spikeMat = Make.Emissive(new Color(0.1f, 0.04f, 0.07f), new Color(0.5f, 0.02f, 0.1f), 0.7f);
            var dice = new System.Random(13);
            for (int i = 0; i < 22; i++)
            {
                Vector3 d = new Vector3((float)dice.NextDouble() * 2f - 1f, (float)dice.NextDouble() * 1.6f - 0.5f, (float)dice.NextDouble() * 1.6f - 0.4f).normalized;
                if (d.z < -0.6f && Mathf.Abs(d.x) < 0.5f && d.y < 0.4f) d.z = 0.2f;   // keep the face clear
                d.Normalize();
                GameObject sp = Make.MeshObject("Spike", _body, cone, spikeMat);
                sp.transform.localPosition = d * 13f;
                sp.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d);
                _spikes.Add(sp.transform);
                _spikeDir.Add(d);
            }
            _eyes = Make.Emissive(new Color(1f, 0.6f, 0.5f), new Color(4f, 0.6f, 0.4f), 0.9f);
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject eye = Shape(PrimitiveType.Sphere, "Eye", _body, new Vector3(s * 6.5f, 4f, -14.2f), new Vector3(5.5f, 3.4f, 2f), _eyes);
                eye.transform.localRotation = Quaternion.Euler(0, 0, s * -18f);
                NoShadow(eye);
                Shape(PrimitiveType.Cube, "Slit", _body, new Vector3(s * 6.5f, 4f, -15.2f), new Vector3(0.8f, 3f, 0.4f), Make.Lit(Color.black, 0.2f));
            }
            _mouthMat = Make.Emissive(new Color(0.15f, 0.02f, 0.04f), new Color(0.6f, 0.05f, 0.1f), 0.5f);
            _mouth = Shape(PrimitiveType.Sphere, "Mouth", _body, new Vector3(0, -5f, -13.6f), new Vector3(14f, 3f, 3f), _mouthMat).transform;
            Light menace = World.NewLight(_boss, "Menace", LightType.Point, new Color(1f, 0.15f, 0.3f), 30000f);
            menace.transform.localPosition = new Vector3(0, -6f, -28f);
            menace.range = 160f;
            menace.shadows = LightShadows.None;

            // The HP bars.
            _bossBar = Bar("Boss bar", new Vector3(28f, 48f, 84f), new Vector2(50f, 4f), out _bossFill, out _bossFillMat, out _bossLag);
            _bossName = Words(_bossBar, "THE YIPS", new Vector3(-25f, 4.6f, 0f), 3.6f, Accent, TextAnchor.MiddleLeft);
            _youBar = Bar("Your bar", new Vector3(13f, 8f, 10f), new Vector2(18f, 2f), out _youFill, out _youFillMat, out Transform youLag);
            youLag.gameObject.SetActive(false);
            _youName = Words(_youBar, "YOU", new Vector3(-9f, 2.6f, 0f), 2f, Color.white, TextAnchor.MiddleLeft);
            for (int i = 0; i < 8; i++)
            {
                GameObject pip = Shape(PrimitiveType.Sphere, "Combo", _youBar, new Vector3(-8.5f + i * 2.4f, -2.8f, 0f), new Vector3(1.4f, 1.9f, 1.4f), Make.Glow(new Color(1f, 0.55f, 0.15f, 0.95f)));
                NoShadow(pip);
                pip.SetActive(false);
                _pips.Add(pip.transform);
            }

            // The shot, its trail, the lightning, the damage number, the red flash.
            _shotMat = Make.Emissive(Color.white, new Color(3f, 2.4f, 2.6f), 0.9f);
            _shot = Shape(PrimitiveType.Sphere, "Shot", Root, Vector3.zero, Vector3.one * 2f, _shotMat).transform;
            NoShadow(_shot.gameObject);
            _shotLight = World.NewLight(_shot, "Shot light", LightType.Point, new Color(1f, 0.85f, 0.9f), 20000f);
            _shotLight.range = 90f;
            _shotLight.shadows = LightShadows.None;
            _shot.gameObject.SetActive(false);
            _trail = Stroke(Root, "Trail", new Color(1f, 0.6f, 0.75f, 0.8f), 1.6f);
            _bolt = Stroke(Root, "Bolt", new Color(1f, 0.25f, 0.3f, 1f), 1.4f);
            _hit = Words(Root, "", BossAt, 9f, Color.white, TextAnchor.MiddleCenter, 128);
            _flashMat = Make.Glow(new Color(1f, 0.1f, 0.15f, 0f), false);
            _flashMat.renderQueue = 3900;
            _flashT = Shape(PrimitiveType.Quad, "Red flash", Root, Vector3.zero, Vector3.one, _flashMat).transform;
            NoShadow(_flashT.gameObject);

            Sun(Root, new Vector3(48f, -30f, 0f), new Color(0.75f, 0.6f, 0.8f), 0.28f);
            _storm = Sun(Root, new Vector3(70f, 20f, 0f), new Color(0.8f, 0.8f, 1f), 0f);
            _storm.shadows = LightShadows.None;
            Lamp(Root, new Vector3(30f, 120f, -60f), new Vector3(0, 0, 10f), new Color(1f, 0.85f, 0.85f), 120000f, 35f, 400f);
            Lamp(Root, new Vector3(-40f, 160f, 80f), BossAt, new Color(0.8f, 0.5f, 0.9f), 220000f, 35f, 400f);
            _doom = World.NewLight(Root, "Doom", LightType.Point, new Color(1f, 0.05f, 0.08f), 0f);
            _doom.transform.localPosition = new Vector3(0, 90f, 80f);
            _doom.range = 600f;
            _doom.shadows = LightShadows.None;
        }

        // A bar facing the camera: a dark back, a white lag that drains after a hit, the coloured fill (anchored left).
        Transform Bar(string name, Vector3 at, Vector2 size, out Transform fill, out Material fillMat, out Transform lag)
        {
            var bar = new GameObject(name).transform;
            bar.SetParent(Root, false);
            bar.localPosition = at;
            GameObject back = Make.MeshObject("Back", bar, Make.RoundedBox(new Vector3(size.x + 1.2f, size.y + 1.2f, 0.6f), Mathf.Min(1.2f, size.y * 0.3f)), Make.Lit(new Color(0.03f, 0.02f, 0.03f), 0.6f));
            NoShadow(back);
            lag = Shape(PrimitiveType.Cube, "Lag", bar, new Vector3(0, 0, -0.35f), new Vector3(size.x, size.y, 0.3f), Make.Emissive(Color.white, new Color(1.4f, 1.3f, 1.3f))).transform;
            fillMat = Make.Emissive(Accent, Accent * 1.6f);
            fill = Shape(PrimitiveType.Cube, "Fill", bar, new Vector3(0, 0, -0.5f), new Vector3(size.x, size.y, 0.3f), fillMat).transform;
            NoShadow(lag.gameObject); NoShadow(fill.gameObject);
            return bar;
        }

        static void SetBar(Transform t, float width, float share)
        {
            share = Mathf.Clamp01(share);
            Vector3 s = t.localScale;
            t.localScale = new Vector3(Mathf.Max(0.001f, width * share), s.y, s.z);
            t.localPosition = new Vector3(-width / 2f + width * share / 2f, t.localPosition.y, t.localPosition.z);
        }

        void Stage(int stage, bool instant)
        {
            _stage = Mathf.Clamp(stage, 1, 3);
            _jitter = _stage == 1 ? 0.55f : _stage == 2 ? 1.3f : 2.4f;
            float len = _stage == 1 ? 1f : _stage == 2 ? 1.35f : 1.75f;
            for (int i = 0; i < _spikes.Count; i++) _spikes[i].localScale = new Vector3(1f, len * (0.8f + (i % 4) * 0.12f), 1f);
            Color glow = _stage == 1 ? new Color(0.25f, 0.02f, 0.08f) : _stage == 2 ? new Color(0.6f, 0.04f, 0.1f) : new Color(1.2f, 0.06f, 0.08f);
            _skin.SetColor("_EmissionColor", glow);
        }

        void Bars(ShotView v, bool instant)
        {
            _bossTo = v.Num("bossHp") / Mathf.Max(1f, v.Num("bossMax", 250f));
            _youTo = v.Num("playerHp") / Mathf.Max(1f, v.Num("playerMax", 100f));
            if (instant) { _bossShown = _bossLagShown = _bossTo; _youShown = _youTo; }
            int combo = v.Int("combo");
            for (int i = 0; i < _pips.Count; i++) _pips[i].gameObject.SetActive(i < combo);
        }

        public override void Begin(ShotView start)
        {
            _dead = _lost = false;
            _boss.gameObject.SetActive(true);
            _bossBar.gameObject.SetActive(true);
            _scale = 1f;
            _doom.intensity = 0f;
            Stage(start.Int("stage", 1), true);
            Bars(start, true);
            if (start.Int("won") == 1) { _dead = true; _boss.gameObject.SetActive(false); _bossBar.gameObject.SetActive(false); }
            if (start.Int("won") == -1) Doom();
        }

        public override void Ready(ShotView v)
        {
            _ball.localPosition = new Vector3(0, 0.84f, 0);
            _ball.localRotation = Quaternion.identity;
            _ball.gameObject.SetActive(true);
            if (!_dead && !_lost) Stage(v.Int("stage", 1), false);
        }

        public override IEnumerator Play(ShotView v)
        {
            StrokeData s = StrokeOf(v);
            int dmg = v.Int("dmg"), taken = v.Int("taken");
            bool crit = v.Flag("crit");
            yield return Swing(_putter, s, () => Sound("putter_strike", "golf_iron", 0.55f, 1.2f));   // puttr-app-2: the putter on the ball, shared by every sensor replay (the old sound until it is recorded)
            Arcade.StartCoroutine(Settle(_putter, 0.6f));

            // The shot up the arena.
            _ball.gameObject.SetActive(false);
            Vector3 from = new Vector3(0, 1.5f, 1f);
            Vector3 to = dmg > 0 ? BossAt + new Vector3(0, -2f, -14f) : Vector3.Lerp(from, BossAt, 0.55f) + new Vector3(18f, -30f, 0f);
            float size = dmg > 0 ? Mathf.Clamp(2f + dmg / 9f, 2.4f, 8f) : 1.6f;
            _shot.gameObject.SetActive(true);
            _shotMat.SetColor("_EmissionColor", crit ? new Color(4f, 3f, 0.8f) : dmg > 0 ? new Color(3f, 1.6f, 2f) : new Color(0.8f, 0.6f, 0.6f));
            _shotLight.color = crit ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.6f, 0.75f);
            if (!Has(dmg > 0 ? "theyips_hit" : "theyips_block")) Sound(dmg > 0 ? "synth_whoosh" : "swish", 0.55f, crit ? 0.8f : 1f);   // puttr-app-2: the impact says it once recorded
            var trail = new List<Vector3>();
            float fly = dmg > 0 ? 0.42f : 0.5f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / fly)
            {
                Vector3 p = Vector3.Lerp(from, to, t) + Vector3.up * (dmg > 0 ? 26f : 10f) * 4f * t * (1f - t);
                _shot.localPosition = p;
                _shot.localScale = Vector3.one * Mathf.Lerp(1.4f, size, t) * (dmg > 0 ? 1f : 1f - t * 0.6f);
                trail.Add(p);
                if (trail.Count > 14) trail.RemoveAt(0);
                Points(_trail, trail);
                yield return null;
            }
            _shot.gameObject.SetActive(false);
            _trail.positionCount = 0;

            if (dmg > 0)
            {
                _recoil = 1f;
                _bossTo = v.Num("bossHp") / Mathf.Max(1f, v.Num("bossMax", 250f));
                Arcade.Sparks(At(to), crit ? 70 : 28);
                if (crit) Sound("theyips_crit", "boom", 0.9f); else Sound("theyips_hit", "hammer_hit", 0.7f);   // puttr-app-2: the game's own sound, the shared one until recorded
                if (crit) Arcade.Shake(0.55f);
                _hit.text = "-" + dmg;
                _hit.color = crit ? new Color(1f, 0.85f, 0.25f) : Color.white;
                _hit.characterSize = (crit ? 13f : 9f) * 10f / 128f * 1.4f;
                _hitAge = 0f;
            }
            else Sound("theyips_block", "sizzle", 0.5f);   // puttr-app-2: the game's own sound, the shared one until recorded

            if (v.Int("stage") != v.Int("lastStage") && v.Int("won") == 0) { Sound("theyips_stage", "gong", 0.7f, 0.8f); Stage(v.Int("stage"), false); }   // puttr-app-2: the monster's roar, the shared gong until recorded
            yield return new WaitForSeconds(dmg > 0 ? 0.35f : 0.15f);

            // It strikes back.
            if (taken > 0 && v.Int("won") != 1)
            {
                _roar = 1f;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.22f) { _lunge = Ease(t); yield return null; }
                Sound("theyips_stage", "laser_zap", 0.8f, 0.7f);   // puttr-app-2: the monster's roar, the shared zap and crash until recorded
                if (!Has("theyips_stage")) Sound("crash", 0.5f, 1.3f);
                Arcade.Shake(0.5f);
                _redFlash = 0.55f;
                _youTo = v.Num("playerHp") / Mathf.Max(1f, v.Num("playerMax", 100f));
                for (float t = 0f; t < 0.4f; t += Time.deltaTime)
                {
                    Bolt();
                    yield return null;
                }
                _bolt.positionCount = 0;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f) { _lunge = 1f - Ease(t); yield return null; }
                _lunge = 0f;
            }
            Bars(v, false);

            if (v.Int("won") == 1)
            {
                _dead = true;
                Sound("theyips_victory", "boom", 1f, 0.7f);   // puttr-app-2: the game's own sound, the shared one until recorded
                Arcade.Celebrate(40, At(BossAt));
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
                {
                    _scale = 1f - Ease(t);
                    _jitter = 4f * (1f - t);
                    if (Random.value < 0.4f) Arcade.Sparks(At(BossAt + Random.insideUnitSphere * 14f), 8);
                    yield return null;
                }
                _boss.gameObject.SetActive(false);
                _bossBar.gameObject.SetActive(false);
            }
            else if (v.Int("won") == -1)
            {
                Sound("theyips_defeat", "rock_screech", 0.8f, 0.7f);   // puttr-app-2: the game's own sound, the shared one until recorded
                Doom();
                _roar = 1f;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.7f) { _scale = 1f + 0.4f * Ease(t); yield return null; }
            }
            else yield return new WaitForSeconds(0.3f);
        }

        void Doom()
        {
            _lost = true;
            _doom.intensity = 900000f;
            foreach ((Transform t, Light l, float seed) f in _fires) f.l.color = new Color(1f, 0.1f, 0.1f);
            _jitter = 3f;
        }

        // Jagged red lightning from its mouth to you.
        void Bolt()
        {
            Vector3 a = Root.InverseTransformPoint(_mouth.position), b = new Vector3(4f, 12f, -4f);
            var pts = new List<Vector3>();
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f;
                Vector3 p = Vector3.Lerp(a, b, t);
                if (i > 0 && i < 12) p += Random.insideUnitSphere * 7f;
                pts.Add(p);
            }
            Points(_bolt, pts);
            _bolt.widthMultiplier = Random.Range(0.8f, 2.2f);
        }

        public override void Tick()
        {
            float dt = Time.deltaTime, now = Time.time;
            // The monster: hovering, jittering, twitching hard now and then, recoiling, lunging.
            if (now > _twitchAt) { _twitch = Random.insideUnitSphere * _jitter * 3f; _twitchAt = now + Random.Range(0.3f, 1.4f) / _stage; }
            _twitch = Vector3.Lerp(_twitch, Vector3.zero, dt * 10f);
            Vector3 shake = new Vector3(Mathf.PerlinNoise(now * 9f, 1f) - 0.5f, Mathf.PerlinNoise(2f, now * 9f) - 0.5f, 0f) * _jitter * 2f;
            _recoil = Mathf.Max(0f, _recoil - dt * 2.2f);
            _roar = Mathf.Max(0f, _roar - dt * 1.2f);
            _boss.localPosition = BossAt + Vector3.up * Mathf.Sin(now * 1.3f) * 3f + shake + _twitch + Vector3.forward * (_recoil * 22f - _lunge * 55f) + Vector3.down * _lunge * 12f;
            _body.localRotation = Quaternion.Euler(_twitch.y * 4f + _recoil * -25f, Mathf.Sin(now * 0.7f) * 12f + _twitch.x * 5f, _twitch.z * 4f);
            _body.localScale = Vector3.one * _scale * (1f + 0.04f * Mathf.Sin(now * 5f * _stage) + _recoil * 0.12f);
            _mouth.localScale = new Vector3(14f, 3f + _roar * 7f, 3f);
            _mouthMat.SetColor("_EmissionColor", new Color(0.6f, 0.05f, 0.1f) * (1f + _roar * 5f));
            _eyes.SetColor("_EmissionColor", new Color(4f, 0.6f, 0.4f) * (0.8f + 0.4f * Mathf.PerlinNoise(now * 4f, 7f) + _roar * 2f));

            // Fires flicker; the storm flashes now and then.
            foreach ((Transform t, Light l, float seed) f in _fires)
            {
                float k = 0.85f + 0.3f * Mathf.PerlinNoise(now * 6f, f.seed);
                f.t.localScale = new Vector3(1f, k, 1f);
                f.l.intensity = 25000f * k;
            }
            if (now > _stormAt) { _stormFlash = 1f; _stormAt = now + Random.Range(4f, 9f); }
            _stormFlash = Mathf.Max(0f, _stormFlash - dt * 4f);
            _storm.intensity = _stormFlash * (Mathf.Sin(now * 60f) > 0f ? 1.6f : 0.7f);

            // The bars ease to their values; the white lag drains after.
            _bossShown = Mathf.MoveTowards(_bossShown, _bossTo, dt * 1.5f);
            _bossLagShown = _bossLagShown > _bossShown ? Mathf.MoveTowards(_bossLagShown, _bossShown, dt * 0.35f) : _bossShown;
            _youShown = Mathf.MoveTowards(_youShown, _youTo, dt * 1.2f);
            SetBar(_bossFill, 50f, _bossShown);
            SetBar(_bossLag, 50f, _bossLagShown);
            SetBar(_youFill, 18f, _youShown);
            _youFillMat.SetColor("_BaseColor", _youShown > 0.5f ? ScoreGreen : _youShown > 0.25f ? ScoreAmber : ScoreRed);
            _youFillMat.SetColor("_EmissionColor", (_youShown > 0.5f ? ScoreGreen : _youShown > 0.25f ? ScoreAmber : ScoreRed) * 0.8f);
            for (int i = 0; i < _pips.Count; i++) _pips[i].localScale = new Vector3(1.4f, 1.9f * (0.8f + 0.4f * Mathf.PerlinNoise(now * 8f, i)), 1.4f);

            Camera eye = Eye();
            if (eye != null)
            {
                Quaternion look = Quaternion.LookRotation(eye.transform.forward, eye.transform.up);
                _bossBar.rotation = look;
                _youBar.rotation = look;
                _bossBar.localPosition = new Vector3(BossAt.x + (_boss.localPosition.x - BossAt.x) * 0.3f, BossAt.y + 27f, BossAt.z);
                // The red flash over the whole view.
                _flashT.position = eye.transform.position + eye.transform.forward * (eye.nearClipPlane + 1f);
                _flashT.rotation = eye.transform.rotation;
                float h = 2f * (eye.nearClipPlane + 1f) * Mathf.Tan(eye.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float s = Root.lossyScale.x > 0f ? Root.lossyScale.x : 1f;
                _flashT.localScale = new Vector3(h * eye.aspect * 1.2f, h * 1.2f, 1f) / s;
            }
            _redFlash = Mathf.Max(0f, _redFlash - dt * 1.4f);
            _flashMat.SetColor("_Color", new Color(1f, 0.08f, 0.12f, _redFlash));
            _flashT.gameObject.SetActive(_redFlash > 0.001f);

            // The damage number flies up off the monster and fades.
            if (_hitAge < 1.2f)
            {
                _hitAge += dt;
                float u = _hitAge / 1.2f;
                _hit.transform.localPosition = BossAt + new Vector3(16f, 10f + 26f * Ease(Mathf.Min(1f, u * 1.4f)), -18f);
                Color c = _hit.color;
                _hit.color = new Color(c.r, c.g, c.b, u < 0.7f ? 1f : 1f - (u - 0.7f) / 0.3f);
                Face(_hit.transform);
            }
            else _hit.text = "";
        }
    }
}
