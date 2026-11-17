// Compare as a fitting bay: a green under three ceiling light panels, and along the front a lit pedestal per putter
// (A, B, C, D, each in its own colour) with the putter standing on it. The putter that's up has its pedestal lit and
// its ring glowing. Every putt rolls in that putter's colour and stays on the green, faint, so each putter's rolls
// overlay the others' and the tighter bundle shows. Units are inches; the cup is at the origin.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class CompareScene : RuleScene
    {
        const float LaneW = 44f, BallR = 1.1f, CupR = 2.4f, PedZ = -176f, PedR = 15f, PedH = 22f;
        static readonly Color[] Hues = { Make.Hex(0x5B8CFF), Make.Hex(0xFFB300), Make.Hex(0xE0609A), Make.Hex(0x3CD6B4) };

        Transform _ball, _start;
        TextMesh _upNext;
        LineRenderer _trail;
        readonly List<Transform> _peds = new List<Transform>();
        readonly List<Material> _rings = new List<Material>();
        readonly List<Light> _spots = new List<Light>();
        readonly List<LineRenderer> _rolls = new List<LineRenderer>();
        readonly List<int> _rollOwner = new List<int>();
        int _count = 2, _up;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(Portrait ? new Vector3(-4f, 128f, -300f) : new Vector3(-10f, 120f, -300f));
        public override Vector3 CameraLook => At(Portrait ? new Vector3(-10f, 30f, -80f) : new Vector3(-40f, 30f, -80f));
        public override float CameraFov => Portrait ? 56f : 44f;

        static Vector3 SpotFor(int feet, int tee) => new Vector3((tee - 2) * 8f, 0f, -feet * 12f);
        float PedX(int i) => (i - (_count - 1) / 2f) * (_count > 3 ? 40f : 46f);

        public override void Build()
        {
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material turf = Make.Lit(new Color(0.13f, 0.46f, 0.22f), 0.15f, 0f, pile, pileNormal, 0.8f);
            turf.SetTextureScale("_BaseMap", new Vector2(3f, 12f));
            Material floor = Make.Lit(new Color(0.08f, 0.085f, 0.095f), 0.3f, 0.1f);
            Material wall = Make.Lit(new Color(0.11f, 0.115f, 0.13f), 0.3f);
            Material hole = Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f);
            Material white = Make.Emissive(Color.white, new Color(0.6f, 0.6f, 0.6f));

            // The bay: floor, back and side walls, ceiling panels.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -1.6f, -60f), new Vector3(700f, 2f, 700f), floor);
            Shape(PrimitiveType.Cube, "Back wall", Root, new Vector3(0, 100f, 70f), new Vector3(400f, 260f, 4f), wall);
            for (int s = -1; s <= 1; s += 2)
                Shape(PrimitiveType.Cube, "Side wall", Root, new Vector3(s * 130f, 100f, -100f), new Vector3(4f, 260f, 360f), wall);
            Shape(PrimitiveType.Cube, "Ceiling", Root, new Vector3(0, 150f, -60f), new Vector3(300f, 4f, 300f), wall);
            Material panel = Make.Emissive(Color.white, new Color(2.2f, 2.3f, 2.5f));
            for (int i = -1; i <= 1; i++)
            {
                Shape(PrimitiveType.Cube, "Panel frame", Root, new Vector3(i * 62f, 147f, 10f), new Vector3(44f, 4f, 22f), Make.Lit(new Color(0.05f, 0.05f, 0.06f), 0.4f));
                Shape(PrimitiveType.Cube, "Panel", Root, new Vector3(i * 62f, 145f, 10f), new Vector3(40f, 1f, 18f), panel)
                    .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Shape(PrimitiveType.Cube, "Wall stripe", Root, new Vector3(0, 40f, 67.8f), new Vector3(400f, 1.5f, 0.4f), Make.Emissive(Color.black, new Color(0.5f, 0.5f, 0.55f)));

            // The green, the cup, the distances.
            Shape(PrimitiveType.Cube, "Green", Root, new Vector3(0, -0.5f, -64f), new Vector3(LaneW, 1f, 160f), turf);
            Make.MeshObject("Cup", Root, Make.Disc(CupR, 48), hole).transform.localPosition = new Vector3(0, 0.03f, 0);
            Hoop("Lip", Root, new Vector3(0, 0.05f, 0), CupR, CupR + 0.4f, white);
            Material tick = Make.Glow(new Color(1f, 1f, 1f, 0.12f), false);
            for (int f = 1; f <= 11; f++)
                Shape(PrimitiveType.Quad, "Foot", Root, new Vector3(0, 0.03f, -f * 12f), new Vector3(LaneW, 0.35f, 1f), tick).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            // A pedestal per putter, built for four; Begin shows as many as are playing.
            for (int i = 0; i < 4; i++)
            {
                var ped = new GameObject("Pedestal " + i).transform;
                ped.SetParent(Root, false);
                Make.MeshObject("Column", ped, Make.Lathe(new[] { new Vector2(PedR + 1.5f, 0f), new Vector2(PedR, 2f), new Vector2(PedR, PedH - 1f), new Vector2(PedR + 1f, PedH - 0.5f), new Vector2(PedR + 1f, PedH), new Vector2(0.01f, PedH) }, 48),
                    Make.Lit(new Color(0.17f, 0.18f, 0.21f), 0.55f, 0.5f));
                var ring = Make.Emissive(Color.black, Hues[i] * 0.4f);
                _rings.Add(ring);
                Hoop("Ring", ped, new Vector3(0, PedH + 0.05f, 0), PedR - 1.2f, PedR + 0.6f, ring);
                Words(ped, ((char)('A' + i)).ToString(), new Vector3(0, PedH * 0.5f, -PedR - 0.3f), 8f, Hues[i]);
                Putter(ped, i);
                Light spot = Lamp(ped, new Vector3(0, 120f, -20f), new Vector3(0, PedH, 0), Color.Lerp(Color.white, Hues[i], 0.25f), 0f, 26f, 300f);
                _spots.Add(spot);
                _peds.Add(ped);
            }
            _upNext = Words(Root, "UP NEXT", Vector3.zero, 2.4f, new Color(1f, 1f, 1f, 0.75f));

            _start = new GameObject("Start").transform;
            _start.SetParent(Root, false);
            Hoop("Spot", _start, new Vector3(0, 0.05f, 0), 3f, 3.6f, Make.Emissive(Color.black, Color.white * 1.2f));
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Emissive(Color.white, new Color(0.3f, 0.3f, 0.3f), 0.7f)).transform;
            _trail = Stroke(Root, "Roll", Color.white, 1.3f);

            Sun(Root, new Vector3(60f, 15f, 0f), new Color(1f, 0.97f, 0.92f), 0.35f);
            for (int i = -1; i <= 1; i++)
                Lamp(Root, new Vector3(i * 62f, 140f, 10f), new Vector3(i * 20f, 0, -50f), new Color(1f, 0.98f, 0.95f), 70000f, 70f, 500f);
        }

        // A putter on its pedestal: a blade, a mallet, an arm lock and a fang, so the four look different.
        static void Putter(Transform ped, int kind)
        {
            Material steel = Make.Lit(new Color(0.75f, 0.77f, 0.8f), 0.8f, 0.9f);
            Material black = Make.Lit(new Color(0.06f, 0.06f, 0.07f), 0.5f, 0.4f);
            Material head = kind == 1 || kind == 3 ? black : steel;
            var p = new GameObject("Putter").transform;
            p.SetParent(ped, false);
            p.localPosition = new Vector3(0, PedH + 0.3f, 0);
            p.localRotation = Quaternion.Euler(0, kind % 2 == 0 ? 8f : -6f, 0);
            if (kind == 1)
                Make.MeshObject("Mallet", p, Make.Lathe(new[] { new Vector2(6.5f, 0f), new Vector2(6.5f, 1.4f), new Vector2(5.2f, 2.6f), new Vector2(0.01f, 2.8f) }, 40), head).transform.localScale = new Vector3(1.2f, 1f, 0.8f);
            else if (kind == 3)
            {
                Shape(PrimitiveType.Cube, "Face", p, new Vector3(0, 1.1f, -1.8f), new Vector3(10f, 2.2f, 1.6f), head);
                for (int s = -1; s <= 1; s += 2) Shape(PrimitiveType.Cube, "Fang", p, new Vector3(s * 3.8f, 0.9f, 1.8f), new Vector3(2.4f, 1.8f, 6f), head);
            }
            else Shape(PrimitiveType.Cube, "Blade", p, new Vector3(0, 1.1f, 0), new Vector3(kind == 2 ? 11f : 10f, 2.2f, 2.4f), head);
            Shape(PrimitiveType.Cube, "Sight", p, new Vector3(0, kind == 1 ? 2.85f : 2.25f, kind == 3 ? -1.8f : 0f), new Vector3(0.3f, 0.1f, 2f), Make.Emissive(Color.white, Color.white));
            float len = kind == 2 ? 42f : 34f;
            var shaft = Shape(PrimitiveType.Cylinder, "Shaft", p, Vector3.zero, new Vector3(0.6f, len / 2f, 0.6f), steel).transform;
            Vector3 heel = new Vector3(kind == 3 ? 0f : -3.5f, 2f, 0f);
            Quaternion lean = Quaternion.Euler(0, 0, -14f);
            shaft.localRotation = lean;
            shaft.localPosition = heel + lean * new Vector3(0, len / 2f, 0);
            var grip = Shape(PrimitiveType.Cylinder, "Grip", p, Vector3.zero, new Vector3(kind == 2 ? 1.6f : 1.3f, kind == 2 ? 8f : 5f, kind == 2 ? 1.6f : 1.3f), black).transform;
            grip.localRotation = lean;
            grip.localPosition = heel + lean * new Vector3(0, len - (kind == 2 ? 8f : 5f), 0);
        }

        void Lay(ShotView v)
        {
            _count = Mathf.Clamp(v.Names.Length, 1, 4);
            for (int i = 0; i < 4; i++)
            {
                _peds[i].gameObject.SetActive(i < _count);
                _peds[i].localPosition = new Vector3(PedX(i), 0, PedZ);
            }
        }

        void Up(ShotView v)
        {
            _up = Mathf.Clamp(v.Next, 0, _count - 1);
            for (int i = 0; i < _count; i++)
            {
                bool on = i == _up && !v.Over;
                _rings[i].SetColor("_EmissionColor", Hues[i] * (on ? 2.6f : 0.35f));
                _spots[i].intensity = on ? 45000f : 6000f;
            }
            _upNext.text = v.Over ? "" : "UP";
            _upNext.color = Hues[_up];
            _upNext.transform.localPosition = new Vector3(PedX(_up), PedH + 48f, PedZ);
            Vector3 from = SpotFor(v.NextFeet, v.NextTee);
            _start.localPosition = from;
            _ball.localPosition = from + Vector3.up * BallR;
            _ball.gameObject.SetActive(!v.Over);
        }

        public override void Begin(ShotView start) { Lay(start); Up(start); }
        public override void Ready(ShotView view) { Up(view); }

        public override IEnumerator Play(ShotView v)
        {
            int who = Mathf.Clamp(v.Player, 0, 3);
            Color hue = Hues[who];
            Sound("lab_beep", 0.35f, 0.8f + who * 0.15f);
            _trail.positionCount = 0;
            Paint(_trail, hue, 1f);
            int feet = v.Feet > 0 ? v.Feet : 6, tee = v.Tee > 0 ? v.Tee : 2;
            List<Vector3> roll = Roll(v, SpotFor(feet, tee), BallR);
            yield return Along(_ball, roll, Mathf.Lerp(0.9f, 1.5f, Mathf.InverseLerp(3f, 11f, feet)), _trail);
            if (v.Made)
            {
                Vector3 top = _ball.localPosition;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localPosition = Vector3.Lerp(top, new Vector3(0, -BallR * 2f, 0), t); yield return null; }
                _ball.gameObject.SetActive(false);
                if (v.Points >= 18) Arcade.Sparks(At(0, 2f, 0), 18);
            }
            // The roll stays on the green, faint, with this putter's others.
            LineRenderer keep = Stroke(Root, "Kept roll", hue, 0.8f);
            var pts = new List<Vector3>();
            foreach (Vector3 p in roll) pts.Add(new Vector3(p.x, 0.1f + who * 0.02f, p.z));
            Points(keep, pts);
            Paint(keep, hue, 0.5f);
            _rolls.Add(keep); _rollOwner.Add(who);
            // Older rolls of this putter fade back a little more.
            int mine = 0;
            for (int i = _rolls.Count - 1; i >= 0; i--)
            {
                if (_rollOwner[i] != who) continue;
                Paint(_rolls[i], hue, Mathf.Lerp(0.55f, 0.18f, Mathf.Clamp01(mine / 8f)));
                mine++;
            }
            yield return new WaitForSeconds(0.7f);
            _trail.positionCount = 0;
        }

        static void Paint(LineRenderer line, Color c, float alpha)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) }, new[] { new GradientAlphaKey(alpha * 0.35f, 0f), new GradientAlphaKey(alpha, 1f) });
            line.colorGradient = g;
        }

        // ── The roll ─────────────────────────────────────────────────────────

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
                    pts.Add(new Vector3(Mathf.Clamp(p.x, -LaneW / 2f + 2f, LaneW / 2f - 2f), y, Mathf.Min(p.y, 14f)));
                }
                Vector2 last = v.Path[v.Path.Count - 1];
                pts.Add(new Vector3(Mathf.Clamp(last.x, -LaneW / 2f + 2f, LaneW / 2f - 2f), y, Mathf.Min(last.y, 14f)));
            }
            else
            {
                float endZ = v.Reached ? (v.Made ? 0f : Mathf.Clamp(v.HasRollout ? v.RolloutIn : 8f, 2f, 14f)) : Mathf.Min(-3f, v.HasRollout ? v.RolloutIn : -8f);
                Vector3 at = new Vector3(v.Made ? v.LineIn * 0.4f : v.LineIn + (v.MissSide == "LEFT" ? -3f : v.MissSide == "RIGHT" ? 3f : 0f), y, endZ);
                Vector3 mid = Vector3.Lerp(pts[0], at, 0.5f) + new Vector3(-at.x * 0.5f, 0, 0);
                for (int i = 1; i <= 16; i++) { float t = i / 16f; pts.Add((1 - t) * (1 - t) * pts[0] + 2 * (1 - t) * t * mid + t * t * at); }
            }
            Vector3 end = pts[pts.Count - 1];
            if (v.Made && new Vector2(end.x, end.z).magnitude > 0.6f) pts.Add(new Vector3(0, y, 0));
            return pts;
        }

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
                Vector3 p = Vector3.Lerp(pts[seg - 1], pts[seg], Mathf.InverseLerp(cum[seg - 1], cum[seg], s));
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
