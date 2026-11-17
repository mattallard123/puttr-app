// Waltz, the Ballroom: a polished dark floor under a chandelier, three tall light columns (BACK, TOP, STRIKE) that
// flash with the clicks (back, top, strike, then a rest), and between putts a ghost putter dancing the target rhythm
// to them. Each putt replays its real stroke; then the split bar shows the stroke's back : through against the target
// split, the ratio dial's needle swings to it (the in-time wedge is ±0.2), a pip stays on the dial, and three rings on
// the floor light with the streak, spinning and blazing when the third in a row makes it a waltz.
// Units: inches. The set faces the camera (-Z); the mat's target line runs left to right.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class WaltzScene : SensorStage
    {
        static readonly Color Coral = Make.Hex(0xFF8A65), Warm = new Color(1f, 0.84f, 0.62f), Gold = new Color(0.85f, 0.68f, 0.38f);
        const float SetX = 22f, DialY = 58f, DialZ = 104f, DialR = 20f, BarY = 5f, BarZ = 14f, BarW = 54f;

        Ticker _ticker;
        TextMesh _number, _unit;
        Putter _putter, _ghost;
        Transform _mat, _ball, _needle, _pips, _split, _splitBack, _splitThrough, _targetTick;
        Material _wedge, _backMat, _throughMat;
        readonly Material[] _colMats = new Material[3];
        readonly Light[] _colLights = new Light[3];
        readonly float[] _flash = new float[3];
        readonly Transform[] _rings = new Transform[3];
        readonly Material[] _ringMats = new Material[3];
        float[] _gaps = { 0.55f, 0.28f, 1.25f };
        float _target = 2f, _needleDeg = 90f, _spin;
        int _lit;
        bool _playing, _waltz;
        Vector3 _ballHome;

        public override Vector3 CameraFrom => Portrait ? At(10f, 64f, -190f) : At(4f, 56f, -146f);
        public override Vector3 CameraLook => Portrait ? At(10f, 50f, 60f) : At(6f, 44f, 60f);
        public override float CameraFov => Portrait ? 40f : 38f;
        public override float ShadowDistance => 400f;

        // ── The set ──────────────────────────────────────────────────────────

        public override void Build()
        {
            // The ballroom: a black mirror floor with a gold inlay, dark red walls, a chandelier.
            GameObject floor = Make.MeshObject("Floor", Root, Make.Disc(600f, 64), Make.Lit(new Color(0.035f, 0.03f, 0.035f), 0.72f, 0.05f));
            floor.transform.localPosition = new Vector3(0, -0.05f, 0);
            Material inlay = Make.Glow(new Color(Gold.r, Gold.g, Gold.b, 0.5f), false);
            foreach (float r in new[] { 60f, 64f })
            {
                GameObject ring = Make.MeshObject("Inlay", Root, Make.Ring(r, r + 0.8f, 96), inlay);
                ring.transform.localPosition = new Vector3(0, 0.05f, 40f);
            }
            Material wall = Make.Lit(new Color(0.16f, 0.04f, 0.05f), 0.35f);
            Shape(PrimitiveType.Cube, "Back wall", Root, new Vector3(0, 120f, 130f), new Vector3(600f, 260f, 4f), wall);
            Material drape = Make.Lit(new Color(0.24f, 0.05f, 0.07f), 0.2f);
            for (int i = -4; i <= 4; i++)
                Shape(PrimitiveType.Cube, "Drape", Root, new Vector3(i * 30f, 100f, 127f), new Vector3(10f, 200f, 3f), drape);
            Material goldM = Make.Lit(Gold, 0.85f, 1f);
            Shape(PrimitiveType.Cube, "Rail", Root, new Vector3(0, 46f, 126f), new Vector3(600f, 1.4f, 2f), goldM);

            // The chandelier.
            Transform ch = new GameObject("Chandelier").transform;
            ch.SetParent(Root, false);
            ch.localPosition = new Vector3(SetX - 150f, 150f, 40f);   // out of frame up and left: its light still warms the room
            Shape(PrimitiveType.Sphere, "Crown", ch, Vector3.zero, new Vector3(9f, 7f, 9f), goldM);
            Shape(PrimitiveType.Cube, "Chain", ch, new Vector3(0, 30f, 0), new Vector3(0.6f, 60f, 0.6f), goldM);
            Material bulb = Make.Emissive(Warm, Warm * 3.5f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                Vector3 at = new Vector3(Mathf.Cos(a) * 14f, -4f + (i % 2) * 3f, Mathf.Sin(a) * 14f);
                Shape(PrimitiveType.Sphere, "Bulb", ch, at, Vector3.one * 2f, bulb);
            }
            GameObject chRing = Make.MeshObject("Arm ring", ch, Make.Ring(13f, 15f, 64), goldM);
            chRing.transform.localPosition = new Vector3(0, -5f, 0);
            Light glow = World.NewLight(ch, "Chandelier light", LightType.Point, Warm, 9000f);
            glow.transform.localPosition = new Vector3(0, -10f, 0);
            glow.range = 220f;

            // The three columns of light.
            string[] names = { "BACK", "TOP", "STRIKE" };
            for (int i = 0; i < 3; i++)
            {
                float x = SetX + (i - 1) * 26f;
                _colMats[i] = Make.Emissive(new Color(0.25f, 0.1f, 0.08f), Coral * 0.15f, 0.9f);
                Cylinder("Column " + names[i], Root, new Vector3(x, 21f, 96f), 3.4f, 40f, _colMats[i]);
                Cylinder("Plinth", Root, new Vector3(x, 1.5f, 96f), 5.2f, 3f, goldM);
                Cylinder("Capital", Root, new Vector3(x, 42f, 96f), 4.8f, 2f, goldM);
                _colLights[i] = World.NewLight(Root, "Column light", LightType.Point, Coral, 0f);
                _colLights[i].transform.localPosition = new Vector3(x, 24f, 88f);
                _colLights[i].range = 70f;
                Words(Root, names[i], new Vector3(x, 5.5f, 90f), 2.8f, new Color(1f, 1f, 1f, 0.75f)).name = "Column label";
            }

            // The ratio dial on the wall.
            Transform dial = new GameObject("Dial").transform;
            dial.SetParent(Root, false);
            dial.localPosition = new Vector3(SetX, DialY, DialZ);
            Make.MeshObject("Dial face", dial, Arc(0f, DialR + 5f, 0f, 180f, 48), Make.Glow(new Color(0.03f, 0.02f, 0.03f, 0.95f), false)).transform.localPosition = new Vector3(0, 0, 0.8f);
            Make.MeshObject("Dial rim", dial, Arc(DialR + 5f, DialR + 6.2f, 0f, 180f, 48), Make.Glow(new Color(Gold.r, Gold.g, Gold.b, 1f) * 0.9f, false)).transform.localPosition = new Vector3(0, 0, 0.7f);
            Make.MeshObject("Track", dial, Arc(DialR - 0.8f, DialR + 0.8f, 0f, 180f, 64), Make.Glow(new Color(1f, 1f, 1f, 0.22f), false)).transform.localPosition = new Vector3(0, 0, 0.5f);
            _wedge = Make.Glow(new Color(Coral.r, Coral.g, Coral.b, 1f) * 1.2f, false);
            Make.MeshObject("In time", dial, Arc(DialR - 3.8f, DialR + 3.8f, 90f - 18f, 90f + 18f, 10), _wedge).transform.localPosition = new Vector3(0, 0, 0.4f);
            for (int k = -2; k <= 2; k++)
            {
                float deg = 90f - k * 45f;
                Make.MeshObject("Tick", dial, Arc(DialR + 1.8f, DialR + 4f, deg - 0.9f, deg + 0.9f, 1), Make.Glow(new Color(1f, 1f, 1f, 0.7f), false)).transform.localPosition = new Vector3(0, 0, 0.3f);
            }
            _pips = new GameObject("Pips").transform;
            _pips.SetParent(dial, false);
            _pips.localPosition = new Vector3(0, 0, 0.1f);
            _needle = new GameObject("Needle").transform;
            _needle.SetParent(dial, false);
            _needle.localPosition = new Vector3(0, 0, -0.5f);
            Shape(PrimitiveType.Cube, "Needle bar", _needle, new Vector3(0, DialR * 0.48f, 0), new Vector3(0.8f, DialR * 0.96f, 0.4f), Make.Emissive(Color.white, Color.white * 1.4f));
            Cylinder("Hub", dial, new Vector3(0, 0, -0.6f), 1.8f, 1f, goldM).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            Make.MeshObject("Number plate", dial, Make.RoundedBox(new Vector3(26f, 9f, 0.6f), 2f), Make.Lit(new Color(0.03f, 0.02f, 0.03f), 0.9f)).transform.localPosition = new Vector3(0, -6f, 0.9f);
            _number = Words(dial, "", new Vector3(3f, -5.8f, 0.4f), 6f, Color.white, TextAnchor.MiddleRight, 160);
            _unit = Words(dial, "", new Vector3(3.8f, -6.6f, 0.4f), 3f, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);

            // The split bar: the stroke's back : through against the target split (a white mark).
            _split = new GameObject("Split").transform;
            _split.SetParent(Root, false);
            _split.localPosition = new Vector3(SetX - 16f, BarY, BarZ);
            Shape(PrimitiveType.Cube, "Split track", _split, new Vector3(0, 0, 0.4f), new Vector3(BarW + 1.4f, 4.4f, 0.4f), Make.Glow(new Color(0.02f, 0.02f, 0.03f, 0.85f), false));
            _backMat = Make.Glow(new Color(Coral.r, Coral.g, Coral.b, 1f) * 1.1f, false);
            _throughMat = Make.Glow(new Color(1f, 1f, 1f, 0.85f), false);
            _splitBack = Shape(PrimitiveType.Cube, "Back share", _split, Vector3.zero, new Vector3(0.01f, 3.2f, 0.3f), _backMat).transform;
            _splitThrough = Shape(PrimitiveType.Cube, "Through share", _split, Vector3.zero, new Vector3(0.01f, 3.2f, 0.3f), _throughMat).transform;
            _targetTick = Shape(PrimitiveType.Cube, "Target split", _split, Vector3.zero, new Vector3(0.6f, 7.5f, 0.2f), Make.Glow(new Color(1f, 0.95f, 0.6f, 1f) * 1.4f, false)).transform;
            Words(_split, "BACK", new Vector3(-BarW / 2f, -4.4f, 0), 2.2f, new Color(1f, 0.75f, 0.65f, 0.85f), TextAnchor.MiddleLeft).name = "Back";
            Words(_split, "THROUGH", new Vector3(BarW / 2f, -4.4f, 0), 2.2f, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleRight).name = "Through";
            foreach (Transform t in new[] { _splitBack, _splitThrough, _targetTick }) NoShadow(t.gameObject);

            // Three rings on the floor for the streak.
            for (int i = 0; i < 3; i++)
            {
                _ringMats[i] = Make.Glow(new Color(1f, 1f, 1f, 0.12f), false);
                _rings[i] = Make.MeshObject("Streak ring " + (i + 1), Root, Make.Ring(5.2f, 7f, 6 * 8), _ringMats[i]).transform;
                _rings[i].localPosition = new Vector3(SetX - 24f + i * 24f, 0.15f, 50f);
                for (int d = 0; d < 6; d++)
                {
                    Transform dot = Shape(PrimitiveType.Cube, "Step", _rings[i], new Vector3(Mathf.Cos(d * 60f * Mathf.Deg2Rad) * 6.1f, 0.05f, Mathf.Sin(d * 60f * Mathf.Deg2Rad) * 6.1f), new Vector3(1.4f, 0.1f, 1.4f), _ringMats[i]).transform;
                    NoShadow(dot.gameObject);
                }
            }

            // The mat, your putter, the ghost putter, the ball.
            _mat = new GameObject("Mat").transform;
            _mat.SetParent(Root, false);
            _mat.localPosition = new Vector3(38f, 0, -26f);
            _mat.localRotation = Quaternion.Euler(0, -90f, 0);   // the putter on the right, rolling toward the middle
            Make.Carpet(out Texture2D pile, out Texture2D pileN);
            Shape(PrimitiveType.Cube, "Mat", _mat, new Vector3(0, 0.25f, 22f), new Vector3(18f, 0.5f, 96f), Make.Lit(new Color(0.16f, 0.48f, 0.24f), 0.12f, 0f, pile, pileN, 0.9f));
            _putter = BuildPutter(_mat, new Vector3(0, 0.5f, -2.2f), Coral, 36f, 1.6f);
            _putter.FaceScale = 2f;
            _ghost = BuildPutter(_mat, new Vector3(0, 0.5f, -2.2f), Coral, 36f, 1.6f);
            Material ghost = Make.Glow(new Color(1f, 0.6f, 0.45f, 0.55f), true);
            foreach (MeshRenderer r in _ghost.Pivot.GetComponentsInChildren<MeshRenderer>()) { r.sharedMaterial = ghost; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            _ballHome = new Vector3(0, 0.5f, 1.6f);
            _ball = BuildBall(_mat, _ballHome, 1.4f);


            Sun(Root, new Vector3(48f, -15f, 0f), new Color(1f, 0.85f, 0.7f), 0.32f);
            Lamp(Root, new Vector3(30f, 220f, -120f), new Vector3(-6f, 0f, 10f), Warm, 170000f, 46f, 700f);
            Lamp(Root, new Vector3(-60f, 180f, 20f), new Vector3(SetX, 40f, DialZ), new Color(1f, 0.7f, 0.6f), 60000f, 40f, 500f);

            _ticker = new Ticker(Root) { Volume = 0.75f, Pitches = new[] { 1.3f, 1f, 1.1f } };
            _ticker.Beat += step => { if (step >= 0 && step < 3) _flash[step] = 1f; };
            SetPutters(false);
        }

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

        float DegFor(float ratio) => 180f * (1f - Mathf.Clamp01((ratio - (_target - 1f)) / 2f));

        // The split bar: back share (coral) from the left, through share (white) to the right.
        void Split(float backShare)
        {
            backShare = Mathf.Clamp01(backShare);
            float wb = BarW * backShare, wt = BarW - wb;
            _splitBack.localScale = new Vector3(Mathf.Max(0.01f, wb), 3.2f, 0.3f);
            _splitBack.localPosition = new Vector3(-BarW / 2f + wb / 2f, 0, 0);
            _splitThrough.localScale = new Vector3(Mathf.Max(0.01f, wt), 3.2f, 0.3f);
            _splitThrough.localPosition = new Vector3(BarW / 2f - wt / 2f, 0, 0);
        }

        void SetPutters(bool mine)
        {
            _putter.Pivot.gameObject.SetActive(mine);
            _ghost.Pivot.gameObject.SetActive(!mine);
        }

        void Lay(ShotView v)
        {
            _target = v.Num("target", 2f);
            _targetTick.localPosition = new Vector3(-BarW / 2f + BarW * _target / (_target + 1f), 0, -0.2f);
            for (int i = _pips.childCount - 1; i >= 0; i--) Object.Destroy(_pips.GetChild(i).gameObject);
            float[] ratios = Floats(v, "ratios");
            bool[] ok = Flags(v, "inTimes");
            for (int i = 0; i < ratios.Length; i++)
            {
                float a = DegFor(ratios[i]) * Mathf.Deg2Rad;
                bool good = i < ok.Length && ok[i];
                NoShadow(Shape(PrimitiveType.Sphere, "Pip", _pips, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * DialR, Vector3.one * 1.5f,
                    Make.Glow(good ? new Color(1f, 0.7f, 0.58f, 1f) : new Color(1f, 1f, 1f, 0.35f), false)));
            }
            Rings(v.Int("streak"));
            float[] gaps = v.Get<float[]>("gaps");
            if (gaps != null && gaps.Length == 3) _gaps = gaps;
            _ticker.Muted = Backdrop;
            _ticker.Start(_gaps);
        }

        void Rings(int streak)
        {
            _lit = Mathf.Min(3, streak);
            _waltz = streak >= 3;
            for (int i = 0; i < 3; i++)
                _ringMats[i].SetColor("_Color", i < _lit ? new Color(Coral.r, Coral.g, Coral.b, 1f) * (_waltz ? 1.8f : 1.2f) : new Color(1f, 1f, 1f, 0.12f));
        }

        // ── The game ─────────────────────────────────────────────────────────

        public override void Begin(ShotView start)
        {
            Lay(start);
            Split(_target / (_target + 1f));
            _splitBack.gameObject.SetActive(false);
            _splitThrough.gameObject.SetActive(false);
            _needle.localRotation = Quaternion.identity;
            _needleDeg = 90f;
            SetPutters(false);
        }

        public override void Ready(ShotView v)
        {
            _ball.localPosition = _ballHome + Vector3.up * 1.4f;
            _ball.gameObject.SetActive(true);
            Lay(v);
            SetPutters(false);
            _playing = false;
        }

        public override void Tick()
        {
            _ticker.Muted = Backdrop;
            _ticker.Tick();
            for (int i = 0; i < 3; i++)
            {
                _flash[i] = Mathf.Max(0f, _flash[i] - Time.deltaTime * 3.2f);
                float f = _flash[i];
                _colMats[i].SetColor("_EmissionColor", Coral * (0.15f + 2.6f * f));
                _colLights[i].intensity = 2500f * f;
            }
            if (!_playing) Dance();
            if (_waltz)
            {
                _spin += Time.deltaTime * 90f;
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f);
                for (int i = 0; i < 3; i++) { _rings[i].localRotation = Quaternion.Euler(0, _spin * (i % 2 == 0 ? 1f : -1f), 0); _rings[i].localScale = Vector3.one * pulse; }
            }
            else for (int i = 0; i < 3; i++) { _rings[i].localRotation = Quaternion.identity; _rings[i].localScale = Vector3.one; }
        }

        // The ghost dances the target: back from click 1 to click 2, down to the strike on click 3, through and home in the rest.
        void Dance()
        {
            if (_ticker.LastAt < 0) { Pose(_ghost, 0f, 0f); return; }
            float top = AngleFor(_ghost, 10f), since = _ticker.Since;
            switch (_ticker.LastStep)
            {
                case 0: { float u = Mathf.Clamp01(since / _gaps[0]); Pose(_ghost, top * (1f - Mathf.Cos(u * Mathf.PI)) / 2f, -1.5f * u); break; }
                case 1: { float u = Mathf.Clamp01(since / _gaps[1]); Pose(_ghost, top * Mathf.Cos(u * Mathf.PI / 2f), -1.5f * (1f - u)); break; }
                default:
                {
                    float follow = _gaps[1] * 1.1f;
                    if (since < follow) Pose(_ghost, -top * 0.85f * Mathf.Sin(since / follow * Mathf.PI / 2f), 0f);
                    else Pose(_ghost, -top * 0.85f * (1f - Ease(Mathf.Clamp01((since - follow) / 0.5f))), 0f);
                    break;
                }
            }
        }

        public override IEnumerator Play(ShotView v)
        {
            StrokeData s = StrokeOf(v);
            if (!s.Has) yield break;
            _playing = true;
            SetPutters(true);
            Pose(_putter, 0f, 0f);
            // The split bar fills as the stroke goes: coral while going back, white coming through.
            _splitBack.gameObject.SetActive(true);
            _splitThrough.gameObject.SetActive(true);
            float total = Mathf.Max(0.05f, s.Back + s.Fwd), share = s.Back / total;
            Split(0f);
            _splitThrough.localScale = new Vector3(0.01f, 3.2f, 0.3f);
            yield return Swing(_putter, s, () => Sound("swish", 0.25f, 1.4f), 1f, (t, phase) =>
            {
                if (phase == 0) { float wb = BarW * share * t; _splitBack.localScale = new Vector3(Mathf.Max(0.01f, wb), 3.2f, 0.3f); _splitBack.localPosition = new Vector3(-BarW / 2f + wb / 2f, 0, 0); }
                else if (phase == 1) { float wb = BarW * share, wt = (BarW - wb) * t; _splitThrough.localScale = new Vector3(Mathf.Max(0.01f, wt), 3.2f, 0.3f); _splitThrough.localPosition = new Vector3(-BarW / 2f + wb + wt / 2f, 0, 0); }
            });
            Split(share);
            // The ball runs off down the mat while the dial's needle swings to the ratio.
            float from = _needleDeg, to = DegFor(s.Rhythm);
            float roll = v.Made ? 40f : 26f + Mathf.Clamp(v.RolloutIn, -10f, 20f) * 0.6f;
            Vector3 b0 = _ball.localPosition, b1 = b0 + new Vector3(0, 0, Mathf.Max(8f, roll));
            Vector3 last = b0;
            for (float t = 0f; t < 1.3f; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / 1.05f);
                Vector3 p = Vector3.Lerp(b0, b1, 1f - (1f - k) * (1f - k));
                if ((p - last).sqrMagnitude > 1e-6f) _ball.localRotation = Quaternion.AngleAxis((p - last).magnitude / 1.4f * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, (p - last).normalized)) * _ball.localRotation;
                _ball.localPosition = p;
                last = p;
                _needleDeg = to + (from - to) * Mathf.Exp(-3.6f * t) * Mathf.Cos(9f * t);
                _needle.localRotation = Quaternion.Euler(0, 0, _needleDeg - 90f);
                if (t > 0.3f) _putter.Pivot.localRotation = Quaternion.Euler(_putter.Angle * Mathf.Max(0f, 1f - (t - 0.3f) * 1.8f), 0, 0);
                yield return null;
            }
            _needleDeg = to;
            _needle.localRotation = Quaternion.Euler(0, 0, to - 90f);
            _ball.gameObject.SetActive(!v.Made);

            bool inTime = v.Flag("inTime"), waltz = v.Flag("waltz");
            Color c = inTime ? new Color(1f, 0.7f, 0.58f) : v.Num("close") > 0f ? ScoreAmber : ScoreRed;
            _number.text = s.Rhythm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            _unit.text = ":1";
            _number.color = c;
            Rings(v.Int("streak"));
            if (inTime)
            {
                _wedge.SetColor("_Color", new Color(1f, 0.8f, 0.7f, 1f) * 1.8f);
                Vector3 ring = _rings[Mathf.Clamp(_lit - 1, 0, 2)].position;
                if (Arcade != null) Arcade.Sparks(ring + Vector3.up * 2f, 16);
                Sound("bell_ding", 0.3f, waltz ? 1.5f : 1.2f);
            }
            if (waltz)
            {
                if (Arcade != null) Arcade.Celebrate(30, At(SetX, 12f, 50f));
                Sound("riser", 0.35f, 1.1f);
            }
            yield return new WaitForSeconds(0.55f);
            _wedge.SetColor("_Color", new Color(Coral.r, Coral.g, Coral.b, 1f) * 1.2f);
            yield return Settle(_putter, 0.25f);
        }
    }
}
