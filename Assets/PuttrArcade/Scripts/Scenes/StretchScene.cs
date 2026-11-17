// Stretch, "The Ruler": a putting lane seen from behind and to the right, with a giant glowing ruler laid along the
// target line behind the ball (4 to 20 in). The band for the next putt glows cyan on it and slides to its new place
// as the distances alternate short and long; the flag stands at the putt's distance up the lane. Each putt replays
// the real stroke: the putter head travels back along the ruler, a marker stands where the backstroke peaked (green
// in the band, amber out) and the ball rolls up the lane to where it stopped. A little metronome by the lane swings at
// your locked tempo, its lamp green when the stroke held it and amber when it drifted. Elastic stretches the ruler
// like a rubber band. Every backstroke so far stays as a faint tick.
// Units: inches; the ball sits at the origin, +Z is the hole, the backstroke goes toward -Z.
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class StretchScene : SensorStage
    {
        static readonly Color Accent = Make.Hex(0x00CCFF);
        const float RulerX = 5.6f, RulerW = 4.4f, RulerEnd = 21f, CupR = 2.125f;

        public override Vector3 CameraFrom => Portrait ? At(28f, 32f, -36f) : At(30f, 25f, -28f);
        public override Vector3 CameraLook => Portrait ? At(-1f, 0f, 21f) : At(0f, 0f, 12f);
        public override float CameraFov => Portrait ? 58f : 48f;
        // The ruler's numbers turn to read along the camera's view.
        float NumberYaw { get { Vector3 d = CameraLook - CameraFrom; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; } }
        public override float ShadowDistance => 500f;

        Putter _putter;
        Transform _ball, _ruler, _band, _peak, _hole, _needle;
        Material _bandMat, _peakMat, _lampMat;
        Renderer _lamp;
        readonly List<Transform> _ticks = new List<Transform>();
        Readout _read;
        float _lo = 9f, _hi = 13f, _loTo = 9f, _hiTo = 13f, _holeZ = 72f, _holeTo = 72f, _lock, _elastic = -1f, _lampFlash;
        Color _lampColour = new Color(0.5f, 0.55f, 0.6f);
        bool _locked;

        // ── The set ──────────────────────────────────────────────────────────

        public override void Build()
        {
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material turf = Make.Lit(new Color(0.09f, 0.3f, 0.16f), 0.12f, 0f, pile, pileNormal, 0.9f);
            Shape(PrimitiveType.Cube, "Lane", Root, new Vector3(0f, -0.5f, 70f), new Vector3(30f, 1f, 230f), turf);
            Material edge = Make.Lit(new Color(0.05f, 0.18f, 0.1f), 0.25f);
            for (int s = -1; s <= 1; s += 2) Shape(PrimitiveType.Cube, "Lane edge", Root, new Vector3(s * 15.6f, 0.1f, 70f), new Vector3(1.2f, 2.2f, 230f), edge);
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0f, -1.5f, 70f), new Vector3(600f, 1f, 700f), Make.Lit(new Color(0.045f, 0.055f, 0.07f), 0.4f));
            Shape(PrimitiveType.Cube, "Back wall", Root, new Vector3(0f, 60f, 200f), new Vector3(600f, 140f, 2f), Make.Lit(new Color(0.04f, 0.06f, 0.09f), 0.3f));
            // Strip lights down the lane edges, cyan.
            Material strip = Make.Emissive(Color.black, Accent * 1.2f);
            for (int s = -1; s <= 1; s += 2) NoShadow(Shape(PrimitiveType.Cube, "Strip", Root, new Vector3(s * 15.6f, 1.25f, 70f), new Vector3(0.4f, 0.15f, 230f), strip));
            // Distance marks every foot up the lane.
            Material mark = Make.Glow(new Color(1f, 1f, 1f, 0.12f), false);
            for (int f = 1; f <= 13; f++) NoShadow(Shape(PrimitiveType.Cube, "Foot", Root, new Vector3(0f, 0.02f, f * 12f), new Vector3(26f, 0.03f, 0.25f), mark));

            // The cup and the flag.
            _hole = new GameObject("Hole").transform;
            _hole.SetParent(Root, false);
            GameObject cup = Make.MeshObject("Cup", _hole, Make.Disc(CupR, 48), Make.Lit(new Color(0.02f, 0.02f, 0.02f), 0.1f));
            cup.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            GameObject rim = Make.MeshObject("Rim", _hole, Make.Ring(CupR, CupR + 0.25f, 48), Make.Lit(new Color(0.9f, 0.92f, 0.9f), 0.5f));
            rim.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            Cylinder("Pole", _hole, new Vector3(0f, 16f, 0f), 0.22f, 32f, Make.Lit(new Color(0.9f, 0.9f, 0.92f), 0.8f, 0.6f));
            Shape(PrimitiveType.Cube, "Flag", _hole, new Vector3(3.8f, 29.5f, 0f), new Vector3(7.5f, 4.8f, 0.15f), Make.Emissive(Accent * 0.6f, Accent * 1.3f, 0.5f));

            // The putter and the ball.
            _putter = BuildPutter(Root, new Vector3(0f, 0f, -1f), Accent);
            _ball = BuildBall(Root, new Vector3(0f, 0f, 0.8f));

            // The ruler: hinged at the ball end, so it stretches backwards.
            _ruler = new GameObject("Ruler").transform;
            _ruler.SetParent(Root, false);
            _ruler.localPosition = new Vector3(RulerX, 0.15f, -1f);
            GameObject slab = Make.MeshObject("Ruler body", _ruler, Make.RoundedBox(new Vector3(RulerW, 0.3f, RulerEnd + 1.5f), 0.12f), Make.Lit(new Color(0.12f, 0.15f, 0.2f), 0.7f, 0.3f));
            slab.transform.localPosition = new Vector3(0f, 0f, -(RulerEnd + 1.5f) / 2f + 0.75f);
            Material tick = Make.Emissive(Color.white, new Color(0.9f, 0.95f, 1f) * 0.8f, 0.4f);
            for (int i = 0; i <= (int)RulerEnd; i++)
            {
                float w = i % 2 == 0 ? RulerW * 0.55f : RulerW * 0.3f;
                NoShadow(Shape(PrimitiveType.Cube, "Tick", _ruler, new Vector3(-RulerW / 2f + w / 2f, 0.17f, -i), new Vector3(w, 0.03f, 0.1f), tick));
                if (i >= 4 && i % 2 == 0)
                {
                    TextMesh n = Words(_ruler, i.ToString(), new Vector3(RulerW * 0.25f, 0.2f, -i), 1.25f, new Color(0.92f, 0.96f, 1f));
                    n.transform.localRotation = Quaternion.Euler(90f, NumberYaw, 0f);
                }
            }
            _bandMat = Make.Glow(new Color(Accent.r, Accent.g, Accent.b, 0.35f), false);
            _band = new GameObject("Band").transform;
            _band.SetParent(_ruler, false);
            NoShadow(Shape(PrimitiveType.Cube, "Band fill", _band, new Vector3(0f, 0.2f, 0f), new Vector3(RulerW + 0.6f, 0.06f, 1f), _bandMat));
            Material rail = Make.Emissive(Accent, Accent * 2.2f, 0.6f);
            for (int s = -1; s <= 1; s += 2) NoShadow(Shape(PrimitiveType.Cube, "Band rail", _band, new Vector3(s * (RulerW / 2f + 0.45f), 0.25f, 0f), new Vector3(0.5f, 0.5f, 1f), rail));
            for (int s = -1; s <= 1; s += 2) NoShadow(Shape(PrimitiveType.Cube, "Band end", _band, new Vector3(0f, 0.25f, s * 0.5f), new Vector3(RulerW + 1.4f, 0.5f, 0.02f), rail));
            _peakMat = Make.Emissive(Color.white, Color.white, 0.6f);
            _peak = new GameObject("Peak").transform;
            _peak.SetParent(_ruler, false);
            NoShadow(Shape(PrimitiveType.Cube, "Peak bar", _peak, new Vector3(0f, 0.3f, 0f), new Vector3(RulerW + 1.2f, 0.12f, 0.35f), _peakMat));
            NoShadow(Shape(PrimitiveType.Cube, "Peak post", _peak, new Vector3(RulerW / 2f + 0.6f, 2.2f, 0f), new Vector3(0.25f, 4.4f, 0.25f), _peakMat));
            NoShadow(Shape(PrimitiveType.Cube, "Peak flag", _peak, new Vector3(RulerW / 2f + 1.7f, 3.8f, 0f), new Vector3(2f, 1.2f, 0.1f), _peakMat));
            _peak.gameObject.SetActive(false);

            // The tempo metronome by the lane: it swings at your locked tempo; its lamp tells you if the stroke kept it.
            Transform metro = new GameObject("Tempo").transform;
            metro.SetParent(Root, false);
            metro.localPosition = new Vector3(-11f, 0f, -8f);
            Material wood = Make.Lit(new Color(0.12f, 0.13f, 0.16f), 0.7f, 0.3f);
            Make.MeshObject("Body", metro, Make.Lathe(new[] { new Vector2(4.2f, 0f), new Vector2(3.6f, 1f), new Vector2(1.4f, 13f), new Vector2(0.01f, 13.5f) }, 4), wood).transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            _needle = new GameObject("Needle").transform;
            _needle.SetParent(metro, false);
            _needle.localPosition = new Vector3(2.6f, 2f, 0f);
            NoShadow(Shape(PrimitiveType.Cube, "Rod", _needle, new Vector3(0f, 6f, 0f), new Vector3(0.25f, 12f, 0.25f), Make.Lit(new Color(0.85f, 0.87f, 0.9f), 0.9f, 1f)));
            NoShadow(Shape(PrimitiveType.Cube, "Weight", _needle, new Vector3(0f, 9f, 0f), new Vector3(1.2f, 1.2f, 0.6f), Make.Emissive(Accent * 0.5f, Accent, 0.6f)));
            _lampMat = Make.Emissive(Color.gray, Color.gray, 0.6f);
            _lamp = Shape(PrimitiveType.Sphere, "Lamp", metro, new Vector3(0f, 14.6f, 0f), Vector3.one * 2.4f, _lampMat).GetComponent<Renderer>();

            _read = new Readout(this, Portrait ? new Vector2(0.5f, 0.02f) : new Vector2(0.55f, 0.35f), Accent);

            Sun(Root, new Vector3(52f, -30f, 0f), new Color(1f, 0.97f, 0.92f), 0.45f);
            Lamp(Root, new Vector3(-30f, 160f, -60f), new Vector3(0f, 0f, 30f), new Color(1f, 0.97f, 0.92f), 45000f, 55f, 900f);
            Lamp(Root, new Vector3(20f, 60f, -30f), new Vector3(RulerX, 0f, -10f), new Color(0.8f, 0.95f, 1f), 90000f, 35f, 300f);
        }

        // ── State ────────────────────────────────────────────────────────────

        public override void Begin(ShotView start)
        {
            _read.Hide();
            _lo = _loTo = start.Num("lo", 9f); _hi = _hiTo = start.Num("hi", 13f);
            _holeZ = _holeTo = Mathf.Max(3, start.NextFeet) * 12f;
            _peak.gameObject.SetActive(false);
            Ready(start);
            _lampColour = new Color(0.5f, 0.55f, 0.6f);
        }

        public override void Ready(ShotView v)
        {
            _loTo = v.Num("lo", _loTo); _hiTo = v.Num("hi", _hiTo);
            _holeTo = Mathf.Max(3, v.NextFeet) * 12f;
            _locked = v.Flag("hasLock");
            _lock = v.Num("tempoLock");
            int ok = v.Int("tempoOk", -1);
            _lampColour = ok == 1 ? ScoreGreen : ok == 0 ? ScoreAmber : new Color(0.5f, 0.55f, 0.6f);
            // Every backstroke so far, as a faint tick.
            float[] lengths = Floats(v, "lengths");
            while (_ticks.Count < lengths.Length)
            {
                GameObject t = Shape(PrimitiveType.Cube, "Past", _ruler, Vector3.zero, new Vector3(RulerW * 0.9f, 0.05f, 0.18f), Make.Glow(new Color(1f, 1f, 1f, 0.35f), false));
                NoShadow(t);
                _ticks.Add(t.transform);
            }
            for (int i = 0; i < _ticks.Count; i++)
            {
                _ticks[i].gameObject.SetActive(i < lengths.Length);
                if (i < lengths.Length) _ticks[i].localPosition = new Vector3(0f, 0.22f, -Mathf.Clamp(lengths[i], 0f, RulerEnd));
            }
        }

        public override void Tick()
        {
            _lo = Mathf.Lerp(_lo, _loTo, Time.deltaTime * 4f);
            _hi = Mathf.Lerp(_hi, _hiTo, Time.deltaTime * 4f);
            _band.localPosition = new Vector3(0f, 0f, -(_lo + _hi) / 2f);
            _band.localScale = new Vector3(1f, 1f, Mathf.Max(0.1f, _hi - _lo));
            _bandMat.SetColor("_Color", new Color(Accent.r, Accent.g, Accent.b, 0.3f + 0.12f * Mathf.Sin(Time.time * 3f)));
            _holeZ = Mathf.Lerp(_holeZ, _holeTo, Time.deltaTime * 3f);
            _hole.localPosition = new Vector3(0f, 0f, _holeZ);
            // The metronome swings at the locked tempo: one side to the other each beat.
            float angle = _locked && _lock > 0 ? 24f * Mathf.Sin(Mathf.PI * Time.time * _lock / 60f) : 0f;
            _needle.localRotation = Quaternion.Euler(0f, 0f, angle);
            _lampFlash = Mathf.Max(0f, _lampFlash - Time.deltaTime * 1.5f);
            _lampMat.SetColor("_EmissionColor", _lampColour * (1.2f + 2.5f * _lampFlash));
            _lampMat.SetColor("_BaseColor", _lampColour);
            if (_elastic >= 0f)
            {
                _elastic += Time.deltaTime / 0.9f;
                float t = Mathf.Min(1f, _elastic);
                float stretch = t < 0.25f ? Mathf.Lerp(1f, 1.35f, Ease(t / 0.25f)) : 1f + 0.35f * Mathf.Cos((t - 0.25f) / 0.75f * Mathf.PI * 3f) * (1f - (t - 0.25f) / 0.75f);
                _ruler.localScale = new Vector3(1f, 1f, stretch);
                if (t >= 1f) { _elastic = -1f; _ruler.localScale = Vector3.one; }
            }
            _read.Size = Portrait ? 1f : 0.62f;
            _read.Spot = Portrait ? new Vector2(0.5f, 0.02f) : new Vector2(0.58f, 0.35f);
            _read.Tick(Eye(), Root);
        }

        // ── A putt ───────────────────────────────────────────────────────────

        public override IEnumerator Play(ShotView v)
        {
            StrokeData s = StrokeOf(v);
            _read.Hide();
            _peak.gameObject.SetActive(false);
            _ball.gameObject.SetActive(true);
            _ball.localPosition = new Vector3(0f, 0.84f, 0.8f);
            _holeTo = _holeZ = Mathf.Max(3, v.Feet) * 12f;
            _hole.localPosition = new Vector3(0f, 0f, _holeZ);
            float lastLo = v.Num("lastLo", _lo), lastHi = v.Num("lastHi", _hi);
            _loTo = lastLo; _hiTo = lastHi;

            // The stroke along the ruler, then the ball up the lane.
            float len = s.LenIn;
            float stop = v.Made ? _holeZ : _holeZ + (v.HasRollout ? v.RolloutIn : -10f);
            float side = v.Made ? 0f : v.MissSide == "LEFT" ? -3.6f : v.MissSide == "RIGHT" ? 3.6f : 0f;
            float dist = Mathf.Max(4f, stop - 0.8f);
            yield return All(
                Swing(_putter, s, () => Sound("click", 0.45f, 1.2f)),
                Later(s.Back + s.Fwd, Run(Reveal(v, len))),
                Later(s.Back + s.Fwd, RollBall(_ball, dist, side, Mathf.Clamp(dist / 80f, 1.1f, 1.9f))));
            if (v.Made)
            {
                Vector3 at = _ball.localPosition;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f) { _ball.localPosition = at + Vector3.down * 3f * t; yield return null; }
                _ball.gameObject.SetActive(false);
            }
            if (v.Flag("elastic"))
            {
                _elastic = 0f;
                Sound("swish", 0.6f, 0.8f);
                Arcade.Sparks(At(RulerX, 1f, -1f - (lastLo + lastHi) / 2f), 34);
                Arcade.Shake(0.12f);
            }
            yield return All(Settle(_putter, 0.5f), Wait(0.5f));
        }

        // At impact: the marker stands where the backstroke peaked, the lamp answers for the tempo, the readout comes up.
        IEnumerator Reveal(ShotView v, float len)
        {
            bool ok = v.Flag("lengthOk");
            Color c = ok ? ScoreGreen : ScoreAmber;
            _peakMat.SetColor("_BaseColor", c);
            _peakMat.SetColor("_EmissionColor", c * 1.6f);
            _peak.gameObject.SetActive(true);
            float z = -Mathf.Clamp(len, 0f, RulerEnd + 0.5f);
            int okT = v.Int("tempoOk", -1);
            _lampColour = okT == 1 ? ScoreGreen : okT == 0 ? ScoreAmber : new Color(0.5f, 0.55f, 0.6f);
            _lampFlash = okT >= 0 ? 1f : 0f;
            _read.Show("BACKSTROKE", F1(len), "in", "", c);
            Sound(ok ? "lab_beep" : "click", 0.5f, ok ? 1.2f : 0.7f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
            {
                _peak.localPosition = new Vector3(0f, 2f * (1f - Ease(t)), z);
                yield return null;
            }
            _peak.localPosition = new Vector3(0f, 0f, z);
        }

        static IEnumerator Run(IEnumerator e) { yield return e; }

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
