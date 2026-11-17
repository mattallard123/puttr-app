// Carbon Copy, "The Copier": a blueprint lab seen from the side, the target off to the right. Putt 1 is scanned by a
// laser and becomes a green hologram putter that stays: the signature. Every copy after it, the hologram replays the
// signature stroke while your putter replays yours at the same moment, so the two can be compared swing for swing.
// Four gauges stand behind (tempo, length, rhythm, face): the signature is the white line, the green band round it is
// the tolerance (it narrows every 3 copies), and your value slides in as a marker coloured by its match. The match
// ring fills to the copy's match; 90 and over stamps it a Carbon Copy.
// Units: inches. +Z is the target (screen right), the camera stands out on +X.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class CarbonCopyScene : SensorStage
    {
        static readonly Color Accent = Make.Hex(0x4CC162);
        const float GaugeX = -28f, GaugeY = 13f, GaugeH = 8f, GaugeW = 5.6f, RingR = 6.5f, RingY = 13f, RingZ = 40.5f, PutterX = -14f, PutterZ = 3f, PutterScale = 0.65f;
        static readonly float[] GaugeZ = { 9f, 15.6f, 22.2f, 28.8f };
        const int Segs = 60;

        public override Vector3 CameraFrom => Portrait ? At(50f, 40f, 19f) : At(46f, 26f, 21f);
        public override Vector3 CameraLook => Portrait ? At(-22f, 28f, 19f) : At(-22f, 15f, 21f);
        public override float CameraFov => Portrait ? 48f : 36f;
        public override float ShadowDistance => 400f;

        sealed class Gauge
        {
            public Transform Band, Line, Marker;
            public Material MarkerMat;
            public TextMesh Value, Sig;
            public float Unit, Band0 = 1f, BandNow = 1f, BandTo = 1f, MarkFrom, MarkTo;
        }

        readonly Gauge[] _g = new Gauge[4];
        readonly List<Renderer> _segs = new List<Renderer>();
        readonly List<Renderer> _pips = new List<Renderer>();
        readonly List<Transform> _labels = new List<Transform>();
        readonly Dictionary<Color, Material> _glows = new Dictionary<Color, Material>();
        Material _segOff, _pipOff, _ghostMat;
        Putter _you, _ghost;
        Transform _ball, _scan, _ringRoot;
        TextMesh _ringNum, _ringWord;
        float _ghostShow, _ghostTo, _stamp = -1f;
        float[] _sig, _std;
        string[] _labelNames = { "TEMPO", "LENGTH", "RHYTHM", "FACE" };
        Vector3 _ballHome;

        Material GlowOf(Color c) { if (!_glows.TryGetValue(c, out Material m)) _glows[c] = m = Make.Glow(c, false); return m; }

        // ── The set ──────────────────────────────────────────────────────────

        public override void Build()
        {
            // The blueprint floor and wall: deep blue with a faint grid.
            Material navy = Make.Lit(new Color(0.06f, 0.13f, 0.26f), 0.35f);
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(-20f, -0.5f, 15f), new Vector3(240f, 1f, 260f), navy);
            Shape(PrimitiveType.Cube, "Wall", Root, new Vector3(-44f, 40f, 15f), new Vector3(1f, 100f, 260f), Make.Lit(new Color(0.05f, 0.11f, 0.22f), 0.3f));
            Material grid = Make.Glow(new Color(0.35f, 0.6f, 1f, 0.16f), false);
            for (float z = -60f; z <= 90f; z += 6f)
                NoShadow(Shape(PrimitiveType.Cube, "Grid", Root, new Vector3(-20f, 0.02f, z), new Vector3(240f, 0.04f, 0.18f), grid));
            for (float x = -42f; x <= 60f; x += 6f)
                NoShadow(Shape(PrimitiveType.Cube, "Grid", Root, new Vector3(x, 0.02f, 15f), new Vector3(0.18f, 0.04f, 260f), grid));
            for (float y = 6f; y <= 90f; y += 6f)
                NoShadow(Shape(PrimitiveType.Cube, "Wall grid", Root, new Vector3(-43.4f, y, 15f), new Vector3(0.04f, 0.18f, 260f), grid));
            for (float z = -60f; z <= 90f; z += 6f)
                NoShadow(Shape(PrimitiveType.Cube, "Wall grid", Root, new Vector3(-43.4f, 40f, z), new Vector3(0.04f, 100f, 0.18f), grid));
            // The strike line on the floor, along the target.
            NoShadow(Shape(PrimitiveType.Cube, "Target line", Root, new Vector3(0f, 0.05f, 20f), new Vector3(0.35f, 0.05f, 90f), Make.Glow(new Color(0.5f, 1f, 0.6f, 0.35f), false)));

            // The two putters: yours in front, the signature hologram just behind it.
            _you = BuildPutter(Root, new Vector3(PutterX, 0f, PutterZ), Accent, 38f, PutterScale);
            _ghost = BuildPutter(Root, new Vector3(PutterX - 4f, 0f, PutterZ), Accent, 38f, PutterScale);
            _ghostMat = Make.Glow(new Color(0.3f, 1f, 0.45f, 0f), true);
            foreach (MeshRenderer r in _ghost.Pivot.GetComponentsInChildren<MeshRenderer>()) { r.sharedMaterial = _ghostMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            _ballHome = new Vector3(PutterX, 0f, PutterZ + 1.45f);
            _ball = BuildBall(Root, _ballHome);
            _ballHome = _ball.localPosition;
            _scan = Shape(PrimitiveType.Cube, "Scan", Root, new Vector3(PutterX - 2f, 13f, -10f), new Vector3(10f, 28f, 0.3f), Make.Glow(new Color(0.4f, 1f, 0.5f, 0.6f), true)).transform;
            NoShadow(_scan.gameObject);
            _scan.gameObject.SetActive(false);

            // The gauges.
            Material panel = Make.Lit(new Color(0.03f, 0.05f, 0.09f), 0.5f, 0.2f);
            Material frame = Make.Emissive(new Color(0.2f, 0.3f, 0.4f), new Color(0.15f, 0.35f, 0.5f), 0.6f);
            for (int i = 0; i < 4; i++)
            {
                float z = GaugeZ[i];
                Shape(PrimitiveType.Cube, "Gauge", Root, new Vector3(GaugeX - 0.6f, GaugeY, z), new Vector3(0.8f, GaugeH * 2f + 2f, GaugeW), panel);
                Shape(PrimitiveType.Cube, "Gauge top", Root, new Vector3(GaugeX - 0.5f, GaugeY + GaugeH + 1.2f, z), new Vector3(1f, 0.4f, GaugeW + 0.4f), frame);
                Shape(PrimitiveType.Cube, "Gauge foot", Root, new Vector3(GaugeX - 0.5f, GaugeY - GaugeH - 1.2f, z), new Vector3(1f, 0.4f, GaugeW + 0.4f), frame);
                Shape(PrimitiveType.Cube, "Stand", Root, new Vector3(GaugeX - 0.6f, (GaugeY - GaugeH - 1.4f) / 2f, z), new Vector3(0.6f, GaugeY - GaugeH - 1.4f, 1.2f), frame);
                var g = new Gauge();
                g.Band = Shape(PrimitiveType.Cube, "Tolerance", Root, new Vector3(GaugeX, GaugeY, z), new Vector3(0.2f, 1f, GaugeW - 0.8f), Make.Glow(new Color(0.3f, 0.95f, 0.45f, 0.3f), false)).transform;
                g.Line = Shape(PrimitiveType.Cube, "Signature", Root, new Vector3(GaugeX + 0.15f, GaugeY, z), new Vector3(0.2f, 0.35f, GaugeW), Make.Glow(new Color(1f, 1f, 1f, 0.95f), false)).transform;
                g.MarkerMat = Make.Glow(Color.white, false);
                g.Marker = Shape(PrimitiveType.Cube, "Yours", Root, new Vector3(GaugeX + 0.35f, GaugeY, z), new Vector3(0.3f, 0.8f, GaugeW + 0.8f), g.MarkerMat).transform;
                foreach (Transform t in new[] { g.Band, g.Line, g.Marker }) NoShadow(t.gameObject);
                g.Band.gameObject.SetActive(false); g.Line.gameObject.SetActive(false); g.Marker.gameObject.SetActive(false);
                TextMesh label = Words(Root, _labelNames[i], new Vector3(GaugeX + 0.5f, GaugeY - GaugeH - 2.9f, z), 0.95f, new Color(1f, 1f, 1f, 0.75f));
                g.Value = Words(Root, "", new Vector3(GaugeX + 0.5f, GaugeY + GaugeH + 2.9f, z), 1.25f, Color.white);
                g.Sig = Words(Root, "", new Vector3(GaugeX + 0.5f, GaugeY - GaugeH - 6.6f, z), 1.6f, new Color(0.6f, 1f, 0.7f, 0.8f));
                _labels.Add(label.transform); _labels.Add(g.Value.transform); _labels.Add(g.Sig.transform);
                _g[i] = g;
            }

            // The match ring, standing on the right, and a pip for every copy under it.
            _ringRoot = new GameObject("Match ring").transform;
            _ringRoot.SetParent(Root, false);
            _ringRoot.localPosition = new Vector3(GaugeX, RingY, RingZ);
            _segOff = Make.Glow(new Color(0.25f, 0.4f, 0.55f, 0.35f), false);
            float len = 2f * Mathf.PI * RingR / Segs * 0.78f;
            for (int i = 0; i < Segs; i++)
            {
                float a = i * 360f / Segs;
                float r = a * Mathf.Deg2Rad;
                GameObject seg = Shape(PrimitiveType.Cube, "Seg", _ringRoot, new Vector3(0f, Mathf.Cos(r) * RingR, Mathf.Sin(r) * RingR), new Vector3(0.4f, 1.6f, len), _segOff);
                seg.transform.localRotation = Quaternion.Euler(a, 0f, 0f);
                NoShadow(seg);
                _segs.Add(seg.GetComponent<Renderer>());
            }
            GameObject back = Shape(PrimitiveType.Cylinder, "Ring back", _ringRoot, new Vector3(-0.8f, 0f, 0f), new Vector3(RingR * 2f + 4f, 0.4f, RingR * 2f + 4f), panel);
            back.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            NoShadow(back);
            Shape(PrimitiveType.Cube, "Ring stand", Root, new Vector3(GaugeX - 0.8f, (RingY - RingR - 2f) / 2f, RingZ), new Vector3(0.8f, RingY - RingR - 2f, 1.6f), frame);
            _ringNum = Words(_ringRoot, "", new Vector3(0.6f, 0.6f, 0f), 3.4f, Color.white);
            _ringWord = Words(_ringRoot, "MATCH", new Vector3(0.6f, 0.3f, 0f), 1.3f, new Color(1f, 1f, 1f, 0.75f));
            _labels.Add(_ringNum.transform); _labels.Add(_ringWord.transform);
            _pipOff = Make.Glow(new Color(0.3f, 0.4f, 0.5f, 0.45f), false);
            for (int i = 0; i < 14; i++)
            {
                GameObject pip = Shape(PrimitiveType.Cube, "Copy", Root, new Vector3(GaugeX + 0.2f, RingY - RingR - 3.2f, RingZ - 6.2f + i * 0.95f), new Vector3(0.3f, 0.7f, 0.72f), _pipOff);
                NoShadow(pip);
                _pips.Add(pip.GetComponent<Renderer>());
            }

            Sun(Root, new Vector3(50f, -60f, 0f), new Color(0.85f, 0.92f, 1f), 0.75f);
            Lamp(Root, new Vector3(80f, 120f, -40f), new Vector3(-10f, 10f, 10f), new Color(0.85f, 0.95f, 1f), 300000f, 50f, 600f);
            Lamp(Root, new Vector3(40f, 70f, 30f), new Vector3(GaugeX, GaugeY, 25f), new Color(0.6f, 1f, 0.75f), 40000f, 40f, 500f);
        }

        // ── State ────────────────────────────────────────────────────────────

        public override void Begin(ShotView start)
        {
            _std = Floats(start, "std");
            for (int i = 0; i < 4; i++) _g[i].Unit = _std.Length > i && _std[i] > 0 ? GaugeH / (3f * _std[i]) : 1f;
            bool signed = start.Flag("signed");
            _ghostShow = _ghostTo = signed ? 1f : 0f;
            ShowSignature(start, signed);
            Ready(start);
            Fill(0, Color.white);
        }

        void ShowSignature(ShotView v, bool signed)
        {
            _sig = Floats(v, "sig");
            for (int i = 0; i < 4; i++)
            {
                Gauge g = _g[i];
                g.Band.gameObject.SetActive(signed); g.Line.gameObject.SetActive(signed);
                if (!signed) { g.Marker.gameObject.SetActive(false); g.Value.text = ""; }
            }
        }

        public override void Ready(ShotView v)
        {
            float tol = v.Num("tolNext", 1f);
            foreach (Gauge g in _g) g.BandTo = tol;
            _ball.localPosition = _ballHome;
            _ball.gameObject.SetActive(true);
            int[] matches = Ints(v, "matches");
            for (int i = 0; i < _pips.Count; i++) _pips[i].sharedMaterial = i < matches.Length ? GlowOf(ScoreColour(matches[i])) : _pipOff;
        }

        static string Fmt(int i, float x) => i switch
        {
            0 => x.ToString("0", CultureInfo.InvariantCulture),
            1 => F1(x) + " in",
            2 => x.ToString("0.0", CultureInfo.InvariantCulture) + ":1",
            _ => Signed(x) + "°",
        };

        void Fill(int lit, Color c)
        {
            Material on = GlowOf(new Color(c.r, c.g, c.b, 1f));
            for (int i = 0; i < _segs.Count; i++) _segs[i].sharedMaterial = i < lit ? on : _segOff;
        }

        public override void Tick()
        {
            _ghostShow = Mathf.MoveTowards(_ghostShow, _ghostTo, Time.deltaTime * 1.5f);
            float pulse = 0.11f + 0.03f * Mathf.Sin(Time.time * 3f);
            _ghostMat.SetColor("_Color", new Color(0.3f, 1f, 0.45f, pulse * _ghostShow));
            foreach (Gauge g in _g)
            {
                g.BandNow = Mathf.Lerp(g.BandNow, g.BandTo, Time.deltaTime * 3f);
                g.Band.localScale = new Vector3(0.2f, Mathf.Max(0.05f, 2f * GaugeH / 3f * g.BandNow), GaugeW - 0.8f);
            }
            if (_stamp >= 0f)
            {
                _stamp += Time.deltaTime / 0.5f;
                float t = Mathf.Min(1f, _stamp);
                _ringRoot.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.5f));
                if (t >= 1f) { _stamp = -1f; _ringRoot.localScale = Vector3.one; }
            }
            foreach (Transform l in _labels) Face(l);
        }

        // ── A putt ───────────────────────────────────────────────────────────

        public override IEnumerator Play(ShotView v)
        {
            StrokeData you = StrokeOf(v);
            _ball.localPosition = _ballHome;
            if (v.Flag("justSigned"))
            {
                // The signature: a laser sweeps the stroke as it is made, then it hangs in the air as a hologram.
                _scan.gameObject.SetActive(true);
                Sound("laser_zap", 0.5f, 0.9f);
                float sweep = 0f;
                yield return All(Swing(_you, you, () => Sound("click", 0.5f, 1.4f), 1f, (t, phase) => { sweep = Mathf.Min(1f, sweep + Time.deltaTime / 1.3f); _scan.localPosition = new Vector3(PutterX - 2f, 13f, Mathf.Lerp(PutterZ - 10f, PutterZ + 8f, sweep)); }),
                    Later(you.Back + you.Fwd, RollBall(_ball, 22f, 0f, 1.1f)));
                _scan.gameObject.SetActive(false);
                ShowSignature(v, true);
                _ghostTo = 1f;
                Sound("synth_whoosh", 0.5f, 1.1f);
                Arcade.Sparks(At(PutterX - 4f, 14f, PutterZ), 24);
                yield return All(Settle(_you, 0.6f), Wait(0.6f));
                yield break;
            }

            // A copy: the hologram replays the signature as you replay yours.
            StrokeData sig = SigStroke(you);

            yield return All(
                Swing(_you, you, () => Sound("click", 0.5f, 1.3f)),
                Swing(_ghost, sig),
                Later(you.Back + you.Fwd, RollBall(_ball, 22f, 0f, 1.1f)));

            // The gauges: your value slides in from the signature line.
            float[] mine = Floats(v, "mine");
            int[] match = Ints(v, "match");
            for (int i = 0; i < 4; i++)
            {
                Gauge g = _g[i];
                float off = mine.Length > i && _sig.Length > i ? (mine[i] - _sig[i]) * g.Unit : 0f;
                g.MarkTo = Mathf.Clamp(off, -GaugeH, GaugeH);
                Color c = ScoreColour(match.Length > i ? match[i] : 0);
                g.MarkerMat.SetColor("_Color", c * 1.4f);
                g.Marker.gameObject.SetActive(true);
                g.Value.text = mine.Length > i ? Fmt(i, mine[i]) : "";
                g.Value.color = c;
            }
            Sound("ratchet", 0.4f, 1.2f);
            int all = v.Int("matchAll");
            Color ring = ScoreColour(all);
            bool copy = v.Flag("copy");
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
            {
                float u = Ease(Mathf.Min(1f, t));
                foreach (Gauge g in _g) g.Marker.localPosition = new Vector3(GaugeX + 0.35f, GaugeY + g.MarkTo * u, g.Line.localPosition.z);
                int lit = Mathf.RoundToInt(Segs * all / 100f * u);
                Fill(lit, ring);
                if (_you.Angle != 0f) Pose(_you, Mathf.Lerp(_you.Angle, 0f, u), 0f);
                if (_ghost.Angle != 0f) Pose(_ghost, Mathf.Lerp(_ghost.Angle, 0f, u), 0f);
                yield return null;
            }
            Pose(_you, 0f, 0f); Pose(_ghost, 0f, 0f);
            Fill(Mathf.RoundToInt(Segs * all / 100f), ring);
            if (copy)
            {
                // Carbon Copy: the ring stamps and throws sparks.
                _stamp = 0f;
                Fill(Segs, Color.Lerp(ring, Color.white, 0.35f));
                Arcade.Sparks(At(GaugeX + 1f, RingY, RingZ), 40);
                Arcade.Shake(0.15f);
                Sound("bell_ding", 0.55f, 1.15f);
                yield return Wait(0.35f);
                Fill(Mathf.RoundToInt(Segs * all / 100f), ring);
            }
            yield return Wait(0.5f);
        }

        StrokeData SigStroke(StrokeData you)
        {
            float tempo = _sig.Length > 0 && _sig[0] > 0 ? _sig[0] : 72f, rhythm = _sig.Length > 2 && _sig[2] > 0 ? _sig[2] : 2f;
            float total = 60f / tempo, back = total * rhythm / (rhythm + 1f);
            float face = _sig.Length > 3 ? _sig[3] : 0f;
            return new StrokeData { Has = true, Tempo = tempo, Rhythm = rhythm, LenIn = _sig.Length > 1 ? _sig[1] : 10f, Back = back, Fwd = total - back, Face = face, BackRot = -1.5f, FwdRot = face + 1.5f };
        }

        // ── Running things side by side ──────────────────────────────────────

        static IEnumerator All(params IEnumerator[] jobs)
        {
            var live = new List<IEnumerator>(jobs);
            while (live.Count > 0)
            {
                for (int i = live.Count - 1; i >= 0; i--) if (!Step(live[i])) live.RemoveAt(i);
                if (live.Count > 0) yield return null;
            }
        }

        // One frame of a job (following nested jobs it yields). False when it is done.
        static bool Step(IEnumerator job)
        {
            if (job.Current is IEnumerator inner && Step(inner)) return true;
            if (!job.MoveNext()) return false;
            if (job.Current is IEnumerator nested) Step(nested);
            return true;
        }

        static IEnumerator Wait(float seconds) { for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null; }
        static IEnumerator Later(float seconds, IEnumerator then)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
            yield return then;
        }
    }
}
