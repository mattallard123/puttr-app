// Pendulum, the Clock Tower: a grand dark hall and a brass clock whose pendulum swings to the target beat (one swing
// per beat, a click at each end), under a standing tempo dial: target ±20 BPM, the pocket (±3) a lit blue wedge.
// Each putt replays its real stroke on the mat at the clock's foot while the clock keeps ticking, so the stroke is
// seen against the beat; then the dial's needle swings to the stroke's tempo, a pip stays on the arc for it, and the
// five streak lamps over the face light to the multiplier (blazing at x5).
// Units: inches. The clock faces the camera (-Z); the mat's target line runs left to right.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class PendulumScene : SensorStage
    {
        static readonly Color Blue = Make.Hex(0x5687FF), Brass = new Color(0.78f, 0.6f, 0.3f), Warm = new Color(1f, 0.82f, 0.6f);
        const float ClockX = 24f, DialY = 52f, DialZ = 84f, DialR = 28f, Swing0 = 20f;

        int _target = 72;
        Ticker _ticker;
        TextMesh _number, _unit;
        Transform _pendulum, _needle, _mat, _ball, _pips, _bobGlow;
        Putter _putter;
        Material _wedge, _bobMat;
        readonly List<Material> _lampMats = new List<Material>();
        readonly List<Transform> _lamps = new List<Transform>();
        readonly List<TextMesh> _labels = new List<TextMesh>();
        Light _blaze;
        int _beats, _lit;
        float _needleDeg = 90f, _flash;
        Vector3 _ballHome;

        public override Vector3 CameraFrom => Portrait ? At(10f, 64f, -190f) : At(4f, 56f, -146f);
        public override Vector3 CameraLook => Portrait ? At(10f, 50f, 60f) : At(6f, 44f, 60f);
        public override float CameraFov => Portrait ? 40f : 38f;
        public override float ShadowDistance => 400f;

        // ── The set ──────────────────────────────────────────────────────────

        public override void Build()
        {
            // The hall: a polished wood floor, a panelled back wall, columns either side.
            Material floor = Make.Lit(new Color(0.2f, 0.13f, 0.09f), 0.82f, 0f, Make.Wood(3), null, 1f);
            GameObject f = Make.MeshObject("Floor", Root, Make.Disc(600f, 64), floor);
            f.transform.localPosition = new Vector3(0, -0.05f, 0);
            Material wall = Make.Lit(new Color(0.07f, 0.06f, 0.08f), 0.3f);
            Shape(PrimitiveType.Cube, "Back wall", Root, new Vector3(0, 120f, 150f), new Vector3(600f, 260f, 4f), wall);
            Material panel = Make.Lit(new Color(0.11f, 0.09f, 0.1f), 0.45f);
            for (int i = -3; i <= 3; i++)
                Shape(PrimitiveType.Cube, "Panel", Root, new Vector3(i * 46f, 60f, 147.5f), new Vector3(38f, 90f, 1.5f), panel);
            Material stone = Make.Lit(new Color(0.18f, 0.16f, 0.15f), 0.35f);
            for (int s = -1; s <= 1; s += 2)
            {
                Cylinder("Column", Root, new Vector3(s * 78f, 90f, 110f), 7f, 180f, stone);
                Cylinder("Column base", Root, new Vector3(s * 78f, 3f, 110f), 9.5f, 6f, stone);
                Light sconce = World.NewLight(Root, "Sconce", LightType.Point, Warm, 2600f);
                sconce.transform.localPosition = new Vector3(s * 66f, 70f, 100f);
                sconce.range = 120f;
                Shape(PrimitiveType.Sphere, "Sconce bulb", Root, new Vector3(s * 68f, 70f, 102f), Vector3.one * 3.2f, Make.Emissive(Warm, Warm * 3f));
            }

            // The clock: everything in its own frame, a little right of centre (the tee sign covers the top left).
            Transform clock = new GameObject("Clock").transform;
            clock.SetParent(Root, false);
            clock.localPosition = new Vector3(ClockX, 0, DialZ);
            Material wood = Make.Lit(new Color(0.24f, 0.13f, 0.07f), 0.7f, 0f, Make.Wood(9), null, 1f);
            Material brass = Make.Lit(Brass, 0.85f, 1f);
            Make.MeshObject("Case", clock, Make.RoundedBox(new Vector3(92f, 92f, 14f), 4f), wood).transform.localPosition = new Vector3(0, 46f, 9f);
            Shape(PrimitiveType.Cube, "Crown", clock, new Vector3(0, 93f, 9f), new Vector3(100f, 4f, 18f), wood);
            Make.MeshObject("Window", clock, Make.RoundedBox(new Vector3(46f, 26f, 1f), 3f), Make.Lit(new Color(0.03f, 0.035f, 0.05f), 0.95f)).transform.localPosition = new Vector3(0, 21f, 1.6f);
            Shape(PrimitiveType.Cube, "Window trim", clock, new Vector3(0, 34.6f, 1.5f), new Vector3(48f, 1f, 1.2f), brass);
            Shape(PrimitiveType.Cube, "Window trim", clock, new Vector3(0, 7.4f, 1.5f), new Vector3(48f, 1f, 1.2f), brass);

            // The dial: a dark face, the track, the pocket wedge, ticks and their BPM, the stroke's BPM under the hub.
            Transform dial = new GameObject("Dial").transform;
            dial.SetParent(clock, false);
            dial.localPosition = new Vector3(0, DialY, 0);
            Make.MeshObject("Dial face", dial, Arc(0f, DialR + 6f, 0f, 180f, 48), Make.Glow(new Color(0.03f, 0.04f, 0.07f, 0.96f), false)).transform.localPosition = new Vector3(0, 0, 1.2f);
            Make.MeshObject("Dial rim", dial, Arc(DialR + 6f, DialR + 7.6f, 0f, 180f, 48), Make.Glow(new Color(Brass.r, Brass.g, Brass.b, 1f) * 0.9f, false)).transform.localPosition = new Vector3(0, 0, 1.1f);
            Make.MeshObject("Number plate", dial, Make.RoundedBox(new Vector3(30f, 10f, 0.6f), 2f), Make.Lit(new Color(0.03f, 0.04f, 0.07f), 0.9f)).transform.localPosition = new Vector3(0, -6.5f, 1.0f);
            Make.MeshObject("Track", dial, Arc(DialR - 1f, DialR + 1f, 0f, 180f, 64), Make.Glow(new Color(1f, 1f, 1f, 0.22f), false)).transform.localPosition = new Vector3(0, 0, 0.6f);
            _wedge = Make.Glow(new Color(Blue.r, Blue.g, Blue.b, 1f) * 1.3f, false);
            Make.MeshObject("Pocket", dial, Arc(DialR - 4.5f, DialR + 4.5f, 90f - 180f * 3f / 40f, 90f + 180f * 3f / 40f, 12), _wedge).transform.localPosition = new Vector3(0, 0, 0.5f);
            _pips = new GameObject("Pips").transform;
            _pips.SetParent(dial, false);
            _pips.localPosition = new Vector3(0, 0, 0.3f);
            for (int k = -2; k <= 2; k++)
            {
                float deg = 90f - k * 45f;
                Vector3 dir = new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad), 0);
                Make.MeshObject("Tick", dial, Arc(DialR + 2.2f, DialR + (k % 2 == 0 ? 5.5f : 4f), deg - 0.8f, deg + 0.8f, 1), Make.Glow(new Color(1f, 1f, 1f, 0.75f), false)).transform.localPosition = new Vector3(0, 0, 0.4f);
                TextMesh lab = Words(dial, "", dir * (DialR - 8f) + new Vector3(0, k == 2 || k == -2 ? 2.5f : 0f, -0.2f), k % 2 == 0 ? 3.6f : 2.8f, new Color(1f, 1f, 1f, k % 2 == 0 ? 0.8f : 0.5f));
                lab.name = "Label " + k;
                _labels.Add(lab);
            }
            _number = Words(dial, "", new Vector3(3.5f, -6.3f, 0.4f), 6.5f, Color.white, TextAnchor.MiddleRight, 160);
            _unit = Words(dial, "BPM", new Vector3(4.5f, -7.3f, 0.4f), 2.6f, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
            _unit.text = "";
            // The needle, from the hub.
            _needle = new GameObject("Needle").transform;
            _needle.SetParent(dial, false);
            _needle.localPosition = new Vector3(0, 0, -0.6f);
            Shape(PrimitiveType.Cube, "Needle bar", _needle, new Vector3(0, DialR * 0.48f, 0), new Vector3(0.9f, DialR * 0.96f, 0.4f), Make.Emissive(Color.white, Color.white * 1.4f));
            Cylinder("Hub", dial, new Vector3(0, 0, -0.7f), 2.2f, 1.2f, brass).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            // Streak lamps in a row on the floor in front of the clock, x1 to x5.
            for (int i = 0; i < 5; i++)
            {
                Vector3 at = new Vector3(-36f + i * 18f, 3.8f, -18f);
                Material m = Make.Emissive(new Color(0.12f, 0.14f, 0.2f), Color.black, 0.9f);
                _lampMats.Add(m);
                _lamps.Add(Shape(PrimitiveType.Sphere, "Lamp " + (i + 1), clock, at, Vector3.one * 5.4f, m).transform);
                Cylinder("Lamp cup", clock, at + new Vector3(0, -3f, 0.4f), 3f, 1.2f, brass);
                Words(clock, "×" + (i + 1), at + new Vector3(0, -0.6f, -4.2f), 2.8f, new Color(1f, 1f, 1f, 0.7f)).name = "Lamp label";
            }
            _blaze = World.NewLight(clock, "Blaze", LightType.Point, Blue, 0f);
            _blaze.transform.localPosition = new Vector3(0f, 30f, -36f);
            _blaze.range = 140f;

            // The pendulum, swinging in the window from under the dial.
            _pendulum = new GameObject("Pendulum").transform;
            _pendulum.SetParent(clock, false);
            _pendulum.localPosition = new Vector3(0, 37f, -0.5f);
            Shape(PrimitiveType.Cube, "Rod", _pendulum, new Vector3(0, -11f, 0), new Vector3(0.8f, 22f, 0.5f), brass);
            _bobMat = Make.Emissive(Brass, Color.black, 0.9f);
            Cylinder("Bob", _pendulum, new Vector3(0, -23f, 0), 4.6f, 1.4f, _bobMat).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            _bobGlow = Make.MeshObject("Bob glow", _pendulum, Make.Disc(7f, 40), Make.Glow(new Color(Blue.r, Blue.g, Blue.b, 0f))).transform;
            _bobGlow.localPosition = new Vector3(0, -23f, -1.4f);
            _bobGlow.localRotation = Quaternion.Euler(-90f, 0, 0);

            // The mat at the clock's foot (its target line runs left to right), the putter and the ball.
            _mat = new GameObject("Mat").transform;
            _mat.SetParent(Root, false);
            _mat.localPosition = new Vector3(38f, 0, -26f);
            _mat.localRotation = Quaternion.Euler(0, -90f, 0);   // the putter on the right, rolling toward the middle
            Make.Carpet(out Texture2D pile, out Texture2D pileN);
            Shape(PrimitiveType.Cube, "Mat", _mat, new Vector3(0, 0.25f, 22f), new Vector3(18f, 0.5f, 96f), Make.Lit(new Color(0.16f, 0.48f, 0.24f), 0.12f, 0f, pile, pileN, 0.9f));
            _putter = BuildPutter(_mat, new Vector3(0, 0.5f, -2.2f), Blue, 36f, 1.6f);
            _putter.FaceScale = 2f;
            _ballHome = new Vector3(0, 0.5f, 1.6f);
            _ball = BuildBall(_mat, _ballHome, 1.4f);


            Sun(Root, new Vector3(50f, 20f, 0f), new Color(1f, 0.9f, 0.78f), 0.35f);
            Lamp(Root, new Vector3(-40f, 230f, -60f), new Vector3(ClockX, 45f, DialZ), Warm, 160000f, 40f, 600f);
            Lamp(Root, new Vector3(30f, 170f, -120f), new Vector3(24f, 0f, -30f), new Color(0.9f, 0.95f, 1f), 100000f, 36f, 400f);

            _ticker = new Ticker(Root) { Volume = 0.75f };
            _ticker.Beat += _ => { _beats++; _flash = 1f; };
        }

        // An arc band in the XY plane facing -Z (both sides drawn), degrees counterclockwise from +X.
        static Mesh Arc(float rIn, float rOut, float deg0, float deg1, int segs)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Lerp(deg0, deg1, i / (float)segs) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(d * rIn); v.Add(d * rOut);
            }
            for (int i = 0; i < segs; i++)
            {
                int a = i * 2;
                t.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2, a, a + 3, a + 1, a, a + 2, a + 3 });
            }
            var m = new Mesh { name = "Arc" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            return m;
        }

        float DegFor(float bpm) => 180f * (1f - Mathf.Clamp01((bpm - (_target - 20f)) / 40f));

        void Lay(ShotView v)
        {
            _target = v.Int("target", 72);
            string[] texts = { (_target - 20) + "", (_target - 10) + "", _target + "", (_target + 10) + "", (_target + 20) + "" };
            for (int i = 0; i < _labels.Count; i++) _labels[i].text = texts[i];
            // Tempo pips for every stroke so far.
            for (int i = _pips.childCount - 1; i >= 0; i--) Object.Destroy(_pips.GetChild(i).gameObject);
            float[] tempos = Floats(v, "tempos");
            bool[] pockets = Flags(v, "pockets");
            for (int i = 0; i < tempos.Length; i++)
            {
                float a = DegFor(tempos[i]) * Mathf.Deg2Rad;
                bool good = i < pockets.Length && pockets[i];
                Transform p = Shape(PrimitiveType.Sphere, "Pip", _pips, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * DialR, Vector3.one * 1.7f,
                    Make.Glow(good ? new Color(0.55f, 0.72f, 1f, 1f) : new Color(1f, 1f, 1f, 0.35f), false)).transform;
                NoShadow(p.gameObject);
            }
            Lamps(v.Int("streak"), false);
        }

        void Lamps(int streak, bool fanfare)
        {
            _lit = Mathf.Min(5, streak);
            for (int i = 0; i < 5; i++)
            {
                bool on = i < _lit;
                _lampMats[i].SetColor("_BaseColor", on ? Blue : new Color(0.12f, 0.14f, 0.2f));
                _lampMats[i].SetColor("_EmissionColor", on ? Blue * (_lit >= 5 ? 3.2f : 1.9f) : Color.black);
                if (fanfare && on && i == _lit - 1 && Arcade != null) Arcade.Sparks(_lamps[i].position, 14);
            }
        }

        // ── The game ─────────────────────────────────────────────────────────

        public override void Begin(ShotView start)
        {
            Lay(start);
            _needleDeg = 90f;
            _needle.localRotation = Quaternion.Euler(0, 0, 0);
            _number.text = ""; _unit.text = "";
            _ticker.Muted = Backdrop;
            _ticker.Start((float)_target);
        }

        public override void Ready(ShotView v)
        {
            _ball.localPosition = _ballHome + Vector3.up * 1.4f;
            _ball.gameObject.SetActive(true);
            if (v.Int("target", _target) != _target) { _target = v.Int("target"); _ticker.Start((float)_target); }
        }

        public override void Tick()
        {
            _ticker.Muted = Backdrop;
            _ticker.Tick();
            // One swing per beat: at an end on each click, through the bottom halfway between.
            float u = Mathf.Clamp01(_ticker.Since / Mathf.Max(0.1f, _ticker.Gap));
            float side = _beats % 2 == 0 ? 1f : -1f;
            if (_ticker.LastAt < 0) { side = 1f; u = 0f; }
            _pendulum.localRotation = Quaternion.Euler(0, 0, side * Swing0 * Mathf.Cos(u * Mathf.PI));
            _flash = Mathf.Max(0f, _flash - Time.deltaTime * 4f);
            _bobGlow.GetComponent<MeshRenderer>().sharedMaterial.SetColor("_Color", new Color(Blue.r, Blue.g, Blue.b, 0.55f * _flash));
            _bobMat.SetColor("_EmissionColor", Brass * (0.15f + 0.9f * _flash));
            float pulse = _lit >= 5 ? 0.5f + 0.5f * Mathf.Sin(Time.time * 7f) : 0f;
            _blaze.intensity = _lit >= 5 ? 9000f + 7000f * pulse : 0f;
            if (_lit >= 5) for (int i = 0; i < 5; i++) _lamps[i].localScale = Vector3.one * (4.6f + 0.6f * pulse);
        }

        public override IEnumerator Play(ShotView v)
        {
            StrokeData s = StrokeOf(v);
            if (!s.Has) yield break;
            bool struck = false;
            yield return Swing(_putter, s, () => { struck = true; Sound("swish", 0.25f, 1.4f); });
            // The ball runs off down the mat while the needle swings to the stroke's tempo and settles.
            float from = _needleDeg, to = DegFor(s.Tempo);
            float roll = v.Made ? 46f : 30f + Mathf.Clamp(v.RolloutIn, -10f, 20f) * 0.6f;
            Vector3 b0 = _ball.localPosition, b1 = b0 + new Vector3(0, 0, Mathf.Max(8f, roll));
            Vector3 last = b0;
            const float spell = 1.4f;
            for (float t = 0f; t < spell; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / 1.1f);
                Vector3 p = Vector3.Lerp(b0, b1, 1f - (1f - k) * (1f - k));
                if (struck && (p - last).sqrMagnitude > 1e-6f) _ball.localRotation = Quaternion.AngleAxis((p - last).magnitude / 1.4f * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, (p - last).normalized)) * _ball.localRotation;
                _ball.localPosition = p;
                last = p;
                float ring = Mathf.Exp(-3.6f * t) * Mathf.Cos(9f * t);
                _needleDeg = to + (from - to) * ring;
                _needle.localRotation = Quaternion.Euler(0, 0, _needleDeg - 90f);
                if (t > 0.35f && _putter.Angle != 0f) _putter.Pivot.localRotation = Quaternion.Euler(_putter.Angle * Mathf.Max(0f, 1f - (t - 0.35f) * 1.6f), 0, 0);
                yield return null;
            }
            _needleDeg = to;
            _needle.localRotation = Quaternion.Euler(0, 0, to - 90f);
            _ball.gameObject.SetActive(!v.Made);
            yield return Settle(_putter, 0.3f);

            bool pocket = v.Flag("inPocket");
            Color c = pocket ? Blue : v.Num("close") > 0f ? ScoreAmber : ScoreRed;
            _number.text = F1(s.Tempo);
            _unit.text = "BPM";
            _number.color = pocket ? new Color(0.62f, 0.76f, 1f) : c;
            Lay(v);
            Lamps(v.Int("streak"), pocket);
            if (pocket)
            {
                _wedge.SetColor("_Color", new Color(0.7f, 0.82f, 1f, 1f) * 1.8f);
                if (Arcade != null) Arcade.Sparks(At(ClockX, DialY + DialR - 4f, DialZ - 2f), 22);
                Sound("bell_ding", 0.35f, v.Int("mult") >= 5 ? 1.5f : 1.2f);
                if (v.Int("mult") >= 5 && Arcade != null) Arcade.Celebrate(30, At(ClockX, DialY + 20f, DialZ - 10f));
            }
            yield return new WaitForSeconds(0.5f);
            _wedge.SetColor("_Color", new Color(Blue.r, Blue.g, Blue.b, 1f) * 1.3f);
        }
    }
}
