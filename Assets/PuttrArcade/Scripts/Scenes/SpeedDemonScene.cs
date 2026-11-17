// Speed Demon as a night drag strip: the putt is the run, the cup is the finish line. Only pace scores, so the strip
// runs on past the cup into a shut-down zone marked 1, 2 and 3 feet (3 ft past scores nothing). A make drops, then a
// ghost ball carries on from the cup to where it would have stopped (the rollout) and the pace gauge by the strip
// swings to it. Every stop leaves a mark in the shut-down zone. A miss runs out past the cup and the rails flash red.
// Units are inches; the cup is at the origin and the putt rolls up +z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class SpeedDemonScene : RuleScene
    {
        const float LaneW = 40f, BallR = 1.1f, CupR = 2.4f, RunOut = 66f, Back = 160f;
        static readonly Color Neon = new Color(1f, 0.16f, 0.33f), Cyan = new Color(0.25f, 0.95f, 1f);

        Transform _ball, _ghost, _needle, _start;
        TextMesh _ghostTag;
        LineRenderer _trail, _ghostTrail;
        Material _railMat, _ghostMat;
        readonly List<Renderer> _marks = new List<Renderer>();
        Vector3 _from, _camAt, _camTo, _lookAt, _lookTo;
        float _needleNow, _needleTo, _flash;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(_camAt);
        public override Vector3 CameraLook => At(_lookAt);
        public override float CameraFov => Portrait ? 60f : 46f;

        static Vector3 SpotFor(int feet, int tee) => new Vector3((tee - 2) * 8f, 0f, -feet * 12f);

        public override void Build()
        {
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material turf = Make.Lit(new Color(0.1f, 0.36f, 0.18f), 0.1f, 0f, pile, pileNormal, 0.8f);
            turf.SetTextureScale("_BaseMap", new Vector2(3f, 16f));
            Material asphalt = Make.Lit(new Color(0.045f, 0.045f, 0.055f), 0.35f);
            Material steel = Make.Lit(new Color(0.22f, 0.23f, 0.26f), 0.6f, 0.8f);
            Material dark = Make.Lit(new Color(0.02f, 0.02f, 0.03f), 0.4f);
            Material white = Make.Emissive(Color.white, new Color(0.6f, 0.6f, 0.6f));
            Material hole = Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f);
            _railMat = Make.Emissive(Neon, Neon * 2.2f);

            // The strip: asphalt all round, the green run, the neon rails.
            Shape(PrimitiveType.Cube, "Asphalt", Root, new Vector3(0, -1.6f, 100f), new Vector3(900f, 2f, 1400f), asphalt);
            float len = Back + RunOut, mid = (RunOut - Back) / 2f;
            Shape(PrimitiveType.Cube, "Strip", Root, new Vector3(0, -0.5f, mid), new Vector3(LaneW, 1f, len), turf);
            for (int s = -1; s <= 1; s += 2)
            {
                Shape(PrimitiveType.Cube, "Rail", Root, new Vector3(s * (LaneW / 2f + 0.7f), 0.7f, mid), new Vector3(1.4f, 1.8f, len), _railMat)
                    .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // Lane stripes every foot, faint, for the sense of speed.
            Material stripe = Make.Glow(new Color(1f, 1f, 1f, 0.07f), false);
            for (int z = -(int)Back + 12; z < 0; z += 12)
                Shape(PrimitiveType.Quad, "Stripe", Root, new Vector3(0, 0.03f, z), new Vector3(LaneW, 0.5f, 1f), stripe).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            // The finish: the cup and its ring.
            Make.MeshObject("Cup", Root, Make.Disc(CupR, 48), hole).transform.localPosition = new Vector3(0, 0.03f, 0);
            Hoop("Lip", Root, new Vector3(0, 0.05f, 0), CupR, CupR + 0.35f, white);
            Hoop("Finish ring", Root, new Vector3(0, 0.06f, 0), CupR + 2.2f, CupR + 3f, Make.Emissive(Color.black, Cyan * 2.5f));

            // The shut-down zone: 1, 2 and 3 feet past, the 3 ft line is where pace scores nothing.
            Color[] zone = { new Color(0.5f, 1f, 0.35f), new Color(1f, 0.75f, 0.2f), Neon };
            for (int f = 1; f <= 3; f++)
            {
                Shape(PrimitiveType.Cube, "Zone line", Root, new Vector3(0, 0.05f, f * 12f), new Vector3(LaneW, 0.1f, f == 3 ? 1.2f : 0.6f), Make.Emissive(Color.black, zone[f - 1] * (f == 3 ? 2.5f : 1.4f)));
                Words(Root, f.ToString(), new Vector3(LaneW / 2f + 8f, 5f + f * 2f, f * 12f), 8f + f * 2.5f, zone[f - 1]);
            }
            Words(Root, "FT PAST", new Vector3(LaneW / 2f + 8f, 0.3f, 2f), 2.2f, new Color(1f, 1f, 1f, 0.6f)).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            // Distance boards down the left side.
            for (int f = 3; f <= 11; f++)
            {
                float z = -f * 12f;
                Shape(PrimitiveType.Cube, "Board post", Root, new Vector3(-LaneW / 2f - 7f, 3f, z), new Vector3(0.6f, 6f, 0.6f), steel);
                Shape(PrimitiveType.Cube, "Board", Root, new Vector3(-LaneW / 2f - 7f, 7.5f, z), new Vector3(7f, 4f, 0.5f), dark);
                Words(Root, f + " FT", new Vector3(-LaneW / 2f - 7f, 7.5f, z - 0.4f), 2.2f, new Color(1f, 1f, 1f, 0.85f));
            }

            // The gantry over the shut-down zone, with its sign and floodlights.
            float gz = RunOut - 6f, gx = LaneW / 2f + 16f;
            for (int s = -1; s <= 1; s += 2)
            {
                Shape(PrimitiveType.Cylinder, "Pole", Root, new Vector3(s * gx, 40f, gz), new Vector3(3f, 40f, 3f), steel);
                Shape(PrimitiveType.Cube, "Flood", Root, new Vector3(s * gx, 84f, gz - 2f), new Vector3(14f, 5f, 4f), dark);
                for (int k = -1; k <= 1; k++)
                    Shape(PrimitiveType.Cube, "Lens", Root, new Vector3(s * gx + k * 4f, 84f, gz - 4.2f), new Vector3(3f, 3f, 0.4f), Make.Emissive(Color.white, new Color(3f, 3f, 2.8f)));
            }
            Shape(PrimitiveType.Cube, "Beam", Root, new Vector3(0, 66f, gz), new Vector3(gx * 2f, 3f, 3f), steel);
            Shape(PrimitiveType.Cube, "Sign", Root, new Vector3(0, 60f, gz - 0.5f), new Vector3(60f, 8f, 1.5f), dark);
            Shape(PrimitiveType.Cube, "Sign edge", Root, new Vector3(0, 60f, gz - 1.4f), new Vector3(61f, 9f, 0.2f), _railMat);
            Shape(PrimitiveType.Cube, "Sign face", Root, new Vector3(0, 60f, gz - 1.6f), new Vector3(59f, 7f, 0.2f), dark);
            Words(Root, "S P E E D   D E M O N", new Vector3(0, 60f, gz - 1.9f), 3.2f, Neon);

            // The pace gauge on its post, right of the strip.
            var gauge = new GameObject("Gauge").transform;
            gauge.SetParent(Root, false);
            gauge.localPosition = new Vector3(LaneW / 2f + 30f, 36f, 18f);
            gauge.localRotation = Quaternion.Euler(8f, -14f, 0);
            gauge.localScale = Vector3.one * 1.6f;
            Shape(PrimitiveType.Cylinder, "Gauge post", Root, new Vector3(LaneW / 2f + 30f, 7f, 22f), new Vector3(3f, 7f, 3f), steel);
            GameObject face = Make.MeshObject("Face", gauge, Make.Disc(14f, 48), dark);
            face.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            Hoop("Bezel", gauge, new Vector3(0, 0, -0.2f), 13.4f, 14.4f, steel).transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            Color[] arc = { new Color(0.45f, 0.95f, 0.35f), new Color(1f, 0.75f, 0.2f), Neon };
            for (int k = 0; k < 3; k++)
            {
                LineRenderer seg = Stroke(gauge, "Arc", arc[k] * 1.6f, 1.3f);
                var pts = new List<Vector3>();
                for (int i = 0; i <= 12; i++)
                {
                    float a = Mathf.Lerp(180f - k * 60f, 180f - (k + 1) * 60f + 2f, i / 12f) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(Mathf.Cos(a) * 10.5f, Mathf.Sin(a) * 10.5f - 2f, -0.5f));
                }
                Points(seg, pts);
            }
            Words(gauge, "PACE", new Vector3(0, 4.5f, -0.6f), 2f, Neon);
            Words(gauge, "DIES", new Vector3(-9f, -4.5f, -0.6f), 1.7f, Color.white);
            Words(gauge, "36 IN", new Vector3(9f, -4.5f, -0.6f), 1.7f, Color.white);
            _needle = new GameObject("Needle").transform;
            _needle.SetParent(gauge, false);
            _needle.localPosition = new Vector3(0, -2f, -0.9f);
            Shape(PrimitiveType.Cube, "Pointer", _needle, new Vector3(0, 4.5f, 0), new Vector3(0.6f, 9f, 0.3f), Make.Emissive(Color.white, Neon * 2f));
            Shape(PrimitiveType.Cylinder, "Hub", _needle, Vector3.zero, new Vector3(2f, 0.3f, 2f), Make.Emissive(Neon, Neon)).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            // The city behind: dark blocks, lit windows.
            var dice = new System.Random(11);
            Material block = Make.Lit(new Color(0.03f, 0.035f, 0.06f), 0.2f);
            Material window = Make.Emissive(new Color(1f, 0.85f, 0.5f), new Color(1.6f, 1.2f, 0.6f));
            for (int i = 0; i < 26; i++)
            {
                float w = 30f + (float)dice.NextDouble() * 50f, h = 60f + (float)dice.NextDouble() * 200f;
                float x = -420f + i * 34f + (float)dice.NextDouble() * 10f, z = 520f + (float)dice.NextDouble() * 120f;
                Shape(PrimitiveType.Cube, "Block", Root, new Vector3(x, h / 2f, z), new Vector3(w, h, 30f), block);
                for (int k = 0; k < 6; k++)
                {
                    GameObject win = Shape(PrimitiveType.Cube, "Window", Root, new Vector3(x + ((float)dice.NextDouble() - 0.5f) * w * 0.8f, 10f + (float)dice.NextDouble() * (h - 20f), z - 15.5f), new Vector3(3f, 4f, 0.5f), window);
                    win.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            Shape(PrimitiveType.Cube, "Night", Root, new Vector3(0, 300f, 760f), new Vector3(2400f, 900f, 4f), Make.Lit(new Color(0.02f, 0.025f, 0.05f), 0f));

            // The start spot, the ball and its ghost.
            _start = new GameObject("Start").transform;
            _start.SetParent(Root, false);
            Hoop("Stage", _start, new Vector3(0, 0.05f, 0), 3.2f, 3.8f, Make.Emissive(Color.black, Color.white * 1.5f));
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Emissive(Color.white, new Color(0.35f, 0.35f, 0.35f), 0.7f)).transform;
            _ghostMat = Make.Glow(new Color(Cyan.r, Cyan.g, Cyan.b, 0.7f));
            _ghost = Shape(PrimitiveType.Sphere, "Ghost", Root, Vector3.zero, Vector3.one * BallR * 2.4f, _ghostMat).transform;
            _ghost.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ghost.gameObject.SetActive(false);
            _ghostTag = Words(Root, "", Vector3.zero, 2.6f, Cyan);
            _trail = Stroke(Root, "Trail", Cyan, 1.4f);
            _ghostTrail = Stroke(Root, "Ghost trail", Cyan, 1.8f);

            Sun(Root, new Vector3(50f, -30f, 0f), new Color(0.55f, 0.65f, 1f), 0.25f);
            Lamp(Root, new Vector3(-gx, 84f, gz - 6f), new Vector3(0, 0, -50f), new Color(1f, 0.95f, 0.9f), 50000f, 62f, 700f);
            Lamp(Root, new Vector3(gx, 84f, gz - 6f), new Vector3(0, 0, -30f), new Color(1f, 0.95f, 0.9f), 50000f, 62f, 700f);
            Lamp(Root, new Vector3(0, 220f, -330f), new Vector3(0, 0, -40f), new Color(0.75f, 0.85f, 1f), 90000f, 50f, 900f);
        }

        void Aim(int feet, int tee, bool snap)
        {
            _from = SpotFor(feet, tee);
            _start.localPosition = _from;
            _ball.localPosition = _from + Vector3.up * BallR;
            _ball.gameObject.SetActive(true);
            // Fixed high behind the strip: the 11 ft start at the bottom, the cup a third up, the gantry above.
            _camTo = Portrait ? new Vector3(-2f, 80f, -215f) : new Vector3(-8f, 72f, -210f);
            _lookTo = Portrait ? new Vector3(-8f, 20f, -20f) : new Vector3(-34f, 20f, -20f);
            if (snap) { _camAt = _camTo; _lookAt = _lookTo; }
        }

        public override void Begin(ShotView start) { Aim(start.NextFeet, start.NextTee, true); }
        public override void Ready(ShotView view) { if (!view.Over) Aim(view.NextFeet, view.NextTee, false); }

        public override void Tick()
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime * 2.5f);
            _camAt = Vector3.Lerp(_camAt, _camTo, k);
            _lookAt = Vector3.Lerp(_lookAt, _lookTo, k);
            _needleNow = Mathf.Lerp(_needleNow, _needleTo, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            if (_needle != null) _needle.localRotation = Quaternion.Euler(0, 0, 90f - Mathf.Clamp01(_needleNow) * 180f + Mathf.Sin(Time.time * 30f) * 0.6f * _needleNow);
            if (_flash > 0f)
            {
                _flash -= Time.deltaTime;
                float on = Mathf.Repeat(_flash * 6f, 1f) > 0.5f ? 4f : 1f;
                _railMat.SetColor("_EmissionColor", Neon * 2.2f * on);
                if (_flash <= 0f) _railMat.SetColor("_EmissionColor", Neon * 2.2f);
            }
        }

        public override IEnumerator Play(ShotView v)
        {
            _ghost.gameObject.SetActive(false);
            _ghostTag.text = "";
            Sound("engine_rev", 0.7f, v.Made ? 1.15f : 1f);
            _ghostTrail.positionCount = 0;
            _trail.positionCount = 0;
            _needleTo = 0f;
            Vector3 from = SpotFor(v.Feet > 0 ? v.Feet : 6, v.Tee > 0 ? v.Tee : 2);
            List<Vector3> roll = Roll(v, from, BallR);
            Gradient(_trail, Cyan, Cyan, 1f);
            yield return Along(_ball, roll, Mathf.Lerp(0.9f, 1.6f, Mathf.InverseLerp(3f, 11f, v.Feet)), _trail);

            if (v.Made)
            {
                // Down it goes, and the ghost carries on to where it would have stopped.
                Vector3 top = _ball.localPosition;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localPosition = Vector3.Lerp(top, new Vector3(0, -BallR * 2f, 0), t); yield return null; }
                _ball.gameObject.SetActive(false);
                float r = v.HasRollout ? v.RolloutIn : 0f;
                float stop = Mathf.Clamp(r, 0f, RunOut - 4f);
                Vector3 g0 = new Vector3(0, BallR * 1.2f, 0), g1 = new Vector3(Mathf.Clamp(v.LineIn * 0.6f, -6f, 6f), BallR * 1.2f, stop);
                Color hot = PaceColor(r);
                _ghostMat.SetColor("_Color", new Color(hot.r, hot.g, hot.b, 0.75f));
                _ghost.gameObject.SetActive(true);
                Gradient(_ghostTrail, Cyan, hot, 0.9f);
                var pts = new List<Vector3>();
                float sec = Mathf.Lerp(0.35f, 0.9f, Mathf.Clamp01(stop / 36f));
                for (float t = 0f; t < 1f; t += Time.deltaTime / sec)
                {
                    float e = 1f - (1f - t) * (1f - t);
                    Vector3 p = Vector3.Lerp(g0, g1, e);
                    _ghost.localPosition = p;
                    pts.Add(p + Vector3.down * (BallR * 1.1f));
                    Points(_ghostTrail, pts);
                    _needleTo = Mathf.Clamp01(Mathf.Max(0f, r) * e / 36f);
                    yield return null;
                }
                _ghost.localPosition = g1;
                _needleTo = Mathf.Clamp01(Mathf.Max(0f, r) / 36f);
                _ghostTag.transform.localPosition = g1 + new Vector3(0, 5f, 0);
                _ghostTag.color = hot;
                _ghostTag.text = !v.HasRollout ? "IN" : r < 0.5f ? "DIED IN" : "+" + Mathf.RoundToInt(r) + " IN";
                Mark(g1, hot);
                if (v.HasRollout && Mathf.Abs(r) < 1.5f) Arcade.Sparks(At(0, 2f, 0), 26);
                if (v.HasRollout && r > 30f) Arcade.Shake(0.4f);
            }
            else
            {
                // Ran out (or never got there): the rails flash.
                _flash = 1f;
                _needleTo = 0f;
                Mark(_ball.localPosition, new Color(0.6f, 0.6f, 0.6f));
            }
            yield return new WaitForSeconds(0.8f);
            Gradient(_trail, Cyan, Cyan, 0.35f);
        }

        static Color PaceColor(float r)
        {
            float k = Mathf.Clamp01(Mathf.Max(0f, r) / 36f);
            return k < 0.33f ? Color.Lerp(new Color(0.45f, 1f, 0.4f), new Color(1f, 0.85f, 0.25f), k / 0.33f) : Color.Lerp(new Color(1f, 0.75f, 0.2f), Neon, (k - 0.33f) / 0.67f);
        }

        void Mark(Vector3 at, Color c)
        {
            // A skid mark where the run stopped; the oldest fade out.
            GameObject m = Make.MeshObject("Mark", Root, Make.Disc(1.4f, 20), Make.Glow(new Color(c.r, c.g, c.b, 0.55f)));
            m.transform.localPosition = new Vector3(at.x, 0.08f, at.z);
            _marks.Add(m.GetComponent<Renderer>());
            while (_marks.Count > 12) { Make.Kill(_marks[0].gameObject); _marks.RemoveAt(0); }
        }

        static void Gradient(LineRenderer line, Color a, Color b, float alpha)
        {
            var g = new UnityEngine.Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) }, new[] { new GradientAlphaKey(alpha * 0.4f, 0f), new GradientAlphaKey(alpha, 1f) });
            line.colorGradient = g;
        }

        // ── The roll ─────────────────────────────────────────────────────────

        /// <summary>The roll in lane inches (x right, z past the cup) from the start spot to where it finished.</summary>
        static List<Vector3> Roll(ShotView v, Vector3 start, float y)
        {
            var pts = new List<Vector3> { new Vector3(start.x, y, start.z) };
            if (v.Path != null && v.Path.Count >= 2)
            {
                int step = Mathf.Max(1, v.Path.Count / 40);
                for (int i = 0; i < v.Path.Count; i += step)
                {
                    Vector2 p = v.Path[i];
                    if (p.y < start.z + 4f) continue;
                    pts.Add(new Vector3(Mathf.Clamp(p.x, -LaneW / 2f + 2f, LaneW / 2f - 2f), y, p.y));
                }
                Vector2 last = v.Path[v.Path.Count - 1];
                pts.Add(new Vector3(Mathf.Clamp(last.x, -LaneW / 2f + 2f, LaneW / 2f - 2f), y, last.y));
            }
            else
            {
                float endZ = v.Reached ? 0f : Mathf.Min(-3f, v.HasRollout ? v.RolloutIn : -8f);
                Vector3 at = new Vector3(v.Made ? v.LineIn * 0.4f : v.LineIn, y, endZ);
                Vector3 mid = Vector3.Lerp(pts[0], at, 0.5f) + new Vector3(-at.x * 0.5f, 0, 0);
                for (int i = 1; i <= 16; i++)
                {
                    float t = i / 16f;
                    pts.Add((1 - t) * (1 - t) * pts[0] + 2 * (1 - t) * t * mid + t * t * at);
                }
            }
            Vector3 end = pts[pts.Count - 1];
            if (v.Made) { if (new Vector2(end.x, end.z).magnitude > 0.6f) pts.Add(new Vector3(0, y, 0)); }
            else if (v.Reached && v.HasRollout && v.RolloutIn > end.z + 2f && pts.Count >= 2)
            {
                Vector3 dir = end - pts[pts.Count - 2];
                if (dir.z < 0.1f) dir = new Vector3(0, 0, 1f);
                float z = Mathf.Min(v.RolloutIn, RunOut - 3f);
                pts.Add(new Vector3(Mathf.Clamp(end.x + dir.x / dir.z * (z - end.z), -LaneW / 2f + 2f, LaneW / 2f - 2f), y, z));
            }
            return pts;
        }

        /// <summary>Roll a ball along points, slowing as it goes, drawing the trail behind it.</summary>
        static IEnumerator Along(Transform ball, List<Vector3> pts, float seconds, LineRenderer trail)
        {
            var cum = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            float total = Mathf.Max(0.01f, cum[pts.Count - 1]);
            var drawn = new List<Vector3>();
            int seg = 1;
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float s = (1f - (1f - t) * (1f - t)) * total;
                while (seg < pts.Count - 1 && cum[seg] < s) seg++;
                float k = Mathf.InverseLerp(cum[seg - 1], cum[seg], s);
                Vector3 p = Vector3.Lerp(pts[seg - 1], pts[seg], k);
                Vector3 step = p - ball.localPosition;
                if (step.sqrMagnitude > 0.0001f) ball.Rotate(Vector3.Cross(Vector3.up, step.normalized), step.magnitude / BallR * Mathf.Rad2Deg, Space.World);
                ball.localPosition = p;
                drawn.Add(new Vector3(p.x, 0.15f, p.z));
                if (trail != null) Points(trail, drawn);
                yield return null;
            }
            ball.localPosition = pts[pts.Count - 1];
        }
    }
}
