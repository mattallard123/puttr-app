// Tour Juice at the juice bar: a dark, glossy bar counter under green neon. Each putt is poured into the big glass
// bottle in the middle: the bottle is graduated in points up to the putt's distance (a make is worth up to its
// distance), and the dispenser above pours glowing juice to the putt's score, the number counting up beside it. The
// juice glows orange for a weak putt, lime for a solid one and Puttr green for tour quality, and fizzes with bubbles
// in proportion. Tour quality foams over the top with a burst of sparks; a full score (every point of its distance)
// sets the whole bar off. A miss only fizzles: the nozzle sputters a few drops, a grey puff, the neon flickers and the ring under the bottle glows red.
// In front, a rack of nine test tubes is the round so far: one per putt, labelled with its distance, filled with
// that putt's juice, the current one ringed; its plate carries the running total out of the perfect score.
//
// Units are inches; the counter top is y = 0, the camera looks up +Z at the back wall. The bar is drawn in
// TourJuiceBar.cs; this file is the putt.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed partial class TourJuiceScene : RuleScene
    {
        static bool Portrait => Screen.height > Screen.width;

        // The card render happens while the arcade is still starting up (with -puttrShots); the game page's backdrop
        // turns slowly round the set. Both show the bar mid-round rather than empty.
        bool Carding => Arcade != null && !Arcade.IsReady && PuttrArcade.Arcade.SceneCards != null;
        bool Showcase => Arcade != null && (Arcade.InBackdrop || Carding);

        public override Vector3 CameraFrom => Carding ? At(-4f, 64f, -120f) : At(-2f, 58f, -118f);
        public override Vector3 CameraLook => Carding ? At(-4f, 10f, -12f) : At(-2f, 12f, -8f);
        public override float CameraFov => Carding ? 37f : Portrait ? 40f : Wide(47f);

        // Wide, the arcade fits a lens to a 16:9 width; the game's part of the screen is narrower, so ask for less.
        static float Wide(float fov)
        {
            float aspect = FreePuttMode.MiddleShare * Screen.width / Mathf.Max(1f, Screen.height), wide = 16f / 9f;
            if (aspect >= wide) return fov;
            return 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect / wide) * Mathf.Rad2Deg;
        }
        public override float ShadowDistance => 400f;

        // ── The putt in the bottle ──

        float _fill;                             // 0..1 of the putt's distance
        int _feet = 7;                           // what the bottle is graduated to
        float _wobble;                           // the surface sloshing after a pour
        float _flash;                            // neon kick after a great putt
        float _flicker;                          // neon stutter after a miss
        float _fizz;                             // how lively the bubbles are
        float _pop = -1f;                        // the number popping in
        bool _showcased;
        float _sour;                             // the puck ring glowing red after a miss
        static readonly Color Sour = new Color(1f, 0.3f, 0.26f).linear;
        Color _lastColor = G;

        ShotView _rackView;

        static Color JuiceColor(float ratio)
        {
            Color low = new Color(1f, 0.42f, 0.12f), mid = new Color(0.86f, 0.9f, 0.2f), high = Make.Bright;
            // Linear, as the renderer works: the brand green as given would come out washed pale.
            return (ratio < 0.55f ? Color.Lerp(low, mid, Mathf.InverseLerp(0.15f, 0.55f, ratio)) : Color.Lerp(mid, high, Mathf.InverseLerp(0.55f, 0.85f, ratio))).linear;
        }

        static string Num(double x)
        {
            double r = System.Math.Round(x, 1);
            return r.ToString(System.Math.Abs(r - System.Math.Round(r)) < 0.001 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        void SetFill(float f, Color c)
        {
            _fill = Mathf.Clamp01(f);
            float h = Mathf.Max(0.001f, _fill * BodyH);
            _liquid.localScale = new Vector3(1f, h, 1f);
            _liquid.gameObject.SetActive(_fill > 0.002f);
            _surface.gameObject.SetActive(_fill > 0.002f);
            _surface.localPosition = new Vector3(0, LiquidBase + h + 0.05f, 0);
            Tint(c);
        }

        void Tint(Color c)
        {
            _liquidMat.SetColor("_BaseColor", c * 0.15f);
            _liquidMat.SetColor("_EmissionColor", c * 1.05f);
            _surfaceMat.SetColor("_Color", new Color(c.r, c.g, c.b, 0.55f));
            _streamMat.SetColor("_BaseColor", c * 0.6f);
            _streamMat.SetColor("_EmissionColor", c * 1.6f);
            _bubbleMat.SetColor("_Color", new Color(Mathf.Lerp(c.r, 1f, 0.8f), Mathf.Lerp(c.g, 1f, 0.8f), Mathf.Lerp(c.b, 1f, 0.8f), 1f));
            _glowLight.color = c;
        }

        // A two-figure score (10.4 of 11) is set a little smaller, so it stays on the screen.
        void SetNumber(string number)
        {
            _number.text = number;
            _number.transform.localScale = Vector3.one * (number.Length >= 4 ? 0.8f : 1f);
        }

        void Readout(string number, string of, string call, Color callColor)
        {
            SetNumber(number);
            _of.text = of;
            _call.text = call;
            _call.color = callColor;
        }

        // ── The rack: the round so far ──

        void ShowRack(ShotView v, int player)
        {
            _rackView = v;

            int[] list = v != null ? v.Get<int[]>("list") : null;
            float[][] hist = v != null ? v.Get<float[][]>("history") : null;
            float[] mine = hist != null && player >= 0 && player < hist.Length ? hist[player] : new float[0];
            for (int i = 0; i < Tubes; i++)
            {
                bool played = i < mine.Length;
                float r = played ? mine[i] : 0f;
                _tubeLabel[i].text = list != null && i < list.Length ? list[i].ToString() : "";
                _tubeLabel[i].color = played ? (r > 0f ? new Color(0.92f, 0.96f, 0.9f) : new Color(1f, 0.42f, 0.38f).linear) : new Color(1f, 1f, 1f, 0.45f);
                SetTube(i, played ? r : 0f, JuiceColor(r));
                _tubeCap[i].sharedMaterial = !played ? _capDim : r > 0f ? _capLit : _capMiss;
            }
            _nowRing.gameObject.SetActive(mine.Length < Tubes && v != null && !v.Over);
            if (mine.Length < Tubes) _nowRing.localPosition = new Vector3(TubeX(mine.Length), RackTop + 0.05f, RackZ);
            string total = v != null && player >= 0 && player < v.ScoreTexts.Length ? v.ScoreTexts[player] : "0";
            int perfect = v != null ? v.Int("perfect", 63) : 63;
            string who = v != null && v.Names.Length > 1 && player < v.Names.Length ? v.Names[player].ToUpperInvariant() + "   " : "";
            _total.text = who + "TOTAL  " + total + "  OF " + perfect;
        }

        void SetTube(int i, float ratio, Color c)
        {
            float h = Mathf.Max(0.001f, ratio * TubeBodyH);
            _tubeJuice[i].localScale = new Vector3(1f, h, 1f);
            _tubeJuice[i].gameObject.SetActive(ratio > 0.01f);
            _tubeMats[i].SetColor("_BaseColor", c * 0.15f);
            _tubeMats[i].SetColor("_EmissionColor", c * 1.05f);
        }

        // ── The game ──

        public override void Begin(ShotView start)
        {
            _showcased = false;
            Graduate(start != null ? start.NextFeet : 7);
            SetFill(0f, G);
            _fizz = 0f;
            Readout("", "", "", Color.white);
            ShowRack(start, start != null ? start.Next : 0);
        }

        public override void Ready(ShotView v) { ShowRack(v, v.Next); }

        public override IEnumerator Play(ShotView v)
        {
            _showcased = true;   // a real putt: the showcase never comes back
            float ratio = v.Feet > 0 ? Mathf.Clamp01((float)(v.Points / v.Feet)) : 0f;
            bool scored = v.Good && v.Points > 0.0001;
            Color c = JuiceColor(ratio);

            // Drain what the last putt left, then mark the bottle out to this putt's distance.
            Readout("", "", "", Color.white);
            if (_fill > 0f)
            {
                float from = _fill;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f) { SetFill(from * (1f - Ease(t)), _lastColor); yield return null; }
                SetFill(0f, c);
            }
            _fizz = 0f;
            Graduate(v.Feet);
            ShowRack(v, v.Player);
            // The tube for this putt waits empty until the bottle is poured.
            int slot = Mathf.Clamp(Hist(v, v.Player) - 1, 0, Tubes - 1);
            SetTube(slot, 0f, c);
            _tubeCap[slot].sharedMaterial = _capDim;

            if (!scored)
            {
                // A fizzle: the nozzle coughs, a few drops, a grey puff, the neon stutters.
                Sound("tourjuice_fizzle", "sizzle", 0.6f);   // puttr-app-2: the game's own sound, the shared one until recorded
                Tint(new Color(1f, 0.45f, 0.3f).linear);
                for (int k = 0; k < 3; k++)
                {
                    _drop.gameObject.SetActive(true);
                    Vector3 from = new Vector3(0, NozzleY - 1f, 0), to = new Vector3(Random.Range(-1.5f, 1.5f), LiquidBase + 0.6f, Random.Range(-1.5f, 1.5f));
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.32f) { _drop.localPosition = Vector3.Lerp(from, to, t * t); yield return null; }
                    _drop.gameObject.SetActive(false);
                    Splat(_bottle.TransformPoint(to), 2, new Color(1f, 0.5f, 0.35f, 0.7f));
                    yield return new WaitForSeconds(0.08f);
                }
                Puff(_bottle.TransformPoint(new Vector3(0, LiquidBase + BodyH * 0.55f, 0)));
                _flicker = 1f;
                _sour = 1f;
                Readout("0", "OF " + v.Feet, string.IsNullOrEmpty(v.Title) ? "MISS" : v.Title, new Color(1f, 0.38f, 0.34f).linear);
                _pop = 0f;
                SetTube(slot, 0f, c);
                _tubeCap[slot].sharedMaterial = _capMiss;
                _tubeLabel[slot].color = new Color(1f, 0.42f, 0.38f).linear;
                yield return new WaitForSeconds(0.7f);
                yield break;
            }

            // The pour: the stream runs, the bottle fills to the score, the number counts with it.
            _stream.gameObject.SetActive(true);
            Sound("tourjuice_pour", "water_plop", 0.5f);   // puttr-app-2: the game's own sound, the shared one until recorded
            Sound("riser", 0.35f, 0.85f + ratio * 0.35f);
            float seconds = Mathf.Lerp(0.8f, 1.6f, ratio);
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float e = 1f - (1f - t) * (1f - t);
                SetFill(ratio * e, c);
                _fizz = ratio * Mathf.Min(1f, t * 2f);
                StreamTo(LiquidBase + _fill * BodyH);
                SetNumber(Num(v.Points * e));
                _of.text = "OF " + v.Feet;
                yield return null;
            }
            SetFill(ratio, c);
            _lastColor = c;
            _stream.gameObject.SetActive(false);
            _wobble = 1f;
            _fizz = ratio;
            if (!Has("tourjuice_pour")) Sound("splash", 0.35f, 1.1f);   // puttr-app-2: the pour says it once recorded
            Readout(Num(v.Points), "OF " + v.Feet, v.Title, (ratio > 0.9f ? Make.Bright : ratio > 0.7f ? new Color(0.86f, 0.92f, 0.3f) : new Color(1f, 0.7f, 0.3f)).linear);
            _pop = 0f;
            SetTube(slot, ratio, c);
            _tubeCap[slot].sharedMaterial = _capLit;
            _tubeLabel[slot].color = new Color(0.92f, 0.96f, 0.9f);

            if (ratio >= 0.995f)
            {
                // Every point of its distance: the whole bar goes off.
                Foam(30);
                _flash = 1.6f;
                Burst(1.5f);
                Arcade?.Celebrate(70, _bottle.TransformPoint(new Vector3(0, NeckTop + 2f, 0)));
                Arcade?.Sparks(_bottle.TransformPoint(new Vector3(0, NeckTop + 4f, 0)), 40);
                if (!Has("tourjuice_tour_quality")) Sound("cheer", 0.6f);   // puttr-app-2: the game's TOUR QUALITY sting (SoundFor) says it once recorded
            }
            else if (ratio > 0.9f)
            {
                Foam(18);
                _flash = 1f;
                Burst(1f);
                Arcade?.Celebrate(30, _bottle.TransformPoint(new Vector3(0, NeckTop + 1f, 0)));
                if (!Has("tourjuice_make")) Sound("golf_clap", 0.5f);   // puttr-app-2: the game's own make (SoundFor) says it once recorded
            }
            yield return new WaitForSeconds(0.8f);
        }

        static int Hist(ShotView v, int player)
        {
            float[][] h = v.Get<float[][]>("history");
            return h != null && player >= 0 && player < h.Length ? h[player].Length : 1;
        }

        // A round in progress, for the card and the game page's backdrop.
        void PoseShowcase()
        {
            _showcased = true;
            Graduate(9);
            float r = 0.93f;
            SetFill(r, JuiceColor(r));
            _fizz = 0.9f;
            // No words in the showcase: the card and the game page put the game's own title over this picture.
            Readout("", "", "", Make.Bright.linear);
            _lastColor = JuiceColor(r);
            float[] demo = { 0.95f, 0.62f, 1f, 0.81f, 0f, 0.93f };
            int[] list = _rackView != null ? _rackView.Get<int[]>("list") : null;
            for (int i = 0; i < Tubes; i++)
            {
                bool played = i < demo.Length;
                SetTube(i, played ? demo[i] : 0f, JuiceColor(played ? demo[i] : 0f));
                _tubeCap[i].sharedMaterial = !played ? _capDim : demo[i] > 0f ? _capLit : _capMiss;
                _tubeLabel[i].text = list != null && i < list.Length ? list[i].ToString() : (3 + (i * 5) % 9).ToString();
                _tubeLabel[i].color = !played ? new Color(1f, 1f, 1f, 0.45f) : demo[i] > 0f ? new Color(0.92f, 0.96f, 0.9f) : new Color(1f, 0.42f, 0.38f).linear;
            }
            _nowRing.gameObject.SetActive(true);
            _nowRing.localPosition = new Vector3(TubeX(demo.Length), RackTop + 0.05f, RackZ);
            _total.text = "";
            // Bubbles already on their way up.
            for (int i = 0; i < _bubbles.Count; i++) _bubbleY[i] = LiquidBase + Random.value * BodyH * r;
        }

        public override void Tick()
        {
            if (_bottle == null) return;
            if (!_showcased && Showcase) PoseShowcase();
            float dt = Time.deltaTime, time = Time.time;

            // Neon: a slow breath, a kick after a great putt, a stutter after a miss.
            _flash = Mathf.Max(0f, _flash - dt * 1.2f);
            _flicker = Mathf.Max(0f, _flicker - dt * 1.4f);
            float neon = 1f + 0.06f * Mathf.Sin(time * 1.7f) + _flash * (0.6f + 0.4f * Mathf.Sin(time * 22f));
            if (_flicker > 0f) neon *= (Mathf.PerlinNoise(time * 30f, 0.3f) > 0.45f ? 1f : 0.15f);
            foreach (Material m in _neonMats) m.SetColor("_EmissionColor", _neonBase[m] * neon);
            _signHalo.SetColor("_Color", new Color(G.r, G.g, G.b, 0.22f * neon));
            _sour = Mathf.Max(0f, _sour - dt * 0.6f);
            _ringMat.SetColor("_EmissionColor", Color.Lerp(G, Sour, Mathf.Clamp01(_sour * 3f)) * (1.6f + 1.2f * _flash) * (_flicker > 0f ? neon : 1f));
            TickBurst(dt);

            // The surface sloshes after a pour.
            _wobble = Mathf.Max(0f, _wobble - dt * 0.7f);
            float w = _wobble * _wobble * 7f;
            _surface.localRotation = Quaternion.Euler(Mathf.Sin(time * 9f) * w, 0, Mathf.Cos(time * 7f) * w);

            // Bubbles rise through the juice and start again at the bottom.
            float level = LiquidBase + _fill * BodyH;
            int live = Mathf.RoundToInt(_fizz * _bubbles.Count);
            for (int i = 0; i < _bubbles.Count; i++)
            {
                bool on = i < live && _fill > 0.04f;
                if (!on) { if (_bubbles[i].gameObject.activeSelf) _bubbles[i].gameObject.SetActive(false); continue; }
                _bubbleY[i] += dt * _bubbleV[i];
                if (_bubbleY[i] > level - 0.4f) { _bubbleY[i] = LiquidBase + 0.6f; _bubbleV[i] = Random.Range(5f, 13f); }
                float wig = Mathf.Sin(time * 5f + i) * 0.35f;
                _bubbles[i].localPosition = new Vector3(_bubbleXZ[i].x + wig, _bubbleY[i], _bubbleXZ[i].y);
                if (!_bubbles[i].gameObject.activeSelf) _bubbles[i].gameObject.SetActive(true);
            }
            // The glow the juice throws on the counter.
            _glowLight.intensity = GlowLight * _fill * (1f + _flash * 0.5f);
            _glowLight.transform.localPosition = new Vector3(0, LiquidBase + _fill * BodyH * 0.5f, 0);

            // The number pops in.
            if (_pop >= 0f)
            {
                _pop += dt / 0.35f;
                float t = Mathf.Min(1f, _pop);
                float s = t < 0.6f ? Mathf.Lerp(0.6f, 1.12f, Ease(t / 0.6f)) : Mathf.Lerp(1.12f, 1f, (t - 0.6f) / 0.4f);
                _numberRoot.localScale = Vector3.one * s;
                if (t >= 1f) _pop = -1f;
            }
            // The current tube's ring breathes.
            if (_nowRing.gameObject.activeSelf) _nowRing.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(time * 4f));

            TickParticles(dt);
        }
    }
}
