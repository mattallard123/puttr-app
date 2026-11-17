// Groove as a rhythm-game stage: the lane is a neon highway running away to the back of a dark stage, with speaker
// stacks either side, spotlights raking the haze and a crowd in silhouette. The pace window is a glowing strike bar
// across the highway (to the hole, and no further past than the round's window); it narrows each round. The combo
// count hangs over the stage. The putt rolls up the highway: stop in the bar and it strikes, the combo pumps and the
// meters jump; outside it the bar flashes red and the combo drops to nothing.
// Units: inches x K on the lane; the cup is at the origin, the golfer down -Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class GrooveScene : RuleScene
    {
        const float K = 6f, CupR = 2.125f * K, BallR = 0.84f * K, Reach = 230f, LaneW = 70f;
        static readonly Color Lime = new Color(0.75f, 1f, 0.2f), Violet = new Color(0.62f, 0.35f, 1f);

        Transform _bar, _ball, _marker, _combo;
        Renderer _barFill;
        Transform _barFar;
        Material _barMat, _barHit, _barMiss, _ledOn, _ledOff, _ledHot;
        TextMesh _comboText, _comboWord, _roundText;
        readonly List<Renderer> _leds = new List<Renderer>();
        readonly List<Transform> _beams = new List<Transform>();
        LineRenderer _trail;
        float _lo = -2f, _hi = 9f, _barLo = -2f, _barHi = 9f, _pump, _level, _levelTo;
        int _streak;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(0f, Portrait ? 95f : 80f, Portrait ? -270f : -240f);
        public override Vector3 CameraLook => At(0f, Portrait ? 45f : 35f, 260f);
        public override float CameraFov => Portrait ? 60f : 46f;

        public override void Build()
        {
            _barMat = Make.Glow(new Color(0.75f, 1f, 0.2f, 0.3f));
            _barHit = Make.Glow(new Color(0.9f, 1f, 0.6f, 0.85f));
            _barMiss = Make.Glow(new Color(1f, 0.15f, 0.2f, 0.6f));
            _ledOn = Make.Emissive(Violet, Violet * 2.4f);
            _ledHot = Make.Emissive(Lime, Lime * 3f);
            _ledOff = Make.Lit(new Color(0.08f, 0.06f, 0.12f), 0.6f);

            // The stage: a dark floor and back, the highway.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -2f, 900f), new Vector3(4000f, 2f, 3000f), Make.Lit(new Color(0.03f, 0.025f, 0.05f), 0.6f));
            Shape(PrimitiveType.Cube, "Back", Root, new Vector3(0, 600f, 2300f), new Vector3(6000f, 1600f, 4f), Make.Emissive(Color.black, new Color(0.035f, 0.015f, 0.07f)));
            Material turf = Make.Lit(new Color(0.12f, 0.36f, 0.16f), 0.4f);
            Shape(PrimitiveType.Cube, "Highway", Root, new Vector3(0, -0.5f, 850f), new Vector3(LaneW * 2f, 1f, 2200f), turf);
            Color edgeC = new Color(0.75f, 0.5f, 1f);
            foreach (float x in new[] { -LaneW, LaneW })
            {
                GameObject e = Shape(PrimitiveType.Cube, "Edge", Root, new Vector3(x, 0.6f, 850f), new Vector3(3f, 1.2f, 2200f), Make.Emissive(edgeC, edgeC * 2.5f));
                e.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            for (int i = 0; i < 46; i++)
            {
                float z = -250f + i * 50f;
                LineRenderer l = Stroke(Root, "Fret", new Color(0.7f, 1f, 0.6f, 0.13f), 1.2f);
                Points(l, new[] { new Vector3(-LaneW, 0.1f, z), new Vector3(LaneW, 0.1f, z) });
            }
            foreach (float x in new[] { -LaneW / 3f, LaneW / 3f })
            {
                LineRenderer l = Stroke(Root, "Lane line", new Color(0.7f, 1f, 0.6f, 0.13f), 1.2f);
                Points(l, new[] { new Vector3(x, 0.1f, -250f), new Vector3(x, 0.1f, 1950f) });
            }
            GameObject hole = Make.MeshObject("Hole", Root, Make.Disc(CupR, 48), Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f));
            hole.transform.localPosition = new Vector3(0, 0.15f, 0);
            Hoop("Lip", Root, new Vector3(0, 0.2f, 0), CupR, CupR + 1.4f, Make.Emissive(Color.white, new Color(0.8f, 0.8f, 0.8f)));

            // The strike bar.
            _bar = new GameObject("Strike bar").transform;
            _bar.SetParent(Root, false);
            _barFill = Shape(PrimitiveType.Cube, "Fill", _bar, new Vector3(0, 0.3f, 0.5f), new Vector3(LaneW * 2f, 0.3f, 1f), _barMat).GetComponent<Renderer>();
            _barFill.shadowCastingMode = ShadowCastingMode.Off;
            Material edge = Make.Emissive(Lime, Lime * 3f);
            Shape(PrimitiveType.Cube, "Near", _bar, new Vector3(0, 0.5f, 0), new Vector3(LaneW * 2f + 16f, 1f, 2f), edge).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            _barFar = Shape(PrimitiveType.Cube, "Far", _bar, new Vector3(0, 0.5f, 1f), new Vector3(LaneW * 2f + 16f, 1f, 2f), edge).transform;
            _barFar.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Light barLight = World.NewLight(_bar, "Bar light", LightType.Point, Lime, 4000f);
            barLight.transform.localPosition = new Vector3(0, 30f, 20f);
            barLight.range = 150f;
            barLight.shadows = LightShadows.None;

            // Speaker stacks with level meters, either side.
            Material cab = Make.Lit(new Color(0.06f, 0.05f, 0.08f), 0.4f);
            Material cone = Make.Lit(new Color(0.02f, 0.02f, 0.03f), 0.7f);
            foreach (float side in new[] { -1f, 1f })
            {
                for (int s = 0; s < 2; s++)
                {
                    float x = side * (LaneW + 70f + s * 40f), z = 140f + s * 260f, h = 150f - s * 30f;
                    Shape(PrimitiveType.Cube, "Stack", Root, new Vector3(x, h / 2f, z), new Vector3(60f, h, 50f), cab);
                    for (int c = 0; c < 2; c++)
                    {
                        GameObject woofer = Shape(PrimitiveType.Cylinder, "Woofer", Root, new Vector3(x, 35f + c * 50f, z - 25.5f), new Vector3(36f, 0.6f, 36f), cone);
                        woofer.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                    }
                    for (int k = 0; k < 10; k++)
                    {
                        GameObject led = Shape(PrimitiveType.Cube, "LED", Root, new Vector3(x - side * 36f, 10f + k * (h - 20f) / 10f, z - 20f), new Vector3(10f, (h - 20f) / 10f - 3f, 6f), _ledOff);
                        led.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                        _leds.Add(led.GetComponent<Renderer>());
                    }
                }
            }
            // The crowd: heads and shoulders in the dark either side at the back.
            Material crowd = Make.Lit(new Color(0.015f, 0.012f, 0.025f), 0.2f);
            var r = new System.Random(9);
            for (int i = 0; i < 44; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * (LaneW + 140f + (float)r.NextDouble() * 520f), z = 700f + (float)r.NextDouble() * 500f, s = 30f + (float)r.NextDouble() * 12f;
                Shape(PrimitiveType.Sphere, "Head", Root, new Vector3(x, 60f + s, z), Vector3.one * s, crowd);
                Shape(PrimitiveType.Capsule, "Body", Root, new Vector3(x, 20f, z), new Vector3(s * 1.8f, 40f, s), crowd);
            }
            // Spotlight beams in the haze.
            for (int i = 0; i < 4; i++)
            {
                float x = -360f + i * 240f;
                GameObject beam = Shape(PrimitiveType.Cylinder, "Beam", Root, new Vector3(x, 380f, 900f), new Vector3(60f, 420f, 60f), Make.Glow(new Color(0.7f, 0.45f, 1f, 0.07f)));
                beam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                _beams.Add(beam.transform);
            }

            // The combo sign hung over the stage.
            _combo = new GameObject("Combo").transform;
            _combo.SetParent(Root, false);
            _combo.localPosition = new Vector3(60f, 150f, 600f);
            _roundText = Words(_combo, "", new Vector3(0, 95f, 0), 18f, new Color(0.8f, 0.75f, 0.95f));
            _comboText = Words(_combo, "", Vector3.zero, 90f, Lime, TextAnchor.MiddleCenter, 160);
            _comboWord = Words(_combo, "STREAK", new Vector3(0, -80f, 0), 20f, Violet);

            _marker = new GameObject("Marker").transform;
            _marker.SetParent(Root, false);
            Hoop("Ring", _marker, new Vector3(0, 0.8f, 0), BallR * 1.1f, BallR * 1.6f, Make.Emissive(Color.white, new Color(2f, 2f, 2f))).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            _marker.gameObject.SetActive(false);
            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.6f, 0f, bc, bn)).transform;
            _ball.gameObject.SetActive(false);
            _trail = Stroke(Root, "Trail", new Color(0.8f, 0.7f, 1f, 0.7f), 2.6f);

            Sun(Root, new Vector3(50f, 0f, 0f), new Color(0.8f, 0.7f, 1f), 0.45f);
            Lamp(Root, new Vector3(0, 380f, -160f), new Vector3(0, 0, 60f), new Color(0.9f, 0.85f, 1f), 600000f, 40f, 900f);
            Lamp(Root, new Vector3(-300f, 400f, 300f), new Vector3(0, 0, 150f), new Color(0.6f, 0.35f, 1f), 500000f, 40f, 900f);
            Lamp(Root, new Vector3(300f, 400f, 300f), new Vector3(0, 0, 150f), new Color(0.6f, 0.35f, 1f), 500000f, 40f, 900f);
        }

        void Set(ShotView v)
        {
            float target = v.Num("target"), window = v.Num("window", 9f);
            _lo = target - 2f;
            _hi = target + window;
            int[] streaks = v.Get<int[]>("streaks");
            _streak = streaks != null && v.Next < streaks.Length ? streaks[v.Next] : v.Int("streak");
            _comboText.text = "x" + _streak;
            _roundText.text = "";
            _levelTo = Mathf.Min(10f, _streak * 2f);
        }

        public override void Begin(ShotView start) { Set(start); _barLo = _lo; _barHi = _hi; Bar(); }
        public override void Ready(ShotView v) { Set(v); }

        void Bar()
        {
            _bar.localPosition = new Vector3(0, 0, _barLo * K);
            float len = Mathf.Max(1f, (_barHi - _barLo) * K);
            _barFill.transform.localPosition = new Vector3(0, 0.3f, len / 2f);
            _barFill.transform.localScale = new Vector3(LaneW * 2f, 0.3f, len);
            _barFar.localPosition = new Vector3(0, 0.5f, len);
        }

        public override void Tick()
        {
            _barLo = Mathf.Lerp(_barLo, _lo, Time.deltaTime * 4f);
            _barHi = Mathf.Lerp(_barHi, _hi, Time.deltaTime * 4f);
            Bar();
            // The meters dance to a beat around the streak's level.
            _level = Mathf.Lerp(_level, _levelTo, Time.deltaTime * 6f);
            int per = 10;
            for (int s = 0; s < _leds.Count / per; s++)
            {
                float lit = Mathf.Clamp(_level + 1.5f + Mathf.Sin(Time.time * 7f + s * 1.7f) * 1.5f, 0f, 10f);
                for (int k = 0; k < per; k++) _leds[s * per + k].sharedMaterial = k < lit ? (k >= 8 ? _ledHot : _ledOn) : _ledOff;
            }
            for (int i = 0; i < _beams.Count; i++) _beams[i].localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 0.6f + i * 1.3f) * 22f);
            _pump = Mathf.Max(0f, _pump - Time.deltaTime * 2.5f);
            _combo.localScale = Vector3.one * (1f + _pump * 0.35f);
            _combo.rotation = Quaternion.LookRotation(_combo.position - CameraFrom);
        }

        public override IEnumerator Play(ShotView v)
        {
            Vector3 start = new Vector3(v.Tee == 1 ? -40f : v.Tee == 3 ? 40f : 0f, 0, -Reach);
            float stopZ = v.HasRollout ? Mathf.Clamp(v.RolloutIn * K, -Reach + 40f, 600f) : (v.Reached ? 60f : -60f);
            Vector3 stop = new Vector3(Mathf.Clamp(v.LineIn * K * 0.5f, -LaneW + 10f, LaneW - 10f), 0, stopZ);
            List<Vector3> pts;
            if (v.Made)
            {
                Vector3 rim = new Vector3(0, 0, -CupR * 0.3f);
                pts = Bezier(start, Vector3.Lerp(start, rim, 0.35f), Vector3.Lerp(start, rim, 0.7f), rim);
                Lift(pts, BallR);
                _ball.gameObject.SetActive(true);
                yield return Roll(_ball, pts, 1.2f, _trail, BallR);
                Sound("drum_hit", 0.9f);
                yield return Drop(_ball, rim, BallR);
                _marker.localPosition = stop;
                _marker.gameObject.SetActive(true);
            }
            else
            {
                pts = Bezier(start, Vector3.Lerp(start, stop, 0.35f), Vector3.Lerp(start, stop, 0.7f), stop);
                Lift(pts, BallR);
                _ball.gameObject.SetActive(true);
                yield return Roll(_ball, pts, 1.4f, _trail, BallR);
            }

            int[] streaks = v.Get<int[]>("streaks");
            int streak = streaks != null && v.Player < streaks.Length ? streaks[v.Player] : 0;
            if (v.Good)
            {
                _barFill.sharedMaterial = _barHit;
                _comboText.text = "x" + streak;
                _comboText.color = Lime;
                _pump = 1f;
                _levelTo = 10f;
                Arcade.Sparks(At(stop + Vector3.up * 6f), 30);
                Arcade.Sparks(At(-LaneW, 4f, (_barLo + _barHi) * K / 2f), 14);
                Arcade.Sparks(At(LaneW, 4f, (_barLo + _barHi) * K / 2f), 14);
                if (streak >= 3) Arcade.Celebrate(16, At(60f, 150f, 580f));
            }
            else
            {
                _barFill.sharedMaterial = _barMiss;
                _comboText.text = "x0";
                _comboText.color = new Color(1f, 0.3f, 0.35f);
                _levelTo = 0f;
                Arcade.Shake(0.12f);
            }
            yield return new WaitForSeconds(0.9f);
            _barFill.sharedMaterial = _barMat;
            _comboText.color = Lime;
            _ball.gameObject.SetActive(false);
            _marker.gameObject.SetActive(false);
            _trail.positionCount = 0;
        }

        // ── Rolling ──────────────────────────────────────────────────────────

        static List<Vector3> Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int n = 40)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= n; i++) { float t = i / (float)n, u = 1f - t; pts.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d); }
            return pts;
        }

        static void Lift(List<Vector3> pts, float y) { for (int i = 0; i < pts.Count; i++) pts[i] = new Vector3(pts[i].x, y, pts[i].z); }

        static IEnumerator Roll(Transform ball, List<Vector3> pts, float seconds, LineRenderer trail, float r)
        {
            int n = pts.Count;
            if (n < 2) yield break;
            var cum = new float[n];
            for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            float total = Mathf.Max(0.001f, cum[n - 1]);
            var drawn = new List<Vector3> { pts[0] };
            int seg = 0;
            Vector3 last = pts[0];
            for (float t = 0f; ; t += Time.deltaTime / seconds)
            {
                float tt = Mathf.Min(1f, t);
                float d = (1f - (1f - tt) * (1f - tt)) * total;
                while (seg < n - 2 && cum[seg + 1] < d) { seg++; drawn.Add(pts[seg]); }
                Vector3 p = Vector3.Lerp(pts[seg], pts[seg + 1], Mathf.InverseLerp(cum[seg], cum[seg + 1], d));
                Vector3 step = p - last;
                if (step.sqrMagnitude > 1e-6f) ball.localRotation = Quaternion.AngleAxis(step.magnitude / r * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, step.normalized)) * ball.localRotation;
                ball.localPosition = p;
                last = p;
                if (trail != null)
                {
                    trail.positionCount = drawn.Count + 1;
                    for (int i = 0; i < drawn.Count; i++) trail.SetPosition(i, drawn[i] - Vector3.up * (r - 0.6f));
                    trail.SetPosition(drawn.Count, p - Vector3.up * (r - 0.6f));
                }
                if (t >= 1f) break;
                yield return null;
            }
        }

        static IEnumerator Drop(Transform ball, Vector3 rim, float r)
        {
            Vector3 from = ball.localPosition;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
            {
                Vector3 p = Vector3.Lerp(from, new Vector3(rim.x * 0.2f, 0f, rim.z * 0.2f), t);
                p.y = from.y - t * t * r * 3.5f;
                ball.localPosition = p;
                yield return null;
            }
            ball.localPosition = new Vector3(0, -r * 4f, 0);
        }
    }
}
